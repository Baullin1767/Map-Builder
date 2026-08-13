using System;
using System.Collections.Generic;
using System.Linq;
using MapBuilder;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MapBuilderEditor
{
    public static class MapPrototypeBuilder
    {
        private const string Root = "Assets/IMG/текстуры почвы";
        private const string CatalogPath = "Assets/MapBuilder/MapTileCatalog.asset";
        private const int SheetSize = 1254;
        private const int CellSize = 156;
        private const int Offset = 3;

        [MenuItem("Tools/Map Builder/Build Prototype")]
        public static void BuildPrototype()
        {
            Sprite[] grass = ImportSheet(Root + "/grass_tileset.png");
            Sprite[] roads = ImportSheet(Root + "/roads_tileset.png");
            Sprite[] water = ImportSheet(Root + "/water_tileset.png");

            MapTileCatalog catalog = AssetDatabase.LoadAssetAtPath<MapTileCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<MapTileCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            List<SpriteMaskVariants> roadGroups = BuildRoadGroups(roads);
            List<SpriteMaskVariants> waterGroups = BuildWaterGroups(water);
            catalog.Configure(grass.ToList(), roadGroups, waterGroups);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            BuildScene(catalog);
            Debug.Log("Map Builder prototype created: 64x64, 3 Tilemaps, deterministic hash contract.");
        }

        private static Sprite[] ImportSheet(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Texture not found: " + path);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = CellSize;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = true;

            string baseName = System.IO.Path.GetFileNameWithoutExtension(path);
            SpriteMetaData[] sheet = new SpriteMetaData[64];
            for (int row = 0; row < 8; row++)
            for (int column = 0; column < 8; column++)
            {
                int index = row * 8 + column;
                sheet[index] = new SpriteMetaData
                {
                    name = baseName + "_" + index,
                    rect = new Rect(
                        Offset + column * CellSize,
                        Offset + (7 - row) * CellSize,
                        CellSize,
                        CellSize),
                    pivot = new Vector2(0.5f, 0.5f),
                    alignment = (int)SpriteAlignment.Center,
                    border = Vector4.zero
                };
            }

#pragma warning disable 618
            importer.spritesheet = sheet;
#pragma warning restore 618
            importer.SaveAndReimport();

            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>()
                .OrderBy(ParseSpriteIndex)
                .ToArray();
            if (sprites.Length != 64)
                throw new InvalidOperationException(path + " produced " + sprites.Length + " sprites instead of 64.");
            return sprites;
        }

        private static int ParseSpriteIndex(Sprite sprite)
        {
            int split = sprite.name.LastIndexOf('_');
            int value;
            return split >= 0 && int.TryParse(sprite.name.Substring(split + 1), out value)
                ? value
                : int.MaxValue;
        }

        private static List<SpriteMaskVariants> BuildRoadGroups(Sprite[] sprites)
        {
            Dictionary<int, List<Sprite>> classified = new Dictionary<int, List<Sprite>>();
            for (int i = 0; i < sprites.Length; i++)
                Add(classified, ClassifyRoad(sprites[i]), sprites[i]);

            List<SpriteMaskVariants> result = new List<SpriteMaskVariants>();
            for (int target = 0; target < 16; target++)
            {
                List<Sprite> variants;
                int rotationQuarterTurns = 0;
                if (!classified.TryGetValue(target, out variants) || variants.Count == 0)
                {
                    if (!TryFindRotatedRoad(classified, target, out variants, out rotationQuarterTurns))
                        variants = Nearest(classified, target, false);
                }
                result.Add(new SpriteMaskVariants
                {
                    mask = target,
                    rotationQuarterTurns = rotationQuarterTurns,
                    sprites = new List<Sprite>(variants)
                });
            }
            return result;
        }

        private static bool TryFindRotatedRoad(
            Dictionary<int, List<Sprite>> groups, int target,
            out List<Sprite> variants, out int rotationQuarterTurns)
        {
            for (int turns = 1; turns < 4; turns++)
            {
                foreach (KeyValuePair<int, List<Sprite>> pair in groups)
                {
                    if (RotateRoadMaskClockwise(pair.Key, turns) != target) continue;
                    variants = pair.Value;
                    rotationQuarterTurns = turns;
                    return true;
                }
            }
            variants = null;
            rotationQuarterTurns = 0;
            return false;
        }

        private static int RotateRoadMaskClockwise(int mask, int turns)
        {
            for (int i = 0; i < turns; i++)
            {
                int rotated = 0;
                if ((mask & MapTopology.North) != 0) rotated |= MapTopology.East;
                if ((mask & MapTopology.East) != 0) rotated |= MapTopology.South;
                if ((mask & MapTopology.South) != 0) rotated |= MapTopology.West;
                if ((mask & MapTopology.West) != 0) rotated |= MapTopology.North;
                mask = rotated;
            }
            return mask;
        }

private static List<SpriteMaskVariants> BuildWaterGroups(Sprite[] sprites)
        {
            Dictionary<int, List<Sprite>> classified = new Dictionary<int, List<Sprite>>();
            for (int i = 0; i < sprites.Length; i++)
            {
                int mask = MapTopology.CanonicalWaterMask(ClassifyWater(sprites[i]));
                Add(classified, mask, sprites[i]);
            }

            HashSet<int> canonical = new HashSet<int>();
            for (int mask = 0; mask < 256; mask++)
                canonical.Add(MapTopology.CanonicalWaterMask(mask));

            List<SpriteMaskVariants> result = new List<SpriteMaskVariants>();
            foreach (int target in canonical.OrderBy(value => value))
            {
                List<Sprite> variants;
                int rotationQuarterTurns = 0;
                if (!classified.TryGetValue(target, out variants) || variants.Count == 0)
                {
                    if (!TryFindRotatedWater(
                        classified, target, out variants, out rotationQuarterTurns))
                    {
                        variants = Nearest(classified, target, true);
                    }
                }
                result.Add(new SpriteMaskVariants
                {
                    mask = target,
                    rotationQuarterTurns = rotationQuarterTurns,
                    sprites = new List<Sprite>(variants)
                });
            }
            return result;
        }

private static bool TryFindRotatedWater(
            Dictionary<int, List<Sprite>> groups, int target,
            out List<Sprite> variants, out int rotationQuarterTurns)
        {
            for (int turns = 1; turns < 4; turns++)
            {
                foreach (KeyValuePair<int, List<Sprite>> pair in groups)
                {
                    if (RotateWaterMaskClockwise(pair.Key, turns) != target) continue;
                    variants = pair.Value;
                    rotationQuarterTurns = turns;
                    return true;
                }
            }
            variants = null;
            rotationQuarterTurns = 0;
            return false;
        }

private static int RotateWaterMaskClockwise(int mask, int turns)
        {
            for (int i = 0; i < turns; i++)
            {
                int rotated = 0;
                if ((mask & MapTopology.North) != 0) rotated |= MapTopology.East;
                if ((mask & MapTopology.East) != 0) rotated |= MapTopology.South;
                if ((mask & MapTopology.South) != 0) rotated |= MapTopology.West;
                if ((mask & MapTopology.West) != 0) rotated |= MapTopology.North;
                if ((mask & MapTopology.NorthEast) != 0) rotated |= MapTopology.SouthEast;
                if ((mask & MapTopology.SouthEast) != 0) rotated |= MapTopology.SouthWest;
                if ((mask & MapTopology.SouthWest) != 0) rotated |= MapTopology.NorthWest;
                if ((mask & MapTopology.NorthWest) != 0) rotated |= MapTopology.NorthEast;
                mask = MapTopology.CanonicalWaterMask(rotated);
            }
            return mask;
        }



        private static void Add(Dictionary<int, List<Sprite>> groups, int mask, Sprite sprite)
        {
            List<Sprite> values;
            if (!groups.TryGetValue(mask, out values))
            {
                values = new List<Sprite>();
                groups.Add(mask, values);
            }
            values.Add(sprite);
        }

        private static List<Sprite> Nearest(
            Dictionary<int, List<Sprite>> groups, int target, bool water)
        {
            List<Sprite> best = null;
            int bestScore = int.MaxValue;
            foreach (KeyValuePair<int, List<Sprite>> pair in groups)
            {
                int difference = pair.Key ^ target;
                int score = BitCount(difference & 15) * (water ? 8 : 1)
                    + BitCount(difference & 240);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = pair.Value;
                }
            }
            return best ?? new List<Sprite>();
        }

        private static int BitCount(int value)
        {
            int result = 0;
            while (value != 0)
            {
                value &= value - 1;
                result++;
            }
            return result;
        }

        private static int ClassifyRoad(Sprite sprite)
        {
            Texture2D texture = sprite.texture;
            RectInt r = RectToInt(sprite.rect);
            int mask = 0;
            if (RoadFeatureRatio(texture, r, Direction.North) > 0.2f) mask |= MapTopology.North;
            if (RoadFeatureRatio(texture, r, Direction.East) > 0.2f) mask |= MapTopology.East;
            if (RoadFeatureRatio(texture, r, Direction.South) > 0.2f) mask |= MapTopology.South;
            if (RoadFeatureRatio(texture, r, Direction.West) > 0.2f) mask |= MapTopology.West;
            return mask;
        }


private static int ClassifyWater(Sprite sprite)
        {
            Texture2D texture = sprite.texture;
            RectInt r = RectToInt(sprite.rect);
            int mask = 0;
            bool n = FeatureRatio(texture, r, Direction.North, IsWaterPixel) > 0.32f;
            bool e = FeatureRatio(texture, r, Direction.East, IsWaterPixel) > 0.32f;
            bool s = FeatureRatio(texture, r, Direction.South, IsWaterPixel) > 0.32f;
            bool w = FeatureRatio(texture, r, Direction.West, IsWaterPixel) > 0.32f;
            if (n) mask |= MapTopology.North;
            if (e) mask |= MapTopology.East;
            if (s) mask |= MapTopology.South;
            if (w) mask |= MapTopology.West;
            if (n && e && CornerRatio(texture, r, 1, 1, IsWaterPixel) > 0.32f)
                mask |= MapTopology.NorthEast;
            if (s && e && CornerRatio(texture, r, 1, -1, IsWaterPixel) > 0.32f)
                mask |= MapTopology.SouthEast;
            if (s && w && CornerRatio(texture, r, -1, -1, IsWaterPixel) > 0.32f)
                mask |= MapTopology.SouthWest;
            if (n && w && CornerRatio(texture, r, -1, 1, IsWaterPixel) > 0.32f)
                mask |= MapTopology.NorthWest;
            return mask;
        }

        private enum Direction { North, East, South, West }

        private static float RoadFeatureRatio(
            Texture2D texture, RectInt rect, Direction direction)
        {
            int matches = 0;
            int count = 0;
            int center = CellSize / 2;
            const int halfSpan = 30;
            const int depthStart = 10;
            const int depthEnd = 16;

            for (int depth = depthStart; depth <= depthEnd; depth++)
            for (int tangent = center - halfSpan; tangent <= center + halfSpan; tangent++)
            {
                int x;
                int y;
                if (direction == Direction.North) { x = tangent; y = CellSize - 1 - depth; }
                else if (direction == Direction.South) { x = tangent; y = depth; }
                else if (direction == Direction.East) { x = CellSize - 1 - depth; y = tangent; }
                else { x = depth; y = tangent; }

                if (IsRoadPixel(texture.GetPixel(rect.x + x, rect.y + y))) matches++;
                count++;
            }
            return count == 0 ? 0f : matches / (float)count;
        }

private static float FeatureRatio(
            Texture2D texture, RectInt rect, Direction direction, Func<Color32, bool> predicate)
        {
            int matches = 0;
            int count = 0;
            int center = CellSize / 2;
            const int halfSpan = 30;
            const int depthStart = 14;
            const int depthEnd = 20;

            for (int depth = depthStart; depth <= depthEnd; depth++)
            for (int tangent = center - halfSpan; tangent <= center + halfSpan; tangent++)
            {
                int x;
                int y;
                if (direction == Direction.North) { x = tangent; y = CellSize - 1 - depth; }
                else if (direction == Direction.South) { x = tangent; y = depth; }
                else if (direction == Direction.East) { x = CellSize - 1 - depth; y = tangent; }
                else { x = depth; y = tangent; }

                if (predicate(texture.GetPixel(rect.x + x, rect.y + y))) matches++;
                count++;
            }
            return count == 0 ? 0f : matches / (float)count;
        }

        private static float CornerRatio(
            Texture2D texture, RectInt rect, int xSign, int ySign,
            Func<Color32, bool> predicate)
        {
            int matches = 0;
            int count = 0;
            int low = 12;
            int high = 48;
            for (int oy = low; oy <= high; oy++)
            for (int ox = low; ox <= high; ox++)
            {
                int x = xSign > 0 ? CellSize - 1 - ox : ox;
                int y = ySign > 0 ? CellSize - 1 - oy : oy;
                if (predicate(texture.GetPixel(rect.x + x, rect.y + y))) matches++;
                count++;
            }
            return matches / (float)count;
        }

        private static RectInt RectToInt(Rect rect)
        {
            return new RectInt(
                Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y),
                Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height));
        }

        private static bool IsWaterPixel(Color32 color)
        {
            return color.a > 64 && color.b > color.r + 18 && color.b > color.g + 5;
        }

        private static bool IsRoadPixel(Color32 color)
        {
            return color.a > 64 && color.b > 29 && color.r > color.g * 0.95f;
        }

        private static void BuildScene(MapTileCatalog catalog)
        {
            GameObject oldRoot = GameObject.Find("Generated Map");
            if (oldRoot != null) UnityEngine.Object.DestroyImmediate(oldRoot);
            GameObject oldSystem = GameObject.Find("Map Generation");
            if (oldSystem != null) UnityEngine.Object.DestroyImmediate(oldSystem);

            GameObject root = new GameObject("Generated Map");
            Grid grid = root.AddComponent<Grid>();
            grid.cellSize = Vector3.one;
            grid.cellGap = Vector3.zero;
            grid.cellLayout = GridLayout.CellLayout.Rectangle;

            Tilemap ground = CreateLayer(root.transform, "Ground", 0);
            Tilemap water = CreateLayer(root.transform, "Water", 10);
            Tilemap roads = CreateLayer(root.transform, "Roads", 20);

            GameObject system = new GameObject("Map Generation");
            MapTilemapRenderer mapRenderer = system.AddComponent<MapTilemapRenderer>();
            mapRenderer.Configure(ground, water, roads, catalog);
            MapGenerationController controller = system.AddComponent<MapGenerationController>();
            controller.Configure(mapRenderer, MapGenerationSettings.Prototype64(), "prototype-seed-001", true);
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
        }
    }
}