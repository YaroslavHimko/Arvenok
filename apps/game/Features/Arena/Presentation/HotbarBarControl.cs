using System;
using System.Collections.Generic;
using System.Linq;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

public partial class HotbarBarControl : Panel
{
    public HotbarHud Hud { get; set; } = null!;
    public int Index { get; set; }
    private readonly List<HotbarSlotControl> _slots = new();
    private readonly Label _handle = new() { MouseFilter = MouseFilterEnum.Stop };
    private readonly Label _cast = new() { MouseFilter = MouseFilterEnum.Ignore };
    private bool _dragging;
    private Vector2 _offset;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop; _handle.Name = "Handle"; _cast.Name = "Cast"; AddChild(_handle); AddChild(_cast);
        _handle.AddThemeFontSizeOverride("font_size", 10); _cast.AddThemeFontSizeOverride("font_size", 11);
        _handle.GuiInput += HandleInput;
    }
    public void Build()
    {
        HotbarBar data = Hud.Layout.Bars[Index]; Visible = data.Visible;
        foreach (var slot in _slots) { RemoveChild(slot); slot.QueueFree(); } _slots.Clear();
        Size = data.Vertical ? new Vector2(40, 16 + data.SlotCount * 40) : new Vector2(data.SlotCount * 40, 56);
        Scale = Vector2.One * data.Scale;
        Vector2 desired = new Vector2(data.X * 1000, data.Y * 650) - Size * Scale / 2;
        Position = new Vector2(Math.Clamp(desired.X, 0, Math.Max(0, 1000 - Size.X * Scale.X)), Math.Clamp(desired.Y, 0, Math.Max(0, 650 - Size.Y * Scale.Y)));
        _handle.Position = Vector2.Zero; _handle.Size = new Vector2(Size.X, 16); _handle.Text = data.Locked ? $"{Index + 1} · locked" : $"{Index + 1} · drag";
        _handle.TooltipText = "H: hotbar settings. Right-click a slot to clear it.";
        _cast.Position = new Vector2(0, -20); _cast.Size = new Vector2(Math.Max(200, Size.X), 20);
        for (int i = 0; i < data.SlotCount; i++)
        {
            var slot = new HotbarSlotControl { Name = "Slot" + i, Hud = Hud, Bar = Index, Index = i, Position = data.Vertical ? new Vector2(1, 17 + i * 40) : new Vector2(1 + i * 40, 17), Size = new Vector2(38, 38) };
            AddChild(slot); _slots.Add(slot);
        }
    }
    public void Refresh()
    {
        Visible = Hud.Layout.Bars[Index].Visible;
        if (!Visible) return;
        foreach (var slot in _slots) slot.Refresh();
        bool casting = Hud.Session.Entities.TryGetValue(Hud.Session.LocalEntityId, out EntitySnapshot me) && me.Casting;
        // A confirmed interruption can arrive before the next full snapshot.
        if (casting && Hud.Session.Activity.Any(e => e.Time >= Hud.Session.SnapshotTime && e.Time >= me.CastStartedAt
            && (e.ActorId == me.Id && e.Kind is CombatEventKind.CastCancelled or CombatEventKind.CastInterrupted || e.TargetId == me.Id && e.Kind == CombatEventKind.Died))) casting = false;
        _cast.Visible = Index == 0 && casting;
        if (casting) _cast.Text = $"{me.ActiveAbilityName} · {Math.Max(0, me.CastEndsAt - Hud.Session.ServerTime):0.0}s";
    }
    private void HandleInput(InputEvent e)
    {
        if (Hud.Layout.Bars[Index].Locked) return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        { _dragging = click.Pressed; _offset = click.Position * Scale; if (!click.Pressed) Hud.MoveBar(Index, Position + Size * Scale / 2); AcceptEvent(); }
        if (e is InputEventMouseMotion motion && _dragging)
        { Position += motion.Position * Scale - _offset; AcceptEvent(); }
    }
}

