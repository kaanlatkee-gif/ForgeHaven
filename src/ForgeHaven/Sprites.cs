using System.Drawing;
using System.Drawing.Drawing2D;

namespace ForgeHaven;

// ---------------------------------------------------------------------------
//  Sprites: all textures are BAKED IN CODE at startup (no asset files).
//  Deterministic per-sprite seeds keep the look stable between runs/saves.
// ---------------------------------------------------------------------------

public static class Sprites
{
    public const int S = 36; // px per tile at zoom 1

    private static bool _init;

    // terrain
    public static Bitmap[] Ground = null!;
    public static Bitmap[] Decor = null!; // pebbles / tufts overlays
    public static Bitmap Rock = null!, IronOre = null!, Crystal = null!, Flora = null!;
    public static Bitmap CopperOre = null!, Water = null!;
    public static readonly Bitmap[] Belt = new Bitmap[4]; // by direction
    public static readonly Bitmap[] FastBelt = new Bitmap[4];
    public static readonly Bitmap[] Rail = new Bitmap[4];
    public static readonly Bitmap[] ElevatedRail = new Bitmap[4];

    // buildings
    private static readonly Dictionary<BuildKind, Bitmap> _buildings = new();
    public static Bitmap Hub = null!;
    public static readonly Bitmap[] WindRotor = new Bitmap[3];

    // creatures
    private static readonly Dictionary<(int shirt, int skin, int hair, Dir dir), Bitmap> _pawns = new();
    public static Bitmap RaiderImg = null!, RaiderApex = null!;
    public static Bitmap GuardBotImg = null!, TrainImg = null!;

    public static Bitmap Building(BuildKind k)
    {
        EnsureInit();
        // TryGetValue: a building with an unregistered sprite draws a visible
        // "missing" checker instead of crashing the render loop.
        return _buildings.TryGetValue(k, out var bmp) ? bmp : Missing;
    }

    /// <summary>Hot-pink checker for kinds with no baked sprite.</summary>
    public static readonly Bitmap Missing = new Bitmap(S, S);

    public static Bitmap Pawn(int shirt, int skin, int hair, Dir dir)
    {
        EnsureInit();
        var key = (shirt, skin, hair, dir);
        if (!_pawns.TryGetValue(key, out var b))
        {
            b = BakePawn(shirt, skin, hair, dir);
            _pawns[key] = b;
        }
        return b;
    }

    // ------------------------------------------------------------ baking ---

    public static void EnsureInit()
    {
        if (_init) return;
        _init = true;

        // "missing sprite" fallback (hot-pink checker) — see Building()
        using (var mg = Graphics.FromImage(Missing))
        {
            mg.Clear(Pal.C(30, 30, 30));
            using var pen = new Pen(Pal.C(255, 0, 144), 3f);
            mg.DrawLine(pen, 0, 0, S, S);
            mg.DrawLine(pen, S, 0, 0, S);
            mg.DrawRectangle(pen, 1, 1, S - 2, S - 2);
        }

        Ground = new Bitmap[3];
        for (int i = 0; i < 3; i++) Ground[i] = BakeGround(i);
        Decor = new Bitmap[3];
        for (int i = 0; i < 3; i++) Decor[i] = BakeDecor(i);
        Rock = BakeRock();
        IronOre = BakeIron();
        Crystal = BakeCrystal();
        Flora = BakeFlora();
        CopperOre = BakeCopper();
        Water = BakeWater();
        for (int i = 0; i < 4; i++) Belt[i] = BakeBelt((Dir)i);
        for (int i = 0; i < 4; i++) FastBelt[i] = BakeFastBelt((Dir)i);
        for (int i = 0; i < 4; i++) Rail[i] = BakeRail((Dir)i, false);
        for (int i = 0; i < 4; i++) ElevatedRail[i] = BakeRail((Dir)i, true);

        _buildings[BuildKind.Drill] = BakeDrill(false);
        _buildings[BuildKind.DeepDrill] = BakeDrill(true);
        _buildings[BuildKind.Smelter] = BakeSmelter();
        _buildings[BuildKind.Fabricator] = BakeFabricator();
        _buildings[BuildKind.BioProcessor] = BakeBio();
        _buildings[BuildKind.StorageCrate] = BakeStorage();
        _buildings[BuildKind.Reactor] = BakeReactor();
        _buildings[BuildKind.SolarPanel] = BakeSolar();
        _buildings[BuildKind.WindTurbine] = BakeWindBase();
        _buildings[BuildKind.Battery] = BakeBattery();
        _buildings[BuildKind.Wall] = BakeWall();
        _buildings[BuildKind.Turret] = BakeTurretBase(false);
        _buildings[BuildKind.HeavyTurret] = BakeTurretBase(true);
        _buildings[BuildKind.Watchtower] = BakeWatchtower();
        _buildings[BuildKind.Hab] = BakeHab();
        _buildings[BuildKind.MessTable] = BakeMessTable();
        _buildings[BuildKind.Lamp] = BakeLamp();
        _buildings[BuildKind.Garden] = BakeGarden();
        _buildings[BuildKind.MedBed] = BakeMedBed();
        _buildings[BuildKind.Lab] = BakeLab();
        _buildings[BuildKind.Splitter] = BakeSplitter();
        _buildings[BuildKind.Junction] = BakeJunction();
        _buildings[BuildKind.OverflowRouter] = BakeRouter();
        _buildings[BuildKind.PowerPole] = BakePole();
        _buildings[BuildKind.Merger] = BakeMerger();
        _buildings[BuildKind.FilterSplitter] = BakeFilterSplitter();
        _buildings[BuildKind.Inserter] = BakeInserter();
        _buildings[BuildKind.TrainStop] = BakeTrainStop();
        _buildings[BuildKind.Locomotive] = BakeTrainIcon();
        _buildings[BuildKind.DronePort] = BakeDronePort();
        _buildings[BuildKind.Pipe] = BakePipe();
        _buildings[BuildKind.Pump] = BakePump();
        _buildings[BuildKind.Tank] = BakeTank();
        _buildings[BuildKind.Boiler] = BakeBoiler();
        _buildings[BuildKind.SteamEngine] = BakeSteamEngine();
        _buildings[BuildKind.FabT2] = BakeFabTier(2);
        _buildings[BuildKind.FabT3] = BakeFabTier(3);
        _buildings[BuildKind.SpikeTrap] = BakeSpike();
        _buildings[BuildKind.IED] = BakeIED();
        _buildings[BuildKind.ShieldGen] = BakeShield();
        _buildings[BuildKind.BotFactory] = BakeBotFactory();
        GuardBotImg = BakeGuardBot();
        TrainImg = BakeTrain();
        for (int i = 0; i < 3; i++) WindRotor[i] = BakeWindRotor(i * 2.09f);
        Hub = BakeHub();

        RaiderImg = BakeRaider(false);
        RaiderApex = BakeRaider(true);
    }

