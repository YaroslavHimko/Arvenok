using System.Collections.Generic;
using System.Numerics;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

internal sealed class CombatEntity
{
    public required long Id;
    public required long PeerId;
    public EntityKind Kind = EntityKind.Player;
    public CharacterClass Class;
    public string Name = "";
    public int MaxHp;
    public float MoveSpeed;
    public MonsterWander? Monster;
    public MonsterExperienceLedger? ExperienceLedger;
    public long LootedLife;
    public CharacterRecord? Character;
    public Vector2 Position;
    public Vector2 Facing = Vector2.UnitX;
    public int Hp;
    public int FactionId = 0;
    public bool CombatEnabled = true;
    public bool CanChooseClass = true;
    public long TargetId;
    public long AttackTargetId;
    public Vector2 ChaseOrigin;
    public double NextRepathAt;
    public List<Vector2>? Path;
    public int Waypoint;
    public bool Dashing;
    public Vector2 DashDestination;
    public float DashSpeed;
    public double DashEndsAt;
    public AbilitySlot DashAbility = AbilitySlot.Mobility;
    public bool Casting;
    public AbilitySlot ActiveAbility;
    public long CastTargetId;
    public double CastStartedAt;
    public double CastEndsAt;
    public AbilityDefinition? AcceptedCast;
    public double RecoveryEndsAt;
    public double DefenseEndsAt;
    public string DefenseName = "";
    public float DamageReduction;
    public float PhysicalReduction;
    public readonly DefensiveState Defenses = new();
    public readonly LifestealState Lifesteal = new();
    public long Activation;
    public bool SanguineEdge { get; set; }
    public double RespawnAt;
    public double DiedAt;
    public readonly double[] Cooldowns = new double[7];
    public bool InCombat;
    public double LastCombatAt = double.NegativeInfinity;
    public long LastSequence;
    public double Tokens;
    public double LastTokenTime;
    public double LastRateNotice = -1;
    public double InventoryTokens = 12;
    public double LastInventoryTokenTime;
    public bool Moving => Path is not null && Waypoint < Path.Count;
}
