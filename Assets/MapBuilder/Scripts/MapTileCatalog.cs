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
        // Road entries can reuse a source sprite through quarter-turn rotation.
        [SerializeField] private List<Sprite> grassSprites = new List<Sprite>();
        [SerializeField] private List<SpriteMaskVariants> roadVariants = new List<SpriteMaskVariants>();
        [SerializeField] private List<SpriteMaskVariants> waterVariants = new List<SpriteMaskVariants>();

        public int GrassSpriteCount { get { return grassSprites.Count; } }
        public int RoadMaskCount { get { return roadVariants.Count; } }
        public int WaterMaskCount { get { return waterVariants.Count; } }

        public void Configure(List<Sprite> grass, List<SpriteMaskVariants> roads, List<SpriteMaskVariants> water)
        {
            grassSprites = grass ?? new List<Sprite>();
            roadVariants = roads ?? new List<SpriteMaskVariants>();
            waterVariants = water ?? new List<SpriteMaskVariants>();
        }

        public Sprite GetGrassSprite(int variant)
        {
            if (grassSprites.Count == 0) return null;
            int index = variant % grassSprites.Count;
            if (index < 0) index += grassSprites.Count;
            return grassSprites[index];
        }

public Sprite GetRoadSprite(int mask, ulong seed, int x, int y)
        {
            return Pick(roadVariants, mask & 15, seed, x, y, false);
        }

public Sprite GetRoadVisual(
            int mask, ulong seed, int x, int y, out int rotationQuarterTurns)
        {
            SpriteMaskVariants entry = FindExact(roadVariants, mask & 15);
            if (entry == null || entry.sprites == null || entry.sprites.Count == 0)
                entry = FindBest(roadVariants, mask & 15, false);
            rotationQuarterTurns = entry == null ? 0 : entry.rotationQuarterTurns;
            if (entry == null || entry.sprites == null || entry.sprites.Count == 0)
                return null;
            uint hash = SeedUtility.CellHash(seed ^ 0xD1B54A32D192ED03UL, x, y);
            return entry.sprites[(int)(hash % (uint)entry.sprites.Count)];
        }

public Sprite GetWaterSprite(int mask, ulong seed, int x, int y)
        {
            return Pick(waterVariants, MapTopology.CanonicalWaterMask(mask), seed, x, y, true);
        }

public Sprite GetWaterVisual(
            int mask, ulong seed, int x, int y, out int rotationQuarterTurns)
        {
            int canonicalMask = MapTopology.CanonicalWaterMask(mask);
            SpriteMaskVariants entry = FindExact(waterVariants, canonicalMask);
            if (entry == null || entry.sprites == null || entry.sprites.Count == 0)
                entry = FindBest(waterVariants, canonicalMask, true);
            rotationQuarterTurns = entry == null ? 0 : entry.rotationQuarterTurns;
            if (entry == null || entry.sprites == null || entry.sprites.Count == 0)
                return null;
            uint hash = SeedUtility.CellHash(seed ^ 0xD1B54A32D192ED03UL, x, y);
            return entry.sprites[(int)(hash % (uint)entry.sprites.Count)];
        }




        public bool HasRoadMask(int mask) { return FindExact(roadVariants, mask & 15) != null; }
        public bool HasWaterMask(int mask)
        {
            return FindExact(waterVariants, MapTopology.CanonicalWaterMask(mask)) != null;
        }

        private static Sprite Pick(
            List<SpriteMaskVariants> entries, int mask, ulong seed, int x, int y, bool water)
        {
            SpriteMaskVariants entry = FindExact(entries, mask);
            if (entry == null || entry.sprites == null || entry.sprites.Count == 0)
                entry = FindBest(entries, mask, water);
            if (entry == null || entry.sprites == null || entry.sprites.Count == 0)
                return null;
            uint hash = SeedUtility.CellHash(seed ^ 0xD1B54A32D192ED03UL, x, y);
            return entry.sprites[(int)(hash % (uint)entry.sprites.Count)];
        }

        private static SpriteMaskVariants FindExact(List<SpriteMaskVariants> entries, int mask)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].mask == mask) return entries[i];
            return null;
        }

        private static SpriteMaskVariants FindBest(
            List<SpriteMaskVariants> entries, int target, bool water)
        {
            SpriteMaskVariants best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].sprites == null || entries[i].sprites.Count == 0) continue;
                int difference = entries[i].mask ^ target;
                int cardinal = BitCount(difference & 15);
                int diagonal = BitCount(difference & 240);
                int score = cardinal * (water ? 8 : 1) + diagonal;
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