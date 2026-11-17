using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// One fixed scene and one authority boundary: ONLY the dedicated server simulates.
// Drawn primitives intentionally replace artwork for this first network/PvP test.
public partial class Arena : Node2D
{
    private const int Port = 7777;
    private const float MoveSpeed = 185f;
    private const float MeleeRange = 90f;
    private const float SpellRange = 240f;
    private const int MaxHp = 100;
    private static readonly Rect2 PlayArea = new(80, 110, 840, 420);

    private sealed class Fighter
    {
        public long Id;
        public Vector2 Position;
        public Vector2 Destination;
        public int Hp = MaxHp;
        public bool Casting;
        public long CastTargetId;
        public double CastEndsAt;
        public double NextActionAt;
        public double RespawnAt;
    }

    private readonly Dictionary<long, Fighter> _fighters = new();
    private bool _dedicatedServer;
    private double _snapshotTimer;
    private long _selectedTargetId = -1;
    private string _status = "Connecting to localhost...";

    public override void _Ready()
    {
        Multiplayer.PeerConnected += PeerJoined;
        Multiplayer.PeerDisconnected += PeerLeft;
        Multiplayer.ConnectedToServer += () =>
            _status = $"Connected as player {Multiplayer.GetUniqueId()}";
        Multiplayer.ConnectionFailed += () => _status = "Connection failed. Start server first.";
        Multiplayer.ServerDisconnected += () =>
        {
            _fighters.Clear();
            _status = "Server disconnected.";
        };

        _dedicatedServer = OS.GetCmdlineUserArgs().Contains("--server");
        var peer = new ENetMultiplayerPeer();
        if (_dedicatedServer)
        {
            Error result = peer.CreateServer(Port, maxClients: 2);
            if (result != Error.Ok)
            {
                GD.PushError($"Could not listen on UDP {Port}: {result}");
                return;
            }
            Multiplayer.MultiplayerPeer = peer;
            GD.Print($"Arvenok dedicated server listening on UDP {Port}");
        }
        else
        {
            Error result = peer.CreateClient("127.0.0.1", Port);
            if (result != Error.Ok)
            {
                _status = $"Could not create client: {result}";
                return;
            }
            Multiplayer.MultiplayerPeer = peer;
        }
    }

    private void PeerJoined(long id)
    {
        if (!_dedicatedServer)
            return;

        Vector2 start = _fighters.Count == 0 ? new Vector2(245, 320) : new Vector2(755, 320);
        _fighters[id] = new Fighter { Id = id, Position = start, Destination = start };
        GD.Print($"Player {id} joined. Total: {_fighters.Count}");
    }

    private void PeerLeft(long id)
    {
        _fighters.Remove(id);
        if (_dedicatedServer)
        {
            Rpc(MethodName.RemoveFighter, id);
            GD.Print($"Player {id} left.");
        }
        if (_selectedTargetId == id)
            _selectedTargetId = -1;
    }

    public override void _Process(double delta)
    {
        if (_dedicatedServer)
        {
            SimulateServer(delta);
            _snapshotTimer += delta;
            if (_snapshotTimer >= 0.10) // 10 snapshots/sec; simple enough for 2 local clients
            {
                _snapshotTimer = 0;
                foreach (Fighter fighter in _fighters.Values)
                    Rpc(MethodName.ReceiveFighter, fighter.Id, fighter.Position,
                        fighter.Hp, fighter.Casting);
            }
        }
        else
        {
            QueueRedraw();
        }
    }

    private void SimulateServer(double delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        foreach (Fighter fighter in _fighters.Values)
        {
            if (fighter.Hp <= 0)
            {
                if (now >= fighter.RespawnAt)
                {
                    fighter.Hp = MaxHp;
                    fighter.Casting = false;
                    fighter.Destination = fighter.Position;
                }
                continue;
            }

            if (fighter.Casting)
            {
                if (now >= fighter.CastEndsAt)
                {
                    fighter.Casting = false;
                    if (_fighters.TryGetValue(fighter.CastTargetId, out Fighter? target)
                        && target.Hp > 0
                        && fighter.Position.DistanceTo(target.Position) <= SpellRange)
                    {
                        DealDamage(target, 30, now);
                    }
                }
                continue; // casters cannot move during the cast
            }

            fighter.Position = fighter.Position.MoveToward(fighter.Destination,
                MoveSpeed * (float)delta);
        }
    }

    private static Vector2 ClampToArena(Vector2 p) => new(
        Mathf.Clamp(p.X, PlayArea.Position.X + 20, PlayArea.End.X - 20),
        Mathf.Clamp(p.Y, PlayArea.Position.Y + 20, PlayArea.End.Y - 20));

    private static bool ValidPoint(Vector2 p) =>
        float.IsFinite(p.X) && float.IsFinite(p.Y);

