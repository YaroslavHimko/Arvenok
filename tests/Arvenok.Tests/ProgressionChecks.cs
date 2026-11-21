using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Arvenok.Contracts;
using Arvenok.Simulation;

namespace Arvenok.Tests;

internal static partial class Program
{
    private static void ProgressionLevels()
    {
        var rules = new ProgressionRules();
        for (int level = 1; level < 10; level++) Require(rules.ExperienceRequired(level) == 80 + 35 * (level - 1) + 12 * (level - 1) * (level - 1));
        CharacterRecord fresh = CharacterRecord.New(Guid.NewGuid());
        var one = rules.Award(fresh, 79); Require(one.Character.Level == 1 && one.Character.SkillPoints == 0 && one.Character.Experience == 79);
        var levelled = rules.Award(fresh, 85); Require(levelled.Character.Level == 2 && levelled.Character.Experience == 5 && levelled.Character.SkillPoints == 1 && levelled.Levels == 1);
        var multiple = rules.Award(fresh, 80 + 127 + 198 + 3);
        Require(multiple.Character.Level == 4 && multiple.Character.Experience == 3 && multiple.Character.SkillPoints == 3 && multiple.Levels == 3);
        var capped = rules.Award(fresh, long.MaxValue);
        Require(capped.Character.Level == 10 && capped.Character.Experience == 0 && capped.Character.SkillPoints == 9 && capped.Awarded == 4428);
        Require(rules.Award(capped.Character, 100).Character == capped.Character && rules.Award(capped.Character, 100).Awarded == 0);
    }

    private static void ProgressionUpgrades()
    {
        var rules = new ProgressionRules();
        CharacterRecord fresh = CharacterRecord.New(Guid.NewGuid()); string skill = rules.SkillId(fresh.Class, AbilitySlot.Primary);
        Require(!rules.TryUpgrade(fresh, skill, fresh.Revision, out _, out _));
        CharacterRecord level2 = rules.Award(fresh, 80).Character;
        Require(rules.TryUpgrade(level2, skill, level2.Revision, out CharacterRecord upgraded, out _));
        Require(upgraded.Ranks.Primary == 2 && upgraded.SkillPoints == 0 && upgraded.Revision == level2.Revision + 1);
        Require(!rules.TryUpgrade(upgraded, skill, level2.Revision, out _, out _));
        CharacterRecord level3 = rules.Award(upgraded, 127).Character;
        Require(!rules.TryUpgrade(level3, skill, level3.Revision, out _, out _));
        CharacterRecord level4 = rules.Award(level3, 198).Character;
        Require(rules.TryUpgrade(level4, skill, level4.Revision, out CharacterRecord rank3, out _) && rank3.Ranks.Primary == 3);
        Require(!rules.TryUpgrade(rank3, skill, rank3.Revision, out _, out _));
        CharacterRecord level7 = rules.Award(rank3, 293 + 412 + 555).Character;
        Require(rules.TryUpgrade(level7, skill, level7.Revision, out CharacterRecord rank4, out _) && rank4.Ranks.Primary == 4);
        Require(!rules.TryUpgrade(rank4, skill, rank4.Revision, out _, out _));
        Require(!rules.TryUpgrade(rank4, "mage.firebolt", rank4.Revision, out _, out _) && !rules.TryUpgrade(rank4, "basic", rank4.Revision, out _, out _));
        Require(rank4.SkillPoints + rank4.Ranks.Spent == rank4.Level - 1);
    }

