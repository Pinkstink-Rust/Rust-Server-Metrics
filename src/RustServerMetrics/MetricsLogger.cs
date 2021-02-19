using Network;
using Newtonsoft.Json;
using RustServerMetrics.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RustServerMetrics
{
    public class MetricsLogger : SingletonComponent<MetricsLogger>
    {
        const string CONFIGURATION_PATH = "HarmonyMods_Data/ServerMetrics/Configuration.json";
        readonly StringBuilder _stringBuilder = new StringBuilder();
        readonly Dictionary<ulong, Action> _playerStatsActions = new Dictionary<ulong, Action>();
        readonly Dictionary<Message.Type, int> _networkUpdates = new Dictionary<Message.Type, int>();

        bool _ready = false;
        ConfigData _configuration;
        ReportUploader _reportUploader;
        Message.Type _lastMessageType;
        Uri _baseUri;

        public Uri BaseUri
        {
            get
            {
                if (_baseUri == null)
                {
                    var uri = new Uri(_configuration.databaseUrl);
                    _baseUri = new Uri(uri, $"/write?db={_configuration.databaseName}&precision=ms&u={_configuration.databaseUser}&p={_configuration.databasePassword}");
                }
                return _baseUri;
            }
        }

        internal static void Initialize()
        {
            new GameObject().AddComponent<MetricsLogger>();
        }

        override protected void Awake()
        {
            base.Awake();
            var messageTypes = Enum.GetValues(typeof(Message.Type));
            foreach (Message.Type messageType in messageTypes)
            {
                if (_networkUpdates.ContainsKey(messageType))
                    continue;
                _networkUpdates.Add(messageType, 0);
            }
            _reportUploader = gameObject.AddComponent<ReportUploader>();
            RegisterCommands();

            LoadConfiguration();
            if (ValidateConfiguration())
            {
                if (!_configuration.enabled)
                {
                    Debug.LogWarning("[ServerMetrics]: Metrics gathering has been disabled in the configuration");
                    return;
                }

                InvokeRepeating(LogNetworkUpdates, UnityEngine.Random.Range(0.05f, 0.15f), 0.1f);
                _ready = true;
            }
        }

        void RegisterCommands()
        {
            const string commandPrefix = "servermetrics";
            ConsoleSystem.Command reloadCommand = new ConsoleSystem.Command()
            {
                Name = "reload",
                Parent = commandPrefix,
                FullName = commandPrefix + "." + "reload",
                ServerAdmin = true,
                Variable = false,
                Call = new Action<ConsoleSystem.Arg>(ReloadCommand)
            };
            ConsoleSystem.Index.Server.Dict[commandPrefix + "." + "reload"] = reloadCommand;

            ConsoleSystem.Command statusCommand = new ConsoleSystem.Command()
            {
                Name = "status",
                Parent = commandPrefix,
                FullName = commandPrefix + "." + "status",
                ServerAdmin = true,
                Variable = false,
                Call = new Action<ConsoleSystem.Arg>(StatusCommand)
            };
            ConsoleSystem.Index.Server.Dict[commandPrefix + "." + "status"] = statusCommand;

            ConsoleSystem.Command[] allCommands = ConsoleSystem.Index.All.Concat(new ConsoleSystem.Command[] { reloadCommand, statusCommand }).ToArray();
            // Would be nice if this had a public setter, or better yet, a register command helper
            typeof(ConsoleSystem.Index)
                .GetProperty(nameof(ConsoleSystem.Index.All), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .SetValue(null, allCommands);
        }

        void StatusCommand(ConsoleSystem.Arg arg)
        {
            _stringBuilder.Clear();
            _stringBuilder.AppendLine("[ServerMetrics]: Status Overview");
            _stringBuilder.Append("\tReady: "); _stringBuilder.Append(_ready); _stringBuilder.AppendLine();
            _stringBuilder.AppendLine("\tReport Uploader:");
            _stringBuilder.Append("\t\tRunning: "); _stringBuilder.Append(_reportUploader.IsRunning); _stringBuilder.AppendLine();
            _stringBuilder.Append("\t\tIn Buffer: "); _stringBuilder.Append(_reportUploader.BufferSize); _stringBuilder.AppendLine();

            arg.ReplyWith(_stringBuilder.ToString());
        }

        void ReloadCommand(ConsoleSystem.Arg arg)
        {
            LoadConfiguration();
            if (!ValidateConfiguration() || _configuration.enabled == false)
            {
                _ready = false;
                CancelInvoke(LogNetworkUpdates);
                foreach (var player in _playerStatsActions)
                {
                    var basePlayer = BasePlayer.FindByID(player.Key);
                    if (basePlayer == null) continue;
                    basePlayer.CancelInvoke(player.Value);
                }
                _reportUploader.Stop();

                if (!_configuration.enabled)
                {
                    Debug.LogWarning("[ServerMetrics]: Metrics gathering has been disabled in the configuration");
                    return;
                }
            }
            else if (!_ready)
            {
                _ready = true;
                foreach (var player in BasePlayer.activePlayerList) OnPlayerInit(player);
                InvokeRepeating(LogNetworkUpdates, UnityEngine.Random.Range(0.05f, 0.15f), 0.1f);
            }
            arg.ReplyWith("[ServerMetrics]: Configuration reloaded");
        }

        internal void OnPlayerInit(BasePlayer player)
        {
            if (!_ready) return;
            var action = new Action(() => GatherPlayerSecondStats(player));
            if (_playerStatsActions.ContainsKey(player.userID))
                player.CancelInvoke(_playerStatsActions[player.userID]);
            _playerStatsActions[player.userID] = action;
            player.InvokeRepeating(action, UnityEngine.Random.Range(0.5f, 1.5f), 1f);
        }

        internal void OnPlayerDisconnected(BasePlayer player)
        {
            if (!_ready) return;
            player.CancelInvoke(_playerStatsActions[player.userID]);
            _playerStatsActions.Remove(player.userID);
        }

        internal void OnNetWritePacketID(Message.Type messageType)
        {
            if (!_ready) return;
            _lastMessageType = messageType;
        }

        internal void OnNetWriteSend(SendInfo sendInfo)
        {
            if (!_ready) return;
            if (sendInfo.connection != null)
            {
                _networkUpdates[_lastMessageType] += 1;
            }
            else if (sendInfo.connections != null)
            {
                _networkUpdates[_lastMessageType] += sendInfo.connections.Count;
            }
        }

        void LogNetworkUpdates()
        {
            if (_networkUpdates.Count < 1) return;
            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            _stringBuilder.Clear();
            _stringBuilder.Append("network_updates,server=");
            _stringBuilder.Append(_configuration.serverTag);
            _stringBuilder.Append(" ");

            var enumerator = _networkUpdates.GetEnumerator();
            var networkUpdate = enumerator.Current;
            _stringBuilder.Append(networkUpdate.Key.ToString());
            _stringBuilder.Append("=");
            _stringBuilder.Append(networkUpdate.Value.ToString());
            _stringBuilder.Append("i");

            while (enumerator.MoveNext())
            {
                networkUpdate = enumerator.Current;
                _stringBuilder.Append(",");
                _stringBuilder.Append(networkUpdate.Key.ToString());
                _stringBuilder.Append("=");
                _stringBuilder.Append(networkUpdate.Value.ToString());
                _stringBuilder.Append("i");
            }

            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);
            _reportUploader.AddToSendBuffer(_stringBuilder.ToString());

            var enumKeys = _networkUpdates.Keys.ToArray();
            foreach (var key in enumKeys)
            {
                _networkUpdates[key] = 0;
            }
        }

        void GatherPlayerSecondStats(BasePlayer player)
        {
            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            _stringBuilder.Clear();
            _stringBuilder.Append("connection_latency,server=");
            _stringBuilder.Append(_configuration.serverTag);
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
        }

        internal void OnPerformanceReportGenerated()
        {
            if (!_ready) return;
            var current = Performance.current;
            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            _stringBuilder.Clear();
            _stringBuilder.Append("framerate,server=");
            _stringBuilder.Append(_configuration.serverTag);
            _stringBuilder.Append(" instant=");
            _stringBuilder.Append(current.frameRate);
            _stringBuilder.Append(",average=");
            _stringBuilder.Append(current.frameRateAverage);
            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            _stringBuilder.Append("frametime,server=");
            _stringBuilder.Append(_configuration.serverTag);
            _stringBuilder.Append(" instant=");
            _stringBuilder.Append(current.frameTime);
            _stringBuilder.Append(",average=");
            _stringBuilder.Append(current.frameTimeAverage);
            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            _stringBuilder.Append("memory,server=");
            _stringBuilder.Append(_configuration.serverTag);
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
            _stringBuilder.Append(_configuration.serverTag);
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
            _stringBuilder.Append(_configuration.serverTag);
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
            _stringBuilder.Append(_configuration.serverTag);
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
            _stringBuilder.Append(_configuration.serverTag);
            _stringBuilder.Append(" count=");
            _stringBuilder.Append(BaseNetworkable.serverEntities.Count);
            _stringBuilder.Append("i ");
            _stringBuilder.Append(epochNow);
            _reportUploader.AddToSendBuffer(_stringBuilder.ToString());
        }

        // This method presently does nothing as we are awaiting a Harmony2 upgrade from Facepunch
        internal TimeWarning OnNewTimeWarning(string name, int maxmilliseconds)
        {
            Debug.Log("OnNewTimeWarning: " + name);
            return null;
        }

        // This method presently does nothing as we are awaiting a Harmony2 upgrade from Facepunch
        internal void OnDisposeTimeWarning(TimeWarning instance)
        {
            Debug.Log("OnTimeWarningDispose");
        }

        bool ValidateConfiguration()
        {
            if (_configuration == null) return false;

            bool valid = true;
            if (_configuration.databaseUrl == ConfigData.DEFAULT_INFLUX_DB_URL)
            {
                Debug.LogError("[ServerMetrics]: Default database url detected in configuration, loading aborted");
                valid = false;
            }

            if (_configuration.databaseName == ConfigData.DEFAULT_INFLUX_DB_NAME)
            {
                Debug.LogError("[ServerMetrics]: Default database name detected in configuration, loading aborted");
                valid = false;
            }

            if (_configuration.serverTag == ConfigData.DEFAULT_SERVER_TAG)
            {
                Debug.LogError("[ServerMetrics]: Default server tag detected in configuration, loading aborted");
                valid = false;
            }

            return valid;
        }

        void LoadConfiguration()
        {
            try
            {
                var configStr = File.ReadAllText(CONFIGURATION_PATH);
                _configuration = JsonConvert.DeserializeObject<ConfigData>(configStr);
                if (_configuration == null) _configuration = new ConfigData();
                var uri = new Uri(_configuration.databaseUrl);
                _baseUri = new Uri(uri, $"/write?db={_configuration.databaseName}&precision=ms&u={_configuration.databaseUser}&p={_configuration.databasePassword}");
            }
            catch
            {
                Debug.LogError("[ServerMetrics]: The configuration seems to be missing or malformed. Defaults will be loaded.");
                _configuration = new ConfigData();

                if (File.Exists(CONFIGURATION_PATH))
                {
                    return;
                }
            }
            SaveConfiguration();
        }

        void SaveConfiguration()
        {
            try
            {
                var configFileInfo = new FileInfo(CONFIGURATION_PATH);
                if (!configFileInfo.Directory.Exists) configFileInfo.Directory.Create();
                var serializedConfiguration = JsonConvert.SerializeObject(_configuration, Formatting.Indented);
                File.WriteAllText(CONFIGURATION_PATH, serializedConfiguration);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ServerMetrics]: Failed to write configuration file");
                Debug.LogException(ex);
            }
        }
    }
}
