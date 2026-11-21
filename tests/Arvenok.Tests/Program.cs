using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Arvenok.Contracts;
using Arvenok.Hosting;
using Arvenok.Simulation;

namespace Arvenok.Tests;

internal static partial class Program
{
    private static int Main()
    {
        var cases = new List<(string Name, Action Run)>
        {
            ("Accounts enforce transactional ownership, six slots, unique names, retries and deletion", AccountOwnership),
            ("Account aggregates retain progression, skills, equipment and position across restart", AccountAggregatePersistence),
            ("All spell scrolls validate ownership and atomically consume one scroll and point", SpellLearning),
            ("Spell learning rolls back failed storage and persists ownership, cooldown gates and replay", SpellPersistence),
            ("All six spell scrolls extend natural death loot and preserve pickup protection", SpellLoot),
            ("Legacy equipment and crafting receipt fingerprints remain replay-compatible", LegacyEconomyReceipt),
            ("Branch learning preserves core ranks, validates gates and spends points once", TreeLearning),
            ("Tree migration preserves legacy ownership and learning commits durably", TreeMigration),
            ("All six branch actives resolve server damage, movement and shared defense cooldowns", BranchActiveSkills),
            ("All six passives alter only their specified authoritative effects", PassiveEffects),
            ("Persisted shared cooldowns survive reconnect and account for server downtime", PersistentCooldowns),
            ("Hand transactions enforce capacity, classes, levels, reservation and exact ownership", WeaponTransactions),
            ("Weapon damage, melee range, attack intervals and bow requirements are authoritative", WeaponCombat),
            ("Shield mitigation and frontal greatsword parry use authoritative weapon state", WeaponDefenses),
            ("Shield-blocked damage earns no lifesteal credit", ShieldBlockedLifesteal),
            ("Atomic hand swaps survive failed storage, restart and request replay", WeaponPersistence),
            ("Defense charges enforce deterministic priority, directions, damage types, expiry and hit deduplication", DefensePriority),
            ("Absorption competes with mitigation without multiplication and expires", DefenseAbsorption),
            ("Lifesteal uses final HP damage, fractional credit, activation caps and no overheal", LifestealCaps),
            ("Authoritative Evasive Stance dodges one direct hit and retains ordinary mitigation", EvasiveWindow),
            ("Hotbar configuration enforces slot counts, scaling and reserved/unique hotkeys", HotbarConfiguration),
            ("Hotbar swaps and clears never assign an unlearned action or move key bindings", HotbarOwnership),
            ("Hotbar layouts persist independently per character without altering gameplay", HotbarPersistence),
            ("Economy data preserves legacy IDs and validates recipes, equipment and loot probabilities", EconomyDefinitions),
            ("Equipment restrictions, swaps, full bags and replay preserve exact ownership", EquipmentOwnership),
            ("Equipment damage, combat lock and maximum HP apply once without free healing", EquipmentCombat),
            ("All classes combine skill ranks and equipment before defense, without changing timings", EquipmentRankedDamage),
            ("Equipment migration, storage failure, reconnect and restart preserve the aggregate", EquipmentPersistence),
            ("Natural monster deaths roll reachable loot once with contributor ownership and expiry", LootDeaths),
            ("Contribution ties use stable character identity and shared EXP remains unchanged", LootContributionTie),
            ("Pickup enforces protection, range, capacity, atomic storage, races and durable replay", LootPickup),
            ("Every scroll crafts once across stacks without class or level restrictions", CraftRecipes),
            ("Crafting failures, full bags, shared inputs, stale packets and replay are atomic", CraftAtomicity),
            ("Crafting is durable before acknowledgement and restores receipts after restart", CraftPersistence),
            ("Admission, stable IDs, capacity and empty-world continuity", Admission),
            ("Movement speed, invalid coordinates and blocked destinations", Movement),
            ("Navigation routes around walls without cutting corners", Navigation),
            ("Separated regions are unreachable; mobility cannot tunnel", Unreachable),
            ("Selection is separate from explicit basic attacks", Targeting),
            ("Ability acceptance validates range and obstructed sight", AbilityEligibility),
            ("Basic attack interval, pursuit, manual cancel and leash", Pursuit),
            ("Safe zones reject either protected participant", Protection),
            ("Entering a safe zone during a cast prevents damage", SafeBoundary),
            ("All classes cancel primary windups on movement and retain cooldown", MoveCancels),
            ("Ordinary damage does not interrupt casting", DamageDoesNotInterrupt),
            ("All interrupts cancel before completion without silence", InterruptBefore),
            ("Interrupt at/after completion cannot undo damage", InterruptAfter),
            ("Range is checked again at completion", RangeRechecked),
            ("Line of sight is checked again at completion", SightRechecked),
            ("Simultaneous lethal attacks trade, independent of join order", Simultaneous),
            ("Target death cancels a pending cast", TargetDeath),
            ("Two attackers can damage the same target", MultipleAttackers),
            ("All defenses use deterministic nonstacking reduction", Defenses),
            ("Death clears effects, target and intentions; respawn is protected", Respawn),
            ("All mobility skills respect distance and collision", Mobility),
            ("Backstep uses facing when no target is selected", BackstepFacing),
            ("Mobility has fixed endpoints, no homing, and retained cooldown on cancel", MobilityCommitment),
            ("Defensive skills can be used while moving", MovingDefense),
            ("Primary skills reject moving casters", StationarySkills),
            ("Duplicate, reordered, delayed and rapid commands cannot repeat damage", Commands),
            ("Disconnect cancels casts and reconnect gets a fresh entity", Reconnect),
            ("Class selection locks before combat and exploration", ClassSelection),
            ("Cooldown, effects and recovery use server simulation time", Timers),
            ("Snapshot copies and JSON round trips preserve state", Snapshots),
            ("Telemetry is bounded and records combat/rejections", Telemetry),
            ("Invalid time steps fail", InvalidTime),
            ("Woodvale manifest and authored map retain existing world landmarks", ForestContent),
            ("Water and shore block movement and Blink while allowing sight", WaterCollision),
            ("Tree, rock, fence and barrel footprints match authoritative collision", PropCollision),
            ("Monsters idle 1-3 seconds and roam within reachable spawn leashes", MonsterRoaming),
            ("Monster simulation is repeatable and continues with zero clients", MonsterContinuity),
            ("Players damage and respawn monsters through the shared combat pipeline", MonsterCombat),
            ("Clients cannot control monsters and sanctuary protection includes PvE", MonsterOwnership),
            ("Monster spawns reject water, protection and unreachable islands", InvalidMonsterSpawns),
            ("Animation manifest covers every class direction, monster and skill effect", AnimationDefinitions),
            ("EXP thresholds, overflow, cap and exactly one skill point per level", ProgressionLevels),
            ("Skill rank gates, point conservation and duplicate upgrade prevention", ProgressionUpgrades),
            ("All twelve rank tables affect actual ability definitions", ProgressionRankValues),
            ("Malformed progression records are rejected and reset refunds earned points", ProgressionValidation),
            ("Monster EXP splits effective damage with deterministic integer remainders", ExperienceShares),
            ("EXP eligibility enforces window, contribution threshold, life and distance", ExperienceEligibility),
            ("Level penalties apply after sharing with consistent integer rounding", ExperiencePenalties),
            ("Natural monster kills level characters once per life without PvP rewards", ExperienceCombat),
            ("Two players receive shared EXP from the existing combat pipeline", ExperienceMultiplayer),
            ("Rank upgrades freeze accepted casts and reject stale or replayed requests", RankedCombat),
            ("Ranked mobility, interrupts and defenses change authoritative combat for every class", RankedCombatEffects),
            ("Stable character records restore progression across new world entity IDs", ProgressionReconnect),
            ("Development saves persist exact progress across repository restarts", CharacterPersistence),
            ("Character saves reject corruption, rollback and concurrent server writers", CharacterSaveValidation),
            ("Inventory starts empty with configurable capacity and validates catalog", InventoryCatalog),
            ("Inventory additions fill compatible stacks then ascending free slots", InventoryAddition),
            ("Full inventory and failed exchanges preserve all original items", InventoryAtomicCapacity),
            ("Move, swap, split and merge preserve identities, metadata and totals", InventorySlotOperations),
            ("Equipment and incompatible metadata never merge", InventoryCompatibility),
            ("Atomic removals check multiple stacks and item types", InventoryRemoval),
            ("Reward/removal retries are idempotent and stale operations fail safely", InventoryRetries),
            ("Deterministic sort preserves every owned instance", InventorySort),
            ("Malformed saved slots and immutable ownership boundaries are enforced", InventoryCorruption),
            ("Inventory ownership, stale concurrent requests and progression coexist", InventoryOwnership),
            ("Inventory and metadata survive reconnect, restart and duplicate server operations", InventoryPersistence),
            ("Legacy progression saves migrate once without starter-item duplication", InventoryMigration),
            ("Inventory corrupt saves and stale aggregate writes are rejected", InventorySaveValidation),
            ("Cast visuals survive ordinary damage and stop on confirmed cancellation", AnimationCancellation),
            ("Confirmed releases produce deduplicated cosmetic travel without delaying damage", AnimationProjectiles),
            ("Defense visuals reconcile late joins, expiry and reordered snapshots", AnimationDefense),
            ("Mobility effects use authoritative origins and bounded server motion", AnimationMobility),
            ("Death holds its final frame and respawn resets player and monster visuals", AnimationDeath),
            ("All twelve skills route to their supplied effects without changing balance", AnimationAllSkills),
            ("Configuration defaults bind only to loopback", Defaults),
            ("CLI configuration overrides environment", ConfigurationPrecedence),
            ("Invalid configuration fails before starting a peer", InvalidConfiguration)
        };
        foreach (CharacterClass a in Enum.GetValues<CharacterClass>())
            foreach (CharacterClass b in Enum.GetValues<CharacterClass>().Where(b => b >= a))
            {
                CharacterClass first = a, second = b;
                cases.Add(($"{a} vs {b}: basic and primary damage both ways", () => Matchup(first, second)));
            }
        int failed = 0;
        foreach ((string name, Action run) in cases)
        {
            try { run(); Console.WriteLine($"PASS {name}"); }
            catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
        }
        Console.WriteLine($"{cases.Count - failed}/{cases.Count} checks passed.");
        return failed == 0 ? 0 : 1;
    }

