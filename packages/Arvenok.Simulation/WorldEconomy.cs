using System;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed partial class WorldSimulation
{
    public EconomyCatalog Economy { get; }
    /// <summary>Single writer. The host's durable save completes before either ownership location changes.</summary>
    public EconomyResult? SubmitEconomy(long peerId, EconomyRequest request, Action<CharacterRecord>? persist = null)
    {
        if (!_peers.TryGetValue(peerId, out long id) || !_entities.TryGetValue(id, out CombatEntity? actor)) return null;
        CharacterRecord before = actor.Character!;
        actor.InventoryTokens = Math.Min(12, actor.InventoryTokens + (Time - actor.LastInventoryTokenTime) * 6); actor.LastInventoryTokenTime = Time;
        if (actor.InventoryTokens < 1) return new(false, false, "Too many inventory requests.", before);
        actor.InventoryTokens--;
        EconomyResult? check = Inventory.CheckEconomy(before, request); if (check is not null) return check;
        if (actor.Hp <= 0) return new(false, false, "Cannot use items while dead.", before);
        bool equipment = request.Operation is EconomyOperation.Equip or EconomyOperation.Unequip;
        if (!Enum.IsDefined(request.Operation) || !Enum.IsDefined(request.Slot)) return new(false, false, "Invalid item operation.", before);
        if ((equipment || request.Operation == EconomyOperation.LearnSkill) && (Time - actor.LastCombatAt < Economy.CombatLockSeconds || actor.Casting || actor.AttackTargetId != 0 || actor.Dashing || Time < actor.RecoveryEndsAt))
            return new(false, false, request.Operation == EconomyOperation.LearnSkill ? "Learning is locked in combat. Finish your action and wait 5 seconds after damage." : "Equipment is locked in combat; stop attacking and wait 5 seconds after damage.", before);
        if (request.Operation == EconomyOperation.Pickup && ValidatePickup(actor, request.ItemId) is string reason) return new(false, false, reason, before);
        EconomyResult result = equipment ? Inventory.PrepareEquipment(before, request) : request.Operation == EconomyOperation.Craft
            ? Inventory.PrepareCraft(before, request, Economy) : request.Operation == EconomyOperation.LearnSkill ? Inventory.PrepareLearning(before, request, Progression.Tree)
            : Inventory.PreparePickup(before, request, _groundLoot[request.ItemId].Item);
        if (!result.Accepted || result.Duplicate) return result;
        Characters.Validate(result.Character);
        persist?.Invoke(result.Character); // Failure leaves the authoritative aggregate unchanged.
        if (request.Operation == EconomyOperation.Pickup) _groundLoot.Remove(request.ItemId);
        actor.Character = result.Character; RecomputeEquipment(actor); QueueProgression(actor);
        return result;
    }
    private void RecomputeEquipment(CombatEntity actor)
    {
        if (actor.Character is null) return;
        actor.MaxHp = Balance.Class(actor.Class).MaxHp + Inventory.EquipmentBonuses(actor.Character).MaxHp;
        actor.Hp = Math.Min(actor.Hp, actor.MaxHp);
        actor.SanguineEdge = Progression.Tree.Learned(actor.Character, "sanguine_edge") && Inventory.Weapon(actor.Character)?.WeaponType == WeaponType.Greatsword;
    }
}
