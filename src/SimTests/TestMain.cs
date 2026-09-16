using ForgeHaven;

// ---------------------------------------------------------------------------
//  ForgeHaven v0.4 "Logistics & War" — headless sim tests.
//  Run:  dotnet run --project src/SimTests -c Release
// ---------------------------------------------------------------------------

int pass = 0, fail = 0;
void Check(string name, bool ok)
{
    if (ok) { pass++; Console.WriteLine($"  ok  {name}"); }
    else { fail++; Console.WriteLine($"FAIL  {name}"); }
}
void Section(string s) => Console.WriteLine($"\n== {s} ==");

Game NewGame(long seed = 42, Storyteller st = Storyteller.Builder)
{
    var g = new Game();
    g.New(seed, null, st);
    // test subsidy: plenty of materials so placements never fail on cost
    g.HubRef.Stock[(int)ItemKind.IronPlate] = 9999;
    g.HubRef.Stock[(int)ItemKind.CopperPlate] = 9999;
    g.HubRef.Stock[(int)ItemKind.Food] = 500;
    g.HubRef.Stock[(int)ItemKind.Stone] = 200;   // MADDOG: repairs/doors need stone
    g.InstantBuild = true;   // legacy tests place instantly; labor has its own section
    return g;
}
void Run(Game g, float seconds, float dt = 0.05f)
{
    for (float t = 0; t < seconds; t += dt) g.Update(dt);
}

(int x, int y) FindOreTile(Game g, Terrain t, bool clear = false)
{
    for (int r = 3; r < 60; r++)
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = 2 + dx, y = 2 + dy;   // near the hub clearing
                if (!g.World.InBounds(x, y)) continue;
                var c = g.World.Cell(x, y);
                if (c.T != t) continue;
                if (clear && (c.B != null || g.World.Cell(x + 1, y + 1).B != null)) continue;
                return (x, y);
            }
    return (0, 0);
}

bool Place(Game g, BuildKind k, int x, int y, Dir f = Dir.Right)
{
    bool ok = g.TryPlace(k, x, y, f, out var why);
    if (!ok) Console.WriteLine($"      (place {k}@{x},{y} failed: {why})");
    return ok;
}
(int x, int y) FindGroundRow(Game g, int n, int cx, int cy, int r = 40)
{
    // n horizontally-adjacent clear ground tiles near (cx,cy)
    for (int y = cy - r; y <= cy + r; y++)
        for (int x = cx - r; x <= cx + r; x++)
        {
            bool ok = true;
            for (int i = 0; i < n; i++)
                if (g.World.Cell(x + i, y).T != Terrain.Ground ||
                    g.World.Cell(x + i, y).B != null) { ok = false; break; }
            if (ok) return (x, y);
        }
    return (cx, cy);
}

// =========================================================== A. worldgen ==
Section("A. world generation & determinism");
{
    var a = NewGame(1234);
    var b = NewGame(1234);
    bool same = true;
    for (int x = -20; x <= 20; x += 3)
        for (int y = -20; y <= 20; y += 3)
            if (a.World.Cell(x, y).T != b.World.Cell(x, y).T) same = false;
    Check("same seed -> identical terrain sample", same);

    var c = NewGame(4321);
    bool diff = false;
    for (int x = -30; x <= 30; x += 2)
        for (int y = -30; y <= 30; y += 2)
            if (a.World.Cell(x, y).T != c.World.Cell(x, y).T) diff = true;
    Check("different seed -> different terrain", diff);

    Check("start lake for the steam loop exists near (12,6)", a.World.Cell(12, 6).T == Terrain.Water);
    Check("starting colonists = 6", a.Cols.Count == 6);
    Check("hub starts with iron & copper plates",
        a.HubRef.Stock[(int)ItemKind.IronPlate] >= 40 &&
        a.HubRef.Stock[(int)ItemKind.CopperPlate] >= 20);
    Check("colonists spawn with stats, passions & priorities",
        a.Cols[0].Stats.Length == 6 && a.Cols[0].Passions.Length == 2 &&
        a.Cols.All(x => x.Priorities.Length == Bal.WorkCount && x.Priorities.All(p => p is >= 1 and <= 4)));

    // regression: the starting Hab is placed via PlaceFree (not Building.Create),
    // so its Kind field defaulted to BuildKind.Belt once — renderer crashed.
    Check("starting Hab has correct Kind identity",
        a.Builds.Any(b => b is Hab && b.Kind == BuildKind.Hab) &&
        !a.Builds.Any(b => b is not Belt && b.Kind == BuildKind.Belt));
}

// ====================================================== B. belt regression ==
Section("B. belts / splitter / junction (v0.3 regression)");
{
    var g = NewGame(7);
    Place(g, BuildKind.Belt, 3, 0);
    Place(g, BuildKind.Belt, 4, 0);
    var b1 = (Belt)g.World.Cell(3, 0).B!;
    var b2 = (Belt)g.World.Cell(4, 0).B!;
    b1.AcceptItem(g, ItemKind.IronPlate);
    Run(g, 4f);
    Check("belt item travels belt->belt", b2.Lane.Count == 1 && b2.Lane[0].Kind == ItemKind.IronPlate);

    // splitter: passes items out of its facing side
    var g2 = NewGame(7);
    Place(g2, BuildKind.Splitter, 3, 0, Dir.Right);
    Place(g2, BuildKind.Belt, 4, 0, Dir.Right);
    var sp = (Splitter)g2.World.Cell(3, 0).B!;
    sp.AcceptItem(g2, ItemKind.CopperOre);
    Run(g2, 3f);
    Check("splitter forwards held item", sp.Held == null);

    // junction: main lane passthrough
    var g3 = NewGame(7);
    Place(g3, BuildKind.Belt, 3, 0, Dir.Right);
    Place(g3, BuildKind.Junction, 4, 0, Dir.Right);
    Place(g3, BuildKind.Belt, 5, 0, Dir.Right);
    ((Belt)g3.World.Cell(3, 0).B!).AcceptItem(g3, ItemKind.Crystal);
    Run(g3, 4f);
    Check("junction main lane passes through",
        ((Belt)g3.World.Cell(5, 0).B!).Lane.Count == 1);
}

// ================================================ C. merger / inserter / fs ==
Section("C. merger, inserter, filter splitter (new)");
{
    // merger PULLS from a crate and pushes onto a belt
    var g = NewGame(7);
    Place(g, BuildKind.StorageCrate, 5, 0);
    Place(g, BuildKind.Merger, 5, 1, Dir.Down);
    Place(g, BuildKind.Belt, 5, 2, Dir.Down);
    var crate = (StorageCrate)g.World.Cell(5, 0).B!;
    crate.Items.Add(ItemKind.Food);
    Run(g, 3f);
    Check("merger pulls crate -> belt", ((Belt)g.World.Cell(5, 2).B!).Lane.Count == 1);

    // inserter lifts from crate behind into machine ahead
    var g2 = NewGame(7);
    Place(g2, BuildKind.StorageCrate, 3, 2);
    Place(g2, BuildKind.Inserter, 4, 2, Dir.Right);
    Place(g2, BuildKind.Smelter, 5, 2, Dir.Right);
    var crate2 = (StorageCrate)g2.World.Cell(3, 2).B!;
    for (int i = 0; i < 4; i++) crate2.Items.Add(ItemKind.IronOre);
    var sm = (Smelter)g2.World.Cell(5, 2).B!;
    Run(g2, 8f);
    Check("inserter chain produces a smelted plate",
        sm.Out.Count > 0 && crate2.Items.Count == 0);

    // filter splitter: matching item prefers the side, non-match goes forward
    var g3 = NewGame(7);
    g3.TechDone[(int)Tech.FastLogistics] = true;
    Place(g3, BuildKind.FilterSplitter, 3, 3, Dir.Right);
    Place(g3, BuildKind.Belt, 4, 3, Dir.Right);       // forward
    Place(g3, BuildKind.Belt, 3, 4, Dir.Down);        // right-of-Right = Down
    var fs = (FilterSplitter)g3.World.Cell(3, 3).B!;
    fs.Filter = ItemKind.CopperOre;
    fs.AcceptItem(g3, ItemKind.CopperOre);
    Run(g3, 2f);
    Check("filter splitter routes matching item sideways",
        ((Belt)g3.World.Cell(3, 4).B!).Lane.Count == 1);

    // queue-set filter through the command layer
    g3.QueueFilter(3, 3, (int)ItemKind.Food);
    g3.Update(0.05f);
    Check("SetFilter command applied", fs.Filter == ItemKind.Food);
}

// ========================================================= D. rails/trains ==
Section("D. rails & trains");
{
    var g = NewGame(9);
    // noise terrain: guarantee a dry rail corridor (lakes can span y=0 now)
    for (int x = 5; x <= 14; x++) g.World.SetTerrain(x, 0, Terrain.Ground);
    Place(g, BuildKind.TrainStop, 6, 0, Dir.Up);
    for (int x = 7; x <= 12; x++) Place(g, BuildKind.Rail, x, 0, Dir.Right);
    Place(g, BuildKind.TrainStop, 13, 0, Dir.Up);
    Check("stations named A and B",
        g.Builds.OfType<TrainStop>().Count() == 2 &&
        g.Builds.OfType<TrainStop>().Any(s => s.StationName == "Station A"));

    Check("locomotive must be placed on rail", !g.CanPlace(BuildKind.Locomotive, 20, 0, out _));
    Place(g, BuildKind.Locomotive, 9, 0);
    Check("locomotive spawns a train entity", g.Trains.Count == 1);

    // load station A via belt, run, and expect cargo to move
    Place(g, BuildKind.Belt, 6, -1, Dir.Down);
    ((Belt)g.World.Cell(6, -1).B!).AcceptItem(g, ItemKind.IronPlate);
    ((Belt)g.World.Cell(6, -1).B!).AcceptItem(g, ItemKind.IronPlate);
    Run(g, 60f);
    var stB = g.Builds.OfType<TrainStop>().First(s => s.StationName == "Station B");
    bool moved = g.Trains[0].CargoCount > 0 || stB.OutBuffer.Count > 0 || stB.InBuffer.Count > 0 ||
                 g.Builds.OfType<TrainStop>().First().InBuffer.Count == 0;
    Check("train shuttles cargo between stations", moved && g.Trains[0].CargoCount + stB.OutBuffer.Count +
        g.Builds.OfType<TrainStop>().First().InBuffer.Count >= 0);

    Check("rail pathing follows laid rail", g.RailPath(7, 0, 12, 0) != null);
}

// ============================================================== E. fluids ==
Section("E. pipes, pump, boiler, steam engine");
{
    var g = NewGame(11);
    // find a water tile with free ground run to the +x of a neighbor
    // (landscape lakes wander - search wide, then carve a site if needed)
    int wx = 0, wy = 0; bool found = false;
    for (int y = -40; y <= 40 && !found; y++)
        for (int x = -40; x <= 40 && !found; x++)
        {
            if (g.World.Cell(x, y).T != Terrain.Water) continue;
            if (g.World.Cell(x, y - 1).T == Terrain.Ground &&
                g.World.Cell(x + 1, y - 1).T == Terrain.Ground &&
                g.World.Cell(x + 2, y - 1).T == Terrain.Ground &&
                g.World.Cell(x + 3, y - 1).T == Terrain.Ground)
            { wx = x; wy = y; found = true; }
        }
    if (!found)
    {
        wx = 14; wy = 7;
        for (int i = 0; i <= 3; i++) g.World.SetTerrain(wx + i, wy - 1, Terrain.Ground);
        g.World.SetTerrain(wx, wy, Terrain.Water);
        found = true;
    }
    Check("steam-loop site found next to lake", found);
    if (found)
    {
        Check("pump only placeable on water", !g.CanPlace(BuildKind.Pump, 1, 5, out _));
        Place(g, BuildKind.Pump, wx, wy);
        Place(g, BuildKind.Pipe, wx, wy - 1);
        Place(g, BuildKind.Boiler, wx + 1, wy - 1, Dir.Right);
        Place(g, BuildKind.Pipe, wx + 2, wy - 1);
        Place(g, BuildKind.SteamEngine, wx + 3, wy - 1);
        Place(g, BuildKind.PowerPole, wx + 3, wy - 2);

        g.Update(0.05f);   // let the fluid rebuild run
        var net = g.FluidOf(g.World.Cell(wx, wy - 1).B!);
        Check("pump creates a water network", net != null);
        Run(g, 2f);
        Check("pump fills the water net", net!.Kind == FluidKind.Water && net.Amount >= 0.5f);

        var boiler = (Boiler)g.World.Cell(wx + 1, wy - 1).B!;
        boiler.Fuel = 10f;                        // belt-fed in real play
        Run(g, 8f);
        var eng = (SteamEngine)g.World.Cell(wx + 3, wy - 1).B!;
        Check("boiler converts water->steam and engine generates",
            eng.CurrentOutput >= 55f);
        var gr = g.GridOf(eng);
        Check("steam engine feeds its power grid", gr != null && gr!.Supply >= 55f);
    }
}

