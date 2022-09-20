using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch]
    public class RPCServer_Attribute_Method_Patch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods(HarmonyInstance harmonyInstance)
        {
            var baseNetworkableType = typeof(BaseNetworkable);
            var baseNetworkableAssembly = baseNetworkableType.Assembly;
            Stack<Type> typesToScan = new Stack<Type>(baseNetworkableAssembly.GetTypes());

            while (typesToScan.TryPop(out Type type))
            {
                var subTypes = type.GetNestedTypes();
                for (int i = 0; i < subTypes.Length; i++)
                    typesToScan.Push(subTypes[i]);

                var methods = type.GetMethods();
                for (int i = 0; i < methods.Length; i++)
                {
                    var method = methods[i];
                    if (method.GetCustomAttribute<BaseEntity.RPC_Server>() != null)
                    {
                        yield return method;
                    }
                }
            }
        }

        [HarmonyPrefix]
        public static void Prefix(ref DateTimeOffset __state)
        {
            __state = DateTimeOffset.UtcNow;
        }

        [HarmonyPostfix]
        public static void Postfix(MethodInfo __originalMethod, DateTimeOffset __state)
        {
            var duration = DateTimeOffset.UtcNow - __state;
            MetricsLogger.Instance?.OnServerRPC(__originalMethod, duration.TotalMilliseconds);
        }
    }
}
