using MapBuilder;
using UnityEditor;
using UnityEngine;

namespace MapBuilderEditor
{
    [InitializeOnLoad]
    internal static class MapTilemapPreviewRefresher
    {
        private static bool refreshScheduled;

        static MapTilemapPreviewRefresher()
        {
            AssemblyReloadEvents.afterAssemblyReload += ScheduleRefresh;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.projectChanged += ScheduleRefresh;
            ScheduleRefresh();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                ScheduleRefresh();
        }

        private static void ScheduleRefresh()
        {
            refreshScheduled = true;
            EditorApplication.update -= RefreshWhenReady;
            EditorApplication.update += RefreshWhenReady;
        }

        private static void RefreshWhenReady()
        {
            if (!refreshScheduled)
            {
                EditorApplication.update -= RefreshWhenReady;
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            refreshScheduled = false;
            EditorApplication.update -= RefreshWhenReady;

            MapGenerationController[] controllers =
                Object.FindObjectsByType<MapGenerationController>(
                    FindObjectsInactive.Exclude);

            for (int i = 0; i < controllers.Length; i++)
            {
                MapGenerationController controller = controllers[i];
                if (controller == null || EditorUtility.IsPersistent(controller)) continue;

                SerializedObject serializedController = new SerializedObject(controller);
                SerializedProperty generateOnStart =
                    serializedController.FindProperty("generateOnStart");
                SerializedProperty debugHash = serializedController.FindProperty("debugHash");
                if (generateOnStart == null || !generateOnStart.boolValue ||
                    debugHash == null || string.IsNullOrEmpty(debugHash.stringValue))
                {
                    continue;
                }

                controller.GenerateFromHash(debugHash.stringValue);
            }
        }
    }
}
