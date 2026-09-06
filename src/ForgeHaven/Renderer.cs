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
            Text(g, "FORGEHAVEN", FMenuTitle, Pal.Accent, client.Width / 2f, client.Height * 0.20f, center: true);
            Text(g, "build the machine — don't forget the people inside it",
                FNorm, Pal.TextDim, client.Width / 2f, client.Height * 0.20f + 46, center: true);
            Text(g, "prototype slice · v0.4", FSmall, Pal.TextDim, client.Width / 2f, client.Height * 0.20f + 68, center: true);

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
        DrawTiles(g, game, client, v.ShowGrid);
        DrawBuildings(g, game, client);
        DrawPrisoners(g, game);
        DrawColonists(g, game, v);
        DrawRaiders(g, game, client);
        DrawTrains(g, game);
        DrawBots(g, game);
        DrawFx(g, game);
        DrawElevated(g, game, client);
        if (v.GhostVisible) DrawGhost(g, v);

        // night veil + warm light sources
        if (v.Sunlight < 0.98f)
            DrawNight(g, game, v, client);

        // ---- map overlay layers (F1..F4) ----
        switch (v.Ovl)
        {
            case OverlayMode.Power: DrawPowerOverlay(g, game, client); break;
            case OverlayMode.Defense: DrawDefenseOverlay(g, game, client); break;
            case OverlayMode.Logistics: DrawLogisticsOverlay(g, game, client); break;
            case OverlayMode.Pollution: DrawPollutionOverlay(g, game, client); break;
        }

        // ---- UI layer ----
        DrawTopBar(g, game, client.Width, v);
        DrawAlerts(g, v, game);
        foreach (var b in v.Buttons) DrawButton(g, b, v.Hover == b.Id);
        DrawBuildTooltip(g, v, client);
        DrawLog(g, game, client);
        DrawSelectionPanel(g, game, v, client);
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
        }
    }

    // ------------------------------------------------------------ menu bg --

    private static void DrawMainMenuBackground(Graphics g, Size client)
    {
        for (int i = 0; i < 32; i++)
        {
            float t = i / 31f;
            var c = Pal.C((int)(16 + 18 * t), (int)(20 + 14 * t), (int)(28 + 26 * t));
            g.FillRectangle(Pal.B(c), 0, i * client.Height / 32, client.Width, client.Height / 32 + 1);
        }
        float tt = Environment.TickCount / 1000f;
        for (int i = 0; i < 60; i++)
        {
            long h = i * 2654435761L;
            float x = ((h & 1023) / 1023f) * client.Width;
            float y = ((((h >> 10) & 1023) / 1023f) + tt * (0.008f + (i % 5) * 0.004f)) % 1f;
            var col = i % 7 == 0 ? Pal.CA(110, Pal.Flora) : Pal.CA(70, Pal.Accent);
            g.FillRectangle(Pal.B(col), x, y * client.Height, 2, 2);
        }
    }

    // --------------------------------------------------------------- world --

    private static void DrawTiles(Graphics g, Game game, Size client, bool showGrid)
    {
        float sz = Camera.Sz;
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
                        g.DrawImage(Sprites.Ground[vId], dest);

                        int decorPick = ((x * 73 + y * 151) % 23 + 23) % 23;
                        if (decorPick < 3)
                            g.DrawImage(Sprites.Decor[decorPick], dest);

                        switch (t.T)
                        {
                            case Terrain.Rock: g.DrawImage(Sprites.Rock, dest); break;
                            case Terrain.IronOre: g.DrawImage(Sprites.IronOre, dest); break;
                            case Terrain.CopperOre: g.DrawImage(Sprites.CopperOre, dest); break;
                            case Terrain.Crystal: g.DrawImage(Sprites.Crystal, dest); break;
                            case Terrain.Flora: g.DrawImage(Sprites.Flora, dest); break;
                            case Terrain.Water: g.DrawImage(Sprites.Water, dest); break;
                        }
                        if (showGrid)
                            g.DrawRectangle(Pal.P(Pal.CA(38, Pal.Grid)), p.X, p.Y, sz, sz);
                    }
            }

        g.InterpolationMode = oldInterp;
    }

    private static void DrawImageAlpha(Graphics g, Bitmap b, Rectangle r, float alpha)
    {
        using var ia = BakedAlpha(alpha);
        g.DrawImage(b, r, 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
    }

    private static void DrawBuildings(Graphics g, Game game, Size client)
    {
        float sz = Camera.Sz;
        var oldInterp = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        foreach (var b in game.Builds)
        {
            if (b.Kind == BuildKind.ElevatedRail) continue;   // drawn in a later pass

            var p = Camera.S(b.X, b.Y);
            if (p.X > client.Width || p.Y > client.Height ||
                p.X + b.W * sz < 0 || p.Y + b.H * sz < 0) continue;

            var dest = new Rectangle((int)p.X, (int)p.Y,
                (int)(b.W * sz + 1), (int)(b.H * sz + 1));

            if (b is Belt belt)
            {
                var img = b is FastBelt ? Sprites.FastBelt[(int)belt.Face] : Sprites.Belt[(int)belt.Face];
                g.DrawImage(img, dest);
                DrawBeltItems(g, belt.Lane, belt.X, belt.Y, belt.Face, sz);
                continue;
            }
            if (b is Hub hub) { DrawHub(g, hub, dest, game); continue; }
            if (b is Junction jn)
            {
                g.DrawImage(Sprites.Building(BuildKind.Junction), dest);
                DrawBeltItems(g, jn.Main, jn.X, jn.Y, jn.Face, sz);
                DrawBeltItems(g, jn.Cross, jn.X, jn.Y, jn.CrossDir, sz);
                continue;
            }
            if (b is Rail rail)
            {
                g.DrawImage(Sprites.Rail[(int)rail.Face], dest);
                continue;
            }
            if (b is Splitter sp)
            {
                g.DrawImage(Sprites.Building(BuildKind.Splitter), dest);
                DrawFacingTick(g, b, sz);
                if (sp.Held != null) DrawItemDot(g, b.X + 0.5f, b.Y + 0.5f, sp.Held.Value, sz);
                continue;
            }
            if (b is FilterSplitter fsp)
            {
                g.DrawImage(Sprites.Building(BuildKind.FilterSplitter), dest);
                DrawFacingTick(g, b, sz);
                // filter chip
                DrawItemDot(g, b.X + 0.28f, b.Y + 0.28f, fsp.Filter, sz * 0.8f);
                if (fsp.Held != null) DrawItemDot(g, b.X + 0.5f, b.Y + 0.6f, fsp.Held.Value, sz);
                continue;
            }
            if (b is OverflowRouter orr)
            {
                g.DrawImage(Sprites.Building(BuildKind.OverflowRouter), dest);
                DrawFacingTick(g, b, sz);
                if (orr.Held != null) DrawItemDot(g, b.X + 0.5f, b.Y + 0.5f, orr.Held.Value, sz);
                continue;
            }
            if (b is Merger mg)
            {
                g.DrawImage(Sprites.Building(BuildKind.Merger), dest);
                DrawFacingTick(g, b, sz);
                if (mg.Held != null) DrawItemDot(g, b.X + 0.5f, b.Y + 0.5f, mg.Held.Value, sz);
                continue;
            }
            if (b is Inserter ins)
            {
                // rotate the arm sprite to face its target
                var st = g.Save();
                g.TranslateTransform(dest.X + dest.Width / 2f, dest.Y + dest.Height / 2f);
                g.RotateTransform((int)ins.Face * 90);
                int off = ins.Swing > 0 ? (int)(MathF.Sin(ins.Swing * MathF.PI) * 4) : 0;
                g.TranslateTransform(-off, 0);
                g.DrawImage(Sprites.Building(BuildKind.Inserter),
                    -dest.Width / 2f, -dest.Height / 2f, dest.Width, dest.Height);
                g.Restore(st);
                if (ins.Held != null)
                    DrawItemDot(g, b.X + 0.5f, b.Y + 0.5f, ins.Held.Value, sz);
                continue;
            }
            if (b is Pipe pipe)
            {
                g.DrawImage(Sprites.Building(BuildKind.Pipe), dest);
                DrawPipeLinks(g, game, pipe, sz);
                continue;
            }
            if (b is TrainStop st2)
            {
                g.DrawImage(Sprites.Building(BuildKind.TrainStop), dest);
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
                g.DrawImage(Sprites.Building(BuildKind.DronePort), dest);
                if (Camera.Zoom >= 0.75f)
                    Text(g, $"\u2042{dp.Drones}", FBold, Pal.C(230, 230, 160), dest.Right - 10, dest.Y + 2);
                DrawCoverDot(g, b, sz, Bal.DronePortCover, Pal.C(230, 230, 160));
                continue;
            }
            if (b is Pump || b is Tank || b is Boiler || b is SteamEngine)
            {
                g.DrawImage(Sprites.Building(b.Kind), dest);
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
                g.DrawImage(Sprites.Building(BuildKind.BotFactory), dest);
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
                g.DrawImage(Sprites.Building(BuildKind.PowerPole), dest);
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

            g.DrawImage(Sprites.Building(b.Kind), dest);

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
            if (b is StorageCrate sc && sc.Items.Count > 0 && Camera.Zoom >= 0.8f)
                Text(g, sc.Items.Count.ToString(), FSmall, Pal.Text, dest.X + dest.Width - 8, dest.Y + 4);

            if (b is MachineBase m)
            {
                DrawFacingTick(g, m, sz);
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
            g.DrawImage(Sprites.ElevatedRail[(int)b.Face], dest);
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
        float r = sz * 0.14f;
        g.FillRectangle(Pal.B(Pal.C(20, 24, 28)), ip.X - r - 1, ip.Y - r - 1, r * 2 + 2, r * 2 + 2);
        g.FillRectangle(Pal.B(Pal.ItemColor(k)), ip.X - r, ip.Y - r, r * 2, r * 2);
    }

    private static void DrawBeltItems(Graphics g, List<BeltItem> lane, int bx, int by, Dir face, float sz)
    {
        int dx = DirU.Dx[(int)face], dy = DirU.Dy[(int)face];
        foreach (var it in lane)
            DrawItemDot(g, bx + 0.5f + dx * (it.Prog - 0.5f), by + 0.5f + dy * (it.Prog - 0.5f), it.Kind, sz);
    }

    private static void DrawHub(Graphics g, Hub hub, Rectangle dest, Game game)
    {
        g.DrawImage(Sprites.Hub, dest);
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
        foreach (var c in game.Cols)
        {
            if (c.Hp <= 0) continue;

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
            var sprite = Sprites.Pawn(c.ShirtTone, c.SkinTone, c.HairTone,
                sleeping ? Dir.Up : c.FaceDir);

            bool selected = v.Sel.Col == c;
            if (selected)
                g.DrawEllipse(Pal.P(Pal.Accent, 2f), dest.X - 3, dest.Bottom - w * 0.28f, w + 6, w * 0.3f);

            var oldInterp = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            if (sleeping || c.State is ColState.Escort or ColState.GoCapture && c.CaptureTarget != null)
                DrawImageAlpha(g, sprite, dest, 0.9f);
            else g.DrawImage(sprite, dest);
            g.InterpolationMode = oldInterp;

            if (c.FlashT > 0)
            {
                int a = (int)(c.FlashT / 0.15f * 150);
                g.FillEllipse(Pal.B(Pal.CA(a, Pal.C(255, 255, 255))), dest.X, dest.Y, w, w);
            }

            Color mc = c.Morale > 60 ? Pal.Good : c.Morale > 30 ? Pal.Warn : Pal.Bad;
            g.DrawArc(Pal.P(mc, 1.6f), p.X - sz * 0.42f + ox, p.Y - sz * 0.42f + oy,
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

    private static void DrawGhost(Graphics g, ViewState v)
    {
        float sz = Camera.Sz;
        var p = Camera.S(v.GhostX, v.GhostY);
        var dest = new Rectangle((int)p.X, (int)p.Y, (int)(sz + 1), (int)(sz + 1));

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
        int alpha = (int)((1f - v.Sunlight) * 150);
        g.FillRectangle(Pal.B(Pal.CA(alpha, Pal.C(6, 9, 20))), 0, 0, client.Width, client.Height);

        // warm light pools
        void Glow(float wx, float wy, float radius, Color col)
        {
            var c = Camera.S(wx, wy);
            float r = radius * Camera.Sz;
            foreach (var (rr, aa) in new[] { (1f, 26), (0.66f, 40), (0.36f, 56) })
                g.FillEllipse(Pal.B(Pal.CA((int)(aa * (1f - v.Sunlight)), col)),
                    c.X - r * rr, c.Y - r * rr, r * 2 * rr, r * 2 * rr);
        }

        foreach (var b in game.Builds)
        {
            if (b is Lamp && game.PowerFracFor(b) > 0.3f)
                Glow(b.X + 0.5f, b.Y + 0.5f, 2.4f, Pal.C(255, 220, 140));
            else if (b is Hub)
                Glow(b.X + 1.5f, b.Y + 1.5f, 4f, Pal.C(120, 200, 255));
            else if (b is TrainStop)
                Glow(b.X + 0.5f, b.Y + 0.5f, 2f, Pal.C(255, 220, 140));
            else if (b is DronePort && game.PowerFracFor(b) > 0.3f)
                Glow(b.X + 0.5f, b.Y + 0.5f, 2f, Pal.C(230, 230, 160));
        }
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

        // stock chips — most important first so narrow windows keep them
        var st = game.HubRef.Stock;
        var items = new (ItemKind k, string ab)[]
        {
            (ItemKind.AdvPart, "PART"),
            (ItemKind.IronPlate, "Fe"),
            (ItemKind.CopperPlate, "Cu"),
            (ItemKind.SciencePack, "Sci"),
            (ItemKind.Ammo, "Amo"),
            (ItemKind.Food, "Foo"),
            (ItemKind.Drone, "Drn"),
            (ItemKind.WarBot, "Bot"),
        };
        foreach (var (k, ab) in items)
            Chip($"{ab} {st[(int)k]}", Pal.ItemColor(k), FSmall);

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

    private static void DrawAlerts(Graphics g, ViewState v, Game game)
    {
        // chips themselves are buttons made by MainForm; just shade behind them
        if (game.Alerts.Count == 0) return;
        int h = Math.Min(game.Alerts.Count, 8) * 24 + 4;
        g.FillRectangle(Pal.B(Pal.CA(150, Pal.C(15, 18, 22))), 6, 42, 320, h);
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
            g.FillRectangle(Pal.B(hover ? Pal.PanelLight : Pal.CA(230, Pal.C(28, 33, 40))), b.R);
            g.FillRectangle(Pal.B(col), b.R.X, b.R.Y + 3, 3, b.R.Height - 6);
            var txt = Fit(g, b.Text, FSmall, b.R.Width - 16);
            g.DrawString(txt, FSmall, Pal.B(col), b.R.X + 9, b.R.Y + b.R.Height / 2 - 7);
            return;
        }

        var bg = !b.Enabled ? Pal.CA(140, Pal.Panel)
               : b.Active ? Pal.CA(255, Pal.C(40, 74, 94))
               : hover ? Pal.CA(255, Pal.PanelLight)
               : Pal.CA(220, Pal.Panel);
        g.FillRectangle(Pal.B(bg), b.R);
        g.DrawRectangle(Pal.P(b.Active ? Pal.Accent : Pal.C(52, 61, 71)), b.R.X, b.R.Y, b.R.Width, b.R.Height);

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
        g.FillRectangle(Pal.B(Pal.CA(245, Pal.Panel)), r);
        g.DrawRectangle(Pal.P(Pal.Accent), r.X, r.Y, r.Width, r.Height);
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

    private static void DrawSelectionPanel(Graphics g, Game game, ViewState v, Size client)
    {
        if (v.Sel.B == null && v.Sel.Col == null && v.Sel.Foe == null) return;

        int w = 250, h = 196;
        var r = new Rectangle(client.Width - w - 10, 36, w, h);
        g.FillRectangle(Pal.B(Pal.CA(235, Pal.Panel)), r);
        g.DrawRectangle(Pal.P(Pal.C(52, 61, 71)), r.X, r.Y, r.Width, r.Height);

        float x = r.X + 10, y = r.Y + 8;
        if (v.Sel.Col is { } c)
        {
            Text(g, $"{c.Name}  —  {c.State}", FBold, Pal.Colonist, x, y); y += 18;
            Text(g, string.Join(", ", c.Traits), FSmall, Pal.TextDim, x, y); y += 15;
            Text(g, $"Passions: {c.PassionText} (2x xp)", FSmall, Pal.ItemColor(ItemKind.SciencePack), x, y); y += 17;
            for (int i = 0; i < 6; i++)
            {
                bool pass = c.IsPassion(i);
                Text(g, $"{Colonist.StatNames[i]} {c.Stats[i]}{(pass ? "\u2764" : "")}",
                    FSmall, pass ? Pal.Good : Pal.TextDim, x + (i % 3) * 78, y + i / 3 * 16);
            }
            y += 36;
            Bar(g, x, y, w - 20, "Morale", c.Morale / 100f, c.Morale >= Bal.FlowMorale ? Pal.Good : Pal.Warn); y += 20;
            Bar(g, x, y, w - 20, "Health", c.Hp / c.MaxHp, Pal.Bad); y += 20;
            Text(g, Fit(g, c.Job != null ? $"Works: {Bal.Name(c.Job.Kind)}" :
                   c.CaptureTarget != null ? "Capturing prisoner" : "No assignment", FSmall, w - 20),
                FSmall, Pal.TextDim, x, y);
        }
        else if (v.Sel.Foe is { } foe)
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
        if (col == null)
            foreach (var t in game.Trains)
                if (MathF.Abs(t.PosX - wt.X) < 0.8f && MathF.Abs(t.PosY - wt.Y) < 0.5f)
                { DrawHoverLabel(g, v.Mouse, $"Locomotive · cargo {t.CargoCount}/{Bal.TrainCap}"); return; }

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
                Terrain.Rock => "Rock formation",
                Terrain.Water => "Water",
                _ => "",
            };
            if (tname.Length > 0)
            {
                DrawHoverLabel(g, v.Mouse, tname);
                if (v.AltDown && t != Terrain.Water)
                    DrawAltCard(g, v.Mouse, tname, new[]
                    {
                        t == Terrain.IronOre ? "Yields Iron Ore." :
                        t == Terrain.CopperOre ? "Yields Copper Ore." :
                        t == Terrain.Crystal ? "Yields Exotic Crystals." :
                        t == Terrain.Flora ? "Yields Biomass (food or boiler fuel)." : "Impassable.",
                        t == Terrain.Rock ? "" : "Harvest: place a Mine Drill on it.",
                    });
            }
            return;
        }

        string? label = col?.Name
            ?? (foe != null ? (foe.Apex ? "Apex Horror" : foe.Downed ? "Downed Horror (capturable)" : "Wild Horror")
            : b != null ? Bal.Name(b.Kind) : null);
        if (label == null) return;

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
        Text(g, "GENERATE WORLD", FTitle, Pal.Accent, client.Width / 2f, 64, center: true);
        Text(g, "same seed + same settings = the same world, forever", FSmall, Pal.TextDim,
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
            "ROTATE R (ghost chevron shows output)   BULLDOZE X (50% refund)",
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
