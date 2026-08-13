using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MapBuilder
{
    public interface IMapHashConsumer
    {
        bool GenerateFromHash(string hash);
    }

    [Serializable]
    public sealed class MapGenerationSettings
    {
        [Min(16)] public int width = 64;
        [Min(16)] public int height = 64;
        [Range(1, 2)] public int minLakes = 1;
        [Range(1, 2)] public int maxLakes = 2;
        [Range(2, 5)] public int roadGates = 3;
        [Range(1, 5)] public int roadPoints = 3;

        public MapGenerationSettings CopyValidated()
        {
            return new MapGenerationSettings
            {
                width = Mathf.Max(16, width),
                height = Mathf.Max(16, height),
                minLakes = Mathf.Clamp(minLakes, 1, 2),
                maxLakes = Mathf.Clamp(maxLakes, Mathf.Clamp(minLakes, 1, 2), 2),
                roadGates = Mathf.Clamp(roadGates, 2, 5),
                roadPoints = Mathf.Clamp(roadPoints, 1, 5)
            };
        }

        public static MapGenerationSettings Prototype64() { return new MapGenerationSettings(); }
    }

    public sealed class MapLayout
    {
        public const int GeneratorVersion = 1;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string Hash { get; private set; }
        public ulong GrassSeed { get; private set; }
        public ulong WaterSeed { get; private set; }
        public ulong RoadSeed { get; private set; }
        public bool[] Water { get; private set; }
        public bool[] Roads { get; private set; }
        public byte[] GrassVariants { get; private set; }
        public byte[] WaterMasks { get; private set; }
        public byte[] RoadMasks { get; private set; }
        public int CellCount { get { return Width * Height; } }

        public MapLayout(int width, int height, string hash)
        {
            Width = width;
            Height = height;
            Hash = hash;
            GrassSeed = SeedUtility.Derive(hash, "grass");
            WaterSeed = SeedUtility.Derive(hash, "water");
            RoadSeed = SeedUtility.Derive(hash, "roads");
            int count = width * height;
            Water = new bool[count];
            Roads = new bool[count];
            GrassVariants = new byte[count];
            WaterMasks = new byte[count];
            RoadMasks = new byte[count];
        }

        public int Index(int x, int y) { return y * Width + x; }
        public bool InBounds(int x, int y) { return x >= 0 && y >= 0 && x < Width && y < Height; }
        public bool IsWater(int x, int y) { return InBounds(x, y) && Water[Index(x, y)]; }
        public bool IsRoad(int x, int y) { return InBounds(x, y) && Roads[Index(x, y)]; }
    }

    public static class SeedUtility
    {
        public static ulong Derive(string hash, string stream)
        {
            if (string.IsNullOrEmpty(hash))
                throw new ArgumentException("Map hash must be non-empty.", "hash");
            string payload = hash + "\0" + stream + "\0v" + MapLayout.GeneratorVersion;
            byte[] bytes;
            using (SHA256 sha = SHA256.Create())
                bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return (ulong)bytes[0] | ((ulong)bytes[1] << 8) | ((ulong)bytes[2] << 16)
                | ((ulong)bytes[3] << 24) | ((ulong)bytes[4] << 32)
                | ((ulong)bytes[5] << 40) | ((ulong)bytes[6] << 48) | ((ulong)bytes[7] << 56);
        }

        public static uint CellHash(ulong seed, int x, int y)
        {
            ulong value = seed;
            value ^= (ulong)(uint)x * 0x9E3779B185EBCA87UL;
            value ^= (ulong)(uint)y * 0xC2B2AE3D27D4EB4FUL;
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return (uint)(value ^ (value >> 32));
        }

        public static float Cell01(ulong seed, int x, int y)
        {
            return (CellHash(seed, x, y) >> 8) * (1f / 16777216f);
        }
    }

    public struct DeterministicRng
    {
        private ulong state;
        private readonly ulong increment;

        public DeterministicRng(ulong seed, ulong sequence)
        {
            state = 0UL;
            increment = (sequence << 1) | 1UL;
            NextUInt();
            state += seed;
            NextUInt();
        }

        public DeterministicRng(ulong seed) : this(seed, 54UL) { }

        public uint NextUInt()
        {
            ulong oldState = state;
            state = oldState * 6364136223846793005UL + increment;
            uint xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rotation = (int)(oldState >> 59);
            return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
        }

        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException("exclusiveMax");
            uint bound = (uint)exclusiveMax;
            uint threshold = unchecked((uint)(-bound)) % bound;
            while (true)
            {
                uint value = NextUInt();
                if (value >= threshold) return (int)(value % bound);
            }
        }

        public int Range(int inclusiveMin, int exclusiveMax)
        {
            return inclusiveMin + NextInt(exclusiveMax - inclusiveMin);
        }

        public float NextFloat() { return (NextUInt() >> 8) * (1f / 16777216f); }
    }

    public static class MapTopology
    {
        public const int North = 1;
        public const int East = 2;
        public const int South = 4;
        public const int West = 8;
        public const int NorthEast = 16;
        public const int SouthEast = 32;
        public const int SouthWest = 64;
        public const int NorthWest = 128;

        public static byte RoadMask(MapLayout layout, int x, int y)
        {
            int mask = 0;
            if (layout.IsRoad(x, y + 1)) mask |= North;
            if (layout.IsRoad(x + 1, y)) mask |= East;
            if (layout.IsRoad(x, y - 1)) mask |= South;
            if (layout.IsRoad(x - 1, y)) mask |= West;
            return (byte)mask;
        }

        public static byte WaterMask(MapLayout layout, int x, int y)
        {
            int mask = 0;
            bool n = layout.IsWater(x, y + 1);
            bool e = layout.IsWater(x + 1, y);
            bool s = layout.IsWater(x, y - 1);
            bool w = layout.IsWater(x - 1, y);
            if (n) mask |= North;
            if (e) mask |= East;
            if (s) mask |= South;
            if (w) mask |= West;
            if (n && e && layout.IsWater(x + 1, y + 1)) mask |= NorthEast;
            if (s && e && layout.IsWater(x + 1, y - 1)) mask |= SouthEast;
            if (s && w && layout.IsWater(x - 1, y - 1)) mask |= SouthWest;
            if (n && w && layout.IsWater(x - 1, y + 1)) mask |= NorthWest;
            return (byte)mask;
        }

        public static byte CanonicalWaterMask(int mask)
        {
            bool n = (mask & North) != 0;
            bool e = (mask & East) != 0;
            bool s = (mask & South) != 0;
            bool w = (mask & West) != 0;
            if (!(n && e)) mask &= ~NorthEast;
            if (!(s && e)) mask &= ~SouthEast;
            if (!(s && w)) mask &= ~SouthWest;
            if (!(n && w)) mask &= ~NorthWest;
            return (byte)mask;
        }
    }
}