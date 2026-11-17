using System;
using System.Collections.Generic;
using System.Numerics;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>Single-writer authoritative duel state. The host supplies elapsed simulation time.</summary>
public sealed class ArenaSimulation
{
    private sealed class Fighter
    {
        public required long Id;
        public Vector2 Position;
        public Vector2 Destination;
        public int Hp = ArenaRules.MaxHp;
        public bool Casting;
        public long CastTargetId;
        public double CastEndsAt;
        public double NextActionAt;
        public double RespawnAt;
    }

    private readonly Dictionary<long, Fighter> _fighters = new();
    private double _elapsed;

    public int PlayerCount => _fighters.Count;

    public bool Join(long playerId)
    {
        if (playerId <= 1 || _fighters.ContainsKey(playerId) || _fighters.Count >= ArenaRules.MaxPlayers)
            return false;

        // Choose a free spawn even when the first player leaves and is replaced.
        Vector2 first = new(245, 320);
        Vector2 second = new(755, 320);
        Vector2 start = first;
        foreach (Fighter existing in _fighters.Values)
            if (Vector2.DistanceSquared(existing.Position, first) < Vector2.DistanceSquared(existing.Position, second))
                start = second;

        _fighters.Add(playerId, new Fighter { Id = playerId, Position = start, Destination = start });
        return true;
    }

    public bool Leave(long playerId) => _fighters.Remove(playerId);

    public IEnumerable<FighterSnapshot> GetSnapshots()
    {
        foreach (Fighter fighter in _fighters.Values)
            yield return new FighterSnapshot(fighter.Id, fighter.Position, fighter.Hp, fighter.Casting);
    }

    // The adapter obtains playerId from authenticated transport context, never from a command payload.
    // The prototype transport supplies only a peer ID; account authentication is still to be implemented.
    public bool RequestMove(long playerId, Vector2 destination)
    {
        if (!float.IsFinite(destination.X) || !float.IsFinite(destination.Y)
            || !_fighters.TryGetValue(playerId, out Fighter? fighter) || fighter.Hp <= 0)
            return false;

        fighter.Destination = Vector2.Clamp(destination, ArenaRules.MinimumPosition, ArenaRules.MaximumPosition);
        fighter.Casting = false;
        return true;
    }

    public bool RequestAttack(long playerId, long targetId, bool useSpell)
    {
        if (playerId == targetId || !_fighters.TryGetValue(playerId, out Fighter? attacker)
            || !_fighters.TryGetValue(targetId, out Fighter? target)
            || attacker.Hp <= 0 || target.Hp <= 0 || attacker.Casting || _elapsed < attacker.NextActionAt)
            return false;

        float range = useSpell ? ArenaRules.SpellRange : ArenaRules.MeleeRange;
        if (Vector2.Distance(attacker.Position, target.Position) > range)
            return false;

        attacker.Destination = attacker.Position;
        if (useSpell)
        {
            attacker.Casting = true;
            attacker.CastTargetId = targetId;
            attacker.CastEndsAt = _elapsed + ArenaRules.SpellCastSeconds;
            attacker.NextActionAt = _elapsed + ArenaRules.SpellCooldownSeconds;
        }
        else
        {
            attacker.NextActionAt = _elapsed + ArenaRules.MeleeCooldownSeconds;
            DealDamage(target, 15);
        }
        return true;
    }

    public void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0 || deltaSeconds > 1)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Use simulation steps in (0, 1] seconds.");

        _elapsed += deltaSeconds;
        foreach (Fighter fighter in _fighters.Values)
        {
            if (fighter.Hp <= 0)
            {
                if (_elapsed >= fighter.RespawnAt)
                {
                    fighter.Hp = ArenaRules.MaxHp;
                    fighter.Casting = false;
                    fighter.Destination = fighter.Position;
                }
                continue;
            }

            if (fighter.Casting)
            {
                if (_elapsed >= fighter.CastEndsAt)
                {
                    fighter.Casting = false;
                    if (_fighters.TryGetValue(fighter.CastTargetId, out Fighter? target)
                        && target.Hp > 0 && Vector2.Distance(fighter.Position, target.Position) <= ArenaRules.SpellRange)
                        DealDamage(target, 30);
                }
                continue;
            }

            Vector2 offset = fighter.Destination - fighter.Position;
            float distance = offset.Length();
            float step = ArenaRules.MoveSpeed * (float)deltaSeconds;
            fighter.Position = distance <= step ? fighter.Destination : fighter.Position + offset / distance * step;
        }
    }

    private void DealDamage(Fighter target, int damage)
    {
        target.Casting = false;
        target.Hp = Math.Max(0, target.Hp - damage);
        if (target.Hp == 0)
            target.RespawnAt = _elapsed + ArenaRules.RespawnSeconds;
    }
}
