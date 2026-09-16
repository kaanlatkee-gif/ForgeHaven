using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ForgeHaven;

// ---------------------------------------------------------------------------
//  Camera (world units are TILES; screen = (tile - cam) * tilePixels).
// ---------------------------------------------------------------------------

public static class Camera
{
    public const int TS = Sprites.S;
    public static float X, Y;
    public static float Zoom = 1f;

    public static float Sz => TS * Zoom;
    public static PointF S(float tx, float ty) => new((tx - X) * Sz, (ty - Y) * Sz);
    public static PointF ToWorld(float sx, float sy) => new(sx / Sz + X, sy / Sz + Y);
}

// ---------------------------------------------------------------------------
//  Renderer: draws everything from game state each paint.
// ---------------------------------------------------------------------------

public static class Renderer
{
    private static Font? _fSmall, _fNorm, _fBold, _fBig, _fTitle, _fMenuTitle;
    private static Font FSmall => _fSmall ??= new Font("Consolas", 8f);
    private static Font FNorm => _fNorm ??= new Font("Segoe UI", 9f);
    private static Font FBold => _fBold ??= new Font("Segoe UI", 9f, FontStyle.Bold);
    private static Font FBig => _fBig ??= new Font("Segoe UI", 13f, FontStyle.Bold);
    private static Font FTitle => _fTitle ??= new Font("Segoe UI", 30f, FontStyle.Bold);
    private static Font FMenuTitle => _fMenuTitle ??= new Font("Segoe UI", 54f, FontStyle.Bold);

    private static readonly ImageAttributes _halfAlpha = BakedAlpha(0.55f);
    private static readonly ImageAttributes _ghostAlpha = BakedAlpha(0.8f);

    private static ImageAttributes BakedAlpha(float a)
    {
        var ia = new ImageAttributes();
        ia.SetColorMatrix(new ColorMatrix { Matrix33 = a });
        return ia;
    }

    private static bool Rotatable(BuildKind k) => k switch
    {
        BuildKind.Wall or BuildKind.Hab or BuildKind.MessTable or BuildKind.Lamp
            or BuildKind.Garden or BuildKind.MedBed or BuildKind.Reactor
            or BuildKind.SolarPanel or BuildKind.WindTurbine or BuildKind.Battery
            or BuildKind.PowerPole or BuildKind.Hub or BuildKind.Pipe or BuildKind.Pump
            or BuildKind.Tank or BuildKind.SpikeTrap or BuildKind.IED or BuildKind.ShieldGen
            or BuildKind.BotFactory or BuildKind.StorageCrate or BuildKind.DronePort => false,
        _ => true,
    };

    public static void Draw(Graphics g, Game game, ViewState v, Size client)
    {
        Sprites.EnsureInit();
        g.Clear(Pal.Bg);

        if (v.App == AppState.MainMenu)
        {
            DrawMainMenuBackground(g, client);

            // soft glow + shadowed title so it reads on the terrain
            float ty = client.Height * 0.20f;
            var tsz = g.MeasureString("FORGEHAVEN", FMenuTitle);
            using (var glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(new RectangleF(
                    client.Width / 2f - tsz.Width * 0.62f, ty - tsz.Height * 0.95f,
                    tsz.Width * 1.24f, tsz.Height * 1.9f));
                using var pgb = new PathGradientBrush(glowPath)
                {
                    CenterColor = Pal.CA(55, Pal.Accent),
                    SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) },
                };
                g.FillPath(pgb, glowPath);
            }
            TextShadow(g, "FORGEHAVEN", FMenuTitle, Pal.Accent, client.Width / 2f, ty, center: true);
            TextShadow(g, Loc.T("build the machine — don't forget the people inside it"),
                FNorm, Pal.Text, client.Width / 2f, ty + 46, center: true);
            TextShadow(g, Loc.T("prototype slice · v0.4"), FSmall, Pal.TextDim, client.Width / 2f, ty + 68, center: true);

            // dialog open (settings / load): veil + only the dialog's buttons
            if (v.ModalTitle != null)
            {
                DrawModalVeil(g, v, client);
                foreach (var b in v.Buttons) DrawButton(g, b, v.Hover == b.Id);
                return;
            }

