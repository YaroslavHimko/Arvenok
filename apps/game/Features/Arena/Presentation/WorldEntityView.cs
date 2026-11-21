using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

/// <summary>Reusable player/monster appearance. Position and facing come only from received snapshots.</summary>
public partial class WorldEntityView : Node2D
{
    private readonly AnimatedSprite2D _sprite = new() { Name = "Sprite", TextureFilter = TextureFilterEnum.Nearest };
    private readonly AnimatedSprite2D _aura = new() { Name = "DefenseAura", TextureFilter = TextureFilterEnum.Nearest, Visible = false };
    private readonly AnimatedSprite2D _trail = new() { Name = "MotionTrail", TextureFilter = TextureFilterEnum.Nearest, Visible = false };
    private CombatAnimationLibrary _library = null!;
    public EntityAnimationState AnimationState { get; private set; } = null!;
    public EntitySnapshot Snapshot { get; private set; }
    public float VisualHeight { get; private set; }
    public bool TraceVisuals { get; set; }
    private string _lastClip = "";

    public override void _Ready() { AddChild(_trail); AddChild(_sprite); AddChild(_aura); }

    internal void Initialize(long id, CombatAnimationLibrary library)
    { _library = library; AnimationState = new EntityAnimationState(id, library.Catalog); }

    public void Apply(EntitySnapshot snapshot, float visualHeight, Vector2 interpolatedPosition, double snapshotTime, double time)
    {
        Snapshot = snapshot;
        Visible = !AnimationState.Removed;
        Position = interpolatedPosition;
        VisualHeight = visualHeight;
        AnimationCatalog catalog = _library.Catalog;
        _sprite.Position = new Vector2(catalog.CellWidth / 2f - catalog.FeetX, catalog.CellHeight / 2f - catalog.FeetY);
        AnimationPose pose = AnimationState.Pose(snapshot, snapshotTime, time);
        string key = catalog.EntityKey(snapshot, pose.Clip);
        _library.Sample(_sprite, key, pose.Elapsed, pose.Duration, pose.From, pose.To);
        if (TraceVisuals && key != _lastClip) GD.Print($"VISUAL body id={snapshot.Id} clip={key}");
        _lastClip = key;
        _aura.Position = new Vector2(0, -catalog.EffectHeight);
        bool hadAura = _aura.Visible, hadTrail = _trail.Visible;
        _aura.Visible = AnimationState.Defending(snapshot, snapshotTime, time);
        if (TraceVisuals && hadAura != _aura.Visible) GD.Print($"VISUAL aura id={snapshot.Id} active={_aura.Visible}");
        if (_aura.Visible) _library.Sample(_aura, "effects/" + catalog.Skill(snapshot.Class, AbilitySlot.Defense).Effect, time);
        _trail.Visible = AnimationState.Dashing(snapshot, snapshotTime, time);
        if (TraceVisuals && hadTrail != _trail.Visible) GD.Print($"VISUAL trail id={snapshot.Id} active={_trail.Visible}");
        if (_trail.Visible)
        {
            string effect = "effects/" + catalog.Skill(snapshot.Class, AbilitySlot.Mobility).Effect;
            _trail.Position = new Vector2(0, -catalog.EffectHeight);
            _trail.Rotation = new Vector2(snapshot.Facing.X, snapshot.Facing.Y).Angle();
            _library.Sample(_trail, effect, time % catalog.Clips[effect].Seconds);
        }
    }

    public bool HitTest(Vector2 screenPoint)
    {
        if (!Visible || !Snapshot.Alive || _sprite.SpriteFrames is null) return false;
        Vector2 pixel = _sprite.ToLocal(screenPoint) + new Vector2(_library.Catalog.CellWidth, _library.Catalog.CellHeight) / 2;
        if (pixel.X < 0 || pixel.Y < 0 || pixel.X >= _library.Catalog.CellWidth || pixel.Y >= _library.Catalog.CellHeight) return false;
        using Image image = _sprite.SpriteFrames.GetFrameTexture(_sprite.Animation, _sprite.Frame).GetImage();
        return image.GetPixel((int)pixel.X, (int)pixel.Y).A > 0.05f;
    }
}
