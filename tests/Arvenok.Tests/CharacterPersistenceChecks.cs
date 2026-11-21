using System;
using System.IO;
using Arvenok.Contracts;
using Arvenok.Hosting;
using Arvenok.Simulation;

namespace Arvenok.Tests;

internal static partial class Program
{
    private static void WithSaveDirectory(Action<string> check)
    {
        string parent = Path.GetFullPath(Path.GetTempPath()), directory = Path.Combine(parent, "arvenok-progression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { check(directory); }
        finally
        {
            string resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("arvenok-progression-", StringComparison.Ordinal)) throw new IOException("Unsafe test cleanup path.");
            Directory.Delete(resolved, recursive: true);
        }
    }
    private static void CharacterPersistence() => WithSaveDirectory(directory =>
    {
        var rules = new ProgressionRules(); CharacterSession session; CharacterRecord saved;
        using (ICharacterRepository repository = new JsonCharacterRepository(directory, rules.Validate))
        {
            session = repository.Create(); Require(session.ResumeToken.Length == 64 && session.Character.Id != Guid.Empty);
            saved = rules.Award(session.Character, 85).Character;
            Require(rules.TryUpgrade(saved, "warrior.crushing-blow", saved.Revision, out CharacterRecord upgraded, out _)); saved = upgraded;
            repository.Save(saved); repository.Save(saved);
            Require(repository.Resume(session.ResumeToken).Character == saved);
            Require(!File.ReadAllText(Path.Combine(directory, "characters", saved.Id.ToString("N") + ".json")).Contains(session.ResumeToken, StringComparison.Ordinal));
        }
        using (ICharacterRepository restarted = new JsonCharacterRepository(directory, rules.Validate))
        {
            Require(restarted.Resume(session.ResumeToken).Character == saved);
            CharacterRecord reset = rules.ResetSkills(saved); restarted.Save(reset);
            Require(restarted.Resume(session.ResumeToken).Character.SkillPoints == 1 && restarted.Load(saved.Id).Ranks == SkillRanks.Initial);
        }
    });
    private static void CharacterSaveValidation() => WithSaveDirectory(directory =>
    {
        var rules = new ProgressionRules(); using ICharacterRepository repository = new JsonCharacterRepository(directory, rules.Validate);
        CharacterSession session = repository.Create(); CharacterRecord progressed = rules.Award(session.Character, 90).Character;
        repository.Save(progressed);
        InvalidSave(() => repository.Save(session.Character));
        InvalidSave(() => repository.Save(progressed with { SkillPoints = 99 }));
        InvalidSave(() => repository.Resume("../../not-a-token"));
        Require(repository.Load(progressed.Id) == progressed);
        bool locked = false;
        try { using var another = new JsonCharacterRepository(directory, rules.Validate); } catch (IOException) { locked = true; }
        Require(locked, "Two development servers must not write the same save repository.");
        string path = Path.Combine(directory, "characters", progressed.Id.ToString("N") + ".json");
        File.WriteAllText(path, "{ corrupted"); InvalidSave(() => repository.Resume(session.ResumeToken));
        Require(File.ReadAllText(path) == "{ corrupted", "Corrupt saves must not be silently replaced with new characters.");
    });
}
