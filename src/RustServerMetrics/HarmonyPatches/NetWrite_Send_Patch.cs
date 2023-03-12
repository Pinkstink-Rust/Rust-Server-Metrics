using Harmony;
using Network;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch(typeof(NetWrite), nameof(NetWrite.Send))]
    public class NetWrite_Send_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(NetWrite __instance, SendInfo info)
        {
            if (SingletonComponent<MetricsLogger>.Instance == null)
                return;
            
            MetricsLogger.Instance.OnNetWriteSend(__instance, info);
        }
    }
}
