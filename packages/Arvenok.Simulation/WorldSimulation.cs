using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Arvenok.Contracts;

namespace Arvenok.Simulation;

/// <summary>Single-writer, continuously running world. Transport and rendering are host responsibilities.</summary>
public sealed partial class WorldSimulation
{
    private readonly Dictionary<long, CombatEntity> _entities = new();
    private readonly Dictionary<long, long> _peers = new();
    private readonly Queue<CombatEvent> _recent = new();
    private readonly List<CombatEvent> _pending = new();
    private readonly List<(CombatEntity Actor, CombatEntity Target, AbilityDefinition Skill)> _resolved = new();
    private readonly CombatRules _rules = new();
    private long _nextEntityId;
    private long _eventSequence;
    private long _activationSequence;
    public CombatEffectRules Effects { get; } = new();
    public CombatBalance Balance { get; }
    public WorldMap Map { get; }
    public double Time { get; private set; }
    public int PlayerCount => _peers.Count;
    public int MonsterCount => Map.Definition.MonsterSpawns.Count;
    public IEnumerable<CombatEvent> RecentActivity => _recent;

    public WorldSimulation(CombatBalance? balance = null, WorldMap? map = null, Random? lootRandom = null, double? utcOrigin = null)
    {
        _utcOrigin = utcOrigin ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        if (!double.IsFinite(_utcOrigin) || _utcOrigin < 0) throw new ArgumentOutOfRangeException(nameof(utcOrigin));
        Balance = balance ?? new CombatBalance();
        Progression = new ProgressionRules(Balance);
        Inventory = new InventoryRules();
        Economy = new EconomyCatalog(Inventory.Catalog);
        _lootRandom = lootRandom ?? new Random();
        Characters = new CharacterStateRules(Progression, Inventory);
        Map = map ?? new WorldMap();
        foreach (MonsterSpawn spawn in Map.Definition.MonsterSpawns)
        {
            long id = ++_nextEntityId;
            var monster = new CombatEntity { Id = id, PeerId = 0, Kind = EntityKind.Monster,
                Name = spawn.Species.ToString(), Position = spawn.Position, Facing = Vector2.UnitY,
                Hp = spawn.MaxHp, MaxHp = spawn.MaxHp, MoveSpeed = spawn.MoveSpeed, CanChooseClass = false,
                Monster = new MonsterWander(spawn, unchecked((int)id * 397 + 1701), Time), ExperienceLedger = new MonsterExperienceLedger() };
            _entities.Add(id, monster);
            Emit(CombatEventKind.Joined, monster, detail: $"Spawned {spawn.Id}");
        }
    }

    public long EntityForPeer(long peerId) => _peers.GetValueOrDefault(peerId);

    public long Join(long peerId, CharacterClass characterClass = CharacterClass.Warrior, CharacterRecord? character = null)
    {
        if (peerId <= 1 || _peers.ContainsKey(peerId) || PlayerCount >= Balance.MaxPlayers || !Enum.IsDefined(characterClass)) return 0;
        character ??= CharacterRecord.New(Guid.NewGuid(), characterClass, Inventory.Catalog.DefaultCapacity);
        character = Characters.Upgrade(character);
        if (_entities.Values.Any(e => e.Character?.Id == character.Id)) return 0;
        characterClass = character.Class;
        long id = ++_nextEntityId;
        ClassDefinition definition = Balance.Class(characterClass);
        var entity = new CombatEntity { Id = id, PeerId = peerId, Class = characterClass, Name = $"Traveler {id}",
            Hp = definition.MaxHp, MaxHp = definition.MaxHp, MoveSpeed = definition.MoveSpeed, Facing = Vector2.UnitY,
            Position = Spawn(id), Tokens = Balance.CommandBurst, LastTokenTime = Time,
            Character = character, CanChooseClass = !character.ClassLocked };
        if (character.Nickname.Length > 0) entity.Name = character.Nickname;
        if (character.WorldPosition is SavedWorldPosition saved && Map.IsWalkable(new Vector2(saved.X, saved.Y)))
            entity.Position = new Vector2(saved.X, saved.Y);
        RecomputeEquipment(entity); entity.Hp = entity.MaxHp;
        RestoreTimers(entity);
        _entities.Add(id, entity);
        _peers.Add(peerId, id);
        Emit(CombatEventKind.Joined, entity, detail: "Entered the world");
        QueueProgression(entity);
        return id;
    }

