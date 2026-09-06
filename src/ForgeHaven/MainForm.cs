using System.Diagnostics;
using WinTimer = System.Windows.Forms.Timer;
using System.Drawing;

namespace ForgeHaven;

/// <summary>
///  WinForms shell: owns the game loop timer, ALL input (routed through
///  Game.Queue* commands), and the UI button model. Rendering lives in
///  Renderer; sprites in Sprites.
/// </summary>
public sealed class MainForm : Form
{
    private readonly Game _game = new();
    private ViewState _view;
    private readonly List<UiButton> _buttons = new();
    private readonly HashSet<Keys> _keys = new();
    private readonly WinTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private static readonly float[] Speeds = { 0.5f, 1f, 2f, 4f };
    private int _speedIdx = 1;

    private ToolKind _tool;
    private BuildKind? _toolKind;
    private Dir _facing = Dir.Right;
    private BuildCategory _cat;
    private bool _catOpen;
    private bool _dragging;          // left-drag painting
    private bool _panning;
    private Point _panGrab, _mouse;
    private bool _rallyPending;      // next map click sets a rally point
    private UiSlider? _dragSlider;   // world-gen knob being dragged

    private OverlayMode _ovl;
    private bool _showMinimap = true;
    private string? _modal;          // null / RESEARCH / WORK / DEV / MENU / SETTINGS / LOAD / SAVE
    private bool _showHelp;

    // world-gen state
    private string _seedText = "";
    private readonly float[] _genVals = { 1f, 1f, 1f, 1f };   // freq, rich, rock, aggro
    private int _storyPick;
    private string _genKey = "";
    private Bitmap? _genPreview;

    private float _autosaveT;
    private string? _toast;          // transient status line
    private float _toastT;
    private Font? _toastFont;        // cached (was allocated twice per frame)

    public MainForm()
    {
        Text = "ForgeHaven — prototype v0.4";
        FormBorderStyle = FormBorderStyle.Sizable;
        WindowState = FormWindowState.Maximized;
        BackColor = Pal.Bg;
        DoubleBuffered = true;
        KeyPreview = true;

        _view = NewView();
        Settings.Load();
        _view.DevMode = Settings.Instance.ShowDevMenu;

        _timer = new WinTimer { Interval = 16 };
        _timer.Tick += (s, e) => OnTick();
        _timer.Start();

        MouseWheel += OnWheel;
        KeyDown += OnKeyDown;
        KeyUp += (s, e) => _keys.Remove(e.KeyCode);
        KeyPress += OnKeyPress;
        MouseMove += OnMouseMove;
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
        FormClosed += (s, e) => { Settings.Save(); _genPreview?.Dispose(); _toastFont?.Dispose(); };
    }

    private static ViewState NewView() => new()
    {
        App = AppState.MainMenu,
        Buttons = new List<UiButton>(),
        Sliders = new List<UiSlider>(),
        Alerts = new List<AlertLine>(),
        Sel = new Selection(),
        GhostReason = "",
    };

    // ================================================================ loop ==

    private void OnTick()
    {
        float dt = MathF.Min(0.05f, (float)_clock.Elapsed.TotalSeconds);
        _clock.Restart();

        // camera keys
        float camSpd = 14f * dt / MathF.Max(0.4f, Camera.Zoom);
        if (_keys.Contains(Keys.W) || _keys.Contains(Keys.Up)) Camera.Y -= camSpd;
        if (_keys.Contains(Keys.S) || _keys.Contains(Keys.Down)) Camera.Y += camSpd;
        if (_keys.Contains(Keys.A) || _keys.Contains(Keys.Left)) Camera.X -= camSpd;
        if (_keys.Contains(Keys.D) || _keys.Contains(Keys.Right)) Camera.X += camSpd;

        if (_view.App == AppState.Playing && !_view.Paused)
        {
            _game.Update(dt * Speeds[_speedIdx]);

            if (Settings.Instance.Autosave && !_game.Won && !_game.Lost)
            {
                _autosaveT += dt;
                if (_autosaveT >= Settings.Instance.AutosaveEverySec)
                {
                    _autosaveT = 0;
                    SaveSystem.Save(_game, "autosave.json", "autosave");
                    _toast = "autosaved"; _toastT = 3f;
                }
            }
        }

        if (_toastT > 0) _toastT -= dt;
        UpdateGhost();
        Invalidate();
    }

    // ============================================================== UI map ==

    private void RebuildUi(Size sz)
    {
        _buttons.Clear();
        ref var v = ref _view;
        v.Buttons = _buttons;
        v.Mouse = _mouse;
        v.Tool = _tool;
        v.ToolBuilding = _toolKind;
        v.ToolFacing = _facing;
        v.Paused = _view.Paused;
        v.Speed = Speeds[_speedIdx];
        v.ShowHelp = _showHelp;
        v.Ovl = _ovl;
        v.ShowMinimap = _showMinimap;
        v.ModalTitle = _modal;
        v.DevMode = Settings.Instance.ShowDevMenu;
        v.Sunlight = _game.Sunlight;
        v.Storyteller = _storyPick;
        v.AltDown = (_keys.Contains(Keys.Menu) || _keys.Contains(Keys.Alt)) ||
                    Control.ModifierKeys == Keys.Alt;
        v.ShowGrid = Settings.Instance.ShowTileGrid;
        v.Alerts = _game.Alerts;

        if (v.App == AppState.MainMenu)
        {
            // dialog open? ONLY its buttons exist (same rule as in-game modals)
            if (_modal == "SETTINGS") { SettingsButtons(sz); return; }
            if (_modal == "LOAD") { LoadButtons(sz); return; }
            MainMenuButtons(sz);
            return;
        }
        if (v.App == AppState.WorldGen) { WorldGenButtons(sz); return; }

        PlayingButtons(sz);
    }

