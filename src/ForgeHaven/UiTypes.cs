using System.Drawing;

namespace ForgeHaven;

// ---------------------------------------------------------------------------
//  UI / view types — GAME PROJECT ONLY (never compiled into the headless
//  SimTests harness). Anything a phone port would not need lives here.
//  Sim files (Types/World/Buildings/Colonist/Enemies/Units/Game/Persistence/
//  Loc) must not reference anything in this file.
// ---------------------------------------------------------------------------

public sealed class UiButton
{
    public Rectangle R;
    public string Id = "";
    public string Text = "";
    public string Sub = "";
    public bool Enabled = true;
    public bool Active;
    public Color? Tint;

    public UiButton(Rectangle r, string id, string text, string sub = "", bool enabled = true, bool active = false, Color? tint = null)
    {
        R = r; Id = id; Text = text; Sub = sub; Enabled = enabled; Active = active; Tint = tint;
    }
}

/// <summary>A horizontal click/drag slider (world-gen screen).</summary>
public sealed class UiSlider
{
    public Rectangle R;
    public string Id = "";
    public string Label = "";
    public float Min = 0f, Max = 1f, Value = 0.5f;
    public string Fmt = "0.00";

    public float Frac => Max <= Min ? 0 : (Value - Min) / (Max - Min);
}

/// <summary>A simple text field (seed entry).</summary>
public sealed class UiTextBox
{
    public Rectangle R;
    public string Id = "";
    public string Label = "";
    public string Text = "";
    public bool Focused;
}

/// <summary>Current selection (colonist, building, or raider).</summary>
public sealed class Selection
{
    public Colonist? Col;
    public Building? B;
    public Raider? Foe;
    public readonly List<Colonist> Cols = new();     // Phase 1: multi-select (draft groups)
    public bool IsNone => Col == null && B == null && Foe == null && Cols.Count == 0;
    public void Clear() { Col = null; B = null; Foe = null; Cols.Clear(); }
}

/// <summary>Positions of the draggable HUD panels. (-1,-1) = default anchor
/// for the current window size. Persisted through Settings.Panels.</summary>
public static class PanelLayout
{
    public static Point Pawn = new(-1, -1);
    public static Point Stock = new(-1, -1);
    public static Point Sel = new(-1, -1);

    public static Rectangle PawnRect(Size c) =>
        Place(new Rectangle(10, c.Height - 256 - 56, 322, 256), Pawn, c);
    public static Rectangle StockRect(Size c) =>
        Place(new Rectangle(8, 30, 500, 150), Stock, c);   // top-left (the resource bar)
    public static Rectangle SelRect(Size c) =>
        Place(new Rectangle(c.Width - 260, 36, 250, 214), Sel, c);

    static Rectangle Place(Rectangle def, Point p, Size c)
    {
        var r = p.X < 0 ? def : new Rectangle(p.X, p.Y, def.Width, def.Height);
        // keep the panel fully on screen
        r.X = Math.Clamp(r.X, 0, Math.Max(0, c.Width - r.Width));
        r.Y = Math.Clamp(r.Y, 0, Math.Max(0, c.Height - r.Height));
        return r;
    }

    public static void SaveTo(Settings s) =>
        s.Panels = $"pawn:{Pawn.X},{Pawn.Y};stock:{Stock.X},{Stock.Y};sel:{Sel.X},{Sel.Y}";

    public static void LoadFrom(string? v)
    {
        Pawn = Stock = Sel = new Point(-1, -1);
        if (string.IsNullOrEmpty(v)) return;
        foreach (var part in v.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            if (kv.Length != 2) continue;
            var xy = kv[1].Split(',');
            if (xy.Length != 2 || !int.TryParse(xy[0], out int x) || !int.TryParse(xy[1], out int y)) continue;
            var p = new Point(x, y);
            if (kv[0] == "pawn") Pawn = p;
            else if (kv[0] == "stock") Stock = p;
            else if (kv[0] == "sel") Sel = p;
        }
    }
}

/// <summary>Everything the renderer needs about input/view state each frame.</summary>
public struct ViewState
{
    public AppState App;
    public ToolKind Tool;
    public (int x, int y)? PileFrom, PileTo;      // stockpile drag preview
    public BuildKind? ToolBuilding;
    public Dir ToolFacing;
    public int GhostX, GhostY;
    public bool GhostValid;
    public string GhostReason;
    public bool GhostVisible;
    public List<UiButton> Buttons;
    public Point Mouse;
    public string? Hover;
    public bool Paused;
    public float Speed;
    public bool ShowHelp;
    public bool AltDown;
    public bool ShowGrid;
    public bool ShowHistory;        // MADDOG iter-6: event log panel [L]
    public int HistScroll;
    public bool ShowStats;          // MADDOG iter-7: statistics panel [S]
    public bool ShowChron;          // SOULS: colony chronicle [C]
    public bool ShowProd;           // ORGANISM: production health [F5]
    public float Landing01;         // LANDING: 0..1 cinematic progress
    public bool LandingHideHub;     // hub still falling: hide the placed one
    public int LandingPawnsOut;     // how many pawns have popped out
    public bool LandPicked;         // worldgen: landing-site marker
    public float LandingShake;      // touchdown screenshake magnitude (px)
    public float LandWorldX, LandWorldY;
    public BuildCategory? OpenCategory;
    public Selection Sel;
    public string? ModalTitle;

    public OverlayMode Ovl;
    public List<UiSlider> Sliders;
    public UiTextBox? SeedBox;
    public Image? GenPreview;
    public string GenCaption;
    public int Storyteller;                 // world-gen selection

    // v0.4 panels
    public List<AlertLine> Alerts;          // clickable alerts stack
    public bool ShowMinimap;
    public bool ShowWork;                   // work priority screen
    public bool ShowDebug;                  // dev menu panel
    public bool DevMode;                    // settings gate
    public float Sunlight;                  // 0..1 for night rendering

    // Phase 1: pawn panel tabs, drag-select box, perf overlay
    public int PawnTab;                     // 0 overview / 1 health / 2 work / 3 combat
    public bool ResOpen;                    // collapsible stock panel (art pass)
    public Rectangle? SelBox;               // active drag-selection box (screen space)
    public bool ShowPerf;                   // dev perf overlay
    public bool ShowFps;                    // settings: always-on fps counter
    public float Fps, FrameMs;              // measured (EMA)
}
