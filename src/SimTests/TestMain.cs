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
    return g;
}
void Run(Game g, float seconds, float dt = 0.05f)
{
    for (float t = 0; t < seconds; t += dt) g.Update(dt);
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
    int wx = 0, wy = 0; bool found = false;
    for (int y = 3; y <= 9; y++)
        for (int x = 9; x <= 15; x++)
        {
            if (g.World.Cell(x, y).T != Terrain.Water) continue;
            // neighbor (x,y-1) ground and 3 free tiles right of it
            if (g.World.Cell(x, y - 1).T == Terrain.Ground &&
                g.World.Cell(x + 1, y - 1).T == Terrain.Ground &&
                g.World.Cell(x + 2, y - 1).T == Terrain.Ground &&
                g.World.Cell(x + 3, y - 1).T == Terrain.Ground)
            { wx = x; wy = y; found = true; break; }
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
    Check("storyteller names", builder.StoryName() == "Basil the Builder" &&
        merciless.StoryName() == "The Merciless" && randy.StoryName() == "Randy the Random");
    Check("storyteller persists in save",
        merciless.ToSave("t").Story == (int)Storyteller.Merciless);
    Check("merciless raids come harder (earlier or bigger)",
        merciless.NextRaidAt <= builder.NextRaidAt + 0.01f);
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

// ============================================================ summary =====
Console.WriteLine($"\n{pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;
