using Facepunch;
using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace RustServerMetrics
{
    public class MetricsTimeWarning
    {
        static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static readonly Type _cachedType = typeof(TimeWarning);
        static readonly StringBuilder _stringBuilder = new StringBuilder();
        static readonly Hashtable _reverseLookup = new Hashtable();

        readonly TimeWarning _timeWarning;

        string _name;
        float _maxMilliseconds;
        double _startEpoch;

        public MetricsTimeWarning()
        {
            _timeWarning = (TimeWarning)Activator.CreateInstance(_cachedType, true);
            _reverseLookup.Add(_timeWarning, this);
        }

        public static double GetCurrentEpoch() => DateTime.UtcNow.Subtract(_epoch).TotalMilliseconds;

        public static TimeWarning GetTimeWarning(string name, int maxMilliseconds)
        {
            if (Rust.Application.isLoading) return null;
            var metricsLogger = SingletonComponent<MetricsLogger>.Instance;
            if (metricsLogger == null || !metricsLogger.Ready) return null;
            var instance = Pool.Get<MetricsTimeWarning>();
            instance._name = name;
            instance._maxMilliseconds = Mathf.Max(maxMilliseconds, 0.5f);
            instance._startEpoch = GetCurrentEpoch();
            return instance._timeWarning;
        }

        public static void DisposeTimeWarning(TimeWarning timeWarning)
        {
            if (timeWarning == null) return;
            var instance = _reverseLookup[timeWarning] as MetricsTimeWarning;
            var currentEpoch = GetCurrentEpoch();
            var duration = currentEpoch - instance._startEpoch;
            if (duration >= instance._maxMilliseconds)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append("TimeWarning: ");
                _stringBuilder.Append("["); _stringBuilder.Append(instance._name); _stringBuilder.Append("] ");
                _stringBuilder.Append(duration); _stringBuilder.Append("ms");
                Debug.LogWarning(_stringBuilder.ToString());
            }
            Pool.Free(ref instance);
        }
    }
}
