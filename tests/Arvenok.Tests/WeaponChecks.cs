using System;
using System.IO;
using System.Linq;
using Arvenok.Contracts;
using Arvenok.Hosting;
using Arvenok.Simulation;

namespace Arvenok.Tests;

internal static partial class Program
{
    private static CharacterRecord WeaponCharacter(CharacterClass kind, string weapon, bool shield = false, int capacity = 24)
    {
        var rules = new InventoryRules(); var c = CharacterRecord.New(Guid.NewGuid(), kind, capacity) with { Level = 6, SkillPoints = 5, ClassLocked = true };
        c = c with { Inventory = Add(rules, c.Inventory!, Amount(weapon, 1)) };
        c = rules.PrepareEquipment(c, GearRequest(c, EconomyOperation.Equip, c.Inventory!.Slots[0].Stack)).Character;
        if (shield)
        {
            c = c with { Inventory = Add(rules, c.Inventory!, Amount("equipment.training_shield", 1)) };
            c = rules.PrepareEquipment(c, GearRequest(c, EconomyOperation.Equip, c.Inventory!.Slots[0].Stack, EquipmentSlot.OffHand)).Character;
        }
        new CharacterStateRules().Validate(c); return c;
    }
    private static void WeaponTransactions()
    {
        var rules = new InventoryRules(); CharacterRecord before = WeaponCharacter(CharacterClass.Warrior, "equipment.rustleaf_sword", true, 3);
        before = before with { Inventory = Add(rules, before.Inventory!, Amount("equipment.weapon.weathered_greatsword", 1), Amount("material.slime_gel", 1), Amount("material.wolf_fang", 1)) };
        ItemStack greatsword = before.Inventory.Slots[0].Stack;
        var request = GearRequest(before, EconomyOperation.Equip, greatsword);
        EconomyResult full = rules.PrepareEquipment(before, request);
        Require(!full.Accepted && full.Character == before && full.Message.Contains("shield"));
        // A separate, smaller fixture has enough room for both displaced owned instances.
        CharacterRecord c = WeaponCharacter(CharacterClass.Warrior, "equipment.rustleaf_sword", true, 3);
        var sword = c.Equipment!.Weapon; var shield = c.Equipment.OffHand;
        c = c with { Inventory = Add(rules, c.Inventory!, Amount("equipment.weapon.weathered_greatsword", 1), Amount("material.slime_gel", 1)) };
        request = GearRequest(c, EconomyOperation.Equip, c.Inventory.Slots[0].Stack);
        var swapped = rules.PrepareEquipment(c, request); Require(swapped.Accepted);
        c = swapped.Character; Require(c.Equipment!.OffHand is null && c.Inventory!.Slots.Any(s => s.Stack == sword) && c.Inventory.Slots.Any(s => s.Stack == shield));
        Require(rules.PrepareEquipment(c, request).Duplicate);
        Require(!rules.PrepareEquipment(c, GearRequest(c, EconomyOperation.Equip, shield!, EquipmentSlot.OffHand)).Accepted);
        new CharacterStateRules().Validate(c);
        InvalidSave(() => new CharacterStateRules().Validate(c with { Equipment = c.Equipment with { OffHand = shield } }));
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>()) foreach (string id in new[] { "equipment.weapon.hunters_dagger", "equipment.weapon.weathered_greatsword" })
        {
            var initial = CharacterRecord.New(Guid.NewGuid(), kind);
            initial = initial with { Inventory = Add(rules, initial.Inventory!, Amount(id, 1)) };
            ItemStack item = initial.Inventory.Slots[0].Stack;
            Require(!rules.PrepareEquipment(initial, GearRequest(initial, EconomyOperation.Equip, item)).Accepted);
            initial = initial with { Level = 2, SkillPoints = 1, ClassLocked = true };
            Require(rules.PrepareEquipment(initial, GearRequest(initial, EconomyOperation.Equip, item)).Accepted == (kind == CharacterClass.Warrior || kind == CharacterClass.Archer && id.Contains("dagger")));
        }
    }
    private static void WeaponCombat()
    {
        var c = WeaponCharacter(CharacterClass.Archer, "equipment.weapon.hunters_dagger");
        var f = new Fixture(CharacterClass.Archer, characterA: c);
        Require(!f.Skill(f.A, f.B, AbilitySlot.Primary) && !f.Skill(f.A, f.B, AbilitySlot.Interrupt));
        Require(f.Send(f.A, CommandKind.Attack, f.B)); f.Step(0.22);
        Require(f.Game.RecentActivity.Last(e => e.Kind == CombatEventKind.Damage).Damage == 9);
        double cooldown = f.State(f.A).Cooldowns.Basic; Require(Math.Abs(cooldown - (f.Game.Time - 0.22 + 1.15)) < 0.03);
        Require(f.State(f.A).WeaponType == WeaponType.Dagger && !f.State(f.A).ShieldEquipped);
        var definition = Arvenok.Simulation.EquipmentCombat.Adapt(new CombatBalance().Class(CharacterClass.Archer).Ability(AbilitySlot.Basic), CharacterClass.Archer, new InventoryRules().Weapon(c), false);
        Require(definition.Range == 48);
        var great = WeaponCharacter(CharacterClass.Warrior, "equipment.weapon.forged_greatsword");
        var g = new Fixture(characterA: great); Require(g.Skill(g.A, g.B, AbilitySlot.Interrupt));
        Require(g.Game.RecentActivity.Last(e => e.Kind == CombatEventKind.Damage).Damage == 8);
        Require(g.Game.RecentActivity.Any(e => e.AbilityName == "Hilt Strike"));
    }
    private static void WeaponDefenses()
    {
        var shield = WeaponCharacter(CharacterClass.Warrior, "equipment.rustleaf_sword", true);
        foreach (CharacterClass attacker in new[] { CharacterClass.Warrior, CharacterClass.Mage })
        {
            var f = new Fixture(CharacterClass.Warrior, attacker, characterA: shield);
            Require(f.Skill(f.A, f.B, AbilitySlot.Defense)); Require(f.Skill(f.B, f.A, AbilitySlot.Interrupt));
            Require(f.State(f.A).MaxHp - f.State(f.A).Hp == CombatRules.Damage(5, attacker == CharacterClass.Mage ? 0.35f : 0.45f));
        }
        var great = WeaponCharacter(CharacterClass.Warrior, "equipment.weapon.weathered_greatsword");
        foreach (bool front in new[] { true, false })
        {
            var f = new Fixture(characterA: great);
            if (front) { Require(f.Send(f.A, CommandKind.Attack, f.B)); Require(f.Send(f.A, CommandKind.Stop)); }
            else { Require(f.Send(f.A, CommandKind.Move, point: new System.Numerics.Vector2(478,448))); f.Step(0.1); }
            Require(f.Skill(f.A, f.B, AbilitySlot.Defense)); Require(f.Skill(f.B, f.A, AbilitySlot.Interrupt));
            Require(f.Game.RecentActivity.Any(e => e.Kind == CombatEventKind.Parried) == front);
            Require(f.State(f.A).Hp == f.State(f.A).MaxHp - (front ? 0 : CombatRules.Damage(5, 0.35f)));
        }
    }
    private static void ShieldBlockedLifesteal()
    {
        var reaver = BranchCharacter(CharacterClass.Warrior, "equipment.weapon.forged_greatsword", false, "sanguine_edge");
        var defender = WeaponCharacter(CharacterClass.Warrior, "equipment.rustleaf_sword", true);
        var f = new Fixture(characterA: reaver, characterB: defender);
        Require(f.Skill(f.B, f.A, AbilitySlot.Interrupt));
        int woundedHp = f.State(f.A).Hp;
        Require(woundedHp < f.State(f.A).MaxHp);
        // Eight unblocked damage banks 0.4 HP. The next hit would cross one HP if Shield Guard earned credit.
        Require(f.Skill(f.A, f.B, AbilitySlot.Interrupt));
        f.Step(0.35);
        Require(f.Skill(f.B, f.A, AbilitySlot.Defense));
        int beforeBlock = f.State(f.B).Hp;
        Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
        f.Step(0.8);
        Require(beforeBlock - f.State(f.B).Hp >= 12);
        Require(f.Game.RecentActivity.Any(e => e.Kind == CombatEventKind.ShieldBlocked && e.TargetId == f.B));
        Require(f.State(f.A).Hp == woundedHp);
    }
    private static void WeaponPersistence() => WithSaveDirectory(directory =>
    {
        var rules = new CharacterStateRules(); var inventory = new InventoryRules(); var c = WeaponCharacter(CharacterClass.Warrior, "equipment.rustleaf_sword", true);
        c = c with { Inventory = Add(inventory, c.Inventory!, Amount("equipment.weapon.forged_greatsword", 1)) };
        EconomyRequest request = GearRequest(c, EconomyOperation.Equip, c.Inventory.Slots[0].Stack); CharacterRecord saved;
        using (var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade))
        {
            repository.Save(c); var world = new WorldSimulation(); long id = world.Join(101, character: c);
            try { world.SubmitEconomy(101, request, _ => throw new IOException("disk full")); throw new Exception("Expected failure"); } catch (IOException) { }
            Require(world.Character(id) == c && repository.Load(c.Id) == c);
            Require(world.SubmitEconomy(101, request, repository.Save)!.Accepted); saved = world.Character(id)!;
        }
        using (var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade))
        {
            Require(repository.Load(c.Id) == saved); var world = new WorldSimulation(); long id = world.Join(102, character: repository.Load(c.Id));
            Require(world.SubmitEconomy(102, request, repository.Save)!.Duplicate && world.Character(id) == saved);
        }
    });
}
