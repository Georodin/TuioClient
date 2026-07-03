using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeyondFutureOne.TuioClient
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Image))]
    public sealed class Tuio11CanvasDebugToken : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerClickHandler, IScrollHandler
    {
        [SerializeField] private int _tokenId = 1;
        [SerializeField] private bool _manualInteractionEnabled = true;
        [SerializeField] private float _scrollRotationDegrees = 5f;
        [SerializeField] private Color _inactiveColor = new Color(0.18f, 0.28f, 0.36f, 0.34f);
        [SerializeField] private Color _manualActiveColor = new Color(0.95f, 0.65f, 0.22f, 0.78f);
        [SerializeField] private Color _tuioActiveColor = new Color(0.1f, 0.72f, 0.42f, 0.92f);
        [SerializeField] private TMP_Text _label;

        private RectTransform _rectTransform;
        private Image _image;
        private bool _isTuioActive;
        private bool _isManuallyActive;
        private bool _debugVisible = true;
        private bool _detectedVisible = true;
        private Vector2 _dragOffset;

        public int TokenId
        {
            get => _tokenId;
            set
            {
                _tokenId = Mathf.Clamp(value, 1, 20);
                RefreshVisual();
            }
        }

        public bool IsTuioActive => _isTuioActive;
        public bool IsManuallyActive => _isManuallyActive;
        public RectTransform RectTransform => _rectTransform != null ? _rectTransform : GetComponent<RectTransform>();

        private void Awake()
        {
            CacheReferences();
            RefreshVisual();
        }

        private void Reset()
        {
            CacheReferences();
            EnsureLabel();
            SetManualActive(false);
        }

        public void Configure(int tokenId, bool manualInteractionEnabled, bool debugVisible)
        {
            _tokenId = Mathf.Clamp(tokenId, 1, 20);
            _manualInteractionEnabled = manualInteractionEnabled;
            _debugVisible = debugVisible;
            _isManuallyActive = false;
            CacheReferences();
            EnsureLabel();
            RefreshVisual();
        }

        public void SetDebugVisible(bool visible)
        {
            _debugVisible = visible;
            RefreshVisual();
        }
        public void SetDetectedVisible(bool visible)
        {
            _detectedVisible = visible;
            RefreshVisual();
        }

        public void SetManualInteractionEnabled(bool enabled)
        {
            _manualInteractionEnabled = enabled;
            RefreshVisual();
        }

        public void SetManualActive(bool active)
        {
            _isManuallyActive = active;
            RefreshVisual();
        }

        public void SetTuioActive(bool active)
        {
            _isTuioActive = active;
            RefreshVisual();
        }

        public void ApplyTuioPose(Vector2 anchoredPosition, float zRotationDegrees)
        {
            CacheReferences();
            _rectTransform.anchoredPosition = anchoredPosition;
            _rectTransform.localRotation = Quaternion.Euler(0f, 0f, zRotationDegrees);
            RefreshVisual();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!CanUseManualInput())
            {
                return;
            }

            CacheReferences();
            if (!(_rectTransform.parent is RectTransform parentTransform))
            {
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentTransform,
                eventData.position,
                eventData.pressEventCamera,
                out var localPointerPosition);

            _dragOffset = GetParentLocalPoint(parentTransform, _rectTransform) - localPointerPosition;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!CanUseManualInput())
            {
                return;
            }

            if (!(_rectTransform.parent is RectTransform parentTransform))
            {
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentTransform,
                eventData.position,
                eventData.pressEventCamera,
                out var localPointerPosition);

            _rectTransform.anchoredPosition = ParentLocalToAnchoredPosition(parentTransform, _rectTransform, localPointerPosition + _dragOffset);
            RefreshVisual();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!CanUseManualInput())
            {
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                SetManualActive(!_isManuallyActive);
            }
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (!CanUseManualInput())
            {
                return;
            }

            CacheReferences();
            var delta = eventData.scrollDelta.y * _scrollRotationDegrees;
            _rectTransform.localRotation *= Quaternion.Euler(0f, 0f, delta);
            RefreshVisual();
        }

        private bool CanUseManualInput()
        {
            return _debugVisible && _manualInteractionEnabled && !_isTuioActive;
        }

        private void CacheReferences()
        {
            _rectTransform = GetComponent<RectTransform>();
            _image = GetComponent<Image>();
        }

        private void EnsureLabel()
        {
            if (_label != null)
            {
                return;
            }

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(transform, false);
            var labelTransform = (RectTransform)labelObject.transform;
            labelTransform.anchorMin = Vector2.zero;
            labelTransform.anchorMax = Vector2.one;
            labelTransform.offsetMin = Vector2.zero;
            labelTransform.offsetMax = Vector2.zero;

            _label = labelObject.GetComponent<TextMeshProUGUI>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 13f;
            _label.enableAutoSizing = true;
            _label.fontSizeMin = 5f;
            _label.fontSizeMax = 16f;
            _label.color = Color.white;
            _label.raycastTarget = false;
        }


        private static Vector2 ParentLocalToAnchoredPosition(RectTransform parentTransform, RectTransform childTransform, Vector2 parentLocalPoint)
        {
            return parentLocalPoint - GetAnchorReferencePoint(parentTransform, childTransform);
        }

        private static Vector2 GetParentLocalPoint(RectTransform parentTransform, RectTransform childTransform)
        {
            return GetAnchorReferencePoint(parentTransform, childTransform) + childTransform.anchoredPosition;
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

        private Vector2 GetNormalizedPosition()
        {
            CacheReferences();

            if (!(_rectTransform.parent is RectTransform parentTransform))
            {
                return Vector2.zero;
            }

            var rect = parentTransform.rect;
            if (Mathf.Approximately(rect.width, 0f) || Mathf.Approximately(rect.height, 0f))
            {
                return Vector2.zero;
            }

            var parentLocalPoint = GetParentLocalPoint(parentTransform, _rectTransform);
            var x = (parentLocalPoint.x - rect.xMin) / rect.width;
            var y = 1f - ((parentLocalPoint.y - rect.yMin) / rect.height);
            return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
        }
        private void RefreshVisual()
        {
            CacheReferences();
            EnsureLabel();

            var shouldRender = _debugVisible || (_detectedVisible && _isTuioActive);

            if (_image != null)
            {
                _image.enabled = shouldRender;
                _image.raycastTarget = _debugVisible && _manualInteractionEnabled && !_isTuioActive;
                _image.color = _isTuioActive ? _tuioActiveColor : _isManuallyActive ? _manualActiveColor : _inactiveColor;
            }

            if (_label != null)
            {
                _label.enabled = shouldRender;
                var normalizedPosition = GetNormalizedPosition();
                var rotationDegrees = Mathf.Repeat(_rectTransform.localEulerAngles.z, 360f);
                _label.text = $"Symbol_ID: {_tokenId}\nposition: {normalizedPosition.x:0.000},{normalizedPosition.y:0.000}\nrotation: {rotationDegrees:0.0} deg\nplaced: {_isManuallyActive.ToString().ToLowerInvariant()}";
            }
        }
    }
}





