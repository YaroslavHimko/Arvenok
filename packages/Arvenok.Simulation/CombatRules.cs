using System;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>Reusable world eligibility policy; no map name, round, or player-count assumptions.</summary>
public sealed class CombatRules
{
    internal bool CanHarm(CombatEntity actor, CombatEntity target, WorldMap map)
    {
        if (actor.Id == target.Id || actor.Hp <= 0 || target.Hp <= 0 || !actor.CombatEnabled || !target.CombatEnabled
            || !map.IsWalkable(actor.Position) || !map.IsWalkable(target.Position)) return false;
        if (actor.FactionId != 0 && actor.FactionId == target.FactionId) return false;
        // The starter sanctuary protects players in both PvP and passive-monster encounters.
        return !(actor.Kind == EntityKind.Player && map.IsProtected(actor.Position))
            && !(target.Kind == EntityKind.Player && map.IsProtected(target.Position));
    }

    // Round halves away from zero once, after the strongest nonstacking defensive modifier.
    public static int Damage(int baseDamage, float reduction) =>
        (int)Math.Round(baseDamage * (1.0 - Math.Clamp(reduction, 0, 1)), MidpointRounding.AwayFromZero);
}
