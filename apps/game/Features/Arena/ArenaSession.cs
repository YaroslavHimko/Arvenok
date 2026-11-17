using System;
using System.Collections.Generic;
using Arvenok.Contracts;
using Arvenok.Game.Features.Arena.Presentation;
using Arvenok.Hosting;
using Arvenok.Simulation;
using Godot;

namespace Arvenok.Game.Features.Arena;

/// <summary>Composition and Godot RPC boundary shared by both sides of the connection.</summary>
public partial class ArenaSession : Node2D
{
    private const double SnapshotInterval = 0.10;
    private readonly Dictionary<long, FighterSnapshot> _fighters = new();
    private ArenaSimulation? _simulation;
    private ENetMultiplayerPeer? _peer;
    private double _snapshotTimer;
    private bool _reportedFirstSnapshot;

    public IReadOnlyDictionary<long, FighterSnapshot> Fighters => _fighters;
    public string Status { get; private set; } = "Connecting...";
    public long LocalPeerId => Multiplayer.GetUniqueId();
    public int MaximumHealth => ArenaRules.MaxHp;
    public bool HasConnection => _peer?.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

    public override void _Ready()
    {
        SessionOptions options;
        try
        {
            options = SessionOptions.Parse(OS.GetCmdlineUserArgs());
            if (OS.HasFeature("dedicated_server")) options = options with { IsServer = true };
        }
        catch (ArgumentException error)
        {
            GD.PushError(error.Message);
            GetTree().Quit(2);
            return;
        }

        Multiplayer.PeerConnected += PeerJoined;
        Multiplayer.PeerDisconnected += PeerLeft;
        Multiplayer.ConnectedToServer += ConnectedToServer;
        Multiplayer.ConnectionFailed += ConnectionFailed;
        Multiplayer.ServerDisconnected += ServerDisconnected;

        _peer = new ENetMultiplayerPeer();
        Error result;
        if (options.IsServer)
        {
            _simulation = new ArenaSimulation();
            _peer.SetBindIP(options.BindAddress);
            result = _peer.CreateServer(options.Port, maxClients: ArenaRules.MaxPlayers);
            if (result != Error.Ok)
            {
                GD.PushError($"Could not listen on {options.BindAddress}:{options.Port}: {result}");
                GetTree().Quit(1);
                return;
            }
            GD.Print($"Arvenok server ready bind={options.BindAddress} port={options.Port} region={options.Region} shard={options.Shard}");
        }
        else
        {
            // Dedicated processes do not construct presentation nodes or load their visual resources.
            AddChild(new ArenaView { Name = "View", Session = this });
            Status = $"Connecting to {options.Host}:{options.Port}...";
            result = _peer.CreateClient(options.Host, options.Port);
            if (result != Error.Ok)
            {
                Status = $"Could not create client: {result}";
                GD.PushError(Status);
                return;
            }
        }
        Multiplayer.MultiplayerPeer = _peer;
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= PeerJoined;
        Multiplayer.PeerDisconnected -= PeerLeft;
        Multiplayer.ConnectedToServer -= ConnectedToServer;
        Multiplayer.ConnectionFailed -= ConnectionFailed;
        Multiplayer.ServerDisconnected -= ServerDisconnected;
        _peer?.Close();
    }

    private void ConnectedToServer()
    {
        Status = $"Connected as player {LocalPeerId}";
        GD.Print(Status);
    }

    private void ConnectionFailed()
    {
        Status = "Connection failed. Start server first.";
        GD.Print(Status);
    }

    private void ServerDisconnected()
    {
        _fighters.Clear();
        Status = "Server disconnected.";
        GD.Print(Status);
    }

    private void PeerJoined(long id)
    {
        if (_simulation is null) return;
        if (!_simulation.Join(id))
        {
            _peer?.DisconnectPeer((int)id);
            return;
        }
        GD.Print($"Player {id} joined. Total: {_simulation.PlayerCount}");
    }

    private void PeerLeft(long id)
    {
        _fighters.Remove(id);
        if (_simulation is null) return;
        _simulation.Leave(id);
        Rpc(MethodName.RemoveFighter, id);
        GD.Print($"Player {id} left.");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_simulation is null || !HasConnection) return;
        _simulation.Advance(delta);
        _snapshotTimer += delta;
        if (_snapshotTimer < SnapshotInterval) return;
        _snapshotTimer %= SnapshotInterval;
        foreach (FighterSnapshot fighter in _simulation.GetSnapshots())
            Rpc(MethodName.ReceiveFighter, fighter.Id, new Vector2(fighter.Position.X, fighter.Position.Y),
                fighter.Hp, fighter.Casting);
    }

    public void SendMove(Vector2 destination)
    {
        if (_simulation is null && HasConnection) RpcId(1, MethodName.RequestMove, destination);
    }

    public void SendAttack(long targetId, bool useSpell)
    {
        if (_simulation is null && HasConnection) RpcId(1, MethodName.RequestAttack, targetId, useSpell);
    }

    // Intent only. Sender identity always comes from the transport, never a client payload.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestMove(Vector2 destination)
    {
        if (!Multiplayer.IsServer()) return;
        _simulation?.RequestMove(Multiplayer.GetRemoteSenderId(), new System.Numerics.Vector2(destination.X, destination.Y));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestAttack(long targetId, bool useSpell)
    {
        if (!Multiplayer.IsServer()) return;
        _simulation?.RequestAttack(Multiplayer.GetRemoteSenderId(), targetId, useSpell);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReceiveFighter(long id, Vector2 position, int hp, bool casting)
    {
        if (_simulation is not null) return;
        _fighters[id] = new FighterSnapshot(id, new System.Numerics.Vector2(position.X, position.Y), hp, casting);
        if (!_reportedFirstSnapshot && _fighters.Count == ArenaRules.MaxPlayers)
        {
            GD.Print("Arena state received: 2 fighters.");
            _reportedFirstSnapshot = true;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RemoveFighter(long id)
    {
        if (_simulation is null) _fighters.Remove(id);
    }
}