    private sealed class Fixture
    {
        public WorldSimulation Game { get; }
        public long A { get; }
        public long B { get; }
        private readonly Dictionary<long, long> _sequences = new();
        public Fixture(CharacterClass a = CharacterClass.Warrior, CharacterClass b = CharacterClass.Warrior,
            bool settle = true, CombatBalance? balance = null, WorldMap? map = null, CharacterRecord? characterA = null,
            CharacterRecord? characterB = null)
        {
            Game = new WorldSimulation(balance, map);
            A = Game.Join(101, a, characterA); B = Game.Join(102, b, characterB);
            Require(A != 0 && B != 0);
            if (settle) Position(new Vector2(480, 448), new Vector2(520, 448));
        }
        public EntitySnapshot State(long id) => Game.GetSnapshots().Single(e => e.Id == id);
        public bool Send(long id, CommandKind kind, long target = 0, AbilitySlot slot = AbilitySlot.Basic, Vector2 point = default,
            CharacterClass characterClass = CharacterClass.Warrior) =>
            Game.Submit(id, new WorldCommand(Next(id), Game.Time, kind, target, slot, point, characterClass));
        public long Next(long id) => _sequences[id] = _sequences.GetValueOrDefault(id) + 1;
        public void Step(double seconds)
        {
            int steps = (int)Math.Ceiling(seconds * 60);
            for (int i = 0; i < steps; i++) Game.Advance(seconds / steps);
        }
        public void Position(Vector2 a, Vector2 b)
        {
            Require(Send(A, CommandKind.Move, point: a) && Send(B, CommandKind.Move, point: b));
            Step(6);
            Require(Vector2.Distance(State(A).Position, a) < 0.01 && Vector2.Distance(State(B).Position, b) < 0.01, "Failed to reach test positions");
        }
        public bool Skill(long actor, long target, AbilitySlot slot) => Send(actor, CommandKind.UseAbility, target, slot);
    }

