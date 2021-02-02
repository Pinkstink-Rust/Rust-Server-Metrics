using Newtonsoft.Json;

namespace RustServerMetrics.Config
{
    class ConfigData
    {
        public const string DEFAULT_INFLUX_DB_URL = "http://exampledb.com";
        public const string DEFAULT_INFLUX_DB_NAME = "CHANGEME_rust_server_example";
        public const string DEFAULT_INFLUX_DB_USER = "admin";
        public const string DEFAULT_INFLUX_DB_PASSWORD = "adminadmin";
        public const string DEFAULT_SERVER_TAG = "CHANGEME-01";

        [JsonProperty(PropertyName = "Enabled")]
        public bool enabled = false;

        [JsonProperty(PropertyName = "Influx Database Url")]
        public string databaseUrl = DEFAULT_INFLUX_DB_URL;

        [JsonProperty(PropertyName = "Influx Database Name")]
        public string databaseName = DEFAULT_INFLUX_DB_NAME;

        [JsonProperty(PropertyName = "Influx Database User")]
        public string databaseUser = DEFAULT_INFLUX_DB_USER;

        [JsonProperty(PropertyName = "Influx Database Password")]
        public string databasePassword = DEFAULT_INFLUX_DB_PASSWORD;

        [JsonProperty(PropertyName = "Server Tag")]
        public string serverTag = DEFAULT_SERVER_TAG;
    }
}
