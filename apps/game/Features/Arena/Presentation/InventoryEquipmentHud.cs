using System;
using System.Linq;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

public partial class InventoryHud
{
    private EquipmentSlot? _selectedEquipment;
    private readonly Button _use = new() { Name = "UseItem", Position = new Vector2(408, 264), Size = new Vector2(160, 30) };
    private readonly EquipmentSlotControl[] _equipmentSlots = new EquipmentSlotControl[3];
    private readonly Label _bonuses = new();
    private readonly VBoxContainer _ingredients = new() { Name = "Ingredients", Position = new Vector2(408, 208), Size = new Vector2(330, 52), MouseFilter = MouseFilterEnum.Ignore };
    private readonly TextureRect _outputIcon = new() { Name = "RecipeOutput", Position = new Vector2(704, 106), Size = new Vector2(32, 32), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Panel _craftedNotice = new() { Name = "CraftedNotice", Position = new Vector2(650, 60), Size = new Vector2(330, 36), MouseFilter = MouseFilterEnum.Ignore };
    private readonly TextureRect _craftedIcon = new() { Position = new Vector2(4, 2), Size = new Vector2(32, 32), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _craftedText = new() { Position = new Vector2(42, 5), MouseFilter = MouseFilterEnum.Ignore };
    private Guid _shownCraft;
    private ulong _noticeUntil;
    private readonly Label[] _ingredientRows = new Label[3];
    private void BuildEquipmentControls()
    {
        _use.Pressed += UseSelected; _panel.AddChild(_use); _panel.AddChild(_ingredients); _panel.AddChild(_outputIcon);
        _ingredients.AddThemeConstantOverride("separation", 0);
        AddChild(_craftedNotice); _craftedNotice.AddChild(_craftedIcon); _craftedNotice.AddChild(_craftedText);
        _craftedText.AddThemeFontSizeOverride("font_size", 14); _craftedText.AddThemeColorOverride("font_color", new Color("8cdb9c")); _craftedNotice.Hide();
        for (int i = 0; i < 3; i++)
        { var row = new Label { MouseFilter = MouseFilterEnum.Ignore }; row.AddThemeFontSizeOverride("font_size", 13); _ingredients.AddChild(row); _ingredientRows[i] = row; }
        var heading = new Label(); Place(heading, new Rect2(760, 57, 180, 28), 18); heading.Text = "Equipment · E";
        foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
        {
            var button = new EquipmentSlotControl { Name = slot.ToString(), Slot = slot, Backpack = this, Position = new Vector2(760, 93 + (int)slot * 56), Size = new Vector2(180, 52), ExpandIcon = true, Alignment = HorizontalAlignment.Left };
            button.AddThemeConstantOverride("icon_max_width", 46); button.AddThemeFontSizeOverride("font_size", 13);
            button.Pressed += () => { _selectedEquipment = slot; _selected = -1; _hovered = -1; _revision = -1; _localFeedback = ""; };
            _panel.AddChild(button); _equipmentSlots[(int)slot] = button;
        }
        Place(_bonuses, new Rect2(760, 262, 180, 68), 14);
    }
    private void RefreshEquipment()
    {
        InventorySnapshot state = Session.Backpack!;
        foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
        {
            ItemStack? item = state.Equipment.At(slot); ItemDefinition? definition = Definition(item); Button button = _equipmentSlots[(int)slot];
            bool reserved = slot == EquipmentSlot.OffHand && Definition(state.Equipment.Weapon)?.Equipment?.TwoHanded == true;
            button.Text = SlotName(slot) + "\n" + (reserved ? "Reserved by MainHand" : definition is null ? "Empty" : "Tier " + definition.Equipment!.Tier);
            button.Icon = ItemIcon(definition); button.Disabled = Session.InventoryPending;
            button.TooltipText = reserved ? "Two-handed MainHand reserves this slot. Unequip it before using a shield." : definition is null ? "Drag compatible equipment here." : ItemTooltip(item!, definition) + "\nSelect to unequip.";
        }
        int damage = Definition(state.Equipment.Weapon)?.Equipment?.FlatDamage ?? 0, hp = Definition(state.Equipment.Body)?.Equipment?.MaxHp ?? 0;
        _bonuses.Text = $"Gear bonuses\nDamage +{damage}   Max HP +{hp}\nSwap out of combat.";
    }
    internal static Texture2D? ItemIcon(ItemDefinition? item) => item is null ? null : GD.Load<Texture2D>($"res://Assets/UI/items/{item.Icon}.{item.IconFormat}");
    private static string SlotName(EquipmentSlot slot) => slot switch { EquipmentSlot.Weapon => "MainHand", EquipmentSlot.Body => "Body Armor", _ => "OffHand" };
    private static string GearText(EquipmentDefinition gear) => $"Tier {gear.Tier} · {(gear.Classes == EquipmentClasses.None ? gear.Class.ToString() : gear.Classes.ToString())} · Level {gear.MinimumLevel}\n{SlotName(gear.Slot)}{(gear.TwoHanded ? " (2H)" : "")} · Damage +{gear.FlatDamage} / Max HP +{gear.MaxHp}";
    public string ItemTooltip(ItemStack item, ItemDefinition definition)
    {
        string text = $"{definition.DisplayName} · {definition.Rarity}\n{definition.Description}\n{CategoryName(definition.Category)} · {item.Quantity} / {definition.MaximumStack}";
        if (definition.Equipment is EquipmentDefinition gear)
        {
            var current = Definition(Session.Backpack?.Equipment.At(gear.Slot))?.Equipment;
            text += "\n" + GearText(gear) + $"\nCompared to equipped: Damage {gear.FlatDamage - (current?.FlatDamage ?? 0):+0;-0;0}, HP {gear.MaxHp - (current?.MaxHp ?? 0):+0;-0;0}";
        }
        return text;
    }
    private void ShowEconomyDetails(ItemStack? hovered, ItemDefinition? hoveredDefinition)
    {
        _use.Hide(); _ingredients.Hide(); _outputIcon.Hide(); _description.Size = new Vector2(329, 47);
        ItemStack? item = _selectedEquipment is EquipmentSlot equipped && _hovered < 0 ? Session.Backpack?.Equipment.At(equipped) : hovered;
        ItemDefinition? definition = Definition(item);
        if (item is null || definition is null || Session.Backpack is not InventorySnapshot state) return;
        _name.Text = definition.DisplayName; _description.Text = definition.Description;
        if (definition.Equipment is EquipmentDefinition gear)
        {
            var current = Definition(state.Equipment.At(gear.Slot))?.Equipment;
            _details.Text = GearText(gear) + $"\n{definition.Rarity} · Compared to equipped:\nDamage {gear.FlatDamage - (current?.FlatDamage ?? 0):+0;-0;0} / HP {gear.MaxHp - (current?.MaxHp ?? 0):+0;-0;0}";
            _use.Text = _selectedEquipment is not null && _hovered < 0 ? "Unequip" : "Equip"; _use.Show();
        }
        if (definition.RecipeId is string recipeId && state.Recipes.FirstOrDefault(r => r.RecipeId == recipeId) is RecipeDefinition recipe)
        {
            ItemDefinition output = state.Catalog.Single(d => d.ItemId == recipe.OutputItemId);
            _outputIcon.Texture = ItemIcon(output); _outputIcon.Show(); _description.Size = new Vector2(289, 47);
            _description.Text = "Makes " + output.DisplayName + ". Consumes this scroll.";
            _details.Text = GearText(output.Equipment!); _ingredients.Show();
            for (int i = 0; i < _ingredientRows.Length; i++)
            {
                Label row = _ingredientRows[i]; row.Visible = i < recipe.Ingredients.Count; if (!row.Visible) continue;
                RecipeIngredient ingredient = recipe.Ingredients[i]; long owned = state.Slots.Where(s => s.Stack.ItemId == ingredient.ItemId).Sum(s => (long)s.Stack.Quantity);
                row.Text = $"{state.Catalog.Single(d => d.ItemId == ingredient.ItemId).DisplayName}: {owned} / {ingredient.Quantity}";
                row.AddThemeColorOverride("font_color", new Color(owned >= ingredient.Quantity ? "8cdb9c" : "f28c86"));
            }
            _use.Text = "Craft / Use scroll"; _use.Show();
        }
        if (definition.SkillNodeId is string nodeId)
        {
            SkillNodeView? node = Session.Progress?.Tree.FirstOrDefault(n => n.Definition.Id == nodeId);
            _details.Text = node is null ? "Spell scroll · another class\nThis scroll cannot teach your class." : $"Spell scroll · Level {node.Definition.RequiredLevel} · 1 point\nRequires {node.Definition.PrerequisiteId.Split('.')[^1].Replace('_', ' ')} rank {node.Definition.PrerequisiteRank}\nWeapon: {node.Definition.WeaponRequirement.Replace('_', ' ')}\n{node.UnmetRequirement}";
            _description.Text = "Teaches an active ability. Consumes this scroll and one skill point only on success.";
            _use.TooltipText = node is null ? "Another class's spell scroll." : node.Definition.Description + "\n" + node.UnmetRequirement;
            _use.Text = "Learn spell"; _use.Show();
        }
        _use.Disabled = Session.InventoryPending || _hovered >= 0 && _hovered != _selected || _splitRow.Visible;
        if (definition.SkillNodeId is string learnId && Session.Progress?.Tree.FirstOrDefault(n => n.Definition.Id == learnId) is not SkillNodeView) _use.Disabled = true;
    }
    private void UpdateCraftNotice()
    {
        if (Session.LastCraftedItem != Guid.Empty && Session.LastCraftedItem != _shownCraft)
        {
            _shownCraft = Session.LastCraftedItem;
            ItemDefinition? item = Definition(Session.Backpack?.Slots.FirstOrDefault(s => s.Stack.Id == _shownCraft)?.Stack);
            if (item is not null) { _craftedIcon.Texture = ItemIcon(item); _craftedText.Text = "Crafted " + item.DisplayName; _noticeUntil = Time.GetTicksMsec() + 4000; }
        }
        _craftedNotice.Visible = Time.GetTicksMsec() < _noticeUntil && Session.HasConnection;
    }
    public void UseSlot(int index) { SelectSlot(index); _hovered = -1; UseSelected(); }
    private void UseSelected()
    {
        if (_selectedEquipment is EquipmentSlot slot && Session.Backpack?.Equipment.At(slot) is ItemStack equipped)
        { Session.RequestEconomy(EconomyOperation.Unequip, equipped.Id, slot); return; }
        ItemStack? item = Entry(_selected); ItemDefinition? definition = Definition(item); if (item is null || definition is null) return;
        if (definition.Equipment is EquipmentDefinition gear) Session.RequestEconomy(EconomyOperation.Equip, item.Id, gear.Slot);
        else if (definition.RecipeId is not null) Session.RequestEconomy(EconomyOperation.Craft, item.Id);
        else if (definition.SkillNodeId is string nodeId) Session.RequestTreeLearn(nodeId, item.Id);
    }
    public bool CanEquipDrop(EquipmentSlot slot, Variant data)
    {
        if (Session.InventoryPending || Session.Backpack is not InventorySnapshot state || data.VariantType != Variant.Type.Dictionary) return false;
        var payload = data.AsGodotDictionary();
        return payload.ContainsKey("inventory") && payload["inventory"].AsString() == state.CharacterId.ToString() && payload.ContainsKey("source")
            && payload.ContainsKey("stack") && payload.ContainsKey("revision") && Definition(Entry(payload["source"].AsInt32()))?.Equipment?.Slot == slot;
    }
    public void EquipDrop(EquipmentSlot slot, Variant data)
    {
        if (!CanEquipDrop(slot, data)) return; var payload = data.AsGodotDictionary();
        if (Guid.TryParse(payload["stack"].AsString(), out Guid id)) Session.SubmitEconomyIntent(new(Guid.NewGuid(), payload["revision"].AsInt64(), EconomyOperation.Equip, id, slot));
    }
}
