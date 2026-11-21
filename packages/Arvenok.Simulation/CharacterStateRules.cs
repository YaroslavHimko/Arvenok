using System;
using System.IO;
using System.Linq;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed class CharacterStateRules(ProgressionRules? progression = null, InventoryRules? inventory = null)
{
    private readonly ProgressionRules _progression = progression ?? new();
    private readonly InventoryRules _inventory = inventory ?? new();
    public CharacterRecord Upgrade(CharacterRecord character)
    {
        if (character.SchemaVersion is 1 or 2 or 3)
        {
            _progression.Validate(character);
            if (character.SchemaVersion == 1 && character.Inventory is not null) throw new InvalidDataException("Legacy character unexpectedly contains inventory.");
            if (character.SchemaVersion == 2 && character.Inventory is null) throw new InvalidDataException("Missing saved inventory.");
            if (character.SchemaVersion < 3 && (character.Equipment?.Weapon is not null || character.Equipment?.Body is not null || character.Equipment?.OffHand is not null)) throw new InvalidDataException("Legacy character unexpectedly contains equipment.");
            if (character.SchemaVersion == 3 && (character.Inventory is null || character.Equipment is null)) throw new InvalidDataException("Missing legacy ownership state.");
            character = character with { SchemaVersion = 4, Revision = checked(character.Revision + 1), Inventory = character.Inventory ?? _inventory.Empty(), Equipment = character.Equipment ?? new() };
        }
        Validate(character); return character;
    }
    public void Validate(CharacterRecord character)
    {
        _progression.Validate(character);
        if (character.SchemaVersion != 4 || character.Inventory is null || character.Equipment is null) throw new InvalidDataException("Missing or unsupported saved inventory/equipment.");
        _inventory.Validate(character.Inventory);
        var gear = new[] { character.Equipment.Weapon, character.Equipment.Body, character.Equipment.OffHand };
        var occupied = gear.Select((item, index) => (item, index)).Where(p => p.item is not null).Select(p => new InventorySlot(p.index, p.item!)).ToArray();
        _inventory.Validate(new InventoryState(3, 1, occupied, Array.Empty<InventoryReceipt>()));
        if (character.Equipment.OffHand is not null && _inventory.Weapon(character)?.TwoHanded == true) throw new InvalidDataException("Two-handed MainHand reserves OffHand.");
        foreach (InventorySlot slot in occupied)
        {
            EquipmentDefinition? definition = _inventory.Catalog.Get(slot.Stack.ItemId).Equipment;
            if (definition is null || (int)definition.Slot != slot.Index || !definition.Supports(character.Class) || definition.MinimumLevel > character.Level
                || character.Inventory.Slots.Any(i => i.Stack.Id == slot.Stack.Id)) throw new InvalidDataException("Invalid equipped ownership or restriction.");
        }
    }
}
