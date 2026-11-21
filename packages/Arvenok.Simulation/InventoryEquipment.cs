using System;
using System.Linq;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed partial class InventoryRules
{
    private static string EconomyFingerprint(EconomyRequest request) => Fingerprint(new { Domain = "Economy.v1", Request = request });
    public EconomyResult? CheckEconomy(CharacterRecord character, EconomyRequest request)
    {
        Validate(character.Inventory!);
        InventoryResult? check = Check(character.Inventory!, request.OperationId, request.ExpectedRevision, EconomyFingerprint(request), false);
        if (check is null) return null;
        Guid created = check.Duplicate ? character.Inventory!.Receipts.Single(r => r.OperationId == request.OperationId && !r.ServerOperation).CreatedItemId : Guid.Empty;
        return new(check.Accepted, check.Duplicate, check.Message, character, created);
    }
    private static EconomyResult EconomyFail(CharacterRecord character, string message) => new(false, false, message, character);
    private static EconomyResult EconomyFinish(CharacterRecord character, ItemStack?[] slots, EquipmentState gear, EconomyRequest request, string message, Guid created = default)
    {
        InventoryResult inventory = Finish(character.Inventory!, slots, request.OperationId, EconomyFingerprint(request), false, created);
        return new(true, false, message, character with { Inventory = inventory.State, Equipment = gear, Revision = checked(character.Revision + 1) }, created);
    }
    public EconomyResult PrepareEquipment(CharacterRecord character, EconomyRequest request)
    {
        EconomyResult? check = CheckEconomy(character, request); if (check is not null) return check;
        if (!Enum.IsDefined(request.Slot)) return EconomyFail(character, "Invalid equipment slot.");
        ItemStack?[] slots = Copy(character.Inventory!); EquipmentState equipment = character.Equipment!;
        if (request.Operation == EconomyOperation.Equip)
        {
            int source = Array.FindIndex(slots, s => s?.Id == request.ItemId);
            if (source < 0) return EconomyFail(character, "Item is not in your backpack.");
            ItemStack item = slots[source]!; EquipmentDefinition? definition = Catalog.Get(item.ItemId).Equipment;
            if (definition is null) return EconomyFail(character, "This legacy or non-equipment item cannot be equipped.");
            if (!definition.Supports(character.Class)) return EconomyFail(character, "Wrong class for this equipment.");
            if (definition.MinimumLevel > character.Level) return EconomyFail(character, $"Requires level {definition.MinimumLevel}.");
            if (request.Slot != definition.Slot) return EconomyFail(character, "Wrong equipment slot.");
            if (definition.Slot == EquipmentSlot.OffHand && Weapon(character)?.TwoHanded == true) return EconomyFail(character, "MainHand reserves OffHand; unequip the two-handed weapon first.");
            slots[source] = equipment.At(definition.Slot); equipment = equipment.With(definition.Slot, item);
            if (definition.TwoHanded && equipment.OffHand is ItemStack shield)
            {
                int empty = Array.FindIndex(slots, s => s is null);
                if (empty < 0) return EconomyFail(character, "Inventory Full. No room for the displaced shield.");
                slots[empty] = shield; equipment = equipment with { OffHand = null };
            }
        }
        else if (request.Operation == EconomyOperation.Unequip)
        {
            ItemStack? item = equipment.At(request.Slot);
            if (item is null || item.Id != request.ItemId) return EconomyFail(character, "Equipped item changed.");
            int empty = Array.FindIndex(slots, s => s is null);
            if (empty < 0) return EconomyFail(character, "Inventory Full.");
            slots[empty] = item; equipment = equipment.With(request.Slot, null);
        }
        else return EconomyFail(character, "Invalid equipment operation.");
        return EconomyFinish(character, slots, equipment, request, request.Operation == EconomyOperation.Equip ? "Equipment equipped." : "Equipment unequipped.");
    }
    public (int Damage, int MaxHp) EquipmentBonuses(CharacterRecord character) =>
        (character.Equipment?.Weapon is ItemStack weapon ? Catalog.Get(weapon.ItemId).Equipment!.FlatDamage : 0,
         character.Equipment?.Body is ItemStack body ? Catalog.Get(body.ItemId).Equipment!.MaxHp : 0);
    public EquipmentDefinition? Weapon(CharacterRecord? character) => character?.Equipment?.Weapon is ItemStack item ? Catalog.Get(item.ItemId).Equipment : null;
}
