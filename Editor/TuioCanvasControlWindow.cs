using BeyondFutureOne.TuioClient;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeyondFutureOne.TuioClient.Editor
{
    public sealed class TuioCanvasControlWindow : EditorWindow
    {
        private Tuio11CanvasAdapter _adapter;
        private Vector2 _scrollPosition;

        [MenuItem("Window/Beyond TUIO Client/TUIO Client")]
        public static void Open()
        {
            GetWindow<TuioCanvasControlWindow>("TUIO Client");
        }

        [MenuItem("GameObject/Beyond TUIO Client/TUIO 1.1 Canvas Debug Setup", false, 10)]
        public static void CreateSetupFromMenu()
        {
            CreateOrSelectSetup();
        }

        private void OnEnable()
        {
            FindAdapter();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
            _adapter = (Tuio11CanvasAdapter)EditorGUILayout.ObjectField("Canvas Adapter", _adapter, typeof(Tuio11CanvasAdapter), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Find Adapter"))
                {
                    FindAdapter();
                }

                if (GUILayout.Button("Create Setup"))
                {
                    _adapter = CreateOrSelectSetup();
                }
            }

            EditorGUILayout.Space(8f);
            DrawConnectionInfo();
            EditorGUILayout.Space(8f);
            DrawSessionSettings();
            EditorGUILayout.Space(8f);
            DrawDebugControls();

            EditorGUILayout.EndScrollView();
        }

        private void DrawConnectionInfo()
        {
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);

            if (_adapter == null)
            {
                EditorGUILayout.HelpBox("No Beyond TUIO client adapter found in the open scene.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Summary", _adapter.ConnectionSummary);
            EditorGUILayout.LabelField("Active Tokens", _adapter.ActiveTokenCount.ToString());
            EditorGUILayout.LabelField("Activity", _adapter.HasRecentControllerActivity ? "Recent TUIO object messages received" : "Waiting for TUIO object messages");

            if (_adapter.LastObjectSymbolId > 0)
            {
                EditorGUILayout.LabelField("Last Token", $"ID {_adapter.LastObjectSymbolId}, session {_adapter.LastObjectSessionId}");
            }
        }

        private void DrawSessionSettings()
        {
            if (_adapter == null || _adapter.TuioSessionBehaviour == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Session", EditorStyles.boldLabel);
            var session = _adapter.TuioSessionBehaviour;
            var sessionObject = new SerializedObject(session);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(sessionObject.FindProperty("<ConnectionType>k__BackingField"), new GUIContent("Connection Type"));
            EditorGUILayout.PropertyField(sessionObject.FindProperty("_ipAddress"), new GUIContent("IP Address"));
            EditorGUILayout.PropertyField(sessionObject.FindProperty("<UdpPort>k__BackingField"), new GUIContent("Port"));
            EditorGUILayout.PropertyField(sessionObject.FindProperty("_startOnAwake"), new GUIContent("Start On Awake"));
            if (EditorGUI.EndChangeCheck())
            {
                sessionObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(session);
                EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            }
        }

        private void DrawDebugControls()
        {
            EditorGUILayout.LabelField("Debug Tokens", EditorStyles.boldLabel);

            if (_adapter == null)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_adapter.DisplayDebugTokens ? "Hide Debug Tokens" : "Show Debug Tokens"))
                {
                    Undo.RecordObject(_adapter, "Toggle TUIO Debug Tokens");
                    _adapter.ToggleDebugTokens();
                    EditorUtility.SetDirty(_adapter);
                }
                if (GUILayout.Button(_adapter.ShowDetectedTokens ? "Hide Detected Tokens" : "Show Detected Tokens"))
                {
                    Undo.RecordObject(_adapter, "Toggle TUIO Detected Tokens");
                    _adapter.ToggleDetectedTokens();
                    EditorUtility.SetDirty(_adapter);
                }

                if (GUILayout.Button("Ensure 1-20"))
                {
                    Undo.RecordObject(_adapter, "Ensure TUIO Debug Tokens");
                    _adapter.EnsureTokenPool();
                    EditorUtility.SetDirty(_adapter);
                    EditorSceneManager.MarkSceneDirty(_adapter.gameObject.scene);
                }
            }

            EditorGUILayout.HelpBox("Debug tokens start FREE. Drag to move, use the mouse wheel to rotate, and right-click to toggle PLACED/FREE. F9 toggles debug visibility at runtime by default.", MessageType.None);
        }

        private void FindAdapter()
        {
            _adapter = FindObjectOfType<Tuio11CanvasAdapter>();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            Repaint();
        }

        private static Tuio11CanvasAdapter CreateOrSelectSetup()
        {
            var existing = FindObjectOfType<Tuio11CanvasAdapter>();
            if (existing != null)
            {
                Selection.activeObject = existing.gameObject;
                return existing;
            }

            var session = FindObjectOfType<BeyondTuio11SessionBehaviour>();
            if (session == null)
            {
                var sessionObject = new GameObject("TUIO 1.1 Session");
                Undo.RegisterCreatedObjectUndo(sessionObject, "Create TUIO 1.1 Session");
                session = sessionObject.AddComponent<BeyondTuio11SessionBehaviour>();
                session.UdpPort = 3333;
            }

            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                var canvasObject = new GameObject("BF_TUIO_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                Undo.RegisterCreatedObjectUndo(canvasObject, "Create BF TUIO Canvas");
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                var scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (FindObjectOfType<EventSystem>() == null)
            {
                var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(eventSystemObject, "Create EventSystem");
            }

            var rootObject = new GameObject("TUIO 1.1 Debug Tokens", typeof(RectTransform), typeof(CanvasGroup), typeof(Tuio11CanvasAdapter));
            Undo.RegisterCreatedObjectUndo(rootObject, "Create TUIO Client Adapter");
            rootObject.transform.SetParent(canvas.transform, false);

            var rootTransform = (RectTransform)rootObject.transform;
            rootTransform.anchorMin = Vector2.zero;
            rootTransform.anchorMax = Vector2.one;
            rootTransform.offsetMin = Vector2.zero;
            rootTransform.offsetMax = Vector2.zero;

            var adapter = rootObject.GetComponent<Tuio11CanvasAdapter>();
            var adapterObject = new SerializedObject(adapter);
            adapterObject.FindProperty("_tuioSessionBehaviour").objectReferenceValue = session;
            adapterObject.FindProperty("_canvas").objectReferenceValue = canvas;
            adapterObject.FindProperty("_tokenRoot").objectReferenceValue = rootTransform;
            adapterObject.ApplyModifiedPropertiesWithoutUndo();
            adapter.EnsureTokenPool();

            Selection.activeObject = adapter.gameObject;
            EditorSceneManager.MarkSceneDirty(adapter.gameObject.scene);
            return adapter;
        }
    }
}