    private static void ProgressionRankValues()
    {
        var rules = new ProgressionRules(); var balance = new CombatBalance();
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>()) for (int rank = 1; rank <= 4; rank++)
        {
            var ranks = new SkillRanks(rank, rank, rank, rank);
            Require(rules.Ability(kind, AbilitySlot.Basic, ranks) == balance.Class(kind).Ability(AbilitySlot.Basic));
            AbilityDefinition primary = rules.Ability(kind, AbilitySlot.Primary, ranks);
            Require(primary.Damage == balance.Class(kind).Ability(AbilitySlot.Primary).Damage + 2 * (rank - 1));
            Require(primary.CastSeconds == balance.Class(kind).Ability(AbilitySlot.Primary).CastSeconds && primary.CooldownSeconds == 7);
            Require(Math.Abs(rules.Ability(kind, AbilitySlot.Mobility, ranks).Range / 32 - ((kind == CharacterClass.Warrior ? 4 : 3.2) + 0.2 * (rank - 1))) < 0.0001);
            Require(rules.Ability(kind, AbilitySlot.Interrupt, ranks).Damage == 4 + rank && rules.Ability(kind, AbilitySlot.Interrupt, ranks).CooldownSeconds == 12 - 0.5 * (rank - 1));
            AbilityDefinition defense = rules.Ability(kind, AbilitySlot.Defense, ranks);
            Require(Math.Abs(defense.EffectSeconds - (2 + 0.2 * (rank - 1))) < 0.0001 && defense.Reduction == 0.35f);
        }
    }

    private static void ProgressionValidation()
    {
        var rules = new ProgressionRules(); CharacterRecord fresh = CharacterRecord.New(Guid.NewGuid());
        foreach (CharacterRecord invalid in new[] { fresh with { Level = 11 }, fresh with { Experience = 80 }, fresh with { Experience = -1 },
            fresh with { SkillPoints = 1 }, fresh with { Ranks = SkillRanks.Initial with { Primary = 0 } }, fresh with { Id = Guid.Empty },
            fresh with { SchemaVersion = 99 }, fresh with { Ranks = SkillRanks.Initial with { Primary = 4 } } }) InvalidSave(() => rules.Validate(invalid));
        CharacterRecord max = rules.Award(fresh, 4428).Character;
        foreach (AbilitySlot slot in ProgressionRules.SkillSlots.Take(3)) for (int i = 0; i < 3; i++)
        { Require(rules.TryUpgrade(max, rules.SkillId(max.Class, slot), max.Revision, out CharacterRecord updated, out _)); max = updated; }
        Require(max.SkillPoints == 0 && max.Ranks.Defense == 1 && max.Ranks.Spent == 9);
        CharacterRecord reset = rules.ResetSkills(max); rules.Validate(reset);
        Require(reset.Ranks == SkillRanks.Initial && reset.SkillPoints == 9 && reset.Experience == max.Experience && reset.Level == 10);
    }
    private static void InvalidSave(Action action)
    {
        try { action(); } catch (System.IO.InvalidDataException) { return; }
        throw new InvalidOperationException("Expected invalid save data.");
    }

    private static void ExperienceShares()
    {
        var ledger = new MonsterExperienceLedger();
        ledger.Record(1, 50, 100, 0); ledger.Record(2, 999, 50, 1);
        var players = new Dictionary<long, ExperienceRecipient> { [1] = new(1, 1, true, Vector2.Zero), [2] = new(2, 1, true, Vector2.Zero) };
        var shares = ledger.Resolve(1, 100, 13, 1, Vector2.Zero, 1, players);
        Require(shares.Count == 2 && shares[0].BaseShare == 7 && shares[1].BaseShare == 6 && shares.Sum(p => p.Experience) == 13);
        Require(ledger.TrackedHits == 0 && ledger.Resolve(1, 100, 13, 1, Vector2.Zero, 1, players).Count == 0);
        ledger.Respawn(); ledger.Record(2, 100, 100, 2);
        Require(ledger.Resolve(1, 100, 13, 1, Vector2.Zero, 2, players).Count == 0, "A delayed old death cannot consume the new life.");
        Require(ledger.Resolve(2, 100, 13, 1, Vector2.Zero, 2, players).Single().Experience == 13);
    }
    private static void ExperienceEligibility()
    {
        var ledger = new MonsterExperienceLedger();
        var players = Enumerable.Range(1, 6).ToDictionary(i => (long)i, i => new ExperienceRecipient(i, 1, true, Vector2.Zero));
        players[2] = players[2] with { Alive = false }; players[3] = players[3] with { Position = new Vector2(385, 0) };
        players.Remove(4);
        ledger.Record(1, 9, 100, 10); ledger.Record(2, 20, 91, 10); ledger.Record(3, 20, 71, 10);
        ledger.Record(4, 20, 51, 10); ledger.Record(5, 20, 31, 10); ledger.Record(6, 11, 11, 10);
        ledger.RemoveAttacker(5);
        Require(ledger.Resolve(1, 100, 10, 1, Vector2.Zero, 25, players).Single().EntityId == 6, "Only the living nearby 10% contributor is eligible at the inclusive 15 second boundary.");
        ledger.Respawn(); ledger.Record(6, 100, 100, 30); ledger.Prune(45.001);
        Require(ledger.TrackedHits == 0 && ledger.Resolve(2, 100, 10, 1, Vector2.Zero, 46, players).Count == 0);
    }
    private static void ExperiencePenalties()
    {
        var players = new Dictionary<long, ExperienceRecipient> { [1] = new(1, 4, true, Vector2.Zero), [2] = new(2, 5, true, Vector2.Zero) };
        var ledger = new MonsterExperienceLedger(); ledger.Record(1, 50, 100, 0); ledger.Record(2, 50, 50, 0);
        var awards = ledger.Resolve(1, 100, 13, 1, Vector2.Zero, 0, players);
        Require(awards[0].Experience == 4 && awards[1].Experience == 2);
        for (int difference = -3; difference <= 6; difference++)
        {
            ledger.Respawn(); ledger.Record(1, 100, 100, 0); players[1] = players[1] with { Level = 4 + difference };
            int expected = difference <= 2 ? 10 : difference == 3 ? 5 : difference == 4 ? 3 : 0;
            Require(ledger.Resolve(ledger.Life, 100, 10, 4, Vector2.Zero, 0, players).Single().Experience == expected);
        }
    }
    private static Fixture ExperienceFixture()
    {
        var spawn = new MonsterSpawn("experience-slime", MonsterSpecies.Slime, new Vector2(520, 448), 20, 0.001f, 32, 1, 3, 0.2, 1, 10);
        var map = new WorldMap(WorldMap.Outdoor with { MonsterSpawns = Array.AsReadOnly(new[] { spawn }) });
        return new Fixture(CharacterClass.Mage, map: map);
    }
    private static void ExperienceCombat()
    {
        var f = ExperienceFixture(); long monster = f.Game.GetSnapshots().Single(e => e.Kind == EntityKind.Monster).Id;
        for (int kill = 0; kill < 8; kill++) { Require(f.Send(f.A, CommandKind.Attack, monster)); f.Step(4.2); }
        CharacterRecord record = f.Game.Character(f.A)!;
        Require(record.Level == 2 && record.Experience == 0 && record.SkillPoints == 1 && record.Ranks == SkillRanks.Initial);
        Require(f.Game.RecentActivity.Count(e => e.Kind == CombatEventKind.LevelUp && e.ActorId == f.A) == 1);
        f.Step(20); Require(f.Game.Character(f.A) == record);
        var pvp = new Fixture(CharacterClass.Mage, balance: LowHp(7)); pvp.Send(pvp.A, CommandKind.Attack, pvp.B); pvp.Step(0.21);
        Require(!pvp.State(pvp.B).Alive && pvp.Game.Character(pvp.A)!.Experience == 0 && pvp.Game.Character(pvp.B)!.Level == 1);
    }
    private static void ExperienceMultiplayer()
    {
        var f = ExperienceFixture(); long monster = f.Game.GetSnapshots().Single(e => e.Kind == EntityKind.Monster).Id;
        f.Skill(f.B, monster, AbilitySlot.Interrupt); f.Skill(f.A, monster, AbilitySlot.Primary); f.Step(1.01);
        Require(f.Game.Character(f.A)!.Experience == 8 && f.Game.Character(f.B)!.Experience == 2);
        Require(f.Game.DrainProgressionUpdates().Sum(u => u.AwardedExperience) == 10);
    }
    private static void RankedCombat()
    {
        var rules = new ProgressionRules();
        CharacterRecord character = rules.Award(CharacterRecord.New(Guid.NewGuid(), CharacterClass.Mage), 405).Character;
        var f = new Fixture(characterA: character); Require(f.State(f.A).Class == CharacterClass.Mage);
        Require(f.Skill(f.A, f.B, AbilitySlot.Primary)); f.Step(0.3);
        var upgrade = new WorldCommand(f.Next(f.A), f.Game.Time, CommandKind.UpgradeSkill, SkillId: "mage.firebolt", ProgressionRevision: f.Game.Character(f.A)!.Revision);
        Require(f.Game.Submit(f.A, upgrade) && !f.Game.Submit(f.A, upgrade));
        Require(!f.Game.Submit(f.A, upgrade with { Sequence = f.Next(f.A), SkillId = "mage.arcane-ward" }));
        Require(f.Game.Character(f.A)!.SkillPoints == 2 && f.Game.Character(f.A)!.Ranks.Primary == 2);
        f.Step(0.71); Require(f.State(f.B).Hp == f.State(f.B).MaxHp - 20, "An already accepted cast keeps rank 1 damage.");
        f.Step(6); Require(f.Skill(f.A, f.B, AbilitySlot.Primary)); f.Step(1.01);
        Require(f.State(f.B).Hp == f.State(f.B).MaxHp - 20 - 22, "The next accepted cast uses rank 2.");
        Require(f.State(f.A).MaxHp == 100 && f.State(f.A).Level == 4);
    }
    private static void ProgressionReconnect()
    {
        var f = ExperienceFixture(); long monster = f.Game.GetSnapshots().Single(e => e.Kind == EntityKind.Monster).Id;
        f.Skill(f.A, monster, AbilitySlot.Primary); f.Step(1.01);
        CharacterRecord saved = f.Game.Character(f.A)!;
        Require(f.Game.Join(999, character: saved) == 0, "One persistent character cannot be joined twice.");
        Require(f.Game.Leave(101)); long replacement = f.Game.Join(101, character: saved);
        Require(replacement != f.A && f.Game.Character(replacement) == saved && f.State(replacement).Protected);
        var restarted = new WorldSimulation(); long restored = restarted.Join(1001, character: saved);
        Require(restarted.Character(restored) == saved && !restarted.GetSnapshots().Single(e => e.Id == restored).CanChooseClass);
    }

    private static void RankedCombatEffects()
    {
        var rules = new ProgressionRules();
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
        {
            CharacterRecord basis = rules.Award(CharacterRecord.New(Guid.NewGuid(), kind), 1665).Character;
            CharacterRecord Ranked(AbilitySlot slot) => basis with { Ranks = SkillRanks.Initial.WithRank(slot, 4), SkillPoints = basis.SkillPoints - 3 };
            var movement = new Fixture(characterA: Ranked(AbilitySlot.Mobility));
            movement.Position(new Vector2(400, 640), new Vector2(kind == CharacterClass.Archer ? 440 : 580, 640));
            Vector2 start = movement.State(movement.A).Position;
            Require(movement.Send(movement.A, CommandKind.UseAbility, movement.B, AbilitySlot.Mobility, new Vector2(900, 640)));
            movement.Step(0.21);
            float range = rules.Ability(kind, AbilitySlot.Mobility, Ranked(AbilitySlot.Mobility).Ranks).Range;
            Require(Math.Abs(Vector2.Distance(start, movement.State(movement.A).Position) - range) < 0.01, "Rank 4 movement distance must reach the simulation.");
            Require(movement.State(movement.B).Hp == movement.State(movement.B).MaxHp && movement.Game.Map.CanTraverse(start, movement.State(movement.A).Position));

            var interrupt = new Fixture(b: CharacterClass.Mage, characterA: Ranked(AbilitySlot.Interrupt));
            Require(interrupt.Skill(interrupt.B, interrupt.A, AbilitySlot.Primary));
            double accepted = interrupt.Game.Time;
            Require(interrupt.Skill(interrupt.A, interrupt.B, AbilitySlot.Interrupt));
            Require(!interrupt.State(interrupt.B).Casting && interrupt.State(interrupt.B).Hp == 92);
            Require(Math.Abs(interrupt.State(interrupt.A).Cooldowns.Interrupt - accepted - 10.5) < 0.0001);

            var defense = new Fixture(b: CharacterClass.Mage, characterA: Ranked(AbilitySlot.Defense));
            Require(defense.Skill(defense.A, 0, AbilitySlot.Defense));
            double ends = defense.Game.Time + 2.6;
            Require(Math.Abs(defense.State(defense.A).DefenseEndsAt - ends) < 0.0001);
            defense.Step(1.3); Require(defense.Skill(defense.B, defense.A, AbilitySlot.Primary)); defense.Step(1.01);
            Require(defense.State(defense.A).Hp == defense.State(defense.A).MaxHp - 13, "Rank 4 defense still reduces damage at 2.31 seconds.");
            defense.Step(0.3); Require(defense.State(defense.A).DefenseEndsAt == 0);
            Require(defense.State(defense.A).MaxHp == defense.Game.Balance.Class(kind).MaxHp);
        }
    }
}