    private static Bitmap Blank()
    {
        var b = new Bitmap(S, S);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.Transparent);
        return b;
    }

    private static Bitmap Make(Action<Graphics, Bitmap> draw)
    {
        var b = new Bitmap(S, S);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            draw(g, b);
        }
        return b;
    }

    private static void Noise(Bitmap b, int seed, int amount)
    {
        var r = new Random(seed);
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
            {
                var c = b.GetPixel(x, y);
                if (c.A == 0) continue;
                int n = r.Next(-amount, amount + 1);
                b.SetPixel(x, y, Color.FromArgb(c.A,
                    Math.Clamp(c.R + n, 0, 255), Math.Clamp(c.G + n, 0, 255), Math.Clamp(c.B + n, 0, 255)));
            }
    }

    private static void Specks(Graphics g, Random r, Color col, int n, int size = 2)
    {
        using var br = new SolidBrush(col);
        for (int i = 0; i < n; i++)
            g.FillRectangle(br, r.Next(2, S - size - 1), r.Next(2, S - size - 1), size, size);
    }

    // ------------------------------------------------------------ terrain --

    private static Bitmap BakeGround(int variant)
    {
        var bases = new[] { Pal.C(44, 48, 52), Pal.C(42, 47, 54), Pal.C(46, 48, 50) };
        var r = new Random(100 + variant);
        var b = Make((g, bmp) =>
        {
            g.Clear(bases[variant]);
            Specks(g, r, Pal.CA(255, Pal.C(52, 57, 62)), 20, 1);
            Specks(g, r, Pal.CA(255, Pal.C(36, 40, 44)), 16, 2);
            Specks(g, r, Pal.CA(255, Pal.C(58, 62, 66)), 8, 1);
        });
        Noise(b, 55 + variant, 5);
        return b;
    }

    private static Bitmap BakeDecor(int variant)
    {
        var r = new Random(900 + variant);
        return Make((g, bmp) =>
        {
            if (variant == 0) // pebbles
                Specks(g, r, Pal.C(70, 76, 84), 5, 2);
            else if (variant == 1) // dry tuft
            {
                using var p = new Pen(Pal.C(86, 96, 64), 1f);
                for (int i = 0; i < 5; i++)
                {
                    int x = r.Next(4, S - 4), y = r.Next(4, S - 6);
                    g.DrawLine(p, x, y + 3, x + r.Next(-2, 3), y);
                }
            }
            else // faint crack
            {
                using var p = new Pen(Pal.C(34, 38, 42), 1f);
                int x = r.Next(6, S - 6), y = r.Next(2, 6);
                for (int i = 0; i < 4; i++)
                {
                    int nx = x + r.Next(-3, 4), ny = y + r.Next(4, 8);
                    g.DrawLine(p, x, y, nx, ny);
                    x = nx; y = ny;
                }
            }
        });
    }

    private static Bitmap BakeRock()
    {
        var r = new Random(301);
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.C(60, 63, 70));
            for (int i = 0; i < 7; i++)
            {
                int w = r.Next(10, 22), h = r.Next(8, 18);
                int x = r.Next(2, S - w - 2), y = r.Next(2, S - h - 2);
                var shade = Pal.C(74 + r.Next(18), 78 + r.Next(18), 86 + r.Next(14));
                using var br = new SolidBrush(shade);
                g.FillEllipse(br, x, y, w, h);
            }
            using var p = new Pen(Pal.C(40, 42, 48), 1f);
            g.DrawLine(p, 6, 20, 16, 26);
            g.DrawLine(p, 22, 8, 30, 16);
            g.DrawLine(p, 10, 8, 14, 14);
        });
        Noise(b, 302, 7);
        return b;
    }

    private static Bitmap BakeIron()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            var r = new Random(401);
            for (int i = 0; i < 5; i++)
            {
                int size = r.Next(7, 13);
                int x = 4 + (i % 3) * 10 + r.Next(-2, 3);
                int y = 4 + (i / 3) * 14 + r.Next(-2, 3);
                using var br = new SolidBrush(Pal.C(122, 74, 40));
                g.FillEllipse(br, x, y, size, size);
                using var hi = new SolidBrush(Pal.C(190, 122, 62));
                g.FillEllipse(hi, x + 1, y + 1, size / 2, size / 2);
                using var sp = new SolidBrush(Pal.C(232, 166, 96));
                g.FillRectangle(sp, x + size / 3, y + size / 3, 2, 2);
            }
        });
        Noise(b, 402, 9);
        return b;
    }

    private static Bitmap BakeCrystal()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            // glow bed
            using (var glow = new SolidBrush(Pal.CA(60, Pal.Crystal)))
                g.FillEllipse(glow, 2, 2, S - 4, S - 4);
            var r = new Random(501);
            for (int i = 0; i < 4; i++)
            {
                float cx = 18 + (i % 2) * 8 - 6 + r.Next(-2, 3);
                float cy = 18 + (i / 2) * 8 - 6 + r.Next(-2, 3);
                float w = 6 + r.Next(3), h = 13 + r.Next(6);
                var pts = new[]
                {
                    new PointF(cx, cy - h / 2),
                    new PointF(cx + w / 2, cy - h / 6),
                    new PointF(cx + w / 3, cy + h / 2),
                    new PointF(cx - w / 3, cy + h / 2),
                    new PointF(cx - w / 2, cy - h / 6),
                };
                using var br = new SolidBrush(Pal.C(126, 66, 214));
                g.FillPolygon(br, pts);
                using var edge = new Pen(Pal.C(200, 160, 255), 1f);
                g.DrawLine(edge, pts[0], pts[1]);
            }
        });
        return b;
    }

    private static Bitmap BakeFlora()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            using (var bed = new SolidBrush(Pal.CA(40, Pal.Flora)))
                g.FillEllipse(bed, 2, 2, S - 4, S - 4);
            var r = new Random(601);
            using var stem = new Pen(Pal.C(30, 130, 108), 1.6f);
            for (int i = 0; i < 6; i++)
            {
                float x = 5 + r.Next(S - 10), y = S - 5 - r.Next(5);
                float tipX = x + r.Next(-5, 6), tipY = y - r.Next(8, 15);
                g.DrawLine(stem, x, y, tipX, tipY);
                using var tip = new SolidBrush(Pal.C(96, 255, 208));
                g.FillEllipse(tip, tipX - 1.6f, tipY - 1.6f, 3.2f, 3.2f);
            }
        });
        return b;
    }

    private static Bitmap BakeBelt(Dir d)
    {
        bool horiz = d is Dir.Right or Dir.Left;
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.BeltCol);
            // side rails
            using (var rail = new SolidBrush(Pal.C(32, 36, 42)))
            {
                if (horiz) { g.FillRectangle(rail, 0, 2, S, 3); g.FillRectangle(rail, 0, S - 5, S, 3); }
                else { g.FillRectangle(rail, 2, 0, 3, S); g.FillRectangle(rail, S - 5, 0, 3, S); }
            }
            // rollers
            using (var roller = new Pen(Pal.C(70, 78, 88), 2f))
                for (int i = 0; i < 4; i++)
                {
                    int p = 6 + i * 8;
                    if (horiz) g.DrawLine(roller, p, 6, p, S - 6);
                    else g.DrawLine(roller, 6, p, S - 6, p);
                }
            // center arrow
            float cx = S / 2f, cy = S / 2f;
            int sgn = d is Dir.Right or Dir.Down ? 1 : -1;
            var dx = horiz ? sgn : 0; var dy = horiz ? 0 : sgn;
            var pts = new[]
            {
                new PointF(cx + dx * 6, cy + dy * 6),
                new PointF(cx - dx * 5 - (horiz ? 0 : 5), cy - dy * 5 - (horiz ? 5 : 0)),
                new PointF(cx - dx * 5 + (horiz ? 0 : 5), cy - dy * 5 + (horiz ? 5 : 0)),
            };
            using var br = new SolidBrush(Pal.CA(170, Pal.Accent));
            g.FillPolygon(br, pts);
        });
        Noise(b, 700 + (int)d, 4);
        return b;
    }

    // -------------------------------------------------------- buildings ----

    private static void PanelBase(Graphics g, Color tone)
    {
        using var br = new SolidBrush(Pal.Darken(tone, 0.25f));
        g.FillRectangle(br, 1, 1, S - 2, S - 2);
        using var br2 = new SolidBrush(tone);
        g.FillRectangle(br2, 3, 3, S - 6, S - 6);
        using var pen = new Pen(Pal.Darken(tone, 0.45f), 1f);
        g.DrawRectangle(pen, 1.5f, 1.5f, S - 3, S - 3);
        // corner rivets
        using var riv = new SolidBrush(Pal.Lighten(tone, 0.25f));
        foreach (var (x, y) in new[] { (3, 3), (S - 5, 3), (3, S - 5), (S - 5, S - 5) })
            g.FillEllipse(riv, x, y, 2, 2);
    }

    private static Bitmap BakeDrill(bool deep)
    {
        var tone = deep ? Pal.C(105, 82, 55) : Pal.C(120, 96, 64);
        var b = Make((g, bmp) =>
        {
            PanelBase(g, tone);
            // gantry
            using (var p = new Pen(Pal.C(45, 40, 34), 2.4f))
            {
                g.DrawLine(p, 7, 7, 18, 20);
                g.DrawLine(p, 29, 7, 18, 20);
            }
            // drill bit
            var pts = new[] { new PointF(18, 14), new PointF(deep ? 24 : 23, 20), new PointF(18, deep ? 31 : 29), new PointF(deep ? 12 : 13, 20) };
            using (var br = new SolidBrush(Pal.C(168, 120, 60)))
                g.FillPolygon(br, pts);
            using (var hl = new Pen(Pal.C(230, 190, 120), 1f))
                g.DrawLine(hl, 18, 14, 18, deep ? 29 : 27);
            if (deep) // hazard striping on the bottom lip
                using (var hz = new Pen(Pal.C(214, 174, 60), 2f))
                    for (int i = 0; i < 3; i++)
                        g.DrawLine(hz, 6 + i * 8, S - 6, 10 + i * 8, S - 10);
        });
        Noise(b, deep ? 802 : 801, 6);
        return b;
    }

    private static Bitmap BakeSmelter()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(150, 84, 60));
            using (var frame = new SolidBrush(Pal.C(70, 42, 32)))
                g.FillRectangle(frame, 7, 8, 22, 20);
            using (var mouth = new SolidBrush(Pal.C(20, 12, 8)))
                g.FillRectangle(mouth, 10, 12, 16, 12);
            // heat glow
            using (var glow = new SolidBrush(Pal.C(255, 150, 50)))
                g.FillEllipse(glow, 13, 15, 10, 6);
            using (var ember = new SolidBrush(Pal.C(255, 220, 130)))
            {
                g.FillEllipse(ember, 15, 16, 3, 3);
                g.FillEllipse(ember, 19, 17, 2, 2);
            }
            // chimney
            using (var ch = new SolidBrush(Pal.C(60, 40, 30)))
                g.FillRectangle(ch, 24, 2, 5, 9);
        });
        Noise(b, 803, 6);
        return b;
    }

    private static Bitmap BakeFabricator()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(96, 96, 128));
            using (var bed = new SolidBrush(Pal.C(52, 52, 74)))
                g.FillRectangle(bed, 7, 20, 22, 9);
            // print head gantry
            using (var p = new Pen(Pal.C(42, 42, 60), 2f))
            {
                g.DrawLine(p, 8, 8, 8, 22);
                g.DrawLine(p, 28, 8, 28, 22);
                g.DrawLine(p, 8, 9, 28, 9);
            }
            using var head = new SolidBrush(Pal.C(90, 200, 255));
            g.FillRectangle(head, 16, 10, 5, 5);
            using var beam = new SolidBrush(Pal.CA(120, Pal.C(90, 200, 255)));
            g.FillRectangle(beam, 17, 15, 3, 7);
        });
        Noise(b, 804, 5);
        return b;
    }

    private static Bitmap BakeBio()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(56, 120, 96));
            using (var tank = new SolidBrush(Pal.CA(220, Pal.C(36, 84, 66))))
                g.FillRectangle(tank, 8, 6, 20, 24);
            using (var glass = new SolidBrush(Pal.CA(200, Pal.C(66, 160, 128))))
                g.FillRectangle(glass, 10, 8, 16, 20);
            using var bub = new SolidBrush(Pal.CA(230, Pal.C(140, 255, 200)));
            var r = new Random(805);
            for (int i = 0; i < 6; i++)
                g.FillEllipse(bub, 11 + r.Next(13), 9 + r.Next(18), 2, 2);
            using (var cap = new SolidBrush(Pal.C(30, 60, 48)))
                g.FillRectangle(cap, 7, 4, 22, 4);
        });
        Noise(b, 805, 4);
        return b;
    }

    private static Bitmap BakeStorage()
    {
        var b = Make((g, bmp) =>
        {
            using var wood = new SolidBrush(Pal.C(122, 98, 66));
            g.FillRectangle(wood, 4, 6, S - 8, S - 10);
            using var dark = new Pen(Pal.C(74, 58, 38), 1.4f);
            for (int i = 1; i < 3; i++)
                g.DrawLine(dark, 4, 6 + i * 8, S - 4, 6 + i * 8);
            g.DrawRectangle(dark, 4, 6, S - 8, S - 10);
            using var brace = new Pen(Pal.C(84, 66, 44), 2f);
            g.DrawLine(brace, 5, 7, S - 5, S - 5);
        });
        Noise(b, 806, 7);
        return b;
    }

    private static Bitmap BakeReactor()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(120, 105, 50));
            using (var ring = new Pen(Pal.C(40, 38, 26), 3f))
                g.DrawEllipse(ring, 7, 7, 22, 22);
            using (var core = new SolidBrush(Pal.C(255, 230, 120)))
                g.FillEllipse(core, 13, 13, 10, 10);
            using (var halo = new SolidBrush(Pal.CA(90, Pal.C(255, 220, 90))))
                g.FillEllipse(halo, 10, 10, 16, 16);
            // hazard corners
            using var hz = new Pen(Pal.C(210, 170, 40), 2f);
            g.DrawLine(hz, 3, S - 4, 8, S - 9);
            g.DrawLine(hz, S - 3, 4, S - 8, 9);
        });
        Noise(b, 807, 5);
        return b;
    }

    private static Bitmap BakeSolar()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(44, 62, 88));
            using (var cell = new SolidBrush(Pal.C(60, 96, 148)))
                g.FillRectangle(cell, 4, 4, S - 8, S - 8);
            using (var gl = new Pen(Pal.C(32, 48, 70), 1f))
                for (int i = 1; i < 3; i++)
                {
                    g.DrawLine(gl, 4, 4 + i * 9, S - 4, 4 + i * 9);
                    g.DrawLine(gl, 4 + i * 9, 4, 4 + i * 9, S - 4);
                }
            using (var glare = new Pen(Pal.CA(120, Pal.C(160, 220, 255)), 2f))
                g.DrawLine(glare, 8, S - 12, S - 14, 6);
        });
        Noise(b, 808, 4);
        return b;
    }

    private static Bitmap BakeWindBase()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(84, 92, 100));
            using var pole = new Pen(Pal.C(160, 168, 176), 3f);
            g.DrawLine(pole, 18, 30, 18, 12);
            using var nac = new SolidBrush(Pal.C(200, 208, 216));
            g.FillEllipse(nac, 15, 8, 6, 6);
        });
        Noise(b, 809, 5);
        return b;
    }

    private static Bitmap BakeWindRotor(float angle)
    {
        return Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            using var blade = new Pen(Pal.C(225, 232, 238), 2.4f);
            for (int i = 0; i < 3; i++)
            {
                float a = angle + i * 2.094f;
                g.DrawLine(blade, 18, 11, 18 + MathF.Cos(a) * 11, 11 + MathF.Sin(a) * 11);
            }
        });
    }

    private static Bitmap BakeBattery()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(62, 110, 84));
            using (var box = new SolidBrush(Pal.C(34, 56, 44)))
                g.FillRectangle(box, 8, 7, 20, 22);
            using var grid = new Pen(Pal.C(70, 120, 92), 1f);
            for (int i = 0; i < 4; i++)
                g.DrawRectangle(grid, 10, 9 + i * 5, 16, 4); // charge pip frames
            using var bolt = new SolidBrush(Pal.C(230, 220, 90));
            g.FillPolygon(bolt, new[] { new PointF(19, 2), new PointF(24, 2), new PointF(21, 8), new PointF(15, 8) });
        });
        Noise(b, 810, 4);
        return b;
    }

    private static Bitmap BakeWall()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(96, 100, 110));
            using var brace = new Pen(Pal.C(66, 70, 78), 2f);
            g.DrawLine(brace, 4, 4, S - 4, S - 4);
            g.DrawLine(brace, S - 4, 4, 4, S - 4);
        });
        Noise(b, 811, 6);
        return b;
    }

    private static Bitmap BakeTurretBase(bool heavy)
    {
        var tone = heavy ? Pal.C(116, 64, 74) : Pal.C(96, 64, 84);
        var b = Make((g, bmp) =>
        {
            PanelBase(g, tone);
            // octagonal armored pivot
            var pts = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                float a = i * MathF.PI / 4f + MathF.PI / 8f;
                pts[i] = new PointF(18 + MathF.Cos(a) * (heavy ? 12 : 10), 18 + MathF.Sin(a) * (heavy ? 12 : 10));
            }
            using (var br = new SolidBrush(Pal.Darken(tone, 0.3f)))
                g.FillPolygon(br, pts);
            using (var br = new SolidBrush(Pal.Lighten(tone, 0.15f)))
                g.FillEllipse(br, 13, 13, 10, 10);
            if (heavy)
                using (var hz = new Pen(Pal.C(214, 174, 60), 2f))
                    g.DrawArc(hz, 8, 8, 20, 20, 30, 60);
        });
        Noise(b, heavy ? 813 : 812, 5);
        return b;
    }

    private static Bitmap BakeWatchtower()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            using var legs = new Pen(Pal.C(96, 76, 50), 2.4f);
            g.DrawLine(legs, 10, S - 4, 14, 10);
            g.DrawLine(legs, S - 10, S - 4, S - 14, 10);
            using (var deck = new SolidBrush(Pal.C(126, 100, 66)))
                g.FillRectangle(deck, 8, 6, S - 16, 8);
            using (var rail = new Pen(Pal.C(84, 66, 44), 1.6f))
                g.DrawRectangle(rail, 8, 3, S - 16, 10);
        });
        Noise(b, 814, 5);
        return b;
    }

    private static Bitmap BakeHab()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(70, 96, 120));
            using (var dome = new SolidBrush(Pal.C(96, 128, 152)))
                g.FillEllipse(dome, 5, 5, 26, 26);
            using (var door = new SolidBrush(Pal.C(30, 38, 48)))
                g.FillRectangle(door, 15, 22, 7, 12);
            using (var win = new SolidBrush(Pal.C(255, 220, 140))) // warm window light
            {
                g.FillEllipse(win, 10, 12, 5, 5);
                g.FillEllipse(win, 21, 12, 5, 5);
            }
        });
        Noise(b, 815, 4);
        return b;
    }

    private static Bitmap BakeMessTable()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            using var wood = new SolidBrush(Pal.C(130, 100, 76));
            g.FillEllipse(wood, 6, 8, 24, 20);
            using var rim = new Pen(Pal.C(84, 64, 46), 1.4f);
            g.FillEllipse(Pal.B(Pal.C(84, 64, 46)), 4, 14, 5, 5);   // stools
            g.FillEllipse(Pal.B(Pal.C(84, 64, 46)), 27, 14, 5, 5);
            g.FillEllipse(Pal.B(Pal.C(84, 64, 46)), 15, 5, 5, 5);
            using var plate = new SolidBrush(Pal.C(210, 214, 218));
            g.FillEllipse(plate, 12, 13, 5, 5);
            g.FillEllipse(plate, 19, 17, 5, 5);
        });
        Noise(b, 816, 4);
        return b;
    }

    private static Bitmap BakeLamp()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            using (var halo = new SolidBrush(Pal.CA(46, Pal.C(255, 220, 130))))
                g.FillEllipse(halo, 2, 0, 32, 32);
            using var pole = new Pen(Pal.C(70, 74, 82), 2.2f);
            g.DrawLine(pole, 18, 32, 18, 12);
            using var bulb = new SolidBrush(Pal.C(255, 226, 140));
            g.FillEllipse(bulb, 14, 6, 8, 8);
        });
        return b;
    }

    private static Bitmap BakeGarden()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.C(52, 62, 44));
            var r = new Random(817);
            using var sprout = new Pen(Pal.C(92, 160, 96), 1.6f);
            for (int i = 0; i < 9; i++)
            {
                int x = 6 + (i % 3) * 12, y = 6 + (i / 3) * 12;
                g.DrawLine(sprout, x, y + 4, x, y);
                using var petal = new SolidBrush(i % 2 == 0 ? Pal.C(150, 220, 130) : Pal.C(230, 160, 190));
                g.FillEllipse(petal, x - 2, y - 2, 4, 4);
            }
        });
        Noise(b, 817, 6);
        return b;
    }

    private static Bitmap BakeMedBed()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(140, 148, 158));
            using (var bed = new SolidBrush(Pal.C(225, 230, 235)))
                g.FillRectangle(bed, 7, 9, 22, 12);
            using (var pillow = new SolidBrush(Pal.C(245, 248, 250)))
                g.FillRectangle(pillow, 7, 9, 6, 12);
            using (var cross = new SolidBrush(Pal.C(215, 70, 80)))
            {
                g.FillRectangle(cross, 21, 11, 6, 2);
                g.FillRectangle(cross, 23, 9, 2, 6);
            }
        });
        Noise(b, 818, 3);
        return b;
    }

    private static Bitmap BakeLab()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(96, 128, 150));
            using (var bench = new SolidBrush(Pal.C(46, 62, 74)))
                g.FillRectangle(bench, 5, 18, 26, 10);
            using (var screen = new SolidBrush(Pal.C(120, 230, 160)))
                g.FillRectangle(screen, 8, 6, 9, 11);
            using var trace = new Pen(Pal.C(20, 80, 40), 1f);
            g.DrawLine(trace, 9, 12, 16, 9);
            using var dish = new Pen(Pal.C(200, 212, 222), 1.6f);
            g.DrawLine(dish, 26, 18, 26, 8);
            g.DrawArc(dish, 21, 3, 10, 8, 200, 140);
        });
        Noise(b, 819, 4);
        return b;
    }

    private static Bitmap BakeSplitter()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.BeltCol);
            using (var rail = new SolidBrush(Pal.C(32, 36, 42)))
            {
                g.FillRectangle(rail, 0, 2, S, 3);
                g.FillRectangle(rail, 0, S - 5, S, 3);
                g.FillRectangle(rail, 2, 0, 3, S);
                g.FillRectangle(rail, S - 5, 0, 3, S);
            }
            using (var hub = new SolidBrush(Pal.C(84, 94, 106)))
                g.FillEllipse(hub, 8, 8, 20, 20);
            using var hubIn = new SolidBrush(Pal.C(56, 64, 74));
            g.FillEllipse(hubIn, 12, 12, 12, 12);
        });
        Noise(b, 820, 4);
        return b;
    }

    private static Bitmap BakeHub()
    {
        int s3 = S * 3;
        var b = new Bitmap(s3, s3);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Pal.C(30, 44, 58));
            using (var deck = new SolidBrush(Pal.C(38, 56, 72)))
                g.FillRectangle(deck, 4, 4, s3 - 8, s3 - 8);
            using (var p = new Pen(Pal.C(24, 36, 48), 2f))
            {
                for (int i = 1; i < 3; i++)
                {
                    g.DrawLine(p, i * S, 4, i * S, s3 - 4);
                    g.DrawLine(p, 4, i * S, s3 - 4, i * S);
                }
            }
            // central tower
            using (var t = new SolidBrush(Pal.C(52, 74, 94)))
                g.FillRectangle(t, s3 / 2 - 16, s3 / 2 - 22, 32, 44);
            using (var core = new SolidBrush(Pal.C(90, 190, 240)))
                g.FillEllipse(core, s3 / 2 - 8, s3 / 2 - 10, 16, 20);
            using (var halo = new Pen(Pal.CA(120, Pal.C(120, 210, 255)), 2f))
                g.DrawEllipse(halo, s3 / 2 - 12, s3 / 2 - 14, 24, 28);
            // mast
            using (var mast = new Pen(Pal.C(170, 190, 205), 2f))
                g.DrawLine(mast, s3 / 2, 14, s3 / 2, 4);
            // deck corner lights
            using var dot = new SolidBrush(Pal.C(120, 200, 240));
            foreach (var (x, y) in new[] { (10, 10), (s3 - 14, 10), (10, s3 - 14), (s3 - 14, s3 - 14) })
                g.FillEllipse(dot, x, y, 4, 4);
        }
        Noise(b, 900, 5);
        return b;
    }

    // ----------------------------------------------------------- pawns -----

    private static readonly Color[] Shirts =
    {
        Pal.C(84, 140, 150), Pal.C(140, 120, 90), Pal.C(120, 140, 90),
        Pal.C(150, 96, 96), Pal.C(96, 118, 160), Pal.C(128, 128, 140),
    };
    private static readonly Color[] Skins =
    {
        Pal.C(240, 200, 160), Pal.C(222, 176, 130), Pal.C(190, 140, 96), Pal.C(150, 105, 70),
    };
    private static readonly Color[] Hairs =
    {
        Pal.C(40, 32, 26), Pal.C(96, 66, 40), Pal.C(200, 176, 120),
        Pal.C(160, 160, 168), Pal.C(140, 60, 44), Pal.C(60, 70, 90),
    };

    private static Bitmap BakePawn(int shirt, int skin, int hair, Dir dir)
    {
        return Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            // shadow
            using (var sh = new SolidBrush(Pal.CA(70, Pal.C(0, 0, 0))))
                g.FillEllipse(sh, 10, 27, 16, 6);
            // body
            using (var body = new SolidBrush(Shirts[shirt]))
                g.FillEllipse(body, 10, 13, 16, 17);
            using (var seam = new SolidBrush(Pal.Darken(Shirts[shirt], 0.3f)))
                g.FillRectangle(seam, 10, 25, 16, 3);
            // head
            using (var hd = new SolidBrush(Skins[skin]))
                g.FillEllipse(hd, 11, 3, 14, 13);
            // hair cap varies by facing (RimWorld-style: hair on the back of the head)
            using var hb = new SolidBrush(Hairs[hair]);
            switch (dir)
            {
                case Dir.Up:    // back of the head: full coverage
                    g.FillEllipse(hb, 10.5f, 2, 15, 13);
                    break;
                case Dir.Down:  // facing us: top cap + eyes
                    g.FillPie(hb, 11, 2, 14, 12, 180, 180);
                    using (var eye = new SolidBrush(Pal.C(30, 26, 22)))
                    {
                        g.FillRectangle(eye, 14, 9, 2, 2);
                        g.FillRectangle(eye, 20, 9, 2, 2);
                    }
                    break;
                case Dir.Right: // profile, hair trailing left
                    g.FillPie(hb, 11, 2, 14, 12, 110, 220);
                    using (var eye = new SolidBrush(Pal.C(30, 26, 22)))
                        g.FillRectangle(eye, 20, 9, 2, 2);
                    break;
                case Dir.Left:
                    g.FillPie(hb, 11, 2, 14, 12, -70, 220);
                    using (var eye = new SolidBrush(Pal.C(30, 26, 22)))
                        g.FillRectangle(eye, 14, 9, 2, 2);
                    break;
            }
        });
    }

    private static Bitmap BakeRaider(bool apex)
    {
        float k = apex ? 1.5f : 1f;
        return Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            float cx = S / 2f, cy = S / 2f;
            // legs: 3 per side
            using (var leg = new Pen(Pal.Darken(apex ? Pal.C(120, 30, 56) : Pal.C(160, 44, 76), 0.2f), 1.6f))
                for (int i = -1; i <= 1; i++)
                {
                    g.DrawLine(leg, cx - 4 * k, cy + i * 5 * k, cx - 12 * k, cy + i * 8 * k + 4);
                    g.DrawLine(leg, cx + 4 * k, cy + i * 5 * k, cx + 12 * k, cy + i * 8 * k + 4);
                }
            // segmented carapace (facing UP: head at top)
            using var shell = new SolidBrush(apex ? Pal.C(126, 34, 58) : Pal.C(160, 44, 76));
            g.FillEllipse(shell, cx - 7 * k, cy - 2 * k, 14 * k, 12 * k);   // abdomen
            g.FillEllipse(shell, cx - 6 * k, cy - 9 * k, 12 * k, 9 * k);   // thorax
            using var head = new SolidBrush(Pal.Darken(apex ? Pal.C(126, 34, 58) : Pal.C(160, 44, 76), 0.25f));
            g.FillEllipse(head, cx - 5 * k, cy - 14 * k, 10 * k, 7 * k);   // head
            // dorsal stripes
            using (var st = new Pen(Pal.CA(140, Pal.C(60, 16, 30)), 1.4f))
                for (int i = 0; i < 3; i++)
                    g.DrawLine(st, cx - 5 * k, cy + (i * 3.4f) * k, cx + 5 * k, cy + (i * 3.4f) * k);
            // mandibles
            using (var mn = new Pen(Pal.C(230, 210, 190), 1.6f))
            {
                g.DrawLine(mn, cx - 4 * k, cy - 14 * k, cx - 7 * k, cy - 18 * k);
                g.DrawLine(mn, cx + 4 * k, cy - 14 * k, cx + 7 * k, cy - 18 * k);
            }
            // eyes
            using (var eye = new SolidBrush(Pal.C(255, 70, 70)))
            {
                g.FillEllipse(eye, cx - 3 * k, cy - 13 * k, 2.4f * k, 2.4f * k);
                g.FillEllipse(eye, cx + 1 * k, cy - 13 * k, 2.4f * k, 2.4f * k);
            }
            if (apex) // spikes
                using (var sp = new Pen(Pal.C(60, 16, 30), 2f))
                {
                    g.DrawLine(sp, cx, cy - 9 * k, cx, cy - 17 * k);
                    g.DrawLine(sp, cx - 6 * k, cy - 6 * k, cx - 11 * k, cy - 12 * k);
                    g.DrawLine(sp, cx + 6 * k, cy - 6 * k, cx + 11 * k, cy - 12 * k);
                }
        });
    }

    // -------------------------------------------------- v0.3 additions ----

    /// <summary>Copper deposit: dark base rock with teal patina nuggets.</summary>
    private static Bitmap BakeCopper()
    {
        var b = Make((g, bmp) =>
        {
            using (var base_ = new SolidBrush(Pal.C(52, 62, 68)))
                g.FillRectangle(base_, 2, 2, S - 4, S - 4);
            var r = new Random(6001);
            Specks(g, r, Pal.C(64, 190, 180), 16, 3);
            Specks(g, r, Pal.C(120, 220, 210), 7, 2);
            using (var dark = new Pen(Pal.C(38, 48, 54), 1f))
            {
                g.DrawRectangle(dark, 2.5f, 2.5f, S - 5, S - 5);
                g.DrawLine(dark, 6, 6, S - 8, S - 7);
            }
        });
        Noise(b, 601, 5);
        return b;
    }

    private static Bitmap BakeFastBelt(Dir d)
    {
        bool horiz = d is Dir.Right or Dir.Left;
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.C(58, 74, 98));
            using (var rail = new SolidBrush(Pal.C(34, 42, 58)))
            {
                if (horiz) { g.FillRectangle(rail, 0, 2, S, 3); g.FillRectangle(rail, 0, S - 5, S, 3); }
                else { g.FillRectangle(rail, 2, 0, 3, S); g.FillRectangle(rail, S - 5, 0, 3, S); }
            }
            // dense rollers
            using (var roller = new Pen(Pal.C(96, 116, 148), 2f))
                for (int i = 0; i < 6; i++)
                {
                    int p = 5 + i * 5;
                    if (horiz) g.DrawLine(roller, p, 6, p, S - 6);
                    else g.DrawLine(roller, 6, p, S - 6, p);
                }
            // double chevron
            float cx = S / 2f, cy = S / 2f;
            int sgn = d is Dir.Right or Dir.Down ? 1 : -1;
            var dx = horiz ? sgn : 0; var dy = horiz ? 0 : sgn;
            using var br = new SolidBrush(Pal.CA(220, Pal.C(120, 220, 255)));
            foreach (float off in new[] { -3f, 3f })
            {
                var pts = new[]
                {
                    new PointF(cx + dx * (6 + off), cy + dy * (6 + off)),
                    new PointF(cx - dx * 4 - (horiz ? 0 : 4) + dx * off, cy - dy * 4 - (horiz ? 4 : 0) + dy * off),
                    new PointF(cx - dx * 4 + (horiz ? 0 : 4) + dx * off, cy - dy * 4 + (horiz ? 4 : 0) + dy * off),
                };
                g.FillPolygon(br, pts);
            }
        });
        Noise(b, 700 + (int)d, 4);
        return b;
    }

    /// <summary>Junction: a cross plate. The renderer adds live lane arrows.</summary>
    private static Bitmap BakeJunction()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.BeltCol);
            using (var rail = new SolidBrush(Pal.C(32, 36, 42)))
            {
                g.FillRectangle(rail, 0, 2, S, 3);
                g.FillRectangle(rail, 0, S - 5, S, 3);
                g.FillRectangle(rail, 2, 0, 3, S);
                g.FillRectangle(rail, S - 5, 0, 3, S);
            }
            using (var plate = new SolidBrush(Pal.C(60, 68, 80)))
                g.FillRectangle(plate, 6, 6, S - 12, S - 12);
            using (var plate2 = new SolidBrush(Pal.C(74, 84, 98)))
                g.FillRectangle(plate2, 9, 9, S - 18, S - 18);
            // faint crossing grooves
            using (var groove = new Pen(Pal.C(48, 56, 66), 2f))
            {
                g.DrawLine(groove, S / 2f, 8, S / 2f, S - 8);
                g.DrawLine(groove, 8, S / 2f, S - 8, S / 2f);
            }
        });
        Noise(b, 821, 4);
        return b;
    }

    /// <summary>Overflow router: splitter body with a bold forward chevron.</summary>
    private static Bitmap BakeRouter()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.BeltCol);
            using (var rail = new SolidBrush(Pal.C(32, 36, 42)))
            {
                g.FillRectangle(rail, 0, 2, S, 3);
                g.FillRectangle(rail, 0, S - 5, S, 3);
                g.FillRectangle(rail, 2, 0, 3, S);
                g.FillRectangle(rail, S - 5, 0, 3, S);
            }
            using (var hub = new SolidBrush(Pal.C(84, 94, 106)))
                g.FillEllipse(hub, 8, 8, 20, 20);
            using (var hubIn = new SolidBrush(Pal.C(56, 64, 74)))
                g.FillEllipse(hubIn, 12, 12, 12, 12);
            // bold straight-ahead chevron (priority)
            using var br = new SolidBrush(Pal.CA(230, Pal.Warn));
            g.FillPolygon(br, new[]
            {
                new PointF(24f, 18f), new PointF(16f, 13f), new PointF(16f, 23f),
            });
        });
        Noise(b, 822, 4);
        return b;
    }

    /// <summary>Power pole: mast, crossarm, insulators.</summary>
    private static Bitmap BakePole()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.CA(0, Pal.C(0, 0, 0)));      // transparent base
            using (var base_ = new SolidBrush(Pal.C(40, 42, 46)))
                g.FillEllipse(base_, 10, 27, 16, 7); // concrete foot
            using (var mast = new Pen(Pal.C(122, 94, 62), 3.4f))
                g.DrawLine(mast, S / 2f, 30, S / 2f, 6);
            using (var arm = new Pen(Pal.C(104, 80, 52), 2.6f))
                g.DrawLine(arm, 8, 10, S - 8, 10);
            using var ins = new SolidBrush(Pal.C(120, 200, 240));
            g.FillEllipse(ins, 7, 7, 4, 4);
            g.FillEllipse(ins, S - 11, 7, 4, 4);
            g.FillEllipse(ins, S / 2f - 2, 4, 4, 4);
        });
        return b;
    }

    // -------------------------------------------------- v0.4 additions ----

    private static Bitmap BakeWater()
    {
        var b = Make((g, bmp) =>
        {
            using (var deep = new SolidBrush(Pal.C(30, 66, 100)))
                g.FillRectangle(deep, 0, 0, S, S);
            using (var wave = new Pen(Pal.C(56, 108, 150), 1.4f))
            {
                g.DrawLine(wave, 4, 12, 16, 10);
                g.DrawLine(wave, 14, 22, 28, 20);
                g.DrawLine(wave, 20, 6, 30, 8);
            }
            using (var glint = new SolidBrush(Pal.CA(120, Pal.C(150, 210, 240))))
                g.FillRectangle(glint, 8, 16, 3, 1);
        });
        Noise(b, 901, 3);
        return b;
    }

    private static Bitmap BakeRail(Dir d, bool elevated)
    {
        bool horiz = d is Dir.Right or Dir.Left;
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.CA(0, Pal.C(0, 0, 0)));   // transparent
            Color wood = elevated ? Pal.C(96, 88, 80) : Pal.C(84, 70, 56);
            Color steel = elevated ? Pal.C(150, 142, 132) : Pal.C(128, 122, 116);
            // sleepers
            using (var sl = new SolidBrush(wood))
                for (int i = 0; i < 4; i++)
                {
                    if (horiz) g.FillRectangle(sl, 3 + i * 9, 8, 4, 20);
                    else g.FillRectangle(sl, 8, 3 + i * 9, 20, 4);
                }
            // rails
            using (var pen = new Pen(steel, 2.2f))
            {
                if (horiz) { g.DrawLine(pen, 0, 12, S, 12); g.DrawLine(pen, 0, 24, S, 24); }
                else { g.DrawLine(pen, 12, 0, 12, S); g.DrawLine(pen, 24, 0, 24, S); }
            }
            if (elevated) // support pylon
            using (var py = new SolidBrush(Pal.C(70, 64, 60)))
            {
                g.FillRectangle(py, S / 2 - 3, S / 2 - 3, 6, 6);
                g.FillRectangle(py, S / 2 - 5, S / 2 - 1, 10, 2);
            }
        });
        return b;
    }

    private static Bitmap BakeMerger()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.BeltCol);
            using (var rail = new SolidBrush(Pal.C(32, 36, 42)))
            {
                g.FillRectangle(rail, 0, 2, S, 3);
                g.FillRectangle(rail, 0, S - 5, S, 3);
                g.FillRectangle(rail, 2, 0, 3, S);
                g.FillRectangle(rail, S - 5, 0, 3, S);
            }
            using (var hub = new SolidBrush(Pal.C(96, 106, 118)))
                g.FillEllipse(hub, 7, 7, 22, 22);
            using (var hubIn = new SolidBrush(Pal.C(64, 72, 82)))
                g.FillEllipse(hubIn, 12, 12, 12, 12);
            // three intake chevrons pointing in
            using var br = new SolidBrush(Pal.CA(200, Pal.Accent));
            g.FillPolygon(br, new[] { new PointF(14f, 18f), new PointF(20f, 14f), new PointF(20f, 22f) });
        });
        Noise(b, 823, 4);
        return b;
    }

    private static Bitmap BakeFilterSplitter()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.BeltCol);
            using (var rail = new SolidBrush(Pal.C(32, 36, 42)))
            {
                g.FillRectangle(rail, 0, 2, S, 3);
                g.FillRectangle(rail, 0, S - 5, S, 3);
                g.FillRectangle(rail, 2, 0, 3, S);
                g.FillRectangle(rail, S - 5, 0, 3, S);
            }
            using (var hub = new SolidBrush(Pal.C(88, 100, 92)))
                g.FillEllipse(hub, 7, 7, 22, 22);
            using (var hubIn = new SolidBrush(Pal.C(58, 68, 62)))
                g.FillEllipse(hubIn, 11, 11, 14, 14);
            // funnel glyph
            using var fn = new Pen(Pal.Warn, 2f);
            g.DrawLine(fn, 12, 13, 18, 18);
            g.DrawLine(fn, 24, 13, 18, 18);
            g.DrawLine(fn, 18, 18, 18, 24);
        });
        Noise(b, 824, 4);
        return b;
    }

    private static Bitmap BakeInserter()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.CA(0, Pal.C(0, 0, 0)));
            using (var base_ = new SolidBrush(Pal.C(60, 66, 74)))
                g.FillEllipse(base_, 10, 22, 16, 10);
            using (var arm = new Pen(Pal.C(180, 150, 96), 3f))
            {
                g.DrawLine(arm, 18, 26, 10, 12);
                g.DrawLine(arm, 10, 12, 4, 15);
            }
            using (var claw = new SolidBrush(Pal.C(230, 200, 130)))
                g.FillEllipse(claw, 2, 13, 5, 5);
        });
        return b;
    }

    private static Bitmap BakeTrainStop()
    {
        var tone = Pal.C(90, 110, 140);
        var b = Make((g, bmp) =>
        {
            PanelBase(g, tone);
            // platform stripes
            using (var p = new Pen(Pal.C(58, 74, 96), 2f))
                for (int i = 0; i < 3; i++)
                    g.DrawLine(p, 5 + i * 10, 5, 5 + i * 10, S - 5);
            // signal mast
            using (var mast = new Pen(Pal.C(150, 160, 175), 2f))
                g.DrawLine(mast, S - 8, S - 6, S - 8, 6);
            using (var lamp = new SolidBrush(Pal.Good))
                g.FillEllipse(lamp, S - 11, 3, 6, 6);
        });
        Noise(b, 830, 5);
        return b;
    }

    private static Bitmap BakeTrainIcon() => BakeTrain();

    private static Bitmap BakeDronePort()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(150, 145, 100));
            // landing pad circle
            using (var ring = new Pen(Pal.C(220, 214, 150), 2f))
                g.DrawEllipse(ring, 8, 8, 20, 20);
            using (var cross = new Pen(Pal.C(200, 194, 140), 1.6f))
            {
                g.DrawLine(cross, 18, 10, 18, 26);
                g.DrawLine(cross, 10, 18, 26, 18);
            }
            // comm mast
            using (var mast = new Pen(Pal.C(120, 118, 88), 2f))
                g.DrawLine(mast, 30, 30, 33, 22);
        });
        Noise(b, 831, 4);
        return b;
    }

    private static Bitmap BakePipe()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.CA(0, Pal.C(0, 0, 0)));
            // flanged pipe segment, vertical+horizontal stubs; renderer adds links
            using (var pipe = new SolidBrush(Pal.C(104, 122, 128)))
            {
                g.FillRectangle(pipe, 8, 4, 8, S - 8);
                g.FillRectangle(pipe, 4, 14, S - 8, 8);
            }
            using (var hl = new SolidBrush(Pal.C(150, 170, 176)))
            {
                g.FillRectangle(hl, 10, 4, 2, S - 8);
                g.FillRectangle(hl, 4, 16, S - 8, 2);
            }
            using (var flange = new SolidBrush(Pal.C(80, 96, 102)))
            {
                g.FillRectangle(flange, 7, 6, 10, 3);
                g.FillRectangle(flange, 7, S - 9, 10, 3);
                g.FillRectangle(flange, 6, 13, 3, 10);
                g.FillRectangle(flange, S - 9, 13, 3, 10);
            }
        });
        return b;
    }

    private static Bitmap BakePump()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(70, 130, 150));
            // impeller housing
            using (var housing = new SolidBrush(Pal.C(52, 104, 124)))
                g.FillEllipse(housing, 8, 8, 20, 20);
            using (var blade = new Pen(Pal.C(150, 214, 230), 2.2f))
            {
                g.DrawLine(blade, 18, 11, 18, 25);
                g.DrawLine(blade, 11, 18, 25, 18);
                g.DrawLine(blade, 13, 13, 23, 23);
                g.DrawLine(blade, 23, 13, 13, 23);
            }
            using (var hubc = new SolidBrush(Pal.C(220, 240, 250)))
                g.FillEllipse(hubc, 16, 16, 4, 4);
        });
        Noise(b, 832, 4);
        return b;
    }

    private static Bitmap BakeTank()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.CA(0, Pal.C(0, 0, 0)));
            using (var cyl = new SolidBrush(Pal.C(110, 130, 135)))
                g.FillEllipse(cyl, 4, 8, 28, 24);
            using (var top = new SolidBrush(Pal.C(140, 162, 168)))
                g.FillEllipse(top, 4, 6, 28, 10);
            using (var band = new Pen(Pal.C(78, 94, 100), 2f))
                g.DrawEllipse(band, 4, 8, 28, 24);
            using (var gauge = new SolidBrush(Pal.Accent))
                g.FillRectangle(gauge, 16, 2, 4, 6);
        });
        return b;
    }

    private static Bitmap BakeBoiler()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(150, 100, 70));
            // firebox
            using (var box = new SolidBrush(Pal.C(60, 42, 34)))
                g.FillRectangle(box, 8, 12, 20, 16);
            using (var fire = new SolidBrush(Pal.C(240, 150, 60)))
                g.FillEllipse(fire, 12, 18, 6, 7);
            using (var fire2 = new SolidBrush(Pal.C(250, 210, 90)))
                g.FillEllipse(fire2, 20, 17, 5, 6);
            // chimney
            using (var chim = new SolidBrush(Pal.C(96, 66, 50)))
                g.FillRectangle(chim, 24, 4, 6, 9);
            using (var steam = new Pen(Pal.CA(160, Pal.Text), 1.4f))
                g.DrawLine(steam, 27, 2, 31, 5);
        });
        Noise(b, 833, 5);
        return b;
    }

    private static Bitmap BakeSteamEngine()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(130, 125, 110));
            // piston block
            using (var block = new SolidBrush(Pal.C(92, 88, 78)))
                g.FillRectangle(block, 6, 14, 14, 12);
            using (var rod = new Pen(Pal.C(190, 190, 200), 2.4f))
            {
                g.DrawLine(rod, 20, 20, 30, 20);
                g.DrawLine(rod, 24, 20, 24, 12);
            }
            using (var wheel = new Pen(Pal.C(210, 200, 180), 2f))
                g.DrawEllipse(wheel, 24, 10, 9, 9);
            using (var fly = new SolidBrush(Pal.C(240, 190, 80)))
                g.FillEllipse(fly, 27, 13, 3, 3);
        });
        Noise(b, 834, 5);
        return b;
    }

    private static Bitmap BakeFabTier(int tier)
    {
        var tone = tier == 3 ? Pal.C(126, 120, 175) : Pal.C(110, 108, 150);
        var b = Make((g, bmp) =>
        {
            PanelBase(g, tone);
            // gantry like the fabricator...
            using (var p = new Pen(Pal.C(50, 50, 74), 2.4f))
            {
                g.DrawLine(p, 6, 8, 18, 22);
                g.DrawLine(p, 30, 8, 18, 22);
            }
            // ...with tier pips
            using (var pip = new SolidBrush(Pal.C(240, 220, 120)))
                for (int i = 0; i < tier; i++)
                    g.FillEllipse(pip, 8 + i * 8, 27, 4, 4);
            // laser slot
            using (var slot = new SolidBrush(Pal.C(120, 220, 255)))
                g.FillRectangle(slot, 15, 5, 6, 3);
        });
        Noise(b, 840 + tier, 6);
        return b;
    }

    private static Bitmap BakeSpike()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.C(52, 50, 48));
            using (var spike = new SolidBrush(Pal.C(178, 178, 186)))
                foreach (var (x, y) in new[] { (8, 8), (18, 8), (28, 8), (8, 20), (18, 20), (28, 20) })
                {
                    g.FillPolygon(spike, new[] { new PointF(x - 4, y + 4), new PointF(x, y - 6), new PointF(x + 4, y + 4) });
                }
            using (var dark = new Pen(Pal.C(36, 34, 32), 1f))
                g.DrawRectangle(dark, 1.5f, 1.5f, S - 3, S - 3);
        });
        Noise(b, 835, 3);
        return b;
    }

    private static Bitmap BakeIED()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.CA(0, Pal.C(0, 0, 0)));
            using (var casing = new SolidBrush(Pal.C(96, 70, 62)))
                g.FillEllipse(casing, 8, 12, 20, 14);
            using (var stripe = new Pen(Pal.C(200, 90, 70), 2f))
                g.DrawLine(stripe, 12, 19, 24, 19);
            using (var blink = new SolidBrush(Pal.Bad))
                g.FillEllipse(blink, 17, 6, 4, 4);
        });
        return b;
    }

    private static Bitmap BakeShield()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(90, 140, 190));
            // emitter coil
            using (var coil = new Pen(Pal.C(150, 220, 255), 2f))
                for (int i = 0; i < 3; i++)
                    g.DrawEllipse(coil, 10 + i * 3, 10 + i * 3, 16 - i * 6, 16 - i * 6);
            using (var core = new SolidBrush(Pal.C(200, 240, 255)))
                g.FillEllipse(core, 16, 16, 4, 4);
        });
        Noise(b, 836, 4);
        return b;
    }

    private static Bitmap BakeBotFactory()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(110, 130, 120));
            // bay door
            using (var door = new SolidBrush(Pal.C(62, 78, 72)))
                g.FillRectangle(door, 8, 10, 20, 18);
            using (var warn = new Pen(Pal.C(214, 174, 60), 1.6f))
                for (int i = 0; i < 3; i++)
                    g.DrawLine(warn, 10 + i * 7, 28, 14 + i * 7, 22);
            using (var eye = new SolidBrush(Pal.BotCol))
                g.FillEllipse(eye, 16, 14, 4, 4);
        });
        Noise(b, 837, 5);
        return b;
    }

    private static Bitmap BakeGuardBot()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Pal.CA(0, Pal.C(0, 0, 0)));
            float cx = S / 2f, cy = S / 2f;
            // hovering chassis
            using (var body = new SolidBrush(Pal.C(78, 116, 102)))
                g.FillEllipse(body, cx - 8, cy - 4, 16, 12);
            using (var armor = new SolidBrush(Pal.C(120, 168, 150)))
                g.FillEllipse(armor, cx - 6, cy - 6, 12, 7);
            // gun barrel points UP
            using (var gun = new Pen(Pal.C(200, 210, 215), 2.2f))
                g.DrawLine(gun, cx, cy - 4, cx, cy - 14);
            // eye
            using (var eye = new SolidBrush(Pal.C(140, 255, 220)))
                g.FillEllipse(eye, cx - 2, cy - 1, 4, 3);
            // hover thrusters
            using (var jet = new Pen(Pal.CA(180, Pal.Accent), 1.6f))
            {
                g.DrawLine(jet, cx - 6, cy + 8, cx - 6, cy + 12);
                g.DrawLine(jet, cx + 6, cy + 8, cx + 6, cy + 12);
            }
        });
        return b;
    }

    private static Bitmap BakeTrain()
    {
        int w = S * 2, h = S;
        var b = new Bitmap(w, h);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Pal.CA(0, Pal.C(0, 0, 0)));
            // body (faces right)
            using (var body = new SolidBrush(Pal.C(120, 130, 148)))
                g.FillRectangle(body, 6, 10, w - 14, 16);
            using (var roof = new SolidBrush(Pal.C(150, 160, 178)))
                g.FillRectangle(roof, 8, 8, w - 18, 6);
            // hazard nose
            using (var nose = new SolidBrush(Pal.C(200, 160, 60)))
                g.FillRectangle(nose, w - 12, 12, 8, 12);
            // cabin window
            using (var win = new SolidBrush(Pal.C(140, 220, 255)))
                g.FillRectangle(win, w - 26, 12, 8, 6);
            // wheels
            using (var wheel = new SolidBrush(Pal.C(40, 44, 50)))
            {
                g.FillEllipse(wheel, 10, 24, 8, 8);
                g.FillEllipse(wheel, 26, 24, 8, 8);
                g.FillEllipse(wheel, w - 24, 24, 8, 8);
            }
            // cargo stripe
            using (var stripe = new Pen(Pal.C(80, 90, 106), 2f))
                g.DrawLine(stripe, 8, 20, w - 16, 20);
        }
        return b;
    }
}

