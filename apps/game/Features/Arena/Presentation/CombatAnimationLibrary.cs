using System;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

internal sealed class CombatAnimationLibrary
{
    public const string Root = "res://Assets/Art2D/woodvale-combat/";
    public AnimationCatalog Catalog { get; }
    public SpriteFrames Frames { get; } = new();

    public CombatAnimationLibrary()
    {
        Catalog = new AnimationCatalog(FileAccess.GetFileAsString(Root + "animation_manifest.json"), FileAccess.GetFileAsString(Root + "visual_bindings.json"));
        Frames.RemoveAnimation("default");
        foreach (AnimationClip clip in Catalog.Clips.Values)
        {
            Texture2D sheet = GD.Load<Texture2D>(Root + clip.File) ?? throw new InvalidOperationException("Missing animation strip: " + clip.File);
            if (sheet.GetWidth() != Catalog.CellWidth * clip.Frames || sheet.GetHeight() != Catalog.CellHeight)
                throw new InvalidOperationException("Animation strip dimensions disagree with manifest: " + clip.File);
            Frames.AddAnimation(clip.Key); Frames.SetAnimationSpeed(clip.Key, clip.Fps);
            Frames.SetAnimationLoopMode(clip.Key, clip.Loop ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);
            for (int frame = 0; frame < clip.Frames; frame++) Frames.AddFrame(clip.Key, new AtlasTexture
            {
                Atlas = sheet, Region = new Rect2(frame * Catalog.CellWidth, 0, Catalog.CellWidth, Catalog.CellHeight), FilterClip = true
            });
        }
        GD.Print($"Woodvale animations ready: {Catalog.Clips.Count} strips; authoritative visual playback.");
    }

    // Select frames from server time explicitly: no animation_finished signal can mutate gameplay.
    public void Sample(AnimatedSprite2D sprite, string key, double elapsed, double duration = 0, double from = 0, double to = 1)
    {
        if (sprite.SpriteFrames != Frames) sprite.SpriteFrames = Frames;
        if (sprite.Animation != key) sprite.Animation = key;
        sprite.Frame = Catalog.Clips[key].Frame(elapsed, duration, from, to);
    }
}
