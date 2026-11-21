using System;
using System.Collections.Generic;
using System.Numerics;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>Shared geometry for navigation, swept movement, mobility and line of sight.</summary>
public sealed class WorldMap
{
    public WorldDefinition Definition { get; }
    public static WorldDefinition Outdoor { get; } = WorldContentLoader.LoadDefault();

    public WorldMap(WorldDefinition? definition = null)
    {
        Definition = definition ?? Outdoor;
        if (Definition.Width <= 0 || Definition.Height <= 0 || Definition.TileSize <= 0 || Definition.BodyRadius < 0
            || Definition.Spawns.Count == 0) throw new ArgumentException("Invalid world dimensions or spawns.");
        foreach (Vector2 spawn in Definition.Spawns)
            if (!IsWalkable(spawn) || !IsProtected(spawn)) throw new ArgumentException("Spawns must be walkable and protected.");
        foreach (MonsterSpawn spawn in Definition.MonsterSpawns)
            if (spawn.Level is < 1 or > 10 || spawn.BaseExperience is < 0 or > 1000000 || !IsWalkable(spawn.Position) || IsProtected(spawn.Position) || FindPath(Definition.Spawns[0], spawn.Position) is null)
                throw new ArgumentException($"Monster spawn {spawn.Id} must be reachable wilderness land.");
    }

    public bool IsProtected(Vector2 position)
    {
        foreach (WorldRect region in Definition.ProtectedRegions) if (region.Contains(position)) return true;
        return false;
    }

    public bool IsWalkable(Vector2 position)
    {
        float r = Definition.BodyRadius;
        if (!Finite(position) || position.X < r || position.Y < r || position.X > Definition.Width - r || position.Y > Definition.Height - r) return false;
        foreach (WorldRect obstacle in Definition.Obstacles) if (obstacle.Inflated(r).Contains(position)) return false;
        foreach (WorldRect water in Definition.Water) if (water.Inflated(r).Contains(position)) return false;
        return true;
    }

    public bool CanTraverse(Vector2 from, Vector2 to) => IsWalkable(from) && IsWalkable(to) && ClearSegment(from, to, Definition.BodyRadius);
    public bool HasLineOfSight(Vector2 from, Vector2 to) => IsWalkable(from) && IsWalkable(to) && ClearSegment(from, to, 0, false);
    public static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private bool ClearSegment(Vector2 from, Vector2 to, float radius, bool blockWater = true)
    {
        foreach (WorldRect obstacle in Definition.Obstacles)
            if (Intersects(from, to, obstacle.Inflated(radius))) return false;
        if (blockWater) foreach (WorldRect water in Definition.Water)
            if (Intersects(from, to, water.Inflated(radius))) return false;
        return true;
    }

    public bool CrossesProtection(Vector2 from, Vector2 to)
    {
        foreach (WorldRect region in Definition.ProtectedRegions)
            if (Intersects(from, to, region.Inflated(Definition.BodyRadius))) return true;
        return false;
    }

    private static bool Intersects(Vector2 from, Vector2 to, WorldRect rect)
    {
        float low = 0, high = 1;
        Vector2 delta = to - from;
        return Slab(from.X, delta.X, rect.X, rect.X + rect.Width, ref low, ref high)
            && Slab(from.Y, delta.Y, rect.Y, rect.Y + rect.Height, ref low, ref high);
    }

    private static bool Slab(float origin, float delta, float min, float max, ref float low, ref float high)
    {
        if (Math.Abs(delta) < 0.00001f) return origin >= min && origin <= max;
        float a = (min - origin) / delta, b = (max - origin) / delta;
        low = Math.Max(low, Math.Min(a, b));
        high = Math.Min(high, Math.Max(a, b));
        return low <= high;
    }

    public Vector2 Sweep(Vector2 from, Vector2 desired)
    {
        if (!IsWalkable(from) || !Finite(desired)) return from;
        if (CanTraverse(from, desired)) return desired;
        // The traversable prefix is monotonic even if the destination lies beyond a wall.
        float low = 0, high = 1;
        for (int i = 0; i < 20; i++)
        {
            float mid = (low + high) / 2;
            if (CanTraverse(from, Vector2.Lerp(from, desired, mid))) low = mid; else high = mid;
        }
        return Vector2.Lerp(from, desired, Math.Max(0, low - 0.0001f));
    }

    public List<Vector2>? FindPath(Vector2 from, Vector2 to, Func<Vector2, Vector2, bool>? segmentAllowed = null)
    {
        bool Traverse(Vector2 a, Vector2 b) => CanTraverse(a, b) && (segmentAllowed?.Invoke(a, b) ?? true);
        if (!IsWalkable(from) || !IsWalkable(to)) return null;
        if (segmentAllowed is not null && (!segmentAllowed(from, from) || !segmentAllowed(to, to))) return null;
        if (Traverse(from, to)) return new List<Vector2> { to };
        float tile = Definition.TileSize;
        int columns = (int)Math.Ceiling(Definition.Width / tile), rows = (int)Math.Ceiling(Definition.Height / tile);
        int Count() => columns * rows;
        Vector2 Center(int cell) => new((cell % columns + 0.5f) * tile, (cell / columns + 0.5f) * tile);
        // Endpoints may be beside a wall. Find a locally visible grid anchor instead of teleporting to a cell.
        int Anchor(Vector2 point)
        {
            int best = -1;
            float nearest = float.MaxValue;
            int x = (int)(point.X / tile), y = (int)(point.Y / tile);
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int cx = x + dx, cy = y + dy;
                if (cx < 0 || cy < 0 || cx >= columns || cy >= rows) continue;
                int cell = cy * columns + cx;
                float distance = Vector2.DistanceSquared(point, Center(cell));
                if (distance < nearest && Traverse(point, Center(cell))) { best = cell; nearest = distance; }
            }
            return best;
        }
        int start = Anchor(from), goal = Anchor(to);
        if (start < 0 || goal < 0) return null;
        var costs = new float[Count()];
        Array.Fill(costs, float.PositiveInfinity);
        var previous = new int[Count()];
        Array.Fill(previous, -1);
        var closed = new bool[Count()];
        var open = new PriorityQueue<int, float>();
        costs[start] = 0;
        open.Enqueue(start, 0);
        while (open.TryDequeue(out int cell, out _))
        {
            if (closed[cell]) continue;
            if (cell == goal)
            {
                var reverse = new List<Vector2> { to };
                for (int cursor = goal; cursor != -1; cursor = previous[cursor]) reverse.Add(Center(cursor));
                reverse.Reverse();
                // Remove unnecessary corners using the same body sweep as movement.
                var path = new List<Vector2>();
                Vector2 anchor = from;
                for (int i = 0; i < reverse.Count;)
                {
                    int next = reverse.Count - 1;
                    while (next > i && !Traverse(anchor, reverse[next])) next--;
                    path.Add(reverse[next]); anchor = reverse[next]; i = next + 1;
                }
                return path;
            }
            closed[cell] = true;
            int x = cell % columns, y = cell / columns;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= columns || ny >= rows) continue;
                int neighbor = ny * columns + nx;
                if (closed[neighbor] || !Traverse(Center(cell), Center(neighbor))) continue;
                float cost = costs[cell] + Vector2.Distance(Center(cell), Center(neighbor));
                if (cost >= costs[neighbor]) continue;
                costs[neighbor] = cost; previous[neighbor] = cell;
                open.Enqueue(neighbor, cost + Vector2.Distance(Center(neighbor), Center(goal)));
            }
        }
        return null;
    }
}