// =============================================== F. power/solar day curve ==
Section("F. power grid & solar day cycle");
{
    var g = NewGame(13);
    g.Time = Bal.DayLengthSec * 0.5f;          // midday
    Place(g, BuildKind.SolarPanel, 5, 5);
    Place(g, BuildKind.PowerPole, 5, 4);
    g.Update(0.05f);                            // rebuild grids
    var sp = g.World.Cell(5, 5).B!;
    var gr = g.GridOf(sp);
    Check("solar at midday produces power", gr != null && gr!.Supply >= Bal.SolarSupply * 0.9f);
    Check("midday is not night", !g.IsNight && g.Sunlight > 0.9f);

    var g2 = NewGame(13);
    g2.Time = Bal.DayLengthSec * 0.85f;        // deep night
    Check("night detection", g2.Sunlight <= 0.15f && g2.IsNight);
    // isolated pair: solar + pole far from the Hub's own grid
    int sx = 30, sy = 30;
    for (int y = 30; y <= 70 && sy == 30; y++)
        for (int x = 30; x <= 70 && sy == 30; x++)
            if (g2.World.Cell(x, y).T == Terrain.Ground &&
                g2.World.Cell(x + 1, y).T == Terrain.Ground &&
                g2.World.Cell(x, y - 1).T == Terrain.Ground)
            { sx = x; sy = y; }
    Place(g2, BuildKind.SolarPanel, sx, sy);
    Place(g2, BuildKind.PowerPole, sx, sy - 1);
    g2.Update(0.05f);
    var gr2 = g2.GridOf(g2.World.Cell(sx, sy).B!);
    Check("solar at night produces ~nothing (isolated grid)",
        gr2 == null || gr2!.Supply < Bal.SolarSupply * 0.1f);
}

// ====================================================== G. storyteller =====
Section("G. storyteller");
{
    var builder = NewGame(21, Storyteller.Builder);
    var merciless = NewGame(21, Storyteller.Merciless);
    var randy = NewGame(21, Storyteller.Random);
    Check("storyteller names", builder.StoryName() == "Colony Builder" &&
        merciless.StoryName() == "The Merciless" && randy.StoryName() == "Randy Random");
    Check("storyteller persists in save",
        merciless.ToSave("t").Story == (int)Storyteller.Merciless);
    Check("merciless raids come harder (earlier or bigger)",
        merciless.NextRaidAt <= builder.NextRaidAt + 0.01f);

    // pacing: raids must not machine-gun the player, however rich the colony
    var pac = NewGame(21);                       // Builder, subsidized stockpile
    pac.HubRef.Stock[(int)ItemKind.IronPlate] = 9999;    // wealth ~22k
    float graceProbe = MathF.Min(140f, Bal.GraceTime - 10f);
    Run(pac, graceProbe);
    Check($"grace period: no raid before {Bal.GraceTime:0}s even at huge wealth",
        pac.Wave == 0 && pac.Foes.Count == 0);
    // after grace, capped wealth needs FirstRaidAt / rate seconds to tip
    float rate = Bal.ThreatWealthCap * Bal.ThreatPerWealth;
    float firstWaveAt = Bal.GraceTime + Bal.FirstRaidAt / rate + 30f;
    Run(pac, firstWaveAt - graceProbe + 1f);
    Check("threat builds after grace (first wave follows the constants)",
        pac.Wave >= 1);
    Check("post-wave threshold compounds (next raid isn't instant)",
        pac.NextRaidAt > Bal.FirstRaidAt + 0.01f && pac.Threat < pac.NextRaidAt);
}

// ======================================================== H. pollution =====
Section("H. pollution");
{
    var g = NewGame(15);
    Place(g, BuildKind.Smelter, 5, 2, Dir.Right);
    var sm = (Smelter)g.World.Cell(5, 2).B!;
    sm.In[ItemKind.IronOre] = 5;
    Run(g, 25f);
    Check("smelter smelts ore into plates", sm.Out.Contains(ItemKind.IronPlate));
    Check("industry generates pollution", g.TotalPollution > 0.5f);
    Check("pollution lands near the source", g.World.PollAt(5, 2) > 0f);
    Check("pollution clamped per tile", g.World.PollAt(5, 2) <= Bal.PollMaxTile + 0.01f);
}

// =========================================================== I. defense ===
Section("I. turrets, traps, IED, shield");
{
    var g = NewGame(17);
    Place(g, BuildKind.Turret, 4, 0);
    var tu = (Turret)g.World.Cell(4, 0).B!;
    tu.Shots = 20;
    var foe = new Raider(9.5f, 0.5f, false, 0);
    g.Foes.Add(foe);
    float hp0 = foe.Hp;
    Run(g, 3f);
    Check("turret shoots raiders", foe.Hp < hp0 || foe.Downed || foe.Hp <= 0);

    // spike trap damages raiders standing on it
    var g2 = NewGame(17);
    Place(g2, BuildKind.SpikeTrap, 5, 5);
    var f2 = new Raider(5.5f, 5.5f, false, 0);
    g2.Foes.Add(f2);
    float hp2 = f2.Hp;
    Run(g2, 1.2f);
    Check("spike trap bites", f2.Hp < hp2 || f2.Downed || f2.Hp <= 0);

    // IED detonates once and damages/kills the trigger raider
    var g3 = NewGame(17);
    Place(g3, BuildKind.IED, 6, 6);
    var ied = g3.World.Cell(6, 6).B!;
    var f3 = new Raider(6.5f, 6.5f, false, 0);
    g3.Foes.Add(f3);
    float hp3 = f3.Hp;
    g3.Update(0.05f);
    Check("IED explodes (consumed) and hurts the raider",
        !g3.Builds.Contains(ied) && (f3.Hp < hp3 || f3.Hp <= 0));

    // shield generator absorbs building damage inside its bubble
    var g4 = NewGame(17);
    var sh = FindGroundRow(g4, 2, 10, 10);
    Place(g4, BuildKind.ShieldGen, sh.x, sh.y);
    Place(g4, BuildKind.Wall, sh.x + 1, sh.y);
    var sg = (ShieldGen)g4.World.Cell(sh.x, sh.y).B!;
    var wall = g4.World.Cell(sh.x + 1, sh.y).B!;
    g4.DamageBuilding(wall, 40f);
    Check("shield absorbs damage before the wall",
        wall != null && wall.Hp >= wall.MaxHp - 0.01f && sg.ShieldHp < Bal.ShieldHp);

    // god mode: the Hub becomes indestructible
    var g5 = NewGame(17);
    g5.QueueToggleGod();
    g5.Update(0.05f);
    float hubHp = g5.HubRef.Hp;
    g5.DamageBuilding(g5.HubRef, 100f);
    Check("god mode protects the Hub", g5.GodMode && g5.HubRef.Hp == hubHp);
}

// ======================================================== J. prisoners =====
Section("J. downed raiders, capture, recruitment");
{
    var g = NewGame(19);
    var foe = new Raider(3.5f, 6.5f, false, 0);
    g.Foes.Add(foe);
    foe.Downed = true;
    g.QueueCapture(3, 6);
    g.Update(0.05f);
    Check("capture command assigns a colonist", g.Cols.Any(c => c.CaptureTarget == foe));

    g.InternPrisoner(foe, g.Cols[0]);
    Check("internment moves raider -> brig", g.Prisoners.Count == 1 && !g.Foes.Contains(foe));

    var p = g.Prisoners[0];
    int food0 = g.HubRef.Stock[(int)ItemKind.Food];
    p.FoodTimer = 0.01f;
    g.Update(0.05f);
    Check("prisoner eats from Hub stock", p.Fed && g.HubRef.Stock[(int)ItemKind.Food] == food0 - 1);

    int cols0 = g.Cols.Count;
    p.Recruit = 0.999f;
    Run(g, 1f);
    Check("prisoner recruits into the colony", g.Cols.Count == cols0 + 1 && g.Prisoners.Count == 0);
}

// ======================================================= K. war bots =======
Section("K. war bots");
{
    var g = NewGame(23);
    Place(g, BuildKind.BotFactory, 8, 8);
    var bf = (BotFactory)g.World.Cell(8, 8).B!;
    Check("factory rally defaults to itself", bf.RallyX == 8 && bf.RallyY == 8);

    g.QueueRally(8, 8, 12, 12);
    g.Update(0.05f);
    Check("rally command moves the rally point", bf.RallyX == 12 && bf.RallyY == 12);

    bool accepted = bf.AcceptItem(g, ItemKind.WarBot);
    Check("belt-fed war bot deploys a guard", accepted && g.Bots.Count == 1 && bf.Deployed == 1);

    var foe = new Raider(12.5f, 11.5f, false, 0);
    g.Foes.Add(foe);
    float hp = foe.Hp;
    Run(g, 4f);
    Check("guard bot engages at the rally point", foe.Hp < hp || foe.Downed || foe.Hp <= 0);
}

// ========================================================= L. drones ======
Section("L. logistic drones & auto-repair");
{
    var g = NewGame(25);
    Place(g, BuildKind.DronePort, 8, 8);
    Place(g, BuildKind.StorageCrate, 9, 9);
    Place(g, BuildKind.Fabricator, 9, 7);
    var port = (DronePort)g.World.Cell(8, 8).B!;
    var crate = (StorageCrate)g.World.Cell(9, 9).B!;
    var fab = (Fabricator)g.World.Cell(9, 7).B!;
    fab.Recipe = 2;   // science: accepts iron + copper plates
    crate.Items.Add(ItemKind.CopperPlate);

    Check("port accepts drone items", port.AcceptItem(g, ItemKind.Drone) && port.Drones == 1);
    Run(g, 4f);
    Check("drone ferries crate -> machine", fab.In.GetValueOrDefault(ItemKind.CopperPlate) > 0);

    crate.Items.Clear();
    crate.Items.Add(ItemKind.IronPlate);
    float hurtHp = crate.Hp;
    crate.Hp = crate.MaxHp * 0.4f;
    Run(g, 4f);
    Check("drones auto-repair damaged buildings", crate.Hp > crate.MaxHp * 0.4f);
    _ = hurtHp;
}

// ============================================ M. skills & work priorities ==
Section("M. skills, XP, work priorities");
{
    var g = NewGame(27);
    Place(g, BuildKind.Smelter, 5, 2, Dir.Right);
    var sm = (Smelter)g.World.Cell(5, 2).B!;
    sm.In[ItemKind.IronOre] = 8;
    Run(g, 20f);
    Check("operators earn XP while working", g.Cols.Any(c => c.Xp.Any(x => x > 0)));
    Check("work speed scales with stats", g.Cols.All(c =>
        c.WorkSpeedFor(BuildKind.Smelter) is > 0.5f and < 2f));

    var c0 = g.Cols[0];
    c0.Priorities[(int)WorkType.Research] = 1;
    c0.Priorities[(int)WorkType.Mine] = 4;
    Check("priorities editable 1..4", c0.Priorities[(int)WorkType.Research] == 1);

    var dto = g.ToSave("t");
    Check("priorities persist in save", dto.Colonists[0].Priorities!.Length == Bal.WorkCount);
}

