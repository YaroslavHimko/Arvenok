using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena;

/// <summary>Opt-in development client. Exercises the same public intent RPCs as interactive input.</summary>
internal sealed class SmokeDriver(ArenaSession session, string role)
{
    private int _stage;
    private bool _interrupted;
    private bool _damaged;
    private bool _died;
    private bool _guarded;
    private long _respawned;
    private int _totalDamage;
    private int _damageEvents;
    private double _nextAction;
    private long _opponent;
    private readonly Dictionary<long, System.Numerics.Vector2> _firstMonsterPositions = new();
    private bool _reportedMonsterMotion;

    public void ObserveState(WorldSnapshot snapshot)
    {
        if (snapshot.Revision % 15 != 0) return;
        EntitySnapshot[] monsters = snapshot.Entities.Where(e => e.Kind == EntityKind.Monster).OrderBy(e => e.Id).ToArray();
        // Compare identical authoritative revisions across independent clients, without relying on arrival time.
        string data = string.Join(";", monsters.Select(e => $"{e.Id}:{e.Species}:{BitConverter.SingleToInt32Bits(e.Position.X)}:{BitConverter.SingleToInt32Bits(e.Position.Y)}:{BitConverter.SingleToInt32Bits(e.Facing.X)}:{BitConverter.SingleToInt32Bits(e.Facing.Y)}:{e.Action}:{e.Wander}:{e.Hp}"));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
        GD.Print($"SMOKE monsters revision={snapshot.Revision} hash={hash} count={monsters.Length}");
        foreach (EntitySnapshot monster in monsters)
        {
            if (!_firstMonsterPositions.TryGetValue(monster.Id, out System.Numerics.Vector2 previous))
                _firstMonsterPositions.Add(monster.Id, monster.Position);
            else if (!_reportedMonsterMotion && monster.Wander == WanderState.Roaming && System.Numerics.Vector2.Distance(previous, monster.Position) > 2)
            { _reportedMonsterMotion = true; GD.Print("SMOKE monster motion observed"); }
        }
    }

    public void Observe(CombatEvent entry)
    {
        if (entry.Kind == CombatEventKind.CastInterrupted) { _interrupted = true; GD.Print("SMOKE interrupt observed"); }
        if (entry.Kind == CombatEventKind.Damage) { _damaged = true; _totalDamage += entry.Damage; _damageEvents++; GD.Print($"SMOKE damage {entry.TargetId} {entry.Damage}"); }
        if (entry.Kind == CombatEventKind.Died) { _died = true; GD.Print("SMOKE death observed"); }
        if (entry.Kind == CombatEventKind.DefenseStarted) { _guarded = true; GD.Print("SMOKE defense observed"); }
        if (entry.Kind == CombatEventKind.Rejected && entry.Detail.Contains("protected")) GD.Print("SMOKE safe-zone rejection observed");
        if (entry.Kind == CombatEventKind.Rejected && entry.Detail.Contains("Duplicate")) GD.Print("SMOKE duplicate rejected");
        if (entry.Kind == CombatEventKind.Respawned) _respawned = entry.ActorId;
    }

    public void Advance()
    {
        if (_stage == 99 || !session.Entities.TryGetValue(session.LocalEntityId, out EntitySnapshot me)
            || session.Entities.Values.Count(e => e.Kind == EntityKind.Player) < 2) return;
        if (_respawned != 0 && _died && _damaged && _interrupted && _guarded
            && session.Entities.TryGetValue(_respawned, out EntitySnapshot respawn) && respawn.Alive && respawn.Protected && respawn.Hp == respawn.MaxHp)
        {
            _stage = 99;
            GD.Print($"SMOKE outcome damage={_totalDamage} hits={_damageEvents} respawnHp={respawn.Hp} protected={respawn.Protected}");
            GD.Print("SMOKE combat complete: interrupt, defense, damage, death, protected respawn");
            return;
        }
        EntitySnapshot other = session.Entities.Values.First(e => e.Kind == EntityKind.Player && e.Id != me.Id);
        _opponent = other.Id;
        bool attacker = role == "attacker";
        if (_stage == 0)
        {
            session.Send(CommandKind.ChooseClass, characterClass: attacker ? CharacterClass.Mage : CharacterClass.Warrior);
            if (attacker) session.Send(CommandKind.UseAbility, other.Id, AbilitySlot.Primary);
            session.Send(CommandKind.SelectTarget, other.Id);
            session.Send(CommandKind.Move, destination: attacker ? new Vector2(480, 448) : new Vector2(520, 448));
            _stage = 1;
            return;
        }
        if (me.Protected || other.Protected || me.Action == ActionState.Moving || other.Action == ActionState.Moving) return;
        if (!_interrupted)
        {
            if (attacker && _stage == 1)
            {
                session.SendDuplicateForSmoke(CommandKind.UseAbility, other.Id, AbilitySlot.Primary);
                _stage = 2;
            }
            if (!attacker && other.Casting && _stage == 1)
            { session.Send(CommandKind.UseAbility, other.Id, AbilitySlot.Interrupt); _stage = 2; }
            return;
        }
        if (!me.Alive || !other.Alive || session.ServerTime < _nextAction || me.Casting || session.ServerTime < me.RecoveryEndsAt) return;
        if (!attacker && !_guarded)
        {
            session.Send(CommandKind.UseAbility, slot: AbilitySlot.Defense);
            _nextAction = session.ServerTime + 0.5;
        }
        if (attacker)
        {
            if (session.ServerTime >= me.Cooldowns.Primary) session.Send(CommandKind.UseAbility, _opponent, AbilitySlot.Primary);
            else if (!me.AutoAttacking) session.Send(CommandKind.Attack, _opponent);
            _nextAction = session.ServerTime + 0.3;
        }
    }
}
