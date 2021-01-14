using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Client
{
    public class Constants
    {
        public static string CurrentAssemblyPath => Process.GetCurrentProcess().MainModule.FileName;
        public static string CurrentAssemblyDirectory(string fileNameOrPath = "") => Path.Combine(Path.GetDirectoryName(CurrentAssemblyPath), fileNameOrPath);

        public const string WSUri = "wss://!serveruri!.herokuapp.com/";
        public static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        public static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        public static string FortniteAppData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame");
        public static string FortniteLog => Path.Combine(FortniteAppData, @"Saved\Logs\FortniteGame.log");
        public static string WName
        {
            get
            {
                var pathComponents = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Split(new string[] { "\\" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                return pathComponents[pathComponents.IndexOf("AppData") - 1];
            }
        }
        public static string FName
        {
            get
            {
                DirectoryInfo info = new DirectoryInfo(Path.Combine(FortniteAppData, @"Saved\Logs"));
                FileInfo[] files = info.GetFiles("*.log", SearchOption.TopDirectoryOnly).OrderBy(p => p.CreationTime).ToArray();
                foreach (FileInfo file in files)
                {
                    using (var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fileStream, Encoding.UTF8))
                    {
                        foreach (var epicUsernameLine in reader.ReadToEnd().Split(new char[] { '\n', '\r' }, StringSplitOptions.None).Where(x => x.Contains("-epicusername=")))
                        {
                            return epicUsernameLine.Split(new string[] { "-epicusername=" }, StringSplitOptions.None)[1].Split(new string[] { " -" }, StringSplitOptions.None)[0];
                        }
                    }
                }

                return "UnknownFortName";
            }
        }
    }
}
