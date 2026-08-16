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
        private const int MinimumRoadStraightBeforeTurn = 3;

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
            EnsureIslandRoadCoverage(layout, island);
            RemoveRoadDeadEnds(layout);
            if (!HasAnyRoad(layout)) TryGenerateFallbackLoop(layout, island);
            int maximumRoadStraight = Mathf.Clamp(13 - settings.roadPoints, 8, 11);
            int minimumDetourSpacing = Mathf.Clamp(
                Mathf.Min(layout.Width, layout.Height) / 8, 6, 10);
            float roadDetourChance = Mathf.Lerp(
                0.3f, 0.5f, (settings.roadPoints - 2) / 3f);
            BreakLongStraightRoads(
                layout, island, maximumRoadStraight, minimumDetourSpacing,
                roadDetourChance);
            RemoveRoadDeadEnds(layout);
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
            // A higher superellipse exponent keeps broad, square-like sides
            // while the harmonics above preserve an organic coastline.
            const float exponent = 5f;

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
                            island, false, null, 1, true);
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
            List<Vector2Int> roadCandidates = BuildRoadCandidates(layout, island);
            int requestedRoutes = Mathf.Clamp(settings.roadGates, 2, 5);

            for (int route = 0; route < requestedRoutes; route++)
            {
                List<Vector2Int> availableCells =
                    FilterFarFromRoads(layout, roadCandidates, 3);
                if (availableCells.Count < 2) break;

                Vector2Int start = availableCells[rng.NextInt(availableCells.Count)];
                Vector2Int end;
                int minimumSpan = Mathf.Max(8, Mathf.Min(layout.Width, layout.Height) / 3);
                if (!TryPickDistant(availableCells, start, minimumSpan, ref rng, out end))
                    continue;

                bool[] existingRoads = (bool[])layout.Roads.Clone();
                List<Vector2Int> completeRoute = FindPath(
                    layout, start, end, false,
                    layout.RoadSeed ^ (ulong)(route * 4099 + settings.roadPoints * 313 + 1),
                    island, true, existingRoads, 1, false, true);
                bool valid = completeRoute.Count > 0;

                if (valid)
                {
                    bool[] closureBlocks = (bool[])existingRoads.Clone();
                    for (int i = 1; i < completeRoute.Count - 1; i++)
                    {
                        Vector2Int p = completeRoute[i];
                        closureBlocks[layout.Index(p.x, p.y)] = true;
                    }

                    List<Vector2Int> closure = FindPath(
                        layout, end, start, false,
                        layout.RoadSeed ^ (ulong)(0xC105E + route * 8191),
                        island, false, closureBlocks, 0, false, true);
                    if (closure.Count == 0)
                    {
                        valid = false;
                    }
                    else
                    {
                        closure.RemoveAt(0);
                        completeRoute.AddRange(closure);
                    }
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
                    layout.Roads[index] = true;
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
                    island, true, existingRoads, 1, false, true);
                if (path.Count == 0) continue;

                bool[] closureBlocks = (bool[])existingRoads.Clone();
                for (int i = 1; i < path.Count - 1; i++)
                {
                    Vector2Int p = path[i];
                    closureBlocks[layout.Index(p.x, p.y)] = true;
                }
                List<Vector2Int> closure = FindPath(
                    layout, end, start, false,
                    layout.RoadSeed ^ (ulong)(0x10CA1 + route * 1999 + attempt),
                    island, false, closureBlocks, 0, false, true);
                if (closure.Count == 0) continue;

                int placed = 0;
                for (int i = 0; i < path.Count; i++)
                {
                    Vector2Int p = path[i];
                    int index = layout.Index(p.x, p.y);
                    layout.Roads[index] = true;
                    placed++;
                }
                for (int i = 1; i < closure.Count; i++)
                {
                    Vector2Int p = closure[i];
                    layout.Roads[layout.Index(p.x, p.y)] = true;
                    placed++;
                }
                if (placed >= 2) return true;
            }
            return false;
        }

        private void EnsureIslandRoadCoverage(MapLayout layout, bool[] island)
        {
            int coverageRadius = Mathf.Clamp(
                Mathf.Min(layout.Width, layout.Height) / 7, 8, 12);
            int maximumExtensions = Mathf.Clamp(settings.roadGates * 2, 4, 8);
            for (int extension = 0; extension < maximumExtensions; extension++)
            {
                int distance;
                Vector2Int anchor = FindFarthestLandFromRoad(
                    layout, island, out distance);
                if (distance <= coverageRadius) break;
                if (!TryAttachCoverageLoop(
                    layout, island, anchor,
                    layout.RoadSeed ^ (ulong)(0xC0A3E + extension * 65537)) &&
                    !TryPlaceCoverageLoopNear(layout, island, anchor))
                {
                    break;
                }
            }
        }

        private static Vector2Int FindFarthestLandFromRoad(
            MapLayout layout, bool[] island, out int farthestDistance)
        {
            int[] distances = new int[layout.CellCount];
            Queue<int> queue = new Queue<int>();
            for (int i = 0; i < distances.Length; i++) distances[i] = -1;
            for (int i = 0; i < layout.CellCount; i++)
            {
                if (!layout.Roads[i]) continue;
                distances[i] = 0;
                queue.Enqueue(i);
            }

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int x = current % layout.Width;
                int y = current / layout.Width;
                for (int d = 0; d < Cardinal.Length; d++)
                {
                    int nx = x + Cardinal[d].x;
                    int ny = y + Cardinal[d].y;
                    if (!layout.InBounds(nx, ny)) continue;
                    int next = layout.Index(nx, ny);
                    if (!island[next] || distances[next] >= 0) continue;
                    distances[next] = distances[current] + 1;
                    queue.Enqueue(next);
                }
            }

            Vector2Int result = default(Vector2Int);
            farthestDistance = -1;
            for (int i = 0; i < layout.CellCount; i++)
            {
                if (!island[i] || layout.Water[i] || distances[i] <= farthestDistance)
                    continue;
                farthestDistance = distances[i];
                result = new Vector2Int(i % layout.Width, i / layout.Width);
            }
            return result;
        }

        private static bool TryAttachCoverageLoop(
            MapLayout layout, bool[] island, Vector2Int anchor, ulong seed)
        {
            List<Vector2Int> roads = new List<Vector2Int>();
            for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
                if (layout.IsRoad(x, y)) roads.Add(new Vector2Int(x, y));
            if (roads.Count < 2) return false;

            Vector2Int firstTarget = roads[0];
            int firstDistance = int.MaxValue;
            for (int i = 0; i < roads.Count; i++)
            {
                int distance = Manhattan(anchor, roads[i]);
                if (distance >= firstDistance) continue;
                firstDistance = distance;
                firstTarget = roads[i];
            }

            Vector2Int secondTarget = firstTarget;
            int secondDistance = int.MaxValue;
            int targetSeparation = Mathf.Max(6,
                Mathf.Min(layout.Width, layout.Height) / 10);
            for (int i = 0; i < roads.Count; i++)
            {
                if (Manhattan(firstTarget, roads[i]) < targetSeparation) continue;
                int distance = Manhattan(anchor, roads[i]);
                if (distance >= secondDistance) continue;
                secondDistance = distance;
                secondTarget = roads[i];
            }
            if (secondTarget == firstTarget) return false;

            List<Vector2Int> firstPath = FindPath(
                layout, anchor, firstTarget, false, seed,
                island, true, null, 1, false, true);
            if (firstPath.Count == 0) return false;

            bool[] secondPathBlocks = new bool[layout.CellCount];
            for (int i = 1; i < firstPath.Count - 1; i++)
            {
                Vector2Int p = firstPath[i];
                secondPathBlocks[layout.Index(p.x, p.y)] = true;
            }
            List<Vector2Int> secondPath = FindPath(
                layout, anchor, secondTarget, false,
                seed ^ 0x9E3779B97F4A7C15UL,
                island, true, secondPathBlocks, 0, false, true);
            if (secondPath.Count == 0) return false;

            for (int i = 0; i < firstPath.Count; i++)
            {
                Vector2Int p = firstPath[i];
                layout.Roads[layout.Index(p.x, p.y)] = true;
            }
            for (int i = 0; i < secondPath.Count; i++)
            {
                Vector2Int p = secondPath[i];
                layout.Roads[layout.Index(p.x, p.y)] = true;
            }
            return true;
        }

        private static bool TryPlaceCoverageLoopNear(
            MapLayout layout, bool[] island, Vector2Int anchor)
        {
            for (int searchRadius = 0; searchRadius <= 6; searchRadius++)
            for (int oy = -searchRadius; oy <= searchRadius; oy++)
            for (int ox = -searchRadius; ox <= searchRadius; ox++)
            for (int size = 5; size >= 3; size--)
            {
                int left = anchor.x + ox - size / 2;
                int bottom = anchor.y + oy - size / 2;
                bool valid = true;
                for (int offset = 0; offset <= size && valid; offset++)
                {
                    int[] xs = { left + offset, left + offset, left, left + size };
                    int[] ys = { bottom, bottom + size, bottom + offset, bottom + offset };
                    for (int side = 0; side < 4; side++)
                    {
                        if (!layout.InBounds(xs[side], ys[side]))
                        {
                            valid = false;
                            break;
                        }
                        int index = layout.Index(xs[side], ys[side]);
                        if (!island[index] || layout.Water[index])
                        {
                            valid = false;
                            break;
                        }
                    }
                }
                if (!valid) continue;

                for (int offset = 0; offset <= size; offset++)
                {
                    layout.Roads[layout.Index(left + offset, bottom)] = true;
                    layout.Roads[layout.Index(left + offset, bottom + size)] = true;
                    layout.Roads[layout.Index(left, bottom + offset)] = true;
                    layout.Roads[layout.Index(left + size, bottom + offset)] = true;
                }
                return true;
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

        private static List<Vector2Int> BuildRoadCandidates(
            MapLayout layout, bool[] island)
        {
            List<Vector2Int> result = new List<Vector2Int>();
            for (int y = 2; y < layout.Height - 2; y++)
            for (int x = 2; x < layout.Width - 2; x++)
            {
                int index = layout.Index(x, y);
                if (!island[index] || layout.Water[index] ||
                    AdjacentToWater(layout, x, y, 1)) continue;
                result.Add(new Vector2Int(x, y));
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
            bool roadCosts, bool[] forbiddenRoads, int forbiddenRadius = 1,
            bool riverCosts = false, bool enforceRoadTurns = false)
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
                        AdjacentToRoad(forbiddenRoads, layout.Width, layout.Height,
                            nx, ny, forbiddenRadius))
                    {
                        continue;
                    }

                    bool currentIsWater = layout.Water[current];
                    bool nextIsWater = layout.Water[next];
                    float step;
                    if (roadCosts && nextIsWater)
                    {
                        step = 8f;
                    }
                    else if (riverCosts)
                    {
                        // A stronger terrain bias makes the stream drift around
                        // local low-cost cells instead of tracing a near-straight
                        // Manhattan route from the lake to the coast.
                        step = 0.72f + SeedUtility.Cell01(seed, nx, ny) * 0.82f;
                    }
                    else
                    {
                        step = 0.85f + SeedUtility.Cell01(seed, nx, ny) * 0.45f;
                    }

                    int oldPrevious = previous[current];
                    if (oldPrevious >= 0)
                    {
                        int px = oldPrevious % layout.Width;
                        int py = oldPrevious / layout.Width;
                        bool continuesStraight =
                            cx - px == Cardinal[d].x && cy - py == Cardinal[d].y;
                        if (riverCosts)
                        {
                            if (continuesStraight)
                            {
                                int straightRun = CountStraightRun(
                                    previous, current, Cardinal[d], layout.Width, 7);
                                if (straightRun >= 3)
                                    step += (straightRun - 2) * 0.48f;
                            }
                            else
                            {
                                // Avoid one-cell zigzags while still allowing broad,
                                // frequent bends once a straight run has developed.
                                step += 0.14f;
                            }
                        }
                        else if (enforceRoadTurns && currentIsWater)
                        {
                            // Once a road enters water, prefer the shortest bridge:
                            // every extra water tile is expensive and turns inside
                            // the river are much more expensive than going straight.
                            if (!continuesStraight) step += 1.25f;
                        }
                        else if (enforceRoadTurns && continuesStraight)
                        {
                            int straightRun = CountStraightRun(
                                previous, current, Cardinal[d], layout.Width, 10);
                            if (straightRun >= 10) continue;
                            if (straightRun >= 5)
                                step += (straightRun - 4) * 0.22f;
                        }
                        else if (enforceRoadTurns)
                        {
                            Vector2Int previousDirection =
                                new Vector2Int(cx - px, cy - py);
                            int straightRun = CountStraightRun(
                                previous, current, previousDirection,
                                layout.Width, MinimumRoadStraightBeforeTurn);
                            if (straightRun < MinimumRoadStraightBeforeTurn &&
                                next != goalIndex)
                            {
                                continue;
                            }
                            step += 0.35f;
                        }
                        else if (!roadCosts && !continuesStraight)
                        {
                            step += 0.42f;
                        }
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

        private static int CountStraightRun(
            int[] previous, int current, Vector2Int direction, int width, int limit)
        {
            int run = 0;
            int cursor = current;
            while (run < limit && previous[cursor] >= 0)
            {
                int prior = previous[cursor];
                int cx = cursor % width;
                int cy = cursor / width;
                int px = prior % width;
                int py = prior / width;
                if (cx - px != direction.x || cy - py != direction.y) break;
                run++;
                cursor = prior;
            }
            return run;
        }

        private static void CarveStream(MapLayout layout, List<Vector2Int> path, ulong seed)
        {
            for (int i = 0; i < path.Count; i++)
            {
                Vector2Int p = path[i];
                SetWater(layout, p.x, p.y);
                if (i <= 0 || i >= path.Count - 1) continue;

                Vector2Int incoming = p - path[i - 1];
                Vector2Int outgoing = path[i + 1] - p;
                bool isTurn = incoming != outgoing;
                if (isTurn)
                {
                    // Complete the 2x2 footprint around a grid turn. The shore
                    // renderer then selects a rounded diagonal transition instead
                    // of exposing a strict 90-degree river elbow.
                    Vector2Int roundedCorner = path[i - 1] + path[i + 1] - p;
                    SetWater(layout, roundedCorner.x, roundedCorner.y);
                }
                else if (
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

        private static void RemoveRoadDeadEnds(MapLayout layout)
        {
            Queue<int> queue = new Queue<int>();
            bool[] queued = new bool[layout.CellCount];
            for (int index = 0; index < layout.CellCount; index++)
            {
                if (!layout.Roads[index]) continue;
                int x = index % layout.Width;
                int y = index / layout.Width;
                if (CountRoadNeighbours(layout, x, y) >= 2) continue;
                queued[index] = true;
                queue.Enqueue(index);
            }

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                queued[index] = false;
                if (!layout.Roads[index]) continue;
                int x = index % layout.Width;
                int y = index / layout.Width;
                if (CountRoadNeighbours(layout, x, y) >= 2) continue;
                layout.Roads[index] = false;
                for (int d = 0; d < Cardinal.Length; d++)
                {
                    int nx = x + Cardinal[d].x;
                    int ny = y + Cardinal[d].y;
                    if (!layout.InBounds(nx, ny)) continue;
                    int neighbour = layout.Index(nx, ny);
                    if (!layout.Roads[neighbour] || queued[neighbour]) continue;
                    queued[neighbour] = true;
                    queue.Enqueue(neighbour);
                }
            }
        }

        private static int CountRoadNeighbours(MapLayout layout, int x, int y)
        {
            int count = 0;
            for (int d = 0; d < Cardinal.Length; d++)
                if (layout.IsRoad(x + Cardinal[d].x, y + Cardinal[d].y)) count++;
            return count;
        }

        private static bool HasAnyRoad(MapLayout layout)
        {
            for (int i = 0; i < layout.Roads.Length; i++)
                if (layout.Roads[i]) return true;
            return false;
        }

        private static bool TryGenerateFallbackLoop(MapLayout layout, bool[] island)
        {
            int loopsPlaced = 0;
            int maximumSize = Mathf.Min(12, Mathf.Min(layout.Width, layout.Height) / 3);
            for (int loop = 0; loop < 2; loop++)
            {
                bool placed = false;
                for (int size = maximumSize; size >= 3 && !placed; size--)
                for (int y = 1; y + size < layout.Height - 1 && !placed; y++)
                for (int x = 1; x + size < layout.Width - 1 && !placed; x++)
                {
                    bool valid = true;
                    for (int oy = 0; oy <= size && valid; oy++)
                    for (int ox = 0; ox <= size; ox++)
                    {
                        if (ox != 0 && ox != size && oy != 0 && oy != size) continue;
                        int px = x + ox;
                        int py = y + oy;
                        int index = layout.Index(px, py);
                        if (!island[index] || layout.Water[index] ||
                            AdjacentToRoad(layout.Roads, layout.Width, layout.Height,
                                px, py, 2))
                        {
                            valid = false;
                            break;
                        }
                    }
                    if (!valid) continue;
                    for (int offset = 0; offset <= size; offset++)
                    {
                        layout.Roads[layout.Index(x + offset, y)] = true;
                        layout.Roads[layout.Index(x + offset, y + size)] = true;
                        layout.Roads[layout.Index(x, y + offset)] = true;
                        layout.Roads[layout.Index(x + size, y + offset)] = true;
                    }
                    placed = true;
                    loopsPlaced++;
                }
                if (!placed) break;
            }
            return loopsPlaced > 0;
        }

        private static void BreakLongStraightRoads(
            MapLayout layout, bool[] island, int maximumStraight,
            int minimumDetourSpacing, float detourChance)
        {
            int safety = layout.CellCount;
            while (safety-- > 0)
            {
                bool changed = false;
                for (int y = 1; y < layout.Height - 1 && !changed; y++)
                for (int x = 1; x < layout.Width - maximumStraight && !changed; x++)
                {
                    bool longRun = true;
                    for (int offset = 0; offset <= maximumStraight; offset++)
                    {
                        int px = x + offset;
                        if (!layout.IsRoad(px, y) ||
                            layout.Water[layout.Index(px, y)] ||
                            layout.IsRoad(px, y - 1) ||
                            layout.IsRoad(px, y + 1))
                        {
                            longRun = false;
                            break;
                        }
                    }
                    int detourX = x + maximumStraight / 2;
                    if (longRun && !HasOneTileRoadLoopNear(
                            layout, detourX, y, minimumDetourSpacing) &&
                        SeedUtility.Cell01(
                        layout.RoadSeed ^ 0x48F2A91UL, detourX, y) < detourChance)
                        changed = TryDetourRoadCell(
                            layout, island, detourX, y, true);
                }

                for (int x = 1; x < layout.Width - 1 && !changed; x++)
                for (int y = 1; y < layout.Height - maximumStraight && !changed; y++)
                {
                    bool longRun = true;
                    for (int offset = 0; offset <= maximumStraight; offset++)
                    {
                        int py = y + offset;
                        if (!layout.IsRoad(x, py) ||
                            layout.Water[layout.Index(x, py)] ||
                            layout.IsRoad(x - 1, py) ||
                            layout.IsRoad(x + 1, py))
                        {
                            longRun = false;
                            break;
                        }
                    }
                    int detourY = y + maximumStraight / 2;
                    if (longRun && !HasOneTileRoadLoopNear(
                            layout, x, detourY, minimumDetourSpacing) &&
                        SeedUtility.Cell01(
                        layout.RoadSeed ^ 0xB73C6D5UL, x, detourY) < detourChance)
                        changed = TryDetourRoadCell(
                            layout, island, x, detourY, false);
                }

                if (!changed) break;
            }
        }

        private static bool HasOneTileRoadLoopNear(
            MapLayout layout, int centerX, int centerY, int radius)
        {
            int minX = Mathf.Max(1, centerX - radius);
            int maxX = Mathf.Min(layout.Width - 2, centerX + radius);
            int minY = Mathf.Max(1, centerY - radius);
            int maxY = Mathf.Min(layout.Height - 2, centerY + radius);
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                if (Mathf.Abs(x - centerX) + Mathf.Abs(y - centerY) > radius ||
                    layout.IsRoad(x, y) || layout.IsWater(x, y))
                {
                    continue;
                }
                if (layout.IsRoad(x, y + 1) && layout.IsRoad(x + 1, y) &&
                    layout.IsRoad(x, y - 1) && layout.IsRoad(x - 1, y))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryDetourRoadCell(
            MapLayout layout, bool[] island, int x, int y, bool horizontal)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                bool valid = true;
                for (int offset = -1; offset <= 1; offset++)
                {
                    int nx = horizontal ? x + offset : x + side;
                    int ny = horizontal ? y + side : y + offset;
                    if (!layout.InBounds(nx, ny)) { valid = false; break; }
                    int index = layout.Index(nx, ny);
                    if (!island[index] || layout.Water[index] || layout.Roads[index])
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid) continue;

                layout.Roads[layout.Index(x, y)] = false;
                for (int offset = -1; offset <= 1; offset++)
                {
                    int nx = horizontal ? x + offset : x + side;
                    int ny = horizontal ? y + side : y + offset;
                    layout.Roads[layout.Index(nx, ny)] = true;
                }
                return true;
            }
            return TryAddLoopBranch(layout, island, x, y, horizontal);
        }

        private static bool TryAddLoopBranch(
            MapLayout layout, bool[] island, int x, int y, bool horizontal)
        {
            for (int side = -1; side <= 1; side += 2)
            for (int forward = -1; forward <= 1; forward += 2)
            {
                int firstX = horizontal ? x : x + side;
                int firstY = horizontal ? y + side : y;
                int secondX = horizontal ? x + forward : x + side;
                int secondY = horizontal ? y + side : y + forward;
                int anchorX = horizontal ? x + forward : x;
                int anchorY = horizontal ? y : y + forward;
                if (!layout.InBounds(firstX, firstY) ||
                    !layout.InBounds(secondX, secondY) ||
                    !layout.IsRoad(anchorX, anchorY)) continue;

                int first = layout.Index(firstX, firstY);
                int second = layout.Index(secondX, secondY);
                if (!island[first] || !island[second] ||
                    layout.Water[first] || layout.Water[second] ||
                    layout.Roads[first] || layout.Roads[second]) continue;

                layout.Roads[first] = true;
                layout.Roads[second] = true;
                return true;
            }
            return false;
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
