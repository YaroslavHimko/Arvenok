using System;
using System.Collections.Generic;
using System.Linq;

namespace Arvenok.Contracts;

public sealed record LearnedNode(string Id, int Rank);
public sealed record SkillTreeState
{
    public IReadOnlyList<LearnedNode> Nodes { get; }
    public SkillTreeState(IReadOnlyList<LearnedNode> nodes) => Nodes = Array.AsReadOnly(nodes.ToArray());
    public bool Has(string id) => Nodes.Any(n => n.Id == id && n.Rank > 0);
    public bool Equals(SkillTreeState? other) => other is not null && Nodes.SequenceEqual(other.Nodes);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var n in Nodes) hash.Add(n); return hash.ToHashCode(); }
}
public sealed record SkillNodeDefinition(string Id, CharacterClass Class, string Branch, string SkillId, string Name,
    bool Passive, AbilitySlot Slot, int RequiredLevel, string PrerequisiteId, int PrerequisiteRank, string? ScrollItemId,
    string WeaponRequirement, string Description, string Icon, AbilityDefinition? Ability);
public sealed record SkillNodeView(SkillNodeDefinition Definition, int Rank, string UnmetRequirement);

/// <summary>Absolute UTC deadlines written by the server; wall time during downtime counts toward cooldown.</summary>
public readonly record struct CharacterTimers(double Basic = 0, double Primary = 0, double Mobility = 0, double Interrupt = 0,
    double Defense = 0, double BranchOne = 0, double BranchTwo = 0, double Recovery = 0, double Combat = 0)
{
    public double EndsAt(AbilitySlot slot) => slot switch { AbilitySlot.Basic => Basic, AbilitySlot.Primary => Primary, AbilitySlot.Mobility => Mobility,
        AbilitySlot.Interrupt => Interrupt, AbilitySlot.Defense => Defense, AbilitySlot.BranchOne => BranchOne, AbilitySlot.BranchTwo => BranchTwo, _ => 0 };
}