public partial class HotbarSlotControl : Button
{
    public HotbarHud Hud { get; set; } = null!;
    public int Bar { get; set; }
    public int Index { get; set; }
    private readonly TextureRect _icon = new() { Position = new Vector2(4, 4), Size = new Vector2(30, 30), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore };
    private readonly HotbarCooldownOverlay _overlay = new() { Size = new Vector2(38, 38), MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _key = new() { Position = new Vector2(1, 0), MouseFilter = MouseFilterEnum.Ignore }, _timer = new() { Position = new Vector2(1, 13), Size = new Vector2(36, 18), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _rank = new() { Position = new Vector2(25, 25), MouseFilter = MouseFilterEnum.Ignore };
    private string _loaded = "";
    private StyleBoxTexture _assignedFrame = null!, _emptyFrame = null!, _lockedFrame = null!;
    private StyleBoxTexture? _currentFrame;
    private static StyleBoxTexture Frame(string file) => new() { Texture = GD.Load<Texture2D>(HotbarHud.Art + "ui/" + file + ".png") };
    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        _key.Name = "Key"; _timer.Name = "Timer"; _rank.Name = "Rank";
        AddChild(_icon); AddChild(_overlay); AddChild(_key); AddChild(_timer); AddChild(_rank);
        foreach (Label text in new[] { _key, _timer, _rank }) { text.AddThemeFontSizeOverride("font_size", text == _timer ? 12 : 9); text.AddThemeColorOverride("font_shadow_color", Colors.Black); text.AddThemeConstantOverride("shadow_offset_x", 1); text.AddThemeConstantOverride("shadow_offset_y", 1); }
        Pressed += () => Hud.Use(Hud.Layout.Bars[Bar].Slots[Index].ActionId);
        GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) { Hud.Clear(Bar, Index); AcceptEvent(); } };
        _assignedFrame = Frame("hotbar_slot");
        _emptyFrame = Frame("hotbar_slot_empty");
        _lockedFrame = Frame("hotbar_slot_locked");
        var selected = Frame("hotbar_slot_selected");
        AddThemeStyleboxOverride("hover", selected);
        AddThemeStyleboxOverride("pressed", selected);
    }
    public void Refresh()
    {
        HotbarBar bar = Hud.Layout.Bars[Bar]; HotbarBinding binding = bar.Slots[Index];
        HotbarAction? action = Hud.Actions().FirstOrDefault(a => a.Id == binding.ActionId);
        string path = action?.Icon ?? ""; if (_loaded != path) { _loaded = path; _icon.Texture = path.Length == 0 ? null : GD.Load<Texture2D>(path); }
        _key.Text = bar.ShowKeys ? binding.Key.Replace("CTRL+", "C+").Replace("SHIFT+", "S+").Replace("ALT+", "A+") : "";
        _rank.Text = action is not null && action.Slot != AbilitySlot.Basic ? action.Rank.ToString() : "";
        _timer.Text = ""; _overlay.Cooldown = _overlay.Recovery = _overlay.Casting = 0;
        string unavailable = action is null ? "Empty slot · drag a learned skill here" : Hud.Availability(action.Slot);
        TooltipText = (action?.Name ?? "Empty") + "\n" + unavailable + "\nRight-click: clear · H: configure";
        _icon.Modulate = action is not null && unavailable.Length > 0 ? new Color(0.55f, 0.55f, 0.55f) : Colors.White;
        _overlay.WrongWeapon = action is not null && Hud.Session.WeaponRequirement(Hud.Session.Progress!.Class, action.Slot).Length > 0;
        var frame = action is null ? _emptyFrame : _overlay.WrongWeapon ? _lockedFrame : _assignedFrame;
        if (frame != _currentFrame) { AddThemeStyleboxOverride("normal", frame); _currentFrame = frame; }
        if (action is not null && Hud.Session.Entities.TryGetValue(Hud.Session.LocalEntityId, out EntitySnapshot me))
        {
            AbilityDefinition skill = Hud.Session.EffectiveAbility(me.Class, action.Slot); double now = Hud.Session.ServerTime;
            double remaining = Math.Max(0, me.Cooldowns.EndsAt(action.Slot) - now);
            _overlay.Cooldown = (float)Math.Clamp(remaining / skill.CooldownSeconds, 0, 1);
            _overlay.Recovery = (float)Math.Clamp((me.RecoveryEndsAt - now) / 0.25, 0, 1);
            if (me.Casting && me.ActiveAbility == action.Slot) _overlay.Casting = (float)Math.Clamp((now - me.CastStartedAt) / Math.Max(0.001, me.CastEndsAt - me.CastStartedAt), 0, 1);
            if (bar.ShowNumbers && remaining > 0) _timer.Text = remaining < 10 ? $"{remaining:0.0}s" : $"{Math.Ceiling(remaining):0}s";
        }
        _overlay.QueueRedraw();
    }
    public override Variant _GetDragData(Vector2 position)
    {
        var data = Hud.DragAction(Hud.Layout.Bars[Bar].Slots[Index].ActionId, Bar, Index); if (data.VariantType == Variant.Type.Nil) return data;
        SetDragPreview(new TextureRect { Texture = _icon.Texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, CustomMinimumSize = new Vector2(40, 40), TextureFilter = TextureFilterEnum.Nearest }); return data;
    }
    public override bool _CanDropData(Vector2 position, Variant data) => Hud.CanDrop(data);
    public override void _DropData(Vector2 position, Variant data) => Hud.Drop(Bar, Index, data);
}

public partial class HotbarCooldownOverlay : Control
{
    public float Cooldown, Recovery, Casting;
    public bool WrongWeapon;
    public override void _Draw()
    {
        Vector2 center = Size / 2; float radius = Size.X / 2;
        if (Cooldown > 0)
        {
            var points = new List<Vector2> { center }; int steps = Math.Max(1, (int)Math.Ceiling(Cooldown * 48));
            for (int i = 0; i <= steps; i++) { float angle = -Mathf.Pi / 2 + Mathf.Tau * Cooldown * i / steps; points.Add(center + Vector2.FromAngle(angle) * radius); }
            DrawColoredPolygon(points.ToArray(), new Color(0, 0, 0, 0.7f));
        }
        if (Recovery > 0) DrawArc(center, radius - 2, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * Recovery, 48, new Color("91b1cb"), 2);
        if (Casting > 0) DrawRect(new Rect2(1, Size.Y - 3, (Size.X - 2) * Casting, 3), new Color("ffce76"));
        if (WrongWeapon) DrawRect(new Rect2(Vector2.One, Size - Vector2.One * 2), new Color("ee695c"), false, 2);
    }
}
