using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Arvenok.Contracts;
using Arvenok.Game.Features.Arena.Presentation;
using Arvenok.Hosting;
using Arvenok.Simulation;
using Godot;

namespace Arvenok.Game.Features.Arena;

/// <summary>Existing RPC composition point. Historical filename retained to preserve script UIDs.</summary>
public partial class ArenaSession : Node2D
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private readonly Dictionary<long, EntitySnapshot> _entities = new();
    private readonly Queue<CombatEvent> _activity = new();
    private readonly CombatBalance _balance = new();
    private WorldSimulation? _simulation;
    private MultiplayerPeer? _peer;
    private double _snapshotTimer;
    private long _revision;
    private long _receivedRevision;
    private long _receivedEvent;
    private long _commandSequence;
    private bool _reportedTwoPlayers;
    private bool _reportedMonsters;
    private SmokeDriver? _smoke;
    private ProgressionSmokeDriver? _progressionSmoke;

    public IReadOnlyDictionary<long, EntitySnapshot> Entities => _entities;
    public IEnumerable<CombatEvent> Activity => _activity;
    public IReadOnlyList<ClassDefinition> Classes => _balance.Classes;
    public WorldDefinition World => WorldMap.Outdoor;
    public string Status { get; private set; } = "Connecting...";
    public long LocalEntityId { get; private set; }
    public double ServerTime { get; private set; }
    public double SnapshotTime { get; private set; }
    public long SnapshotRevision => _receivedRevision;
    public bool HasConnection => _peer?.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;
    public ClassDefinition Definition(CharacterClass kind) => Classes[(int)kind];
    public bool IsProgressionSmokeFinished() => _progressionSmoke?.Finished == true;

    public override void _Ready()
    {
        SessionOptions options;
        try
        {
            options = SessionOptions.Parse(OS.GetCmdlineUserArgs());
            if (OS.HasFeature("dedicated_server")) options = options with { IsServer = true };
        }
        catch (ArgumentException error) { GD.PushError(error.Message); GetTree().Quit(2); return; }
        Multiplayer.PeerConnected += PeerJoined;
        Multiplayer.PeerDisconnected += PeerLeft;
        Multiplayer.ConnectedToServer += ConnectedToServer;
        Multiplayer.ConnectionFailed += ConnectionFailed;
        Multiplayer.ServerDisconnected += ServerDisconnected;
        _options = options;
        Error result;
        if (options.IsServer)
        {
            _simulation = new WorldSimulation(_balance);
            if (!InitializeCharacterStore(options)) return;
            result = OpenSecurePeer();
            if (result != Error.Ok) { GD.PushError($"Could not listen on {options.BindAddress}:{options.Port}: {result}"); GetTree().Quit(1); return; }
            GD.Print($"Arvenok server ready bind={options.BindAddress} port={options.Port} region={options.Region} shard={options.Shard}");
        }
        else
        {
            AddChild(new AccountScreens { Name = "Accounts", Session = this });
            if (options.SmokeRole is "attacker" or "defender") _smoke = new SmokeDriver(this, options.SmokeRole);
            if (options.SmokeRole.StartsWith("progression-", StringComparison.Ordinal)) _progressionSmoke = new ProgressionSmokeDriver(this, options.SmokeRole);
            Status = $"Connecting to {options.Host}:{options.Port}...";
            result = OpenSecurePeer();
            if (result != Error.Ok) { Status = $"Could not create client: {result}"; GD.PushError(Status); return; }
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
        CloseCharacterStore();
        _peer?.Close();
    }

    private void ConnectedToServer()
    {
        Status = "Connected securely. Log in or register.";
    }
    private void ConnectionFailed() { ClearWorldPresentation(); AccountState = null; AccountBusy = false; Status = "Server unavailable or TLS verification failed. Retry connection."; }
    private void ServerDisconnected() { ClearWorldPresentation(); AccountState = null; AccountBusy = false; Status = "Server disconnected. Reconnect and log in."; }

    private void PeerJoined(long peerId)
    {
        if (_simulation is null) return;
        _connections[peerId] = (Guid.NewGuid(), _simulation.Time);
    }

    private void PeerLeft(long peerId)
    {
        if (_simulation is null) return;
        SaveAndLeave(peerId);
        _connections.Remove(peerId); _identifying.Remove(peerId);
        _accountSessions.Remove(peerId);
        _accountRequests.Remove(peerId); _lifecycleCommands.Remove(peerId);
        GD.Print($"Peer {peerId} left. Total: {_simulation.PlayerCount}. World continues at {_simulation.Time:F2}s.");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_simulation is null)
        {
            if (HasConnection && LocalEntityId != 0) ServerTime += delta;
            CheckAccountTimeout();
            _smoke?.Advance();
            RetryInventory();
            _progressionSmoke?.Advance();
            return;
        }
        // An empty world continues ticking. Client acknowledgements never gate combat.
        _simulation.Advance(delta);
        PublishEvents();
        CheckCharacterStorage();
        _snapshotTimer += delta;
        if (_snapshotTimer < _balance.SnapshotInterval) return;
        _snapshotTimer %= _balance.SnapshotInterval;
        PublishState(false);
    }

    private void PublishState(bool reliable)
    {
        if (_simulation is null || _simulation.PlayerCount == 0) return;
        var state = new WorldSnapshot(++_revision, _simulation.Time, _simulation.GetSnapshots().ToArray()) { Loot = _simulation.GroundDrops() };
        using var output = new MemoryStream();
        using (var compressor = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
            JsonSerializer.Serialize(compressor, state, Json);
        byte[] payload = output.ToArray();
        // Leave room for RPC/ENet headers. Larger rosters use reliable fragmentation rather than lossy oversized packets.
        foreach (int peer in Multiplayer.GetPeers())
            if (_simulation.EntityForPeer(peer) != 0) RpcId(peer, MethodName.ReceiveEssentialState, payload);
    }

    private void PublishEvents()
    {
        if (_simulation is null) return;
        CombatEvent[] events = _simulation.DrainEvents();
        foreach (CombatEvent entry in events)
        {
            if (entry.Kind == CombatEventKind.Rejected)
            {
                foreach (int peer in Multiplayer.GetPeers())
                    if (_simulation.EntityForPeer(peer) == entry.ActorId)
                        RpcId(peer, MethodName.ReceiveEvent, JsonSerializer.Serialize(entry, Json));
            }
            else foreach (int peer in Multiplayer.GetPeers())
                if (_simulation.EntityForPeer(peer) != 0) RpcId(peer, MethodName.ReceiveEvent, JsonSerializer.Serialize(entry, Json));
        }
        if (events.Any(entry => entry.Kind != CombatEventKind.Rejected)) PublishState(true);
        PublishProgression();
        PublishInventoryChanges();
    }

    public void Send(CommandKind kind, long targetId = 0, AbilitySlot slot = AbilitySlot.Basic,
        Vector2 destination = default, CharacterClass characterClass = CharacterClass.Warrior)
    {
        if (_simulation is not null || !HasConnection || LocalEntityId == 0 || _receivedRevision == 0) return;
        RpcId(1, MethodName.RequestCommand, ++_commandSequence, ServerTime, (int)kind, targetId, (int)slot, destination, (int)characterClass);
    }

    internal void SendDuplicateForSmoke(CommandKind kind, long targetId, AbilitySlot slot)
    {
        Send(kind, targetId, slot);
        RpcId(1, MethodName.RequestCommand, _commandSequence, ServerTime, (int)kind, targetId, (int)slot, Vector2.Zero, (int)CharacterClass.Warrior);
    }

    // No client-supplied actor ID, damage, HP, cooldown, or resulting position.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestCommand(long sequence, double observedTime, int kind, long targetId, int slot, Vector2 destination, int characterClass)
    {
        if (!Multiplayer.IsServer() || _simulation is null) return;
        long entityId = _simulation.EntityForPeer(Multiplayer.GetRemoteSenderId());
        _simulation.Submit(entityId, new WorldCommand(sequence, observedTime, (CommandKind)kind, targetId,
            (AbilitySlot)slot, new System.Numerics.Vector2(destination.X, destination.Y), (CharacterClass)characterClass));
        PublishEvents();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Welcome(long entityId, string reconnectCredential)
    {
        if (_simulation is not null) return;
        LocalEntityId = entityId;
        if (GetNodeOrNull<ArenaView>("View") is null) AddChild(new ArenaView { Name = "View", Session = this });
        Status = "Connected to the shared world"; GD.Print(Status);
        GD.Print($"Local entity assigned: {entityId}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReceiveSnapshot(byte[] payload) => ApplyState(payload);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEssentialState(byte[] payload) => ApplyState(payload);

    private void ApplyState(byte[] payload)
    {
        if (_simulation is not null || LocalEntityId == 0) return;
        using var input = new MemoryStream(payload, writable: false);
        using var decompressor = new BrotliStream(input, CompressionMode.Decompress);
        WorldSnapshot? state = JsonSerializer.Deserialize<WorldSnapshot>(decompressor, Json);
        if (state is null || state.Revision <= _receivedRevision) return;
        _receivedRevision = state.Revision; ServerTime = state.Time; SnapshotTime = state.Time;
        Loot = state.Loot;
        _entities.Clear();
        foreach (EntitySnapshot entity in state.Entities) _entities.Add(entity.Id, entity);
        if (!_reportedMonsters && _entities.Values.Any(e => e.Kind == EntityKind.Monster))
        {
            EntitySnapshot[] monsters = _entities.Values.Where(e => e.Kind == EntityKind.Monster).OrderBy(e => e.Id).ToArray();
            GD.Print($"World monsters received: {monsters.Length}. IDs: {string.Join(",", monsters.Select(e => e.Id))}");
            _reportedMonsters = true;
        }
        _smoke?.ObserveState(state);
        if (!_reportedTwoPlayers && _entities.Values.Count(e => e.Kind == EntityKind.Player) >= 2)
        { GD.Print("World state received: 2 players."); _reportedTwoPlayers = true; }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEvent(string json)
    {
        if (_simulation is not null) return;
        CombatEvent entry = JsonSerializer.Deserialize<CombatEvent>(json, Json);
        if (entry.Sequence <= _receivedEvent) return;
        _receivedEvent = entry.Sequence;
        _activity.Enqueue(entry);
        while (_activity.Count > _balance.TelemetryCapacity) _activity.Dequeue();
        _smoke?.Observe(entry);
        _progressionSmoke?.Observe(entry);
    }
}