    private Vector2 Spawn(long id) => Map.Definition.Spawns[(int)((id - 1) % Map.Definition.Spawns.Count)];

    public bool Leave(long peerId)
    {
        if (!_peers.Remove(peerId, out long id) || !_entities.Remove(id, out CombatEntity? entity)) return false;
        EndCombat(entity, "Disconnected");
        Emit(CombatEventKind.Left, entity, detail: "Disconnected");
        InvalidateTarget(id);
        foreach (CombatEntity monster in _entities.Values) monster.ExperienceLedger?.RemoveAttacker(id);
        _dirtyCharacters.Remove(id);
        return true;
    }

    public IEnumerable<EntitySnapshot> GetSnapshots()
    {
        foreach (CombatEntity entity in _entities.Values) yield return Snapshot(entity);
    }

    private EntitySnapshot Snapshot(CombatEntity e) => new(e.Id, e.Kind, e.Name, e.Class, e.Position, e.Facing,
        e.Hp, e.MaxHp, e.Hp == 0 ? ActionState.Dead : e.Casting
            ? e.ActiveAbility == AbilitySlot.Basic ? ActionState.BasicAttackWindup : ActionState.Casting
            : Time < e.RecoveryEndsAt ? ActionState.SkillRecovery : e.Moving ? ActionState.Moving : ActionState.Idle,
        e.TargetId, e.AttackTargetId != 0, Map.IsProtected(e.Position), e.CanChooseClass,
        e.ActiveAbility, e.CastStartedAt, e.CastEndsAt, e.RecoveryEndsAt, e.DefenseEndsAt, e.RespawnAt,
        new CooldownSnapshot(e.Cooldowns[0], e.Cooldowns[1], e.Cooldowns[2], e.Cooldowns[3], e.Cooldowns[4], e.Cooldowns[5], e.Cooldowns[6]),
        e.Monster?.Spawn.Species, e.Monster?.State ?? WanderState.None, e.DiedAt, e.Dashing ? e.DashEndsAt : 0,
        e.Character?.Level ?? e.Monster?.Spawn.Level ?? 1)
        { DodgeEndsAt = e.Defenses.DodgeEndsAt, ParryEndsAt = e.Defenses.ParryEndsAt, PerfectBlockEndsAt = e.Defenses.PerfectBlockEndsAt,
            ShellEndsAt = e.Defenses.ShellEndsAt, ShellRemaining = Time < e.Defenses.ShellEndsAt ? e.Defenses.ShellRemaining : 0,
            WeaponType = Inventory.Weapon(e.Character)?.WeaponType ?? WeaponType.None, ShieldEquipped = e.Character?.Equipment?.OffHand is not null,
            ActiveAbilityName = e.Casting ? e.AcceptedCast!.Name : "", DefenseName = Time < e.DefenseEndsAt ? e.DefenseName : "" };

    public CombatEvent[] DrainEvents()
    {
        CombatEvent[] events = _pending.ToArray();
        _pending.Clear();
        return events;
    }

