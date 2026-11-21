using System;
using System.IO;
using System.Linq;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

public partial class HotbarHud
{
    private readonly Panel _settings = new() { Name = "Settings", Position = new Vector2(180, 110), Size = new Vector2(640, 350), ZIndex = 30 };
    private readonly OptionButton _barChoice = new(), _slotCount = new(), _slotChoice = new();
    private readonly CheckBox _visible = new() { Text = "Visible" }, _vertical = new() { Text = "Vertical" }, _locked = new() { Text = "Lock position" }, _numbers = new() { Text = "Cooldown numbers" }, _keys = new() { Text = "Hotkeys" };
    private readonly SpinBox _scale = new() { MinValue = 80, MaxValue = 125, Step = 5, Suffix = "%" };
    private readonly Button _binding = new() { Text = "Set hotkey" };
    private readonly Label _settingsFeedback = new();
    private readonly HBoxContainer _palette = new();
    private bool _refreshing, _captureKey;
    private void BuildSettings()
    {
        _barChoice.Name = "BarChoice"; _slotCount.Name = "SlotCount"; _slotChoice.Name = "SlotChoice";
        _visible.Name = "BarVisible"; _vertical.Name = "Vertical"; _locked.Name = "Locked"; _numbers.Name = "Numbers"; _keys.Name = "Keys"; _scale.Name = "Scale"; _binding.Name = "Binding";
        var toggle = new Button { Name = "OpenSettings", Text = "H · Hotbars", Position = new Vector2(20, 574), Size = new Vector2(114, 32) }; toggle.Pressed += ToggleSettings; AddChild(toggle);
        _settings.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("111d24"), BorderColor = new Color("c4a560"), BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2 });
        AddChild(_settings); _settings.Hide();
        var title = new Label { Text = "Hotbars · saved per character", Position = new Vector2(18, 12) }; title.AddThemeFontSizeOverride("font_size", 20); _settings.AddChild(title);
        var close = new Button { Text = "Close", Position = new Vector2(540, 10), Size = new Vector2(80, 30) }; close.Pressed += CloseSettings; _settings.AddChild(close);
        for (int i = 1; i <= 3; i++) _barChoice.AddItem("Bar " + i);
        foreach (int count in new[] { 8, 10, 12 }) _slotCount.AddItem(count + " slots", count);
        for (int i = 1; i <= 12; i++) _slotChoice.AddItem("Slot " + i);
        Put(_barChoice, 20, 60, 120); Put(_slotCount, 150, 60, 120); Put(_visible, 285, 60, 120); Put(_vertical, 425, 60, 140);
        Put(_locked, 20, 105, 170); Put(_numbers, 190, 105, 220); Put(_keys, 425, 105, 130);
        Put(_scale, 20, 150, 120); Put(_slotChoice, 150, 150, 120); Put(_binding, 285, 150, 195);
        var clearKey = new Button { Name = "ClearKey", Text = "Unbind key" }; Put(clearKey, 490, 150, 130);
        clearKey.Pressed += () => { if (Layout is not null) SetLayout(HotbarLayouts.BindKey(Layout, _barChoice.Selected, _slotChoice.Selected, "")); };
        var hint = new Label { Text = "Drag learned icons to a slot. Right-click a slot to clear.\nUnlock position, then drag a bar's header to move it.", Position = new Vector2(20, 198) }; hint.AddThemeFontSizeOverride("font_size", 14); _settings.AddChild(hint);
        _palette.Position = new Vector2(20, 245); _palette.Size = new Vector2(600, 42); _settings.AddChild(_palette);
        _settingsFeedback.Position = new Vector2(20, 299); _settingsFeedback.Size = new Vector2(600, 42); _settingsFeedback.AutowrapMode = TextServer.AutowrapMode.WordSmart; _settingsFeedback.AddThemeFontSizeOverride("font_size", 13); _settings.AddChild(_settingsFeedback);
        _barChoice.ItemSelected += _ => RefreshSettings(); _slotChoice.ItemSelected += _ => RefreshSettings();
        _slotCount.ItemSelected += _ => ChangeSettings();
        foreach (var check in new[] { _visible, _vertical, _locked, _numbers, _keys }) check.Toggled += _ => ChangeSettings();
        _scale.ValueChanged += _ => ChangeSettings();
        _binding.Pressed += () => { _captureKey = true; Message = "Press a key (Ctrl/Alt/Shift supported). Escape cancels. I/E/K/F/Space remain reserved."; };
    }
    private void Put(Control control, float x, float y, float width)
    { control.Position = new Vector2(x, y); control.Size = new Vector2(width, 32); _settings.AddChild(control); }
    public void CloseSettings() { _settings.Hide(); _captureKey = false; GetViewport().GuiReleaseFocus(); }
    private void ToggleSettings()
    {
        if (_settings.Visible) { CloseSettings(); return; }
        OpeningSettings?.Invoke(); _settings.Show(); RefreshSettings();
        foreach (Node child in _palette.GetChildren()) { _palette.RemoveChild(child); child.QueueFree(); }
        foreach (HotbarAction action in Actions())
        {
            var icon = new SkillDragButton { Hud = this, ActionId = action.Id, Icon = GD.Load<Texture2D>(action.Icon), ExpandIcon = true, CustomMinimumSize = new Vector2(42, 42), TooltipText = action.Name + " · drag to a bar" };
            icon.AddThemeConstantOverride("icon_max_width", 34); _palette.AddChild(icon);
        }
    }
    private void RefreshSettings()
    {
        if (Layout is null) return; _refreshing = true;
        HotbarBar bar = Layout.Bars[_barChoice.Selected]; _slotCount.Select(Array.IndexOf(new[] { 8, 10, 12 }, bar.SlotCount));
        _visible.ButtonPressed = bar.Visible; _vertical.ButtonPressed = bar.Vertical; _locked.ButtonPressed = bar.Locked;
        _numbers.ButtonPressed = bar.ShowNumbers; _keys.ButtonPressed = bar.ShowKeys; _scale.Value = Math.Round(bar.Scale * 100);
        for (int i = 0; i < 12; i++) _slotChoice.SetItemDisabled(i, i >= bar.SlotCount);
        if (_slotChoice.Selected >= bar.SlotCount) _slotChoice.Select(0);
        _binding.Text = bar.Slots[_slotChoice.Selected].Key is string key && key.Length > 0 ? "Key: " + key : "Set hotkey";
        _refreshing = false;
    }
    private void ChangeSettings()
    {
        if (_refreshing || Layout is null) return;
        var before = Layout.Bars[_barChoice.Selected];
        try { SetLayout(HotbarLayouts.ChangeBar(Layout, _barChoice.Selected, before with { SlotCount = _slotCount.GetSelectedId(), Visible = _visible.ButtonPressed,
            Vertical = _vertical.ButtonPressed, Locked = _locked.ButtonPressed, ShowNumbers = _numbers.ButtonPressed, ShowKeys = _keys.ButtonPressed, Scale = (float)_scale.Value / 100 })); }
        catch (InvalidDataException error) { Message = error.Message; RefreshSettings(); }
    }
    private void CaptureKey(InputEventKey key)
    {
        if (key.Keycode is Key.Shift or Key.Ctrl or Key.Alt) return;
        _captureKey = false; if (key.Keycode == Key.Escape) { Message = "Key change cancelled."; return; }
        try { SetLayout(HotbarLayouts.BindKey(Layout, _barChoice.Selected, _slotChoice.Selected, KeyText(key))); }
        catch (InvalidDataException error) { Message = error.Message; }
    }
}
