using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace RustServerMetrics
{
    class ReportUploader : MonoBehaviour
    {
        const int _sendBufferCapacity = 100000;

        readonly List<string> _sendBuffer = new List<string>(_sendBufferCapacity);
        readonly StringBuilder _payloadBuilder = new StringBuilder();

        Socket _socket;

        bool _isRunning = false;
        byte[] _data = null;
        MetricsLogger _metricsLogger;

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
        }

        IEnumerator SendBufferLoop()
        {
            yield return CoroutineEx.waitForEndOfFrame;
            while (_isRunning)
            {
                if (_socket == null || _socket.Connected != true)
                {
                    if (_socket != null)
                    {
                        _socket.Close();
                        _socket.Dispose();
                        _socket = null;
                    }
                    _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                    {
                        SendTimeout = 10000
                    };
                    Debug.Log("[ServerMetrics] Connecting to QuestDb...");
                    var connectTask = _socket.ConnectAsync(_metricsLogger.Configuration.questDbHostName, _metricsLogger.Configuration.questDbPort);
                    yield return new WaitUntil(() => connectTask.IsCompleted);
                    if (!_socket.Connected)
                    {
                        Debug.LogError("[ServerMetrics] Failure connecting to QuestDb");
                        if (connectTask.IsFaulted) Debug.LogException(connectTask.Exception);
                        yield return CoroutineEx.waitForSeconds(5);
                        continue;
                    }
                    Debug.Log("[ServerMetrics] Connected to QuestDb");
                }

                while (_isRunning && _socket.Connected && _sendBuffer.Count > 0)
                {
                    int amountToTake = Mathf.Min(_sendBuffer.Count, BatchSize);
                    for (int i = 0; i < amountToTake; i++)
                    {
                        _payloadBuilder.Append(_sendBuffer[i]);
                        _payloadBuilder.Append("\n");
                    }
                    _sendBuffer.RemoveRange(0, amountToTake);
                    _data = Encoding.UTF8.GetBytes(_payloadBuilder.ToString());
                    _payloadBuilder.Clear();
                    var arraySegment = new ArraySegment<byte>(_data);
                    var sendTask = _socket.SendAsync(arraySegment, SocketFlags.None);
                    yield return new WaitUntil(() => sendTask.IsCompleted);
                    if (sendTask.IsFaulted)
                    {
                        Debug.LogError("[ServerMetrics] Failure sending metrics to QuestDb");
                        Debug.LogException(sendTask.Exception);
                    }
                    yield return CoroutineEx.waitForEndOfFrame;
                }
                yield return CoroutineEx.waitForEndOfFrame;
            }
        }

        public void Start_Client()
        {
            _isRunning = true;
            StartCoroutine(SendBufferLoop());
        }

        public void Stop_Client()
        {
            _isRunning = false;
            StopAllCoroutines();
            _socket.Close();
        }

        void OnDestroy()
        {
            Stop_Client();
        }
    }
}