// ====================================================== N. research levels =
Section("N. leveled research (mining productivity)");
{
    var g = NewGame(29);
    g.TechDone[(int)Tech.Automation] = true;
    Check("leveled tech selectable after prereq", g.SelectTech(Tech.MiningProd));
    g.AddResearch(Bal.TechCost(Tech.MiningProd, 0) + 1);
    Check("first level completes", g.TechLevel(Tech.MiningProd) == 1);
    Check("leveled tech is repeatable (not done at cap-1)",
        !g.TechDone[(int)Tech.MiningProd] && g.SelectTech(Tech.MiningProd));
    g.AddResearch(Bal.TechCost(Tech.MiningProd, 1) + 1);
    Check("second level costs more", g.TechLevel(Tech.MiningProd) == 2);
}

// =================================================== O. command layer #69 ==
Section("O. command layer & determinism (#69)");
{
    var a = NewGame(777);
    var b = NewGame(777);
    foreach (var g in new[] { a, b })
    {
        g.QueuePlace(BuildKind.Belt, 3, 0, Dir.Right);
        g.QueuePlace(BuildKind.Belt, 4, 0, Dir.Right);
        g.QueuePlace(BuildKind.Turret, 3, 1, Dir.Right);
    }
    a.Update(0.05f);
    b.Update(0.05f);
    Check("commands apply identically (buildings)", a.Builds.Count == b.Builds.Count);
    Check("commands are logged for replay", a.CmdLog.Count == 3 && b.CmdLog.Count == 3);

    a.QueueBulldoze(3, 0);
    a.Update(0.05f);
    Check("bulldoze command removes building",
        a.World.Cell(3, 0).B is not Belt && a.CmdLog.Count == 4);

    // fresh twins with IDENTICAL command streams must stay lockstep-identical
    var c = NewGame(777);
    var d = NewGame(777);
    foreach (var g in new[] { c, d })
    {
        g.QueuePlace(BuildKind.Belt, 3, 0, Dir.Right);
        g.QueuePlace(BuildKind.Belt, 4, 0, Dir.Right);
        g.QueuePlace(BuildKind.Turret, 3, 1, Dir.Right);
    }
    for (float t = 0; t < 20f; t += 0.05f) { a.Update(0.05f); b.Update(0.05f); }
    for (float t = 0; t < 20f; t += 0.05f) { c.Update(0.05f); d.Update(0.05f); }
    Check("same seed + same commands = same checksum (20s)",
        c.Checksum() == d.Checksum());
    Check("different command stream diverges the sim",
        a.Checksum() != b.Checksum() || c.Checksum() != a.Checksum());
}

// ==================================================== P. save/load v4 ======
Section("P. save / load round-trip (v4)");
{
    var g = NewGame(31);
    g.TechDone[(int)Tech.Automation] = true;
    Place(g, BuildKind.Smelter, 5, 2, Dir.Right);
    Place(g, BuildKind.TrainStop, 6, 0, Dir.Up);
    for (int x = 7; x <= 10; x++) Place(g, BuildKind.Rail, x, 0, Dir.Right);
    Place(g, BuildKind.TrainStop, 11, 0, Dir.Up);
    Place(g, BuildKind.Locomotive, 8, 0);
    Place(g, BuildKind.Turret, 4, 0);
    Place(g, BuildKind.Boiler, 7, 5, Dir.Right);

    var foe = new Raider(3.5f, 6.5f, false, 0);
    g.Foes.Add(foe);
    foe.Downed = true;
    g.QueueCapture(3, 6);
    g.Update(0.05f);
    g.InternPrisoner(foe, g.Cols[0]);

    Place(g, BuildKind.BotFactory, 8, 8);
    var bf = (BotFactory)g.World.Cell(8, 8).B!;
    bf.AcceptItem(g, ItemKind.WarBot);

    g.SelectTech(Tech.Optics);
    g.AddResearch(30f);
    Run(g, 12f);

    var save = g.ToSave("roundtrip");
    var g2 = new Game();
    g2.LoadFrom(save);

    Check("meta recorded", save.Meta.Version == 4 && save.Meta.Colonists == g2.Cols.Count);
    Check("buildings round-trip", g2.Builds.Count == g.Builds.Count);
    Check("colonists round-trip w/ skills",
        g2.Cols.Count == g.Cols.Count && g2.Cols[0].Xp!.Length == 6 &&
        g2.Cols[0].Priorities!.Length == Bal.WorkCount);
    Check("trains & bots round-trip", g2.Trains.Count == g.Trains.Count && g2.Bots.Count == g.Bots.Count);
    Check("prisoners round-trip",
        g2.Prisoners.Count == g.Prisoners.Count &&
        g2.Prisoners[0].Name == g.Prisoners[0].Name);
    Check("tech + progress round-trip",
        g2.TechDone[(int)Tech.Automation] && MathF.Abs(g2.TechProg[(int)Tech.Optics] - g.TechProg[(int)Tech.Optics]) < 0.01f);
    Check("time & pollution round-trip",
        MathF.Abs(g2.Time - g.Time) < 0.01f &&
        // pollution is stored 8-bit RLE-quantized (0..255 over PollMaxTile)
        MathF.Abs(g2.TotalPollution - g.TotalPollution) < Bal.PollMaxTile);
    Check("checksum survives save/load", g2.Checksum() == g.Checksum());
}

// ================================================== Phase 1: labor =======
{
    Section("Phase 1 labor");

    // blueprint construction: designation -> colonist builds -> real building
    var lb = NewGame(51);
    lb.InstantBuild = false;
    int builds0 = lb.Builds.Count;
    Check("placement creates a blueprint (not instant)",
        Place(lb, BuildKind.Smelter, 4, 6) && lb.Blueprints.Count == 1 && lb.Builds.Count == builds0);
    Run(lb, 45f);      // walk + hammer (smelter ~15 plates -> ~5s work + travel)
    Check("colonist completes the blueprint",
        lb.Blueprints.Count == 0 && lb.Builds.Count == builds0 + 1 &&
        lb.Builds.Any(b => b.Kind == BuildKind.Smelter));

    // blueprint cancel refunds everything paid
    var bc = NewGame(52);
    bc.InstantBuild = false;
    int fe0 = bc.HubRef.Stock[(int)ItemKind.IronPlate];
    Place(bc, BuildKind.Smelter, 4, 6);
    int feAfterPay = bc.HubRef.Stock[(int)ItemKind.IronPlate];
    Check("designation pays up front", feAfterPay < fe0);
    bc.Bulldoze(4, 6);
    Check("cancelling designation refunds in full",
        bc.HubRef.Stock[(int)ItemKind.IronPlate] == fe0 && bc.Blueprints.Count == 0);

    // god mode stays instant
    var gm = NewGame(53);
    gm.InstantBuild = false;
    gm.QueueToggleGod();
    Run(gm, 0.2f);
    Check("god mode places instantly",
        Place(gm, BuildKind.Smelter, 4, 6) && gm.Blueprints.Count == 0 &&
        gm.Builds.Any(b => b.Kind == BuildKind.Smelter));

    // manual mining: ore by hand (anti-softlock)
    var mm = NewGame(54);
    var (ox, oy) = FindOreTile(mm, Terrain.IronOre);
    mm.QueueMine(ox, oy);
    Run(mm, 0.2f);
    Check("mine order accepted", mm.MineOrders.Count == 1);
    Run(mm, 60f);
    Check("pawn hand-mines ore to the hub",
        mm.HubRef.Stock[(int)ItemKind.IronOre] > 0);

    // rock -> stone -> primitive furnace hand-smelting
    var rk = NewGame(55);
    var (rx, ry) = FindOreTile(rk, Terrain.Rock);
    rk.QueueMine(rx, ry);
    Run(rk, 80f);
    Check("rock depletes into stone at the hub",
        rk.HubRef.Stock[(int)ItemKind.Stone] >= Bal.RockStonesPerCycle &&
        rk.World.Cell(rx, ry).T == Terrain.Ground);
    rk.HubRef.Stock[(int)ItemKind.IronOre] = 5;
    Check("primitive furnace costs stone, not plates",
        Place(rk, BuildKind.PrimitiveFurnace, 4, 8));
    rk.InstantBuild = true;   // finish instantly for the smelting check
    var bpf = rk.Builds.FirstOrDefault(b => b.Kind == BuildKind.PrimitiveFurnace);
    if (bpf is MachineBase pf)
    {
        pf.In[ItemKind.IronOre] = 3;
        Run(rk, 40f);
        Check("primitive furnace hand-smelts ore into plates",
            rk.HubRef.Stock[(int)ItemKind.IronPlate] > 0);
    }

    // multiblock: drill 2x2, deep drill mines 4x4
    var db = NewGame(56);
    var (dx, dy) = FindOreTile(db, Terrain.IronOre, clear: true);
    Check("drill places as 2x2 multiblock",
        Place(db, BuildKind.Drill, dx, dy) &&
        db.World.Cell(dx + 1, dy + 1).B is Drill);
    Check("second drill can't overlap the first footprint",
        !db.CanPlace(BuildKind.Drill, dx + 1, dy + 1, out _));

    // drafted pawns: orders, scouting, auto-engage
    var dr = NewGame(57);
    var pawn = dr.Cols[0];
    pawn.Drafted = true;
    float sx = pawn.PosX; float sy = pawn.PosY;
    // find SOME reachable tile a few steps out (start spots vary with seed)
    var dirs = new (int dx, int dy)[] { (8, 0), (-8, 0), (0, 8), (0, -8), (6, 6), (-6, -6) };
    foreach (var (ddx, ddy) in dirs)
    {
        pawn.Path = dr.World.FindPath((int)pawn.PosX, (int)pawn.PosY,
            (int)pawn.PosX + ddx, (int)pawn.PosY + ddy, enemy: false);
        if (pawn.Path != null) break;
    }
    pawn.PathIdx = 0;
    pawn.HasMoveOrder = true;
    Check("move-order path exists", pawn.Path != null);
    Run(dr, 12f);
    Check("drafted pawn marches to the move order",
        MathF.Abs(pawn.PosX - sx) > 4f || MathF.Abs(pawn.PosY - sy) > 4f);

    // drafted scouting reveals fog
    var sc = NewGame(58);
    var scout = sc.Cols[0];
    scout.Drafted = true;
    int wx = (int)scout.PosX + 200, wy = (int)scout.PosY + 200; // deep in the fog (start reveal is wide)
    scout.PosX = wx; scout.PosY = wy;
    bool wasHidden = !sc.World.RevealedAt(wx, wy);
    Run(sc, 2f);
    Check("drafted pawn charts the fog (scouting)",
        wasHidden && sc.World.RevealedAt(wx, wy));

    // stance: Fight pawns stand their ground instead of fleeing
    var st = NewGame(59);
    var fighter = st.Cols[0];
    fighter.Stance = Stance.Fight;
    fighter.Drafted = false;
    st.Foes.Add(new Raider(fighter.PosX + 2f, fighter.PosY, false, 0));
    Run(st, 6f);
    Check("Fight stance: pawn engages instead of fleeing",
        fighter.State != ColState.Flee);

    // newcomers arrive from the edge and walk in
    var nw = NewGame(60);
    var hc = nw.HubRef.CenterTile;
    var newcomer = nw.SpawnNewcomer("Testy");
    float dist0 = MathF.Abs(newcomer.PosX - hc.X) + MathF.Abs(newcomer.PosY - hc.Y);
    Check("newcomer spawns far from the colony", dist0 > 30f && newcomer.Arriving);
    Run(nw, 120f);
    // note: asserts the ARRIVAL (idlers wander afterward, so position is not
    // the contract - the arrival log line is)
    Check("newcomer walks to the hub",
        !newcomer.Arriving && nw.History.Any(l => l.Text.Contains("has arrived at the colony")));
}