    private static void Require(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected an argument error.");
    }
    private static CombatBalance LowHp(int hp) => new() { Classes = Array.AsReadOnly(new CombatBalance().Classes.Select(c => c with { MaxHp = hp }).ToArray()) };

    private static void Admission()
    {
        var game = new WorldSimulation(new CombatBalance { MaxPlayers = 3 });
        Require(game.Join(0) == 0 && game.Join(1) == 0);
        long first = game.Join(55);
        Require(first != 55 && first > 0 && game.Join(55) == 0 && game.Join(56) > 0 && game.Join(57) > 0 && game.Join(58) == 0);
        Require(game.Leave(55) && game.Leave(56) && game.Leave(57));
        game.Advance(1);
        Require(game.Time == 1 && game.Join(55) > first && game.PlayerCount == 1);
    }

    private static void Movement()
    {
        var f = new Fixture(settle: false);
        Vector2 start = f.State(f.A).Position;
        Require(f.Send(f.A, CommandKind.Move, point: start + new Vector2(200, 0)));
        f.Step(0.5);
        Require(Math.Abs(f.State(f.A).Position.X - start.X - 84.8f) < 0.01);
        foreach (Vector2 invalid in new[] { new Vector2(float.NaN, 0), new Vector2(float.PositiveInfinity, 1), new Vector2(-1, 1), new Vector2(650, 400) })
            Require(!f.Send(f.A, CommandKind.Move, point: invalid));
        Require(!f.Send(999, CommandKind.Move, point: start));
    }

    private static void Navigation()
    {
        var f = new Fixture();
        Require(f.Send(f.A, CommandKind.Move, point: new Vector2(800, 448)));
        Vector2 previous = f.State(f.A).Position;
        for (int i = 0; i < 480; i++)
        {
            f.Step(1.0 / 60);
            Vector2 current = f.State(f.A).Position;
            Require(f.Game.Map.CanTraverse(previous, current), "Movement tunneled through geometry");
            Require(Vector2.Distance(previous, current) <= 169.6f / 60 + 0.01f);
            previous = current;
        }
        Require(Vector2.Distance(previous, new Vector2(800, 448)) < 0.01f);
    }

    private static void Unreachable()
    {
        var map = new WorldMap(new WorldDefinition(960, 640, 32, 12,
            Array.AsReadOnly(new[] { new WorldRect(448, 0, 64, 640) }),
            Array.AsReadOnly(new[] { new WorldRect(32, 32, 100, 100), new WorldRect(800, 32, 100, 100) }),
            Array.AsReadOnly(new[] { new Vector2(80, 80), new Vector2(850, 80) })));
        var f = new Fixture(settle: false, map: map);
        f.Position(new Vector2(400, 320), new Vector2(560, 320));
        Require(map.FindPath(f.State(f.A).Position, f.State(f.B).Position) is null);
        Require(!f.Send(f.A, CommandKind.Attack, f.B));
        Require(f.Skill(f.A, f.B, AbilitySlot.Mobility));
        f.Step(0.21);
        Require(f.State(f.A).Position.X < 436.01f);
        Require(!f.Skill(f.A, f.B, AbilitySlot.Interrupt));
    }

    private static void Targeting()
    {
        var f = new Fixture();
        Require(!f.Send(f.A, CommandKind.SelectTarget, f.A) && !f.Send(f.A, CommandKind.SelectTarget, 999));
        Require(f.Send(f.A, CommandKind.SelectTarget, f.B));
        f.Step(2);
        Require(f.State(f.B).Hp == 125 && !f.State(f.A).AutoAttacking);
        Require(f.Send(f.A, CommandKind.Attack, f.B));
        f.Step(0.19);
        Require(f.State(f.B).Hp == 125);
        f.Step(0.02);
        Require(f.State(f.B).Hp == 116);
    }

    private static void Pursuit()
    {
        var f = new Fixture();
        f.Position(new Vector2(400, 640), new Vector2(620, 640));
        Require(f.Send(f.A, CommandKind.Attack, f.B));
        f.Step(2);
        Require(f.State(f.B).Hp < 125 && Vector2.Distance(f.State(f.A).Position, f.State(f.B).Position) <= 51.2f);
        Require(f.Send(f.A, CommandKind.Move, point: new Vector2(300, 640)));
        f.Step(0.3);
        Require(!f.State(f.A).AutoAttacking && !f.State(f.A).Casting);
        f.Position(new Vector2(480, 640), new Vector2(520, 640));
        Require(f.Send(f.A, CommandKind.Attack, f.B));
        f.Step(0.21);
        int hp = f.State(f.B).Hp;
        f.Step(0.99);
        Require(f.State(f.B).Hp == hp);
        f.Step(0.3);
        Require(f.State(f.B).Hp == hp - 9);
        f.Send(f.B, CommandKind.Move, point: new Vector2(1500, 640));
        f.Step(6);
        Require(!f.State(f.A).AutoAttacking, "Pursuit must stop at its origin leash");
    }