    private void MainMenuButtons(Size sz)
    {
        int w = 300, x = sz.Width / 2 - w / 2;
        int y = sz.Height / 2 - 60;
        _buttons.Add(new UiButton(new Rectangle(x, y, w, 44), "mm:new", "NEW COLONY", "generate a world, pick a storyteller"));
        _buttons.Add(new UiButton(new Rectangle(x, y + 52, w, 44), "mm:load", "LOAD GAME", "from " + SaveSystem.SaveDir));
        _buttons.Add(new UiButton(new Rectangle(x, y + 104, w, 44), "mm:settings", "SETTINGS", "toggle dev menu, autosave"));
        _buttons.Add(new UiButton(new Rectangle(x, y + 156, w, 44), "mm:help", "HELP", "controls & survival tips"));
        _buttons.Add(new UiButton(new Rectangle(x, y + 208, w, 44), "mm:quit", "QUIT"));
    }

    private void WorldGenButtons(Size sz)
    {
        ref var v = ref _view;
        int leftW = 400;
        int x0 = sz.Width / 2 - (leftW + 20 + 260) / 2;
        int colX = x0 + 16, colW = leftW - 32;

        v.SeedBox = new UiTextBox { Id = "seed", Label = "WORLD SEED (backspace = reroll)", Text = _seedText, Focused = true };

        v.Sliders.Clear();
        v.Sliders.Add(new UiSlider { Id = "freq", Label = "Ore frequency", Min = 0.4f, Max = 2.5f, Value = _genVals[0] });
        v.Sliders.Add(new UiSlider { Id = "rich", Label = "Ore richness", Min = 0.5f, Max = 2.5f, Value = _genVals[1] });
        v.Sliders.Add(new UiSlider { Id = "rock", Label = "Rock density", Min = 0.3f, Max = 2.5f, Value = _genVals[2] });
        v.Sliders.Add(new UiSlider { Id = "agg", Label = "Wildlife aggression", Min = 0.5f, Max = 2f, Value = _genVals[3] });

        // storyteller picks
        int y = sz.Height - 210;
        var names = new[] { ("THE BUILDER", "gentle pacing — learn the machine"),
                            ("CHAOS THEORY", "random events, random raids"),
                            ("MERCILESS", "big raids, no mercy") };
        for (int i = 0; i < 3; i++)
            _buttons.Add(new UiButton(new Rectangle(colX, y + i * 46, colW, 40), $"story:{i}",
                names[i].Item1, names[i].Item2, active: _storyPick == i));

        int px = x0 + leftW + 20;
        _buttons.Add(new UiButton(new Rectangle(px, sz.Height - 128, 260, 52), "worldgen:go", "LAND HERE", "start the colony"));
        _buttons.Add(new UiButton(new Rectangle(px, sz.Height - 68, 260, 40), "worldgen:back", "BACK"));
    }

    private void PlayingButtons(Size sz)
    {
        ref var v = ref _view;
        v.Sel = _view.Sel;

        // While a modal is open ONLY its own buttons exist — everything else
        // sits under the veil where it can't be clicked or drawn on top.
        if (_modal != null)
        {
            switch (_modal)
            {
                case "RESEARCH": ResearchButtons(sz); break;
                case "WORK": WorkButtons(sz); break;
                case "DEV": DevButtons(sz); break;
                case "MENU": MenuButtons(sz); break;
                case "SETTINGS": SettingsButtons(sz); break;
                case "LOAD": LoadButtons(sz); break;
                case "SAVE": SaveButtons(sz); break;
            }
            if (_game.Won && !_game.ContinueAfterWin)
                _buttons.Add(new UiButton(new Rectangle(sz.Width / 2 - 90, sz.Height / 2 + 40, 180, 40),
                    "win:cont", "KEEP PLAYING"));
            return;
        }

        // ---- bottom architect bar ----
        {
            string[] cats = { "LOGISTICS", "PRODUCTION", "POWER", "DEFENSE", "COLONY" };
            int bw = 108, gap = 4;
            int total = cats.Length * (bw + gap) + 130;
            int x = sz.Width / 2 - total / 2;
            int y = sz.Height - 46;

            for (int i = 0; i < cats.Length; i++)
            {
                _buttons.Add(new UiButton(new Rectangle(x, y, bw, 40), $"cat:{i}",
                    cats[i], "", active: _catOpen && (int)_cat == i, enabled: !(_catOpen && (int)_cat != i) || true));
                x += bw + gap;
            }
            _buttons.Add(new UiButton(new Rectangle(x + 6, y, 56, 40), "tool:x", "X⚡", "bulldoze",
                active: _tool == ToolKind.Bulldoze));
            _buttons.Add(new UiButton(new Rectangle(x + 66, y, 56, 40), "menu:esc", "≡", "menu"));

            if (_catOpen) CategoryTray(sz);
        }

        // ---- right-side vertical toggles ----
        int rx = sz.Width - 118;
        _buttons.Add(new UiButton(new Rectangle(rx, 60, 108, 30), "ui:research", "RESEARCH [T]", active: _modal == "RESEARCH"));
        _buttons.Add(new UiButton(new Rectangle(rx, 94, 108, 30), "ui:work", "WORK [P]", active: _modal == "WORK"));
        _buttons.Add(new UiButton(new Rectangle(rx, 128, 108, 30), "ui:pause", _view.Paused ? "▶ PLAY" : "⏸ PAUSE"));
        _buttons.Add(new UiButton(new Rectangle(rx, 162, 108, 30), "ui:slower", "« slower"));
        _buttons.Add(new UiButton(new Rectangle(rx, 196, 108, 30), "ui:faster", "faster »"));
        _buttons.Add(new UiButton(new Rectangle(rx, 230, 108, 30), "ui:minimap", "MINIMAP [M]", active: _showMinimap));

        // ---- alerts (clickable chips) ----
        int ay = 44;
        foreach (var a in _game.Alerts)
        {
            var b = new UiButton(new Rectangle(10, ay, 312, 22), $"alert:{_game.Alerts.IndexOf(a)}",
                "• " + a.Text) { Tint = a.Col };
            _buttons.Add(b);
            ay += 24;
        }

        // ---- selection actions ----
        SelectionButtons(sz);
    }