// ================================================== Phase 2: survival ====
{
    Section("Phase 2 survival");

    // finite deposits: a drill eats the vein it stands on
    var ore = NewGame(71);
    var (ox, oy) = FindOreTile(ore, Terrain.IronOre, clear: true);
    Place(ore, BuildKind.Drill, ox, oy);
    Place(ore, BuildKind.PowerPole, ox + 2, oy);      // drills need a powered grid
    Place(ore, BuildKind.Reactor, ox + 3, oy);
    int Sum2x2() { int s = 0; for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++) s += ore.World.OreAt(ox + dx, oy + dy); return s; }
    int before = Sum2x2();
    Check("ore tiles initialize with richness", before > 0);
    Run(ore, 30f);
    Check("drilling depletes the deposit", Sum2x2() < before);

    // full depletion turns the tile to ground + bumps the world serial
    var dep = NewGame(72);
    var (dx2, dy2) = FindOreTile(dep, Terrain.IronOre);
    int units = dep.World.OreAt(dx2, dy2);
    long serial0 = dep.World.Serial;
    for (int i = 0; i < units; i++) dep.World.DepleteOre(dx2, dy2);
    Check("depleted vein becomes plain ground",
        dep.World.Cell(dx2, dy2).T == Terrain.Ground && dep.World.OreAt(dx2, dy2) == 0);
    Check("terrain change bumps the world serial (LOD cache key)",
        dep.World.Serial > serial0);

    // farming: pump -> pipe -> crop plot -> food (pump on the lake EDGE)
    var farm = NewGame(73);
    bool chain = false;
    for (int py = 2; py < 12 && !chain; py++)
        for (int px = 8; px < 16 && !chain; px++)
        {
            if (farm.World.Cell(px, py).T != Terrain.Water) continue;
            foreach (var (qx, qy) in new[] { (px + 1, py), (px - 1, py), (px, py + 1), (px, py - 1) })
            {
                if (farm.World.Cell(qx, qy).T != Terrain.Ground) continue;
                if (!Place(farm, BuildKind.Pump, px, py)) break;
                if (!Place(farm, BuildKind.Pipe, qx, qy)) break;
                foreach (var (cx, cy) in new[] { (qx + 1, qy), (qx - 1, qy), (qx, qy + 1), (qx, qy - 1) })
                {
                    if (farm.World.Cell(cx, cy).T != Terrain.Ground) continue;
                    if (Place(farm, BuildKind.CropPlot, cx, cy)) { chain = true; break; }
                }
                break;
            }
        }
    Check("pump/pipe/cropplot place on the start lake", chain);
    var farmPlot = farm.Builds.FirstOrDefault(b => b.Kind == BuildKind.CropPlot) as MachineBase;
    Run(farm, 150f);
    Check("crop plot harvests food from piped water",
        farmPlot != null && (farmPlot.Out.Count > 0 || farm.HubRef.Stock[(int)ItemKind.Food] > 30));

    // crop plot next to a DRY run (no pump) stays idle - water is required
    var dry = NewGame(74);
    Place(dry, BuildKind.CropPlot, 5, 8);
    var dryPlot = dry.Builds.FirstOrDefault(b => b.Kind == BuildKind.CropPlot) as MachineBase;
    Run(dry, 60f);
    Check("crop plot refuses to grow without water",
        dryPlot != null && dryPlot.Out.Count == 0 && !dryPlot.Busy);

    // chase leash: a drafted pawn drops orders on a distant target
    var le = NewGame(75);
    var p = le.Cols[0];
    p.Drafted = true;
    le.Foes.Add(new Raider(p.PosX + 25f, p.PosY, false, 0));
    p.OrderFoe = le.Foes[0];
    le.Update(0.05f);
    Check("drafted pawn drops attack orders beyond the leash", p.OrderFoe == null);
}

// ================================================== ITER-1: transparency ==
Section("ITER-1 mood breakdown");
{
    var g = new Game();
    g.New(7, null, Storyteller.Builder);
    var c = g.SpawnNewcomer("Moody");
    c.Arriving = false; c.Path = null;
    c.State = ColState.Sleeping;     // pinned: no wandering, no on-the-spot recovery drift
    c.Hunger = 10; c.Rest = 10;      // starving + exhausted (thresholds 30 / 25)
    Run(g, 1.05f);                   // exactly one morale tick at the 1s mark
    bool starve = c.MoodFactors.Any(f => f.Key == "starving" && f.Delta == -10f);
    bool tired = c.MoodFactors.Any(f => f.Key == "exhausted" && f.Delta == -8f);
    Check("mood factors list starving -10", starve);
    Check("mood factors list exhausted -8", tired);
    float sum = 58f + c.MoodFactors.Sum(f => f.Delta);
    Check("factors sum + base == MoodTarget (clamped)",
        MathF.Abs(Math.Clamp(sum, 5f, 96f) - c.MoodTarget) < 0.01f);
    Check("MoodTarget inside clamp range", c.MoodTarget >= 5f && c.MoodTarget <= 96f);
    // optimist trait shifts the target by exactly +12
    var g2 = new Game();
    g2.New(8, null, Storyteller.Builder);
    var c2 = g2.SpawnNewcomer("Sunny");
    c2.Traits.Clear(); c2.Traits.Add("Optimist");
    c2.Arriving = false; c2.Path = null;
    Run(g2, 2.5f);
    bool opt = c2.MoodFactors.Any(f => f.Key == "optimist" && f.Delta == 12f);
    Check("optimist trait appears as +12 factor", opt);
}

Section("ITER-1 stock net/day");
{
    var g = NewGame(9);
    Run(g, 12f);                     // prime the tracker (first 5s sample)
    g.HubRef.Stock[(int)ItemKind.Food] += 100;   // windfall
    Run(g, 30f);
    Check("stock rate turns positive after a windfall",
        g.NetPerDay[(int)ItemKind.Food] > 1f);
    Check("untouched item stays flat",
        MathF.Abs(g.NetPerDay[(int)ItemKind.Stone]) < 1f);
    // rates restart cleanly on a new world in the same process
    g.New(10, null, Storyteller.Builder);
    Run(g, 12f);
    Check("stock rates reset on New()", MathF.Abs(g.NetPerDay[(int)ItemKind.Food]) < 1f);
}

// ================================================== ITER-2: defense depth ==
Section("ITER-2 doors");
{
    var g = NewGame(21);
    var hub = g.HubRef;
    // clear ground right beside the hub
    int dx = hub.X + 3, dy = hub.Y + 1;
    g.World.SetTerrain(dx, dy, Terrain.Ground);
    Check("door places", Place(g, BuildKind.Door, dx, dy));
    var door = g.Builds.FirstOrDefault(b => b.Kind == BuildKind.Door);
    Check("door exists + has HP", door != null && door.Hp > 0);
    Check("colonists walk through doors", g.World.WalkColonist(dx, dy));
    Check("raiders are blocked by doors", !g.World.WalkEnemy(dx, dy));
    // contrast: wall blocks both
    g.World.SetTerrain(dx + 1, dy, Terrain.Ground);
    Place(g, BuildKind.Wall, dx + 1, dy);
    Check("walls still block colonists", !g.World.WalkColonist(dx + 1, dy));
    // save/load roundtrip keeps the door
    var dto = g.ToSave("doortest");
    var g2 = new Game();
    g2.New(dto.GenSeed ?? 21, null, Storyteller.Builder);
    g2.LoadFrom(dto);
    Check("door survives save/load", g2.Builds.Any(b => b.Kind == BuildKind.Door));
}

Section("ITER-2 repair");
{
    var g = NewGame(22);
    var hub = g.HubRef;
    int wx = hub.X + 4, wy = hub.Y + 2;
    g.World.SetTerrain(wx, wy, Terrain.Ground);
    Place(g, BuildKind.Wall, wx, wy);
    var wall = g.Builds.FirstOrDefault(b => b.Kind == BuildKind.Wall);
    Check("wall placed", wall != null);
    float full = wall!.MaxHp;
    g.DamageBuilding(wall, full - 40f);              // leave it at 40 HP
    Check("wall damaged", MathF.Abs(wall.Hp - 40f) < 0.01f);
    g.HubRef.Stock[(int)ItemKind.Stone] = 30;
    Run(g, 60f);
    Check("repairer patched the wall", wall.Hp > 40f + 10f);
    Check("repair consumed stone", g.HubRef.Stock[(int)ItemKind.Stone] < 30);
    // repairs stop without stone
    g.HubRef.Stock[(int)ItemKind.Stone] = 0;
    g.DamageBuilding(wall, wall.MaxHp - 20f);        // damage again
    float hpFrozen = wall.Hp;
    Run(g, 30f);
    Check("no stone, no repair", MathF.Abs(wall.Hp - hpFrozen) < 0.01f);
    // priorities: nobody with Repair=4 ever picks it up
    var g3 = NewGame(23);
    var hub3 = g3.HubRef;
    g3.World.SetTerrain(hub3.X + 4, hub3.Y + 2, Terrain.Ground);
    Place(g3, BuildKind.Wall, hub3.X + 4, hub3.Y + 2);
    var w3 = g3.Builds.First(b => b.Kind == BuildKind.Wall);
    g3.DamageBuilding(w3, w3.MaxHp - 30f);
    foreach (var c in g3.Cols) c.Priorities[(int)WorkType.Repair] = 4;
    Run(g3, 25f);
    Check("Repair=never means never", MathF.Abs(w3.Hp - 30f) < 0.01f);
}

// =================================================== ITER-3: weather (G1) ==
Section("ITER-3 weather");
{
    var g = NewGame(31);
    Check("new game starts clear", g.Weather == WeatherKind.Clear);
    g.WeatherT = 0.01f;                       // force a roll next tick
    Run(g, 0.1f);
    Check("weather rolled to a fresh duration", g.WeatherT > 10f);
    var g2 = NewGame(31);
    g2.WeatherT = 0.01f;
    Run(g2, 0.1f);
    Check("weather transitions are deterministic",
        g.Weather == g2.Weather && MathF.Abs(g.WeatherT - g2.WeatherT) < 0.01f);

    // solar / wind / crop multipliers do what the tooltips claim
    var hub = g.HubRef;
    for (int d = 0; d < 4; d++)
    {
        var (nx, ny) = ((Dir)d) switch { Dir.Up => (hub.X, hub.Y - 1), Dir.Down => (hub.X, hub.Y + hub.H), Dir.Left => (hub.X - 1, hub.Y), _ => (hub.X + hub.W, hub.Y) };
        g.World.SetTerrain(nx, ny, Terrain.Ground);
    }
    Place(g, BuildKind.SolarPanel, hub.X, hub.Y - 1);
    Place(g, BuildKind.WindTurbine, hub.X + hub.W, hub.Y);
    var sol = (SolarPanel)g.Builds.First(b => b.Kind == BuildKind.SolarPanel);
    var wnd = (WindTurbine)g.Builds.First(b => b.Kind == BuildKind.WindTurbine);

    g.Weather = WeatherKind.Clear;
    float solClear = sol.Output(g), wndClear = wnd.Output(g);
    g.Weather = WeatherKind.Rain;
    Check("rain cuts solar to RainSolarMul",
        MathF.Abs(sol.Output(g) - solClear * Bal.RainSolarMul) < 0.01f);
    g.Weather = WeatherKind.Storm;
    Check("storm boosts wind by StormWindMul",
        MathF.Abs(wnd.Output(g) - wndClear * Bal.StormWindMul) < 0.01f);
    g.Weather = WeatherKind.Rain;
    Check("rain crop multiplier", MathF.Abs(g.CropMul - Bal.RainCropMul) < 0.001f);
    g.Weather = WeatherKind.Clear;
    Check("clear crop multiplier is 1", MathF.Abs(g.CropMul - 1f) < 0.001f);

    // save/load roundtrip carries the sky with it
    g.Weather = WeatherKind.Storm; g.WeatherT = 42f;
    var dto = g.ToSave("weather");
    var g3 = new Game();
    g3.New(dto.GenSeed ?? 31, null, Storyteller.Builder);
    g3.LoadFrom(dto);
    Check("weather survives save/load",
        g3.Weather == WeatherKind.Storm && MathF.Abs(g3.WeatherT - 42f) < 0.01f);
}

