using System.Collections.Generic;
using System.Linq;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

public sealed partial class WorldSimulation
{
    public ProgressionRules Progression { get; }
    private readonly List<(CombatEntity Monster, long Life)> _monsterDeaths = new();
    private readonly List<ProgressionUpdate> _progressionUpdates = new();
    private readonly HashSet<long> _dirtyCharacters = new();

    public CharacterRecord? Character(long entityId) => _entities.GetValueOrDefault(entityId)?.Character;
    public CharacterRecord? CharacterForExit(long entityId)
    {
        if (!_entities.TryGetValue(entityId, out var entity) || entity.Character is not CharacterRecord record) return null;
        var position = entity.Hp <= 0 ? Spawn(entity.Id) : entity.Position;
        entity.Character = record with { WorldPosition = new(position.X, position.Y), Revision = record.Revision + 1 };
        return entity.Character;
    }
    public ProgressionSnapshot? PlayerProgression(long entityId) => Character(entityId) is CharacterRecord record ? Progression.Snapshot(record) : null;
    private AbilityDefinition Ability(CombatEntity entity, AbilitySlot slot)
    {
        AbilityDefinition skill = slot >= AbilitySlot.BranchOne ? Progression.Tree.Active(entity.Class, slot)!.Ability!
            : Progression.Ability(entity.Class, slot, entity.Character?.Ranks ?? SkillRanks.Initial);
        skill = EquipmentCombat.Adapt(skill, entity.Class, Inventory.Weapon(entity.Character), entity.Character?.Equipment?.OffHand is not null);
        if (slot == AbilitySlot.Defense && Progression.Tree.Learned(entity.Character, "ward_weaving")) skill = skill with { EffectSeconds = skill.EffectSeconds + 0.2 };
        if (slot == AbilitySlot.Mobility && Progression.Tree.Learned(entity.Character, "light_footwork") && Inventory.Weapon(entity.Character)?.WeaponType == WeaponType.Dagger) skill = skill with { CooldownSeconds = skill.CooldownSeconds - 1 };
        return skill;
    }

    private void LockClass(CombatEntity entity)
    {
        entity.CanChooseClass = false;
        if (entity.Character is not CharacterRecord character || character.ClassLocked) return;
        entity.Character = character with { ClassLocked = true, Revision = character.Revision + 1 };
        QueueProgression(entity);
    }

    private bool UpgradeSkill(CombatEntity actor, WorldCommand command)
    {
        if (!Progression.TryUpgrade(actor.Character!, command.SkillId, command.ProgressionRevision, out CharacterRecord upgraded, out string reason))
            return Reject(actor, reason);
        actor.Character = upgraded;
        QueueProgression(actor, message: "Skill upgraded.");
        return true;
    }

    private void QueueProgression(CombatEntity actor, int awarded = 0, int levels = 0, string message = "")
    {
        _dirtyCharacters.Add(actor.Id);
        _progressionUpdates.Add(new ProgressionUpdate(actor.Id, 0, true, message, awarded, levels, Progression.Snapshot(actor.Character!)));
        if (_progressionUpdates.Count > Balance.TelemetryCapacity) _progressionUpdates.RemoveAt(0);
    }
    public ProgressionUpdate[] DrainProgressionUpdates()
    { ProgressionUpdate[] updates = _progressionUpdates.ToArray(); _progressionUpdates.Clear(); return updates; }
    public CharacterRecord[] DrainCharacterSaves()
    {
        CharacterRecord[] records = _dirtyCharacters.Select(Character).OfType<CharacterRecord>().ToArray();
        _dirtyCharacters.Clear(); return records;
    }

    private void ResolveMonsterExperience()
    {
        if (_monsterDeaths.Count == 0) return;
        // Resolve after the full simultaneous damage batch, so dead contributors cannot be credited by iteration order.
        var recipients = _entities.Values.Where(e => e.Character is not null)
            .ToDictionary(e => e.Id, e => new ExperienceRecipient(e.Id, e.Character!.Level, e.Hp > 0, e.Position));
        foreach (var death in _monsterDeaths)
        {
            CombatEntity monster = death.Monster; MonsterSpawn spawn = monster.Monster!.Spawn;
            SpawnMonsterLoot(monster, death.Life, recipients);
            foreach (ExperienceAward award in monster.ExperienceLedger!.Resolve(death.Life, monster.MaxHp, spawn.BaseExperience, spawn.Level, monster.Position, Time, recipients))
            {
                if (award.Experience <= 0 || !_entities.TryGetValue(award.EntityId, out CombatEntity? player)) continue;
                var result = Progression.Award(player.Character!, award.Experience);
                if (result.Awarded == 0) continue;
                player.Character = result.Character;
                QueueProgression(player, result.Awarded, result.Levels);
                if (result.Levels > 0) Emit(CombatEventKind.LevelUp, player, detail: $"Reached level {player.Character.Level}");
            }
        }
        _monsterDeaths.Clear();
    }
}
