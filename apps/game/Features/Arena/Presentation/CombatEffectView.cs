using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

/// <summary>Nonphysical, finite-lived VFX. No signals or overlap callbacks feed back into gameplay.</summary>
public partial class CombatEffectView : AnimatedSprite2D
{
    public CombatEffectCue Cue { get; private set; }
    private CombatAnimationLibrary _library = null!;
    public bool TraceVisuals { get; set; }
    private Vector2? _firstPosition;
    private bool _reportedTravel;
    internal void Initialize(CombatEffectCue cue, CombatAnimationLibrary library)
    {
        Cue = cue; _library = library; TextureFilter = TextureFilterEnum.Nearest;
        // The effect's center is above feet; Y sorting alone would hide projectiles under their caster.
        Rotation = cue.Rotation; ZIndex = 1; Name = "CombatEffect";
    }
    public void Sample(double time)
    {
        System.Numerics.Vector2 position = Cue.Position(time);
        Position = new Vector2(position.X, position.Y);
        _firstPosition ??= Position;
        if (TraceVisuals && Cue.Projectile && !_reportedTravel && Position.DistanceTo(_firstPosition.Value) > 2)
        { _reportedTravel = true; GD.Print($"VISUAL projectile travelled sequence={Cue.Sequence} effect={Cue.Effect}"); }
        _library.Sample(this, "effects/" + Cue.Effect, time - Cue.StartedAt);
    }
}
