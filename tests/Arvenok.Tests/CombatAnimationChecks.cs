using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Arvenok.Contracts;
using Arvenok.Game.Features.Arena.Presentation;

namespace Arvenok.Tests;

internal static partial class Program
{
    private static AnimationCatalog AnimationData()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Arvenok.sln"))) root = Directory.GetParent(root)?.FullName ?? throw new Exception("Repository not found.");
        string collection = Path.Combine(root, "apps/game/Assets/Art2D/woodvale-combat");
        return new AnimationCatalog(File.ReadAllText(Path.Combine(collection, "animation_manifest.json")), File.ReadAllText(Path.Combine(collection, "visual_bindings.json")));
    }

    private static void AnimationDefinitions()
    {
        AnimationCatalog data = AnimationData();
        Require(data.Clips.Count == 134 && data.CellWidth == 96 && data.CellHeight == 96);
        foreach (AnimationClip clip in data.Clips.Values)
        {
            Require(clip.Frames == 8 && clip.Frame(0) == 0);
            Require(clip.Loop ? clip.Frame(clip.Seconds) == 0 : clip.Frame(clip.Seconds * 5) == 7);
        }
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
            foreach (AbilitySlot slot in Enum.GetValues<AbilitySlot>()) Require(data.Clips.ContainsKey("effects/" + data.Skill(kind, slot).Effect));
        var f = new Fixture();
        foreach (var facing in new[] { (Vector2.UnitY, "down"), (-Vector2.UnitX, "left"), (Vector2.UnitX, "right"), (-Vector2.UnitY, "up") })
            Require(data.EntityKey(f.State(f.A) with { Facing = facing.Item1 }, "walk").EndsWith("/" + facing.Item2, StringComparison.Ordinal));
    }

    private static void AnimationCancellation()
    {
        AnimationCatalog data = AnimationData();
        var f = new Fixture(CharacterClass.Mage, CharacterClass.Warrior);
        var view = new EntityAnimationState(f.A, data); var effects = new CombatEffectPlanner(data);
        f.Game.DrainEvents();
        Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
        EntitySnapshot cast = f.State(f.A); double castTime = f.Game.Time;
        foreach (CombatEvent e in f.Game.DrainEvents()) view.Observe(e);
        Require(view.Pose(cast, castTime, castTime + 0.4).Clip == "cast");
        Require(f.Send(f.B, CommandKind.Attack, f.A)); f.Step(0.23);
        foreach (CombatEvent e in f.Game.DrainEvents()) view.Observe(e);
        Require(f.State(f.A).Casting && view.Pose(f.State(f.A), f.Game.Time, f.Game.Time).Clip == "hurt");
        f.Step(0.2);
        Require(view.Pose(f.State(f.A), f.Game.Time, f.Game.Time).Clip == "cast", "Hurt must resume the original cast progress.");
        Require(f.Send(f.A, CommandKind.Move, point: new Vector2(464, 448)));
        foreach (CombatEvent e in f.Game.DrainEvents()) { view.Observe(e); Require(effects.Observe(e, f.Game.Time).Count == 0); }
        Require(view.Pose(cast, castTime, f.Game.Time).Clip != "cast", "An older snapshot must not revive a cancelled cast.");
        f.Step(1);
        Require(!f.Game.RecentActivity.Any(e => e.ActorId == f.A && e.Kind == CombatEventKind.Damage));

        var interrupted = new Fixture(CharacterClass.Mage);
        var interruptedView = new EntityAnimationState(interrupted.A, data);
        interrupted.Game.DrainEvents(); interrupted.Skill(interrupted.A, interrupted.B, AbilitySlot.Primary); interrupted.Step(0.1);
        EntitySnapshot beforeInterrupt = interrupted.State(interrupted.A); double beforeTime = interrupted.Game.Time;
        interrupted.Step(0.1); interrupted.Skill(interrupted.B, interrupted.A, AbilitySlot.Interrupt);
        foreach (CombatEvent e in interrupted.Game.DrainEvents()) interruptedView.Observe(e);
        Require(interruptedView.Pose(beforeInterrupt, beforeTime, interrupted.Game.Time).Clip == "interrupt");
        Require(interruptedView.Pose(beforeInterrupt, beforeTime, interrupted.Game.Time + 0.4).Clip != "cast");
    }

    private static void AnimationProjectiles()
    {
        AnimationCatalog data = AnimationData();
        foreach (CharacterClass kind in new[] { CharacterClass.Mage, CharacterClass.Archer })
        {
            var f = new Fixture(kind); f.Game.DrainEvents();
            Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
            f.Step(f.Game.Balance.Class(kind).Ability(AbilitySlot.Primary).CastSeconds + 0.01);
            CombatEvent[] events = f.Game.DrainEvents();
            CombatEvent hit = events.Single(e => e.Kind == CombatEventKind.Damage);
            Require(hit.ActorPosition == f.State(f.A).Position && hit.TargetPosition == f.State(f.B).Position);
            int hp = f.State(f.B).Hp;
            Require(hp == f.State(f.B).MaxHp - hit.Damage, "Server damage occurs before cosmetic flight.");
            var planner = new CombatEffectPlanner(data);
            Require(events.Where(e => e.Kind == CombatEventKind.SkillCompleted).All(e => planner.Observe(e, hit.Time).Count == 0));
            var cues = planner.Observe(hit, hit.Time);
            CombatEffectCue shot = cues.Single(c => c.Projectile);
            Require(shot.EndsAt > hit.Time && shot.Position(hit.Time) == shot.From && shot.Position(shot.EndsAt) == shot.To);
            Vector2 middle = shot.Position((hit.Time + shot.EndsAt) / 2);
            Require(Vector2.Distance(middle, (shot.From + shot.To) / 2) < 0.01f && f.State(f.B).Hp == hp);
            Require(planner.Observe(hit, hit.Time).Count == 0 && new CombatEffectPlanner(data).Observe(hit, hit.Time + 2).Count == 0);
            if (kind == CharacterClass.Mage) Require(cues.Any(c => c.Effect == "mage/firebolt_impact" && c.StartedAt == hit.Time));
        }
    }

    private static void AnimationDefense()
    {
        AnimationCatalog data = AnimationData();
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
        {
            var f = new Fixture(kind); f.Game.DrainEvents(); f.Skill(f.A, 0, AbilitySlot.Defense);
            EntitySnapshot active = f.State(f.A); double at = f.Game.Time;
            var joined = new EntityAnimationState(f.A, data);
            Require(joined.Defending(active, at, at + 1));
            Require(!joined.Defending(active, at, active.DefenseEndsAt));
            foreach (CombatEvent e in f.Game.DrainEvents()) joined.Observe(e);
            Require(joined.Defending(active with { DefenseEndsAt = 0 }, at, at + 1), "New reliable activation wins over a snapshot from earlier in the same tick.");
            f.Step(2.01);
            foreach (CombatEvent e in f.Game.DrainEvents()) joined.Observe(e);
            Require(!joined.Defending(active, at, at + 1), "Reliable expiry beats an older snapshot, even with an older presentation clock.");
            f.Step(14); f.Skill(f.A, 0, AbilitySlot.Defense);
            EntitySnapshot moving = f.State(f.A) with { Action = ActionState.Moving };
            foreach (CombatEvent e in f.Game.DrainEvents()) joined.Observe(e);
            Require(joined.Defending(moving, f.Game.Time, f.Game.Time) && joined.Pose(moving, f.Game.Time, f.Game.Time).Clip == "walk");
            f.Game.Leave(101);
            foreach (CombatEvent e in f.Game.DrainEvents()) joined.Observe(e);
            Require(joined.Removed && !joined.Defending(moving, f.Game.Time - 0.1, f.Game.Time), "Disconnect clears attached effects without waiting for a snapshot.");
        }
    }

    private static void AnimationMobility()
    {
        AnimationCatalog data = AnimationData();
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
        {
            var f = new Fixture(kind); f.Game.DrainEvents();
            Vector2 start = f.State(f.A).Position;
            Require(f.Send(f.A, CommandKind.UseAbility, f.B, AbilitySlot.Mobility, start + new Vector2(0, 80)));
            CombatEvent movement = f.Game.DrainEvents().Single(e => e.Kind == CombatEventKind.Movement);
            Require(movement.MovementFrom == start);
            var state = new EntityAnimationState(f.A, data); state.Observe(movement);
            var cues = new CombatEffectPlanner(data).Observe(movement, f.Game.Time);
            if (kind == CharacterClass.Mage)
            {
                Require(cues.Count == 2 && cues[0].From != cues[1].From && movement.ActorPosition == f.State(f.A).Position);
                Require(!state.Dashing(f.State(f.A), f.Game.Time, f.Game.Time));
            }
            else
            {
                Require(cues.Count == 0 && f.State(f.A).Position == start && state.Dashing(f.State(f.A), f.Game.Time, f.Game.Time));
                double snapshotTime = f.Game.Time; EntitySnapshot moving = f.State(f.A);
                f.Send(f.A, CommandKind.Stop);
                foreach (CombatEvent e in f.Game.DrainEvents()) state.Observe(e);
                Require(!state.Dashing(moving, snapshotTime, f.Game.Time + 0.01), "Cancelled dash removes its trail before its original expiry.");
            }
        }
    }

    private static void AnimationDeath()
    {
        AnimationCatalog data = AnimationData();
        var f = PassiveMonsterFixture();
        long monster = f.Game.GetSnapshots().Single(e => e.Kind == EntityKind.Monster).Id;
        f.Game.DrainEvents(); f.Skill(f.A, monster, AbilitySlot.Primary); f.Step(1.01);
        EntitySnapshot dead = f.State(monster);
        Require(dead.DiedAt > 0);
        foreach (EntitySnapshot entity in new[] { dead, dead with { Kind = EntityKind.Player, Species = null } })
        {
            var lateJoin = new EntityAnimationState(entity.Id, data);
            AnimationPose pose = lateJoin.Pose(entity, f.Game.Time, f.Game.Time + 2);
            Require(pose.Clip == "death" && data.Clips[data.EntityKey(entity, pose.Clip)].Frame(pose.Elapsed) == 7);
            var view = new EntityAnimationState(entity.Id, data);
            foreach (CombatEvent e in f.Game.RecentActivity) view.Observe(e);
            Require(view.Pose(entity with { Hp = entity.MaxHp }, dead.DiedAt, dead.DiedAt).Clip == "death", "Reliable death wins over a living snapshot from the same tick.");
            Require(!view.Defending(entity with { DefenseEndsAt = 100 }, f.Game.Time, f.Game.Time));
            var respawn = new CombatEvent(10000, dead.RespawnAt, CombatEventKind.Respawned, entity.Id, 0, entity.Class, entity.Class,
                AbilitySlot.Basic, "", 0, 0, "");
            view.Observe(respawn);
            Require(view.Pose(entity, f.Game.Time, dead.RespawnAt).Clip != "death", "Respawn event wins over an old dead snapshot.");
        }
        f.Step(3.1); Require(f.State(monster).DiedAt == 0 && f.State(monster).Alive);
    }

    private static void AnimationAllSkills()
    {
        AnimationCatalog data = AnimationData();
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
            foreach (AbilitySlot slot in new[] { AbilitySlot.Basic, AbilitySlot.Primary, AbilitySlot.Interrupt })
            {
                var f = new Fixture(kind); f.Game.DrainEvents();
                Require(f.Skill(f.A, f.B, slot));
                f.Step(f.Game.Balance.Class(kind).Ability(slot).CastSeconds + 0.04);
                CombatEvent hit = f.Game.DrainEvents().First(e => e.Kind == CombatEventKind.Damage);
                var cues = new CombatEffectPlanner(data).Observe(hit, hit.Time);
                Require(cues.Any(c => c.Effect == data.Skill(kind, slot).Effect));
            }
    }
}
