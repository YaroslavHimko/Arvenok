using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arvenok.Game.Features.Arena.Presentation;

namespace Arvenok.Tests;

internal static partial class Program
{
    private static void HotbarConfiguration()
    {
        var skills = new[] { "warrior.crushing-blow", "warrior.rush", "warrior.shield-bash", "warrior.guard", "basic_attack" };
        HotbarLayout layout = HotbarLayouts.Default(skills); HotbarLayouts.Validate(layout);
        Require(layout.Bars.Count == 3 && layout.Bars[0].SlotCount == 8 && layout.Bars.Count(b => b.Visible) == 1);
        Require(layout.Bars[0].Slots[3].Key == "4" && layout.Bars[0].Slots[3].ActionId == skills[3]);
        foreach (int slots in new[] { 8, 10, 12 }) foreach (bool vertical in new[] { true, false }) foreach (float scale in new[] { 0.8f, 1.25f })
            HotbarLayouts.Validate(HotbarLayouts.ChangeBar(layout, 2, layout.Bars[2] with { Visible = true, Vertical = vertical, SlotCount = slots, Scale = scale }));
        InvalidSave(() => HotbarLayouts.ChangeBar(layout, 0, layout.Bars[0] with { SlotCount = 13 }));
        InvalidSave(() => HotbarLayouts.ChangeBar(layout, 0, layout.Bars[0] with { Scale = 1.3f }));
        InvalidSave(() => HotbarLayouts.ChangeBar(layout, 0, layout.Bars[0] with { X = float.NaN }));
        InvalidSave(() => HotbarLayouts.ChangeBar(layout, 0, layout.Bars[0] with { Visible = false }));
        InvalidSave(() => HotbarLayouts.BindKey(layout, 1, 0, "1"));
        foreach (string key in new[] { "I", "CTRL+K", "SHIFT+SPACE", "E", "F", "H", "F8" }) InvalidSave(() => HotbarLayouts.BindKey(layout, 1, 0, key));
        Require(HotbarLayouts.BindKey(layout, 1, 0, "CTRL+SHIFT+Z").Bars[1].Slots[0].Key == "CTRL+SHIFT+Z");
    }
    private static void HotbarOwnership()
    {
        string[] skills = { "mage.firebolt", "mage.blink", "mage.counterspell", "mage.arcane_ward", "basic_attack" };
        var learned = skills.ToHashSet(); HotbarLayout layout = HotbarLayouts.Default(skills);
        layout = HotbarLayouts.Assign(layout, 0, 7, skills[0], learned);
        layout = HotbarLayouts.Assign(layout, 0, 1, skills[0], learned, 0, 7);
        Require(layout.Bars[0].Slots[1].ActionId == skills[0] && layout.Bars[0].Slots[7].ActionId == skills[1]);
        Require(layout.Bars[0].Slots[1].Key == "2" && layout.Bars[0].Slots[7].Key == "8");
        layout = HotbarLayouts.Assign(layout, 1, 0, skills[1], learned, 0, 7);
        Require(layout.Bars[1].Slots[0].ActionId == skills[1] && layout.Bars[0].Slots[7].ActionId == "");
        layout = HotbarLayouts.Assign(layout, 1, 0, "", learned);
        Require(layout.Bars[1].Slots[0].ActionId == "");
        InvalidSave(() => HotbarLayouts.Assign(layout, 1, 0, "mage.flame_lance", learned));
        InvalidSave(() => HotbarLayouts.Assign(layout, 1, 0, skills[0], learned, 0, 7));
    }
    private static void HotbarPersistence() => WithSaveDirectory(directory =>
    {
        var store = new HotbarLayoutStore(Path.Combine(directory, "hotbars")); Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        HotbarLayout first = HotbarLayouts.Default(new[] { "warrior.guard" }), second = HotbarLayouts.Default(new[] { "mage.arcane_ward" });
        first = HotbarLayouts.ChangeBar(first, 1, first.Bars[1] with { Visible = true, Vertical = true, X = 0.95f, Y = 0.4f, Locked = false, Scale = 0.8f });
        store.Save(a, first); store.Save(b, second);
        var restarted = new HotbarLayoutStore(Path.Combine(directory, "hotbars"));
        Require(JsonSerializer.Serialize(first) == JsonSerializer.Serialize(restarted.Load(a)));
        Require(JsonSerializer.Serialize(second) == JsonSerializer.Serialize(restarted.Load(b)));
        Require(restarted.Load(Guid.NewGuid()) is null);
        InvalidSave(() => store.Save(Guid.Empty, first));
        string path = Path.Combine(directory, "hotbars", a.ToString("N") + ".json"); File.WriteAllText(path, JsonSerializer.Serialize(first with { Version = 99 }));
        InvalidSave(() => store.Load(a)); Require(File.ReadAllText(path).Contains("99"));
    });
}
