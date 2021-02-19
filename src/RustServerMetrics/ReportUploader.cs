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
        const int _batchAmount = 200;
        const int _sendBufferCapacity = 10000;

        readonly List<string> _sendBuffer = new List<string>(_sendBufferCapacity);
        readonly StringBuilder _payloadBuilder = new StringBuilder();

        bool _isRunning = false;
        ushort _attempt = 0;
        byte[] _data = null;
        Uri _uri = null;
        MetricsLogger _metricsLogger;

        public bool IsRunning => _isRunning;
        public int BufferSize => _sendBuffer.Count;

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
                int amountToTake = Mathf.Min(_sendBuffer.Count, _batchAmount);
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
                Debug.LogError(request.downloadHandler.text);
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
}
