using System;
using Arvenok.Contracts;

namespace Arvenok.Game.Features.Arena.Presentation;

public readonly record struct AnimationPose(string Clip, double Elapsed, double Duration = 0, double From = 0, double To = 1);

/// <summary>Presentation only. Reliable events reconcile newer outcomes against older position snapshots.</summary>
public sealed class EntityAnimationState(long id, AnimationCatalog catalog)
{
    private long _sequence;
    private double _castStopped = -1, _castAt = -1, _castDuration;
    private AbilitySlot _castSlot;
    private double _deathAt = -1, _respawnAt = -1, _defenseEnded = -1, _motionStopped = -1;
    private double _defenseAt = -1, _defenseUntil, _motionAt = -1, _motionUntil;
    private string _reaction = "hurt", _gesture = "idle";
    private double _reactionAt = -1, _reactionSeconds, _gestureAt = -1, _gestureSeconds, _gestureFrom;
    public bool Removed { get; private set; }

    public void Observe(CombatEvent entry)
    {
        if (entry.Sequence <= _sequence) return;
        _sequence = entry.Sequence;
        if (entry.Kind == CombatEventKind.Died && entry.TargetId == id) { _deathAt = entry.Time; _castStopped = entry.Time; return; }
        if (entry.Kind == CombatEventKind.Damage && entry.TargetId == id)
        {
            // An interrupt reaction has priority over the damage from that same interrupt.
            if (_reaction == "interrupt" && entry.Time < _reactionAt + _reactionSeconds) return;
            _reaction = "hurt"; _reactionAt = entry.Time; _reactionSeconds = catalog.HurtSeconds;
        }
        if (entry.ActorId != id) return;
        switch (entry.Kind)
        {
            case CombatEventKind.Left: Removed = true; break;
            case CombatEventKind.Respawned:
            case CombatEventKind.ClassChanged:
                _respawnAt = entry.Time; _reactionAt = _gestureAt = _castAt = -1; _castStopped = entry.Time;
                _defenseEnded = _motionStopped = entry.Time;
                break;
            case CombatEventKind.CastStarted:
                _castAt = entry.Time; _castDuration = entry.CastDuration; _castSlot = entry.Ability;
                break;
            case CombatEventKind.CastCancelled:
                _castStopped = entry.Time;
                break;
            case CombatEventKind.CastInterrupted:
                _castStopped = entry.Time; _reaction = "interrupt"; _reactionAt = entry.Time; _reactionSeconds = catalog.InterruptSeconds;
                break;
            case CombatEventKind.SkillCompleted:
                if (entry.Ability == AbilitySlot.Mobility) { _motionStopped = entry.Time; break; }
                if (entry.Ability == AbilitySlot.Defense) break;
                _castStopped = entry.Time;
                SkillVisual skill = catalog.Skill(entry.ActorClass, entry.Ability);
                Gesture(skill.Release, entry.Time, skill.ReleaseSeconds, entry.Ability == AbilitySlot.Basic ? 0.5 : 0);
                break;
            case CombatEventKind.DefenseStarted:
                _defenseAt = entry.Time; _defenseUntil = entry.EffectEndsAt;
                SkillVisual defense = catalog.Skill(entry.ActorClass, AbilitySlot.Defense);
                Gesture(defense.Release, entry.Time, defense.ReleaseSeconds);
                break;
            case CombatEventKind.DefenseEnded: _defenseEnded = entry.Time; break;
            case CombatEventKind.Movement when entry.MovementFrom is not null:
                _motionAt = entry.Time; _motionUntil = entry.EffectEndsAt;
                break;
        }
    }

    private void Gesture(string clip, double time, double duration, double from = 0)
    { _gesture = clip; _gestureAt = time; _gestureSeconds = duration; _gestureFrom = from; }

    public bool Alive(EntitySnapshot snapshot, double snapshotTime)
    {
        if (Removed || _deathAt >= snapshotTime && _deathAt >= _respawnAt) return false;
        if (_respawnAt >= snapshotTime && _respawnAt > _deathAt) return true;
        return snapshot.Alive;
    }

    public bool Defending(EntitySnapshot snapshot, double snapshotTime, double time) => Alive(snapshot, snapshotTime)
        && (_defenseAt >= snapshotTime && _defenseAt > _defenseEnded ? time < _defenseUntil
            : _defenseEnded < snapshotTime && time < snapshot.DefenseEndsAt);

    public bool Dashing(EntitySnapshot snapshot, double snapshotTime, double time) => Alive(snapshot, snapshotTime)
        && (_motionAt >= snapshotTime && _motionAt > _motionStopped ? time < _motionUntil
            : _motionStopped < snapshotTime && time < snapshot.MotionEndsAt);

    public AnimationPose Pose(EntitySnapshot snapshot, double snapshotTime, double time)
    {
        if (!Alive(snapshot, snapshotTime))
            return new("death", Math.Max(0, time - Math.Max(snapshot.DiedAt, _deathAt)));
        // A newer living snapshot also clears missed death/respawn reactions.
        double lifeStart = _respawnAt > _deathAt ? _respawnAt : snapshot.Alive && snapshotTime > _deathAt ? _deathAt : -1;
        if (_reactionAt > lifeStart && time < _reactionAt + _reactionSeconds)
            return new(_reaction, Math.Max(0, time - _reactionAt), _reactionSeconds);
        double castAt = snapshot.CastStartedAt, duration = snapshot.CastEndsAt - castAt;
        AbilitySlot slot = snapshot.ActiveAbility;
        bool casting = snapshot.Casting;
        if (_castAt >= snapshotTime) { castAt = _castAt; duration = _castDuration; slot = _castSlot; casting = true; }
        if (casting && castAt > _castStopped && time < castAt + duration)
            return new(catalog.Skill(snapshot.Class, slot).Windup, Math.Max(0, time - castAt), duration, 0, slot == AbilitySlot.Basic ? 0.5 : 1);
        if (_gestureAt > lifeStart && time < _gestureAt + _gestureSeconds && !(_gesture == "defend" && snapshot.Action == ActionState.Moving))
            return new(_gesture, Math.Max(0, time - _gestureAt), _gestureSeconds, _gestureFrom);
        return new(snapshot.Action == ActionState.Moving || Dashing(snapshot, snapshotTime, time) ? "walk" : "idle", time);
    }
}
