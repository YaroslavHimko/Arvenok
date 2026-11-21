using System;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

/// <summary>Owner-only read model; native controls submit stable skill IDs, never ranks or EXP.</summary>
public partial class ProgressionHud : Control
{
    private const string Art = "res://Assets/UI/woodvale-progression/ui/";
    private readonly Label _level = new(), _experience = new(), _heading = new(), _feedback = new();
    private readonly Button _points = new();
    private readonly Control _fillClip = new();
    private readonly Panel _skills = new();
    private readonly Label[] _names = new Label[4], _values = new Label[4], _gates = new Label[4];
    private readonly Button[] _upgrades = new Button[4];
    private readonly string[] _skillIds = new string[4];
    private readonly SkillDragButton[] _icons = new SkillDragButton[4];
    public HotbarHud Hotbar { get; set; } = null!;
    private TextureRect _sparkle = null!;
    private long _revision = -1;
    private bool _pending;
    public ArenaSession Session { get; set; } = null!;
    public bool SkillsVisible => _skills.Visible || _treeWindow.Visible;
    public Action? Opening { get; set; }
    public void Close() { _skills.Hide(); _treeWindow.Hide(); GetViewport().GuiReleaseFocus(); }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        ZIndex = 20;
        Picture(this, "level_badge", new Rect2(22, 469, 55, 47));
        Place(this, _level, new Rect2(27, 480, 45, 26), 19, centered: true);
        Picture(this, "exp_frame", new Rect2(90, 469, 598, 47));
        _fillClip.Position = new Vector2(137, 494); _fillClip.Size = new Vector2(506, 16);
        _fillClip.ClipContents = true; _fillClip.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_fillClip);
        Picture(_fillClip, "exp_fill", new Rect2(-6, -3, 518, 22));
        _sparkle = Picture(this, "exp_sparkles", new Rect2(130, 482, 28, 28));
        Place(this, _experience, new Rect2(138, 490, 504, 23), 14, centered: true);
        _points.Position = new Vector2(710, 479); _points.Size = new Vector2(270, 36);
        _points.Icon = GD.Load<Texture2D>(Art + "skill_point_icon.png");
        _points.ExpandIcon = true; _points.AddThemeConstantOverride("icon_max_width", 34);
        _points.AddThemeFontSizeOverride("font_size", 16);
        _points.Pressed += ToggleSkills; AddChild(_points);
        _skills.Name = "Skills"; _skills.Position = new Vector2(90, 104); _skills.Size = new Vector2(820, 355);
        var panel = new StyleBoxFlat { BgColor = new Color("111d24"), BorderColor = new Color("c4a560"),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8 };
        _skills.AddThemeStyleboxOverride("panel", panel); AddChild(_skills);
        Picture(_skills, "upgrade_panel", new Rect2(3, 3, 814, 56)).Modulate = new Color(1, 1, 1, 0.16f);
        Place(_skills, _heading, new Rect2(20, 12, 580, 30), 18);
        var tree = new Button { Name = "OpenTree", Text = "Skill tree", Position = new Vector2(610, 11), Size = new Vector2(106, 32) };
        tree.Pressed += () => { _skills.Hide(); _treeWindow.Show(); RefreshTree(); }; _skills.AddChild(tree);
        var close = new Button { Text = "Close", Position = new Vector2(726, 11), Size = new Vector2(80, 32) };
        close.Pressed += ToggleSkills; _skills.AddChild(close);
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            float y = 60 + i * 65;
            _names[i] = new Label(); _values[i] = new Label(); _gates[i] = new Label();
            Place(_skills, _names[i], new Rect2(76, y, 505, 22), 17);
            Place(_skills, _values[i], new Rect2(76, y + 23, 505, 43), 14);
            _icons[i] = new SkillDragButton { Name = "SkillIcon" + i, Position = new Vector2(22, y + 7), Size = new Vector2(42, 42), ExpandIcon = true };
            _icons[i].AddThemeConstantOverride("icon_max_width", 34); _skills.AddChild(_icons[i]);
            _upgrades[i] = new Button { Name = "Upgrade" + (AbilitySlot)(i + 1), Position = new Vector2(611, y + 2), Size = new Vector2(186, 30), Text = "Upgrade · 1 point" };
            _upgrades[i].Pressed += () => Session.RequestSkillUpgrade(_skillIds[index]);
            _skills.AddChild(_upgrades[i]);
            Place(_skills, _gates[i], new Rect2(604, y + 33, 200, 26), 13, centered: true);
        }
        Place(_skills, _feedback, new Rect2(22, 326, 774, 24), 14);
        _skills.Hide();
        BuildTree();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.K || key.Keycode == Key.Escape && SkillsVisible)
        { ToggleSkills(); GetViewport().SetInputAsHandled(); }
    }

    public void ToggleSkills()
    {
        if (_treeWindow.Visible) { Close(); return; }
        if (!_skills.Visible) Opening?.Invoke();
        _skills.Visible = !_skills.Visible;
        if (!_skills.Visible) GetViewport().GuiReleaseFocus();
        _revision = -1;
    }

    public override void _Process(double delta)
    {
        ProgressionSnapshot? state = Session.Progress;
        _sparkle.Visible = Session.LastExperienceAward > 0 && Session.ServerTime - Session.ProgressionMessageAt < 1.2;
        _feedback.Text = Session.ServerTime - Session.ProgressionMessageAt < 5 ? Session.ProgressionMessage : "Basic attacks are unranked. Each upgrade costs one point.";
        if (state is null) { _experience.Text = "Waiting for character…"; _points.Disabled = true; _fillClip.Size = new Vector2(0, 16); return; }
        UpdateTree();
        if (state.Revision == _revision && _pending == Session.UpgradePending) return;
        _revision = state.Revision; _pending = Session.UpgradePending;
        _level.Text = state.Level.ToString();
        float fraction = state.RequiredExperience == 0 ? 1 : (float)state.Experience / state.RequiredExperience;
        _fillClip.Size = new Vector2(506 * fraction, 16);
        _sparkle.Position = new Vector2(123 + 506 * fraction, 482);
        _experience.Text = state.RequiredExperience == 0 ? "MAX LEVEL · 100%" : $"{state.Experience} / {state.RequiredExperience} EXP · {fraction * 100:F1}%";
        _points.Disabled = false; _points.Text = $"K · Skills   {state.SkillPoints} point{(state.SkillPoints == 1 ? "" : "s")}";
        _heading.Text = $"{state.Class} skills · Level {state.Level} · {state.SkillPoints} points available";
        for (int i = 0; i < 4; i++)
        {
            SkillProgressionView skill = state.Skills[i];
            _skillIds[i] = skill.Id;
            _icons[i].Hud = Hotbar; _icons[i].ActionId = skill.Id;
            _icons[i].Icon = GD.Load<Texture2D>(HotbarHud.Art + "skills/" + skill.Id.Split('.')[^1].Replace('-', '_') + ".png");
            _icons[i].TooltipText = skill.Current.Name + " · drag to a hotbar slot";
            _names[i].Text = $"{i + 1}  {skill.Current.Name}    Rank {skill.Rank} / 4";
            _values[i].Text = "Now:  " + Describe(skill.Current) + "\n" + (skill.Next is null ? "Maximum rank reached" : "Next: " + Describe(skill.Next));
            _upgrades[i].Disabled = _pending || skill.Next is null || state.SkillPoints == 0 || state.Level < skill.RequiredLevel;
            _upgrades[i].Text = skill.Next is null ? "Max rank" : _pending ? "Waiting for server…" : "Upgrade · 1 point";
            _gates[i].Text = skill.Next is null ? "" : state.Level < skill.RequiredLevel ? $"Requires level {skill.RequiredLevel}" : state.SkillPoints == 0 ? "Earn a skill point to upgrade" : $"Level {skill.RequiredLevel} required · Ready";
        }
    }

    private string Describe(AbilityDefinition skill)
    {
        string effect = skill.Effect switch
        {
            AbilityEffect.Defense when skill.AbsorbAmount > 0 => $"Absorbs up to {skill.AbsorbAmount} damage over {skill.EffectSeconds:0.##}s",
            AbilityEffect.Defense when skill.Slot is AbilitySlot.BranchOne or AbilitySlot.BranchTwo => $"Blocks one frontal hit within {skill.EffectSeconds:0.##}s",
            AbilityEffect.Defense => $"{skill.Reduction * 100:0}% reduction for {skill.EffectSeconds:0.#}s",
            AbilityEffect.Sidestep => $"{skill.SidestepDistance / Session.World.TileSize:0.##}-tile sidestep and {skill.Damage} damage",
            AbilityEffect.Blink or AbilityEffect.Backstep or AbilityEffect.Rush => $"{skill.Range / Session.World.TileSize:0.#} tiles of movement",
            AbilityEffect.Interrupt => $"{skill.Damage} damage · interrupts casts",
            _ => $"{skill.Damage} damage · {skill.CastSeconds:0.##}s cast"
        };
        return $"{effect} · {skill.CooldownSeconds:0.#}s cooldown";
    }
    private static TextureRect Picture(Node parent, string file, Rect2 rect)
    {
        var picture = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = GD.Load<Texture2D>(Art + file + ".png"), Position = rect.Position, Size = rect.Size,
            StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
        parent.AddChild(picture); return picture;
    }
    private static void Place(Node parent, Label label, Rect2 rect, int size, bool centered = false)
    {
        label.Position = rect.Position; label.Size = rect.Size; label.MouseFilter = MouseFilterEnum.Ignore;
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", new Color("f3e5c9"));
        label.AddThemeColorOverride("font_shadow_color", Colors.Black); label.AddThemeConstantOverride("shadow_offset_x", 1); label.AddThemeConstantOverride("shadow_offset_y", 1);
        if (centered) label.HorizontalAlignment = HorizontalAlignment.Center;
        parent.AddChild(label);
    }
}
