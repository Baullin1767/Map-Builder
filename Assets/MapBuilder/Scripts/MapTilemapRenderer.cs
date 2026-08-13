using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace MapBuilder
{
    public sealed class MapTilemapRenderer : MonoBehaviour
    {
        [SerializeField] private Tilemap groundTilemap;
        [SerializeField] private Tilemap waterTilemap;
        [SerializeField] private Tilemap roadTilemap;
        [SerializeField] private MapTileCatalog catalog;
        private readonly Dictionary<string, Tile> tileCache = new Dictionary<string, Tile>();

        public Tilemap GroundTilemap { get { return groundTilemap; } }
        public Tilemap WaterTilemap { get { return waterTilemap; } }
        public Tilemap RoadTilemap { get { return roadTilemap; } }
        public MapTileCatalog Catalog { get { return catalog; } }

        public void Configure(Tilemap ground, Tilemap water, Tilemap roads, MapTileCatalog tileCatalog)
        {
            groundTilemap = ground;
            waterTilemap = water;
            roadTilemap = roads;
            catalog = tileCatalog;
        }

public bool Render(MapLayout layout)
        {
            if (layout == null || groundTilemap == null || waterTilemap == null ||
                roadTilemap == null || catalog == null)
            {
                Debug.LogError("MapTilemapRenderer is not fully configured.", this);
                return false;
            }

            Clear();
            int count = layout.CellCount;
            TileBase[] groundTiles = new TileBase[count];
            TileBase[] waterTiles = new TileBase[count];
            TileBase[] roadTiles = new TileBase[count];

            for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                int index = layout.Index(x, y);
                groundTiles[index] = TileFor(catalog.GetGrassSprite(layout.GrassVariants[index]));
                if (layout.Water[index])
                {
                    int rotationQuarterTurns;
                    Sprite waterSprite = catalog.GetWaterVisual(
                        layout.WaterMasks[index], layout.WaterSeed, x, y,
                        out rotationQuarterTurns);
                    waterTiles[index] = TileFor(waterSprite, rotationQuarterTurns);
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
            roadTilemap.SetTilesBlock(bounds, roadTiles);
            groundTilemap.CompressBounds();
            waterTilemap.CompressBounds();
            roadTilemap.CompressBounds();
            return true;
        }

        public void Clear()
        {
            if (groundTilemap != null) groundTilemap.ClearAllTiles();
            if (waterTilemap != null) waterTilemap.ClearAllTiles();
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
            tile.transform = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, -90f * rotationQuarterTurns));
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