    private void CategoryTray(Size sz)
    {
        var kinds = Enum.GetValues<BuildKind>()
            .Where(k => Bal.CategoryOf(k) == _cat && k != BuildKind.Hub).ToList();

        int bw = 104, bh = 44, gap = 4;
        int perRow = Math.Min(9, kinds.Count);
        int x0 = sz.Width / 2 - (perRow * (bw + gap)) / 2;
        int y = sz.Height - 46 - bh - 6;

        int i = 0;
        foreach (var k in kinds)
        {
            int row = i / perRow;
            _buttons.Add(new UiButton(
                new Rectangle(x0 + (i % perRow) * (bw + gap), y - row * (bh + gap), bw, bh),
                $"arch:{(int)k}", Bal.Name(k), Bal.CostText(k),
                enabled: _game.HasMaterials(k), active: _toolKind == k));
            i++;
        }
    }

    private void SelectionButtons(Size sz)
    {
        var sel = _view.Sel;
        int x = sz.Width - 260, y = 270;   // below the right-side toggle stack
        if (sel.B is Fabricator fab)
        {
            for (int r = 0; r < 5; r++)
                _buttons.Add(new UiButton(new Rectangle(x, y + r * 30, 240, 26), $"recipe:{r}",
                    fab.RecipeNameOf(r), active: fab.Recipe == r));
        }
        if (sel.B is FilterSplitter)
            _buttons.Add(new UiButton(new Rectangle(x, y, 240, 26), "act:filter", "CYCLE FILTER"));
        if (sel.B is BotFactory)
        {
            _buttons.Add(new UiButton(new Rectangle(x, y, 240, 26), "act:rally",
                _rallyPending ? "CLICK MAP…" : "SET RALLY POINT"));
        }
        if (sel.Foe is { Downed: true } && !sel.Foe.Captured)
            _buttons.Add(new UiButton(new Rectangle(x, y, 240, 30), "act:capture", "CAPTURE PRISONER",
                "intern at the Hub brig", tint: Pal.Warn));
    }

    private void ResearchButtons(Size sz)
    {
        int w = 640, h = 560;   // must match Renderer.DrawResearchPanel
        var r = new Rectangle(sz.Width / 2 - w / 2, sz.Height / 2 - h / 2 + 20, w, h);
        for (int i = 0; i < Bal.TechCount; i++)
        {
            var row = new Rectangle(r.X + 12, r.Y + 88 + i * 48, r.Width - 24, 44);
            var t = (Tech)i;
            var pre = Bal.TechPrereq(t);
            bool locked = pre != Tech.None && !_game.Has(pre);
            _buttons.Add(new UiButton(row, $"tech:{i}", "", enabled: !locked,
                active: _game.ActiveTech == t && !_game.TechDone[i]));
        }
        _buttons.Add(new UiButton(new Rectangle(r.Right - 110, r.Y + 12, 98, 30), "research:close", "CLOSE"));
    }

    private void WorkButtons(Size sz)
    {
        int w = 660, rows = Math.Min(_game.Cols.Count, 9);
        var r = new Rectangle(sz.Width / 2 - w / 2, sz.Height / 2 - (110 + rows * 34) / 2, w, 110 + rows * 34);
        int nameW = 140, cellW = (w - nameW - 30) / Bal.WorkCount;
        for (int ci = 0; ci < rows; ci++)
        {
            var c = _game.Cols[ci];
            for (int wi = 0; wi < Bal.WorkCount; wi++)
            {
                var cell = new Rectangle(r.X + 20 + nameW + wi * cellW + 4, r.Y + 70 + ci * 34 + 4, cellW - 8, 26);
                int p = c.Priorities[wi];
                var tint = p switch { 1 => Pal.Bad, 2 => Pal.Warn, 3 => Pal.TextDim, _ => Pal.C(70, 76, 84) };
                _buttons.Add(new UiButton(cell, $"work:{ci}:{wi}", p.ToString(), tint: tint));
            }
        }
        _buttons.Add(new UiButton(new Rectangle(r.Right - 110, r.Y + 12, 98, 30), "work:close", "CLOSE"));
    }

