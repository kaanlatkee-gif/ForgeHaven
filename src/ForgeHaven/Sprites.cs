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
    public static Bitmap[] Trees = null!;      // forest variants (terrain pass)
    public static Bitmap CopperOre = null!, Water = null!;
    public static readonly Bitmap[] WaterFrames = new Bitmap[3];   // animated
    public static readonly Bitmap[] Belt = new Bitmap[4]; // by direction (frame 0 alias)
    public static readonly Bitmap[] FastBelt = new Bitmap[4];
    public static readonly Bitmap[][] BeltFrames = new Bitmap[4][];     // [dir][frame]
    public static readonly Bitmap[][] FastBeltFrames = new Bitmap[4][];
    public static readonly Bitmap[,] BeltMerge = new Bitmap[2, 4];      // [side, facing]
    public static readonly Bitmap[][][] BeltCurve = new Bitmap[2][][];      // [turn cw/ccw][inlet] -> frames
    public static readonly Bitmap[][][] FastBeltCurve = new Bitmap[2][][];
    public static readonly Bitmap[] Rail = new Bitmap[4];
    public static readonly Bitmap[] ElevatedRail = new Bitmap[4];

    // buildings
    private static readonly Dictionary<BuildKind, Bitmap> _buildings = new();
    public static Bitmap Hub = null!;
    public static readonly Bitmap[] WindRotor = new Bitmap[3];

    // creatures
    private static readonly Dictionary<(int shirt, int skin, int hair, int belt, Dir dir), Bitmap> _pawns = new();
    public static Bitmap RaiderImg = null!, RaiderApex = null!;
    public static Bitmap BeastImg = null!, CarcassImg = null!;   // MADDOG iter-4
    public static Bitmap TraderImg = null!;                       // MADDOG iter-9
    public static Bitmap ArkImg = null!;                          // RECLAMATION

    /// <summary>Phase 1 art pass: per-item sprite, shown on belts and in the
    /// resource panel. Overridable via items/&lt;name&gt;.png.</summary>
    public static readonly Bitmap[] ItemIcons = new Bitmap[Bal.ItemCount];

    /// <summary>Growth-stage overlay for the crop plot (scaled by progress).</summary>
    public static Bitmap CropImg = null!;

    /// <summary>Scaled item icons, cached per (item, size). Belt cargo draws
    /// hundreds per frame — pre-scaled blits instead of per-item scaling.</summary>
    private static readonly Dictionary<(int k, int s), Bitmap> _iconScaled = new();
    public static Bitmap ItemIconScaled(ItemKind k, int sizePx)
    {
        var key = ((int)k, sizePx);
        if (_iconScaled.TryGetValue(key, out var b)) return b;
        var src = ItemIcons[(int)k];
        var bmp = new Bitmap(src, new Size(sizePx, sizePx));
        _iconScaled[key] = bmp;
        return bmp;
    }

    public static string ItemAssetName(ItemKind k) => k switch
    {
        ItemKind.IronOre => "iron_ore",
        ItemKind.Crystal => "crystal",
        ItemKind.Biomass => "biomass",
        ItemKind.IronPlate => "iron_plate",
        ItemKind.Ammo => "ammo",
        ItemKind.Food => "food",
        ItemKind.AdvPart => "adv_part",
        ItemKind.CopperOre => "copper_ore",
        ItemKind.CopperPlate => "copper_plate",
        ItemKind.SciencePack => "science_pack",
        ItemKind.Drone => "drone",
        ItemKind.WarBot => "warbot",
        ItemKind.Stone => "stone",
        ItemKind.Slag => "slag",
        ItemKind.Gear => "gear",
        ItemKind.Circuit => "circuit",
        _ => "unknown",
    };
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

    public static Bitmap Pawn(int shirt, int skin, int hair, int belt, Dir dir)
    {
        EnsureInit();
            var key = (shirt, skin, hair, belt, dir);
        if (!_pawns.TryGetValue(key, out var b))
        {
            b = ComposePawn(shirt, skin, hair, belt, dir);
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
        Trees = new Bitmap[5];
        for (int i = 0; i < 5; i++) Trees[i] = BakeTree(i);
        CopperOre = BakeCopper();
        for (int i = 0; i < 3; i++) WaterFrames[i] = BakeWaterFrame(i);
        Water = WaterFrames[0];   // static alias (menus, previews)
        for (int i = 0; i < 4; i++)
        {
            BeltFrames[i] = new Bitmap[4];
            for (int f = 0; f < 4; f++) BeltFrames[i][f] = BakeBelt((Dir)i, f);
            Belt[i] = BeltFrames[i][0];
            FastBeltFrames[i] = new Bitmap[5];
            for (int f = 0; f < 5; f++) FastBeltFrames[i][f] = BakeFastBelt((Dir)i, f);
            FastBelt[i] = FastBeltFrames[i][0];
        }
        BakeBeltMerges();
        BeltCurve[0] = new Bitmap[4][]; BeltCurve[1] = new Bitmap[4][];
        FastBeltCurve[0] = new Bitmap[4][]; FastBeltCurve[1] = new Bitmap[4][];
        FillCurveSets(BeltCurve, false);
        FillCurveSets(FastBeltCurve, true);
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
        _buildings[BuildKind.Door] = BakeDoor();
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
        _buildings[BuildKind.ArkWreck] = BakeArkWreck();
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
        _buildings[BuildKind.PrimitiveFurnace] = BakePrimitiveFurnace();
        _buildings[BuildKind.CropPlot] = BakeCropPlot();
        CropImg = BakeCropGrowth();
        GuardBotImg = BakeGuardBot();
        TrainImg = BakeTrain();
        for (int i = 0; i < 3; i++) WindRotor[i] = BakeWindRotor(i * 2.09f);
        Hub = BakeHub();

        RaiderImg = BakeRaider(false);
        RaiderApex = BakeRaider(true);
        BeastImg = BakeBeast();          // MADDOG iter-4: wildlife
        CarcassImg = BakeCarcass();
        TraderImg = BakeTrader();        // MADDOG iter-9: caravan
        ArkImg = BakeArk();              // RECLAMATION: the wreck

        BakeItemIcons();

        // last step: any PNGs in <exe>/assets/ override what we just baked
        ApplyExternalAssets();
    }

    // --------------------------------------------- external texture layer --
    //  Optional art overrides: PNGs in <exe>/assets/ replace the code-baked
    //  sprites 1:1. Missing files fall back to the baked versions, so the
    //  game is fully playable with an absent/empty folder (tests, CI, clean
    //  checkouts, shipping the exe alone). See assets/README.txt for names.

    /// <summary>How many textures were overridden from the assets folder.</summary>
    public static int AssetsLoaded { get; private set; }

    private static string AssetRoot => Path.Combine(AppContext.BaseDirectory, "assets");

    private static Bitmap? TryAsset(string relPath)
    {
        try
        {
            var p = Path.Combine(AssetRoot, relPath);
            if (!File.Exists(p)) return null;
            using var src = new Bitmap(p);

            // normalize NEAR-32 square-ish art to exactly 32x32 (odd sizes
            // like 36x36 would shimmer drawn nearest-neighbor). Larger art
            // (hi-res pawn parts, 64px multiblock sprites, frame strips)
            // stays native — draw sites scale it with the right filtering.
            int mx = Math.Max(src.Width, src.Height), mn = Math.Min(src.Width, src.Height);
            bool nearS = mx <= S * 3 / 2 && mx - mn <= mx / 3;
            Bitmap result;
            if (nearS && (src.Width != S || src.Height != S))
            {
                result = new Bitmap(S, S);
                using (var rg = Graphics.FromImage(result))
                {
                    rg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    rg.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    rg.DrawImage(src, 0, 0, S, S);
                }
            }
            else result = new Bitmap(src);      // detach from the file handle

            AssetsLoaded++;
            return result;
        }
        catch { return null; }
    }

    /// <summary>Directional sets (belts, rails): belt_right.png... or a single
    /// belt.png (assumed to point RIGHT) rotated into the other facings.</summary>
    private static void ApplyDirSet(Bitmap[] set, string name)
    {
        string[] dn = { "right", "down", "left", "up" };
        var plain = TryAsset($"buildings/{name}.png");
        for (int d = 0; d < 4; d++)
        {
            Bitmap? ext = TryAsset($"buildings/{name}_{dn[d]}.png");
            if (ext == null && plain != null)
            {
                ext = new Bitmap(plain);
                ext.RotateFlip((RotateFlipType)d);   // 0 none, 1 = 90cw, 2 = 180, 3 = 270
            }
            if (ext != null) set[d] = ext;
        }
    }

    /// <summary>Loads buildings/&lt;name&gt;.png as a horizontal FRAME STRIP
    /// (frame size = image height; a lone square loads as 1 frame = static).
    /// Returns null when no file exists.</summary>
    private static Bitmap[]? TryStrip(string name)
    {
        var img = TryAsset($"buildings/{name}.png");
        if (img == null) return null;
        int h = img.Height;
        if (img.Width == h) return new[] { img };
        int n = img.Width / h;
        var frames = new Bitmap[n];
        for (int i = 0; i < n; i++)
            frames[i] = img.Clone(new Rectangle(i * h, 0, h, h), img.PixelFormat);
        img.Dispose();
        return frames;
    }

    private static Bitmap[] RotateFrames(Bitmap[] fs, int quarterTurns)
    {
        if (quarterTurns == 0) return fs;
        var r = new Bitmap[fs.Length];
        for (int i = 0; i < fs.Length; i++)
        {
            r[i] = (Bitmap)fs[i].Clone();
            for (int k = 0; k < quarterTurns; k++)
                r[i].RotateFlip(System.Drawing.RotateFlipType.Rotate90FlipNone);
        }
        return r;
    }

    private static Bitmap[] MirrorFrames(Bitmap[] fs)
    {
        var r = new Bitmap[fs.Length];
        for (int i = 0; i < fs.Length; i++)
        {
            r[i] = (Bitmap)fs[i].Clone();
            r[i].RotateFlip(System.Drawing.RotateFlipType.RotateNoneFlipX);
        }
        return r;
    }

    /// <summary>Horizontal strip from frames (export helper).</summary>
    private static Bitmap Strip(Bitmap[] frames)
    {
        int s = frames[0].Width;
        var b = new Bitmap(s * frames.Length, s);
        using (var g = Graphics.FromImage(b))
            for (int i = 0; i < frames.Length; i++) g.DrawImage(frames[i], i * s, 0);
        return b;
    }

    private static void ApplyExternalAssets()
    {
        AssetsLoaded = 0;

        // terrain
        for (int i = 0; i < 3; i++) Ground[i] = TryAsset($"terrain/ground{i}.png") ?? Ground[i];
        for (int i = 0; i < 3; i++) Decor[i] = TryAsset($"terrain/decor{i}.png") ?? Decor[i];
        Rock = TryAsset("terrain/rock.png") ?? Rock;
        IronOre = TryAsset("terrain/iron.png") ?? IronOre;
        CopperOre = TryAsset("terrain/copper.png") ?? CopperOre;
        Crystal = TryAsset("terrain/crystal.png") ?? Crystal;
        Flora = TryAsset("terrain/flora.png") ?? Flora;
        for (int i = 0; i < 5; i++) Trees[i] = TryAsset($"terrain/tree{i}.png") ?? Trees[i];
        for (int i = 0; i < 3; i++) WaterFrames[i] = TryAsset($"terrain/water{i}.png") ?? WaterFrames[i];
        Water = WaterFrames[0];

        // animated overlays
        for (int i = 0; i < 3; i++) WindRotor[i] = TryAsset($"anim/windrotor{i}.png") ?? WindRotor[i];

        // buildings — lowercase enum name, e.g. smelter.png, filtersplitter.png
        foreach (var k in Enum.GetValues<BuildKind>())
        {
            var ext = TryAsset($"buildings/{k.ToString().ToLowerInvariant()}.png");
            if (ext != null && _buildings.ContainsKey(k)) _buildings[k] = ext;
        }
        Hub = TryAsset("buildings/hub.png") ?? Hub;   // separate field, kept in sync

        // rails (directional, single sprites)
        ApplyDirSet(Rail, "rail");
        ApplyDirSet(ElevatedRail, "elevatedrail");

        // BELTS: frame strips. belt.png (facing RIGHT) is rotated into the
        // other facings; belt_<dir>.png overrides one facing. Frame size is
        // the image height; a lone square = static belt.
        string[] dn = { "right", "down", "left", "up" };
        var beltStrip = TryStrip("belt");
        if (beltStrip != null)
            for (int d = 0; d < 4; d++) BeltFrames[d] = RotateFrames(beltStrip, d);
        for (int d = 0; d < 4; d++)
        {
            var perDir = TryStrip($"belt_{dn[d]}");
            if (perDir != null) BeltFrames[d] = perDir;
        }
        for (int d = 0; d < 4; d++) Belt[d] = BeltFrames[d][0];

        var fastStrip = TryStrip("fastbelt");
        if (fastStrip != null)
            for (int d = 0; d < 4; d++) FastBeltFrames[d] = RotateFrames(fastStrip, d);
        for (int d = 0; d < 4; d++)
        {
            var perDir = TryStrip($"fastbelt_{dn[d]}");
            if (perDir != null) FastBeltFrames[d] = perDir;
        }
        for (int d = 0; d < 4; d++) FastBelt[d] = FastBeltFrames[d][0];

        // CURVES: belt_curve.png is the canonical turn (input NORTH, output
        // EAST); the game rotates it for the other three inlets and mirrors
        // it for counter-clockwise turns.
        var curveStrip = TryStrip("belt_curve");
        if (curveStrip != null)
        {
            var mirrored = MirrorFrames(curveStrip);
            for (int t = 0; t < 2; t++)
                for (int inDir = 0; inDir < 4; inDir++)
                    BeltCurve[t][inDir] = RotateFrames(t == 0 ? curveStrip : mirrored, (inDir - 3 + 4) % 4);
        }
        var fastCurveStrip = TryStrip("fastbelt_curve");
        if (fastCurveStrip != null)
        {
            var mirrored = MirrorFrames(fastCurveStrip);
            for (int t = 0; t < 2; t++)
                for (int inDir = 0; inDir < 4; inDir++)
                    FastBeltCurve[t][inDir] = RotateFrames(t == 0 ? fastCurveStrip : mirrored, (inDir - 3 + 4) % 4);
        }

        // units
        RaiderImg = TryAsset("units/raider.png") ?? RaiderImg;
        BeastImg = TryAsset("units/beast.png") ?? BeastImg;
        CarcassImg = TryAsset("units/carcass.png") ?? CarcassImg;
        TraderImg = TryAsset("units/trader.png") ?? TraderImg;
        ArkImg = TryAsset("units/ark.png") ?? ArkImg;
        RaiderApex = TryAsset("units/raider_apex.png") ?? RaiderApex;
        GuardBotImg = TryAsset("units/guardbot.png") ?? GuardBotImg;
        TrainImg = TryAsset("units/train.png") ?? TrainImg;

        // items (belt cargo + resource panel)
        for (int i = 0; i < ItemIcons.Length; i++)
        {
            var k = (ItemKind)i;
            ItemIcons[i] = TryAsset($"items/{ItemAssetName(k)}.png") ?? ItemIcons[i];
        }
    }

    /// <summary>Writes every current sprite into assets/ as PNG — a
    /// paint-ready template set. Never overwrites existing files, so your
    /// art is safe. Restarts the game to load your edits. Templates are
    /// written to the exe's assets/ folder AND the source tree's
    /// src/ForgeHaven/assets/ (when found), so painted art survives
    /// Clean / bin deletes and Debug-vs-Release switches.</summary>
    public static (int written, int skipped) ExportTemplates()
    {
        EnsureInit();
        int written = 0, skipped = 0;

        // find the source-tree assets folder by walking up from the exe
        string? srcAssets = null;
        var exeDir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && exeDir != null; i++, exeDir = exeDir.Parent!)
        {
            var probe = Path.Combine(exeDir.FullName, "src", "ForgeHaven", "assets");
            if (Directory.Exists(probe)) { srcAssets = probe; break; }
        }

        void Save(Bitmap b, string rel)
        {
            try
            {
                var p = Path.Combine(AssetRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                if (File.Exists(p)) skipped++;
                else { b.Save(p, System.Drawing.Imaging.ImageFormat.Png); written++; }

                if (srcAssets != null)
                {
                    var p2 = Path.Combine(srcAssets, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(p2)!);
                    if (!File.Exists(p2)) b.Save(p2, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { skipped++; }
        }

        for (int i = 0; i < 3; i++) Save(Ground[i], $"terrain/ground{i}.png");
        for (int i = 0; i < 3; i++) Save(Decor[i], $"terrain/decor{i}.png");
        Save(Rock, "terrain/rock.png");
        Save(IronOre, "terrain/iron.png");
        Save(CopperOre, "terrain/copper.png");
        Save(Crystal, "terrain/crystal.png");
        Save(Flora, "terrain/flora.png");
        for (int i = 0; i < 5; i++) Save(Trees[i], $"terrain/tree{i}.png");
        for (int i = 0; i < 3; i++) Save(WaterFrames[i], $"terrain/water{i}.png");
        for (int i = 0; i < 3; i++) Save(WindRotor[i], $"anim/windrotor{i}.png");
        foreach (var k in Enum.GetValues<BuildKind>())
            if (_buildings.TryGetValue(k, out var bmp))
                Save(bmp, $"buildings/{k.ToString().ToLowerInvariant()}.png");
        string[] dn = { "right", "down", "left", "up" };
        for (int d = 0; d < 4; d++)
        {
            Save(Rail[d], $"buildings/rail_{dn[d]}.png");
            Save(ElevatedRail[d], $"buildings/elevatedrail_{dn[d]}.png");
        }
        // BELT STRIPS: paint belt.png as a horizontal frame strip (frame
        // size = height). belt_curve.png is the CANONIAL curve (input north,
        // output east) — the game rotates + mirrors it into all 8 turns.
        Save(Strip(BeltFrames[0]), "buildings/belt.png");
        Save(Strip(FastBeltFrames[0]), "buildings/fastbelt.png");
        Save(Strip(BeltCurve[0][3]), "buildings/belt_curve.png");
        Save(Strip(FastBeltCurve[0][3]), "buildings/fastbelt_curve.png");
        for (int i = 0; i < ItemIcons.Length; i++)
            Save(ItemIcons[i], $"items/{ItemAssetName((ItemKind)i)}.png");
        Save(RaiderImg, "units/raider.png");
        Save(RaiderApex, "units/raider_apex.png");
        Save(GuardBotImg, "units/guardbot.png");
        Save(TrainImg, "units/train.png");
        // PAWN GRAY TEMPLATES (Minecraft-style): ONE file per part, painted
        // in grayscale. White takes the full tone color, darker grays become
        // shaded versions, alpha is kept. The game tints body <- shirt
        // palette, head <- skin palette, hair <- hair palette, per pawn.
        Save(BakePawnShadow(), "units/pawns/shadow.png");
        Save(BakePawnBody(), "units/pawns/body.png");
        Save(BakePawnBelt(), "units/pawns/belt.png");   // waist-pack, belt palette
        // whole-pawn templates: copy to pawn_<dir>.png to switch that
        // direction to the one-file workflow (magic keys = tint regions)
        foreach (var d in Enum.GetValues<Dir>())
            Save(BakePawnWholeTemplate(d), $"units/pawns/pawn_TEMPLATE_{d.ToString().ToLowerInvariant()}.png");
        foreach (var d in Enum.GetValues<Dir>())
        {
            string ds = d.ToString().ToLowerInvariant();
            Save(BakePawnHead(), $"units/pawns/head_{ds}.png");
            Save(BakePawnHair(d), $"units/pawns/hair_{ds}.png");
            Save(BakePawnFace(d), $"units/pawns/face_{ds}.png");
        }

        try
        {
            var doc = Path.Combine(AssetRoot, "README.txt");
            if (!File.Exists(doc)) File.WriteAllText(doc, AssetsDoc);
        }
        catch { }
        return (written, skipped);
    }

    private const string AssetsDoc =
        "FORGEHAVEN - custom textures\n" +
        "============================\n" +
        "Any PNG in this folder overrides the matching built-in sprite. Missing\n" +
        "files simply use the baked versions - the folder is fully optional.\n" +
        "Restart the game after editing. Use the in-game DEV menu\n" +
        "'EXPORT SPRITE TEMPLATES' to dump paint-ready templates (it never\n" +
        "overwrites your files).\n\n" +
        "Sizes: 32x32 px recommended. Any size works (the game scales), but\n" +
        "32x32 is pixel-perfect. train.png may be 64x32. Transparency is\n" +
        "supported and encouraged - let the ground show through.\n\n" +
        "Names:\n" +
        "  terrain/  ground0..2, decor0..2, rock, iron, copper, crystal, flora,\n" +
        "            water0..2 (animated)\n" +
        "  anim/     windrotor0..2\n" +
        "  buildings/<kind>.png - lowercase enum name, e.g.\n" +
        "            belt, fastbelt, splitter, junction, overflowrouter, merger,\n" +
        "            filtersplitter, inserter, rail, elevatedrail, trainstop,\n" +
        "            locomotive, droneport, drill, deepdrill, smelter,\n" +
        "            fabricator, fabt2, fabt3, bioprocessor, storagecrate,\n" +
        "            powerpole, reactor, solarpanel, windturbine, battery, pipe,\n" +
        "            pump, tank, boiler, steamengine, wall, turret, heavyturret,\n" +
        "            watchtower, spiketrap, ied, shieldgen, botfactory, hab,\n" +
        "            messtable, lamp, garden, medbed, lab, hub,\n" +
        "            cropplot (crop growth overlay: items/crop.png)\n" +
        "  Belts/rails are directional: belt_right/down/left/up.png - or supply\n" +
        "  just belt.png pointing RIGHT and the other facings are rotated.\n" +
        "  units/    raider, raider_apex, guardbot, train\n" +
        "  items/    iron_ore, copper_ore, crystal, biomass, stone,\n" +
        "            iron_plate, copper_plate, ammo, food, adv_part,\n" +
        "            science_pack, drone, warbot   (belt cargo + stock panel)\n" +
        "  units/pawns/ shadow.png, body_s0..5.png, head_sk0..3_<direction>.png,\n" +
        "              hair_h0..5_<direction>.png, face_<direction>.png\n\n" +
        "Directional convention: RIGHT = output/facing side. Light comes from\n" +
        "the top-left (NW) in the baked art.\n";

    private static Bitmap Blank()
    {
        var b = new Bitmap(S, S);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.Transparent);
        return b;
    }

    private static Bitmap Make(Action<Graphics, Bitmap> draw) => Make(1, draw);

    /// <summary>Hi-res variant: runs the same S-space drawing through a
    /// scale transform, producing a (S*scale)^2 bitmap. Used for pawns so
    /// both procedural parts and hi-res asset art survive zooming.</summary>
    private static Bitmap Make(int scale, Action<Graphics, Bitmap> draw)
    {
        var b = new Bitmap(S * scale, S * scale);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(scale, scale);
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
            // large soft patches — organic variation instead of uniform flat fill
            for (int i = 0; i < 6; i++)
            {
                int w = r.Next(10, 24), h = r.Next(8, 18);
                int x = r.Next(-4, S - 6), y = r.Next(-4, S - 6);
                var tone = i % 2 == 0
                    ? Pal.C(Math.Min(255, bases[variant].R + 6), Math.Min(255, bases[variant].G + 6), Math.Min(255, bases[variant].B + 5))
                    : Pal.C(Math.Max(0, bases[variant].R - 6), Math.Max(0, bases[variant].G - 6), Math.Max(0, bases[variant].B - 5));
                using var br = new SolidBrush(Pal.CA(45, tone));
                g.FillEllipse(br, x, y, w, h);
            }
            Specks(g, r, Pal.CA(255, Pal.C(52, 57, 62)), 20, 1);
            Specks(g, r, Pal.CA(255, Pal.C(36, 40, 44)), 16, 2);
            Specks(g, r, Pal.CA(255, Pal.C(58, 62, 66)), 8, 1);
            Specks(g, r, Pal.CA(120, Pal.C(84, 90, 96)), 4, 1);   // sparse bright grit
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
            g.Clear(Color.Transparent);   // ground shows between rocks
            // cast shadow (light from NW)
            using (var sh = new SolidBrush(Pal.CA(90, Pal.C(8, 10, 12))))
                g.FillEllipse(sh, 6, 12, 24, 16);
            // 3-tone boulders: top light, body, bottom shade + outline
            for (int i = 0; i < 6; i++)
            {
                int w = r.Next(10, 20), h = r.Next(8, 16);
                int x = r.Next(2, S - w - 2), y = r.Next(1, S - h - 3);
                int t = r.Next(3);
                var body = Pal.C(62 + t * 8, 66 + t * 8, 74 + t * 7);
                using (var br = new SolidBrush(body))
                    g.FillEllipse(br, x, y, w, h);
                using (var hi = new SolidBrush(Pal.Lighten(body, 0.28f)))
                    g.FillEllipse(hi, x + 1, y + 1, w * 0.55f, h * 0.45f);
                using (var lo = new SolidBrush(Pal.Darken(body, 0.35f)))
                    g.FillEllipse(lo, x + 2, y + (int)(h * 0.6f), w * 0.7f, h * 0.35f);
                using (var pen = new Pen(Pal.C(30, 32, 38), 1f))
                    g.DrawEllipse(pen, x, y, w, h);
            }
        });
        Noise(b, 302, 6);
        return b;
    }

    private static Bitmap BakeIron()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            // ore bed: darker dirt patch the nuggets sit in
            using (var bed = new SolidBrush(Pal.CA(150, Pal.C(30, 24, 18))))
                g.FillEllipse(bed, 2, 3, S - 4, S - 7);
            using (var sh = new SolidBrush(Pal.CA(80, Pal.C(8, 10, 12))))
                g.FillEllipse(sh, 5, 8, 22, 18);
            var r = new Random(401);
            for (int i = 0; i < 6; i++)
            {
                int size = r.Next(7, 13);
                int x = 3 + (i % 3) * 10 + r.Next(-2, 3);
                int y = 3 + (i / 3) * 13 + r.Next(-2, 3);
                using (var br = new SolidBrush(Pal.C(118, 70, 38)))
                    g.FillEllipse(br, x, y, size, size);
                using (var hi = new SolidBrush(Pal.C(196, 128, 66)))
                    g.FillEllipse(hi, x + 1, y + 1, size / 2, size / 2);
                using (var sp = new SolidBrush(Pal.C(240, 178, 108)))
                    g.FillRectangle(sp, x + size / 3, y + size / 3, 2, 2);
                using (var pen = new Pen(Pal.C(60, 34, 18), 1f))
                    g.DrawEllipse(pen, x, y, size, size);
            }
        });
        Noise(b, 402, 8);
        return b;
    }

    private static Bitmap BakeCrystal()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            // layered glow bed
            using (var glow = new SolidBrush(Pal.CA(36, Pal.Crystal)))
                g.FillEllipse(glow, 1, 3, S - 2, S - 6);
            using (var glow2 = new SolidBrush(Pal.CA(70, Pal.Crystal)))
                g.FillEllipse(glow2, 6, 8, S - 12, S - 14);
            // ground shadow
            using (var sh = new SolidBrush(Pal.CA(90, Pal.C(10, 6, 18))))
                g.FillEllipse(sh, 7, 23, 18, 6);
            var r = new Random(501);
            for (int i = 0; i < 4; i++)
            {
                float cx = 17 + (i % 2) * 8 - 6 + r.Next(-2, 3);
                float cy = 17 + (i / 2) * 7 - 5 + r.Next(-2, 3);
                float w = 6 + r.Next(3), h = 13 + r.Next(6);
                var pts = new[]
                {
                    new PointF(cx, cy - h / 2),
                    new PointF(cx + w / 2, cy - h / 6),
                    new PointF(cx + w / 3, cy + h / 2),
                    new PointF(cx - w / 3, cy + h / 2),
                    new PointF(cx - w / 2, cy - h / 6),
                };
                using (var br = new SolidBrush(Pal.C(118, 60, 205)))
                    g.FillPolygon(br, pts);
                using (var edge = new Pen(Pal.C(214, 176, 255), 1.2f))
                    g.DrawPolygon(edge, pts);
                // bright core
                using (var core = new SolidBrush(Pal.CA(180, Pal.C(240, 220, 255))))
                    g.FillEllipse(core, cx - 1, cy - h / 4, 2, 3);
            }
        });
        return b;
    }

    /// <summary>A forest tile = one tree, drawn as CHUNKY PIXEL ART to match
    /// the rest of the tileset: a 12x24 logical grid of 3px "pixels",
    /// rectangles only, no anti-aliasing, hard palette edges. 1 tile wide,
    /// 2 tall (drawn one tile above the anchor). Variants: 0 cold pine,
    /// 1 warm pine, 2 broadleaf, 3 birch, 4 burnt snag (volcanic).</summary>
    private static Bitmap BakeTree(int v)
    {
        var b = new Bitmap(S, S * 2);              // 12x24 logical pixels of 3px
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Color.Transparent);
            void P(int px, int py, int pw, int ph, Color c)
                => g.FillRectangle(new SolidBrush(c), px * 3, py * 3, pw * 3, ph * 3);

            // blocky ground shadow
            P(3, 23, 6, 1, Color.FromArgb(70, 6, 10, 8));

            if (v == 4)
            {
                // burnt snag: charred trunk + bare blocky branches + ember
                P(5, 12, 2, 11, Pal.C(48, 40, 38));
                P(5, 12, 1, 11, Pal.C(64, 54, 50));
                P(2, 9, 3, 1, Pal.C(52, 44, 42));
                P(7, 7, 3, 1, Pal.C(52, 44, 42));
                P(3, 6, 2, 1, Pal.C(58, 50, 46));
                P(6, 4, 2, 1, Pal.C(58, 50, 46));
                P(1, 8, 1, 2, Pal.C(52, 44, 42));   // drooping branch tips
                P(10, 7, 1, 2, Pal.C(52, 44, 42));
                P(5, 17, 1, 1, Pal.C(255, 130, 55));  // ember in the crack
                P(5, 18, 1, 1, Pal.C(255, 90, 40));
                return b;
            }

            // palettes (dark base / mid / sun-lit / outline)
            Color dark, mid, hi, edge, trunk;
            if (v == 0) { dark = Pal.C(20, 58, 40); mid = Pal.C(30, 86, 54); hi = Pal.C(48, 118, 74); edge = Pal.C(14, 42, 30); trunk = Pal.C(66, 48, 34); }
            else if (v == 1) { dark = Pal.C(26, 64, 40); mid = Pal.C(40, 94, 56); hi = Pal.C(60, 126, 80); edge = Pal.C(18, 48, 32); trunk = Pal.C(74, 54, 38); }
            else if (v == 2) { dark = Pal.C(24, 70, 38); mid = Pal.C(38, 104, 50); hi = Pal.C(60, 134, 70); edge = Pal.C(16, 52, 30); trunk = Pal.C(80, 58, 40); }
            else { dark = Pal.C(74, 108, 66); mid = Pal.C(102, 138, 84); hi = Pal.C(136, 168, 106); edge = Pal.C(54, 82, 50); trunk = Pal.C(214, 208, 192); }   // birch

            if (v == 0 || v == 1)
            {
                // pine: stacked blocky tiers, widest at the lower middle
                int baseY = 2;
                int[] tw = { 2, 4, 4, 6, 6, 8, 8, 10, 10, 8 };
                for (int i = 0; i < tw.Length; i++)
                {
                    int y = baseY + i * 2;
                    int w = tw[i], x0 = 6 - w / 2;
                    P(x0, y, w, 2, i % 3 == 2 ? mid : dark);          // tier body
                    P(x0, y, 1, 2, hi);                               // sun-lit left edge
                    P(x0 + w - 1, y, 1, 2, edge);                     // dark right edge
                    P(x0 + 1, y, w - 2, 1, i % 2 == 0 ? Pal.CA(60, hi) : mid);  // dithered top
                }
                // trunk visible below the lowest tier
                P(5, 22, 2, 2, trunk);
                P(5, 22, 1, 2, Pal.CA(120, trunk));
            }
            else if (v == 2)
            {
                // broadleaf: chunky blob canopy, two-tone + dither holes
                P(3, 3, 6, 2, dark);
                P(2, 5, 8, 4, dark);
                P(1, 7, 10, 5, dark);
                P(2, 12, 8, 3, dark);
                P(3, 15, 6, 2, dark);
                P(2, 5, 8, 2, mid);                                   // mid band
                P(1, 7, 10, 2, mid);
                P(3, 3, 4, 1, hi);                                    // sun crown
                P(2, 5, 3, 1, hi);
                P(3, 6, 2, 1, hi);
                P(9, 8, 1, 3, edge);                                  // shaded rim
                P(8, 12, 1, 2, edge);
                P(5, 8, 1, 1, dark);                                  // dither holes
                P(7, 10, 1, 1, dark);
                P(4, 12, 1, 1, edge);
                P(5, 16, 2, 7, trunk);                                // trunk
                P(4, 18, 1, 2, trunk);                                // root flare
                P(7, 18, 1, 2, trunk);
                P(5, 16, 1, 7, Pal.CA(110, trunk));
            }
            else
            {
                // birch: pale marked trunk + small airy canopy
                P(5, 12, 2, 11, trunk);
                P(5, 12, 1, 11, Pal.C(238, 234, 222));
                P(5, 15, 1, 1, Pal.C(70, 64, 58));                    // bark marks
                P(6, 18, 1, 1, Pal.C(70, 64, 58));
                P(5, 21, 1, 1, Pal.C(70, 64, 58));
                P(3, 4, 6, 2, dark);
                P(2, 6, 8, 3, dark);
                P(3, 9, 6, 2, dark);
                P(2, 6, 4, 1, hi);
                P(3, 4, 2, 1, hi);
                P(9, 6, 1, 3, edge);
                P(4, 7, 1, 1, mid);                                   // dither
                P(6, 9, 1, 1, mid);
            }
        }
        Noise(b, 7700 + v * 11, 5);
        return b;
    }

    private static Bitmap BakeFlora()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            // ground contact shadow
            using (var sh = new SolidBrush(Pal.CA(70, Pal.C(6, 12, 10))))
                g.FillEllipse(sh, 5, S - 8, S - 10, 5);
            // soft spore field
            using (var bed = new SolidBrush(Pal.CA(28, Pal.Flora)))
                g.FillEllipse(bed, 2, 4, S - 4, S - 8);
            var r = new Random(601);
            for (int i = 0; i < 5; i++)
            {
                float x = 6 + r.Next(S - 12), y = 9 + r.Next(10);
                float h = 6 + r.Next(7);
                // stem
                using (var stem = new Pen(Pal.C(28, 118, 96), 1.8f))
                    g.DrawLine(stem, x, y + h, x + (r.Next(3) - 1), y);
                // glow halo behind the cap
                using (var halo = new SolidBrush(Pal.CA(46, Pal.Flora)))
                    g.FillEllipse(halo, x - 5.5f, y - 5.5f, 11, 11);
                // cap
                using (var cap = new SolidBrush(Pal.C(64, 226, 184)))
                    g.FillEllipse(cap, x - 3.2f, y - 3.2f, 6.4f, 5f);
                using (var hi = new SolidBrush(Pal.C(150, 255, 226)))
                    g.FillEllipse(hi, x - 1.6f, y - 2.6f, 2.6f, 1.8f);
            }
        });
        return b;
    }

    private static Bitmap BakeBelt(Dir d, int frame = 0)
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
            // scrolling rollers: 8px period, 2px/frame, 4-frame loop
            using (var roller = new Pen(Pal.C(70, 78, 88), 2f))
                for (int i = -1; i < 5; i++)
                {
                    int p = 6 + i * 8 + frame * 2;
                    if (p < 6 || p > S - 6) continue;
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
        // 2x2 multiblock: baked at 2S x 2S so it never gets stretched
        var tone = deep ? Pal.C(105, 82, 55) : Pal.C(120, 96, 64);
        int W = S * 2;
        var b = new Bitmap(W, W);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // base plate + rim
            using (var baseB = new SolidBrush(tone)) g.FillRectangle(baseB, 1, 1, W - 2, W - 2);
            using (var edge = new Pen(Pal.Darken(tone, 0.45f), 2f)) g.DrawRectangle(edge, 1, 1, W - 4, W - 4);
            // tread scuffs
            using (var tread = new Pen(Pal.Darken(tone, 0.18f), 1f))
                for (int i = 0; i < 6; i++)
                {
                    g.DrawLine(tread, 5 + i * 3, 5, 8 + i * 3, 8);
                    g.DrawLine(tread, W - 9 - i * 3, W - 9, W - 6 - i * 3, W - 6);
                }
            // corner bolts
            using (var bolt = new SolidBrush(Pal.C(70, 62, 50)))
                foreach (var (bx, by) in new[] { (5, 5), (W - 8, 5), (5, W - 8), (W - 8, W - 8) })
                    g.FillRectangle(bolt, bx, by, 3, 3);
            // output ports on every edge middle (drills can face any way)
            using (var port = new SolidBrush(Pal.C(52, 48, 42)))
            {
                g.FillRectangle(port, W / 2 - 5, 1, 10, 5);
                g.FillRectangle(port, W / 2 - 5, W - 6, 10, 5);
                g.FillRectangle(port, 1, W / 2 - 5, 5, 10);
                g.FillRectangle(port, W - 6, W / 2 - 5, 5, 10);
            }
            // gantry to the center
            using (var p = new Pen(Pal.C(45, 40, 34), 3f))
            {
                g.DrawLine(p, 8, 8, W / 2, W / 2);
                g.DrawLine(p, W - 8, 8, W / 2, W / 2);
                g.DrawLine(p, 8, W - 8, W / 2, W / 2);
                g.DrawLine(p, W - 8, W - 8, W / 2, W / 2);
            }
            // hub ring + drill bit
            float c = W / 2f;
            using (var ring = new Pen(Pal.C(60, 54, 46), 2f)) g.DrawEllipse(ring, c - 11, c - 11, 22, 22);
            var pts = new[] { new PointF(c, c - 14), new PointF(c + 8, c), new PointF(c, c + 14), new PointF(c - 8, c) };
            using (var br = new SolidBrush(Pal.C(168, 120, 60))) g.FillPolygon(br, pts);
            using (var hl = new Pen(Pal.C(230, 190, 120), 1.4f)) g.DrawLine(hl, c, c - 14, c, c + 12);
            if (deep) // hazard stripes across the south rim
                using (var hz = new Pen(Pal.C(214, 174, 60), 3f))
                    for (int i = 0; i < 6; i++)
                        g.DrawLine(hz, 6 + i * 10, W - 8, 12 + i * 10, W - 4);
        }
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

    /// <summary>MADDOG iter-2: plank door, metal band + handle (procedural).</summary>
    private static Bitmap BakeDoor()
    {
        var b = Make((g, bmp) =>
        {
            PanelBase(g, Pal.C(148, 112, 72));
            // vertical planks
            using var plank = new Pen(Pal.C(112, 82, 52), 1.4f);
            for (int x = 8; x < S - 6; x += 7) g.DrawLine(plank, x, 4, x, S - 4);
            // dark frame
            using var frame = new Pen(Pal.C(70, 52, 34), 2f);
            g.DrawRectangle(frame, 3, 3, S - 7, S - 7);
            // metal band across the middle
            using var band = new Pen(Pal.C(120, 126, 138), 3f);
            g.DrawLine(band, 4, S / 2, S - 5, S / 2);
            // handle
            using var hb = new SolidBrush(Pal.C(220, 190, 90));
            g.FillEllipse(hb, S - 12, S / 2 - 5, 5, 9);
        });
        Noise(b, 812, 5);
        return b;
    }

    /// <summary>MADDOG iter-4: a grazer - stocky quadruped, earthy hide,
    /// one pale horn. Faces RIGHT in the baked sprite (rotate by FaceAngle).</summary>
    private static Bitmap BakeBeast()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            float cx = S / 2f, cy = S / 2f;

            var hide = Pal.C(168, 138, 96);
            var hideDark = Pal.Darken(hide, 0.3f);

            using (var sh = new SolidBrush(Color.FromArgb(70, 10, 12, 16)))
                g.FillEllipse(sh, cx - 9, cy - 6, 18, 12);

            // legs (four stubby)
            using var leg = new SolidBrush(Pal.C(104, 84, 56));
            g.FillRectangle(leg, cx - 7, cy + 3, 3, 6);
            g.FillRectangle(leg, cx + 4, cy + 3, 3, 6);
            g.FillRectangle(leg, cx - 3, cy + 4, 3, 5);
            g.FillRectangle(leg, cx + 1, cy + 4, 3, 5);

            // body
            using (var body = new SolidBrush(hide))
                g.FillEllipse(body, cx - 9, cy - 6, 17, 12);
            using (var shade = new SolidBrush(hideDark))
                g.FillEllipse(shade, cx - 9, cy, 17, 6);

            // head + snout, off the right end
            using (var head = new SolidBrush(hide))
                g.FillEllipse(head, cx + 5, cy - 7, 9, 8);
            using (var snout = new SolidBrush(hideDark))
                g.FillEllipse(snout, cx + 11, cy - 4, 5, 5);

            // one pale horn
            using var horn = new Pen(Pal.C(226, 218, 196), 1.8f);
            g.DrawLine(horn, cx + 8, cy - 7, cx + 11, cy - 12);

            // tail
            using var tail = new Pen(hideDark, 1.6f);
            g.DrawLine(tail, cx - 9, cy - 3, cx - 13, cy - 7);

            // hide speckles
            using var sp = new SolidBrush(Pal.C(140, 112, 74));
            g.FillEllipse(sp, cx - 4, cy - 4, 2, 2);
            g.FillEllipse(sp, cx + 1, cy - 2, 2, 2);
            g.FillEllipse(sp, cx - 7, cy + 1, 2, 2);
        });
        Noise(b, 821, 4);
        return b;
    }

    /// <summary>MADDOG iter-4: a fallen grazer - slumped hide + pale rib bones.</summary>
    private static Bitmap BakeCarcass()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            float cx = S / 2f, cy = S / 2f;

            using (var sh = new SolidBrush(Color.FromArgb(90, 10, 12, 16)))
                g.FillEllipse(sh, cx - 10, cy - 4, 20, 9);

            // slumped body
            using (var hide = new SolidBrush(Pal.C(128, 104, 72)))
                g.FillEllipse(hide, cx - 9, cy - 5, 16, 9);
            using (var dark = new SolidBrush(Pal.C(96, 78, 54)))
                g.FillEllipse(dark, cx - 9, cy - 1, 16, 5);

            // exposed ribs
            using var rib = new Pen(Pal.C(220, 214, 196), 1.3f);
            for (int i = 0; i < 4; i++)
                g.DrawArc(rib, cx - 6 + i * 3, cy - 4, 3, 7, 200, 140);

            // limp head
            using (var head = new SolidBrush(Pal.C(118, 96, 66)))
                g.FillEllipse(head, cx + 6, cy + 1, 6, 5);
        });
        Noise(b, 822, 3);
        return b;
    }

    /// <summary>RECLAMATION: the ark wreck - a tilted hull ring, scorched
    /// plates, one ember porthole still glowing. Stretched over 3x3 tiles.</summary>
    private static Bitmap BakeArk()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            float cx = S / 2f, cy = S / 2f;

            using (var sh = new SolidBrush(Color.FromArgb(90, 8, 10, 14)))
                g.FillEllipse(sh, cx - 14, cy - 6, 28, 16);

            // hull ring, broken on the upper right
            using (var hull = new Pen(Pal.C(88, 98, 122), 5f))
                g.DrawArc(hull, cx - 13, cy - 13, 26, 26, 40, 300);
            using (var inner = new Pen(Pal.C(56, 64, 84), 3f))
                g.DrawArc(inner, cx - 8, cy - 8, 16, 16, 60, 260);

            // scorched hull plates
            using var plate = new SolidBrush(Pal.C(70, 78, 96));
            g.FillPolygon(plate, new PointF[]
            { new(cx - 10, cy + 2), new(cx - 2, cy + 9), new(cx - 11, cy + 10) });
            g.FillRectangle(plate, cx + 2, cy + 3, 8, 6);

            // the ember porthole - the only warm light left in the wreck
            using (var ember = new SolidBrush(Pal.C(255, 150, 70)))
                g.FillEllipse(ember, cx - 2, cy - 5, 5, 5);
            using (var halo = new Pen(Pal.C(255, 190, 110), 1.4f))
                g.DrawEllipse(halo, cx - 4, cy - 7, 9, 9);

            // scattered debris
            using var deb = new Pen(Pal.C(48, 54, 70), 1.6f);
            g.DrawLine(deb, cx - 15, cy + 12, cx - 9, cy + 14);
            g.DrawLine(deb, cx + 10, cy + 12, cx + 15, cy + 10);
        });
        Noise(b, 824, 5);
        return b;
    }

    /// <summary>MADDOG iter-9: trade caravan cart - wooden bed, striped
    /// awning, big wheels. A homely splash of commerce.</summary>
    private static Bitmap BakeTrader()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            float cx = S / 2f, cy = S / 2f;
            var wood = Pal.C(139, 104, 66);
            var woodDark = Pal.C(96, 70, 42);

            using (var sh = new SolidBrush(Color.FromArgb(70, 10, 12, 16)))
                g.FillEllipse(sh, cx - 12, cy - 4, 24, 11);

            // wheels
            using var wheel = new Pen(Pal.C(64, 48, 30), 2f);
            g.DrawEllipse(wheel, cx - 11, cy + 2, 8, 8);
            g.DrawEllipse(wheel, cx + 3, cy + 2, 8, 8);

            // cart bed
            using (var bed = new SolidBrush(wood))
                g.FillRectangle(bed, cx - 10, cy - 6, 20, 9);
            using (var edge = new Pen(woodDark, 1.6f))
                g.DrawRectangle(edge, cx - 10, cy - 6, 20, 9);

            // crates in the bed
            using var crate = new SolidBrush(Pal.C(178, 148, 92));
            g.FillRectangle(crate, cx - 8, cy - 9, 6, 4);
            g.FillRectangle(crate, cx - 1, cy - 10, 5, 5);
            g.FillRectangle(crate, cx + 5, cy - 8, 4, 3);

            // striped awning on two posts
            using var post = new Pen(woodDark, 1.4f);
            g.DrawLine(post, cx - 8, cy - 6, cx - 8, cy - 15);
            g.DrawLine(post, cx + 7, cy - 6, cx + 7, cy - 15);
            for (int i = 0; i < 6; i++)
            {
                using var stripe = new SolidBrush(i % 2 == 0
                    ? Pal.C(196, 70, 62) : Pal.C(226, 214, 190));
                g.FillRectangle(stripe, cx - 11 + i * 4, cy - 17, 4, 4);
            }
        });
        Noise(b, 823, 4);
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

    /// <summary>The colony hub: a crash-landed landing craft. Octagonal
    /// armored hull on four piston legs (one bent from the crash), a glowing
    /// reactor core under a shroud, dorsal antenna, and a half-open cargo
    /// ramp with warm light spilling out. Also serves as the falling sprite
    /// in the landing cinematic. Exports to assets/buildings/Hub.png so the
    /// whole thing can be hand-refined without touching code.</summary>
    private static Bitmap BakeHub()
    {
        int s3 = S * 3;
        var b = new Bitmap(s3, s3);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float c = s3 / 2f;

            // ---- crash scorch under everything
            using (var sc = new SolidBrush(Color.FromArgb(110, 14, 12, 10)))
                g.FillEllipse(sc, c - s3 * 0.42f, c - s3 * 0.30f, s3 * 0.84f, s3 * 0.66f);

            // ---- landing legs (pistons + pads). Front-left took the hit:
            // shorter and bent.
            void Leg(float lx, float ly, float len, bool bent)
            {
                using (var piston = new SolidBrush(Pal.C(96, 104, 116)))
                    g.FillRectangle(piston, lx - 3, ly, 6, len);
                using (var rod = new SolidBrush(Pal.C(150, 158, 170)))
                    g.FillRectangle(rod, lx - 1.6f, ly + len * 0.45f, 3.2f, len * 0.55f);
                if (bent)
                    using (var bp = new Pen(Pal.C(96, 104, 116), 4f))
                        g.DrawLine(bp, lx, ly + len * 0.7f, lx + 7, ly + len);
                using (var pad = new SolidBrush(Pal.C(64, 70, 80)))
                    g.FillEllipse(pad, lx - 7, ly + len - 2, 14, 6);
            }
            float legLen = s3 * 0.16f;
            Leg(s3 * 0.16f, s3 * 0.30f, legLen * 1.1f, bent: true);    // crashed
            Leg(s3 * 0.84f, s3 * 0.30f, legLen, bent: false);
            Leg(s3 * 0.16f, s3 * 0.66f, legLen, bent: false);
            Leg(s3 * 0.84f, s3 * 0.66f, legLen, bent: false);

            // ---- octagonal hull: dark rim, plated deck
            var oct = new PointF[]
            {
                new(c - s3 * 0.40f, c - s3 * 0.26f), new(c - s3 * 0.26f, c - s3 * 0.40f),
                new(c + s3 * 0.26f, c - s3 * 0.40f), new(c + s3 * 0.40f, c - s3 * 0.26f),
                new(c + s3 * 0.40f, c + s3 * 0.26f), new(c + s3 * 0.26f, c + s3 * 0.40f),
                new(c - s3 * 0.26f, c + s3 * 0.40f), new(c - s3 * 0.40f, c + s3 * 0.26f),
            };
            using (var rim = new SolidBrush(Pal.C(30, 42, 56)))
                g.FillPolygon(rim, oct);
            var deck = new PointF[]
            {
                new(c - s3 * 0.35f, c - s3 * 0.23f), new(c - s3 * 0.23f, c - s3 * 0.35f),
                new(c + s3 * 0.23f, c - s3 * 0.35f), new(c + s3 * 0.35f, c - s3 * 0.23f),
                new(c + s3 * 0.35f, c + s3 * 0.23f), new(c + s3 * 0.23f, c + s3 * 0.35f),
                new(c - s3 * 0.23f, c + s3 * 0.35f), new(c - s3 * 0.35f, c + s3 * 0.23f),
            };
            using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, s3, s3), Pal.C(52, 74, 94), Pal.C(36, 52, 68), 90f))
                g.FillPolygon(lg, deck);
            using (var edge = new Pen(Pal.C(88, 106, 126), 1.6f))
                g.DrawPolygon(edge, deck);

            // hull plating seams + rivets
            using (var seam = new Pen(Pal.C(26, 38, 52), 1.2f))
            {
                g.DrawLine(seam, c - s3 * 0.35f, c - s3 * 0.06f, c + s3 * 0.35f, c - s3 * 0.06f);
                g.DrawLine(seam, c - s3 * 0.35f, c + s3 * 0.10f, c + s3 * 0.35f, c + s3 * 0.10f);
                g.DrawLine(seam, c - s3 * 0.06f, c - s3 * 0.35f, c - s3 * 0.06f, c + s3 * 0.35f);
                g.DrawLine(seam, c + s3 * 0.10f, c - s3 * 0.35f, c + s3 * 0.10f, c + s3 * 0.35f);
            }
            using (var riv = new SolidBrush(Pal.C(120, 136, 154)))
                foreach (var (rx, ry) in new[]
                         { (-0.30f, -0.28f), (0.30f, -0.28f), (-0.30f, 0.28f), (0.30f, 0.28f),
                           (0f, -0.31f), (0f, 0.31f), (-0.33f, 0f), (0.33f, 0f) })
                    g.FillEllipse(riv, c + rx * s3 - 1.6f, c + ry * s3 - 1.6f, 3.2f, 3.2f);

            // ---- heat vents (dark slats), starboard
            using (var vent = new SolidBrush(Pal.C(22, 30, 40)))
                for (int i = 0; i < 4; i++)
                    g.FillRectangle(vent, c + s3 * 0.16f, c - s3 * 0.22f + i * s3 * 0.075f, s3 * 0.13f, s3 * 0.032f);

            // ---- central reactor shroud + glowing core
            using (var shroud = new SolidBrush(Pal.C(40, 58, 76)))
                g.FillEllipse(shroud, c - s3 * 0.16f, c - s3 * 0.20f, s3 * 0.32f, s3 * 0.32f);
            using (var ring = new Pen(Pal.C(20, 30, 42), 3f))
                g.DrawEllipse(ring, c - s3 * 0.16f, c - s3 * 0.20f, s3 * 0.32f, s3 * 0.32f);
            using (var core = new SolidBrush(Pal.C(120, 215, 250)))
                g.FillEllipse(core, c - s3 * 0.085f, c - s3 * 0.125f, s3 * 0.17f, s3 * 0.17f);
            using (var hot = new SolidBrush(Pal.C(210, 245, 255)))
                g.FillEllipse(hot, c - s3 * 0.038f, c - s3 * 0.078f, s3 * 0.076f, s3 * 0.076f);
            using (var halo = new Pen(Color.FromArgb(120, Pal.C(120, 210, 255)), 2.4f))
                g.DrawEllipse(halo, c - s3 * 0.115f, c - s3 * 0.155f, s3 * 0.23f, s3 * 0.23f);
            // radial vanes over the shroud
            using (var vane = new Pen(Pal.C(56, 76, 96), 2.2f))
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.PI / 4f;
                    g.DrawLine(vane,
                        c + MathF.Cos(a) * s3 * 0.115f, c - s3 * 0.04f + MathF.Sin(a) * s3 * 0.115f,
                        c + MathF.Cos(a) * s3 * 0.158f, c - s3 * 0.04f + MathF.Sin(a) * s3 * 0.158f);
                }

            // ---- dorsal antenna mast + dish + beacon
            using (var mast = new Pen(Pal.C(170, 190, 205), 2.2f))
                g.DrawLine(mast, c - s3 * 0.02f, c - s3 * 0.35f, c - s3 * 0.02f, c - s3 * 0.46f);
            using (var dish = new Pen(Pal.C(140, 160, 178), 2f))
                g.DrawArc(dish, c - s3 * 0.06f, c - s3 * 0.50f, s3 * 0.09f, s3 * 0.09f, 200, 260);
            using (var bcn = new SolidBrush(Pal.C(255, 90, 90)))
                g.FillEllipse(bcn, c - s3 * 0.032f, c - s3 * 0.475f, 3.6f, 3.6f);

            // ---- cargo ramp, half open, warm interior light (south face)
            var ramp = new PointF[]
            {
                new(c - s3 * 0.14f, c + s3 * 0.35f), new(c + s3 * 0.14f, c + s3 * 0.35f),
                new(c + s3 * 0.20f, c + s3 * 0.46f), new(c - s3 * 0.20f, c + s3 * 0.46f),
            };
            using (var door = new SolidBrush(Pal.C(28, 40, 54)))
                g.FillPolygon(door, ramp);
            using (var light = new SolidBrush(Color.FromArgb(230, 255, 196, 120)))
                g.FillPolygon(light, new PointF[]
                {
                    new(c - s3 * 0.09f, c + s3 * 0.35f), new(c + s3 * 0.09f, c + s3 * 0.35f),
                    new(c + s3 * 0.13f, c + s3 * 0.44f), new(c - s3 * 0.13f, c + s3 * 0.44f),
                });
            // hazard chevrons flanking the ramp
            using (var hz1 = new Pen(Pal.C(224, 176, 60), 2.6f))
                for (int i = 0; i < 3; i++)
                {
                    float yy = c + s3 * 0.36f + i * s3 * 0.032f;
                    g.DrawLine(hz1, c - s3 * 0.24f, yy, c - s3 * 0.185f, yy);
                    g.DrawLine(hz1, c + s3 * 0.185f, yy, c + s3 * 0.24f, yy);
                }

            // ---- hull ID + corner strobes
            TextOn(g, "FH-06", new Font("Segoe UI", 6.5f, FontStyle.Bold), Pal.C(150, 200, 230),
                c - s3 * 0.115f, c + s3 * 0.245f, s3 * 0.23f);
            using (var dot = new SolidBrush(Pal.C(120, 200, 240)))
                foreach (var (px, py) in new[]
                         { (-0.34f, -0.20f), (0.34f, -0.20f), (-0.34f, 0.20f), (0.34f, 0.20f) })
                    g.FillEllipse(dot, c + px * s3 - 2.2f, c + py * s3 - 2.2f, 4.4f, 4.4f);
        }
        Noise(b, 900, 5);
        return b;
    }

    /// <summary>Small centered text helper for sprite bakes.</summary>
    private static void TextOn(Graphics g, string s, Font f, Color col, float x, float y, float w)
    {
        var sz = g.MeasureString(s, f);
        using var br = new SolidBrush(col);
        g.DrawString(s, f, br, x + (w - sz.Width) / 2f, y);
    }

    // ----------------------------------------------------------- pawns -----

    private static readonly Color[] Shirts =
    {
        Pal.C(84, 140, 150), Pal.C(140, 120, 90), Pal.C(120, 140, 90),
        Pal.C(150, 96, 96), Pal.C(96, 118, 160), Pal.C(128, 128, 140),
    };
    private static readonly Color[] Belts =      // waist-pack utility colors
    {
        Pal.C(120, 85, 55), Pal.C(70, 74, 82), Pal.C(105, 110, 70),
        Pal.C(170, 140, 95), Pal.C(110, 60, 60), Pal.C(85, 100, 115),
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

    private static Bitmap ComposePawn(int shirt, int skin, int hair, int belt, Dir dir)
    {
        // WHOLE-PAWN MODE: one file per direction (pawn_down.png...). Magic
        // keys tag the 4 tint regions, everything else stays as painted —
        // one file does the job of all the parts.
        string ds = dir.ToString().ToLowerInvariant();
        var whole = TryAsset($"units/pawns/pawn_{ds}.png");
        if (whole != null)
            return ApplyTones(whole, Color.Transparent, Shirts[shirt], Skins[skin], Hairs[hair], Belts[belt]);

        return Make(3, (g, bmp) =>   // 3x supersampled: smooth pawns, hi-res part art survives
        {
            g.DrawImage(PawnPart("shadow", null, shirt, skin, hair, belt), 0, 0, S, S);
            g.DrawImage(PawnPart("body", null, shirt, skin, hair, belt), 0, 0, S, S);
            g.DrawImage(PawnPart("belt", null, shirt, skin, hair, belt), 0, 0, S, S);
            g.DrawImage(PawnPart("head", dir, shirt, skin, hair, belt), 0, 0, S, S);
            g.DrawImage(PawnPart("hair", dir, shirt, skin, hair, belt), 0, 0, S, S);
            g.DrawImage(PawnPart("face", dir, shirt, skin, hair, belt), 0, 0, S, S);
        });
    }

    /// <summary>Whole-pawn TEMPLATE for painters: the default pawn with
    /// every tintable region painted in its magic key color. Copy to
    /// pawn_&lt;dir&gt;.png, repaint, done — keys become the pawn's colors.</summary>
    private static Bitmap BakePawnWholeTemplate(Dir dir)
    {
        return Make(3, (g, bmp) =>
        {
            g.DrawImage(BakePawnShadow(), 0, 0, S, S);
            g.DrawImage(ApplyTones(BakePawnBody(), KeyShirt, KeyShirt, KeySkin, KeyHair, KeyBelt), 0, 0, S, S);
            g.DrawImage(ApplyTones(BakePawnBelt(), KeyBelt, KeyShirt, KeySkin, KeyHair, KeyBelt), 0, 0, S, S);
            g.DrawImage(ApplyTones(BakePawnHead(), KeySkin, KeyShirt, KeySkin, KeyHair, KeyBelt), 0, 0, S, S);
            g.DrawImage(ApplyTones(BakePawnHair(dir), KeyHair, KeyShirt, KeySkin, KeyHair, KeyBelt), 0, 0, S, S);
            g.DrawImage(BakePawnFace(dir), 0, 0, S, S);
        });
    }

    private static readonly Dictionary<string, Bitmap> _pawnParts = new();

    /// <summary>GRAY-TEMPLATE SYSTEM (Minecraft-dye style): ONE grayscale
    /// file per part; the game tints it with the pawn's palette color.
    /// Templates are cached under their file name, tinted results under
    /// "file#tone". Paint white = full tone color, darker gray = shading.</summary>
    private static Bitmap PawnPart(string part, Dir? dir, int shirt, int skin, int hair, int belt)
    {
        string direction = dir?.ToString().ToLowerInvariant() ?? "";
        string file = part switch
        {
            "shadow" => "units/pawns/shadow.png",
            "body" => "units/pawns/body.png",
            "belt" => "units/pawns/belt.png",
            "head" => $"units/pawns/head_{direction}.png",
            "hair" => $"units/pawns/hair_{direction}.png",
            "face" => $"units/pawns/face_{direction}.png",
            _ => throw new ArgumentOutOfRangeException(nameof(part)),
        };

        // untinted parts (face, shadow) are returned as-is
        if (part is not ("body" or "belt" or "head" or "hair"))
        {
            if (_pawnParts.TryGetValue(file, out var fixedBmp)) return fixedBmp;
            var fb = TryAsset(file) ?? (part switch
            {
                "shadow" => BakePawnShadow(),
                "face" => BakePawnFace(dir!.Value),
                _ => throw new ArgumentOutOfRangeException(nameof(part)),
            });
            _pawnParts[file] = fb;
            return fb;
        }

        string key = $"{file}#{shirt}.{skin}.{hair}.{belt}";
        if (_pawnParts.TryGetValue(key, out var tinted)) return tinted;

        Bitmap tpl;
        if (_pawnParts.TryGetValue(file, out var cachedTpl)) tpl = cachedTpl;
        else
        {
            tpl = TryAsset(file) ?? (part switch
            {
                "body" => BakePawnBody(),
                "belt" => BakePawnBelt(),
                "head" => BakePawnHead(),
                "hair" => BakePawnHair(dir!.Value),
                _ => throw new ArgumentOutOfRangeException(nameof(part)),
            });
            _pawnParts[file] = tpl;
        }
        var col = part switch
        {
            "body" => Shirts[shirt], "belt" => Belts[belt],
            "head" => Skins[skin], _ => Hairs[hair],
        };
        var result = ApplyTones(tpl, col, Shirts[shirt], Skins[skin], Hairs[hair], Belts[belt]);
        _pawnParts[key] = result;
        return result;
    }

    // MAGIC KEYS (Minecraft-dye style): near-white colors that read as
    // "white" to the eye but tag a region for a specific palette. Mutually
    // distinct by >12 in at least one channel, tolerance is 6.
    private static readonly Color KeyShirt = Pal.C(255, 247, 240);   // #FFF7F0 warm white
    private static readonly Color KeySkin  = Pal.C(240, 247, 255);   // #F0F7FF cool white
    private static readonly Color KeyHair  = Pal.C(242, 255, 241);   // #F2FFF1 green white
    private static readonly Color KeyBelt  = Pal.C(255, 232, 240);   // #FFE8F0 pink white

    private static bool NearKey(byte r, byte g, byte b, Color k) =>
        Math.Abs(r - k.R) <= 6 && Math.Abs(g - k.G) <= 6 && Math.Abs(b - k.B) <= 6;

    /// <summary>Tone mapper for gray templates. Per pixel: a magic key
    /// takes that palette's color; neutral gray/white takes neutralTone
    /// (skipped when neutralTone is transparent — whole-pawn mode, grays
    /// stay as painted); ANY OTHER COLOR PASSES THROUGH EXACTLY AS PAINTED
    /// (outlines, buckles, camo...). LockBits pass, cached per combo.</summary>
    private static Bitmap ApplyTones(Bitmap src, Color neutralTone, Color shirt, Color skin, Color hair, Color belt)
    {
        bool tintNeutral = neutralTone.A != 0;
        var b = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, src.Width, src.Height);
        var sb = src.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var db = b.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        int n = Math.Abs(sb.Stride) * src.Height;
        var sp = new byte[n];
        System.Runtime.InteropServices.Marshal.Copy(sb.Scan0, sp, 0, n);
        var dp = new byte[n];
        for (int i = 0; i < n; i += 4)
        {
            byte pr = sp[i + 2], pg = sp[i + 1], pb = sp[i];    // memory order: B G R A
            byte a = sp[i + 3];
            Color t = default;
            if (a != 0)
            {
                if (NearKey(pr, pg, pb, KeyShirt)) t = shirt;
                else if (NearKey(pr, pg, pb, KeySkin)) t = skin;
                else if (NearKey(pr, pg, pb, KeyHair)) t = hair;
                else if (NearKey(pr, pg, pb, KeyBelt)) t = belt;
                else if (tintNeutral &&
                         Math.Max(pr, Math.Max(pg, pb)) - Math.Min(pr, Math.Min(pg, pb)) <= 6)
                    t = neutralTone;
            }
            if (t.A != 0)
            {
                dp[i] = (byte)(pb * t.B / 255);
                dp[i + 1] = (byte)(pg * t.G / 255);
                dp[i + 2] = (byte)(pr * t.R / 255);
            }
            else { dp[i] = pb; dp[i + 1] = pg; dp[i + 2] = pr; }
            dp[i + 3] = a;
        }
        System.Runtime.InteropServices.Marshal.Copy(dp, 0, db.Scan0, n);
        src.UnlockBits(sb);
        b.UnlockBits(db);
        return b;
    }

    private static Bitmap BakePawnShadow() => Make(3, (g, bmp) =>
    {
        g.Clear(Color.Transparent);
        using var sh = new SolidBrush(Pal.CA(70, Pal.C(0, 0, 0)));
        g.FillEllipse(sh, 10, 27, 16, 6);
    });

    // Pawn bakes are GRAY TEMPLATES: white = full tone color, 178 gray =
    // 70% shade (matches the old Darken(col, 0.3) seam). Tint() applies
    // the per-pawn palette color.

    private static Bitmap BakePawnBody() => Make(3, (g, bmp) =>
    {
        g.Clear(Color.Transparent);
        using var body = new SolidBrush(Color.White);
        g.FillEllipse(body, 10, 13, 16, 17);
    });

    // waist-pack: band + side pouch, gray template (white = full belt color)
    private static Bitmap BakePawnBelt() => Make(3, (g, bmp) =>
    {
        g.Clear(Color.Transparent);
        using var band = new SolidBrush(Color.White);
        g.FillRectangle(band, 10, 24, 16, 3);
        using var pouch = new SolidBrush(Pal.C(225, 225, 225));
        g.FillRectangle(pouch, 6.5f, 20, 5, 8);
        using var flap = new SolidBrush(Pal.C(178, 178, 178));
        g.FillRectangle(flap, 6.5f, 20, 5, 2);
    });

    private static Bitmap BakePawnHead() => Make(3, (g, bmp) =>
    {
        g.Clear(Color.Transparent);
        using var hd = new SolidBrush(Color.White);
        g.FillEllipse(hd, 11, 3, 14, 13);
    });

    private static Bitmap BakePawnHair(Dir dir) => Make(3, (g, bmp) =>
    {
        g.Clear(Color.Transparent);
        using var hb = new SolidBrush(Color.White);
        switch (dir)
        {
            case Dir.Up: g.FillEllipse(hb, 10.5f, 2, 15, 13); break;
            case Dir.Down: g.FillPie(hb, 11, 2, 14, 12, 180, 180); break;
            case Dir.Right: g.FillPie(hb, 11, 2, 14, 12, 110, 220); break;
            case Dir.Left: g.FillPie(hb, 11, 2, 14, 12, -70, 220); break;
        }
    });

    private static Bitmap BakePawnFace(Dir dir) => Make(3, (g, bmp) =>
    {
        g.Clear(Color.Transparent);
        using var eye = new SolidBrush(Pal.C(30, 26, 22));
        if (dir == Dir.Down)
        {
            g.FillRectangle(eye, 14, 9, 2, 2);
            g.FillRectangle(eye, 20, 9, 2, 2);
        }
        else if (dir == Dir.Right)
            g.FillRectangle(eye, 20, 9, 2, 2);
        else if (dir == Dir.Left)
            g.FillRectangle(eye, 14, 9, 2, 2);
    });

    // ------------------------------------------------------ item sprites --
    // One 32x32 sprite per ItemKind: belt cargo at a glance + stock panel.
    // Same conventions as terrain/buildings: NW light, transparent bg.

    private static void BakeItemIcons()
    {
        for (int i = 0; i < ItemIcons.Length; i++)
            ItemIcons[i] = BakeItemIcon((ItemKind)i);
    }

    private static Bitmap BakeItemIcon(ItemKind k)
    {
        return Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            float cx = S / 2f, cy = S / 2f;
            Action<Color, Color> lumps = (baseCol, hi) =>      // ore triple-lump
            {
                using (var b = new SolidBrush(baseCol))
                {
                    g.FillEllipse(b, cx - 11, cy - 2, 14, 12);
                    g.FillEllipse(b, cx - 2, cy - 8, 13, 12);
                    g.FillEllipse(b, cx - 5, cy + 2, 10, 9);
                }
                using (var h = new SolidBrush(hi))
                {
                    g.FillEllipse(h, cx - 8, cy, 4, 3);
                    g.FillEllipse(h, cx + 1, cy - 5, 4, 3);
                    g.FillEllipse(h, cx - 3, cy + 4, 3, 2);
                }
            };
            Action<Color, Color> plates = (col, hi) =>         // stacked plates
            {
                for (int i = 0; i < 3; i++)
                {
                    float y = cy + 6 - i * 6;
                    using var p = new SolidBrush(i == 2 ? hi : col);
                    g.FillPolygon(p, new PointF[]
                    {
                        new(cx - 10, y), new(cx - 4, y - 4), new(cx + 11, y - 4), new(cx + 5, y),
                    });
                    using var edge = new Pen(Pal.Darken(col, 0.4f), 1f);
                    g.DrawLine(edge, cx - 10, y, cx + 5, y);
                }
            };
            switch (k)
            {
                case ItemKind.IronOre:
                    lumps(Pal.C(140, 106, 70), Pal.C(196, 150, 96));
                    break;
                case ItemKind.CopperOre:
                    lumps(Pal.C(40, 120, 120), Pal.C(96, 196, 186));
                    break;
                case ItemKind.Stone:
                    lumps(Pal.C(118, 116, 110), Pal.C(172, 170, 162));
                    break;
                case ItemKind.Slag:
                    // ORGANISM: a jagged grey clink with a cold sheen
                    using (var clink = new SolidBrush(Pal.C(96, 92, 88)))
                        g.FillPolygon(clink, new PointF[]
                        {
                            new(cx - 10, cy + 4), new(cx - 4, cy - 8), new(cx + 3, cy - 3),
                            new(cx + 10, cy - 7), new(cx + 8, cy + 7), new(cx - 2, cy + 9),
                        });
                    using (var sheen = new SolidBrush(Pal.C(150, 146, 140)))
                    { g.FillEllipse(sheen, cx - 5, cy - 2, 4, 3); g.FillEllipse(sheen, cx + 2, cy + 2, 3, 3); }
                    break;
                case ItemKind.Gear:
                    // ORGANISM: toothed wheel with a dark hub
                    using (var wheelB = new SolidBrush(Pal.C(176, 182, 194)))
                        g.FillEllipse(wheelB, cx - 9, cy - 9, 18, 18);
                    using (var teeth = new SolidBrush(Pal.C(150, 156, 170)))
                        for (int t = 0; t < 8; t++)
                        {
                            float a = t * MathF.PI / 4f;
                            g.FillEllipse(teeth, cx + MathF.Cos(a) * 10f - 2.2f, cy + MathF.Sin(a) * 10f - 2.2f, 4.4f, 4.4f);
                        }
                    using (var hub = new SolidBrush(Pal.C(40, 45, 52)))
                        g.FillEllipse(hub, cx - 3.5f, cy - 3.5f, 7, 7);
                    break;
                case ItemKind.Circuit:
                    // ORGANISM: green board, gold traces
                    using (var board = new SolidBrush(Pal.C(38, 92, 66)))
                        g.FillRectangle(board, cx - 10, cy - 8, 20, 16);
                    using (var trace = new Pen(Pal.C(212, 178, 90), 1.4f))
                    {
                        g.DrawLine(trace, cx - 7, cy - 4, cx + 7, cy - 4);
                        g.DrawLine(trace, cx - 7, cy + 2, cx + 3, cy + 2);
                        g.DrawLine(trace, cx - 4, cy - 4, cx - 4, cy + 5);
                        g.DrawLine(trace, cx + 5, cy - 4, cx + 5, cy + 2);
                    }
                    using (var chip = new SolidBrush(Pal.C(24, 28, 34)))
                        g.FillRectangle(chip, cx - 2, cy - 1, 6, 5);
                    break;
                case ItemKind.Crystal:
                    using (var body = new SolidBrush(Pal.C(154, 92, 255)))
                        g.FillPolygon(body, new PointF[]
                        {
                            new(cx, cy - 12), new(cx + 9, cy - 2), new(cx + 4, cy + 11),
                            new(cx - 4, cy + 11), new(cx - 9, cy - 2),
                        });
                    using (var facet = new SolidBrush(Pal.C(216, 190, 255)))
                        g.FillPolygon(facet, new PointF[]
                        {
                            new(cx, cy - 12), new(cx + 9, cy - 2), new(cx, cy + 1),
                        });
                    using (var edge = new Pen(Pal.C(104, 52, 180), 1.2f))
                        g.DrawPolygon(edge, new PointF[]
                        {
                            new(cx, cy - 12), new(cx + 9, cy - 2), new(cx + 4, cy + 11),
                            new(cx - 4, cy + 11), new(cx - 9, cy - 2),
                        });
                    break;
                case ItemKind.Biomass:
                    using (var b = new SolidBrush(Pal.C(47, 178, 140)))
                    {
                        g.FillEllipse(b, cx - 10, cy - 6, 12, 11);
                        g.FillEllipse(b, cx - 1, cy - 9, 12, 12);
                        g.FillEllipse(b, cx - 6, cy + 1, 11, 9);
                    }
                    using (var h = new SolidBrush(Pal.C(120, 235, 190)))
                        g.FillEllipse(h, cx + 1, cy - 6, 4, 4);
                    break;
                case ItemKind.IronPlate:
                    plates(Pal.C(168, 172, 182), Pal.C(214, 218, 226));
                    break;
                case ItemKind.CopperPlate:
                    plates(Pal.C(196, 108, 66), Pal.C(240, 158, 110));
                    break;
                case ItemKind.Ammo:
                    using (var box = new SolidBrush(Pal.C(70, 60, 34)))
                        g.FillRectangle(box, cx - 11, cy - 8, 22, 17);
                    using (var band = new SolidBrush(Pal.C(255, 205, 80)))
                    {
                        g.FillRectangle(band, cx - 11, cy - 5, 22, 4);
                        g.FillRectangle(band, cx - 11, cy + 3, 22, 4);
                    }
                    using (var edge = new Pen(Pal.C(40, 34, 20), 1.2f))
                        g.DrawRectangle(edge, cx - 11, cy - 8, 22, 17);
                    break;
                case ItemKind.Food:
                    using (var tin = new SolidBrush(Pal.C(96, 150, 82)))
                        g.FillRectangle(tin, cx - 10, cy - 7, 20, 14);
                    using (var lid = new SolidBrush(Pal.C(150, 210, 120)))
                        g.FillRectangle(lid, cx - 10, cy - 7, 20, 5);
                    using (var label = new SolidBrush(Pal.C(240, 240, 220)))
                        g.FillRectangle(label, cx - 3, cy - 1, 6, 6);
                    using (var edge = new Pen(Pal.C(52, 88, 46), 1.2f))
                        g.DrawRectangle(edge, cx - 10, cy - 7, 20, 14);
                    break;
                case ItemKind.AdvPart:
                    using (var pcb = new SolidBrush(Pal.C(38, 66, 92)))
                        g.FillRectangle(pcb, cx - 10, cy - 9, 20, 18);
                    using (var chip = new SolidBrush(Pal.C(110, 200, 255)))
                        g.FillRectangle(chip, cx - 5, cy - 4, 10, 8);
                    using (var pins = new Pen(Pal.C(230, 200, 110), 1.2f))
                        for (int i = 0; i < 3; i++)
                        {
                            g.DrawLine(pins, cx - 9, cy - 6 + i * 6, cx - 12, cy - 6 + i * 6);
                            g.DrawLine(pins, cx + 9, cy - 6 + i * 6, cx + 12, cy - 6 + i * 6);
                        }
                    using (var edge = new Pen(Pal.C(24, 42, 60), 1.2f))
                        g.DrawRectangle(edge, cx - 10, cy - 9, 20, 18);
                    break;
                case ItemKind.SciencePack:
                    using (var flask = new SolidBrush(Pal.C(120, 220, 235)))
                    {
                        g.FillRectangle(flask, cx - 3, cy - 12, 6, 8);          // neck
                        g.FillPolygon(flask, new PointF[]                       // body
                        {
                            new(cx - 3, cy - 4), new(cx + 3, cy - 4),
                            new(cx + 10, cy + 10), new(cx - 10, cy + 10),
                        });
                    }
                    using (var glow = new SolidBrush(Pal.C(210, 250, 255)))
                    {
                        g.FillEllipse(glow, cx - 5, cy + 4, 4, 3);              // bubbles
                        g.FillEllipse(glow, cx + 1, cy + 6, 3, 2);
                    }
                    using (var cap = new SolidBrush(Pal.C(80, 96, 110)))
                        g.FillRectangle(cap, cx - 4, cy - 13, 8, 3);
                    break;
                case ItemKind.Drone:
                    using (var shadow = new SolidBrush(Color.FromArgb(60, 10, 12, 16)))
                        g.FillEllipse(shadow, cx - 8, cy + 7, 16, 5);
                    using (var body = new SolidBrush(Pal.C(220, 220, 190)))
                        g.FillEllipse(body, cx - 7, cy - 4, 14, 10);
                    using (var eye = new SolidBrush(Pal.C(90, 180, 210)))
                        g.FillEllipse(eye, cx + 1, cy - 1, 4, 4);
                    using (var rotor = new Pen(Pal.C(120, 120, 110), 2f))
                    {
                        g.DrawLine(rotor, cx - 12, cy - 5, cx - 3, cy - 5);
                        g.DrawLine(rotor, cx + 3, cy - 5, cx + 12, cy - 5);
                    }
                    break;
                case ItemKind.WarBot:
                    using (var head = new SolidBrush(Pal.C(150, 70, 70)))
                        g.FillRectangle(head, cx - 9, cy - 7, 18, 15);
                    using (var visor = new SolidBrush(Pal.C(255, 90, 90)))
                        g.FillRectangle(visor, cx - 6, cy - 3, 12, 4);
                    using (var antenna = new Pen(Pal.C(90, 50, 50), 1.6f))
                        g.DrawLine(antenna, cx, cy - 7, cx, cy - 12);
                    using (var edge = new Pen(Pal.C(80, 36, 36), 1.2f))
                        g.DrawRectangle(edge, cx - 9, cy - 7, 18, 15);
                    break;
            }

            // shared NW rim light
            if (k is ItemKind.IronOre or ItemKind.CopperOre or ItemKind.Stone
                or ItemKind.Biomass or ItemKind.IronPlate or ItemKind.CopperPlate)
                using (var rim = new Pen(Color.FromArgb(90, 255, 255, 245), 1f))
                    g.DrawArc(rim, cx - 10, cy - 9, 20, 18, 190f, 70f);
        });
    }

    /// <summary>Tilled soil square with furrows and a few seed dots.</summary>
    private static Bitmap BakeCropPlot()
    {
        return Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            using (var soil = new SolidBrush(Pal.C(92, 70, 48)))
                g.FillRectangle(soil, 2, 2, S - 4, S - 4);
            using (var dark = new SolidBrush(Pal.C(70, 52, 34)))
                for (int i = 0; i < 4; i++)
                    g.FillRectangle(dark, 4, 6 + i * 7, S - 8, 3);      // furrows
            using (var rim = new Pen(Pal.C(60, 44, 28), 1.4f))
                g.DrawRectangle(rim, 2, 2, S - 4, S - 4);
            using (var seed = new SolidBrush(Pal.C(190, 170, 120)))
                foreach (var (sx, sy) in new[] { (8, 8), (18, 15), (12, 24), (23, 25) })
                    g.FillEllipse(seed, sx, sy, 2.4f, 2.4f);
        });
    }

    /// <summary>Lush crop bush drawn on top of the soil, scaled by growth.</summary>
    private static Bitmap BakeCropGrowth()
    {
        return Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            float cx = S / 2f, cy = S / 2f;
            using (var leaf = new SolidBrush(Pal.C(74, 190, 90)))
            {
                g.FillEllipse(leaf, cx - 10, cy - 10, 12, 12);
                g.FillEllipse(leaf, cx - 2, cy - 12, 12, 12);
                g.FillEllipse(leaf, cx - 8, cy - 2, 11, 11);
                g.FillEllipse(leaf, cx + 1, cy - 3, 9, 9);
            }
            using (var hi = new SolidBrush(Pal.C(150, 230, 140)))
                g.FillEllipse(hi, cx - 4, cy - 8, 5, 4);
            using (var veg = new SolidBrush(Pal.C(230, 200, 90)))     // ripe veggies
                foreach (var (vx, vy) in new[] { (cx - 7f, cy + 1f), (cx + 3f, cy - 1f), (cx - 1f, cy + 4f) })
                    g.FillEllipse(veg, vx, vy, 3.4f, 3.4f);
        });
    }

    /// <summary>Stone firebox: ring of stacked stones, dark mouth, ember
    /// glow. No power, no tech — the anti-softlock hand smelter.</summary>
    private static Bitmap BakePrimitiveFurnace()
    {
        return Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            float cx = S / 2f, cy = S / 2f;

            // ring of stones
            var stone = Pal.C(128, 126, 118);
            var stoneDark = Pal.C(88, 86, 80);
            for (int i = 0; i < 10; i++)
            {
                double a = i * Math.PI * 2 / 10;
                float sx = cx + MathF.Cos((float)a) * 11f, sy = cy + MathF.Sin((float)a) * 11f;
                using var b = new SolidBrush(i % 2 == 0 ? stone : stoneDark);
                g.FillEllipse(b, sx - 3.4f, sy - 3.4f, 6.8f, 6.8f);
            }

            // dark mouth + ember glow
            using (var hole = new SolidBrush(Pal.C(26, 18, 14)))
                g.FillEllipse(hole, cx - 7, cy - 7, 14, 14);
            using (var ember = new SolidBrush(Pal.C(236, 130, 50)))
                g.FillEllipse(ember, cx - 4, cy - 1, 8, 5);
            using (var hot = new SolidBrush(Pal.C(255, 200, 90)))
                g.FillEllipse(hot, cx - 2, cy, 4, 2);

            // heat shimmer ticks above the mouth
            using (var tick = new Pen(Pal.CA(140, Pal.C(255, 170, 80)), 1.1f))
                for (int i = 0; i < 3; i++)
                    g.DrawLine(tick, cx - 6 + i * 6, cy - 9, cx - 6 + i * 6, cy - 12);

            // NW rim light on the stone ring
            using (var rim = new Pen(Color.FromArgb(100, 235, 235, 225), 1f))
                g.DrawArc(rim, cx - 14, cy - 14, 28, 28, 190f, 80f);
        });
    }

    /// <summary>Top-down raider: a hunched scavenger in a hood with a crude
    /// pipe rifle held forward. Sprite faces UP; the renderer rotates it by
    /// the unit's facing angle. Apex = veteran: crimson armor, spiked
    /// pauldron, heavier muzzle brake. Light comes from the top-left.</summary>
    private static Bitmap BakeRaider(bool apex)
    {
        float k = apex ? 1.2f : 1f;
        return Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            float cx = S / 2f, cy = S / 2f;

            var armor = apex ? Pal.C(150, 38, 48) : Pal.C(96, 78, 62);
            var armorDark = Pal.Darken(armor, 0.35f);
            var metal = Pal.C(120, 126, 134);
            var metalDark = Pal.C(74, 79, 86);
            var hood = apex ? Pal.C(64, 18, 26) : Pal.C(52, 48, 44);

            // ground shadow
            using (var sh = new SolidBrush(Color.FromArgb(70, 10, 12, 16)))
                g.FillEllipse(sh, cx - 8 * k, cy - 8 * k, 16 * k, 16 * k);

            // boots (splayed stance)
            using (var bt = new SolidBrush(Pal.C(36, 32, 30)))
            {
                g.FillEllipse(bt, cx - 6.5f * k, cy + 7 * k, 5 * k, 4 * k);
                g.FillEllipse(bt, cx + 1.5f * k, cy + 7 * k, 5 * k, 4 * k);
            }

            // looted backpack
            using (var bp = new SolidBrush(Pal.C(70, 56, 40)))
                g.FillRectangle(bp, cx - 3.5f * k, cy + 2 * k, 7 * k, 5.5f * k);
            using (var strap = new Pen(Pal.C(48, 38, 28), 1.2f))
                g.DrawLine(strap, cx - 3.5f * k, cy + 4 * k, cx + 3.5f * k, cy + 4 * k);

            // hunched coat torso
            using (var coat = new SolidBrush(armor))
                g.FillEllipse(coat, cx - 6.5f * k, cy - 5 * k, 13 * k, 11 * k);
            using (var dk = new SolidBrush(armorDark))
                g.FillEllipse(dk, cx - 6.5f * k, cy - 1 * k, 13 * k, 7 * k);
            using (var strap = new Pen(Pal.C(40, 34, 28), 1.6f))
                g.DrawLine(strap, cx - 5 * k, cy - 4 * k, cx + 4 * k, cy + 1 * k);

            // arms reaching forward to the weapon
            using (var arm = new Pen(armor, 2.6f * k))
            {
                g.DrawLine(arm, cx - 5.5f * k, cy - 3 * k, cx - 2.5f * k, cy - 8 * k);
                g.DrawLine(arm, cx + 5.5f * k, cy - 3 * k, cx + 2 * k, cy - 7 * k);
            }
            using (var hd = new SolidBrush(Pal.C(196, 148, 108)))
            {
                g.FillEllipse(hd, cx - 3.4f * k, cy - 9 * k, 2.6f * k, 2.6f * k);
                g.FillEllipse(hd, cx + 1.2f * k, cy - 8 * k, 2.6f * k, 2.6f * k);
            }

            // crude pipe rifle, barrel pointing forward (up)
            using (var gun = new SolidBrush(metalDark))
                g.FillRectangle(gun, cx - 1.6f * k, cy - 13 * k, 3.2f * k, 9 * k);
            using (var sheen = new SolidBrush(metal))
                g.FillRectangle(sheen, cx - 1.6f * k, cy - 13 * k, 1.2f * k, 9 * k);
            using (var muz = new SolidBrush(Pal.C(50, 52, 58)))
                g.FillRectangle(muz, cx - 2.1f * k, cy - 13.5f * k, 4.2f * k, 2 * k);
            using (var wd = new SolidBrush(Pal.C(88, 62, 40)))
                g.FillRectangle(wd, cx - 2.2f * k, cy - 5 * k, 4.4f * k, 4 * k);

            // shoulder pauldrons (left one scavenged plate)
            using (var pd = new SolidBrush(metal))
                g.FillEllipse(pd, cx - 8.5f * k, cy - 5.5f * k, 5 * k, 5 * k);
            using (var edge = new Pen(metalDark, 1f))
                g.DrawEllipse(edge, cx - 8.5f * k, cy - 5.5f * k, 5 * k, 5 * k);
            using (var pd2 = new SolidBrush(armorDark))
                g.FillEllipse(pd2, cx + 3.5f * k, cy - 5.5f * k, 5 * k, 5 * k);

            if (apex)   // spiked veteran pauldron
                using (var sp = new Pen(Pal.C(40, 12, 18), 1.6f))
                {
                    g.DrawLine(sp, cx - 6 * k, cy - 5.5f * k, cx - 7.5f * k, cy - 9 * k);
                    g.DrawLine(sp, cx - 4.5f * k, cy - 7 * k, cx - 5.5f * k, cy - 10.5f * k);
                }

            // hooded head + mask gap + eye glint
            using (var h = new SolidBrush(hood))
                g.FillEllipse(h, cx - 4.5f * k, cy - 12.5f * k, 9 * k, 9 * k);
            using (var face = new SolidBrush(Pal.C(58, 50, 46)))
                g.FillEllipse(face, cx - 2.8f * k, cy - 11.2f * k, 5.6f * k, 5.6f * k);
            using (var eye = new SolidBrush(apex ? Pal.C(255, 60, 60) : Pal.C(255, 120, 80)))
            {
                g.FillEllipse(eye, cx - 2.2f * k, cy - 9.4f * k, 1.6f * k, 1.6f * k);
                g.FillEllipse(eye, cx + 0.8f * k, cy - 9.4f * k, 1.6f * k, 1.6f * k);
            }
            // hood tip trailing back
            using (var tip = new SolidBrush(hood))
                g.FillPolygon(tip, new PointF[]
                {
                    new(cx - 2.5f * k, cy - 5 * k), new(cx + 2.5f * k, cy - 5 * k), new(cx, cy - 1.5f * k)
                });

            // NW rim light (art convention: light from top-left)
            using (var rim = new Pen(Color.FromArgb(110, 235, 235, 220), 1.1f))
            {
                g.DrawArc(rim, cx - 4.5f * k, cy - 12.5f * k, 9 * k, 9 * k, 190f, 80f);
                g.DrawArc(rim, cx - 6.5f * k, cy - 5 * k, 13 * k, 11 * k, 200f, 70f);
            }
        });
    }

    // -------------------------------------------------- v0.3 additions ----

    /// <summary>Copper deposit: dark base rock with teal patina nuggets.</summary>
    private static Bitmap BakeCopper()
    {
        var b = Make((g, bmp) =>
        {
            g.Clear(Color.Transparent);
            // ore bed: darker dirt patch (teal-tinted)
            using (var bed = new SolidBrush(Pal.CA(150, Pal.C(20, 30, 30))))
                g.FillEllipse(bed, 2, 3, S - 4, S - 7);
            using (var sh = new SolidBrush(Pal.CA(80, Pal.C(8, 10, 12))))
                g.FillEllipse(sh, 5, 8, 22, 18);
            var r = new Random(6001);
            for (int i = 0; i < 6; i++)
            {
                int size = r.Next(7, 12);
                int x = 3 + (i % 3) * 10 + r.Next(-2, 3);
                int y = 3 + (i / 3) * 13 + r.Next(-2, 3);
                using (var br = new SolidBrush(Pal.C(38, 128, 128)))
                    g.FillEllipse(br, x, y, size, size);
                using (var hi = new SolidBrush(Pal.C(86, 196, 188)))
                    g.FillEllipse(hi, x + 1, y + 1, size / 2, size / 2);
                using (var sp = new SolidBrush(Pal.C(160, 240, 232)))
                    g.FillRectangle(sp, x + size / 3, y + size / 3, 2, 2);
                using (var pen = new Pen(Pal.C(18, 70, 70), 1f))
                    g.DrawEllipse(pen, x, y, size, size);
            }
        });
        Noise(b, 601, 8);
        return b;
    }

    /// <summary>Mindustry-style merge marker: chevrons curving from a side
    /// input into the main flow. Baked once for facing Right / input from
    /// the north (left of travel), rotated + mirrored for the rest.</summary>
    private static Bitmap BakeBeltMergeLeft()
    {
        var b = new Bitmap(S, S);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // quarter curve: top-center -> top-right corner -> right-center
            var p0 = new PointF(S / 2f, 4);
            var p1 = new PointF(S - 5, 5);
            var p2 = new PointF(S - 4, S / 2f);
            using var pen = new Pen(Pal.CA(210, Pal.Accent), 2.2f);
            g.DrawBezier(pen, p0, p1, p2, p2);
            // three chevrons riding the curve
            for (int k = 0; k < 3; k++)
            {
                float t = 0.18f + k * 0.30f;
                float mt = 1 - t;
                float bx = mt * mt * p0.X + 2 * mt * t * p1.X + t * t * p2.X;
                float by = mt * mt * p0.Y + 2 * mt * t * p1.Y + t * t * p2.Y;
                float tx = 2 * mt * (p1.X - p0.X) + 2 * t * (p2.X - p1.X);
                float ty = 2 * mt * (p1.Y - p0.Y) + 2 * t * (p2.Y - p1.Y);
                float len = MathF.Sqrt(tx * tx + ty * ty);
                tx /= len; ty /= len;
                using var ch = new Pen(Pal.CA(230, Pal.Accent), 2f);
                g.DrawLine(ch, bx - tx * 3 - ty * 2.6f, by - ty * 3 + tx * 2.6f, bx + tx * 2, by + ty * 2);
                g.DrawLine(ch, bx - tx * 3 + ty * 2.6f, by - ty * 3 - tx * 2.6f, bx + tx * 2, by + ty * 2);
            }
        }
        return b;
    }

    private static void BakeBeltMerges()
    {
        var left = BakeBeltMergeLeft();
        var right = (Bitmap)left.Clone();
        right.RotateFlip(System.Drawing.RotateFlipType.RotateNoneFlipX);  // input from the south
        for (int f = 0; f < 4; f++)
        {
            // Rotate90 x f: Right(0)->Down(1)->Left(2)->Up(3), matching Dir
            var l = (Bitmap)left.Clone();
            var r = (Bitmap)right.Clone();
            for (int k = 0; k < f; k++)
            {
                l.RotateFlip(System.Drawing.RotateFlipType.Rotate90FlipNone);
                r.RotateFlip(System.Drawing.RotateFlipType.Rotate90FlipNone);
            }
            BeltMerge[0, f] = l;
            BeltMerge[1, f] = r;
        }
    }

    /// <summary>Curved belt, canonical orientation: input from the NORTH
    /// (top edge), output EAST (right edge) — a clockwise quarter turn.
    /// Rails are quarter arcs around the top-right corner; rollers are
    /// radial ticks scrolling along the arc (4-frame loop).</summary>
    private static Bitmap BakeBeltCurve(bool fast, int frame)
    {
        var b = new Bitmap(S, S);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var surface = fast ? Pal.C(58, 74, 98) : Pal.BeltCol;
            var railC = fast ? Pal.C(34, 42, 58) : Pal.C(32, 36, 42);
            var roller = fast ? Pal.C(96, 116, 148) : Pal.C(70, 78, 88);
            float cx = S, cy = 0;                       // arc center: top-right corner
            // band exactly as thick as the straights (r 5..27) so the
            // joints line up flush - no step between curve and neighbors
            using (var pen = new Pen(surface, 22f))
                g.DrawArc(pen, cx - 16, cy - 16, 32, 32, 90, 90);
            using (var pen = new Pen(railC, 3f))        // rails flush with straight rails
            {
                g.DrawArc(pen, cx - 3.5f, cy - 3.5f, 7, 7, 90, 90);
                g.DrawArc(pen, cx - 28.5f, cy - 28.5f, 57, 57, 90, 90);
            }
            using (var pen = new Pen(roller, 2f))       // scrolling rollers (radial ticks)
                for (int k = 0; k < 8; k++)
                {
                    double a = (90 + (k * 11.25 + frame * 22.5) % 90) * Math.PI / 180;
                    float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                    g.DrawLine(pen, cx + 7 * ca, cy + 7 * sa, cx + 25 * ca, cy + 25 * sa);
                }
            // chevron riding the arc, pointing along the flow
            double mid = 135 * Math.PI / 180;
            float cm = (float)Math.Cos(mid), sm = (float)Math.Sin(mid);
            float tx = cx + 16f * cm, ty = cy + 16f * sm;
            float fx = (float)Math.Sin(mid), fy = -(float)Math.Cos(mid);
            var accent = fast ? Pal.C(120, 220, 255) : Pal.Accent;
            using var br = new SolidBrush(Pal.CA(220, accent));
            g.FillPolygon(br, new[]
            {
                new PointF(tx, ty),
                new PointF(tx - fx * 5 - cm * 3, ty - fy * 5 - sm * 3),
                new PointF(tx - fx * 5 + cm * 3, ty - fy * 5 + sm * 3),
            });
        }
        return b;
    }

    /// <summary>Builds all 8 curve variants (4 inlets x 2 turns) from the
    /// canonical bake by rotation + mirroring. sets[turn][inlet] = frames;
    /// turn 0 = clockwise, canonical inlet is Up(3).</summary>
    private static void FillCurveSets(Bitmap[][][] sets, bool fast)
    {
        var canon = new Bitmap[4];
        var mirror = new Bitmap[4];
        for (int f = 0; f < 4; f++)
        {
            canon[f] = BakeBeltCurve(fast, f);
            mirror[f] = (Bitmap)canon[f].Clone();
            mirror[f].RotateFlip(System.Drawing.RotateFlipType.RotateNoneFlipX);
        }
        for (int t = 0; t < 2; t++)
            for (int inDir = 0; inDir < 4; inDir++)
            {
                int k = (inDir - 3 + 4) % 4;            // rotations from canonical
                var fs = new Bitmap[4];
                for (int f = 0; f < 4; f++)
                {
                    fs[f] = (Bitmap)(t == 0 ? canon[f] : mirror[f]).Clone();
                    for (int r = 0; r < k; r++)
                        fs[f].RotateFlip(System.Drawing.RotateFlipType.Rotate90FlipNone);
                }
                sets[t][inDir] = fs;
            }
    }

    /// <summary>3x3 crashed ark section — landmark scenery, baked at full
    /// 3S x 3S so it never renders as a stretched 32px sprite.</summary>
    private static Bitmap BakeArkWreck()
    {
        int W = S * 3;
        var b = new Bitmap(W, W);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // tilted hull section with a torn edge
            using (var hull = new SolidBrush(Pal.C(96, 92, 100)))
                g.FillPolygon(hull, new[] { new PointF(10, 70), new PointF(30, 18), new PointF(78, 8),
                                            new PointF(88, 44), new PointF(64, 86), new PointF(24, 88) });
            using (var dark = new SolidBrush(Pal.C(64, 60, 70)))
                g.FillPolygon(dark, new[] { new PointF(30, 18), new PointF(78, 8), new PointF(88, 44), new PointF(52, 40) });
            // rib frames
            using (var rib = new Pen(Pal.C(50, 46, 56), 2.5f))
            {
                g.DrawLine(rib, 40, 16, 36, 74);
                g.DrawLine(rib, 58, 12, 56, 72);
                g.DrawLine(rib, 74, 12, 76, 52);
            }
            // scorch + debris
            using (var sc = new SolidBrush(Pal.CA(90, Pal.C(20, 16, 14))))
                g.FillEllipse(sc, 14, 58, 34, 26);
            using (var deb = new SolidBrush(Pal.C(78, 74, 82)))
            {
                g.FillRectangle(deb, 8, 24, 6, 4);
                g.FillRectangle(deb, 84, 66, 7, 5);
                g.FillRectangle(deb, 66, 78, 5, 4);
            }
            using (var hz = new Pen(Pal.C(200, 160, 60), 3f))
                g.DrawLine(hz, 22, 78, 40, 84);
        }
        return b;
    }

    private static Bitmap BakeFastBelt(Dir d, int frame = 0)
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
            // scrolling rollers: 5px period, 1px/frame, 5-frame loop
            using (var roller = new Pen(Pal.C(96, 116, 148), 2f))
                for (int i = -1; i < 7; i++)
                {
                    int p = 5 + i * 5 + frame;
                    if (p < 5 || p > S - 6) continue;
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

    private static Bitmap BakeWaterFrame(int f)
    {
        var b = Make((g, bmp) =>
        {
            using (var deep = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, S, S), Pal.C(24, 54, 88), Pal.C(36, 76, 112), 90f))
                g.FillRectangle(deep, 0, 0, S, S);
            var r = new Random(910 + f * 7);
            // drifting wave crests (frame offset gives the motion)
            using (var wave = new Pen(Pal.C(60, 114, 158), 1.4f))
                for (int i = 0; i < 5; i++)
                {
                    int x = r.Next(-4, S - 10), y = r.Next(3, S - 4);
                    int len = r.Next(7, 13);
                    int dx = (f - 1) * 3;
                    g.DrawLine(wave, x + dx, y, x + len + dx, y - 1);
                }
            using (var wave2 = new Pen(Pal.CA(120, Pal.C(120, 180, 220)), 1.1f))
                for (int i = 0; i < 3; i++)
                {
                    int x = r.Next(0, S - 8), y = r.Next(4, S - 3);
                    int dx = (1 - f) * 4;
                    g.DrawLine(wave2, x + dx, y, x + 6 + dx, y);
                }
            // sparkles
            using (var sp = new SolidBrush(Pal.CA(170, Pal.C(180, 225, 250))))
                for (int i = 0; i < 3; i++)
                    g.FillEllipse(sp, r.Next(3, S - 5), r.Next(3, S - 5), 2, 1);
        });
        Noise(b, 920 + f, 3);
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

