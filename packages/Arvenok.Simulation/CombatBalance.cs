using System;
using System.Collections.Generic;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>All v0.1 balance values. One tile is 32 world pixels throughout the simulation.</summary>
public sealed record CombatBalance
{
    public const float PixelsPerTile = 32;
    public static float Tiles(float value) => value * PixelsPerTile;
    public int MaxPlayers { get; init; } = 32;
    public double SnapshotInterval { get; init; } = 1.0 / 15;
    public double RecoverySeconds { get; init; } = 0.25;
    public double RespawnSeconds { get; init; } = 3;
    public float PursuitLeash { get; init; } = Tiles(12);
    public double RepathSeconds { get; init; } = 0.25;
    public double CombatTimeoutSeconds { get; init; } = 5;
    public double CommandMaxAge { get; init; } = 2;
    public double CommandBurst { get; init; } = 24;
    public double CommandsPerSecond { get; init; } = 12;
    public int TelemetryCapacity { get; init; } = 256;
    public IReadOnlyList<ClassDefinition> Classes { get; init; } = CreateClasses();
    public ClassDefinition Class(CharacterClass value) => Classes[(int)value];

    private static IReadOnlyList<ClassDefinition> CreateClasses() => Array.AsReadOnly(new[]
    {
        Define(CharacterClass.Warrior, 125, 5.3f, 9, 1.6f, DamageType.Physical,
            "Crushing Blow", 18, 1.6f, 0.7, "Rush", AbilityEffect.Rush, 4, "Shield Bash", 1.6f, "Guard"),
        Define(CharacterClass.Mage, 100, 5, 7, 6, DamageType.Magic,
            "Firebolt", 20, 6.5f, 1, "Blink", AbilityEffect.Blink, 3.2f, "Counterspell", 6, "Arcane Ward"),
        Define(CharacterClass.Archer, 110, 5, 8, 6.5f, DamageType.Physical,
            "Power Shot", 19, 6.5f, 0.85, "Backstep", AbilityEffect.Backstep, 3.2f, "Disrupting Arrow", 6.5f, "Evasive Stance")
    });

    private static ClassDefinition Define(CharacterClass kind, int hp, float speed, int basicDamage, float basicRange,
        DamageType type, string primary, int damage, float range, double cast, string mobility,
        AbilityEffect moveEffect, float travel, string interrupt, float interruptRange, string defense) =>
        new(kind, hp, Tiles(speed), Array.AsReadOnly(new[]
        {
            new AbilityDefinition(AbilitySlot.Basic, "Basic Attack", AbilityEffect.Damage, type, basicDamage, Tiles(basicRange), 0.2, 1.25, true),
            new AbilityDefinition(AbilitySlot.Primary, primary, AbilityEffect.Damage, type, damage, Tiles(range), cast, 7, true),
            new AbilityDefinition(AbilitySlot.Mobility, mobility, moveEffect, type, 0, Tiles(travel), 0, 16,
                TravelSeconds: moveEffect == AbilityEffect.Blink ? 0 : 0.18),
            new AbilityDefinition(AbilitySlot.Interrupt, interrupt, AbilityEffect.Interrupt, type, 5, Tiles(interruptRange), 0, 12),
            new AbilityDefinition(AbilitySlot.Defense, defense, AbilityEffect.Defense, type, 0, 0, 0, 16, false, 2, 0.35f)
        }));
}
