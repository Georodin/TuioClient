using System;
using System.Collections.Generic;
using TMPro;
using TuioNet.Tuio11;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.UI;

namespace BeyondFutureOne.TuioClient
{
    [Serializable]
    public sealed class TuioDebugTokenSelection
    {
        private const int AllTokensMask = (1 << Tuio11CanvasAdapter.SupportedTokenCount) - 1;

        [SerializeField] private int _mask = AllTokensMask;

        public bool IsEnabled(int tokenId)
        {
            if (tokenId < Tuio11CanvasAdapter.MinSupportedTokenId || tokenId > Tuio11CanvasAdapter.MaxSupportedTokenId)
            {
                return false;
            }

            return (_mask & (1 << (tokenId - Tuio11CanvasAdapter.MinSupportedTokenId))) != 0;
        }

        public void Validate()
        {
            _mask &= AllTokensMask;
        }
    }

    [DisallowMultipleComponent]
    public sealed class Tuio11CanvasAdapter : MonoBehaviour
    {
        public const int MinSupportedTokenId = 1;
        public const int MaxSupportedTokenId = 24;
        public const int SupportedTokenCount = MaxSupportedTokenId - MinSupportedTokenId + 1;

        [Header("TUIO")]
        [SerializeField] private BeyondTuio11SessionBehaviour _tuioSessionBehaviour;
        [SerializeField] private bool _registerOnEnable = true;
        [SerializeField] private float _recentMessageWindowSeconds = 3f;

        [Header("Canvas")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _tokenRoot;
        [SerializeField] private CanvasGroup _tokenRootCanvasGroup;
        [SerializeField] private Vector2 _tokenSize = new Vector2(86f, 86f);
        [SerializeField] private Vector2 _debugLabelSize = new Vector2(200f, 86f);
        [SerializeField] private float _debugLabelGap = 16f;
        [SerializeField] private Vector2 _tokenGridPadding = new Vector2(40f, 32f);

        [Header("Debug Tokens")]
        [SerializeField, InspectorName("Enabled Token IDs")] private TuioDebugTokenSelection _enabledTokenIds = new TuioDebugTokenSelection();
        [SerializeField] private bool _displayDebugTokens = true;
        [SerializeField] private bool _showDetectedTokens = true;
        [SerializeField] private bool _createMissingTokens = true;
        [SerializeField] private bool _manualInteractionEnabled = true;
        [SerializeField] private bool _layoutTokensOnCreate = true;
        [SerializeField] private KeyCode _toggleDebugVisibilityShortcut = KeyCode.F9;

        [Header("Runtime Debug UI")]
        [SerializeField] private bool _showRuntimeDebugPanel = true;
        [SerializeField] private GameObject _runtimeDebugPanel;
        [SerializeField] private TMP_Text _connectionStatusText;
        [SerializeField] private Button _debugVisibilityButton;
        [SerializeField] private Button _detectedVisibilityButton;

        [Header("Diagnostics")]
        [SerializeField] private bool _logObjectEvents;

        private readonly Dictionary<uint, Tuio11CanvasDebugToken> _tokensBySessionId = new Dictionary<uint, Tuio11CanvasDebugToken>();
        private readonly Dictionary<int, Tuio11CanvasDebugToken> _tokensBySymbolId = new Dictionary<int, Tuio11CanvasDebugToken>();
        private Tuio11Dispatcher _dispatcher;
        private float _lastObjectEventTime = -1f;
        private int _lastObjectSymbolId = -1;
        private uint _lastObjectSessionId;

        public BeyondTuio11SessionBehaviour TuioSessionBehaviour => _tuioSessionBehaviour;
        public Canvas Canvas => _canvas;
        public RectTransform TokenRoot => _tokenRoot;
        public bool DisplayDebugTokens => _displayDebugTokens;
        public bool ShowDetectedTokens => _showDetectedTokens;
        public bool HasRecentControllerActivity => _lastObjectEventTime >= 0f && Time.realtimeSinceStartup - _lastObjectEventTime < _recentMessageWindowSeconds;
        public int ActiveTokenCount => _tokensBySessionId.Count;
        public int LastObjectSymbolId => _lastObjectSymbolId;
        public uint LastObjectSessionId => _lastObjectSessionId;

        public string ConnectionSummary
        {
            get
            {
                if (_tuioSessionBehaviour == null)
                {
                    return "No Beyond TUIO session assigned.";
                }

                var state = HasRecentControllerActivity ? "LIVE" : _tuioSessionBehaviour.IsRunning ? "waiting" : "stopped";
                return $"{_tuioSessionBehaviour.Endpoint} ({state})";
            }
        }

        private static T FindSceneObject<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>();
#else
            return FindObjectOfType<T>();
#endif
        }

