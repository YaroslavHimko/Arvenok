using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed class SkillTreeCatalog
{
    private readonly Dictionary<string, SkillNodeDefinition> _nodes = new(StringComparer.Ordinal);
    private readonly ProgressionRules _progression;
    public IReadOnlyList<SkillNodeDefinition> Nodes { get; }
    public SkillTreeCatalog(ProgressionRules progression)
    {
        _progression = progression;
        using var skills = SkillsEquipmentContent.Read("Skills"); using var tree = SkillsEquipmentContent.Read("Tree");
        if (!skills.RootElement.GetProperty("explicitly_add_not_replace_core").GetBoolean() || skills.RootElement.GetProperty("global_cooldown_s").GetDouble() != 0.25
            || tree.RootElement.GetProperty("branches_are_exclusive").GetBoolean() || tree.RootElement.GetProperty("current_level_cap").GetInt32() != ProgressionRules.LevelCap) throw new InvalidDataException("Unsupported skill tree rules.");
        foreach (JsonProperty core in skills.RootElement.GetProperty("old_core_skills").EnumerateObject())
        {
            CharacterClass kind = Enum.Parse<CharacterClass>(core.Name, true); int index = 0;
            foreach (var id in core.Value.EnumerateArray())
                if (progression.SkillId(kind, (AbilitySlot)(++index)) != core.Name + "." + id.GetString()!.Replace('_', '-')) throw new InvalidDataException("Core skill mapping changed.");
        }
        var definitions = skills.RootElement.GetProperty("skills").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!);
        var slots = new Dictionary<CharacterClass, int>();
        foreach (JsonElement entry in tree.RootElement.GetProperty("nodes").EnumerateArray())
        {
            CharacterClass kind = Enum.Parse<CharacterClass>(entry.GetProperty("class").GetString()!, true);
            string id = entry.GetProperty("node_id").GetString()!, suffix = entry.GetProperty("skill_id").GetString()!;
            JsonElement data = definitions[suffix]; bool passive = data.GetProperty("type").GetString() == "passive";
            if (data.GetProperty("class").GetString() != kind.ToString().ToLowerInvariant() || entry.GetProperty("max_rank").GetInt32() != 1 || entry.GetProperty("point_cost").GetInt32() != 1) throw new InvalidDataException("Invalid branch node.");
            string name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(suffix.Replace('_', ' '));
            AbilitySlot slot = passive ? AbilitySlot.Basic : (AbilitySlot)(5 + slots.GetValueOrDefault(kind));
            if (!passive) slots[kind] = slots.GetValueOrDefault(kind) + 1;
            AbilityDefinition? ability = null;
            double Number(string key, double fallback = 0) => data.TryGetProperty(key, out var value) ? value.GetDouble() : fallback;
            if (!passive)
            {
                AbilityEffect effect = suffix is "perfect_block" or "arcane_shell" ? AbilityEffect.Defense : suffix == "evasive_cut" ? AbilityEffect.Sidestep : AbilityEffect.Damage;
                ability = new(slot, name, effect, kind == CharacterClass.Mage ? DamageType.Magic : DamageType.Physical,
                    (int)Number("damage"), (float)Number("range_tiles") * CombatBalance.PixelsPerTile, Number("cast_s"), Number("cooldown_s"), effect == AbilityEffect.Damage, Number("duration_s"))
                    { AbsorbAmount = (int)Number("absorb_damage"), SidestepDistance = (float)Number("side_step_tiles") * CombatBalance.PixelsPerTile };
                if (slot > AbilitySlot.BranchTwo || ability.Damage is < 0 or > 100 || !double.IsFinite(ability.CastSeconds + ability.CooldownSeconds + ability.EffectSeconds + ability.Range) || ability.CastSeconds < 0 || ability.CastSeconds > 5 || ability.CooldownSeconds <= 0 || ability.CooldownSeconds > 60 || ability.Range < 0 || ability.Range > 320) throw new InvalidDataException("Invalid branch ability.");
            }
            var prerequisite = entry.GetProperty("prerequisite");
            string prior = prerequisite.TryGetProperty("node", out var node) ? node.GetString()! : kind.ToString().ToLowerInvariant() + "." + prerequisite.GetProperty("skill").GetString()!.Replace('_', '-');
            var definition = new SkillNodeDefinition(id, kind, entry.GetProperty("branch").GetString()!, kind.ToString().ToLowerInvariant() + "." + suffix, name,
                passive, slot, entry.GetProperty("level_requirement").GetInt32(), prior, prerequisite.GetProperty("rank").GetInt32(), entry.GetProperty("requires_item").GetString(),
                data.TryGetProperty("requires", out var weapon) ? weapon.GetString()! : "", data.GetProperty("effect").GetString()!, entry.GetProperty("icon").GetString()![7..], ability);
            if (!_nodes.TryAdd(id, definition) || definition.RequiredLevel is < 1 or > 10 || definition.Passive == (definition.ScrollItemId is not null)) throw new InvalidDataException("Invalid tree ownership requirements.");
        }
        if (_nodes.Count != 12 || slots.Values.Any(count => count != 2) || slots.Count != 3) throw new InvalidDataException("Expected two branches per class.");
        foreach (var node in _nodes.Values)
            if (!_nodes.ContainsKey(node.PrerequisiteId) && !ProgressionRules.SkillSlots.Any(s => progression.SkillId(node.Class, s) == node.PrerequisiteId)) throw new InvalidDataException("Unknown prerequisite.");
        Nodes = Array.AsReadOnly(_nodes.Values.ToArray());
    }
    public SkillNodeDefinition? Find(string id) => _nodes.GetValueOrDefault(id);
    public SkillNodeDefinition? Active(CharacterClass kind, AbilitySlot slot) => Nodes.FirstOrDefault(n => n.Class == kind && !n.Passive && n.Slot == slot);
    public bool Learned(CharacterRecord? character, string suffix) => character is not null && Nodes.Any(n => n.Class == character.Class && n.SkillId.EndsWith("." + suffix, StringComparison.Ordinal) && character.Tree.Has(n.Id));
    public int PrerequisiteRank(CharacterRecord character, SkillNodeDefinition node) => _nodes.ContainsKey(node.PrerequisiteId)
        ? character.Tree.Nodes.FirstOrDefault(n => n.Id == node.PrerequisiteId)?.Rank ?? 0
        : character.Ranks.Rank(ProgressionRules.SkillSlots.Single(s => _progression.SkillId(character.Class, s) == node.PrerequisiteId));
    public string Requirement(CharacterRecord character, SkillNodeDefinition node, bool checkScroll = true)
    {
        if (node.Class != character.Class) return "This skill belongs to another class.";
        if (character.Tree.Has(node.Id)) return "Already learned.";
        if (character.Level < node.RequiredLevel) return $"Requires level {node.RequiredLevel}.";
        if (PrerequisiteRank(character, node) < node.PrerequisiteRank) return $"Requires {node.PrerequisiteId} rank {node.PrerequisiteRank}.";
        if (character.SkillPoints <= 0) return "No unspent skill points.";
        if (checkScroll && node.ScrollItemId is string scroll && character.Inventory?.Slots.Any(s => s.Stack.ItemId == scroll) != true) return "Matching spell scroll required.";
        return "";
    }
    public void Validate(CharacterRecord character)
    {
        if (character.Tree is null || character.Tree.Nodes.Count > 4 || character.Tree.Nodes.Select(n => n.Id).Distinct().Count() != character.Tree.Nodes.Count) throw new InvalidDataException("Invalid learned nodes.");
        foreach (var learned in character.Tree.Nodes)
        {
            var node = Find(learned.Id);
            if (node is null || node.Class != character.Class || learned.Rank != 1 || character.Level < node.RequiredLevel || PrerequisiteRank(character, node) < node.PrerequisiteRank) throw new InvalidDataException("Invalid learned skill prerequisite.");
        }
        if (character.SchemaVersion < 4 && (character.Tree.Nodes.Count != 0 || character.Timers != default)) throw new InvalidDataException("Legacy record contains new progression state.");
        foreach (double value in Enum.GetValues<AbilitySlot>().Select(character.Timers.EndsAt).Append(character.Timers.Recovery).Append(character.Timers.Combat))
            if (!double.IsFinite(value) || value < 0 || value > 253402300799) throw new InvalidDataException("Invalid persisted server deadline.");
    }
}
