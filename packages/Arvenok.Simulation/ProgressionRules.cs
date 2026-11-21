using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>All leveling and rank decisions are made by the world owner, using versioned authored tuning.</summary>
public sealed class ProgressionRules
{
    public const int LevelCap = 10;
    public const int RankCap = 4;
    public static IReadOnlyList<AbilitySlot> SkillSlots { get; } = Array.AsReadOnly(new[] { AbilitySlot.Primary, AbilitySlot.Mobility, AbilitySlot.Interrupt, AbilitySlot.Defense });
    private readonly int[] _experience;
    private readonly int[] _rankLevels;
    private readonly Dictionary<(CharacterClass, AbilitySlot), (string Id, AbilityDefinition[] Ranks)> _skills = new();
    private readonly Dictionary<string, (CharacterClass Class, AbilitySlot Slot)> _ids = new(StringComparer.Ordinal);
    private readonly CombatBalance _balance;
    public SkillTreeCatalog Tree { get; }
    public ProgressionRules(CombatBalance? balance = null, string? json = null)
    {
        _balance = balance ?? new CombatBalance();
        if (json is null)
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Arvenok.Progression") ?? throw new InvalidDataException("Missing progression data.");
            using var reader = new StreamReader(stream); json = reader.ReadToEnd();
        }
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unknown progression schema.");
        _experience = root.GetProperty("experienceToNextLevel").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        _rankLevels = root.GetProperty("rankRequiredLevels").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        if (_experience.Length != LevelCap - 1 || _experience.Any(e => e <= 0) || !_rankLevels.SequenceEqual(new[] { 1, 2, 4, 7 }))
            throw new InvalidDataException("Invalid progression thresholds.");
        foreach (JsonElement skill in root.GetProperty("skills").EnumerateArray())
        {
            CharacterClass kind = Enum.Parse<CharacterClass>(skill.GetProperty("class").GetString()!);
            AbilitySlot slot = Enum.Parse<AbilitySlot>(skill.GetProperty("slot").GetString()!);
            if (!Enum.IsDefined(kind) || !SkillSlots.Contains(slot)) throw new InvalidDataException("Unknown class skill.");
            string id = skill.GetProperty("id").GetString()!;
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64) throw new InvalidDataException("Invalid skill ID.");
            AbilityDefinition baseline = _balance.Class(kind).Ability(slot);
            var ranks = new AbilityDefinition[RankCap];
            foreach (string field in new[] { "damage", "rangeTiles", "cooldownSeconds", "effectSeconds" })
                if (skill.TryGetProperty(field, out JsonElement values) && values.GetArrayLength() != RankCap) throw new InvalidDataException("Every skill needs four ranks.");
            for (int index = 0; index < RankCap; index++)
            {
                double Value(string name, double fallback) => skill.TryGetProperty(name, out JsonElement values) ? values[index].GetDouble() : fallback;
                double damage = Value("damage", baseline.Damage), range = Value("rangeTiles", baseline.Range / CombatBalance.PixelsPerTile);
                double cooldown = Value("cooldownSeconds", baseline.CooldownSeconds), duration = Value("effectSeconds", baseline.EffectSeconds);
                if (!double.IsFinite(damage + range + cooldown + duration) || damage < 0 || damage > 100000 || damage != Math.Floor(damage)
                    || range < 0 || range > 32 || cooldown <= 0 || duration < 0 || duration > 30) throw new InvalidDataException("Invalid skill rank values.");
                ranks[index] = baseline with { Damage = (int)damage, Range = (float)range * CombatBalance.PixelsPerTile, CooldownSeconds = cooldown, EffectSeconds = duration };
            }
            _skills.Add((kind, slot), (id, ranks)); _ids.Add(id, (kind, slot));
        }
        if (_skills.Count != 12) throw new InvalidDataException("Expected the twelve existing class skills.");
        Tree = new SkillTreeCatalog(this);
    }

    public int ExperienceRequired(int level) => level == LevelCap ? 0 : level is >= 1 and < LevelCap ? _experience[level - 1] : throw new ArgumentOutOfRangeException(nameof(level));
    public int RankRequiredLevel(int rank) => rank is >= 1 and <= RankCap ? _rankLevels[rank - 1] : throw new ArgumentOutOfRangeException(nameof(rank));
    public AbilityDefinition Ability(CharacterClass kind, AbilitySlot slot, SkillRanks ranks) => slot == AbilitySlot.Basic
        ? _balance.Class(kind).Ability(slot) : _skills[(kind, slot)].Ranks[ranks.Rank(slot) - 1];
    public string SkillId(CharacterClass kind, AbilitySlot slot) => _skills[(kind, slot)].Id;

    public void Validate(CharacterRecord record)
    {
        if (record.SchemaVersion is not (1 or 2 or 3 or 4) || record.Id == Guid.Empty || !Enum.IsDefined(record.Class) || record.Level is < 1 or > LevelCap
            || record.Experience < 0 || (record.Level == LevelCap ? record.Experience != 0 : record.Experience >= ExperienceRequired(record.Level))
            || record.SkillPoints < 0 || record.SkillPoints > 9 || record.Revision < 1 || record.Revision == long.MaxValue)
            throw new InvalidDataException("Invalid saved character progression.");
        foreach (AbilitySlot slot in SkillSlots)
        {
            int rank = record.Ranks.Rank(slot);
            if (rank is < 1 or > RankCap || record.Level < RankRequiredLevel(rank)) throw new InvalidDataException("Invalid saved skill rank.");
        }
        Tree.Validate(record);
        if (record.Ranks.Spent + record.Tree.Nodes.Count + record.SkillPoints != record.Level - 1) throw new InvalidDataException("Saved skill points do not match earned levels.");
        if (!record.ClassLocked && (record.Level != 1 || record.Experience != 0)) throw new InvalidDataException("Progressed characters must retain their class.");
    }

    public (CharacterRecord Character, int Awarded, int Levels) Award(CharacterRecord character, long experience)
    {
        Validate(character);
        if (experience < 0) throw new ArgumentOutOfRangeException(nameof(experience));
        if (experience == 0 || character.Level == LevelCap) return (character, 0, 0);
        int remaining = Enumerable.Range(character.Level, LevelCap - character.Level).Sum(ExperienceRequired) - character.Experience;
        int awarded = (int)Math.Min(experience, remaining), level = character.Level, progress = character.Experience + awarded;
        while (level < LevelCap && progress >= ExperienceRequired(level)) { progress -= ExperienceRequired(level); level++; }
        int gained = level - character.Level;
        return (character with { Level = level, Experience = level == LevelCap ? 0 : progress, SkillPoints = character.SkillPoints + gained,
            ClassLocked = true, Revision = character.Revision + 1 }, awarded, gained);
    }

    public bool TryUpgrade(CharacterRecord character, string skillId, long expectedRevision, out CharacterRecord upgraded, out string reason)
    {
        upgraded = character; reason = "";
        var skill = default((CharacterClass Class, AbilitySlot Slot));
        if (string.IsNullOrEmpty(skillId) || !_ids.TryGetValue(skillId, out skill) || skill.Class != character.Class) reason = "That skill does not belong to your class.";
        else if (expectedRevision != character.Revision) reason = "Progress changed; review the current ranks and try again.";
        else if (character.Ranks.Rank(skill.Slot) >= RankCap) reason = "Skill is already rank 4.";
        else if (character.Level < RankRequiredLevel(character.Ranks.Rank(skill.Slot) + 1)) reason = "Character level is too low for that rank.";
        else if (character.SkillPoints <= 0) reason = "No unspent skill points.";
        if (reason.Length > 0) return false;
        upgraded = character with { Ranks = character.Ranks.WithRank(skill.Slot, character.Ranks.Rank(skill.Slot) + 1),
            SkillPoints = character.SkillPoints - 1, ClassLocked = true, Revision = character.Revision + 1 };
        return true;
    }

    public CharacterRecord ResetSkills(CharacterRecord character)
    {
        Validate(character);
        if (character.Tree.Nodes.Count != 0) throw new InvalidOperationException("Core reset is unavailable after learning branch nodes; their prerequisites must be preserved.");
        return character with { Ranks = SkillRanks.Initial, SkillPoints = character.Level - 1, Revision = character.Revision + 1 };
    }

    public ProgressionSnapshot Snapshot(CharacterRecord character) => new(character.Id, character.Revision, character.Class, character.Level,
        character.Experience, ExperienceRequired(character.Level), character.SkillPoints, character.Ranks,
        Array.AsReadOnly(SkillSlots.Select(slot =>
        {
            int rank = character.Ranks.Rank(slot); var skill = _skills[(character.Class, slot)];
            return new SkillProgressionView(skill.Id, slot, rank, rank == RankCap ? 0 : RankRequiredLevel(rank + 1),
                skill.Ranks[rank - 1], rank == RankCap ? null : skill.Ranks[rank]);
        }).ToArray()))
        {
            Tree = Array.AsReadOnly(Tree.Nodes.Where(n => n.Class == character.Class).Select(n => new SkillNodeView(n, character.Tree.Has(n.Id) ? 1 : 0, Tree.Requirement(character, n))).ToArray()),
            BranchSkills = Array.AsReadOnly(Tree.Nodes.Where(n => n.Class == character.Class && !n.Passive && character.Tree.Has(n.Id))
                .Select(n => new SkillProgressionView(n.SkillId, n.Slot, 1, 0, n.Ability!, null)).ToArray())
        };
}
