using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>Validated source-pack rules. Runtime authority never accepts item definitions from clients.</summary>
public sealed partial class EconomyCatalog
{
    private sealed record RollEntry(string Id, double Chance, int Min, int Max);
    private sealed record Pool(double Chance, (string Id, int Weight)[] Entries);
    private sealed record LootTable(RollEntry[] Independent, Pool[] Pools);
    private readonly Dictionary<MonsterSpecies, LootTable> _loot = new();
    public IReadOnlyList<RecipeDefinition> Recipes { get; }
    public double CombatLockSeconds { get; }
    public double ProtectionSeconds { get; }
    public double PublicSeconds { get; }
    public float PickupRange { get; }
    public EconomyCatalog(ItemCatalog? items = null, string? recipesJson = null, string? lootJson = null, string? rulesJson = null)
    {
        items ??= new();
        try
        {
            using JsonDocument rules = JsonDocument.Parse(rulesJson ?? Read("Rules"));
            JsonElement r = rules.RootElement; Version(r);
            CombatLockSeconds = Positive(r, "out_of_combat_seconds");
            ProtectionSeconds = Positive(r, "loot_protection_seconds"); PublicSeconds = Positive(r, "loot_public_seconds_after_protection");
            PickupRange = (float)Positive(r, "loot_pickup_range_tiles") * CombatBalance.PixelsPerTile;
            if (r.GetProperty("allow_equip_in_combat").GetBoolean() || !r.GetProperty("require_class_match").GetBoolean()
                || !r.GetProperty("server_authority").GetBoolean() || !r.GetProperty("no_crafting_stations_for_v1").GetBoolean()
                || !r.GetProperty("no_random_crafting_failures").GetBoolean()
                || !r.GetProperty("equipment_slots").EnumerateArray().Select(e => e.GetString()).SequenceEqual(new[] { "weapon", "body" }))
                throw new InvalidDataException("Unsupported economy rule change.");
            using JsonDocument recipes = JsonDocument.Parse(recipesJson ?? Read("Recipes")); Version(recipes.RootElement);
            var result = new List<RecipeDefinition>(); var ids = new HashSet<string>(); var scrolls = new HashSet<string>();
            foreach (JsonElement entry in recipes.RootElement.GetProperty("recipes").EnumerateArray())
            {
                string id = entry.GetProperty("recipe_id").GetString()!;
                ItemDefinition scroll = items.Get(entry.GetProperty("scroll_item_id").GetString()!);
                ItemDefinition output = items.Get(entry.GetProperty("output_item_id").GetString()!);
                var ingredients = entry.GetProperty("ingredients").EnumerateArray().Select(i =>
                    new RecipeIngredient(i.GetProperty("item_id").GetString()!, i.GetProperty("quantity").GetInt32())).ToArray();
                if (!ids.Add(id) || !scrolls.Add(scroll.ItemId) || scroll.RecipeId != id || scroll.Category != ItemCategory.Recipe
                    || output.Equipment?.Tier != 2 || entry.GetProperty("output_quantity").GetInt32() != 1
                    || entry.GetProperty("station_requirement").ValueKind != JsonValueKind.Null || entry.GetProperty("craft_time_seconds").GetDouble() != 0
                    || entry.GetProperty("success_chance").GetDouble() != 1 || !entry.GetProperty("consumes_scroll_on_success").GetBoolean()
                    || entry.GetProperty("learned_recipe").GetBoolean() || ingredients.Length is < 1 or > 16
                    || ingredients.Select(i => i.ItemId).Distinct().Count() != ingredients.Length
                    || ingredients.Any(i => i.Quantity is < 1 or > 999 || items.Get(i.ItemId).Category is not (ItemCategory.MonsterMaterial or ItemCategory.EquipmentKeyPiece)))
                    throw new InvalidDataException("Invalid direct-use recipe.");
                result.Add(new(id, scroll.ItemId, output.ItemId, Array.AsReadOnly(ingredients)));
            }
            ExtendRecipes(items, result, ids, scrolls);
            if (items.Items.Any(i => i.RecipeId is not null && !ids.Contains(i.RecipeId))) throw new InvalidDataException("Missing recipe.");
            Recipes = result.AsReadOnly();
            using JsonDocument loot = JsonDocument.Parse(lootJson ?? Read("Loot")); Version(loot.RootElement);
            foreach (JsonProperty monster in loot.RootElement.GetProperty("monsters").EnumerateObject())
            {
                if (!Enum.TryParse(monster.Name, true, out MonsterSpecies species) || !Enum.IsDefined(species) || _loot.ContainsKey(species))
                    throw new InvalidDataException("Unknown or duplicate loot monster.");
                var independent = new List<RollEntry>(); var pools = new List<Pool>();
                foreach (string key in new[] { "material_rolls", "keypiece_rolls" })
                    foreach (JsonElement e in monster.Value.GetProperty(key).EnumerateArray())
                    {
                        ItemDefinition item = items.Get(e.GetProperty("id").GetString()!);
                        int min = e.GetProperty("min").GetInt32(), max = e.GetProperty("max").GetInt32();
                        if (min < 1 || max < min || max > item.MaximumStack || item.Category != (key == "material_rolls" ? ItemCategory.MonsterMaterial : ItemCategory.EquipmentKeyPiece))
                            throw new InvalidDataException("Invalid material drop.");
                        independent.Add(new(item.ItemId, Chance(e), min, max));
                    }
                foreach (string key in new[] { "equipment_pool", "recipe_pool", "rare_equipment_pool" })
                {
                    JsonElement group = monster.Value.GetProperty(key); double chance = Chance(group);
                    var entries = group.GetProperty("entries").EnumerateArray().Select(e => (Id: e.GetProperty("id").GetString()!, Weight: e.GetProperty("weight").GetInt32())).ToArray();
                    if (entries.Any(e => e.Weight <= 0 || (key == "recipe_pool" ? items.Get(e.Id).RecipeId is null : items.Get(e.Id).Equipment?.Tier != (key == "rare_equipment_pool" ? 2 : 1)))
                        || entries.Sum(e => (long)e.Weight) > int.MaxValue || chance > 0 && entries.Length == 0 || entries.Select(e => e.Id).Distinct().Count() != entries.Length)
                        throw new InvalidDataException("Invalid weighted loot pool.");
                    pools.Add(new(chance, entries));
                }
                _loot.Add(species, new(independent.ToArray(), pools.ToArray()));
            }
            if (_loot.Count != Enum.GetValues<MonsterSpecies>().Length) throw new InvalidDataException("Missing monster loot table.");
            LoadAdditiveLoot(items);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Malformed economy data.", e); }
    }
    public IReadOnlyList<ItemAmount> Roll(MonsterSpecies species, Random random)
    {
        LootTable table = _loot[species]; var drops = new List<ItemAmount>();
        foreach (RollEntry entry in table.Independent)
            if (random.NextDouble() < entry.Chance) drops.Add(new(entry.Id, random.Next(entry.Min, entry.Max + 1), Array.Empty<ItemProperty>()));
        foreach (Pool pool in table.Pools)
        {
            if (random.NextDouble() >= pool.Chance) continue;
            int choice = random.Next(pool.Entries.Sum(e => e.Weight));
            foreach (var entry in pool.Entries)
            { choice -= entry.Weight; if (choice < 0) { drops.Add(new(entry.Id, 1, Array.Empty<ItemProperty>())); break; } }
        }
        foreach (RollEntry entry in _additive.GetValueOrDefault(species) ?? Array.Empty<RollEntry>())
            if (random.NextDouble() < entry.Chance) drops.Add(new(entry.Id, random.Next(entry.Min, entry.Max + 1), Array.Empty<ItemProperty>()));
        return drops;
    }
    private static string Read(string name)
    { using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Arvenok.Economy." + name) ?? throw new InvalidDataException("Missing economy data."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static void Version(JsonElement root) { if (root.GetProperty("schema_version").GetString() != "1.0") throw new InvalidDataException("Unsupported economy schema."); }
    private static double Chance(JsonElement e)
    { double n = e.GetProperty("chance").GetDouble(); if (!double.IsFinite(n) || n < 0 || n > 1) throw new InvalidDataException("Invalid loot probability."); return n; }
    private static double Positive(JsonElement e, string key)
    { double n = e.GetProperty(key).GetDouble(); if (!double.IsFinite(n) || n <= 0 || n > 3600) throw new InvalidDataException("Invalid economy duration/range."); return n; }
}
