using System;
using UnityEngine;

namespace MapBuilder
{
    public sealed class MapGenerationController : MonoBehaviour, IMapHashConsumer
    {
        [SerializeField] private MapTilemapRenderer tilemapRenderer;
        [SerializeField] private MapGenerationSettings settings = new MapGenerationSettings();
        [SerializeField] private bool generateOnStart = true;
        [SerializeField] private string debugHash = "prototype-seed-001";

        public MapLayout LastLayout { get; private set; }
        public string LastHash { get; private set; }
        public MapGenerationSettings Settings { get { return settings; } }

        private void Start()
        {
            if (generateOnStart) GenerateFromHash(debugHash);
        }

        public void Configure(
            MapTilemapRenderer renderer, MapGenerationSettings generationSettings,
            string initialDebugHash, bool generateAtStart)
        {
            tilemapRenderer = renderer;
            settings = generationSettings ?? MapGenerationSettings.Prototype64();
            debugHash = initialDebugHash;
            generateOnStart = generateAtStart;
        }

        public MapLayout BuildLayout(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                throw new ArgumentException("Map hash must be non-empty.", "hash");
            return new MapLayoutGenerator(settings).Generate(hash);
        }

        public bool GenerateFromHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                Debug.LogError("Map hash must be non-empty. Existing map was kept.", this);
                return false;
            }
            if (tilemapRenderer == null)
            {
                Debug.LogError("MapGenerationController has no Tilemap renderer.", this);
                return false;
            }

            MapLayout layout;
            try { layout = BuildLayout(hash); }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }

            if (!tilemapRenderer.Render(layout)) return false;
            LastLayout = layout;
            LastHash = hash;
            return true;
        }

        [ContextMenu("Generate Debug Map")]
        public void GenerateDebugMap() { GenerateFromHash(debugHash); }

        [ContextMenu("Generate Random Map")]
        public void GenerateRandomMap()
        {
            debugHash = Guid.NewGuid().ToString("N");
            GenerateFromHash(debugHash);
        }
    }
}
