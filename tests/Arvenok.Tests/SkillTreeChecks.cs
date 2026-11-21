using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arvenok.Contracts;
using Arvenok.Hosting;
using Arvenok.Simulation;

namespace Arvenok.Tests;

internal static partial class Program
{
    private static CharacterRecord BranchCharacter(CharacterClass kind, string weapon, bool shield, params string[] suffixes)
    {
        var progress = new ProgressionRules(); var c = WeaponCharacter(kind, weapon, shield) with { Level = 10, SkillPoints = 9 };
        var learned = new HashSet<string>(); SkillRanks ranks = c.Ranks;
        void AddNode(SkillNodeDefinition node)
        {
            if (!learned.Add(node.Id)) return;
            if (progress.Tree.Find(node.PrerequisiteId) is SkillNodeDefinition prior) AddNode(prior);
            else { var slot = ProgressionRules.SkillSlots.Single(s => progress.SkillId(kind, s) == node.PrerequisiteId); ranks = ranks.WithRank(slot, Math.Max(ranks.Rank(slot), node.PrerequisiteRank)); }
        }
        foreach (string suffix in suffixes) AddNode(progress.Tree.Nodes.Single(n => n.Class == kind && n.SkillId.EndsWith("." + suffix, StringComparison.Ordinal)));
        c = c with { Ranks = ranks, Tree = new SkillTreeState(learned.OrderBy(n => n, StringComparer.Ordinal).Select(n => new LearnedNode(n, 1)).ToArray()), SkillPoints = 9 - ranks.Spent - learned.Count };
        new CharacterStateRules().Validate(c); return c;
    }
    private static EconomyRequest LearnRequest(CharacterRecord c, SkillNodeDefinition node, Guid scroll = default) =>
        new(Guid.NewGuid(), c.Inventory!.Revision, EconomyOperation.LearnSkill, scroll) { NodeId = node.Id, ProgressionRevision = c.Revision };
    private static void TreeLearning()
    {
        var progress = new ProgressionRules(); var inventory = new InventoryRules();
        Require(progress.Tree.Nodes.Count == 12 && progress.Tree.Nodes.Count(n => n.Passive) == 6);
        foreach (var node in progress.Tree.Nodes.Where(n => n.Passive))
        {
            var c = CharacterRecord.New(Guid.NewGuid(), node.Class) with { Level = 4, SkillPoints = 3, ClassLocked = true };
            var missing = inventory.PrepareLearning(c, LearnRequest(c, node), progress.Tree); Require(!missing.Accepted && missing.Character == c);
            AbilitySlot prerequisite = ProgressionRules.SkillSlots.Single(s => progress.SkillId(node.Class, s) == node.PrerequisiteId);
            c = c with { Ranks = c.Ranks.WithRank(prerequisite, 2), SkillPoints = 2 };
            var request = LearnRequest(c, node); var result = inventory.PrepareLearning(c, request, progress.Tree);
            Require(result.Accepted && result.Character.Tree.Has(node.Id) && result.Character.SkillPoints == 1 && result.Character.Ranks == c.Ranks);
            Require(inventory.PrepareLearning(result.Character, request, progress.Tree).Duplicate);
            Require(!inventory.PrepareLearning(result.Character, LearnRequest(result.Character, node), progress.Tree).Accepted);
            Require(!inventory.PrepareLearning(c, request with { ProgressionRevision = c.Revision + 1 }, progress.Tree).Accepted);
            new CharacterStateRules().Validate(result.Character);
            var wrong = c with { Class = (CharacterClass)(((int)c.Class + 1) % 3) };
            Require(!inventory.PrepareLearning(wrong, LearnRequest(wrong, node), progress.Tree).Accepted);
        }
        var blank = new Fixture(); Require(!blank.Skill(blank.A, blank.B, AbilitySlot.BranchOne));
        Require(!blank.Skill(blank.A, blank.B, AbilitySlot.BranchTwo));
    }
    private static void TreeMigration() => WithSaveDirectory(directory =>
    {
        var progress = new ProgressionRules(); var rules = new CharacterStateRules(progress); var inventory = new InventoryRules();
        var original = WeaponCharacter(CharacterClass.Warrior, "equipment.rustleaf_sword", true) with { SchemaVersion = 3, Ranks = new(2,2,1,2), SkillPoints = 2 };
        string folder = Path.Combine(directory, "characters"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, original.Id.ToString("N") + ".json"), System.Text.Json.JsonSerializer.Serialize(original));
        using var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade);
        CharacterRecord migrated = repository.Load(original.Id);
        Require(migrated.SchemaVersion == 4 && migrated.Ranks == original.Ranks && migrated.SkillPoints == original.SkillPoints && migrated.Inventory == original.Inventory && migrated.Equipment == original.Equipment && migrated.Tree.Nodes.Count == 0);
        Require(repository.Load(original.Id) == migrated && rules.Upgrade(migrated) == migrated);
        var node = progress.Tree.Nodes.Single(n => n.SkillId == "warrior.shield_mastery");
        var world = new WorldSimulation(); long id = world.Join(101, character: migrated); var request = LearnRequest(migrated, node);
        try { world.SubmitEconomy(101, request, _ => throw new IOException("disk full")); throw new Exception("Expected storage failure"); } catch (IOException) { }
        Require(world.Character(id) == migrated && repository.Load(original.Id) == migrated);
        Require(world.SubmitEconomy(101, request, repository.Save)!.Accepted);
        CharacterRecord learned = repository.Load(original.Id); Require(learned.Tree.Has(node.Id) && learned.SkillPoints == original.SkillPoints - 1);
        Require(world.SubmitEconomy(101, request, repository.Save)!.Duplicate);
        InvalidSave(() => rules.Validate(learned with { Tree = new SkillTreeState(learned.Tree.Nodes.Concat(learned.Tree.Nodes).ToArray()) }));
        InvalidSave(() => rules.Validate(learned with { Ranks = SkillRanks.Initial }));
    });
    private static void BranchActiveSkills()
    {
        var tree = new ProgressionRules().Tree;
        foreach (var node in tree.Nodes.Where(n => !n.Passive))
        {
            string weapon = node.WeaponRequirement switch { "staff" => "equipment.willow_staff", "bow" => "equipment.elm_shortbow", "dagger" => "equipment.weapon.hunters_dagger", "two_handed_sword" => "equipment.weapon.weathered_greatsword", _ => "equipment.rustleaf_sword" };
            var c = BranchCharacter(node.Class, weapon, node.WeaponRequirement == "shield", node.SkillId.Split('.')[^1]);
            var f = new Fixture(characterA: c);
            if (node.Ability!.Effect == AbilityEffect.Sidestep) f.Position(new(480,448), new(500,448));
            double start = f.Game.Time; var position = f.State(f.A).Position;
            Require(f.Skill(f.A, f.B, node.Slot), node.Name + " should start");
            Require(Math.Abs(f.State(f.A).Cooldowns.EndsAt(node.Slot) - start - node.Ability.CooldownSeconds) < 0.0001);
            if (node.Ability.Effect == AbilityEffect.Damage || node.Ability.Effect == AbilityEffect.Sidestep)
            {
                f.Step(node.Ability.CastSeconds + 0.02);
                int bonus = new InventoryRules().EquipmentBonuses(c).Damage;
                Require(f.Game.RecentActivity.Last(e => e.Kind == CombatEventKind.Damage && e.ActorId == f.A).Damage == node.Ability.Damage + bonus, node.Name + " damage");
            }
            if (node.Ability.Effect == AbilityEffect.Sidestep) Require(System.Numerics.Vector2.Distance(position, f.State(f.A).Position) <= 38.401 && !f.State(f.A).Casting);
            if (node.Ability.Effect == AbilityEffect.Defense)
            {
                f.Step(0.3); Require(!f.Skill(f.A, f.B, AbilitySlot.Defense), "Shared defense cooldown");
                Require(f.State(f.A).Cooldowns.Defense == f.State(f.A).Cooldowns.EndsAt(node.Slot));
            }
        }
    }
    private static void PassiveEffects()
    {
        foreach (CharacterClass kind in new[] { CharacterClass.Mage, CharacterClass.Archer })
        {
            string passive = kind == CharacterClass.Mage ? "embers" : "keen_eye", weapon = kind == CharacterClass.Mage ? "equipment.willow_staff" : "equipment.elm_shortbow";
            var c = BranchCharacter(kind, weapon, false, passive);
            var f = new Fixture(characterA: c); Require(f.Skill(f.A, f.B, AbilitySlot.Primary)); f.Step(1.1);
            int core = new ProgressionRules().Ability(kind, AbilitySlot.Primary, c.Ranks).Damage;
            Require(f.State(f.B).MaxHp - f.State(f.B).Hp == core + 2);
            var map = WorldMap.Outdoor with { MonsterSpawns = new[] { WorldMap.Outdoor.MonsterSpawns[0] with { Position = new(540,448), RoamRadius = 0, MaxHp = 200 } } };
            var pve = new Fixture(characterA: c, map: new WorldMap(map)); long monster = pve.Game.GetSnapshots().Single(e => e.Kind == EntityKind.Monster).Id;
            Require(pve.Skill(pve.A, monster, AbilitySlot.Primary)); pve.Step(1.1); Require(pve.State(monster).MaxHp - pve.State(monster).Hp == core + 3);
        }
        var ward = new Fixture(characterA: BranchCharacter(CharacterClass.Mage, "equipment.willow_staff", false, "ward_weaving"));
        Require(ward.Skill(ward.A, ward.B, AbilitySlot.Defense)); Require(Math.Abs(ward.State(ward.A).DefenseEndsAt - ward.Game.Time - 2.4) < 0.001);
        var foot = new Fixture(characterA: BranchCharacter(CharacterClass.Archer, "equipment.weapon.hunters_dagger", false, "light_footwork"));
        Require(foot.Skill(foot.A, foot.B, AbilitySlot.Mobility)); Require(Math.Abs(foot.State(foot.A).Cooldowns.Mobility - foot.Game.Time - 15) < 0.001);
        var mastery = new Fixture(characterA: BranchCharacter(CharacterClass.Warrior, "equipment.rustleaf_sword", true, "shield_mastery"));
        Require(mastery.Skill(mastery.A, mastery.B, AbilitySlot.Defense)); Require(mastery.Skill(mastery.B, mastery.A, AbilitySlot.Primary)); mastery.Step(0.72);
        Require(mastery.State(mastery.A).MaxHp - mastery.State(mastery.A).Hp == 9);
        var blood = new Fixture(characterA: BranchCharacter(CharacterClass.Warrior, "equipment.weapon.weathered_greatsword", false, "sanguine_edge"));
        Require(blood.Skill(blood.B, blood.A, AbilitySlot.Interrupt)); Require(blood.Skill(blood.A, blood.B, AbilitySlot.Primary)); blood.Step(0.72);
        Require(blood.State(blood.A).Hp == blood.State(blood.A).MaxHp - 4 && blood.Game.RecentActivity.Single(e => e.Kind == CombatEventKind.Healed).Damage == 1);
    }
    private static void PersistentCooldowns()
    {
        var c = BranchCharacter(CharacterClass.Warrior, "equipment.rustleaf_sword", true, "perfect_block");
        var world = new WorldSimulation(utcOrigin: 1000); long id = world.Join(101, character: c);
        Require(world.Submit(id, new(1, world.Time, CommandKind.UseAbility, Slot: AbilitySlot.BranchOne)));
        for (int i=0;i<120;i++) world.Advance(1d/60);
        var saved = world.Character(id)!; Require(saved.Timers.BranchOne == 1018 && saved.Timers.Defense == 1018);
        Require(world.Leave(101)); id = world.Join(101, character: saved);
        Require(!world.Submit(id, new(1, world.Time, CommandKind.UseAbility, Slot: AbilitySlot.Defense)));
        var restart = new WorldSimulation(utcOrigin: 1005); long restored = restart.Join(102, character: saved);
        Require(Math.Abs(restart.GetSnapshots().Single(e => e.Id == restored).Cooldowns.Defense - 13) < 0.0001);
        Require(!restart.Submit(restored, new(1, 0, CommandKind.UseAbility, Slot: AbilitySlot.BranchOne)));
        for(int i=0;i<14;i++) restart.Advance(1);
        Require(restart.Submit(restored, new(2, restart.Time, CommandKind.UseAbility, Slot: AbilitySlot.Defense)));
    }
}
