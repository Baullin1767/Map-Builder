using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapBuilder
{
    public sealed class MapLayoutGenerator
    {
        private readonly MapGenerationSettings settings;
        private enum LakePattern
        {
            Cove,
            Chain,
            Bend,
            Branch
        }

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
            FillOcean(layout);
            bool[] island = GenerateIsland(layout);
            GenerateInlandWater(layout, island);
            GenerateRoads(layout, island);
            RemoveShortRoadFragments(layout, 2);
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

        private static void FillOcean(MapLayout layout)
        {
            for (int i = 0; i < layout.Water.Length; i++)
                layout.Water[i] = true;
        }

        private static bool[] GenerateIsland(MapLayout layout)
        {
            bool[] island = new bool[layout.CellCount];
            DeterministicRng rng = new DeterministicRng(layout.WaterSeed, 11UL);

            int marginX = Mathf.Max(2, Mathf.RoundToInt(layout.Width * 0.06f));
            int marginY = Mathf.Max(2, Mathf.RoundToInt(layout.Height * 0.06f));
            float radiusX = Mathf.Max(3f, (layout.Width - marginX * 2) * 0.5f);
            float radiusY = Mathf.Max(3f, (layout.Height - marginY * 2) * 0.5f);
            float centerX = (layout.Width - 1) * 0.5f + rng.Range(-1, 2);
            float centerY = (layout.Height - 1) * 0.5f + rng.Range(-1, 2);
            float phaseA = rng.NextFloat() * Mathf.PI * 2f;
            float phaseB = rng.NextFloat() * Mathf.PI * 2f;
            float phaseC = rng.NextFloat() * Mathf.PI * 2f;
            const float exponent = 3f;

            for (int y = 1; y < layout.Height - 1; y++)
            for (int x = 1; x < layout.Width - 1; x++)
            {
                float nx = (x - centerX) / radiusX;
                float ny = (y - centerY) / radiusY;
                float angle = Mathf.Atan2(ny, nx);
                float edge = 1f
                    + Mathf.Sin(angle * 3f + phaseA) * 0.055f
                    + Mathf.Sin(angle * 5f + phaseB) * 0.032f
                    + Mathf.Cos(angle * 2f + phaseC) * 0.018f;
                float shape = Mathf.Pow(Mathf.Abs(nx), exponent)
                    + Mathf.Pow(Mathf.Abs(ny), exponent);
                if (shape > Mathf.Pow(edge, exponent)) continue;

                int index = layout.Index(x, y);
                island[index] = true;
                layout.Water[index] = false;
            }

            return island;
        }

        private void GenerateInlandWater(MapLayout layout, bool[] island)
        {
            DeterministicRng rng = new DeterministicRng(layout.WaterSeed, 37UL);
            List<Vector2Int> lakeCandidates = new List<Vector2Int>();
            int minimumMapSide = Mathf.Min(layout.Width, layout.Height);
            int coastClearance = Mathf.Clamp(minimumMapSide / 10, 3, 6);
            for (int y = 3; y < layout.Height - 3; y++)
            for (int x = 3; x < layout.Width - 3; x++)
            {
                int index = layout.Index(x, y);
                if (!island[index] || AdjacentToWater(layout, x, y, coastClearance)) continue;
                lakeCandidates.Add(new Vector2Int(x, y));
            }

            int lakeCount = rng.Range(settings.minLakes, settings.maxLakes + 1);
            List<Vector2Int> lakeCenters = new List<Vector2Int>();
            PickSpaced(lakeCandidates, lakeCenters, lakeCount,
                Mathf.Max(6, minimumMapSide / 7), ref rng);
            if (lakeCenters.Count < lakeCount)
            {
                PickSpaced(lakeCandidates, lakeCenters, lakeCount - lakeCenters.Count,
                    Mathf.Max(4, minimumMapSide / 10), ref rng);
            }

            List<Vector2Int> coast = BuildIslandCoast(layout, island);
            List<Vector2Int> riverMouths = new List<Vector2Int>();
            LakePattern[] lakePatterns = BuildShuffledLakePatterns(ref rng);
            int guaranteedRivers = Mathf.Min(2, lakeCenters.Count);
            for (int lake = 0; lake < lakeCenters.Count; lake++)
            {
                Vector2Int center = lakeCenters[lake];

                // The first two lakes always feed separate rivers to the sea.
                // Later lakes can remain enclosed or produce extra streams.
                if (lake < guaranteedRivers || rng.NextFloat() < 0.45f)
                {
                    List<Vector2Int> availableCoast = FilterFarFromPoints(
                        coast, riverMouths, Mathf.Max(6, minimumMapSide / 5));
                    Vector2Int mouth;
                    if (TryPickDistant(availableCoast, center,
                        minimumMapSide / 4, ref rng, out mouth))
                    {
                        List<Vector2Int> stream = FindPath(
                            layout, center, mouth, false,
                            layout.WaterSeed ^ (ulong)(0x51EA + lake * 131),
                            island, false, null);
                        CarveStream(layout, stream, layout.WaterSeed ^ (ulong)(0xA11CE + lake));
                        riverMouths.Add(mouth);
                    }
                }

                int radiusX = rng.Range(2, Mathf.Max(3, layout.Width / 12) + 1);
                int radiusY = rng.Range(2, Mathf.Max(3, layout.Height / 12) + 1);
                CarveLake(layout, island, center, radiusX, radiusY,
                    lakePatterns[lake % lakePatterns.Length],
                    layout.WaterSeed ^ (ulong)(0x1000 + lake * 977));
            }
        }

        private static LakePattern[] BuildShuffledLakePatterns(ref DeterministicRng rng)
        {
            LakePattern[] patterns =
            {
                LakePattern.Cove,
                LakePattern.Chain,
                LakePattern.Bend,
                LakePattern.Branch
            };
            for (int i = patterns.Length - 1; i > 0; i--)
            {
                int swap = rng.Range(0, i + 1);
                LakePattern value = patterns[i];
                patterns[i] = patterns[swap];
                patterns[swap] = value;
            }
            return patterns;
        }

        private static List<Vector2Int> FilterFarFromPoints(
            List<Vector2Int> source, List<Vector2Int> points, int minimumDistance)
        {
            if (points.Count == 0) return new List<Vector2Int>(source);
            List<Vector2Int> result = new List<Vector2Int>();
            for (int i = 0; i < source.Count; i++)
            {
                bool valid = true;
                for (int point = 0; point < points.Count; point++)
                {
                    if (Manhattan(source[i], points[point]) >= minimumDistance) continue;
                    valid = false;
                    break;
                }
                if (valid) result.Add(source[i]);
            }
            return result;
        }

        private void GenerateRoads(MapLayout layout, bool[] island)
        {
            DeterministicRng rng = new DeterministicRng(layout.RoadSeed, 29UL);
            List<Vector2Int> coast = BuildIslandCoast(layout, island);
            int requestedRoutes = Mathf.Clamp(settings.roadGates, 2, 5);

            for (int route = 0; route < requestedRoutes; route++)
            {
                List<Vector2Int> availableCoast = FilterFarFromRoads(layout, coast, 3);
                if (availableCoast.Count < 2) break;

                Vector2Int start = availableCoast[rng.NextInt(availableCoast.Count)];
                Vector2Int end;
                int minimumSpan = Mathf.Max(8, Mathf.Min(layout.Width, layout.Height) / 2);
                if (!TryPickDistant(availableCoast, start, minimumSpan, ref rng, out end))
                    continue;

                List<Vector2Int> controlPoints = new List<Vector2Int> { start };
                int controlCount = Mathf.Clamp(settings.roadPoints, 2, 5);
                for (int point = 1; point < controlCount - 1; point++)
                {
                    float t = point / (float)(controlCount - 1);
                    Vector2 interpolated = Vector2.Lerp(start, end, t);
                    Vector2Int candidate;
                    if (TryPickControlPoint(
                        layout, island, interpolated, route, point, ref rng, out candidate))
                    {
                        controlPoints.Add(candidate);
                    }
                }
                controlPoints.Add(end);

                bool[] existingRoads = (bool[])layout.Roads.Clone();
                List<Vector2Int> completeRoute = new List<Vector2Int>();
                bool valid = true;
                for (int segment = 0; segment < controlPoints.Count - 1; segment++)
                {
                    List<Vector2Int> path = FindPath(
                        layout, controlPoints[segment], controlPoints[segment + 1], false,
                        layout.RoadSeed ^ (ulong)(route * 4099 + segment * 313 + 1),
                        island, true, existingRoads);
                    if (path.Count == 0)
                    {
                        valid = false;
                        break;
                    }
                    if (completeRoute.Count > 0) path.RemoveAt(0);
                    completeRoute.AddRange(path);
                }

                if (!valid)
                {
                    TryGenerateLocalRoad(layout, island, ref rng, route);
                    continue;
                }
                for (int i = 0; i < completeRoute.Count; i++)
                {
                    Vector2Int p = completeRoute[i];
                    int index = layout.Index(p.x, p.y);
                    // The planned line may cross a stream or lake, but there is
                    // deliberately no road tile on water: each bank gets an end.
                    if (!layout.Water[index]) layout.Roads[index] = true;
                }
            }
        }

        private static bool TryGenerateLocalRoad(
            MapLayout layout, bool[] island, ref DeterministicRng rng, int route)
        {
            List<Vector2Int> candidates = new List<Vector2Int>();
            for (int y = 2; y < layout.Height - 2; y++)
            for (int x = 2; x < layout.Width - 2; x++)
            {
                int index = layout.Index(x, y);
                if (!island[index] || layout.Water[index] ||
                    AdjacentToRoad(layout.Roads, layout.Width, layout.Height, x, y, 3))
                {
                    continue;
                }
                candidates.Add(new Vector2Int(x, y));
            }

            if (candidates.Count < 2) return false;
            bool[] existingRoads = (bool[])layout.Roads.Clone();
            int minimumSpan = Mathf.Max(7, Mathf.Min(layout.Width, layout.Height) / 5);
            for (int attempt = 0; attempt < 24; attempt++)
            {
                Vector2Int start = candidates[rng.NextInt(candidates.Count)];
                Vector2Int end;
                if (!TryPickDistant(candidates, start, minimumSpan, ref rng, out end))
                    continue;
                List<Vector2Int> path = FindPath(
                    layout, start, end, false,
                    layout.RoadSeed ^ (ulong)(0xFA11 + route * 997 + attempt),
                    island, true, existingRoads);
                if (path.Count == 0) continue;

                int placed = 0;
                for (int i = 0; i < path.Count; i++)
                {
                    Vector2Int p = path[i];
                    int index = layout.Index(p.x, p.y);
                    if (layout.Water[index]) continue;
                    layout.Roads[index] = true;
                    placed++;
                }
                if (placed >= 2) return true;
            }
            return false;
        }

        private static List<Vector2Int> BuildIslandCoast(MapLayout layout, bool[] island)
        {
            List<Vector2Int> result = new List<Vector2Int>();
            for (int y = 1; y < layout.Height - 1; y++)
            for (int x = 1; x < layout.Width - 1; x++)
            {
                int index = layout.Index(x, y);
                if (!island[index] || layout.Water[index]) continue;
                for (int d = 0; d < Cardinal.Length; d++)
                {
                    Vector2Int n = new Vector2Int(x, y) + Cardinal[d];
                    if (!island[layout.Index(n.x, n.y)])
                    {
                        result.Add(new Vector2Int(x, y));
                        break;
                    }
                }
            }
            return result;
        }

        private static List<Vector2Int> FilterFarFromRoads(
            MapLayout layout, List<Vector2Int> source, int radius)
        {
            List<Vector2Int> result = new List<Vector2Int>();
            for (int i = 0; i < source.Count; i++)
            {
                Vector2Int p = source[i];
                if (!AdjacentToRoad(layout.Roads, layout.Width, layout.Height, p.x, p.y, radius))
                    result.Add(p);
            }
            return result;
        }

        private static bool TryPickControlPoint(
            MapLayout layout, bool[] island, Vector2 center, int route, int point,
            ref DeterministicRng rng, out Vector2Int result)
        {
            int radius = Mathf.Max(3, Mathf.Min(layout.Width, layout.Height) / 9);
            Vector2 direction = new Vector2(-(route % 2 == 0 ? 1f : -1f), route % 3 - 1f).normalized;
            center += direction * radius * (point % 2 == 0 ? -0.65f : 0.65f);

            for (int attempt = 0; attempt < 48; attempt++)
            {
                int x = Mathf.RoundToInt(center.x) + rng.Range(-radius, radius + 1);
                int y = Mathf.RoundToInt(center.y) + rng.Range(-radius, radius + 1);
                if (!layout.InBounds(x, y)) continue;
                int index = layout.Index(x, y);
                if (!island[index] || layout.Water[index] ||
                    AdjacentToWater(layout, x, y, 2) ||
                    AdjacentToRoad(layout.Roads, layout.Width, layout.Height, x, y, 2))
                {
                    continue;
                }
                result = new Vector2Int(x, y);
                return true;
            }
            result = default(Vector2Int);
            return false;
        }

        private static void PickSpaced(
            List<Vector2Int> source, List<Vector2Int> destination,
            int requested, int minDistance, ref DeterministicRng rng)
        {
            if (source.Count == 0) return;
            int attempts = Mathf.Max(64, source.Count * 2);
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

        private static bool TryPickDistant(
            List<Vector2Int> source, Vector2Int origin, int minimumDistance,
            ref DeterministicRng rng, out Vector2Int result)
        {
            result = default(Vector2Int);
            int bestDistance = -1;
            int samples = Mathf.Min(96, source.Count);
            for (int i = 0; i < samples; i++)
            {
                Vector2Int candidate = source[rng.NextInt(source.Count)];
                int distance = Manhattan(origin, candidate);
                if (distance <= bestDistance) continue;
                bestDistance = distance;
                result = candidate;
            }
            return bestDistance >= minimumDistance;
        }

        private static List<Vector2Int> FindPath(
            MapLayout layout, Vector2Int start, Vector2Int goal,
            bool blockWater, ulong seed, bool[] allowedCells,
            bool roadCosts, bool[] forbiddenRoads)
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
                    float priority = scores[index]
                        + Manhattan(new Vector2Int(x, y), goal) * 0.78f;
                    if (priority >= bestPriority) continue;
                    bestPriority = priority;
                    bestListIndex = i;
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
                    if (closed[next] || (allowedCells != null && !allowedCells[next])) continue;
                    if (blockWater && layout.Water[next]) continue;
                    if (forbiddenRoads != null && next != goalIndex && next != startIndex &&
                        AdjacentToRoad(forbiddenRoads, layout.Width, layout.Height, nx, ny, 1))
                    {
                        continue;
                    }

                    float step = 0.85f + SeedUtility.Cell01(seed, nx, ny) * 0.45f;
                    if (roadCosts && layout.Water[next]) step += 0.12f;

                    int oldPrevious = previous[current];
                    if (oldPrevious >= 0)
                    {
                        int px = oldPrevious % layout.Width;
                        int py = oldPrevious / layout.Width;
                        if (cx - px != Cardinal[d].x || cy - py != Cardinal[d].y)
                            step += roadCosts ? 0.72f : 0.42f;
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

        private static void CarveStream(MapLayout layout, List<Vector2Int> path, ulong seed)
        {
            for (int i = 0; i < path.Count; i++)
            {
                Vector2Int p = path[i];
                SetWater(layout, p.x, p.y);
                if (i > 0 && i < path.Count - 1 &&
                    SeedUtility.Cell01(seed, p.x, p.y) < 0.18f)
                {
                    Vector2Int direction = path[i + 1] - path[i - 1];
                    Vector2Int normal = Mathf.Abs(direction.x) > Mathf.Abs(direction.y)
                        ? Vector2Int.up : Vector2Int.right;
                    SetWater(layout, p.x + normal.x, p.y + normal.y);
                }
            }
        }

        private static void CarveLake(
            MapLayout layout, bool[] island, Vector2Int center,
            int radiusX, int radiusY, LakePattern pattern, ulong seed)
        {
            int size = Mathf.Clamp((radiusX + radiusY) / 2, 2, 4);
            int rotation = (int)(seed & 3UL);
            bool mirror = ((seed >> 2) & 1UL) != 0UL;

            switch (pattern)
            {
                case LakePattern.Cove:
                    CarveCoveLake(layout, island, center, size, rotation, mirror);
                    break;
                case LakePattern.Chain:
                    CarveChainLake(layout, island, center, size, rotation, mirror);
                    break;
                case LakePattern.Bend:
                    CarveBendLake(layout, island, center, size, rotation, mirror);
                    break;
                default:
                    CarveBranchLake(layout, island, center, size, rotation, mirror);
                    break;
            }
        }

        private static void CarveCoveLake(
            MapLayout layout, bool[] island, Vector2Int center,
            int size, int rotation, bool mirror)
        {
            int halfWidth = Mathf.Max(2, size);
            int halfHeight = Mathf.Max(2, size - 1);
            CarveRoundedBlock(
                layout, island, center, Vector2Int.zero,
                halfWidth, halfHeight, rotation, mirror);

            Vector2Int bay = new Vector2Int(halfWidth + 1, halfHeight - 1);
            CarveRoundedBlock(
                layout, island, center, bay,
                Mathf.Max(1, halfWidth / 2), 1, rotation, mirror);
        }

        private static void CarveChainLake(
            MapLayout layout, bool[] island, Vector2Int center,
            int size, int rotation, bool mirror)
        {
            int spacing = Mathf.Max(2, size - 1);
            Vector2Int lower = new Vector2Int(0, -spacing);
            Vector2Int middle = Vector2Int.zero;
            Vector2Int upper = new Vector2Int(0, spacing);

            CarveRoundedBlock(
                layout, island, center, lower,
                Mathf.Max(1, size - 1), 1, rotation, mirror);
            CarveRoundedBlock(
                layout, island, center, middle,
                Mathf.Max(2, size), 1, rotation, mirror);
            CarveRoundedBlock(
                layout, island, center, upper,
                Mathf.Max(1, size - 2), 2, rotation, mirror);
            CarveOrthogonalConnection(
                layout, island, center, lower, upper, 1, rotation, mirror, true);
        }

        private static void CarveBendLake(
            MapLayout layout, bool[] island, Vector2Int center,
            int size, int rotation, bool mirror)
        {
            int arm = Mathf.Max(2, size);
            Vector2Int first = new Vector2Int(-arm, 0);
            Vector2Int second = new Vector2Int(0, arm);

            CarveRoundedBlock(
                layout, island, center, first, 2,
                Mathf.Max(1, size - 2), rotation, mirror);
            CarveRoundedBlock(
                layout, island, center, second,
                Mathf.Max(1, size - 2), 2, rotation, mirror);
            CarveOrthogonalConnection(
                layout, island, center, first, second, 1,
                rotation, mirror, true);
        }

        private static void CarveBranchLake(
            MapLayout layout, bool[] island, Vector2Int center,
            int size, int rotation, bool mirror)
        {
            int arm = Mathf.Max(2, size);
            Vector2Int left = new Vector2Int(-arm, 0);
            Vector2Int right = new Vector2Int(arm, 0);
            Vector2Int top = new Vector2Int(0, arm);

            CarveRoundedBlock(
                layout, island, center, Vector2Int.zero, 2, 2, rotation, mirror);
            CarveRoundedBlock(
                layout, island, center, left, 1,
                Mathf.Max(1, size - 2), rotation, mirror);
            CarveRoundedBlock(
                layout, island, center, right, 2, 1, rotation, mirror);
            CarveRoundedBlock(
                layout, island, center, top,
                Mathf.Max(1, size - 2), 1, rotation, mirror);
            CarveOrthogonalConnection(
                layout, island, center, left, right, 1,
                rotation, mirror, true);
            CarveOrthogonalConnection(
                layout, island, center, Vector2Int.zero, top, 1,
                rotation, mirror, false);
        }

        private static void CarveRoundedBlock(
            MapLayout layout, bool[] island, Vector2Int center,
            Vector2Int localCenter, int halfWidth, int halfHeight,
            int rotation, bool mirror)
        {
            halfWidth = Mathf.Max(1, halfWidth);
            halfHeight = Mathf.Max(1, halfHeight);
            for (int y = -halfHeight; y <= halfHeight; y++)
            for (int x = -halfWidth; x <= halfWidth; x++)
            {
                bool chamferedCorner = Mathf.Abs(x) == halfWidth &&
                    Mathf.Abs(y) == halfHeight;
                if (chamferedCorner) continue;
                SetLakeWater(
                    layout, island, center,
                    TransformLakeOffset(localCenter + new Vector2Int(x, y), rotation, mirror));
            }
        }

        private static void CarveOrthogonalConnection(
            MapLayout layout, bool[] island, Vector2Int center,
            Vector2Int start, Vector2Int end, int halfWidth,
            int rotation, bool mirror, bool horizontalFirst)
        {
            Vector2Int elbow = horizontalFirst
                ? new Vector2Int(end.x, start.y)
                : new Vector2Int(start.x, end.y);
            CarveLakeSegment(
                layout, island, center, start, elbow,
                halfWidth, rotation, mirror);
            CarveLakeSegment(
                layout, island, center, elbow, end,
                halfWidth, rotation, mirror);
        }

        private static void CarveLakeSegment(
            MapLayout layout, bool[] island, Vector2Int center,
            Vector2Int start, Vector2Int end, int halfWidth,
            int rotation, bool mirror)
        {
            int xStep = start.x <= end.x ? 1 : -1;
            for (int x = start.x; x != end.x + xStep; x += xStep)
            for (int offset = -halfWidth; offset <= halfWidth; offset++)
            {
                SetLakeWater(
                    layout, island, center,
                    TransformLakeOffset(
                        new Vector2Int(x, start.y + offset), rotation, mirror));
            }

            int yStep = start.y <= end.y ? 1 : -1;
            for (int y = start.y; y != end.y + yStep; y += yStep)
            for (int offset = -halfWidth; offset <= halfWidth; offset++)
            {
                SetLakeWater(
                    layout, island, center,
                    TransformLakeOffset(
                        new Vector2Int(end.x + offset, y), rotation, mirror));
            }
        }

        private static Vector2Int TransformLakeOffset(
            Vector2Int offset, int rotation, bool mirror)
        {
            if (mirror) offset.x = -offset.x;
            switch (rotation & 3)
            {
                case 1: return new Vector2Int(offset.y, -offset.x);
                case 2: return new Vector2Int(-offset.x, -offset.y);
                case 3: return new Vector2Int(-offset.y, offset.x);
                default: return offset;
            }
        }

        private static void SetLakeWater(
            MapLayout layout, bool[] island, Vector2Int center, Vector2Int offset)
        {
            int x = center.x + offset.x;
            int y = center.y + offset.y;
            if (!layout.InBounds(x, y)) return;
            int index = layout.Index(x, y);
            if (island[index]) layout.Water[index] = true;
        }

        private static void SetWater(MapLayout layout, int x, int y)
        {
            if (layout.InBounds(x, y)) layout.Water[layout.Index(x, y)] = true;
        }

        private static bool AdjacentToWater(MapLayout layout, int x, int y, int radius)
        {
            for (int oy = -radius; oy <= radius; oy++)
            for (int ox = -radius; ox <= radius; ox++)
                if (layout.IsWater(x + ox, y + oy)) return true;
            return false;
        }

        private static bool AdjacentToRoad(
            bool[] roads, int width, int height, int x, int y, int radius)
        {
            if (roads == null || width <= 0 || height <= 0) return false;
            for (int oy = -radius; oy <= radius; oy++)
            for (int ox = -radius; ox <= radius; ox++)
            {
                int nx = x + ox;
                int ny = y + oy;
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && roads[ny * width + nx])
                    return true;
            }
            return false;
        }

        private static void RemoveShortRoadFragments(MapLayout layout, int minimumSize)
        {
            bool[] visited = new bool[layout.CellCount];
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            List<int> component = new List<int>();
            for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                int start = layout.Index(x, y);
                if (!layout.Roads[start] || visited[start]) continue;
                component.Clear();
                visited[start] = true;
                queue.Enqueue(new Vector2Int(x, y));
                while (queue.Count > 0)
                {
                    Vector2Int p = queue.Dequeue();
                    int index = layout.Index(p.x, p.y);
                    component.Add(index);
                    for (int d = 0; d < Cardinal.Length; d++)
                    {
                        Vector2Int n = p + Cardinal[d];
                        if (!layout.InBounds(n.x, n.y)) continue;
                        int next = layout.Index(n.x, n.y);
                        if (!layout.Roads[next] || visited[next]) continue;
                        visited[next] = true;
                        queue.Enqueue(n);
                    }
                }
                if (component.Count >= minimumSize) continue;
                for (int i = 0; i < component.Count; i++) layout.Roads[component[i]] = false;
            }
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

        private static int Manhattan(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }
    }
}
