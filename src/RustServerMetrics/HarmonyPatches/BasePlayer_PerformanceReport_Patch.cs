using Harmony;
using System.Text;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PerformanceReport))]
    public class BasePlayer_PerformanceReport_Patch
    {
        static readonly StringBuilder _stringBuilder = new StringBuilder();

        [HarmonyPrefix]
        public static bool Prefix(BasePlayer __instance, BaseEntity.RPCMessage msg)
        {
            if (!MetricsLogger.Instance._requestedClientPerf.Contains(__instance.userID)) return true;

            int memory = msg.read.Int32();
            int gc = msg.read.Int32();
            float fps = msg.read.Float();
            int uptime = msg.read.Int32();
            bool streamerMode = msg.read.Bit();

            MetricsLogger.Instance.OnClientPerformanceReport(__instance, memory, gc, fps, uptime, streamerMode);
            return false;
        }
    }
}