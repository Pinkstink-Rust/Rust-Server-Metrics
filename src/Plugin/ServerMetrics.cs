using Network;
using Newtonsoft.Json;
using Oxide.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Oxide.Plugins
{
    [Info("Server Metrics", "Pinkstink", "1.0.12")]
    class ServerMetrics : RustPlugin
    {
        readonly StringBuilder _stringBuilder = new StringBuilder();
        readonly Dictionary<ulong, Action> _playerStatsActions = new Dictionary<ulong, Action>();
        public static ServerMetrics Instance { get; private set; }
        Uri _baseUri;
        Uri BaseUri
        {
            get
            {
                if (_baseUri == null)
                {
                    var uri = new Uri(config.databaseUrl);
                    _baseUri = new Uri(uri, $"/write?db={config.databaseName}&precision=ms&u={config.databaseUser}&p={config.databasePassword}");
                }
                return _baseUri;
            }
        }
        ReportUploader _reportUploader;

        void OnServerInitialized()
        {
            bool halt = false;
            if (config.databaseUrl == ConfigData.DEFAULT_INFLUX_DB_URL)
            {
                PrintError("Default Database Url Detected, change this and reload plugin");
                halt = true;
            }

            if (config.databaseName == ConfigData.DEFAULT_INFLUX_DB_NAME)
            {
                PrintError("Default Database Name Detected, change this and reload plugin");
                halt = true;
            }

            if (config.serverTag == ConfigData.DEFAULT_SERVER_TAG)
            {
                PrintError("Default Server Tag Detected, change this and reload plugin");
                halt = true;
            }

            if (halt)
            {
                Interface.Oxide.UnloadPlugin(Name);
                return;
            }

            Instance = this;
            _reportUploader = new GameObject().AddComponent<ReportUploader>();
            Subscribe(nameof(OnPerformanceReportGenerated));
            Subscribe(nameof(OnPlayerDisconnected));
            foreach (var player in BasePlayer.activePlayerList)
                OnPlayerConnected(player);
            Subscribe(nameof(OnPlayerConnected));
        }

        void OnPlayerConnected(BasePlayer player)
        {
            var action = new Action(() => GatherPlayerSecondStats(player));
            if (_playerStatsActions.ContainsKey(player.userID))
                player.CancelInvoke(_playerStatsActions[player.userID]);
            _playerStatsActions[player.userID] = action;
            player.InvokeRepeating(action, UnityEngine.Random.Range(0.5f, 1.5f), 1f);
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            player.CancelInvoke(_playerStatsActions[player.userID]);
            _playerStatsActions.Remove(player.userID);
        }

        void GatherPlayerSecondStats(BasePlayer player)
        {
            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            _stringBuilder.Append("connection_latency,server=");
            _stringBuilder.Append(config.serverTag);
            _stringBuilder.Append(",steamid=");
            _stringBuilder.Append(player.UserIDString);
            _stringBuilder.Append(",ip=");
            _stringBuilder.Append(player.net.connection.ipaddress);
            _stringBuilder.Append(" ping=");
            var averagePing = Net.sv.GetAveragePing(player.net.connection);
            _stringBuilder.Append(averagePing);
            _stringBuilder.Append("i,packet_loss=");
            var packetLoss = Net.sv.GetStat(player.net.connection, BaseNetwork.StatTypeLong.PacketLossLastSecond);
            _stringBuilder.Append(packetLoss);
            _stringBuilder.Append("i ");
            _stringBuilder.Append(epochNow);
            _reportUploader.AddToSendBuffer(_stringBuilder.ToString());
            _stringBuilder.Clear();
        }

        void Unload()
        {
            Unsubscribe(nameof(OnPerformanceReportGenerated));
            Unsubscribe(nameof(OnPlayerConnected));
            Unsubscribe(nameof(OnPlayerDisconnected));
            foreach (var player in _playerStatsActions)
            {
                var basePlayer = BasePlayer.FindByID(player.Key);
                if (basePlayer == null) continue;
                basePlayer.CancelInvoke(player.Value);
            }
            if (_reportUploader == null) return;
            _reportUploader.Stop();
            UnityEngine.Object.Destroy(_reportUploader.gameObject);
            Instance = null;
        }

        void Loaded()
        {
            Unsubscribe(nameof(OnPerformanceReportGenerated));
            Unsubscribe(nameof(OnPlayerConnected));
            Unsubscribe(nameof(OnPlayerDisconnected));
        }

        void OnPerformanceReportGenerated()
        {
            var current = Performance.current;
            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            _stringBuilder.Append("framerate,server=");
            _stringBuilder.Append(config.serverTag);
            _stringBuilder.Append(" instant=");
            _stringBuilder.Append(current.frameRate);
            _stringBuilder.Append(",average=");
            _stringBuilder.Append(current.frameRateAverage);
            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            _stringBuilder.Append("frametime,server=");
            _stringBuilder.Append(config.serverTag);
            _stringBuilder.Append(" instant=");
            _stringBuilder.Append(current.frameTime);
            _stringBuilder.Append(",average=");
            _stringBuilder.Append(current.frameTimeAverage);
            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            _stringBuilder.Append("memory,server=");
            _stringBuilder.Append(config.serverTag);
            _stringBuilder.Append(" used=");
            _stringBuilder.Append(current.memoryUsageSystem);
            _stringBuilder.Append("i,collections=");
            _stringBuilder.Append(current.memoryCollections);
            _stringBuilder.Append("i,allocations=");
            _stringBuilder.Append(current.memoryAllocations);
            _stringBuilder.Append("i,gc=");
            _stringBuilder.Append(current.gcTriggered);
            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            _stringBuilder.Append("tasks,server=");
            _stringBuilder.Append(config.serverTag);
            _stringBuilder.Append(" load_balancer=");
            _stringBuilder.Append(current.loadBalancerTasks);
            _stringBuilder.Append("i,invoke_handler=");
            _stringBuilder.Append(current.invokeHandlerTasks);
            _stringBuilder.Append("i,workshop_skins_queue=");
            _stringBuilder.Append(current.workshopSkinsQueued);
            _stringBuilder.Append("i ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            var bytesReceivedLastSecond = Net.sv.GetStat(null, BaseNetwork.StatTypeLong.BytesReceived_LastSecond);
            var bytesSentLastSecond = Net.sv.GetStat(null, BaseNetwork.StatTypeLong.BytesSent_LastSecond);
            var packetLossLastSecond = Net.sv.GetStat(null, BaseNetwork.StatTypeLong.PacketLossLastSecond);

            _stringBuilder.Append("network,server=");
            _stringBuilder.Append(config.serverTag);
            _stringBuilder.Append(" bytes_received=");
            _stringBuilder.Append(bytesReceivedLastSecond);
            _stringBuilder.Append("i,bytes_sent=");
            _stringBuilder.Append(bytesSentLastSecond);
            _stringBuilder.Append("i,packet_loss=");
            _stringBuilder.Append(packetLossLastSecond);
            _stringBuilder.Append("i ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            _stringBuilder.Append("players,server=");
            _stringBuilder.Append(config.serverTag);
            _stringBuilder.Append(" count=");
            _stringBuilder.Append(BasePlayer.activePlayerList.Count);
            _stringBuilder.Append("i,joining=");
            _stringBuilder.Append(ServerMgr.Instance.connectionQueue.Joining);
            _stringBuilder.Append("i,queued=");
            _stringBuilder.Append(ServerMgr.Instance.connectionQueue.Queued);
            _stringBuilder.Append("i ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            _stringBuilder.Append("entities,server=");
            _stringBuilder.Append(config.serverTag);
            _stringBuilder.Append(" count=");
            _stringBuilder.Append(BaseNetworkable.serverEntities.Count);
            _stringBuilder.Append("i ");
            _stringBuilder.Append(epochNow);
            _reportUploader.AddToSendBuffer(_stringBuilder.ToString());
            _stringBuilder.Clear();
        }

        class ReportUploader : MonoBehaviour
        {
            const int _batchAmount = 200;
            const int _sendBufferCapacity = 10000;
            readonly List<string> _sendBuffer = new List<string>(_sendBufferCapacity);
            readonly StringBuilder _payloadBuilder = new StringBuilder();
            bool _isRunning = false;
            ushort _attempt = 0;
            byte[] _data = null;
            Uri _uri = null;

            public void AddToSendBuffer(string payload)
            {
                if (_sendBuffer.Count == _sendBufferCapacity)
                    _sendBuffer.RemoveAt(0);

                _sendBuffer.Add(payload);

                if (!_isRunning)
                    StartCoroutine(SendBufferLoop());
            }

            IEnumerator SendBufferLoop()
            {
                _isRunning = true;
                yield return null;

                while (_sendBuffer.Count > 0 && _isRunning)
                {
                    int amountToTake = Mathf.Min(_sendBuffer.Count, _batchAmount);
                    for (int i = 0; i < amountToTake; i++)
                    {
                        _payloadBuilder.Append(_sendBuffer[i]);
                        _payloadBuilder.Append("\n");
                    }
                    _sendBuffer.RemoveRange(0, amountToTake);
                    _attempt = 0;
                    _data = Encoding.UTF8.GetBytes(_payloadBuilder.ToString());
                    _uri = Instance.BaseUri;
                    _payloadBuilder.Clear();
                    yield return SendRequest();
                }
                _isRunning = false;
            }

            IEnumerator SendRequest()
            {
                var request = new UnityWebRequest(_uri, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(_data),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                yield return request.SendWebRequest();

                if (request.isNetworkError)
                {
                    if (_attempt >= 5)
                    {
                        Debug.LogError($"Error submitting metric: 5 consecutive network failures");
                        yield break;
                    }

                    _attempt++;
                    yield return SendRequest();
                    yield break;
                }

                if (request.isHttpError)
                {
                    Debug.LogError($"Error submitting metric: {request.error}");
                    yield break;
                }
            }

            void OnDestroy()
            {
                Stop();
            }

            public void Stop()
            {
                _isRunning = false;
                StopAllCoroutines();
            }
        }

        #region Configuration
        ConfigData config = new ConfigData();

        class ConfigData
        {
            public const string DEFAULT_INFLUX_DB_URL = "http://exampledb.com";
            public const string DEFAULT_INFLUX_DB_NAME = "CHANGEME_rust_server_example";
            public const string DEFAULT_INFLUX_DB_USER = "admin";
            public const string DEFAULT_INFLUX_DB_PASSWORD = "adminadmin";
            public const string DEFAULT_SERVER_TAG = "CHANGEME-01";

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

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null) LoadDefaultConfig();

            }
            catch
            {
                PrintError("Your config seems to be corrupted. Will load defaults.");
                LoadDefaultConfig();
                return;
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig() => config = new ConfigData();

        protected override void SaveConfig() => Config.WriteObject(config);

        #endregion
    }
}
