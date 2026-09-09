using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace HowToSuck.Editor
{
    // Unity 6000.3 can retain stale Hierarchy rows after restoring build scenes.
    // Rebuild only that window's cache, outside the failing IMGUI event. Keep the
    // original exception visible; this is an Editor workaround, not a log filter.
    [InitializeOnLoad]
    public static class HierarchyWindowRecovery
    {
        private static readonly Type WindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
        private static readonly MethodInfo Reload = WindowType?.GetMethod("ReloadData",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);
        private static bool pending;
        private static double nextRecovery;

        static HierarchyWindowRecovery() => Application.logMessageReceived += OnLog;

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Exception || pending || Reload == null ||
                EditorApplication.timeSinceStartup < nextRecovery ||
                !message.StartsWith("ArgumentOutOfRangeException:", StringComparison.Ordinal) ||
                stackTrace == null || !stackTrace.Contains("UnityEditor.GameObjectTreeViewDataSource.GetItem"))
                return;

            pending = true;
            EditorApplication.update += Recover;
        }

        [MenuItem("How to Suck/Восстановить окно Hierarchy", priority = 30)]
        public static void Recover()
        {
            EditorApplication.update -= Recover;
            pending = false;
            nextRecovery = EditorApplication.timeSinceStartup + 5;
            if (Reload == null) return;
            foreach (var window in Resources.FindObjectsOfTypeAll(WindowType))
            {
                Reload.Invoke(window, null);
                ((EditorWindow)window).Repaint();
            }
        }
    }
}