    private static void AbilityEligibility()
    {
        var melee = new Fixture();
        melee.Position(new Vector2(400, 640), new Vector2(500, 640));
        Require(!melee.Skill(melee.A, melee.B, AbilitySlot.Primary));
        Require(!melee.Skill(melee.A, melee.B, AbilitySlot.Interrupt));
        var ranged = new Fixture(CharacterClass.Mage);
        ranged.Position(new Vector2(600, 448), new Vector2(770, 448));
        Require(!ranged.Skill(ranged.A, ranged.B, AbilitySlot.Primary));
        Require(!ranged.Skill(ranged.A, ranged.B, AbilitySlot.Interrupt));
        Require(ranged.State(ranged.A).Cooldowns.Primary == 0 && ranged.State(ranged.A).Cooldowns.Interrupt == 0);
        ranged.Position(new Vector2(400, 640), new Vector2(800, 640));
        Require(!ranged.Skill(ranged.A, ranged.B, AbilitySlot.Primary));
        Require(!ranged.Send(ranged.A, CommandKind.Attack, ranged.B));
    }

    private static void Protection()
    {
        var f = new Fixture(settle: false);
        Require(f.Send(f.A, CommandKind.SelectTarget, f.B));
        Require(!f.Send(f.A, CommandKind.Attack, f.B) && !f.Skill(f.A, f.B, AbilitySlot.Primary));
        f.Send(f.B, CommandKind.Move, point: new Vector2(410, 340));
        f.Step(3);
        Require(!f.Send(f.A, CommandKind.Attack, f.B) && !f.Send(f.B, CommandKind.Attack, f.A));
        Require(f.State(f.A).Hp == 125 && f.State(f.B).Hp == 125);
    }

    private static void SafeBoundary()
    {
        var f = new Fixture(CharacterClass.Mage, CharacterClass.Mage);
        f.Position(new Vector2(410, 400), new Vector2(410, 365));
        Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
        Require(f.Send(f.B, CommandKind.Move, point: new Vector2(370, 365)));
        f.Step(1.1);
        Require(f.State(f.B).Protected && f.State(f.B).Hp == 100);
        Require(!f.Send(f.A, CommandKind.Attack, f.B));
        f.Send(f.B, CommandKind.Move, point: new Vector2(410, 365));
        f.Step(1);
        Require(f.Send(f.A, CommandKind.Attack, f.B));
    }

    private static void Matchup(CharacterClass a, CharacterClass b)
    {
        var f = new Fixture(a, b);
        foreach ((long actor, long target, CharacterClass kind) in new[] { (f.A, f.B, a), (f.B, f.A, b) })
        {
            ClassDefinition data = f.Game.Balance.Class(kind);
            int hp = f.State(target).Hp;
            Require(f.Skill(actor, target, AbilitySlot.Primary));
            f.Step(data.Ability(AbilitySlot.Primary).CastSeconds - 0.01);
            Require(f.State(target).Hp == hp);
            f.Step(0.02);
            Require(f.State(target).Hp == hp - data.Ability(AbilitySlot.Primary).Damage);
            f.Step(0.26);
            hp = f.State(target).Hp;
            Require(f.Send(actor, CommandKind.Attack, target));
            f.Step(0.21);
            Require(f.State(target).Hp == hp - data.Ability(AbilitySlot.Basic).Damage);
            f.Send(actor, CommandKind.Stop);
            f.Step(0.3);
        }
    }

    private static void MoveCancels()
    {
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
        {
            var f = new Fixture(kind);
            Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
            f.Step(0.2);
            Require(f.Send(f.A, CommandKind.Move, point: f.State(f.A).Position + new Vector2(-10, 0)));
            f.Step(1.2);
            Require(f.State(f.B).Hp == 125 && !f.State(f.A).Casting && f.State(f.A).Cooldowns.Primary > f.Game.Time);
            Require(!f.Skill(f.A, f.B, AbilitySlot.Primary));
        }
    }

    private static void DamageDoesNotInterrupt()
    {
        var f = new Fixture(CharacterClass.Mage);
        Require(f.Skill(f.A, f.B, AbilitySlot.Primary) && f.Send(f.B, CommandKind.Attack, f.A));
        f.Step(0.21);
        Require(f.State(f.A).Hp == 91 && f.State(f.A).Casting);
        f.Send(f.B, CommandKind.Stop);
        f.Step(0.8);
        Require(f.State(f.B).Hp == 105);
    }

    private static void InterruptBefore()
    {
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
        {
            var f = new Fixture(CharacterClass.Mage, kind);
            Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
            f.Step(0.99);
            Require(f.Skill(f.B, f.A, AbilitySlot.Interrupt));
            Require(!f.State(f.A).Casting && f.State(f.A).Hp == 95);
            f.Step(0.05);
            Require(f.State(f.B).Hp == f.State(f.B).MaxHp);
            Require(f.Skill(f.A, f.B, AbilitySlot.Defense), "Interrupt must not silence");
            Require(f.State(f.A).Cooldowns.Primary > f.Game.Time);
        }
    }

    private static void InterruptAfter()
    {
        foreach (double elapsed in new[] { 1.0, 1.02 })
        {
            var f = new Fixture(CharacterClass.Mage);
            Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
            f.Step(elapsed);
            Require(f.Skill(f.B, f.A, AbilitySlot.Interrupt));
            Require(f.State(f.B).Hp == 105 && f.State(f.A).Hp == 95);
            Require(!f.Game.RecentActivity.Any(e => e.Kind == CombatEventKind.CastInterrupted));
        }
    }

    private static void RangeRechecked()
    {
        var f = new Fixture(CharacterClass.Mage, CharacterClass.Mage);
        f.Position(new Vector2(480, 640), new Vector2(680, 640));
        Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
        f.Send(f.B, CommandKind.Move, point: new Vector2(900, 640));
        f.Step(1.1);
        Require(f.State(f.B).Hp == 100);
        var melee = new Fixture();
        Require(melee.Send(melee.A, CommandKind.Attack, melee.B));
        melee.Send(melee.B, CommandKind.Move, point: new Vector2(600, 448));
        melee.Step(0.21);
        Require(melee.State(melee.B).Hp == 125, "Melee must recheck range during windup");
    }

