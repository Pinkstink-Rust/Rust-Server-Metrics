using Facepunch;
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
        readonly Dictionary<ulong, uint> _perfReportDelayCounter = new Dictionary<ulong, uint>();
        readonly Dictionary<Message.Type, int> _networkUpdates = new Dictionary<Message.Type, int>();
        internal readonly HashSet<ulong> _requestedClientPerf = new HashSet<ulong>(1000);

        public bool Ready { get; private set; }
        internal ConfigData Configuration { get; private set; }
        ReportUploader _reportUploader;
        Message.Type _lastMessageType;
        Uri _baseUri;

        public bool DebugLogging => Configuration?.debugLogging == true;
        public Uri BaseUri
        {
            get
            {
                if (_baseUri == null)
                {
                    var uri = new Uri(Configuration.databaseUrl);
                    _baseUri = new Uri(uri, $"/write?db={Configuration.databaseName}&precision=ms&u={Configuration.databaseUser}&p={Configuration.databasePassword}");
                }
                return _baseUri;
            }
        }

        internal static void Initialize()
        {
            new GameObject().AddComponent<MetricsLogger>();
            // Pool 100 Metrics Time Warnings
            Pool.FillBuffer<MetricsTimeWarning>(100);
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
                if (!Configuration.enabled)
                {
                    Debug.LogWarning("[ServerMetrics]: Metrics gathering has been disabled in the configuration");
                    return;
                }

                InvokeRepeating(LogNetworkUpdates, UnityEngine.Random.Range(0.25f, 0.75f), 0.5f);
                Ready = true;
            }
        }

        void RegisterCommands()
        {
            const string commandPrefix = "servermetrics";
            ConsoleSystem.Command reloadCfgCommand = new ConsoleSystem.Command()
            {
                Name = "reloadcfg",
                Parent = commandPrefix,
                FullName = commandPrefix + "." + "reloadcfg",
                ServerAdmin = true,
                Variable = false,
                Call = new Action<ConsoleSystem.Arg>(ReloadCfgCommand)
            };
            ConsoleSystem.Index.Server.Dict[commandPrefix + "." + "reloadcfg"] = reloadCfgCommand;

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

            ConsoleSystem.Command[] allCommands = ConsoleSystem.Index.All.Concat(new ConsoleSystem.Command[] { reloadCfgCommand, statusCommand }).ToArray();
            // Would be nice if this had a public setter, or better yet, a register command helper
            typeof(ConsoleSystem.Index)
                .GetProperty(nameof(ConsoleSystem.Index.All), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .SetValue(null, allCommands);
        }

        void StatusCommand(ConsoleSystem.Arg arg)
        {
            _stringBuilder.Clear();
            _stringBuilder.AppendLine("[ServerMetrics]: Status");
            _stringBuilder.AppendLine("Overview");
            _stringBuilder.Append("\tReady: "); _stringBuilder.Append(Ready); _stringBuilder.AppendLine();
            _stringBuilder.AppendLine("Report Uploader:");
            _stringBuilder.Append("\tRunning: "); _stringBuilder.Append(_reportUploader.IsRunning); _stringBuilder.AppendLine();
            _stringBuilder.Append("\tIn Buffer: "); _stringBuilder.Append(_reportUploader.BufferSize); _stringBuilder.AppendLine();
            arg.ReplyWith(_stringBuilder.ToString());
        }

        void ReloadCfgCommand(ConsoleSystem.Arg arg)
        {
            LoadConfiguration();
            if (!ValidateConfiguration() || Configuration.enabled == false)
            {
                Ready = false;
                CancelInvoke(LogNetworkUpdates);
                foreach (var player in _playerStatsActions)
                {
                    var basePlayer = BasePlayer.FindByID(player.Key);
                    if (basePlayer == null) continue;
                    basePlayer.CancelInvoke(player.Value);
                }
                _reportUploader.Stop();

                if (!Configuration.enabled)
                {
                    arg.ReplyWith("[ServerMetrics]: Metrics gathering has been disabled in the configuration");
                    return;
                }
            }
            else if (!Ready)
            {
                Ready = true;
                foreach (var player in BasePlayer.activePlayerList) OnPlayerInit(player);
                InvokeRepeating(LogNetworkUpdates, UnityEngine.Random.Range(0.25f, 0.75f), 0.5f);
            }
            arg.ReplyWith("[ServerMetrics]: Configuration reloaded");
        }

        internal void OnPlayerInit(BasePlayer player)
        {
            if (!Ready) return;
            var action = new Action(() => GatherPlayerSecondStats(player));
            if (_playerStatsActions.TryGetValue(player.userID, out Action existingAction))
                player.CancelInvoke(existingAction);
            _playerStatsActions[player.userID] = action;
            player.InvokeRepeating(action, UnityEngine.Random.Range(0.5f, 1.5f), 1f);
        }

        internal void OnPlayerDisconnected(BasePlayer player)
        {
            if (!Ready) return;
            if (_playerStatsActions.TryGetValue(player.userID, out Action action))
                player.CancelInvoke(action);
            _playerStatsActions.Remove(player.userID);
            _requestedClientPerf.Remove(player.userID);
        }

        internal void OnNetWritePacketID(Message.Type messageType)
        {
            if (!Ready) return;
            _lastMessageType = messageType;
        }

        internal void OnNetWriteSend(SendInfo sendInfo)
        {
            if (!Ready) return;
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
            _stringBuilder.Append(Configuration.serverTag);
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

        internal void OnOxidePluginMetrics(Dictionary<string, double> metrics)
        {
            if (!Ready) return;
            if (metrics.Count < 1) return;
            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            foreach (var metric in metrics)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append("oxide_plugins,server=");
                _stringBuilder.Append(Configuration.serverTag);
                _stringBuilder.Append(",plugin=\"");
                _stringBuilder.Append(metric.Key);
                _stringBuilder.Append("\" hookTime=");
                _stringBuilder.Append(metric.Value);
                _stringBuilder.Append(" ");
                _stringBuilder.Append(epochNow);
                _reportUploader.AddToSendBuffer(_stringBuilder.ToString());
            }
        }

        internal void OnClientPerformanceReport(BasePlayer player, int memory, int garbage, float fps, int uptime, bool streamerMode)
        {
            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            _stringBuilder.Clear();
            _stringBuilder.Append("client_performance,server=");
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(",steamid=");
            _stringBuilder.Append(player.UserIDString);
            _stringBuilder.Append(" memory=");
            _stringBuilder.Append(memory);
            _stringBuilder.Append("i,garbage=");
            _stringBuilder.Append(garbage);
            _stringBuilder.Append("i,fps=");
            _stringBuilder.Append(fps);
            _stringBuilder.Append(",uptime=");
            _stringBuilder.Append(uptime);
            _stringBuilder.Append("i,streamerMode=");
            _stringBuilder.Append(streamerMode);
            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);
            _reportUploader.AddToSendBuffer(_stringBuilder.ToString());
            _requestedClientPerf.Remove(player.userID);
        }

        void GatherPlayerSecondStats(BasePlayer player)
        {
            if (!player.IsReceivingSnapshot)
            {
                _perfReportDelayCounter.TryGetValue(player.userID, out uint perfReportCounter);
                if (perfReportCounter < 4)
                {
                    _perfReportDelayCounter[player.userID] = perfReportCounter + 1;
                }
                else
                {
                    _perfReportDelayCounter[player.userID] = 0;
                    _requestedClientPerf.Add(player.userID);
                    player.ClientRPCPlayer(null, player, "GetPerformanceReport");
                }
            }

            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            _stringBuilder.Clear();
            _stringBuilder.Append("connection_latency,server=");
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(",steamid=");
            _stringBuilder.Append(player.UserIDString);
            _stringBuilder.Append(",ip=");
            var colonIndex = player.net.connection.ipaddress.LastIndexOf(':');
            var portStrippedIp = player.net.connection.ipaddress.Substring(0, colonIndex);
            _stringBuilder.Append(portStrippedIp);
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
            if (!Ready) return;
            var current = Performance.current;
            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            _stringBuilder.Clear();
            _stringBuilder.Append("framerate,server=");
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(" instant=");
            _stringBuilder.Append(current.frameRate);
            _stringBuilder.Append(",average=");
            _stringBuilder.Append(current.frameRateAverage);
            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            _stringBuilder.Append("frametime,server=");
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(" instant=");
            _stringBuilder.Append(current.frameTime);
            _stringBuilder.Append(",average=");
            _stringBuilder.Append(current.frameTimeAverage);
            _stringBuilder.Append(" ");
            _stringBuilder.Append(epochNow);
            _stringBuilder.Append("\n");

            _stringBuilder.Append("memory,server=");
            _stringBuilder.Append(Configuration.serverTag);
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
            _stringBuilder.Append(Configuration.serverTag);
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
            _stringBuilder.Append(Configuration.serverTag);
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
            _stringBuilder.Append(Configuration.serverTag);
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
            _stringBuilder.Append(Configuration.serverTag);
            _stringBuilder.Append(" count=");
            _stringBuilder.Append(BaseNetworkable.serverEntities.Count);
            _stringBuilder.Append("i ");
            _stringBuilder.Append(epochNow);
            _reportUploader.AddToSendBuffer(_stringBuilder.ToString());
        }

        bool ValidateConfiguration()
        {
            if (Configuration == null) return false;

            bool valid = true;
            if (Configuration.databaseUrl == ConfigData.DEFAULT_INFLUX_DB_URL)
            {
                Debug.LogError("[ServerMetrics]: Default database url detected in configuration, loading aborted");
                valid = false;
            }

            if (Configuration.databaseName == ConfigData.DEFAULT_INFLUX_DB_NAME)
            {
                Debug.LogError("[ServerMetrics]: Default database name detected in configuration, loading aborted");
                valid = false;
            }

            if (Configuration.serverTag == ConfigData.DEFAULT_SERVER_TAG)
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
                Configuration = JsonConvert.DeserializeObject<ConfigData>(configStr);
                if (Configuration == null) Configuration = new ConfigData();
                var uri = new Uri(Configuration.databaseUrl);
                _baseUri = new Uri(uri, $"/write?db={Configuration.databaseName}&precision=ms&u={Configuration.databaseUser}&p={Configuration.databasePassword}");
            }
            catch
            {
                Debug.LogError("[ServerMetrics]: The configuration seems to be missing or malformed. Defaults will be loaded.");
                Configuration = new ConfigData();

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
                var serializedConfiguration = JsonConvert.SerializeObject(Configuration, Formatting.Indented);
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
