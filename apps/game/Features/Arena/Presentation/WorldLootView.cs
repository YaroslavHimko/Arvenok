using System;
using System.Collections.Generic;
using System.Linq;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

public partial class ForestWorldView
{
    private readonly Dictionary<Guid, (Node2D Root, Label Label, Control Tooltip)> _lootViews = new();
    public void ApplyLoot(ArenaSession session)
    {
        var current = session.Loot.Select(l => l.Id).ToHashSet();
        foreach (Guid id in _lootViews.Keys.Where(id => !current.Contains(id)).ToArray())
        { _lootViews[id].Root.QueueFree(); _lootViews.Remove(id); }
        foreach (GroundLoot drop in session.Loot)
        {
            if (!_lootViews.TryGetValue(drop.Id, out var view))
            {
                var root = new Node2D { Name = "Loot_" + drop.Id.ToString("N"), Position = Point(drop.Position) };
                root.AddChild(new Sprite2D { Texture = GD.Load<Texture2D>("res://Assets/UI/items/loot_bag.png"), Scale = new Vector2(0.3f, 0.3f), Position = new Vector2(0, -10) });
                var label = new Label { Position = new Vector2(-64, -41), Size = new Vector2(128, 18), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
                label.AddThemeFontSizeOverride("font_size", 10); label.AddThemeColorOverride("font_shadow_color", Colors.Black); label.AddThemeConstantOverride("shadow_offset_x", 1); label.AddThemeConstantOverride("shadow_offset_y", 1);
                var tooltip = new Control { Position = new Vector2(-14, -28), Size = new Vector2(28, 35), MouseFilter = Control.MouseFilterEnum.Pass };
                root.AddChild(label); root.AddChild(tooltip); _sorted.AddChild(root); view = (root, label, tooltip); _lootViews.Add(drop.Id, view);
            }
            ItemDefinition? item = session.Backpack?.Catalog.FirstOrDefault(i => i.ItemId == drop.Item.ItemId);
            bool mine = session.Backpack?.CharacterId == drop.OwnerId, open = session.ServerTime >= drop.PublicAt;
            view.Label.Text = (item?.DisplayName ?? drop.Item.ItemId) + (drop.Item.Quantity > 1 ? " ×" + drop.Item.Quantity : "");
            view.Label.Modulate = !mine && !open ? new Color("aaaaaa") : item?.Rarity == "uncommon" ? new Color("87baff") : new Color("f4e4b8");
            view.Tooltip.TooltipText = view.Label.Text + (open ? "\nPublic pickup" : mine ? "\nYour protected loot" : $"\nProtected for {Math.Max(0, drop.PublicAt - session.ServerTime):0}s") + "\nClick or press F nearby to pick up.";
        }
    }
    public Guid PickLoot(Vector2 world) => _lootViews.OrderBy(p => p.Value.Root.Position.DistanceSquaredTo(world)).ThenBy(p => p.Key)
        .Where(p => p.Value.Root.Position.DistanceSquaredTo(world) <= 24 * 24).Select(p => p.Key).FirstOrDefault();
}