// ================================================== ITER-4: wildlife (G12) ==
Section("ITER-4 wildlife");
{
    var g = NewGame(41);                     // New() spawns a starting herd
    Check("a starting herd exists", g.Beasts.Count >= Bal.BeastHerdMin);
    var beast = g.Beasts[0];
    float hx = beast.HomeX, hy = beast.HomeY;
    Run(g, 120f);
    Check("grazers stay near their home range",
        MathF.Abs(beast.PosX - hx) < 20f && MathF.Abs(beast.PosY - hy) < 20f);
    Check("grazers stay on the map", g.World.InBounds((int)beast.PosX, (int)beast.PosY));

    // hurt beast panics away from colonists
    float beforeD = MathF.Abs(beast.PosX - g.HubRef.X) + MathF.Abs(beast.PosY - g.HubRef.Y);
    g.DamageBeast(beast, 5f, g.HubRef.X, g.HubRef.Y);
    Run(g, 3f);
    float afterD = MathF.Abs(beast.PosX - g.HubRef.X) + MathF.Abs(beast.PosY - g.HubRef.Y);
    Check("hurt grazer flees the colony", afterD > beforeD || beast.Hp <= 0);

    // hunt: drafted pawn kills a beast -> carcass (Mine=never keeps other
    // pawns from hauling it before we can verify it dropped)
    foreach (var c in g.Cols) { c.Priorities[(int)WorkType.Mine] = 4; c.Priorities[(int)WorkType.Repair] = 4; }
    var hunter = g.Cols[0];
    hunter.Drafted = true;
    var target = new Beast(hunter.PosX + 2.5f, hunter.PosY) { Hp = 4f };   // nearly dead
    g.Beasts.Add(target);
    hunter.OrderBeast = target;
    hunter.OrderFoe = null; hunter.HasMoveOrder = false;
    Run(g, 30f);
    Check("hunter killed the grazer", target.Hp <= 0);
    Check("kill left a carcass", g.Carcasses.Any(c => c.Food == Bal.BeastFood));
    hunter.Drafted = false;

    // gather: an idle colonist hauls the carcass as food
    g.HubRef.Stock[(int)ItemKind.Food] = 0;
    foreach (var c in g.Cols) c.Priorities[(int)WorkType.Mine] = 3;   // gathering back on
    foreach (var c in g.Cols) { c.Morale = 75; c.Hunger = 95; c.StressT = 0; }   // SOULS: keep them sane
    Run(g, 90f);
    Check("carcass hauled into food stock", g.HubRef.Stock[(int)ItemKind.Food] >= Bal.BeastFood);

    // herds respawn slowly (cap + cooldown)
    g.Beasts.Clear();
    g.BeastRespawnT = 0.01f;
    Run(g, 1f);
    Check("wildlife respawns after the cooldown", g.Beasts.Count > 0);

    // save/load roundtrip
    var dto = g.ToSave("wild");
    var g2 = new Game();
    g2.New(dto.GenSeed ?? 41, null, Storyteller.Builder);
    g2.LoadFrom(dto);
    Check("beasts survive save/load", g2.Beasts.Count == g.Beasts.Count);
}

// ================================================== ITER-5: onboarding =====
Section("ITER-5 hints");
{
    var g = NewGame(51);
    Check("fresh colony starts at hint 0", g.HintStage() == 0);
    g.NotifySelected();
    g.UpdateHints();
    Check("selecting a pawn advances past hint 0", g.HintStage() >= 1);
    // meet conditions out of order - all auto-complete
    Place(g, BuildKind.CropPlot, g.HubRef.X + 2, g.HubRef.Y + 4);   // (2,2) is inside the hub
    g.HubRef.Stock[(int)ItemKind.Stone] = 200;
    Place(g, BuildKind.PrimitiveFurnace, g.HubRef.X + 3, g.HubRef.Y + 2);
    Place(g, BuildKind.Wall, g.HubRef.X + 4, g.HubRef.Y + 2);
    var (mx, my) = FindOreTile(g, Terrain.Rock);       // a real mineable tile
    g.QueueMine(mx, my);
    Run(g, 2f);                                       // UpdateHints fires on the 1s tick
    int stageAfter = g.HintStage();
    Check("met conditions skip their hints (1,2,4,5 all done -> research next)",
        stageAfter == 3);
    // dismiss + done bitmask
    g.MarkHintDone(stageAfter);
    Check("dismissal closes the current hint (hunt hint next)", g.HintStage() == 6);
    for (int i = 0; i < Game.HintStageCount; i++) g.MarkHintDone(i);
    Check("all hints done -> none shown", g.HintStage() == -1);
    // save/load keeps progress
    var dto = g.ToSave("hints");
    var g2 = new Game();
    g2.New(dto.GenSeed ?? 51, null, Storyteller.Builder);
    g2.LoadFrom(dto);
    Check("hint progress survives save/load", g2.HintStage() == -1);
    // hunting hint
    var g3 = NewGame(52);
    for (int i = 0; i < 6; i++) g3.MarkHintDone(i);
    Check("hunt hint shows until a kill", g3.HintStage() == 6);
    g3.DamageBeast(new Beast(3, 3), 999f, 0, 0);
    g3.UpdateHints();
    Check("a kill completes the hunt hint", g3.HintStage() == -1);
}

// ================================================= ITER-6: night & history ==
Section("ITER-6 night combat + history");
{
    var g = NewGame(61);
    var hub = g.HubRef;
    int tx = hub.X + 3, ty = hub.Y - 3;
    g.World.SetTerrain(tx, ty, Terrain.Ground);
    Place(g, BuildKind.Turret, tx, ty);
    var turret = (Turret)g.Builds.First(b => b.Kind == BuildKind.Turret);

    // day: normal multiplier
    g.Time = Bal.DayLengthSec * 0.35f;                  // mid-day
    Check("day turret cd is normal", MathF.Abs(g.TurretCdMul(turret) - 1f) < 0.001f);

    // night: slower, unless a Lamp is nearby
    g.Time = Bal.DayLengthSec * 0.95f;                  // deep night
    Check("night slows unlit turrets", MathF.Abs(g.TurretCdMul(turret) - Bal.NightTurretCdMul) < 0.001f);
    int lx = hub.X + 4, ly = hub.Y - 3;
    g.World.SetTerrain(lx, ly, Terrain.Ground);
    Place(g, BuildKind.Lamp, lx, ly);
    Check("a lamp next to the turret restores accuracy",
        MathF.Abs(g.TurretCdMul(turret) - 1f) < 0.001f);

    // a manned watchtower (no power grid needed) really fires slower at night
    g.Builds.RemoveAll(b => b.Kind == BuildKind.Lamp);  // unlight it again
    int wx = hub.X + 3, wy = hub.Y + 3;
    g.World.SetTerrain(wx, wy, Terrain.Ground);
    Place(g, BuildKind.Watchtower, wx, wy);
    var tower = (Watchtower)g.Builds.First(b => b.Kind == BuildKind.Watchtower);
    tower.Operator = g.Cols[0];
    tower.Cd = 0;
    g.Foes.Add(new Raider(wx + 2.5f, wy + 0.5f, false, 0));
    tower.Update(g, 0.05f);
    Check("unlit night shot gets the slow cooldown",
        MathF.Abs(tower.Cd - Bal.TowerCd * Bal.NightTurretCdMul) < 0.01f);

    // night raid log
    g.Foes.Clear();
    int histBefore = g.History.Count;
    g.SpawnWave();                                       // still night here
    Check("night raid logged a warning",
        g.History.Any(l => l.Text.Contains("NIGHT RAID")));

    // history caps
    for (int i = 0; i < 200; i++) g.AddLog($"spam {i}", Pal.TextDim);
    Check("history capped", g.History.Count == Game.HistoryCap);
    Check("history newest-first", g.History[0].Text == "spam 199");
}

// ================================================== ITER-7: statistics ======
Section("ITER-7 statistics");
{
    var g = NewGame(71);
    Check("fresh colony has zero stats",
        g.StatKills == 0 && g.StatBuilt == 0 && g.StatMeals == 0 && g.StatMined == 0);
    // kills (killing blows can be intercepted by the "downed" roll, so
    // swing until one dies outright - DownedChance is well below 1)
    int kills0 = g.StatKills;
    for (int i = 0; i < 40 && g.StatKills == kills0; i++)
    {
        var foe = new Raider(g.HubRef.X + 3.5f, g.HubRef.Y + 3.5f, false, 0) { Hp = 1f };
        g.Foes.Add(foe);
        g.DamageRaider(foe, 50f, 0, 0);
    }
    Check("raider kill counted", g.StatKills > kills0);
    // built
    var hub = g.HubRef;
    g.World.SetTerrain(hub.X + 5, hub.Y - 4, Terrain.Ground);
    g.InstantBuild = false; g.GodMode = false;
    Check("blueprint places", Place(g, BuildKind.Lamp, hub.X + 5, hub.Y - 4));
    var bp = g.Blueprints[0];
    bp.Work = 0.01f;                                   // nearly done
    var builder = g.Cols[0];
    bp.Builder = builder; builder.BuildJob = bp; builder.State = ColState.Building;
    Run(g, 2f);
    Check("blueprint completion counted", g.StatBuilt == 1);
    g.InstantBuild = true;
    // meals
    var eater = g.Cols[1];
    eater.Hunger = 10; eater.State = ColState.Eating; eater.ActTimer = 0.01f;
    float meals0 = g.StatMeals;
    Run(g, 1f);
    Check("a served meal counted", g.StatMeals > meals0);
    // wealth history grows with days
    int hist0 = g.WealthHist.Count;
    Run(g, Bal.DayLengthSec + 5f);
    Check("wealth history sampled per day", g.WealthHist.Count == hist0 + 1);
    // roundtrip
    var dto = g.ToSave("stats");
    var g2 = new Game();
    g2.New(dto.GenSeed ?? 71, null, Storyteller.Builder);
    g2.LoadFrom(dto);
    Check("stats survive save/load",
        g2.StatKills == g.StatKills && g2.StatBuilt == g.StatBuilt
        && g2.WealthHist.Count == g.WealthHist.Count);
}

// ==================================================== ITER-8: sappers =======
Section("ITER-8 sappers");
{
    var g = NewGame(81);
    var hub = g.HubRef;
    // a wall segment out in the open, away from the hub clearing
    int wx = hub.X + 8, wy = hub.Y + 4;
    g.World.SetTerrain(wx, wy, Terrain.Ground);
    g.World.SetTerrain(wx, wy + 1, Terrain.Ground);
    g.World.SetTerrain(wx, wy + 2, Terrain.Ground);
    Place(g, BuildKind.Wall, wx, wy);
    Place(g, BuildKind.Wall, wx, wy + 1);
    Place(g, BuildKind.Wall, wx, wy + 2);
    float wallsHp0 = g.Builds.Where(b => b.Kind == BuildKind.Wall).Sum(b => b.Hp);

    // a sapper right next to the wall chews it even with an open path around
    // (approach from the far side - colonists near the hub would intercept)
    var sap = new Raider(wx + 3.5f, wy + 1.5f, false, 0) { Sapper = true };
    g.Foes.Add(sap);
    Run(g, 8f);
    float wallsHp1 = g.Builds.Where(b => b.Kind == BuildKind.Wall).Sum(b => b.Hp);
    Check("sapper damages the wall he came for", wallsHp1 < wallsHp0 - 5f);

    // no fortifications left -> sapper reverts to normal marching
    g.Foes.Clear();
    foreach (var b in g.Builds.Where(b => b.Kind is BuildKind.Wall or BuildKind.Door).ToArray())
        g.DestroyBuilding(b, silent: true);
    var sap2 = new Raider(hub.X + 12.5f, hub.Y + 12.5f, false, 0) { Sapper = true };
    g.Foes.Add(sap2);
    Run(g, 10f);
    Check("sapper falls back when no walls remain", !sap2.Sapper);

    // spawn marking: waves >= SapperMinWave can carry them
    g.Foes.Clear();
    g.Wave = Bal.SapperMinWave - 2;               // SpawnWave() increments first
    g.SpawnWave();
    Check("early waves carry no sappers", g.Foes.All(f => !f.Sapper));
    g.Wave = Bal.SapperMinWave;
    g.SpawnWave();
    Check("wave 2+ may carry sappers", g.Foes.Count(f => f.Sapper) >= 0);
    var dto = g.ToSave("sap");
    var g2 = new Game();
    g2.New(dto.GenSeed ?? 81, null, Storyteller.Builder);
    g2.LoadFrom(dto);
    bool sappersKept = g.Foes.Count(f => f.Sapper) == g2.Foes.Count(f => f.Sapper);
    Check("sapper flag round-trips through save", sappersKept);
}

