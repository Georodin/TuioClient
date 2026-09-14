using System;
using System.Collections.Generic;
using TuioNet.Tuio11;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BeyondFutureOne.TuioClient
{
    /// <summary>
    /// When activatable, maps TUIO 1.1 cursors to uGUI pointer events (touch, multitouch, drag).
    /// Runs alongside StandaloneInputModule / Input System UI so mouse input still works.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Tuio11UiTouchSimulator : MonoBehaviour
    {
        private const int PointerIdBase = 1000;

        [SerializeField] private bool _activatable;
        [SerializeField] private BeyondTuio11SessionBehaviour _tuioSessionBehaviour;
        [SerializeField] private EventSystem _eventSystem;
        [SerializeField] private bool _registerOnEnable = true;

        private readonly Dictionary<uint, CursorPointerState> _pointersBySessionId = new Dictionary<uint, CursorPointerState>();
        private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>(16);
        private readonly List<uint> _releaseBuffer = new List<uint>(8);
        private Tuio11Dispatcher _dispatcher;
        private int _nextPointerId = PointerIdBase;

        public bool Activatable
        {
            get => _activatable;
            set
            {
                if (_activatable == value)
                {
                    return;
                }

                _activatable = value;
                if (!_activatable)
                {
                    ReleaseAllPointers();
                }
            }
        }

        public BeyondTuio11SessionBehaviour TuioSessionBehaviour
        {
            get => _tuioSessionBehaviour;
            set => _tuioSessionBehaviour = value;
        }

        private void Reset()
        {
            _tuioSessionBehaviour = FindSceneObject<BeyondTuio11SessionBehaviour>();
            _eventSystem = EventSystem.current != null ? EventSystem.current : FindSceneObject<EventSystem>();
        }

        private void OnEnable()
        {
            if (_registerOnEnable)
            {
                RegisterDispatcher();
            }
        }

        private void OnDisable()
        {
            ReleaseAllPointers();
            UnregisterDispatcher();
        }

        private void Update()
        {
            if (!_activatable)
            {
                if (_pointersBySessionId.Count > 0)
                {
                    ReleaseAllPointers();
                }

                return;
            }

            var eventSystem = ResolveEventSystem();
            if (eventSystem == null)
            {
                return;
            }

            foreach (var pair in _pointersBySessionId)
            {
                ProcessPointer(eventSystem, pair.Value);
            }

            for (var i = 0; i < _releaseBuffer.Count; i++)
            {
                if (_pointersBySessionId.TryGetValue(_releaseBuffer[i], out var state))
                {
                    ProcessRelease(eventSystem, state);
                    _pointersBySessionId.Remove(_releaseBuffer[i]);
                }
            }

            _releaseBuffer.Clear();
        }

        public void RegisterDispatcher()
        {
            if (_dispatcher != null)
            {
                return;
            }

            if (_tuioSessionBehaviour == null)
            {
                _tuioSessionBehaviour = FindSceneObject<BeyondTuio11SessionBehaviour>();
            }

            if (_tuioSessionBehaviour == null)
            {
                Debug.LogWarning("[Beyond TUIO Client] Tuio11UiTouchSimulator has no session assigned.", this);
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

            _dispatcher.OnCursorAdd += HandleCursorAdd;
            _dispatcher.OnCursorUpdate += HandleCursorUpdate;
            _dispatcher.OnCursorRemove += HandleCursorRemove;
        }

        public void UnregisterDispatcher()
        {
            if (_dispatcher == null)
            {
                return;
            }

            _dispatcher.OnCursorAdd -= HandleCursorAdd;
            _dispatcher.OnCursorUpdate -= HandleCursorUpdate;
            _dispatcher.OnCursorRemove -= HandleCursorRemove;
            _dispatcher = null;
        }

        private void HandleCursorAdd(object sender, Tuio11Cursor cursor)
        {
            if (!_activatable || cursor == null)
            {
                return;
            }

            var eventSystem = ResolveEventSystem();
            if (eventSystem == null)
            {
                return;
            }

            if (_pointersBySessionId.ContainsKey(cursor.SessionId))
            {
                HandleCursorUpdate(sender, cursor);
                return;
            }

            var pointerId = _nextPointerId++;
            var state = new CursorPointerState
            {
                SessionId = cursor.SessionId,
                PointerId = pointerId,
                ScreenPosition = ToScreenPosition(cursor),
                PressedThisFrame = true,
                EventData = new PointerEventData(eventSystem)
                {
                    pointerId = pointerId,
                    button = PointerEventData.InputButton.Left
                }
            };

            _pointersBySessionId[cursor.SessionId] = state;
        }

        private void HandleCursorUpdate(object sender, Tuio11Cursor cursor)
        {
            if (!_activatable || cursor == null)
            {
                return;
            }

            if (!_pointersBySessionId.TryGetValue(cursor.SessionId, out var state))
            {
                HandleCursorAdd(sender, cursor);
                return;
            }

            state.ScreenPosition = ToScreenPosition(cursor);
        }

        private void HandleCursorRemove(object sender, Tuio11Cursor cursor)
        {
            if (cursor == null)
            {
                return;
            }

            if (_pointersBySessionId.ContainsKey(cursor.SessionId) && !_releaseBuffer.Contains(cursor.SessionId))
            {
                _releaseBuffer.Add(cursor.SessionId);
            }
        }

        private void ProcessPointer(EventSystem eventSystem, CursorPointerState state)
        {
            var data = state.EventData;
            var previousPosition = data.position;
            data.Reset();
            data.pointerId = state.PointerId;
            data.button = PointerEventData.InputButton.Left;
            data.position = state.ScreenPosition;
            data.delta = state.PressedThisFrame ? Vector2.zero : state.ScreenPosition - previousPosition;

            eventSystem.RaycastAll(data, _raycastResults);
            var raycast = FindFirstRaycast(_raycastResults);
            data.pointerCurrentRaycast = raycast;
            _raycastResults.Clear();

            HandlePointerExitAndEnter(data, raycast.gameObject);

            if (state.PressedThisFrame)
            {
                ProcessPress(data);
                state.PressedThisFrame = false;
            }

            ProcessMove(data);
            ProcessDrag(data);
        }

        private void ProcessRelease(EventSystem eventSystem, CursorPointerState state)
        {
            var data = state.EventData;
            data.pointerId = state.PointerId;
            data.button = PointerEventData.InputButton.Left;
            data.position = state.ScreenPosition;

            eventSystem.RaycastAll(data, _raycastResults);
            data.pointerCurrentRaycast = FindFirstRaycast(_raycastResults);
            _raycastResults.Clear();

            ProcessReleaseInternal(data);
            HandlePointerExitAndEnter(data, null);
        }

        private void ReleaseAllPointers()
        {
            var eventSystem = ResolveEventSystem();
            if (eventSystem == null)
            {
                _pointersBySessionId.Clear();
                _releaseBuffer.Clear();
                return;
            }

            foreach (var state in _pointersBySessionId.Values)
            {
                ProcessRelease(eventSystem, state);
            }

            _pointersBySessionId.Clear();
            _releaseBuffer.Clear();
        }

        private static void ProcessPress(PointerEventData data)
        {
            var currentOverGo = data.pointerCurrentRaycast.gameObject;
            data.pressPosition = data.position;
            data.pointerPressRaycast = data.pointerCurrentRaycast;
            data.eligibleForClick = true;
            data.delta = Vector2.zero;
            data.dragging = false;
            data.useDragThreshold = true;
            data.pointerClick = null;
            data.pointerPress = null;
            data.rawPointerPress = currentOverGo;

            if (currentOverGo == null)
            {
                return;
            }

            var newPressed = ExecuteEvents.ExecuteHierarchy(currentOverGo, data, ExecuteEvents.pointerDownHandler);
            if (newPressed == null)
            {
                newPressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentOverGo);
            }

            var time = Time.unscaledTime;
            if (newPressed == data.lastPress && time - data.clickTime < 0.3f)
            {
                data.clickCount++;
            }
            else
            {
                data.clickCount = 1;
            }

            data.pointerPress = newPressed;
            data.rawPointerPress = currentOverGo;
            data.clickTime = time;
            data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(currentOverGo);

            if (data.pointerDrag != null)
            {
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
            }
        }

        private static void ProcessMove(PointerEventData data)
        {
            var target = data.pointerCurrentRaycast.gameObject;
            if (target != null)
            {
                ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerMoveHandler);
            }
        }

        private void ProcessDrag(PointerEventData data)
        {
            if (data.pointerDrag == null)
            {
                return;
            }

            if (!data.dragging && ShouldStartDrag(data.pressPosition, data.position, EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 10, data.useDragThreshold))
            {
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler);
                data.dragging = true;
            }

            if (data.dragging)
            {
                if (data.pointerPress != null && data.pointerPress != data.pointerDrag)
                {
                    ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
                    data.eligibleForClick = false;
                    data.pointerPress = null;
                    data.rawPointerPress = null;
                }

                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
            }
        }

        private static void ProcessReleaseInternal(PointerEventData data)
        {
            var currentOverGo = data.pointerCurrentRaycast.gameObject;

            if (data.pointerPress != null)
            {
                ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            }

            var pointerUpHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentOverGo);
            if (data.pointerPress == pointerUpHandler && data.eligibleForClick)
            {
                ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerClickHandler);
            }
            else if (data.pointerDrag != null && data.dragging)
            {
                ExecuteEvents.ExecuteHierarchy(currentOverGo, data, ExecuteEvents.dropHandler);
            }

            if (data.pointerDrag != null && data.dragging)
            {
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
            }

            data.eligibleForClick = false;
            data.pointerPress = null;
            data.rawPointerPress = null;
            data.dragging = false;
            data.pointerDrag = null;
        }

        private static void HandlePointerExitAndEnter(PointerEventData data, GameObject newEnterTarget)
        {
            if (data.pointerEnter == newEnterTarget)
            {
                return;
            }

            var commonRoot = FindCommonRoot(data.pointerEnter, newEnterTarget);

            if (data.pointerEnter != null)
            {
                var current = data.pointerEnter.transform;
                while (current != null && (commonRoot == null || !current.IsChildOf(commonRoot.transform)))
                {
                    ExecuteEvents.Execute(current.gameObject, data, ExecuteEvents.pointerExitHandler);
                    current = current.parent;
                }
            }

            data.pointerEnter = newEnterTarget;

            if (newEnterTarget == null)
            {
                return;
            }

            var enterTransform = newEnterTarget.transform;
            while (enterTransform != null && enterTransform != (commonRoot != null ? commonRoot.transform : null))
            {
                ExecuteEvents.Execute(enterTransform.gameObject, data, ExecuteEvents.pointerEnterHandler);
                enterTransform = enterTransform.parent;
            }
        }

        private static GameObject FindCommonRoot(GameObject g1, GameObject g2)
        {
            if (g1 == null || g2 == null)
            {
                return null;
            }

            var t1 = g1.transform;
            while (t1 != null)
            {
                var t2 = g2.transform;
                while (t2 != null)
                {
                    if (t1 == t2)
                    {
                        return t1.gameObject;
                    }

                    t2 = t2.parent;
                }

                t1 = t1.parent;
            }

            return null;
        }

        private static bool ShouldStartDrag(Vector2 pressPos, Vector2 currentPos, float threshold, bool useDragThreshold)
        {
            if (!useDragThreshold)
            {
                return true;
            }

            return (pressPos - currentPos).sqrMagnitude >= threshold * threshold;
        }

        private static RaycastResult FindFirstRaycast(List<RaycastResult> candidates)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].gameObject != null)
                {
                    return candidates[i];
                }
            }

            return default;
        }

        private static Vector2 ToScreenPosition(Tuio11Cursor cursor)
        {
            var x = Mathf.Clamp01(cursor.Position.X);
            var y = Mathf.Clamp01(cursor.Position.Y);
            return new Vector2(x * Screen.width, (1f - y) * Screen.height);
        }

        private EventSystem ResolveEventSystem()
        {
            if (_eventSystem == null)
            {
                _eventSystem = EventSystem.current != null ? EventSystem.current : FindSceneObject<EventSystem>();
            }

            return _eventSystem;
        }

        private static T FindSceneObject<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>();
#else
            return FindObjectOfType<T>();
#endif
        }

        private sealed class CursorPointerState
        {
            public uint SessionId;
            public int PointerId;
            public Vector2 ScreenPosition;
            public bool PressedThisFrame;
            public PointerEventData EventData;
        }
    }
}
