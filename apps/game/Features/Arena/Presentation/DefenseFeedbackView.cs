using System;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

/// <summary>Finite, event-driven cosmetics; deduplication is owned by ForestWorldView's event cursor.</summary>
public partial class DefenseFeedbackView : Node2D
{
    private Sprite2D _sprite = null!;
    private Label _label = null!;
    public long EntityId { get; private set; }
    public double StartedAt { get; private set; }
    public static bool Supports(CombatEventKind kind) => kind is CombatEventKind.Dodged or CombatEventKind.Parried or CombatEventKind.PerfectBlocked or CombatEventKind.Absorbed or CombatEventKind.Healed or CombatEventKind.ShieldBlocked;
    public void Initialize(CombatEvent entry)
    {
        EntityId = entry.Kind == CombatEventKind.Healed ? entry.ActorId : entry.TargetId; StartedAt = entry.Time;
        string effect = entry.Kind switch { CombatEventKind.Dodged => "dodge", CombatEventKind.Parried => "parry", CombatEventKind.Healed => "lifesteal", _ => "block" };
        string label = entry.Kind switch { CombatEventKind.Dodged => "DODGE", CombatEventKind.Parried => "PARRY", CombatEventKind.Healed => $"+{entry.Damage} HP", CombatEventKind.Absorbed => "ABSORB", _ => "BLOCK" };
        ZIndex = 5; TextureFilter = TextureFilterEnum.Nearest;
        _sprite = new Sprite2D { Texture = GD.Load<Texture2D>(HotbarHud.Art + "vfx/" + effect + "_6frames_96.png"), Hframes = 6, Position = new Vector2(0, -30) };
        _label = new Label { Text = label, Position = new Vector2(-60, -86), Size = new Vector2(120, 24), HorizontalAlignment = HorizontalAlignment.Center };
        _label.AddThemeFontSizeOverride("font_size", 16); _label.AddThemeColorOverride("font_color", entry.Kind == CombatEventKind.Healed ? new Color("8bffae") : new Color("ffd580"));
        _label.AddThemeColorOverride("font_shadow_color", Colors.Black); _label.AddThemeConstantOverride("shadow_offset_x", 1); _label.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(_sprite); AddChild(_label);
    }
    public void Sample(double now, Vector2 position)
    {
        double age = Math.Max(0, now - StartedAt); Position = position;
        _sprite.Visible = age < 0.5; _sprite.Frame = Math.Clamp((int)(age * 12), 0, 5);
        _label.Position = new Vector2(-60, -86 - (float)age * 12);
        Modulate = new Color(1, 1, 1, (float)Math.Clamp((1 - age) * 3, 0, 1));
    }
}
