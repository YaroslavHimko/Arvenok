using System;
using System.Collections.Generic;
using System.Linq;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

/// <summary>Terrain and scenery for the existing world. Physics footprints mirror the shared map data.</summary>
public partial class ForestWorldView : Node2D
{
    private readonly Node2D _sorted = new() { Name = "SceneryAndEntities", YSortEnabled = true };
    private readonly Dictionary<long, WorldEntityView> _entities = new();
    private WoodvaleArt _art = null!;
    private CombatAnimationLibrary _animations = null!;
    private long _lastEvent;
    private CombatEffectPlanner _effectPlanner = null!;
    private readonly List<CombatEffectView> _effects = new();
    private readonly List<DefenseFeedbackView> _feedback = new();
    private readonly List<(long Owner, double Started, Sprite2D Sprite)> _levelUps = new();
    private bool _traceVisuals;
    public WorldDefinition Definition { get; set; } = null!;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        WorldArt data = Definition.Art ?? throw new InvalidOperationException("World art definitions are missing.");
        _art = new WoodvaleArt(data);
        _animations = new CombatAnimationLibrary();
        _effectPlanner = new CombatEffectPlanner(_animations.Catalog);
        _traceVisuals = OS.GetCmdlineUserArgs().Contains("--smoke-role");
        BuildTerrain(data);
        AddChild(_sorted);
        var assets = data.PropAssets.ToDictionary(p => p.Id);
        foreach (PropPlacement placement in data.Props)
        {
            PropAsset asset = assets[placement.AssetId];
            var prop = new StaticBody2D { Name = placement.Id, Position = Point(placement.Position), CollisionLayer = 1, CollisionMask = 0 };
            prop.AddChild(new Sprite2D { Name = "Sprite", Texture = _art.Texture(asset.File), Position = new Vector2(0, -asset.Height / 2f) });
            if (asset.Footprint is WorldRect footprint) AddShape(prop, footprint);
            _sorted.AddChild(prop);
        }
        // Retain the original map's impassable rock formations, now dressed with the supplied rock art.
        foreach (WorldRect region in data.RockFormations)
        {
            var body = new StaticBody2D { Name = "RockFormation", CollisionLayer = 1, CollisionMask = 0 };
            AddShape(body, region); AddChild(body);
            Texture2D rock = _art.Texture(assets["rock_large"].File);
            for (float y = region.Y + 24; y <= region.Y + region.Height; y += 24)
                for (float x = region.X + 16; x < region.X + region.Width; x += 40)
                {
                    var root = new Node2D { Position = new Vector2(x, y) };
                    root.AddChild(new Sprite2D { Texture = rock, Position = new Vector2(0, -rock.GetHeight() / 2f) });
                    _sorted.AddChild(root);
                }
        }
        GD.Print($"Woodvale art ready: 12 directional poses, 4 monster sprites, {data.Props.Count} props, {data.Cells.Count} terrain cells.");
    }

    private void BuildTerrain(WorldArt data)
    {
        int size = (int)Definition.TileSize;
        var tileset = new TileSet { TileSize = new Vector2I(size, size) };
        tileset.AddPhysicsLayer();
        tileset.SetPhysicsLayerCollisionLayer(0, 1);
        tileset.SetPhysicsLayerCollisionMask(0, 0);
        var atlas = new TileSetAtlasSource { Texture = _art.Texture(data.TerrainAtlas), TextureRegionSize = new Vector2I(size, size) };
        tileset.AddSource(atlas, 0);
        foreach (TerrainTile tile in data.Tiles)
        {
            var coordinate = new Vector2I(tile.AtlasX, tile.AtlasY);
            atlas.CreateTile(coordinate);
            if (tile.Walkable) continue;
            TileData tileData = atlas.GetTileData(coordinate, 0);
            tileData.SetCollisionPolygonsCount(0, 1);
            float half = size / 2f;
            tileData.SetCollisionPolygonPoints(0, 0, new[] { new Vector2(-half, -half), new Vector2(half, -half),
                new Vector2(half, half), new Vector2(-half, half) });
        }
        var ground = new TileMapLayer { Name = "Ground", TileSet = tileset, ZIndex = -2 };
        var water = new TileMapLayer { Name = "WaterAndShore", TileSet = tileset, ZIndex = -1 };
        AddChild(ground); AddChild(water);
        var tiles = data.Tiles.ToDictionary(t => t.Id);
        TerrainTile grass = tiles["grass"];
        foreach (TerrainCell cell in data.Cells)
        {
            TerrainTile tile = tiles[cell.TileId];
            var point = new Vector2I(cell.X, cell.Y);
            ground.SetCell(point, 0, tile.Walkable ? new Vector2I(tile.AtlasX, tile.AtlasY) : new Vector2I(grass.AtlasX, grass.AtlasY));
            if (!tile.Walkable) water.SetCell(point, 0, new Vector2I(tile.AtlasX, tile.AtlasY));
        }
    }

    private static void AddShape(Node parent, WorldRect footprint) => parent.AddChild(new CollisionShape2D
    {
        Name = "Footprint", Shape = new RectangleShape2D { Size = new Vector2(footprint.Width, footprint.Height) },
        Position = new Vector2(footprint.X + footprint.Width / 2, footprint.Y + footprint.Height / 2)
    });

    public void Apply(IReadOnlyDictionary<long, EntitySnapshot> snapshots, IReadOnlyDictionary<long, Vector2> positions,
        IEnumerable<CombatEvent> activity, double snapshotTime, double time)
    {
        foreach (EntitySnapshot snapshot in snapshots.Values)
        {
            if (!_entities.TryGetValue(snapshot.Id, out WorldEntityView? visual))
            {
                visual = new WorldEntityView { Name = $"Entity{snapshot.Id}", TraceVisuals = _traceVisuals };
                visual.Initialize(snapshot.Id, _animations);
                _entities.Add(snapshot.Id, visual); _sorted.AddChild(visual);
            }
        }
        foreach (CombatEvent entry in activity)
        {
            if (entry.Sequence <= _lastEvent) continue;
            _lastEvent = entry.Sequence;
            if (DefenseFeedbackView.Supports(entry.Kind) && time - entry.Time < 1)
            {
                var feedback = new DefenseFeedbackView(); feedback.Initialize(entry); _sorted.AddChild(feedback); _feedback.Add(feedback);
                if (_traceVisuals) GD.Print($"VISUAL defense sequence={entry.Sequence} effect={entry.Kind} owner={feedback.EntityId}");
            }
            if (entry.Kind == CombatEventKind.LevelUp && time - entry.Time < 1.6 && snapshots.ContainsKey(entry.ActorId))
            {
                var sparkle = new Sprite2D { Texture = GD.Load<Texture2D>("res://Assets/UI/woodvale-progression/ui/level_up_effect.png"),
                    Scale = new Vector2(0.28f, 0.28f), ZIndex = 10 };
                _sorted.AddChild(sparkle); _levelUps.Add((entry.ActorId, entry.Time, sparkle));
                if (_traceVisuals) GD.Print($"VISUAL level up sequence={entry.Sequence} entity={entry.ActorId}");
            }
            if (_entities.TryGetValue(entry.ActorId, out WorldEntityView? actor)) actor.AnimationState.Observe(entry);
            if (entry.TargetId != entry.ActorId && _entities.TryGetValue(entry.TargetId, out WorldEntityView? target)) target.AnimationState.Observe(entry);
            float height = snapshots.TryGetValue(entry.TargetId, out EntitySnapshot victim) && victim.Kind == EntityKind.Monster
                ? _art.Entity(victim).GetHeight() : 72;
            foreach (CombatEffectCue cue in _effectPlanner.Observe(entry, time, height))
            {
                // Only render entities in the current loaded world; the dedicated server has no view.
                if (!snapshots.ContainsKey(cue.OwnerId) || cue.TargetId != 0 && !snapshots.ContainsKey(cue.TargetId)) continue;
                var effect = new CombatEffectView { TraceVisuals = _traceVisuals }; effect.Initialize(cue, _animations);
                _sorted.AddChild(effect); _effects.Add(effect);
                if (_traceVisuals) GD.Print($"VISUAL effect sequence={cue.Sequence} effect={cue.Effect}");
            }
        }
        foreach (EntitySnapshot snapshot in snapshots.Values)
            _entities[snapshot.Id].Apply(snapshot, snapshot.Kind == EntityKind.Player ? 91 : _art.Entity(snapshot).GetHeight(),
                positions.GetValueOrDefault(snapshot.Id, Point(snapshot.Position)), snapshotTime, time);
        foreach (long id in _entities.Keys.Where(id => !snapshots.ContainsKey(id)).ToArray())
        { _entities[id].QueueFree(); _entities.Remove(id); }
        foreach (CombatEffectView effect in _effects.ToArray())
        {
            CombatEffectCue cue = effect.Cue;
            if (time >= cue.EndsAt || !_entities.TryGetValue(cue.OwnerId, out WorldEntityView? owner) || owner.AnimationState.Removed
                || cue.TargetId != 0 && (!_entities.TryGetValue(cue.TargetId, out WorldEntityView? target) || target.AnimationState.Removed))
            { effect.QueueFree(); _effects.Remove(effect); }
            else effect.Sample(time);
        }
        foreach (var feedback in _feedback.ToArray())
        {
            if (time - feedback.StartedAt >= 1 || !positions.TryGetValue(feedback.EntityId, out Vector2 position)) { feedback.QueueFree(); _feedback.Remove(feedback); }
            else feedback.Sample(time, position);
        }
        foreach (var effect in _levelUps.ToArray())
        {
            float age = (float)(time - effect.Started);
            if (age >= 1.6f || !positions.TryGetValue(effect.Owner, out Vector2 position))
            { effect.Sprite.QueueFree(); _levelUps.Remove(effect); }
            else
            {
                effect.Sprite.Position = position + new Vector2(0, -84 - age * 14);
                effect.Sprite.Modulate = new Color(1, 1, 1, Math.Clamp((1.6f - age) * 2, 0, 1));
            }
        }
    }

    public long Pick(Vector2 screenPoint, long localId)
    {
        // Pick the frontmost opaque sprite pixel, rather than its transparent 96x96 canvas.
        foreach (WorldEntityView visual in _entities.Values.OrderByDescending(v => v.Position.Y))
            if (visual.Snapshot.Id != localId && visual.HitTest(screenPoint)) return visual.Snapshot.Id;
        return 0;
    }

    public float VisualHeight(long id) => _entities.TryGetValue(id, out WorldEntityView? visual) ? visual.VisualHeight : 91;
    private static Vector2 Point(System.Numerics.Vector2 point) => new(point.X, point.Y);
}
