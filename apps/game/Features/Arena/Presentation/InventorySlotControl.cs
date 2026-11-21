using System;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

public partial class InventorySlotControl : Button
{
    public InventoryHud Backpack { get; set; } = null!;
    public int SlotIndex { get; set; }
    private readonly TextureRect _icon = new() { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore,
        Position = new Vector2(10, 6), Size = new Vector2(36, 36), TextureFilter = TextureFilterEnum.Nearest };
    private readonly Label _quantity = new() { Name = "Quantity", Position = new Vector2(1, 35), Size = new Vector2(51, 17), HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = MouseFilterEnum.Ignore };
    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(54, 54); MouseDefaultCursorShape = CursorShape.PointingHand;
        AddChild(_icon); AddChild(_quantity); _quantity.AddThemeFontSizeOverride("font_size", 13);
        _quantity.AddThemeColorOverride("font_shadow_color", Colors.Black); _quantity.AddThemeConstantOverride("shadow_offset_x", 1);
        Pressed += () => Backpack.SelectSlot(SlotIndex);
        GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, DoubleClick: true, ButtonIndex: MouseButton.Left }) Backpack.UseSlot(SlotIndex); };
        MouseEntered += () => Backpack.HoverSlot(SlotIndex);
        MouseExited += () => Backpack.HoverSlot(-1);
    }
    public void ShowItem(ItemStack? stack, ItemDefinition? definition, bool selected, bool pending)
    {
        Disabled = pending;
        _icon.Texture = definition is null ? null : GD.Load<Texture2D>($"res://Assets/UI/items/{definition.Icon}.{definition.IconFormat}");
        _quantity.Text = stack is null || definition!.MaximumStack == 1 ? "" : stack.Quantity.ToString();
        TooltipText = stack is null ? $"Empty slot {SlotIndex + 1}" : Backpack.ItemTooltip(stack, definition!);
        var style = new StyleBoxFlat { BgColor = new Color("17262d"), BorderColor = new Color(selected ? "f3cd72" : definition?.Rarity == "uncommon" ? "528cdd" : "45616a"),
            BorderWidthLeft = selected ? 2 : 1, BorderWidthRight = selected ? 2 : 1, BorderWidthTop = selected ? 2 : 1, BorderWidthBottom = selected ? 2 : 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 };
        AddThemeStyleboxOverride("normal", style);
        var hover = (StyleBoxFlat)style.Duplicate(); hover.BorderColor = new Color("b5d8d5"); hover.BgColor = new Color("263b42");
        AddThemeStyleboxOverride("hover", hover); AddThemeStyleboxOverride("focus", hover);
    }
    public override Variant _GetDragData(Vector2 position)
    {
        var payload = Backpack.DragData(SlotIndex);
        if (payload.VariantType == Variant.Type.Nil) return payload;
        var preview = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Texture = _icon.Texture,
            CustomMinimumSize = new Vector2(42, 42), TextureFilter = TextureFilterEnum.Nearest, Modulate = new Color(1, 1, 1, 0.8f) };
        SetDragPreview(preview); return payload;
    }
    public override bool _CanDropData(Vector2 position, Variant data) => Backpack.CanDrop(SlotIndex, data);
    public override void _DropData(Vector2 position, Variant data) => Backpack.DropOn(SlotIndex, data, Input.IsKeyPressed(Key.Shift));
}