    private static void SightRechecked()
    {
        var f = new Fixture(CharacterClass.Mage, CharacterClass.Mage);
        f.Position(new Vector2(610, 640), new Vector2(770, 640));
        Require(f.Game.Map.HasLineOfSight(f.State(f.A).Position, f.State(f.B).Position));
        Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
        f.Send(f.B, CommandKind.Move, point: new Vector2(770, 480));
        f.Step(1.01);
        Require(!f.Game.Map.HasLineOfSight(f.State(f.A).Position, f.State(f.B).Position));
        Require(f.State(f.B).Hp == 100);
    }

    private static void Simultaneous()
    {
        foreach (bool reverse in new[] { false, true })
        {
            var f = new Fixture(balance: LowHp(9));
            long first = reverse ? f.B : f.A, second = reverse ? f.A : f.B;
            Require(f.Send(first, CommandKind.Attack, second) && f.Send(second, CommandKind.Attack, first));
            f.Step(0.21);
            Require(f.State(f.A).Hp == 0 && f.State(f.B).Hp == 0);
            Require(f.Game.RecentActivity.Count(e => e.Kind == CombatEventKind.Died) == 2);
        }
    }

    private static void TargetDeath()
    {
        var f = new Fixture(CharacterClass.Mage, balance: LowHp(5));
        long c = f.Game.Join(103);
        f.Send(c, CommandKind.Move, point: new Vector2(550, 448)); f.Step(4);
        Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
        Require(f.Skill(c, f.B, AbilitySlot.Interrupt));
        f.Step(1.1);
        Require(f.State(f.B).Hp == 0 && !f.State(f.A).Casting);
        Require(!f.Game.RecentActivity.Any(e => e.Kind == CombatEventKind.Damage && e.ActorId == f.A));
    }

    private static void MultipleAttackers()
    {
        var f = new Fixture();
        long c = f.Game.Join(103);
        f.Send(c, CommandKind.Move, point: new Vector2(550, 448)); f.Step(4);
        Require(f.Send(f.A, CommandKind.Attack, f.B) && f.Send(c, CommandKind.Attack, f.B));
        f.Step(0.21);
        Require(f.State(f.B).Hp == 107);
    }

    private static void Defenses()
    {
        Require(CombatRules.Damage(20, 0.35f) == 13 && CombatRules.Damage(9, 0.35f) == 6 && CombatRules.Damage(5, 0.35f) == 3);
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
        {
            var f = new Fixture(CharacterClass.Mage, kind);
            Require(f.Skill(f.B, 0, AbilitySlot.Defense));
            Require(!f.Skill(f.B, 0, AbilitySlot.Defense));
            Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
            f.Step(1.01);
            Require(f.State(f.B).Hp == f.State(f.B).MaxHp - 13);
            f.Step(1.1);
            Require(f.State(f.B).DefenseEndsAt == 0);
        }
    }

    private static void Respawn()
    {
        var f = new Fixture(balance: LowHp(5));
        Require(f.Send(f.B, CommandKind.SelectTarget, f.A));
        Require(f.Skill(f.B, 0, AbilitySlot.Defense));
        Require(f.Send(f.A, CommandKind.Attack, f.B));
        f.Step(0.21);
        EntitySnapshot dead = f.State(f.B);
        Require(!dead.Alive && dead.DefenseEndsAt == 0 && dead.TargetId == 0 && !dead.AutoAttacking && !dead.Casting);
        Require(!f.Send(f.B, CommandKind.Move, point: new Vector2(500, 500)) && !f.Skill(f.B, f.A, AbilitySlot.Primary));
        Require(!f.State(f.A).AutoAttacking && f.State(f.A).TargetId == 0);
        f.Step(2.9); Require(!f.State(f.B).Alive);
        f.Step(0.12); Require(f.State(f.B).Alive && f.State(f.B).Protected && f.State(f.B).Hp == 5);
    }

    private static void Mobility()
    {
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
        {
            var f = new Fixture(kind);
            f.Position(new Vector2(600, 448), new Vector2(560, 448));
            // Rush targets the far side of a wall; Blink aims through it; Backstep moves away from target into it.
            if (kind == CharacterClass.Warrior) f.Position(new Vector2(600, 448), new Vector2(800, 448));
            f.Send(f.A, CommandKind.SelectTarget, f.B);
            Vector2 start = f.State(f.A).Position;
            Require(f.Send(f.A, CommandKind.UseAbility, f.B, AbilitySlot.Mobility, new Vector2(900, 448)));
            if (kind != CharacterClass.Mage) Require(f.State(f.A).Position == start, "Dash must travel over time");
            f.Step(0.21);
            Vector2 end = f.State(f.A).Position;
            Require(f.Game.Map.CanTraverse(start, end) && end.X < 628.01f && end.X > start.X);
            Require(Vector2.Distance(start, end) <= f.Game.Balance.Class(kind).Ability(AbilitySlot.Mobility).Range);
            Require(f.State(f.B).Hp == 125);
        }
    }

    private static void BackstepFacing()
    {
        var f = new Fixture(CharacterClass.Archer);
        f.Send(f.A, CommandKind.Move, point: new Vector2(500, 448)); f.Step(0.2);
        Vector2 before = f.State(f.A).Position;
        Require(f.Skill(f.A, 0, AbilitySlot.Mobility));
        f.Step(0.21);
        Require(Math.Abs(before.X - f.State(f.A).Position.X - 102.4f) < 0.01f);
    }

    private static void MovingDefense()
    {
        var f = new Fixture();
        f.Send(f.A, CommandKind.Move, point: new Vector2(600, 448));
        Require(f.Skill(f.A, 0, AbilitySlot.Defense));
        float before = f.State(f.A).Position.X;
        f.Step(0.1);
        Require(f.State(f.A).Position.X > before && f.State(f.A).DefenseEndsAt > f.Game.Time);
    }

