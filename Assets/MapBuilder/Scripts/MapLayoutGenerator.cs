using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapBuilder
{
    public sealed class MapLayoutGenerator
    {
        private readonly MapGenerationSettings settings;
        private static readonly Vector2Int[] Cardinal =
        {
            Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
        };

        public MapLayoutGenerator(MapGenerationSettings settings)
        {
            this.settings = (settings ?? MapGenerationSettings.Prototype64()).CopyValidated();
        }

        public MapLayout Generate(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                throw new ArgumentException("Map hash must be non-empty.", "hash");

            MapLayout layout = new MapLayout(settings.width, settings.height, hash);
            FillGrass(layout);
            GenerateWater(layout);
            GenerateRoads(layout);
            BuildMasks(layout);
            return layout;
        }

        private static void FillGrass(MapLayout layout)
        {
            for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
                layout.GrassVariants[layout.Index(x, y)] =
                    (byte)(SeedUtility.CellHash(layout.GrassSeed, x, y) % 64u);
        }

        private void GenerateWater(MapLayout layout)
        {
            DeterministicRng rng = new DeterministicRng(layout.WaterSeed, 11UL);
            int sideA = rng.NextInt(4);
            int sideB = (sideA + 2) % 4;
            Vector2Int start = BoundaryPoint(sideA, layout.Width, layout.Height, ref rng, 6);
            Vector2Int end = BoundaryPoint(sideB, layout.Width, layout.Height, ref rng, 6);

            List<Vector2Int> river = FindPath(
                layout, start, end, false, layout.WaterSeed, null, false);
            if (river.Count == 0)
                river = StraightFallback(start, end);

            for (int i = 0; i < river.Count; i++)
            {
                Vector2Int p = river[i];
                SetWater(layout, p.x, p.y);
                float widthRoll = SeedUtility.Cell01(layout.WaterSeed ^ 0xA11CEUL, p.x, p.y);
                if (widthRoll < 0.72f)
                {
                    Vector2Int direction = i + 1 < river.Count
                        ? river[i + 1] - p
                        : p - river[Mathf.Max(0, i - 1)];
                    Vector2Int normal = new Vector2Int(-direction.y, direction.x);
                    if (normal == Vector2Int.zero) normal = Vector2Int.right;
                    SetWater(layout, p.x + normal.x, p.y + normal.y);
                    if (widthRoll < 0.26f)
                        SetWater(layout, p.x - normal.x, p.y - normal.y);
                }
            }

            int lakeCount = rng.Range(settings.minLakes, settings.maxLakes + 1);
            for (int lake = 0; lake < lakeCount; lake++)
            {
                int baseIndex = ((lake + 1) * river.Count) / (lakeCount + 1);
                int jitter = Mathf.Max(1, river.Count / 12);
                int index = Mathf.Clamp(baseIndex + rng.Range(-jitter, jitter + 1), 0, river.Count - 1);
                Vector2Int center = river[index];
                int radiusX = rng.Range(3, 7);
                int radiusY = rng.Range(3, 7);
                CarveLake(layout, center, radiusX, radiusY,
                    layout.WaterSeed ^ (ulong)(0x1000 + lake * 977));
            }

            ExpandWaterByOneCell(layout);
            RemoveWaterTips(layout.Water, layout.Width, layout.Height);
        }

        private static void ExpandWaterByOneCell(MapLayout layout)
        {
            bool[] source = (bool[])layout.Water.Clone();
            for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                if (!source[layout.Index(x, y)]) continue;
                SetWater(layout, x, y);
                SetWater(layout, x + 1, y);
                SetWater(layout, x - 1, y);
                SetWater(layout, x, y + 1);
                SetWater(layout, x, y - 1);
            }
        }

        private static void SetWater(MapLayout layout, int x, int y)
        {
            if (layout.InBounds(x, y))
                layout.Water[layout.Index(x, y)] = true;
        }

        private static void CarveLake(
            MapLayout layout, Vector2Int center, int radiusX, int radiusY, ulong seed)
        {
            for (int y = center.y - radiusY - 1; y <= center.y + radiusY + 1; y++)
            for (int x = center.x - radiusX - 1; x <= center.x + radiusX + 1; x++)
            {
                if (!layout.InBounds(x, y)) continue;
                float nx = (x - center.x) / (float)radiusX;
                float ny = (y - center.y) / (float)radiusY;
                float distance = nx * nx + ny * ny;
                float edge = 0.78f + SeedUtility.Cell01(seed, x, y) * 0.42f;
                if (distance <= edge)
                    layout.Water[layout.Index(x, y)] = true;
            }
        }

        private void GenerateRoads(MapLayout layout)
        {
            int largestComponent;
            int[] components = BuildLandComponents(layout, out largestComponent);
            if (largestComponent < 0) return;

            DeterministicRng rng = new DeterministicRng(layout.RoadSeed, 29UL);
            List<Vector2Int> boundary = new List<Vector2Int>();
            for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                bool edge = x == 0 || y == 0 || x == layout.Width - 1 || y == layout.Height - 1;
                if (edge && components[layout.Index(x, y)] == largestComponent)
                    boundary.Add(new Vector2Int(x, y));
            }

            List<Vector2Int> nodes = new List<Vector2Int>();
            PickSpaced(boundary, nodes, settings.roadGates, 8, ref rng);

            List<Vector2Int> interior = new List<Vector2Int>();
            for (int y = 6; y < layout.Height - 6; y++)
            for (int x = 6; x < layout.Width - 6; x++)
            {
                int index = layout.Index(x, y);
                if (components[index] != largestComponent || AdjacentToWater(layout, x, y, 2))
                    continue;
                interior.Add(new Vector2Int(x, y));
            }
            PickSpaced(interior, nodes, settings.roadPoints, 10, ref rng);
            if (nodes.Count < 2) return;

            List<Edge> mst = BuildMinimumSpanningTree(nodes);
            HashSet<long> usedPairs = new HashSet<long>();
            for (int i = 0; i < mst.Count; i++)
            {
                ConnectRoad(layout, nodes[mst[i].a], nodes[mst[i].b], components, largestComponent);
                usedPairs.Add(PairKey(mst[i].a, mst[i].b));
            }

            Edge? extra = FindShortestExtraEdge(nodes, usedPairs);
            if (extra.HasValue)
                ConnectRoad(layout, nodes[extra.Value.a], nodes[extra.Value.b], components, largestComponent);

            for (int i = 0; i < nodes.Count; i++)
                layout.Roads[layout.Index(nodes[i].x, nodes[i].y)] = true;
        }

        private static void PickSpaced(
            List<Vector2Int> source, List<Vector2Int> destination,
            int requested, int minDistance, ref DeterministicRng rng)
        {
            if (source.Count == 0) return;
            int attempts = Mathf.Max(32, source.Count * 2);
            while (requested > 0 && attempts-- > 0)
            {
                Vector2Int candidate = source[rng.NextInt(source.Count)];
                bool valid = true;
                for (int i = 0; i < destination.Count; i++)
                {
                    if (Manhattan(candidate, destination[i]) < minDistance)
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid) continue;
                destination.Add(candidate);
                requested--;
            }
        }

        private static int[] BuildLandComponents(MapLayout layout, out int largestComponent)
        {
            int[] components = new int[layout.CellCount];
            for (int i = 0; i < components.Length; i++) components[i] = -1;
            largestComponent = -1;
            int largestSize = 0;
            int component = 0;
            Queue<Vector2Int> queue = new Queue<Vector2Int>();

            for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                int index = layout.Index(x, y);
                if (layout.Water[index] || components[index] >= 0) continue;

                int size = 0;
                components[index] = component;
                queue.Enqueue(new Vector2Int(x, y));
                while (queue.Count > 0)
                {
                    Vector2Int p = queue.Dequeue();
                    size++;
                    for (int d = 0; d < Cardinal.Length; d++)
                    {
                        Vector2Int n = p + Cardinal[d];
                        if (!layout.InBounds(n.x, n.y)) continue;
                        int ni = layout.Index(n.x, n.y);
                        if (layout.Water[ni] || components[ni] >= 0) continue;
                        components[ni] = component;
                        queue.Enqueue(n);
                    }
                }

                if (size > largestSize)
                {
                    largestSize = size;
                    largestComponent = component;
                }
                component++;
            }
            return components;
        }

        private static bool AdjacentToWater(MapLayout layout, int x, int y, int radius)
        {
            for (int oy = -radius; oy <= radius; oy++)
            for (int ox = -radius; ox <= radius; ox++)
                if (layout.IsWater(x + ox, y + oy)) return true;
            return false;
        }

        private void ConnectRoad(
            MapLayout layout, Vector2Int start, Vector2Int end,
            int[] components, int allowedComponent)
        {
            List<Vector2Int> path = FindPath(
                layout, start, end, true, layout.RoadSeed,
                components, true, allowedComponent);
            for (int i = 0; i < path.Count; i++)
            {
                int index = layout.Index(path[i].x, path[i].y);
                if (!layout.Water[index]) layout.Roads[index] = true;
            }
        }

        private static List<Vector2Int> FindPath(
            MapLayout layout, Vector2Int start, Vector2Int goal,
            bool blockWater, ulong seed, int[] components,
            bool roadCosts, int allowedComponent = -1)
        {
            int count = layout.CellCount;
            float[] scores = new float[count];
            int[] previous = new int[count];
            bool[] closed = new bool[count];
            List<int> open = new List<int>();
            for (int i = 0; i < count; i++)
            {
                scores[i] = float.PositiveInfinity;
                previous[i] = -1;
            }

            int startIndex = layout.Index(start.x, start.y);
            int goalIndex = layout.Index(goal.x, goal.y);
            scores[startIndex] = 0f;
            open.Add(startIndex);

            while (open.Count > 0)
            {
                int bestListIndex = 0;
                float bestPriority = float.PositiveInfinity;
                for (int i = 0; i < open.Count; i++)
                {
                    int index = open[i];
                    int x = index % layout.Width;
                    int y = index / layout.Width;
                    float priority = scores[index] + Manhattan(new Vector2Int(x, y), goal) * 0.72f;
                    if (priority < bestPriority)
                    {
                        bestPriority = priority;
                        bestListIndex = i;
                    }
                }

                int current = open[bestListIndex];
                open.RemoveAt(bestListIndex);
                if (closed[current]) continue;
                if (current == goalIndex) break;
                closed[current] = true;

                int cx = current % layout.Width;
                int cy = current / layout.Width;
                for (int d = 0; d < Cardinal.Length; d++)
                {
                    int nx = cx + Cardinal[d].x;
                    int ny = cy + Cardinal[d].y;
                    if (!layout.InBounds(nx, ny)) continue;
                    int next = layout.Index(nx, ny);
                    if (closed[next]) continue;
                    if (blockWater && layout.Water[next]) continue;
                    if (components != null && allowedComponent >= 0 && components[next] != allowedComponent)
                        continue;

                    float step;
                    if (roadCosts)
                    {
                        step = layout.Roads[next] ? 0.28f : 1f;
                        if (AdjacentToWater(layout, nx, ny, 1)) step += 3.5f;
                        bool boundary = nx == 0 || ny == 0 || nx == layout.Width - 1 || ny == layout.Height - 1;
                        if (boundary && next != startIndex && next != goalIndex) step += 6f;
                    }
                    else
                    {
                        step = 0.8f + SeedUtility.Cell01(seed, nx, ny) * 2.2f;
                    }

                    int oldPrevious = previous[current];
                    if (oldPrevious >= 0)
                    {
                        int px = oldPrevious % layout.Width;
                        int py = oldPrevious / layout.Width;
                        int oldDx = cx - px;
                        int oldDy = cy - py;
                        if (oldDx != Cardinal[d].x || oldDy != Cardinal[d].y)
                            step += roadCosts ? 0.32f : 0.48f;
                    }

                    float candidate = scores[current] + step;
                    if (candidate >= scores[next]) continue;
                    scores[next] = candidate;
                    previous[next] = current;
                    open.Add(next);
                }
            }

            if (startIndex != goalIndex && previous[goalIndex] < 0)
                return new List<Vector2Int>();

            List<Vector2Int> result = new List<Vector2Int>();
            int cursor = goalIndex;
            result.Add(new Vector2Int(cursor % layout.Width, cursor / layout.Width));
            while (cursor != startIndex)
            {
                cursor = previous[cursor];
                if (cursor < 0) return new List<Vector2Int>();
                result.Add(new Vector2Int(cursor % layout.Width, cursor / layout.Width));
            }
            result.Reverse();
            return result;
        }

        private static List<Vector2Int> StraightFallback(Vector2Int start, Vector2Int end)
        {
            List<Vector2Int> result = new List<Vector2Int>();
            Vector2Int p = start;
            result.Add(p);
            while (p.x != end.x)
            {
                p.x += Math.Sign(end.x - p.x);
                result.Add(p);
            }
            while (p.y != end.y)
            {
                p.y += Math.Sign(end.y - p.y);
                result.Add(p);
            }
            return result;
        }

        private static Vector2Int BoundaryPoint(
            int side, int width, int height, ref DeterministicRng rng, int margin)
        {
            int safeX = Mathf.Max(1, width - margin * 2);
            int safeY = Mathf.Max(1, height - margin * 2);
            switch (side)
            {
                case 0: return new Vector2Int(margin + rng.NextInt(safeX), height - 1);
                case 1: return new Vector2Int(width - 1, margin + rng.NextInt(safeY));
                case 2: return new Vector2Int(margin + rng.NextInt(safeX), 0);
                default: return new Vector2Int(0, margin + rng.NextInt(safeY));
            }
        }

        private static void RemoveWaterTips(bool[] cells, int width, int height)
        {
            bool changed;
            do
            {
                changed = false;
                bool[] remove = new bool[cells.Length];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (!cells[index]) continue;
                    int neighbors = 0;
                    if (y + 1 < height && cells[index + width]) neighbors++;
                    if (x + 1 < width && cells[index + 1]) neighbors++;
                    if (y > 0 && cells[index - width]) neighbors++;
                    if (x > 0 && cells[index - 1]) neighbors++;
                    if (neighbors > 1) continue;
                    remove[index] = true;
                    changed = true;
                }
                for (int i = 0; i < cells.Length; i++)
                    if (remove[i]) cells[i] = false;
            }
            while (changed);
        }

        private static void BuildMasks(MapLayout layout)
        {
            for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                int index = layout.Index(x, y);
                if (layout.Water[index])
                    layout.WaterMasks[index] = MapTopology.WaterMask(layout, x, y);
                if (layout.Roads[index])
                    layout.RoadMasks[index] = MapTopology.RoadMask(layout, x, y);
            }
        }

        private struct Edge
        {
            public int a;
            public int b;
            public int distance;
        }

        private static List<Edge> BuildMinimumSpanningTree(List<Vector2Int> nodes)
        {
            List<Edge> result = new List<Edge>();
            bool[] connected = new bool[nodes.Count];
            connected[0] = true;
            int connectedCount = 1;
            while (connectedCount < nodes.Count)
            {
                Edge best = new Edge { a = -1, b = -1, distance = int.MaxValue };
                for (int a = 0; a < nodes.Count; a++)
                {
                    if (!connected[a]) continue;
                    for (int b = 0; b < nodes.Count; b++)
                    {
                        if (connected[b]) continue;
                        int distance = Manhattan(nodes[a], nodes[b]);
                        if (distance < best.distance)
                            best = new Edge { a = a, b = b, distance = distance };
                    }
                }
                if (best.a < 0) break;
                result.Add(best);
                connected[best.b] = true;
                connectedCount++;
            }
            return result;
        }

        private static Edge? FindShortestExtraEdge(List<Vector2Int> nodes, HashSet<long> usedPairs)
        {
            Edge best = new Edge { a = -1, b = -1, distance = int.MaxValue };
            for (int a = 0; a < nodes.Count; a++)
            for (int b = a + 1; b < nodes.Count; b++)
            {
                if (usedPairs.Contains(PairKey(a, b))) continue;
                int distance = Manhattan(nodes[a], nodes[b]);
                if (distance < best.distance)
                    best = new Edge { a = a, b = b, distance = distance };
            }
            return best.a >= 0 ? (Edge?)best : null;
        }

        private static long PairKey(int a, int b)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            return ((long)min << 32) | (uint)max;
        }

        private static int Manhattan(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }
    }
}
