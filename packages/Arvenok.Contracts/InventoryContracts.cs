using System;
using System.Collections.Generic;
using System.Linq;

namespace Arvenok.Contracts;

public enum ItemCategory { Equipment, RawMaterial, ProcessedMaterial, MonsterMaterial, EquipmentKeyPiece, Consumable, Recipe, SpellScroll }
public sealed record ItemDefinition(string ItemId, string DisplayName, string Description, ItemCategory Category,
    string Icon, int MaximumStack, bool Tradable, bool Droppable)
{
    public string IconFormat { get; init; } = "svg";
    public string Rarity { get; init; } = "common";
    public EquipmentDefinition? Equipment { get; init; }
    public string? RecipeId { get; init; }
    public string? SkillNodeId { get; init; }
}
public sealed record ItemProperty(string Key, string Value);

/// <summary>Owned identity, distinct from catalog identity. Properties are opaque and all affect stacking.</summary>
public sealed record ItemStack
{
    public Guid Id { get; init; }
    public string ItemId { get; init; }
    public int Quantity { get; init; }
    public IReadOnlyList<ItemProperty> Properties { get; }
    public ItemStack(Guid id, string itemId, int quantity, IReadOnlyList<ItemProperty> properties)
    { Id = id; ItemId = itemId; Quantity = quantity; Properties = Array.AsReadOnly(properties.ToArray()); }
    public bool Equals(ItemStack? other) => other is not null && Id == other.Id && ItemId == other.ItemId && Quantity == other.Quantity && Properties.SequenceEqual(other.Properties);
    public override int GetHashCode() => HashCode.Combine(Id, ItemId, Quantity);
}
public sealed record InventorySlot(int Index, ItemStack Stack);
public sealed record InventoryReceipt(Guid OperationId, string Fingerprint, long AppliedRevision, bool ServerOperation)
{
    public Guid CreatedItemId { get; init; }
}

/// <summary>Immutable server save state; absent indices are empty. Receipts never go to other clients.</summary>
public sealed record InventoryState
{
    public int Capacity { get; }
    public long Revision { get; }
    public IReadOnlyList<InventorySlot> Slots { get; }
    public IReadOnlyList<InventoryReceipt> Receipts { get; }
    public InventoryState(int capacity, long revision, IReadOnlyList<InventorySlot> slots, IReadOnlyList<InventoryReceipt> receipts)
    { Capacity = capacity; Revision = revision; Slots = Array.AsReadOnly(slots.ToArray()); Receipts = Array.AsReadOnly(receipts.ToArray()); }
    public static InventoryState Empty(int capacity = 24) => new(capacity, 1, Array.Empty<InventorySlot>(), Array.Empty<InventoryReceipt>());
    public bool Equals(InventoryState? other) => other is not null && Capacity == other.Capacity && Revision == other.Revision
        && Slots.SequenceEqual(other.Slots) && Receipts.SequenceEqual(other.Receipts);
    public override int GetHashCode() => HashCode.Combine(Capacity, Revision);
}
public enum InventoryOperation { Drag, Move, Swap, Merge, Split, Sort }
public sealed record InventoryRequest(Guid OperationId, long ExpectedRevision, InventoryOperation Operation,
    int Source = -1, int Destination = -1, Guid SourceId = default, Guid DestinationId = default, int Quantity = 0);
public sealed record InventorySnapshot(Guid CharacterId, int Capacity, long Revision, IReadOnlyList<InventorySlot> Slots,
    IReadOnlyList<ItemDefinition> Catalog)
{
    public EquipmentState Equipment { get; init; } = new();
    public IReadOnlyList<RecipeDefinition> Recipes { get; init; } = Array.Empty<RecipeDefinition>();
}
public sealed record InventoryUpdate(Guid OperationId, bool Accepted, bool Duplicate, string Message, InventorySnapshot State);

/// <summary>Trusted server inputs only. Supports multi-ingredient consumption and output capacity in one preparation.</summary>
public sealed record ItemAmount(string ItemId, int Quantity, IReadOnlyList<ItemProperty> Properties);
public sealed record InventoryExchange(Guid OperationId, long ExpectedRevision, IReadOnlyList<ItemAmount> Remove, IReadOnlyList<ItemAmount> Add);
public sealed record InventoryResult(bool Accepted, bool Duplicate, string Message, InventoryState State);
