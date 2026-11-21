using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Arvenok.Contracts;
using Arvenok.Hosting;
using Arvenok.Simulation;

namespace Arvenok.Tests;

internal static partial class Program
{
    private sealed class FixedLootRandom(double value) : Random
    {
        public override double NextDouble() => value;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }
    private static void EconomyDefinitions()
    {
        var items = new ItemCatalog(); var economy = new EconomyCatalog(items);
        Require(items.Items.Count(i => i.Equipment is not null) == 17 && economy.Recipes.Count == 8);
        Require(items.Get("equipment.training_sword").Equipment is null && items.Get("recipe.knights_longsword").RecipeId is null);
        Require(economy.CombatLockSeconds == 5 && economy.ProtectionSeconds == 20 && economy.PublicSeconds == 100 && economy.PickupRange == 64);
        foreach (MonsterSpecies species in Enum.GetValues<MonsterSpecies>())
        {
            Require(economy.Roll(species, new FixedLootRandom(0.999999)).Count == 0);
            var all = economy.Roll(species, new FixedLootRandom(0));
            Require(all.Count > 0 && all.All(a => items.Get(a.ItemId).MaximumStack >= a.Quantity));
            Require(!all.Any(a => a.ItemId == "material.wood_log"));
            Require(all.Any(a => a.ItemId == "material.iron_ore") == (species == MonsterSpecies.Wolf));
        }
        var slime = economy.Roll(MonsterSpecies.Slime, new FixedLootRandom(0));
        Require(slime.Count == 4 && slime[0].ItemId == "material.slime_gel" && slime.All(i => !i.ItemId.StartsWith("recipe.")));
        string path = Path.Combine(FindRepository(), "content/art-source/ui/arvenok-loot-v1/data/loot_tables.json");
        string json = File.ReadAllText(path);
        InvalidSave(() => new EconomyCatalog(lootJson: json.Replace("0.88", "1.1")));
        InvalidSave(() => new EconomyCatalog(lootJson: json.Replace("material.slime_gel", "material.missing")));
        InvalidSave(() => new EconomyCatalog(lootJson: json.Replace("\"weight\": 1", "\"weight\": 0")));
        var random = new Random(1979); int gel = 0, sword = 0;
        for (int i = 0; i < 20000; i++) foreach (ItemAmount drop in economy.Roll(MonsterSpecies.Slime, random))
        { if (drop.ItemId == "material.slime_gel") { gel++; Require(drop.Quantity is >= 1 and <= 3); } if (drop.ItemId == "equipment.rustleaf_sword") sword++; }
        Require(gel is > 17200 and < 18000 && sword is > 350 and < 650);
    }
    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arvenok.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
    private static CharacterRecord GearCharacter(int level = 1, CharacterClass kind = CharacterClass.Warrior, int capacity = 24)
    {
        var character = CharacterRecord.New(Guid.NewGuid(), kind, capacity);
        return level == 1 ? character : new ProgressionRules().Award(character, 698).Character;
    }
    private static EconomyRequest GearRequest(CharacterRecord character, EconomyOperation operation, ItemStack item, EquipmentSlot slot = EquipmentSlot.Weapon) =>
        new(Guid.NewGuid(), character.Inventory!.Revision, operation, item.Id, slot);
    private static void EquipmentOwnership()
    {
        var inventory = new InventoryRules(); CharacterRecord c = GearCharacter(capacity: 2);
        c = c with { Inventory = Add(inventory, c.Inventory!, Amount("equipment.rustleaf_sword", 1, new ItemProperty("maker", "legacy-metadata")), Amount("equipment.ironbark_sword", 1)) };
        ItemStack first = c.Inventory.Slots[0].Stack, second = c.Inventory.Slots[1].Stack;
        Require(!inventory.PrepareEquipment(c, GearRequest(c, EconomyOperation.Equip, second)).Accepted);
        Require(!inventory.PrepareEquipment(c with { Class = CharacterClass.Mage }, GearRequest(c, EconomyOperation.Equip, first)).Accepted);
        var request = GearRequest(c, EconomyOperation.Equip, first); EconomyResult equipped = inventory.PrepareEquipment(c, request);
        Require(equipped.Accepted && equipped.Character.Equipment!.Weapon == first && equipped.Character.Inventory!.Slots.Count == 1);
        Require(inventory.PrepareEquipment(equipped.Character, request).Duplicate);
        c = equipped.Character with { Inventory = Add(inventory, equipped.Character.Inventory!, Amount("equipment.rustleaf_sword", 1)) };
        Require(!inventory.PrepareEquipment(c, GearRequest(c, EconomyOperation.Unequip, first)).Accepted);
        ItemStack replacement = c.Inventory.Slots[0].Stack;
        c = inventory.PrepareEquipment(c, GearRequest(c, EconomyOperation.Equip, replacement)).Character;
        Require(c.Inventory!.Slots.Count == 2 && c.Inventory.Slots[0].Stack == first && c.Equipment!.Weapon == replacement);
        new CharacterStateRules().Validate(c);
        InvalidSave(() => new CharacterStateRules().Validate(c with { Equipment = new(first) }));
    }
    private static void EquipmentCombat()
    {
        var rules = new InventoryRules(); CharacterRecord c = GearCharacter(5);
        c = c with { Inventory = Add(rules, c.Inventory!, Amount("equipment.ironbark_sword", 1), Amount("equipment.warden_mail", 1)) };
        var f = new Fixture(characterA: c); ItemStack sword = c.Inventory.Slots[0].Stack, body = c.Inventory.Slots[1].Stack;
        Require(f.Game.SubmitEconomy(101, GearRequest(f.Game.Character(f.A)!, EconomyOperation.Equip, sword))!.Accepted);
        int hp = f.State(f.A).Hp, max = f.State(f.A).MaxHp;
        Require(f.Game.SubmitEconomy(101, GearRequest(f.Game.Character(f.A)!, EconomyOperation.Equip, body, EquipmentSlot.Body))!.Accepted);
        Require(f.State(f.A).Hp == hp && f.State(f.A).MaxHp == max + 10);
        Require(f.Skill(f.A, f.B, AbilitySlot.Primary)); f.Step(0.7);
        Require(f.Game.RecentActivity.Last(e => e.Kind == CombatEventKind.Damage && e.ActorId == f.A).Damage == 20);
        var blocked = GearRequest(f.Game.Character(f.A)!, EconomyOperation.Unequip, body, EquipmentSlot.Body);
        Require(!f.Game.SubmitEconomy(101, blocked)!.Accepted);
        f.Step(5.1);
        Require(f.Game.SubmitEconomy(101, blocked)!.Accepted);
        Require(f.State(f.A).MaxHp == max && f.State(f.A).Hp == hp);
        for (int i = 0; i < 3; i++)
        {
            f.Step(0.5);
            Require(f.Game.SubmitEconomy(101, GearRequest(f.Game.Character(f.A)!, EconomyOperation.Equip, body, EquipmentSlot.Body))!.Accepted);
            Require(f.Game.SubmitEconomy(101, GearRequest(f.Game.Character(f.A)!, EconomyOperation.Unequip, body, EquipmentSlot.Body))!.Accepted);
        }
        Require(f.State(f.A).MaxHp == max && f.State(f.A).Hp == hp);
    }
    private static void EquipmentPersistence() => WithSaveDirectory(directory =>
    {
        var rules = new CharacterStateRules(); var inventory = new InventoryRules(); CharacterRecord saved; EconomyRequest request;
        using (var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade))
        {
            CharacterRecord character = repository.Create().Character;
            character = character with { Inventory = Add(inventory, character.Inventory!, Amount("equipment.padded_guard_coat", 1)), Revision = 2 };
            repository.Save(character);
            var world = new WorldSimulation(); long entity = world.Join(101, character: character);
            request = GearRequest(character, EconomyOperation.Equip, character.Inventory!.Slots[0].Stack, EquipmentSlot.Body);
            try { world.SubmitEconomy(101, request, _ => throw new IOException("simulated disk failure")); throw new Exception("Expected storage failure"); }
            catch (IOException) { }
            Require(world.Character(entity) == character && repository.Load(character.Id) == character);
            Require(world.SubmitEconomy(101, request, repository.Save)!.Accepted);
            saved = world.Character(entity)!; Require(repository.Load(saved.Id) == saved);
        }
        using (var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade))
        {
            var world = new WorldSimulation(); long entity = world.Join(102, character: repository.Load(saved.Id));
            Require(world.SubmitEconomy(102, request, repository.Save)!.Duplicate);
            Require(world.GetSnapshots().Single(e => e.Id == entity).MaxHp == world.Balance.Class(saved.Class).MaxHp + 5);
            CharacterRecord legacy = saved with { Id = Guid.NewGuid(), SchemaVersion = 2, Equipment = null };
            string path = Path.Combine(directory, "characters", legacy.Id.ToString("N") + ".json"); File.WriteAllText(path, JsonSerializer.Serialize(legacy));
            CharacterRecord migrated = repository.Load(legacy.Id);
            Require(migrated.SchemaVersion == 4 && migrated.Inventory == legacy.Inventory && migrated.Equipment == new EquipmentState());
            Require(repository.Load(legacy.Id) == migrated);
        }
    });
    private static void EquipmentRankedDamage()
    {
        var inventory = new InventoryRules(); var progress = new ProgressionRules();
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
            foreach (AbilitySlot slot in new[] { AbilitySlot.Basic, AbilitySlot.Primary, AbilitySlot.Interrupt })
            {
                CharacterRecord c = GearCharacter(5, kind);
                Require(progress.TryUpgrade(c, progress.SkillId(kind, AbilitySlot.Primary), c.Revision, out c, out _));
                string weaponId = inventory.Catalog.Items.Single(d => d.Equipment is { Tier: 2, MinimumLevel: 5, Slot: EquipmentSlot.Weapon } gear && gear.Class == kind).ItemId;
                c = c with { Inventory = Add(inventory, c.Inventory!, Amount(weaponId, 1)) };
                c = inventory.PrepareEquipment(c, GearRequest(c, EconomyOperation.Equip, c.Inventory.Slots[0].Stack)).Character;
                var f = new Fixture(kind, CharacterClass.Warrior, characterA: c);
                Require(f.Skill(f.B, f.A, AbilitySlot.Defense));
                AbilityDefinition definition = progress.Ability(kind, slot, c.Ranks);
                Require(slot == AbilitySlot.Basic ? f.Send(f.A, CommandKind.Attack, f.B) : f.Skill(f.A, f.B, slot));
                f.Step(definition.CastSeconds + 0.02);
                CombatEvent[] hits = f.Game.RecentActivity.Where(e => e.Kind == CombatEventKind.Damage && e.ActorId == f.A).ToArray();
                Require(hits.Length == 1 && hits[0].Damage == CombatRules.Damage(definition.Damage + 2, 0.35f));
                Require(Math.Abs(f.State(f.A).Cooldowns.EndsAt(slot) - (hits[0].Time - definition.CastSeconds + definition.CooldownSeconds)) < 0.04);
            }
        // Reconnect/respawn uses the effective maximum; removing armor clamps rather than granting HP.
        CharacterRecord armored = GearCharacter(5);
        armored = armored with { Inventory = Add(inventory, armored.Inventory!, Amount("equipment.warden_mail", 1)) };
        ItemStack body = armored.Inventory.Slots[0].Stack;
        armored = inventory.PrepareEquipment(armored, GearRequest(armored, EconomyOperation.Equip, body, EquipmentSlot.Body)).Character;
        var fixture = new Fixture(characterA: armored);
        Require(fixture.State(fixture.A).Hp == 135);
        Require(fixture.Game.SubmitEconomy(101, GearRequest(fixture.Game.Character(fixture.A)!, EconomyOperation.Unequip, body, EquipmentSlot.Body))!.Accepted);
        Require(fixture.State(fixture.A).Hp == 125 && fixture.State(fixture.A).MaxHp == 125);
    }
    private static (WorldSimulation World, long A, long B) LootWorld()
    {
        var map = WorldMap.Outdoor with { MonsterSpawns = new[] { WorldMap.Outdoor.MonsterSpawns[0] with { Position = new Vector2(480, 448), MaxHp = 8, RoamRadius = 0 } } };
        var world = new WorldSimulation(map: new WorldMap(map), lootRandom: new FixedLootRandom(0));
        long a = world.Join(101), b = world.Join(102);
        world.Submit(a, new(1, world.Time, CommandKind.Move, Destination: new Vector2(460, 448)));
        world.Submit(b, new(1, world.Time, CommandKind.Move, Destination: new Vector2(480, 480)));
        for (int i = 0; i < 300; i++) world.Advance(1d / 60);
        long monster = world.GetSnapshots().Single(e => e.Kind == EntityKind.Monster).Id;
        Require(world.Submit(a, new(2, world.Time, CommandKind.Attack, monster)));
        for (int i = 0; i < 60; i++) world.Advance(1d / 60);
        Require(world.GroundDrops().Length == 4);
        return (world, a, b);
    }
    private static void LootDeaths()
    {
        var (world, a, _) = LootWorld(); GroundLoot[] drops = world.GroundDrops();
        Require(drops.Select(l => l.DeathId).Distinct().Count() == 1 && drops.All(d => d.OwnerId == world.Character(a)!.Id));
        Require(drops.All(d => world.Map.IsWalkable(d.Position) && world.Map.FindPath(world.Map.Definition.Spawns[0], d.Position) is not null));
        Require(drops.All(d => d.PublicAt - d.SpawnedAt == 20 && d.ExpiresAt - d.PublicAt == 100));
        for (int i = 0; i < 5; i++) world.Advance(1);
        Require(world.GroundDrops().Select(d => d.Id).SequenceEqual(drops.Select(d => d.Id)), "Respawn cannot reroll the previous death.");
        for (int i = 0; i < 121; i++) world.Advance(1);
        Require(world.GroundDrops().Length == 0);
    }
    private static void LootContributionTie()
    {
        var map = WorldMap.Outdoor with { MonsterSpawns = new[] { WorldMap.Outdoor.MonsterSpawns[0] with { Position = new Vector2(480, 448), MaxHp = 18, RoamRadius = 0, BaseExperience = 30 } } };
        var world = new WorldSimulation(map: new WorldMap(map), lootRandom: new FixedLootRandom(0));
        var larger = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"); var smaller = Guid.Parse("11111111-1111-1111-1111-111111111111");
        long a = world.Join(101, character: CharacterRecord.New(larger)), b = world.Join(102, character: CharacterRecord.New(smaller));
        world.Submit(a, new(1, 0, CommandKind.Move, Destination: new Vector2(460, 448)));
        world.Submit(b, new(1, 0, CommandKind.Move, Destination: new Vector2(480, 480)));
        for (int i = 0; i < 300; i++) world.Advance(1d / 60);
        long monster = world.GetSnapshots().Single(e => e.Kind == EntityKind.Monster).Id;
        Require(world.Submit(a, new(2, world.Time, CommandKind.Attack, monster)) && world.Submit(b, new(2, world.Time, CommandKind.Attack, monster)));
        for (int i = 0; i < 60; i++) world.Advance(1d / 60);
        Require(world.GroundDrops().Length == 4 && world.GroundDrops().All(d => d.OwnerId == smaller));
        Require(world.Character(a)!.Experience == 15 && world.Character(b)!.Experience == 15);
    }
    private static void LootPickup() => WithSaveDirectory(directory =>
    {
        var (world, a, b) = LootWorld(); GroundLoot drop = world.GroundDrops().Single(d => d.Item.ItemId == "equipment.rustleaf_sword");
        EconomyRequest Request(long entity) => new(Guid.NewGuid(), world.Character(entity)!.Inventory!.Revision, EconomyOperation.Pickup, drop.Id);
        Require(!world.SubmitEconomy(102, Request(b))!.Accepted);
        var request = Request(a); CharacterRecord original = world.Character(a)!;
        try { world.SubmitEconomy(101, request, _ => throw new IOException("disk")); throw new Exception("Expected failure"); } catch (IOException) { }
        Require(world.Character(a) == original && world.GroundDrops().Any(d => d.Id == drop.Id));
        var rules = new CharacterStateRules(); using var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade);
        Require(world.SubmitEconomy(101, request, repository.Save)!.Accepted);
        Require(world.Character(a)!.Inventory!.Slots.Single().Stack == drop.Item && !world.GroundDrops().Any(d => d.Id == drop.Id));
        Require(world.SubmitEconomy(101, request, repository.Save)!.Duplicate && !world.SubmitEconomy(102, Request(b))!.Accepted);
        world.Leave(101); long restored = world.Join(103, character: repository.Load(original.Id));
        Require(world.SubmitEconomy(103, request, repository.Save)!.Duplicate && world.Character(restored)!.Inventory!.Slots.Count == 1);
        drop = world.GroundDrops()[0];
        Require(!world.SubmitEconomy(103, new(Guid.NewGuid(), world.Character(restored)!.Inventory!.Revision, EconomyOperation.Pickup, drop.Id))!.Accepted, "Reconnect in sanctuary is out of range.");
        for (int i = 0; i < 21; i++) world.Advance(1);
        Require(world.SubmitEconomy(102, Request(b), repository.Save)!.Accepted, "Public loot opens at 20 seconds.");
        var pickup = new InventoryRules(); CharacterRecord full = GearCharacter(capacity: 1);
        full = full with { Inventory = Add(pickup, full.Inventory!, Amount("resource.stone", 999)) };
        EconomyResult failure = pickup.PreparePickup(full, new(Guid.NewGuid(), full.Inventory.Revision, EconomyOperation.Pickup, Guid.NewGuid()), drop.Item);
        Require(!failure.Accepted && failure.Character == full && failure.Message == "Inventory Full.");
    });
    private static CharacterRecord CraftCharacter(RecipeDefinition recipe, int capacity = 24)
    {
        var inventory = new InventoryRules(); CharacterRecord c = GearCharacter(capacity: capacity);
        c = c with { Inventory = Add(inventory, c.Inventory!, Amount(recipe.ScrollItemId, 1)) };
        foreach (RecipeIngredient ingredient in recipe.Ingredients)
            c = c with { Inventory = Add(inventory, c.Inventory!, Amount(ingredient.ItemId, ingredient.Quantity)) };
        return c;
    }
    private static void CraftRecipes()
    {
        var inventory = new InventoryRules(); var economy = new EconomyCatalog();
        foreach (RecipeDefinition recipe in economy.Recipes)
        {
            CharacterRecord c = CraftCharacter(recipe);
            InventorySlot first = c.Inventory!.Slots.Single(s => s.Stack.ItemId == recipe.Ingredients[0].ItemId);
            c = c with { Inventory = inventory.Prepare(c.Inventory, Slots(c.Inventory, InventoryOperation.Split, first.Index, 23, 2)).State };
            var request = GearRequest(c, EconomyOperation.Craft, c.Inventory!.Slots[0].Stack);
            EconomyResult result = inventory.PrepareCraft(c, request, economy);
            Require(result.Accepted && result.Character.Level == 1 && result.Character.Inventory!.Slots.Count == 1);
            ItemStack output = result.Character.Inventory!.Slots[0].Stack;
            Require(output.ItemId == recipe.OutputItemId && output.Id == result.CreatedItemId && output.Properties.Single().Value == recipe.RecipeId);
            EconomyResult duplicate = inventory.PrepareCraft(result.Character, request, economy);
            Require(duplicate.Duplicate && duplicate.CreatedItemId == output.Id && duplicate.Character == result.Character);
            Require(!inventory.PrepareEquipment(result.Character, GearRequest(result.Character, EconomyOperation.Equip, output, inventory.Catalog.Get(output.ItemId).Equipment!.Slot)).Accepted);
        }
    }
    private static void CraftAtomicity()
    {
        var inventory = new InventoryRules(); var economy = new EconomyCatalog(); RecipeDefinition recipe = economy.Recipes[0];
        CharacterRecord c = CraftCharacter(recipe, 4); var request = GearRequest(c, EconomyOperation.Craft, c.Inventory!.Slots[0].Stack);
        Require(c.Inventory.Slots.Count == 4 && inventory.PrepareCraft(c, request, economy).Accepted, "Full bag succeeds after consuming its scroll/inputs.");
        c = GearCharacter(); c = c with { Inventory = Add(inventory, c.Inventory!, Amount(recipe.ScrollItemId, 1)) };
        EconomyResult missing = inventory.PrepareCraft(c, GearRequest(c, EconomyOperation.Craft, c.Inventory.Slots[0].Stack), economy);
        Require(!missing.Accepted && missing.Character == c);
        c = CraftCharacter(recipe); c = c with { Inventory = Add(inventory, c.Inventory!, Amount(recipe.ScrollItemId, 1)) };
        ItemStack[] scrolls = c.Inventory.Slots.Where(s => s.Stack.ItemId == recipe.ScrollItemId).Select(s => s.Stack).ToArray();
        request = GearRequest(c, EconomyOperation.Craft, scrolls[1]); var competing = GearRequest(c, EconomyOperation.Craft, scrolls[0]);
        EconomyResult success = inventory.PrepareCraft(c, request, economy);
        Require(success.Character.Inventory!.Slots.Any(s => s.Stack.Id == scrolls[0].Id) && success.Character.Inventory.Slots.All(s => s.Stack.Id != scrolls[1].Id));
        Require(!inventory.PrepareCraft(success.Character, competing, economy).Accepted);
        Require(!inventory.PrepareCraft(success.Character, competing with { OperationId = Guid.NewGuid(), ExpectedRevision = success.Character.Inventory.Revision }, economy).Accepted);
        Require(!inventory.PrepareCraft(success.Character, request with { ItemId = scrolls[0].Id }, economy).Accepted);
        Require(!inventory.PrepareCraft(success.Character, request with { OperationId = Guid.NewGuid(), ExpectedRevision = success.Character.Inventory.Revision }, economy).Accepted);
    }
    private static void CraftPersistence() => WithSaveDirectory(directory =>
    {
        var rules = new CharacterStateRules(); var economy = new EconomyCatalog(); CharacterRecord c = CraftCharacter(economy.Recipes[0]);
        var request = GearRequest(c, EconomyOperation.Craft, c.Inventory!.Slots[0].Stack); CharacterRecord saved;
        using (var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade))
        {
            repository.Save(c); var world = new WorldSimulation(); long a = world.Join(101, character: c);
            try { world.SubmitEconomy(101, request, _ => throw new IOException("disk failure")); throw new Exception("Expected failure"); } catch (IOException) { }
            Require(world.Character(a) == c && repository.Load(c.Id) == c);
            Require(world.SubmitEconomy(101, request, repository.Save)!.Accepted);
            saved = world.Character(a)!; Require(repository.Load(c.Id) == saved);
        }
        using (var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade))
        {
            var world = new WorldSimulation(); long a = world.Join(102, character: repository.Load(c.Id));
            Require(world.SubmitEconomy(102, request, repository.Save)!.Duplicate && world.Character(a) == saved);
        }
    });
}
