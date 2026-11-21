using System;
using System.Linq;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

public partial class ProgressionHud
{
    private readonly Panel _treeWindow = new() { Name = "SkillTree", Position = new Vector2(20, 104), Size = new Vector2(960, 355) };
    private readonly Label _treeTitle = new(), _treeStatus = new();
    private readonly RichTextLabel _treeDetail = new() { Name = "Details", Position = new Vector2(640, 97), Size = new Vector2(292, 165), ScrollActive = true };
    private readonly Button _learn = new() { Name = "Learn", Text = "Learn · 1 point", Position = new Vector2(640, 270), Size = new Vector2(290, 32) };
    private readonly Button[] _nodes = new Button[4];
    private readonly SkillDragButton[] _treeIcons = new SkillDragButton[4], _roots = new SkillDragButton[4];
    private readonly Label[] _branchNames = new Label[2];
    private string _selectedNode = "";
    private long _treeRevision = -1, _treeInventoryRevision = -1;
    private void BuildTree()
    {
        _treeWindow.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("111d24"), BorderColor = new Color("c4a560"), BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2 });
        AddChild(_treeWindow); _treeWindow.Hide();
        Place(_treeWindow, _treeTitle, new Rect2(18, 8, 680, 28), 18);
        var core = new Button { Text = "Core ranks", Position = new Vector2(734, 8), Size = new Vector2(110, 28) };
        core.Pressed += () => { _treeWindow.Hide(); _skills.Show(); }; _treeWindow.AddChild(core);
        var close = new Button { Text = "Close", Position = new Vector2(854, 8), Size = new Vector2(85, 28) }; close.Pressed += Close; _treeWindow.AddChild(close);
        for (int i = 0; i < 4; i++)
        {
            _roots[i] = new SkillDragButton { Name = "Core" + i, Position = new Vector2(18 + i * 235, 44), Size = new Vector2(218, 42), ExpandIcon = true };
            _roots[i].AddThemeConstantOverride("icon_max_width", 32); _roots[i].AddThemeFontSizeOverride("font_size", 12); _treeWindow.AddChild(_roots[i]);
        }
        for (int branch = 0; branch < 2; branch++)
        {
            int x = 18 + branch * 302;
            _branchNames[branch] = new Label(); Place(_treeWindow, _branchNames[branch], new Rect2(x, 94, 282, 24), 17, true);
            var connector = new ColorRect { Position = new Vector2(x + 140, 168), Size = new Vector2(2, 53), Color = new Color("ae996a"), MouseFilter = MouseFilterEnum.Ignore }; _treeWindow.AddChild(connector);
            for (int row = 0; row < 2; row++)
            {
                int index = branch * 2 + row;
                var button = new Button { Name = "Node" + index, Position = new Vector2(x, 123 + row * 91), Size = new Vector2(282, 64), Alignment = HorizontalAlignment.Left };
                button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += () => { _selectedNode = Session.Progress!.Tree[index].Definition.Id; RefreshTree(); };
                _treeWindow.AddChild(button); _nodes[index] = button;
                var icon = new SkillDragButton { Name = "Icon" + index, Position = new Vector2(x + 5, 132 + row * 91), Size = new Vector2(46, 46), ExpandIcon = true };
                icon.AddThemeConstantOverride("icon_max_width", 38); icon.Pressed += () => { _selectedNode = Session.Progress!.Tree[index].Definition.Id; RefreshTree(); };
                _treeWindow.AddChild(icon); _treeIcons[index] = icon;
            }
        }
        _treeDetail.AddThemeFontSizeOverride("normal_font_size", 13); _treeDetail.AddThemeColorOverride("default_color", new Color("f3e5c9")); _treeWindow.AddChild(_treeDetail);
        _treeWindow.AddChild(_learn); _learn.Pressed += () => Session.RequestTreeLearn(_selectedNode);
        Place(_treeWindow, _treeStatus, new Rect2(18, 307, 914, 40), 13); _treeStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }
    private void UpdateTree()
    {
        if (!_treeWindow.Visible || Session.Progress is not ProgressionSnapshot state) return;
        if (state.Revision != _treeRevision || Session.Backpack?.Revision != _treeInventoryRevision) RefreshTree();
        _learn.Disabled = Session.InventoryPending || Session.UpgradePending || Session.Progress.Tree.FirstOrDefault(n => n.Definition.Id == _selectedNode) is not SkillNodeView selected || TreeRequirement(selected).Length > 0;
        _treeStatus.Text = Session.ServerTime - Session.EconomyMessageAt < 5 ? Session.InventoryMessage : "Each node costs 1 point. Active nodes also consume their spell scroll. Learn out of combat. Drag learned active icons to a hotbar.";
    }
    private string TreeRequirement(SkillNodeView node)
    {
        if (node.Rank > 0) return "Already learned.";
        // Ownership may arrive before the next progression update, so use the current private backpack.
        if (node.UnmetRequirement == "Matching spell scroll required." && Session.Backpack?.Slots.Any(s => s.Stack.ItemId == node.Definition.ScrollItemId) == true) return "";
        if (node.UnmetRequirement.Length > 0) return node.UnmetRequirement;
        return node.Definition.ScrollItemId is string scroll && Session.Backpack?.Slots.Any(s => s.Stack.ItemId == scroll) != true ? "Matching spell scroll required." : "";
    }
    private void RefreshTree()
    {
        if (Session.Progress is not ProgressionSnapshot state) return;
        _treeRevision = state.Revision; _treeInventoryRevision = Session.Backpack?.Revision ?? -1;
        _treeTitle.Text = $"{state.Class} skill tree · Level {state.Level} · {state.SkillPoints} points";
        for (int i = 0; i < 4; i++)
        {
            var skill = state.Skills[i]; _roots[i].Hud = Hotbar; _roots[i].ActionId = skill.Id; _roots[i].Text = skill.Current.Name + " · " + skill.Rank + "/4";
            _roots[i].Icon = GD.Load<Texture2D>(HotbarHud.Art + "skills/" + skill.Id.Split('.')[^1].Replace('-', '_') + ".png");
            _roots[i].TooltipText = Describe(skill.Current) + "\nCore ranks: open Core ranks to compare and upgrade.";
            SkillNodeView node = state.Tree[i]; var data = node.Definition;
            _nodes[i].Text = "             " + data.Name + "\n             " + (node.Rank > 0 ? "Learned · 1/1" : "Level " + data.RequiredLevel + " · " + (data.Passive ? "Passive" : "Active"));
            _nodes[i].TooltipText = data.Description + "\n" + TreeRequirement(node);
            _nodes[i].Modulate = node.Rank > 0 ? new Color("95dfae") : TreeRequirement(node).Length == 0 ? new Color("ffd888") : Colors.White;
            _treeIcons[i].Hud = Hotbar; _treeIcons[i].ActionId = !data.Passive && node.Rank > 0 ? data.SkillId : ""; _treeIcons[i].Icon = GD.Load<Texture2D>(HotbarHud.Art + data.Icon);
            _treeIcons[i].TooltipText = _nodes[i].TooltipText;
            if (i % 2 == 0) _branchNames[i / 2].Text = char.ToUpperInvariant(data.Branch[0]) + data.Branch[1..];
        }
        SkillNodeView selected = state.Tree.FirstOrDefault(n => n.Definition.Id == _selectedNode) ?? state.Tree[0]; _selectedNode = selected.Definition.Id;
        var d = selected.Definition;
        _treeDetail.Text = d.Name + " · Rank " + selected.Rank + "/1\n" + d.Description + (d.Ability is null ? "" : "\n" + Describe(d.Ability)) + "\nLevel " + d.RequiredLevel + " · 1 point\nRequires " + d.PrerequisiteId.Split('.')[^1].Replace('_', ' ').Replace('-', ' ') + " rank " + d.PrerequisiteRank
            + (d.WeaponRequirement.Length > 0 ? "\nWeapon: " + d.WeaponRequirement.Replace('_', ' ') : "") + (d.ScrollItemId is not null ? "\nSpell scroll: " + (Session.Backpack?.Slots.Any(s => s.Stack.ItemId == d.ScrollItemId) == true ? "owned" : "needed") : "");
        _learn.Text = selected.Rank > 0 ? "Learned" : d.Passive ? "Learn · 1 point" : "Learn · scroll + 1 point"; _learn.TooltipText = TreeRequirement(selected);
    }
}
