using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapBuilder
{
    [Serializable]
    public sealed class SpriteMaskVariants
    {
        public int mask;
        public int rotationQuarterTurns;
        public List<Sprite> sprites = new List<Sprite>();
    }

    [CreateAssetMenu(menuName = "Map Builder/Tile Catalog", fileName = "MapTileCatalog")]
    public sealed class MapTileCatalog : ScriptableObject
    {
        [Header("Optional art overrides")]
        [Tooltip("Leave empty to use the generated solid-colour ground sprite.")]
        [SerializeField] private List<Sprite> grassSprites = new List<Sprite>();
        [Tooltip("Optional road sprites grouped by four-direction connection mask.")]
        [SerializeField] private List<SpriteMaskVariants> roadVariants = new List<SpriteMaskVariants>();
        [Tooltip("Leave empty to use the generated solid-colour water sprite.")]
        [SerializeField] private Sprite baseWaterSprite;
        [Tooltip("Optional shore sprites grouped by eight-direction land mask.")]
        [SerializeField] private List<SpriteMaskVariants> shoreVariants = new List<SpriteMaskVariants>();

        [Header("Primitive fallback")]
        [SerializeField] private bool usePrimitiveFallback = true;
        [SerializeField, Range(8, 64)] private int primitiveResolution = 16;
        [SerializeField] private Color32 groundColor = new Color32(92, 153, 74, 255);
        [SerializeField] private Color32 waterColor = new Color32(54, 132, 190, 255);
        [SerializeField] private Color32 roadColor = new Color32(151, 111, 72, 255);

        private readonly Dictionary<string, Sprite> primitiveSprites =
            new Dictionary<string, Sprite>();
        private readonly List<Texture2D> primitiveTextures = new List<Texture2D>();

        public int GrassSpriteCount { get { return CountUsable(grassSprites) > 0 ? CountUsable(grassSprites) : (usePrimitiveFallback ? 1 : 0); } }
        public int RoadMaskCount { get { return CountUsable(roadVariants) > 0 ? CountUsable(roadVariants) : (usePrimitiveFallback ? 16 : 0); } }
        public int ShoreMaskCount { get { return CountUsable(shoreVariants) > 0 ? CountUsable(shoreVariants) : (usePrimitiveFallback ? 256 : 0); } }
        public int WaterMaskCount { get { return baseWaterSprite != null || usePrimitiveFallback ? 1 : 0; } }
        public Sprite BaseWaterSprite { get { return baseWaterSprite != null ? baseWaterSprite : PrimitiveSolid("Water", waterColor); } }
        public bool UsesPrimitiveFallback { get { return usePrimitiveFallback; } }

        public void Configure(
            List<Sprite> grass,
            List<SpriteMaskVariants> roads,
            Sprite water,
            List<SpriteMaskVariants> shores)
        {
            grassSprites = grass ?? new List<Sprite>();
            roadVariants = roads ?? new List<SpriteMaskVariants>();
            baseWaterSprite = water;
            shoreVariants = shores ?? new List<SpriteMaskVariants>();
            ClearPrimitiveCache();
        }

        public void ConfigurePrimitiveFallback(
            Color32 ground, Color32 water, Color32 road)
        {
            usePrimitiveFallback = true;
            groundColor = ground;
            waterColor = water;
            roadColor = road;
            ClearPrimitiveCache();
        }

        public Sprite GetGrassSprite(int variant)
        {
            Sprite sprite = GetUsableSprite(grassSprites, variant);
            return sprite != null ? sprite : PrimitiveSolid("Ground", groundColor);
        }

        public Sprite GetRoadVisual(
            int mask, ulong seed, int x, int y, out int rotationQuarterTurns)
        {
            Sprite sprite = GetVisual(
                roadVariants, mask & 15, seed, x, y, false,
                out rotationQuarterTurns);
            if (sprite != null || !usePrimitiveFallback) return sprite;
            rotationQuarterTurns = 0;
            return PrimitiveRoad(mask & 15);
        }

        public Sprite GetRoadSprite(int mask, ulong seed, int x, int y)
        {
            int rotationQuarterTurns;
            return GetRoadVisual(mask, seed, x, y, out rotationQuarterTurns);
        }

        public Sprite GetWaterSprite(int mask, ulong seed, int x, int y)
        {
            return BaseWaterSprite;
        }

        public Sprite GetWaterVisual(
            int mask, ulong seed, int x, int y, out int rotationQuarterTurns)
        {
            if (MapTopology.CanonicalWaterMask(mask) == 255)
            {
                rotationQuarterTurns = 0;
                return BaseWaterSprite;
            }
            return GetShoreVisual(mask, seed, x, y, out rotationQuarterTurns);
        }

        public Sprite GetShoreVisual(
            int landMask, ulong seed, int x, int y, out int rotationQuarterTurns)
        {
            int canonicalMask = MapTopology.CanonicalWaterMask(landMask);
            Sprite sprite = GetVisual(
                shoreVariants, canonicalMask,
                seed ^ 0xA0761D6478BD642FUL, x, y, true,
                out rotationQuarterTurns);
            if (sprite != null || !usePrimitiveFallback) return sprite;
            rotationQuarterTurns = 0;
            return PrimitiveShore(canonicalMask);
        }

        public bool HasRoadMask(int mask)
        {
            return usePrimitiveFallback || HasVisual(roadVariants, mask & 15, false);
        }

        public bool HasWaterMask(int mask)
        {
            return baseWaterSprite != null || usePrimitiveFallback;
        }

        public bool HasShoreMask(int mask)
        {
            return usePrimitiveFallback ||
                HasVisual(shoreVariants, MapTopology.CanonicalWaterMask(mask), true);
        }

        private Sprite PrimitiveSolid(string label, Color32 color)
        {
            if (!usePrimitiveFallback) return null;
            string key = label + ":" + ColorUtility.ToHtmlStringRGBA(color);
            return GetOrCreatePrimitive(key, delegate(Color32[] pixels, int size)
            {
                for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            });
        }

        private Sprite PrimitiveRoad(int mask)
        {
            string key = "Road:" + mask + ":" + ColorUtility.ToHtmlStringRGBA(roadColor);
            return GetOrCreatePrimitive(key, delegate(Color32[] pixels, int size)
            {
                int width = Mathf.Max(3, size * 3 / 8);
                int low = (size - width) / 2;
                int high = low + width;
                int center = size / 2;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool horizontal = y >= low && y < high &&
                        (((mask & MapTopology.West) != 0 && x <= center) ||
                         ((mask & MapTopology.East) != 0 && x >= center));
                    bool vertical = x >= low && x < high &&
                        (((mask & MapTopology.South) != 0 && y <= center) ||
                         ((mask & MapTopology.North) != 0 && y >= center));
                    bool hub = x >= low && x < high && y >= low && y < high;
                    pixels[y * size + x] = horizontal || vertical || hub
                        ? roadColor : new Color32(0, 0, 0, 0);
                }
            });
        }

        private Sprite PrimitiveShore(int landMask)
        {
            string key = "Shore:" + landMask + ":" + ColorUtility.ToHtmlStringRGBA(groundColor);
            return GetOrCreatePrimitive(key, delegate(Color32[] pixels, int size)
            {
                int band = Mathf.Max(2, size / 3);
                bool n = (landMask & MapTopology.North) != 0;
                bool e = (landMask & MapTopology.East) != 0;
                bool s = (landMask & MapTopology.South) != 0;
                bool w = (landMask & MapTopology.West) != 0;
                bool ne = (landMask & MapTopology.NorthEast) != 0;
                bool se = (landMask & MapTopology.SouthEast) != 0;
                bool sw = (landMask & MapTopology.SouthWest) != 0;
                bool nw = (landMask & MapTopology.NorthWest) != 0;

                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool land = true;
                    if (!n && y >= size - band) land = false;
                    if (!e && x >= size - band) land = false;
                    if (!s && y < band) land = false;
                    if (!w && x < band) land = false;
                    if (n && e && !ne && x + y >= (size - 1) * 2 - band) land = false;
                    if (s && e && !se && x - y >= size - band) land = false;
                    if (s && w && !sw && x + y < band) land = false;
                    if (n && w && !nw && y - x >= size - band) land = false;
                    pixels[y * size + x] = land
                        ? groundColor : new Color32(0, 0, 0, 0);
                }
            });
        }

        private Sprite GetOrCreatePrimitive(string key, Action<Color32[], int> paint)
        {
            Sprite cached;
            if (primitiveSprites.TryGetValue(key, out cached) && cached != null)
                return cached;

            int size = Mathf.Clamp(primitiveResolution, 8, 64);
            Color32[] pixels = new Color32[size * size];
            paint(pixels, size);
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "Primitive_" + key;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.DontSave;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(
                texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = "Primitive_" + key;
            sprite.hideFlags = HideFlags.DontSave;
            primitiveTextures.Add(texture);
            primitiveSprites[key] = sprite;
            return sprite;
        }

        private void OnDisable()
        {
            ClearPrimitiveCache();
        }

        private void ClearPrimitiveCache()
        {
            foreach (Sprite sprite in primitiveSprites.Values)
                DestroyGeneratedObject(sprite);
            for (int i = 0; i < primitiveTextures.Count; i++)
                DestroyGeneratedObject(primitiveTextures[i]);
            primitiveSprites.Clear();
            primitiveTextures.Clear();
        }

        private static void DestroyGeneratedObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private static Sprite GetUsableSprite(List<Sprite> sprites, int variant)
        {
            int count = CountUsable(sprites);
            if (count == 0) return null;
            int selected = variant % count;
            if (selected < 0) selected += count;
            for (int i = 0; i < sprites.Count; i++)
            {
                if (sprites[i] == null) continue;
                if (selected-- == 0) return sprites[i];
            }
            return null;
        }

        private static int CountUsable(List<Sprite> sprites)
        {
            if (sprites == null) return 0;
            int count = 0;
            for (int i = 0; i < sprites.Count; i++)
                if (sprites[i] != null) count++;
            return count;
        }

        private static int CountUsable(List<SpriteMaskVariants> entries)
        {
            if (entries == null) return 0;
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && CountUsable(entries[i].sprites) > 0) count++;
            return count;
        }

        private static bool HasVisual(
            List<SpriteMaskVariants> entries, int mask, bool useDiagonalWeight)
        {
            SpriteMaskVariants entry = FindExact(entries, mask);
            if (entry != null && CountUsable(entry.sprites) > 0) return true;
            if (useDiagonalWeight)
            {
                int turns;
                if (TryFindRotated(entries, mask, out entry, out turns)) return true;
            }
            return FindBest(entries, mask, useDiagonalWeight) != null;
        }

        private static Sprite GetVisual(
            List<SpriteMaskVariants> entries,
            int mask,
            ulong seed,
            int x,
            int y,
            bool useDiagonalWeight,
            out int rotationQuarterTurns)
        {
            SpriteMaskVariants entry = FindExact(entries, mask);
            rotationQuarterTurns = 0;
            if ((entry == null || CountUsable(entry.sprites) == 0) && useDiagonalWeight)
                TryFindRotated(entries, mask, out entry, out rotationQuarterTurns);
            if (entry == null || CountUsable(entry.sprites) == 0)
                entry = FindBest(entries, mask, useDiagonalWeight);

            if (rotationQuarterTurns == 0 && entry != null)
                rotationQuarterTurns = entry.rotationQuarterTurns;
            if (entry == null) return null;
            int count = CountUsable(entry.sprites);
            if (count == 0) return null;
            uint hash = SeedUtility.CellHash(seed ^ 0xD1B54A32D192ED03UL, x, y);
            return GetUsableSprite(entry.sprites, (int)(hash % (uint)count));
        }

        private static bool TryFindRotated(
            List<SpriteMaskVariants> entries,
            int target,
            out SpriteMaskVariants entry,
            out int rotationQuarterTurns)
        {
            if (entries != null)
            {
                for (int turns = 1; turns < 4; turns++)
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] == null || CountUsable(entries[i].sprites) == 0) continue;
                    if (RotateMaskClockwise(entries[i].mask, turns) != target) continue;
                    entry = entries[i];
                    rotationQuarterTurns = turns;
                    return true;
                }
            }
            entry = null;
            rotationQuarterTurns = 0;
            return false;
        }

        private static int RotateMaskClockwise(int mask, int turns)
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

        private static SpriteMaskVariants FindExact(
            List<SpriteMaskVariants> entries, int mask)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].mask == mask) return entries[i];
            return null;
        }

        private static SpriteMaskVariants FindBest(
            List<SpriteMaskVariants> entries, int target, bool useDiagonalWeight)
        {
            if (entries == null) return null;
            SpriteMaskVariants best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null || CountUsable(entries[i].sprites) == 0) continue;
                int difference = entries[i].mask ^ target;
                int cardinal = BitCount(difference & 15);
                int diagonal = BitCount(difference & 240);
                int score = cardinal * (useDiagonalWeight ? 8 : 1) + diagonal;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = entries[i];
                }
            }
            return best;
        }

        private static int BitCount(int value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }
    }
}
