using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena;

/// <summary>Opt-in native integration scenario, using only normal movement, combat and upgrade RPCs.</summary>
internal sealed class ProgressionSmokeDriver(ArenaSession session, string role)
{
    private int _stage;
    private double _nextAction;
    private long _monster;
    private bool _duplicate, _stale;
    private readonly HashSet<long> _rankedHits = new();
    public bool Finished => _stage == 99;
    public void ObserveProgress(ProgressionUpdate update)
    {
        if (update.AwardedExperience > 0) GD.Print($"PROGRESSION award={update.AwardedExperience} level={update.State.Level} xp={update.State.Experience} points={update.State.SkillPoints}");
        if (update.RequestSequence > 0 && !update.Accepted)
        {
            if (update.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)) _duplicate = true;
            if (update.Message.Contains("changed", StringComparison.OrdinalIgnoreCase)) _stale = true;
            GD.Print("PROGRESSION rejected=" + update.Message);
        }
    }
    public void Observe(CombatEvent entry)
    {
        if (entry.Kind == CombatEventKind.Died && entry.TargetId == _monster) GD.Print($"PROGRESSION death={entry.Sequence}");
        if (entry.Kind == CombatEventKind.Damage && entry.TargetId == _monster && entry.Ability == AbilitySlot.Primary
            && entry.Damage == (entry.ActorClass == CharacterClass.Mage ? 22 : 20))
        { _rankedHits.Add(entry.ActorId); GD.Print($"PROGRESSION ranked-damage actor={entry.ActorId} amount={entry.Damage}"); }
    }
    public void Advance()
    {
        if (_stage == 99 || session.Progress is not ProgressionSnapshot state || !session.Entities.TryGetValue(session.LocalEntityId, out EntitySnapshot me)) return;
        if (role == "progression-restore") { Complete(state, "restored"); return; }
        EntitySnapshot[] players = session.Entities.Values.Where(e => e.Kind == EntityKind.Player).ToArray();
        if (players.Length != 2 || session.ServerTime < _nextAction) return;
        EntitySnapshot monster = session.Entities.Values.FirstOrDefault(e => e.Kind == EntityKind.Monster && e.Level == 6);
        if (monster.Id == 0) return;
        _monster = monster.Id;
        _nextAction = session.ServerTime + 0.25;
        if (_stage == 0)
        {
            session.Send(CommandKind.ChooseClass, characterClass: role == "progression-a" ? CharacterClass.Warrior : CharacterClass.Mage);
            session.Send(CommandKind.Move, destination: Point(monster.Position));
            _stage = 1; return;
        }
        if (_stage == 1)
        {
            if (players.Any(p => System.Numerics.Vector2.Distance(p.Position, monster.Position) > 190)) return;
            _stage = 2;
        }
        if (_stage == 2)
        {
            if (players.All(p => p.Level >= 2))
            {
                session.Send(CommandKind.Stop);
                if (state.Level < 2) return; // Owner update follows the public level snapshot.
                session.DuplicateUpgradeForSmoke(); _stage = 3; return;
            }
            if (monster.Alive && !me.AutoAttacking) session.Send(CommandKind.Attack, monster.Id);
            return;
        }
        if (_stage == 3)
        {
            if (session.UpgradePending || state.Ranks.Primary != 2 || !_duplicate || !_stale) return;
            if (_rankedHits.Contains(me.Id))
            {
                if (_rankedHits.Count == 2) Complete(state, "earned");
                return;
            }
            if (!monster.Alive || monster.Hp < 50 || me.Casting || session.ServerTime < me.RecoveryEndsAt || session.ServerTime < me.Cooldowns.Primary) return;
            float distance = System.Numerics.Vector2.Distance(me.Position, monster.Position);
            if (distance > session.EffectiveAbility(me.Class, AbilitySlot.Primary).Range - 4)
            { session.Send(CommandKind.Move, destination: Point(monster.Position)); return; }
            if (me.Action == ActionState.Moving) { session.Send(CommandKind.Stop); return; }
            session.Send(CommandKind.UseAbility, monster.Id, AbilitySlot.Primary);
        }
    }
    private void Complete(ProgressionSnapshot state, string outcome)
    {
        _stage = 99;
        GD.Print("PROGRESSION state=" + JsonSerializer.Serialize(new { state.CharacterId, state.Class, state.Level, state.Experience, state.SkillPoints, state.Ranks, state.Revision }));
        GD.Print("PROGRESSION complete=" + outcome);
    }
    private static Vector2 Point(System.Numerics.Vector2 value) => new(value.X, value.Y);
}
