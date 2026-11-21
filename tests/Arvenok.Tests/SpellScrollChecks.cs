using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arvenok.Contracts;
using Arvenok.Hosting;
using Arvenok.Simulation;

namespace Arvenok.Tests;

internal static partial class Program
{
    private static CharacterRecord ScrollCharacter(SkillNodeDefinition node)
    {
        var inventory = new InventoryRules(); var tree = new ProgressionRules().Tree;
        string weapon = node.Class switch { CharacterClass.Mage => "equipment.willow_staff", CharacterClass.Archer => "equipment.elm_shortbow", _ => "equipment.rustleaf_sword" };
        var c = BranchCharacter(node.Class, weapon, false, tree.Find(node.PrerequisiteId)!.SkillId.Split('.')[^1]);
        return c with { Inventory = Add(inventory, c.Inventory!, Amount(node.ScrollItemId!, 2)) };
    }
    private static void SpellLearning()
    {
        var progress = new ProgressionRules(); var inventory = new InventoryRules();
        Require(inventory.Catalog.Items.Count(i => i.Category == ItemCategory.SpellScroll) == 6);
        foreach (var node in progress.Tree.Nodes.Where(n => !n.Passive))
        {
            CharacterRecord c = ScrollCharacter(node); ItemStack[] scrolls = c.Inventory!.Slots.Where(s => s.Stack.ItemId == node.ScrollItemId).Select(s => s.Stack).ToArray();
            var request = LearnRequest(c, node, scrolls[1].Id);
            Require(!inventory.PrepareLearning(c, request with { ItemId = Guid.NewGuid() }, progress.Tree).Accepted);
            Require(!inventory.PrepareLearning(c with { Level = 5 }, request, progress.Tree).Accepted);
            Require(!inventory.PrepareLearning(c with { SkillPoints = 0 }, request, progress.Tree).Accepted);
            Require(!inventory.PrepareLearning(c with { Tree = new SkillTreeState(Array.Empty<LearnedNode>()) }, request, progress.Tree).Accepted);
            Require(!inventory.PrepareCraft(c, request with { Operation = EconomyOperation.Craft }, new EconomyCatalog()).Accepted);
            var result = inventory.PrepareLearning(c, request, progress.Tree);
            Require(result.Accepted && result.Character.Tree.Has(node.Id) && result.Character.SkillPoints == c.SkillPoints - 1 && result.Character.Ranks == c.Ranks);
            Require(result.Character.Inventory!.Slots.Any(s => s.Stack.Id == scrolls[0].Id) && result.Character.Inventory.Slots.All(s => s.Stack.Id != scrolls[1].Id));
            Require(inventory.PrepareLearning(result.Character, request, progress.Tree).Duplicate);
            var competing = inventory.PrepareLearning(result.Character, LearnRequest(result.Character, node, scrolls[0].Id), progress.Tree);
            Require(!competing.Accepted && competing.Character == result.Character);
            Require(!inventory.PrepareLearning(result.Character, request with { ItemId = scrolls[0].Id }, progress.Tree).Accepted);
            new CharacterStateRules().Validate(result.Character);
            var wrongClass = CharacterRecord.New(Guid.NewGuid(), (CharacterClass)(((int)node.Class + 1) % 3)) with { Level = 10, SkillPoints = 9, ClassLocked = true, Inventory = c.Inventory };
            Require(!inventory.PrepareLearning(wrongClass, LearnRequest(wrongClass, node, scrolls[0].Id), progress.Tree).Accepted);
        }
    }
    private static void SpellPersistence() => WithSaveDirectory(directory =>
    {
        var rules = new CharacterStateRules(); var node = new ProgressionRules().Tree.Nodes.Single(n => n.SkillId == "mage.flame_lance");
        CharacterRecord c = ScrollCharacter(node), learned; EconomyRequest request = LearnRequest(c, node, c.Inventory!.Slots[0].Stack.Id);
        using (var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade))
        {
            repository.Save(c); var world = new WorldSimulation(); long a = world.Join(101, character: c); world.Join(102);
            try { world.SubmitEconomy(101, request, _ => throw new IOException("disk full")); throw new Exception("Expected failure"); } catch (IOException) { }
            Require(world.Character(a) == c && repository.Load(c.Id) == c);
            Require(world.SubmitEconomy(101, request, repository.Save)!.Accepted); learned = world.Character(a)!;
            Require(repository.Load(c.Id) == learned);
        }
        using (var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade))
        {
            var world = new WorldSimulation(); long a = world.Join(103, character: repository.Load(c.Id));
            Require(world.SubmitEconomy(103, request, repository.Save)!.Duplicate && world.Character(a) == learned);
            Require(world.PlayerProgression(a)!.BranchSkills.Single().Id == "mage.flame_lance");
        }
        var f = new Fixture(characterA: c); Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
        var blocked = f.Game.SubmitEconomy(101, LearnRequest(f.Game.Character(f.A)!, node, request.ItemId)); Require(blocked?.Accepted == false);
        f.Step(1.1); blocked = f.Game.SubmitEconomy(101, LearnRequest(f.Game.Character(f.A)!, node, request.ItemId)); Require(blocked?.Accepted == false);
        f.Step(5.1); Require(f.Game.SubmitEconomy(101, LearnRequest(f.Game.Character(f.A)!, node, request.ItemId))!.Accepted);
    });
    private static void SpellLoot()
    {
        var economy = new EconomyCatalog(); var all = new System.Collections.Generic.HashSet<string>();
        foreach (MonsterSpecies species in Enum.GetValues<MonsterSpecies>())
        {
            var drops = economy.Roll(species, new FixedLootRandom(0));
            foreach (var drop in drops.Where(d => d.ItemId.StartsWith("scroll.skill.", StringComparison.Ordinal))) { all.Add(drop.ItemId); Require(drop.Quantity == 1); }
            Require(drops.Any(d => d.ItemId.StartsWith("material.", StringComparison.Ordinal)));
        }
        Require(all.Count == 6);
        var (world, a, b) = LootWorld(); var scroll = world.GroundDrops().Single(d => d.Item.ItemId.StartsWith("scroll.skill.", StringComparison.Ordinal));
        var request = new EconomyRequest(Guid.NewGuid(), world.Character(a)!.Inventory!.Revision, EconomyOperation.Pickup, scroll.Id);
        Require(!world.SubmitEconomy(102, request with { OperationId = Guid.NewGuid(), ExpectedRevision = world.Character(b)!.Inventory!.Revision })!.Accepted);
        Require(world.SubmitEconomy(101, request)!.Accepted && world.SubmitEconomy(101, request)!.Duplicate);
        Require(world.GroundDrops().All(d => d.Id != scroll.Id) && world.Character(a)!.Inventory!.Slots.Single().Stack.Id == scroll.Item.Id);
    }
    private static void LegacyEconomyReceipt()
    {
        var inventory = new InventoryRules(); var c = CharacterRecord.New(Guid.NewGuid());
        var request = new EconomyRequest(Guid.NewGuid(), 1, EconomyOperation.Equip, Guid.NewGuid());
        var oldWire = new { Domain = "Economy.v1", Request = new { request.OperationId, request.ExpectedRevision, request.Operation, request.ItemId, request.Slot } };
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(oldWire))));
        var receipt = new InventoryReceipt(request.OperationId, fingerprint, 2, false);
        c = c with { Inventory = new InventoryState(24, 2, Array.Empty<InventorySlot>(), new[] { receipt }) };
        Require(inventory.CheckEconomy(c, request)?.Duplicate == true, "Adding learn metadata must preserve old receipt hashes.");
    }
}
