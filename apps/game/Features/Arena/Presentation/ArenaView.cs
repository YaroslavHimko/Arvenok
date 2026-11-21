using System;
using System.Collections.Generic;
using System.Linq;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

/// <summary>Client input and HUD. All entity state and facing come from server snapshots.</summary>
public partial class ArenaView : Node2D
{
    private static readonly Rect2 WorldViewport = new(20, 102, 960, 364);
    private static readonly Color Ink = new(0.055f, 0.078f, 0.10f);
    private static readonly Color Muted = new(0.67f, 0.77f, 0.77f);
    private static readonly Color Gold = new(0.95f, 0.79f, 0.37f);
    private readonly Dictionary<long, Vector2> _positions = new();
    private readonly Dictionary<long, Vector2> _from = new();
    private double _interpolation;
    private long _revision;
    private bool _showActivity;
    private Vector2 _camera;
    private ForestWorldView _world = null!;
    private ProgressionHud _progression = null!;
    private InventoryHud _inventory = null!;
    private HotbarHud _hotbar = null!;
    public ArenaSession Session { get; set; } = null!;

    public override void _Ready()
    {
        var clip = new Control { Name = "WorldViewport", Position = WorldViewport.Position, Size = WorldViewport.Size,
            ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = -1, TextureFilter = TextureFilterEnum.Nearest };
        _world = new ForestWorldView { Name = "Forest", Definition = Session.World };
        AddChild(clip); clip.AddChild(_world);
        _progression = new ProgressionHud { Name = "Progression", Session = Session };
        AddChild(_progression);
        _inventory = new InventoryHud { Name = "Inventory", Session = Session, Opening = _progression.Close };
        _progression.Opening = _inventory.Close;
        AddChild(_inventory);
        _hotbar = new HotbarHud { Session = Session, ModalBlocked = () => _progression.SkillsVisible || _inventory.BackpackVisible,
            Availability = slot => Session.Entities.TryGetValue(Session.LocalEntityId, out EntitySnapshot me) ? Unavailable(me, slot) : "Waiting for character",
            Activate = slot => { if (Session.Entities.TryGetValue(Session.LocalEntityId, out EntitySnapshot me)) Activate(me, slot); },
            OpeningSettings = () => { _progression.Close(); _inventory.Close(); } };
        AddChild(_hotbar); _progression.Hotbar = _hotbar;
        _progression.Opening = () => { _inventory.Close(); _hotbar.CloseSettings(); };
        _inventory.Opening = () => { _progression.Close(); _hotbar.CloseSettings(); };
    }

