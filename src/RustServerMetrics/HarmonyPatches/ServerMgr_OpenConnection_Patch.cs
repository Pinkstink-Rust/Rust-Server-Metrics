using Harmony;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch(typeof(ServerMgr), nameof(ServerMgr.OpenConnection))]
    public class ServerMgr_OpenConnection_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            MetricsLogger.Instance?.OnServerStarted();
        }
    }
}
