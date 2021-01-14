using Newtonsoft.Json;

namespace Client
{
    public class RemoteState
    {
        [JsonProperty("latency")]
        public int Latency = 0;

        [JsonProperty("activated")]
        public bool Activated = false;

        [JsonProperty("up")]
        public bool Up = false;

        [JsonProperty("down")]
        public bool Down = false;
    }
}
