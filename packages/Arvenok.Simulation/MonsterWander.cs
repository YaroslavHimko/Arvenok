using System;
using System.Collections.Generic;
using System.Numerics;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>Passive server-owned roaming. It submits paths to the same movement/collision code as players.</summary>
internal sealed class MonsterWander
{
    private readonly Random _random;
    private double _idleUntil;
    public MonsterSpawn Spawn { get; }
    public WanderState State { get; private set; }

    public MonsterWander(MonsterSpawn spawn, int seed, double time)
    {
        Spawn = spawn;
        _random = new Random(seed);
        Rest(time);
    }

    public void Rest(double time)
    {
        State = WanderState.Idle;
        _idleUntil = time + Spawn.IdleMinSeconds + _random.NextDouble() * (Spawn.IdleMaxSeconds - Spawn.IdleMinSeconds);
    }

    public void Died() => State = WanderState.Dead;

    public void Advance(CombatEntity entity, WorldMap map, double time)
    {
        if (entity.Moving || entity.Casting || time < entity.RecoveryEndsAt) return;
        if (State is WanderState.Roaming or WanderState.ReturningHome) { Rest(time); return; }
        if (time < _idleUntil) return;

        if (Vector2.DistanceSquared(entity.Position, Spawn.Position) > Spawn.RoamRadius * Spawn.RoamRadius)
        { ReturnHome(entity, map, time); return; }

        for (int attempt = 0; attempt < 8; attempt++)
        {
            double angle = _random.NextDouble() * Math.Tau;
            float distance = (float)Math.Sqrt(_random.NextDouble()) * Spawn.RoamRadius;
            Vector2 destination = Spawn.Position + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * distance;
            if (Vector2.DistanceSquared(entity.Position, destination) < 64) continue;
            List<Vector2>? path = map.FindPath(entity.Position, destination, (from, to) => InRoamArea(from, to, map));
            if (path is null) continue;
            entity.Path = path; entity.Waypoint = 0; State = WanderState.Roaming;
            return;
        }
        ReturnHome(entity, map, time);
    }

    private bool InRoamArea(Vector2 from, Vector2 to, WorldMap map) =>
        Vector2.DistanceSquared(from, Spawn.Position) <= Spawn.RoamRadius * Spawn.RoamRadius + 0.001f
        && Vector2.DistanceSquared(to, Spawn.Position) <= Spawn.RoamRadius * Spawn.RoamRadius + 0.001f
        && !map.CrossesProtection(from, to);

    private void ReturnHome(CombatEntity entity, WorldMap map, double time)
    {
        if (Vector2.DistanceSquared(entity.Position, Spawn.Position) < 4) { Rest(time); return; }
        // Failed routes never teleport. An unreachable home simply leads to another idle interval.
        bool insideLeash = Vector2.DistanceSquared(entity.Position, Spawn.Position) <= Spawn.RoamRadius * Spawn.RoamRadius;
        List<Vector2>? path = map.FindPath(entity.Position, Spawn.Position,
            (from, to) => insideLeash ? InRoamArea(from, to, map) : !map.CrossesProtection(from, to));
        if (path is null) { Rest(time); return; }
        entity.Path = path; entity.Waypoint = 0; State = WanderState.ReturningHome;
    }

    public void AfterMovement(CombatEntity entity, double time)
    {
        if (!entity.Moving && State is WanderState.Roaming or WanderState.ReturningHome) Rest(time);
    }
}
