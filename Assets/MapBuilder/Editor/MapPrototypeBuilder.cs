using MapBuilder;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MapBuilderEditor
{
    public static class MapPrototypeBuilder
    {
        private const string CatalogPath = "Assets/MapBuilder/MapTileCatalog.asset";

        [MenuItem("Tools/Map Builder/Build Prototype")]
        public static void BuildPrototype()
        {
            MapTileCatalog catalog = GetOrCreateCatalog();
            catalog.Configure(null, null, null, null);
            catalog.ConfigurePrimitiveFallback(
                new Color32(92, 153, 74, 255),
                new Color32(54, 132, 190, 255),
                new Color32(151, 111, 72, 255));
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            BuildScene(catalog);
            Debug.Log("Map Builder prototype created with replaceable primitive sprites.");
        }

        [MenuItem("Tools/Map Builder/Rebuild Primitive Catalog")]
        public static void RebuildPrimitiveCatalog()
        {
            MapTileCatalog catalog = GetOrCreateCatalog();
            catalog.Configure(null, null, null, null);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            MapGenerationController controller =
                Object.FindAnyObjectByType<MapGenerationController>();
            if (controller != null)
            {
                controller.GenerateDebugMap();
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
            }
            Debug.Log("Map catalog rebuilt without external tileset dependencies.");
        }

        private static MapTileCatalog GetOrCreateCatalog()
        {
            MapTileCatalog catalog = AssetDatabase.LoadAssetAtPath<MapTileCatalog>(CatalogPath);
            if (catalog != null) return catalog;
            catalog = ScriptableObject.CreateInstance<MapTileCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            return catalog;
        }

        private static void BuildScene(MapTileCatalog catalog)
        {
            GameObject oldRoot = GameObject.Find("Generated Map");
            if (oldRoot != null) Object.DestroyImmediate(oldRoot);
            GameObject oldSystem = GameObject.Find("Map Generation");
            if (oldSystem != null) Object.DestroyImmediate(oldSystem);

            GameObject root = new GameObject("Generated Map");
            Grid grid = root.AddComponent<Grid>();
            grid.cellSize = new Vector3(0.8f, 0.8f, 1f);
            grid.cellGap = Vector3.zero;
            grid.cellLayout = GridLayout.CellLayout.Rectangle;

            Tilemap ground = CreateLayer(root.transform, "Ground", 0);
            Tilemap water = CreateLayer(root.transform, "Water", 10);
            Tilemap shore = CreateLayer(root.transform, "Shore", 15);
            Tilemap roads = CreateLayer(root.transform, "Roads", 20);

            GameObject system = new GameObject("Map Generation");
            MapTilemapRenderer mapRenderer = system.AddComponent<MapTilemapRenderer>();
            mapRenderer.Configure(ground, water, shore, roads, catalog);
            MapGenerationController controller = system.AddComponent<MapGenerationController>();
            controller.Configure(
                mapRenderer, MapGenerationSettings.Prototype64(),
                "prototype-seed-001", true);
            MapGenerationCanvasBuilder.Rebuild(controller);
            controller.GenerateFromHash("prototype-seed-001");

            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.orthographic = true;
                camera.orthographicSize = 34f;
                camera.transform.position = new Vector3(31.5f, 31.5f, -10f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(18, 29, 24, 255);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = system;
        }

        private static Tilemap CreateLayer(Transform parent, string name, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Tilemap tilemap = go.AddComponent<Tilemap>();
            TilemapRenderer renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = order;
            renderer.mode = TilemapRenderer.Mode.Chunk;
            return tilemap;
        }
    }

    [CustomEditor(typeof(MapGenerationController))]
    public sealed class MapGenerationControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (GUILayout.Button("Generate Debug Map"))
                ((MapGenerationController)target).GenerateDebugMap();

            if (GUILayout.Button("Generate Random Hash & Map"))
            {
                MapGenerationController controller = (MapGenerationController)target;
                Undo.RecordObject(controller, "Generate Random Map");
                controller.GenerateRandomMap();
                EditorUtility.SetDirty(controller);
                serializedObject.Update();
            }
        }
    }
}
