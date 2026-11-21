using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed partial class EconomyCatalog
{
    private readonly Dictionary<MonsterSpecies, RollEntry[]> _additive = new();
    private static void ExtendRecipes(ItemCatalog items, List<RecipeDefinition> recipes, HashSet<string> ids, HashSet<string> scrolls)
    {
        using var document = SkillsEquipmentContent.Read();
        foreach (JsonElement entry in document.RootElement.GetProperty("crafting_recipe_additions").EnumerateArray())
        {
            string id = entry.GetProperty("recipe_id").GetString()!; string scroll = entry.GetProperty("scroll_item_id").GetString()!; string output = entry.GetProperty("output_item_id").GetString()!;
            var ingredients = entry.GetProperty("ingredients").EnumerateArray().Select(i => new RecipeIngredient(i.GetProperty("item_id").GetString()!, i.GetProperty("quantity").GetInt32())).ToArray();
            if (!ids.Add(id) || !scrolls.Add(scroll) || items.Get(scroll).RecipeId != id || items.Get(scroll).Category != ItemCategory.Recipe || items.Get(output).Equipment?.Tier != 2
                || entry.GetProperty("craft_time_seconds").GetDouble() != 0 || entry.GetProperty("success_chance").GetDouble() != 1 || !entry.GetProperty("consumes_scroll_on_success").GetBoolean()
                || ingredients.Length is < 1 or > 16 || ingredients.Select(i => i.ItemId).Distinct().Count() != ingredients.Length
                || ingredients.Any(i => i.Quantity is < 1 or > 999 || items.Get(i.ItemId).Category is not (ItemCategory.MonsterMaterial or ItemCategory.EquipmentKeyPiece or ItemCategory.RawMaterial)))
                throw new InvalidDataException("Invalid extension recipe.");
            recipes.Add(new(id, scroll, output, Array.AsReadOnly(ingredients)));
        }
    }
    private void LoadAdditiveLoot(ItemCatalog items)
    {
        var entries = new Dictionary<MonsterSpecies, List<RollEntry>>();
        void Add(JsonElement monsters)
        {
            foreach (JsonProperty monster in monsters.EnumerateObject())
            {
                if (!Enum.TryParse(monster.Name, true, out MonsterSpecies species) || !_loot.ContainsKey(species)) throw new InvalidDataException("Invalid additive monster.");
                if (!entries.TryGetValue(species, out var list)) entries.Add(species, list = new());
                foreach (JsonElement entry in monster.Value.EnumerateArray())
                {
                    string id = SkillsEquipmentContent.ItemId(entry.GetProperty("item_id").GetString()!); ItemDefinition item = items.Get(id);
                    int min = entry.TryGetProperty("min", out var a) ? a.GetInt32() : 1, max = entry.TryGetProperty("max", out var b) ? b.GetInt32() : 1;
                    if (min < 1 || max < min || max > item.MaximumStack || list.Any(e => e.Id == id)) throw new InvalidDataException("Invalid additive loot.");
                    list.Add(new(id, Chance(entry), min, max));
                }
            }
        }
        using var equipment = SkillsEquipmentContent.Read(); Add(equipment.RootElement.GetProperty("new_weapon_loot_additions"));
        using var scrolls = SkillsEquipmentContent.Read("Scrolls");
        foreach (JsonProperty monster in scrolls.RootElement.GetProperty("world_drop_tables").EnumerateObject())
        {
            if (!Enum.TryParse(monster.Name, true, out MonsterSpecies species) || !_loot.ContainsKey(species)) throw new InvalidDataException("Invalid spell scroll monster.");
            if (!entries.TryGetValue(species, out var list)) entries.Add(species, list = new());
            foreach (JsonProperty drop in monster.Value.EnumerateObject())
            {
                double chance = drop.Value.GetDouble();
                if (items.Get(drop.Name).Category != ItemCategory.SpellScroll || !double.IsFinite(chance) || chance < 0 || chance > 1 || list.Any(e => e.Id == drop.Name)) throw new InvalidDataException("Invalid spell scroll drop.");
                list.Add(new(drop.Name, chance, 1, 1));
            }
        }
        using var overrides = SkillsEquipmentContent.Read("LootOverrides");
        if (overrides.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unknown loot overrides.");
        Add(overrides.RootElement.GetProperty("monsters"));
        foreach (var entry in entries) _additive.Add(entry.Key, entry.Value.ToArray());
    }
}