    // Clients send INTENT. Sender identity comes from transport, never an RPC argument.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestMove(Vector2 destination)
    {
        if (!Multiplayer.IsServer() || !ValidPoint(destination)) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (!_fighters.TryGetValue(sender, out Fighter? fighter) || fighter.Hp <= 0) return;

        fighter.Destination = ClampToArena(destination);
        fighter.Casting = false; // moving cancels your own spell
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestAttack(long targetId, bool useSpell)
    {
        if (!Multiplayer.IsServer()) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (targetId == sender) return;
        if (!_fighters.TryGetValue(sender, out Fighter? attacker)
            || !_fighters.TryGetValue(targetId, out Fighter? target)) return;
        if (attacker.Hp <= 0 || target.Hp <= 0 || attacker.Casting) return;

        double now = Time.GetTicksMsec() / 1000.0;
        if (now < attacker.NextActionAt) return;
        float range = useSpell ? SpellRange : MeleeRange;
        if (attacker.Position.DistanceTo(target.Position) > range) return;

        attacker.Destination = attacker.Position; // stop while attacking
        if (useSpell)
        {
            attacker.Casting = true;
            attacker.CastTargetId = targetId;
            attacker.CastEndsAt = now + 1.25;
            attacker.NextActionAt = now + 1.60;
        }
        else
        {
            attacker.NextActionAt = now + 0.80;
            DealDamage(target, 15, now);
        }
    }

    private static void DealDamage(Fighter target, int damage, double now)
    {
        target.Casting = false; // any successful hit interrupts a cast
        target.Hp = Math.Max(0, target.Hp - damage);
        if (target.Hp == 0)
            target.RespawnAt = now + 3.0;
    }

    // Only the server (authority = peer 1) may publish state to clients.
    [Rpc(MultiplayerApi.RpcMode.Authority,
        TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReceiveFighter(long id, Vector2 position, int hp, bool casting)
    {
        if (_dedicatedServer) return;
        if (!_fighters.TryGetValue(id, out Fighter? fighter))
        {
            fighter = new Fighter { Id = id };
            _fighters[id] = fighter;
        }
        fighter.Position = position;
        fighter.Hp = hp;
        fighter.Casting = casting;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RemoveFighter(long id)
    {
        if (_dedicatedServer) return;
        _fighters.Remove(id);
        if (_selectedTargetId == id) _selectedTargetId = -1;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_dedicatedServer || Multiplayer.MultiplayerPeer == null ||
            Multiplayer.MultiplayerPeer.GetConnectionStatus() !=
                MultiplayerPeer.ConnectionStatus.Connected)
            return;

        if (@event is InputEventMouseButton click && click.Pressed
            && click.ButtonIndex == MouseButton.Left)
        {
            Vector2 point = GetGlobalMousePosition();
            foreach (Fighter fighter in _fighters.Values)
            {
                if (fighter.Id != Multiplayer.GetUniqueId()
                    && point.DistanceTo(fighter.Position) <= 28f)
                {
                    _selectedTargetId = fighter.Id;
                    return;
                }
            }
            if (PlayArea.HasPoint(point))
                RpcId(1, MethodName.RequestMove, point);
        }
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (_selectedTargetId < 0) return;
            if (key.Keycode == Key.Key1)
                RpcId(1, MethodName.RequestAttack, _selectedTargetId, false);
            else if (key.Keycode == Key.Key2)
                RpcId(1, MethodName.RequestAttack, _selectedTargetId, true);
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 1000, 650), new Color(0.055f, 0.078f, 0.103f));
        DrawRect(PlayArea, new Color(0.12f, 0.17f, 0.18f));
        DrawRect(PlayArea, new Color(0.30f, 0.45f, 0.44f), false, 3f);

        LabelAt(new Vector2(25, 37), "ARVENOK  /  PVP PROTOTYPE", 23, Colors.White);
        LabelAt(new Vector2(25, 70), _status, 17, new Color(0.70f, 0.83f, 0.86f));
        LabelAt(new Vector2(80, 560),
            "CLICK ground: move   |   CLICK opponent: target   |   1: strike (90 range)   |   2: bolt (240 range)",
            16, Colors.White);
        LabelAt(new Vector2(80, 588),
            "Strike = 15 dmg / 0.8s   |   Bolt = 30 dmg / 1.25s cast   |   Taking damage interrupts casting",
            14, new Color(0.74f, 0.83f, 0.80f));
        LabelAt(new Vector2(80, 614),
            "Move during casting to cancel it. Players respawn 3s after defeat. Server decides all combat.",
            14, new Color(0.74f, 0.83f, 0.80f));

        foreach (Fighter fighter in _fighters.Values)
        {
            bool mine = fighter.Id == Multiplayer.GetUniqueId();
            bool targeted = fighter.Id == _selectedTargetId;
            Color color = fighter.Hp == 0
                ? new Color(0.45f, 0.45f, 0.45f)
                : mine ? new Color(0.34f, 0.75f, 0.94f)
                       : new Color(0.96f, 0.47f, 0.35f);

            if (targeted)
                DrawArc(fighter.Position, 29f, 0, Mathf.Tau, 60,
                    new Color(0.98f, 0.88f, 0.31f), 3f);
            DrawCircle(fighter.Position, 20f, color);
            if (fighter.Casting)
                DrawArc(fighter.Position, 25f, 0, Mathf.Tau * 0.75f, 32,
                    Colors.Yellow, 3f);

            Vector2 bar = fighter.Position + new Vector2(-28, -39);
            DrawRect(new Rect2(bar, new Vector2(56, 7)), new Color(0.25f, 0.2f, 0.2f));
            DrawRect(new Rect2(bar, new Vector2(56f * fighter.Hp / MaxHp, 7)),
                new Color(0.34f, 0.86f, 0.48f));
            LabelAt(fighter.Position + new Vector2(-33, 44),
                mine ? $"YOU [{fighter.Id}]" : $"ENEMY [{fighter.Id}]",
                14, Colors.White);
        }
    }

    private void LabelAt(Vector2 pos, string value, int size, Color color) =>
        DrawString(ThemeDB.FallbackFont, pos, value,
            HorizontalAlignment.Left, -1, size, color);
}
