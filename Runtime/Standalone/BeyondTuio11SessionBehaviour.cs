using System;
using TuioNet.Common;
using UnityEngine;

namespace BeyondFutureOne.TuioClient
{
    [DisallowMultipleComponent]
    public sealed class BeyondTuio11SessionBehaviour : MonoBehaviour
    {
        [field: SerializeField] public TuioVersion TuioVersion { get; private set; } = TuioNet.Common.TuioVersion.Tuio11;
        [field: SerializeField] public TuioConnectionType ConnectionType { get; set; } = TuioConnectionType.UDP;
        [SerializeField] private string _ipAddress = "10.0.0.20";
        [field: SerializeField] public int UdpPort { get; set; } = 3333;
        [SerializeField] private bool _startOnAwake = true;
        [SerializeField] private bool _processMessagesInUpdate = true;

        private TuioSession _session;
        private BeyondTuioUnityLogger _logger;
        private Exception _lastException;
        private float _lastMessageRealtime = -1f;

        public ITuioDispatcher TuioDispatcher
        {
            get
            {
                EnsureSession();
                return _session.TuioDispatcher;
            }
        }

        public string IpAddress => _ipAddress;
        public int Port => UdpPort;
        public bool IsRunning => _session != null;
        public Exception LastException => _lastException;
        public float LastMessageRealtime => _lastMessageRealtime;
        public string Endpoint => $"{ConnectionType} {_ipAddress}:{UdpPort}";
        public bool HasRecentMessages(float activeWindowSeconds) => _lastMessageRealtime >= 0f && Time.realtimeSinceStartup - _lastMessageRealtime <= activeWindowSeconds;

        private void Awake()
        {
            TuioVersion = TuioNet.Common.TuioVersion.Tuio11;

            if (_startOnAwake)
            {
                EnsureSession();
            }
        }

        private void OnValidate()
        {
            TuioVersion = TuioNet.Common.TuioVersion.Tuio11;
            UdpPort = Mathf.Clamp(UdpPort, 1, 65535);
        }

        private void Update()
        {
            if (!_processMessagesInUpdate || _session == null)
            {
                return;
            }

            try
            {
                _session.ProcessMessages();
            }
            catch (Exception exception)
            {
                _lastException = exception;
            }
        }

        public void Restart()
        {
            DisposeSession();
            EnsureSession();
        }

        public void MarkMessageReceived()
        {
            _lastMessageRealtime = Time.realtimeSinceStartup;
            _lastException = null;
        }

        private void EnsureSession()
        {
            if (_session != null)
            {
                return;
            }

            _logger = new BeyondTuioUnityLogger();
            _session = new TuioSession(_logger, TuioNet.Common.TuioVersion.Tuio11, ConnectionType, _ipAddress, UdpPort, false);
            _lastException = null;
        }

        private void OnApplicationQuit()
        {
            DisposeSession();
        }

        private void OnDestroy()
        {
            DisposeSession();
        }

        private void DisposeSession()
        {
            if (_session == null)
            {
                return;
            }

            _session.Dispose();
            _session = null;
        }
    }
}