            foreach (var b in v.Buttons) DrawButton(g, b, v.Hover == b.Id);
            if (v.ShowHelp) DrawHelp(g, client);   // help sits ON TOP of the buttons
            return;
        }

        if (v.App == AppState.WorldGen)
        {
            DrawWorldGen(g, v, client);
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (v.LandingShake > 0.1f)      // touchdown: the whole screen jolts
        {
            float a = (float)(Environment.TickCount % 97) / 97f * MathF.PI * 2f;
            g.TranslateTransform(MathF.Cos(a) * v.LandingShake, MathF.Sin(a * 1.7f) * v.LandingShake);
        }
        DrawTiles(g, game, client, v.ShowGrid);
        // night veil + light pools go UNDER the buildings: ground glows warm,
        // sprites stay crisp instead of being washed by glow blobs
        if (v.Sunlight < 0.98f)
            DrawNight(g, game, v, client);
        DrawTrees(g, game, client);
        DrawBuildings(g, game, client, v);
        DrawPilesAndZones(g, game, v);
        DrawBlueprints(g, game);
        DrawMineOrders(g, game);
        DrawPrisoners(g, game);
        DrawColonists(g, game, v);
        DrawBlight(g, game, client);
        DrawRaiders(g, game, client);
        DrawWildlife(g, game, client);
        DrawTrains(g, game);
        DrawBots(g, game);
        DrawFx(g, game);
        DrawElevated(g, game, client);
        if (v.Sel.B is PowerPole) DrawPoleInfo(g, game, v.Sel.B);   // selection keeps its coverage visible
        if (v.GhostVisible) DrawGhost(g, game, v);

        // ---- map overlay layers (F1..F4) ----
        switch (v.Ovl)
        {
            case OverlayMode.Power: DrawPowerOverlay(g, game, client); break;
            case OverlayMode.Defense: DrawDefenseOverlay(g, game, client); break;
            case OverlayMode.Logistics: DrawLogisticsOverlay(g, game, client); break;
            case OverlayMode.Pollution: DrawPollutionOverlay(g, game, client); break;
        }

        // ---- weather overlay (under the HUD) ----
        DrawWeather(g, game, client);

        // ---- UI layer ----
        DrawTopBar(g, game, client.Width, v);
        DrawQuestChip(g, game, client);
        DrawAlerts(g, v, game, client);
        DrawStockPanel(g, game, v, client);
        DrawSelectionPanel(g, game, v, client);
        DrawPawnPanel(g, v, client);
        // panels are BACKGROUNDS: interactive buttons paint on top of them.
        // (Regression: the COMBAT-tab stance bar and the WORK +/- rows sat
        // INSIDE the pawn panel rect, which was painted after them - the
        // panel background covered its own buttons.)
        foreach (var b in v.Buttons) DrawButton(g, b, v.Hover == b.Id);
        DrawBuildTooltip(g, v, client);
        DrawLog(g, game, client);
        if (v.ShowHistory) DrawHistory(g, game, v, client);
        if (v.Landing01 > 0f) DrawLanding(g, game, v, client);
        if (v.LandingShake > 0.1f) g.ResetTransform();
        if (v.ShowStats) DrawStats(g, game, v, client);
        if (v.ShowChron) DrawChron(g, game, v, client);
        if (v.ShowProd) DrawProd(g, game, v, client);
        DrawHints(g, game, client);
        DrawPerf(g, game, v, client);

        // drag-selection box
        if (v.SelBox is { } box)
        {
            using var pen = new Pen(Pal.Accent, 1.6f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            g.FillRectangle(Pal.B(Pal.CA(18, Pal.Accent)), box.X, box.Y, box.Width, box.Height);
        }
        DrawHoverTooltip(g, game, v, client);
        if (v.ShowMinimap) DrawMinimap(g, game, client);

        if (game.RaidBannerT > 0)
            DrawRaidBanner(g, game, client);

        if (v.Paused && !game.Lost && !(game.Won && !game.ContinueAfterWin) && !v.ShowHelp)
            Text(g, "PAUSED", FBig, Pal.Warn, client.Width / 2f, 34, center: true);

        if (v.ShowHelp) DrawHelp(g, client);
        if (game.Won && !game.ContinueAfterWin) DrawWin(g, game, client);
        if (game.Lost) DrawLose(g, game, client);

        // ---- modal overlays ----
        if (v.ModalTitle != null)
        {
            DrawModalVeil(g, v, client);
            foreach (var b in v.Buttons) DrawButton(g, b, v.Hover == b.Id);
            if (v.ModalTitle == "RESEARCH") DrawResearchPanel(g, game, v, client);
            if (v.ModalTitle == "WORK") DrawWorkPanel(g, game, v, client);
            if (v.ModalTitle == "TRADE") DrawTradePanel(g, game, v, client);
        }
    }

    // ------------------------------------------------------------ menu bg --

    private static Bitmap? _menuTerrain;
    private static PathGradientBrush? _vignette;
    private static Size _vignetteSize;

    private static void DrawMainMenuBackground(Graphics g, Size client)
    {
        Sprites.EnsureInit();
        _menuTerrain ??= BuildMenuTerrain();

        // slow drift inside an 8% cover-fit margin (never exposes an edge)
        float t = Environment.TickCount / 1000f;
        float scale = Math.Max(client.Width / (float)_menuTerrain.Width,
                                client.Height / (float)_menuTerrain.Height) * 1.08f;
        int dw = (int)(_menuTerrain.Width * scale), dh = (int)(_menuTerrain.Height * scale);
        int ox = (client.Width - dw) / 2 + (int)(MathF.Sin(t * 0.05f) * dw * 0.03f);
        int oy = (client.Height - dh) / 2 + (int)(MathF.Cos(t * 0.04f) * dh * 0.03f);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(_menuTerrain, ox, oy, dw, dh);
        g.InterpolationMode = InterpolationMode.Default;

        // mute the terrain so the title & buttons read on top of it
        g.FillRectangle(Pal.B(Pal.CA(135, Pal.C(12, 16, 20))), 0, 0, client.Width, client.Height);

        // drifting fog banks — big, soft, very low alpha
        for (int i = 0; i < 4; i++)
        {
            long h = (long)i * 2654435761L;
            float fw = client.Width * (0.35f + (h & 255) / 255f * 0.3f);
            float fh = client.Height * 0.16f;
            float x = ((h & 1023) / 1023f * client.Width + t * (6 + i * 3)) % (client.Width + fw) - fw;
            float y = client.Height * (0.15f + ((h >> 10) & 1023) / 1023f * 0.7f);
            using var fog = new SolidBrush(Pal.CA(13, Pal.C(140, 170, 200)));
            g.FillEllipse(fog, x, y, fw, fh);
        }

        // floating spores / dust motes — soft glow, gentle bob
        for (int i = 0; i < 26; i++)
        {
            long h = (long)i * 40503L + 12345;
            float bx = (h & 1023) / 1023f * client.Width;
            float by = (((h >> 10) & 1023) / 1023f * client.Height + t * (4 + (i % 5) * 2)) % client.Height;
            float dx = MathF.Sin(t * (0.3f + (i % 4) * 0.12f) + i) * 14f;
            float dy = MathF.Cos(t * (0.24f + (i % 3) * 0.1f) + i * 2f) * 10f;
            var col = i % 7 == 0 ? Pal.Flora : i % 5 == 0 ? Pal.Accent : Pal.C(200, 215, 230);
            int a0 = 26 + (i % 3) * 9;
            float r = 1.6f + (i % 4) * 0.7f;
            using (var halo = new SolidBrush(Pal.CA(a0 / 2, col)))
                g.FillEllipse(halo, bx + dx - r * 2.2f, by + dy - r * 2.2f, r * 4.4f, r * 4.4f);
            using (var core = new SolidBrush(Pal.CA(a0, col)))
                g.FillEllipse(core, bx + dx - r, by + dy - r, r * 2, r * 2);
        }

        // vignette (cached per window size): dark edges, clear center
        if (_vignette == null || _vignetteSize != client)
        {
            _vignette?.Dispose();
            var path = new GraphicsPath();
            path.AddRectangle(new Rectangle(0, 0, client.Width, client.Height));
            _vignette = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(0, 0, 0, 0),
                SurroundColors = new[] { Color.FromArgb(165, 8, 11, 16) },
                FocusScales = new PointF(0.35f, 0.45f),
            };
            _vignetteSize = client;
        }
        g.FillRectangle(_vignette, 0, 0, client.Width, client.Height);
    }

    /// <summary>Renders a slice of a real generated world (fixed menu seed,
    /// stamped with the scenic start resources) — the backdrop IS the game's
    /// own terrain art.</summary>
    private static Bitmap BuildMenuTerrain()
    {
        int x0 = -14, y0 = -12, tw = 31, th = 25;
        var bmp = new Bitmap(tw * Sprites.S, th * Sprites.S);
        var w = new World(new GenParams { Seed = 20260909 });
        Game.StampStart(w);   // guaranteed mix: lake, ores, flora, crystal, clearing
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            for (int x = 0; x < tw; x++)
                for (int y = 0; y < th; y++)
                {
                    int wx = x0 + x, wy = y0 + y;
                    var t = w.Cell(wx, wy).T;
                    var dest = new Rectangle(x * Sprites.S, y * Sprites.S, Sprites.S, Sprites.S);
                    int vId = ((wx * 31 + wy * 17) % 3 + 3) % 3;
                    DrawSpriteSmart(g, Sprites.Ground[vId], dest);
                    int decorPick = ((wx * 73 + wy * 151) % 23 + 23) % 23;
                    if (decorPick < 3) DrawSpriteSmart(g, Sprites.Decor[decorPick], dest);
                    switch (t)
                    {
                        case Terrain.Rock: DrawSpriteSmart(g, Sprites.Rock, dest); break;
                        case Terrain.IronOre: DrawRotated(g, Sprites.IronOre, dest, x, y); break;
                        case Terrain.CopperOre: DrawRotated(g, Sprites.CopperOre, dest, x, y); break;
                        case Terrain.Crystal: DrawSpriteSmart(g, Sprites.Crystal, dest); break;
                        case Terrain.Flora: DrawSpriteSmart(g, Sprites.Flora, dest); break;
                        case Terrain.Water: DrawSpriteSmart(g, Sprites.Water, dest); break;
                    }
                }
        }
        return bmp;
    }

    // --------------------------------------------------------------- world --

    // ---------------------------------------------------------- terrain LOD --
    // Zoomed out, drawing every visible tile as its own DrawImage tanks the
    // framerate (a 1080p view at min zoom is ~16k calls per frame). Instead
    // each revealed 32x32 chunk is rendered ONCE into a bitmap at a quantized
    // zoom step; per frame we just blit chunk bitmaps, scaled slightly when
    // the user zoomed between steps. Terrain never changes during play, so a
    // chunk bitmap stays valid until the zoom step or the world changes.
    private static readonly float[] LodSteps = { 20, 14, 10, 7, 5 };
    private static readonly Dictionary<long, Bitmap> _lodChunks = new();
    private static float _lodStep = -1;
    private static World? _lodWorld;
    private static long _lodSerial = -1;

    /// <summary>Cache step for this zoom, or 0 = full-quality direct drawing
    /// (zoomed in there are few tiles on screen, no cache needed).</summary>
    private static float LodStepFor(float sz)
    {
        if (sz > LodSteps[0]) return 0;
        foreach (var s in LodSteps) if (sz <= s) return s;
        return LodSteps[^1];
    }

    private static long LodKey(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

    private static void DrawTilesLod(Graphics g, Game game, Size client, bool showGrid, float step)
    {
        float sz = Camera.Sz;
        if (_lodWorld != game.World || _lodStep != step || _lodSerial != game.World.Serial)
        {
            foreach (var b in _lodChunks.Values) b.Dispose();
            _lodChunks.Clear();
            _lodWorld = game.World;
            _lodStep = step;
            _lodSerial = game.World.Serial;
        }

        int tx0 = (int)MathF.Floor(Camera.X) - 1;
        int ty0 = (int)MathF.Floor(Camera.Y) - 1;
        int tx1 = (int)MathF.Floor(Camera.X + client.Width / sz) + 1;
        int ty1 = (int)MathF.Floor(Camera.Y + client.Height / sz) + 1;

        var oldInterp = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        // OPTIMIZATION: building several 640px chunk bitmaps in one frame
        // hitches while panning zoomed-out. Budget a few builds per frame;
        // chunks over the budget draw directly this frame (and get cached
        // on a later frame instead).
        int buildsLeft = 4;

        for (int cy = ty0 >> 5; cy <= ty1 >> 5; cy++)
            for (int cx = tx0 >> 5; cx <= tx1 >> 5; cx++)
            {
                var ch = game.World.ChunkOrNull(cx, cy);
                if (ch?.Revealed != true)
                {
                    var fp = Camera.S(cx << 5, cy << 5);
                    var rect = new Rectangle((int)fp.X, (int)fp.Y, (int)(32 * sz + 1), (int)(32 * sz + 1));
                    g.FillRectangle(Pal.B(Pal.Fog), rect);
                    long h = ((long)cx * 31 + cy * 57) & 1023;
                    if ((h & 7) == 0)
                    {
                        float ox = (h / 1023f) * 30 * sz, oy = ((h * 13) % 997 / 997f) * 30 * sz;
                        g.FillRectangle(Pal.B(Pal.CA(20, Pal.C(40, 48, 58))), rect.X + ox, rect.Y + oy, 2, 2);
                    }
                    continue;
                }

                var key = LodKey(cx, cy);
                if (!_lodChunks.TryGetValue(key, out var bmp))
                {
                    if (buildsLeft <= 0)
                    {
                        DrawChunkDirect(g, game, cx, cy, tx0, ty0, tx1, ty1);
                        continue;
                    }
                    buildsLeft--;
                    bmp = BuildLodChunk(game, cx, cy, step);
                    // memory guard: keep the cache under ~16M pixels total
                    int cap = Math.Max(24, 16_000_000 / (bmp.Width * bmp.Height));
                    if (_lodChunks.Count >= cap)
                    {
                        foreach (var b in _lodChunks.Values) b.Dispose();
                        _lodChunks.Clear();
                    }
                    _lodChunks[key] = bmp;
                }
                var p = Camera.S(cx << 5, cy << 5);
                g.DrawImage(bmp,
                    new Rectangle((int)p.X, (int)p.Y, (int)(32 * sz + 1), (int)(32 * sz + 1)),
                    0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel);
            }

        // tile grid at low zoom: one line per row/column, not a rect per tile
        if (showGrid && sz >= 8)
        {
            var pen = Pal.P(Pal.CA(38, Pal.Grid));   // shared cache - do NOT dispose
            var tl = Camera.S(tx0, ty0);
            var br = Camera.S(tx1 + 1, ty1 + 1);
            for (int x = tx0; x <= tx1 + 1; x++)
            {
                float px = Camera.S(x, 0).X;
                g.DrawLine(pen, px, tl.Y, px, br.Y);
            }
            for (int y = ty0; y <= ty1 + 1; y++)
            {
                float py = Camera.S(0, y).Y;
                g.DrawLine(pen, tl.X, py, br.X, py);
            }
        }

        g.InterpolationMode = oldInterp;
    }

    /// <summary>Ground variant + decor + terrain overlay for one tile
    /// (shared by the cached-chunk baker and the direct fallback).</summary>
    private static void DrawTileContent(Graphics g, in Tile tile, Rectangle dest, int x, int y)
    {
        int vId = ((x * 31 + y * 17) % 3 + 3) % 3;
        DrawSpriteSmart(g, Sprites.Ground[vId], dest);

        int decorPick = ((x * 73 + y * 151) % 23 + 23) % 23;
        if (decorPick < 3)
            DrawSpriteSmart(g, Sprites.Decor[decorPick], dest);

        switch (tile.T)
        {
            case Terrain.Rock: DrawSpriteSmart(g, Sprites.Rock, dest); break;
            case Terrain.IronOre: DrawRotated(g, Sprites.IronOre, dest, x, y); break;
            case Terrain.CopperOre: DrawRotated(g, Sprites.CopperOre, dest, x, y); break;
            case Terrain.Crystal: DrawSpriteSmart(g, Sprites.Crystal, dest); break;
            case Terrain.Flora: DrawSpriteSmart(g, Sprites.Flora, dest); break;
            case Terrain.Water: DrawSpriteSmart(g, Sprites.Water, dest); break;
        }
    }

    /// <summary>Budget-exceeded fallback: draw a chunk's tiles straight to
    /// the screen this frame (slow but immediate), clipped to the view.</summary>
    private static void DrawChunkDirect(Graphics g, Game game, int cx, int cy,
        int tx0, int ty0, int tx1, int ty1)
    {
        var ch = game.World.ChunkOrNull(cx, cy);
        if (ch == null) return;
        int x0 = Math.Max(tx0, cx << 5), x1 = Math.Min(tx1, (cx << 5) + 31);
        int y0 = Math.Max(ty0, cy << 5), y1 = Math.Min(ty1, (cy << 5) + 31);
        float sz = Camera.Sz;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            {
                var p = Camera.S(x, y);
                var dest = new Rectangle((int)p.X, (int)p.Y, (int)(sz + 1), (int)(sz + 1));
                DrawTileContent(g, ch.Tiles[(x & 31) + (y & 31) * World.CS], dest, x, y);
            }
    }

    /// <summary>Render one revealed chunk (32x32 tiles) into a bitmap at the
    /// given pixels-per-tile step. Mirrors the direct path's ground-variant,
    /// decor and ore-rotation hashing exactly, so zooming in/out does not
    /// reshuffle the terrain look.</summary>
    private static Bitmap BuildLodChunk(Game game, int cx, int cy, float step)
    {
        int t = (int)step;
        var bmp = new Bitmap(32 * t, 32 * t);
        var ch = game.World.ChunkOrNull(cx, cy)!;
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            for (int ly = 0; ly < 32; ly++)
                for (int lx = 0; lx < 32; lx++)
                {
                    int x = (cx << 5) + lx, y = (cy << 5) + ly;
                    var dest = new Rectangle(lx * t, ly * t, t + 1, t + 1);
                    DrawTileContent(g, ch.Tiles[lx + ly * World.CS], dest, x, y);
                }
        }
        return bmp;
    }

    private static void DrawTiles(Graphics g, Game game, Size client, bool showGrid)
    {
        float sz = Camera.Sz;

        // ---- LOD: zoomed out, terrain comes from the per-chunk cache (one
        // blit per 32x32 chunk instead of ~1000 tile draws per chunk per frame)
        float lodStep = LodStepFor(sz);
        if (lodStep > 0)
        {
            DrawTilesLod(g, game, client, showGrid, lodStep);
            return;
        }

        int tx0 = (int)MathF.Floor(Camera.X) - 1;
        int ty0 = (int)MathF.Floor(Camera.Y) - 1;
        int tx1 = (int)MathF.Floor(Camera.X + client.Width / sz) + 1;
        int ty1 = (int)MathF.Floor(Camera.Y + client.Height / sz) + 1;

        var oldInterp = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        for (int cy = ty0 >> 5; cy <= ty1 >> 5; cy++)
            for (int cx = tx0 >> 5; cx <= tx1 >> 5; cx++)
            {
                var ch = game.World.ChunkOrNull(cx, cy);
                if (ch?.Revealed != true)
                {
                    var fp = Camera.S(cx << 5, cy << 5);
                    var rect = new Rectangle((int)fp.X, (int)fp.Y, (int)(32 * sz + 1), (int)(32 * sz + 1));
                    g.FillRectangle(Pal.B(Pal.Fog), rect);
                    long h = ((long)cx * 31 + cy * 57) & 1023;
                    if ((h & 7) == 0)
                    {
                        float ox = (h / 1023f) * 30 * sz, oy = ((h * 13) % 997 / 997f) * 30 * sz;
                        g.FillRectangle(Pal.B(Pal.CA(20, Pal.C(40, 48, 58))), rect.X + ox, rect.Y + oy, 2, 2);
                    }
                    continue;
                }

                int x0 = Math.Max(tx0, cx << 5), x1 = Math.Min(tx1, (cx << 5) + 31);
                int y0 = Math.Max(ty0, cy << 5), y1 = Math.Min(ty1, (cy << 5) + 31);
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        var t = ch.Tiles[(x & 31) + (y & 31) * World.CS];
                        var p = Camera.S(x, y);
                        var dest = new Rectangle((int)p.X, (int)p.Y, (int)(sz + 1), (int)(sz + 1));

                        // NOTE: world coords go negative (hub sits at 0,0 and the
                        // camera starts centered on it) — C# % keeps the sign, so
                        // normalize or Ground[-2] crashes the first frame.
                        int vId = ((x * 31 + y * 17) % 3 + 3) % 3;
                        DrawSpriteSmart(g, Sprites.Ground[vId], dest);

                        int decorPick = ((x * 73 + y * 151) % 23 + 23) % 23;
                        if (decorPick < 3)
                            DrawSpriteSmart(g, Sprites.Decor[decorPick], dest);

                        switch (t.T)
                        {
                            case Terrain.Rock: DrawSpriteSmart(g, Sprites.Rock, dest); break;
                            case Terrain.IronOre: DrawRotated(g, Sprites.IronOre, dest, x, y); break;
                            case Terrain.CopperOre: DrawRotated(g, Sprites.CopperOre, dest, x, y); break;
                            case Terrain.Crystal: DrawSpriteSmart(g, Sprites.Crystal, dest); break;
                            case Terrain.Flora: DrawSpriteSmart(g, Sprites.Flora, dest); break;
                            case Terrain.Water: DrawSpriteSmart(g, Sprites.Water, dest); break;
                        }
                        if (showGrid)
                            g.DrawRectangle(Pal.P(Pal.CA(38, Pal.Grid)), p.X, p.Y, sz, sz);
                    }
            }

        g.InterpolationMode = oldInterp;
    }

        private static void DrawRotated(Graphics g, Bitmap sprite, Rectangle dest, int x, int y)
    {
        int hash = x * 73856093 ^ y * 19349663;
        float angle = ((hash & 3) * 90f);
    
        var state = g.Save();
        g.TranslateTransform(
            dest.X + dest.Width / 2f,
            dest.Y + dest.Height / 2f);
        g.RotateTransform(angle);
        if (sprite.Width > Sprites.S) g.InterpolationMode = InterpolationMode.Bilinear;
        g.DrawImage(sprite,
            -dest.Width / 2f,
            -dest.Height / 2f,
            dest.Width,
            dest.Height);
        g.Restore(state);
    }
    private static void DrawImageAlpha(Graphics g, Bitmap b, Rectangle r, float alpha)
    {
        using var ia = BakedAlpha(alpha);
        g.DrawImage(b, r, 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
    }

    /// <summary>Forest pass: 2-tile-tall trees drawn dynamically (not baked
    /// into the chunk cache) so their canopy can rise over the tile above,
    /// with hash-based variant/scale/offset jitter. Collision stays tile-
    /// based: trees are walkable, just slower (Bal.TreeWalkMul).</summary>
    private static void DrawTrees(Graphics g, Game game, Size client)
    {
        float sz = Camera.Sz;
        if (sz < 7) return;                        // too far out: forests read as color
        var old = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        int x0 = (int)MathF.Floor(Camera.X) - 1, x1 = (int)MathF.Ceiling(Camera.X + client.Width / sz) + 1;
        int y0 = (int)MathF.Floor(Camera.Y) - 2, y1 = (int)MathF.Ceiling(Camera.Y + client.Height / sz) + 1;
        var biome = game.BiomeKind;
        bool detail = sz >= 10f;                   // far out: plain sprites, no jitter math
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            {
                if (!game.World.InBounds(x, y)) continue;
                // FOG: unrevealed chunks draw nothing (and cost nothing)
                if (!game.World.RevealedAt(x, y)) continue;
                if (game.World.Cell(x, y).T != Terrain.Tree) continue;
                int h = ((x * 73856093) ^ (y * 19349663)) & 1023;
                // variant: biome leans the mix; hash picks
                int v = h % 100;
                int variant = biome switch
                {
                    Biome.Volcanic => v < 45 ? 4 : v < 70 ? 0 : 1,
                    Biome.Glacial  => v < 55 ? 0 : v < 80 ? 3 : 1,
                    Biome.Fungal   => v < 55 ? 2 : v < 80 ? 0 : 1,
                    _              => v < 38 ? 0 : v < 62 ? 1 : v < 85 ? 2 : 3,
                };
                var p = Camera.S(x, y);
                if (!detail)
                {
                    DrawSpriteSmart(g, Sprites.Trees[variant],
                        (int)p.X, (int)(p.Y - sz), (int)sz, (int)(sz * 2));
                    continue;
                }
                float jx = (h % 7) - 3;                       // in-block offset
                float sc = 0.92f + (h % 5) * 0.045f;          // scale jitter
                int w = (int)(sz * sc), hh = (int)(sz * 2 * sc);
                DrawSpriteSmart(g, Sprites.Trees[variant],
                    (int)(p.X + jx), (int)(p.Y - hh + sz * 1.02f), w, hh);
            }
        g.InterpolationMode = old;
    }

    /// <summary>STOCKPILES + dropped cargo: zone outlines with contents,
    /// ground piles with count badges, and the drag-rectangle preview.</summary>
    private static void DrawPilesAndZones(Graphics g, Game game, ViewState v)
    {
        float sz = Camera.Sz;
        if (sz < 6) return;

        // zone tiles
        foreach (var kv in game.PileZones)
        {
            long key = kv;
            int x = (int)(key >> 32), y = (int)(key & 0xFFFFFFFFL);
            var p = Camera.S(x, y);
            var r = new Rectangle((int)p.X, (int)p.Y, (int)sz + 1, (int)sz + 1);
            using (var fill = new SolidBrush(Pal.CA(26, Pal.C(255, 215, 120))))
                g.FillRectangle(fill, r);
            using (var pen = new Pen(Pal.CA(160, Pal.C(255, 215, 120))))
                g.DrawRectangle(pen, r);
            if (game.PileCells.TryGetValue(key, out var cell))
            {
                DrawItemDot(g, x + 0.5f, y + 0.5f, cell.Kind, sz * 0.8f);
                Text(g, cell.N.ToString(), FSmall, Pal.Text, r.Right - 2, r.Y + 1);
            }
        }

        // dropped piles
        foreach (var pile in game.ItemPiles)
        {
            DrawItemDot(g, pile.X, pile.Y, pile.Kind, sz * 0.85f);
            var p = Camera.S(pile.X, pile.Y);
            Text(g, pile.N.ToString(), FSmall, Pal.Text, p.X + sz * 0.3f, p.Y - sz * 0.3f);
        }

        // stockpile drag preview
        if (v.Tool == ToolKind.Stockpile && v.PileFrom is { } f && v.PileTo is { } t)
        {
            int ax = Math.Min(f.x, t.x), ay = Math.Min(f.y, t.y);
            int bw = Math.Abs(t.x - f.x) + 1, bh = Math.Abs(t.y - f.y) + 1;
            var p0 = Camera.S(ax, ay);
            var r = new Rectangle((int)p0.X, (int)p0.Y, (int)(bw * sz), (int)(bh * sz));
            using (var pen = new Pen(Pal.C(255, 215, 120), 2f))
            {
                pen.DashPattern = new float[] { 5, 3 };
                g.DrawRectangle(pen, r);
            }
        }
    }

    private static long _beltClock;      // frozen while paused

    /// <summary>HI-RES FRIENDLY DRAW: baked pixel-art (<= S px wide) keeps
    /// NearestNeighbor crispness; larger asset overrides (e.g. a 192px pack)
    /// downscale with Bilinear so they look clean instead of crunched.
    /// One int compare for baked art - free at frame rate.</summary>
    private static void DrawSpriteSmart(Graphics g, Bitmap img, Rectangle dest)
    {
        if (img.Width > Sprites.S)
        {
            var oi = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(img, dest);
            g.InterpolationMode = oi;
        }
        else g.DrawImage(img, dest);
    }

    private static void DrawSpriteSmart(Graphics g, Bitmap img, int x, int y, int w, int h)
    {
        if (img.Width > Sprites.S)
        {
            var oi = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(img, x, y, w, h);
            g.InterpolationMode = oi;
        }
        else g.DrawImage(img, x, y, w, h);
    }

    /// <summary>Draw a building sprite with the right filtering: baked
    /// pixel-art stays NearestNeighbor, but an asset override larger than
    /// the bake is downscaled with Bilinear so hi-res art looks clean.</summary>
    private static void DrawBuildingSprite(Graphics g, Building b, Rectangle dest)
    {
        var img = Sprites.Building(b.Kind);
        if (img.Width > Sprites.S * b.W)
        {
            var oi = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(img, dest);
            g.InterpolationMode = oi;
        }
        else g.DrawImage(img, dest);
    }

    private static void DrawBuildings(Graphics g, Game game, Size client, ViewState v)
    {
        float sz = Camera.Sz;
        var oldInterp = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        if (!v.Paused) _beltClock = Environment.TickCount;

        // ITEM Z-PASS: items riding belts are drawn AFTER all buildings, so
        // an item crossing from belt A into belt B sits on top of BOTH
        // (it used to vanish under the receiving belt's sprite).
        var itemPass = new List<(List<BeltItem> Lane, int X, int Y, Dir F, Dir? Bend)>();

        foreach (var b in game.Builds)
        {
            if (b.Kind == BuildKind.ElevatedRail) continue;   // drawn in a later pass
            if (v.LandingHideHub && b is Hub) continue;       // it is still falling from orbit

            var p = Camera.S(b.X, b.Y);
            if (p.X > client.Width || p.Y > client.Height ||
                p.X + b.W * sz < 0 || p.Y + b.H * sz < 0) continue;

            var dest = new Rectangle((int)p.X, (int)p.Y,
                (int)(b.W * sz + 1), (int)(b.H * sz + 1));

            if (b is Belt belt)
            {
                int bface = (int)belt.Face;
                Bitmap[] frames;
                if (belt.BendIn is Dir bin)
                {
                    // curved belt: pick the turn set (cw/ccw) by the cross
                    // product of outlet x inlet
                    int cross = DirU.Dx[bface] * DirU.Dy[(int)bin] - DirU.Dy[bface] * DirU.Dx[(int)bin];
                    frames = (b is FastBelt ? Sprites.FastBeltCurve : Sprites.BeltCurve)
                             [cross < 0 ? 0 : 1][(int)bin];
                }
                else
                    frames = b is FastBelt ? Sprites.FastBeltFrames[bface] : Sprites.BeltFrames[bface];
                DrawSpriteSmart(g, frames[(int)(_beltClock / 130 % frames.Length)], dest);

                // MINDUSTRY-STYLE MERGE VARIANTS: straight belts can show
                // left-only, right-only, or both-side feeders. A true curve
                // already shows its single side input by using the curve frame.
                if (belt.BendIn == null)
                {
                    int mask = belt.SideInputMask(game);
                    if ((mask & Belt.SideLeftMask) != 0)
                        DrawSpriteSmart(g, Sprites.BeltMerge[0, bface], dest);
                    if ((mask & Belt.SideRightMask) != 0)
                        DrawSpriteSmart(g, Sprites.BeltMerge[1, bface], dest);
                }

                itemPass.Add((belt.Lane, belt.X, belt.Y, belt.Face, belt.BendIn));
                continue;
            }
            if (b is CropPlot cp)
            {
                DrawBuildingSprite(g, b, dest);
                float grow = cp.Busy ? 0.35f + 0.65f * cp.Prog / cp.CraftTime : 0f;
                if (grow > 0.02f)
                {
                    int cs = Math.Max(4, (int)(dest.Width * 0.8f * grow));
                    var crop = Sprites.CropImg;
                    var oldI = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(crop,
                        dest.X + (dest.Width - cs) / 2, dest.Y + dest.Height - cs - 2, cs, cs);
                    g.InterpolationMode = oldI;
                }
                continue;
            }
            if (b is Hub hub) { DrawHub(g, hub, dest, game); continue; }
            if (b is Junction jn)
            {
                DrawBuildingSprite(g, b, dest);
                itemPass.Add((jn.Main, jn.X, jn.Y, jn.Face, null));
                itemPass.Add((jn.Cross, jn.X, jn.Y, jn.CrossDir, null));
                continue;
            }
            if (b is Rail rail)
            {
                DrawSpriteSmart(g, Sprites.Rail[(int)rail.Face], dest);
                continue;
            }
            if (b is Splitter sp)
            {
                DrawBuildingSprite(g, b, dest);
                DrawFacingTick(g, b, sz);
                if (sp.Held != null) DrawItemDot(g, b.X + 0.5f, b.Y + 0.5f, sp.Held.Value, sz);
                continue;
            }
            if (b is FilterSplitter fsp)
            {
                DrawBuildingSprite(g, b, dest);
                DrawFacingTick(g, b, sz);
                // filter chip
                DrawItemDot(g, b.X + 0.28f, b.Y + 0.28f, fsp.Filter, sz * 0.8f);
                if (fsp.Held != null) DrawItemDot(g, b.X + 0.5f, b.Y + 0.6f, fsp.Held.Value, sz);
                continue;
            }
            if (b is OverflowRouter orr)
            {
                DrawBuildingSprite(g, b, dest);
                DrawFacingTick(g, b, sz);
                if (orr.Held != null) DrawItemDot(g, b.X + 0.5f, b.Y + 0.5f, orr.Held.Value, sz);
                continue;
            }
            if (b is Merger mg)
            {
                DrawBuildingSprite(g, b, dest);
                DrawFacingTick(g, b, sz);
                if (mg.Held != null) DrawItemDot(g, b.X + 0.5f, b.Y + 0.5f, mg.Held.Value, sz);
                continue;
            }
            if (b is Inserter ins)
            {
                // base plate (baked), then a REAL arm: pivot at the center,
                // claw sweeping source -> sink, carrying the item icon
                DrawBuildingSprite(g, b, dest);

                var back = DirU.Opposite(ins.Face);
                int reach = ins.Reach;
                float srcX = b.X + 0.5f + DirU.Dx[(int)back] * reach;
                float srcY = b.Y + 0.5f + DirU.Dy[(int)back] * reach;
                float snkX = b.X + 0.5f + DirU.Dx[(int)ins.Face];
                float snkY = b.Y + 0.5f + DirU.Dy[(int)ins.Face];

                float t = ins.Arm;
                t = t * t * (3f - 2f * t);                 // smoothstep ease
                float cxw = srcX + (snkX - srcX) * t;
                float cyw = srcY + (snkY - srcY) * t;

                var piv = Camera.S(b.X + 0.5f, b.Y + 0.5f);
                var claw = Camera.S(cxw, cyw);

                // two-segment arm: shoulder -> elbow -> claw
                float ex = piv.X + (claw.X - piv.X) * 0.55f;
                float ey = piv.Y + (claw.Y - piv.Y) * 0.55f - sz * 0.18f;   // raised elbow
                using (var arm = new Pen(Pal.C(210, 205, 190), Math.Max(1.5f, sz * 0.09f)))
                {
                    arm.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    arm.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLine(arm, piv.X, piv.Y - sz * 0.1f, ex, ey);
                    g.DrawLine(arm, ex, ey, claw.X, claw.Y);
                }
                // claw
                using (var clawB = new SolidBrush(Pal.C(235, 190, 80)))
                    g.FillEllipse(clawB, claw.X - sz * 0.14f, claw.Y - sz * 0.14f, sz * 0.28f, sz * 0.28f);

                if (ins.Held is ItemKind hk)
                    DrawItemDot(g, cxw, cyw - 0.12f, hk, sz);
                if (ins.Filter is ItemKind fk)
                    DrawItemDot(g, b.X + 0.28f, b.Y + 0.28f, fk, sz * 0.8f);
                continue;
            }
            if (b is Pipe pipe)
            {
                DrawBuildingSprite(g, b, dest);
                DrawPipeLinks(g, game, pipe, sz);
                continue;
            }
            if (b is TrainStop st2)
            {
                DrawBuildingSprite(g, b, dest);
                if (Camera.Zoom >= 0.75f)
                {
                    Text(g, st2.StationName, FSmall, Pal.Text, dest.X + dest.Width / 2, dest.Y - 10, center: true);
                    Text(g, $"{st2.InBuffer.Count}in/{st2.OutBuffer.Count}out", FSmall, Pal.TextDim,
                        dest.X + dest.Width / 2, dest.Bottom + 1, center: true);
                }
                continue;
            }
            if (b is DronePort dp)
            {
                DrawBuildingSprite(g, b, dest);
                if (Camera.Zoom >= 0.75f)
                    Text(g, $"\u2042{dp.Drones}", FBold, Pal.C(230, 230, 160), dest.Right - 10, dest.Y + 2);
                DrawCoverDot(g, b, sz, Bal.DronePortCover, Pal.C(230, 230, 160));
                continue;
            }
            if (b is Pump || b is Tank || b is Boiler || b is SteamEngine)
            {
                DrawBuildingSprite(g, b, dest);
                if (Camera.Zoom >= 0.8f)
                {
                    if (b is Tank || b is Pump || b is SteamEngine)
                    {
                        var net = game.FluidOf(b);
                        if (net != null && net.Kind != FluidKind.None)
                        {
                            var col = net.Kind == FluidKind.Steam ? Pal.Text : Pal.Water;
                            Text(g, $"{(int)(net.Fill01 * 100)}", FSmall, col, dest.X + dest.Width / 2, dest.Y + 1, center: true);
                        }
                    }
                    if (b is Boiler bo && bo.Fuel > 0.1f)
                        Text(g, $"fuel {(int)bo.Fuel}", FSmall, Pal.Warn, dest.X + dest.Width / 2, dest.Bottom - 11, center: true);
                    if (b is SteamEngine se && se.CurrentOutput > 1f)
                        Text(g, $"+{(int)se.CurrentOutput}", FSmall, Pal.Good, dest.X + dest.Width / 2, dest.Bottom - 11, center: true);
                }
                continue;
            }
            if (b is BotFactory bf)
            {
                DrawBuildingSprite(g, b, dest);
                Text(g, $"{bf.Deployed}/{Bal.BotCap}", FSmall, Pal.BotCol, dest.X + dest.Width / 2, dest.Y - 10, center: true);
                // rally flag
                var rp = Camera.S(bf.RallyX + 0.5f, bf.RallyY + 0.5f);
                if (bf.RallyX != bf.X || bf.RallyY != bf.Y)
                {
                    g.DrawLine(Pal.P(Pal.CA(160, Pal.BotCol), 1.4f),
                        dest.X + dest.Width / 2, dest.Y + dest.Height / 2, rp.X, rp.Y);
                    g.DrawEllipse(Pal.P(Pal.BotCol, 1.6f), rp.X - 5, rp.Y - 5, 10, 10);
                }
                continue;
            }
            if (b is PowerPole)
            {
                DrawBuildingSprite(g, b, dest);
                continue;
            }

            // generic (machines, turrets, walls, colony...)
            float lx = 0, ly = 0;
            if (b is Turret tr && tr.LungeT > 0)
            {
                float k = MathF.Sin(MathF.PI * (1 - tr.LungeT / 0.14f)) * sz * 0.09f;
                lx = tr.LungeDx * k; ly = tr.LungeDy * k;
            }
            if (b is Watchtower wt && wt.LungeT > 0)
            {
                float k = MathF.Sin(MathF.PI * (1 - wt.LungeT / 0.14f)) * sz * 0.05f;
                lx = wt.LungeDx * k; ly = wt.LungeDy * k;
            }
            dest.Offset((int)lx, (int)ly);

            DrawBuildingSprite(g, b, dest);

            // Mindustry-style item IO has no fixed machine port now:
            // adjacent belts/buildings define input vs output by what they accept.

            if (b is WindTurbine)
            {
                int frame = (int)(game.Time * 9) % 3;
                g.DrawImage(Sprites.WindRotor[frame], dest);
            }
            if (b is Battery)
            {
                var gr = game.GridOf(b);
                float frac = gr != null && gr.Cap > 0 ? gr.Stored / gr.Cap : 0;
                int pips = (int)(frac * 4 + 0.5f);
                for (int i = 0; i < pips; i++)
                {
                    var pp = Camera.S(b.X + 0.31f, b.Y + 0.27f + i * 0.14f);
                    g.FillRectangle(Pal.B(Pal.C(150, 230, 150)), pp.X, pp.Y - sz * 0.05f, sz * 0.42f, sz * 0.09f);
                }
            }
            if (b is StorageSilo silo)
            {
                DrawBuildingSprite(g, b, dest);
                if (silo.StoredKind is ItemKind sk)
                {
                    DrawItemDot(g, b.X + b.W / 2f, b.Y + b.H / 2f, sk, sz * 1.2f);
                    Text(g, $"{silo.N}", FBold, Pal.Text, dest.Right - 6, dest.Y + 4);
                }
                continue;
            }
            if (b is StorageCrate sc && sc.Items.Count > 0 && Camera.Zoom >= 0.8f)
                Text(g, sc.Items.Count.ToString(), FSmall, Pal.Text, dest.X + dest.Width - 8, dest.Y + 4);

            if (b is MachineBase m)
            {
                if (m.Busy)
                {
                    float w = sz * 0.8f;
                    float bx = dest.X + sz * 0.1f, by = dest.Bottom - sz * 0.16f;
                    g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), bx, by, w, 3);
                    g.FillRectangle(Pal.B(Pal.Accent), bx, by, w * Math.Min(1, m.Craft01), 3);
                }
                bool needsOp = m.NeedsWorker && m.Operator == null &&
                    (!game.Has(Tech.Automation) || m is Lab or Watchtower);
                if (needsOp) Badge(g, dest, "!", Pal.Warn);
            }

            if (b is Turret tu)
            {
                float cx = dest.X + dest.Width / 2f, cy = dest.Y + dest.Height / 2f;
                float dx = DirU.Dx[(int)tu.Face], dy = DirU.Dy[(int)tu.Face];
                if (tu.Target != null)
                {
                    dx = tu.Target.PosX - (tu.X + .5f); dy = tu.Target.PosY - (tu.Y + .5f);
                    float len = MathF.Sqrt(dx * dx + dy * dy);
                    if (len > 0.01f) { dx /= len; dy /= len; }
                }
                float thick = tu is HeavyTurret ? 3.4f : 2.2f;
                g.DrawLine(Pal.P(Pal.C(228, 234, 240), thick), cx, cy,
                    cx + dx * dest.Width * 0.46f, cy + dy * dest.Width * 0.46f);
                if (tu is HeavyTurret)
                    g.DrawLine(Pal.P(Pal.C(228, 234, 240), thick), cx - dy * 3, cy + dx * 3,
                        cx - dy * 3 + dx * dest.Width * 0.42f, cy + dx * 3 + dy * dest.Width * 0.42f);
            }

            if (b is ShieldGen sg)
            {
                // bubble
                float frac = Math.Max(0, sg.ShieldHp / Bal.ShieldHp);
                if (frac > 0)
                {
                    var c = Camera.S(b.X + 0.5f, b.Y + 0.5f);
                    float r = Bal.ShieldRadius * sz;
                    var col = frac > 0.5 ? Pal.Accent : Pal.Warn;
                    using var pen = new Pen(Pal.CA((int)(150 * frac + 40), col), 2f);
                    g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
                    g.FillEllipse(Pal.B(Pal.CA((int)(16 * frac), col)), c.X - r, c.Y - r, r * 2, r * 2);
                    Text(g, $"{(int)sg.ShieldHp}", FSmall, col, c.X, c.Y - r - 10, center: true);
                }
            }

            if (b.PowerDemand > 0 && game.PowerFracFor(b) < 0.55f)
                DrawBolt(g, dest, game.PowerFracFor(b) <= 0.02f ? Pal.Bad : Pal.Warn);

            if (b.Hp < b.MaxHp)
            {
                float w = dest.Width * 0.8f;
                float bx = dest.X + dest.Width * 0.1f, by = dest.Y + 2;
                g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), bx, by, w, 3);
                g.FillRectangle(Pal.B(b.Hp / b.MaxHp > 0.4f ? Pal.Good : Pal.Bad),
                    bx, by, w * Math.Max(0, b.Hp / b.MaxHp), 3);
            }
        }

        foreach (var (lane, ix, iy, ifc, ibend) in itemPass)
                DrawBeltItems(g, lane, ix, iy, ifc, ibend, sz);

            g.InterpolationMode = oldInterp;
    }

    /// <summary>Elevated rails draw above everything on the ground plane.</summary>
    private static void DrawElevated(Graphics g, Game game, Size client)
    {
        float sz = Camera.Sz;
        var old = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        foreach (var b in game.Builds)
        {
            if (b.Kind != BuildKind.ElevatedRail) continue;
            var p = Camera.S(b.X, b.Y);
            if (p.X > client.Width || p.Y > client.Height || p.X + sz < 0 || p.Y + sz < 0) continue;
            var dest = new Rectangle((int)p.X, (int)p.Y - (int)(sz * 0.22f), (int)(sz + 1), (int)(sz + 1));
            DrawSpriteSmart(g, Sprites.ElevatedRail[(int)b.Face], dest);
            // shadow to sell the height
            g.FillRectangle(Pal.B(Pal.CA(60, Pal.C(0, 0, 0))), (int)p.X + 2, (int)p.Y + (int)(sz * 0.55f), (int)(sz * 0.7f), 3);
        }
        g.InterpolationMode = old;
    }

    private static void DrawPipeLinks(Graphics g, Game game, Pipe p, float sz)
    {
        var net = game.FluidOf(p);
        var c = Camera.S(p.X + 0.5f, p.Y + 0.5f);
        // connections
        for (int d = 0; d < 4; d++)
        {
            var (nx, ny) = DirU.Step(p.X, p.Y, (Dir)d);
            if (!game.World.InBounds(nx, ny)) continue;
            if (game.World.Cell(nx, ny).B is Pipe or Tank or Boiler or SteamEngine or Pump)
            {
                g.DrawLine(Pal.P(Pal.C(104, 122, 128), sz * 0.22f), c.X, c.Y,
                    c.X + DirU.Dx[d] * sz * 0.5f, c.Y + DirU.Dy[d] * sz * 0.5f);
            }
        }
        // fluid fill dot
        if (net != null && net.Kind != FluidKind.None)
        {
            var col = net.Kind == FluidKind.Steam ? Pal.C(235, 240, 245) : Pal.C(60, 140, 220);
            float r = sz * (0.10f + 0.13f * net.Fill01);
            g.FillEllipse(Pal.B(col), c.X - r, c.Y - r, r * 2, r * 2);
        }
    }

    private static void DrawFacingTick(Graphics g, Building b, float sz)
    {
        var c = Camera.S(b.X + 0.5f, b.Y + 0.5f);
        float ox = DirU.Dx[(int)b.Face] * sz * 0.42f;
        float oy = DirU.Dy[(int)b.Face] * sz * 0.42f;
        g.FillEllipse(Pal.B(Pal.Accent), c.X + ox - sz * 0.07f, c.Y + oy - sz * 0.07f, sz * 0.14f, sz * 0.14f);
    }

    private static void DrawCoverDot(Graphics g, Building b, float sz, float radius, Color col)
    {
        // subtle coverage hint (small corner dot — full circles live in overlays)
        var p = Camera.S(b.X + 0.5f, b.Y + 0.5f);
        g.DrawEllipse(new Pen(Pal.CA(60, col), 1f), p.X - radius * sz, p.Y - radius * sz, radius * 2 * sz, radius * 2 * sz);
    }

    private static void Badge(Graphics g, RectangleF r, string s, Color col)
    {
        float d = Math.Max(13, r.Width * 0.36f);
        g.FillEllipse(Pal.B(col), r.Right - d * 0.7f, r.Y - d * 0.3f, d, d);
        Text(g, s, FBold, Pal.C(20, 24, 28), r.Right - d * 0.7f + d / 2, r.Y - d * 0.3f + d / 2 - 1, center: true);
    }

    private static void DrawBolt(Graphics g, RectangleF r, Color col)
    {
        float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.12f;
        var pts = new[]
        {
            new PointF(cx - 2.6f, cy - 5f),
            new PointF(cx + 2.8f, cy - 5f),
            new PointF(cx + 0.2f, cy - 1.2f),
            new PointF(cx + 2.8f, cy - 1.2f),
            new PointF(cx - 1.4f, cy + 5.4f),
            new PointF(cx + 0.1f, cy + 0.6f),
            new PointF(cx - 2.6f, cy + 0.6f),
        };
        g.FillPolygon(Pal.B(col), pts);
        g.DrawPolygon(Pal.P(Pal.C(15, 18, 22), 1f), pts);
    }

    private static void DrawItemDot(Graphics g, float wx, float wy, ItemKind k, float sz)
    {
        var ip = Camera.S(wx, wy);
        // art pass: real item sprites once tiles are big enough to read them
        if (sz >= 14f)
        {
            int px = Math.Max(6, (int)MathF.Round(sz * 0.56f / 2f) * 2);   // quantized
            var icon = Sprites.ItemIconScaled(k, px);
            g.DrawImage(icon, ip.X - px / 2f, ip.Y - px / 2f, px, px);
            return;
        }
        float r = sz * 0.14f;
        g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), ip.X - r - 1, ip.Y - r - 1, r * 2 + 2, r * 2 + 2);
        g.FillRectangle(Pal.B(Pal.ItemColor(k)), ip.X - r, ip.Y - r, r * 2, r * 2);
    }

    private static void DrawBeltItems(Graphics g, List<BeltItem> lane, int bx, int by, Dir face, Dir? bend, float sz)
    {
        foreach (var it in lane)
        {
            // Correct entry animation: start at the actual side the item came
            // from, arrive at center halfway, then leave along the belt face.
            // Per-item Entry wins over BendIn so both-side merge belts don't
            // force every item through the same side animation.
            var off = Bal.BeltItemOffset(face, bend, it.Entry, it.Prog);
            DrawItemDot(g, bx + 0.5f + off.X, by + 0.5f + off.Y, it.Kind, sz);
        }
    }

    private static void DrawHub(Graphics g, Hub hub, Rectangle dest, Game game)
    {
        if (Sprites.Hub.Width > Sprites.S * 3)
        {
            var oi = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(Sprites.Hub, dest);
            g.InterpolationMode = oi;
        }
        else g.DrawImage(Sprites.Hub, dest);
        if (Camera.Zoom >= 0.65f)
        {
            Text(g, $"PARTS {hub.Stock[(int)ItemKind.AdvPart]}/{Bal.PartsToWin}", FBold, Pal.Text,
                dest.X + dest.Width / 2, dest.Y + dest.Height * 0.62f, center: true);

            var gr0 = game.GridOf(hub);
            if (gr0 != null)
            {
                var col = gr0.Frac >= 0.999f ? Pal.Good : gr0.Frac > 0.5f ? Pal.Warn : Pal.Bad;
                Text(g, $"{(int)gr0.Supply}pwr", FSmall, col, dest.X + dest.Width / 2, dest.Y + 6, center: true);
            }
            // brig count
            int pris = game.Prisoners.Count;
            if (pris > 0)
                Text(g, $"brig: {pris}", FSmall, Pal.Warn, dest.X + dest.Width / 2, dest.Y - 8, center: true);
        }

        float w = dest.Width * 0.85f;
        float bx = dest.X + dest.Width * 0.075f, by = dest.Bottom - 8;
        g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), bx, by, w, 4);
        float frac = Math.Max(0, hub.Hp / hub.MaxHp);
        g.FillRectangle(Pal.B(frac > 0.4f ? Pal.Good : Pal.Bad), bx, by, w * frac, 4);
    }

    // ------------------------------------------------------------ units ---

    private static void DrawTrains(Graphics g, Game game)
    {
        float sz = Camera.Sz;
        foreach (var t in game.Trains)
        {
            var p = Camera.S(t.PosX, t.PosY);
            float w = sz * 1.4f;
            var st = g.Save();
            g.TranslateTransform(p.X, p.Y);
            g.RotateTransform(t.Angle * 180f / MathF.PI);
            var old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(Sprites.TrainImg, -w / 2, -w / 4, w, w / 2);
            g.InterpolationMode = old;
            g.Restore(st);
            if (t.CargoCount > 0 && Camera.Zoom >= 0.75f)
                Text(g, t.CargoCount.ToString(), FSmall, Pal.Text, p.X, p.Y - sz * 0.55f, center: true);
        }
    }

    private static void DrawBots(Graphics g, Game game)
    {
        float sz = Camera.Sz;
        foreach (var b in game.Bots)
        {
            var p = Camera.S(b.X, b.Y);
            float w = sz * 0.8f;
            float bob = MathF.Sin(b.WalkPhase) * sz * 0.05f;
            var st = g.Save();
            g.TranslateTransform(p.X, p.Y + bob);
            g.RotateTransform(b.FaceAngle * 180f / MathF.PI + 90f);
            var old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(Sprites.GuardBotImg, -w / 2, -w / 2, w, w);
            g.InterpolationMode = old;
            g.Restore(st);

            if (b.Hp < Bal.BotHp)
            {
                float bw = w * 0.8f;
                g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), p.X - bw / 2, p.Y - w / 2 - 5, bw, 3);
                g.FillRectangle(Pal.B(Pal.BotCol), p.X - bw / 2, p.Y - w / 2 - 5, bw * (b.Hp / Bal.BotHp), 3);
            }
        }
    }

    private static void DrawPrisoners(Graphics g, Game game)
    {
        float sz = Camera.Sz;
        foreach (var p in game.Prisoners)
        {
            var s = Camera.S(p.X, p.Y);
            // grey pawn-ish blob with bars
            g.FillEllipse(Pal.B(Pal.C(90, 96, 104)), s.X - sz * 0.3f, s.Y - sz * 0.34f, sz * 0.6f, sz * 0.68f);
            using var bars = new Pen(Pal.C(30, 34, 40), 1.6f);
            for (int i = 0; i < 3; i++)
                g.DrawLine(bars, s.X - sz * 0.26f + i * sz * 0.18f, s.Y - sz * 0.3f,
                    s.X - sz * 0.26f + i * sz * 0.18f, s.Y + sz * 0.32f);
            var rc = p.Fed ? Pal.TextDim : Pal.Bad;
            if (Camera.Zoom >= 0.7f)
                Text(g, Fit(g, $"{p.Name} {(int)(p.Recruit * 100)}%", FSmall, sz * 2.4f), FSmall, rc,
                    s.X, s.Y + sz * 0.44f, center: true);
        }
    }

    // ------------------------------------------------------------- pawns ---

    private static void DrawColonists(Graphics g, Game game, ViewState v)
    {
        float sz = Camera.Sz;
        int ci = 0;
        foreach (var c in game.Cols)
        {
            if (c.Hp <= 0) continue;
            // LANDING: pawns pop out of the hub one by one after touchdown
            if (v.Landing01 > 0f && ci >= v.LandingPawnsOut) { ci++; continue; }
            ci++;

            float ox = 0, oy = 0;
            if (c.LungeT > 0)
            {
                float k = MathF.Sin(MathF.PI * (1 - c.LungeT / 0.16f)) * sz * 0.20f;
                ox = c.LungeDx * k; oy = c.LungeDy * k;
            }
            if (c.Path != null && c.PathIdx < (c.Path?.Count ?? 0))
                oy += MathF.Abs(MathF.Sin(c.BobT)) * -sz * 0.04f;

            var p = Camera.S(c.PosX, c.PosY);
            float w = sz * 1.06f;
            var dest = new Rectangle((int)(p.X - w / 2 + ox), (int)(p.Y - w * 0.62f + oy), (int)w, (int)w);

            bool sleeping = c.State is ColState.Sleeping or ColState.Heal;
            var sprite = Sprites.Pawn(c.ShirtTone, c.SkinTone, c.HairTone, c.BeltTone,
                sleeping ? Dir.Up : c.FaceDir);

            bool selected = v.Sel.Col == c || v.Sel.Cols.Contains(c);
            if (selected)
            {
                var ringCol = c.Drafted ? Pal.Bad : Pal.Accent;
                // Use a fresh pen instead of a cached/shared pen to avoid drawing with
                // a possibly-disposed cached resource. Also use float literals.
                using var ringPen = new Pen(ringCol, 2f);
                g.DrawEllipse(ringPen, dest.X - 3f, dest.Bottom - w * 0.28f, w + 6f, w * 0.3f);
                if (c.Drafted)
                {
                    float cx2 = dest.X + w / 2f, cy2 = dest.Y - 4;
                    using var pen2 = new Pen(Pal.Bad, 2f);
                    g.DrawLine(pen2, cx2 - 4, cy2 + 4, cx2, cy2);
                    g.DrawLine(pen2, cx2, cy2, cx2 + 4, cy2 + 4);
                }
            }

            var oldInterp = g.InterpolationMode;
            // pawns are 3x-supersampled now: bilinear keeps them smooth at
            // every zoom instead of magnifying the pixel grid
            g.InterpolationMode = InterpolationMode.Bilinear;
            if (sleeping || c.State is ColState.Escort or ColState.GoCapture && c.CaptureTarget != null)
                DrawImageAlpha(g, sprite, dest, 0.9f);
            else g.DrawImage(sprite, dest);
            g.InterpolationMode = oldInterp;

            if (c.FlashT > 0)
            {
                int a = (int)(c.FlashT / 0.15f * 150);
                g.FillEllipse(Pal.B(Pal.CA(a, Pal.C(255, 255, 255))), dest.X, dest.Y, w, w);
            }

            // PAWN INVENTORY: little cargo bundle on the shoulder
            if (c.CarryN > 0 && w >= 12)
            {
                int px = Math.Max(5, (int)(w * 0.48f));
                var icon = Sprites.ItemIconScaled((ItemKind)c.CarryKind, px);
                g.DrawImage(icon, dest.Right - px, dest.Y - px / 2, px, px);
            }

            Color mc = c.Morale > 60 ? Pal.Good : c.Morale > 30 ? Pal.Warn : Pal.Bad;
            // Fresh pen to avoid using shared/disposed Pen from Pal.P
            using (var arcPen = new Pen(mc, 1.6f))
                g.DrawArc(arcPen, p.X - sz * 0.42f + ox, p.Y - sz * 0.42f + oy,
                    sz * 0.84f, sz * 0.84f, -90, c.Morale / 100f * 360);

            string? glyph = c.State switch
            {
                ColState.Sleeping => "Z",
                ColState.Heal => "+",
                ColState.Flee => "!",
                ColState.Eating => "~",
                ColState.GoCapture => "\u2691",
                ColState.Escort => "\u2691",
                _ => c.InFlow ? "*" : null,
            };
            if (glyph != null)
                Text(g, glyph, FBold, c.State is ColState.Heal or ColState.GoCapture or ColState.Escort ? Pal.Good : mc,
                    dest.X + dest.Width / 2, dest.Y - 9, center: true);

            if (Camera.Zoom >= 0.85f || selected)
                Text(g, c.Name, FSmall, Pal.Text, dest.X + dest.Width / 2, dest.Bottom + 2, center: true);
        }
    }

    private static void DrawRaiders(Graphics g, Game game, Size client)
    {
        float sz = Camera.Sz;
        foreach (var r in game.Foes)
        {
            if (r.Hp <= 0) continue;
            if (!game.World.RevealedAt((int)r.PosX, (int)r.PosY)) continue;

            var p = Camera.S(r.PosX, r.PosY);
            float w = sz * (r.Apex ? 1.5f : 1.0f);

            if (r.Downed)
            {
                // lying flat, greyed out
                var st0 = g.Save();
                g.TranslateTransform(p.X, p.Y);
                g.RotateTransform(r.FaceAngle * 180f / MathF.PI);
                var old0 = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                DrawImageAlpha(g, r.Apex ? Sprites.RaiderApex : Sprites.RaiderImg,
                    new Rectangle((int)(-w / 2), (int)(-w / 4), (int)w, (int)w / 2), 0.55f);
                g.InterpolationMode = old0;
                g.Restore(st0);
                if (Camera.Zoom >= 0.7f)
                    Text(g, "DOWNED — click to capture", FSmall, Pal.Warn, p.X, p.Y - w, center: true);
                continue;
            }

            float ox = 0, oy = 0;
            if (r.LungeT > 0)
            {
                float k = MathF.Sin(MathF.PI * (1 - r.LungeT / 0.18f)) * sz * 0.24f;
                ox = r.LungeDx * k; oy = r.LungeDy * k;
            }
            ox += MathF.Sin(r.WalkPhase) * sz * 0.03f;

            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var oldInterp = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.TranslateTransform(p.X + ox, p.Y + oy);
            g.RotateTransform(r.FaceAngle * 180f / MathF.PI + 90f);
            g.DrawImage(r.Apex ? Sprites.RaiderApex : Sprites.RaiderImg, -w / 2, -w / 2, w, w);
            g.Restore(state);
            g.InterpolationMode = oldInterp;

            if (r.FlashT > 0)
            {
                int a = (int)(r.FlashT / 0.12f * 160);
                g.FillEllipse(Pal.B(Pal.CA(a, Pal.C(255, 255, 255))), p.X + ox - w / 2, p.Y + oy - w / 2, w, w);
            }

            if (r.Hp < r.MaxHp)
            {
                float bw = w * 0.8f;
                g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), p.X - bw / 2, p.Y - w / 2 - 6, bw, 3);
                g.FillRectangle(Pal.B(Pal.Bad), p.X - bw / 2, p.Y - w / 2 - 6, bw * (r.Hp / r.MaxHp), 3);
            }
        }
    }

    private static void DrawFx(Graphics g, Game game)
    {
        foreach (var p in game.Fx)
        {
            float a = Math.Max(0, p.Ttl / p.Max);
            var col = Pal.CA((int)(210 * a), p.Col);
            if (p.Kind == 0)
            {
                var s = Camera.S(p.Ax, p.Ay);
                var e = Camera.S(p.Bx, p.By);
                g.DrawLine(Pal.P(col, 2f), s, e);
            }
            else if (p.Kind == 1)
            {
                var s = Camera.S(p.Ax, p.Ay);
                g.FillEllipse(Pal.B(col), s.X - 3, s.Y - 3, 6, 6);
            }
            else if (p.Kind == 2)
            {
                var s = Camera.S(p.Ax, p.Ay);
                float r = (1 - a) * 26 + 4;
                g.DrawEllipse(Pal.P(col, 2f), s.X - r, s.Y - r, r * 2, r * 2);
            }
            else
            {
                // drone flight: dot moving from A to B with a fading trail
                float t = 1 - a;
                float x = p.Ax + (p.Bx - p.Ax) * t;
                float y = p.Ay + (p.By - p.Ay) * t - MathF.Sin(t * MathF.PI) * 0.8f; // hop arc
                var s = Camera.S(x, y);
                g.FillEllipse(Pal.B(col), s.X - 4, s.Y - 4, 8, 8);
                var tail = Camera.S(p.Ax + (p.Bx - p.Ax) * MathF.Max(0, t - 0.12f),
                                    p.Ay + (p.By - p.Ay) * MathF.Max(0, t - 0.12f));
                g.DrawLine(Pal.P(col, 1.6f), tail, s);
            }
        }
    }

    private static void DrawGhost(Graphics g, Game game, ViewState v)
    {
        float sz = Camera.Sz;

        // power pole placement preview: the coverage circle it WILL provide,
        // plus dashed wires to the poles/hubs it would link to (green = will
        // join the grid, amber = isolated pole)
        if (v.ToolBuilding == BuildKind.PowerPole)
        {
            var gc = Camera.S(v.GhostX + 0.5f, v.GhostY + 0.5f);
            float gr = Bal.PoleCover * sz;
            bool linked = false;
            foreach (var o in game.Builds)
            {
                if (o is not (PowerPole or Hub)) continue;
                float dx = o.X - v.GhostX, dy = o.Y - v.GhostY;
                if (dx * dx + dy * dy > Bal.PoleWire * Bal.PoleWire) continue;
                linked = true;
                var op = Camera.S(o.X + o.W / 2f, o.Y + o.H / 2f - 0.35f);
                using var wire = new Pen(Pal.CA(190, Pal.Wire), 1.6f) { DashStyle = DashStyle.Dash };
                g.DrawLine(wire, gc, op);
            }
            var col = linked ? Pal.Good : Pal.Warn;
            using var pen = new Pen(Pal.CA(150, col), 1.8f) { DashStyle = DashStyle.Dash };
            g.DrawEllipse(pen, gc.X - gr, gc.Y - gr, gr * 2, gr * 2);
            g.FillEllipse(Pal.B(Pal.CA(10, col)), gc.X - gr, gc.Y - gr, gr * 2, gr * 2);
        }

        // MADDOG iter-4: defense placement preview - the range ring it WILL cover
        float ring = v.ToolBuilding switch
        {
            BuildKind.Turret => game.Has(Tech.Optics) ? Bal.TurretRangeUp : Bal.TurretRange,
            BuildKind.HeavyTurret => game.Has(Tech.Optics) ? Bal.HeavyRangeUp : Bal.HeavyRange,
            BuildKind.Watchtower => Bal.TowerRange,
            _ => 0f,
        };
        if (ring > 0f)
        {
            var rc = Camera.S(v.GhostX + 0.5f, v.GhostY + 0.5f);
            float rr = ring * sz;
            using var rpen = new Pen(Pal.CA(150, Pal.Bad), 1.8f) { DashStyle = DashStyle.Dash };
            g.DrawEllipse(rpen, rc.X - rr, rc.Y - rr, rr * 2, rr * 2);
            g.FillEllipse(Pal.B(Pal.CA(10, Pal.Bad)), rc.X - rr, rc.Y - rr, rr * 2, rr * 2);
        }

        var p = Camera.S(v.GhostX, v.GhostY);
        // multiblock ghosts (drills are 2x2) show their real footprint
        float gw = sz + 1, gh = sz + 1;
        if (v.ToolBuilding is BuildKind bk)
        {
            gw = Bal.FootW(bk) * sz + 1;
            gh = Bal.FootH(bk) * sz + 1;
        }
        var dest = new Rectangle((int)p.X, (int)p.Y, (int)gw, (int)gh);

        if (v.ToolBuilding is BuildKind kind)
        {
            Bitmap bmp = kind switch
            {
                BuildKind.Belt => Sprites.Belt[(int)v.ToolFacing],
                BuildKind.FastBelt => Sprites.FastBelt[(int)v.ToolFacing],
                BuildKind.Rail => Sprites.Rail[(int)v.ToolFacing],
                BuildKind.ElevatedRail => Sprites.ElevatedRail[(int)v.ToolFacing],
                BuildKind.Hub => Sprites.Hub,
                _ => Sprites.Building(kind),
            };
            var d2 = kind == BuildKind.Hub
                ? new Rectangle(dest.X, dest.Y, dest.Width * 3, dest.Height * 3) : dest;
            var old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(bmp, d2, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, _ghostAlpha);
            g.InterpolationMode = old;
        }

        var tint = v.GhostValid ? Pal.GhostOk : Pal.GhostBad;
        g.FillRectangle(Pal.B(Pal.CA(46, tint)), dest);
        g.DrawRectangle(Pal.P(tint, 1.5f), dest.X, dest.Y, dest.Width, dest.Height);

        if (v.ToolBuilding is BuildKind k2 && Rotatable(k2))
        {
            float cx = dest.X + dest.Width / 2f, cy = dest.Y + dest.Height / 2f;
            float dx = DirU.Dx[(int)v.ToolFacing], dy = DirU.Dy[(int)v.ToolFacing];
            var tip = new PointF(cx + dx * sz * 0.46f, cy + dy * sz * 0.46f);
            var a1 = new PointF(cx + dx * sz * 0.18f - dy * sz * 0.20f, cy + dy * sz * 0.18f - dx * sz * 0.20f);
            var a2 = new PointF(cx + dx * sz * 0.18f + dy * sz * 0.20f, cy + dy * sz * 0.18f + dx * sz * 0.20f);
            g.FillPolygon(Pal.B(Pal.CA(235, tint)), new[] { tip, a1, a2 });
            g.DrawPolygon(Pal.P(Pal.C(10, 12, 14), 1.4f), new[] { tip, a1, a2 });
            Text(g, "R ↻", FBold, Pal.Text, dest.Right - 4, dest.Bottom - 20);
        }

        if (!v.GhostValid && v.GhostReason.Length > 0)
        {
            string reason = Fit(g, v.GhostReason, FSmall, 280);
            var size = g.MeasureString(reason, FSmall);
            float vw = g.VisibleClipBounds.Width;
            float gx = Math.Min(v.Mouse.X + 12, Math.Max(4, vw - size.Width - 16));
            g.FillRectangle(Pal.B(Pal.CA(220, Pal.Panel)), gx, v.Mouse.Y + 8, size.Width + 10, 18);
            Text(g, reason, FSmall, Pal.GhostBad, gx + 5, v.Mouse.Y + 11);
        }
    }

    // ------------------------------------------------------------ night ----

    private static void DrawNight(Graphics g, Game game, ViewState v, Size client)
    {
        // dusk veil over the ground layer (buildings draw above it, self-lit)
        int alpha = (int)((1f - v.Sunlight) * 150);
        g.FillRectangle(Pal.B(Pal.CA(alpha, Pal.C(6, 9, 20))), 0, 0, client.Width, client.Height);

        // smooth light pools: one cached radial-gradient bitmap per color,
        // scaled per light - no more stacked hard-edged ellipses
        float strength = 1f - v.Sunlight;
        float t = Environment.TickCount / 1000f;

        void Glow(float wx, float wy, float radius, Color col, bool flicker = false)
        {
            float r = radius;
            if (flicker)
                r *= 1f + 0.045f * MathF.Sin(t * 7f + wx * 3.1f + wy * 5.7f);
            var c = Camera.S(wx, wy);
            float px = r * Camera.Sz;
            var glow = LightGlow(col);
            using var ia = BakedAlpha(strength);
            var dest = new Rectangle((int)(c.X - px), (int)(c.Y - px), (int)(px * 2), (int)(px * 2));
            g.DrawImage(glow, dest, 0, 0, glow.Width, glow.Height, GraphicsUnit.Pixel, ia);
        }

        foreach (var b in game.Builds)
        {
            if (b is Lamp && game.PowerFracFor(b) > 0.3f)
                Glow(b.X + 0.5f, b.Y + 0.5f, 2.6f, Pal.C(255, 220, 140), flicker: true);
            else if (b is Hub)
                Glow(b.X + 1.5f, b.Y + 1.5f, 4.2f, Pal.C(120, 200, 255));
            else if (b is TrainStop)
                Glow(b.X + 0.5f, b.Y + 0.5f, 2.1f, Pal.C(255, 220, 140));
            else if (b is DronePort && game.PowerFracFor(b) > 0.3f)
                Glow(b.X + 0.5f, b.Y + 0.5f, 2.1f, Pal.C(230, 230, 160));
        }
    }

    private static readonly Dictionary<Color, Bitmap> _glows = new();

    /// <summary>128x128 radial gradient (bright center -> fully transparent
    /// edge), baked once per color and scaled per light source.</summary>
    private static Bitmap LightGlow(Color col)
    {
        if (_glows.TryGetValue(col, out var cached)) return cached;
        const int G = 128;
        var bmp = new Bitmap(G, G);
        using (var g = Graphics.FromImage(bmp))
        {
            var path = new GraphicsPath();
            path.AddEllipse(0, 0, G, G);
            using var pgb = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(210, col),
                SurroundColors = new[] { Color.FromArgb(0, col) },
                FocusScales = new PointF(0.12f, 0.12f),
            };
            g.FillPath(pgb, path);
        }
        _glows[col] = bmp;
        return bmp;
    }

    /// <summary>Selected power pole: coverage circle + live wire links.</summary>
    private static void DrawPoleInfo(Graphics g, Game game, Building b)
    {
        var c = Camera.S(b.X + 0.5f, b.Y + 0.5f);
        float r = Bal.PoleCover * Camera.Sz;
        using var pen = new Pen(Pal.CA(140, Pal.Good), 1.6f) { DashStyle = DashStyle.Dash };
        g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
        g.FillEllipse(Pal.B(Pal.CA(10, Pal.Good)), c.X - r, c.Y - r, r * 2, r * 2);
        DrawPoleWires(g, game, b);
    }

    /// <summary>Wire links from a pole/ghost position to every pole/hub in
    /// wire range. Returns true if at least one connection exists.</summary>
    private static bool DrawPoleWires(Graphics g, Game game, Building from)
    {
        var a = Camera.S(from.X + 0.5f, from.Y + 0.5f);
        bool any = false;
        foreach (var o in game.Builds)
        {
            if (o == from || o is not (PowerPole or Hub)) continue;
            float dx = o.X - from.X, dy = o.Y - from.Y;
            if (dx * dx + dy * dy > Bal.PoleWire * Bal.PoleWire) continue;
            any = true;
            var bp = Camera.S(o.X + o.W / 2f, o.Y + o.H / 2f - 0.35f);
            using var wire = new Pen(Pal.CA(200, Pal.Wire), 1.8f);
            g.DrawLine(wire, a, bp);
        }
        return any;
    }

    // ------------------------------------------------------ map overlays ---

    private static void DrawPowerOverlay(Graphics g, Game game, Size client)
    {
        float sz = Camera.Sz;

        foreach (var gr in game.Grids)
        {
            var col = gr.Frac >= 0.999f ? Pal.Good : gr.Frac > 0.5f ? Pal.Warn : Pal.Bad;

            foreach (var a in gr.Nodes)
                foreach (var b in gr.Nodes)
                {
                    if (a.GetHashCode() >= b.GetHashCode()) continue;
                    float dx = b.X - a.X, dy = b.Y - a.Y;
                    if (dx * dx + dy * dy > Bal.PoleWire * Bal.PoleWire) continue;
                    var pa = Camera.S(a.X + a.W / 2f, a.Y + a.H / 2f - 0.35f);
                    var pb = Camera.S(b.X + b.W / 2f, b.Y + b.H / 2f - 0.35f);
                    g.DrawLine(Pal.P(Pal.CA(210, Pal.Wire), 1.8f), pa, pb);
                }

            var pen = new Pen(Pal.CA(120, col), 1.6f) { DashStyle = DashStyle.Dash };
            foreach (var n in gr.Nodes)
            {
                float cover = n is Hub ? Bal.HubCover : Bal.PoleCover;
                var c = Camera.S(n.X + n.W / 2f, n.Y + n.H / 2f);
                float r = cover * sz;
                g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
            }

            var fn = gr.Nodes[0];
            var fp = Camera.S(fn.X + fn.W / 2f, fn.Y + fn.H / 2f);
            string label = $"{(int)gr.Supply}/{(int)gr.Demand} pwr";
            if (gr.Cap > 0) label += $"  bat {(int)(gr.Stored / gr.Cap * 100)}%";
            var ls = g.MeasureString(label, FSmall);
            g.FillRectangle(Pal.B(Pal.CA(220, Pal.C(15, 18, 22))), fp.X - ls.Width / 2 - 4, fp.Y - sz * 1.1f - 12, ls.Width + 8, 16);
            Text(g, label, FSmall, col, fp.X, fp.Y - sz * 1.1f - 4, center: true);
        }

        foreach (var b in game.Builds)
        {
            bool consumer = b.PowerDemand > 0 && game.GridOf(b) == null;
            bool generator = b is Reactor or SolarPanel or WindTurbine or Battery && game.GridOf(b) == null;
            if (!consumer && !generator) continue;
            var p = Camera.S(b.X + 0.5f, b.Y + 0.1f);
            string tag = consumer ? "NO GRID" : "UNWIRED";
            var s = g.MeasureString(tag, FSmall);
            g.FillRectangle(Pal.B(Pal.CA(210, Pal.C(15, 18, 22))), p.X - s.Width / 2 - 3, p.Y - 12, s.Width + 6, 15);
            Text(g, tag, FSmall, consumer ? Pal.Bad : Pal.TextDim, p.X, p.Y - 5, center: true);
        }

        g.FillRectangle(Pal.B(Pal.CA(225, Pal.Panel)), 8, 32, 250, 66);
        g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), 8, 32, 250, 66);
        Text(g, "POWER OVERLAY  [F1]", FBold, Pal.Accent, 16, 38);
        Text(g, "green OK · amber strained · red blackout", FSmall, Pal.TextDim, 16, 56);
        Text(g, "circles = coverage · wire poles to the Hub grid", FSmall, Pal.TextDim, 16, 70);
        Text(g, "generators must sit in a coverage circle", FSmall, Pal.TextDim, 16, 84);
    }

    private static void DrawDefenseOverlay(Graphics g, Game game, Size client)
    {
        foreach (var b in game.Builds)
        {
            float range = b switch
            {
                HeavyTurret ht => ht.Range(game),
                Turret t => t.Range(game),
                Watchtower => Bal.TowerRange,
                BotFactory bf => Bal.BotAggro,
                _ => 0,
            };
            if (range <= 0) continue;
            var c = Camera.S(b.X + 0.5f, b.Y + 0.5f);
            float r = range * Camera.Sz;
            var col = b is Watchtower ? Pal.Colonist : b is BotFactory ? Pal.BotCol : Pal.Bad;
            var pen = new Pen(Pal.CA(110, col), 1.6f) { DashStyle = DashStyle.Dash };
            g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
            g.FillEllipse(Pal.B(Pal.CA(16, col)), c.X - r, c.Y - r, r * 2, r * 2);
        }
        g.FillRectangle(Pal.B(Pal.CA(225, Pal.Panel)), 8, 32, 260, 48);
        g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), 8, 32, 260, 48);
        Text(g, "DEFENSE OVERLAY  [F2]", FBold, Pal.Accent, 16, 38);
        Text(g, "dashed circles = turret / tower / bot range", FSmall, Pal.TextDim, 16, 56);
    }

    private static void DrawLogisticsOverlay(Graphics g, Game game, Size client)
    {
        float sz = Camera.Sz;
        foreach (var b in game.Builds)
        {
            Dir face; List<BeltItem> lane;
            if (b is Belt bl) { face = bl.Face; lane = bl.Lane; }
            else if (b is Junction j) { face = j.Face; lane = j.Main; }
            else continue;

            int dx = DirU.Dx[(int)face], dy = DirU.Dy[(int)face];
            for (int i = 0; i < 3; i++)
            {
                float t = 0.28f + i * 0.24f;
                var p = Camera.S(b.X + 0.5f + dx * (t - 0.5f), b.Y + 0.5f + dy * (t - 0.5f));
                var tip = new PointF(p.X + dx * sz * 0.18f, p.Y + dy * sz * 0.18f);
                var a1 = new PointF(p.X - dx * sz * 0.10f - dy * sz * 0.12f, p.Y - dy * sz * 0.10f - dx * sz * 0.12f);
                var a2 = new PointF(p.X - dx * sz * 0.10f + dy * sz * 0.12f, p.Y - dy * sz * 0.10f + dx * sz * 0.12f);
                g.FillPolygon(Pal.B(Pal.CA(190, Pal.Accent)), new[] { tip, a1, a2 });
            }
            if (lane.Count > 0)
                Text(g, lane.Count.ToString(), FSmall, Pal.Text, Camera.S(b.X, b.Y).X + 2, Camera.S(b.X, b.Y).Y + 1);
        }
        g.FillRectangle(Pal.B(Pal.CA(225, Pal.Panel)), 8, 32, 260, 48);
        g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), 8, 32, 260, 48);
        Text(g, "LOGISTICS OVERLAY  [F3]", FBold, Pal.Accent, 16, 38);
        Text(g, "arrows = belt direction · number = items in lane", FSmall, Pal.TextDim, 16, 56);
    }

    private static void DrawPollutionOverlay(Graphics g, Game game, Size client)
    {
        float sz = Camera.Sz;
        int tx0 = (int)MathF.Floor(Camera.X), ty0 = (int)MathF.Floor(Camera.Y);
        int tx1 = (int)MathF.Floor(Camera.X + client.Width / sz) + 1;
        int ty1 = (int)MathF.Floor(Camera.Y + client.Height / sz) + 1;

        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        for (int y = ty0; y <= ty1; y++)
            for (int x = tx0; x <= tx1; x++)
            {
                float p = game.World.PollAt(x, y);
                if (p <= 0.05f) continue;
                int a = (int)Math.Min(150, p / Bal.PollMaxTile * 200);
                var s = Camera.S(x, y);
                g.FillRectangle(Pal.B(Pal.CA(a, Pal.Pollution)), s.X, s.Y, sz + 1, sz + 1);
            }
        g.SmoothingMode = old;

        g.FillRectangle(Pal.B(Pal.CA(225, Pal.Panel)), 8, 32, 280, 66);
        g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), 8, 32, 280, 66);
        Text(g, "POLLUTION OVERLAY  [F4]", FBold, Pal.Accent, 16, 38);
        Text(g, "industry emits; flora absorbs; raids aim at the haze", FSmall, Pal.TextDim, 16, 56);
        Text(g, $"total pollution: {(int)game.TotalPollution}", FBold,
            game.TotalPollution > 800 ? Pal.Warn : Pal.TextDim, 16, 74);
    }

    // ------------------------------------------------------------- minimap --

    private static void DrawMinimap(Graphics g, Game game, Size client)
    {
        int w = 216, h = 140, pad = 10;
        var r = new Rectangle(client.Width - w - pad, client.Height - h - pad - 44, w, h);
        g.FillRectangle(Pal.B(Pal.CA(235, Pal.C(12, 15, 19))), r);
        g.DrawRectangle(Pal.P(Pal.C(70, 80, 92)), r.X, r.Y, r.Width, r.Height);

        // window: ~72x46 chunks centered on the camera
        float cx = Camera.X + (client.Width / Camera.Sz) / 2;
        float cy = Camera.Y + (client.Height / Camera.Sz) / 2;
        int c0x = (int)(cx / Chunk.S) - 36, c1x = (int)(cx / Chunk.S) + 36;
        int c0y = (int)(cy / Chunk.S) - 23, c1y = (int)(cy / Chunk.S) + 23;
        float px = (float)r.Width / (c1x - c0x), py = (float)r.Height / (c1y - c0y);
        px = Math.Min(px, py); py = px;
        float ox = r.X + r.Width / 2f - (c0x + c1x) / 2f * px;
        float oy = r.Y + r.Height / 2f - (c0y + c1y) / 2f * py;

        for (int cyy = c0y; cyy <= c1y; cyy++)
            for (int cxx = c0x; cxx <= c1x; cxx++)
            {
                var ch = game.World.ChunkOrNull(cxx, cyy);
                int ix = (int)(cxx * px + ox), iy = (int)(cyy * py + oy);
                int iw = (int)Math.Ceiling(px), ih = (int)Math.Ceiling(py);
                Color c;
                if (ch == null || !ch.Revealed) c = Pal.Fog;
                else
                {
                    var t = ch.Tiles[Chunk.S / 2 + Chunk.S / 2 * Chunk.S].T;
                    c = t switch
                    {
                        Terrain.IronOre => Pal.IronOre,
                        Terrain.CopperOre => Pal.CopperOre,
                        Terrain.Crystal => Pal.Crystal,
                        Terrain.Flora => Pal.Flora,
                        Terrain.Tree => Pal.C(44, 92, 56),
                        Terrain.Rock => Pal.Rock,
                        Terrain.Water => Pal.Water,
                        _ => Pal.C(43, 47, 50),
                    };
                }
                g.FillRectangle(Pal.B(c), ix, iy, iw, ih);
            }

        // buildings, pings, raiders, camera box  (world tile -> chunk float -> px)
        float MPx(float wx) => wx / Chunk.S * px + ox;
        float MPy(float wy) => wy / Chunk.S * py + oy;
        foreach (var b in game.Builds)
            g.FillRectangle(Pal.B(b is Hub ? Pal.Accent : Pal.C(200, 205, 212)),
                (int)MPx(b.X), (int)MPy(b.Y), b is Hub ? 4 : 2, b is Hub ? 4 : 2);
        foreach (var fo in game.Foes)
            g.FillRectangle(Pal.B(Pal.Enemy), (int)MPx(fo.PosX) - 1, (int)MPy(fo.PosY) - 1, 3, 3);
        foreach (var t in game.Trains)
            g.FillRectangle(Pal.B(Pal.TrainCol), (int)MPx(t.PosX) - 1, (int)MPy(t.PosY) - 1, 4, 4);

        float vw = client.Width / Camera.Sz, vh = client.Height / Camera.Sz;
        g.DrawRectangle(Pal.P(Pal.Text, 1f),
            (int)MPx(Camera.X), (int)MPy(Camera.Y),
            Math.Max(4, (int)(vw / Chunk.S * px)), Math.Max(4, (int)(vh / Chunk.S * py)));

        Text(g, "minimap [M] — click to jump", FSmall, Pal.TextDim, r.X + 6, r.Bottom + 3);
    }

    /// <summary>Screen rect of the minimap (input mapping).</summary>
    public static Rectangle MinimapRect(Size client) =>
        new(client.Width - 216 - 10, client.Height - 140 - 10 - 44, 216, 140);

    // ---------------------------------------------------------------- HUD ---

    private static void DrawTopBar(Graphics g, Game game, int width, ViewState v)
    {
        g.FillRectangle(Pal.B(Pal.CA(235, Pal.Panel)), 0, 0, width, 26);
        g.DrawLine(Pal.P(Pal.C(50, 58, 68)), 0, 26, width, 26);

        // right-anchored cluster: speed + storyteller (drawn first to claim space)
        string speed = v.Paused ? "II PAUSED" : $"{v.Speed:0.#}x";
        string rightTxt = $"[,][.] {speed} · {game.StoryName()}";
        var rw = g.MeasureString(rightTxt, FSmall).Width;
        Text(g, rightTxt, FSmall, v.Paused ? Pal.Warn : Pal.TextDim, width - 8 - rw, 7);
        float limit = width - 16 - rw;      // chips must stop before this

        float x = 8;
        bool Room(float w) => x + w <= limit;

        void Chip(string s, Color c, Font f)
        {
            var sw = g.MeasureString(s, f).Width;
            if (!Room(sw)) return;
            Text(g, s, f, c, x, 7);
            x += sw + 10;
        }

        Chip($"Day {game.Day}", Pal.Text, FBold);

        // sun/moon dial
        if (Room(20))
        {
            float sun = game.Sunlight;
            var dialCol = sun > 0.5 ? Pal.Warn : Pal.C(150, 170, 220);
            g.FillEllipse(Pal.B(dialCol), x + 2, 6, 12, 12);
            if (sun <= 0.5) g.FillRectangle(Pal.B(Pal.Panel), x + 8, 4, 10, 14); // moon crescent
            x += 20;
        }

        Chip($"Wealth {(int)game.Wealth}", Pal.TextDim, FSmall);

        // MADDOG iter-7: threat pressure meter (the player sees the next raid coming)
        float thFrac = Math.Clamp(game.Threat / Math.Max(1f, game.NextRaidAt), 0, 1);
        if (Room(120))
        {
            Color tc = thFrac > 0.75f ? Pal.Bad : thFrac > 0.4f ? Pal.Warn : Pal.Good;
            Text(g, Loc.T("THREAT"), FSmall, tc, x, 7);
            g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), x + 50, 8, 60, 9);
            g.FillRectangle(Pal.B(tc), x + 50, 8, 60 * thFrac, 9);
            g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), x + 50, 8, 60, 9);
            x += 120;
        }

        // (resources live in ONE place now: the collapsible STOCK panel,
        // top-left. The bar keeps colony stats only.)

        // power
        int badGrids = 0;
        foreach (var gr in game.Grids) if (gr.Demand > gr.Supply + 0.5f) badGrids++;
        Color pc = badGrids == 0 ? Pal.Good : Pal.Warn;
        string bat = game.BatteryFrac01 > 0 ? $" +BAT {(int)(game.BatteryFrac01 * 100)}%" : "";
        string grids = game.Grids.Count > 1 ? $" ({game.Grids.Count} grids)" : "";
        Chip($"PWR {game.PowerDemandTotal}/{game.PowerSupplyTotal}{bat}{grids}", pc, FSmall);

        // research progress
        if (game.ActiveTech != Tech.None && !game.Has(game.ActiveTech))
        {
            int i = (int)game.ActiveTech;
            int lvl = game.TechLevel(game.ActiveTech);
            float frac = Math.Clamp(game.TechProg[i] / Bal.TechCost(game.ActiveTech, lvl), 0, 1);
            if (Room(128))
            {
                Text(g, "RES", FSmall, Pal.ItemColor(ItemKind.SciencePack), x, 7);
                g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), x + 30, 8, 90, 9);
                g.FillRectangle(Pal.B(Pal.ItemColor(ItemKind.SciencePack)), x + 30, 8, 90 * frac, 9);
                g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), x + 30, 8, 90, 9);
                x += 128;
            }
        }

        // pollution
        if (game.TotalPollution > 60)
            Chip($"POLL {(int)game.TotalPollution}",
                game.TotalPollution > 800 ? Pal.Bad : Pal.Warn, FSmall);

        // threat meter
        if (game.Wealth >= Bal.GraceWealth * 0.6f && Room(116))
        {
            float frac = Math.Clamp(game.Threat / Math.Max(1, game.NextRaidAt), 0, 1);
            var tc = frac > 0.75f ? Pal.Bad : frac > 0.4f ? Pal.Warn : Pal.TextDim;
            Text(g, "THREAT", FSmall, tc, x, 7);
            g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), x + 44, 8, 60, 9);
            g.FillRectangle(Pal.B(tc), x + 44, 8, 60 * frac, 9);
            g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), x + 44, 8, 60, 9);
            x += 116;
        }

        if (v.Ovl != OverlayMode.None)
            Chip(v.Ovl.ToString().ToUpper() + " VIEW", Pal.Accent, FBold);
    }

    // ------------------------------------------------- RECLAMATION: blight

    /// <summary>Purple corruption wash over blighted chunks, keyed by chunk
    /// level so the frontier glows darker toward the deep wastes.</summary>
    private static void DrawBlight(Graphics g, Game game, Size client)
    {
        float viewW = client.Width / Camera.Sz, viewH = client.Height / Camera.Sz;
        int cx0 = (int)MathF.Floor(Camera.X / 32f) - 1, cx1 = (int)MathF.Floor((Camera.X + viewW) / 32f) + 1;
        int cy0 = (int)MathF.Floor(Camera.Y / 32f) - 1, cy1 = (int)MathF.Floor((Camera.Y + viewH) / 32f) + 1;
        for (int cx = cx0; cx <= cx1; cx++)
            for (int cy = cy0; cy <= cy1; cy++)
            {
                float lvl = game.BlightAt(cx, cy);
                if (lvl <= 0.06f) continue;
                var p0 = Camera.S(cx * 32, cy * 32);
                var p1 = Camera.S((cx + 1) * 32, (cy + 1) * 32);
                var rect = Rectangle.FromLTRB((int)p0.X, (int)p0.Y, (int)p1.X, (int)p1.Y);
                g.FillRectangle(Pal.B(Pal.CA((int)(lvl * 80), Pal.C(84, 42, 120))), rect);
                if (lvl > 0.55f && Camera.Sz >= 10)
                    using (var pen = new Pen(Pal.CA((int)(lvl * 110), Pal.C(120, 60, 160)), 1.2f))
                        for (int i = 0; i < 5; i++)
                        {
                            int hx = (cx * 7 + i * 13) % 32 * 1, hy = (cy * 11 + i * 7) % 32;
                            var s0 = Camera.S(cx * 32 + hx, cy * 32 + hy);
                            g.DrawLine(pen, s0.X, s0.Y, s0.X + Camera.Sz * 0.6f, s0.Y - Camera.Sz * 0.6f);
                        }
            }
    }

    // ------------------------------------------------------ wildlife (iter-4)

    /// <summary>Grazers + carcasses. Beasts face right in the sprite, so
    /// rotate by FaceAngle directly. Fleeing beasts get a panic tint.</summary>
    private static void DrawWildlife(Graphics g, Game game, Size client)
    {
        float sz = Camera.Sz;

        // MADDOG iter-9: the caravan (drawn under the beasts, above carcasses)
        if (game.Trader is { } tr)
        {
            var tp = Camera.S(tr.PosX, tr.PosY);
            float tw = sz * 1.4f;
            var oldT = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(Sprites.TraderImg, tp.X - tw / 2, tp.Y - tw / 2 + MathF.Sin(tr.WalkPhase) * sz * 0.02f, tw, tw);
            g.InterpolationMode = oldT;
            if (tr.Arrived && Camera.Zoom >= 0.55f)
            {
                Text(g, Loc.T("TRADE"), FSmall, Pal.Accent, tp.X, tp.Y - tw / 2 - 12, center: true);
                // window countdown ring
                float frac = Math.Clamp(tr.WindowT / Bal.TradeWindowSec, 0, 1);
                g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), tp.X - 16, tp.Y + tw / 2 + 2, 32, 4);
                g.FillRectangle(Pal.B(Pal.Accent), tp.X - 16, tp.Y + tw / 2 + 2, 32 * frac, 4);
            }
        }

        // carcasses first (they lie on the ground under everything alive)
        foreach (var car in game.Carcasses)
        {
            if (!game.World.RevealedAt((int)car.X, (int)car.Y)) continue;
            var p = Camera.S(car.X, car.Y);
            float w = sz * 0.9f;
            var oldI = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            DrawImageAlpha(g, Sprites.CarcassImg, new Rectangle((int)(p.X - w / 2), (int)(p.Y - w / 2), (int)w, (int)w),
                car.T < 60f ? 0.45f : 0.85f);      // fades as it rots
            g.InterpolationMode = oldI;
        }

        foreach (var b in game.Beasts)
        {
            if (b.Hp <= 0) continue;
            if (!game.World.RevealedAt((int)b.PosX, (int)b.PosY)) continue;

            var p = Camera.S(b.PosX, b.PosY);
            float w = sz * 1.0f;
            float ox = MathF.Sin(b.WalkPhase) * sz * 0.02f;

            var state = g.Save();
            var oldInterp = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.TranslateTransform(p.X + ox, p.Y);
            g.RotateTransform(b.FaceAngle * 180f / MathF.PI);
            g.DrawImage(Sprites.BeastImg, -w / 2, -w / 2, w, w);
            g.Restore(state);
            g.InterpolationMode = oldInterp;

            if (b.FlashT > 0)
            {
                int a = (int)(b.FlashT / 0.12f * 160);
                g.FillEllipse(Pal.B(Pal.CA(a, Pal.C(255, 255, 255))), p.X + ox - w / 2, p.Y - w / 2, w, w);
            }
            if (b.FleeT > 0 && Camera.Zoom >= 0.7f)
                Text(g, "!", FBold, Pal.Warn, p.X, p.Y - w, center: true);

            if (b.Hp < b.MaxHp)
            {
                float bw = w * 0.8f;
                g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), p.X - bw / 2, p.Y - w / 2 - 5, bw, 3);
                g.FillRectangle(Pal.B(Pal.Warn), p.X - bw / 2, p.Y - w / 2 - 5, bw * b.Hp / b.MaxHp, 3);
            }
        }
    }

    // ------------------------------------------------------ hints (iter-5)

    /// <summary>The current onboarding hint as a dismissible chip above the
    /// architect bar. One hint at a time, auto-advances with real progress.</summary>
    public static Rectangle HintDismiss { get; private set; }   // click target, cleared when hidden

    /// <summary>MADDOG iter-9: the caravan's barter board.</summary>
    private static void DrawTradePanel(Graphics g, Game game, ViewState v, Size client)
    {
        var r = new Rectangle(client.Width / 2 - 260, 90, 520, 386);
        PanelBg(g, r);
        string head = game.Trader is { Arrived: true } tr
            ? $"TRADE CARAVAN - departs in {tr.WindowT:0}s"
            : "TRADE CARAVAN";
        PanelTitle(g, r, head);

        float y = r.Y + 32;
        Text(g, Loc.T("Hub stock is the purse. One click per deal."), FSmall, Pal.TextDim, r.X + 14, y);
        y += 22;
        for (int i = 0; i < Bal.TradeDeals.Length; i++)
        {
            var d = Bal.TradeDeals[i];
            bool soldOut = game.Trader != null && game.Trader.SoldDeals[i] >= d.Max;
            bool afford = game.HubRef.Stock[(int)d.Give] >= d.GiveN;
            var row = new Rectangle(r.X + 12, (int)y, r.Width - 24, 34);
            g.FillRectangle(Pal.B(Pal.CA(soldOut ? 100 : 180, Pal.Panel)), row);
            string rhs = d.Recruit
                ? Loc.T("NEW COLONIST joins")
                : $"{d.GetN} {Bal.ItemName(d.Get)}";
            string lhs = $"{d.GiveN} {Bal.ItemName(d.Give)}";
            var iconL = Sprites.ItemIcons[(int)d.Give];
            var oldI = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(iconL, row.X + 8, row.Y + 6, 20, 20);
            if (!d.Recruit) g.DrawImage(Sprites.ItemIcons[(int)d.Get], row.X + 210, row.Y + 6, 20, 20);
            g.InterpolationMode = oldI;
            Text(g, lhs, FSmall, afford ? Pal.Text : Pal.Bad, row.X + 34, row.Y + 10);
            Text(g, "\u2192", FSmall, Pal.TextDim, row.X + 182, row.Y + 10);
            Text(g, rhs, FSmall, Pal.Good, row.X + 236, row.Y + 10);
            Text(g, soldOut ? $"[{Loc.T("sold out")}]" : $"[{d.Max - (game.Trader?.SoldDeals[i] ?? 0)} left]",
                FSmall, soldOut ? Pal.Bad : Pal.TextDim, row.Right - 116, row.Y + 10);
            y += 40;
        }
    }

    /// <summary>ORGANISM: production health board [F5] + status pips over
    /// every machine while open.</summary>
    private static void DrawProd(Graphics g, Game game, ViewState v, Size client)
    {
        // pips over machines
        foreach (var b in game.Builds)
        {
            if (b is not MachineBase m) continue;
            var p = Camera.S(b.X + b.W / 2f, b.Y - 0.35f);
            Color c = m.StatusText(game) switch
            {
                "BROKEN DOWN" => Pal.Bad,
                "RUNNING" => Pal.Good,
                "STARVED" or "NO POWER" or "UNSTAFFED" => Pal.Warn,
                _ => Pal.TextDim,
            };
            using var dot = new SolidBrush(c);
            g.FillEllipse(dot, p.X - 3, p.Y - 3, 6, 6);
        }

        var r = new Rectangle(client.Width - 268, 268, 252, 300);
        PanelBg(g, r);
        PanelTitle(g, r, "PRODUCTION  [F5]");
        var groups = game.Builds.OfType<MachineBase>()
            .GroupBy(m => m.StatusText(game))
            .OrderByDescending(gr => gr.Count());
        float y = r.Y + 30;
        foreach (var gr in groups)
        {
            Color c = gr.Key switch
            {
                "BROKEN DOWN" => Pal.Bad,
                "RUNNING" => Pal.Good,
                "STARVED" or "NO POWER" or "UNSTAFFED" => Pal.Warn,
                _ => Pal.TextDim,
            };
            Text(g, $"{gr.Key}", FSmall, c, r.X + 12, y);
            Text(g, $"{gr.Count()}", FSmall, c, r.Right - 30, y);
            y += 17;
        }
        y += 6;
        int rowsLeft = Math.Max(0, 14 - (int)((y - r.Y - 30) / 17));
        foreach (var m in game.Builds.OfType<MachineBase>().Take(rowsLeft))
        {
            if (y > r.Bottom - 18) break;
            Text(g, Fit(g, $"{Bal.Name(m.Kind)} @ {m.X},{m.Y}", FSmall, 120), FSmall,
                Pal.TextDim, r.X + 12, y);
            string st = m.StatusText(game);
            Text(g, st, FSmall,
                st == "BROKEN DOWN" ? Pal.Bad : st == "RUNNING" ? Pal.Good : Pal.Warn,
                r.Right - 12 - g.MeasureString(st, FSmall).Width, y);
            y += 16;
        }
    }

    /// <summary>SOULS: the colony chronicle - the run as a story.</summary>
    private static void DrawChron(Graphics g, Game game, ViewState v, Size client)
    {
        var r = new Rectangle(client.Width / 2 - 250, client.Height / 2 - 180, 500, 380);
        PanelBg(g, r);
        PanelTitle(g, r, "CHRONICLE  [C]");
        float y = r.Y + 30;
        foreach (var (day, text) in game.Chronicle.Take(18))
        {
            Text(g, $"d{day}", FSmall, Pal.Accent, r.X + 10, y);
            Text(g, Fit(g, text, FSmall, r.Width - 60), FSmall, Pal.Text, r.X + 42, y);
            y += 19;
            if (y > r.Bottom - 16) break;
        }
    }

    /// <summary>MADDOG iter-7 (M7): colony statistics with wealth sparkline.</summary>
    public static Rectangle StatsRect(Size client) =>
        new Rectangle(client.Width / 2 - 240, client.Height / 2 - 190, 480, 380);

    private static void DrawStats(Graphics g, Game game, ViewState v, Size client)
    {
        var r = StatsRect(client);
        PanelBg(g, r);
        PanelTitle(g, r, "COLONY REPORT  [S]");

        var rows = new (string label, string val)[]
        {
            (Loc.T("Day"), $"{game.Day}"),
            (Loc.T("Colonists"), $"{game.Cols.Count(c => c.Hp > 0)}"),
            (Loc.T("Wealth"), $"{(int)game.Wealth}"),
            (Loc.T("Raids survived"), $"{game.Wave}"),
            (Loc.T("Horrors slain"), $"{game.StatKills}"),
            (Loc.T("Grazers hunted"), $"{game.StatBeastsHunted}"),
            (Loc.T("Buildings raised"), $"{game.StatBuilt}"),
            (Loc.T("Meals served"), $"{game.StatMeals}"),
            (Loc.T("Tiles hand-mined"), $"{game.StatMined}"),
            (Loc.T("Weather"), game.Weather.ToString()),
        };
        float y = r.Y + 30;
        for (int i = 0; i < rows.Length; i++)
        {
            float col = i % 2 == 0 ? r.X + 16 : r.X + 244;
            float row = y + (i / 2) * 22;
            Text(g, rows[i].label, FSmall, Pal.TextDim, col, row);
            Text(g, rows[i].val, FBold, Pal.Text, col + 150, row - 1);
        }
        y += 5 * 22 + 10;

        // wealth sparkline (up to 90 days)
        Text(g, Loc.T("Wealth over time"), FSmall, Pal.TextDim, r.X + 16, y); y += 18;
        var chart = new Rectangle(r.X + 16, (int)y, r.Width - 32, 110);
        g.FillRectangle(Pal.B(Pal.C(16, 20, 24)), chart);
        g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), chart.X, chart.Y, chart.Width, chart.Height);
        if (game.WealthHist.Count >= 2)
        {
            float max = MathF.Max(1f, game.WealthHist.Max());
            using var pen = new Pen(Pal.Accent, 1.6f);
            var pts = new PointF[game.WealthHist.Count];
            for (int i = 0; i < pts.Length; i++)
            {
                float fx = chart.X + 2 + (chart.Width - 4) * i / (float)(pts.Length - 1);
                float fy = chart.Bottom - 2 - (chart.Height - 4) * game.WealthHist[i] / max;
                pts[i] = new PointF(fx, fy);
            }
            g.DrawLines(pen, pts);
            Text(g, $"max {(int)max}", FSmall, Pal.TextDim, chart.Right - 60, chart.Y + 4);
        }
        else
            Text(g, Loc.T("a new day's sample appears each dawn"), FSmall, Pal.TextDim,
                chart.X + 12, chart.Y + chart.Height / 2 - 8);
    }

    /// <summary>Event-history panel rect (left-center, scrollable via wheel).</summary>
    public static Rectangle HistoryRect(Size client) =>
        new Rectangle(12, client.Height / 2 - 170, 470, 360);

    private static void DrawHistory(Graphics g, Game game, ViewState v, Size client)
    {
        var r = HistoryRect(client);
        PanelBg(g, r);
        PanelTitle(g, r, "EVENT LOG  [L]");
        int y = r.Y + 28;
        int shown = 0;
        for (int i = v.HistScroll; i < game.History.Count && shown < 14; i++, shown++)
        {
            var line = game.History[i];
            Text(g, Fit(g, $"d{game.Day}  {line.Text}", FSmall, r.Width - 20), FSmall,
                line.Col, r.X + 10, y);
            y += 16;
        }
        if (game.History.Count > 14)
            Text(g, $"- wheel to scroll ({v.HistScroll}/{Math.Max(0, game.History.Count - 14)}) -",
                FSmall, Pal.TextDim, r.X + 10, r.Bottom - 20);
    }

    private static void DrawHints(Graphics g, Game game, Size client)
    {
        HintDismiss = Rectangle.Empty;
        int stage = game.HintStage();
        if (stage < 0 || game.Time < 4f) return;                 // let the opening logs breathe
        string txt = stage switch
        {
            0 => Loc.T("Click a colonist: skills, mood, work priorities. Drag a box to select several."),
            1 => Loc.T("Gather ore: press [V] and drag over a deposit - or place a Drill from Production."),
            2 => Loc.T("Smelt it: place a Primitive Furnace (Production) and feed it stone from mining."),
            3 => Loc.T("Open RESEARCH [T] and start Automation - machines then run unstaffed."),
            4 => Loc.T("Secure food: a Crop Plot by water (plus a Pump) grows Food for free."),
            5 => Loc.T("Raiders scale with wealth. Wall off a yard, add a Door, keep a turret behind it."),
            _ => Loc.T("Draft a pawn [R], right-click a grazer to hunt - carcasses become Food."),
        };
        float w = Math.Min(720, client.Width - 40);
        var r = new Rectangle(client.Width / 2 - (int)w / 2, client.Height - 132, (int)w, 30);
        g.FillRectangle(Pal.B(Pal.CA(235, Pal.Panel)), r);
        g.DrawRectangle(Pal.P(Pal.Accent), r.X, r.Y, r.Width, r.Height);
        Text(g, Fit(g, txt, FSmall, w - 40), FSmall, Pal.Text, r.X + 12, r.Y + 8);
        Text(g, $"[{stage + 1}/{Game.HintStageCount}]  x", FSmall, Pal.TextDim, r.Right - 74, r.Y + 8);
        HintDismiss = new Rectangle(r.Right - 22, r.Y, 22, 30);
    }

    /// <summary>LANDING v2 (crash-landing, RimWorld-style): the colony hub
    /// itself falls from orbit, thrusters screaming, kicks up dust on
    /// impact, and the six survivors pop out of it one by one. The sim is
    /// frozen while this plays; any click or key skips it.</summary>
    private static void DrawLanding(Graphics g, Game game, ViewState v, Size client)
    {
        float t = Math.Clamp(v.Landing01, 0f, 1f);
        const float fallEnd = 0.42f;      // keep in sync with MainForm LandFallEnd

        // letterbox bars slide away as the hub descends
        int bar = (int)(client.Height * 0.11f * (1f - t));
        using (var blk = new SolidBrush(Color.FromArgb(235, 6, 8, 10)))
        {
            g.FillRectangle(blk, 0, 0, client.Width, bar);
            g.FillRectangle(blk, 0, client.Height - bar, client.Width, bar);
        }

        float sz = Camera.Sz;
        var hub = game.HubRef;
        var p0 = Camera.S(hub.X, hub.Y);
        int hs = (int)(3 * sz);
        var finalRect = new Rectangle((int)p0.X, (int)p0.Y, hs, hs);
        var mid = new PointF(finalRect.X + hs / 2f, finalRect.Y + hs * 0.62f);

        float df = Math.Clamp(t / fallEnd, 0f, 1f);
        float ease = df * df;                            // gravity accelerates

        // growing ground shadow
        using (var sh = new SolidBrush(Color.FromArgb((int)(30 + 110 * ease), 0, 0, 0)))
            g.FillEllipse(sh, mid.X - hs * (0.25f + 0.35f * ease), finalRect.Bottom - hs * 0.10f,
                hs * (0.5f + 0.7f * ease), hs * (0.16f + 0.2f * ease));

        if (t < fallEnd)
        {
            float yTop = finalRect.Y - (1f - ease) * (client.Height * 0.95f + 260f);
            var rect = new Rectangle(finalRect.X, (int)yTop, hs, hs);

            // twin thruster cones under the hull
            float flick = 0.75f + 0.25f * MathF.Sin(Environment.TickCount / 26f);
            float fl = hs * 0.5f * flick;
            foreach (float fx in new[] { rect.X + hs * 0.26f, rect.X + hs * 0.74f })
            {
                using (var flame = new SolidBrush(Color.FromArgb(210, 255, 170, 80)))
                    g.FillPolygon(flame, new PointF[]
                    { new(fx - hs * 0.09f, rect.Bottom - 2), new(fx + hs * 0.09f, rect.Bottom - 2), new(fx, rect.Bottom + fl) });
                using (var core = new SolidBrush(Color.FromArgb(200, 255, 235, 170)))
                    g.FillPolygon(core, new PointF[]
                    { new(fx - hs * 0.045f, rect.Bottom - 2), new(fx + hs * 0.045f, rect.Bottom - 2), new(fx, rect.Bottom + fl * 0.55f) });
            }

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(Sprites.Hub, rect);        // (was Building() -> pink "missing" checker)
        }
        else
        {
            // impact flash
            float impact = t - fallEnd;
            if (impact < 0.10f)
                using (var flash = new SolidBrush(Color.FromArgb((int)(160 * (1 - impact / 0.10f)), 255, 230, 190)))
                    g.FillRectangle(flash, 0, 0, client.Width, client.Height);

            // dust rings rolling out from the crash site
            for (int i = 0; i < 3; i++)
            {
                float dd = Math.Clamp((impact - i * 0.09f) / 0.5f, 0f, 1f);
                if (dd <= 0f) continue;
                using var dust = new Pen(Color.FromArgb((int)(150 * (1 - dd)), 150, 140, 120), 2.6f);
                g.DrawEllipse(dust, mid.X - 16 - 90 * dd, mid.Y - 8 - 45 * dd, 32 + 180 * dd, 16 + 90 * dd);
            }
        }

        // title: fades in with the fall, out with the bars
        int ta = Math.Min(255, (int)(t * 650));
        ta = Math.Min(ta, (int)((1f - t) * 900));
        if (ta > 4)
        {
            using var brush = new SolidBrush(Color.FromArgb(ta, 226, 232, 240));
            g.DrawString("FORGEHAVEN", FTitle, brush, client.Width / 2f - g.MeasureString("FORGEHAVEN", FTitle).Width / 2f, client.Height * 0.16f);
            string sub = t < fallEnd
                ? Loc.T("brace for impact")
                : Loc.T("day 1 - the survivors emerge");
            using var sbr = new SolidBrush(Color.FromArgb(ta * 3 / 4, 160, 176, 190));
            g.DrawString(sub, FBig, sbr, client.Width / 2f - g.MeasureString(sub, FBig).Width / 2f, client.Height * 0.16f + 54);
        }

        if (t < 0.95f)
        {
            string skip = Loc.T("click or press any key to skip");
            using var kbr = new SolidBrush(Color.FromArgb(140, 120, 132, 144));
            g.DrawString(skip, FSmall, kbr, client.Width / 2f - g.MeasureString(skip, FSmall).Width / 2f, client.Height - bar - 26);
        }
    }

    /// <summary>RECLAMATION: the current chapter objective, always visible.</summary>
    private static void DrawQuestChip(Graphics g, Game game, Size client)
    {
        string txt = game.ObjectiveText();
        if (txt.Length == 0) return;
        var f = FSmall;
        var w = (int)g.MeasureString(txt, f).Width + 22;
        // centered in the gap between the stock panel (left) and the
        // selection panel (right), just under the top bar
        int x0 = StockToggleRect(client).Right + 12;
        int x1 = client.Width - 268;
        int cx = Math.Max(x0, (x0 + x1) / 2);
        var r = new Rectangle(cx - w / 2, 30, w, 20);
        if (r.X < x0) r.X = x0;
        g.FillRectangle(Pal.B(Pal.CA(215, Pal.Panel)), r);
        g.DrawRectangle(Pal.P(game.FinalePending ? Pal.Bad : Pal.Accent), r.X, r.Y, r.Width, r.Height);
        Text(g, txt, f, game.FinalePending ? Pal.Bad : Pal.Accent, r.X + 11, r.Y + 4);
    }

    // ------------------------------------------------------ weather (iter-3)

    /// <summary>Ambient rain/storm overlay: sky tint, animated streaks drawn
    /// as ONE GraphicsPath (a single GDI call), lightning flash in storms.
    /// Deterministic from game time - consumes no RNG state.</summary>
    private static void DrawWeather(Graphics g, Game game, Size client)
    {
        if (game.Weather == WeatherKind.Clear) return;
        bool storm = game.Weather == WeatherKind.Storm;

        // moody sky tint
        g.FillRectangle(Pal.B(Pal.CA(storm ? 74 : 42, Pal.C(38, 58, 88))),
            0, 0, client.Width, client.Height);

        // falling streaks: hash-seeded per drop, wrapped by height
        int drops = storm ? 220 : 130;
        float t = game.Time;
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        for (int i = 0; i < drops; i++)
        {
            uint h = (uint)i * 747796405u + 1u;
            h = (h ^ (h >> 13)) * 1274126177u;
            float speed = 300f + ((h >> 20) & 63) * 9f;
            float x = ((h >> 10) & 1023) / 1023f * client.Width;
            float y = (((h & 1023) / 1023f) * client.Height + t * speed) % client.Height;
            path.AddLine(x, y, x - 6f, y + 12f);
            path.StartFigure();
        }
        using var pen = new Pen(Pal.CA(120, Pal.C(170, 200, 235)), 1.1f);
        g.DrawPath(pen, path);

        // storm lightning: brief flash every ~9s of game time
        if (storm && (t % 9f) < 0.09f)
            g.FillRectangle(Pal.B(Pal.CA(90, Pal.C(232, 236, 255))),
                0, 0, client.Width, client.Height);
    }

    private static void DrawAlerts(Graphics g, ViewState v, Game game, Size client)
    {
        // chips themselves are buttons made by MainForm; just shade behind them
        if (game.Alerts.Count == 0) return;
        int y0 = v.ResOpen ? StockPanelRect(client).Bottom + 6 : 42;
        int h = Math.Min(game.Alerts.Count, 8) * 24 + 4;
        g.FillRectangle(Pal.B(Pal.CA(150, Pal.C(15, 18, 22))), 6, y0, 320, h);
    }

    private static void DrawRaidBanner(Graphics g, Game game, Size client)
    {
        float pulse = 0.6f + 0.4f * MathF.Sin(Environment.TickCount / 90f);
        var col = Pal.CA((int)(230 * pulse), Pal.Bad);
        Text(g, $"— RAID {game.Wave} INBOUND —", FBig, col, client.Width / 2f, 52, center: true);

        var sp = Camera.S(game.RaidPingX, game.RaidPingY);
        float cx = client.Width / 2f, cy = client.Height / 2f;
        float dx = sp.X - cx, dy = sp.Y - cy;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        dx /= len; dy /= len;
        float ex = cx + dx * (Math.Min(client.Width, client.Height) / 2f - 70);
        float ey = cy + dy * (Math.Min(client.Width, client.Height) / 2f - 70);
        g.DrawEllipse(Pal.P(col, 3f), ex - 12, ey - 12, 24, 24);
        g.DrawLine(Pal.P(col, 3f), ex, ey, ex + dx * 14, ey + dy * 14);
    }

    // ----------------------------------------------------------- buttons ---

    public static void DrawButton(Graphics g, UiButton b, bool hover)
    {
        if (b.Id.StartsWith("tech:")) return;      // drawn by the research panel

        if (b.Id.StartsWith("alert:"))             // alert chip
        {
            var col = b.Tint ?? Pal.Warn;
            using var ap = RoundPath(b.R, 5);
            g.FillPath(Pal.B(hover ? Pal.CA(235, Pal.C(38, 46, 56)) : Pal.CA(225, Pal.C(24, 29, 36))), ap);
            using var ab = new Pen(Pal.CA(90, col), 1f);
            g.DrawPath(ab, ap);
            g.FillRectangle(Pal.B(col), b.R.X + 4, b.R.Y + 4, 3, b.R.Height - 8);
            var txt = Fit(g, b.Text, FSmall, b.R.Width - 18);
            g.DrawString(txt, FSmall, Pal.B(col), b.R.X + 12, b.R.Y + b.R.Height / 2 - 7);
            return;
        }

        // style pass: rounded silhouette + vertical gradient + hover glow
        using var bp = RoundPath(b.R, b.R.Height >= 34 ? 7 : 5);
        Color top = !b.Enabled ? Pal.CA(120, Pal.Panel)
                  : b.Active ? Pal.C(46, 82, 104)
                  : hover ? Pal.C(52, 62, 74)
                  : Pal.C(38, 46, 56);
        Color bot = !b.Enabled ? Pal.CA(130, Pal.Panel)
                  : b.Active ? Pal.C(28, 54, 70)
                  : hover ? Pal.C(34, 42, 52)
                  : Pal.C(26, 32, 40);
        using (var lg = new LinearGradientBrush(
            b.R, top, bot, 90f))
            g.FillPath(lg, bp);
        if (hover && b.Enabled && !b.Active)   // faint glow ring on hover
        {
            using var glow = new Pen(Pal.CA(70, Pal.Accent), 1.2f);
            g.DrawPath(glow, bp);
        }
        using var bord = new Pen(b.Active ? Pal.Accent : Pal.C(62, 74, 90), 1.1f);
        g.DrawPath(bord, bp);

        var tc = b.Tint ?? (b.Enabled ? Pal.Text : Pal.TextDim);
        if (b.Sub.Length > 0)
        {
            Text(g, Fit(g, b.Text, FBold, b.R.Width - 16), FBold, tc, b.R.X + 8, b.R.Y + 4);
            Text(g, Fit(g, b.Sub, FSmall, b.R.Width - 16), FSmall,
                b.Enabled ? Pal.TextDim : Pal.C(90, 96, 104), b.R.X + 8, b.R.Y + b.R.Height - 17);
        }
        else
            Text(g, Fit(g, b.Text, FBold, b.R.Width - 10), FBold, tc,
                b.R.X + b.R.Width / 2f, b.R.Y + b.R.Height / 2 - 6, center: true);
    }

    private static void DrawBuildTooltip(Graphics g, ViewState v, Size client)
    {
        if (v.Hover == null || !v.Hover.StartsWith("arch:")) return;
        if (!int.TryParse(v.Hover[5..], out int ki)) return;
        var k = (BuildKind)ki;

        int w = 330;
        var body = Wrap(Bal.Blurb(k) + "\n" + Bal.Needs(k), g, FNorm, w - 20);
        int h = 44 + body.Length * 15;
        int x = Math.Min(v.Mouse.X + 18, client.Width - w - 8);
        int y = Math.Max(30, v.Mouse.Y - h - 10);

        var r = new Rectangle(x, y, w, h);
        PanelBg(g, r);
        g.FillRectangle(Pal.B(Pal.CA(200, Pal.Accent)), r.X + 1, r.Y + 20, 3, r.Height - 24);
        // name left, cost right — each capped so they never collide or spill
        string cost = Bal.CostText(k);
        float costW = Math.Min(g.MeasureString(cost, FBold).Width, w * 0.5f - 10);
        Text(g, Fit(g, Bal.Name(k), FBold, w - 20 - costW - 8), FBold, Pal.Text, x + 10, y + 8);
        Text(g, Fit(g, cost, FBold, costW), FBold, Pal.Text, x + w - 10 - costW, y + 8);
        float ty = y + 27;
        foreach (var line in body)
        {
            bool isNeeds = line.StartsWith("Needs:");
            Text(g, line, FSmall, isNeeds ? Pal.Warn : Pal.TextDim, x + 10, ty);
            ty += 15;
        }
    }

    private static void DrawLog(Graphics g, Game game, Size client)
    {
        float maxW = Math.Min(520, client.Width * 0.5f);
        float y = client.Height - 96;
        foreach (var line in game.Log)
        {
            Text(g, Fit(g, line.Text, FSmall, maxW), FSmall,
                Pal.CA((int)Math.Min(230, line.T * 100 + 80), line.Col), 10, y);
            y -= 17;
        }
    }

    // -------------------------------------------------------- selection ----

    // -------------------------------------------------- art pass: panels --

    /// <summary>Rounded panel background with an accent-lit top edge.</summary>
    /// <summary>Rounded-rect path used by every panel/button silhouette.</summary>
    public static GraphicsPath RoundPath(Rectangle r, int d)
    {
        var path = new GraphicsPath();
        if (r.Width <= d * 2 || r.Height <= d * 2)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void PanelBg(Graphics g, Rectangle r)
    {
        // soft drop shadow (two layers, slightly offset)
        using (var sh1 = RoundPath(new Rectangle(r.X + 2, r.Y + 3, r.Width, r.Height), 12))
            g.FillPath(Pal.B(Pal.CA(60, Pal.C(0, 0, 0))), sh1);
        using (var sh2 = RoundPath(new Rectangle(r.X + 1, r.Y + 2, r.Width, r.Height), 12))
            g.FillPath(Pal.B(Pal.CA(35, Pal.C(0, 0, 0))), sh2);

        // vertical gradient: lighter lid, deeper bottom — reads as a solid
        // pane of glass instead of a flat translucent box
        using var path = RoundPath(r, 12);
        var rect = new Rectangle(r.X, r.Y, r.Width, r.Height);
        using (var lg = new LinearGradientBrush(
            rect, Pal.CA(246, Pal.PanelLight), Pal.CA(246, Pal.Panel), 90f))
            g.FillPath(lg, path);
        using var border = new Pen(Pal.C(70, 84, 102), 1.4f);
        g.DrawPath(border, path);
        // accent hairline along the top — ties the HUD together
        using var accent = new Pen(Pal.CA(120, Pal.Accent), 1.4f);
        g.DrawLine(accent, r.X + 12, r.Y + 0.5f, r.Right - 12, r.Y + 0.5f);
    }

    public static Rectangle StockPanelRect(Size client) => PanelLayout.StockRect(client);
    public static Rectangle StockToggleRect(Size client)
    {
        var r = StockPanelRect(client);
        return new Rectangle(r.Right + 2, r.Y, 26, 24);
    }
    public static Rectangle SelPanelRect(Size client) => PanelLayout.SelRect(client);

    /// <summary>Draggable title strip: grip dots + label + separator.</summary>
    private static void PanelTitle(Graphics g, Rectangle r, string title)
    {
        for (int i = 0; i < 3; i++)
        {
            g.FillRectangle(Pal.B(Pal.TextDim), r.X + 10 + i * 5, r.Y + 6, 2, 2);
            g.FillRectangle(Pal.B(Pal.TextDim), r.X + 10 + i * 5, r.Y + 11, 2, 2);
        }
        Text(g, Loc.T(title), FSmall, Pal.TextDim, r.X + 32, r.Y + 3);
        using var sep = new Pen(Pal.C(44, 52, 62), 1f);
        g.DrawLine(sep, r.X + 6, r.Y + 20, r.Right - 6, r.Y + 20);
    }

    /// <summary>Collapsible inventory: every item as icon + count.</summary>
    private static void DrawStockPanel(Graphics g, Game game, ViewState v, Size client)
    {
        if (!v.ResOpen) return;
        var r = StockPanelRect(client);
        PanelBg(g, r);
        PanelTitle(g, r, "STOCK");
        var st = game.HubRef.Stock;
        for (int i = 0; i < Bal.ItemCount; i++)
        {
            int col = i % 5, row = i / 5;
            float cx = r.X + 10 + col * 96, cy = r.Y + 26 + row * 28;
            var oldI = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(Sprites.ItemIcons[i], cx, cy, 22, 22);
            g.InterpolationMode = oldI;
            int n = st[i];
            Text(g, n > 0 ? n.ToString() : "-", FSmall,
                n > 0 ? Pal.ItemColor((ItemKind)i) : Pal.TextDim, cx + 26, cy + 4);
            // rolling net income: can the player see a deficit coming?
            float rate = game.NetPerDay[i];
            if (MathF.Abs(rate) >= 0.5f)
                Text(g, (rate > 0 ? "+" : "") + rate.ToString("0.0") + "/d", FSmall,
                    rate > 0 ? Pal.Good : Pal.Bad, cx + 56, cy + 4);
        }
    }

    // ------------------------------------------- Phase 1: pawn panel (BL) --

    public static Rectangle PawnPanelRect(Size client) => PanelLayout.PawnRect(client);
    public static Rectangle[] PawnTabRects(Size client)
    {
        var p = PawnPanelRect(client);
        return new[]
        {
            new Rectangle(p.X + 4, p.Y + 24, 76, 22),
            new Rectangle(p.X + 84, p.Y + 24, 76, 22),
            new Rectangle(p.X + 164, p.Y + 24, 76, 22),
            new Rectangle(p.X + 244, p.Y + 24, 74, 22),
        };
    }
    /// <summary>Y of work-priority row i (0..6) inside the WORK tab.</summary>
    public static float PawnWorkRowY(Size client, int i) => PawnPanelRect(client).Y + 64 + i * 24;

    private static void DrawPawnPanel(Graphics g, ViewState v, Size client)
    {
        var multi = v.Sel.Cols.Count > 1;
        if (v.Sel.Col == null && !multi) return;

        var r = PawnPanelRect(client);
        PanelBg(g, r);
        PanelTitle(g, r, "COLONIST");

        string[] tabNames = { "OVERVIEW", "HEALTH", "WORK", "COMBAT" };
        var tabs = PawnTabRects(client);
        for (int i = 0; i < 4; i++)
        {
            var tr = tabs[i];
            bool on = v.PawnTab == i;
            g.FillRectangle(Pal.B(on ? Pal.PanelLight : Pal.CA(120, Pal.Panel)), tr);
            g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), tr.X, tr.Y, tr.Width, tr.Height);
            Text(g, Loc.T(tabNames[i]), FSmall, on ? Pal.Accent : Pal.TextDim, tr.X + 6, tr.Y + 4);
        }

        float x = r.X + 12, y = r.Y + 52;
        float w = r.Width - 24;

        if (multi)
        {
            Text(g, $"{v.Sel.Cols.Count} colonists selected", FBold, Pal.Colonist, x, y); y += 22;
            int drafted = v.Sel.Cols.Count(c => c.Drafted);
            Text(g, $"drafted: {drafted} / {v.Sel.Cols.Count}", FSmall, drafted > 0 ? Pal.Bad : Pal.TextDim, x, y); y += 18;
            Text(g, "right-click ground = move - right-click raider = attack", FSmall, Pal.TextDim, x, y); y += 18;
            Text(g, "buttons below: draft all, stance, auto-engage", FSmall, Pal.TextDim, x, y);
            return;
        }

        var c = v.Sel.Col!;
        switch (v.PawnTab)
        {
            case 0: // overview
                Text(g, c.Name, FBold, Pal.Colonist, x, y); y += 19;
                string act = c.Drafted ? Loc.T("DRAFTED - awaiting orders")
                    : c.Arriving ? Loc.T("traveling to the colony")
                    : c.BuildJob != null ? Loc.T("building") + ": " + Bal.Name(c.BuildJob.Kind)
                    : c.MineJob != null ? Loc.T("mining by hand")
                    : c.ExcavJob != null ? Loc.T("excavating the Ark wreck")
                    : c.Job != null ? Loc.T("works") + ": " + Bal.Name(c.Job.Kind)
                    : c.CaptureTarget != null ? Loc.T("capturing prisoner")
                    : Loc.T("idle");
                Text(g, act, FSmall, c.Drafted ? Pal.Bad : Pal.TextDim, x, y); y += 17;
                Text(g, string.Join(", ", c.Traits), FSmall, Pal.TextDim, x, y); y += 16;
                Text(g, Loc.T("Passions") + ": " + c.PassionText + " (2x xp)", FSmall,
                    Pal.ItemColor(ItemKind.SciencePack), x, y); y += 20;
                for (int i = 0; i < 6; i++)
                {
                    bool pass = c.IsPassion(i);
                    Text(g, Colonist.StatNames[i] + " " + c.Stats[i] + (pass ? "\u2764" : ""),
                        FSmall, pass ? Pal.Good : Pal.TextDim, x + (i % 3) * 100, y + i / 3 * 16);
                }
                y += 36;
                Bar(g, x, y, w, Loc.T("Morale") + "  \u2192 " + ((int)c.MoodTarget) + "%",
                    c.Morale / 100f, c.Morale >= Bal.FlowMorale ? Pal.Good : Pal.Warn);
                y += 24;
                // transparency: what is moving this pawn's mood right now
                foreach (var f in c.MoodFactors.OrderByDescending(f => MathF.Abs(f.Delta)).Take(5))
                {
                    Text(g, Loc.T(f.Key), FSmall, Pal.TextDim, x + 4, y);
                    Text(g, (f.Delta > 0 ? "+" : "") + f.Delta.ToString("0"), FSmall,
                        f.Delta > 0 ? Pal.Good : Pal.Bad, x + w - 28, y);
                    y += 13;
                }
                break;

            case 1: // health
                Bar(g, x, y, w, Loc.T("Health"), c.Hp / c.MaxHp, Pal.Bad); y += 24;
                Bar(g, x, y, w, Loc.T("Hunger"), c.Hunger / 100f, c.Hunger < 25 ? Pal.Bad : Pal.Good); y += 24;
                Bar(g, x, y, w, Loc.T("Rest"), c.Rest / 100f, c.Rest < 25 ? Pal.Warn : Pal.Good); y += 24;
                Bar(g, x, y, w, Loc.T("Morale"), c.Morale / 100f, c.Morale >= Bal.FlowMorale ? Pal.Good : Pal.Warn); y += 28;
                if (c.CarryN > 0)
                {
                    Text(g, Loc.T("Carrying") + ": " + c.CarryN + " x " + Bal.ItemName((ItemKind)c.CarryKind),
                        FSmall, Pal.Text, x, y); y += 18;
                }
                Text(g, Loc.T("Traits") + ": " + string.Join(", ", c.Traits), FSmall, Pal.TextDim, x, y); y += 18;
                Text(g, Loc.T("Stance") + ": " + StanceText(c.Stance), FSmall, Pal.TextDim, x, y);
                break;

            case 2: // work priorities: rows here, +/- buttons belong to MainForm
                Text(g, Loc.T("1 = first choice - 4 = never"), FSmall, Pal.TextDim, x, r.Y + 52);
                for (int i = 0; i < Bal.WorkCount; i++)
                {
                    var wt = (WorkType)i;
                    float ry = PawnWorkRowY(client, i);
                    bool passion = c.IsPassion(Bal.StatOf(wt));
                    Text(g, Loc.T(Bal.WorkName(wt)), FSmall, passion ? Pal.Good : Pal.Text, x, ry + 3);
                    string dots = c.Priorities[i] >= 4 ? "never" : new string('\u25cf', c.Priorities[i]);
                    Text(g, dots, FSmall, c.Priorities[i] >= 4 ? Pal.Bad : Pal.Accent, x + 190, ry + 3);
                }
                break;

            case 3: // combat
                Text(g, Loc.T("Draft") + ": " + (c.Drafted ? Loc.T("ON") : Loc.T("OFF")),
                    FBold, c.Drafted ? Pal.Bad : Pal.TextDim, x, y); y += 22;
                Text(g, Loc.T("Stance") + ": " + StanceText(c.Stance), FSmall, Pal.TextDim, x, y); y += 20;
                Text(g, Loc.T("Auto-engage") + ": " + (c.AutoEngage ? Loc.T("ON") : Loc.T("OFF")),
                    FSmall, c.AutoEngage ? Pal.Good : Pal.TextDim, x, y); y += 22;
                Text(g, Loc.T("Melee") + ": " + (3 + c.Stats[0] * 0.8f).ToString("0") + " " + Loc.T("dmg"),
                    FSmall, Pal.TextDim, x, y); y += 18;
                Text(g, Loc.T("Drafted pawns scout fog and follow"), FSmall, Pal.TextDim, x, y); y += 15;
                Text(g, Loc.T("right-click orders. [R] toggles draft."), FSmall, Pal.TextDim, x, y);
                break;
        }
    }

    private static string StanceText(Stance s) => s switch
    {
        Stance.Fight => Loc.T("Fight"),
        Stance.Ignore => Loc.T("Ignore"),
        _ => Loc.T("Flee"),
    };

    // ------------------------------------------------- Phase 1: labor UI --

    /// <summary>Blueprints: translucent building sprite + progress bar.</summary>
    private static void DrawBlueprints(Graphics g, Game game)
    {
        foreach (var bp in game.Blueprints)
        {
            var p = Camera.S(bp.X, bp.Y);
            float sz = Camera.Sz;
            var rect = new Rectangle((int)p.X, (int)p.Y, (int)(bp.W * sz), (int)(bp.H * sz));
            using var pen = new Pen(Pal.Accent, 1.6f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, rect);
            var sprite = Sprites.Building(bp.Kind);
            DrawImageAlpha(g, sprite,
                new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4), 0.42f);
            // progress bar
            float frac = 1f - Math.Clamp(bp.Work / MathF.Max(0.001f, bp.WorkMax), 0, 1);
            var bar = new Rectangle(rect.X + 2, rect.Bottom - 7, rect.Width - 4, 5);
            g.FillRectangle(Pal.B(Pal.C(18, 22, 26)), bar);
            g.FillRectangle(Pal.B(Pal.Good), bar.X, bar.Y, (int)(bar.Width * frac), bar.Height);
            if (bp.Builder == null)
                Text(g, "?", FSmall, Pal.Warn, rect.Right - 12, rect.Y + 2);
        }
    }

    /// <summary>Mine designations: crossed pick marks on the tile.</summary>
    private static void DrawMineOrders(Graphics g, Game game)
    {
        float sz = Camera.Sz;
        if (sz < 9) return;                          // invisible when zoomed far out
        foreach (var mo in game.MineOrders)
        {
            var p = Camera.S(mo.X + 0.5f, mo.Y + 0.5f);
            float q = sz * 0.3f;
            // Defensive: skip invalid coordinates that would make GDI+ throw.
            if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.X) || float.IsInfinity(p.Y) ||
                float.IsNaN(q) || float.IsInfinity(q)) continue;

            // Compute endpoints and add extra validation to avoid passing out-of-range
            // values to GDI+ which can throw ArgumentException.
            float x1 = p.X - q, y1 = p.Y - q, x2 = p.X + q, y2 = p.Y + q;
            if (float.IsNaN(x1) || float.IsNaN(y1) || float.IsNaN(x2) || float.IsNaN(y2) ||
                float.IsInfinity(x1) || float.IsInfinity(y1) || float.IsInfinity(x2) || float.IsInfinity(y2))
                continue;

            const float MaxCoord = 1e7f; // conservative safety bound in device pixels
            if (MathF.Abs(x1) > MaxCoord || MathF.Abs(y1) > MaxCoord || MathF.Abs(x2) > MaxCoord || MathF.Abs(y2) > MaxCoord)
                continue;

            var pen = Pal.P(Pal.Warn, 2f);
            g.DrawLine(pen, x1, y1, x2, y2);

            x1 = p.X - q; y1 = p.Y + q; x2 = p.X + q; y2 = p.Y - q;
            if (float.IsNaN(x1) || float.IsNaN(y1) || float.IsNaN(x2) || float.IsNaN(y2) ||
                float.IsInfinity(x1) || float.IsInfinity(y1) || float.IsInfinity(x2) || float.IsInfinity(y2))
                continue;
            if (MathF.Abs(x1) > MaxCoord || MathF.Abs(y1) > MaxCoord || MathF.Abs(x2) > MaxCoord || MathF.Abs(y2) > MaxCoord)
                continue;

            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    /// <summary>Perf overlay (dev) / small FPS counter (settings).</summary>
    private static void DrawPerf(Graphics g, Game game, ViewState v, Size client)
    {
        if (!v.ShowPerf && !v.ShowFps) return;
        int x = 10, y = client.Height - 130;
        var lines = new List<string> { $"fps {v.Fps,5:0}   frame {v.FrameMs,4:0.0} ms" };
        if (v.ShowPerf)
        {
            lines.Add($"colonists {game.Cols.Count}  buildings {game.Builds.Count}  foes {game.Foes.Count}");
            lines.Add($"blueprints {game.Blueprints.Count}  mine orders {game.MineOrders.Count}");
            lines.Add($"LOD chunks cached {_lodChunks.Count} (step {_lodStep:0}px)");
            lines.Add($"belts/bots/trains {game.Builds.OfType<Belt>().Count()}/{game.Bots.Count}/{game.Trains.Count}");
        }
        int h = 16 * lines.Count + 10, w = 260;
        g.FillRectangle(Pal.B(Pal.CA(210, Pal.C(12, 15, 19))), x, y, w, h);
        g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), x, y, w, h);
        for (int i = 0; i < lines.Count; i++)
            Text(g, lines[i], FSmall, i == 0 ? Pal.Good : Pal.TextDim, x + 8, y + 6 + i * 16);
    }

    private static void DrawSelectionPanel(Graphics g, Game game, ViewState v, Size client)
    {
        if (v.Sel.B == null && v.Sel.Foe == null) return;   // pawns get their own bottom-left panel

        var r = SelPanelRect(client);
        int w = r.Width;
        PanelBg(g, r);
        PanelTitle(g, r, "INSPECTOR");

        float x = r.X + 10, y = r.Y + 26;
        if (v.Sel.Foe is { } foe)
        {
            Text(g, foe.Apex ? "Apex Horror" : "Wild Horror", FBold, foe.Downed ? Pal.Warn : Pal.Bad, x, y); y += 20;
            Bar(g, x, y, w - 20, "Health", foe.Hp / foe.MaxHp, Pal.Bad); y += 24;
            if (foe.Downed)
            {
                Text(g, "DOWNED — helpless.", FSmall, Pal.Warn, x, y); y += 16;
                Text(g, "Click Capture to intern it", FSmall, Pal.TextDim, x, y); y += 16;
                Text(g, "in the Hub brig.", FSmall, Pal.TextDim, x, y);
            }
            else
            {
                Text(g, $"Melee {foe.Dps:F0} dps · HP {(int)foe.Hp}/{(int)foe.MaxHp}", FSmall, Pal.TextDim, x, y);
            }
        }
        else if (v.Sel.B is { } b)
        {
            Text(g, Bal.Name(b.Kind), FBold, Pal.Text, x, y); y += 17;
            float ty = y;
            foreach (var line in Wrap(Bal.Blurb(b.Kind), g, FSmall, w - 20))
            { Text(g, line, FSmall, Pal.TextDim, x, ty); ty += 14; }
            y = ty + 4;

            string status = b switch
            {
                PowerPole or Battery => GridInfo(game, b),
                TrainStop ts2 => $"Buffers {ts2.InBuffer.Count} in / {ts2.OutBuffer.Count} out",
                DronePort dport => $"Drones housed: {dport.Drones}",
                Boiler boil => $"Biomass fuel: {(int)boil.Fuel}",
                SteamEngine eng => eng.CurrentOutput > 0 ? $"Generating +{(int)eng.CurrentOutput}" : "Idle (needs steam)",
                ShieldGen sg => $"Bubble {(int)sg.ShieldHp}/{(int)Bal.ShieldHp}",
                MachineBase m when m is Watchtower => m.Operator != null ? $"Gunner: {m.Operator.Name}" : "UNMANNED",
                MachineBase m => m.Operator != null ? $"Operator: {m.Operator.Name}"
                    : m.NeedsWorker && !game.Has(Tech.Automation) ? "NO OPERATOR" : "Automated",
                Turret t => $"Magazine: {t.Shots}/{t.MagCap}",
                StorageCrate sc => $"Stored: {sc.Items.Count}/{Bal.StorageCap}",
                Hub hub => $"HP {(int)hub.Hp}/{(int)hub.MaxHp} · prisoners {game.Prisoners.Count}",
                WindTurbine wtu => $"Output now: {(int)wtu.Output(game)} power",
                _ => $"HP {(int)b.Hp}/{(int)b.MaxHp}",
            };
            Text(g, Fit(g, status, FSmall, w - 20), FSmall, Pal.Accent, x, y); y += 16;
            if (b is MachineBase m2 && m2.In.Count > 0)
            {
                var ins = string.Join(", ", m2.In.Select(kv => $"{Bal.ItemName(kv.Key)}x{kv.Value}"));
                Text(g, Fit(g, "In: " + ins, FSmall, w - 20), FSmall, Pal.TextDim, x, y);
            }
        }
    }

    private static string GridInfo(Game game, Building b)
    {
        var gr = game.GridOf(b);
        if (gr == null) return "On no grid";
        string s = $"Grid: {(int)gr.Supply}/{(int)gr.Demand} pwr ({(int)(gr.Frac * 100)}%)";
        if (gr.Cap > 0) s += $" · bat {(int)(gr.Stored / gr.Cap * 100)}%";
        return s;
    }

    private static void Bar(Graphics g, float x, float y, float w, string label, float frac, Color col)
    {
        Text(g, label, FSmall, Pal.TextDim, x, y);
        g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), x + 55, y + 3, w - 55, 9);
        g.FillRectangle(Pal.B(col), x + 55, y + 3, (w - 55) * Math.Clamp(frac, 0, 1), 9);
        g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), x + 55, y + 3, w - 55, 9);
    }

    // --------------------------------------------------- hover tooltips ----

    private static int _veinMemoX = int.MinValue, _veinMemoY;
    private static long _veinMemoSerial;
    private static (int tiles, int units) _veinMemo;

    /// <summary>Flood-fill the connected vein from a tile (capped) and sum
    /// the ore units left in it. Memoized: the tooltip recomputes every frame
    /// while hovered, but the vein only changes when the world serial does.</summary>
    private static (int tiles, int units) VeinEstimate(Game game, int tx, int ty)
    {
        if (_veinMemoX == tx && _veinMemoY == ty && _veinMemoSerial == game.World.Serial)
            return _veinMemo;
        var result = VeinEstimateUncached(game, tx, ty);
        _veinMemoX = tx; _veinMemoY = ty; _veinMemoSerial = game.World.Serial;
        _veinMemo = result;
        return result;
    }

    private static (int tiles, int units) VeinEstimateUncached(Game game, int tx, int ty)
    {
        var t0 = game.World.Cell(tx, ty).T;
        var seen = new HashSet<long>();
        var q = new Queue<(int x, int y)>();
        q.Enqueue((tx, ty));
        seen.Add(((long)tx << 32) ^ (uint)ty);
        int tiles = 0, units = 0;
        while (q.Count > 0 && tiles < 250)
        {
            var (x, y) = q.Dequeue();
            tiles++;
            units += game.World.OreAt(x, y);
            for (int d = 0; d < 4; d++)
            {
                var (nx, ny) = DirU.Step(x, y, (Dir)d);
                var key = ((long)nx << 32) ^ (uint)ny;
                if (seen.Contains(key) || !game.World.InBounds(nx, ny)) continue;
                if (game.World.Cell(nx, ny).T != t0) continue;
                seen.Add(key);
                q.Enqueue((nx, ny));
            }
        }
        return (tiles, units);
    }

    private static void DrawHoverTooltip(Graphics g, Game game, ViewState v, Size client)
    {
        if (v.Tool != ToolKind.None || v.Hover != null || v.ShowHelp) return;

        var wt = Camera.ToWorld(v.Mouse.X, v.Mouse.Y);
        int tx = (int)MathF.Floor(wt.X), ty = (int)MathF.Floor(wt.Y);

        if (!game.World.RevealedAt(tx, ty)) return;

        Colonist? col = null; Raider? foe = null;
        foreach (var c in game.Cols)
        {
            if (c.Hp <= 0) continue;
            float d = MathF.Sqrt((c.PosX - wt.X) * (c.PosX - wt.X) + (c.PosY - wt.Y) * (c.PosY - wt.Y));
            if (d < 0.5f) { col = c; break; }
        }
        if (col == null)
            foreach (var r in game.Foes)
            {
                float d = MathF.Sqrt((r.PosX - wt.X) * (r.PosX - wt.X) + (r.PosY - wt.Y) * (r.PosY - wt.Y));
                if (d < (r.Apex ? 0.9f : 0.6f)) { foe = r; break; }
            }
        if (col == null && foe == null && game.Trader is { } tr2)
        {
            float td = MathF.Sqrt((tr2.PosX - wt.X) * (tr2.PosX - wt.X) + (tr2.PosY - wt.Y) * (tr2.PosY - wt.Y));
            if (td < 0.9f)
            {
                DrawHoverLabel(g, v.Mouse, tr2.Arrived
                    ? Loc.T($"Trade caravan - window open {tr2.WindowT:0}s - click to barter")
                    : Loc.T("Trade caravan - still rolling in"));
                return;
            }
        }
        if (col == null && foe == null)
            foreach (var bs in game.Beasts)
            {
                if (bs.Hp <= 0) continue;
                float d = MathF.Sqrt((bs.PosX - wt.X) * (bs.PosX - wt.X) + (bs.PosY - wt.Y) * (bs.PosY - wt.Y));
                if (d < 0.6f)
                {
                    DrawHoverLabel(g, v.Mouse,
                        Loc.T($"Grazer - peaceful - ~{Bal.BeastFood} Food - draft a pawn, right-click to hunt"));
                    return;
                }
            }
        if (col == null && foe == null)
            foreach (var car in game.Carcasses)
            {
                float d = MathF.Sqrt((car.X - wt.X) * (car.X - wt.X) + (car.Y - wt.Y) * (car.Y - wt.Y));
                if (d < 0.6f)
                {
                    DrawHoverLabel(g, v.Mouse,
                        Loc.T($"Carcass - {car.Food} Food - a colonist will haul it"));
                    return;
                }
            }

        if (col == null)
            foreach (var t in game.Trains)
                if (MathF.Abs(t.PosX - wt.X) < 0.8f && MathF.Abs(t.PosY - wt.Y) < 0.5f)
                { DrawHoverLabel(g, v.Mouse, $"Locomotive · cargo {t.CargoCount}/{Bal.TrainCap}"); return; }

        // unfinished blueprint under the cursor: who is on it, how far along?
        if (col == null && foe == null && game.World.InBounds(tx, ty))
            foreach (var bp in game.Blueprints)
                if (tx >= bp.X && tx < bp.X + bp.W && ty >= bp.Y && ty < bp.Y + bp.H)
                {
                    float frac = 1f - Math.Clamp(bp.Work / MathF.Max(0.001f, bp.WorkMax), 0, 1);
                    string who = bp.Builder != null ? bp.Builder.Name : Loc.T("awaiting a builder");
                    DrawHoverLabel(g, v.Mouse,
                        $"{Bal.Name(bp.Kind)} ({Loc.T("blueprint")}, {(int)(frac * 100)}%) - {who}");
                    return;
                }

        Building? b = (col == null && foe == null && game.World.InBounds(tx, ty))
            ? game.World.Cell(tx, ty).B ?? game.World.Cell(tx, ty).B2 : null;

        if (col == null && foe == null && b == null && game.World.InBounds(tx, ty))
        {
            var t = game.World.Cell(tx, ty).T;
            string tname = t switch
            {
                Terrain.IronOre => "Iron Ore deposit",
                Terrain.CopperOre => "Copper Ore deposit",
                Terrain.Crystal => "Exotic Crystal formation",
                Terrain.Flora => "Bioluminescent Flora",
                Terrain.Tree => "Forest",
                Terrain.Rock => "Rock formation",
                Terrain.Water => "Water",
                _ => "",
            };
            if (tname.Length > 0)
            {
                if (t is Terrain.IronOre or Terrain.CopperOre or Terrain.Crystal or Terrain.Flora or Terrain.Tree)
                {
                    var (tiles, units) = VeinEstimate(game, tx, ty);
                    DrawHoverLabel(g, v.Mouse, $"{tname} — ~{units} units · {tiles} tiles");
                }
                else
                DrawHoverLabel(g, v.Mouse, tname);
                if (v.AltDown && t != Terrain.Water)
                    DrawAltCard(g, v.Mouse, tname, new[]
                    {
                        t == Terrain.IronOre ? "Yields Iron Ore." :
                        t == Terrain.CopperOre ? "Yields Copper Ore." :
                        t == Terrain.Crystal ? "Yields Exotic Crystals." :
                        t == Terrain.Tree ? "Fell it for wood (Biomass) - hauled to the hub by hand." :
                        t == Terrain.Flora ? "Yields Biomass (food or boiler fuel)." : "Impassable.",
                        t == Terrain.Rock ? "" :
                        t == Terrain.Tree ? "Mark it with the mine tool [V] to fell it." :
                        "Harvest: place a Mine Drill on it.",
                    });
            }
            return;
        }

        string? label = col?.Name
            ?? (foe != null ? (foe.Apex ? "Apex Horror" : foe.Downed ? "Downed Horror (capturable)" : "Wild Horror")
            : b != null ? Bal.Name(b.Kind) : null);
        if (label == null) return;

        // rotate affordance: R turns the hovered building (empty hand)
        if (b != null && b.W == b.H && v.Sel.Col == null && v.Sel.Cols.Count == 0)
            label += Loc.T(" — R rotates");

        DrawHoverLabel(g, v.Mouse, label);

        if (!v.AltDown) return;

        if (col != null)
            DrawAltCard(g, v.Mouse, $"{col.Name} — colonist", new[]
            {
                $"{string.Join(", ", col.Traits)} · {col.State}",
                $"Skills: {string.Join(" ", Enumerable.Range(0, 6).Select(i => $"{Colonist.StatNames[i]}{col.Stats[i]}{(col.IsPassion(i) ? "!" : "")}"))}",
                $"Hunger {(int)col.Hunger} · Rest {(int)col.Rest} · Morale {(int)col.Morale}",
                col.Job != null ? $"Job: {Bal.Name(col.Job.Kind)}" :
                    col.CaptureTarget != null ? "Capturing a prisoner" : "No job",
                $"Work prefs: {string.Join(" ", Enumerable.Range(0, Bal.WorkCount).Select(i => $"{Bal.WorkName((WorkType)i)[..3]}{col.Priorities[i]}"))}",
            });
        else if (foe != null)
            DrawAltCard(g, v.Mouse, foe.Apex ? "Apex Horror" : "Wild Horror", new[]
            {
                $"HP {(int)foe.Hp}/{(int)foe.MaxHp} · melee {foe.Dps:F0} dps",
                foe.Downed ? "DOWNED — select and click Capture." : "Shoot it; survivors may go down and be captured.",
            });
        else if (b != null)
            DrawAltCard(g, v.Mouse, Bal.Name(b.Kind), new[]
            {
                Bal.Blurb(b.Kind),
                Bal.Needs(b.Kind),
                b.PowerDemand > 0 ? $"Power: {(int)(game.PowerFracFor(b) * 100)}% of grid supply" : "",
                b is Turret t ? $"Magazine {t.Shots}/{t.MagCap}" :
                b is MachineBase m ? (m.Operator != null ? $"Operator: {m.Operator.Name}" :
                    m.NeedsWorker && !game.Has(Tech.Automation) ? "Needs an operator!" : "Automated") :
                b is Building bb ? $"HP {(int)bb.Hp}/{(int)bb.MaxHp}" : "",
            });
    }

    private static void DrawHoverLabel(Graphics g, Point mouse, string text)
    {
        text = Fit(g, text, FSmall, 300);
        var size = g.MeasureString(text, FSmall);
        float lx = Math.Min(mouse.X + 12, Math.Max(4, g.VisibleClipBounds.Width - size.Width - 14));
        g.FillRectangle(Pal.B(Pal.CA(225, Pal.Panel)), lx, mouse.Y - 22, size.Width + 10, 17);
        g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), lx, mouse.Y - 22, size.Width + 10, 17);
        Text(g, text, FSmall, Pal.Text, lx + 5, mouse.Y - 19);
    }

    private static void DrawAltCard(Graphics g, Point mouse, string title, string[] lines)
    {
        int w = 320;
        foreach (var l in lines) w = Math.Max(w, (int)g.MeasureString(l, FSmall).Width + 24);
        w = Math.Min(w, 400);
        // wrap anything longer than the card rather than bleeding out of it
        var wrapped = new List<string>();
        foreach (var l in lines) wrapped.AddRange(Wrap(l, g, FSmall, w - 16));
        int h = 30 + wrapped.Count * 15;
        int x = mouse.X + 16, y = mouse.Y + 6;
        if (x + w > g.VisibleClipBounds.Width - 6) x = Math.Max(6, (int)g.VisibleClipBounds.Width - w - 6);
        var r = new Rectangle(x, y, w, h);
        g.FillRectangle(Pal.B(Pal.CA(245, Pal.Panel)), r);
        g.DrawRectangle(Pal.P(Pal.Accent), r.X, r.Y, r.Width, r.Height);
        Text(g, Fit(g, title, FBold, w - 16), FBold, Pal.Accent, x + 8, y + 6);
        float ty = y + 24;
        foreach (var l in wrapped)
        {
            if (l.Length == 0) continue;
            Text(g, l, FSmall, Pal.TextDim, x + 8, ty);
            ty += 15;
        }
    }

    // -------------------------------------------------------------- modals --

    private static void DrawModalVeil(Graphics g, ViewState v, Size client)
    {
        int alpha = v.App == AppState.Playing ? 160 : 200;
        g.FillRectangle(Pal.B(Pal.CA(alpha, v.App == AppState.Playing ? Pal.C(0, 0, 0) : Pal.C(8, 10, 14))),
            0, 0, client.Width, client.Height);
        Text(g, v.ModalTitle!, FTitle, Pal.Accent, client.Width / 2f,
            v.App == AppState.Playing ? client.Height / 2f - 180 : 90, center: true);
    }

    private static void DrawResearchPanel(Graphics g, Game game, ViewState v, Size client)
    {
        int w = 640, h = 560;   // 9 rows × 48 + header + footer — 500 clipped the last row
        var r = new Rectangle(client.Width / 2 - w / 2, client.Height / 2 - h / 2 + 20, w, h);
        g.FillRectangle(Pal.B(Pal.CA(250, Pal.Panel)), r);
        g.DrawRectangle(Pal.P(Pal.Accent, 1.5f), r.X, r.Y, r.Width, r.Height);

        Text(g, "RESEARCH — science packs required", FBig, Pal.Accent, r.X + 20, r.Y + 14);
        Text(g, "Fabricate Science Packs (1 Iron + 1 Copper plate), belt them into a staffed",
            FSmall, Pal.TextDim, r.X + 20, r.Y + 46);
        Text(g, "Lab, then pick a project. Click a project to make it active.",
            FSmall, Pal.TextDim, r.X + 20, r.Y + 62);

        int y = r.Y + 88;
        for (int i = 0; i < Bal.TechCount; i++)
        {
            var t = (Tech)i;
            bool done = game.TechDone[i];
            bool leveled = LeveledTech.IsLeveled(t);
            int lvl = game.TechLevels[i];
            bool active = game.ActiveTech == t && !done;
            var pre = Bal.TechPrereq(t);
            bool locked = pre != Tech.None && !game.Has(pre);

            var row = new Rectangle(r.X + 12, y, r.Width - 24, 44);
            var border = done ? Pal.Good : active ? Pal.Accent : locked ? Pal.C(60, 66, 74) : Pal.C(52, 61, 71);
            g.FillRectangle(Pal.B(Pal.CA(active ? 235 : 210, Pal.C(28, 34, 42))), row);
            g.DrawRectangle(Pal.P(border, active ? 1.8f : 1f), row.X, row.Y, row.Width, row.Height);

            string title = (done ? "[MAX] " : active ? "▶ " : "") + Bal.TechName(t)
                + (leveled ? $"  Lv{lvl}" : "");
            Text(g, Fit(g, title, FBold, row.Width - 220), FBold,
                done ? Pal.Good : locked ? Pal.TextDim : Pal.Text, row.X + 10, row.Y + 5);
            Text(g, Fit(g, Bal.TechBlurb(t), FSmall, row.Width - 220), FSmall, Pal.TextDim, row.X + 10, row.Y + 22);

            string right = done ? "MAXED"
                : locked ? $"Locked — needs {Bal.TechName(pre)}"
                : $"{Bal.PackCost(t, lvl)} packs";
            right = Fit(g, right, FSmall, 186);
            Text(g, right, FSmall, done ? Pal.Good : locked ? Pal.Bad : Pal.ItemColor(ItemKind.SciencePack),
                row.Right - 12 - g.MeasureString(right, FSmall).Width, row.Y + 5);

            if (!done)
            {
                float frac = Math.Clamp(game.TechProg[i] / Bal.TechCost(t, lvl), 0, 1);
                float bw = 170;
                g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), row.Right - 12 - bw, row.Y + 26, bw, 9);
                g.FillRectangle(Pal.B(Pal.ItemColor(ItemKind.SciencePack)), row.Right - 12 - bw, row.Y + 26, bw * frac, 9);
                g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), row.Right - 12 - bw, row.Y + 26, bw, 9);
            }
            y += 48;
        }
        Text(g, "Esc / T to close", FSmall, Pal.TextDim, r.X + 20, r.Bottom - 22);
    }

    /// <summary>RimWorld-style work priority table (click cells to cycle 1..4).</summary>
    private static void DrawWorkPanel(Graphics g, Game game, ViewState v, Size client)
    {
        int w = 660, rows = Math.Min(game.Cols.Count, 9);
        int h = 110 + rows * 34;
        var r = new Rectangle(client.Width / 2 - w / 2, client.Height / 2 - h / 2, w, h);
        g.FillRectangle(Pal.B(Pal.CA(250, Pal.Panel)), r);
        g.DrawRectangle(Pal.P(Pal.Accent, 1.5f), r.X, r.Y, r.Width, r.Height);

        Text(g, "WORK PRIORITIES", FBig, Pal.Accent, r.X + 20, r.Y + 14);
        Text(g, "1 = first choice · 4 = never. Click a cell to cycle. Assignments respect these.",
            FSmall, Pal.TextDim, r.X + 20, r.Y + 46);

        int nameW = 140, cellW = (w - nameW - 30) / Bal.WorkCount;
        int hy = r.Y + 74;
        for (int wi = 0; wi < Bal.WorkCount; wi++)
            Text(g, Bal.WorkName((WorkType)wi), FBold, Pal.TextDim, r.X + 20 + nameW + wi * cellW, hy, center: true);

        // row labels (priority number cells are buttons made by MainForm)
        for (int ci = 0; ci < rows; ci++)
        {
            var c = game.Cols[ci];
            Text(g, Fit(g, c.Name, FBold, nameW - 10), FBold, Pal.Colonist, r.X + 20, r.Y + 70 + ci * 34 + 7);
            Text(g, c.PassionText, FSmall, Pal.ItemColor(ItemKind.SciencePack),
                r.X + 20, r.Y + 70 + ci * 34 + 22);
        }

        // per-colonist priority numbers are drawn as tiny buttons created by MainForm
        Text(g, "Esc / P to close", FSmall, Pal.TextDim, r.X + 20, r.Bottom - 22);
    }

    // ------------------------------------------------------- world-gen UI --

    private static void DrawWorldGen(Graphics g, ViewState v, Size client)
    {
        DrawMainMenuBackground(g, client);
        TextShadow(g, "GENERATE WORLD", FTitle, Pal.Accent, client.Width / 2f, 64, center: true);
        TextShadow(g, "same seed + same settings = the same world, forever", FSmall, Pal.TextDim,
            client.Width / 2f, 96, center: true);

        int leftW = 400;
        int x0 = client.Width / 2 - (leftW + 20 + 260) / 2;

        var col = new Rectangle(x0, 130, leftW, client.Height - 200);
        g.FillRectangle(Pal.B(Pal.CA(235, Pal.Panel)), col);
        g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), col.X, col.Y, col.Width, col.Height);

        var box = v.SeedBox;
        if (box != null)
        {
            var r2 = new Rectangle(col.X + 16, col.Y + 16, col.Width - 32, 34);
            box.R = r2;
            g.FillRectangle(Pal.B(Pal.CA(box.Focused ? 255 : 220, Pal.C(16, 20, 26))), r2);
            g.DrawRectangle(Pal.P(box.Focused ? Pal.Accent : Pal.C(52, 61, 71), box.Focused ? 2f : 1f), r2);
            Text(g, box.Label, FSmall, Pal.TextDim, r2.X + 8, r2.Y + 3);
            string shown = box.Text.Length > 0 ? box.Text : "random";
            var col2 = box.Text.Length > 0 ? Pal.Text : Pal.TextDim;
            Text(g, shown, FBold, col2, r2.X + 8, r2.Y + 14);
            if (box.Focused && (Environment.TickCount / 400) % 2 == 0)
            {
                var tw = g.MeasureString(shown, FBold).Width;
                g.FillRectangle(Pal.B(Pal.Accent), r2.X + 10 + tw, r2.Y + 15, 7, 13);
            }
        }

        float sy = col.Y + 66;
        foreach (var s in v.Sliders)
        {
            s.R = new Rectangle(col.X + 16, (int)sy, col.Width - 32, 44);
            Text(g, s.Label, FSmall, Pal.TextDim, s.R.X, s.R.Y);
            string val = s.Value.ToString(s.Fmt) + "x";
            Text(g, val, FBold, Pal.Accent, s.R.Right - g.MeasureString(val, FBold).Width, s.R.Y);

            float tx = s.R.X + 6, tw2 = s.R.Width - 12;
            float hy = s.R.Y + 26;
            g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), tx, hy, tw2, 6);
            g.FillRectangle(Pal.B(Pal.Accent), tx, hy, tw2 * s.Frac, 6);
            g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), tx, hy, tw2, 6);
            float hx = tx + tw2 * s.Frac;
            g.FillRectangle(Pal.B(Pal.PanelLight), hx - 5, hy - 6, 10, 18);
            g.DrawRectangle(Pal.P(Pal.Text), hx - 5, hy - 6, 10, 18);
            sy += 52;
        }

        Text(g, "storyteller shapes raids & events →", FSmall, Pal.TextDim, col.X + 16, sy + 4);

        // live terrain preview (right column)
        int px = x0 + leftW + 20;
        if (v.GenPreview != null)
        {
            var pr = new Rectangle(px, 130, 260, 200);
            var old = g.InterpolationMode;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.DrawImage(v.GenPreview, pr);
            g.InterpolationMode = old;
            g.DrawRectangle(Pal.P(Pal.C(70, 80, 92)), pr.X, pr.Y, pr.Width, pr.Height);
            Text(g, "seed " + v.GenCaption, FSmall, Pal.TextDim, pr.X, pr.Bottom + 4);
            Text(g, Loc.T("click the map to pick your landing site"), FSmall, Pal.Accent, pr.X, pr.Bottom + 18);

            if (v.LandPicked)
            {
                float ppx = 65f + v.LandWorldX / 6f, ppy = 50f + v.LandWorldY / 6f;
                float mx = pr.X + ppx * pr.Width / 130f, my = pr.Y + ppy * pr.Height / 100f;
                using var ret = new Pen(Pal.Accent, 2f);
                g.DrawEllipse(ret, mx - 7, my - 7, 14, 14);
                g.DrawLine(ret, mx - 12, my, mx - 9, my);
                g.DrawLine(ret, mx + 9, my, mx + 12, my);
                g.DrawLine(ret, mx, my - 12, mx, my - 9);
                g.DrawLine(ret, mx, my + 9, mx, my + 12);
                Text(g, Loc.T("LAND HERE"), FSmall, Pal.Accent, mx + 14, my - 7);
            }
        }

        foreach (var b in v.Buttons) DrawButton(g, b, v.Hover == b.Id);
    }

    // ------------------------------------------------------------ overlays --

    private static void DrawHelp(Graphics g, Size client)
    {
        int w = Math.Min(680, client.Width - 16);
        int h = Math.Min(520, client.Height - 60);
        var r = new Rectangle(client.Width / 2 - w / 2, 42, w, h);
        g.FillRectangle(Pal.B(Pal.CA(250, Pal.Panel)), r);
        g.DrawRectangle(Pal.P(Pal.Accent, 1.5f), r.X, r.Y, r.Width, r.Height);
        Text(g, "FORGEHAVEN — CONTROLS & HELP", FBig, Pal.Accent, r.X + 20, r.Y + 16);

        string help = string.Join("\n", new[]
        {
            "",
            "PAN WASD/arrows/middle-drag   ZOOM wheel   SPEED , .   PAUSE space",
            "BUILD Architect bar (bottom) or 1-5; drag paints belts/rails/pipes",
            "ROTATE R (belt/arm direction)   BULLDOZE X (50% refund)",
            "INSPECT hover; ALT = detail card   SELECT click (raiders too!)",
            "VIEWS F1 power · F2 defense · F3 logistics · F4 pollution",
            "RESEARCH T   WORK PRIORITIES P   MINIMAP M   MENU Esc",
            "",
            "POWER    Poles wire within 7 tiles, feed within 5. The Hub feeds 8.",
            "         Each grid balances itself — watch F1.",
            "FLUIDS   Pump on water -> pipes -> Boiler (burns biomass) -> steam",
            "         pipes -> Steam Engine = +90 power. Tanks buffer.",
            "TRAINS   Lay Rail between two Stations, drop a Locomotive on it.",
            "         It loads one station, unloads into the other.",
            "DRONES   Craft Drones (fab recipe 3) and belt them into a Drone",
            "         Port: they ferry crate->machine and repair damage.",
            "ARMY     Craft War Bots -> Bot Factory. Select factory, Set Rally.",
            "PRISONERS Turrets sometimes DOWN raiders. Click the body, Capture;",
            "         feed them at the Hub and they may join the colony.",
            "",
            "GOAL     Deliver " + Bal.PartsToWin + " Advanced Parts to the Hub.",
            "         Pollution angers the wildlife — and aims it at you.",
        });
        Text(g, help, FNorm, Pal.Text, r.X + 20, r.Y + 52);
    }

    private static void DrawWin(Graphics g, Game game, Size client)
    {
        float cx = client.Width / 2f, cy = client.Height / 2f;
        g.FillRectangle(Pal.B(Pal.CA(170, Pal.C(0, 0, 0))), 0, 0, client.Width, client.Height);

        if (game.FinaleCleared)
        {
            if (game.EndingChosen == 1)
            {
                Text(g, Fit(g, "THE BEACON BURNS", FTitle, client.Width - 40), FTitle, Pal.Good, cx, cy - 90, center: true);
                Text(g, Fit(g, $"The ark's last signal reached the stars on Day {game.Day}.", FBig, client.Width - 40),
                    FBig, Pal.Text, cx, cy - 40, center: true);
                Text(g, Loc.T("Rescue is coming. The colony will be a city someday."), FNorm, Pal.TextDim, cx, cy, center: true);
                Text(g, Loc.T("They will tell this story for a thousand years."), FNorm, Pal.TextDim, cx, cy + 26, center: true);
            }
            else if (game.EndingChosen == 2)
            {
                Text(g, Fit(g, "THIS WORLD IS HOME NOW", FTitle, client.Width - 40), FTitle, Pal.Accent, cx, cy - 80, center: true);
                Text(g, Fit(g, "They stayed. The blight recedes; hope spreads with the lamplight.", FBig, client.Width - 40),
                    FBig, Pal.Text, cx, cy - 30, center: true);
                Text(g, Loc.T("The colony plays on - [N] for a new world whenever you're ready."), FNorm, Pal.TextDim, cx, cy, center: true);
            }
            else
            {
                Text(g, Fit(g, "THE SILENCE IS BROKEN", FTitle, client.Width - 40), FTitle, Pal.Good, cx, cy - 110, center: true);
                Text(g, Fit(g, $"The last horde broke on your walls on Day {game.Day}.", FBig, client.Width - 40),
                    FBig, Pal.Text, cx, cy - 60, center: true);
                Text(g, Loc.T("Inside the open ark: a beacon, and enough fuel for one message."), FNorm, Pal.Text, cx, cy - 10, center: true);
                Text(g, Loc.T("Or tear it out, plant it here, and never leave."), FNorm, Pal.TextDim, cx, cy + 14, center: true);
                Text(g, Loc.T("The choice belongs to the colony."), FBold, Pal.Accent, cx, cy + 52, center: true);
            }
            return;
        }

        Text(g, Fit(g, "SIGNAL BEACON ONLINE", FTitle, client.Width - 40), FTitle, Pal.Good, cx, cy - 80, center: true);
        Text(g, Fit(g, $"{Bal.PartsToWin} Advanced Parts delivered on Day {game.Day}.", FBig, client.Width - 40),
            FBig, Pal.Text, cx, cy - 30, center: true);
        Text(g, "The galaxy knows ForgeHaven is alive.", FNorm, Pal.TextDim, cx, cy, center: true);
    }

    private static void DrawLose(Graphics g, Game game, Size client)
    {
        float cx = client.Width / 2f, cy = client.Height / 2f;
        g.FillRectangle(Pal.B(Pal.CA(170, Pal.C(0, 0, 0))), 0, 0, client.Width, client.Height);
        Text(g, Fit(g, "THE HUB HAS FALLEN", FTitle, client.Width - 40), FTitle, Pal.Bad, cx, cy - 80, center: true);
        Text(g, Fit(g, $"Colony survived to Day {game.Day}, Wave {game.Wave}.", FBig, client.Width - 40),
            FBig, Pal.Text, cx, cy - 30, center: true);
        Text(g, "The planet keeps the wreck. Press N or use the menu to try again.", FNorm, Pal.TextDim, cx, cy, center: true);
    }

    // ------------------------------------------------------------- helpers --

    /// <summary>Trim s (with an ellipsis) so it paints no wider than maxW.</summary>
    private static string Fit(Graphics g, string s, Font f, float maxW)
    {
        if (string.IsNullOrEmpty(s) || maxW <= 8) return s;
        if (g.MeasureString(s, f).Width <= maxW) return s;
        while (s.Length > 1 && g.MeasureString(s + "…", f).Width > maxW)
            s = s[..^1];
        return s + "…";
    }

    /// <summary>Text with a soft drop shadow (for text painted over terrain).</summary>
    private static void TextShadow(Graphics g, string s, Font f, Color c, float x, float y, bool center = false)
    {
        if (string.IsNullOrEmpty(s)) return;
        var shadow = Pal.B(Pal.CA(190, Pal.C(5, 8, 12)));
        if (center)
        {
            var size = g.MeasureString(s, f);
            g.DrawString(s, f, shadow, x - size.Width / 2 + 2, y - size.Height / 2 + 2);
            g.DrawString(s, f, Pal.B(c), x - size.Width / 2, y - size.Height / 2);
        }
        else
        {
            g.DrawString(s, f, shadow, x + 2, y + 2);
            g.DrawString(s, f, Pal.B(c), x, y);
        }
    }

    private static void Text(Graphics g, string s, Font f, Color c, float x, float y, bool center = false)
    {
        if (string.IsNullOrEmpty(s)) return;
        if (center)
        {
            var size = g.MeasureString(s, f);
            g.DrawString(s, f, Pal.B(c), x - size.Width / 2, y - size.Height / 2);
        }
        else g.DrawString(s, f, Pal.B(c), x, y);
    }

    private static string[] Wrap(string s, Graphics g, Font f, int width)
    {
        var outp = new List<string>();
        foreach (var raw in s.Split('\n'))
        {
            var words = raw.Split(' ');
            var cur = "";
            foreach (var w in words)
            {
                var probe = cur.Length == 0 ? w : cur + " " + w;
                if (g.MeasureString(probe, f).Width > width && cur.Length > 0)
                {
                    outp.Add(cur);
                    cur = w;
                }
                else cur = probe;
            }
            outp.Add(cur);
        }
        return outp.ToArray();
    }
}
