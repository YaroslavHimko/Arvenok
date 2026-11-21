using System;
using System.Text.Json;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena;

public partial class ArenaSession
{
    public InventorySnapshot? Backpack { get; private set; }
    public bool InventoryPending => _pendingInventory is not null || _pendingEconomy is not null;
    public string InventoryMessage { get; private set; } = "";
    private InventoryRequest? _pendingInventory;
    private double _inventoryRetryAt;
    public string InspectBackpack() => Backpack is null ? "null" : JsonSerializer.Serialize(new { Backpack.CharacterId, Backpack.Capacity, Backpack.Revision, Backpack.Slots, Backpack.Equipment }, Json);
    public string BackpackFeedback() => InventoryMessage;
    public bool BackpackBusy() => InventoryPending;

    public void RequestInventoryOperation(InventoryOperation operation, int source = -1, int destination = -1, int quantity = 0)
    {
        if (_simulation is not null || Backpack is null || InventoryPending || !HasConnection) return;
        Guid Identity(int index)
        { foreach (InventorySlot slot in Backpack.Slots) if (slot.Index == index) return slot.Stack.Id; return Guid.Empty; }
        var request = new InventoryRequest(Guid.NewGuid(), Backpack.Revision, operation, source, destination, Identity(source), Identity(destination), quantity);
        SendInventoryRequest(request);
    }
    internal void SendInventoryRequest(InventoryRequest request)
    {
        if (_simulation is not null || !HasConnection || LocalEntityId == 0) return;
        _pendingInventory = request; InventoryMessage = "Waiting for server…";
        _inventoryRetryAt = Time.GetTicksMsec() / 1000.0 + 3;
        RpcId(1, MethodName.MutateInventory, JsonSerializer.Serialize(request, Json));
    }
    public void SubmitInventoryIntent(InventoryRequest request)
    { if (!InventoryPending) SendInventoryRequest(request); }
    private void RetryInventory()
    {
        RetryEconomy();
        if (_pendingInventory is not null && HasConnection && Time.GetTicksMsec() / 1000.0 >= _inventoryRetryAt)
        { _inventoryRetryAt = Time.GetTicksMsec() / 1000.0 + 3; RpcId(1, MethodName.MutateInventory, JsonSerializer.Serialize(_pendingInventory, Json)); }
    }
    private void SendInventory(long peer)
    {
        if (_simulation?.PlayerInventory(_simulation.EntityForPeer(peer)) is InventorySnapshot state)
            RpcId(peer, MethodName.ReceiveInventory, JsonSerializer.Serialize(new InventoryUpdate(Guid.Empty, true, false, "", state), Json));
    }
    private async void PublishInventoryChanges()
    {
        if (_simulation is null) return;
        long[] peers = _simulation.DrainInventoryChanges();
        if (peers.Length == 0) return;
        try { await _persistenceTail; foreach (long peer in peers) SendInventory(peer); }
        catch (Exception error) { GD.PushError("Inventory save failed: " + error.Message); GetTree().Quit(1); }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void MutateInventory(string json)
    {
        if (_simulation is null || _characters is null || !Multiplayer.IsServer()) return;
        long peer = Multiplayer.GetRemoteSenderId();
        if (!_connections.TryGetValue(peer, out var connection) || _simulation.EntityForPeer(peer) == 0) return;
        try
        {
            if (string.IsNullOrEmpty(json) || json.Length > 2048) throw new JsonException("Empty or oversized inventory request.");
            InventoryRequest request = JsonSerializer.Deserialize<InventoryRequest>(json, Json) ?? throw new JsonException("Empty request.");
            InventoryUpdate? update = _simulation.SubmitInventory(peer, request);
            if (update is null) return;
            PublishEvents(); // Enqueue the complete character aggregate before acknowledging success.
            await _persistenceTail;
            if (_connections.TryGetValue(peer, out var current) && current.Nonce == connection.Nonce)
                RpcId(peer, MethodName.ReceiveInventory, JsonSerializer.Serialize(update, Json));
        }
        catch (JsonException)
        {
            InventorySnapshot? state = _simulation.PlayerInventory(_simulation.EntityForPeer(peer));
            if (state is not null) RpcId(peer, MethodName.ReceiveInventory, JsonSerializer.Serialize(new InventoryUpdate(Guid.Empty, false, false, "Invalid Operation: malformed request.", state), Json));
        }
        catch (Exception error)
        { GD.PushError("Inventory persistence failed: " + error.Message); GetTree().Quit(1); }
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveInventory(string json)
    {
        if (_simulation is not null || LocalEntityId == 0) return;
        InventoryUpdate? update = JsonSerializer.Deserialize<InventoryUpdate>(json, Json);
        if (update is null || Backpack is not null && update.State.CharacterId != Backpack.CharacterId) return;
        if (update.OperationId == _pendingInventory?.OperationId) _pendingInventory = null;
        if (Backpack is not null && update.State.Revision < Backpack.Revision) return;
        if (Backpack is null) GD.Print($"Backpack loaded: character={update.State.CharacterId} slots={update.State.Capacity} occupied={update.State.Slots.Count}");
        Backpack = update.State; InventoryMessage = update.Message;
    }
}