    private static void MobilityCommitment()
    {
        var f = new Fixture(CharacterClass.Warrior, CharacterClass.Mage);
        f.Position(new Vector2(400, 640), new Vector2(550, 640));
        Require(f.Skill(f.A, f.B, AbilitySlot.Mobility));
        Require(f.Send(f.B, CommandKind.UseAbility, slot: AbilitySlot.Mobility, point: new Vector2(750, 640)));
        f.Step(0.21);
        Require(Math.Abs(f.State(f.A).Position.X - 526) < 0.01f, "Rush should retain its original endpoint");
        Require(Vector2.Distance(f.State(f.A).Position, f.State(f.B).Position) > 51.2f, "Rush should not guarantee connection");
        Require(f.State(f.B).Hp == 100);

        var cancel = new Fixture(CharacterClass.Archer);
        Require(cancel.Skill(cancel.A, 0, AbilitySlot.Mobility));
        cancel.Step(0.05);
        Vector2 position = cancel.State(cancel.A).Position;
        Require(cancel.Send(cancel.A, CommandKind.Stop));
        cancel.Step(0.5);
        Require(cancel.State(cancel.A).Position == position && cancel.State(cancel.A).Cooldowns.Mobility > cancel.Game.Time);
        Require(!cancel.Skill(cancel.A, 0, AbilitySlot.Mobility));
    }

    private static void StationarySkills()
    {
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
        {
            var f = new Fixture(kind);
            f.Send(f.A, CommandKind.Move, point: new Vector2(600, 448));
            Require(!f.Skill(f.A, f.B, AbilitySlot.Primary));
            Require(f.State(f.A).Cooldowns.Primary == 0);
        }
    }

    private static void Commands()
    {
        var f = new Fixture(CharacterClass.Mage);
        long seq = f.Next(f.A);
        var command = new WorldCommand(seq, f.Game.Time, CommandKind.UseAbility, f.B, AbilitySlot.Primary);
        Require(f.Game.Submit(f.A, command));
        Require(!f.Game.Submit(f.A, command) && !f.Game.Submit(f.A, command with { Sequence = seq - 1 }));
        Require(!f.Game.Submit(f.A, command with { Sequence = f.Next(f.A), ObservedServerTime = f.Game.Time - 3 }));
        Require(!f.Game.Submit(f.A, command with { Sequence = f.Next(f.A), ObservedServerTime = double.NaN }));
        for (int i = 0; i < 80; i++) Require(!f.Skill(f.A, f.B, AbilitySlot.Primary));
        f.Step(1.01);
        Require(f.State(f.B).Hp == 105 && f.Game.RecentActivity.Count(e => e.Kind == CombatEventKind.CastStarted) == 1);
        Require(!f.Game.Submit(999, command));
        Require(!f.Send(f.A, (CommandKind)99));
        Require(!f.Send(f.A, CommandKind.UseAbility, f.B, (AbilitySlot)99));
    }

    private static void Reconnect()
    {
        var f = new Fixture(CharacterClass.Mage);
        Require(f.Skill(f.A, f.B, AbilitySlot.Primary));
        Require(f.Game.Leave(102));
        f.Step(1.1);
        Require(!f.State(f.A).Casting && f.Game.PlayerCount == 1);
        long replacement = f.Game.Join(102, CharacterClass.Archer);
        Require(replacement != f.B && f.State(replacement).Hp == 110 && f.State(replacement).Protected);
        Require(!f.Send(f.A, CommandKind.Attack, f.B));
    }

    private static void ClassSelection()
    {
        var f = new Fixture(settle: false);
        Require(f.Send(f.A, CommandKind.ChooseClass, characterClass: CharacterClass.Mage));
        Require(f.State(f.A).Hp == 100);
        f.Send(f.A, CommandKind.Move, point: new Vector2(480, 448)); f.Step(4);
        Require(!f.Send(f.A, CommandKind.ChooseClass, characterClass: CharacterClass.Warrior));
        f.Send(f.A, CommandKind.Move, point: new Vector2(160, 160)); f.Step(4);
        Require(!f.Send(f.A, CommandKind.ChooseClass, characterClass: CharacterClass.Archer));
    }

    private static void Timers()
    {
        var f = new Fixture();
        Require(f.Skill(f.A, 0, AbilitySlot.Defense));
        double cooldown = f.State(f.A).Cooldowns.Defense;
        Require(!f.Skill(f.A, f.B, AbilitySlot.Interrupt));
        f.Step(0.26);
        Require(f.Skill(f.A, f.B, AbilitySlot.Interrupt));
        f.Step(1.75);
        Require(f.State(f.A).DefenseEndsAt == 0 && f.State(f.A).Cooldowns.Defense == cooldown);
        Require(!f.Skill(f.A, 0, AbilitySlot.Defense));
        f.Step(14.1); Require(f.Skill(f.A, 0, AbilitySlot.Defense));
    }

    private static void Snapshots()
    {
        var f = new Fixture(CharacterClass.Mage);
        f.Skill(f.A, f.B, AbilitySlot.Primary);
        EntitySnapshot copy = f.State(f.A) with { Hp = 0 };
        Require(copy.Hp == 0 && f.State(f.A).Hp == 100);
        var state = new WorldSnapshot(12, f.Game.Time, f.Game.GetSnapshots().ToArray());
        var options = new JsonSerializerOptions { IncludeFields = true };
        WorldSnapshot? restored = JsonSerializer.Deserialize<WorldSnapshot>(JsonSerializer.Serialize(state, options), options);
        Require(restored is not null && restored.Entities.SequenceEqual(state.Entities));
    }

