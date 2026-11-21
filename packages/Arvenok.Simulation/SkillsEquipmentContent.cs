using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

internal static class SkillsEquipmentContent
{
    internal static JsonDocument Read(string resource = "Equipment")
    {
        using Stream stream = typeof(SkillsEquipmentContent).Assembly.GetManifestResourceStream("Arvenok.Skills." + resource) ?? throw new InvalidDataException("Missing skill extension content.");
        return JsonDocument.Parse(stream);
    }
    internal static string ItemId(string proposed) => proposed == "equipment.shield.training_shield" ? "equipment.training_shield" : proposed;
    internal static ItemDefinition[] Extend(ItemDefinition[] original)
    {
        using var document = Read(); var items = original.ToList();
        foreach (JsonElement entry in document.RootElement.GetProperty("items").EnumerateArray())
        {
            string id = ItemId(entry.GetProperty("id").GetString()!);
            WeaponType type = entry.GetProperty("weapon_type").GetString() switch { "two_handed_sword" => WeaponType.Greatsword, "dagger" => WeaponType.Dagger, "shield" => WeaponType.Shield, _ => throw new InvalidDataException("Unknown extension weapon.") };
            var classes = entry.GetProperty("class").EnumerateArray().Select(c => Enum.Parse<CharacterClass>(c.GetString()!, true)).ToArray();
            string rarity = entry.GetProperty("rarity").GetString()!;
            var gear = new EquipmentDefinition(type == WeaponType.Shield ? EquipmentSlot.OffHand : EquipmentSlot.Weapon, classes[0], entry.GetProperty("level_required").GetInt32(), rarity == "uncommon" ? 2 : 1, entry.GetProperty("bonus_direct_damage").GetInt32(), 0)
            {
                WeaponType = type, Classes = (EquipmentClasses)classes.Aggregate(0, (mask, c) => mask | (1 << (int)c)),
                BasicInterval = entry.TryGetProperty("basic_attack_interval_s", out var interval) ? interval.GetDouble() : 0,
                RangeTiles = entry.TryGetProperty("range_tiles", out var range) ? range.GetSingle() : 0
            };
            string icon = "v02_" + Path.GetFileNameWithoutExtension(entry.GetProperty("icon").GetString()!);
            var item = new ItemDefinition(id, entry.GetProperty("name").GetString()!, type == WeaponType.Shield ? "A wooden shield for timed Guard and Perfect Block." : $"{type} weapon. Requires level {gear.MinimumLevel}.", ItemCategory.Equipment, icon, 1, true, true)
                { Equipment = gear, Rarity = rarity, IconFormat = "png" };
            int index = items.FindIndex(i => i.ItemId == id);
            if (index >= 0)
            {
                if (id != "equipment.training_shield" || items[index].Equipment is not null) throw new InvalidDataException("Extension would overwrite existing equipment.");
                items[index] = item; // Promote the existing placeholder without changing owned instance identities.
            }
            else items.Add(item);
        }
        foreach (JsonElement recipe in document.RootElement.GetProperty("crafting_recipe_additions").EnumerateArray())
        {
            string output = recipe.GetProperty("output_item_id").GetString()!;
            items.Add(new(recipe.GetProperty("scroll_item_id").GetString()!, "Recipe: " + items.Single(i => i.ItemId == output).DisplayName,
                "Consumes this crafting recipe and its materials to create one weapon.", ItemCategory.Recipe, "v02_spell_scroll", 1, true, true)
                { IconFormat = "png", RecipeId = recipe.GetProperty("recipe_id").GetString()! });
        }
        using var scrolls = Read("Scrolls"); using var tree = Read("Tree");
        foreach (JsonElement scroll in scrolls.RootElement.GetProperty("scrolls").EnumerateArray())
        {
            string id = scroll.GetProperty("id").GetString()!;
            JsonElement node = tree.RootElement.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("requires_item").GetString() == id);
            string name = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(scroll.GetProperty("skill_id").GetString()!.Replace('_', ' '));
            if (scroll.GetProperty("item_type").GetString() != "spell_scroll" || scroll.GetProperty("max_stack").GetInt32() != 1 || scroll.GetProperty("class").GetString() != node.GetProperty("class").GetString()) throw new InvalidDataException("Invalid spell scroll definition.");
            items.Add(new(id, "Spell: " + name, "Teaches " + name + ". Consumes this spell scroll and one skill point only when learning succeeds.", ItemCategory.SpellScroll, "v02_spell_scroll", 1, true, true)
                { IconFormat = "png", SkillNodeId = node.GetProperty("node_id").GetString()! });
        }
        return items.ToArray();
    }
}
