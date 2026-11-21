using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

public partial class EquipmentSlotControl : Button
{
    public InventoryHud Backpack { get; set; } = null!;
    public EquipmentSlot Slot { get; set; }
    public override bool _CanDropData(Vector2 position, Variant data) => Backpack.CanEquipDrop(Slot, data);
    public override void _DropData(Vector2 position, Variant data) => Backpack.EquipDrop(Slot, data);
}