    public bool Submit(long entityId, WorldCommand command)
    {
        if (!_entities.TryGetValue(entityId, out CombatEntity? actor) || actor.Kind != EntityKind.Player || actor.PeerId <= 1) return false;
        actor.Tokens = Math.Min(Balance.CommandBurst, actor.Tokens + (Time - actor.LastTokenTime) * Balance.CommandsPerSecond);
        actor.LastTokenTime = Time;
        if (actor.Tokens < 1)
        {
            if (Time - actor.LastRateNotice >= 1) { actor.LastRateNotice = Time; Reject(actor, "Too many requests", command.Slot); }
            return false;
        }
        actor.Tokens--;
        if (command.Sequence <= actor.LastSequence || command.Sequence <= 0) return Reject(actor, "Duplicate or reordered request", command.Slot);
        actor.LastSequence = command.Sequence; // Rejected commands cannot be replayed after circumstances change.
        if (!double.IsFinite(command.ObservedServerTime) || command.ObservedServerTime > Time + 0.5
            || Time - command.ObservedServerTime > Balance.CommandMaxAge) return Reject(actor, "Expired request", command.Slot);
        if (!Enum.IsDefined(command.Kind) || !Enum.IsDefined(command.Slot) || !Enum.IsDefined(command.Class)) return Reject(actor, "Unknown command or ability");
        if (actor.Hp <= 0) return Reject(actor, "You are dead", command.Slot);
        if (!Map.IsWalkable(actor.Position)) return Reject(actor, "Invalid actor position", command.Slot);
        bool accepted = command.Kind switch
        {
            CommandKind.Move => Move(actor, command.Destination),
            CommandKind.SelectTarget => Select(actor, command.TargetId),
            CommandKind.Attack => Attack(actor, command.TargetId),
            CommandKind.Stop => Stop(actor),
            CommandKind.UseAbility => Use(actor, command.Slot, command.TargetId, command.Destination),
            CommandKind.ChooseClass => ChooseClass(actor, command.Class),
            CommandKind.UpgradeSkill => UpgradeSkill(actor, command),
            _ => false
        };
        ResolveMonsterExperience();
        if (accepted) PersistTimers(actor);
        return accepted;
    }

    private bool Move(CombatEntity actor, Vector2 destination)
    {
        List<Vector2>? path = Map.FindPath(actor.Position, destination);
        if (path is null) return Reject(actor, "Destination is blocked or unreachable");
        StopAttack(actor, "Manual movement");
        CancelCast(actor, "Movement cancelled the cast");
        FinishDash(actor, "Movement cancelled mobility");
        actor.Path = path; actor.Waypoint = 0;
        Emit(CombatEventKind.Movement, actor, detail: "Moving");
        return true;
    }

    private bool Select(CombatEntity actor, long targetId)
    {
        if (targetId != 0 && (targetId == actor.Id || !_entities.TryGetValue(targetId, out CombatEntity? target) || target.Hp <= 0))
            return Reject(actor, "Invalid target");
        if (actor.TargetId != targetId) StopAttack(actor, "Target selection changed");
        actor.TargetId = targetId;
        Emit(CombatEventKind.TargetChanged, actor, detail: targetId == 0 ? "Target cleared" : $"Selected {_entities[targetId].Name}");
        return true;
    }

    private bool Attack(CombatEntity actor, long targetId)
    {
        if (!TryEnemy(actor, targetId, out CombatEntity target)) return Reject(actor, "Target is invalid or protected");
        if (actor.Casting || Time < actor.RecoveryEndsAt) return Reject(actor, "Already committed to an action");
        if (Vector2.Distance(actor.Position, target.Position) > Balance.PursuitLeash) return Reject(actor, "Target exceeds pursuit leash");
        if (Map.FindPath(actor.Position, target.Position) is null) return Reject(actor, "Target is unreachable");
        actor.TargetId = targetId; actor.AttackTargetId = targetId; actor.ChaseOrigin = actor.Position;
        actor.NextRepathAt = 0;
        Emit(CombatEventKind.AttackStarted, actor, target, detail: "Basic attack enabled");
        Pursue(actor);
        return true;
    }

    private bool Stop(CombatEntity actor)
    {
        StopAttack(actor, "Cancelled"); CancelCast(actor, "Cancelled"); FinishDash(actor, "Cancelled"); actor.Path = null;
        return true;
    }

    private bool ChooseClass(CombatEntity actor, CharacterClass kind)
    {
        if (!actor.CanChooseClass || !Map.IsProtected(actor.Position) || actor.Casting || Time < actor.RecoveryEndsAt || actor.InCombat)
            return Reject(actor, "Class selection is locked for this character");
        if (actor.Class != kind && (actor.Character!.Equipment!.Weapon is not null || actor.Character.Equipment.Body is not null || actor.Character.Equipment.OffHand is not null))
            return Reject(actor, "Unequip your gear before changing class");
        actor.Class = kind; actor.MaxHp = Balance.Class(kind).MaxHp; actor.Hp = actor.MaxHp; actor.MoveSpeed = Balance.Class(kind).MoveSpeed;
        actor.Character = actor.Character! with { Class = kind, Revision = actor.Character!.Revision + 1 };
        RecomputeEquipment(actor);
        QueueProgression(actor);
        Emit(CombatEventKind.ClassChanged, actor, detail: $"Chose {kind}");
        return true;
    }

