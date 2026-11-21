using System;
using System.Linq;
using System.Threading.Tasks;
using Arvenok.Contracts;
using Arvenok.Hosting;
using Arvenok.Simulation;
using Microsoft.Data.Sqlite;

namespace Arvenok.Tests;
internal static partial class Program
{
    private static void AccountOwnership() => WithSaveDirectory(directory =>
    {
        var rules = new CharacterStateRules();
        Guid account, character;
        using (var db = new AccountRepository(directory, rules.Validate, rules.Upgrade))
        {
            account = db.Register("tester_one", "long test password");
            Require(db.Register("TESTER_ONE", "long test password") == account);
            Require(db.Login("tester_one", "wrong password") is null && db.Login("absent_user", "wrong password") is null);
            Require(db.List(account).Length == 0);
            Guid other = db.Register("tester_two", "another long password");
            Guid command = Guid.NewGuid();
            var c = db.Create(account, command, "FirstHero", CharacterClass.Mage); character = c.Id;
            Require(c.Level == 1 && c.Inventory!.Slots.Count == 0 && c.ClassLocked && c.SkillPoints == 0);
            Require(db.Create(account, command, "FirstHero", CharacterClass.Mage).Id == character);
            Throws(() => db.Owned(other, character));
            Throws(() => db.Create(other, Guid.NewGuid(), "firsthero", CharacterClass.Archer));
            Throws(() => db.Create(account, Guid.NewGuid(), "Admin", CharacterClass.Warrior));
            Throws(() => db.Delete(other, Guid.NewGuid(), character, "FirstHero"));
            Throws(() => db.Delete(account, Guid.NewGuid(), character, "firsthero"));
            for (int i = 0; i < 4; i++) db.Create(account, Guid.NewGuid(), "Another" + i, (CharacterClass)(i % 3));
            int successes = 0;
            Parallel.For(0, 2, i => { try { db.Create(account, Guid.NewGuid(), "LastHero" + i, CharacterClass.Warrior); System.Threading.Interlocked.Increment(ref successes); } catch (ArgumentException) { } });
            Require(successes == 1 && db.List(account).Length == 6);
            var moved = c with { WorldPosition = new(480,448), Revision = c.Revision + 1 };
            db.Save(moved); Require(db.Load(c.Id).WorldPosition == moved.WorldPosition);
            Guid delete = Guid.NewGuid(); db.Delete(account, delete, c.Id, c.Nickname); db.Delete(account, delete, c.Id, c.Nickname);
            InvalidSave(() => db.Save(moved));
            Require(db.List(account).Length == 5);
            db.Create(other, Guid.NewGuid(), "FIRSTHERO", CharacterClass.Archer);
            Require(db.List(other).Single().Class == CharacterClass.Archer);
        }
        using var restarted = new AccountRepository(directory, rules.Validate, rules.Upgrade);
        Require(restarted.Login("TESTER_ONE", "long test password") == account && restarted.List(account).Length == 5);
        Throws(() => restarted.Load(character));
    });
    private static void AccountAggregatePersistence() => WithSaveDirectory(directory =>
    {
        var rules = new CharacterStateRules();
        CharacterRecord saved;
        Guid account;
        using (var db = new AccountRepository(directory, rules.Validate, rules.Upgrade))
        {
            account = db.Register("aggregate_user", "aggregate test password");
            var fresh = db.Create(account, Guid.NewGuid(), "SavedReaver", CharacterClass.Warrior);
            var advanced = BranchCharacter(CharacterClass.Warrior, "equipment.weapon.forged_greatsword", false, "sunder_strike");
            saved = advanced with { Id = fresh.Id, OwnerAccountId = account, Nickname = fresh.Nickname,
                Revision = 20, WorldPosition = new(480,448) };
            db.Save(saved);
            Guid other = db.Register("racing_user", "racing test password");
            int successes = 0;
            Parallel.For(0, 2, i =>
            {
                try { db.Create(i == 0 ? account : other, Guid.NewGuid(), i == 0 ? "SameName" : "SAMENAME", CharacterClass.Archer); System.Threading.Interlocked.Increment(ref successes); }
                catch (ArgumentException) { }
            });
            Require(successes == 1);
            using var sql = new SqliteConnection("Pooling=False;Data Source=" + System.IO.Path.Combine(directory, "accounts.sqlite3"));
            sql.Open();
            using var check = sql.CreateCommand();
            check.CommandText = "PRAGMA foreign_key_check";
            using (var reader = check.ExecuteReader()) Require(!reader.Read());
            check.CommandText = "SELECT password FROM accounts WHERE username='aggregate_user'";
            string hash = (string)check.ExecuteScalar()!;
            Require(hash.StartsWith("pbkdf2-sha256$600000$", StringComparison.Ordinal) && !hash.Contains("aggregate test password"));
            Require(PasswordHash.Create("aggregate test password") != hash);
        }
        using var restarted = new AccountRepository(directory, rules.Validate, rules.Upgrade);
        CharacterRecord restored = restarted.Owned(account, saved.Id);
        Require(restored == saved);
        var world = new WorldSimulation();
        long entity = world.Join(101, character: restored);
        var state = world.GetSnapshots().Single(e => e.Id == entity);
        Require(state.Name == saved.Nickname && state.Position == new System.Numerics.Vector2(480,448));
        Require(world.Character(entity)!.Tree.Has("warrior.reaver.sunder_strike"));
        Require(state.WeaponType == WeaponType.Greatsword);
    });
}