        private void Reset()
        {
            _tuioSessionBehaviour = FindSceneObject<BeyondTuio11SessionBehaviour>();
            _canvas = GetComponentInParent<Canvas>();
            _tokenRoot = transform as RectTransform;
            _tokenRootCanvasGroup = GetComponent<CanvasGroup>();
        }

        private void Awake()
        {
            EnsureDebugTokenSelection();
            ResolveReferences();
            EnsureTokenRootCanvasGroup();
            EnsureTokenPool();
            SetDebugTokensVisible(_displayDebugTokens);
            EnsureRuntimeDebugPanel();
            RefreshRuntimeDebugPanel();
        }

        private void OnEnable()
        {
            if (_debugVisibilityButton != null)
            {
                _debugVisibilityButton.onClick.AddListener(ToggleDebugTokens);
            }

            if (_detectedVisibilityButton != null)
            {
                _detectedVisibilityButton.onClick.AddListener(ToggleDetectedTokens);
            }

            if (_registerOnEnable)
            {
                RegisterDispatcher();
            }
        }

        private void OnDisable()
        {
            if (_debugVisibilityButton != null)
            {
                _debugVisibilityButton.onClick.RemoveListener(ToggleDebugTokens);
            }

            if (_detectedVisibilityButton != null)
            {
                _detectedVisibilityButton.onClick.RemoveListener(ToggleDetectedTokens);
            }

            UnregisterDispatcher();
        }

        private void Update()
        {
            if (IsDebugToggleShortcutPressed())
            {
                ToggleDebugTokens();
            }

            RefreshRuntimeDebugPanel();
        }

