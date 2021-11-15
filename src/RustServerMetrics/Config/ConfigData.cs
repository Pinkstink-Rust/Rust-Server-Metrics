using Newtonsoft.Json;

namespace RustServerMetrics.Config
{
    class ConfigData
    {
        public const string DEFAULT_INFLUX_DB_URL = "http://exampledb.com";
        public const string DEFAULT_INFLUX_API_TOKEN = "INSERT_API_TOKEN_HERE";
        public const string DEFAULT_INFLUX_ORG_ID = "INSERT_ORGANIZATION_ID_HERE";
        public const string DEFAULT_INFLUX_BUCKET_ID = "INSERT_BUCKET_ID_HERE";
        public const string DEFAULT_SERVER_TAG = "CHANGEME-01";

        [JsonProperty(PropertyName = "Enabled")]
        public bool enabled = false;

        [JsonProperty(PropertyName = "Influx Database Url")]
        public string databaseUrl = DEFAULT_INFLUX_DB_URL;

        [JsonProperty(PropertyName = "Influx API Token")]
        public string apiToken = DEFAULT_INFLUX_API_TOKEN;

        [JsonProperty(PropertyName = "Influx Organization ID")]
        public string orgId = DEFAULT_INFLUX_ORG_ID;

        [JsonProperty(PropertyName = "Influx Bucket ID")]
        public string bucketId = DEFAULT_INFLUX_BUCKET_ID;

        [JsonProperty(PropertyName = "Server Tag")]
        public string serverTag = DEFAULT_SERVER_TAG;

        [JsonProperty(PropertyName = "Debug Logging")]
        public bool debugLogging = false;

        [JsonProperty(PropertyName = "Amount of metrics to submit in each request")]
        public ushort batchSize = 1000;
    }
}
