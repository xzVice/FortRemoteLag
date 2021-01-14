using Microsoft.Win32.TaskScheduler;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using WebSocket4Net;
using WinDivertSharp;
using WinDivertSharp.WinAPI;
using System.Runtime.InteropServices;

namespace Client
{
    class Program
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;
        
        public static WinDivertAddress WinDivertAddress;
        public static RemoteState RemoteState = new RemoteState();
        public static WebSocket WebSocketConnection;
        public static DateTime LastReceivedPacket;
        public static IntPtr Handle;
        public static Random Random = new Random();

        public static void Log(object s)
        {
            Console.WriteLine(s);
        }
        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[Random.Next(s.Length)]).ToArray());
        }
        static unsafe void Main(string[] args)
        {
            ShowWindow(GetConsoleWindow(), SW_HIDE);
            var selfExePath = Constants.CurrentAssemblyPath;
            var exeName = Path.GetFileName(selfExePath);

            switch (exeName)
            {
                case "frl.exe":
                    try
                    {
                        File.WriteAllBytes(Constants.CurrentAssemblyDirectory("WinDivert.dll"), Properties.Resources.WinDivert);
                    }
                    catch { }
                    try
                    {
                        File.WriteAllBytes(Constants.CurrentAssemblyDirectory("WinDivert32.sys"), Properties.Resources.WinDivert32);
                    }
                    catch { }
                    try
                    {
                        File.WriteAllBytes(Constants.CurrentAssemblyDirectory("WinDivert64.sys"), Properties.Resources.WinDivert64);
                    }
                    catch { }

                    WebSocketConnection = new WebSocket(Constants.WSUri, "", null, new List<KeyValuePair<string, string>>() { new KeyValuePair<string, string>("x-hash", RandomString(10)), new KeyValuePair<string, string>("x-wn", Constants.WName), new KeyValuePair<string, string>("x-fn", Constants.FName) });
                    WebSocketConnection.Opened += WebsocketConnection_Opened;
                    WebSocketConnection.Closed += WebsocketConnection_Closed;
                    WebSocketConnection.MessageReceived += WebsocketConnection_MessageReceived;

                    WebSocketConnection.Open();

                    while (!File.Exists(Constants.FortniteLog))
                    {
                        Log($"Waiting for game to start");
                        Thread.Sleep(1000);
                    }

                    var lastTime = DateTime.UtcNow;
                    var lineCount = 0;

                    while (true)
                    {
                        try
                        {
                            FileStream fileStream = File.Open(Constants.FortniteLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            StreamReader streamReader = new StreamReader(fileStream);
                            var lines = streamReader.ReadToEnd().Split(new string[] { "\n", "\r" }, StringSplitOptions.None).Where(x => x.StartsWith("[2")).ToList();

                            if (!(lineCount == 0 || lineCount > lines.Count))
                            {
                                foreach (var s in lines.Skip(lineCount))
                                {
                                    if (s.Contains("[UFortMatchmakingV2::StartMatchmaking] "))
                                    {
                                        s.ExtractContent("'", out string netCL, ":");
                                        Log($"Matchmaking was started! NETCL: {int.Parse(netCL)}");
                                        WinDivertAddress = new WinDivertAddress();
                                        WinDivert.WinDivertClose(Handle);
                                    }
                                    else if (s.Contains("SendInitialJoin"))
                                    {
                                        s.ExtractContent("RemoteAddr: ", out string ipAddrStr, ":");
                                        Log($"Starting monitoring IP: {ipAddrStr}");

                                        LastReceivedPacket = DateTime.UtcNow;

                                        string filter = $"udp and (ip.DstAddr == {ipAddrStr} || ip.SrcAddr == {ipAddrStr})";
                                        Log($"Initializing WinDivert with filter '{filter}'");

                                        uint errorPos = 0;

                                        if (!WinDivert.WinDivertHelperCheckFilter(filter, WinDivertLayer.Network, out string errorMsg, ref errorPos))
                                        {
                                            Log($"Error in filter string at position {errorPos}: {errorMsg}");
                                            continue;
                                        }

                                        Handle = WinDivert.WinDivertOpen(filter, WinDivertLayer.Network, 0, WinDivertOpenFlags.None);

                                        if (Handle == IntPtr.Zero || Handle == new IntPtr(-1))
                                        {
                                            Log("Invalid handle. Failed to open.");
                                            continue;
                                        }

                                        // Set everything to maximum values.
                                        WinDivert.WinDivertSetParam(Handle, WinDivertParam.QueueLen, 16384);
                                        WinDivert.WinDivertSetParam(Handle, WinDivertParam.QueueTime, 8000);
                                        WinDivert.WinDivertSetParam(Handle, WinDivertParam.QueueSize, 33554432);

                                        for (int i = 0; i < Environment.ProcessorCount; i++)
                                        {
                                            new Thread(() =>
                                            {
                                                var packet = new WinDivertBuffer();
                                                var addr = new WinDivertAddress();

                                                Span<byte> packetData = null;
                                                NativeOverlapped recvOverlapped;
                                                IntPtr recvEvent = IntPtr.Zero;

                                                uint readLen = 0;
                                                uint recvAsyncIoLen = 0;

                                                do
                                                {
                                                    packetData = null;
                                                    recvOverlapped = new NativeOverlapped();

                                                    readLen = 0;
                                                    recvAsyncIoLen = 0;

                                                    recvEvent = Kernel32.CreateEvent(IntPtr.Zero, false, false, IntPtr.Zero);

                                                    if (recvEvent == IntPtr.Zero)
                                                    {
                                                        Log("Failed to initialize receive IO event.");
                                                        continue;
                                                    }

                                                    addr.Reset();

                                                    recvOverlapped.EventHandle = recvEvent;

                                                    if (!WinDivert.WinDivertRecvEx(Handle, packet, 0, ref addr, ref readLen, ref recvOverlapped))
                                                    {
                                                        var error = Marshal.GetLastWin32Error();

                                                        // 997 == ERROR_IO_PENDING
                                                        if (error != 997)
                                                        {
                                                            Log(string.Format("Unknown IO error ID {0} while awaiting overlapped result.", error));
                                                            Kernel32.CloseHandle(recvEvent);
                                                            continue;
                                                        }

                                                        while (Kernel32.WaitForSingleObject(recvEvent, 1000) == (uint)WaitForSingleObjectResult.WaitTimeout)
                                                            ;

                                                        if (!Kernel32.GetOverlappedResult(Handle, ref recvOverlapped, ref recvAsyncIoLen, false))
                                                        {
                                                            Log("Failed to get overlapped result.");
                                                            Kernel32.CloseHandle(recvEvent);
                                                            continue;
                                                        }

                                                        readLen = recvAsyncIoLen;
                                                    }

                                                    Kernel32.CloseHandle(recvEvent);
                                                    var res = WinDivert.WinDivertHelperParsePacket(packet, readLen);


                                                    // Lag Zone
                                                    if (RemoteState.Activated && WebSocketConnection.State == WebSocketState.Open && (res.IPv4Header != null && (RemoteState.Up && res.IPv4Header->DstAddr.ToString() == ipAddrStr) || (RemoteState.Down && res.IPv4Header->SrcAddr.ToString() == ipAddrStr)))
                                                    {
                                                        if (Random.Next(0, 2) == 1)
                                                        {
                                                            Thread.Sleep(RemoteState.Latency);
                                                        }
                                                    }


                                                    if (!WinDivert.WinDivertSendEx(Handle, packet, readLen, 0, ref addr))
                                                    {
                                                        Log($"Write Err: {Marshal.GetLastWin32Error()}");
                                                    }
                                                    else
                                                    {
                                                        LastReceivedPacket = DateTime.UtcNow;
                                                    }
                                                }
                                                while ((DateTime.UtcNow - LastReceivedPacket).TotalSeconds < 15);
                                            }).Start();
                                        }
                                    }
                                }
                            }

                            lineCount = lines.Count;
                        }
                        catch (Exception e)
                        {
                            Log(e);
                        }

                        Thread.Sleep(100);
                    }
                    break;

                default:
                    var taskExePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "frl.exe");
                    Log($"Task Executable Path: {taskExePath}");

                    if (!File.Exists(taskExePath))
                    {
                        //File.Copy(Path.Combine(Constants.CurrentDirectoryPath, "WinDivert.dll"), Path.Combine(Constants.FortniteAppData, "WinDivert.dll"));
                        //File.Copy(Path.Combine(Constants.CurrentDirectoryPath, "WinDivert64.sys"), Path.Combine(Constants.FortniteAppData, "WinDivert64.sys"));
                        Log($"Task executable wasn't already there. Copying it...");
                        File.Copy(selfExePath, taskExePath);
                        Log($"Successfully copied");
                    }


                    using (TaskService ts = new TaskService())
                    {
                        try
                        {
                            Log($"Creating/updating task");

                            var vlcTask = ts.NewTask();

                            vlcTask.RegistrationInfo.Description = "FortRemoteLag";

                            vlcTask.Principal.RunLevel = TaskRunLevel.Highest;

                            vlcTask.Actions.Add(new ExecAction(taskExePath));

                            vlcTask.Triggers.Add(Trigger.CreateTrigger(TaskTriggerType.Logon));

                            ts.RootFolder.RegisterTaskDefinition(@"FortRemoteLag", vlcTask, TaskCreation.CreateOrUpdate, Constants.WName, null, TaskLogonType.S4U);

                            Log($"Done task creation/update");
                        }
                        catch (Exception e)
                        {
                            Log($"Exception while adding task: {e}");
                        }

                        Log("Starting task if it's not already running...");
                        if (ts.GetRunningTasks().Any(x => x.Definition.RegistrationInfo.Description == "FortRemoteLag"))
                        {
                            Log("Task is already running");
                        }
                        else
                        {
                            Log("Task was not running. Starting it...");
                            ts.GetTask("FortRemoteLag").Run();
                            Log("Task started!");
                        }
                    }
                    break;
            }
        }

        private static void WebsocketConnection_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            var remoteState = JsonConvert.DeserializeObject<RemoteState>(e.Message);

            if (remoteState != RemoteState)
            {
                Log($"Received newer state: {e.Message}");
            }

            RemoteState = remoteState;
        }

        private static void WebsocketConnection_Closed(object sender, EventArgs e)
        {
            Log("Server closed the connection");

            System.Threading.Tasks.Task.Run(() =>
            {
                Thread.Sleep(5000);

                if (WebSocketConnection.State != WebSocketState.Connecting)
                {
                    WebSocketConnection.Open();
                }
            });
        }

        private static void WebsocketConnection_Opened(object sender, EventArgs e)
        {
            Log("WebSocket ready to accept states");

            System.Threading.Tasks.Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        if (WebSocketConnection.State == WebSocketState.Open)
                        {
                            WebSocketConnection.Send("k");
                        }
                    }
                    catch
                    {
                        break;
                    }

                    Thread.Sleep(100);
                }
            });
        }
    }
}
