using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace RustServerMetrics
{
    class ReportUploader : MonoBehaviour
    {
        const int _sendBufferCapacity = 100000;

        readonly Action _notifySubsequentNetworkFailuresAction;
        readonly Action _notifySubsequentHttpFailuresAction;

        readonly List<string> _sendBuffer = new List<string>(_sendBufferCapacity);
        readonly StringBuilder _payloadBuilder = new StringBuilder();

        bool _isRunning = false;
        ushort _attempt = 0;
        byte[] _data = null;
        Uri _uri = null;
        MetricsLogger _metricsLogger;

        bool _throttleNetworkErrorMessages = false;
        uint _accumulatedNetworkErrors = 0;

        bool _throttleHttpErrorMessages = false;
        uint _accumulatedHttpErrors = 0;

        public ushort BatchSize
        {
            get
            {
                var configVal = _metricsLogger.Configuration?.batchSize ?? 1000;
                if (configVal < 1000) return 1000;
                return configVal;
            }
        }
        public bool IsRunning => _isRunning;
        public int BufferSize => _sendBuffer.Count;

        public ReportUploader()
        {
            _notifySubsequentNetworkFailuresAction = new Action(NotifySubsequentNetworkFailures);
            _notifySubsequentHttpFailuresAction = new Action(NotifySubsequentHttpFailures);
        }

        void Awake()
        {
            _metricsLogger = GetComponent<MetricsLogger>();
            if (_metricsLogger == null)
            {
                Debug.LogError("[ServerMetrics] ReportUploader failed to find the MetricsLogger component");
                Destroy(this);
            }
        }

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
                int amountToTake = Mathf.Min(_sendBuffer.Count, BatchSize);
                for (int i = 0; i < amountToTake; i++)
                {
                    _payloadBuilder.Append(_sendBuffer[i]);
                    _payloadBuilder.Append("\n");
                }
                _sendBuffer.RemoveRange(0, amountToTake);
                _attempt = 0;
                _data = Encoding.UTF8.GetBytes(_payloadBuilder.ToString());
                _uri = _metricsLogger.BaseUri;
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
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 5,
                useHttpContinue = true,
                redirectLimit = 5
            };
            yield return request.SendWebRequest();

            if (request.isNetworkError)
            {
                if (_attempt >= 2)
                {
                    if (_throttleNetworkErrorMessages)
                    {
                        _accumulatedNetworkErrors += 1;
                    }
                    else
                    {
                        Debug.LogError($"Two consecutive network failures occurred while submitting a batch of metrics");
                        InvokeHandler.Invoke(this, _notifySubsequentNetworkFailuresAction, 5);
                        _throttleNetworkErrorMessages = true;
                    }
                    yield break;
                }

                _attempt++;
                yield return SendRequest();
                yield break;
            }

            if (request.isHttpError)
            {
                if (_throttleHttpErrorMessages)
                {
                    _accumulatedHttpErrors += 1;
                }
                else
                {
                    Debug.LogError($"A HTTP error occurred while submitting batch of metrics: {request.error}");
                    if (_metricsLogger.DebugLogging) Debug.LogError(request.downloadHandler.text);
                    InvokeHandler.Invoke(this, _notifySubsequentHttpFailuresAction, 5);
                    _throttleHttpErrorMessages = true;
                }

                yield break;
            }
        }

        void NotifySubsequentNetworkFailures()
        {
            _throttleNetworkErrorMessages = false;
            if (_accumulatedNetworkErrors == 0) return;
            Debug.LogError($"{_accumulatedNetworkErrors} subsequent network errors occurred in the last 5 seconds");
            _accumulatedNetworkErrors = 0;
        }

        void NotifySubsequentHttpFailures()
        {
            _throttleHttpErrorMessages = false;
            if (_accumulatedHttpErrors == 0) return;
            Debug.LogError($"{_accumulatedHttpErrors} subsequent HTTP errors occurred in the last 5 seconds");
            _accumulatedHttpErrors = 0;
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
}
