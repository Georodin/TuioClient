using UnityEditor;
using UnityEngine;

namespace BeyondFutureOne.TuioClient.Editor
{
    [InitializeOnLoad]
    internal static class TuioSessionPlayModeCleanup
    {
        static TuioSessionPlayModeCleanup()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseAllSessions;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                ReleaseAllSessions();
            }
        }

        private static void ReleaseAllSessions()
        {
            var sessions = FindSessions();
            for (var i = 0; i < sessions.Length; i++)
            {
                if (sessions[i] != null)
                {
                    sessions[i].Stop();
                }
            }
        }

        private static BeyondTuio11SessionBehaviour[] FindSessions()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<BeyondTuio11SessionBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<BeyondTuio11SessionBehaviour>(true);
#endif
        }
    }
}