    private static void Telemetry()
    {
        var f = new Fixture(balance: new CombatBalance { TelemetryCapacity = 32 });
        f.Send(f.A, CommandKind.Attack, f.B); f.Step(0.21); f.Send(f.A, CommandKind.Stop); f.Step(5.2);
        Require(f.Game.RecentActivity.Any(e => e.Kind == CombatEventKind.CombatStarted) && f.Game.RecentActivity.Any(e => e.Kind == CombatEventKind.CombatEnded));
        for (int i = 0; i < 60; i++) { f.Send(f.A, CommandKind.SelectTarget, 999); f.Step(0.1); }
        Require(f.Game.RecentActivity.Count() == 32 && f.Game.RecentActivity.Last().Kind == CombatEventKind.Rejected);
        Require(f.Game.DrainEvents().Length <= 32 && f.Game.DrainEvents().Length == 0);
    }

    private static void InvalidTime()
    {
        var game = new WorldSimulation();
        foreach (double delta in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, 2.0 }) Throws(() => game.Advance(delta));
    }

    private static void ForestContent()
    {
        WorldDefinition world = WorldMap.Outdoor;
        WorldArt art = world.Art ?? throw new InvalidOperationException("Art data is missing");
        Require(world.Width == 1920 && world.Height == 1280 && world.TileSize == 32);
        Require(world.Spawns[0] == new Vector2(144, 160) && world.ProtectedRegions[0] == new WorldRect(32, 32, 352, 352));
        Require(art.Characters.Count == 3 && art.Characters.All(c => c.Directions.Count == 4));
        Require(art.Monsters.Count == 4 && art.Tiles.Count == 24 && art.Cells.Count == 60 * 40);
        Require(art.Tiles.Single(t => t.Id == "shore_n") is { AtlasX: 0, AtlasY: 3, Walkable: false });
        Require(art.RockFormations.Contains(new WorldRect(640, 224, 96, 384)));
        Require(world.MonsterSpawns.Select(m => m.Species).Distinct().Count() == 4);
        foreach (TerrainTile tile in art.Tiles.Where(t => !t.Walkable))
            foreach (TerrainCell cell in art.Cells.Where(c => c.TileId == tile.Id))
                Require(world.Water.Any(r => r.Contains(new Vector2((cell.X + 0.5f) * 32, (cell.Y + 0.5f) * 32))));
    }

    private static void WaterCollision()
    {
        var f = new Fixture(CharacterClass.Mage);
        f.Position(new Vector2(520, 192), new Vector2(400, 192));
        Require(!f.Game.Map.CanTraverse(new Vector2(520, 192), new Vector2(520, 416)));
        Require(f.Game.Map.HasLineOfSight(new Vector2(520, 192), new Vector2(520, 416)));
        Require(!f.Send(f.A, CommandKind.Move, point: new Vector2(520, 272)));
        Require(!f.Send(f.A, CommandKind.Move, point: new Vector2(464, 240)), "The shoreline is blocked too");
        Require(f.Send(f.A, CommandKind.UseAbility, slot: AbilitySlot.Mobility, point: new Vector2(520, 416)));
        Require(f.State(f.A).Position.Y < 212.1f && f.Game.Map.IsWalkable(f.State(f.A).Position));
        Require(f.Send(f.A, CommandKind.Move, point: new Vector2(520, 416)));
        Vector2 previous = f.State(f.A).Position;
        for (int step = 0; step < 480; step++)
        {
            f.Step(1.0 / 60);
            Vector2 current = f.State(f.A).Position;
            Require(f.Game.Map.CanTraverse(previous, current)); previous = current;
        }
        Require(Vector2.Distance(previous, new Vector2(520, 416)) < 0.01f);
    }

    private static void PropCollision()
    {
        var map = new WorldMap();
        WorldArt art = map.Definition.Art!;
        foreach (PropPlacement prop in art.Props)
        {
            PropAsset asset = art.PropAssets.Single(a => a.Id == prop.AssetId);
            if (asset.Footprint is not WorldRect footprint) continue;
            Vector2 center = prop.Position + new Vector2(footprint.X + footprint.Width / 2, footprint.Y + footprint.Height / 2);
            Require(!map.IsWalkable(center), $"Missing collision for {prop.Id}");
            Require(!map.HasLineOfSight(center, center));
        }
        Require(map.IsWalkable(new Vector2(340, 218)), "Decorative flowers must not create walls");
    }

    private static void MonsterRoaming()
    {
        var game = new WorldSimulation();
        EntitySnapshot[] initial = game.GetSnapshots().Where(e => e.Kind == EntityKind.Monster).ToArray();
        Require(game.PlayerCount == 0 && game.MonsterCount == WorldMap.Outdoor.MonsterSpawns.Count && initial.All(e => e.Wander == WanderState.Idle));
        var homes = initial.ToDictionary(e => e.Id, e => game.Map.Definition.MonsterSpawns.Single(m => m.Position == e.Position));
        var previous = initial.ToDictionary(e => e.Id);
        var idleStarted = initial.ToDictionary(e => e.Id, _ => 0.0);
        var starts = initial.ToDictionary(e => e.Id, _ => 0);
        const double delta = 1.0 / 30;
        for (int tick = 0; tick < 3600; tick++)
        {
            game.Advance(delta);
            foreach (EntitySnapshot monster in game.GetSnapshots())
            {
                MonsterSpawn home = homes[monster.Id];
                EntitySnapshot old = previous[monster.Id];
                Require(game.Map.CanTraverse(old.Position, monster.Position), "Monster clipped an obstacle or water");
                Require(!game.Map.CrossesProtection(old.Position, monster.Position), "Monster entered sanctuary");
                Require(Vector2.Distance(monster.Position, home.Position) <= home.RoamRadius + 0.01f, "Monster left its leash");
                Require(Vector2.Distance(monster.Position, old.Position) <= home.MoveSpeed * delta + 0.01f, "Monster teleported or exceeded speed");
                if (old.Wander == WanderState.Idle && monster.Wander == WanderState.Idle) Require(old.Position == monster.Position);
                if (old.Wander != WanderState.Idle && monster.Wander == WanderState.Idle) idleStarted[monster.Id] = game.Time;
                if (old.Wander == WanderState.Idle && monster.Wander != WanderState.Idle)
                {
                    double pause = game.Time - idleStarted[monster.Id];
                    Require(pause >= 1 - delta && pause <= 3 + delta, $"Invalid idle pause: {pause}");
                    starts[monster.Id]++;
                }
                previous[monster.Id] = monster;
            }
            game.DrainEvents();
        }
        Require(starts.Values.All(count => count >= 2), "Every species must complete multiple wandering cycles");
    }

    private static void MonsterContinuity()
    {
        var first = new WorldSimulation();
        var second = new WorldSimulation();
        var initial = first.GetSnapshots().ToArray();
        long player = first.Join(100);
        Require(first.Leave(100) && player != 0);
        for (int i = 0; i < 600; i++) { first.Advance(1.0 / 30); second.Advance(1.0 / 30); }
        EntitySnapshot[] current = first.GetSnapshots().ToArray();
        Require(first.PlayerCount == 0 && current.SequenceEqual(second.GetSnapshots()));
        Require(initial.Select(e => e.Id).SequenceEqual(current.Select(e => e.Id)));
        Require(initial.Zip(current).All(pair => pair.First.Position != pair.Second.Position));
        long replacement = first.Join(100);
        Require(replacement > player && first.GetSnapshots().Count(e => e.Kind == EntityKind.Monster) == WorldMap.Outdoor.MonsterSpawns.Count);
    }

    private static Fixture PassiveMonsterFixture()
    {
        var spawn = new MonsterSpawn("test-slime", MonsterSpecies.Slime, new Vector2(520, 448), 7, 0.1f, 64, 1, 3, 3);
        var map = new WorldMap(WorldMap.Outdoor with { MonsterSpawns = Array.AsReadOnly(new[] { spawn }) });
        return new Fixture(CharacterClass.Mage, map: map);
    }

    private static void MonsterCombat()
    {
        var f = PassiveMonsterFixture();
        long monster = f.Game.GetSnapshots().Single(e => e.Kind == EntityKind.Monster).Id;
        Require(f.Send(f.A, CommandKind.SelectTarget, monster));
        Require(f.Skill(f.A, monster, AbilitySlot.Primary));
        f.Step(1.01);
        Require(f.State(monster).Hp == 0 && f.State(monster).Action == ActionState.Dead && f.State(monster).Wander == WanderState.Dead);
        Require(f.State(f.A).TargetId == 0 && !f.State(monster).CanChooseClass);
        Require(f.Game.RecentActivity.Any(e => e.Kind == CombatEventKind.Died && e.TargetId == monster && e.TargetKind == EntityKind.Monster));
        f.Step(2.8); Require(!f.State(monster).Alive);
        f.Step(0.21);
        Require(f.State(monster).Alive && f.State(monster).Hp == 7 && !f.State(monster).Protected);
        Require(f.State(monster).Position == new Vector2(520, 448) && f.State(monster).Wander == WanderState.Idle);
    }

    private static void MonsterOwnership()
    {
        var f = PassiveMonsterFixture();
        long monster = f.Game.GetSnapshots().Single(e => e.Kind == EntityKind.Monster).Id;
        Require(!f.Send(monster, CommandKind.Move, point: new Vector2(700, 700)));
        Require(!f.Send(monster, CommandKind.ChooseClass, characterClass: CharacterClass.Archer));
        f.Send(f.A, CommandKind.Move, point: new Vector2(370, 370)); f.Step(3);
        Require(f.State(f.A).Protected);
        Require(!f.Skill(f.A, monster, AbilitySlot.Primary) && !f.Send(f.A, CommandKind.Attack, monster));
    }

    private static void InvalidMonsterSpawns()
    {
        MonsterSpawn template = WorldMap.Outdoor.MonsterSpawns[0];
        foreach (Vector2 invalid in new[] { new Vector2(520, 272), new Vector2(144, 160), new Vector2(float.NaN, 40) })
            Throws(() => new WorldMap(WorldMap.Outdoor with { MonsterSpawns = Array.AsReadOnly(new[] { template with { Position = invalid } }) }));
        var island = new WorldDefinition(640, 640, 32, 12, Array.AsReadOnly(new[] { new WorldRect(300, 0, 40, 640) }),
            Array.AsReadOnly(new[] { new WorldRect(32, 32, 128, 128) }), Array.AsReadOnly(new[] { new Vector2(80, 80) }))
            { MonsterSpawns = Array.AsReadOnly(new[] { template with { Position = new Vector2(500, 320) } }) };
        Throws(() => new WorldMap(island));
    }
    private static void Defaults()
    {
        SessionOptions options = SessionOptions.Parse(Array.Empty<string>(), _ => null);
        Require(!options.IsServer && options.Host == "127.0.0.1" && options.BindAddress == "127.0.0.1" && options.Port == 7777);
    }
    private static void ConfigurationPrecedence()
    {
        var environment = new Dictionary<string, string> { ["ARVENOK_PORT"] = "9000", ["ARVENOK_REGION"] = "eu-test" };
        SessionOptions options = SessionOptions.Parse(new[] { "--server", "--port", "8888", "--bind", "0.0.0.0" }, name => environment.GetValueOrDefault(name));
        Require(options.IsServer && options.Port == 8888 && options.Region == "eu-test" && options.BindAddress == "0.0.0.0");
    }
    private static void InvalidConfiguration()
    {
        foreach (string[] arguments in new[]
        {
            new[] { "--typo" }, new[] { "--port" }, new[] { "--port", "--server" }, new[] { "--smoke-role", "invalid" },
            new[] { "--port", "0" }, new[] { "--port", "65536" }, new[] { "--port", "NaN" },
            new[] { "--port", "7777", "--port", "8888" }, new[] { "--server", "--server" },
            new[] { "--bind", "not-an-ip" }, new[] { "--host", "bad host" }, new[] { "--region", "bad\nlabel" }, new[] { "--shard", "" }
        }) Throws(() => SessionOptions.Parse(arguments, _ => null));
    }
}
