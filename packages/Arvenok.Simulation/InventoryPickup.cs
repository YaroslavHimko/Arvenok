using System;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed partial class InventoryRules
{
    /// <summary>The world owns the drop identity. Whole insertion succeeds before its location is changed.</summary>
    public EconomyResult PreparePickup(CharacterRecord character, EconomyRequest request, ItemStack item)
    {
        EconomyResult? check = CheckEconomy(character, request); if (check is not null) return check;
        if (request.Operation != EconomyOperation.Pickup) return EconomyFail(character, "Invalid pickup operation.");
        Validate(new InventoryState(1, 1, new[] { new InventorySlot(0, item) }, Array.Empty<InventoryReceipt>()));
        ItemStack?[] slots = Copy(character.Inventory!);
        if (Array.Exists(slots, s => s?.Id == item.Id) || character.Equipment!.Weapon?.Id == item.Id || character.Equipment.Body?.Id == item.Id || character.Equipment.OffHand?.Id == item.Id)
            return EconomyFail(character, "Item is already owned.");
        int remaining = item.Quantity;
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            ItemStack? current = slots[i]; if (current is null || !Compatible(current, item)) continue;
            int put = Math.Min(remaining, Catalog.Get(item.ItemId).MaximumStack - current.Quantity);
            slots[i] = current with { Quantity = current.Quantity + put }; remaining -= put;
        }
        if (remaining > 0)
        {
            int empty = Array.FindIndex(slots, s => s is null);
            if (empty < 0) return EconomyFail(character, "Inventory Full.");
            slots[empty] = item with { Quantity = remaining };
        }
        return EconomyFinish(character, slots, character.Equipment!, request, "Picked up " + Catalog.Get(item.ItemId).DisplayName + ".", item.Id);
    }
}
