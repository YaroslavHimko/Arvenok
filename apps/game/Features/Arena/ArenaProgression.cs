using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Arvenok.Contracts;
using Arvenok.Simulation;
using Arvenok.Hosting;
using Godot;

namespace Arvenok.Game.Features.Arena;

public partial class ArenaSession
{
    private ICharacterRepository? _characters;
    private Task _persistenceTail = Task.CompletedTask;
    private readonly Dictionary<long, (Guid Nonce, double Started)> _connections = new();
    private readonly HashSet<long> _identifying = new();
    private long _pendingUpgrade;
    private bool _storageFailed;
    public ProgressionSnapshot? Progress { get; private set; }
    public bool UpgradePending => _pendingUpgrade != 0;
    public string ProgressionMessage { get; private set; } = "";
    public double ProgressionMessageAt { get; private set; }
    public int LastExperienceAward { get; private set; }
    public int LastLevelsGained { get; private set; }
    public long ProgressionNotice { get; private set; }

    private static string RepositoryDirectory() => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".."));
    private bool InitializeCharacterStore(SessionOptions options)
    {
        try
        {
            string path = options.DataDirectory.Length > 0 ? Path.GetFullPath(options.DataDirectory) : Path.Combine(RepositoryDirectory(), ".local", "server", options.Shard);
            _characters = new AccountRepository(path, _simulation!.Characters.Validate, _simulation.Characters.Upgrade);
            if (options.GrantInventory != Guid.Empty)
            {
#if !DEBUG
                throw new InvalidOperationException("Development inventory grants are disabled in release builds.");
#else
                if (!OS.IsDebugBuild()) throw new InvalidOperationException("Development inventory grants require a Debug build.");
                CharacterRecord character = _characters.Load(options.GrantInventory);
                InventoryResult grant = Arvenok.Simulation.DevelopmentInventory.Prepare(_simulation.Inventory, character.Inventory!);
                if (!grant.Accepted) throw new InvalidOperationException(grant.Message);
                if (!grant.Duplicate) _characters.Save(character with { Inventory = grant.State, Revision = character.Revision + 1 });
                GD.Print($"Development inventory kit character={character.Id} duplicate={grant.Duplicate}");
                GetTree().Quit(); return false;
#endif
            }
            if (options.ResetSkills != Guid.Empty)
            {
                CharacterRecord character = _characters.Load(options.ResetSkills);
                _characters.Save(_simulation.Progression.ResetSkills(character));
                GD.Print($"Development skill reset saved for character {character.Id}; refunded {character.Level - 1} points.");
                GetTree().Quit(); return false;
            }
            GD.Print("Development character storage: " + path);
            return true;
        }
        catch (Exception error) { GD.PushError("Character storage unavailable: " + error.Message); GetTree().Quit(2); return false; }
    }
    private void QueueCharacterSave(CharacterRecord character)
    {
        if (_characters is null) return;
        Task previous = _persistenceTail;
        _persistenceTail = Task.Run(async () => { await previous.ConfigureAwait(false); _characters.Save(character); });
    }
    private void PublishProgression()
    {
        if (_simulation is null) return;
        foreach (CharacterRecord character in _simulation.DrainCharacterSaves()) QueueCharacterSave(character);
        foreach (ProgressionUpdate update in _simulation.DrainProgressionUpdates())
            foreach (int peer in Multiplayer.GetPeers())
                if (_simulation.EntityForPeer(peer) == update.EntityId) RpcId(peer, MethodName.ReceiveProgression, JsonSerializer.Serialize(update, Json));
    }
    private void CheckCharacterStorage()
    {
        if (_persistenceTail.IsFaulted && !_storageFailed)
        { _storageFailed = true; GD.PushError("Progression save failed; stopping development server: " + _persistenceTail.Exception!.GetBaseException().Message); GetTree().Quit(1); }
        foreach (var connection in _connections.ToArray())
            if ((!_accountSessions.ContainsKey(connection.Key) && _simulation!.Time - connection.Value.Started > 120)
                || _accountSessions.TryGetValue(connection.Key, out var session) && session.Expires <= DateTimeOffset.UtcNow) _peer?.DisconnectPeer((int)connection.Key);
    }
    private void CloseCharacterStore()
    {
        _connections.Clear();
        try
        {
            Task.WhenAll(_passwordTasks.ToArray()).GetAwaiter().GetResult();
            if (_characters is not null && _simulation is not null)
            {
                foreach (EntitySnapshot entity in _simulation.GetSnapshots()) if (_simulation.CharacterForExit(entity.Id) is CharacterRecord record) QueueCharacterSave(record);
                _persistenceTail.GetAwaiter().GetResult();
            }
        }
        catch (Exception error) { GD.PushError("Final character save failed: " + error.Message); }
        finally { _characters?.Dispose(); }
    }

    public EquipmentDefinition? CurrentWeapon => Backpack?.Equipment.Weapon is ItemStack item ? Backpack.Catalog.FirstOrDefault(d => d.ItemId == item.ItemId)?.Equipment : null;
    public string WeaponRequirement(CharacterClass kind, AbilitySlot slot)
    {
        if (slot < AbilitySlot.BranchOne) return EquipmentCombat.Unavailable(kind, slot, CurrentWeapon);
        SkillNodeView? node = Progress?.Tree.FirstOrDefault(n => n.Definition.Slot == slot && !n.Definition.Passive);
        return node is null || node.Rank == 0 ? "Skill is not learned." : WorldSimulation.WeaponRequirement(node.Definition.WeaponRequirement, CurrentWeapon?.WeaponType ?? WeaponType.None, Backpack?.Equipment.OffHand is not null);
    }
    public AbilityDefinition EffectiveAbility(CharacterClass kind, AbilitySlot slot)
    {
        AbilityDefinition skill = slot >= AbilitySlot.BranchOne ? Progress!.Tree.Single(n => !n.Definition.Passive && n.Definition.Slot == slot).Definition.Ability!
            : Progress?.Class == kind && slot != AbilitySlot.Basic ? Progress.Skills.Single(s => s.Slot == slot).Current : Definition(kind).Ability(slot);
        skill = EquipmentCombat.Adapt(skill, kind, CurrentWeapon, Backpack?.Equipment.OffHand is not null);
        bool Learned(string suffix) => Progress?.Tree.Any(n => n.Rank > 0 && n.Definition.SkillId.EndsWith("." + suffix, StringComparison.Ordinal)) == true;
        if (slot == AbilitySlot.Defense && Learned("ward_weaving")) skill = skill with { EffectSeconds = skill.EffectSeconds + 0.2 };
        if (slot == AbilitySlot.Mobility && Learned("light_footwork") && CurrentWeapon?.WeaponType == WeaponType.Dagger) skill = skill with { CooldownSeconds = skill.CooldownSeconds - 1 };
        return skill;
    }
    public string InspectProgress() => JsonSerializer.Serialize(Progress, Json);
    public void RequestTreeLearn(string nodeId, Guid scroll = default)
    {
        if (Progress is null || Backpack is null || InventoryPending || UpgradePending) return;
        SkillNodeView? node = Progress.Tree.FirstOrDefault(n => n.Definition.Id == nodeId); if (node is null) return;
        if (scroll == Guid.Empty && node.Definition.ScrollItemId is string item) scroll = Backpack.Slots.FirstOrDefault(s => s.Stack.ItemId == item)?.Stack.Id ?? Guid.Empty;
        SubmitEconomyIntent(new(Guid.NewGuid(), Backpack.Revision, EconomyOperation.LearnSkill, scroll) { NodeId = nodeId, ProgressionRevision = Progress.Revision });
    }

    public void RequestSkillUpgrade(string skillId)
    {
        if (_simulation is not null || !HasConnection || LocalEntityId == 0 || Progress is null || UpgradePending) return;
        _pendingUpgrade = ++_commandSequence;
        RpcId(1, MethodName.UpgradeCharacterSkill, _pendingUpgrade, ServerTime, skillId, Progress.Revision);
    }

    internal void DuplicateUpgradeForSmoke()
    {
        if (Progress is null) return;
        long revision = Progress.Revision;
        RequestSkillUpgrade(Progress.Skills[0].Id);
        RpcId(1, MethodName.UpgradeCharacterSkill, _pendingUpgrade, ServerTime, Progress.Skills[0].Id, revision);
        RpcId(1, MethodName.UpgradeCharacterSkill, ++_commandSequence, ServerTime, Progress.Skills[1].Id, revision);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void UpgradeCharacterSkill(long sequence, double observedTime, string skillId, long revision)
    {
        if (_simulation is null || !Multiplayer.IsServer()) return;
        long peer = Multiplayer.GetRemoteSenderId(), entity = _simulation.EntityForPeer(peer);
        if (entity == 0) return;
        bool accepted = skillId.Length <= 64 && _simulation.Submit(entity, new WorldCommand(sequence, observedTime, CommandKind.UpgradeSkill,
            SkillId: skillId, ProgressionRevision: revision));
        string message = accepted ? "Skill upgraded." : _simulation.RecentActivity.LastOrDefault(e => e.ActorId == entity && e.Kind == CombatEventKind.Rejected).Detail ?? "Upgrade rejected.";
        PublishEvents();
        var response = new ProgressionUpdate(entity, sequence, accepted, message, 0, 0, _simulation.PlayerProgression(entity)!);
        RpcId(peer, MethodName.ReceiveProgression, JsonSerializer.Serialize(response, Json));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveProgression(string json)
    {
        if (_simulation is not null) return;
        ProgressionUpdate? update = JsonSerializer.Deserialize<ProgressionUpdate>(json, Json);
        if (update is null || update.EntityId != LocalEntityId) return;
        if (update.RequestSequence != 0 && update.RequestSequence == _pendingUpgrade) _pendingUpgrade = 0;
        if (Progress is not null && update.State.Revision < Progress.Revision) return;
        Progress = update.State;
        _progressionSmoke?.ObserveProgress(update);
        if (update.AwardedExperience > 0 || update.LevelsGained > 0 || update.Message.Length > 0)
        {
            LastExperienceAward = update.AwardedExperience; LastLevelsGained = update.LevelsGained;
            ProgressionMessage = update.LevelsGained > 0 ? $"+{update.AwardedExperience} EXP · Level {update.State.Level}! {update.LevelsGained} skill point{(update.LevelsGained == 1 ? "" : "s")} gained. Press K to upgrade."
                : update.AwardedExperience > 0 ? $"+{update.AwardedExperience} EXP" : update.Message;
            ProgressionMessageAt = ServerTime; ProgressionNotice++;
        }
    }
}
