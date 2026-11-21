using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>Shared read-only derivation for authoritative simulation and owner tooltips.</summary>
public static class EquipmentCombat
{
    public static string Unavailable(CharacterClass kind, AbilitySlot slot, EquipmentDefinition? weapon) =>
        kind == CharacterClass.Archer && weapon?.WeaponType == WeaponType.Dagger && slot is AbilitySlot.Primary or AbilitySlot.Interrupt ? "Requires a bow; dagger is equipped." : "";
    public static AbilityDefinition Adapt(AbilityDefinition skill, CharacterClass kind, EquipmentDefinition? weapon, bool shield)
    {
        if (skill.Slot == AbilitySlot.Basic && weapon is not null)
            skill = skill with { CooldownSeconds = weapon.BasicInterval > 0 ? weapon.BasicInterval : skill.CooldownSeconds,
                Range = weapon.RangeTiles > 0 ? weapon.RangeTiles * CombatBalance.PixelsPerTile : skill.Range };
        if (kind == CharacterClass.Warrior && skill.Slot == AbilitySlot.Interrupt && !shield)
            skill = skill with { Name = weapon?.WeaponType == WeaponType.Dagger ? "Knife Break" : "Hilt Strike" };
        if (kind == CharacterClass.Warrior && skill.Slot == AbilitySlot.Defense && weapon?.WeaponType == WeaponType.Greatsword)
            skill = skill with { Name = "Parry Stance" };
        return skill;
    }
}
