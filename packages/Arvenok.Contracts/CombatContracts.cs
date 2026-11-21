using System;
using System.Collections.Generic;
using System.Numerics;

namespace Arvenok.Contracts;

public enum CharacterClass { Warrior, Mage, Archer }
public enum EntityKind { Player, Monster, Structure }
public enum MonsterSpecies { Slime, Mushroom, Wolf, Sprout }
public enum WanderState { None, Idle, Roaming, ReturningHome, Dead }
public enum ActionState { Idle, Moving, BasicAttackWindup, Casting, SkillRecovery, Dead }
public enum AbilitySlot { Basic, Primary, Mobility, Interrupt, Defense, BranchOne, BranchTwo }
public enum AbilityEffect { Damage, Rush, Blink, Backstep, Interrupt, Defense, Sidestep }
public enum DamageType { Physical, Magic }
public enum CommandKind { Move, SelectTarget, Attack, Stop, UseAbility, ChooseClass, UpgradeSkill }
public enum CombatEventKind
{
    Joined, Left, ClassChanged, Movement, TargetChanged, AttackStarted, AttackStopped,
    CastStarted, CastCancelled, CastInterrupted, SkillCompleted, Damage, DefenseStarted,
    DefenseEnded, Died, Respawned, Rejected, CombatStarted, CombatEnded, LevelUp,
    Dodged, Parried, PerfectBlocked, Absorbed, Healed, ShieldBlocked
}

public sealed record AbilityDefinition(AbilitySlot Slot, string Name, AbilityEffect Effect,
    DamageType DamageType, int Damage, float Range, double CastSeconds, double CooldownSeconds,
    bool Interruptible = false, double EffectSeconds = 0, float Reduction = 0, double TravelSeconds = 0)
{
    public int AbsorbAmount { get; init; }
    public float SidestepDistance { get; init; }
}

public sealed record ClassDefinition(CharacterClass Class, int MaxHp, float MoveSpeed,
    IReadOnlyList<AbilityDefinition> Abilities)
{
    public AbilityDefinition Ability(AbilitySlot slot) => Abilities[(int)slot];
}

public readonly record struct CooldownSnapshot(double Basic, double Primary, double Mobility, double Interrupt, double Defense, double BranchOne = 0, double BranchTwo = 0)
{
    public double EndsAt(AbilitySlot slot) => slot switch
    {
        AbilitySlot.Basic => Basic, AbilitySlot.Primary => Primary, AbilitySlot.Mobility => Mobility,
        AbilitySlot.Interrupt => Interrupt, AbilitySlot.Defense => Defense, AbilitySlot.BranchOne => BranchOne, AbilitySlot.BranchTwo => BranchTwo, _ => double.PositiveInfinity
    };
}

/// <summary>Immutable read model, independent of a visual scene or transport peer identifier.</summary>
public readonly record struct EntitySnapshot(long Id, EntityKind Kind, string Name, CharacterClass Class,
    Vector2 Position, Vector2 Facing, int Hp, int MaxHp, ActionState Action, long TargetId,
    bool AutoAttacking, bool Protected, bool CanChooseClass, AbilitySlot ActiveAbility,
    double CastStartedAt, double CastEndsAt, double RecoveryEndsAt, double DefenseEndsAt,
    double RespawnAt, CooldownSnapshot Cooldowns, MonsterSpecies? Species = null, WanderState Wander = WanderState.None,
    double DiedAt = 0, double MotionEndsAt = 0, int Level = 1)
{
    public bool Alive => Hp > 0;
    public bool Casting => Action is ActionState.Casting or ActionState.BasicAttackWindup;
    public double DodgeEndsAt { get; init; }
    public double ParryEndsAt { get; init; }
    public double PerfectBlockEndsAt { get; init; }
    public double ShellEndsAt { get; init; }
    public int ShellRemaining { get; init; }
    public WeaponType WeaponType { get; init; }
    public bool ShieldEquipped { get; init; }
    public string ActiveAbilityName { get; init; } = "";
    public string DefenseName { get; init; } = "";
}

public sealed record WorldSnapshot(long Revision, double Time, EntitySnapshot[] Entities)
{
    public GroundLoot[] Loot { get; init; } = Array.Empty<GroundLoot>();
}

/// <summary>The actor is supplied separately by the server's peer-to-entity mapping.</summary>
public readonly record struct WorldCommand(long Sequence, double ObservedServerTime, CommandKind Kind,
    long TargetId = 0, AbilitySlot Slot = AbilitySlot.Basic, Vector2 Destination = default,
    CharacterClass Class = CharacterClass.Warrior, string SkillId = "", long ProgressionRevision = 0);

public readonly record struct CombatEvent(long Sequence, double Time, CombatEventKind Kind,
    long ActorId, long TargetId, CharacterClass ActorClass, CharacterClass TargetClass,
    AbilitySlot Ability, string AbilityName, int Damage, double CastDuration, string Detail,
    EntityKind ActorKind = EntityKind.Player, EntityKind TargetKind = EntityKind.Player,
    Vector2 ActorPosition = default, Vector2 TargetPosition = default, Vector2 Facing = default,
    Vector2? MovementFrom = null, double EffectEndsAt = 0, WeaponType WeaponType = WeaponType.None);

public readonly record struct WorldRect(float X, float Y, float Width, float Height)
{
    public bool Contains(Vector2 point) => point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Y + Height;
    public WorldRect Inflated(float radius) => new(X - radius, Y - radius, Width + 2 * radius, Height + 2 * radius);
}

public sealed record WorldDefinition(float Width, float Height, float TileSize, float BodyRadius,
    IReadOnlyList<WorldRect> Obstacles, IReadOnlyList<WorldRect> ProtectedRegions, IReadOnlyList<Vector2> Spawns)
{
    public IReadOnlyList<WorldRect> Water { get; init; } = Array.Empty<WorldRect>();
    public IReadOnlyList<MonsterSpawn> MonsterSpawns { get; init; } = Array.Empty<MonsterSpawn>();
    public WorldArt? Art { get; init; }
}
