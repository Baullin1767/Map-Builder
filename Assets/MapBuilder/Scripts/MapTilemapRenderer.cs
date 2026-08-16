using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MapBuilder
{
    public sealed class MapTilemapRenderer : MonoBehaviour
    {
        [SerializeField] private Tilemap groundTilemap;
        [SerializeField] private Tilemap waterTilemap;
        [SerializeField] private Tilemap shoreTilemap;
        [SerializeField] private Tilemap roadTilemap;
        [SerializeField] private MapTileCatalog catalog;
        private readonly Dictionary<string, Tile> tileCache = new Dictionary<string, Tile>();

        public Tilemap GroundTilemap { get { return groundTilemap; } }
        public Tilemap WaterTilemap { get { return waterTilemap; } }
        public Tilemap ShoreTilemap { get { return shoreTilemap; } }
        public Tilemap RoadTilemap { get { return roadTilemap; } }
        public MapTileCatalog Catalog { get { return catalog; } }

        public void Configure(
            Tilemap ground,
            Tilemap water,
            Tilemap shores,
            Tilemap roads,
            MapTileCatalog tileCatalog)
        {
            groundTilemap = ground;
            waterTilemap = water;
            shoreTilemap = shores;
            roadTilemap = roads;
            catalog = tileCatalog;
        }

        public bool Render(MapLayout layout)
        {
            EnsureShoreTilemap();
            if (layout == null || groundTilemap == null || waterTilemap == null ||
                shoreTilemap == null || roadTilemap == null || catalog == null)
            {
                Debug.LogError("MapTilemapRenderer is not fully configured.", this);
                return false;
            }

            Clear();
            int count = layout.CellCount;
            TileBase[] groundTiles = new TileBase[count];
            TileBase[] waterTiles = new TileBase[count];
            TileBase[] shoreTiles = new TileBase[count];
            TileBase[] roadTiles = new TileBase[count];

            for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                int index = layout.Index(x, y);
                groundTiles[index] = TileFor(
                    catalog.GetGrassSprite(layout.GrassVariants[index]));

                if (layout.Water[index])
                {
                    waterTiles[index] = TileFor(catalog.BaseWaterSprite);
                }
                else if (HasWaterNeighbor(layout, x, y))
                {
                    // Water continues beneath the transparent portion of the
                    // shoreline sprite, so the new 16x16 coast tiles compose cleanly.
                    waterTiles[index] = TileFor(catalog.BaseWaterSprite);
                    int rotationQuarterTurns;
                    Sprite shoreSprite = catalog.GetShoreVisual(
                        LandMask(layout, x, y), layout.WaterSeed, x, y,
                        out rotationQuarterTurns);
                    shoreTiles[index] = TileFor(shoreSprite, rotationQuarterTurns);
                }

                if (layout.Roads[index])
                {
                    int rotationQuarterTurns;
                    Sprite roadSprite = catalog.GetRoadVisual(
                        layout.RoadMasks[index], layout.RoadSeed, x, y,
                        out rotationQuarterTurns);
                    roadTiles[index] = TileFor(roadSprite, rotationQuarterTurns);
                }
            }

            BoundsInt bounds = new BoundsInt(0, 0, 0, layout.Width, layout.Height, 1);
            groundTilemap.SetTilesBlock(bounds, groundTiles);
            waterTilemap.SetTilesBlock(bounds, waterTiles);
            shoreTilemap.SetTilesBlock(bounds, shoreTiles);
            roadTilemap.SetTilesBlock(bounds, roadTiles);
            groundTilemap.CompressBounds();
            waterTilemap.CompressBounds();
            shoreTilemap.CompressBounds();
            roadTilemap.CompressBounds();
            return true;
        }

        private static bool HasWaterNeighbor(MapLayout layout, int x, int y)
        {
            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oy == 0) continue;
                if (layout.IsWater(x + ox, y + oy)) return true;
            }
            return false;
        }

        private static int LandMask(MapLayout layout, int x, int y)
        {
            bool n = IsLand(layout, x, y + 1);
            bool e = IsLand(layout, x + 1, y);
            bool s = IsLand(layout, x, y - 1);
            bool w = IsLand(layout, x - 1, y);
            int mask = 0;
            if (n) mask |= MapTopology.North;
            if (e) mask |= MapTopology.East;
            if (s) mask |= MapTopology.South;
            if (w) mask |= MapTopology.West;
            if (n && e && IsLand(layout, x + 1, y + 1)) mask |= MapTopology.NorthEast;
            if (s && e && IsLand(layout, x + 1, y - 1)) mask |= MapTopology.SouthEast;
            if (s && w && IsLand(layout, x - 1, y - 1)) mask |= MapTopology.SouthWest;
            if (n && w && IsLand(layout, x - 1, y + 1)) mask |= MapTopology.NorthWest;
            return MapTopology.CanonicalWaterMask(mask);
        }

        private static bool IsLand(MapLayout layout, int x, int y)
        {
            return layout.InBounds(x, y) && !layout.Water[layout.Index(x, y)];
        }

        private void EnsureShoreTilemap()
        {
            if (shoreTilemap != null || waterTilemap == null) return;
            Transform parent = waterTilemap.transform.parent;
            if (parent == null) return;

            Transform existing = parent.Find("Shore");
            if (existing != null) shoreTilemap = existing.GetComponent<Tilemap>();
            if (shoreTilemap != null) return;

            GameObject layer = new GameObject("Shore");
            layer.transform.SetParent(parent, false);
            shoreTilemap = layer.AddComponent<Tilemap>();
            TilemapRenderer renderer = layer.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = 15;
            renderer.mode = TilemapRenderer.Mode.Chunk;
        }

        public void Clear()
        {
            if (groundTilemap != null) groundTilemap.ClearAllTiles();
            if (waterTilemap != null) waterTilemap.ClearAllTiles();
            if (shoreTilemap != null) shoreTilemap.ClearAllTiles();
            if (roadTilemap != null) roadTilemap.ClearAllTiles();
        }

        private TileBase TileFor(Sprite sprite, int rotationQuarterTurns = 0)
        {
            if (sprite == null) return null;
            rotationQuarterTurns = ((rotationQuarterTurns % 4) + 4) % 4;
            string key = sprite.GetEntityId() + ":" + rotationQuarterTurns;
            Tile tile;
            if (tileCache.TryGetValue(key, out tile)) return tile;
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = "RuntimeTile_" + sprite.name + "_r" + rotationQuarterTurns;
            tile.sprite = sprite;
            tile.transform = Matrix4x4.Rotate(
                Quaternion.Euler(0f, 0f, -90f * rotationQuarterTurns));
            tile.colliderType = Tile.ColliderType.None;
            tile.hideFlags = HideFlags.DontSave;
            tileCache.Add(key, tile);
            return tile;
        }

        private void OnDestroy()
        {
            foreach (Tile tile in tileCache.Values)
            {
                if (tile == null) continue;
                if (Application.isPlaying) Destroy(tile);
                else DestroyImmediate(tile);
            }
            tileCache.Clear();
        }
    }
}
