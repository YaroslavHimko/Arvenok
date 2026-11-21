using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed class ItemCatalog
{
    private readonly Dictionary<string, ItemDefinition> _items;
    public IReadOnlyList<ItemDefinition> Items { get; }
    public int DefaultCapacity { get; }
    public int MaximumCapacity { get; }
    public ItemCatalog(string? json = null)
    {
        bool extension = json is null;
        if (json is null)
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Arvenok.Items") ?? throw new InvalidDataException("Item catalog is missing.");
            using var reader = new StreamReader(stream); json = reader.ReadToEnd();
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json); JsonElement root = document.RootElement;
            DefaultCapacity = root.GetProperty("defaultCapacity").GetInt32(); MaximumCapacity = root.GetProperty("maximumCapacity").GetInt32();
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || MaximumCapacity is < 1 or > 48 || DefaultCapacity < 1 || DefaultCapacity > MaximumCapacity)
                throw new InvalidDataException("Invalid inventory capacity configuration.");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true }; options.Converters.Add(new JsonStringEnumConverter());
            ItemDefinition[] items = root.GetProperty("items").Deserialize<ItemDefinition[]>(options) ?? throw new InvalidDataException("Empty catalog.");
            if (extension) items = SkillsEquipmentContent.Extend(items);
            _items = new(StringComparer.Ordinal);
            foreach (ItemDefinition item in items)
            {
                if (item is null || item.ItemId is null || !Regex.IsMatch(item.ItemId, "^[a-z][a-z0-9_.]{1,79}$") || string.IsNullOrWhiteSpace(item.DisplayName)
                    || item.DisplayName.Length > 80 || string.IsNullOrWhiteSpace(item.Description) || item.Description.Length > 500 || !Enum.IsDefined(item.Category)
                    || !Regex.IsMatch(item.Icon ?? "", "^[a-z][a-z0-9_-]{0,39}$") || item.MaximumStack is < 1 or > 999
                    || item.Category is ItemCategory.Equipment or ItemCategory.Recipe or ItemCategory.SpellScroll && item.MaximumStack != 1 || !_items.TryAdd(item.ItemId, item))
                    throw new InvalidDataException("Invalid or duplicate item definition.");
                if (item.IconFormat is not ("svg" or "png") || item.Rarity is not ("common" or "uncommon")
                    || item.RecipeId is not null && (item.Category != ItemCategory.Recipe || !Regex.IsMatch(item.RecipeId, "^craft\\.[a-z_]+$")))
                    throw new InvalidDataException("Invalid item artwork, rarity or recipe reference.");
                if (item.Category == ItemCategory.SpellScroll ? item.RecipeId is not null || string.IsNullOrEmpty(item.SkillNodeId) || !Regex.IsMatch(item.SkillNodeId, "^[a-z][a-z0-9_.]{1,99}$") : item.SkillNodeId is not null)
                    throw new InvalidDataException("Crafting recipes and spell scrolls must remain distinct.");
                if (item.Equipment is EquipmentDefinition gear)
                {
                    if (item.Category != ItemCategory.Equipment || !Enum.IsDefined(gear.Slot) || !Enum.IsDefined(gear.Class) || !Enum.IsDefined(gear.WeaponType)
                        || gear.Tier is not (1 or 2) || gear.MinimumLevel is < 1 or > 10 || gear.FlatDamage is < 0 or > 10 || gear.MaxHp is < 0 or > 100
                        || ((int)gear.Classes & ~7) != 0 || !double.IsFinite(gear.BasicInterval) || gear.BasicInterval < 0 || gear.BasicInterval > 10
                        || !float.IsFinite(gear.RangeTiles) || gear.RangeTiles < 0 || gear.RangeTiles > 10
                        || gear.Slot == EquipmentSlot.OffHand && (gear.WeaponType != WeaponType.Shield || gear.Class != CharacterClass.Warrior || gear.Classes is not (EquipmentClasses.None or EquipmentClasses.Warrior) || gear.FlatDamage != 0 || gear.MaxHp != 0)
                        || gear.WeaponType == WeaponType.Shield && gear.Slot != EquipmentSlot.OffHand)
                        throw new InvalidDataException("Invalid equipment definition.");
                    if (gear.WeaponType == WeaponType.None)
                    {
                        if (gear.MinimumLevel != (gear.Tier == 1 ? 1 : 5) || gear.FlatDamage != (gear.Slot == EquipmentSlot.Weapon ? gear.Tier : 0) || gear.MaxHp != (gear.Slot == EquipmentSlot.Body ? gear.Tier * 5 : 0)) throw new InvalidDataException("Invalid legacy equipment tuning.");
                        if (gear.Slot == EquipmentSlot.Weapon) _items[item.ItemId] = item with { Equipment = gear with { WeaponType = gear.Class switch { CharacterClass.Mage => WeaponType.Staff, CharacterClass.Archer => WeaponType.Bow, _ => WeaponType.Sword } } };
                    }
                }
            }
            if (_items.Count == 0) throw new InvalidDataException("Empty item catalog.");
            Items = Array.AsReadOnly(_items.Values.OrderBy(i => i.ItemId, StringComparer.Ordinal).ToArray());
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("Malformed item catalog.", error); }
    }
    public bool TryGet(string id, out ItemDefinition definition) => _items.TryGetValue(id, out definition!);
    public ItemDefinition Get(string id) => !string.IsNullOrEmpty(id) && _items.TryGetValue(id, out var item) ? item : throw new InvalidDataException("Unknown saved item definition: " + id);
}