    public override void _Process(double delta)
    {
        if (_revision != Session.SnapshotRevision)
        {
            _revision = Session.SnapshotRevision;
            _interpolation = 0;
            foreach (EntitySnapshot entity in Session.Entities.Values)
            {
                Vector2 target = Point(entity.Position);
                Vector2 current = _positions.GetValueOrDefault(entity.Id, target);
                // Teleports/respawns must not slide through obstacles visually.
                _from[entity.Id] = current.DistanceTo(target) > 60 ? target : current;
            }
            foreach (long id in _positions.Keys.Where(id => !Session.Entities.ContainsKey(id)).ToArray())
            { _positions.Remove(id); _from.Remove(id); }
        }
        _interpolation = Math.Min(1, _interpolation + delta * 15);
        foreach (EntitySnapshot entity in Session.Entities.Values)
            _positions[entity.Id] = _from.GetValueOrDefault(entity.Id, Point(entity.Position)).Lerp(Point(entity.Position), (float)_interpolation);
        if (_positions.TryGetValue(Session.LocalEntityId, out Vector2 local))
        {
            _camera = new Vector2(Math.Clamp(local.X, 480, Session.World.Width - 480),
                Math.Clamp(local.Y, WorldViewport.Size.Y / 2, Session.World.Height - WorldViewport.Size.Y / 2));
        }
        _world.Position = WorldViewport.Size / 2 - _camera;
        _world.Apply(Session.Entities, _positions, Session.Activity, Session.SnapshotTime, Session.ServerTime);
        _world.ApplyLoot(Session);
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_progression.SkillsVisible || _inventory.BackpackVisible || _hotbar.SettingsVisible) return;
        if (!Session.Entities.TryGetValue(Session.LocalEntityId, out EntitySnapshot me)) return;
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.F8 && OS.IsDebugBuild()) { _showActivity = !_showActivity; return; }
            if (key.Keycode == Key.Escape) { Session.Send(CommandKind.Stop); return; }
            if (key.Keycode == Key.F)
            {
                GroundLoot? nearby = Session.Loot.Where(l => System.Numerics.Vector2.DistanceSquared(me.Position, l.Position) <= 64 * 64)
                    .OrderBy(l => l.OwnerId != Session.Backpack?.CharacterId && Session.ServerTime < l.PublicAt)
                    .ThenBy(l => System.Numerics.Vector2.DistanceSquared(me.Position, l.Position)).ThenBy(l => l.Id).FirstOrDefault();
                if (nearby is not null) Session.RequestEconomy(EconomyOperation.Pickup, nearby.Id);
                return;
            }
            if (key.Keycode is Key.F1 or Key.F2 or Key.F3 && me.CanChooseClass)
            {
                Session.Send(CommandKind.ChooseClass, characterClass: (CharacterClass)((int)key.Keycode - (int)Key.F1));
                return;
            }
            if (key.Keycode == Key.Space) Activate(me, AbilitySlot.Basic);
        }
        if (@event is not InputEventMouseButton click || !click.Pressed || click.ButtonIndex != MouseButton.Left) return;
        Vector2 screen = GetGlobalMousePosition();
        if (me.CanChooseClass && screen.Y is >= 60 and <= 91)
        {
            for (int i = 0; i < 3; i++) if (ClassRect(i).HasPoint(screen))
                Session.Send(CommandKind.ChooseClass, characterClass: (CharacterClass)i);
            return;
        }
        if (!WorldViewport.HasPoint(screen)) return;
        Guid loot = _world.PickLoot(ScreenToWorld(screen));
        if (loot != Guid.Empty) { Session.RequestEconomy(EconomyOperation.Pickup, loot); return; }
        long selected = _world.Pick(screen, me.Id);
        if (selected != 0) { Session.Send(CommandKind.SelectTarget, selected); return; }
        Session.Send(CommandKind.Move, destination: ScreenToWorld(screen));
    }

    private Vector2 ScreenToWorld(Vector2 screen) => screen - WorldViewport.GetCenter() + _camera;
    private static Vector2 Point(System.Numerics.Vector2 point) => new(point.X, point.Y);
    private static Rect2 ClassRect(int index) => new(20 + index * 155, 60, 145, 31);

    private string Unavailable(EntitySnapshot me, AbilitySlot slot)
    {
        if (!me.Alive) return "Dead";
        string requirement = Session.WeaponRequirement(me.Class, slot); if (requirement.Length > 0) return requirement;
        if (me.Casting || Session.ServerTime < me.RecoveryEndsAt) return "Committed";
        double remaining = me.Cooldowns.EndsAt(slot) - Session.ServerTime;
        if (remaining > 0) return $"{remaining:F1}s";
        AbilityDefinition skill = Session.EffectiveAbility(me.Class, slot);
        if (skill.CastSeconds > 0 && slot != AbilitySlot.Basic && me.Action == ActionState.Moving) return "Stop first";
        if (slot == AbilitySlot.Basic || skill.Effect is AbilityEffect.Damage or AbilityEffect.Interrupt or AbilityEffect.Rush or AbilityEffect.Sidestep)
        {
            if (!Session.Entities.TryGetValue(me.TargetId, out EntitySnapshot target) || !target.Alive) return "Select target";
            if (me.Protected || target.Protected) return "Protected";
            if (slot != AbilitySlot.Basic && skill.Effect != AbilityEffect.Rush && System.Numerics.Vector2.Distance(me.Position, target.Position) > skill.Range) return "Out of range";
        }
        return "";
    }

    private void Activate(EntitySnapshot me, AbilitySlot slot)
    {
        if (Unavailable(me, slot) != "") return;
        // The mobility button aims toward the facing direction; keyboard 2 aims Blink at the cursor.
        Vector2 aim = WorldViewport.HasPoint(GetGlobalMousePosition()) ? ScreenToWorld(GetGlobalMousePosition())
            : Point(me.Position) + Point(me.Facing) * Session.EffectiveAbility(me.Class, slot).Range;
        Session.Send(slot == AbilitySlot.Basic ? CommandKind.Attack : CommandKind.UseAbility,
            me.TargetId, slot, aim);
    }

    public override void _Draw()
    {
        DrawSetTransform(WorldViewport.GetCenter() - _camera);
        foreach (WorldRect region in Session.World.ProtectedRegions)
        {
            DrawRect(Rect(region), new Color(0.42f, 0.76f, 0.61f), false, 3);
            Label(new Vector2(region.X + 18, region.Y + 29), "SANCTUARY", 19, new Color(0.65f, 0.91f, 0.76f));
        }
        Session.Entities.TryGetValue(Session.LocalEntityId, out EntitySnapshot me);
        foreach (EntitySnapshot entity in Session.Entities.Values) DrawEntity(entity, me);
        foreach (CombatEvent entry in Session.Activity)
        {
            double age = Session.ServerTime - entry.Time;
            if (entry.Kind != CombatEventKind.Damage || age < 0 || age > 1.3 || !_positions.TryGetValue(entry.TargetId, out Vector2 position)) continue;
            Label(position + new Vector2(-12, -_world.VisualHeight(entry.TargetId) - 12 - (float)age * 28), $"-{entry.Damage}", 22, Gold);
        }
        DrawSetTransform(Vector2.Zero);
        // Mask geometry outside the scrolling world viewport.
        DrawRect(new Rect2(0, 0, 1000, 102), Ink);
        DrawRect(new Rect2(0, 466, 1000, 184), Ink);
        DrawRect(new Rect2(0, 102, 20, 364), Ink);
        DrawRect(new Rect2(980, 102, 20, 364), Ink);
        DrawRect(WorldViewport, new Color(0.35f, 0.46f, 0.38f), false, 2);
        DrawHud(me);
        if (_showActivity && OS.IsDebugBuild()) DrawActivity();
    }

    private void DrawEntity(EntitySnapshot entity, EntitySnapshot me)
    {
        Vector2 position = _positions.GetValueOrDefault(entity.Id, Point(entity.Position));
        if (entity.Id == me.TargetId) DrawArc(position, 25, 0, Mathf.Tau, 40, Gold, 3);
        float top = -_world.VisualHeight(entity.Id);
        Bar(position + new Vector2(-29, top - 8), new Vector2(58, 6), (float)entity.Hp / entity.MaxHp, new Color(0.43f, 0.84f, 0.49f));
        Label(position + new Vector2(-26, 18), !entity.Alive ? "Defeated" : $"{(entity.Id == me.Id ? "YOU" : entity.Name)}  Lv {entity.Level}", 12, Colors.White);
        if (entity.Casting)
        {
            string skillName = entity.ActiveAbilityName;
            float progress = (float)((Session.ServerTime - entity.CastStartedAt) / Math.Max(0.01, entity.CastEndsAt - entity.CastStartedAt));
            Bar(position + new Vector2(-40, top - 20), new Vector2(80, 6), progress, Gold);
            Label(position + new Vector2(-45, top - 27), skillName, 12, Gold);
        }
    }

    private void DrawHud(EntitySnapshot me)
    {
        Label(new Vector2(20, 28), "ARVENOK  /  SHARED WORLD", 22, Gold);
        Label(new Vector2(20, 50), me.Id == 0 ? Session.Status : $"{me.Name}  |  {me.Class}  |  HP {me.Hp}/{me.MaxHp}", 16, Colors.White);
        if (me.Id == 0) return;
        if (me.CanChooseClass)
            for (int i = 0; i < 3; i++)
            {
                Rect2 rect = ClassRect(i);
                DrawRect(rect, (int)me.Class == i ? new Color(0.29f, 0.38f, 0.31f) : new Color(0.12f, 0.17f, 0.19f));
                Label(rect.Position + new Vector2(9, 21), $"F{i + 1}  {(CharacterClass)i}", 15, Colors.White);
            }
        else Label(new Vector2(20, 80), "Click: move / select  |  Space: attack  |  Esc: stop", 14, Muted);
        string region = me.Protected ? "SANCTUARY / protected" : "WILDERNESS / PvP enabled";
        Label(new Vector2(660, 28), region, 16, me.Protected ? Colors.LightGreen : Gold);
        if (Session.Entities.TryGetValue(me.TargetId, out EntitySnapshot target))
        {
            string type = target.Species?.ToString() ?? target.Class.ToString();
            Label(new Vector2(660, 52), $"{target.Name} / Lv {target.Level} {type}  {target.Hp}/{target.MaxHp}", 15, Colors.White);
            Bar(new Vector2(660, 63), new Vector2(300, 8), (float)target.Hp / target.MaxHp, Gold);
            if (target.Protected) Label(new Vector2(660, 90), "Target is protected", 13, Muted);
        }
        else Label(new Vector2(660, 62), "No target selected", 15, Muted);
        string state = me.AutoAttacking ? "Pursuing / basic attacks active" : me.Action.ToString();
        if (!me.Alive) state = $"Defeated. Returning to sanctuary in {Math.Max(0, me.RespawnAt - Session.ServerTime):F1}s";
        if (me.DefenseEndsAt > Session.ServerTime) state += $"  |  {me.DefenseName} ({me.DefenseEndsAt - Session.ServerTime:F1}s)";
        if (me.Casting)
        {
            state = $"{me.ActiveAbilityName}  {Math.Max(0, me.CastEndsAt - Session.ServerTime):F1}s";
            Bar(new Vector2(20, 519), new Vector2(960, 4), (float)((Session.ServerTime - me.CastStartedAt) / Math.Max(0.01, me.CastEndsAt - me.CastStartedAt)), Gold);
        }
        Label(new Vector2(20, 537), state, 15, Gold);
        CombatEvent feedback = Session.Activity.LastOrDefault(e => Session.ServerTime - e.Time < 5
            && (e.ActorId == me.Id || e.TargetId == me.Id)
            && e.Kind is CombatEventKind.Rejected or CombatEventKind.CastCancelled or CombatEventKind.CastInterrupted or CombatEventKind.Died or CombatEventKind.Respawned);
        string notice = Session.ServerTime - Session.ProgressionMessageAt < 5 ? Session.ProgressionMessage : "";
        if (!_inventory.BackpackVisible && Session.ServerTime - Session.EconomyMessageAt < 6) notice = Session.InventoryMessage;
        if (_hotbar.ActionFeedback.Length > 0) notice = _hotbar.ActionFeedback;
        Label(new Vector2(20, 637), notice.Length > 0 ? notice : feedback.Sequence != 0 ? feedback.Detail : "Click: move / select | Space: attack | 1-4: abilities | I/E: items | F: loot | K: skills | Esc: close / stop | F8: log", 13, Muted);
    }

    private void DrawActivity()
    {
        DrawRect(new Rect2(35, 120, 930, 265), new Color(0.035f, 0.05f, 0.06f, 0.95f));
        Label(new Vector2(49, 144), "RECENT COMBAT / server timestamps / F8 to close", 16, Gold);
        int row = 0;
        foreach (CombatEvent entry in Session.Activity.TakeLast(10))
        {
            string detail = entry.Kind == CombatEventKind.Damage ? $"{entry.Damage} damage" : entry.Detail;
            Label(new Vector2(49, 166 + row++ * 21),
                $"{entry.Time,7:F2}  {entry.ActorId} {(entry.ActorKind == EntityKind.Player ? entry.ActorClass.ToString() : entry.ActorKind.ToString())} > {entry.TargetId}  {entry.Kind}  {entry.AbilityName}  {detail}", 12, Muted);
        }
    }

    private static Rect2 Rect(WorldRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    private void Bar(Vector2 position, Vector2 size, float fraction, Color color)
    {
        DrawRect(new Rect2(position, size), new Color(0.12f, 0.14f, 0.14f));
        DrawRect(new Rect2(position, new Vector2(size.X * Math.Clamp(fraction, 0, 1), size.Y)), color);
    }
    private void Label(Vector2 position, string value, int size, Color color) =>
        DrawString(ThemeDB.FallbackFont, position, value, HorizontalAlignment.Left, -1, size, color);
}
