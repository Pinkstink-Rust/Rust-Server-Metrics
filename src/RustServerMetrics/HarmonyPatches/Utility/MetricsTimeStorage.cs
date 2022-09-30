using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RustServerMetrics.HarmonyPatches.Utility;

public class MetricsTimeStorage<TKey>
{
    private readonly string _metricKey;
    private readonly Action<StringBuilder, TKey> _stringBuilderSerializer;
    private Dictionary<TKey, double> dict = new ();
    private readonly StringBuilder sb = new();
    
    public MetricsTimeStorage(string metricKey, Action<StringBuilder, TKey> stringBuilderSerializer)
    {
        _metricKey = metricKey;
        _stringBuilderSerializer = stringBuilderSerializer;
    }
    
    public void LogTime(TKey key, double milliseconds)
    {
        if (!MetricsLogger.Instance.Ready) 
            return;
        
        if (!dict.TryGetValue(key, out double currentDuration))
        {
            dict.Add(key, milliseconds);
            return;
        }
        
        dict[key] = currentDuration + milliseconds;        
    }

    public void SerializeToStringBuilder()
    {
        if (!MetricsLogger.Instance.Ready) 
            return;
        
        foreach (var item in dict)
        {
            var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            sb.Clear();
            
            sb.Append(_metricKey);
            sb.Append(",server=");
            sb.Append(MetricsLogger.Instance.Configuration.serverTag);
            
            _stringBuilderSerializer.Invoke(sb, item.Key);
            
            sb.Append("\" duration=");
            sb.Append((float)item.Value);
            sb.Append(" ");
            sb.Append(epochNow);
            MetricsLogger.Instance.AddToSendBuffer(sb.ToString());
        }
 
        dict.Clear();
    }


}