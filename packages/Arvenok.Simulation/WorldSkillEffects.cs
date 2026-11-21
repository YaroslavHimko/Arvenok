using System;
using System.Linq;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed partial class WorldSimulation
{
    private readonly double _utcOrigin;
    private string SkillRequirement(CombatEntity actor, AbilitySlot slot)
    {
        if (slot < AbilitySlot.BranchOne) return EquipmentCombat.Unavailable(actor.Class, slot, Inventory.Weapon(actor.Character));
        SkillNodeDefinition? node = Progression.Tree.Active(actor.Class, slot);
        if (node is null || actor.Character?.Tree.Has(node.Id) != true) return "Skill is not learned.";
        return WeaponRequirement(node.WeaponRequirement, Inventory.Weapon(actor.Character)?.WeaponType ?? WeaponType.None, actor.Character.Equipment?.OffHand is not null);
    }
    public static string WeaponRequirement(string requirement, WeaponType type, bool shield)
    {
        bool valid = requirement switch { "" => true, "shield" => shield, "two_handed_sword" => type == WeaponType.Greatsword,
            "staff" => type == WeaponType.Staff, "bow" => type == WeaponType.Bow, "dagger" => type == WeaponType.Dagger, _ => false };
        return valid ? "" : "Requires " + requirement.Replace('_', ' ') + ".";
    }
    private void ActivateDefense(CombatEntity actor, AbilityDefinition skill)
    {
        actor.Defenses.Clear(); actor.DefenseEndsAt = Time + skill.EffectSeconds; actor.DefenseName = skill.Name;
        actor.DamageReduction = actor.PhysicalReduction = skill.Reduction;
        if (skill.Slot >= AbilitySlot.BranchOne)
        {
            if (skill.AbsorbAmount > 0) actor.Defenses.Shell(actor.DefenseEndsAt, skill.AbsorbAmount);
            else actor.Defenses.PerfectBlock(Time + Effects.PerfectBlockSeconds);
        }
        else
        {
            if (actor.Class == CharacterClass.Archer) actor.Defenses.Dodge(Time + Effects.DodgeSeconds);
            if (actor.Class == CharacterClass.Warrior && Inventory.Weapon(actor.Character)?.WeaponType == WeaponType.Greatsword) actor.Defenses.Parry(Time + Effects.ParrySeconds);
            if (actor.Class == CharacterClass.Warrior && actor.Character?.Equipment?.OffHand is not null)
            {
                actor.PhysicalReduction = Progression.Tree.Learned(actor.Character, "shield_mastery") ? Effects.MasteryPhysicalReduction : Effects.ShieldPhysicalReduction;
                actor.DamageReduction = Effects.ShieldMagicReduction;
            }
        }
        Emit(CombatEventKind.DefenseStarted, actor, skill: skill, detail: skill.Name);
    }
    private void SetSkillCooldown(CombatEntity actor, AbilityDefinition skill)
    {
        double ends = Time + skill.CooldownSeconds; actor.Cooldowns[(int)skill.Slot] = ends;
        if (skill.Effect != AbilityEffect.Defense || actor.Class == CharacterClass.Archer) return;
        AbilitySlot branch = actor.Class == CharacterClass.Warrior ? AbilitySlot.BranchOne : AbilitySlot.BranchTwo;
        actor.Cooldowns[(int)AbilitySlot.Defense] = actor.Cooldowns[(int)branch] = ends;
    }
    private void RestoreTimers(CombatEntity actor)
    {
        CharacterTimers timers = actor.Character!.Timers; double now = _utcOrigin + Time;
        foreach (AbilitySlot slot in Enum.GetValues<AbilitySlot>()) actor.Cooldowns[(int)slot] = Time + Math.Max(0, timers.EndsAt(slot) - now);
        actor.RecoveryEndsAt = Time + Math.Max(0, timers.Recovery - now);
        if (timers.Combat > now) { actor.LastCombatAt = Time + timers.Combat - now - Economy.CombatLockSeconds; actor.InCombat = true; }
    }
    private void PersistTimers(CombatEntity actor)
    {
        if (actor.Character is not CharacterRecord character) return;
        double Deadline(double end) => end > Time ? _utcOrigin + end : 0;
        var timers = new CharacterTimers(Deadline(actor.Cooldowns[0]), Deadline(actor.Cooldowns[1]), Deadline(actor.Cooldowns[2]), Deadline(actor.Cooldowns[3]),
            Deadline(actor.Cooldowns[4]), Deadline(actor.Cooldowns[5]), Deadline(actor.Cooldowns[6]),
            Deadline(Math.Max(actor.RecoveryEndsAt, actor.Casting ? actor.CastEndsAt + Balance.RecoverySeconds : 0)), Deadline(actor.LastCombatAt + Economy.CombatLockSeconds));
        if (character.Timers == timers) return;
        actor.Character = character with { Timers = timers, Revision = checked(character.Revision + 1) }; QueueProgression(actor);
    }
    private int PassiveDamage(CombatEntity actor, CombatEntity target, AbilityDefinition skill)
    {
        if (skill.Slot != AbilitySlot.Primary) return 0;
        bool enabled = Progression.Tree.Learned(actor.Character, "embers") || Progression.Tree.Learned(actor.Character, "keen_eye") && Inventory.Weapon(actor.Character)?.WeaponType == WeaponType.Bow;
        return enabled ? target.Kind == EntityKind.Monster ? 2 : target.Kind == EntityKind.Player ? 1 : 0 : 0;
    }
}