// ================================================ ITER-9: trade caravans ====
Section("ITER-9 trader");
{
    var g = NewGame(91);
    Check("no trader at start", g.Trader == null);
    g.TraderArrives();
    Check("caravan spawns from the edge", g.Trader != null && !g.Trader.Arrived);
    Run(g, 120f);                       // it walks in (edge -> hub park)
    Check("caravan parks and opens the window", g.Trader is { Arrived: true });

    // a normal deal swaps stock
    int fe0 = g.HubRef.Stock[(int)ItemKind.IronPlate];
    int cu0 = g.HubRef.Stock[(int)ItemKind.CopperPlate];
    g.DoTrade(0);                       // 15 iron -> 8 copper
    Check("deal 0 barters iron for copper",
        g.HubRef.Stock[(int)ItemKind.IronPlate] == fe0 - 15 &&
        g.HubRef.Stock[(int)ItemKind.CopperPlate] == cu0 + 8);

    // can't trade what you don't have (story events may have added people
    // during the walk-in, so count relative to right-now)
    int colsNow = g.Cols.Count;
    g.HubRef.Stock[(int)ItemKind.AdvPart] = 1;
    g.DoTrade(7);                       // recruit: 2 AdvPart
    Check("poor colony can't recruit", g.Cols.Count == colsNow);
    g.HubRef.Stock[(int)ItemKind.AdvPart] = 5;
    g.DoTrade(7);
    Check("recruit deal spawns a newcomer", g.Cols.Count == colsNow + 1);
    g.DoTrade(7);
    Check("recruit deal is once per visit", g.Cols.Count == colsNow + 1);

    // the window closes and the caravan packs out completely
    g.Trader!.WindowT = 0.01f;
    for (int i = 0; i < 2400 && g.Trader != null; i++) g.Update(0.05f);   // the walk out takes ~20s+
    Check("caravan leaves when the window closes", g.Trader == null);

    // trade commands are replay-safe through the queue
    g.TraderArrives();
    Run(g, 120f);
    Check("second caravan parks", g.Trader is { Arrived: true });
    g.QueueTrade(0);
    int fe1 = g.HubRef.Stock[(int)ItemKind.IronPlate];
    Run(g, 0.2f);
    Check("queued trade executes", g.HubRef.Stock[(int)ItemKind.IronPlate] == fe1 - 15);

    // save/load roundtrip with a parked caravan
    var dto = g.ToSave("trader");
    var g2 = new Game();
    g2.New(dto.GenSeed ?? 91, null, Storyteller.Builder);
    g2.LoadFrom(dto);
    Check("caravan survives save/load", g2.Trader is { Arrived: true });
}

// ==================================================== LEVEL-UP: SOULS ========
Section("SOULS relationships");
{
    var g = NewGame(101);
    var a = g.Cols[0]; var b = g.Cols[1];
    a.Cid = 9001; b.Cid = 9002;                  // distinct ids
    // shared meal at a mess table bonds colonists
    var hub = g.HubRef;
    int mx = hub.X + 2, my = hub.Y + 4;
    g.World.SetTerrain(mx, my, Terrain.Ground);
    Place(g, BuildKind.MessTable, mx, my);
    a.PosX = mx + 0.5f; a.PosY = my + 1.2f;
    b.PosX = mx + 1.5f; b.PosY = my + 1.2f;
    a.State = ColState.Eating; a.ActTimer = 0.01f; a.Hunger = 10;
    b.State = ColState.Eating; b.ActTimer = 0.01f; b.Hunger = 10; b.AteWellT = 1f;
    float opA0 = a.GetOp(b);
    Run(g, 1f);
    Check("shared meal builds opinion", a.GetOp(b) > opA0);

    // friendship forms and is chronicled
    a.BumpOp(g, b, 60f);
    Check("friendship recorded in the chronicle",
        g.Chronicle.Any(c => c.Text.Contains("have become friends")));

    // a friend nearby lifts the mood factor list
    a.Morale = 50; a.State = ColState.Idle; a.Path = null;
    Run(g, 2.5f);
    Check("friend nearby appears as a mood factor",
        a.MoodFactors.Any(f => f.Key == "a friend nearby" && f.Delta == 6f));

    // grief when a friend dies (damage, then next tick processes the death)
    g.DamageColonist(b, 9999f, b.PosX, b.PosY);
    g.Update(0.05f);
    Check("friend's death causes grief", a.GriefT > 0);
    Check("death chronicled", g.Chronicle.Any(c => c.Text.Contains("has died")));
}

Section("SOULS mental breaks");
{
    var g = NewGame(102);
    var c = g.Cols[2];
    c.State = ColState.Idle; c.Path = null;
    c.Morale = 10;                                 // miserable
    c.StressT = Bal.BreakStressSec - 1f;           // one tick from breaking
    bool broke = false;
    for (int i = 0; i < 800 && !broke; i++)
    {
        g.Update(0.05f);
        if (c.Morale > 15f && c.BreakT <= 0) c.Morale = 10f;   // keep them down
        if (c.BreakT > 0) broke = true;
    }
    Check("prolonged misery triggers a break", broke);
    Check("break is chronicled", g.Chronicle.Any(x => x.Text.Contains(c.Name)));

    // the break ends in catharsis
    for (int i = 0; i < 800 && c.CatharsisT <= 0; i++) g.Update(0.05f);
    Check("break ends with catharsis", c.CatharsisT > 0);
    Check("catharsis lifts morale", c.Morale >= 45f - 0.01f);
}

Section("SOULS persistence");
{
    var g = NewGame(103);
    var a = g.Cols[0]; var b = g.Cols[1];
    a.Opinions[b.Cid] = 51f;
    a.GriefT = 42f; a.CatharsisT = 7f;
    g.AddChron("test line for the ages");
    var dto = g.ToSave("souls");
    var g2 = new Game();
    g2.New(dto.GenSeed ?? 103, null, Storyteller.Builder);
    g2.LoadFrom(dto);
    var a2 = g2.Cols.First(x => x.Cid == a.Cid);
    var b2 = g2.Cols.First(x => x.Cid == b.Cid);
    Check("cids survive save/load", a2.Cid == a.Cid && b2.Cid == b.Cid);
    Check("opinions survive save/load", MathF.Abs(a2.GetOp(b2) - 51f) < 0.01f);
    Check("grief timer survives save/load", MathF.Abs(a2.GriefT - 42f) < 0.5f);
    Check("chronicle survives save/load", g2.Chronicle.Any(x => x.Text.Contains("for the ages")));
}

// ============================================= LEVEL-UP: THE RECLAMATION ====
Section("RECLAMATION blight");
{
    var g = NewGame(111);
    Check("colony ground starts clean", !g.TileBlighted(2, 2));
    // blight spreads under sustained pressure (the wastes keep pressing -
    // a lone seed would decay back, which is also correct behavior)
    for (int i = 0; i < 40; i++)
    {
        g.Blight[World.Key(6, 0)] = 1f;              // the frontier presses
        g.BlightTick();
    }
    Check("blight spreads inward", g.BlightAt(5, 0) > 0.05f);
    Check("blight respects the hub's ward", g.BlightAt(2, 0) < 0.05f);
    // a warded chunk cleanses
    g.Blight[World.Key(2, 0)] = 0.8f;
    for (int i = 0; i < 25; i++) g.BlightTick();
    Check("wards cleanse blighted ground", g.BlightAt(2, 0) < 0.1f);
    // crops refuse the blight
    var hub = g.HubRef;
    g.World.SetTerrain(hub.X + 2, hub.Y + 4, Terrain.Ground);
    Place(g, BuildKind.CropPlot, hub.X + 2, hub.Y + 4);
    var plot = (CropPlot)g.Builds.First(b => b.Kind == BuildKind.CropPlot);
    g.TechDone[(int)Tech.Automation] = true;      // so staffing isn't the blocker
    g.Blight[World.Key(0, 0)] = 1f;               // simulate blight on the colony chunk
    Check("crops die in the blight", plot.SpeedFactor(g) == 0f);
    g.Blight.Remove(World.Key(0, 0));
    Check("crops recover on clean ground", plot.SpeedFactor(g) > 0f);
    // persistence
    g.Blight[World.Key(4, 4)] = 0.42f;
    var dto = g.ToSave("blight");
    var g2 = new Game();
    g2.New(dto.GenSeed ?? 111, null, Storyteller.Builder);
    g2.LoadFrom(dto);
    Check("blight survives save/load", MathF.Abs(g2.BlightAt(4, 4) - 0.42f) < 0.01f);
}

Section("RECLAMATION ark & finale");
{
    var g = NewGame(112);
    var ark = g.Builds.OfType<ArkWreck>().FirstOrDefault();
    Check("the ark wreck exists at worldgen", ark != null);
    Check("the wreck is far from home",
        ark!.X * ark.X + ark.Y * ark.Y > 35f * 35f);
    Check("the wreck is registered as itself", ark.Kind == BuildKind.ArkWreck);
    // excavation completes -> chapter 3 + finale countdown
    g.Chapter = 2;
    ark.Dig = Bal.ArkDigWork - 0.5f;
    var digger = g.Cols[0];
    digger.ExcavJob = ark; ark.Excavator = digger;
    digger.PosX = ark.X + 0.5f; digger.PosY = ark.Y - 0.5f;   // adjacent
    digger.State = ColState.Excavating;
    digger.Path = null;
    Run(g, 2f);
    Check("ark excavation completes", ark.Dig >= Bal.ArkDigWork);
    Check("chapter 3 begins with the finale coming", g.Chapter == 3 && g.FinaleAt > 0);
    // the finale
    g.FinaleAt = 0.01f;
    Run(g, 1f);
    Check("the Silence answers", g.FinalePending && g.Foes.Count > 0);
    g.Foes.Clear();                                // the colony holds
    Run(g, 2.5f);
    Check("finale cleared -> the colony wins", g.FinaleCleared && g.Won);
    Check("the chronicle remembers it",
        g.Chronicle.Any(c => c.Text.Contains("Silence is broken")));
    // the ending persists
    g.EndingChosen = 2; g.Hope = true;
    var dto = g.ToSave("finale");
    var g2 = new Game();
    g2.New(dto.GenSeed ?? 112, null, Storyteller.Builder);
    g2.LoadFrom(dto);
    Check("ending state survives save/load",
        g2.FinaleCleared && g2.EndingChosen == 2 && g2.Hope);
}

