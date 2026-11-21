using System;
using System.Linq;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed partial class InventoryRules
{
    public EconomyResult PrepareLearning(CharacterRecord character, EconomyRequest request, SkillTreeCatalog tree)
    {
        EconomyResult? check = CheckEconomy(character, request); if (check is not null) return check;
        if (request.Operation != EconomyOperation.LearnSkill || request.NodeId is null || request.NodeId.Length > 100 || tree.Find(request.NodeId) is not SkillNodeDefinition node) return EconomyFail(character, "Unknown skill node.");
        if (request.ProgressionRevision != character.Revision) return EconomyFail(character, "Progress changed; review current requirements.");
        string reason = tree.Requirement(character, node, checkScroll: false); if (reason.Length > 0) return EconomyFail(character, reason);
        ItemStack?[] slots = Copy(character.Inventory!);
        if (node.ScrollItemId is string scroll)
        {
            int source = Array.FindIndex(slots, s => s?.Id == request.ItemId && s.ItemId == scroll);
            if (source < 0) return EconomyFail(character, "Matching spell scroll is not in your backpack.");
            ItemDefinition definition = Catalog.Get(scroll);
            if (definition.Category != ItemCategory.SpellScroll || definition.SkillNodeId != node.Id || definition.RecipeId is not null) return EconomyFail(character, "This is not a matching spell-learning scroll.");
            slots[source] = null;
        }
        else if (request.ItemId != Guid.Empty) return EconomyFail(character, "Passive learning does not consume an item.");
        CharacterRecord learned = character with { Tree = new SkillTreeState(character.Tree.Nodes.Append(new LearnedNode(node.Id, 1)).OrderBy(n => n.Id, StringComparer.Ordinal).ToArray()), SkillPoints = character.SkillPoints - 1, ClassLocked = true };
        return EconomyFinish(learned, slots, character.Equipment!, request, "Learned " + node.Name + ".");
    }
}
