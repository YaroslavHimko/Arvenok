using System;
using System.Linq;
using Arvenok.Contracts;
using Godot;

namespace Arvenok.Game.Features.Arena.Presentation;

public partial class AccountScreens : Control
{
    public ArenaSession Session { get; set; } = null!;
    private readonly Panel _panel = new() { Position = new(100, 65), Size = new(800, 505) };
    private readonly Label _status = new() { Position = new(100, 580), Size = new(800, 50), AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly HBoxContainer _worldMenu = new() { Position = new(670, 42), Size = new(310, 28) };
    private readonly ColorRect _background = new() { Color = new("0b141b"), Size = new(1000,650), MouseFilter = MouseFilterEnum.Ignore };
    private Button? _confirmAction;
    private Func<bool>? _canConfirm;
    private LineEdit _username = null!, _password = null!, _confirm = null!, _nickname = null!;
    private OptionButton _class = null!;
    private Guid _selected;
    private string _page = "login";
    private AccountReply? _shown;
    private bool _wasWorld;
    private string _validation = "";
    public override void _Ready()
    {
        ZIndex = 30; MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest;
        AddChild(_background); AddChild(_panel); AddChild(_status); AddChild(_worldMenu);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("111d24"), BorderColor = new Color("c4a560"),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2 });
        foreach (var action in new[] { AccountOperation.Return, AccountOperation.Logout })
        {
            var button = new Button { Text = action == AccountOperation.Return ? "Character selection" : "Logout" };
            button.Pressed += () => Session.AccountAction(action); _worldMenu.AddChild(button);
        }
        Render();
    }
    public override void _Process(double delta)
    {
        bool world = Session.LocalEntityId != 0;
        _worldMenu.Visible = world; _panel.Visible = !world; _status.Visible = !world;
        _background.Visible = !world;
        _status.Text = _validation.Length > 0 ? _validation : Session.Status;
        if (_shown != Session.AccountState || world != _wasWorld)
        {
            _shown = Session.AccountState; _wasWorld = world; _validation = "";
            _page = _shown?.Authenticated == true ? "selection" : "login"; Render();
        }
        foreach (Button button in _panel.FindChildren("*", "Button", true, false).OfType<Button>()) button.Disabled = Session.AccountBusy;
        if (_confirmAction is not null) _confirmAction.Disabled = Session.AccountBusy || !Session.HasConnection || _canConfirm?.Invoke() == false;
        foreach (Button button in _worldMenu.GetChildren().OfType<Button>()) button.Disabled = Session.AccountBusy;
    }
    private Label Text(string text, float x, float y, float width = 700, int size = 18)
    {
        var label = new Label { Text = text, Position = new(x,y), Size = new(width,30), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size); _panel.AddChild(label); return label;
    }
    private Button Button(string text, float x, float y, Action action, float width = 180)
    {
        var button = new Button { Text = text, Position = new(x,y), Size = new(width,36) };
        button.Pressed += action; _panel.AddChild(button); return button;
    }
    private LineEdit Field(string placeholder, float y, bool secret = false)
    {
        var input = new LineEdit { PlaceholderText = placeholder, Secret = secret, Position = new(200,y), Size = new(400,36), MaxLength = secret ? 128 : 32 };
        _panel.AddChild(input); return input;
    }
    private void Preview(CharacterClass kind, Vector2 position)
    {
        string file = Session.World.Art!.Characters.Single(c => c.Class == kind).Directions["down"];
        var picture = new TextureRect { Texture = GD.Load<Texture2D>("res://Assets/Art2D/woodvale/" + file), Position = position, Size = new(80,80),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore };
        _panel.AddChild(picture);
    }
    private void Render()
    {
        foreach (Node child in _panel.GetChildren()) { _panel.RemoveChild(child); child.QueueFree(); }
        _confirmAction = null; _canConfirm = null;
        Text("ARVENOK", 30, 20, size: 28);
        if (_page is "login" or "register")
        {
            bool registration = _page == "register";
            Text(registration ? "Create account" : "Log in", 200, 80);
            _username = Field("Username", 130); _password = Field("Password", 182, true);
            _confirm = Field("Confirm password", 234, true); _confirm.Visible = registration;
            var reveal = new CheckButton { Text = "Show password", Position = new(200,280) };
            reveal.Toggled += visible => { _password.Secret = !visible; _confirm.Secret = !visible; }; _panel.AddChild(reveal);
            _confirmAction = Button(registration ? "Register" : "Login", 200,330, () =>
            {
                if (registration && _password.Text != _confirm.Text) { _validation = "Passwords do not match."; _confirm.GrabFocus(); return; }
                _validation = "";
                Session.AccountAction(registration ? AccountOperation.Register : AccountOperation.Login, username: _username.Text, password: _password.Text);
                _password.Clear(); _confirm.Clear();
            });
            _canConfirm = () => _username.Text.Trim().Length >= 3 && _password.Text.Length >= (registration ? 12 : 1)
                && (!registration || _password.Text == _confirm.Text);
            Button(registration ? "Back to login" : "Register account", 420,330, () => { _page = registration ? "login" : "register"; Render(); });
            Text("Username 3–32 letters, digits or underscores. Password 12–128 characters.",200,390,400,14);
            Button("Reconnect",200,445,Session.ReconnectAccount,400);
            return;
        }
        CharacterCard[] cards = Session.AccountState?.Characters ?? Array.Empty<CharacterCard>();
        if (_page == "create")
        {
            Text("Create character",200,82); _nickname = Field("Nickname",132); _nickname.MaxLength = 16;
            _class = new OptionButton { Position = new(200,185), Size = new(400,36) };
            foreach (string name in Enum.GetNames<CharacterClass>()) _class.AddItem(name);
            _panel.AddChild(_class); Preview(CharacterClass.Warrior,new(360,240));
            _class.ItemSelected += index =>
            {
                foreach (TextureRect image in _panel.GetChildren().OfType<TextureRect>()) { _panel.RemoveChild(image); image.QueueFree(); }
                Preview((CharacterClass)index,new(360,240));
            };
            Text("3–16 letters, digits or underscores. Start with a letter.",200,330,400,14);
            _confirmAction = Button("Create",200,390,() => Session.AccountAction(AccountOperation.Create,nickname:_nickname.Text,kind:(CharacterClass)_class.Selected));
            _canConfirm = () => System.Text.RegularExpressions.Regex.IsMatch(_nickname.Text.Trim(),"^[A-Za-z][A-Za-z0-9_]{2,15}$");
            Button("Cancel",420,390,() => { _page="selection";Render(); }); return;
        }
        var selected = cards.FirstOrDefault(c => c.Id == _selected);
        if (_page == "delete" && selected is not null)
        {
            Text("Permanently delete " + selected.Nickname + "?",50,90);
            Text($"{selected.Class} · Level {selected.Level}\nAll EXP, inventory, equipment, skills, scrolls, crafting records and saved position will be permanently lost. Type the exact nickname to confirm.",50,140,700).Size = new(700,120);
            _nickname = Field("Type " + selected.Nickname,280); _nickname.MaxLength=16;
            _confirmAction = Button("Permanently delete",200,390,() => Session.AccountAction(AccountOperation.Delete,selected.Id,_nickname.Text));
            _canConfirm = () => _nickname.Text == selected.Nickname;
            Button("Cancel",420,390,() => { _page="selection";Render(); }); return;
        }
        Text($"Characters · {cards.Length} / 6",30,65);
        for(int i=0;i<6;i++)
        {
            int x=30+(i%3)*250, y=110+(i/3)*145;
            if(i<cards.Length)
            {
                var card=cards[i];
                var cardButton = Button($"{card.Nickname}\n{card.Class} · Level {card.Level}",x,y,() => { _selected=card.Id;Render(); },230);
                cardButton.Size=new(230,130); cardButton.Alignment = HorizontalAlignment.Left; cardButton.AddThemeFontSizeOverride("font_size",14);
                Preview(card.Class,new(x+150,y+45));
                if(_selected==card.Id) Text("Selected",x+10,y+96,130,14);
            }
            else Button("Create character",x,y,() => { _page="create";Render(); },230).Size=new(230,130);
        }
        if(selected is not null)
        {
            Button("Enter world",30,425,() => Session.AccountAction(AccountOperation.Enter,selected.Id),225);
            Button("Delete character",280,425,() => { _page="delete";Render(); },225);
        }
        Button("Logout",530,425,() => Session.AccountAction(AccountOperation.Logout),230);
    }
}
