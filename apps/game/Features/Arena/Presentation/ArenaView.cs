using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

/// <summary>Client-only placeholder art and input. Never owns authoritative combat state.</summary>
public partial class ArenaView : Node2D
{
    private static readonly Rect2 PlayArea = new(80, 110, 840, 420);
    private long _selectedTargetId = -1;
    public ArenaSession Session { get; set; } = null!;

    public override void _Process(double delta)
    {
        if (!Session.Fighters.ContainsKey(_selectedTargetId)) _selectedTargetId = -1;
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Session.HasConnection) return;
        if (@event is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left)
        {
            Vector2 point = GetGlobalMousePosition();
            foreach (FighterSnapshot fighter in Session.Fighters.Values)
            {
                if (fighter.Id != Session.LocalPeerId && point.DistanceTo(PositionOf(fighter)) <= 28f)
                {
                    _selectedTargetId = fighter.Id;
                    return;
                }
            }
            if (PlayArea.HasPoint(point)) Session.SendMove(point);
        }
        else if (@event is InputEventKey key && key.Pressed && !key.Echo && _selectedTargetId >= 0)
        {
            if (key.Keycode == Key.Key1) Session.SendAttack(_selectedTargetId, false);
            else if (key.Keycode == Key.Key2) Session.SendAttack(_selectedTargetId, true);
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 1000, 650), new Color(0.055f, 0.078f, 0.103f));
        DrawRect(PlayArea, new Color(0.12f, 0.17f, 0.18f));
        DrawRect(PlayArea, new Color(0.30f, 0.45f, 0.44f), false, 3f);
        LabelAt(new Vector2(25, 37), "ARVENOK  /  PVP PROTOTYPE", 23, Colors.White);
        LabelAt(new Vector2(25, 70), Session.Status, 17, new Color(0.70f, 0.83f, 0.86f));
        LabelAt(new Vector2(80, 560),
            "CLICK ground: move   |   CLICK opponent: target   |   1: strike (90 range)   |   2: bolt (240 range)", 16, Colors.White);
        LabelAt(new Vector2(80, 588),
            "Strike = 15 dmg / 0.8s   |   Bolt = 30 dmg / 1.25s cast   |   Taking damage interrupts casting",
            14, new Color(0.74f, 0.83f, 0.80f));
        LabelAt(new Vector2(80, 614),
            "Move during casting to cancel it. Players respawn 3s after defeat. Server decides all combat.",
            14, new Color(0.74f, 0.83f, 0.80f));

        foreach (FighterSnapshot fighter in Session.Fighters.Values)
        {
            Vector2 position = PositionOf(fighter);
            bool mine = fighter.Id == Session.LocalPeerId;
            Color color = fighter.Hp == 0 ? new Color(0.45f, 0.45f, 0.45f)
                : mine ? new Color(0.34f, 0.75f, 0.94f) : new Color(0.96f, 0.47f, 0.35f);
            if (fighter.Id == _selectedTargetId)
                DrawArc(position, 29f, 0, Mathf.Tau, 60, new Color(0.98f, 0.88f, 0.31f), 3f);
            DrawCircle(position, 20f, color);
            if (fighter.Casting) DrawArc(position, 25f, 0, Mathf.Tau * 0.75f, 32, Colors.Yellow, 3f);
            Vector2 bar = position + new Vector2(-28, -39);
            DrawRect(new Rect2(bar, new Vector2(56, 7)), new Color(0.25f, 0.2f, 0.2f));
            DrawRect(new Rect2(bar, new Vector2(56f * fighter.Hp / Session.MaximumHealth, 7)), new Color(0.34f, 0.86f, 0.48f));
            LabelAt(position + new Vector2(-33, 44), mine ? $"YOU [{fighter.Id}]" : $"ENEMY [{fighter.Id}]", 14, Colors.White);
        }
    }

    private static Vector2 PositionOf(FighterSnapshot fighter) => new(fighter.Position.X, fighter.Position.Y);

    private void LabelAt(Vector2 position, string value, int size, Color color) =>
        DrawString(ThemeDB.FallbackFont, position, value, HorizontalAlignment.Left, -1, size, color);
}
