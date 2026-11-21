using System;
using System.Collections.Generic;

namespace Arvenok.Contracts;

public readonly record struct SkillRanks(int Primary, int Mobility, int Interrupt, int Defense)
{
    public static SkillRanks Initial => new(1, 1, 1, 1);
    public int Spent => Primary + Mobility + Interrupt + Defense - 4;
    public int Rank(AbilitySlot slot) => slot switch
    { AbilitySlot.Primary => Primary, AbilitySlot.Mobility => Mobility, AbilitySlot.Interrupt => Interrupt, AbilitySlot.Defense => Defense, _ => 1 };
    public SkillRanks WithRank(AbilitySlot slot, int rank) => slot switch
    {
        AbilitySlot.Primary => this with { Primary = rank }, AbilitySlot.Mobility => this with { Mobility = rank },
        AbilitySlot.Interrupt => this with { Interrupt = rank }, AbilitySlot.Defense => this with { Defense = rank },
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };
}

/// <summary>Versioned server save record; never accepted from a game client.</summary>
public sealed record CharacterRecord(int SchemaVersion, Guid Id, CharacterClass Class, bool ClassLocked,
    int Level, int Experience, int SkillPoints, SkillRanks Ranks, long Revision)
{
    public Guid OwnerAccountId { get; init; }
    public string Nickname { get; init; } = "";
    public SavedWorldPosition? WorldPosition { get; init; }
    public InventoryState? Inventory { get; init; }
    public EquipmentState? Equipment { get; init; }
    public SkillTreeState Tree { get; init; } = new(Array.Empty<LearnedNode>());
    public CharacterTimers Timers { get; init; }
    public static CharacterRecord New(Guid id, CharacterClass kind = CharacterClass.Warrior, int inventoryCapacity = 24) =>
        new(4, id, kind, false, 1, 0, 0, SkillRanks.Initial, 1) { Inventory = InventoryState.Empty(inventoryCapacity), Equipment = new() };
}

public sealed record SavedWorldPosition(float X, float Y);

public sealed record SkillProgressionView(string Id, AbilitySlot Slot, int Rank, int RequiredLevel,
    AbilityDefinition Current, AbilityDefinition? Next);
public sealed record ProgressionSnapshot(Guid CharacterId, long Revision, CharacterClass Class, int Level,
    int Experience, int RequiredExperience, int SkillPoints, SkillRanks Ranks, IReadOnlyList<SkillProgressionView> Skills)
{
    public IReadOnlyList<SkillNodeView> Tree { get; init; } = Array.Empty<SkillNodeView>();
    public IReadOnlyList<SkillProgressionView> BranchSkills { get; init; } = Array.Empty<SkillProgressionView>();
}
public sealed record ProgressionUpdate(long EntityId, long RequestSequence, bool Accepted, string Message,
    int AwardedExperience, int LevelsGained, ProgressionSnapshot State);
