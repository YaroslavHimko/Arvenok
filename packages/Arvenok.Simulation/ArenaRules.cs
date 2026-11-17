using System.Numerics;

namespace Arvenok.Simulation;

/// <summary>Rules for the existing duel, not global rules for every future game mode.</summary>
public static class ArenaRules
{
    public const int MaxPlayers = 2;
    public const int MaxHp = 100;
    public const float MoveSpeed = 185f;
    public const float MeleeRange = 90f;
    public const float SpellRange = 240f;
    public const double SpellCastSeconds = 1.25;
    public const double SpellCooldownSeconds = 1.60;
    public const double MeleeCooldownSeconds = 0.80;
    public const double RespawnSeconds = 3.0;
    public static readonly Vector2 MinimumPosition = new(100, 130);
    public static readonly Vector2 MaximumPosition = new(900, 510);
}