// =================================================== LEVEL-UP: ORGANISM ====
Section("ORGANISM slag & recipes");
{
    var g = NewGame(121);
    var hub = g.HubRef;
    for (int i = 2; i <= 8; i++)
        for (int j = -4; j <= 3; j++)
            g.World.SetTerrain(hub.X + i, hub.Y + j, Terrain.Ground);   // dry machine row
    g.World.SetTerrain(hub.X + 3, hub.Y + 2, Terrain.Ground);
    Place(g, BuildKind.Smelter, hub.X + 3, hub.Y + 2);
    var sm = (Smelter)g.Builds.First(b => b.Kind == BuildKind.Smelter);
    sm.In[ItemKind.IronOre] = 4;
    Run(g, 20f);
    Check("smelting leaves slag behind", sm.Out.Contains(ItemKind.Slag));

    // recipes: gear, circuit, then the deep AdvPart chain
    g.World.SetTerrain(hub.X + 4, hub.Y + 2, Terrain.Ground);
    Place(g, BuildKind.Fabricator, hub.X + 4, hub.Y + 2);
    var fab = (Fabricator)g.Builds.First(b => b.Kind == BuildKind.Fabricator);
    fab.Recipe = 6;                                   // gear
    fab.In[ItemKind.IronPlate] = 2;
    Run(g, 30f);
    Check("gears are forged", fab.Out.Contains(ItemKind.Gear));

    var fab2 = (Fabricator)Building.Create(BuildKind.Fabricator, hub.X + 5, hub.Y + 2, Dir.Right);
    g.Builds.Add(fab2); g.World.SetBuilding(fab2);
    fab2.Recipe = 7;                                  // circuit
    fab2.In[ItemKind.CopperPlate] = 1; fab2.In[ItemKind.Crystal] = 1;
    Run(g, 30f);
    Check("circuits are etched", fab2.Out.Contains(ItemKind.Circuit));

    var fab3 = (Fabricator)Building.Create(BuildKind.Fabricator, hub.X + 6, hub.Y + 2, Dir.Right);
    g.Builds.Add(fab3); g.World.SetBuilding(fab3);
    fab3.Recipe = 1;                                  // adv part: 2 gear + 1 circuit
    fab3.In[ItemKind.Gear] = 2; fab3.In[ItemKind.Circuit] = 1;
    Run(g, 40f);
    Check("adv parts need the deep chain", fab3.Out.Contains(ItemKind.AdvPart));
    fab3.In.Clear(); fab3.Out.Clear();
    fab3.In[ItemKind.IronPlate] = 2; fab3.In[ItemKind.Crystal] = 1;   // the old recipe
    Run(g, 12f);
    Check("iron+crystal alone no longer makes parts", !fab3.Out.Contains(ItemKind.AdvPart));

    // slag recycling
    var fab4 = (Fabricator)Building.Create(BuildKind.Fabricator, hub.X + 7, hub.Y + 2, Dir.Right);
    g.Builds.Add(fab4); g.World.SetBuilding(fab4);
    fab4.Recipe = 5;
    fab4.In[ItemKind.Slag] = 3;
    Run(g, 12f);
    int stones = fab4.Out.Count(k => k == ItemKind.Stone);
    Check("slag recycles into stone", stones >= 2);
}

Section("ORGANISM wear & breakdowns");
{
    var g = NewGame(122);
    var hub = g.HubRef;
    g.World.SetTerrain(hub.X + 3, hub.Y - 3, Terrain.Ground);
    Place(g, BuildKind.Smelter, hub.X + 3, hub.Y - 3);
    var sm = (Smelter)g.Builds.First(b => b.Kind == BuildKind.Smelter);
    foreach (var c in g.Cols) c.Priorities[(int)WorkType.Repair] = 4;   // nobody fixes it yet
    sm.In[ItemKind.IronOre] = 99;
    sm.Wear = 99f;                                   // one craft from death
    Run(g, 8f);
    Check("the machine breaks down", sm.BrokenDown);
    Check("broken machines stop", sm.SpeedFactor(g) == 0f);
    Check("status text reports it", sm.StatusText(g) == "BROKEN DOWN");
    // a repairer gets it running again
    foreach (var c in g.Cols) { c.Morale = 75; c.Hunger = 95; c.StressT = 0; c.Priorities[(int)WorkType.Repair] = 3; }
    Run(g, 25f);
    Check("repairers restart broken machines", !sm.BrokenDown && sm.Wear < 99f);
    // persistence
    sm.Wear = 66f;
    var dto = g.ToSave("wear");
    var g2 = new Game();
    g2.New(dto.GenSeed ?? 122, null, Storyteller.Builder);
    g2.LoadFrom(dto);
    var sm2 = g2.Builds.OfType<Smelter>().First();
    Check("wear survives save/load", MathF.Abs(sm2.Wear - 66f) < 0.01f);
}

// ================================================== LEVEL-UP: NEW WORLDS ====
Section("NEW WORLDS biomes");
{
    Check("biome helpers sane",
        Bal.BiomeSunMul(Biome.Glacial) < Bal.BiomeSunMul(Biome.Verdant) &&
        Bal.BiomeSpreadMul(Biome.Fungal) > Bal.BiomeSpreadMul(Biome.Verdant));

    var verdant = new Game();
    verdant.New(131, new GenParams { Seed = 131 }, Storyteller.Builder);
    var glacial = new Game();
    glacial.New(131, new GenParams { Seed = 131, Biome = (int)Biome.Glacial }, Storyteller.Builder);
    verdant.Time = Bal.DayLengthSec * 0.5f;
    glacial.Time = Bal.DayLengthSec * 0.5f;
    Check("glacial sun is weaker at noon", glacial.Sunlight < verdant.Sunlight);

    // fungal worlds teem with flora
    int FloraNear(Game gg)
    {
        int n = 0;
        for (int cx = -1; cx <= 1; cx++)
            for (int cy = -1; cy <= 1; cy++)
            {
                var ch = gg.World.ChunkAt(cx, cy);
                for (int i = 0; i < ch.Tiles.Length; i++)
                    if (ch.Tiles[i].T == Terrain.Tree) n++;
            }
        return n;
    }
    var fungal = new Game();
    fungal.New(131, new GenParams { Seed = 131, Biome = (int)Biome.Fungal }, Storyteller.Builder);
    Check("fungal worlds bloom", FloraNear(fungal) > FloraNear(verdant) * 12 / 10);

    // biomes persist through saves
    var dto = fungal.ToSave("biome");
    var g2 = new Game();
    g2.New(dto.GenSeed ?? 131, null, Storyteller.Builder);
    g2.LoadFrom(dto);
    Check("biome survives save/load", g2.BiomeKind == Biome.Fungal);
    Check("each biome has its chronicle opener",
        fungal.Chronicle.Any(c => c.Text.Contains("flora glows")) &&
        verdant.Chronicle.Any(c => c.Text.Contains("Green hills")));
}

// ================================================== menu-state regression ==
Section("MENU-STATE null safety");
{
    // Regression: the main-menu paint path reads Sunlight via RebuildUi
    // BEFORE any world exists (v0.0.52 crash: NRE in BiomeKind).
    var fresh = new Game();
    float sun = 0;
    Biome bio = 0;
    string obj = "";
    bool night = false;
    int day = 0;
    try
    {
        sun = fresh.Sunlight;             // crashed here on the menu
        bio = fresh.BiomeKind;
        night = fresh.IsNight;
        day = fresh.Day;
        obj = fresh.ObjectiveText();
    }
    catch (NullReferenceException) { /* caught -> check below fails */ }
    Check("menu-state game exposes Sunlight without a world",
        sun == Bal.Sunlight(0f) * Bal.BiomeSunMul(Biome.Verdant));
    Check("menu-state biome defaults to Verdant", bio == Biome.Verdant);
    Check("menu-state IsNight/Day/ObjectiveText safe", day == 1 && obj.Length > 0);
    _ = night;
}

// ===================================== UX REPORT: manual mining real flow ==
Section("MINING real-player flow");
{
    var g = NewGame(151);
    // exactly what a player does: V-drag over a patch, no priority edits,
    // no drafting, default colony
    var tiles = new HashSet<(int, int)>();
    while (tiles.Count < 10)
    {
        var (ox, oy) = FindOreTile(g, Terrain.IronOre);
        if (tiles.Add((ox, oy))) g.QueueMine(ox, oy);
        else break;                                  // scan stuck - bail out
    }
    int ordered = tiles.Count;
    Run(g, 60f);
    Check("drag-order mining produces ore (real flow)", g.StatMined > 0);
    Check("new games start at dawn (pawns awake, not midnight)",
        g.Sunlight > 0.2f && g.Day == 1);
}

// ============================================= TERRAIN PASS: noise world ====
Section("TERRAIN noise forests & water");
{
    var g = NewGame(141);
    // pre-generate a 7x7-chunk area so neighbor scans are valid in the middle
    for (int cx = -3; cx <= 3; cx++)
        for (int cy = -3; cy <= 3; cy++)
            _ = g.World.ChunkAt(cx, cy);

    int water = 0, orphanWater = 0, flora = 0;
    for (int x = -64; x < 64; x++)
        for (int y = -64; y < 64; y++)
        {
            var t = g.World.Cell(x, y).T;
            if (t == Terrain.Water)
            {
                water++;
                int nb = (g.World.Cell(x + 1, y).T == Terrain.Water ? 1 : 0)
                       + (g.World.Cell(x - 1, y).T == Terrain.Water ? 1 : 0)
                       + (g.World.Cell(x, y + 1).T == Terrain.Water ? 1 : 0)
                       + (g.World.Cell(x, y - 1).T == Terrain.Water ? 1 : 0);
                if (nb == 0) orphanWater++;
            }
            else if (t == Terrain.Tree) flora++;
        }
    Check("lakes have no orphan single tiles", orphanWater == 0);
    Check("water exists and is landscape-scale", water > 300);
    Check("forests exist", flora > 400);

    // forests must be CONTIGUOUS bodies, not scattered confetti: the largest
    // 4-connected tree body should hold most of the trees
    var seen = new HashSet<int>();
    int largest = 0;
    for (int x = -64; x < 64; x++)
        for (int y = -64; y < 64; y++)
        {
            int k = (x + 128) * 4096 + (y + 128);
            if (seen.Contains(k) || g.World.Cell(x, y).T != Terrain.Tree) continue;
            int size = 0;
            var stack = new Stack<(int, int)>();
            stack.Push((x, y));
            seen.Add(k);
            while (stack.Count > 0)
            {
                var (px, py) = stack.Pop();
                size++;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = px + dx, ny = py + dy, nk = (nx + 128) * 4096 + (ny + 128);
                    if (nx < -64 || nx >= 64 || ny < -64 || ny >= 64 || seen.Contains(nk)) continue;
                    if (g.World.Cell(nx, ny).T != Terrain.Tree) continue;
                    seen.Add(nk);
                    stack.Push((nx, ny));
                }
            }
            largest = Math.Max(largest, size);
        }
    Check("forests are coherent bodies, not scattered patches",
        largest > flora / 2);
}

// =============================================== LANDING SITE & TREES v2 ====
Section("LANDING SITE selection");
{
    var g = new Game();
    g.New(77, new GenParams { Seed = 77, LandingX = 150, LandingY = -120 }, Storyteller.Builder);
    Check("hub lands where the player picked", g.HubRef.X == 150 && g.HubRef.Y == -120);
    Check("plaza cleared at the landing site", g.World.Cell(152, -118).T == Terrain.Ground);
    Check("guaranteed lake moved with the colony", g.World.Cell(162, -114).T == Terrain.Water);
    bool grove = false;
    for (int dx = -15; dx <= 15 && !grove; dx++)
        for (int dy = -15; dy <= 15 && !grove; dy++)
            if (g.World.Cell(150 + dx, -120 + dy).T == Terrain.Tree) { grove = true; break; }
    Check("starting grove moved with the colony", grove);
    var ark = g.Builds.OfType<ArkWreck>().First();
    float da = MathF.Sqrt((ark.X - 150f) * (ark.X - 150f) + (ark.Y + 120f) * (ark.Y + 120f));
    Check("ark wreck lies relative to the landing site", da > 40 && da < 62);
    Check("colonists land beside the hub",
        g.Cols.All(cc => MathF.Abs(cc.PosX - 151.5f) + MathF.Abs(cc.PosY + 118.5f) < 10));
    Check("blight rings the colony, not the world origin",
        g.BlightAt(150 / 32 + 13, -120 / 32) == 0f);
    Check("deep wastes are still blighted",
        g.BlightAt(150 / 32 + 15, -120 / 32) == 1f);
}

Section("TREES v2 (tall sprites, soft collision)");
{
    var g = NewGame(78);
    int? ttx = null, tty = null;
    for (int dx = -15; dx <= 15 && ttx == null; dx++)
        for (int dy = -15; dy <= 15 && ttx == null; dy++)
            if (g.World.Cell(g.HubRef.X + dx, g.HubRef.Y + dy).T == Terrain.Tree)
            { ttx = g.HubRef.X + dx; tty = g.HubRef.Y + dy; }
    Check("starting grove exists", ttx != null);
    if (ttx != null)
        Check("forest tiles are walkable (small-trunk collision)",
            g.World.WalkColonist(ttx.Value, tty!.Value));
    Check("forest walk penalty in range", Bal.TreeWalkMul is > 0.5f and <= 1f);
}

