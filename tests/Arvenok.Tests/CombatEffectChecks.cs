using System;
using System.Linq;
using Arvenok.Contracts;
using Arvenok.Simulation;

namespace Arvenok.Tests;

internal static partial class Program
{
    private static void DefensePriority()
    {
        var state = new DefensiveState(); state.Dodge(0.25); state.Parry(0.35); state.PerfectBlock(0.4);
        Require(state.Resolve(1, 0.1, 20, DamageType.Physical, true, true, 0.35f).Outcome == HitOutcome.Dodged);
        Require(state.Resolve(1, 0.1, 20, DamageType.Physical, true, true, 0.35f).Outcome == HitOutcome.Duplicate);
        Require(state.ParryEndsAt == 0.35 && state.PerfectBlockEndsAt == 0.4);
        Require(state.Resolve(2, 0.1, 20, DamageType.Physical, true, true, 0.35f).Outcome == HitOutcome.Parried);
        Require(state.Resolve(3, 0.1, 20, DamageType.Magic, true, true, 0.35f).Outcome == HitOutcome.PerfectBlocked);
        Require(state.Resolve(4, 0.1, 20, DamageType.Physical, true, true, 0.35f).Damage == 13);
        foreach (bool front in new[] { true, false })
        {
            var parry = new DefensiveState(); parry.Parry(0.35);
            Require(parry.Resolve(1, 0.1, 20, DamageType.Magic, true, front, 0).Damage == 20);
            Require(parry.Resolve(2, 0.1, 20, DamageType.Physical, false, front, 0).Damage == 20);
            Require(parry.Resolve(3, 0.1, 20, DamageType.Physical, true, front, 0).Damage == (front ? 0 : 20));
        }
        var dodge = new DefensiveState(); dodge.Dodge(0.25);
        Require(dodge.Resolve(1, 0.1, 20, DamageType.Physical, false, true, 0).Damage == 20);
        Require(dodge.Resolve(2, 0.25, 20, DamageType.Physical, true, true, 0).Damage == 20);
        var rear = new DefensiveState(); rear.PerfectBlock(0.4);
        Require(rear.Resolve(1, 0.2, 20, DamageType.Magic, true, false, 0).Damage == 20);
        Require(rear.Resolve(2, 0.4, 20, DamageType.Magic, true, true, 0).Damage == 20);
    }
    private static void DefenseAbsorption()
    {
        var state = new DefensiveState(); state.Shell(2, 16);
        Require(state.Resolve(1, 0.1, 10, DamageType.Physical, true, true, 0.35f).Damage == 0 && state.ShellRemaining == 6);
        Require(state.Resolve(2, 0.2, 20, DamageType.Magic, true, true, 0.35f).Damage == 13 && state.ShellRemaining == 6);
        Require(state.Resolve(3, 0.3, 10, DamageType.Magic, true, true, 0.35f).Damage == 4 && state.ShellRemaining == 0);
        state.Shell(2, 16); Require(state.Resolve(4, 2, 20, DamageType.Magic, true, true, 0).Damage == 20);
        state.Dodge(3); state.Clear(); Require(state.Resolve(5, 2.1, 20, DamageType.Magic, true, true, 0).Damage == 20);
    }
    private static void LifestealCaps()
    {
        var rules = new CombatEffectRules(); var state = new LifestealState();
        Require(state.Heal(rules, 1, 1, 9, false, 20, true) == 0);
        Require(state.Heal(rules, 2, 1, 9, false, 20, true) == 1);
        Require(state.Heal(rules, 2, 1, 9, false, 20, true) == 0);
        Require(state.Heal(rules, 3, 1, 100, false, 20, true) == 5);
        Require(state.Heal(rules, 3, 2, 100, false, 20, true) == 0);
        Require(state.Heal(rules, 4, 1, 100, true, 20, true) == 3);
        Require(state.Heal(rules, 4, 2, 100, true, 20, true) == 0);
        Require(state.Heal(rules, 5, 1, 100, false, 2, true) == 2);
        Require(state.Heal(rules, 6, 1, 100, false, 0, true) == 0);
        Require(state.Heal(rules, 7, 1, 100, false, 20, false) == 0);
        Require(state.Heal(rules, 8, 1, 0, false, 20, true) == 0);
    }
    private static void EvasiveWindow()
    {
        var f = new Fixture(CharacterClass.Mage, CharacterClass.Archer);
        Require(f.Send(f.B, CommandKind.UseAbility, slot: AbilitySlot.Defense));
        int hp = f.State(f.B).Hp;
        Require(f.Send(f.A, CommandKind.UseAbility, f.B, AbilitySlot.Interrupt));
        Require(f.State(f.B).Hp == hp && f.Game.RecentActivity.Any(e => e.Kind == CombatEventKind.Dodged && e.TargetId == f.B));
        Require(f.State(f.B).DodgeEndsAt == 0 && f.State(f.B).DefenseEndsAt > f.Game.Time);
        f.Step(0.3); Require(f.Send(f.A, CommandKind.UseAbility, f.B, AbilitySlot.Primary)); f.Step(1.1);
        Require(f.State(f.B).Hp == hp - CombatRules.Damage(20, 0.35f));
    }
}
