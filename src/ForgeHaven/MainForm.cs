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
    private bool _selecting;         // left-drag pawn box-select
    private bool _resOpen = true;    // collapsible stock panel (top-left, open by default)
    // LANDING pacing (fractions of the whole): fall 42%, shake 12%, pawns 46%
    private const float LandingDur = 6.0f;
    private const float LandFallEnd = 0.42f, LandShakeEnd = 0.54f, LandPawnStart = 0.55f, LandPawnStep = 0.06f;
    private float _landingT;         // >0: the landing cinematic is playing
    private string? _dragPanel;      // panel being dragged ("pawn"/"stock"/"sel")
    private Point _dragPanelGrab;
    private Point _selFrom;
    private readonly Stopwatch _renderClock = Stopwatch.StartNew();
    private readonly Stopwatch _fpsClock = Stopwatch.StartNew();
    private static readonly int[] FpsChoices = { 0, 30, 60, 120, 144 };
    private static readonly (int w, int h)[] ResChoices = { (1280, 720), (1600, 900), (1920, 1080), (2560, 1440) };
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
    private readonly float[] _genVals = { 1f, 1f, 1f, 1f, 1f, 1f };   // freq, rich, rock, aggro, forest, water
    private int _biomePick;                                     // NEW WORLDS
    private int _landX, _landY;                                 // picked landing site (world tiles)
    private bool _landPicked;
    private bool _piling;                          // stockpile rect drag
    private (int x, int y) _pileFrom, _pileTo;
    private int _lastPaintX, _lastPaintY;      // belt drag: rotate toward the drag
    private int _storyPick;
    private string _genKey = "";
    private Bitmap? _genPreview;

    private float _autosaveT;
    private int _autoSlot;            // 0..2: rotating autosave slot
    private string? _toast;          // transient status line
    private float _toastT;
    private Font? _toastFont;        // cached (was allocated twice per frame)
    private bool _assetsReported;    // one-time "custom textures" notice

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
        Loc.Load(Settings.Instance.Language);
        PanelLayout.LoadFrom(Settings.Instance.Panels);
        ApplyDisplaySettings();
        LostFocus += (s, e) =>
        {
            if (_view.App == AppState.Playing && Settings.Instance.PauseOnFocusLoss)
                _view.Paused = true;
        };
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

        // LANDING: the camera glides from high orbit down to the colony
        if (_landingT > 0)
        {
            _landingT -= dt;
            if (_landingT <= 0) { FinishLanding(); }
            else
            {
                float p = 1f - _landingT / LandingDur;
                float e = 1f - MathF.Pow(1f - p, 3);        // ease-out zoom
                Camera.Zoom = 0.30f + 0.70f * e;
                CenterOn(_game.HubRef.CenterTile.X, _game.HubRef.CenterTile.Y);
            }
        }

        // camera keys (locked while the pod is still falling)
        if (_landingT <= 0)
        {
            float camSpd = 14f * dt / MathF.Max(0.4f, Camera.Zoom);
            if (_keys.Contains(Keys.W) || _keys.Contains(Keys.Up)) Camera.Y -= camSpd;
            if (_keys.Contains(Keys.S) || _keys.Contains(Keys.Down)) Camera.Y += camSpd;
            if (_keys.Contains(Keys.A) || _keys.Contains(Keys.Left)) Camera.X -= camSpd;
            if (_keys.Contains(Keys.D) || _keys.Contains(Keys.Right)) Camera.X += camSpd;
        }

        // LANDING: the world holds its breath until the dust settles
        if (_view.App == AppState.Playing && !_view.Paused && _landingT <= 0)
        {
            _game.Update(dt * Speeds[_speedIdx]);

            if (Settings.Instance.Autosave && !_game.Won && !_game.Lost)
            {
                _autosaveT += dt;
                if (_autosaveT >= Settings.Instance.AutosaveEverySec)
                {
                    _autosaveT = 0;
                    // MADDOG iter-2 (M11): rotate 3 autosave slots so one bad
                    // write can never wipe the only recovery point
                    _autoSlot = (_autoSlot + 1) % 3;
                    SaveSystem.Save(_game, $"autosave{_autoSlot + 1}.json",
                        $"autosave day {_game.Day}");
                    _toast = "autosaved"; _toastT = 3f;
                }
            }
        }

        if (_toastT > 0) _toastT -= dt;

        // frame cap: sim always steps at tick rate; rendering is throttled
        float cap = Settings.Instance.VSync ? 60f : Settings.Instance.FpsLimit;
        bool render = true;
        if (cap > 0 && (float)_renderClock.Elapsed.TotalMilliseconds < 1000f / cap - 1.5f)
            render = false;
        if (render)
        {
            _renderClock.Restart();
            UpdateGhost();
            Invalidate();
        }
    }

    // ============================================================== UI map ==

    private void RebuildUi(Size sz)
    {
        _buttons.Clear();
        ref var v = ref _view;
        v.Buttons = _buttons;
        v.Mouse = _mouse;
        v.Tool = _tool;
        v.PileFrom = _piling ? _pileFrom : null;
        v.PileTo = _piling ? _pileTo : null;
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
        v.ShowFps = Settings.Instance.ShowFps;
        v.ResOpen = _resOpen;
        v.Landing01 = _landingT > 0 ? 1f - _landingT / LandingDur : 0f;
        v.LandingHideHub = _landingT > 0 && v.Landing01 < LandFallEnd;
        v.LandingShake = _landingT > 0 && v.Landing01 >= LandFallEnd && v.Landing01 < LandShakeEnd
            ? (1f - (v.Landing01 - LandFallEnd) / (LandShakeEnd - LandFallEnd)) * 7f : 0f;
        v.LandingPawnsOut = _landingT <= 0 ? 9999
            : v.Landing01 < LandPawnStart ? 0
            : (int)((v.Landing01 - LandPawnStart) / LandPawnStep) + 1;
        v.LandPicked = _landPicked; v.LandWorldX = _landX; v.LandWorldY = _landY;

        // bugfix: a pawn dying while selected left a ghost panel behind
        if (_view.Sel.Col != null && !_game.Cols.Contains(_view.Sel.Col)) _view.Sel.Col = null;
        if (_view.Sel.Cols.Count > 0) _view.Sel.Cols.RemoveAll(c => !_game.Cols.Contains(c));
        v.Alerts = _game.Alerts;

        if (v.App == AppState.MainMenu)
        {
            // dialog open? ONLY its buttons exist (same rule as in-game modals)
            if (_modal == "SETTINGS") { SettingsButtons(sz); return; }
            if (_modal == "LOAD") { LoadButtons(sz); return; }
            if (_modal == "TRADE")
            {
                int ty = 90 + 32 + 22;
                for (int i = 0; i < Bal.TradeDeals.Length; i++)
                {
                    bool soldOut = _game.Trader != null && _game.Trader.SoldDeals[i] >= Bal.TradeDeals[i].Max;
                    bool afford = _game.HubRef.Stock[(int)Bal.TradeDeals[i].Give] >= Bal.TradeDeals[i].GiveN;
                    _buttons.Add(new UiButton(new Rectangle(sz.Width / 2 + 130, ty, 92, 30), $"trade:{i}",
                        Loc.T("TRADE"), enabled: !soldOut && afford && _game.Trader is { Arrived: true }));
                    ty += 40;
                }
                return;
            }
            MainMenuButtons(sz);
            return;
        }
        if (v.App == AppState.WorldGen) { WorldGenButtons(sz); return; }

        PlayingButtons(sz);
        PawnPanelButtons(sz);
    }

    /// <summary>Phase 1: buttons over the bottom-left pawn panel.</summary>
    private void PawnPanelButtons(Size sz)
    {
        if (_game.Cols.Count == 0) return;
        var tabs = Renderer.PawnTabRects(sz);
        var p = Renderer.PawnPanelRect(sz);

        if (_view.Sel.Cols.Count > 1)
        {
            int y = p.Bottom - 76;
            _buttons.Add(new UiButton(new Rectangle(p.X + 10, y, 145, 28), "pawns:draft", "DRAFT ALL [R]"));
            _buttons.Add(new UiButton(new Rectangle(p.X + 165, y, 145, 28), "pawns:undraft", "UNDRAFT ALL", tint: Pal.Good));
            string s = _view.Sel.Cols.All(c => c.Stance == Stance.Fight) ? "STANCE: FIGHT"
                : _view.Sel.Cols.All(c => c.Stance == Stance.Flee) ? "STANCE: FLEE" : "STANCE: MIXED";
            _buttons.Add(new UiButton(new Rectangle(p.X + 10, y + 32, 145, 28), "pawns:cycle", s));
            bool auto = _view.Sel.Cols.All(c => c.AutoEngage);
            _buttons.Add(new UiButton(new Rectangle(p.X + 165, y + 32, 145, 28), "pawns:auto",
                auto ? "AUTO-ENGAGE: ON" : "AUTO-ENGAGE: OFF", active: auto));
            return;
        }
        if (_view.Sel.Col == null) return;

        string[] tabIds = { "pawn:tab:0", "pawn:tab:1", "pawn:tab:2", "pawn:tab:3" };
        for (int i = 0; i < 4; i++)
            _buttons.Add(new UiButton(tabs[i], tabIds[i], "", active: _view.PawnTab == i));

        var c = _view.Sel.Col;
        switch (_view.PawnTab)
        {
            case 2: // work priorities
                for (int i = 0; i < Bal.WorkCount; i++)
                {
                    float ry = Renderer.PawnWorkRowY(sz, i);
                    _buttons.Add(new UiButton(new Rectangle(p.X + 228, (int)ry, 40, 20), $"pawn:work:{i}:-", "-", "lower priority"));
                    _buttons.Add(new UiButton(new Rectangle(p.X + 272, (int)ry, 40, 20), $"pawn:work:{i}:+", "+", "raise priority",
                        active: c.Priorities[i] < 4));
                }
                break;
            case 3: // combat
                int y = p.Bottom - 108;
                _buttons.Add(new UiButton(new Rectangle(p.X + 10, y, 145, 28), "pawn:draft",
                    c.Drafted ? "UNDRAFT [R]" : "DRAFT [R]", active: c.Drafted, tint: c.Drafted ? Pal.Bad : Pal.Good));
                _buttons.Add(new UiButton(new Rectangle(p.X + 165, y, 145, 28), "pawn:auto",
                    c.AutoEngage ? "AUTO-ENGAGE: ON" : "AUTO-ENGAGE: OFF", active: c.AutoEngage));
                _buttons.Add(new UiButton(new Rectangle(p.X + 10, y + 32, 96, 26), "pawn:stance:0",
                    "FIGHT", active: c.Stance == Stance.Fight));
                _buttons.Add(new UiButton(new Rectangle(p.X + 112, y + 32, 96, 26), "pawn:stance:1",
                    "FLEE", active: c.Stance == Stance.Flee));
                _buttons.Add(new UiButton(new Rectangle(p.X + 214, y + 32, 96, 26), "pawn:stance:2",
                    "IGNORE", active: c.Stance == Stance.Ignore));
                break;
        }
    }

    private void MainMenuButtons(Size sz)
    {
        int w = 300, x = sz.Width / 2 - w / 2;
        int y = sz.Height / 2 - 60;
        _buttons.Add(new UiButton(new Rectangle(x, y, w, 44), "mm:new", Loc.T("NEW COLONY"), Loc.T("generate a world, pick a storyteller")));
        _buttons.Add(new UiButton(new Rectangle(x, y + 52, w, 44), "mm:load", Loc.T("LOAD GAME"), Loc.T("from") + " " + SaveSystem.SaveDir));
        _buttons.Add(new UiButton(new Rectangle(x, y + 104, w, 44), "mm:settings", Loc.T("SETTINGS"), Loc.T("toggle dev menu, autosave")));
        _buttons.Add(new UiButton(new Rectangle(x, y + 156, w, 44), "mm:help", Loc.T("HELP"), Loc.T("controls & survival tips")));
        _buttons.Add(new UiButton(new Rectangle(x, y + 208, w, 44), "mm:quit", Loc.T("QUIT")));
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
        v.Sliders.Add(new UiSlider { Id = "forest", Label = "Forest density", Min = 0f, Max = 2f, Value = _genVals[4] });
        v.Sliders.Add(new UiSlider { Id = "water", Label = "Water & lakes", Min = 0f, Max = 2f, Value = _genVals[5] });

        // storyteller picks
        int y = sz.Height - 210;
        var names = new[] { ("COLONY BUILDER", "gentle pacing — learn the machine"),
                            ("RANDY RANDOM", "random events, random raids"),
                            ("THE MERCILESS", "big raids, no mercy") };
        for (int i = 0; i < 3; i++)
            _buttons.Add(new UiButton(new Rectangle(colX, y + i * 46, colW, 40), $"story:{i}",
                names[i].Item1, names[i].Item2, active: _storyPick == i));

        int px = x0 + leftW + 20;

        // NEW WORLDS: pick your landing site
        var bnames = new[] { ("VERDANT", "green hills, balanced everything"),
                             ("VOLCANIC", "crystal-rich basalt, little water"),
                             ("GLACIAL", "pale sun, iron near the surface"),
                             ("FUNGAL", "flora everywhere, blight spreads fast") };
        int by = sz.Height - 128 - 4 * 40 - 12;
        for (int i = 0; i < 4; i++)
            _buttons.Add(new UiButton(new Rectangle(px, by + i * 40, 260, 36), $"biome:{i}",
                bnames[i].Item1, bnames[i].Item2, active: _biomePick == i));

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
            _buttons.Add(new UiButton(new Rectangle(x - 56, y, 52, 40), "tool:mine", "⛏",
                _tool == ToolKind.Mine ? "mine orders ON" : "hand mining orders [V]",
                active: _tool == ToolKind.Mine));
            _buttons.Add(new UiButton(new Rectangle(x - 56 - 58, y + 42, 52, 40), "tool:pile", "▦",
                _tool == ToolKind.Stockpile ? "stockpile zones ON" : "stockpile zones [drag a rect]",
                active: _tool == ToolKind.Stockpile));
            _buttons.Add(new UiButton(new Rectangle(x + 6, y, 56, 40), "tool:x", "X⚡", "bulldoze",
                active: _tool == ToolKind.Bulldoze));
            _buttons.Add(new UiButton(new Rectangle(x + 66, y, 56, 40), "menu:esc", "≡", "menu"));

            if (_catOpen) CategoryTray(sz);
        }

        // ---- right-side vertical toggles ----
        int rx = sz.Width - 118;
        _buttons.Add(new UiButton(new Rectangle(rx, 60, 108, 30), "ui:research", "RESEARCH [T]", active: _modal == "RESEARCH"));
        var resR = Renderer.StockToggleRect(sz);
        _buttons.Add(new UiButton(resR, "ui:res", _resOpen ? "\u25BE" : "\u25B8",
            _resOpen ? "hide stock" : "show full stock [B]"));
        _buttons.Add(new UiButton(new Rectangle(rx, 94, 108, 30), "ui:work", "WORK [P]", active: _modal == "WORK"));
        _buttons.Add(new UiButton(new Rectangle(rx, 128, 108, 30), "ui:pause", _view.Paused ? "▶ PLAY" : "⏸ PAUSE"));
        _buttons.Add(new UiButton(new Rectangle(rx, 162, 108, 30), "ui:slower", "« slower"));
        _buttons.Add(new UiButton(new Rectangle(rx, 196, 108, 30), "ui:faster", "faster »"));
        _buttons.Add(new UiButton(new Rectangle(rx, 230, 108, 30), "ui:minimap", "MINIMAP [M]", active: _showMinimap));

        // ---- alerts (clickable chips) ----
        // they flow below the stock panel when it is open (top-left)
        int ay = _resOpen ? PanelLayout.StockRect(ClientSize).Bottom + 6 : 44;
        foreach (var a in _game.Alerts)
        {
            var b = new UiButton(new Rectangle(10, ay, 312, 22), $"alert:{_game.Alerts.IndexOf(a)}",
                "• " + a.Text) { Tint = a.Col };
            _buttons.Add(b);
            ay += 24;
        }

        // RECLAMATION: the final choice
        if (_game.Won && _game.FinaleCleared && !_game.ContinueAfterWin && _game.EndingChosen == 0)
        {
            _buttons.Add(new UiButton(new Rectangle(sz.Width / 2 - 190, sz.Height / 2 + 76, 180, 34),
                "end:signal", "SIGNAL HOME - call for rescue"));
            _buttons.Add(new UiButton(new Rectangle(sz.Width / 2 + 10, sz.Height / 2 + 76, 180, 34),
                "end:stay", "STAY - remake this world"));
        }

        // ---- selection actions ----
        SelectionButtons(sz);
    }

    private void CategoryTray(Size sz)
    {
        var kinds = Enum.GetValues<BuildKind>()
            .Where(k => Bal.CategoryOf(k) == _cat && k != BuildKind.Hub && k != BuildKind.ArkWreck).ToList();

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
            for (int r = 0; r < Bal.FabRecipeCount; r++)
                _buttons.Add(new UiButton(new Rectangle(x, y + r * 26, 240, 24), $"recipe:{r}",
                    fab.RecipeNameOf(r), active: fab.Recipe == r));
        }
        if (sel.B is FilterSplitter or Inserter)
            _buttons.Add(new UiButton(new Rectangle(x, y, 240, 26), "act:filter",
                sel.B is Inserter ? "CYCLE GRAB FILTER" : "CYCLE FILTER"));
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
        _buttons.Add(new UiButton(new Rectangle(r.X + 224, r.Y + 170, 200, 26), "dev:perf",
            _view.ShowPerf ? "PERF OVERLAY: ON" : "PERF OVERLAY: OFF", active: _view.ShowPerf));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 170, 452, 34), "dev:export",
            "EXPORT SPRITE TEMPLATES to assets/", "paint over the PNGs, restart to load"));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 205, 452, 34), "dev:lang",
            "EXPORT LANGUAGE TEMPLATE (assets/lang/template.txt)",
            "for human translators: copy to <code>.txt, translate the right side"));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 243, 452, 26), "dev:note",
            "dev commands replay identically (command layer)", enabled: false));
        _buttons.Add(new UiButton(new Rectangle(r.Right - 110, r.Y + 280, 98, 30), "dev:close", "CLOSE"));
    }

    private void MenuButtons(Size sz)
    {
        var r = new Rectangle(sz.Width / 2 - 160, sz.Height / 2 - 155, 320, 320);
        var ids = new List<string> { "menu:resume", "menu:save", "menu:load", "menu:settings", "menu:help" };
        var txt = new List<string> { Loc.T("RESUME"), Loc.T("SAVE GAME"), Loc.T("LOAD GAME"), Loc.T("SETTINGS"), Loc.T("HELP") };
        if (Settings.Instance.ShowDevMenu)
        {
            ids.Add("menu:dev");
            txt.Add(Loc.T("DEV / CHEATS"));
        }
        ids.Add("menu:quit");
        txt.Add(Loc.T("QUIT TO MENU"));
        for (int i = 0; i < ids.Count; i++)
            _buttons.Add(new UiButton(new Rectangle(r.X, r.Y + i * 44, r.Width, 40), ids[i], txt[i]));
    }

    private void SettingsButtons(Size sz)
    {
        var r = new Rectangle(sz.Width / 2 - 220, sz.Height / 2 - 190, 440, 380);
        var s = Settings.Instance;
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 12, 412, 34), "set:dev",
            s.ShowDevMenu ? Loc.T("DEV MENU: SHOWN (in-game ≡)") : Loc.T("DEV MENU: HIDDEN"),
            Loc.T("unlocks the cheat/debug panel — feature #70"), active: s.ShowDevMenu));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 52, 412, 34), "set:autosave",
            s.Autosave ? Loc.T("AUTOSAVE: ON") + $" ({s.AutosaveEverySec}s)" : Loc.T("AUTOSAVE: OFF"), active: s.Autosave));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 92, 412, 34), "set:grid",
            s.ShowTileGrid ? Loc.T("TILE GRID: ON [G]") : Loc.T("TILE GRID: OFF [G]"), active: s.ShowTileGrid));
        string langLabel = s.Language == "en" ? Loc.T("LANGUAGE")
            : $"{Loc.T("LANGUAGE")}: {Loc.DisplayName(s.Language)} · {Loc.LoadedCount}";
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 132, 412, 34), "set:lang",
            langLabel, Loc.T("cycle — human translations: assets/lang/<code>.txt")));

        // ---- display options (Phase 1) ----
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 176, 200, 30), "set:fullscreen",
            s.Fullscreen ? "FULLSCREEN: ON" : "FULLSCREEN: OFF", active: s.Fullscreen));
        _buttons.Add(new UiButton(new Rectangle(r.X + 226, r.Y + 176, 200, 30), "set:res",
            $"WINDOW: {s.WindowW}x{s.WindowH}", "windowed size (click to cycle)"));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 214, 200, 30), "set:vsync",
            s.VSync ? "VSYNC (60 CAP): ON" : "VSYNC (60 CAP): OFF", active: s.VSync));
        _buttons.Add(new UiButton(new Rectangle(r.X + 226, r.Y + 214, 200, 30), "set:fps",
            s.VSync ? "FPS LIMIT: (vsync on)" : s.FpsLimit == 0 ? "FPS LIMIT: UNLIMITED" : $"FPS LIMIT: {s.FpsLimit}",
            "frame cap when vsync is off"));
        _buttons.Add(new UiButton(new Rectangle(r.X + 14, r.Y + 252, 200, 30), "set:focus",
            s.PauseOnFocusLoss ? "PAUSE ON FOCUS LOSS: ON" : "PAUSE ON FOCUS LOSS: OFF", active: s.PauseOnFocusLoss));
        _buttons.Add(new UiButton(new Rectangle(r.X + 226, r.Y + 252, 200, 30), "set:fpsdisp",
            s.ShowFps ? "FPS COUNTER: ON" : "FPS COUNTER: OFF", active: s.ShowFps));
        _buttons.Add(new UiButton(new Rectangle(r.Right - 110, r.Bottom - 42, 98, 30), "set:close", Loc.T("CLOSE")));
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

        if (_dragPanel != null)
        {
            var pr = _dragPanel switch
            {
                "pawn" => PanelLayout.PawnRect(ClientSize),
                "stock" => PanelLayout.StockRect(ClientSize),
                _ => PanelLayout.SelRect(ClientSize),
            };
            var pos = new Point(e.X - _dragPanelGrab.X, e.Y - _dragPanelGrab.Y);
            if (_dragPanel == "pawn") PanelLayout.Pawn = pos;
            else if (_dragPanel == "stock") PanelLayout.Stock = pos;
            else PanelLayout.Sel = pos;
            return;
        }

        var hit = HitButton(e.Location);
        _view.Hover = hit?.Id;
        Cursor = OverPanelHandle(e.Location) ? Cursors.SizeAll : Cursors.Default;

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

        if (_piling)
        {
            _pileTo = TileUnder(e.Location);
            return;
        }
        if (_dragging && _tool == ToolKind.Build && _toolKind != null && DragPaintable(_toolKind.Value))
        {
            var (tx, ty) = TileUnder(e.Location);
            Dir prevFacing = _facing;
            bool turned = false;
            // BELT UX: dragging a directional piece rotates it toward the
            // drag - drag right and the belts face right (Mindustry-style)
            if ((tx != _lastPaintX || ty != _lastPaintY) &&
                _toolKind is BuildKind.Belt or BuildKind.FastBelt or BuildKind.Rail or BuildKind.ElevatedRail)
            {
                int ddx = tx - _lastPaintX, ddy = ty - _lastPaintY;
                if (ddx != 0 || ddy != 0)
                {
                    _facing = Bal.DirFromDelta(ddx, ddy);
                    // a 90° turn mid-drag bends the previous tile into a curve
                    turned = _toolKind is BuildKind.Belt or BuildKind.FastBelt &&
                             _facing != prevFacing && _facing != DirU.Opposite(prevFacing);
                }
            }
            PaintAt((tx, ty));
            if (turned)
            {
                var corner = _game.World.Cell(_lastPaintX, _lastPaintY).B;
                if (corner is Belt cb && cb.Kind == _toolKind.Value)
                {
                    cb.Face = _facing;                       // out along the new drag direction
                    cb.BendIn = DirU.Opposite(prevFacing);   // in from where the drag came
                }
            }
            _lastPaintX = tx; _lastPaintY = ty;
        }
        if (_dragging && _tool == ToolKind.Bulldoze)
        {
            var (tx, ty) = TileUnder(e.Location);
            _game.QueueBulldoze(tx, ty);
        }
        if (_dragging && _tool == ToolKind.Mine)
        {
            var (tx, ty) = TileUnder(e.Location);
            _game.QueueMine(tx, ty);
        }
        if (_selecting)
        {
            int x0 = Math.Min(_selFrom.X, e.X), y0 = Math.Min(_selFrom.Y, e.Y);
            int x1 = Math.Max(_selFrom.X, e.X), y1 = Math.Max(_selFrom.Y, e.Y);
            _view.SelBox = new Rectangle(x0, y0, x1 - x0, y1 - y0);
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

    /// <summary>Rotate the placed building under the cursor (R hotkey,
    /// empty hand). Shift = counter-clockwise. Returns false when the
    /// cursor isn't over a rotatable building.</summary>
    private bool RotateHovered(bool ccw)
    {
        if (_view.App != AppState.Playing) return false;
        var (tx, ty) = TileUnder(_mouse);
        if (!_game.World.InBounds(tx, ty)) return false;
        var b = _game.World.Cell(tx, ty).B ?? _game.World.Cell(tx, ty).B2;
        if (b == null || b.W != b.H) return false;   // square footprints only
        b.Rotate(ccw);
        return true;
    }

    private (int x, int y) TileUnder(Point p)
    {
        var w = Camera.ToWorld(p.X, p.Y);
        return ((int)MathF.Floor(w.X), (int)MathF.Floor(w.Y));
    }

    private void OnMouseDown(object? s, MouseEventArgs e)
    {
        Focus();
        if (_landingT > 0) { FinishLanding(); return; }   // skip the landing
        var hit = HitButton(e.Location);
        if (e.Button == MouseButtons.Middle)
        {
            _panning = true; _panGrab = e.Location; return;
        }
        if (e.Button == MouseButtons.Right)
        {
            // Phase 1: drafted pawns -> right-click issues orders
            // (bugfix: an armed tool/category must still cancel first, or you
            // could never put a tool down while drafted pawns were selected)
            if (_view.App == AppState.Playing && _modal == null
                && _tool == ToolKind.None && !_catOpen)
            {
                var draftedSel = _view.Sel.Cols.Count > 0
                    ? _view.Sel.Cols.Where(cc => cc.Drafted).ToList()
                    : _view.Sel.Col is { Drafted: true } c0 ? new List<Colonist> { c0 } : new List<Colonist>();
                if (draftedSel.Count > 0)
                {
                    var wt = Camera.ToWorld(e.X, e.Y);
                    Raider? foe = null;
                    foreach (var r in _game.Foes)
                    {
                        float dx = r.PosX - wt.X, dy = r.PosY - wt.Y;
                        float rad = r.Apex ? 0.9f : 0.6f;
                        if (dx * dx + dy * dy < rad * rad) { foe = r; break; }
                    }
                    // MADDOG iter-4: right-click a grazer = hunt order
                    Beast? beast = null;
                    if (foe == null)
                        foreach (var b in _game.Beasts)
                        {
                            if (b.Hp <= 0) continue;
                            float dx = b.PosX - wt.X, dy = b.PosY - wt.Y;
                            if (dx * dx + dy * dy < 0.45f) { beast = b; break; }
                        }
                    int otx = (int)MathF.Floor(wt.X), oty = (int)MathF.Floor(wt.Y);
                    foreach (var c in draftedSel)
                    {
                        if (foe != null)
                        {
                            c.OrderFoe = foe;
                            c.OrderBeast = null;
                            c.HasMoveOrder = false;
                            c.Path = null;
                        }
                        else if (beast != null)
                        {
                            c.OrderBeast = beast;
                            c.OrderFoe = null;
                            c.HasMoveOrder = false;
                            c.Path = null;
                        }
                        else
                        {
                            c.OrderFoe = null;
                            var path = _game.World.FindPath((int)c.PosX, (int)c.PosY, otx, oty, enemy: false);
                            if (path != null)
                            {
                                c.Path = path; c.PathIdx = 0;
                                c.OrderX = otx; c.OrderY = oty;
                                c.HasMoveOrder = true;
                            }
                            else { _toast = "no path there"; _toastT = 2f; }
                        }
                    }
                    return;
                }
            }
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

        // draggable HUD panels: grab by the title strip
        if (_view.App == AppState.Playing && _modal == null)
        {
            foreach (var (pid, pr) in new[]
            {
                ("pawn", PanelLayout.PawnRect(ClientSize)),
                ("stock", PanelLayout.StockRect(ClientSize)),
                ("sel", PanelLayout.SelRect(ClientSize)),
            })
            {
                if (new Rectangle(pr.X, pr.Y, pr.Width, 22).Contains(e.Location))
                {
                    _dragPanel = pid;
                    _dragPanelGrab = new Point(e.X - pr.X, e.Y - pr.Y);
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

        // LANDING SITE: click the world preview to choose where to settle
        if (_view.App == AppState.WorldGen && e.Button == MouseButtons.Left)
        {
            var pr = new Rectangle(ClientSize.Width / 2 + 80, 130, 260, 200);
            if (pr.Contains(e.Location))
            {
                float ppx = (e.X - pr.X) * 130f / pr.Width;
                float ppy = (e.Y - pr.Y) * 100f / pr.Height;
                _landX = (int)MathF.Round((ppx - 65f) * 6f);
                _landY = (int)MathF.Round((ppy - 50f) * 6f);
                _landPicked = true;
                _toast = $"landing site: {_landX}, {_landY}";
                _toastT = 2.5f;
                return;
            }
        }

        if (_modal == null && _view.App == AppState.Playing
            && !Renderer.HintDismiss.IsEmpty && Renderer.HintDismiss.Contains(e.Location))
        {
            _game.MarkHintDone(_game.HintStage());
            return;
        }
        if (_modal != null) return;

        // Menu / world-gen screens: there is no world yet, so there is nothing
        // to select, paint, or mine. (Regression: a canvas click here used to
        // arm _selecting, and mouse-up ran ClickSelect -> World.InBounds NRE.)
        if (_view.App != AppState.Playing) return;

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
            _lastPaintX = tx; _lastPaintY = ty;
            return;
        }
        if (_tool == ToolKind.Bulldoze)
        {
            _game.QueueBulldoze(tx, ty);
            _dragging = true;
            return;
        }
        if (_tool == ToolKind.Mine)
        {
            _game.QueueMine(tx, ty);
            _dragging = true;
            return;
        }
        if (_tool == ToolKind.Stockpile)
        {
            _piling = true; _pileFrom = (tx, ty); _pileTo = (tx, ty);
            return;
        }

        // Phase 1: start a selection box (click = single select on release)
        _selFrom = e.Location;
        _selecting = true;
    }

    private void OnMouseUp(object? s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Middle) _panning = false;
        if (e.Button == MouseButtons.Left)
        {
            if (_dragPanel != null)
            {
                _dragPanel = null;
                PanelLayout.SaveTo(Settings.Instance);
                Settings.Save();
                return;
            }
            _dragging = false; _everDownInMini = false; _dragSlider = null;
            if (_piling)
            {
                _piling = false;
                _game.QueueStockpile(_pileFrom.x, _pileFrom.y, _pileTo.x, _pileTo.y);
                return;
            }
            if (_selecting)
            {
                _selecting = false;
                var box = _view.SelBox ?? new Rectangle(e.X, e.Y, 0, 0);
                _view.SelBox = null;
                if (box.Width < 7 && box.Height < 7) ClickSelect(new Point(box.X, box.Y));
                else FinishBoxSelect(box);
            }
        }
    }

    private bool OverPanelHandle(Point p)
    {
        if (_view.App != AppState.Playing || _modal != null) return false;
        foreach (var pr in new[]
        {
            PanelLayout.PawnRect(ClientSize),
            PanelLayout.StockRect(ClientSize),
            PanelLayout.SelRect(ClientSize),
        })
            if (new Rectangle(pr.X, pr.Y, pr.Width, 22).Contains(p)) return true;
        return false;
    }

    /// <summary>Select every colonist inside the screen-space box.</summary>
    private void FinishBoxSelect(Rectangle box)
    {
        _game.NotifySelected();
        var a = Camera.ToWorld(box.X, box.Y);
        var b = Camera.ToWorld(box.Right, box.Bottom);
        float x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X);
        float y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
        _view.Sel.Clear();
        foreach (var c in _game.Cols)
        {
            if (c.Hp <= 0) continue;
            if (c.PosX >= x0 && c.PosX <= x1 && c.PosY >= y0 && c.PosY <= y1)
                _view.Sel.Cols.Add(c);
        }
        if (_view.Sel.Cols.Count > 0)
        {
            _view.Sel.Col = _view.Sel.Cols[0];      // panel shows details of the first
            if (_view.Sel.Cols.Count == 1) _view.Sel.Cols.Clear();   // single: behave like a click
        }
    }

    /// <summary>World-gen slider: set value from a screen x, write back to _genVals.</summary>
    private void SetSliderFromX(UiSlider sl, int mx)
    {
        float frac = Math.Clamp((mx - (sl.R.X + 6)) / (float)(sl.R.Width - 12), 0f, 1f);
        sl.Value = sl.Min + frac * (sl.Max - sl.Min);
        int idx = sl.Id switch
        {
            "freq" => 0, "rich" => 1, "rock" => 2, "agg" => 3, "forest" => 4, _ => 5,
        };
        _genVals[idx] = sl.Value;
    }

    private void OnWheel(object? s, MouseEventArgs e)
    {
        if (_landingT > 0) return;                    // cinematic in progress
        // MADDOG iter-6: mouse wheel scrolls the event history when hovering it
        if (_view.ShowHistory && Renderer.HistoryRect(ClientSize).Contains(e.Location))
        {
            int dir = e.Delta > 0 ? -1 : 1;
            int maxScroll = Math.Max(0, _game.History.Count - 14);
            _view.HistScroll = Math.Clamp(_view.HistScroll + dir, 0, maxScroll);
            return;
        }
        // grab the world point under the cursor BEFORE the zoom changes
        var anchor = Camera.ToWorld(e.X, e.Y);
        float z = Camera.Zoom * (e.Delta > 0 ? 1.15f : 1f / 1.15f);
        Camera.Zoom = Math.Clamp(z, 0.35f, 3f);
        Camera.X = anchor.X - e.X / Camera.Sz;
        Camera.Y = anchor.Y - e.Y / Camera.Sz;
    }

    private void ClickSelect(Point p)
    {
        if (_game.World == null) return;      // no world yet (menu/worldgen)
        _game.NotifySelected();
        // MADDOG iter-9: clicking the parked caravan opens the barter board
        if (_game.Trader is { } tr)
        {
            var w = Camera.ToWorld(p.X, p.Y);
            float d = MathF.Sqrt((tr.PosX - w.X) * (tr.PosX - w.X) + (tr.PosY - w.Y) * (tr.PosY - w.Y));
            if (d < 0.9f && tr.Arrived) { ToggleModal("TRADE"); return; }
        }
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
        if (_landingT > 0) { FinishLanding(); return; }   // skip the landing
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
            case Keys.R when _modal == null:
                if (_view.Sel.Col != null || _view.Sel.Cols.Count > 0) ToggleDraftSelected();
                else if (_tool == ToolKind.None && RotateHovered(e.Shift)) { /* rotated placed building */ }
                else _facing = DirU.TurnRight(_facing);
                break;
            case Keys.X when _modal == null: ToggleBulldoze(); break;
            case Keys.V when _modal == null: ToggleMineTool(); break;
            case Keys.B when _modal == null: _resOpen = !_resOpen; break;
            case Keys.T: ToggleModal("RESEARCH"); break;
            case Keys.P: ToggleModal("WORK"); break;
            case Keys.M: _showMinimap = !_showMinimap; break;
            case Keys.L when _view.App == AppState.Playing:
                _view.ShowHistory = !_view.ShowHistory; _view.HistScroll = 0; break;
            case Keys.S when _view.App == AppState.Playing && _modal == null:
                _view.ShowStats = !_view.ShowStats; break;
            case Keys.C when _view.App == AppState.Playing && _modal == null:
                _view.ShowChron = !_view.ShowChron; break;
            case Keys.F5 when _view.App == AppState.Playing && _modal == null:
                _view.ShowProd = !_view.ShowProd; break;
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

    /// <summary>Phase 1: hand-mining designator (ore tiles + rocks).</summary>
    private void ToggleMineTool()
    {
        if (_tool == ToolKind.Mine) { _tool = ToolKind.None; return; }
        _tool = ToolKind.Mine; _toolKind = null; _catOpen = false;
    }

    private void TogglePileTool()
    {
        if (_tool == ToolKind.Stockpile) { _tool = ToolKind.None; return; }
        _tool = ToolKind.Stockpile; _toolKind = null; _catOpen = false;
    }

    private void ToggleModal(string m) => _modal = _modal == m ? null : m;

    /// <summary>Phase 1: window mode / resolution. GDI+ has no real vsync;
    /// "VSYNC" is an honest 60fps frame cap (see OnTick).</summary>
    private void ApplyDisplaySettings()
    {
        var s = Settings.Instance;
        if (s.Fullscreen)
        {
            FormBorderStyle = FormBorderStyle.None;
            if (WindowState != FormWindowState.Maximized) WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            ClientSize = new Size(s.WindowW, s.WindowH);
        }
    }

    private void PickCategory(int i)
    {
        // Phase 1 fix: clicking a DIFFERENT category while one is open must
        // switch directly (old code assigned _cat first, so the != check was
        // always false and every cross-click just closed the tray)
        bool same = (int)_cat == i && _catOpen;
        _cat = (BuildCategory)i;
        _catOpen = !same;
        _tool = ToolKind.None; _toolKind = null;
    }

    // ========================================================= buttons =====

    private void HandleButton(string id)
    {
        if (id.StartsWith("pawn:", StringComparison.Ordinal)) { HandlePawnButton(id); return; }

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
            case "ui:res": _resOpen = !_resOpen; return;
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
            // fix: compare BEFORE reassigning _cat, or the != check is always
            // false and clicking another category just closes the tray
            int i = int.Parse(id[4..]);
            bool same = (int)_cat == i && _catOpen;
            _cat = (BuildCategory)i;
            _catOpen = !same;
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
        if (id.StartsWith("biome:"))
        {
            _biomePick = int.Parse(id[6..]);
            return;
        }
        if (id == "end:signal")
        {
            _game.EndingChosen = 1;
            _game.AddChron("They aimed the beacon at the stars and let it burn.");
            return;
        }
        if (id == "end:stay")
        {
            _game.EndingChosen = 2;
            _game.ContinueAfterWin = true;
            _game.Hope = true;
            _game.AddChron("They chose to stay. The blight will learn to fear the lamplight.");
            return;
        }
        if (id.StartsWith("trade:"))
        {
            _game.QueueTrade(int.Parse(id[6..]));
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
                if (_view.Sel.B is FilterSplitter fsp)
                    _game.QueueFilter(fsp.X, fsp.Y, ((int)fsp.Filter + 1) % (int)ItemKind.AdvPart);
                else if (_view.Sel.B is Inserter ins)
                {
                    // cycle: no filter -> IronOre -> ... -> none again
                    int next = ins.Filter == null ? 0 : ((int)ins.Filter + 1) % ((int)ItemKind.AdvPart + 1);
                    _game.QueueFilter(ins.X, ins.Y, next);
                }
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
            case "set:fullscreen":
                Settings.Instance.Fullscreen = !Settings.Instance.Fullscreen; Settings.Save();
                ApplyDisplaySettings(); return;
            case "set:res":
            {
                int idx = Array.FindIndex(ResChoices, r2 => r2.w == Settings.Instance.WindowW && r2.h == Settings.Instance.WindowH);
                idx = (idx + 1) % ResChoices.Length;
                Settings.Instance.WindowW = ResChoices[idx].w;
                Settings.Instance.WindowH = ResChoices[idx].h;
                Settings.Save();
                if (!Settings.Instance.Fullscreen) ApplyDisplaySettings();
                return;
            }
            case "set:vsync": Settings.Instance.VSync = !Settings.Instance.VSync; Settings.Save(); return;
            case "set:fps":
            {
                int idx = Array.IndexOf(FpsChoices, Settings.Instance.FpsLimit);
                idx = (idx + 1) % FpsChoices.Length;
                Settings.Instance.FpsLimit = FpsChoices[idx];
                Settings.Save(); return;
            }
            case "set:focus": Settings.Instance.PauseOnFocusLoss = !Settings.Instance.PauseOnFocusLoss; Settings.Save(); return;
            case "set:fpsdisp": Settings.Instance.ShowFps = !Settings.Instance.ShowFps; Settings.Save(); return;
            case "set:lang":
            {
                var avail = Loc.Available();
                int idx = avail.IndexOf(Settings.Instance.Language);
                Settings.Instance.Language = avail[(idx + 1) % avail.Count];
                Settings.Save();
                Loc.Load(Settings.Instance.Language);
                _toast = Settings.Instance.Language == "en" ? "language: english (builtin)"
                    : $"language: {Loc.DisplayName(Settings.Instance.Language)} — {Loc.LoadedCount} {Loc.T("strings translated")}";
                _toastT = 4f;
                return;
            }

            // dev menu (#70)
            case "dev:fe": _game.QueueDevGive(ItemKind.IronPlate, 200); return;
            case "dev:cu": _game.QueueDevGive(ItemKind.CopperPlate, 200); return;
            case "dev:sci": _game.QueueDevGive(ItemKind.SciencePack, 100); return;
            case "dev:packs": _game.QueueDevGive(ItemKind.AdvPart, 10); return;
            case "dev:tech": _game.QueueDevResearch(); return;
            case "dev:reveal": _game.QueueDevReveal(); return;
            case "dev:raid": _game.QueueDevRaid(); return;
            case "dev:god": _game.QueueToggleGod(); return;
            case "dev:perf": _view.ShowPerf = !_view.ShowPerf; return;
            case "dev:export":
            {
                var (written, skipped) = Sprites.ExportTemplates();
                _toast = $"exported {written} templates ({skipped} kept) — edit & restart";
                _toastT = 6f;
                return;
            }
            case "dev:lang":
            {
                var (path, count) = Loc.ExportTemplate();
                _toast = path.Length > 0 ? $"{count} strings -> {path}"
                    : "template already exists — open assets/lang/template.txt";
                _toastT = 6f;
                return;
            }

            case "tool:x": ToggleBulldoze(); return;
            case "tool:mine": ToggleMineTool(); return;
            case "tool:pile": TogglePileTool(); return;
        }
    }

    // ================================================ Phase 1: pawn UI --

    private void HandlePawnButton(string id)
    {
        var sel = _view.Sel.Cols.Count > 0 ? _view.Sel.Cols.ToList()
            : _view.Sel.Col != null ? new List<Colonist> { _view.Sel.Col } : new();
        if (sel.Count == 0 && !id.StartsWith("pawn:tab:")) return;

        if (id.StartsWith("pawn:tab:")) { _view.PawnTab = id[^1] - '0'; return; }
        if (id == "pawn:draft") { foreach (var c in sel) SetDrafted(c, !c.Drafted); return; }
        if (id == "pawn:auto") { foreach (var c in sel) c.AutoEngage = !c.AutoEngage; return; }
        if (id.StartsWith("pawn:stance:"))
        {
            var st = (Stance)(id[^1] - '0');
            foreach (var c in sel) c.Stance = st;
            return;
        }
        if (id.StartsWith("pawn:work:"))
        {
            var c = _view.Sel.Col; if (c == null) return;
            var parts = id.Split(':');
            int wi = int.Parse(parts[2]), delta = parts[3] == "+" ? 1 : -1;
            c.Priorities[wi] = Math.Clamp(c.Priorities[wi] + delta, 1, 4);
            return;
        }
        if (id == "pawns:draft") { foreach (var c in sel) SetDrafted(c, true); return; }
        if (id == "pawns:undraft") { foreach (var c in sel) SetDrafted(c, false); return; }
        if (id == "pawns:cycle")
        {
            // Flee -> Fight -> Ignore -> Flee (whole selection together)
            var next = _view.Sel.Cols.All(c => c.Stance == Stance.Flee) ? Stance.Fight
                : _view.Sel.Cols.All(c => c.Stance == Stance.Fight) ? Stance.Ignore : Stance.Flee;
            foreach (var c in sel) c.Stance = next;
            return;
        }
        if (id == "pawns:auto") { bool on = !sel.All(c => c.AutoEngage); foreach (var c in sel) c.AutoEngage = on; return; }
    }

    /// <summary>Drafting frees manual labor (blueprint / mine job) so other
    /// pawns can take it; undrafting lets the pawn seek work again.</summary>
    private void SetDrafted(Colonist c, bool on)
    {
        if (c.Drafted == on) return;
        c.Drafted = on;
        if (on)
        {
            // bugfix: a drafted operator kept their machine "manned" forever
            if (c.Job != null) { if (c.Job.Operator == c) c.Job.Operator = null; c.Job = null; }
            if (c.BuildJob != null) { if (c.BuildJob.Builder == c) c.BuildJob.Builder = null; c.BuildJob = null; }
            if (c.MineJob != null) { if (c.MineJob.Miner == c) c.MineJob.Miner = null; c.MineJob = null; }
            if (c.RepairJob != null) { if (c.RepairJob.Repairer == c) c.RepairJob.Repairer = null; c.RepairJob = null; }
            if (c.GatherJob != null) { if (c.GatherJob.Gatherer == c) c.GatherJob.Gatherer = null; c.GatherJob = null; }
            if (c.ExcavJob != null) { if (c.ExcavJob.Excavator == c) c.ExcavJob.Excavator = null; c.ExcavJob = null; }
            // bugfix: drafting a sleeper left them asleep standing up
            if (c.State is ColState.Sleeping or ColState.Heal) c.BedRelease();
            c.Bed = null;
            if (c.State is ColState.Building or ColState.GoBuild or ColState.Mining or ColState.GoMine
                or ColState.GoWork or ColState.Working or ColState.Flee)
                c.State = ColState.Idle;
            c.OrderFoe = null;
            c.HasMoveOrder = false;
            c.Arriving = false;
            c.Path = null;
        }
        else
        {
            c.OrderFoe = null;
            c.HasMoveOrder = false;
            c.State = ColState.Idle;
        }
    }

    private void ToggleDraftSelected()
    {
        var sel = _view.Sel.Cols.Count > 0 ? _view.Sel.Cols.ToList()
            : _view.Sel.Col != null ? new List<Colonist> { _view.Sel.Col } : new();
        if (sel.Count == 0) return;
        bool anyUndrafted = sel.Any(c => !c.Drafted);
        foreach (var c in sel) SetDrafted(c, anyUndrafted);
    }

    // ===================================================== game lifecycle ==

    private void StartWorldGen()
    {
        _view.App = AppState.WorldGen;
        // fix: a stale static seed made every new-world open on the same map
        _seedText = Random.Shared.NextInt64().ToString();
        _landPicked = false;                     // pick a fresh site for this world
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
            Biome = _biomePick,
            ForestMul = _genVals[4],
            WaterMul = _genVals[5],
            LandingX = _landPicked ? _landX : 0,
            LandingY = _landPicked ? _landY : 0,
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
        // LANDING: start high and glide in (any click/key skips)
        _landingT = LandingDur;
        Camera.Zoom = 0.30f;
        CenterOn(_game.HubRef.CenterTile.X, _game.HubRef.CenterTile.Y);
        _autosaveT = 0;
    }

    private void FinishLanding()
    {
        _landingT = 0;
        Camera.Zoom = 1f;
        CenterOn(_game.HubRef.CenterTile.X, _game.HubRef.CenterTile.Y);
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

        // fps accounting (EMA over rendered frames)
        float ms = (float)_fpsClock.Elapsed.TotalMilliseconds;
        _fpsClock.Restart();
        _view.FrameMs = _view.FrameMs <= 0 ? ms : _view.FrameMs * 0.92f + ms * 0.08f;
        _view.Fps = 1000f / MathF.Max(0.001f, _view.FrameMs);

        // one-time notice when custom art is active
        if (!_assetsReported)
        {
            _assetsReported = true;
            if (Sprites.AssetsLoaded > 0)
            {
                _toast = $"{Sprites.AssetsLoaded} custom textures active";
                _toastT = 4f;
            }
        }

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
                        Terrain.Tree => Pal.C(44, 92, 56),
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
        1 => "ADV PART  (2 gears + 1 circuit)",
        2 => "SCIENCE PACK  (1 iron + 1 copper)",
        3 => "LOGISTIC DRONE  (1 iron + 1 copper)",
        4 => "WAR BOT  (2 iron + 1 copper)",
        5 => "RECYCLE SLAG  (3 slag > 2 stone)",
        6 => "GEAR  (2 iron plates)",
        _ => "CIRCUIT  (1 copper + 1 crystal)",
    };
}