    private void DevButtons(Size sz)
    {
        var r = new Rectangle(sz.Width / 2 - 240, sz.Height / 2 - 170, 480, 340);
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 10, 200, 34), "dev:fe", "+200 IRON PLATES"));
        _buttons.Add(new UiButton(new Rectangle(r.X + 224, r.Y + 10, 200, 34), "dev:cu", "+200 COPPER PLATES"));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 50, 200, 34), "dev:sci", "+100 SCIENCE"));
        _buttons.Add(new UiButton(new Rectangle(r.X + 224, r.Y + 50, 200, 34), "dev:packs", "+10 ADV PARTS"));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 90, 200, 34), "dev:tech", "FINISH ACTIVE RESEARCH"));
        _buttons.Add(new UiButton(new Rectangle(r.X + 224, r.Y + 90, 200, 34), "dev:reveal", "REVEAL MAP (8 chunks)"));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 130, 200, 34), "dev:raid", "SPAWN RAID NOW"));
        _buttons.Add(new UiButton(new Rectangle(r.X + 224, r.Y + 130, 200, 34), "dev:god",
            _game.GodMode ? "GOD MODE: ON" : "GOD MODE: OFF", active: _game.GodMode));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 220, 452, 26), "dev:note",
            "dev commands replay identically (command layer)", enabled: false));
        _buttons.Add(new UiButton(new Rectangle(r.Right - 110, r.Y + 280, 98, 30), "dev:close", "CLOSE"));
    }

    private void MenuButtons(Size sz)
    {
        var r = new Rectangle(sz.Width / 2 - 160, sz.Height / 2 - 155, 320, 320);
        var ids = new List<string> { "menu:resume", "menu:save", "menu:load", "menu:settings", "menu:help" };
        var txt = new List<string> { "RESUME", "SAVE GAME", "LOAD GAME", "SETTINGS", "HELP" };
        if (Settings.Instance.ShowDevMenu)
        {
            ids.Add("menu:dev");
            txt.Add("DEV / CHEATS");
        }
        ids.Add("menu:quit");
        txt.Add("QUIT TO MENU");
        for (int i = 0; i < ids.Count; i++)
            _buttons.Add(new UiButton(new Rectangle(r.X, r.Y + i * 44, r.Width, 40), ids[i], txt[i]));
    }

    private void SettingsButtons(Size sz)
    {
        var r = new Rectangle(sz.Width / 2 - 220, sz.Height / 2 - 140, 440, 280);
        var s = Settings.Instance;
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 12, 412, 34), "set:dev",
            s.ShowDevMenu ? "DEV MENU: SHOWN (in-game ≡)" : "DEV MENU: HIDDEN",
            "unlocks the cheat/debug panel — feature #70", active: s.ShowDevMenu));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 52, 412, 34), "set:autosave",
            s.Autosave ? $"AUTOSAVE: every {s.AutosaveEverySec}s" : "AUTOSAVE: OFF", active: s.Autosave));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 92, 412, 34), "set:grid",
            s.ShowTileGrid ? "TILE GRID: ON [G]" : "TILE GRID: OFF [G]", active: s.ShowTileGrid));
        _buttons.Add(new UiButton(new Rectangle(r.Right - 110, r.Bottom - 42, 98, 30), "set:close", "CLOSE"));
    }

    private void LoadButtons(Size sz)
    {
        var r = new Rectangle(sz.Width / 2 - 260, sz.Height / 2 - 180, 520, 360);
        var saves = SaveSystem.ListSaves();
        int y = r.Y + 12;
        if (saves.Count == 0)
            _buttons.Add(new UiButton(new Rectangle(r.X + 14, y, 492, 30), "load:none", "no saves found", enabled: false));
        foreach (var (path, meta) in saves)
        {
            string label = $"{meta.Name}  ·  Day {meta.Day}  ·  {meta.Colonists} colonists  ·  {meta.Wealth} wealth";
            _buttons.Add(new UiButton(new Rectangle(r.X + 14, y, 400, 30), $"load:{path}", label));
            _buttons.Add(new UiButton(new Rectangle(r.X + 420, y, 86, 30), $"del:{path}", "DELETE",
                tint: Pal.Bad));
            y += 34;
            if (y > r.Bottom - 40) break;
        }
        _buttons.Add(new UiButton(new Rectangle(r.Right - 110, r.Bottom - 42, 98, 30), "load:close", "CLOSE"));
    }

    private void SaveButtons(Size sz)
    {
        var r = new Rectangle(sz.Width / 2 - 220, sz.Height / 2 - 120, 440, 240);
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 12, 412, 40), "save:slot1",
            "SAVE SLOT 1", _game.World.P.Seed.ToString()));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 58, 412, 40), "save:slot2",
            "SAVE SLOT 2", "colony-" + _game.Day));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 104, 412, 40), "save:slot3",
            "SAVE SLOT 3", "wealth " + (int)_game.Wealth));
        _buttons.Add(new UiButton(new Rectangle(r.Right - 110, r.Bottom - 42, 98, 30), "save:close", "CLOSE"));
    }

    // ============================================================ input =====

    private UiButton? HitButton(Point p) =>
        _buttons.LastOrDefault(b => b.R.Contains(p) && b.Enabled && b.Id != "dev:note");

    private void OnMouseMove(object? s, MouseEventArgs e)
    {
        _mouse = e.Location;
        var hit = HitButton(e.Location);
        _view.Hover = hit?.Id;

        if (_dragSlider != null)
        {
            SetSliderFromX(_dragSlider, e.X);
            return;
        }

        if (_panning)
        {
            Camera.X -= (e.X - _panGrab.X) / Camera.Sz;
            Camera.Y -= (e.Y - _panGrab.Y) / Camera.Sz;
            _panGrab = e.Location;
        }

        if (_dragging && _tool == ToolKind.Build && _toolKind != null && DragPaintable(_toolKind.Value))
            PaintAt(TileUnder(e.Location));
        if (_dragging && _tool == ToolKind.Bulldoze)
        {
            var (tx, ty) = TileUnder(e.Location);
            _game.QueueBulldoze(tx, ty);
        }

        // minimap drag
        if (_everDownInMini && _showMinimap && Renderer.MinimapRect(ClientSize).Contains(e.Location))
            MiniJump(e.Location);
    }

    private bool _everDownInMini;

    private void MiniJump(Point p)
    {
        var r = Renderer.MinimapRect(ClientSize);
        // invert the minimap transform: px -> world tile
        float cx = Camera.X + (ClientSize.Width / Camera.Sz) / 2;
        float cy = Camera.Y + (ClientSize.Height / Camera.Sz) / 2;
        float px = (float)r.Width / 72f;
        int c0x = (int)(cx / Chunk.S) - 36, c0y = (int)(cy / Chunk.S) - 23;
        float ox = r.X + r.Width / 2f - (c0x + c0x + 72) / 2f * px;
        float oy = r.Y + r.Height / 2f - (c0y + c0y + 46) / 2f * px;
        float wx = ((p.X - ox) / px) * Chunk.S;
        float wy = ((p.Y - oy) / px) * Chunk.S;
        CenterOn(wx, wy);
    }

    private void CenterOn(float wx, float wy)
    {
        Camera.X = wx - (ClientSize.Width / Camera.Sz) / 2;
        Camera.Y = wy - (ClientSize.Height / Camera.Sz) / 2;
    }

    private (int x, int y) TileUnder(Point p)
    {
        var w = Camera.ToWorld(p.X, p.Y);
        return ((int)MathF.Floor(w.X), (int)MathF.Floor(w.Y));
    }

    private void OnMouseDown(object? s, MouseEventArgs e)
    {
        Focus();
        var hit = HitButton(e.Location);
        if (e.Button == MouseButtons.Middle)
        {
            _panning = true; _panGrab = e.Location; return;
        }
        if (e.Button == MouseButtons.Right)
        {
            if (_modal != null) { _modal = null; return; }
            if (_catOpen) { _catOpen = false; return; }
            _rallyPending = false;
            if (_tool != ToolKind.None) { _tool = ToolKind.None; _toolKind = null; return; }
            _view.Sel.Clear();
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        // main-menu help overlay: any click dismisses it
        if (_view.App == AppState.MainMenu && _showHelp)
        {
            _showHelp = false;
            return;
        }

        // world-gen sliders sit under everything else on that screen
        if (_view.App == AppState.WorldGen)
        {
            foreach (var sl in _view.Sliders)
            {
                var track = new Rectangle(sl.R.X, sl.R.Y + 14, sl.R.Width, sl.R.Height - 14);
                if (track.Contains(e.Location))
                {
                    _dragSlider = sl;
                    SetSliderFromX(sl, e.X);
                    return;
                }
            }
        }

        if (_showMinimap && _modal == null && Renderer.MinimapRect(ClientSize).Contains(e.Location))
        {
            _everDownInMini = true;
            MiniJump(e.Location);
            return;
        }

        if (hit != null) { HandleButton(hit.Id); return; }
        if (_modal != null) return;

        var (tx, ty) = TileUnder(e.Location);

        // rally point placement mode
        if (_rallyPending)
        {
            if (_view.Sel.B is BotFactory bf)
            {
                _game.QueueRally(bf.X, bf.Y, tx, ty);
                _toast = "rally set"; _toastT = 2f;
            }
            _rallyPending = false;
            return;
        }

        if (_tool == ToolKind.Build && _toolKind != null)
        {
            PaintAt((tx, ty));
            _dragging = true;
            return;
        }
        if (_tool == ToolKind.Bulldoze)
        {
            _game.QueueBulldoze(tx, ty);
            _dragging = true;
            return;
        }

        ClickSelect(e.Location);
    }

    private void OnMouseUp(object? s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Middle) _panning = false;
        if (e.Button == MouseButtons.Left) { _dragging = false; _everDownInMini = false; _dragSlider = null; }
    }

    /// <summary>World-gen slider: set value from a screen x, write back to _genVals.</summary>
    private void SetSliderFromX(UiSlider sl, int mx)
    {
        float frac = Math.Clamp((mx - (sl.R.X + 6)) / (float)(sl.R.Width - 12), 0f, 1f);
        sl.Value = sl.Min + frac * (sl.Max - sl.Min);
        int idx = sl.Id switch
        {
            "freq" => 0, "rich" => 1, "rock" => 2, _ => 3,
        };
        _genVals[idx] = sl.Value;
    }

    private void OnWheel(object? s, MouseEventArgs e)
    {
        // grab the world point under the cursor BEFORE the zoom changes
        var anchor = Camera.ToWorld(e.X, e.Y);
        float z = Camera.Zoom * (e.Delta > 0 ? 1.15f : 1f / 1.15f);
        Camera.Zoom = Math.Clamp(z, 0.35f, 3f);
        Camera.X = anchor.X - e.X / Camera.Sz;
        Camera.Y = anchor.Y - e.Y / Camera.Sz;
    }

    private void ClickSelect(Point p)
    {
        var wt = Camera.ToWorld(p.X, p.Y);
        foreach (var c in _game.Cols)
        {
            float dx = c.PosX - wt.X, dy = c.PosY - wt.Y;
            if (dx * dx + dy * dy < 0.45f)
            {
                _view.Sel.Clear(); _view.Sel.Col = c; return;
            }
        }
        foreach (var r in _game.Foes)
        {
            float dx = r.PosX - wt.X, dy = r.PosY - wt.Y;
            float rad = r.Apex ? 0.9f : 0.6f;
            if (dx * dx + dy * dy < rad * rad)
            {
                _view.Sel.Clear(); _view.Sel.Foe = r; return;
            }
        }
        var (tx, ty) = ((int)MathF.Floor(wt.X), (int)MathF.Floor(wt.Y));
        if (_game.World.InBounds(tx, ty))
        {
            var b = _game.World.Cell(tx, ty).B ?? _game.World.Cell(tx, ty).B2;
            if (b != null) { _view.Sel.Clear(); _view.Sel.B = b; return; }
        }
        _view.Sel.Clear();
    }

    /// <summary>Place-drag applies to linear/series kinds only.</summary>
    private static bool DragPaintable(BuildKind k) => k is BuildKind.Belt or BuildKind.FastBelt
        or BuildKind.Rail or BuildKind.ElevatedRail
        or BuildKind.Pipe or BuildKind.Wall or BuildKind.PowerPole;

    private void PaintAt((int x, int y) t)
    {
        if (_toolKind == null) return;
        _game.QueuePlace(_toolKind.Value, t.x, t.y, _facing);
    }

    private void UpdateGhost()
    {
        ref var v = ref _view;
        v.GhostVisible = _view.App == AppState.Playing && _modal == null &&
                         (_tool == ToolKind.Build || _tool == ToolKind.Bulldoze) && !_panning;
        if (!v.GhostVisible) { v.GhostReason = ""; return; }

        var (tx, ty) = TileUnder(_mouse);
        v.GhostX = tx; v.GhostY = ty;

        if (_tool == ToolKind.Bulldoze)
        {
            var b = _game.World.InBounds(tx, ty) ? _game.World.Cell(tx, ty).B ?? _game.World.Cell(tx, ty).B2 : null;
            v.GhostValid = b != null && b.Kind != BuildKind.Hub;
            v.GhostReason = v.GhostValid ? "" : "nothing to remove";
            return;
        }

        var k = _toolKind!.Value;
        v.GhostValid = _game.CanPlace(k, tx, ty, out var reason);
        v.GhostReason = v.GhostValid ? "" : reason;
    }

    // ============================================================== keys ====

    private void OnKeyDown(object? s, KeyEventArgs e)
    {
        _keys.Add(e.KeyCode);
        if (_view.App == AppState.WorldGen && e.KeyCode == Keys.Escape)
        {
            _view.App = AppState.MainMenu; return;
        }
        if (_view.App == AppState.MainMenu)
        {
            if (e.KeyCode == Keys.Escape && (_modal != null || _showHelp))
            {
                _modal = null;
                _showHelp = false;
                return;
            }
            if (e.KeyCode == Keys.N) StartWorldGen();
            if (e.KeyCode == Keys.H) _showHelp = !_showHelp;
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Escape:
                if (_showHelp) { _showHelp = false; break; }
                if (_modal != null) _modal = null;
                else if (_tool != ToolKind.None) { _tool = ToolKind.None; _toolKind = null; }
                else if (_catOpen) _catOpen = false;
                else _modal = "MENU";
                break;
            case Keys.Space:
                if (_modal == null) _view.Paused = !_view.Paused;
                break;
            case Keys.R when _modal == null: _facing = DirU.TurnRight(_facing); break;
            case Keys.X when _modal == null: ToggleBulldoze(); break;
            case Keys.T: ToggleModal("RESEARCH"); break;
            case Keys.P: ToggleModal("WORK"); break;
            case Keys.M: _showMinimap = !_showMinimap; break;
            case Keys.G: Settings.Instance.ShowTileGrid = !Settings.Instance.ShowTileGrid; Settings.Save(); break;
            case Keys.N when _game.Lost || _game.Won: StartWorldGen(); break;
            case Keys.H when _modal == null: _showHelp = !_showHelp; break;
            case Keys.Oemcomma: _speedIdx = Math.Max(0, _speedIdx - 1); break;
            case Keys.OemPeriod: _speedIdx = Math.Min(Speeds.Length - 1, _speedIdx + 1); break;
            case Keys.D1 when _modal == null: PickCategory(0); break;
            case Keys.D2 when _modal == null: PickCategory(1); break;
            case Keys.D3 when _modal == null: PickCategory(2); break;
            case Keys.D4 when _modal == null: PickCategory(3); break;
            case Keys.D5 when _modal == null: PickCategory(4); break;
        }

        // F1..F4 = map overlays
        if (e.KeyCode is >= Keys.F1 and <= Keys.F4)
            _ovl = _ovl == (OverlayMode)(e.KeyCode - Keys.F1 + 1)
                ? OverlayMode.None
                : (OverlayMode)(e.KeyCode - Keys.F1 + 1);
    }

    private void OnKeyPress(object? s, KeyPressEventArgs e)
    {
        if (_view.App != AppState.WorldGen) return;
        if (_view.SeedBox?.Focused != true) return;
        if (e.KeyChar == '\r' || e.KeyChar == '\n') { GenerateAndStart(); return; }
        if (e.KeyChar == '\b') _seedText = _seedText.Length > 0 ? _seedText[..^1] : "";
        else if (e.KeyChar >= 32 && _seedText.Length < 19) _seedText += e.KeyChar;
        _view.SeedBox.Text = _seedText;
        e.Handled = true;
    }

    private void ToggleBulldoze()
    {
        if (_tool == ToolKind.Bulldoze) { _tool = ToolKind.None; return; }
        _tool = ToolKind.Bulldoze; _toolKind = null; _catOpen = false;
    }

    private void ToggleModal(string m) => _modal = _modal == m ? null : m;

    private void PickCategory(int i)
    {
        _cat = (BuildCategory)i;
        _catOpen = !_catOpen || (int)_cat != i ? true : false;
        _tool = ToolKind.None; _toolKind = null;
    }

    // ========================================================= buttons =====

    private void HandleButton(string id)
    {
        switch (id)
        {
            case "mm:new": StartWorldGen(); return;
            case "mm:load": _modal = "LOAD"; return;
            case "mm:settings": _modal = "SETTINGS"; return;
            case "mm:help": _showHelp = !_showHelp; return;
            case "mm:quit": Settings.Save(); Close(); return;

            case "worldgen:go": GenerateAndStart(); return;
            case "worldgen:back": _view.App = AppState.MainMenu; return;

            case "menu:esc": _modal = _modal == "MENU" ? null : "MENU"; return;
            case "menu:resume": _modal = null; return;
            case "menu:dev": _modal = "DEV"; return;
            case "menu:save": _modal = "SAVE"; return;
            case "menu:load": _modal = "LOAD"; return;
            case "menu:settings": _modal = "SETTINGS"; return;
            case "menu:help": _showHelp = true; _modal = null; return;
            case "menu:quit":
                _modal = null; _view.App = AppState.MainMenu;
                _view.Paused = false; _tool = ToolKind.None; _toolKind = null;
                return;

            case "ui:research": ToggleModal("RESEARCH"); return;
            case "ui:work": ToggleModal("WORK"); return;
            case "ui:pause": _view.Paused = !_view.Paused; return;
            case "ui:slower": _speedIdx = Math.Max(0, _speedIdx - 1); return;
            case "ui:faster": _speedIdx = Math.Min(Speeds.Length - 1, _speedIdx + 1); return;
            case "ui:minimap": _showMinimap = !_showMinimap; return;

            case "research:close": _modal = null; return;
            case "work:close": _modal = null; return;
            case "dev:close": _modal = null; return;
            case "set:close": _modal = null; Settings.Save(); return;
            case "load:close": _modal = null; return;
            case "save:close": _modal = null; return;

            case "win:cont": _game.ContinueAfterWin = true; return;
        }

        if (id.StartsWith("story:"))
        {
            _storyPick = int.Parse(id[6..]);
            return;
        }
        if (id.StartsWith("cat:"))
        {
            int i = int.Parse(id[4..]);
            _cat = (BuildCategory)i;
            _catOpen = !_catOpen || (int)_cat != i;
            _tool = ToolKind.None; _toolKind = null;
            return;
        }
        if (id.StartsWith("arch:"))
        {
            var k = (BuildKind)int.Parse(id[5..]);
            if (_toolKind == k && _tool == ToolKind.Build) { _tool = ToolKind.None; _toolKind = null; }
            else { _tool = ToolKind.Build; _toolKind = k; _catOpen = false; }
            return;
        }
        if (id.StartsWith("alert:"))
        {
            int i = int.Parse(id[6..]);
            if (i >= 0 && i < _game.Alerts.Count)
                CenterOn(_game.Alerts[i].X, _game.Alerts[i].Y);
            return;
        }
        if (id.StartsWith("tech:"))
        {
            _game.QueueSelectTech((Tech)int.Parse(id[5..]));
            return;
        }
        if (id.StartsWith("recipe:"))
        {
            if (_view.Sel.B is Fabricator fab)
                _game.QueueRecipe(fab.X, fab.Y, int.Parse(id[7..]));
            return;
        }
        if (id.StartsWith("work:"))
        {
            var parts = id[5..].Split(':');
            int ci = int.Parse(parts[0]), wi = int.Parse(parts[1]);
            if (ci < _game.Cols.Count)
            {
                var c = _game.Cols[ci];
                c.Priorities[wi] = c.Priorities[wi] >= 4 ? 1 : c.Priorities[wi] + 1;
            }
            return;
        }
        if (id.StartsWith("load:"))
        {
            var dto = SaveSystem.LoadFile(id[5..]);
            if (dto != null)
            {
                _game.LoadFrom(dto);
                EnterPlayFromLoad();
            }
            return;
        }
        if (id.StartsWith("del:"))
        {
            try { File.Delete(id[4..]); } catch { }
            return;
        }
        if (id.StartsWith("save:slot"))
        {
            string slot = id[5..];
            SaveSystem.Save(_game, slot + ".json", $"{slot} d{_game.Day}");
            _toast = "saved"; _toastT = 3f;
            _modal = null;
            return;
        }

        switch (id)
        {
            // selection actions
            case "act:filter":
                if (_view.Sel.B is FilterSplitter fs)
                    _game.QueueFilter(fs.X, fs.Y, ((int)fs.Filter + 1) % (int)ItemKind.AdvPart);
                return;
            case "act:rally": _rallyPending = true; return;
            case "act:capture":
                if (_view.Sel.Foe is { Downed: true } f)
                {
                    _game.QueueCapture((int)f.PosX, (int)f.PosY);
                    _view.Sel.Clear();
                }
                return;

            // settings
            case "set:dev":
                Settings.Instance.ShowDevMenu = !Settings.Instance.ShowDevMenu;
                Settings.Save();
                return;
            case "set:autosave": Settings.Instance.Autosave = !Settings.Instance.Autosave; Settings.Save(); return;
            case "set:grid": Settings.Instance.ShowTileGrid = !Settings.Instance.ShowTileGrid; Settings.Save(); return;

            // dev menu (#70)
            case "dev:fe": _game.QueueDevGive(ItemKind.IronPlate, 200); return;
            case "dev:cu": _game.QueueDevGive(ItemKind.CopperPlate, 200); return;
            case "dev:sci": _game.QueueDevGive(ItemKind.SciencePack, 100); return;
            case "dev:packs": _game.QueueDevGive(ItemKind.AdvPart, 10); return;
            case "dev:tech": _game.QueueDevResearch(); return;
            case "dev:reveal": _game.QueueDevReveal(); return;
            case "dev:raid": _game.QueueDevRaid(); return;
            case "dev:god": _game.QueueToggleGod(); return;

            case "tool:x": ToggleBulldoze(); return;
        }
    }

    // ===================================================== game lifecycle ==

    private void StartWorldGen()
    {
        _view.App = AppState.WorldGen;
        _genPreview?.Dispose();
        _genPreview = null;
    }

    private GenParams CurrentParams()
    {
        long seed;
        if (_seedText.Length == 0) seed = Random.Shared.NextInt64();
        else if (!long.TryParse(_seedText, out seed)) seed = _seedText.GetHashCode();
        return new GenParams
        {
            Seed = seed,
            OreFreq = _genVals[0],
            OreRich = _genVals[1],
            RockDensity = _genVals[2],
            Aggression = _genVals[3],
        };
    }

    private void GenerateAndStart()
    {
        var p = CurrentParams();
        _game.New(p.Seed, p, (Storyteller)_storyPick);
        _view.App = AppState.Playing;
        _modal = null; _tool = ToolKind.None; _toolKind = null;
        _view.Paused = false; _view.Sel.Clear();
        _speedIdx = 1;
        Camera.X = -6; Camera.Y = -6; Camera.Zoom = 1f;
        CenterOn(4, 4);
        _autosaveT = 0;
    }

    private void EnterPlayFromLoad()
    {
        _view.App = AppState.Playing;
        _modal = null; _tool = ToolKind.None; _toolKind = null;
        _view.Paused = false; _view.Sel.Clear();
        var c = _game.HubRef.CenterTile;
        CenterOn(c.X, c.Y);
    }

    // =========================================================== painting ==

    protected override void OnPaint(PaintEventArgs e)
    {
        RebuildUi(ClientSize);

        // world-gen preview (rebuild when seed or knobs change)
        if (_view.App == AppState.WorldGen)
        {
            if (_seedText.Length == 0)
            {
                // materialize a random seed so LAND HERE matches the preview
                _seedText = Random.Shared.NextInt64().ToString();
            }
            string key = _seedText + "|" +
                string.Join("|", _genVals.Select(f => f.ToString("0.00")));
            if (_genPreview == null || key != _genKey)
            {
                _genPreview?.Dispose();
                _genPreview = BuildGenPreview(CurrentParams());
                _genKey = key;
            }
            _view.GenPreview = _genPreview;
            _view.GenCaption = _genPreview.Tag as string ?? "";
        }

        Renderer.Draw(e.Graphics, _game, _view, ClientSize);

        // toast
        if (_toastT > 0 && _toast != null)
        {
            var g = e.Graphics;
            _toastFont ??= new Font("Segoe UI", 9f, FontStyle.Bold);
            var sz = g.MeasureString(_toast, _toastFont);
            g.FillRectangle(Pal.B(Pal.CA(200, Pal.Panel)), 10, ClientSize.Height - 118, sz.Width + 12, 20);
            g.DrawString(_toast, _toastFont, Pal.B(Pal.Good), 16, ClientSize.Height - 115);
        }

        base.OnPaint(e);
    }

    private Bitmap BuildGenPreview(GenParams p)
    {
        // sampled at half res (130x100), stretched on draw — snappy knob drags
        int W = 130, H = 100;
        var bmp = new Bitmap(W, H);
        var w = new World(p.Clone());
        using (var g = Graphics.FromImage(bmp))
        {
            for (int py = 0; py < H; py++)
                for (int px = 0; px < W; px++)
                {
                    int tx = (px - W / 2) * 6, ty = (py - H / 2) * 6;
                    var t = w.Cell(tx, ty).T;
                    Color c = t switch
                    {
                        Terrain.IronOre => Pal.IronOre,
                        Terrain.CopperOre => Pal.CopperOre,
                        Terrain.Crystal => Pal.Crystal,
                        Terrain.Flora => Pal.Flora,
                        Terrain.Rock => Pal.Rock,
                        Terrain.Water => Pal.Water,
                        _ => ((px + py) & 3) == 0 ? Pal.C(47, 52, 56) : Pal.GroundA,
                    };
                    bmp.SetPixel(px, py, c);
                }
        }
        bmp.Tag = p.Seed.ToString();
        return bmp;
    }
}

// Extension used by the selection panel for recipe names.
internal static class FabUiExtensions
{
    public static string RecipeNameOf(this Fabricator f, int r) => r switch
    {
        0 => "AMMO  (1 iron plate)",
        1 => "ADV PART  (2 iron + 1 crystal)",
        2 => "SCIENCE PACK  (1 iron + 1 copper)",
        3 => "LOGISTIC DRONE  (1 iron + 1 copper)",
        _ => "WAR BOT  (2 iron + 1 copper)",
    };
}
