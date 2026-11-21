using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arvenok.Hosting;
using Arvenok.Contracts;
using Arvenok.Simulation;

namespace Arvenok.Tests;

internal static partial class Program
{
    private static ItemAmount Amount(string id, int quantity, params ItemProperty[] properties) => new(id, quantity, properties);
    private static InventoryExchange Exchange(InventoryState state, ItemAmount[]? remove = null, params ItemAmount[] add) =>
        new(Guid.NewGuid(), state.Revision, remove ?? Array.Empty<ItemAmount>(), add);
    private static InventoryState Add(InventoryRules rules, InventoryState state, params ItemAmount[] items)
    { InventoryResult result = rules.Prepare(state, Exchange(state, add: items)); Require(result.Accepted, result.Message); return result.State; }
    private static InventoryRequest Slots(InventoryState state, InventoryOperation operation, int source, int destination, int quantity = 0) =>
        new(Guid.NewGuid(), state.Revision, operation, source, destination, state.Slots.SingleOrDefault(s => s.Index == source)?.Stack.Id ?? Guid.Empty,
            state.Slots.SingleOrDefault(s => s.Index == destination)?.Stack.Id ?? Guid.Empty, quantity);
    private static void InventoryCatalog()
    {
        var rules = new InventoryRules(); Require(rules.Empty().Capacity == 24 && rules.Empty().Slots.Count == 0 && rules.Empty().Revision == 1);
        Require(rules.Catalog.Items.Count == 51 && rules.Catalog.Get("resource.wood_log").MaximumStack == 999);
        Require(rules.Catalog.Get("keypiece.longsword_blade_fragment").MaximumStack == 99 && rules.Catalog.Get("recipe.knights_longsword").MaximumStack == 1);
        InvalidSave(() => new ItemCatalog("{}"));
        InvalidSave(() => new ItemCatalog("{\"schemaVersion\":1,\"defaultCapacity\":49,\"maximumCapacity\":48,\"items\":[]}"));
        InvalidSave(() => new ItemCatalog("{ broken"));
        var definition = rules.Catalog.Get("equipment.training_sword");
        string CatalogJson(params ItemDefinition[] items) => JsonSerializer.Serialize(new { schemaVersion = 1, defaultCapacity = 24, maximumCapacity = 48, items });
        InvalidSave(() => new ItemCatalog(CatalogJson(definition with { MaximumStack = 99 })));
        InvalidSave(() => new ItemCatalog(CatalogJson(definition, definition)));
        InvalidSave(() => new ItemCatalog(CatalogJson(definition with { ItemId = "../invalid", Icon = "../../file" })));
    }
    private static void InventoryAddition()
    {
        var rules = new InventoryRules(); var empty = rules.Empty();
        var state = Add(rules, empty, Amount("resource.wood_log", 990)); Guid first = state.Slots[0].Stack.Id;
        state = Add(rules, state, Amount("resource.wood_log", 20));
        Require(state.Slots.Count == 2 && state.Slots[0].Index == 0 && state.Slots[0].Stack.Quantity == 999 && state.Slots[0].Stack.Id == first);
        Require(state.Slots[1].Index == 1 && state.Slots[1].Stack.Quantity == 11 && state.Slots[1].Stack.Id != first);
        Require(empty.Slots.Count == 0 && rules.Empty().Slots.Count == 0);
    }
    private static void InventoryAtomicCapacity()
    {
        var rules = new InventoryRules(); var full = Add(rules, InventoryState.Empty(2), Amount("resource.wood_log", 999), Amount("equipment.training_sword", 1));
        InventoryResult failed = rules.Prepare(full, Exchange(full, add: new[] { Amount("resource.stone", 1) }));
        Require(!failed.Accepted && ReferenceEquals(failed.State, full) && failed.Message == "Inventory Full.");
        failed = rules.Prepare(full, Exchange(full, new[] { Amount("resource.wood_log", 999) }, Amount("equipment.training_shield", 2)));
        Require(!failed.Accepted && failed.State == full);
        InventoryResult success = rules.Prepare(full, Exchange(full, new[] { Amount("resource.wood_log", 999) }, Amount("equipment.training_shield", 1)));
        Require(success.Accepted && success.State.Slots[0].Stack.ItemId == "equipment.training_shield" && success.State.Slots[1] == full.Slots[1]);
        var partial = Add(rules, InventoryState.Empty(1), Amount("resource.stone", 998));
        Require(!rules.Prepare(partial, Exchange(partial, add: new[] { Amount("resource.stone", 2) })).Accepted && partial.Slots[0].Stack.Quantity == 998);
    }
    private static void InventorySlotOperations()
    {
        var rules = new InventoryRules(); var state = Add(rules, rules.Empty(), Amount("resource.wood_log", 900)); Guid original = state.Slots[0].Stack.Id;
        state = rules.Prepare(state, Slots(state, InventoryOperation.Split, 0, 3, 400)).State;
        Require(state.Slots[0].Stack.Quantity == 500 && state.Slots[1].Stack.Quantity == 400 && state.Slots[1].Stack.Id != original);
        state = Add(rules, state, Amount("resource.wood_log", 300));
        state = rules.Prepare(state, Slots(state, InventoryOperation.Merge, 0, 3)).State;
        Require(state.Slots.Single(s => s.Index == 3).Stack.Quantity == 999 && state.Slots.Single(s => s.Index == 0).Stack.Quantity == 201);
        Require(state.Slots[0].Stack.Id == original && rules.Count(state, "resource.wood_log", Array.Empty<ItemProperty>()) == 1200);
        var small = Add(rules, rules.Empty(), Amount("resource.stone", 50));
        small = rules.Prepare(small, Slots(small, InventoryOperation.Split, 0, 1, 10)).State; Guid destination = small.Slots[1].Stack.Id;
        small = rules.Prepare(small, Slots(small, InventoryOperation.Merge, 0, 1)).State;
        Require(small.Slots.Count == 1 && small.Slots[0].Stack.Id == destination && small.Slots[0].Stack.Quantity == 50);
        small = Add(rules, small, Amount("equipment.training_sword", 1)); Guid sword = small.Slots[0].Stack.Id;
        small = rules.Prepare(small, Slots(small, InventoryOperation.Swap, 0, 1)).State;
        Require(small.Slots[1].Stack.Id == sword && small.Slots[0].Stack.Id == destination);
        small = rules.Prepare(small, Slots(small, InventoryOperation.Move, 1, 23)).State;
        Require(small.Slots[1].Index == 23 && small.Slots[1].Stack.Id == sword);
        foreach (int bad in new[] { -1, 24, int.MaxValue }) Require(!rules.Prepare(small, Slots(small, InventoryOperation.Move, 23, bad)).Accepted);
        Require(!rules.Prepare(small, Slots(small, InventoryOperation.Split, 0, 2, 50)).Accepted);
    }
    private static void InventoryCompatibility()
    {
        var rules = new InventoryRules(); var state = Add(rules, rules.Empty(), Amount("equipment.training_sword", 2),
            Amount("resource.wood_log", 5, new ItemProperty("origin", "forest")), Amount("resource.wood_log", 7, new ItemProperty("origin", "mountain")));
        Require(state.Slots.Count == 4 && state.Slots[0].Stack.Id != state.Slots[1].Stack.Id);
        Require(!rules.Prepare(state, Slots(state, InventoryOperation.Merge, 0, 1)).Accepted);
        Require(!rules.Prepare(state, Slots(state, InventoryOperation.Merge, 2, 3)).Accepted);
        var swapped = rules.Prepare(state, Slots(state, InventoryOperation.Drag, 2, 3));
        Require(swapped.Accepted && swapped.State.Slots[2].Stack == state.Slots[3].Stack && swapped.State.Slots[3].Stack == state.Slots[2].Stack);
    }
    private static void InventoryRemoval()
    {
        var rules = new InventoryRules(); var state = Add(rules, rules.Empty(), Amount("resource.wood_log", 1200), Amount("resource.stone", 4));
        InventoryResult result = rules.Prepare(state, Exchange(state, new[] { Amount("resource.wood_log", 1100), Amount("resource.stone", 4) }));
        Require(result.Accepted && result.State.Slots.Count == 1 && result.State.Slots[0].Index == 1 && result.State.Slots[0].Stack.Quantity == 100);
        Require(!rules.Prepare(state, Exchange(state, new[] { Amount("resource.wood_log", 1201) })).Accepted);
        Require(!rules.Prepare(state, Exchange(state, new[] { Amount("resource.wood_log", 1200), Amount("resource.stone", 5) })).Accepted);
        Require(state.Slots.Sum(s => s.Stack.Quantity) == 1204);
    }
    private static void InventoryRetries()
    {
        var rules = new InventoryRules(); var empty = rules.Empty(); var reward = Exchange(empty, add: new[] { Amount("resource.wood_log", 100) });
        var added = rules.Prepare(empty, reward); var retry = rules.Prepare(added.State, reward);
        Require(retry.Accepted && retry.Duplicate && retry.State == added.State);
        Require(!rules.Prepare(added.State, reward with { Add = new[] { Amount("resource.wood_log", 200) } }).Accepted);
        var removal = Exchange(added.State, new[] { Amount("resource.wood_log", 30) }); var removed = rules.Prepare(added.State, removal);
        Require(rules.Prepare(removed.State, removal).Duplicate && removed.State.Slots[0].Stack.Quantity == 70);
        var move = Slots(removed.State, InventoryOperation.Move, 0, 4); var moved = rules.Prepare(removed.State, move);
        Require(rules.Prepare(moved.State, move).Duplicate);
        Require(!rules.Prepare(moved.State, move with { OperationId = Guid.NewGuid() }).Accepted);
        Require(!rules.Prepare(moved.State, Slots(moved.State, InventoryOperation.Move, 4, 5) with { SourceId = Guid.NewGuid() }).Accepted);
        InventoryState aged = moved.State;
        for (int i = 0; i < 130; i++) aged = rules.Prepare(aged, new InventoryRequest(Guid.NewGuid(), aged.Revision, InventoryOperation.Sort)).State;
        Require(aged.Receipts.Count(r => !r.ServerOperation) == 128 && !rules.Prepare(aged, move).Accepted, "A retry older than the client receipt window must fail its old revision.");
        Require(rules.Prepare(aged, reward).Duplicate && rules.Prepare(aged, removal).Duplicate, "Trusted receipts cannot be evicted by client actions.");
        Guid sharedId = Guid.NewGuid();
        var clientOperation = new InventoryRequest(sharedId, empty.Revision, InventoryOperation.Sort);
        InventoryState reserved = rules.Prepare(empty, clientOperation).State;
        var trusted = Exchange(reserved, add: new[] { Amount("resource.stone", 1) }) with { OperationId = sharedId };
        var rewarded = rules.Prepare(reserved, trusted);
        Require(rewarded.Accepted && !rewarded.Duplicate && rules.Prepare(rewarded.State, trusted).Duplicate
            && rules.Prepare(rewarded.State, clientOperation).Duplicate, "Untrusted request IDs must not poison trusted reward IDs.");
        rules.Validate(rewarded.State);
    }
    private static void InventorySort()
    {
        var rules = new InventoryRules(); var state = Add(rules, rules.Empty(), Amount("monster.wolf_pelt", 3), Amount("resource.wood_log", 5),
            Amount("equipment.training_shield", 1), Amount("consumable.training_tonic", 9));
        var result = rules.Prepare(state, new InventoryRequest(Guid.NewGuid(), state.Revision, InventoryOperation.Sort));
        Require(result.Accepted && result.State.Slots[0].Stack.ItemId == "equipment.training_shield" && result.State.Slots[1].Stack.ItemId == "consumable.training_tonic");
        Require(result.State.Slots.Select(s => s.Stack).OrderBy(s => s.Id).SequenceEqual(state.Slots.Select(s => s.Stack).OrderBy(s => s.Id)));
    }
    private static void InventoryCorruption()
    {
        var rules = new InventoryRules(); var state = Add(rules, rules.Empty(), Amount("resource.wood_log", 20, new ItemProperty("origin", "forest")));
        var restored = JsonSerializer.Deserialize<InventoryState>(JsonSerializer.Serialize(state))!; Require(restored == state); rules.Validate(restored);
        foreach (var stack in new[] { state.Slots[0].Stack with { Quantity = 1000 }, state.Slots[0].Stack with { Id = Guid.Empty }, state.Slots[0].Stack with { ItemId = "missing.item" } })
            InvalidSave(() => rules.Validate(new InventoryState(24, 1, new[] { new InventorySlot(0, stack) }, Array.Empty<InventoryReceipt>())));
        InvalidSave(() => rules.Validate(new InventoryState(24, 1, new[] { state.Slots[0], state.Slots[0] }, Array.Empty<InventoryReceipt>())));
        var source = state.Slots.ToArray(); var detached = new InventoryState(24, state.Revision, source, state.Receipts); source[0] = new InventorySlot(3, state.Slots[0].Stack);
        Require(detached.Slots[0].Index == 0);
    }
    private static void InventoryOwnership()
    {
        var game = new WorldSimulation(); long a = game.Join(101), b = game.Join(102);
        Require(game.Character(a)!.Inventory!.Slots.Count == 0 && game.Character(b)!.Inventory!.Capacity == 24);
        var reward = Exchange(game.Character(a)!.Inventory!, add: new[] { Amount("resource.wood_log", 100) });
        Require(game.ExchangeInventory(a, reward)!.Accepted);
        var state = game.Character(a)!.Inventory!;
        var command = Slots(state, InventoryOperation.Split, 0, 1, 30);
        Require(game.SubmitInventory(999, command) is null && !game.SubmitInventory(102, command)!.Accepted);
        Require(game.Character(b)!.Inventory!.Slots.Count == 0);
        var competing = command with { OperationId = Guid.NewGuid(), Destination = 2 };
        var prepared = System.Threading.Tasks.Task.WhenAll(System.Threading.Tasks.Task.Run(() => game.Inventory.Prepare(state, command)),
            System.Threading.Tasks.Task.Run(() => game.Inventory.Prepare(state, competing))).GetAwaiter().GetResult();
        Require(prepared.All(p => p.Accepted), "Parallel preparation does not mutate authoritative state.");
        Require(game.SubmitInventory(101, command)!.Accepted && !game.SubmitInventory(101, competing)!.Accepted);
        Require(game.Character(a)!.Inventory!.Slots.Sum(s => s.Stack.Quantity) == 100);
        Require(game.SubmitInventory(101, command)!.Duplicate);
        CharacterRecord saved = game.Character(a)!; game.Leave(101); long restored = game.Join(103, character: saved);
        Require(game.Character(restored) == saved && game.PlayerInventory(restored)!.CharacterId == saved.Id);
        Require(!JsonSerializer.Serialize(game.GetSnapshots()).Contains("wood_log", StringComparison.Ordinal), "Public snapshots never reveal backpacks.");
        var upgraded = new ProgressionRules().Award(saved, 80).Character;
        Require(upgraded.Inventory == saved.Inventory && upgraded.Level == 2 && upgraded.SkillPoints == 1);
    }
    private static void InventoryPersistence() => WithSaveDirectory(directory =>
    {
        var rules = new CharacterStateRules(); var inventory = new InventoryRules(); CharacterSession session; CharacterRecord saved; InventoryExchange reward;
        using (var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade))
        {
            session = repository.Create();
            reward = Exchange(session.Character.Inventory!, add: new[] { Amount("resource.wood_log", 500, new ItemProperty("origin", "old-forest")) });
            var result = inventory.Prepare(session.Character.Inventory!, reward);
            saved = session.Character with { Inventory = result.State, Revision = session.Character.Revision + 1 };
            repository.Save(saved); Require(repository.Resume(session.ResumeToken).Character == saved);
        }
        using (var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade))
        {
            var restored = repository.Resume(session.ResumeToken).Character;
            Require(restored == saved && restored.Inventory!.Slots[0].Stack.Properties.Single().Value == "old-forest");
            Require(inventory.Prepare(restored.Inventory!, reward).Duplicate);
            var remove = Exchange(restored.Inventory!, new[] { Amount("resource.wood_log", 100, new ItemProperty("origin", "old-forest")) });
            var result = inventory.Prepare(restored.Inventory!, remove);
            repository.Save(restored with { Inventory = result.State, Revision = restored.Revision + 1 });
            Require(inventory.Prepare(repository.Load(saved.Id).Inventory!, remove).Duplicate);
        }
    });
    private static void InventoryMigration() => WithSaveDirectory(directory =>
    {
        var rules = new CharacterStateRules(); var inventory = new InventoryRules(); CharacterRecord legacy = CharacterRecord.New(Guid.NewGuid()) with { SchemaVersion = 1, Inventory = null };
        legacy = new ProgressionRules().Award(legacy, 85).Character;
        using var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade);
        string path = Path.Combine(directory, "characters", legacy.Id.ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(legacy));
        CharacterRecord migrated = repository.Load(legacy.Id);
        Require(migrated.SchemaVersion == 4 && migrated.Inventory!.Slots.Count == 0 && migrated.Level == 2 && migrated.Experience == 5 && migrated.SkillPoints == 1);
        Require(repository.Load(legacy.Id) == migrated);
        var kit = DevelopmentInventory.Prepare(inventory, migrated.Inventory!); Require(kit.Accepted && !kit.Duplicate);
        var saved = migrated with { Inventory = kit.State, Revision = migrated.Revision + 1 }; repository.Save(saved);
        Require(DevelopmentInventory.Prepare(inventory, repository.Load(saved.Id).Inventory!).Duplicate);
        Require(repository.Load(saved.Id).Inventory == kit.State);
    });
    private static void InventorySaveValidation() => WithSaveDirectory(directory =>
    {
        var rules = new CharacterStateRules(); var inventory = new InventoryRules();
        using var repository = new JsonCharacterRepository(directory, rules.Validate, rules.Upgrade);
        CharacterRecord fresh = repository.Create().Character;
        CharacterRecord saved = fresh with { Inventory = Add(inventory, fresh.Inventory!, Amount("resource.stone", 3)), Revision = fresh.Revision + 1 };
        repository.Save(saved);
        InvalidSave(() => repository.Save(fresh with { Revision = saved.Revision + 1 }));
        Require(repository.Load(saved.Id) == saved);
        CharacterRecord newer = new ProgressionRules().Award(saved, 1).Character;
        System.Threading.Tasks.Task.WhenAll(System.Threading.Tasks.Task.Run(() => repository.Save(newer)),
            System.Threading.Tasks.Task.Run(() => { try { repository.Save(saved); } catch (InvalidDataException) { } })).GetAwaiter().GetResult();
        Require(repository.Load(saved.Id) == newer, "Concurrent repository writes must not roll back the newest aggregate.");
        string path = Path.Combine(directory, "characters", saved.Id.ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(saved with { Inventory = null }));
        InvalidSave(() => repository.Load(saved.Id));
        File.WriteAllText(path, JsonSerializer.Serialize(saved).Replace("resource.stone", "missing.item", StringComparison.Ordinal));
        InvalidSave(() => repository.Load(saved.Id));
        Require(File.ReadAllText(path).Contains("missing.item", StringComparison.Ordinal));
    });
}