    private bool Use(CombatEntity actor, AbilitySlot slot, long targetId, Vector2 destination)
    {
        if (slot == AbilitySlot.Basic) return Attack(actor, targetId);
        string requirement = SkillRequirement(actor, slot);
        if (requirement.Length > 0) return Reject(actor, requirement, slot);
        AbilityDefinition skill = Ability(actor, slot);
        if (actor.Casting || Time < actor.RecoveryEndsAt) return Reject(actor, "Already committed to an action", slot);
        if (Time < actor.Cooldowns[(int)slot]) return Reject(actor, "Ability is on cooldown", slot);
        CombatEntity? target = null;
        if (skill.Effect is AbilityEffect.Damage or AbilityEffect.Interrupt or AbilityEffect.Rush or AbilityEffect.Sidestep)
        {
            if (!TryEnemy(actor, targetId, out target)) return Reject(actor, "Target is invalid or protected", slot);
            if (skill.Effect != AbilityEffect.Rush && !InRange(actor, target, skill)) return Reject(actor, "Out of range or line of sight", slot);
            if (skill.Effect == AbilityEffect.Rush && Vector2.Distance(actor.Position, target.Position) > Balance.PursuitLeash)
                return Reject(actor, "Target exceeds pursuit leash", slot);
        }
        if (skill.CastSeconds > 0 && actor.Moving) return Reject(actor, "Stop moving before casting", slot);
        Vector2 mobilityStart = actor.Position, mobilityEnd = actor.Position;
        if (skill.Effect is AbilityEffect.Rush or AbilityEffect.Blink or AbilityEffect.Backstep or AbilityEffect.Sidestep)
        {
            Vector2 direction;
            if (skill.Effect == AbilityEffect.Blink)
            {
                if (!WorldMap.Finite(destination)) return Reject(actor, "Invalid destination", slot);
                direction = destination - actor.Position;
            }
            else if (skill.Effect == AbilityEffect.Rush) direction = target!.Position - actor.Position;
            else if (skill.Effect == AbilityEffect.Sidestep) { Vector2 forward = target!.Position - actor.Position; direction = new Vector2(-forward.Y, forward.X); }
            else direction = _entities.TryGetValue(actor.TargetId, out CombatEntity? selected) && selected.Hp > 0
                ? actor.Position - selected.Position : -actor.Facing;
            float distance = direction.Length();
            if (distance < 0.01f || !float.IsFinite(distance)) return Reject(actor, "No mobility direction", slot);
            float travel = skill.Effect == AbilityEffect.Sidestep ? skill.SidestepDistance : skill.Effect == AbilityEffect.Backstep ? skill.Range : Math.Min(distance, skill.Range);
            if (skill.Effect == AbilityEffect.Rush) travel = Math.Min(travel, Math.Max(0, distance - Map.Definition.BodyRadius * 2));
            mobilityEnd = Map.Sweep(actor.Position, actor.Position + direction / distance * travel);
            if (Vector2.DistanceSquared(mobilityEnd, actor.Position) < 1) return Reject(actor, "Movement is blocked", slot);
        }
        StopAttack(actor, "Skill requested");
        LockClass(actor);
        actor.ActiveAbility = slot;
        SetSkillCooldown(actor, skill);
        if (skill.Effect == AbilityEffect.Sidestep)
        {
            StartCast(actor, target!, skill); actor.Dashing = true; actor.DashAbility = slot; actor.DashDestination = mobilityEnd;
            actor.DashSpeed = Vector2.Distance(actor.Position, mobilityEnd) / (float)skill.CastSeconds; actor.DashEndsAt = actor.CastEndsAt;
            actor.Defenses.Dodge(Time + Effects.DodgeSeconds);
            Emit(CombatEventKind.Movement, actor, target, skill, origin: mobilityStart); return true;
        }
        if (skill.CastSeconds > 0) { StartCast(actor, target!, skill); return true; }
        actor.Activation = ++_activationSequence;
        if (skill.Effect == AbilityEffect.Defense)
        {
            ActivateDefense(actor, skill);
        }
        else if (skill.Effect == AbilityEffect.Interrupt)
        {
            DealDamage(actor, target!, skill);
        }
        else
        {
            actor.Facing = Vector2.Normalize(mobilityEnd - actor.Position);
            actor.Path = null;
            if (skill.TravelSeconds > 0)
            {
                // A committed straight dash to the accepted endpoint; it never homes onto a moving target.
                actor.Dashing = true; actor.DashAbility = slot; actor.DashDestination = mobilityEnd;
                actor.DashSpeed = Vector2.Distance(actor.Position, mobilityEnd) / (float)skill.TravelSeconds;
                actor.DashEndsAt = Time + skill.TravelSeconds;
                actor.RecoveryEndsAt = actor.DashEndsAt + Balance.RecoverySeconds;
                Emit(CombatEventKind.Movement, actor, skill: skill, detail: skill.Name, origin: mobilityStart);
                return true;
            }
            actor.Position = mobilityEnd;
            Emit(CombatEventKind.Movement, actor, skill: skill, detail: skill.Name, origin: mobilityStart);
        }
        actor.RecoveryEndsAt = Time + Balance.RecoverySeconds;
        if (skill.Effect != AbilityEffect.Interrupt) Emit(CombatEventKind.SkillCompleted, actor, target, skill);
        return true;
    }

