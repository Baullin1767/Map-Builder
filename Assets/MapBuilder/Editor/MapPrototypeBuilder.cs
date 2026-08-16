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
        private const string TiledRoot = "Assets/Img/Tiled_files";
        private const string ExteriorPath = TiledRoot + "/Tiles_exterior.png";
        private const string CoastPath = TiledRoot + "/Water_coasts.png";
        private const string WaterDetailPath = TiledRoot + "/water_detilazation_v2.png";
        private const string CatalogPath = "Assets/MapBuilder/MapTileCatalog.asset";
        private const int SheetSize = 1254;
        private const int CellSize = 156;
        private const int WaterTileSize = 68;
        private const int WaterPixelsPerUnit = 85;
        private const int Offset = 3;
        private const int TiledPixelsPerUnit = 16;
        private const int GrassSpriteIndex = 227;
        private const int EarthSpriteIndex = 231;
        private const int BaseWaterSpriteIndex = 0;
        private const int ShoreEndSpriteIndex = 67;
        private const int ShoreStraightSpriteIndex = 86;
        private const int ShoreInnerCornerSpriteIndex = 90;
        private const int ShoreOuterCornerSpriteIndex = 92;

        [MenuItem("Tools/Map Builder/Build Prototype")]
        public static void BuildPrototype()
        {
            Sprite[] exterior = LoadTiledSheet(ExteriorPath);
            Sprite[] coasts = LoadTiledSheet(CoastPath);
            Sprite[] waterDetails = LoadTiledSheet(WaterDetailPath);
            Sprite grass = FindSprite(exterior, "Tiles_exterior", GrassSpriteIndex);
            Sprite earth = FindSprite(exterior, "Tiles_exterior", EarthSpriteIndex);
            Sprite baseWater = FindSprite(
                waterDetails, "water_detilazation_v2", BaseWaterSpriteIndex);

            MapTileCatalog catalog = AssetDatabase.LoadAssetAtPath<MapTileCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<MapTileCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.Configure(
                new List<Sprite> { grass },
                BuildEarthRoadGroups(earth),
                baseWater,
                BuildShoreGroups(coasts));
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            BuildScene(catalog);
            Debug.Log("Map Builder prototype created from the 16x16 Tiled Files tilesets.");
        }

        [MenuItem("Tools/Map Builder/Rebuild Tiled Catalog")]
        public static void RebuildTiledCatalog()
        {
            MapTileCatalog catalog = AssetDatabase.LoadAssetAtPath<MapTileCatalog>(CatalogPath);
            if (catalog == null)
                throw new InvalidOperationException("Tile catalog not found: " + CatalogPath);

            Sprite[] exterior = LoadTiledSheet(ExteriorPath);
            Sprite[] coasts = LoadTiledSheet(CoastPath);
            Sprite[] waterDetails = LoadTiledSheet(WaterDetailPath);
            Sprite grass = FindSprite(exterior, "Tiles_exterior", GrassSpriteIndex);
            Sprite earth = FindSprite(exterior, "Tiles_exterior", EarthSpriteIndex);
            Sprite baseWater = FindSprite(
                waterDetails, "water_detilazation_v2", BaseWaterSpriteIndex);
            catalog.Configure(
                new List<Sprite> { grass },
                BuildEarthRoadGroups(earth),
                baseWater,
                BuildShoreGroups(coasts));
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            MapGenerationController controller =
                UnityEngine.Object.FindAnyObjectByType<MapGenerationController>();
            if (controller != null)
            {
                controller.GenerateDebugMap();
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
            }

            Debug.Log("Map catalog rebuilt from Tiles Exterior, Water Coast and Water Detalization.");
        }

        private static Sprite[] LoadTiledSheet(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Texture not found: " + path);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = TiledPixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = true;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>()
                .OrderBy(ParseSpriteIndex)
                .ToArray();
        }

        private static Sprite FindSprite(Sprite[] sprites, string prefix, int index)
        {
            string expected = prefix + "_" + index;
            Sprite sprite = sprites.FirstOrDefault(value => value.name == expected);
            if (sprite == null)
                throw new InvalidOperationException("Sprite not found: " + expected);
            return sprite;
        }

        private static List<SpriteMaskVariants> BuildEarthRoadGroups(Sprite earth)
        {
            return new List<SpriteMaskVariants>
            {
                new SpriteMaskVariants
                {
                    mask = 0,
                    rotationQuarterTurns = 0,
                    sprites = new List<Sprite> { earth }
                }
            };
        }

        private static List<SpriteMaskVariants> BuildShoreGroups(Sprite[] sprites)
        {
            // Water_coasts contains several visually different coast families in
            // the same mask range (grass, dirt slopes and cliff fragments). A
            // mask-based scan therefore mixed incompatible pieces around one
            // lake. Keep one coherent grass family and rotate its four topology
            // archetypes for every direction.
            return new List<SpriteMaskVariants>
            {
                ShoreVariant(MapTopology.North,
                    FindSprite(sprites, "Water_coasts", ShoreEndSpriteIndex)),
                ShoreVariant(
                    MapTopology.North | MapTopology.East | MapTopology.South |
                    MapTopology.NorthEast | MapTopology.SouthEast,
                    FindSprite(sprites, "Water_coasts", ShoreStraightSpriteIndex)),
                ShoreVariant(
                    MapTopology.East | MapTopology.South | MapTopology.SouthEast,
                    FindSprite(sprites, "Water_coasts", ShoreOuterCornerSpriteIndex)),
                ShoreVariant(
                    MapTopology.North | MapTopology.East | MapTopology.South |
                    MapTopology.West | MapTopology.NorthEast |
                    MapTopology.SouthEast | MapTopology.NorthWest,
                    FindSprite(sprites, "Water_coasts", ShoreInnerCornerSpriteIndex))
            };
        }

        private static SpriteMaskVariants ShoreVariant(int mask, Sprite sprite)
        {
            return new SpriteMaskVariants
            {
                mask = MapTopology.CanonicalWaterMask(mask),
                rotationQuarterTurns = 0,
                sprites = new List<Sprite> { sprite }
            };
        }

        private static Sprite[] LoadExistingSheet(string path, int expectedCount, int pixelsPerUnit)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Texture not found: " + path);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = true;
            importer.SaveAndReimport();

            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
                .ToArray();
            if (sprites.Length != expectedCount)
                throw new InvalidOperationException(
                    path + " produced " + sprites.Length + " sprites instead of " + expectedCount + ".");
            return sprites;
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

            // The atlas' native vertical straight has slightly different edge
            // geometry. Reusing the horizontal family through a quarter turn
            // keeps straight runs visually continuous in both orientations.
            classified.Remove(MapTopology.North | MapTopology.South);

            // Several decorative diagonal patches expose the same edge mask as
            // a road corner but do not join the narrow road family cleanly.
            // Keep one canonical corner and rotate it for all four directions.
            SetRoadGroup(
                classified, sprites, MapTopology.South | MapTopology.West,
                "roads_tileset_5");
            classified.Remove(MapTopology.North | MapTopology.East);
            classified.Remove(MapTopology.East | MapTopology.South);
            classified.Remove(MapTopology.North | MapTopology.West);

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

        private static void SetRoadGroup(
            Dictionary<int, List<Sprite>> groups, Sprite[] sprites, int mask,
            params string[] spriteNames)
        {
            HashSet<string> requested = new HashSet<string>(spriteNames);
            List<Sprite> selected = sprites
                .Where(sprite => requested.Contains(sprite.name))
                .ToList();
            if (selected.Count != requested.Count)
                throw new InvalidOperationException(
                    "One or more curated road sprites are missing for mask " + mask + ".");
            groups[mask] = selected;
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
                if (IsCleanInteriorWater(sprites[i]) ||
                    !IsCanonicalShoreSprite(sprites[i]))
                {
                    continue;
                }
                int mask = MapTopology.CanonicalWaterMask(ClassifyWater(sprites[i]));
                Add(classified, mask, sprites[i]);
            }

            // Edge sampling intentionally tolerates small details, so a shoreline
            // quarter can otherwise look like a fully surrounded water tile. Never
            // let those sprites into the interior pool: every pixel must be water.
            List<Sprite> cleanInterior = sprites.Where(IsCleanInteriorWater).ToList();
            if (cleanInterior.Count == 0)
                throw new InvalidOperationException(
                    "Water tileset does not contain a clean interior-water sprite.");
            classified[MapTopology.CanonicalWaterMask(255)] = cleanInterior;

            // Use one coherent source family for the regular coast. The masks
            // on individual 68x68 quarters are ambiguous along the cut line;
            // explicit representatives keep straight runs and corners joined.
            SetWaterGroup(classified, sprites, 0x6E,
                "water_tileset_3_0", "water_tileset_3_1",
                "water_tileset_4_0", "water_tileset_4_1");
            classified.Remove(0x37);
            classified.Remove(0x9B);
            classified.Remove(0xCD);

            // Explicit corner quarters supplied by the tileset author. The sprite
            // families are intentionally inverted between outer and inner masks.
            SetWaterGroup(classified, sprites, 0x26, "water_tileset_50_0");
            SetWaterGroup(classified, sprites, 0x4C, "water_tileset_49_1");
            SetWaterGroup(classified, sprites, 0x13, "water_tileset_50_2");
            SetWaterGroup(classified, sprites, 0x89, "water_tileset_51_3");

            SetWaterGroup(classified, sprites, 0x7F, "water_tileset_44_0");
            SetWaterGroup(classified, sprites, 0xEF, "water_tileset_44_1");
            SetWaterGroup(classified, sprites, 0xBF, "water_tileset_40_2");
            SetWaterGroup(classified, sprites, 0xDF, "water_tileset_41_3");

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

        private static void SetWaterGroup(
            Dictionary<int, List<Sprite>> groups, Sprite[] sprites, int mask,
            params string[] spriteNames)
        {
            HashSet<string> requested = new HashSet<string>(spriteNames);
            List<Sprite> selected = sprites
                .Where(sprite => requested.Contains(sprite.name))
                .ToList();
            if (selected.Count != requested.Count)
                throw new InvalidOperationException(
                    "One or more curated water sprites are missing for mask " + mask + ".");
            groups[MapTopology.CanonicalWaterMask(mask)] = selected;
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
            bool n = FeatureRatio(texture, r, Direction.North, IsWaterPixel) > 0.5f;
            bool e = FeatureRatio(texture, r, Direction.East, IsWaterPixel) > 0.5f;
            bool s = FeatureRatio(texture, r, Direction.South, IsWaterPixel) > 0.5f;
            bool w = FeatureRatio(texture, r, Direction.West, IsWaterPixel) > 0.5f;
            if (n) mask |= MapTopology.North;
            if (e) mask |= MapTopology.East;
            if (s) mask |= MapTopology.South;
            if (w) mask |= MapTopology.West;
            if (n && e && CornerRatio(texture, r, 1, 1, IsWaterPixel) > 0.5f)
                mask |= MapTopology.NorthEast;
            if (s && e && CornerRatio(texture, r, 1, -1, IsWaterPixel) > 0.5f)
                mask |= MapTopology.SouthEast;
            if (s && w && CornerRatio(texture, r, -1, -1, IsWaterPixel) > 0.5f)
                mask |= MapTopology.SouthWest;
            if (n && w && CornerRatio(texture, r, -1, 1, IsWaterPixel) > 0.5f)
                mask |= MapTopology.NorthWest;
            return mask;
        }

        private static int ClassifyLand(Sprite sprite)
        {
            Texture2D texture = sprite.texture;
            RectInt r = RectToInt(sprite.rect);
            int mask = 0;
            bool n = FeatureRatio(texture, r, Direction.North, IsLandPixel) > 0.5f;
            bool e = FeatureRatio(texture, r, Direction.East, IsLandPixel) > 0.5f;
            bool s = FeatureRatio(texture, r, Direction.South, IsLandPixel) > 0.5f;
            bool w = FeatureRatio(texture, r, Direction.West, IsLandPixel) > 0.5f;
            if (n) mask |= MapTopology.North;
            if (e) mask |= MapTopology.East;
            if (s) mask |= MapTopology.South;
            if (w) mask |= MapTopology.West;
            if (n && e && CornerRatio(texture, r, 1, 1, IsLandPixel) > 0.5f)
                mask |= MapTopology.NorthEast;
            if (s && e && CornerRatio(texture, r, 1, -1, IsLandPixel) > 0.5f)
                mask |= MapTopology.SouthEast;
            if (s && w && CornerRatio(texture, r, -1, -1, IsLandPixel) > 0.5f)
                mask |= MapTopology.SouthWest;
            if (n && w && CornerRatio(texture, r, -1, 1, IsLandPixel) > 0.5f)
                mask |= MapTopology.NorthWest;
            return MapTopology.CanonicalWaterMask(mask);
        }

        private static float LandCoverage(Sprite sprite)
        {
            Texture2D texture = sprite.texture;
            RectInt rect = RectToInt(sprite.rect);
            int landPixels = 0;
            for (int y = 0; y < rect.height; y++)
            for (int x = 0; x < rect.width; x++)
                if (IsLandPixel(texture.GetPixel(rect.x + x, rect.y + y))) landPixels++;
            return landPixels / (float)(rect.width * rect.height);
        }

        private enum Direction { North, East, South, West }

        private static float RoadFeatureRatio(
            Texture2D texture, RectInt rect, Direction direction)
        {
            int matches = 0;
            int count = 0;
            int size = Mathf.Min(rect.width, rect.height);
            int center = size / 2;
            int halfSpan = Mathf.Max(1, Mathf.RoundToInt(size * (30f / CellSize)));
            int depthStart = Mathf.Max(0, Mathf.RoundToInt(size * (10f / CellSize)));
            int depthEnd = Mathf.Min(
                size - 1, Mathf.RoundToInt(size * (16f / CellSize)));

            for (int depth = depthStart; depth <= depthEnd; depth++)
            for (int tangent = center - halfSpan; tangent <= center + halfSpan; tangent++)
            {
                int x;
                int y;
                if (direction == Direction.North) { x = tangent; y = size - 1 - depth; }
                else if (direction == Direction.South) { x = tangent; y = depth; }
                else if (direction == Direction.East) { x = size - 1 - depth; y = tangent; }
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
            int size = Mathf.Min(rect.width, rect.height);

            // Connectivity is decided on the actual outer edge. Sampling only
            // the center or several pixels inward admits cropped shoreline
            // fragments whose land lies outside this 68x68 quarter.
            for (int tangent = 0; tangent < size; tangent++)
            {
                int x;
                int y;
                if (direction == Direction.North) { x = tangent; y = size - 1; }
                else if (direction == Direction.South) { x = tangent; y = 0; }
                else if (direction == Direction.East) { x = size - 1; y = tangent; }
                else { x = 0; y = tangent; }

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
            int size = Mathf.Min(rect.width, rect.height);
            int low = Mathf.Max(3, Mathf.RoundToInt(size * 0.07f));
            int high = Mathf.Max(low + 1, Mathf.RoundToInt(size * 0.31f));
            for (int oy = low; oy <= high; oy++)
            for (int ox = low; ox <= high; ox++)
            {
                int x = xSign > 0 ? size - 1 - ox : ox;
                int y = ySign > 0 ? size - 1 - oy : oy;
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

        private static bool IsCleanInteriorWater(Sprite sprite)
        {
            Texture2D texture = sprite.texture;
            RectInt rect = RectToInt(sprite.rect);
            for (int y = 0; y < rect.height; y++)
            for (int x = 0; x < rect.width; x++)
            {
                if (!IsWaterPixel(texture.GetPixel(rect.x + x, rect.y + y)))
                    return false;
            }
            return true;
        }

        private static bool IsCanonicalShoreSprite(Sprite sprite)
        {
            // Blocks 0-23 are the atlas' regular coast shapes. Later blocks are
            // islands, ponds and channels; their quarters can share an edge mask
            // with a coast while still leaving a disconnected rock fragment.
            string[] nameParts = sprite.name.Split('_');
            int blockIndex;
            if (nameParts.Length < 4 ||
                !int.TryParse(nameParts[nameParts.Length - 2], out blockIndex) ||
                blockIndex >= 24)
            {
                return false;
            }

            Texture2D texture = sprite.texture;
            RectInt rect = RectToInt(sprite.rect);
            int landPixels = 0;
            for (int y = 0; y < rect.height; y++)
            for (int x = 0; x < rect.width; x++)
            {
                Color32 color = texture.GetPixel(rect.x + x, rect.y + y);
                if (color.a > 64 && !IsWaterPixel(color)) landPixels++;
            }

            float landCoverage = landPixels / (float)(rect.width * rect.height);
            return landCoverage >= 0.08f && landCoverage <= 0.75f;
        }

        private static bool IsWaterPixel(Color32 color)
        {
            return color.a > 64 && color.b > color.r + 18 && color.b > color.g + 5;
        }

        private static bool IsLandPixel(Color32 color)
        {
            return color.a > 64 && !IsWaterPixel(color);
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
            controller.Configure(mapRenderer, MapGenerationSettings.Prototype64(), "prototype-seed-001", true);
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
