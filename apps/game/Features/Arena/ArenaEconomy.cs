using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena;

public partial class ArenaSession
{
    public GroundLoot[] Loot { get; private set; } = Array.Empty<GroundLoot>();
    public string InspectLoot() => JsonSerializer.Serialize(Loot, Json);
    public string InspectWorld() => JsonSerializer.Serialize(new { Time = ServerTime, LocalEntityId, Entities = Entities.Values }, Json);
    public string InspectCombatEvents() => JsonSerializer.Serialize(Activity, Json);
    private string _lastEconomyReply = "null";
    public Guid LastCraftedItem { get; private set; }
    public double EconomyMessageAt { get; private set; } = double.NegativeInfinity;
    public string InspectEconomyReply() => _lastEconomyReply;
    private EconomyRequest? _pendingEconomy;
    private readonly HashSet<long> _economyInFlight = new();
    private double _economyRetryAt;
    public void RequestEconomy(EconomyOperation operation, Guid item, EquipmentSlot slot = EquipmentSlot.Weapon)
    {
        if (Backpack is null || InventoryPending) return;
        SubmitEconomyIntent(new(Guid.NewGuid(), Backpack.Revision, operation, item, slot));
    }
    public void SubmitEconomyIntent(EconomyRequest request)
    {
        if (_simulation is not null || InventoryPending || !HasConnection || LocalEntityId == 0) return;
        _pendingEconomy = request; InventoryMessage = "Waiting for server…"; _economyRetryAt = Time.GetTicksMsec() / 1000.0 + 3;
        RpcId(1, MethodName.MutateEconomy, JsonSerializer.Serialize(request, Json));
    }
    private void RetryEconomy()
    {
        if (_pendingEconomy is null || !HasConnection || Time.GetTicksMsec() / 1000.0 < _economyRetryAt) return;
        _economyRetryAt = Time.GetTicksMsec() / 1000.0 + 3;
        RpcId(1, MethodName.MutateEconomy, JsonSerializer.Serialize(_pendingEconomy, Json));
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void MutateEconomy(string json)
    {
        if (_simulation is null || _characters is null || !Multiplayer.IsServer()) return;
        long peer = Multiplayer.GetRemoteSenderId();
        if (!_connections.TryGetValue(peer, out var connection) || _simulation.EntityForPeer(peer) == 0 || !_economyInFlight.Add(peer)) return;
        EconomyRequest? request = null;
        try
        {
            if (string.IsNullOrEmpty(json) || json.Length > 1024) throw new JsonException("Invalid request size.");
            request = JsonSerializer.Deserialize<EconomyRequest>(json, Json) ?? throw new JsonException("Empty request.");
            // Drain prior asynchronous progression/inventory writes. No await occurs between preparation,
            // synchronous durable save and simulation commit; competing RPCs cannot claim the same drop.
            while (true) { var tail = _persistenceTail; await tail; if (tail == _persistenceTail) break; }
            if (!_connections.TryGetValue(peer, out var current) || current.Nonce != connection.Nonce) return;
            EconomyResult? result = _simulation.SubmitEconomy(peer, request, _characters.Save);
            if (result is null) return;
            PublishEvents();
            RpcId(peer, MethodName.ReceiveEconomy, JsonSerializer.Serialize(new EconomyUpdate(request.OperationId, result.Accepted, result.Duplicate,
                result.Message, result.CreatedItemId, _simulation.PlayerInventory(_simulation.EntityForPeer(peer))!), Json));
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            GD.PushWarning("Economy request failed: " + error.Message);
            if (_simulation.PlayerInventory(_simulation.EntityForPeer(peer)) is InventorySnapshot state)
                RpcId(peer, MethodName.ReceiveEconomy, JsonSerializer.Serialize(new EconomyUpdate(request?.OperationId ?? Guid.Empty, false, false,
                    error is JsonException ? "Invalid item request." : "Could not save items. Nothing was consumed; try again.", Guid.Empty, state), Json));
        }
        catch (Exception error) { GD.PushError("Economy persistence failed: " + error.Message); GetTree().Quit(1); }
        finally { _economyInFlight.Remove(peer); }
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEconomy(string json)
    {
        if (_simulation is not null || LocalEntityId == 0) return;
        EconomyUpdate? update = JsonSerializer.Deserialize<EconomyUpdate>(json, Json);
        if (update is null || Backpack is not null && update.State.CharacterId != Backpack.CharacterId) return;
        if (update.OperationId == _pendingEconomy?.OperationId) _pendingEconomy = null;
        if (Backpack is not null && update.State.Revision < Backpack.Revision) return;
        Backpack = update.State; InventoryMessage = update.Message;
        EconomyMessageAt = ServerTime;
        if (update.Accepted && !update.Duplicate && update.Message.StartsWith("Crafted ", StringComparison.Ordinal)) LastCraftedItem = update.CreatedItemId;
        _lastEconomyReply = JsonSerializer.Serialize(new { update.OperationId, update.Accepted, update.Duplicate, update.Message, update.CreatedItemId }, Json);
        GD.Print($"Economy result op={update.OperationId} accepted={update.Accepted} duplicate={update.Duplicate} created={update.CreatedItemId} message={update.Message}");
    }
}
