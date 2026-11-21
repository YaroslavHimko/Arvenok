using System;
using System.Linq;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>Fixed, one-time sample kit for the offline development host command. Never invoked by login or combat.</summary>
public static class DevelopmentInventory
{
    public static readonly Guid KitOperationId = new("81d7fc31-2516-40e1-8a5d-94d75fe66182");
    public static InventoryResult Prepare(InventoryRules rules, InventoryState state)
    {
        // A fixed reward ID is durable per character. Re-running the CLI is a safe no-op even after rearrangement.
        if (state.Receipts.Any(r => r.OperationId == KitOperationId && r.ServerOperation)) return new(true, true, "Development kit already granted.", state);
        ItemAmount Amount(string id, int quantity) => new(id, quantity, Array.Empty<ItemProperty>());
        return rules.Prepare(state, new InventoryExchange(KitOperationId, state.Revision, Array.Empty<ItemAmount>(), new[] {
            Amount("equipment.training_sword", 1), Amount("equipment.training_shield", 1), Amount("resource.wood_log", 1200),
            Amount("resource.stone", 80), Amount("resource.iron_ore", 35), Amount("monster.wolf_pelt", 12),
            Amount("keypiece.longsword_blade_fragment", 3), Amount("recipe.knights_longsword", 1),
            Amount("processed.iron_ingot", 10), Amount("consumable.training_tonic", 5) }));
    }
}
