using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arvenok.Contracts;

namespace Arvenok.Game.Features.Arena.Presentation;

// Pure presentation metadata; also compiled by the engine-independent regression harness.
public sealed record AnimationClip(string Key, string File, int Frames, double Fps, bool Loop)
{
    public double Seconds => Frames / Fps;
    public int Frame(double elapsed, double duration = 0, double from = 0, double to = 1)
    {
        double progress = duration > 0 ? Math.Clamp(elapsed / duration, 0, 1) : Math.Max(0, elapsed) / Seconds;
        if (duration <= 0) progress = Loop ? progress % 1 : Math.Min(1, progress);
        return Math.Clamp((int)((from + progress * (to - from)) * Frames), 0, Frames - 1);
    }
}

public sealed record SkillVisual(string Windup, string Release, string Mode, string Effect, string Impact, double ReleaseSeconds);

public sealed class AnimationCatalog
{
    public IReadOnlyDictionary<string, AnimationClip> Clips { get; }
    private readonly Dictionary<(CharacterClass, AbilitySlot), SkillVisual> _skills;
    public int CellWidth { get; }
    public int CellHeight { get; }
    public float FeetX { get; }
    public float FeetY { get; }
    public float EffectHeight { get; }
    public double HurtSeconds { get; }
    public double InterruptSeconds { get; }
    public double ProjectileSpeed { get; }
    public double ProjectileMin { get; }
    public double ProjectileMax { get; }

    public AnimationCatalog(string manifestJson, string bindingsJson)
    {
        using JsonDocument manifest = JsonDocument.Parse(manifestJson), bindings = JsonDocument.Parse(bindingsJson);
        JsonElement root = manifest.RootElement, settings = bindings.RootElement;
        Require(root.GetProperty("format_version").GetString() == "1.0" && settings.GetProperty("schemaVersion").GetInt32() == 1, "Unsupported animation schema.");
        Require(root.GetProperty("layout").GetString() == "horizontal_strip_no_gaps", "Expected horizontal animation strips.");
        CellWidth = root.GetProperty("cell_px")[0].GetInt32(); CellHeight = root.GetProperty("cell_px")[1].GetInt32();
        Require(CellWidth > 0 && CellHeight > 0, "Invalid animation cell size.");
        FeetX = settings.GetProperty("feetPixel")[0].GetSingle(); FeetY = settings.GetProperty("feetPixel")[1].GetSingle();
        Require(float.IsFinite(FeetX) && float.IsFinite(FeetY) && FeetX >= 0 && FeetX <= CellWidth && FeetY >= 0 && FeetY <= CellHeight, "Invalid feet anchor.");
        EffectHeight = (float)Positive(settings, "effectHeight");
        HurtSeconds = Positive(settings, "hurtSeconds"); InterruptSeconds = Positive(settings, "interruptSeconds");
        ProjectileSpeed = Positive(settings, "projectilePixelsPerSecond");
        ProjectileMin = Positive(settings, "minProjectileSeconds"); ProjectileMax = Positive(settings, "maxProjectileSeconds");
        Require(ProjectileMax >= ProjectileMin, "Invalid projectile lifetime.");
        var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        void Add(string key, JsonElement value)
        {
            string file = value.GetProperty("file").GetString() ?? "";
            int count = value.GetProperty("frames").GetInt32(); double fps = value.GetProperty("fps").GetDouble();
            Require(file.StartsWith("assets/animations/", StringComparison.Ordinal) && file.EndsWith(".png", StringComparison.Ordinal)
                && !file.Contains("..", StringComparison.Ordinal) && !file.Contains('\\') && !file.Contains(':'), "Invalid animation path.");
            Require(count > 0 && count <= 128 && double.IsFinite(fps) && fps > 0, "Invalid animation frame metadata.");
            clips.Add(key, new AnimationClip(key, file, count, fps, value.GetProperty("loop").GetBoolean()));
        }
        foreach (JsonProperty actor in root.GetProperty("characters").EnumerateObject())
            foreach (JsonProperty clip in actor.Value.EnumerateObject())
                foreach (JsonProperty direction in clip.Value.EnumerateObject()) Add($"characters/{actor.Name}/{clip.Name}/{direction.Name}", direction.Value);
        foreach (JsonProperty actor in root.GetProperty("monsters").EnumerateObject())
            foreach (JsonProperty clip in actor.Value.EnumerateObject()) Add($"monsters/{actor.Name}/{clip.Name}", clip.Value);
        foreach (JsonProperty effect in root.GetProperty("effects").EnumerateObject()) Add($"effects/{effect.Name}", effect.Value);
        Clips = clips;
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
            foreach (string clip in new[] { "idle", "walk", "basic_attack", "cast", "interrupt", "defend", "hurt", "death" })
                foreach (string direction in new[] { "down", "left", "right", "up" }) Require(clips.ContainsKey($"characters/{kind.ToString().ToLowerInvariant()}/{clip}/{direction}"), "Missing player clip/direction.");
        foreach (MonsterSpecies species in Enum.GetValues<MonsterSpecies>())
            foreach (string clip in new[] { "idle", "move", "attack", "hurt", "death" }) Require(clips.ContainsKey($"monsters/{species.ToString().ToLowerInvariant()}/{clip}"), "Missing monster clip.");
        _skills = new();
        foreach (CharacterClass kind in Enum.GetValues<CharacterClass>())
            foreach (AbilitySlot slot in Enum.GetValues<AbilitySlot>())
            {
                JsonElement skill = settings.GetProperty("classes").GetProperty(kind.ToString()).GetProperty(slot.ToString());
                string Text(string name) => skill.GetProperty(name).GetString() ?? "";
                var mapping = new SkillVisual(Text("windup"), Text("release"), Text("mode"), Text("effect"), Text("impact"), Positive(skill, "releaseSeconds"));
                Require(new[] { "melee", "projectile", "impact", "trail", "blink", "aura" }.Contains(mapping.Mode), "Unknown effect mode.");
                foreach (string clip in new[] { mapping.Windup, mapping.Release }) Require(clips.ContainsKey($"characters/{kind.ToString().ToLowerInvariant()}/{clip}/down"), "Unknown mapped body clip.");
                foreach (string effect in new[] { mapping.Effect, mapping.Impact }) if (effect.Length > 0) Require(clips.ContainsKey("effects/" + effect), "Unknown mapped effect.");
                _skills.Add((kind, slot), mapping);
            }
    }

    public SkillVisual Skill(CharacterClass kind, AbilitySlot slot) => _skills[(kind, slot)];
    public string EntityKey(EntitySnapshot entity, string clip)
    {
        if (entity.Species is MonsterSpecies species)
        {
            string monsterClip = clip switch { "walk" => "move", "cast" or "basic_attack" => "attack", "interrupt" => "hurt", "defend" => "idle", _ => clip };
            return $"monsters/{species.ToString().ToLowerInvariant()}/{monsterClip}";
        }
        string direction = Math.Abs(entity.Facing.Y) >= Math.Abs(entity.Facing.X)
            ? entity.Facing.Y >= 0 ? "down" : "up" : entity.Facing.X >= 0 ? "right" : "left";
        return $"characters/{entity.Class.ToString().ToLowerInvariant()}/{clip}/{direction}";
    }
    public double FlightSeconds(float distance) => Math.Clamp(distance / ProjectileSpeed, ProjectileMin, ProjectileMax);
    private static double Positive(JsonElement value, string name)
    {
        double result = value.GetProperty(name).GetDouble(); Require(double.IsFinite(result) && result > 0, "Invalid visual timing/offset: " + name); return result;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
