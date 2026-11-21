using System;
using System.Collections.Generic;
using System.Linq;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

/// <summary>Backpack controls display only confirmed owner state. Drag data is intent with revision/identity preconditions.</summary>
public partial class InventoryHud : Control
{
    private readonly Panel _panel = new();
    private readonly GridContainer _grid = new() { Columns = 6 };
    private readonly List<InventorySlotControl> _slots = new();
    private readonly Label _title = new(), _name = new(), _description = new(), _details = new(), _feedback = new();
    private readonly Button _sort = new() { Text = "Sort" }, _split = new() { Text = "Split stack" };
    private readonly HBoxContainer _splitRow = new();
    private readonly SpinBox _amount = new() { MinValue = 1, Step = 1, AllowGreater = false, AllowLesser = false };
    private InventoryRequest? _splitRequest;
    private long _revision = -1;
    private bool _pending;
    private int _selected = -1, _hovered = -1;
    private string _localFeedback = "";
    public ArenaSession Session { get; set; } = null!;
    public Action? Opening { get; set; }
    public bool BackpackVisible => _panel.Visible;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest; ZIndex = 21;
        var toggle = new Button { Name = "OpenBackpack", Text = "I · Backpack", Position = new Vector2(500, 60), Size = new Vector2(145, 31) };
        toggle.Pressed += ToggleBackpack; AddChild(toggle);
        _panel.Name = "Backpack"; _panel.Position = new Vector2(20, 108); _panel.Size = new Vector2(960, 352);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("111d24"), BorderColor = new Color("c4a560"),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7, CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7 });
        AddChild(_panel); Place(_title, new Rect2(20, 13, 610, 30), 21);
        var close = new Button { Name = "Close", Text = "Close", Position = new Vector2(862, 12), Size = new Vector2(80, 30) };
        close.Pressed += Close; _panel.AddChild(close);
        var scroll = new ScrollContainer { Name = "Slots", Position = new Vector2(20, 58), Size = new Vector2(370, 234), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _grid.Name = "Grid"; _grid.AddThemeConstantOverride("h_separation", 6); _grid.AddThemeConstantOverride("v_separation", 6);
        _panel.AddChild(scroll); scroll.AddChild(_grid);
        Place(_name, new Rect2(408, 59, 329, 44), 18);
        Place(_description, new Rect2(408, 106, 329, 47), 14);
        Place(_details, new Rect2(408, 157, 329, 100), 14);
        _sort.Position = new Vector2(20, 304); _sort.Size = new Vector2(90, 30); _sort.Name = "Sort";
        _sort.Pressed += () => { _localFeedback = ""; Session.RequestInventoryOperation(InventoryOperation.Sort); }; _panel.AddChild(_sort);
        _split.Position = new Vector2(120, 304); _split.Size = new Vector2(120, 30); _split.Name = "Split";
        _split.Pressed += SplitSelected; _panel.AddChild(_split);
        _splitRow.Name = "SplitQuantity"; _splitRow.Position = new Vector2(408, 264); _splitRow.Size = new Vector2(328, 34);
        _amount.Name = "Amount"; _amount.CustomMinimumSize = new Vector2(120, 30); _splitRow.AddChild(_amount);
        var confirm = new Button { Name = "Confirm", Text = "Split" }; confirm.Pressed += ConfirmSplit; _splitRow.AddChild(confirm);
        var cancel = new Button { Text = "Cancel" }; cancel.Pressed += () => { _splitRequest = null; _splitRow.Hide(); }; _splitRow.AddChild(cancel);
        _panel.AddChild(_splitRow); _splitRow.Hide();
        Place(_feedback, new Rect2(408, 306, 329, 38), 13);
        BuildEquipmentControls();
        _panel.Hide();
    }
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode is Key.I or Key.E || key.Keycode == Key.Escape && BackpackVisible)
        { ToggleBackpack(); GetViewport().SetInputAsHandled(); }
    }
    public void ToggleBackpack()
    { if (_panel.Visible) Close(); else { Opening?.Invoke(); _panel.Show(); _revision = -1; } }
    public void Close()
    { _panel.Hide(); _splitRow.Hide(); _splitRequest = null; GetViewport().GuiReleaseFocus(); }
    public void SelectSlot(int index) { _selectedEquipment = null; _selected = index; _hovered = -1; _revision = -1; _localFeedback = ""; ShowDetails(); }
    public void HoverSlot(int index) { _hovered = index; ShowDetails(); }
    private ItemStack? Entry(int index) => Session.Backpack?.Slots.FirstOrDefault(s => s.Index == index)?.Stack;
    private ItemDefinition? Definition(ItemStack? stack) => stack is null ? null : Session.Backpack?.Catalog.FirstOrDefault(d => d.ItemId == stack.ItemId);
    public override void _Process(double delta)
    {
        UpdateCraftNotice();
        InventorySnapshot? state = Session.Backpack;
        _feedback.Text = Session.InventoryPending ? "Waiting for server…" : _localFeedback.Length > 0 ? _localFeedback : Session.InventoryMessage.Length > 0 ? Session.InventoryMessage : "Drag to move / merge / swap. Shift-drag to split.";
        if (state is null)
        {
            _title.Text = "Backpack · waiting for character"; _sort.Disabled = _split.Disabled = true;
            if (_revision != -1)
            {
                foreach (var slot in _slots) slot.ShowItem(null, null, false, true);
                foreach (var slot in _equipmentSlots) { slot.Icon = null; slot.Disabled = true; slot.Text = slot.Slot.ToString(); }
                _bonuses.Text = ""; _revision = -1; ShowDetails();
            }
            return;
        }
        if (state.Revision == _revision && _pending == Session.InventoryPending) return;
        if (_splitRequest is not null && _splitRequest.ExpectedRevision != state.Revision) { _splitRequest = null; _splitRow.Hide(); }
        _revision = state.Revision; _pending = Session.InventoryPending;
        _title.Text = $"Backpack    {state.Slots.Count} / {state.Capacity} slots";
        if (_slots.Count != state.Capacity)
        {
            foreach (var slot in _slots) { _grid.RemoveChild(slot); slot.QueueFree(); } _slots.Clear();
            for (int i = 0; i < state.Capacity; i++)
            { var slot = new InventorySlotControl { Name = "Slot" + i, SlotIndex = i, Backpack = this }; _grid.AddChild(slot); _slots.Add(slot); }
        }
        foreach (InventorySlotControl slot in _slots) { ItemStack? entry = Entry(slot.SlotIndex); slot.ShowItem(entry, Definition(entry), _selected == slot.SlotIndex, _pending); }
        _sort.Disabled = _pending; _split.Disabled = _pending || Entry(_selected)?.Quantity is null or < 2 || state.Slots.Count == state.Capacity;
        RefreshEquipment();
        ShowDetails();
    }
    private void ShowDetails()
    {
        ItemStack? stack = Entry(_hovered >= 0 ? _hovered : _selected); ItemDefinition? definition = Definition(stack);
        _name.Text = definition?.DisplayName ?? "Select an item";
        _description.Text = definition?.Description ?? "Your backpack stores materials and individual items. Hover to inspect, click to select.";
        _details.Text = stack is null || definition is null ? "Empty slots are available for future rewards.\nNo weight or encumbrance." :
            $"{CategoryName(definition.Category)}\nQuantity: {stack.Quantity}   Stack limit: {definition.MaximumStack}" + (stack.Properties.Count > 0 ? "\n" + string.Join(", ", stack.Properties.Select(p => p.Key + ": " + p.Value)) : "");
        ShowEconomyDetails(stack, definition);
    }
    public static string CategoryName(ItemCategory category) => category switch
    { ItemCategory.RawMaterial => "Raw material", ItemCategory.ProcessedMaterial => "Processed material", ItemCategory.MonsterMaterial => "Monster material",
        ItemCategory.EquipmentKeyPiece => "Equipment key piece", ItemCategory.SpellScroll => "Spell scroll", _ => category.ToString() };
    public Variant DragData(int source)
    {
        if (Session.Backpack is not InventorySnapshot state || Session.InventoryPending || Entry(source) is not ItemStack stack) return default;
        SelectSlot(source);
        return new Godot.Collections.Dictionary { ["inventory"] = state.CharacterId.ToString(), ["source"] = source, ["stack"] = stack.Id.ToString(), ["revision"] = state.Revision };
    }
    public bool CanDrop(int destination, Variant data)
    {
        if (Session.InventoryPending || Session.Backpack is not InventorySnapshot state || data.VariantType != Variant.Type.Dictionary) return false;
        var payload = data.AsGodotDictionary();
        return destination >= 0 && destination < state.Capacity && payload.ContainsKey("inventory") && payload["inventory"].AsString() == state.CharacterId.ToString()
            && payload.ContainsKey("source") && payload["source"].AsInt32() != destination && payload.ContainsKey("stack") && payload.ContainsKey("revision");
    }
    public void DropOn(int destination, Variant data, bool split)
    {
        if (!CanDrop(destination, data)) return;
        _localFeedback = "";
        var payload = data.AsGodotDictionary();
        if (!Guid.TryParse(payload["stack"].AsString(), out Guid sourceId)) return;
        var request = new InventoryRequest(Guid.NewGuid(), payload["revision"].AsInt64(), split ? InventoryOperation.Split : InventoryOperation.Drag,
            payload["source"].AsInt32(), destination, sourceId, Entry(destination)?.Id ?? Guid.Empty);
        if (split) BeginSplit(request); else Session.SubmitInventoryIntent(request);
    }
    private void SplitSelected()
    {
        _localFeedback = "";
        if (Session.Backpack is not InventorySnapshot state || Entry(_selected) is not ItemStack stack) return;
        int destination = Enumerable.Range(0, state.Capacity).FirstOrDefault(i => Entry(i) is null, -1);
        if (destination < 0) return;
        BeginSplit(new InventoryRequest(Guid.NewGuid(), state.Revision, InventoryOperation.Split, _selected, destination, stack.Id));
    }
    private void BeginSplit(InventoryRequest request)
    {
        if (Entry(request.Source) is not ItemStack stack || stack.Quantity < 2 || Entry(request.Destination) is not null)
        { _localFeedback = "Split needs at least two items and an empty destination."; return; }
        _splitRequest = request; _amount.MaxValue = stack.Quantity - 1; _amount.Value = Math.Max(1, stack.Quantity / 2); _splitRow.Show();
    }
    private void ConfirmSplit()
    {
        if (_splitRequest is not null) Session.SubmitInventoryIntent(_splitRequest with { Quantity = (int)_amount.Value });
        _splitRequest = null; _splitRow.Hide();
    }
    private void Place(Label label, Rect2 rect, int font)
    {
        label.Position = rect.Position; label.Size = rect.Size; label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.MouseFilter = MouseFilterEnum.Ignore; label.AddThemeFontSizeOverride("font_size", font);
        label.AddThemeColorOverride("font_color", new Color("f3e5c9")); _panel.AddChild(label);
    }
}
