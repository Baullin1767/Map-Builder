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
        [SerializeField] private List<Sprite> grassSprites = new List<Sprite>();
        [SerializeField] private List<SpriteMaskVariants> roadVariants = new List<SpriteMaskVariants>();
        [SerializeField] private Sprite baseWaterSprite;
        [SerializeField] private List<SpriteMaskVariants> shoreVariants = new List<SpriteMaskVariants>();

        public int GrassSpriteCount { get { return grassSprites.Count; } }
        public int RoadMaskCount { get { return roadVariants.Count; } }
        public int ShoreMaskCount { get { return shoreVariants.Count; } }
        public int WaterMaskCount { get { return baseWaterSprite == null ? 0 : 1; } }
        public Sprite BaseWaterSprite { get { return baseWaterSprite; } }

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
        }

        public Sprite GetGrassSprite(int variant)
        {
            if (grassSprites.Count == 0) return null;
            int index = variant % grassSprites.Count;
            if (index < 0) index += grassSprites.Count;
            return grassSprites[index];
        }

        public Sprite GetRoadVisual(
            int mask, ulong seed, int x, int y, out int rotationQuarterTurns)
        {
            return GetVisual(
                roadVariants, mask & 15, seed, x, y, false,
                out rotationQuarterTurns);
        }

        public Sprite GetRoadSprite(int mask, ulong seed, int x, int y)
        {
            int rotationQuarterTurns;
            return GetRoadVisual(mask, seed, x, y, out rotationQuarterTurns);
        }

        public Sprite GetWaterSprite(int mask, ulong seed, int x, int y)
        {
            return baseWaterSprite;
        }

        public Sprite GetWaterVisual(
            int mask, ulong seed, int x, int y, out int rotationQuarterTurns)
        {
            if (MapTopology.CanonicalWaterMask(mask) == 255)
            {
                rotationQuarterTurns = 0;
                return baseWaterSprite;
            }
            return GetShoreVisual(mask, seed, x, y, out rotationQuarterTurns);
        }

        public Sprite GetShoreVisual(
            int landMask, ulong seed, int x, int y, out int rotationQuarterTurns)
        {
            return GetVisual(
                shoreVariants, MapTopology.CanonicalWaterMask(landMask),
                seed ^ 0xA0761D6478BD642FUL, x, y, true,
                out rotationQuarterTurns);
        }

        public bool HasRoadMask(int mask)
        {
            SpriteMaskVariants entry = FindExact(roadVariants, mask & 15);
            return (entry != null && entry.sprites != null && entry.sprites.Count > 0) ||
                FindBest(roadVariants, mask & 15, false) != null;
        }

        public bool HasWaterMask(int mask)
        {
            return baseWaterSprite != null;
        }

        public bool HasShoreMask(int mask)
        {
            return FindExact(
                shoreVariants, MapTopology.CanonicalWaterMask(mask)) != null;
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
            if ((entry == null || entry.sprites == null || entry.sprites.Count == 0) &&
                useDiagonalWeight)
            {
                TryFindRotated(entries, mask, out entry, out rotationQuarterTurns);
            }
            if (entry == null || entry.sprites == null || entry.sprites.Count == 0)
                entry = FindBest(entries, mask, useDiagonalWeight);

            if (rotationQuarterTurns == 0 && entry != null)
                rotationQuarterTurns = entry.rotationQuarterTurns;
            if (entry == null || entry.sprites == null || entry.sprites.Count == 0)
                return null;

            uint hash = SeedUtility.CellHash(seed ^ 0xD1B54A32D192ED03UL, x, y);
            return entry.sprites[(int)(hash % (uint)entry.sprites.Count)];
        }

        private static bool TryFindRotated(
            List<SpriteMaskVariants> entries,
            int target,
            out SpriteMaskVariants entry,
            out int rotationQuarterTurns)
        {
            for (int turns = 1; turns < 4; turns++)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].sprites == null || entries[i].sprites.Count == 0) continue;
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
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].mask == mask) return entries[i];
            return null;
        }

        private static SpriteMaskVariants FindBest(
            List<SpriteMaskVariants> entries, int target, bool useDiagonalWeight)
        {
            SpriteMaskVariants best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].sprites == null || entries[i].sprites.Count == 0) continue;
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