    private bool TryEnemy(CombatEntity actor, long id, out CombatEntity target)
    {
        target = null!;
        return _entities.TryGetValue(id, out target!) && _rules.CanHarm(actor, target, Map);
    }

    private bool InRange(CombatEntity actor, CombatEntity target, AbilityDefinition skill) =>
        Vector2.Distance(actor.Position, target.Position) <= skill.Range + 0.001f && Map.HasLineOfSight(actor.Position, target.Position);

    private void StartCast(CombatEntity actor, CombatEntity target, AbilityDefinition skill)
    {
        actor.Path = null; actor.Casting = true; actor.ActiveAbility = skill.Slot; actor.CastTargetId = target.Id;
        actor.CastStartedAt = Time; actor.CastEndsAt = Time + skill.CastSeconds;
        actor.AcceptedCast = skill;
        actor.Activation = ++_activationSequence;
        SetSkillCooldown(actor, skill);
        actor.Facing = target.Position == actor.Position ? actor.Facing : Vector2.Normalize(target.Position - actor.Position);
        Emit(CombatEventKind.CastStarted, actor, target, skill);
        PersistTimers(actor);
    }

    private void CancelCast(CombatEntity actor, string reason, bool interrupted = false)
    {
        if (!actor.Casting) return;
        AbilityDefinition skill = actor.AcceptedCast ?? Ability(actor, actor.ActiveAbility);
        actor.Casting = false; actor.CastEndsAt = 0;
        actor.AcceptedCast = null;
        Emit(interrupted ? CombatEventKind.CastInterrupted : CombatEventKind.CastCancelled, actor, skill: skill, detail: reason);
    }

    private void StopAttack(CombatEntity actor, string reason)
    {
        if (actor.AttackTargetId == 0) return;
        actor.AttackTargetId = 0; actor.Path = null;
        Emit(CombatEventKind.AttackStopped, actor, detail: reason);
    }

    private void Pursue(CombatEntity actor)
    {
        if (actor.AttackTargetId == 0) return;
        if (!TryEnemy(actor, actor.AttackTargetId, out CombatEntity target)
            || Vector2.Distance(actor.ChaseOrigin, target.Position) > Balance.PursuitLeash
            || Vector2.Distance(actor.ChaseOrigin, actor.Position) > Balance.PursuitLeash)
        { StopAttack(actor, "Target lost, protected, or beyond pursuit leash"); CancelCast(actor, "Attack target unavailable"); return; }
        if (actor.Casting || Time < actor.RecoveryEndsAt) return;
        AbilityDefinition basic = Ability(actor, AbilitySlot.Basic);
        if (InRange(actor, target, basic))
        {
            actor.Path = null;
            if (Time >= actor.Cooldowns[0]) StartCast(actor, target, basic);
            return;
        }
        if (Time < actor.NextRepathAt) return;
        actor.NextRepathAt = Time + Balance.RepathSeconds;
        actor.Path = Map.FindPath(actor.Position, target.Position); actor.Waypoint = 0;
        if (actor.Path is null) StopAttack(actor, "Target is unreachable");
    }

