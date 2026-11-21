using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Arvenok.Contracts;
using Arvenok.Hosting;
using Arvenok.Game.Features.Arena.Presentation;
using Godot;

namespace Arvenok.Game.Features.Arena;

public partial class ArenaSession
{
    private sealed record LoginSession(Guid Account, DateTimeOffset Expires);
    private readonly Dictionary<long, LoginSession> _accountSessions = new();
    private readonly Dictionary<Guid, long> _activeAccounts = new();
    private readonly Dictionary<string, (DateTimeOffset Until, int Count)> _authLimits = new();
    private readonly Dictionary<long, HashSet<Guid>> _lifecycleCommands = new();
    private int _passwordJobs;
    private readonly HashSet<Task> _passwordTasks = new();
    private readonly Dictionary<long, (double Started, int Count)> _accountRequests = new();
    private double _accountDeadline;
    private Guid _accountCommand;
    public AccountReply? AccountState { get; private set; }
    public bool AccountBusy { get; private set; }
    public string InspectAccount() => JsonSerializer.Serialize(AccountState);
    public void SubmitAccount(string json)
    {
        if (_simulation is not null || AccountBusy) return;
        if (!HasConnection) { Status = "Server unavailable. Reconnect first."; return; }
        var request = JsonSerializer.Deserialize<AccountRequest>(json);
        if (request is null) return;
        AccountBusy = true; _accountCommand = request.CommandId;
        _accountDeadline = Time.GetTicksMsec() / 1000.0 + 15;
        Status = "Waiting for server...";
        RpcId(1, MethodName.AccountIntent, json);
    }
    public void AccountAction(AccountOperation action, Guid character = default, string nickname = "", CharacterClass kind = CharacterClass.Warrior,
        string username = "", string password = "") =>
        SubmitAccount(JsonSerializer.Serialize(new AccountRequest(Guid.NewGuid(), action, username, password, nickname, kind, character)));
    private void CheckAccountTimeout()
    {
        if (AccountBusy && Time.GetTicksMsec() / 1000.0 >= _accountDeadline)
        {
            _peer?.Close(); ClearWorldPresentation(); AccountState = null; AccountBusy = false;
            Status = "Request timed out. Reconnect and log in to check the result.";
        }
    }
    private bool AllowAuthentication(string key)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (string expired in _authLimits.Where(p => p.Value.Until <= now).Select(p => p.Key).ToArray()) _authLimits.Remove(expired);
        if (!_authLimits.TryGetValue(key, out var window)) window = (now.AddMinutes(1), 0);
        if (window.Count >= 8 || _authLimits.Count > 4096) return false;
        _authLimits[key] = (window.Until, window.Count + 1); return true;
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void AccountIntent(string json)
    {
        if (_simulation is null || _characters is not AccountRepository repository || string.IsNullOrEmpty(json) || json.Length > 2048) return;
        long peer = Multiplayer.GetRemoteSenderId();
        if (!_connections.TryGetValue(peer, out var connection) || !_identifying.Add(peer)) return;
        AccountRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<AccountRequest>(json);
            if (request is null || request.CommandId == Guid.Empty || !Enum.IsDefined(request.Operation)) throw new ArgumentException("Invalid account request.");
            var limit = _accountRequests.GetValueOrDefault(peer);
            if (_simulation.Time - limit.Started >= 60) limit = (_simulation.Time, 0);
            _accountRequests[peer] = (limit.Started, limit.Count + 1);
            if (limit.Count >= 90) throw new ArgumentException("Too many requests. Wait one minute.");
            bool authenticating = request.Operation is AccountOperation.Login or AccountOperation.Register;
            if (authenticating)
            {
                if (_accountSessions.ContainsKey(peer)) throw new ArgumentException("Log out before signing in to another account.");
                if (request.Username.Length > 32 || request.Password.Length > 128) throw new ArgumentException("Invalid username or password.");
                string address = SecurePeer.GetPeerAddress((int)peer);
                if (!AllowAuthentication("ip:" + address) || !AllowAuthentication("user:" + request.Username.Trim().ToUpperInvariant()) || _passwordJobs >= 4)
                    throw new ArgumentException("Too many attempts. Wait one minute and try again.");
                _passwordJobs++;
                Guid? account;
                var job = Task.Run(() => request.Operation == AccountOperation.Register ? repository.Register(request.Username, request.Password) : repository.Login(request.Username, request.Password));
                _passwordTasks.Add(job);
                try { account = await job; }
                finally { _passwordJobs--; _passwordTasks.Remove(job); }
                if (!_connections.TryGetValue(peer, out var current) || current.Nonce != connection.Nonce) return;
                if (account is null) throw new ArgumentException("Invalid username or password.");
                _accountSessions[peer] = new(account.Value, DateTimeOffset.UtcNow.AddHours(4));
                _lifecycleCommands[peer] = new();
                GD.Print("Account authentication accepted.");
            }
            else
            {
                if (!_accountSessions.TryGetValue(peer, out var session) || session.Expires <= DateTimeOffset.UtcNow)
                    throw new ArgumentException("Please log in again.");
                if (_lifecycleCommands[peer].Count >= 256 && request.Operation != AccountOperation.Logout)
                    throw new ArgumentException("Please log out and log in again.");
                while (true) { var tail = _persistenceTail; await tail; if (tail == _persistenceTail) break; }
                if (!_connections.TryGetValue(peer, out var current) || current.Nonce != connection.Nonce) return;
                if (request.Operation is AccountOperation.Enter or AccountOperation.Return or AccountOperation.Logout
                    && _lifecycleCommands[peer].Contains(request.CommandId))
                { ReplyAccount(peer, request.CommandId, true, "Request already completed."); return; }
                switch (request.Operation)
                {
                    case AccountOperation.Create:
                        if (_simulation.EntityForPeer(peer) != 0) throw new ArgumentException("Return to character selection first.");
                        repository.Create(session.Account, request.CommandId, request.Nickname, request.Class); break;
                    case AccountOperation.Delete:
                        if (_simulation.GetSnapshots().Any(e => _simulation.Character(e.Id)?.Id == request.CharacterId))
                            throw new ArgumentException("Return this character to selection before deleting it.");
                        repository.Delete(session.Account, request.CommandId, request.CharacterId, request.Nickname); break;
                    case AccountOperation.Enter:
                        if (_activeAccounts.ContainsKey(session.Account)) throw new ArgumentException("This account already has a character in the world.");
                        CharacterRecord saved = repository.Owned(session.Account, request.CharacterId);
                        long entity = _simulation.Join(peer, character: saved);
                        if (entity == 0) throw new ArgumentException("World entry is unavailable.");
                        _activeAccounts.Add(session.Account, peer);
                        GD.Print($"Player {entity} joined. Total: {_simulation.PlayerCount}. World time: {_simulation.Time:F2}s");
                        RpcId(peer, MethodName.Welcome, entity, "");
                        SendInventory(peer); PublishEvents(); break;
                    case AccountOperation.Return: SaveAndLeave(peer); break;
                    case AccountOperation.Logout:
                        SaveAndLeave(peer); _accountSessions.Remove(peer); break;
                    case AccountOperation.List: break;
                    default: throw new ArgumentException("Invalid operation.");
                }
                if (request.Operation is AccountOperation.Enter or AccountOperation.Return or AccountOperation.Logout)
                    _lifecycleCommands[peer].Add(request.CommandId);
            }
            ReplyAccount(peer, request.CommandId, true, request.Operation == AccountOperation.Delete ? "Character permanently deleted." : "");
        }
        catch (Exception error) when (error is ArgumentException or JsonException)
        { if (_connections.ContainsKey(peer)) ReplyAccount(peer, request?.CommandId ?? Guid.Empty, false, error is JsonException ? "Invalid request." : error.Message); }
        catch (Exception)
        { GD.PushError("Account operation failed in storage."); if (_connections.ContainsKey(peer)) ReplyAccount(peer, request?.CommandId ?? Guid.Empty, false, "Storage unavailable. Reconnect to check the result."); }
        finally { _identifying.Remove(peer); }
    }
    private void ReplyAccount(long peer, Guid command, bool accepted, string message)
    {
        bool loggedIn = _accountSessions.TryGetValue(peer, out var session);
        var cards = loggedIn ? ((AccountRepository)_characters!).List(session!.Account) : Array.Empty<CharacterCard>();
        RpcId(peer, MethodName.ReceiveAccount, JsonSerializer.Serialize(new AccountReply(command, accepted, message, loggedIn, cards, _simulation!.EntityForPeer(peer) != 0)));
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveAccount(string json)
    {
        if (_simulation is not null) return;
        var reply = JsonSerializer.Deserialize<AccountReply>(json);
        if (reply is null || reply.CommandId != _accountCommand) return;
        AccountBusy = false; AccountState = reply; Status = reply.Message;
        if (!reply.InWorld) ClearWorldPresentation();
    }
    private void SaveAndLeave(long peer)
    {
        if (_simulation is null) return;
        long entity = _simulation.EntityForPeer(peer);
        if (entity != 0)
        {
            // Flush earlier writes before the final authoritative position and before releasing the account reservation.
            PublishProgression();
            _persistenceTail.GetAwaiter().GetResult();
            if (_simulation.CharacterForExit(entity) is CharacterRecord record) _characters!.Save(record);
            _simulation.Leave(peer); PublishEvents();
            // Pending async replies/intents from the previous world binding must expire before a character switch.
            if (_connections.TryGetValue(peer, out var connection)) _connections[peer] = (Guid.NewGuid(), connection.Started);
        }
        if (_accountSessions.TryGetValue(peer, out var session) && _activeAccounts.GetValueOrDefault(session.Account) == peer) _activeAccounts.Remove(session.Account);
    }
    private void ClearWorldPresentation()
    {
        Node? view = GetNodeOrNull("View");
        if (view is not null) { RemoveChild(view); view.QueueFree(); }
        _entities.Clear(); _activity.Clear(); LocalEntityId = 0; _receivedRevision = 0; _receivedEvent = 0; _commandSequence = 0;
        Progress = null; Backpack = null; Loot = Array.Empty<GroundLoot>();
        _pendingUpgrade = 0; _pendingInventory = null; _pendingEconomy = null;
    }
}
