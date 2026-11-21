using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arvenok.Game.Features.Arena.Presentation;

// Client preferences only; these records never enter character gameplay saves or RPCs.
public sealed record HotbarBinding(string ActionId, string Key);
public sealed record HotbarBar(int SlotCount, bool Visible, bool Vertical, float X, float Y, float Scale,
    bool Locked, bool ShowNumbers, bool ShowKeys, IReadOnlyList<HotbarBinding> Slots);
public sealed record HotbarLayout(int Version, IReadOnlyList<HotbarBar> Bars);

public static class HotbarLayouts
{
    public static HotbarLayout Default(IReadOnlyList<string> skills) => new(2, Enumerable.Range(0, 3).Select(bar =>
        new HotbarBar(8, bar == 0, false, 0.5f, 0.89f - bar * 0.1f, 1, true, true, true,
            Enumerable.Range(0, 12).Select(slot => new HotbarBinding(bar == 0 && slot < skills.Count ? skills[slot] : "",
                bar == 0 && slot < 8 ? (slot + 1).ToString() : "")).ToArray())).ToArray());
    public static bool Reserved(string key) => new[] { "I", "E", "K", "F", "H", "ESCAPE", "SPACE", "F1", "F2", "F3", "F8" }.Contains(key.Split('+').Last());
    public static void Validate(HotbarLayout layout)
    {
        if (layout is null || layout.Version != 2 || layout.Bars is null || layout.Bars.Count != 3 || layout.Bars.Any(b => b is null) || layout.Bars.All(b => !b.Visible)) throw new InvalidDataException("Invalid hotbar layout header.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (HotbarBar bar in layout.Bars)
        {
            if (bar is null || bar.SlotCount is not (8 or 10 or 12) || !float.IsFinite(bar.X + bar.Y + bar.Scale)
                || bar.X < 0 || bar.X > 1 || bar.Y < 0 || bar.Y > 1 || bar.Scale < 0.8f || bar.Scale > 1.25f || bar.Slots is null || bar.Slots.Count != 12)
                throw new InvalidDataException("Invalid hotbar dimensions or settings.");
            foreach (var slot in bar.Slots)
            {
                if (slot is null || slot.ActionId is null || slot.ActionId.Length > 80 || slot.ActionId.Length > 0 && !Regex.IsMatch(slot.ActionId, "^[a-z][a-z0-9_.-]+$")
                    || slot.Key is null || slot.Key.Length > 40 || slot.Key.Length > 0 && (!Regex.IsMatch(slot.Key, "^(CTRL\\+)?(ALT\\+)?(SHIFT\\+)?([A-Z0-9]|F(?:[1-9]|1[0-2])|MINUS|EQUAL|TAB)$") || Reserved(slot.Key)))
                    throw new InvalidDataException("Invalid action or reserved key binding.");
                if (slot.Key.Length > 0 && !keys.Add(slot.Key)) throw new InvalidDataException("A hotkey may bind only one slot.");
            }
        }
    }
    public static HotbarLayout ChangeBar(HotbarLayout layout, int index, HotbarBar bar)
    { var bars = layout.Bars.ToArray(); bars[index] = bar; var updated = layout with { Bars = bars }; Validate(updated); return updated; }
    public static HotbarLayout Assign(HotbarLayout layout, int bar, int slot, string action, IReadOnlySet<string> learned, int fromBar = -1, int fromSlot = -1)
    {
        if (action.Length > 0 && !learned.Contains(action)) throw new InvalidDataException("Only learned active skills can be assigned.");
        if (bar is < 0 or > 2 || slot < 0 || slot >= layout.Bars[bar].SlotCount) throw new InvalidDataException("Invalid hotbar slot.");
        var bars = layout.Bars.Select(b => b with { Slots = b.Slots.ToArray() }).ToArray();
        var target = bars[bar].Slots.ToArray(); string old = target[slot].ActionId;
        if (fromBar >= 0)
        {
            if (fromBar > 2 || fromSlot < 0 || fromSlot >= bars[fromBar].SlotCount || bars[fromBar].Slots[fromSlot].ActionId != action)
                throw new InvalidDataException("Dragged hotbar action changed.");
            if (fromBar == bar && fromSlot == slot) return layout;
            var source = bars[fromBar].Slots.ToArray(); source[fromSlot] = source[fromSlot] with { ActionId = old }; bars[fromBar] = bars[fromBar] with { Slots = source };
            if (fromBar == bar) target = source;
        }
        target[slot] = target[slot] with { ActionId = action }; bars[bar] = bars[bar] with { Slots = target };
        var result = layout with { Bars = bars }; Validate(result); return result;
    }
    public static HotbarLayout BindKey(HotbarLayout layout, int bar, int slot, string key)
    {
        var slots = layout.Bars[bar].Slots.ToArray(); slots[slot] = slots[slot] with { Key = key };
        return ChangeBar(layout, bar, layout.Bars[bar] with { Slots = slots });
    }
}

public sealed class HotbarLayoutStore(string directory)
{
    private string FilePath(Guid character) => character == Guid.Empty ? throw new InvalidDataException("No character identity.") : Path.Combine(directory, character.ToString("N") + ".json");
    public HotbarLayout? Load(Guid character)
    {
        string path = FilePath(character); if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 32768) throw new InvalidDataException("Hotbar layout is too large.");
        HotbarLayout layout = JsonSerializer.Deserialize<HotbarLayout>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty hotbar layout.");
        HotbarLayouts.Validate(layout); return layout;
    }
    public void Save(Guid character, HotbarLayout layout)
    {
        HotbarLayouts.Validate(layout); Directory.CreateDirectory(directory); string path = FilePath(character), temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(layout)); File.Move(temporary, path, true);
    }
}
