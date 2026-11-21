using System;
using System.Collections.Generic;
using System.Numerics;
using Arvenok.Contracts;

namespace Arvenok.Game.Features.Arena.Presentation;

public readonly record struct CombatEffectCue(long Sequence, string Effect, bool Projectile, long OwnerId, long TargetId,
    Vector2 From, Vector2 To, double StartedAt, double EndsAt, float Rotation)
{
    public Vector2 Position(double time) => Projectile ? Vector2.Lerp(From, To, (float)Math.Clamp((time - StartedAt) / (EndsAt - StartedAt), 0, 1)) : From;
}

/// <summary>Produces finite cosmetic trajectories from confirmed outcomes, never collision or damage.</summary>
public sealed class CombatEffectPlanner(AnimationCatalog catalog)
{
    private long _sequence;
    public IReadOnlyList<CombatEffectCue> Observe(CombatEvent entry, double time, float targetHeight = 72)
    {
        var result = new List<CombatEffectCue>();
        if (entry.Sequence <= _sequence) return result;
        _sequence = entry.Sequence;
        if (entry.ActorKind != EntityKind.Player) return result;
        SkillVisual skill = catalog.Skill(entry.ActorClass, entry.Ability);
        if (entry.Ability == AbilitySlot.Basic && entry.WeaponType == WeaponType.Dagger) skill = catalog.Skill(CharacterClass.Warrior, AbilitySlot.Basic);
        Vector2 from = entry.ActorPosition - new Vector2(0, catalog.EffectHeight);
        Vector2 to = entry.TargetPosition - new Vector2(0, Math.Min(catalog.EffectHeight, targetHeight / 2));
        void Add(string effect, Vector2 start, Vector2 end, bool projectile = false, double seconds = 0, float rotation = 0)
        {
            if (effect.Length == 0) return;
            double ends = entry.Time + (seconds > 0 ? seconds : catalog.Clips["effects/" + effect].Seconds);
            if (time >= ends) return; // A delayed reliable packet cannot resurrect an expired visual.
            result.Add(new CombatEffectCue(entry.Sequence, effect, projectile, entry.ActorId, entry.TargetId, start, end, entry.Time, ends, rotation));
        }
        // Hit outcomes confirm release AND impact. Avoided and fully absorbed shots still travel visually.
        // A partial absorb has a separate Damage event, which supplies its one projectile.
        if (entry.Kind == CombatEventKind.Damage && entry.Damage > 0 || entry.Kind is CombatEventKind.Dodged or CombatEventKind.Parried or CombatEventKind.PerfectBlocked
            || entry.Kind == CombatEventKind.Absorbed && entry.Damage == 0)
        {
            Vector2 direction = to - from;
            float angle = MathF.Atan2(direction.Y, direction.X);
            switch (skill.Mode)
            {
                case "projectile": Add(skill.Effect, from, to, true, catalog.FlightSeconds(direction.Length()), angle); break;
                case "melee": Add(skill.Effect, (from + to) / 2, to, rotation: angle); break;
                case "impact": Add(skill.Effect, to, to); break;
            }
            Add(skill.Impact, to, to);
        }
        else if (entry.Kind == CombatEventKind.Movement && entry.MovementFrom is Vector2 origin && skill.Mode == "blink")
        {
            Vector2 departure = origin - new Vector2(0, catalog.EffectHeight);
            Add(skill.Effect, departure, departure); Add(skill.Effect, from, from);
        }
        return result;
    }
}
