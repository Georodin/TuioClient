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
        private bool _isDisposing;
        private bool _shutdownHandlersRegistered;

        public ITuioDispatcher TuioDispatcher
        {
            get
            {
                if (_session == null && CanCreateSession)
                {
                    EnsureSession();
                }

                return _session != null ? _session.TuioDispatcher : null;
            }
        }

        public string IpAddress => _ipAddress;
        public int Port => UdpPort;
        public bool IsRunning => _session != null;
        public Exception LastException => _lastException;
        public float LastMessageRealtime => _lastMessageRealtime;
        public string Endpoint => $"{ConnectionType} {_ipAddress}:{UdpPort}";
        public bool HasRecentMessages(float activeWindowSeconds) => _lastMessageRealtime >= 0f && Time.realtimeSinceStartup - _lastMessageRealtime <= activeWindowSeconds;

        private bool CanCreateSession => !_isDisposing && Application.isPlaying && isActiveAndEnabled;

        private void Awake()
        {
            TuioVersion = TuioNet.Common.TuioVersion.Tuio11;
        }

        private void OnEnable()
        {
            RegisterShutdownHandlers();
            _isDisposing = false;

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
            Stop();
            _isDisposing = false;
            EnsureSession();
        }

        public void Stop()
        {
            DisposeSession();
        }

        public void MarkMessageReceived()
        {
            _lastMessageRealtime = Time.realtimeSinceStartup;
            _lastException = null;
        }

        private void EnsureSession()
        {
            if (_session != null || !CanCreateSession)
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

        private void OnDisable()
        {
            UnregisterShutdownHandlers();
            DisposeSession();
        }

        private void OnDestroy()
        {
            UnregisterShutdownHandlers();
            DisposeSession();
        }

        private void RegisterShutdownHandlers()
        {
            if (_shutdownHandlersRegistered)
            {
                return;
            }

            Application.quitting += DisposeSession;
            _shutdownHandlersRegistered = true;
        }

        private void UnregisterShutdownHandlers()
        {
            if (!_shutdownHandlersRegistered)
            {
                return;
            }

            Application.quitting -= DisposeSession;
            _shutdownHandlersRegistered = false;
        }

        private void DisposeSession()
        {
            _isDisposing = true;

            var session = _session;
            var port = UdpPort;
            var connectionType = ConnectionType;
            _session = null;

            if (session == null)
            {
                return;
            }

            try
            {
                session.Dispose();
            }
            catch (Exception exception)
            {
                _lastException = exception;
            }

            if (connectionType == TuioConnectionType.UDP)
            {
                TuioUdpPortReleaser.Release(port);
            }
        }
    }
}