        private bool IsDebugToggleShortcutPressed()
        {
            if (_toggleDebugVisibilityShortcut == KeyCode.None)
            {
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && TryGetInputSystemKey(_toggleDebugVisibilityShortcut, out var key))
            {
                return Keyboard.current[key].wasPressedThisFrame;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(_toggleDebugVisibilityShortcut);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static bool TryGetInputSystemKey(KeyCode keyCode, out Key key)
        {
            switch (keyCode)
            {
                case KeyCode.F1: key = Key.F1; return true;
                case KeyCode.F2: key = Key.F2; return true;
                case KeyCode.F3: key = Key.F3; return true;
                case KeyCode.F4: key = Key.F4; return true;
                case KeyCode.F5: key = Key.F5; return true;
                case KeyCode.F6: key = Key.F6; return true;
                case KeyCode.F7: key = Key.F7; return true;
                case KeyCode.F8: key = Key.F8; return true;
                case KeyCode.F9: key = Key.F9; return true;
                case KeyCode.F10: key = Key.F10; return true;
                case KeyCode.F11: key = Key.F11; return true;
                case KeyCode.F12: key = Key.F12; return true;
                case KeyCode.Space: key = Key.Space; return true;
                case KeyCode.Return: key = Key.Enter; return true;
                case KeyCode.Escape: key = Key.Escape; return true;
                case KeyCode.Tab: key = Key.Tab; return true;
                case KeyCode.BackQuote: key = Key.Backquote; return true;
                case KeyCode.Alpha0: key = Key.Digit0; return true;
                case KeyCode.Alpha1: key = Key.Digit1; return true;
                case KeyCode.Alpha2: key = Key.Digit2; return true;
                case KeyCode.Alpha3: key = Key.Digit3; return true;
                case KeyCode.Alpha4: key = Key.Digit4; return true;
                case KeyCode.Alpha5: key = Key.Digit5; return true;
                case KeyCode.Alpha6: key = Key.Digit6; return true;
                case KeyCode.Alpha7: key = Key.Digit7; return true;
                case KeyCode.Alpha8: key = Key.Digit8; return true;
                case KeyCode.Alpha9: key = Key.Digit9; return true;
                case KeyCode.A: key = Key.A; return true;
                case KeyCode.B: key = Key.B; return true;
                case KeyCode.C: key = Key.C; return true;
                case KeyCode.D: key = Key.D; return true;
                case KeyCode.E: key = Key.E; return true;
                case KeyCode.F: key = Key.F; return true;
                case KeyCode.G: key = Key.G; return true;
                case KeyCode.H: key = Key.H; return true;
                case KeyCode.I: key = Key.I; return true;
                case KeyCode.J: key = Key.J; return true;
                case KeyCode.K: key = Key.K; return true;
                case KeyCode.L: key = Key.L; return true;
                case KeyCode.M: key = Key.M; return true;
                case KeyCode.N: key = Key.N; return true;
                case KeyCode.O: key = Key.O; return true;
                case KeyCode.P: key = Key.P; return true;
                case KeyCode.Q: key = Key.Q; return true;
                case KeyCode.R: key = Key.R; return true;
                case KeyCode.S: key = Key.S; return true;
                case KeyCode.T: key = Key.T; return true;
                case KeyCode.U: key = Key.U; return true;
                case KeyCode.V: key = Key.V; return true;
                case KeyCode.W: key = Key.W; return true;
                case KeyCode.X: key = Key.X; return true;
                case KeyCode.Y: key = Key.Y; return true;
                case KeyCode.Z: key = Key.Z; return true;
                default:
                    key = Key.None;
                    return false;
            }
        }
#endif
        private void OnValidate()
        {
            EnsureDebugTokenSelection();

            _tokenSize.x = Mathf.Max(16f, _tokenSize.x);
            _tokenSize.y = Mathf.Max(16f, _tokenSize.y);
            _debugLabelSize.x = Mathf.Max(24f, _debugLabelSize.x);
            _debugLabelSize.y = Mathf.Max(16f, _debugLabelSize.y);
            _debugLabelGap = Mathf.Max(0f, _debugLabelGap);
            _tokenGridPadding.x = Mathf.Max(0f, _tokenGridPadding.x);
            _tokenGridPadding.y = Mathf.Max(0f, _tokenGridPadding.y);
            _recentMessageWindowSeconds = Mathf.Max(0.25f, _recentMessageWindowSeconds);
        }

        public void RegisterDispatcher()
        {
            if (_dispatcher != null)
            {
                return;
            }

            ResolveReferences();

            if (_tuioSessionBehaviour == null)
            {
                Debug.LogWarning("[Beyond TUIO Client] Cannot register: no BeyondTuio11SessionBehaviour assigned.", this);
                return;
            }

            try
            {
                _dispatcher = (Tuio11Dispatcher)_tuioSessionBehaviour.TuioDispatcher;
            }
            catch (InvalidCastException exception)
            {
                Debug.LogError($"[Beyond TUIO Client] Session is not configured for TUIO 1.1. {exception.Message}", this);
                return;
            }

            _dispatcher.OnObjectAdd += HandleObjectAdd;
            _dispatcher.OnObjectUpdate += HandleObjectUpdate;
            _dispatcher.OnObjectRemove += HandleObjectRemove;
        }

        public void UnregisterDispatcher()
        {
            if (_dispatcher == null)
            {
                return;
            }

            _dispatcher.OnObjectAdd -= HandleObjectAdd;
            _dispatcher.OnObjectUpdate -= HandleObjectUpdate;
            _dispatcher.OnObjectRemove -= HandleObjectRemove;
            _dispatcher = null;
        }

        public void SetDebugTokensVisible(bool visible)
        {
            _displayDebugTokens = visible;
            EnsureTokenRootCanvasGroup();
            ApplyDebugCanvasGroup();
            EnsureTokenPool();
            ApplyTokenPoolVisibility();
            ApplyRuntimeDebugPanelVisibility();

            RefreshRuntimeDebugPanel();
        }


        public void SetDetectedTokensVisible(bool visible)
        {
            _showDetectedTokens = visible;
            EnsureTokenRootCanvasGroup();
            ApplyDebugCanvasGroup();
            EnsureTokenPool();

            foreach (var token in _tokensBySymbolId.Values)
            {
                token.SetDetectedVisible(visible);
            }

            RefreshRuntimeDebugPanel();
        }
        public void ToggleDebugTokens()
        {
            SetDebugTokensVisible(!_displayDebugTokens);
        }

        public void ToggleDetectedTokens()
        {
            SetDetectedTokensVisible(!_showDetectedTokens);
        }
        private void EnsureTokenRootCanvasGroup()
        {
            ResolveReferences();

            if (_tokenRoot == null)
            {
                return;
            }

            if (_tokenRootCanvasGroup == null)
            {
                _tokenRootCanvasGroup = _tokenRoot.GetComponent<CanvasGroup>();
            }

            if (_tokenRootCanvasGroup == null)
            {
                _tokenRootCanvasGroup = _tokenRoot.gameObject.AddComponent<CanvasGroup>();
            }

            ApplyDebugCanvasGroup();
        }

        private void ApplyDebugCanvasGroup()
        {
            if (_tokenRootCanvasGroup == null)
            {
                return;
            }

            // F9 / debug pool toggle is the master switch for all debug token visuals, including live detections.
            _tokenRootCanvasGroup.alpha = _displayDebugTokens ? 1f : 0f;
            _tokenRootCanvasGroup.blocksRaycasts = _displayDebugTokens && _manualInteractionEnabled;
            _tokenRootCanvasGroup.interactable = _displayDebugTokens && _manualInteractionEnabled;
        }

        public void SetTokenManualActive(int tokenId, bool active)
        {
            EnsureTokenPool();
            if (_tokensBySymbolId.TryGetValue(tokenId, out var token))
            {
                token.SetManualActive(active);
            }
        }

        public void SetTokenPose(int tokenId, Vector2 anchoredPosition, float zRotationDegrees)
        {
            EnsureTokenPool();
            if (_tokensBySymbolId.TryGetValue(tokenId, out var token))
            {
                token.ApplyTuioPose(anchoredPosition, zRotationDegrees);
            }
        }

        public bool TryGetDebugTokenPose(int tokenId, out Vector2 anchoredPosition, out float zRotationDegrees, out bool placed)
        {
            EnsureTokenPool();
            if (_tokensBySymbolId.TryGetValue(tokenId, out var token))
            {
                anchoredPosition = token.RectTransform.anchoredPosition;
                zRotationDegrees = token.RectTransform.localEulerAngles.z;
                placed = token.IsManuallyActive;
                return true;
            }

            anchoredPosition = Vector2.zero;
            zRotationDegrees = 0f;
            placed = false;
            return false;
        }

        public void EnsureTokenPool()
        {
            EnsureDebugTokenSelection();
            ResolveReferences();
            IndexExistingTokens();

            if (!_createMissingTokens || _tokenRoot == null)
            {
                return;
            }

            for (var id = MinSupportedTokenId; id <= MaxSupportedTokenId; id++)
            {
                if (!IsDebugTokenEnabled(id) || _tokensBySymbolId.ContainsKey(id))
                {
                    continue;
                }

                var token = CreateToken(id);
                _tokensBySymbolId[id] = token;
            }

            ApplyTokenPoolVisibility();
        }

        private void ResolveReferences()
        {
            if (_tuioSessionBehaviour == null)
            {
                _tuioSessionBehaviour = FindSceneObject<BeyondTuio11SessionBehaviour>();
            }

            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
            }

            if (_tokenRoot == null)
            {
                _tokenRoot = transform as RectTransform;
            }

            if (_tokenRootCanvasGroup == null && _tokenRoot != null)
            {
                _tokenRootCanvasGroup = _tokenRoot.GetComponent<CanvasGroup>();
            }
        }

        private void IndexExistingTokens()
        {
            _tokensBySymbolId.Clear();

            if (_tokenRoot == null)
            {
                return;
            }

            var tokens = _tokenRoot.GetComponentsInChildren<Tuio11CanvasDebugToken>(true);
            foreach (var token in tokens)
            {
                token.SetLabelLayout(_debugLabelSize, _debugLabelGap);
                token.SetManualInteractionEnabled(_manualInteractionEnabled);
                token.SetDebugVisible(_displayDebugTokens && IsDebugTokenEnabled(token.TokenId));
                token.SetDetectedVisible(_showDetectedTokens);

                if (!_tokensBySymbolId.ContainsKey(token.TokenId))
                {
                    _tokensBySymbolId.Add(token.TokenId, token);
                }
            }
        }

        private Tuio11CanvasDebugToken CreateToken(int tokenId, bool? layoutOnCreate = null)
        {
            var tokenObject = new GameObject($"TUIO 1.1 Token {tokenId:00}", typeof(RectTransform), typeof(Image), typeof(Tuio11CanvasDebugToken));
            tokenObject.transform.SetParent(_tokenRoot, false);

            var rectTransform = (RectTransform)tokenObject.transform;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = _tokenSize;

            if (layoutOnCreate ?? _layoutTokensOnCreate)
            {
                rectTransform.anchoredPosition = GetDefaultTokenPosition(tokenId);
            }

            var token = tokenObject.GetComponent<Tuio11CanvasDebugToken>();
            token.Configure(tokenId, _manualInteractionEnabled, _displayDebugTokens && IsDebugTokenEnabled(tokenId), _debugLabelSize, _debugLabelGap);
            token.SetDetectedVisible(_showDetectedTokens);
            token.SetManualActive(false);
            return token;
        }

        private Vector2 GetDefaultTokenPosition(int tokenId)
        {
            var zeroBasedIndex = tokenId - MinSupportedTokenId;
            var columns = Mathf.CeilToInt(Mathf.Sqrt(SupportedTokenCount));
            var column = zeroBasedIndex % columns;
            var row = zeroBasedIndex / columns;
            var tokenWithLabelSize = new Vector2(_tokenSize.x + _debugLabelGap + _debugLabelSize.x, Mathf.Max(_tokenSize.y, _debugLabelSize.y));
            var spacing = tokenWithLabelSize + _tokenGridPadding;
            var totalRows = Mathf.CeilToInt(SupportedTokenCount / (float)columns);
            var origin = new Vector2(-(columns - 1) * spacing.x * 0.5f, (totalRows - 1) * spacing.y * 0.5f);
            var tokenCenterOffset = new Vector2(-(_debugLabelGap + _debugLabelSize.x) * 0.5f, 0f);

            return origin + new Vector2(column * spacing.x, -row * spacing.y) + tokenCenterOffset;
        }

        private static bool IsSupportedSymbolId(uint symbolId)
        {
            return symbolId >= MinSupportedTokenId && symbolId <= MaxSupportedTokenId && symbolId <= int.MaxValue;
        }

        private bool IsDebugTokenEnabled(int tokenId)
        {
            return _enabledTokenIds != null && _enabledTokenIds.IsEnabled(tokenId);
        }

        private void EnsureDebugTokenSelection()
        {
            if (_enabledTokenIds == null)
            {
                _enabledTokenIds = new TuioDebugTokenSelection();
            }

            _enabledTokenIds.Validate();
        }

        private void ApplyTokenPoolVisibility()
        {
            foreach (var pair in _tokensBySymbolId)
            {
                pair.Value.SetDebugVisible(_displayDebugTokens && IsDebugTokenEnabled(pair.Key));
            }
        }

        private void ApplyRuntimeDebugPanelVisibility()
        {
            if (_runtimeDebugPanel != null)
            {
                _runtimeDebugPanel.SetActive(_showRuntimeDebugPanel && _displayDebugTokens);
            }
        }

        private bool TryGetOrCreateDetectedToken(uint symbolId, out Tuio11CanvasDebugToken token)
        {
            token = null;
            if (!IsSupportedSymbolId(symbolId))
            {
                return false;
            }

            var tokenId = (int)symbolId;
            if (_tokensBySymbolId.TryGetValue(tokenId, out token))
            {
                return true;
            }

            if (!_createMissingTokens || _tokenRoot == null)
            {
                return false;
            }

            token = CreateDetectedToken(tokenId);
            _tokensBySymbolId[tokenId] = token;
            return true;
        }

        private void HandleObjectAdd(object sender, Tuio11Object tuioObject)
        {
            MarkObjectEvent(tuioObject);

            if (!TryGetOrCreateDetectedToken(tuioObject.SymbolId, out var token))
            {
                return;
            }

            _tokensBySessionId[tuioObject.SessionId] = token;
            token.SetTuioActive(true);
            ApplyDebugCanvasGroup();
            ApplyTuioObject(token, tuioObject);

            if (_logObjectEvents)
            {
                Debug.Log($"[Beyond TUIO Client] Added token {tuioObject.SymbolId} session {tuioObject.SessionId}.", this);
            }
        }

        private void HandleObjectUpdate(object sender, Tuio11Object tuioObject)
        {
            MarkObjectEvent(tuioObject);

            if (!_tokensBySessionId.TryGetValue(tuioObject.SessionId, out var token) && !TryGetOrCreateDetectedToken(tuioObject.SymbolId, out token))
            {
                return;
            }

            token.SetTuioActive(true);
            ApplyDebugCanvasGroup();
            ApplyTuioObject(token, tuioObject);
        }

        private void HandleObjectRemove(object sender, Tuio11Object tuioObject)
        {
            MarkObjectEvent(tuioObject);

            if (_tokensBySessionId.Remove(tuioObject.SessionId, out var token))
            {
                token.SetTuioActive(false);
                ApplyDebugCanvasGroup();
            }

            if (_logObjectEvents)
            {
                Debug.Log($"[Beyond TUIO Client] Removed token {tuioObject.SymbolId} session {tuioObject.SessionId}.", this);
            }
        }

        private void MarkObjectEvent(Tuio11Object tuioObject)
        {
            _lastObjectEventTime = Time.realtimeSinceStartup;
            _lastObjectSymbolId = (int)tuioObject.SymbolId;
            _lastObjectSessionId = tuioObject.SessionId;

            if (_tuioSessionBehaviour != null)
            {
                _tuioSessionBehaviour.MarkMessageReceived();
            }
        }

        private void ApplyTuioObject(Tuio11CanvasDebugToken token, Tuio11Object tuioObject)
        {
            token.ApplyTuioPose(ToAnchoredPosition(token.RectTransform, tuioObject), -Mathf.Rad2Deg * tuioObject.Angle);
        }

        private Vector2 ToAnchoredPosition(RectTransform tokenTransform, Tuio11Object tuioObject)
        {
            var root = _tokenRoot != null ? _tokenRoot : transform as RectTransform;
            if (root == null || tokenTransform == null)
            {
                return Vector2.zero;
            }

            var normalizedPosition = new Vector2(
                Mathf.Clamp01((float)tuioObject.Position.X),
                Mathf.Clamp01((float)tuioObject.Position.Y));

            return NormalizedToAnchoredPosition(root, tokenTransform, normalizedPosition);
        }

        private static Vector2 NormalizedToAnchoredPosition(RectTransform parentTransform, RectTransform childTransform, Vector2 normalizedPosition)
        {
            var rect = parentTransform.rect;
            var parentLocalPoint = new Vector2(
                rect.xMin + normalizedPosition.x * rect.width,
                rect.yMin + (1f - normalizedPosition.y) * rect.height);

            return parentLocalPoint - GetAnchorReferencePoint(parentTransform, childTransform);
        }

        private static Vector2 GetAnchorReferencePoint(RectTransform parentTransform, RectTransform childTransform)
        {
            var rect = parentTransform.rect;
            var anchor = new Vector2(
                Mathf.Lerp(childTransform.anchorMin.x, childTransform.anchorMax.x, childTransform.pivot.x),
                Mathf.Lerp(childTransform.anchorMin.y, childTransform.anchorMax.y, childTransform.pivot.y));

            return new Vector2(
                rect.xMin + anchor.x * rect.width,
                rect.yMin + anchor.y * rect.height);
        }

        private Tuio11CanvasDebugToken CreateDetectedToken(int tokenId)
        {
            var token = CreateToken(tokenId, layoutOnCreate: false);
            token.SetDebugVisible(false);
            token.SetDetectedVisible(_showDetectedTokens);
            return token;
        }

        private void EnsureRuntimeDebugPanel()
        {
            if (!_showRuntimeDebugPanel || _canvas == null)
            {
                return;
            }

            ResolveRuntimeDebugPanelReference();

            if (_runtimeDebugPanel != null && _connectionStatusText != null && _debugVisibilityButton != null && _detectedVisibilityButton != null)
            {
                ApplyRuntimeDebugPanelVisibility();
                return;
            }

            var panelObject = new GameObject("Beyond TUIO Debug Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(_canvas.transform, false);

            var panelTransform = (RectTransform)panelObject.transform;
            panelTransform.anchorMin = new Vector2(0f, 1f);
            panelTransform.anchorMax = new Vector2(0f, 1f);
            panelTransform.pivot = new Vector2(0f, 1f);
            panelTransform.anchoredPosition = new Vector2(18f, -18f);
            panelTransform.sizeDelta = new Vector2(460f, 126f);

            var panelImage = panelObject.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.58f);
            panelImage.raycastTarget = true;

            var statusObject = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
            statusObject.transform.SetParent(panelObject.transform, false);
            var statusTransform = (RectTransform)statusObject.transform;
            statusTransform.anchorMin = new Vector2(0f, 0f);
            statusTransform.anchorMax = new Vector2(1f, 1f);
            statusTransform.offsetMin = new Vector2(12f, 10f);
            statusTransform.offsetMax = new Vector2(-132f, -10f);

            _connectionStatusText = statusObject.GetComponent<TextMeshProUGUI>();
            _connectionStatusText.alignment = TextAlignmentOptions.Left;
            _connectionStatusText.fontSize = 18f;
            _connectionStatusText.color = Color.white;
            _connectionStatusText.raycastTarget = false;

            var buttonObject = new GameObject("Toggle Debug", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(panelObject.transform, false);
            var buttonTransform = (RectTransform)buttonObject.transform;
            buttonTransform.anchorMin = new Vector2(1f, 0.5f);
            buttonTransform.anchorMax = new Vector2(1f, 0.5f);
            buttonTransform.pivot = new Vector2(1f, 0.5f);
            buttonTransform.anchoredPosition = new Vector2(-12f, 24f);
            buttonTransform.sizeDelta = new Vector2(108f, 38f);

            buttonObject.GetComponent<Image>().color = new Color(0.12f, 0.24f, 0.34f, 0.92f);
            _debugVisibilityButton = buttonObject.GetComponent<Button>();

            var buttonLabelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            buttonLabelObject.transform.SetParent(buttonObject.transform, false);
            var buttonLabelTransform = (RectTransform)buttonLabelObject.transform;
            buttonLabelTransform.anchorMin = Vector2.zero;
            buttonLabelTransform.anchorMax = Vector2.one;
            buttonLabelTransform.offsetMin = Vector2.zero;
            buttonLabelTransform.offsetMax = Vector2.zero;

            var buttonLabel = buttonLabelObject.GetComponent<TextMeshProUGUI>();
            buttonLabel.alignment = TextAlignmentOptions.Center;
            buttonLabel.fontSize = 16f;
            buttonLabel.color = Color.white;
            buttonLabel.raycastTarget = false;
            var detectedButtonObject = new GameObject("Toggle Detected", typeof(RectTransform), typeof(Image), typeof(Button));
            detectedButtonObject.transform.SetParent(panelObject.transform, false);
            var detectedButtonTransform = (RectTransform)detectedButtonObject.transform;
            detectedButtonTransform.anchorMin = new Vector2(1f, 0.5f);
            detectedButtonTransform.anchorMax = new Vector2(1f, 0.5f);
            detectedButtonTransform.pivot = new Vector2(1f, 0.5f);
            detectedButtonTransform.anchoredPosition = new Vector2(-12f, -24f);
            detectedButtonTransform.sizeDelta = new Vector2(108f, 38f);

            detectedButtonObject.GetComponent<Image>().color = new Color(0.12f, 0.24f, 0.34f, 0.92f);
            _detectedVisibilityButton = detectedButtonObject.GetComponent<Button>();

            var detectedButtonLabelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            detectedButtonLabelObject.transform.SetParent(detectedButtonObject.transform, false);
            var detectedButtonLabelTransform = (RectTransform)detectedButtonLabelObject.transform;
            detectedButtonLabelTransform.anchorMin = Vector2.zero;
            detectedButtonLabelTransform.anchorMax = Vector2.one;
            detectedButtonLabelTransform.offsetMin = Vector2.zero;
            detectedButtonLabelTransform.offsetMax = Vector2.zero;

            var detectedButtonLabel = detectedButtonLabelObject.GetComponent<TextMeshProUGUI>();
            detectedButtonLabel.alignment = TextAlignmentOptions.Center;
            detectedButtonLabel.fontSize = 16f;
            detectedButtonLabel.color = Color.white;
            detectedButtonLabel.raycastTarget = false;

            _runtimeDebugPanel = panelObject;
            ApplyRuntimeDebugPanelVisibility();
        }

        private void ResolveRuntimeDebugPanelReference()
        {
            if (_runtimeDebugPanel != null || _canvas == null)
            {
                return;
            }

            var existingPanel = _canvas.transform.Find("Beyond TUIO Debug Panel");
            if (existingPanel != null)
            {
                _runtimeDebugPanel = existingPanel.gameObject;
            }
        }

        private void RefreshRuntimeDebugPanel()
        {
            if (_connectionStatusText == null)
            {
                return;
            }

            var state = HasRecentControllerActivity ? "LIVE TUIO" : "waiting";
            var endpoint = _tuioSessionBehaviour != null ? _tuioSessionBehaviour.Endpoint : "no session";
            var error = _tuioSessionBehaviour != null && _tuioSessionBehaviour.LastException != null ? $"\n{_tuioSessionBehaviour.LastException.GetType().Name}" : string.Empty;
            var lastToken = _lastObjectSymbolId > 0 ? $"token {_lastObjectSymbolId} session {_lastObjectSessionId}" : "none";
            _connectionStatusText.text = $"Beyond TUIO 1.1: {state}\n{endpoint}\nPool {(_displayDebugTokens ? "visible" : "hidden")} ({_toggleDebugVisibilityShortcut}) | Live {(_showDetectedTokens ? "visible" : "hidden")} | last {lastToken}{error}";

            if (_debugVisibilityButton != null)
            {
                var label = _debugVisibilityButton.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = _displayDebugTokens ? "Pool On" : "Pool Off";
                }
            }
            if (_detectedVisibilityButton != null)
            {
                var label = _detectedVisibilityButton.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = _showDetectedTokens ? "Live On" : "Live Off";
                }
            }
        }
    }
}














