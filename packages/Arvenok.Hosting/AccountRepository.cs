using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arvenok.Contracts;
using Microsoft.Data.Sqlite;

namespace Arvenok.Hosting;

/// <summary>One transactional character aggregate retains all existing gameplay ownership and receipts.</summary>
public sealed class AccountRepository : ICharacterRepository
{
    private readonly string _connection;
    private readonly Action<CharacterRecord> _validate;
    private readonly Func<CharacterRecord, CharacterRecord> _upgrade;
    private readonly FileStream _lease;
    private static readonly string DummyHash = PasswordHash.Create("invalid-password-placeholder");
    public AccountRepository(string directory, Action<CharacterRecord> validate, Func<CharacterRecord, CharacterRecord> upgrade)
    {
        Directory.CreateDirectory(directory);
        _lease = new FileStream(Path.Combine(directory, "server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "accounts.sqlite3"), ForeignKeys = true, Pooling = false }.ToString();
        _validate = validate; _upgrade = upgrade;
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS accounts(
                id TEXT PRIMARY KEY, username TEXT NOT NULL UNIQUE, password TEXT NOT NULL,
                created TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, enabled INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS characters(
                id TEXT PRIMARY KEY, account_id TEXT NOT NULL REFERENCES accounts(id),
                slot INTEGER NOT NULL CHECK(slot BETWEEN 0 AND 5), name TEXT NOT NULL,
                normalized_name TEXT NOT NULL UNIQUE, revision INTEGER NOT NULL, state TEXT NOT NULL,
                UNIQUE(account_id,slot));
            CREATE INDEX IF NOT EXISTS characters_owner ON characters(account_id);
            CREATE TABLE IF NOT EXISTS commands(
                account_id TEXT NOT NULL REFERENCES accounts(id), command_id TEXT NOT NULL,
                fingerprint TEXT NOT NULL, result TEXT NOT NULL, PRIMARY KEY(account_id,command_id));
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(_connection); db.Open(); return db; }
    private static SqliteCommand Command(SqliteConnection db, string sql, params object[] parameters)
    {
        var command = db.CreateCommand(); command.CommandText = sql;
        for (int i = 0; i < parameters.Length; i++) command.Parameters.AddWithValue("$" + i, parameters[i]);
        return command;
    }
    private static string Username(string value)
    {
        string name = value.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(name, "^[a-z][a-z0-9_]{2,31}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Username must be 3–32 letters, digits or underscores, starting with a letter.");
        return name;
    }
    public Guid Register(string username, string password)
    {
        string canonical = Username(username);
        string hash = PasswordHash.Create(password);
        Guid id = Guid.NewGuid();
        using var db = Open();
        using var insert = Command(db, "INSERT INTO accounts(id,username,password) VALUES($0,$1,$2)", id.ToString(), canonical, hash);
        try { insert.ExecuteNonQuery(); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            // A lost registration acknowledgement can be retried with the same credentials.
            Guid? existing = Login(username, password);
            if (existing.HasValue) return existing.Value;
            throw new ArgumentException("Username is already registered.");
        }
        return id;
    }
    public Guid? Login(string username, string password)
    {
        string canonical;
        try { canonical = Username(username); } catch (ArgumentException) { canonical = ""; }
        using var db = Open();
        using var query = Command(db, "SELECT id,password FROM accounts WHERE username=$0 AND enabled=1", canonical);
        using var reader = query.ExecuteReader();
        bool found = reader.Read();
        bool valid = PasswordHash.Verify(password, found ? reader.GetString(1) : DummyHash);
        return found && valid ? Guid.Parse(reader.GetString(0)) : null;
    }
    public CharacterCard[] List(Guid account)
    {
        using var db = Open();
        using var query = Command(db, "SELECT state FROM characters WHERE account_id=$0 ORDER BY slot", account.ToString());
        using var reader = query.ExecuteReader();
        var cards = new List<CharacterCard>();
        while (reader.Read())
        {
            CharacterRecord c = Decode(reader.GetString(0));
            cards.Add(new(c.Id, c.Nickname, c.Class, c.Level));
        }
        return cards.ToArray();
    }
    private CharacterRecord Decode(string json)
    {
        var record = JsonSerializer.Deserialize<CharacterRecord>(json) ?? throw new InvalidDataException("Invalid character data.");
        var upgraded = _upgrade(record); _validate(upgraded); return upgraded;
    }
    public CharacterRecord Load(Guid id)
    {
        using var db = Open();
        using var query = Command(db, "SELECT state FROM characters WHERE id=$0", id.ToString());
        return Decode(query.ExecuteScalar() as string ?? throw new ArgumentException("Character is unavailable."));
    }
    public CharacterRecord Owned(Guid account, Guid id)
    {
        CharacterRecord c = Load(id);
        if (c.OwnerAccountId != account) throw new ArgumentException("Character is unavailable.");
        return c;
    }
    public CharacterRecord Create(Guid account, Guid operation, string nickname, CharacterClass kind)
    {
        string name = nickname.Trim();
        if (!Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_]{2,15}$", RegexOptions.CultureInvariant) || !Enum.IsDefined(kind))
            throw new ArgumentException("Nickname must be 3–16 ASCII letters, digits or underscores, starting with a letter.");
        string normalized = name.ToUpperInvariant();
        if (new[] { "ADMIN", "ADMINISTRATOR", "MODERATOR", "SYSTEM", "SERVER", "SUPPORT", "GAMEMASTER", "DEVELOPER", "GM" }.Any(word => normalized == word || normalized.StartsWith(word + "_", StringComparison.Ordinal)))
            throw new ArgumentException("That nickname is reserved.");
        if (operation == Guid.Empty) throw new ArgumentException("A command ID is required.");
        string fingerprint = "create|" + name + "|" + (int)kind;
        using var db = Open();
        using var transaction = db.BeginTransaction();
        string? replay = Receipt(db, account, operation, fingerprint);
        if (replay is not null) return Owned(account, Guid.Parse(replay));
        using var slots = Command(db, "SELECT slot FROM characters WHERE account_id=$0", account.ToString());
        var used = new HashSet<int>();
        using (var reader = slots.ExecuteReader()) while (reader.Read()) used.Add(reader.GetInt32(0));
        int slot = Enumerable.Range(0, 6).FirstOrDefault(i => !used.Contains(i), -1);
        if (slot < 0) throw new ArgumentException("All six character slots are occupied.");
        CharacterRecord character = CharacterRecord.New(Guid.NewGuid(), kind) with { OwnerAccountId = account, Nickname = name, ClassLocked = true };
        _validate(character);
        using var insert = Command(db, "INSERT INTO characters(id,account_id,slot,name,normalized_name,revision,state) VALUES($0,$1,$2,$3,$4,$5,$6)",
            character.Id.ToString(), account.ToString(), slot, name, normalized, character.Revision, JsonSerializer.Serialize(character));
        try { insert.ExecuteNonQuery(); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) { throw new ArgumentException("Nickname is taken or the account has no free slot."); }
        Remember(db, account, operation, fingerprint, character.Id.ToString());
        transaction.Commit();
        return character;
    }
    public void Delete(Guid account, Guid operation, Guid characterId, string confirmation)
    {
        if (operation == Guid.Empty) throw new ArgumentException("A command ID is required.");
        string fingerprint = "delete|" + characterId + "|" + confirmation;
        using var db = Open();
        using var transaction = db.BeginTransaction();
        if (Receipt(db, account, operation, fingerprint) is not null) return;
        using var query = Command(db, "SELECT name FROM characters WHERE id=$0 AND account_id=$1", characterId.ToString(), account.ToString());
        if (query.ExecuteScalar() is not string name || name != confirmation) throw new ArgumentException("Type the exact nickname of your character to confirm deletion.");
        using var delete = Command(db, "DELETE FROM characters WHERE id=$0 AND account_id=$1", characterId.ToString(), account.ToString());
        delete.ExecuteNonQuery();
        Remember(db, account, operation, fingerprint, "deleted");
        transaction.Commit();
    }
    private static string? Receipt(SqliteConnection db, Guid account, Guid operation, string fingerprint)
    {
        using var query = Command(db, "SELECT fingerprint,result FROM commands WHERE account_id=$0 AND command_id=$1", account.ToString(), operation.ToString());
        using var reader = query.ExecuteReader();
        if (!reader.Read()) return null;
        if (reader.GetString(0) != fingerprint) throw new ArgumentException("Command ID was already used.");
        return reader.GetString(1);
    }
    private static void Remember(SqliteConnection db, Guid account, Guid operation, string fingerprint, string result)
    {
        using var command = Command(db, "INSERT INTO commands VALUES($0,$1,$2,$3)", account.ToString(), operation.ToString(), fingerprint, result);
        command.ExecuteNonQuery();
    }
    public void Save(CharacterRecord character)
    {
        _validate(character);
        using var db = Open();
        using var transaction = db.BeginTransaction();
        using var query = Command(db, "SELECT state FROM characters WHERE id=$0 AND account_id=$1", character.Id.ToString(), character.OwnerAccountId.ToString());
        var previous = Decode(query.ExecuteScalar() as string ?? throw new InvalidDataException("Character was deleted or its owner is invalid."));
        if (previous.Nickname != character.Nickname || previous.Class != character.Class || previous.Revision > character.Revision
            || previous.Inventory!.Revision > character.Inventory!.Revision
            || previous.Inventory.Revision == character.Inventory.Revision && (previous.Inventory != character.Inventory || previous.Equipment != character.Equipment))
            throw new InvalidDataException("Refusing a conflicting character save.");
        using var save = Command(db, "UPDATE characters SET state=$0,revision=$1 WHERE id=$2", JsonSerializer.Serialize(character), character.Revision, character.Id.ToString());
        save.ExecuteNonQuery(); transaction.Commit();
    }
    public CharacterSession Create() => throw new InvalidOperationException("Account ownership is required.");
    public CharacterSession Resume(string token) => throw new InvalidOperationException("Login is required.");
    public void Dispose() => _lease.Dispose();
}
