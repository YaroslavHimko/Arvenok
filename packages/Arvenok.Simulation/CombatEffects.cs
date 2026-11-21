using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>Server content. Presentation never submits these values.</summary>
public sealed class CombatEffectRules
{
    public double DodgeSeconds { get; }
    public double ParrySeconds { get; }
    public double PerfectBlockSeconds { get; }
    public float GuardReduction { get; }
    public float ShieldPhysicalReduction { get; }
    public float ShieldMagicReduction { get; }
    public float MasteryPhysicalReduction { get; }
    public int PvePercent { get; }
    public int PvpPercent { get; }
    public int PveHealCap { get; }
    public int PvpHealCap { get; }
    public CombatEffectRules()
    {
        using Stream stream = typeof(CombatEffectRules).Assembly.GetManifestResourceStream("Arvenok.Skills.Effects") ?? throw new InvalidDataException("Missing effect rules.");
        using JsonDocument document = JsonDocument.Parse(stream); JsonElement root = document.RootElement;
        if (root.GetProperty("probabilistic_avoidance").GetBoolean()) throw new InvalidDataException("Random avoidance is not supported.");
        var effects = root.GetProperty("effects"); var block = effects.GetProperty("block"); var heal = effects.GetProperty("lifesteal");
        DodgeSeconds = effects.GetProperty("dodge").GetProperty("active_window_s").GetDouble();
        ParrySeconds = effects.GetProperty("parry").GetProperty("window_s").GetDouble();
        PerfectBlockSeconds = block.GetProperty("perfect_block_window_s").GetDouble();
        GuardReduction = block.GetProperty("guard_base_reduction").GetSingle();
        ShieldPhysicalReduction = block.GetProperty("shield_guard_physical_reduction").GetSingle();
        ShieldMagicReduction = block.GetProperty("shield_guard_magic_reduction").GetSingle();
        MasteryPhysicalReduction = block.GetProperty("shield_mastery_physical_reduction").GetSingle();
        PvePercent = (int)Math.Round(heal.GetProperty("pve_fraction").GetDouble() * 100);
        PvpPercent = (int)Math.Round(heal.GetProperty("pvp_fraction").GetDouble() * 100);
        PveHealCap = heal.GetProperty("heal_cap_per_skill_activation_pve_hp").GetInt32(); PvpHealCap = heal.GetProperty("heal_cap_per_skill_activation_pvp_hp").GetInt32();
        foreach (double value in new[] { DodgeSeconds, ParrySeconds, PerfectBlockSeconds }) if (!double.IsFinite(value) || value <= 0 || value > 1) throw new InvalidDataException("Invalid defensive window.");
        foreach (float value in new[] { GuardReduction, ShieldPhysicalReduction, ShieldMagicReduction, MasteryPhysicalReduction }) if (!float.IsFinite(value) || value < 0 || value >= 1) throw new InvalidDataException("Invalid mitigation.");
        if (PvePercent is < 0 or > 100 || PvpPercent is < 0 or > 100 || PveHealCap is < 0 or > 100 || PvpHealCap is < 0 or > 100) throw new InvalidDataException("Invalid lifesteal.");
    }
}

public enum HitOutcome { Damage, Dodged, Parried, PerfectBlocked, Absorbed, Duplicate }
public readonly record struct HitResolution(int Damage, HitOutcome Outcome, bool Mitigated);

/// <summary>One charge per window. Each authoritative activation may hit a given entity only once.</summary>
public sealed class DefensiveState
{
    private readonly HashSet<long> _hits = new();
    private readonly Queue<long> _order = new();
    private long _retired;
    public double DodgeEndsAt { get; private set; }
    public double ParryEndsAt { get; private set; }
    public double PerfectBlockEndsAt { get; private set; }
    public double ShellEndsAt { get; private set; }
    public int ShellRemaining { get; private set; }
    public void Dodge(double until) => DodgeEndsAt = until;
    public void Parry(double until) => ParryEndsAt = until;
    public void PerfectBlock(double until) => PerfectBlockEndsAt = until;
    public void Shell(double until, int amount) { ShellEndsAt = until; ShellRemaining = amount; }
    public void Clear() { DodgeEndsAt = ParryEndsAt = PerfectBlockEndsAt = ShellEndsAt = 0; ShellRemaining = 0; }
    public HitResolution Resolve(long activation, double now, int rawDamage, DamageType type, bool directTargeted, bool frontal, float reduction)
    {
        if (activation <= _retired || !_hits.Add(activation)) return new(0, HitOutcome.Duplicate, false);
        _order.Enqueue(activation);
        if (_order.Count > 4096) { long removed = _order.Dequeue(); _hits.Remove(removed); _retired = Math.Max(_retired, removed); }
        if (directTargeted && now < DodgeEndsAt) { DodgeEndsAt = 0; return new(0, HitOutcome.Dodged, true); }
        if (directTargeted && frontal && type == DamageType.Physical && now < ParryEndsAt) { ParryEndsAt = 0; return new(0, HitOutcome.Parried, true); }
        if (directTargeted && frontal && now < PerfectBlockEndsAt) { PerfectBlockEndsAt = 0; return new(0, HitOutcome.PerfectBlocked, true); }
        int damage = CombatRules.Damage(rawDamage, reduction);
        bool absorbed = false;
        if (now < ShellEndsAt && ShellRemaining > 0)
        {
            // A single strongest defense wins; an absorb is not multiplied by ordinary mitigation.
            int shellDamage = Math.Max(0, rawDamage - ShellRemaining);
            if (shellDamage <= damage) { int amount = Math.Min(rawDamage, ShellRemaining); ShellRemaining -= amount; damage = shellDamage; absorbed = amount > 0; }
        }
        return new(damage, absorbed ? HitOutcome.Absorbed : HitOutcome.Damage, absorbed || damage < rawDamage);
    }
}

/// <summary>Integer hundredths avoid random rounding. Caps cover the complete activation, including multiple targets.</summary>
public sealed class LifestealState
{
    private readonly Dictionary<long, int> _healed = new();
    private readonly HashSet<(long Activation, long Target)> _hits = new();
    private readonly Queue<long> _order = new();
    private long _retired;
    private int _fraction;
    public int Heal(CombatEffectRules rules, long activation, long target, int actualHpDamage, bool pvp, int missingHp, bool eligible)
    {
        if (!eligible || actualHpDamage <= 0 || activation <= _retired || !_hits.Add((activation, target))) return 0;
        if (!_healed.ContainsKey(activation))
        {
            _healed.Add(activation, 0); _order.Enqueue(activation);
            if (_order.Count > 128) { long old = _order.Dequeue(); _retired = Math.Max(_retired, old); _healed.Remove(old); _hits.RemoveWhere(h => h.Activation == old); }
        }
        int available = Math.Max(0, (pvp ? rules.PvpHealCap : rules.PveHealCap) - _healed[activation]);
        if (available == 0 || missingHp <= 0) return 0;
        long credit = _fraction + (long)actualHpDamage * (pvp ? rules.PvpPercent : rules.PvePercent);
        int heal = (int)Math.Min(Math.Min(credit / 100, available), missingHp);
        _fraction = (int)(credit % 100); _healed[activation] += heal;
        return heal;
    }
}
