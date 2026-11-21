using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;

namespace Arvenok.Contracts;

// Keep the original numeric wire/save values. Weapon is the logical MainHand, Body is BodyArmor.
public enum EquipmentSlot { Weapon, Body, OffHand }
public enum WeaponType { None, Sword, Bow, Staff, Greatsword, Dagger, Shield }
[Flags] public enum EquipmentClasses { None = 0, Warrior = 1, Mage = 2, Archer = 4 }
public sealed record EquipmentDefinition(EquipmentSlot Slot, CharacterClass Class, int MinimumLevel, int Tier, int FlatDamage, int MaxHp)
{
    public WeaponType WeaponType { get; init; }
    public EquipmentClasses Classes { get; init; }
    public double BasicInterval { get; init; }
    public float RangeTiles { get; init; }
    public bool TwoHanded => WeaponType is WeaponType.Greatsword or WeaponType.Bow or WeaponType.Staff;
    public bool Supports(CharacterClass kind) => Classes == EquipmentClasses.None ? kind == Class : ((int)Classes & (1 << (int)kind)) != 0;
}
public sealed record EquipmentState(ItemStack? Weapon = null, ItemStack? Body = null, ItemStack? OffHand = null)
{
    public ItemStack? At(EquipmentSlot slot) => slot switch { EquipmentSlot.Weapon => Weapon, EquipmentSlot.Body => Body, EquipmentSlot.OffHand => OffHand, _ => null };
    public EquipmentState With(EquipmentSlot slot, ItemStack? item) => slot switch { EquipmentSlot.Weapon => this with { Weapon = item }, EquipmentSlot.Body => this with { Body = item }, EquipmentSlot.OffHand => this with { OffHand = item }, _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
}
public sealed record RecipeIngredient(string ItemId, int Quantity);
public sealed record RecipeDefinition(string RecipeId, string ScrollItemId, string OutputItemId, IReadOnlyList<RecipeIngredient> Ingredients);
public enum EconomyOperation { Equip, Unequip, Craft, Pickup, LearnSkill }
public sealed record EconomyRequest(Guid OperationId, long ExpectedRevision, EconomyOperation Operation, Guid ItemId, EquipmentSlot Slot = EquipmentSlot.Weapon)
{
    // Omit absent fields to preserve existing durable Economy.v1 request fingerprints.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? NodeId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long ProgressionRevision { get; init; }
}
public sealed record EconomyResult(bool Accepted, bool Duplicate, string Message, CharacterRecord Character, Guid CreatedItemId = default);
public sealed record EconomyUpdate(Guid OperationId, bool Accepted, bool Duplicate, string Message, Guid CreatedItemId, InventorySnapshot State);
public sealed record GroundLoot(Guid Id, Guid DeathId, ItemStack Item, Guid OwnerId, Vector2 Position, double SpawnedAt, double PublicAt, double ExpiresAt);
