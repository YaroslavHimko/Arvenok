using System;
using System.Linq;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed partial class InventoryRules
{
    public EconomyResult PrepareCraft(CharacterRecord character, EconomyRequest request, EconomyCatalog economy)
    {
        EconomyResult? check = CheckEconomy(character, request); if (check is not null) return check;
        if (request.Operation != EconomyOperation.Craft) return EconomyFail(character, "Invalid crafting operation.");
        ItemStack?[] slots = Copy(character.Inventory!);
        int source = Array.FindIndex(slots, s => s?.Id == request.ItemId);
        if (source < 0) return EconomyFail(character, "Scroll is not in your backpack.");
        ItemStack scroll = slots[source]!;
        RecipeDefinition? recipe = economy.Recipes.FirstOrDefault(r => r.ScrollItemId == scroll.ItemId && r.RecipeId == Catalog.Get(scroll.ItemId).RecipeId);
        if (recipe is null) return EconomyFail(character, "This item has no usable recipe.");
        // Remove this exact scroll identity. The remaining ingredients can span multiple stacks.
        slots[source] = null;
        foreach (RecipeIngredient ingredient in recipe.Ingredients)
        {
            int remaining = ingredient.Quantity;
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                ItemStack? item = slots[i]; if (item is null || item.ItemId != ingredient.ItemId) continue;
                int take = Math.Min(item.Quantity, remaining); remaining -= take;
                slots[i] = take == item.Quantity ? null : item with { Quantity = item.Quantity - take };
            }
            if (remaining > 0) return EconomyFail(character, "Missing ingredients: " + Catalog.Get(ingredient.ItemId).DisplayName + ". Nothing consumed.");
        }
        int empty = Array.FindIndex(slots, s => s is null);
        if (empty < 0) return EconomyFail(character, "Inventory Full.");
        var output = new ItemStack(Guid.NewGuid(), recipe.OutputItemId, 1, new[] { new ItemProperty("crafted_recipe", recipe.RecipeId) });
        slots[empty] = output;
        return EconomyFinish(character, slots, character.Equipment!, request, "Crafted " + Catalog.Get(recipe.OutputItemId).DisplayName + ".", output.Id);
    }
}
