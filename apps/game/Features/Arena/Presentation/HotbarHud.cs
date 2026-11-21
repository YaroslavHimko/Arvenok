using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

public sealed record HotbarAction(string Id, string Name, AbilitySlot Slot, int Rank, string Icon);

public partial class HotbarHud : Control
{
    public const string Art = "res://Assets/UI/arvenok-skills-v02/";
    private readonly List<HotbarBarControl> _bars = new();
    private HotbarLayoutStore _store = null!;
    private Guid _character;
    private CharacterClass _class;
    private string[] _previousSkills = Array.Empty<string>();
    private ProgressionSnapshot? _actionProgress;
    private InventorySnapshot? _actionInventory;
    private HotbarAction[] _actions = Array.Empty<HotbarAction>();
    private ulong _feedbackUntil;
    public string ActionFeedback => Time.GetTicksMsec() < _feedbackUntil ? Message : "";
    public HotbarLayout Layout { get; private set; } = null!;
    public ArenaSession Session { get; set; } = null!;
    public Func<AbilitySlot, string> Availability { get; set; } = _ => "Not ready";
    public Action<AbilitySlot> Activate { get; set; } = _ => { };
    public Func<bool> ModalBlocked { get; set; } = () => false;
    public Action? OpeningSettings { get; set; }
    public string Message { get; private set; } = "";
    public bool SettingsVisible => _settings.Visible;
    public IReadOnlyList<HotbarAction> Actions()
    {
        if (Session.Progress is not ProgressionSnapshot progress) return Array.Empty<HotbarAction>();
        if (ReferenceEquals(progress, _actionProgress) && ReferenceEquals(Session.Backpack, _actionInventory)) return _actions;
        _actionProgress = progress;
        _actionInventory = Session.Backpack;
        _actions = progress.Skills.Concat(progress.BranchSkills).Select(s => new HotbarAction(s.Id, Session.EffectiveAbility(progress.Class, s.Slot).Name, s.Slot, s.Rank, Art + "skills/" + s.Id.Split('.').Last().Replace('-', '_') + ".png"))
            .Append(new("basic_attack", "Basic Attack", AbilitySlot.Basic, 1, "res://Assets/UI/items/sword.svg")).ToArray();
        return _actions;
    }
    public override void _Ready()
    {
        Name = "Hotbars"; ZIndex = 10; MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest;
        string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", ".local", "client", "hotbars"));
        _store = new(root);
        for (int i = 0; i < 3; i++) { var bar = new HotbarBarControl { Name = "Bar" + (i + 1), Hud = this, Index = i }; AddChild(bar); _bars.Add(bar); }
        BuildSettings();
    }
    public override void _Process(double delta)
    {
        if (Session.Progress is not ProgressionSnapshot progress) { foreach (var bar in _bars) bar.Hide(); return; }
        if (progress.CharacterId != _character)
        {
            _character = progress.CharacterId; _class = progress.Class; _previousSkills = progress.Skills.Select(s => s.Id).ToArray();
            try { Layout = _store.Load(_character) ?? HotbarLayouts.Default(_previousSkills.Append("basic_attack").ToArray()); }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
            { Layout = HotbarLayouts.Default(_previousSkills.Append("basic_attack").ToArray()); Message = "Could not restore hotbar: " + error.Message; }
            Rebuild();
        }
        if (progress.Class != _class)
        {
            string[] ids = progress.Skills.Select(s => s.Id).ToArray();
            SetLayout(Layout with { Bars = Layout.Bars.Select(b => b with { Slots = b.Slots.Select(s =>
            { int index = Array.IndexOf(_previousSkills, s.ActionId); return index >= 0 && index < ids.Length ? s with { ActionId = ids[index] } : s; }).ToArray() }).ToArray() });
            _previousSkills = ids; _class = progress.Class;
        }
        foreach (HotbarBarControl bar in _bars) bar.Refresh();
        _settingsFeedback.Text = Message;
    }
    public string InspectLayout() => Layout is null ? "null" : JsonSerializer.Serialize(Layout);
    private void Rebuild() { foreach (var bar in _bars) bar.Build(); RefreshSettings(); }
    public void SetLayout(HotbarLayout layout)
    {
        try { HotbarLayouts.Validate(layout); _store.Save(_character, layout); Layout = layout; Message = "Layout saved for this character."; Rebuild(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Message = error.Message; }
    }
    public Variant DragAction(string action, int fromBar = -1, int fromSlot = -1)
    {
        if (!Actions().Any(a => a.Id == action)) return default;
        return new Godot.Collections.Dictionary { ["hotbar_character"] = _character.ToString(), ["action"] = action, ["bar"] = fromBar, ["slot"] = fromSlot };
    }
    public bool CanDrop(Variant data)
    {
        if (Layout is null || data.VariantType != Variant.Type.Dictionary) return false;
        var payload = data.AsGodotDictionary();
        return payload.ContainsKey("hotbar_character") && payload["hotbar_character"].AsString() == _character.ToString() && payload.ContainsKey("action")
            && payload.ContainsKey("bar") && payload.ContainsKey("slot") && Actions().Any(a => a.Id == payload["action"].AsString());
    }
    public void Drop(int bar, int slot, Variant data)
    {
        if (!CanDrop(data)) return; var payload = data.AsGodotDictionary();
        try { SetLayout(HotbarLayouts.Assign(Layout, bar, slot, payload["action"].AsString(), Actions().Select(a => a.Id).ToHashSet(), payload["bar"].AsInt32(), payload["slot"].AsInt32())); }
        catch (InvalidDataException error) { Message = error.Message; }
    }
    public void Clear(int bar, int slot) => SetLayout(HotbarLayouts.Assign(Layout, bar, slot, "", Actions().Select(a => a.Id).ToHashSet()));
    public void Use(string id)
    {
        HotbarAction? action = Actions().FirstOrDefault(a => a.Id == id);
        if (action is null || ModalBlocked() || SettingsVisible) return;
        string reason = Availability(action.Slot);
        if (reason.Length > 0) { Message = reason; _feedbackUntil = Time.GetTicksMsec() + 2500; return; }
        Message = ""; Activate(action.Slot);
    }
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (_captureKey) { CaptureKey(key); GetViewport().SetInputAsHandled(); return; }
        if (key.Keycode == Key.H || key.Keycode == Key.Escape && SettingsVisible) { ToggleSettings(); GetViewport().SetInputAsHandled(); return; }
        if (Layout is null || SettingsVisible || ModalBlocked() || GetViewport().GuiGetFocusOwner() is LineEdit or SpinBox) return;
        string binding = KeyText(key);
        foreach (HotbarBar bar in Layout.Bars.Where(b => b.Visible)) foreach (HotbarBinding slot in bar.Slots.Take(bar.SlotCount))
            if (slot.Key == binding && slot.ActionId.Length > 0) { Use(slot.ActionId); GetViewport().SetInputAsHandled(); return; }
    }
    private static string KeyText(InputEventKey key)
    {
        string raw = key.Keycode is >= Key.Key0 and <= Key.Key9 ? ((int)key.Keycode - (int)Key.Key0).ToString() : key.Keycode.ToString().ToUpperInvariant();
        return (key.CtrlPressed ? "CTRL+" : "") + (key.AltPressed ? "ALT+" : "") + (key.ShiftPressed ? "SHIFT+" : "") + raw;
    }
    public void MoveBar(int index, Vector2 center)
    {
        var bar = Layout.Bars[index];
        SetLayout(HotbarLayouts.ChangeBar(Layout, index, bar with { X = Math.Clamp(center.X / 1000, 0, 1), Y = Math.Clamp(center.Y / 650, 0, 1) }));
    }
}

public partial class SkillDragButton : Button
{
    public HotbarHud Hud { get; set; } = null!;
    public string ActionId { get; set; } = "";
    public override Variant _GetDragData(Vector2 position)
    {
        Variant data = Hud.DragAction(ActionId); if (data.VariantType == Variant.Type.Nil) return data;
        var preview = new TextureRect { Texture = Icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, CustomMinimumSize = new Vector2(40, 40), TextureFilter = TextureFilterEnum.Nearest };
        SetDragPreview(preview); return data;
    }
}
