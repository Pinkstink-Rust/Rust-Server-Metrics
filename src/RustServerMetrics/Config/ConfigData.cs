using Newtonsoft.Json;
using System.Net;

namespace RustServerMetrics.Config
{
    class ConfigData
    {
        public const string DEFAULT_QUESTDB_HOST_NAME = "INSERT_QUESTDB_HOST_NAME_HERE";
        public const string DEFAULT_SERVER_TAG = "CHANGEME-01";

        [JsonProperty(PropertyName = "Enabled")]
        public bool enabled = false;

        [JsonProperty(PropertyName = "QuestDB Host Name")]
        public string questDbHostName = DEFAULT_QUESTDB_HOST_NAME;

        [JsonProperty(PropertyName = "QuestDB Line Protocol Port")]
        public ushort questDbPort = 0;

        [JsonProperty(PropertyName = "Server Tag")]
        public string serverTag = DEFAULT_SERVER_TAG;

        [JsonProperty(PropertyName = "Debug Logging")]
        public bool debugLogging = false;

        [JsonProperty(PropertyName = "Amount of metrics to submit in each request")]
        public ushort batchSize = 1000;
    }
}
