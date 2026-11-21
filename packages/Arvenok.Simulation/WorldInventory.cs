using System;
using System.Collections.Generic;
using System.Linq;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed partial class WorldSimulation
{
    public InventoryRules Inventory { get; }
    public CharacterStateRules Characters { get; }
    private readonly HashSet<long> _inventoryChangedPeers = new();
    public long[] DrainInventoryChanges() { long[] peers = _inventoryChangedPeers.ToArray(); _inventoryChangedPeers.Clear(); return peers; }
    public InventorySnapshot? PlayerInventory(long entityId) => Character(entityId) is CharacterRecord record
        ? new(record.Id, record.Inventory!.Capacity, record.Inventory.Revision, record.Inventory.Slots, Inventory.Catalog.Items)
            { Equipment = record.Equipment!, Recipes = Economy.Recipes } : null;

    /// <summary>Peer ownership comes from the transport. No client-supplied character ID is accepted.</summary>
    public InventoryUpdate? SubmitInventory(long peerId, InventoryRequest request)
    {
        if (!_peers.TryGetValue(peerId, out long entityId) || !_entities.TryGetValue(entityId, out CombatEntity? actor)) return null;
        actor.InventoryTokens = Math.Min(12, actor.InventoryTokens + (Time - actor.LastInventoryTokenTime) * 6);
        actor.LastInventoryTokenTime = Time;
        InventoryResult result;
        if (actor.InventoryTokens < 1) result = new(false, false, "Too many inventory requests.", actor.Character!.Inventory!);
        else
        {
            actor.InventoryTokens--;
            result = Inventory.Prepare(actor.Character!.Inventory!, request);
            CommitInventory(actor, result);
        }
        return new(request.OperationId, result.Accepted, result.Duplicate, result.Message, PlayerInventory(entityId)!);
    }

    /// <summary>Trusted reward/removal boundary, never exposed as a client RPC. Expected revision is mandatory.</summary>
    public InventoryResult? ExchangeInventory(long entityId, InventoryExchange exchange)
    {
        if (!_entities.TryGetValue(entityId, out CombatEntity? actor) || actor.Character is null) return null;
        InventoryResult result = Inventory.Prepare(actor.Character.Inventory!, exchange);
        CommitInventory(actor, result);
        if (result.Accepted && !result.Duplicate) _inventoryChangedPeers.Add(actor.PeerId);
        return result;
    }
    private void CommitInventory(CombatEntity actor, InventoryResult result)
    {
        if (!result.Accepted || result.Duplicate) return;
        actor.Character = actor.Character! with { Inventory = result.State, Revision = checked(actor.Character.Revision + 1) };
        QueueProgression(actor); // Keep the existing progression revision precondition current too.
    }
}