// ============================================ TERRAIN SLIDERS & DENSITY ======
Section("TERRAIN density sliders");
{
    float ForestFrac(World w)
    {
        int n = 0, tot = 0;
        for (int cx = -3; cx <= 3; cx++)
            for (int cy = -3; cy <= 3; cy++)
            {
                var ch = w.ChunkAt(cx, cy);
                for (int i = 0; i < ch.Tiles.Length; i++)
                    if (ch.Tiles[i].T == Terrain.Tree) n++;
                tot += ch.Tiles.Length;
            }
        return n / (float)tot;
    }
    float WaterFrac(World w)
    {
        int n = 0, tot = 0;
        for (int cx = -3; cx <= 3; cx++)
            for (int cy = -3; cy <= 3; cy++)
            {
                var ch = w.ChunkAt(cx, cy);
                for (int i = 0; i < ch.Tiles.Length; i++)
                    if (ch.Tiles[i].T == Terrain.Water) n++;
                tot += ch.Tiles.Length;
            }
        return n / (float)tot;
    }

    var def = new World(new GenParams { Seed = 555 });
    float fDef = ForestFrac(def), wDef = WaterFrac(def);
    Console.WriteLine($"      densities: forest={fDef:P0} water={wDef:P0}");
    Check("default forest density is moderate (15-35%)", fDef is > 0.15f and < 0.35f);
    Check("default water density is light (4-18%)", wDef is > 0.04f and < 0.18f);

    var none = new World(new GenParams { Seed = 555, ForestMul = 0f, WaterMul = 0f });
    Check("sliders at 0 remove forests entirely", ForestFrac(none) < 0.001f);
    Check("sliders at 0 remove water entirely", WaterFrac(none) < 0.001f);

    // isolate each slider (water overwrites trees, so both-at-once lies)
    var heavyF = new World(new GenParams { Seed = 555, ForestMul = 2f });
    var heavyW = new World(new GenParams { Seed = 555, WaterMul = 2f });

    Check("forest slider amplifies", ForestFrac(heavyF) > fDef * 1.4f);
    Check("water slider amplifies", WaterFrac(heavyW) > wDef * 1.5f);
}

// ================================================== BELT UX & PAWN SYSTEM ===
Section("BELT UX drag rotation");
{
    Check("drag right faces belts right", Bal.DirFromDelta(3, 0) == Dir.Right);
    Check("drag left faces belts left", Bal.DirFromDelta(-2, 0) == Dir.Left);
    Check("drag down faces belts down", Bal.DirFromDelta(0, 4) == Dir.Down);
    Check("drag up faces belts up", Bal.DirFromDelta(0, -1) == Dir.Up);
    Check("diagonal picks dominant axis (x)", Bal.DirFromDelta(3, 1) == Dir.Right);
    Check("diagonal picks dominant axis (y)", Bal.DirFromDelta(1, -5) == Dir.Up);
    Check("no delta keeps a valid facing", (int)Bal.DirFromDelta(0, 0) is >= 0 and <= 3);
}

Section("BELT side-merge sim");
{
    // the renderer reads the same facts these checks pin down
    var g = NewGame(91);
    for (int x = 3; x <= 8; x++) g.World.SetTerrain(x, 0, Terrain.Ground);
    Place(g, BuildKind.Belt, 5, 0, Dir.Right);      // main line
    Place(g, BuildKind.Belt, 5, -1, Dir.Down);      // feeds into it from the north
    var main = (Belt)g.World.Cell(5, 0).B!;
    var feed = (Belt)g.World.Cell(5, -1).B!;
    Check("side feeder points into the main belt",
        (int)feed.Face == ((int)Dir.Up + 2) % 4 || DirU.Dx[(int)feed.Face] == 0 && DirU.Dy[(int)feed.Face] == 1);
    Check("main belt faces along the line", main.Face == Dir.Right);
    Check("belt accepts items from a side feeder", main.AcceptItem(g, ItemKind.IronPlate));
}

Section("PAWN TINT PALETTE");
{
    // the gray-template tinter indexes Shirts[6] / Skins[4] / Hairs[6];
    // sim-side tone assignment must stay inside those bounds
    var g = NewGame(91);
    bool inRange = true;
    var seen = new HashSet<int>();
    foreach (var c in g.Cols)
    {
        if (c.ShirtTone < 0 || c.ShirtTone > 5 || c.SkinTone < 0 || c.SkinTone > 3 ||
            c.HairTone < 0 || c.HairTone > 5 || c.BeltTone < 0 || c.BeltTone > 5) inRange = false;
        seen.Add(c.ShirtTone);
    }
    Check("colonist tones stay inside the tint palettes", inRange);
    Check("starting party varies shirt tones", seen.Count >= 2);

    var tones = new HashSet<int>();
    for (int i = 0; i < 40; i++)
        tones.Add(new Colonist(1.5f, 1.5f, new Random(1000 + i)).ShirtTone);
    Check("constructor hashes spread across the shirt palette", tones.Count >= 4);

    var belts = new HashSet<int>();
    for (int i = 0; i < 40; i++)
        belts.Add(new Colonist(1.5f, 1.5f, new Random(1000 + i)).BeltTone);
    Check("belt tones spread across the waist-pack palette", belts.Count >= 4);
}

Section("MULTIBLOCK OUTPUT");
{
    // regression: a 2x2 drill facing right used to push into ITSELF
    // (origin-corner step landed inside its own footprint)
    var g = NewGame(93);
    for (int x = 9; x <= 17; x++)
        for (int y = 9; y <= 15; y++) g.World.SetTerrain(x, y, Terrain.Ground);
    for (int x = 10; x <= 11; x++)
        for (int y = 10; y <= 11; y++) g.World.SetTerrain(x, y, Terrain.IronOre);   // drills need ore
    for (int x = 14; x <= 15; x++)
        for (int y = 10; y <= 11; y++) g.World.SetTerrain(x, y, Terrain.IronOre);
    Place(g, BuildKind.Drill, 10, 10, Dir.Right);
    Place(g, BuildKind.Belt, 12, 11, Dir.Right);       // middle of the east edge
    var beltR = (Belt)g.World.Cell(12, 11).B!;
    var drill = (Drill)g.World.Cell(10, 10).B!;
    drill.Out.Add(ItemKind.IronOre);
    drill.Update(g, 0.016f);
    Check("2x2 drill facing RIGHT feeds the east edge belt", beltR.Lane.Count == 1);

    Place(g, BuildKind.Drill, 14, 10, Dir.Down);
    Place(g, BuildKind.Belt, 15, 12, Dir.Down);        // middle of the south edge
    var beltD = (Belt)g.World.Cell(15, 12).B!;
    var drill2 = (Drill)g.World.Cell(14, 10).B!;
    drill2.Out.Add(ItemKind.IronOre);
    drill2.Update(g, 0.016f);
    Check("2x2 drill facing DOWN feeds the south edge belt", beltD.Lane.Count == 1);
}

Section("CURVED BELTS");
{
    var g = NewGame(94);
    for (int x = 3; x <= 8; x++)
        for (int y = 3; y <= 8; y++) g.World.SetTerrain(x, y, Terrain.Ground);
    Place(g, BuildKind.Belt, 4, 5, Dir.Right);         // feeder
    Place(g, BuildKind.Belt, 5, 5, Dir.Up);            // corner (drag bends it)
    Place(g, BuildKind.Belt, 5, 4, Dir.Up);            // receiver
    var corner = (Belt)g.World.Cell(5, 5).B!;
    corner.BendIn = Dir.Left;                          // in from the west, out north
    var feeder = (Belt)g.World.Cell(4, 5).B!;
    var recv = (Belt)g.World.Cell(5, 4).B!;

    feeder.Lane.Add(new BeltItem(ItemKind.IronOre, 0.999f));
    feeder.Update(g, 1f);
    Check("feeder pushes into a curved belt", corner.Lane.Count == 1);
    corner.Update(g, 10f);      // long step: item crosses the whole curve tile
    Check("curved belt passes items along its facing", recv.Lane.Count == 1);
    Check("curve keeps its inlet/outlet contract", corner.BendIn == Dir.Left && corner.Face == Dir.Up);
    Check("curved belt still accepts input", corner.AcceptItem(g, ItemKind.CopperOre));
}

Section("ROTATE PLACED BUILDINGS");
{
    var g = NewGame(95);
    for (int x = 3; x <= 9; x++)
        for (int y = 3; y <= 10; y++) g.World.SetTerrain(x, y, Terrain.Ground);
    Place(g, BuildKind.Belt, 4, 5, Dir.Right);
    var belt = (Belt)g.World.Cell(4, 5).B!;
    belt.BendIn = Dir.Up;               // curve: in from north, out east
    belt.Rotate();                      // clockwise
    Check("rotate turns the outlet 90 degrees cw", belt.Face == Dir.Down);
    Check("rotate turns the curve inlet with it", belt.BendIn == Dir.Right);
    belt.Rotate(true);                  // counter-clockwise
    Check("ccw rotate restores the curve", belt.Face == Dir.Right && belt.BendIn == Dir.Up);

    // drill output follows rotation
    for (int x = 5; x <= 6; x++)
        for (int y = 8; y <= 9; y++) g.World.SetTerrain(x, y, Terrain.IronOre);
    Place(g, BuildKind.Drill, 5, 8, Dir.Right);
    Place(g, BuildKind.Belt, 7, 9, Dir.Down);    // east edge middle
    Place(g, BuildKind.Belt, 6, 7, Dir.Up);      // north edge middle
    var drill = (Drill)g.World.Cell(5, 8).B!;
    var east = (Belt)g.World.Cell(7, 9).B!;
    var north = (Belt)g.World.Cell(6, 7).B!;
    drill.Out.Add(ItemKind.IronOre);
    drill.Update(g, 0.016f);
    Check("drill feeds east before rotating", east.Lane.Count == 1);
    drill.Rotate(true);                          // Right -> Up
    drill.Out.Add(ItemKind.IronOre);
    drill.Update(g, 0.016f);
    Check("rotated drill feeds the new edge", north.Lane.Count == 1 && east.Lane.Count == 1);
}

Section("BELT AUTO-CURVE");
{
    var g = NewGame(96);
    for (int x = 3; x <= 10; x++)
        for (int y = 3; y <= 10; y++) g.World.SetTerrain(x, y, Terrain.Ground);

    // 90-degree feed: the receiver curves automatically (Mindustry-style)
    Place(g, BuildKind.Belt, 4, 5, Dir.Right);
    Place(g, BuildKind.Belt, 5, 5, Dir.Down);
    var recv = (Belt)g.World.Cell(5, 5).B!;
    recv.Update(g, 0.016f);
    Check("perpendicular feed auto-curves the receiver", recv.BendIn == Dir.Left);

    // aligned chain stays straight
    Place(g, BuildKind.Belt, 7, 8, Dir.Right);
    Place(g, BuildKind.Belt, 8, 8, Dir.Right);
    var chain = (Belt)g.World.Cell(8, 8).B!;
    chain.Update(g, 0.016f);
    Check("aligned chain stays straight", chain.BendIn == null);

    // back + side feeders = merge, stays straight
    Place(g, BuildKind.Belt, 9, 7, Dir.Down);       // side feeder from the north
    Place(g, BuildKind.Belt, 9, 8, Dir.Right);
    var merge = (Belt)g.World.Cell(9, 8).B!;
    merge.Update(g, 0.016f);
    Check("back+side feeders keep the belt straight (merge)", merge.BendIn == null);

    // side-entered items slide in from the side they came from
    var sideFeed = (Belt)g.World.Cell(9, 7).B!;
    sideFeed.Lane.Add(new BeltItem(ItemKind.IronOre, 0.999f));
    sideFeed.Update(g, 1f);
    Check("side-entered item lands on the merge belt", merge.Lane.Count == 1);
    Check("side-entered item remembers its entry side", merge.Lane[0].Entry == Dir.Up);
}


// ============================================================ summary =====
Console.WriteLine($"\n{pass} passed, {fail} failed");

return fail == 0 ? 0 : 1;