    public void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0 || deltaSeconds > 1)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Use steps in (0, 1] seconds.");
        Time += deltaSeconds;
        ExpireLoot();
        foreach (CombatEntity actor in _entities.Values)
        {
            actor.ExperienceLedger?.Prune(Time);
            if (actor.Hp <= 0)
            {
                if (Time >= actor.RespawnAt)
                {
                    actor.Hp = actor.MaxHp; actor.Position = actor.Monster?.Spawn.Position ?? Spawn(actor.Id); actor.RespawnAt = 0; actor.DiedAt = 0;
                    actor.Monster?.Rest(Time);
                    actor.ExperienceLedger?.Respawn();
                    Emit(CombatEventKind.Respawned, actor, detail: actor.Kind == EntityKind.Monster ? "Respawned at home" : "Respawned in the sanctuary");
                }
                continue;
            }
            if (actor.DefenseEndsAt > 0 && Time >= actor.DefenseEndsAt)
            { actor.DefenseEndsAt = 0; actor.DamageReduction = 0; Emit(CombatEventKind.DefenseEnded, actor); }
            if (actor.InCombat && Time - actor.LastCombatAt >= Balance.CombatTimeoutSeconds) EndCombat(actor, "Disengaged");
            if (actor.Monster is MonsterWander monster) monster.Advance(actor, Map, Time);
            else Pursue(actor);
            if (actor.Dashing) AdvanceDash(actor, (float)deltaSeconds);
            else if (!actor.Casting) AdvanceMovement(actor, (float)deltaSeconds);
            actor.Monster?.AfterMovement(actor, Time);
            if (!Map.IsProtected(actor.Position)) LockClass(actor);
        }
        ResolveCasts();
        ResolveMonsterExperience();
    }

    private void AdvanceDash(CombatEntity actor, float delta)
    {
        Vector2 offset = actor.DashDestination - actor.Position;
        float distance = offset.Length();
        if (distance <= 0.001f) { FinishDash(actor); return; }
        float step = Math.Min(distance, actor.DashSpeed * delta);
        actor.Position = Map.Sweep(actor.Position, actor.Position + offset / distance * step);
        if (Time + 0.000000001 >= actor.DashEndsAt || Vector2.DistanceSquared(actor.Position, actor.DashDestination) < 0.01f)
            FinishDash(actor);
    }

    private void FinishDash(CombatEntity actor, string detail = "")
    {
        if (!actor.Dashing) return;
        actor.Dashing = false;
        actor.RecoveryEndsAt = Time + Balance.RecoverySeconds;
        if (actor.DashAbility == AbilitySlot.Mobility) Emit(CombatEventKind.SkillCompleted, actor, skill: Ability(actor, actor.DashAbility), detail: detail);
        PersistTimers(actor);
    }

    private void AdvanceMovement(CombatEntity actor, float delta)
    {
        float remaining = actor.MoveSpeed * delta;
        while (actor.Moving && remaining > 0)
        {
            Vector2 waypoint = actor.Path![actor.Waypoint];
            Vector2 offset = waypoint - actor.Position;
            float distance = offset.Length();
            if (distance < 0.001f) { actor.Waypoint++; continue; }
            float step = Math.Min(remaining, distance);
            Vector2 end = Map.Sweep(actor.Position, actor.Position + offset / distance * step);
            if (Vector2.DistanceSquared(end, actor.Position) < 0.000001f) { actor.Path = null; StopAttack(actor, "Path blocked"); break; }
            actor.Facing = offset / distance; actor.Position = end; remaining -= step;
            if (step >= distance) actor.Waypoint++;
            if (actor.AttackTargetId != 0 && TryEnemy(actor, actor.AttackTargetId, out CombatEntity target)
                && InRange(actor, target, Ability(actor, AbilitySlot.Basic))) { actor.Path = null; break; }
        }
    }

    private void ResolveCasts()
    {
        _resolved.Clear();
        // Validate all completions before applying damage: same-tick lethal hits can trade fairly.
        foreach (CombatEntity actor in _entities.Values)
        {
            if (!actor.Casting || Time + 0.000000001 < actor.CastEndsAt) continue;
            AbilityDefinition skill = actor.AcceptedCast ?? Ability(actor, actor.ActiveAbility);
            actor.Casting = false; actor.CastEndsAt = 0; actor.RecoveryEndsAt = Time + Balance.RecoverySeconds;
            actor.AcceptedCast = null;
            PersistTimers(actor);
            if (TryEnemy(actor, actor.CastTargetId, out CombatEntity target) && InRange(actor, target, skill))
            {
                _resolved.Add((actor, target, skill));
                Emit(CombatEventKind.SkillCompleted, actor, target, skill);
            }
            else Emit(CombatEventKind.CastCancelled, actor, skill: skill, detail: "Target lost, protected, out of range, or obscured");
        }
        foreach (var resolved in _resolved) DealDamage(resolved.Actor, resolved.Target, resolved.Skill);
    }

    private void DealDamage(CombatEntity actor, CombatEntity target, AbilityDefinition skill)
    {
        if (target.Hp <= 0) return;
        float reduction = Time < target.DefenseEndsAt ? skill.DamageType == DamageType.Physical ? Math.Max(target.DamageReduction, target.PhysicalReduction) : target.DamageReduction : 0;
        int bonus = skill.Damage > 0 && actor.Character is not null ? Inventory.EquipmentBonuses(actor.Character).Damage : 0;
        bool frontal = Vector2.Dot(target.Facing, actor.Position - target.Position) >= 0;
        HitResolution hit = target.Defenses.Resolve(actor.Activation, Time, skill.Damage + bonus + PassiveDamage(actor, target, skill), skill.DamageType, true, frontal, reduction);
        if (hit.Outcome == HitOutcome.Duplicate) return;
        if (skill.Effect == AbilityEffect.Interrupt)
        {
            bool interrupted = hit.Outcome is HitOutcome.Damage or HitOutcome.Absorbed && target.Casting && Time < target.CastEndsAt
                && (target.AcceptedCast ?? Ability(target, target.ActiveAbility)).Interruptible;
            if (interrupted) CancelCast(target, $"Interrupted by {skill.Name}", true);
            Emit(CombatEventKind.SkillCompleted, actor, target, skill, detail: interrupted ? "Interrupt successful" : "No active cast interrupted");
        }
        TouchCombat(actor); TouchCombat(target);
        if (hit.Outcome is HitOutcome.Dodged or HitOutcome.Parried or HitOutcome.PerfectBlocked or HitOutcome.Absorbed)
            Emit(hit.Outcome switch { HitOutcome.Dodged => CombatEventKind.Dodged, HitOutcome.Parried => CombatEventKind.Parried,
                HitOutcome.PerfectBlocked => CombatEventKind.PerfectBlocked, _ => CombatEventKind.Absorbed }, actor, target, skill, Math.Min(target.Hp, hit.Damage));
        if (hit.Outcome == HitOutcome.PerfectBlocked || hit.Outcome == HitOutcome.Absorbed && target.Defenses.ShellRemaining == 0)
        { target.DefenseEndsAt = 0; Emit(CombatEventKind.DefenseEnded, target, detail: "Defense consumed"); }
        int damage = Math.Min(target.Hp, hit.Damage);
        bool shieldBlocked = hit.Outcome == HitOutcome.Damage && Time < target.DefenseEndsAt
            && target.Character?.Equipment?.OffHand is not null && reduction > 0;
        if (shieldBlocked) Emit(CombatEventKind.ShieldBlocked, actor, target, skill, detail: "Shield Guard");
        if (actor.Kind == EntityKind.Player) target.ExperienceLedger?.Record(actor.Id, damage, target.Hp, Time);
        target.Hp -= damage;
        if (damage > 0) Emit(CombatEventKind.Damage, actor, target, skill, damage);
        int healed = actor.Lifesteal.Heal(Effects, actor.Activation, target.Id, damage, target.Kind == EntityKind.Player,
            actor.MaxHp - actor.Hp, actor.Hp > 0 && actor.SanguineEdge && hit.Outcome == HitOutcome.Damage && !shieldBlocked);
        if (healed > 0) { actor.Hp += healed; Emit(CombatEventKind.Healed, actor, actor, skill, healed, $"+{healed} HP"); }
        if (target.Hp != 0) return;
        CancelCast(target, "Died"); StopAttack(target, "Died");
        target.TargetId = 0; target.Path = null; target.Dashing = false; target.RecoveryEndsAt = 0;
        if (target.DefenseEndsAt > 0) Emit(CombatEventKind.DefenseEnded, target, detail: "Died");
        target.DefenseEndsAt = 0; target.DamageReduction = 0;
        target.PhysicalReduction = 0; target.Defenses.Clear();
        double respawnSeconds = target.Monster?.Spawn.RespawnSeconds ?? Balance.RespawnSeconds;
        target.RespawnAt = Time + respawnSeconds; target.DiedAt = Time; target.Monster?.Died();
        EndCombat(target, "Died");
        Emit(CombatEventKind.Died, actor, target, skill, detail: $"Defeated; respawn in {respawnSeconds:0.#} seconds");
        if (target.ExperienceLedger is not null) _monsterDeaths.Add((target, target.ExperienceLedger.Life));
        InvalidateTarget(target.Id);
    }

    private void InvalidateTarget(long id)
    {
        foreach (CombatEntity other in _entities.Values)
        {
            if (other.TargetId == id) { other.TargetId = 0; Emit(CombatEventKind.TargetChanged, other, detail: "Target unavailable"); }
            if (other.AttackTargetId == id) StopAttack(other, "Target unavailable");
            if (other.Casting && other.CastTargetId == id) CancelCast(other, "Target unavailable");
        }
    }

    private void TouchCombat(CombatEntity actor)
    {
        // A staged simultaneous hit may come from an actor who died in this resolution batch.
        if (actor.Hp <= 0) return;
        if (!actor.InCombat) { actor.InCombat = true; Emit(CombatEventKind.CombatStarted, actor); }
        actor.LastCombatAt = Time;
        PersistTimers(actor);
    }

    private void EndCombat(CombatEntity actor, string detail)
    {
        if (!actor.InCombat) return;
        actor.InCombat = false; Emit(CombatEventKind.CombatEnded, actor, detail: detail);
    }

    private bool Reject(CombatEntity actor, string reason, AbilitySlot slot = AbilitySlot.Basic)
    {
        AbilityDefinition? skill = Enum.IsDefined(slot) ? Ability(actor, slot) : null;
        Emit(CombatEventKind.Rejected, actor, skill: skill, detail: reason);
        return false;
    }

    private void Emit(CombatEventKind kind, CombatEntity actor, CombatEntity? target = null,
        AbilityDefinition? skill = null, int damage = 0, string detail = "", Vector2? origin = null)
    {
        var entry = new CombatEvent(++_eventSequence, Time, kind, actor.Id, target?.Id ?? 0,
            actor.Class, target?.Class ?? actor.Class, skill?.Slot ?? actor.ActiveAbility,
            skill?.Name ?? "", damage, skill?.CastSeconds ?? 0, detail, actor.Kind, target?.Kind ?? actor.Kind,
            actor.Position, target?.Position ?? actor.Position, actor.Facing, origin,
            skill?.Effect == AbilityEffect.Defense ? actor.DefenseEndsAt : actor.Dashing ? actor.DashEndsAt : 0, Inventory.Weapon(actor.Character)?.WeaponType ?? WeaponType.None);
        _recent.Enqueue(entry);
        while (_recent.Count > Balance.TelemetryCapacity) _recent.Dequeue();
        // Bound unsent diagnostics too, even when a headless host temporarily stops draining.
        if (_pending.Count < Balance.TelemetryCapacity) _pending.Add(entry);
    }
}
