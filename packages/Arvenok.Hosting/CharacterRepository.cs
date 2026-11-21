using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arvenok.Contracts;

namespace Arvenok.Hosting;

public sealed record CharacterSession(CharacterRecord Character, string ResumeToken);
public interface ICharacterRepository : IDisposable
{
    CharacterSession Create();
    CharacterSession Resume(string token);
    CharacterRecord Load(Guid id);
    void Save(CharacterRecord character);
}

/// <summary>Development storage. The host schedules these operations off its simulation thread.</summary>
public sealed class JsonCharacterRepository : ICharacterRepository
{
    private readonly string _root;
    private readonly Action<CharacterRecord> _validate;
    private readonly Func<CharacterRecord, CharacterRecord> _upgrade;
    private readonly Func<Guid, CharacterRecord> _create;
    private readonly FileStream _lease;
    private readonly object _writeGate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public JsonCharacterRepository(string directory, Action<CharacterRecord> validate,
        Func<CharacterRecord, CharacterRecord>? upgrade = null, Func<Guid, CharacterRecord>? create = null)
    {
        _root = Path.GetFullPath(directory); _validate = validate;
        _upgrade = upgrade ?? (record => record); _create = create ?? (id => CharacterRecord.New(id));
        Directory.CreateDirectory(Path.Combine(_root, "characters")); Directory.CreateDirectory(Path.Combine(_root, "credentials"));
        _lease = new FileStream(Path.Combine(_root, "server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public CharacterSession Create()
    {
        CharacterRecord character = _create(Guid.NewGuid());
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Save(character);
        WriteAtomic(Path.Combine(_root, "credentials", TokenHash(token) + ".json"), JsonSerializer.Serialize(character.Id));
        return new CharacterSession(character, token);
    }
    public CharacterSession Resume(string token)
    {
        string index = Path.Combine(_root, "credentials", TokenHash(token) + ".json");
        if (!File.Exists(index)) throw new InvalidDataException("Reconnect credential is not known to this development server.");
        Guid id;
        try { id = JsonSerializer.Deserialize<Guid>(File.ReadAllText(index)); }
        catch (JsonException e) { throw new InvalidDataException("Corrupt character credential index.", e); }
        return new CharacterSession(Load(id), token);
    }
    public CharacterRecord Load(Guid id)
    { lock (_writeGate) return LoadCore(id); }
    private CharacterRecord LoadCore(Guid id)
    {
        if (id == Guid.Empty) throw new InvalidDataException("Invalid character ID.");
        CharacterRecord record;
        try { record = JsonSerializer.Deserialize<CharacterRecord>(File.ReadAllText(CharacterPath(id)), Json) ?? throw new InvalidDataException("Empty character save."); }
        catch (Exception e) when (e is JsonException or ArgumentException or NullReferenceException) { throw new InvalidDataException("Corrupt character save.", e); }
        if (record.Id != id) throw new InvalidDataException("Save identity disagrees with its filename.");
        CharacterRecord upgraded = _upgrade(record);
        _validate(upgraded);
        if (upgraded != record) WriteAtomic(CharacterPath(id), JsonSerializer.Serialize(upgraded, Json));
        return upgraded;
    }
    public void Save(CharacterRecord character)
    { lock (_writeGate) SaveCore(character); }
    private void SaveCore(CharacterRecord character)
    {
        _validate(character);
        string path = CharacterPath(character.Id);
        if (File.Exists(path))
        {
            CharacterRecord previous = Load(character.Id);
            if (previous.Inventory is not null && (character.Inventory is null || previous.Inventory.Revision > character.Inventory.Revision
                || previous.Inventory.Revision == character.Inventory.Revision && (previous.Inventory != character.Inventory || previous.Equipment != character.Equipment)))
                throw new InvalidDataException("Refusing to overwrite newer or conflicting inventory.");
            if (previous.Revision > character.Revision) throw new InvalidDataException("Refusing to overwrite newer progression.");
            if (previous.Revision == character.Revision)
            {
                if (previous != character) throw new InvalidDataException("Conflicting progression at the same revision.");
                return;
            }
        }
        WriteAtomic(path, JsonSerializer.Serialize(character, Json));
    }
    public static string TokenHash(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 64 || !IsHex(token)) throw new InvalidDataException("Invalid reconnect credential.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
    private static bool IsHex(string value)
    { foreach (char c in value) if (!Uri.IsHexDigit(c)) return false; return true; }
    private string CharacterPath(Guid id) => Path.Combine(_root, "characters", id.ToString("N") + ".json");
    private static void WriteAtomic(string path, string content)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content); stream.Write(bytes); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose() => _lease.Dispose();
}
