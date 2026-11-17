using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Arvenok.Contracts;
using Arvenok.Hosting;
using Arvenok.Simulation;

namespace Arvenok.Tests;

// Package-free executable regression suite: failures return a nonzero exit code in local runs and CI.
internal static class Program
{
    private static int Main()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("Admission rejects server IDs, duplicates and capacity overflow", Admission),
            ("Movement rejects unknown players and nonfinite input", InvalidMovement),
            ("Movement respects speed and arena boundaries", Movement),
            ("Attacks reject self, unknown and out-of-range targets", InvalidAttacks),
            ("Melee damage and cooldown are authoritative", Melee),
            ("Spell completes after cast time and prevents movement", Spell),
            ("Moving cancels a cast", MoveInterrupts),
            ("Damage interrupts a cast", DamageInterrupts),
            ("Spell rechecks range at completion", RangeRechecked),
            ("Dead fighters cannot act and respawn on simulation time", Respawn),
            ("Departing targets cannot receive pending spells", DepartingTarget),
            ("Snapshots cannot mutate server state", SnapshotIsolation),
            ("Simulation rejects invalid elapsed time", InvalidTime),
            ("Configuration defaults bind only to loopback", Defaults),
            ("CLI configuration overrides environment", ConfigurationPrecedence),
            ("Invalid configuration fails before starting a peer", InvalidConfiguration)
        };
        int failed = 0;
        foreach ((string name, Action run) in cases)
        {
            try { run(); Console.WriteLine($"PASS {name}"); }
            catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
        }
        Console.WriteLine($"{cases.Length - failed}/{cases.Length} checks passed.");
        return failed == 0 ? 0 : 1;
    }

    private static ArenaSimulation Duel(bool adjacent = false)
    {
        var game = new ArenaSimulation();
        Require(game.Join(2) && game.Join(3));
        if (adjacent)
        {
            game.RequestMove(3, new Vector2(300, 320));
            Advance(game, 3);
        }
        return game;
    }

    private static FighterSnapshot Fighter(ArenaSimulation game, long id) => game.GetSnapshots().Single(f => f.Id == id);
    private static void Advance(ArenaSimulation game, double seconds)
    {
        int steps = (int)Math.Ceiling(seconds * 60);
        for (int index = 0; index < steps; index++) game.Advance(seconds / steps);
    }
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
    private static void Throws(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected an argument error.");
    }

    private static void Admission()
    {
        var game = new ArenaSimulation();
        Require(!game.Join(0) && !game.Join(1));
        Require(game.Join(2) && !game.Join(2) && game.Join(3) && !game.Join(4));
        Require(game.Leave(2) && game.Join(4) && game.PlayerCount == 2);
        Require(Fighter(game, 3).Position != Fighter(game, 4).Position);
    }
    private static void InvalidMovement()
    {
        ArenaSimulation game = Duel();
        Require(!game.RequestMove(999, Vector2.Zero));
        Require(!game.RequestMove(2, new Vector2(float.NaN, 0)));
        Require(!game.RequestMove(2, new Vector2(0, float.PositiveInfinity)));
        Advance(game, 1);
        Require(Fighter(game, 2).Position == new Vector2(245, 320));
    }
    private static void Movement()
    {
        ArenaSimulation game = Duel();
        game.RequestMove(2, new Vector2(900, 320));
        Advance(game, 0.5);
        Require(Math.Abs(Fighter(game, 2).Position.X - 337.5f) < 0.01f);
        game.RequestMove(2, new Vector2(-10000, 10000));
        Advance(game, 10);
        Require(Fighter(game, 2).Position == ArenaRules.MinimumPosition with { Y = ArenaRules.MaximumPosition.Y });
    }
    private static void InvalidAttacks()
    {
        ArenaSimulation game = Duel();
        Require(!game.RequestAttack(2, 2, false) && !game.RequestAttack(2, 99, false));
        Require(!game.RequestAttack(99, 2, false) && !game.RequestAttack(2, 3, false) && !game.RequestAttack(2, 3, true));
    }
    private static void Melee()
    {
        ArenaSimulation game = Duel(true);
        Require(game.RequestAttack(2, 3, false) && Fighter(game, 3).Hp == 85);
        Require(!game.RequestAttack(2, 3, false));
        Advance(game, 0.81);
        Require(game.RequestAttack(2, 3, false) && Fighter(game, 3).Hp == 70);
    }
    private static void Spell()
    {
        ArenaSimulation game = Duel(true);
        Require(game.RequestAttack(2, 3, true));
        Vector2 start = Fighter(game, 2).Position;
        Advance(game, 1.2);
        Require(Fighter(game, 2).Casting && Fighter(game, 3).Hp == 100 && Fighter(game, 2).Position == start);
        Advance(game, 0.1);
        Require(!Fighter(game, 2).Casting && Fighter(game, 3).Hp == 70);
        Require(!game.RequestAttack(2, 3, false));
    }
    private static void MoveInterrupts()
    {
        ArenaSimulation game = Duel(true);
        Require(game.RequestAttack(2, 3, true));
        Require(game.RequestMove(2, new Vector2(245, 320)));
        Advance(game, 2);
        Require(!Fighter(game, 2).Casting && Fighter(game, 3).Hp == 100);
    }
    private static void DamageInterrupts()
    {
        ArenaSimulation game = Duel(true);
        Require(game.RequestAttack(2, 3, true) && game.RequestAttack(3, 2, false));
        Advance(game, 2);
        Require(Fighter(game, 2).Hp == 85 && !Fighter(game, 2).Casting && Fighter(game, 3).Hp == 100);
    }
    private static void RangeRechecked()
    {
        ArenaSimulation game = Duel(true);
        Require(game.RequestAttack(2, 3, true));
        game.RequestMove(3, new Vector2(900, 320));
        Advance(game, 1.4);
        Require(Fighter(game, 3).Hp == 100 && !Fighter(game, 2).Casting);
    }
    private static void Respawn()
    {
        ArenaSimulation game = Duel(true);
        for (int hit = 0; hit < 7; hit++)
        {
            Require(game.RequestAttack(2, 3, false));
            if (hit < 6) Advance(game, 0.81);
        }
        Require(Fighter(game, 3).Hp == 0);
        Require(!game.RequestMove(3, Vector2.Zero) && !game.RequestAttack(3, 2, false));
        Advance(game, 2.9);
        Require(Fighter(game, 3).Hp == 0);
        Advance(game, 0.2);
        Require(Fighter(game, 3).Hp == 100);
    }
    private static void DepartingTarget()
    {
        ArenaSimulation game = Duel(true);
        game.RequestAttack(2, 3, true);
        game.Leave(3);
        Advance(game, 2);
        Require(game.PlayerCount == 1 && !Fighter(game, 2).Casting);
    }
    private static void SnapshotIsolation()
    {
        ArenaSimulation game = Duel();
        FighterSnapshot copy = Fighter(game, 2) with { Hp = 0 };
        Require(copy.Hp == 0 && Fighter(game, 2).Hp == 100);
    }
    private static void InvalidTime()
    {
        ArenaSimulation game = Duel();
        foreach (double delta in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, 2.0 })
            Throws(() => game.Advance(delta));
    }
    private static void Defaults()
    {
        SessionOptions options = SessionOptions.Parse(Array.Empty<string>(), _ => null);
        Require(!options.IsServer && options.Host == "127.0.0.1" && options.BindAddress == "127.0.0.1" && options.Port == 7777);
    }
    private static void ConfigurationPrecedence()
    {
        var environment = new Dictionary<string, string> { ["ARVENOK_PORT"] = "9000", ["ARVENOK_REGION"] = "eu-test" };
        SessionOptions options = SessionOptions.Parse(new[] { "--server", "--port", "8888", "--bind", "0.0.0.0" },
            name => environment.GetValueOrDefault(name));
        Require(options.IsServer && options.Port == 8888 && options.Region == "eu-test" && options.BindAddress == "0.0.0.0");
    }
    private static void InvalidConfiguration()
    {
        foreach (string[] arguments in new[]
        {
            new[] { "--typo" }, new[] { "--port" }, new[] { "--port", "--server" },
            new[] { "--port", "0" }, new[] { "--port", "65536" }, new[] { "--port", "NaN" },
            new[] { "--port", "7777", "--port", "8888" }, new[] { "--server", "--server" },
            new[] { "--bind", "not-an-ip" }, new[] { "--host", "bad host" },
            new[] { "--region", "bad\nlabel" }, new[] { "--shard", "" }
        }) Throws(() => SessionOptions.Parse(arguments, _ => null));
    }
}
