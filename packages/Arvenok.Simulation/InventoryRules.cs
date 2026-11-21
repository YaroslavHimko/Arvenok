using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>Pure preparation: failed operations return the original state, never partial ownership changes.</summary>
public sealed partial class InventoryRules
{
    public ItemCatalog Catalog { get; }
    public InventoryRules(ItemCatalog? catalog = null) => Catalog = catalog ?? new ItemCatalog();
    public InventoryState Empty() => InventoryState.Empty(Catalog.DefaultCapacity);
    public void Validate(InventoryState state)
    {
        if (state is null || state.Capacity < 1 || state.Capacity > Catalog.MaximumCapacity || state.Revision < 1 || state.Revision == long.MaxValue
            || state.Slots.Count > state.Capacity || state.Receipts.Count > 4224) throw new InvalidDataException("Invalid saved inventory header.");
        var ids = new HashSet<Guid>(); int previous = -1;
        foreach (InventorySlot slot in state.Slots)
        {
            if (slot is null || slot.Stack is null || slot.Index <= previous || slot.Index >= state.Capacity || slot.Stack.Id == Guid.Empty || !ids.Add(slot.Stack.Id))
                throw new InvalidDataException("Invalid or duplicate saved inventory slot/identity.");
            previous = slot.Index;
            ItemDefinition definition = Catalog.Get(slot.Stack.ItemId);
            if (slot.Stack.Quantity < 1 || slot.Stack.Quantity > definition.MaximumStack || !ValidProperties(slot.Stack.Properties))
                throw new InvalidDataException("Invalid saved item quantity or metadata.");
        }
        var operations = new HashSet<(Guid, bool)>();
        foreach (InventoryReceipt receipt in state.Receipts)
            if (receipt is null || receipt.OperationId == Guid.Empty || !operations.Add((receipt.OperationId, receipt.ServerOperation)) || receipt.AppliedRevision < 2 || receipt.AppliedRevision > state.Revision
                || receipt.Fingerprint is null || receipt.Fingerprint.Length != 64 || !receipt.Fingerprint.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid saved inventory operation receipt.");
        if (state.Receipts.Count(r => r.ServerOperation) > 4096 || state.Receipts.Count(r => !r.ServerOperation) > 128)
            throw new InvalidDataException("Invalid inventory operation history.");
    }
    private static bool ValidProperties(IReadOnlyList<ItemProperty> properties) => properties is not null && properties.Count <= 16
        && properties.All(p => p is not null && !string.IsNullOrWhiteSpace(p.Key) && p.Key.Length <= 32 && p.Value is not null && p.Value.Length <= 256)
        && properties.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() == properties.Count
        && properties.Select(p => p.Key).SequenceEqual(properties.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal));
    private static bool SameProperties(IReadOnlyList<ItemProperty> left, IReadOnlyList<ItemProperty> right) => left.SequenceEqual(right);
    private bool Compatible(ItemStack left, ItemStack right) => left.ItemId == right.ItemId && Catalog.Get(left.ItemId).MaximumStack > 1
        && SameProperties(left.Properties, right.Properties);
    private static InventoryResult Fail(InventoryState state, string message) => new(false, false, message, state);
    private static string Fingerprint<T>(T operation) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(operation))));
    private static InventoryResult? Check(InventoryState state, Guid id, long revision, string fingerprint, bool server)
    {
        if (id == Guid.Empty) return Fail(state, "Invalid Operation: missing request ID.");
        // Client-chosen IDs cannot reserve or poison a future trusted reward's ID.
        InventoryReceipt? receipt = state.Receipts.FirstOrDefault(r => r.OperationId == id && r.ServerOperation == server);
        if (receipt is not null) return receipt.Fingerprint == fingerprint
            ? new(true, true, "Already applied.", state) : Fail(state, "Invalid Operation: request ID was reused.");
        if (revision != state.Revision) return Fail(state, "Inventory changed. Review the current slots and try again.");
        if (state.Revision >= long.MaxValue - 1) return Fail(state, "Inventory revision limit reached.");
        if (server && state.Receipts.Count(r => r.ServerOperation) >= 4096) return Fail(state, "Server reward history is full; storage maintenance required.");
        return null;
    }
    private static InventoryResult Finish(InventoryState before, ItemStack?[] slots, Guid id, string fingerprint, bool server, Guid createdItemId = default)
    {
        long revision = before.Revision + 1;
        var history = before.Receipts.Where(r => r.ServerOperation).Concat(before.Receipts.Where(r => !r.ServerOperation).TakeLast(server ? 128 : 127))
            .Append(new InventoryReceipt(id, fingerprint, revision, server) { CreatedItemId = createdItemId }).OrderBy(r => r.AppliedRevision).ToArray();
        var occupied = slots.Select((entry, index) => (entry, index)).Where(p => p.entry is not null).Select(p => new InventorySlot(p.index, p.entry!)).ToArray();
        return new(true, false, "Inventory updated.", new InventoryState(before.Capacity, revision, occupied, history));
    }
    private static ItemStack?[] Copy(InventoryState state)
    { var slots = new ItemStack?[state.Capacity]; foreach (InventorySlot slot in state.Slots) slots[slot.Index] = slot.Stack; return slots; }

    public InventoryResult Prepare(InventoryState state, InventoryRequest request)
    {
        Validate(state);
        string hash = Fingerprint(request);
        InventoryResult? check = Check(state, request.OperationId, request.ExpectedRevision, hash, false);
        if (check is not null) return check;
        if (!Enum.IsDefined(request.Operation)) return Fail(state, "Invalid Operation.");
        ItemStack?[] slots = Copy(state);
        if (request.Operation == InventoryOperation.Sort)
        {
            if (request.Source != -1 || request.Destination != -1 || request.SourceId != Guid.Empty || request.DestinationId != Guid.Empty || request.Quantity != 0)
                return Fail(state, "Invalid Operation: sort has no slot arguments.");
            ItemStack[] sorted = state.Slots.OrderBy(s => CategoryOrder(Catalog.Get(s.Stack.ItemId).Category))
                .ThenBy(s => s.Stack.ItemId, StringComparer.Ordinal).ThenBy(s => s.Index).Select(s => s.Stack).ToArray();
            Array.Clear(slots); Array.Copy(sorted, slots, sorted.Length);
            return Finish(state, slots, request.OperationId, hash, false);
        }
        int source = request.Source, destination = request.Destination;
        if (source < 0 || destination < 0 || source >= state.Capacity || destination >= state.Capacity || source == destination)
            return Fail(state, "Invalid Operation: slot index.");
        ItemStack? sourceStack = slots[source], to = slots[destination];
        if (sourceStack is null || sourceStack.Id != request.SourceId || (to?.Id ?? Guid.Empty) != request.DestinationId)
            return Fail(state, "Invalid Operation: slot identity changed.");
        if (request.Operation != InventoryOperation.Split && request.Quantity != 0) return Fail(state, "Invalid Operation: unexpected quantity.");
        InventoryOperation operation = request.Operation == InventoryOperation.Drag
            ? to is null ? InventoryOperation.Move : Compatible(sourceStack, to) ? InventoryOperation.Merge : InventoryOperation.Swap : request.Operation;
        switch (operation)
        {
            case InventoryOperation.Move:
                if (to is not null) return Fail(state, "Invalid Operation: destination is occupied.");
                slots[destination] = sourceStack; slots[source] = null; break;
            case InventoryOperation.Swap:
                if (to is null || Compatible(sourceStack, to)) return Fail(state, "Invalid Operation: compatible stacks must merge.");
                slots[source] = to; slots[destination] = sourceStack; break;
            case InventoryOperation.Merge:
                if (to is null || !Compatible(sourceStack, to)) return Fail(state, "Cannot Merge: items are incompatible.");
                int moved = Math.Min(sourceStack.Quantity, Catalog.Get(to.ItemId).MaximumStack - to.Quantity);
                if (moved == 0) return Fail(state, "Cannot Merge: destination stack is full.");
                slots[destination] = to with { Quantity = to.Quantity + moved };
                slots[source] = moved == sourceStack.Quantity ? null : sourceStack with { Quantity = sourceStack.Quantity - moved }; break;
            case InventoryOperation.Split:
                if (to is not null || request.Quantity < 1 || request.Quantity >= sourceStack.Quantity || Catalog.Get(sourceStack.ItemId).MaximumStack <= 1)
                    return Fail(state, "Invalid Operation: choose 1 to quantity minus 1 and an empty destination.");
                slots[source] = sourceStack with { Quantity = sourceStack.Quantity - request.Quantity };
                slots[destination] = sourceStack with { Id = Guid.NewGuid(), Quantity = request.Quantity }; break;
        }
        return Finish(state, slots, request.OperationId, hash, false);
    }

    /// <summary>Server-only add/remove/exchange. Prepare both sides before an external ownership coordinator commits them.</summary>
    public InventoryResult Prepare(InventoryState state, InventoryExchange exchange)
    {
        Validate(state);
        string hash = Fingerprint(exchange);
        InventoryResult? check = Check(state, exchange.OperationId, exchange.ExpectedRevision, hash, true);
        if (check is not null) return check;
        if (exchange.Remove is null || exchange.Add is null || exchange.Remove.Count + exchange.Add.Count is < 1 or > 64)
            return Fail(state, "Invalid Operation: empty or excessive exchange.");
        foreach (ItemAmount amount in exchange.Remove.Concat(exchange.Add))
            if (amount is null || amount.ItemId is null || !Catalog.TryGet(amount.ItemId, out _) || amount.Quantity is < 1 or > 1000000 || !ValidProperties(amount.Properties))
                return Fail(state, "Invalid Operation: item, quantity or metadata.");
        ItemStack?[] slots = Copy(state);
        foreach (ItemAmount amount in exchange.Remove)
        {
            int needed = amount.Quantity;
            for (int i = 0; i < slots.Length && needed > 0; i++)
            {
                ItemStack? entry = slots[i];
                if (entry is null || entry.ItemId != amount.ItemId || !SameProperties(entry.Properties, amount.Properties)) continue;
                int take = Math.Min(needed, entry.Quantity); needed -= take;
                slots[i] = take == entry.Quantity ? null : entry with { Quantity = entry.Quantity - take };
            }
            if (needed > 0) return Fail(state, "Not Enough Items.");
        }
        foreach (ItemAmount amount in exchange.Add)
        {
            ItemDefinition definition = Catalog.Get(amount.ItemId); int remaining = amount.Quantity;
            if (definition.MaximumStack > 1)
                for (int i = 0; i < slots.Length && remaining > 0; i++)
                {
                    ItemStack? entry = slots[i];
                    if (entry is null || entry.ItemId != amount.ItemId || !SameProperties(entry.Properties, amount.Properties)) continue;
                    int put = Math.Min(remaining, definition.MaximumStack - entry.Quantity); remaining -= put;
                    slots[i] = entry with { Quantity = entry.Quantity + put };
                }
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                if (slots[i] is not null) continue;
                int put = Math.Min(remaining, definition.MaximumStack); remaining -= put;
                slots[i] = new ItemStack(Guid.NewGuid(), amount.ItemId, put, amount.Properties);
            }
            if (remaining > 0) return Fail(state, "Inventory Full.");
        }
        return Finish(state, slots, exchange.OperationId, hash, true);
    }
    public long Count(InventoryState state, string itemId, IReadOnlyList<ItemProperty> properties) =>
        state.Slots.Where(s => s.Stack.ItemId == itemId && SameProperties(s.Stack.Properties, properties)).Sum(s => (long)s.Stack.Quantity);
    private static int CategoryOrder(ItemCategory category) => category switch
    { ItemCategory.Equipment => 0, ItemCategory.Consumable => 1, ItemCategory.RawMaterial => 2, ItemCategory.ProcessedMaterial => 3,
        ItemCategory.MonsterMaterial => 4, ItemCategory.EquipmentKeyPiece => 5, ItemCategory.Recipe => 6, _ => 7 };
}
