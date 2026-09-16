using System.Drawing;
using System.Text;

namespace ForgeHaven;

// ---------------------------------------------------------------------------
//  Game: the whole simulation. Owns the open world, economy, LOCAL POWER
//  GRIDS, FLUID networks, trains, guard bots, prisoners, pollution, the
//  storyteller, wealth-scaled raids, alerts, the deterministic command queue
//  (multiplayer groundwork) and save/load rehydration.
// ---------------------------------------------------------------------------

/// <summary>One local electric network: poles wired together plus every
/// machine/generator/battery under their coverage.</summary>
public sealed class PowerGrid
{
    public readonly List<Building> Nodes = new();
    public readonly List<Building> Attached = new();
    public float Supply, Demand, Stored, Cap, Frac;

    public void Update(Game g, float dt)
    {
        float supply = 0, demand = 0, cap = 0, battRate = 0;

        foreach (var n in Nodes)
            if (n is Hub) supply += Bal.BaseSupply;

        foreach (var b in Attached)
        {
            switch (b)
            {
                case Reactor: supply += Bal.ReactorSupply; break;
                case SolarPanel sp: supply += sp.Output(g); break;
                case WindTurbine wt: supply += wt.Output(g); break;
                case SteamEngine se: supply += se.CurrentOutput; break;
                case Battery: cap += Bal.BatteryCap; battRate += Bal.BatteryRate; break;
            }
            demand += b.PowerDemand;
        }
        foreach (var n in Nodes) demand += n.PowerDemand;

        if (demand > supply && Stored > 0 && cap > 0)
        {
            float draw = Math.Min(demand - supply, battRate * dt);
            draw = Math.Min(draw, Stored);
            supply += draw;
            Stored -= draw;
        }
        else if (supply > demand && cap > 0 && Stored < cap)
        {
            float charge = Math.Min(supply - demand, battRate * dt);
            Stored = Math.Min(cap, Stored + charge);
        }

        Supply = supply;
        Demand = demand;
        Cap = cap;
        Frac = demand <= 0 ? 1f : Math.Min(1f, supply / demand);
    }
}

/// <summary>One connected pipe run carrying a single fluid (water or steam).</summary>
public sealed class PipeNet
{
    public readonly List<Building> Members = new();
    public FluidKind Kind = FluidKind.None;
    public float Amount, Cap;
    public float Fill01 => Cap <= 0 ? 0 : Amount / Cap;
}

public sealed class Game
{
    public World World = null!;
    public readonly List<Building> Builds = new();
    public readonly List<Colonist> Cols = new();
    public readonly List<Raider> Foes = new();
    public readonly List<Beast> Beasts = new();      // MADDOG iter-4: wildlife
    public readonly List<Carcass> Carcasses = new(); // MADDOG iter-4: hunting
    public float BeastRespawnT = 240f;
    public bool HuntedOnce;                          // iter-5: hint condition
    public Trader? Trader;                           // MADDOG iter-9: caravan in town

    // MADDOG iter-5: first-colony onboarding hints (M2 lite). Bitmask of
    // stages the player has completed; conditions auto-advance in UpdateHints.
    public const int HintStageCount = 7;
    public int HintsDone;
    private bool _selSeen;
    private float _hintT;
    public readonly List<Train> Trains = new();
    public readonly List<GuardBot> Bots = new();
    public readonly List<Prisoner> Prisoners = new();
    public readonly List<Particle> Fx = new();
    public readonly List<LogLine> Log = new();
    public readonly List<AlertLine> Alerts = new();
    public Hub HubRef = null!;

    // Phase 1: designation-based construction + manual mining
    public bool InstantBuild;                       // tests / god mode: place = built
    public readonly List<Blueprint> Blueprints = new();
    public readonly List<MineOrder> MineOrders = new();
    public readonly List<ItemPile> ItemPiles = new();          // dropped cargo
    public readonly HashSet<long> PileZones = new();           // STOCKPILE: designated tiles
    public readonly Dictionary<long, ItemPile> PileCells = new();   // per-tile zone contents
    private float _haulScanT;
    private readonly Dictionary<long, Blueprint> _bpAt = new();
    private readonly Dictionary<long, MineOrder> _mineAt = new();

    /// <summary>Seeded RNG — ALL simulation randomness flows from here so the
    /// game is deterministic (multiplayer/replay groundwork).</summary>
    public Random Rand = new();

    public float Time;
    // MADDOG iter-3: ambient weather state
    public WeatherKind Weather = WeatherKind.Clear;
    public float WeatherT = 240f;               // seconds until the sky changes
    public float SolarMul => Weather switch { WeatherKind.Rain => Bal.RainSolarMul, WeatherKind.Storm => Bal.StormSolarMul, _ => 1f };
    public float WindMul => Weather == WeatherKind.Storm ? Bal.StormWindMul : 1f;
    public float CropMul => Weather == WeatherKind.Rain ? Bal.RainCropMul : 1f;
    // UI transparency: smoothed per-day stock delta (recomputed every 5s of game time)
    public readonly float[] NetPerDay = new float[Bal.ItemCount];
    private readonly int[] _stockPrev = new int[Bal.ItemCount];
    private float _stockRateT;
    private bool _stockPrimed;
    public int Day => (int)(Time / Bal.DayLengthSec) + 1;
    public float DayFrac01 => (Time % Bal.DayLengthSec) / Bal.DayLengthSec;
    /// <summary>Null-safe on purpose: the main-menu paint path reads Sunlight
    /// before any world exists (World is null until New()). Verdant = x1 sun,
    /// i.e. exactly the pre-biome behavior.</summary>
    public Biome BiomeKind => World?.P != null ? (Biome)World.P.Biome : Biome.Verdant;
    public float Sunlight => Bal.Sunlight(DayFrac01) * Bal.BiomeSunMul(BiomeKind);
    public bool IsNight => Sunlight < 0.15f;

    // power (local grids)
    public readonly List<PowerGrid> Grids = new();
    private readonly Dictionary<Building, PowerGrid> _gridOf = new();
    public bool PowerDirty = true;
    public int PowerSupplyTotal, PowerDemandTotal;
    public float BatteryFrac01 { get; private set; }

    // fluids
    public readonly List<PipeNet> Fluids = new();
    private readonly Dictionary<Building, PipeNet> _fluidOf = new();
    private readonly Dictionary<Boiler, List<PipeNet>> _boilerIn = new();
    private readonly Dictionary<Boiler, PipeNet?> _boilerOut = new();
    public bool FluidDirty = true;
    private float _fluidT;

    // research
    public readonly bool[] TechDone = new bool[Bal.TechCount];
    public readonly float[] TechProg = new float[Bal.TechCount];
    public readonly int[] TechLevels = new int[Bal.TechCount];
    public Tech ActiveTech = Tech.Automation;

    // raids & storyteller
    public Storyteller Story = Storyteller.Builder;
    public float Wealth { get; private set; }
    public float Threat;
    public float NextRaidAt = Bal.FirstRaidAt;
    public int Wave;
    public float RecentCombat;
    public float RaidBannerT;
    public float RaidPingX, RaidPingY;
    public float TotalPollution;          // recomputed by the pollution pass
    public float DebugWealthBonus;
    private float _randThreatMul = 1f;
    private float _eventT = 100f;

    public bool Won, Lost, ContinueAfterWin;
    public bool GodMode;                  // dev menu

    // trains
    public readonly Dictionary<(int x, int y), Train> TrainRes = new();

    // command layer (multiplayer groundwork)
    public readonly Queue<SimCmd> CmdQueue = new();
    public readonly List<string> CmdLog = new();

    public float BeltSpeed => Has(Tech.Overclock) ? Bal.BeltSpeedFast : Bal.BeltSpeed;
    public bool Has(Tech t) => t != Tech.None && TechDone[(int)t];
    public int TechLevel(Tech t) => t == Tech.None ? 0 : TechLevels[(int)t];

    private float _throttleMsg;
    private string _lastThrottled = "";
    private float _wealthTimer;
    private float _powerTimer;
    private float _alertTimer;
    private float _pollTimer;
    private float _assignTimer;

    // ------------------------------------------------------------ lifecycle

    public void New(long? seed = null, GenParams? p = null, Storyteller story = Storyteller.Builder)
    {
        _stockPrimed = false;      // stock-rate tracker restarts with the new world
        var par = p ?? new GenParams();
        if (seed.HasValue) par.Seed = seed.Value;
        World = new World(par.Clone());
        Rand = new Random(unchecked((int)(par.Seed ^ (par.Seed >> 32))));
        Story = story;

        Builds.Clear(); Cols.Clear(); Foes.Clear(); Trains.Clear(); Bots.Clear();
        Beasts.Clear(); Carcasses.Clear();
        Prisoners.Clear(); Fx.Clear(); Log.Clear(); Alerts.Clear(); History.Clear();
        CmdQueue.Clear(); CmdLog.Clear(); TrainRes.Clear();
        Time = Bal.DayLengthSec * 0.30f; Wave = 0; Threat = 0; NextRaidAt = Bal.FirstRaidAt;
        RecentCombat = 0; RaidBannerT = 0; Wealth = 0; DebugWealthBonus = 0;
        TotalPollution = 0; _randThreatMul = 1f; _eventT = 100f;
        Won = Lost = ContinueAfterWin = GodMode = false;
        HintsDone = 0; _selSeen = false; HuntedOnce = false; Trader = null;
        StatKills = StatBeastsHunted = StatBuilt = StatMined = StatMeals = 0;
        WealthHist.Clear(); _lastDay = Day;
        for (int i = 0; i < Bal.TechCount; i++) { TechDone[i] = false; TechProg[i] = 0; TechLevels[i] = 0; }
        ActiveTech = Tech.Automation;
        Grids.Clear(); _gridOf.Clear(); PowerDirty = true;
        Fluids.Clear(); _fluidOf.Clear(); _boilerIn.Clear(); _boilerOut.Clear(); FluidDirty = true;

        // LANDING SITE: the colony starts wherever the player chose to land
        int LX = par.LandingX, LY = par.LandingY;
        var hub = new Hub { X = LX, Y = LY };
        HubRef = hub;
        Builds.Add(hub);
        World.SetBuilding(hub);

        StampStart(World, LX, LY);

        hub.Stock[(int)ItemKind.IronPlate] = Bal.StartIronPlates;
        hub.Stock[(int)ItemKind.CopperPlate] = Bal.StartCopperPlates;
        hub.Stock[(int)ItemKind.Food] = Bal.StartFood;

        PlaceFree(new Hab(), LX + 1, LY - 2);

        // RECLAMATION: the ark wreck lies far from home, waiting to be found
        for (int attempt = 0; attempt < 60; attempt++)
        {
            float ang = (float)(Rand.NextDouble() * Math.PI * 2);
            float dist = 42f + (float)Rand.NextDouble() * 16f;
            int ax = LX + (int)MathF.Round(MathF.Cos(ang) * dist), ay = LY + (int)MathF.Round(MathF.Sin(ang) * dist);
            if (!World.InBounds(ax, ay)) continue;
            World.SetTerrainCircle(ax + 1, ay + 1, 4.5f, Terrain.Ground);
            World.DemoteOrphanWater(ax - 8, ay - 8, ax + 10, ay + 10);
            PlaceFree(new ArkWreck(), ax, ay);
            World.RevealAround(ax + 1, ay + 1, 2);
            break;
        }

        for (int i = 0; i < 6; i++)
        {
            var c0 = new Colonist(LX + 1.5f + Rand.Next(-2, 3), LY + 4.5f + Rand.Next(-1, 2), Rand);
            c0.Cid = _nextCid++;
            Cols.Add(c0);
        }

        World.RevealAround(LX + 1, LY + 1, 2);

        // MADDOG iter-4: the world is alive from minute one
        SpawnHerd();

        Chronicle.Clear();
        _traderChron = _stormChron = _breachChron = false;
        Blight.Clear(); _blightChron = false; _blightT = 2f;
        Chapter = 0; FinaleAt = -1; FinalePending = FinaleCleared = Hope = false; EndingChosen = 0;
        AddChron("Six survivors stepped out of the ark's shadow and lit the first fire.");
        AddChron(BiomeKind switch
        {
            Biome.Volcanic => "The ground here still smokes. Crystal veins glow through the basalt.",
            Biome.Glacial => "The sun is a pale coin over the ice. Iron lies close to the surface.",
            Biome.Fungal => "The flora glows in waves. Beautiful. Hungry.",
            _ => "Green hills, clean water. Almost like home.",
        });
        Time = Bal.DayLengthSec * Bal.DayStartFrac;   // dawn, not midnight: nobody sleeps through the landing
        AddLog("The ark is gone. Six survivors. One hub.", Pal.Accent);
        AddLog($"Goal: deliver {Bal.PartsToWin} Advanced Parts to the Hub.", Pal.Accent);
        AddLog($"Storyteller: {StoryName()}. [T] research · [P] work · F1-F4 views.", Pal.TextDim);
    }

    public static void StampStart(World w, int ox = 0, int oy = 0)
    {
        w.SetTerrainCircle(ox + 1, oy + 1, 11f, Terrain.Ground);   // the colony plaza: guaranteed dry build space
        w.SetTerrainCircle(ox - 11, oy + 5, 3.6f, Terrain.IronOre);
        w.SetTerrainCircle(ox + 11, oy - 2, 3.0f, Terrain.CopperOre);
        w.SetTerrainCircle(ox - 3, oy + 10, 3.4f, Terrain.Tree);   // starting grove: early biomass
        w.SetTerrainCircle(ox + 13, oy - 11, 2.4f, Terrain.Crystal);
        w.SetTerrainCircle(ox + 12, oy + 6, 3.6f, Terrain.Water);  // lake for the steam loop
        w.DemoteOrphanWater(ox - 24, oy - 24, ox + 26, oy + 26);   // guaranteed paints can strand noise lakes
    }

    public string StoryName() => Story switch
    {
        Storyteller.Random => "Randy Random",
        Storyteller.Merciless => "The Merciless",
        _ => "Colony Builder",
    };

    private void PlaceFree(Building b, int x, int y)
    {
        b.X = x; b.Y = y;
        Builds.Add(b);
        World.SetBuilding(b);
    }

    // -------------------------------------------------------------  wealth

    private void ComputeWealth()
    {
        float w = 150f;
        foreach (var b in Builds) w += Bal.CostWealth(b.Kind);
        foreach (var c in Cols) if (c.Hp > 0) w += 40 + c.Stats.Sum() * 2f;
        foreach (var t in Trains) w += 80;
        foreach (var b in Bots) w += 30;
        for (int i = 0; i < HubRef.Stock.Length; i++)
            w += HubRef.Stock[i] * Bal.ItemWealth((ItemKind)i);
        for (int i = 0; i < Bal.TechCount; i++)
        {
            if (TechDone[i]) w += 120;
            w += TechLevels[i] * 80;
        }
        w += DebugWealthBonus;
        Wealth = w;
    }

    // --------------------------------------------------------  build costs

    public bool HasMaterials(BuildKind k) =>
        HubRef.Stock[(int)ItemKind.IronPlate] >= Bal.CostIron(k) &&
        HubRef.Stock[(int)ItemKind.CopperPlate] >= Bal.CostCopper(k) &&
        HubRef.Stock[(int)ItemKind.Stone] >= Bal.CostStone(k);

    private void PayMaterials(BuildKind k)
    {
        HubRef.Stock[(int)ItemKind.IronPlate] -= Bal.CostIron(k);
        HubRef.Stock[(int)ItemKind.CopperPlate] -= Bal.CostCopper(k);
        HubRef.Stock[(int)ItemKind.Stone] -= Bal.CostStone(k);
    }

    // ------------------------------------------------------------ placement

    public bool CanPlace(BuildKind kind, int x, int y, out string reason)
    {
        reason = "";
        if (!World.InBounds(x, y)) { reason = "Outside the world"; return false; }
        if (!World.RevealedAt(x, y)) { reason = "Unexplored territory"; return false; }
        var tile = World.Cell(x, y);

        if (kind == BuildKind.ElevatedRail)
        {
            // raised track: goes on the B2 layer, over anything (except another)
            if (tile.B2 != null) { reason = "Elevated rail already here"; return false; }
        }
        else if (kind == BuildKind.Locomotive)
        {
            // sits ON rails/stations — the rail check below is the gate
        }
        else if (tile.B != null) { reason = "Occupied"; return false; }

        var gate = Bal.TechGate(kind);
        if (gate != Tech.None && !Has(gate))
        { reason = $"Requires research: {Bal.TechName(gate)}"; return false; }

        if (kind == BuildKind.Pump)
        {
            if (tile.T != Terrain.Water) { reason = "Pumps must sit on Water"; return false; }
        }
        else if (tile.T == Terrain.Water) { reason = "Can't build on water (use a Pump or Elevated Rail)"; return false; }

        if (kind is BuildKind.Drill or BuildKind.DeepDrill or BuildKind.BlastDrill)
        {
            // COLLECTIBLES: trees are hand-felled cargo, not drill-able ore
            if (tile.T is not (Terrain.IronOre or Terrain.CopperOre or Terrain.Crystal or Terrain.Flora))
            { reason = "Drills must sit on iron, copper, crystal or flora"; return false; }
        }
        else if (tile.T == Terrain.Rock) { reason = "Solid rock"; return false; }

        if (kind == BuildKind.Locomotive)
        {
            if (!World.IsRailAt(x, y)) { reason = "Place locomotives on rail or a station"; return false; }
        }

        // multiblock footprints: every cell of the W x H area must be clear
        int fw = Bal.FootW(kind), fh = Bal.FootH(kind);
        for (int dx = 0; dx < fw; dx++)
            for (int dy = 0; dy < fh; dy++)
            {
                int cx = x + dx, cy = y + dy;
                if (dx != 0 || dy != 0)
                {
                    if (!World.InBounds(cx, cy)) { reason = "Outside the world"; return false; }
                    if (!World.RevealedAt(cx, cy)) { reason = "Unexplored territory"; return false; }
                    var ct = World.Cell(cx, cy);
                    if (ct.B != null || ct.B2 != null) { reason = "Occupied"; return false; }
                    if (ct.T == Terrain.Water) { reason = "Can't build on water"; return false; }
                    if (ct.T == Terrain.Rock) { reason = "Solid rock"; return false; }
                }
                if (_bpAt.ContainsKey(((long)cx << 32) ^ (uint)cy))
                { reason = "Blueprint already here"; return false; }
            }

        if (!HasMaterials(kind)) { reason = "Needs " + Bal.CostText(kind) + " (at Hub)"; return false; }
        return true;
    }

    public bool TryPlace(BuildKind kind, int x, int y, Dir face, out string reason)
    {
        if (!CanPlace(kind, x, y, out reason)) return false;
        PayMaterials(kind);
        World.RevealAround(x, y, 0);

        if (kind == BuildKind.Locomotive)
        {
            // not a building: spawn a train entity on the track here
            var t = new Train { PosX = x + 0.5f, PosY = y + 0.5f };
            Trains.Add(t);
            AddLog("Locomotive deployed — it will shuttle between stations.", Pal.Accent);
            return true;
        }

        // Phase 1: the player DESIGNATES. A colonist walks over and builds it
        // (unless instant build is on: tests, god mode).
        if (!InstantBuild && !GodMode)
        {
            var bp = new Blueprint
            {
                Kind = kind, X = x, Y = y, Face = face,
                W = Bal.FootW(kind), H = Bal.FootH(kind),
            };
            bp.WorkMax = bp.Work = MathF.Max(Bal.BuildWorkMin,
                (Bal.CostIron(kind) + Bal.CostCopper(kind) + Bal.CostStone(kind)) * Bal.BuildWorkPerPlate);
            Blueprints.Add(bp);
            for (int dx = 0; dx < bp.W; dx++)
                for (int dy = 0; dy < bp.H; dy++)
                    _bpAt[((long)(x + dx) << 32) ^ (uint)(y + dy)] = bp;
            AddLog($"{Bal.Name(kind)} designated — a colonist will build it.", Pal.TextDim);
            return true;
        }

        PlaceBuilding(kind, x, y, face);
        return true;
    }

    /// <summary>Actually create + register a finished building (shared by
    /// instant placement and blueprint completion).</summary>
    private Building PlaceBuilding(BuildKind kind, int x, int y, Dir face)
    {
        var b = Building.Create(kind, x, y, face);
        Builds.Add(b);
        if (kind == BuildKind.ElevatedRail) World.SetBuilding2(b);
        else World.SetBuilding(b);
        PowerDirty = true;
        FluidDirty |= b is Pipe or Pump or Tank or Boiler or SteamEngine;

        if (b is TrainStop st)
        {
            int n = Builds.Count(v => v is TrainStop);
            st.StationName = $"Station {(char)('A' + n - 1)}";
        }
        if (b is BotFactory bf) { bf.RallyX = x; bf.RallyY = y; }

        if (b is MachineBase m && m.NeedsWorker && !Has(Tech.Automation))
            AssignOperator(m);
        return b;
    }

    /// <summary>Blueprint finished: become a real building, free the builder.</summary>
    public void CompleteBlueprint(Blueprint bp)
    {
        if (!Blueprints.Contains(bp)) return;
        if (bp.Builder != null)
        {
            if (bp.Builder.BuildJob == bp) bp.Builder.BuildJob = null;
            if (bp.Builder.State is ColState.Building or ColState.GoBuild)
                bp.Builder.State = ColState.Idle;
            bp.Builder = null;
        }
        for (int dx = 0; dx < bp.W; dx++)
            for (int dy = 0; dy < bp.H; dy++)
                _bpAt.Remove(((long)(bp.X + dx) << 32) ^ (uint)(bp.Y + dy));
        Blueprints.Remove(bp);
        // bugfix: a completed building buries any mine order under it
        var placed = PlaceBuilding(bp.Kind, bp.X, bp.Y, bp.Face);
        StatBuilt++;
        for (int dx = 0; dx < placed.W; dx++)
            for (int dy = 0; dy < placed.H; dy++)
                if (_mineAt.TryGetValue(((long)(placed.X + dx) << 32) ^ (uint)(placed.Y + dy), out var buried))
                    CancelMineOrder(buried);
        SpawnSpark(placed.X + placed.W / 2f, placed.Y + placed.H / 2f, Pal.Good);
        if (Bal.CategoryOf(placed.Kind) != BuildCategory.Logistics)
            AddLog($"{Bal.Name(placed.Kind)} construction complete.", Pal.Good);
    }

    public void Bulldoze(int x, int y)
    {
        if (!World.InBounds(x, y)) return;

        // blueprint under the cursor: cancel the designation, FULL refund
        var key = ((long)x << 32) ^ (uint)y;
        if (_bpAt.TryGetValue(key, out var bp))
        {
            HubRef.Stock[(int)ItemKind.IronPlate] += Bal.CostIron(bp.Kind);
            HubRef.Stock[(int)ItemKind.CopperPlate] += Bal.CostCopper(bp.Kind);
            HubRef.Stock[(int)ItemKind.Stone] += Bal.CostStone(bp.Kind);
            if (bp.Builder != null)
            {
                if (bp.Builder.BuildJob == bp) bp.Builder.BuildJob = null;
                if (bp.Builder.State is ColState.Building or ColState.GoBuild)
                    bp.Builder.State = ColState.Idle;
            }
            for (int dx = 0; dx < bp.W; dx++)
                for (int dy = 0; dy < bp.H; dy++)
                    _bpAt.Remove(((long)(bp.X + dx) << 32) ^ (uint)(bp.Y + dy));
            Blueprints.Remove(bp);
            AddLog($"{Bal.Name(bp.Kind)} designation cancelled — full refund.", Pal.TextDim);
            return;
        }
        // mine order under the cursor: cancel it
        if (_mineAt.TryGetValue(key, out var mo)) { CancelMineOrder(mo); return; }

        var tile = World.Cell(x, y);
        var b = tile.B;
        bool elevated = false;
        if (b == null && tile.B2 != null) { b = tile.B2; elevated = true; }
        if (b == null || b is Hub) return;

        // 50% refund, min 1 — but only for materials actually paid (pipes cost
        // 0 iron; refunding 1 iron for them would mint metal from nothing)
        int fe = Bal.CostIron(b.Kind) <= 0 ? 0 : Math.Max(1, Bal.CostIron(b.Kind) / 2);
        int cu = Bal.CostCopper(b.Kind) <= 0 ? 0 : Math.Max(1, Bal.CostCopper(b.Kind) / 2);
        HubRef.Stock[(int)ItemKind.IronPlate] += fe;
        HubRef.Stock[(int)ItemKind.CopperPlate] += cu;

        if (b is BotFactory f)
            foreach (var bot in Bots.Where(bt => bt.Home == f).ToList()) KillBot(bot);

        AddLog($"{Bal.Name(b.Kind)} scrapped (+{fe} Fe / +{cu} Cu plates).", Pal.TextDim);
        DestroyBuilding(b, silent: true, elevated);
    }

    public void DestroyBuilding(Building b, bool silent, bool elevated = false)
    {
        if (b is Wall or Door && RecentCombat > 0 && !_breachChron)
        {
            _breachChron = true;                       // the first breach is the one you remember
            AddChron("The walls were breached. The colony held - barely.");
        }
        if (b.Repairer != null)
        {
            if (b.Repairer.RepairJob == b) { b.Repairer.RepairJob = null; b.Repairer.State = ColState.Idle; }
            b.Repairer = null;
        }
        if (b is MachineBase m && m.Operator != null)
        {
            m.Operator.Job = null;
            m.Operator.State = ColState.Idle;
            m.Operator = null;
        }
        if (b is MedBed mb && mb.Occupant != null)
        {
            mb.Occupant.Bed = null;
            mb.Occupant.SleepTargetIsBed = false;
            mb.Occupant = null;
        }
        if (!silent)
            AddLog($"{Bal.Name(b.Kind)} was destroyed!", Pal.Bad);
        if (elevated || b.Kind == BuildKind.ElevatedRail) World.ClearBuilding2(b);
        else World.ClearBuilding(b);
        Builds.Remove(b);
        PowerDirty = true;
        FluidDirty |= b is Pipe or Pump or Tank or Boiler or SteamEngine;
    }

    // ------------------------------------------------------------- staffing

    /// <summary>Assign the best free colonist by WORK PRIORITY (then distance).</summary>
    public void AssignOperator(MachineBase m)
    {
        if (m.Operator != null) return;
        var wt = Bal.WorkOf(m.Kind) ?? WorkType.Craft;
        int si = Bal.StatOf(wt);
        Colonist? best = null; int bp = int.MaxValue, bsk = -1; float bd = float.MaxValue;
        foreach (var c in Cols)
        {
            if (c.Hp <= 0 || c.Drafted || c.Arriving || c.Job != null
                || c.BuildJob != null || c.MineJob != null || c.HaulTarget != null) continue;
            int p = c.Priorities[(int)wt];
            if (p >= 4) continue;                       // 4 = never
            int sk = c.Stats[si];                       // suits their abilities
            float d = Math.Abs(c.PosX - (m.X + .5f)) + Math.Abs(c.PosY - (m.Y + .5f));
            if (p < bp || (p == bp && (sk > bsk || (sk == bsk && d < bd))))
            { bp = p; bsk = sk; bd = d; best = c; }
        }
        if (best == null)
        {
            AddLog($"{Bal.Name(m.Kind)} needs an operator — nobody has {Bal.WorkName(wt)} enabled.", Pal.Warn);
            return;
        }
        best.Job = m;
        best.State = ColState.Idle;
        m.Operator = best;
        AddLog($"{best.Name} is now operating the {Bal.Name(m.Kind)}.", Pal.TextDim);
    }

    public void ReleaseFromJob(Colonist c)
    {
        if (c.Job != null && c.Job.Operator == c) c.Job.Operator = null;
        if (c.BuildJob != null) { if (c.BuildJob.Builder == c) c.BuildJob.Builder = null; c.BuildJob = null; }
        if (c.MineJob != null) { if (c.MineJob.Miner == c) c.MineJob.Miner = null; c.MineJob = null; }
        if (c.RepairJob != null) { if (c.RepairJob.Repairer == c) c.RepairJob.Repairer = null; c.RepairJob = null; }
        if (c.GatherJob != null) { if (c.GatherJob.Gatherer == c) c.GatherJob.Gatherer = null; c.GatherJob = null; }
        if (c.ExcavJob != null) { if (c.ExcavJob.Excavator == c) c.ExcavJob.Excavator = null; c.ExcavJob = null; }
        c.Job = null;
    }

    public void Unassign(MachineBase m)
    {
        if (m.Operator != null)
        {
            m.Operator.Job = null;
            m.Operator.State = ColState.Idle;
            AddLog($"{m.Operator.Name} was relieved from the {Bal.Name(m.Kind)}.", Pal.TextDim);
            m.Operator = null;
        }
    }

    public static Point? WorkSpot(World w, MachineBase m, Colonist c)
    {
        Point? best = null; float bd = float.MaxValue;
        for (int dx = -1; dx <= m.W; dx++)
            for (int dy = -1; dy <= m.H; dy++)
            {
                int x = m.X + dx, y = m.Y + dy;
                bool onEdge = dx == -1 || dy == -1 || dx == m.W || dy == m.H;
                if (!onEdge || !w.WalkColonist(x, y)) continue;
                float d = MathF.Abs(x + .5f - c.PosX) + MathF.Abs(y + .5f - c.PosY);
                if (d < bd) { bd = d; best = new Point(x, y); }
            }
        return best;
    }

    // -----------------------------------------------------------  prisoners

    public void StartCapture(Raider r)
    {
        if (!r.Downed || r.Captured) return;
        Colonist? best = null; float bd = float.MaxValue;
        foreach (var c in Cols)
        {
            if (c.Hp <= 0 || c.Job != null || c.State is ColState.Flee) continue;
            float d = MathF.Abs(c.PosX - r.PosX) + MathF.Abs(c.PosY - r.PosY);
            if (d < bd) { bd = d; best = c; }
        }
        if (best == null) { AddLog("No free colonist to capture the prisoner.", Pal.Warn); return; }
        best.CaptureTarget = r;
        best.State = ColState.GoCapture;
        AddLog($"{best.Name} is going to capture the downed horror.", Pal.Accent);
    }

    public void InternPrisoner(Raider r, Colonist by)
    {
        Foes.Remove(r);
        var c = HubRef.CenterTile;
        Prisoners.Add(new Prisoner(c.X + Rand.Next(-2, 3), c.Y + 3 + Rand.Next(0, 2), Rand)
        {
            // recruit progress carries over from how wounded they were
        });
        AddLog($"{by.Name} interned a horror in the Hub brig. Feed it — it may join us.", Pal.Accent);
    }

    public void RecruitPrisoner(Prisoner p)
    {
        Prisoners.Remove(p);
        var col = SpawnNewcomer(p.Name);
        var c = HubRef.CenterTile;
        AddLog($"{p.Name} has joined the colony — walking in from the wastes!", Pal.Good);
        SpawnSpark(c.X, c.Y + 3, Pal.Good);
    }

    /// <summary>Phase 1: newcomers walk in from far away (map edge), not
    /// teleport-into-the-colony. They chart fog as they travel.</summary>
    public Colonist SpawnNewcomer(string? name = null)
    {
        var hc = HubRef.CenterTile;
        // bugfix: a spawn point in water/rock used to strand the newcomer -
        // probe several angles until the hub is actually reachable
        Colonist col;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float ang = (float)(Rand.NextDouble() * Math.PI * 2);
            int px = (int)(hc.X + MathF.Cos(ang) * Bal.NewcomerEdgeDist);
            int py = (int)(hc.Y + MathF.Sin(ang) * Bal.NewcomerEdgeDist);
            if (!World.InBounds(px, py)) continue;
            var path = World.FindPath(px, py, (int)hc.X, (int)hc.Y + 3, enemy: false);
            if (path == null) continue;
            col = new Colonist(px + 0.5f, py + 0.5f, Rand)
            { Arriving = true, Path = path, PathIdx = 0, Cid = _nextCid++ };
            if (name != null) col.Name = name;
            Cols.Add(col);
            return col;
        }
        // no reachable edge (tiny island world?): spawn by the hub
        col = new Colonist(hc.X + 0.5f + Rand.Next(-1, 2), hc.Y + 3.5f, Rand) { Arriving = true, Cid = _nextCid++ };
        if (name != null) col.Name = name;
        Cols.Add(col);
        return col;
    }

    // ---------------------------------------------------------  war bots

    public void SpawnGuardBot(BotFactory f)
    {
        Bots.Add(new GuardBot { X = f.X + 0.5f, Y = f.Y + 0.5f, Home = f });
        AddLog("War Bot deployed.", Pal.TextDim);
    }

    public void KillBot(GuardBot b)
    {
        Bots.Remove(b);
        if (b.Home != null && b.Home.Deployed > 0) b.Home.Deployed--;
        AddLog("A War Bot was destroyed.", Pal.Bad);
    }

    // -------------------------------------------------------------  damage

    /// <summary>Building damage with shield absorption + god mode.</summary>
    public void DamageBuilding(Building b, float dmg)
    {
        if (b == null || b.Hp <= 0) return;
        if (GodMode && b is Hub) return;
        foreach (var s in Builds)
            if (s is ShieldGen sg && sg.TryAbsorb(b, dmg))
            {
                SpawnSpark(b.X + .5f, b.Y + .5f, Pal.Accent);
                return;
            }
        b.Hp -= dmg;
        if (b.Hp <= 0) DestroyBuilding(b, silent: false);
    }

    public void Explode(float x, float y, float radius, float dmg)
    {
        Fx.Add(new Particle(2, x, y, 0, 0, 0.5f, Pal.Warn));
        foreach (var r in Foes.ToArray())
        {
            if (r.Hp <= 0 || r.Downed) continue;
            float d = MathF.Sqrt((r.PosX - x) * (r.PosX - x) + (r.PosY - y) * (r.PosY - y));
            if (d <= radius) DamageRaider(r, dmg, x, y);
        }
        foreach (var c in Cols.ToArray())
        {
            float d = MathF.Sqrt((c.PosX - x) * (c.PosX - x) + (c.PosY - y) * (c.PosY - y));
            if (d <= radius) DamageColonist(c, dmg * 0.4f, x, y);
        }
        AddLog("An IED detonated!", Pal.Warn);
    }

    public void DamageRaider(Raider r, float dmg, float fromX, float fromY)
    {
        if (r.Hp <= 0 || r.Downed || r.Captured) return;
        r.Hp -= dmg;
        r.FlashT = 0.12f;
        if (r.Hp <= Bal.DownedAtFrac * r.MaxHp)
        {
            // severely wounded: chance to go down instead of dying (capturable!)
            if (Rand.NextDouble() < Bal.DownedChance)
            {
                r.Hp = MathF.Max(1f, r.MaxHp * 0.08f);
                r.Downed = true;
                AddLog("A horror is DOWNED — click it to capture.", Pal.Warn);
                return;
            }
        }
        if (r.Hp <= 0)
        {
            Fx.Add(new Particle(2, r.PosX, r.PosY, 0, 0, 0.4f, Pal.Enemy));
            StatKills++;
            if (r.Apex) AddLog("The apex beast is down!", Pal.Good);
        }
    }

    public void DamageColonist(Colonist c, float dmg, float fromX, float fromY)
    {
        if (c.Hp <= 0) return;
        c.Hp -= dmg;
        c.FlashT = 0.15f;
    }

    // ---------------------------------------------------------------  power

    /// <summary>MADDOG iter-6 (G8): is a Lamp close enough to keep this
    /// building's guns accurate at night?</summary>
    public bool LampNear(Building b)
    {
        float r2 = Bal.LampTurretRadius * Bal.LampTurretRadius;
        foreach (var o in Builds)
        {
            if (o.Kind != BuildKind.Lamp) continue;
            float dx = o.X - b.X, dy = o.Y - b.Y;
            if (dx * dx + dy * dy <= r2) return true;
        }
        return false;
    }

    /// <summary>Cooldown multiplier: 1 by day, worse at night unless lit.</summary>
    public float TurretCdMul(Building b) =>
        !IsNight ? 1f : LampNear(b) ? 1f : Bal.NightTurretCdMul;

    public float PowerFracFor(Building b) =>
        _gridOf.TryGetValue(b, out var gr) ? gr.Frac : 0f;

    public PowerGrid? GridOf(Building b) => _gridOf.TryGetValue(b, out var gr) ? gr : null;

    private void RebuildGrids()
    {
        Grids.Clear();
        _gridOf.Clear();

        var nodes = new List<Building>();
        foreach (var b in Builds)
            if (b is PowerPole || b is Hub) nodes.Add(b);

        var parent = new Dictionary<Building, Building>();
        Building Find(Building b) { while (parent[b] != b) { parent[b] = parent[parent[b]]; b = parent[b]; } return b; }
        foreach (var n in nodes) parent[n] = n;
        for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
            {
                var a = nodes[i]; var b = nodes[j];
                float dx = a.X + a.W / 2f - (b.X + b.W / 2f);
                float dy = a.Y + a.H / 2f - (b.Y + b.H / 2f);
                float wr = MathF.Max(a.WireRange, b.WireRange);   // substations reach far
                if (dx * dx + dy * dy <= wr * wr)
                {
                    var ra = Find(a); var rb = Find(b);
                    if (ra != rb) parent[ra] = rb;
                }
            }

        var gridsByRoot = new Dictionary<Building, PowerGrid>();
        PowerGrid GridFor(Building n)
        {
            var r = Find(n);
            if (!gridsByRoot.TryGetValue(r, out var gr))
            {
                gr = new PowerGrid();
                gridsByRoot[r] = gr;
                Grids.Add(gr);
            }
            return gr;
        }
        foreach (var n in nodes)
        {
            var gr = GridFor(n);
            gr.Nodes.Add(n);
            _gridOf[n] = gr;
        }

        foreach (var b in Builds)
        {
            if (b is PowerPole || b is Hub) continue;
            bool wants = b.PowerDemand > 0 || b is Reactor or SolarPanel or WindTurbine or Battery or SteamEngine;
            if (!wants) continue;

            Building? bestNode = null; float bd = float.MaxValue;
            foreach (var n in nodes)
            {
                float cover = n.CoverRange;      // hub + substation override
                float dx = b.X + b.W / 2f - (n.X + n.W / 2f);
                float dy = b.Y + b.H / 2f - (n.Y + n.H / 2f);
                float d2 = dx * dx + dy * dy;
                if (d2 <= cover * cover && d2 < bd)
                { bd = d2; bestNode = n; }
            }
            if (bestNode != null)
            {
                var gr = _gridOf[bestNode];
                gr.Attached.Add(b);
                _gridOf[b] = gr;
            }
        }
    }

    private void UpdatePower(float dt)
    {
        _powerTimer -= dt;
        if (PowerDirty || _powerTimer <= 0)
        {
            _powerTimer = 0.5f;
            RebuildGrids();
            PowerDirty = false;
        }

        float supply = 0, demand = 0, stored = 0, cap = 0;
        foreach (var gr in Grids)
        {
            gr.Update(this, dt);
            supply += gr.Supply; demand += gr.Demand;
            stored += gr.Stored; cap += gr.Cap;
        }
        PowerSupplyTotal = (int)supply;
        PowerDemandTotal = (int)demand;
        BatteryFrac01 = cap <= 0 ? 0 : stored / cap;
    }

    // --------------------------------------------------------------  fluids

    public PipeNet? FluidOf(Building b) => _fluidOf.TryGetValue(b, out var n) ? n : null;

    private void RebuildFluids()
    {
        Fluids.Clear();
        _fluidOf.Clear();
        _boilerIn.Clear();
        _boilerOut.Clear();

        // NOTE: boilers are intentionally NOT part of any network — they are
        // one-way converters BETWEEN two networks (water side -> face side).
        // Unioning through them would fuse the water and steam runs.
        var fluidB = new List<Building>();
        foreach (var b in Builds)
            if (b is Pipe or Pump or Tank or SteamEngine) fluidB.Add(b);

        var parent = new Dictionary<Building, Building>();
        Building Find(Building b) { while (parent[b] != b) { parent[b] = parent[parent[b]]; b = parent[b]; } return b; }
        foreach (var b in fluidB) parent[b] = b;

        foreach (var a in fluidB)
            for (int d = 0; d < 4; d++)
            {
                var (nx, ny) = DirU.Step(a.X, a.Y, (Dir)d);
                if (!World.InBounds(nx, ny)) continue;
                var nb = World.Cell(nx, ny).B;
                if (nb is not (Pipe or Tank or SteamEngine or Pump)) continue;   // no Boiler bridge
                var ra = Find(a); var rb = Find(nb);
                if (ra != rb) parent[ra] = rb;
            }

        var netsByRoot = new Dictionary<Building, PipeNet>();
        foreach (var b in fluidB)
        {
            var r = Find(b);
            if (!netsByRoot.TryGetValue(r, out var net))
            {
                net = new PipeNet();
                netsByRoot[r] = net;
                Fluids.Add(net);
            }
            net.Members.Add(b);
            _fluidOf[b] = net;
            net.Cap += b is Tank ? Bal.TankCap : Bal.PipeCap;
        }

        // boilers: input nets = all adjacent, output net = the one on the FACE side
        foreach (var b in Builds)
        {
            if (b is not Boiler bo) continue;
            var nets = new List<PipeNet>();
            for (int d = 0; d < 4; d++)
            {
                var (nx, ny) = DirU.Step(bo.X, bo.Y, (Dir)d);
                if (!World.InBounds(nx, ny)) continue;
                var nb = World.Cell(nx, ny).B;
                if (nb is not (Pipe or Tank or SteamEngine or Pump)) continue;
                if (_fluidOf.TryGetValue(nb, out var n) && !nets.Contains(n)) nets.Add(n);
            }
            var (ox, oy) = DirU.Step(bo.X, bo.Y, bo.Face);
            PipeNet? outNet = World.InBounds(ox, oy) &&
                World.Cell(ox, oy).B is Pipe or Tank or SteamEngine or Pump
                ? _fluidOf.GetValueOrDefault(World.Cell(ox, oy).B!) : null;
            _boilerIn[bo] = nets;
            _boilerOut[bo] = outNet;
        }
    }

    private void FluidTick(float dt)
    {
        foreach (var net in Fluids)
        {
            // pumps draw water (from infinite lakes)
            foreach (var m in net.Members)
                if (m is Pump && (net.Kind is FluidKind.None or FluidKind.Water))
                {
                    net.Kind = FluidKind.Water;
                    net.Amount = Math.Min(net.Cap, net.Amount + Bal.PumpRate * dt);
                }

            // steam engines consume steam
            foreach (var m in net.Members)
            {
                if (m is not SteamEngine se) continue;
                se.CurrentOutput = 0;
                if (net.Kind != FluidKind.Steam || net.Amount <= 0) continue;
                float consume = Math.Min(Bal.EngineSteamRate * dt, net.Amount);
                net.Amount -= consume;
                se.CurrentOutput = consume / (Bal.EngineSteamRate * dt) * Bal.EnginePower;
            }
        }

        // boilers: water in -> steam out, burning biomass
        foreach (var (bo, inNets) in _boilerIn)
        {
            var outNet = _boilerOut.GetValueOrDefault(bo);
            if (outNet == null || outNet.Kind == FluidKind.Water) continue; // needs a separate steam run
            if (outNet.Amount >= outNet.Cap) continue;

            float room = outNet.Cap - outNet.Amount;
            float conv = Math.Min(Bal.BoilerRate * dt, room);
            conv = Math.Min(conv, bo.Fuel / Bal.BoilerBiomassPerWater);

            float done = 0;
            foreach (var net in inNets)
            {
                if (net == outNet || net.Kind != FluidKind.Water) continue;
                float take = Math.Min(conv - done, net.Amount);
                net.Amount -= take;
                done += take;
                if (done >= conv) break;
            }
            if (done > 0)
            {
                outNet.Kind = FluidKind.Steam;
                outNet.Amount += done;
                bo.Fuel -= done * Bal.BoilerBiomassPerWater;
            }
        }
    }

    // -------------------------------------------------------------  trains

    /// <summary>BFS along rail tiles (ground + elevated) between two points.</summary>
    public List<Point>? RailPath(int sx, int sy, int gx, int gy)
    {
        if (sx == gx && sy == gy) return new List<Point>();
        var prev = new Dictionary<long, long>();
        var q = new Queue<(int x, int y)>();
        long K(int x, int y) => (long)x << 32 | (uint)y;
        long goal = K(gx, gy);
        q.Enqueue((sx, sy));
        prev[K(sx, sy)] = -1;
        int steps = 0;

        while (q.Count > 0 && steps++ < 6000)
        {
            var (x, y) = q.Dequeue();
            if (K(x, y) == goal)
            {
                var path = new List<Point>();
                long cur = goal;
                while (cur != -1)
                {
                    path.Add(new Point((int)(cur >> 32), (int)(cur & 0xFFFFFFFFL)));
                    cur = prev[cur];
                }
                path.Reverse();
                return path;
            }
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DirU.Dx[d], ny = y + DirU.Dy[d];
                long nk = K(nx, ny);
                if (prev.ContainsKey(nk)) continue;
                if (nk == goal || World.IsRailAt(nx, ny))
                {
                    prev[nk] = K(x, y);
                    q.Enqueue((nx, ny));
                }
            }
        }
        return null;
    }

    // -----------------------------------------------------------  pollution

    private void PollutionPass(float dt)
    {
        // emit
        foreach (var b in Builds)
        {
            float e = Bal.PollEmit(b.Kind) * dt;
            if (e > 0) World.AddPoll(b.X, b.Y, e);
        }

        // diffuse inside active chunks + compute the global total
        TotalPollution = 0;
        foreach (var kv in World.AllChunks)
        {
            var c = kv.Value;
            float sum = 0;
            for (int i = 0; i < c.Pol.Length; i++) sum += c.Pol[i];
            if (sum <= 0.01f) continue;

            TotalPollution += sum;

            // cheap blur: swap with the 4-neighborhood average a bit
            float k = Bal.PollDiffuse * dt;
            for (int y = 1; y < Chunk.S - 1; y++)
                for (int x = 1; x < Chunk.S - 1; x++)
                {
                    int i = x + y * Chunk.S;
                    float avg = (c.Pol[i - 1] + c.Pol[i + 1] + c.Pol[i - Chunk.S] + c.Pol[i + Chunk.S]) * 0.25f;
                    c.Pol[i] += (avg - c.Pol[i]) * k;
                }

            // flora tiles scrub pollution
            for (int i = 0; i < c.Tiles.Length; i++)
                if (c.Tiles[i].T is (Terrain.Flora or Terrain.Tree) && c.Pol[i] > 0)
                    c.Pol[i] = Math.Max(0, c.Pol[i] - Bal.PollFloraAbsorb * dt);
        }
    }

    // ---------------------------------------------------------------  loop

    public void Update(float dt)
    {
        if (Lost || (Won && !ContinueAfterWin)) return;

        ExecuteCmds();

        Time += dt;

        // MADDOG iter-3: the sky does its own thing (ambient, not an EventDirector event)
        WeatherT -= dt;
        if (WeatherT <= 0) RollWeather();

        // MADDOG iter-7: wealth history sampled once per day
        if (Day != _lastDay)
        {
            _lastDay = Day;
            WealthHist.Add(Wealth);
            if (WealthHist.Count > 90) WealthHist.RemoveAt(0);
        }

        // MADDOG iter-5: onboarding hint conditions (cheap, once a second)
        _hintT -= dt;
        if (_hintT <= 0)
        {
            _hintT = 1f;
            UpdateHints();
            UpdateChapters();
        }

        // RECLAMATION: the blight breathes; the finale counts down
        _blightT -= dt;
        if (_blightT <= 0) { _blightT = 2f; BlightTick(); }
        if (FinaleAt > 0 && Time >= FinaleAt) { FinaleAt = -1; SpawnFinale(); }

        // rolling stock rates for the UI (per-day, EMA-smoothed, 5s samples)
        _stockRateT -= dt;
        if (_stockRateT <= 0)
        {
            _stockRateT = 5f;
            if (!_stockPrimed)
            {
                for (int i = 0; i < Bal.ItemCount; i++) _stockPrev[i] = HubRef.Stock[i];
                Array.Clear(NetPerDay);
                _stockPrimed = true;
            }
            else
            {
                float scale = Bal.DayLengthSec / 5f;
                for (int i = 0; i < Bal.ItemCount; i++)
                {
                    int st = HubRef.Stock[i];
                    float inst = (st - _stockPrev[i]) * scale;
                    _stockPrev[i] = st;
                    NetPerDay[i] = NetPerDay[i] * 0.88f + inst * 0.12f;
                }
            }
        }
        RecentCombat = Math.Max(0, RecentCombat - dt);
        RaidBannerT = Math.Max(0, RaidBannerT - dt);

        _wealthTimer -= dt;
        if (_wealthTimer <= 0)
        {
            _wealthTimer = 1f;
            ComputeWealth();
            UpdateReveal();
        }

        UpdatePower(dt);

        _fluidT -= dt;
        if (_fluidT <= 0 || FluidDirty)
        {
            if (FluidDirty) { RebuildFluids(); FluidDirty = false; }
            _fluidT = 0.25f;
        }
        FluidTick(dt);

        _pollTimer -= dt;
        if (_pollTimer <= 0) { _pollTimer = 1f; PollutionPass(1f); }

        UpdateRaids(dt);
        UpdateStoryteller(dt);

        _alertTimer -= dt;
        if (_alertTimer <= 0) { _alertTimer = 1f; RebuildAlerts(); }

        _assignTimer -= dt;
        if (_assignTimer <= 0)
        {
            _assignTimer = 2.5f;
            AutoAssignWorkers();
        }

        foreach (var b in Builds.ToArray()) b.Update(this, dt);
        foreach (var t in Trains.ToArray()) t.Update(this, dt);
        foreach (var bot in Bots.ToArray()) bot.Update(this, dt);
        foreach (var p in Prisoners.ToArray()) p.Update(this, dt);
        foreach (var c in Cols.ToArray())
        {
            c.Update(this, dt);
            if (c.Hp <= 0) c.Die(this);          // SOULS: no more silent deaths
        }
        Cols.RemoveAll(c => c.Hp <= 0);
        foreach (var r in Foes.ToArray()) r.Update(this, dt);
        Foes.RemoveAll(r => r.Hp <= 0);

        // MADDOG iter-4: wildlife lives its quiet life
        foreach (var b in Beasts.ToArray()) b.Update(this, dt);
        for (int i = Carcasses.Count - 1; i >= 0; i--)
        {
            Carcasses[i].T -= dt;
            if (Carcasses[i].T <= 0) Carcasses.RemoveAt(i);
        }
        BeastRespawnT -= dt;
        if (BeastRespawnT <= 0)
        {
            if (Beasts.Count < Bal.BeastCap) SpawnHerd();
            BeastRespawnT = Rand.Next(Bal.BeastRespawnMin, Bal.BeastRespawnMax);
        }

        // MADDOG iter-9: the caravan's visit
        if (Trader != null)
        {
            Trader.Update(this, dt);
            if (Trader.Dead) Trader = null;
        }

        for (int i = Fx.Count - 1; i >= 0; i--)
        {
            Fx[i].Ttl -= dt;
            if (Fx[i].Ttl <= 0) Fx.RemoveAt(i);
        }

        for (int i = Log.Count - 1; i >= 0; i--)
        {
            Log[i].T -= dt;
            if (Log[i].T <= 0) Log.RemoveAt(i);
        }

        if (!Won && HubRef.Stock[(int)ItemKind.AdvPart] >= Bal.PartsToWin)
        {
            Won = true;
            AddLog("THE SIGNAL BEACON IS ONLINE.", Pal.Good);
        }
        if (!Lost && HubRef.Hp <= 0)
        {
            Lost = true;
            AddLog("The Hub is destroyed. ForgeHaven falls silent.", Pal.Bad);
        }
    }

    /// <summary>MADDOG iter-4: a grazer herd wanders in somewhere on
    /// revealed ground, a healthy distance from the hub.</summary>
    public void SpawnHerd()
    {
        var hc = HubRef.CenterTile;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            float ang = (float)(Rand.NextDouble() * Math.PI * 2);
            float dist = Bal.BeastSpawnDistMin +
                (float)Rand.NextDouble() * (Bal.BeastSpawnDistMax - Bal.BeastSpawnDistMin);
            int cx = (int)(hc.X + MathF.Cos(ang) * dist);
            int cy = (int)(hc.Y + MathF.Sin(ang) * dist);
            if (!World.InBounds(cx, cy)) continue;
            var t = World.Cell(cx, cy);
            if (t.T is not Terrain.Ground) continue;
            if (!World.RevealedAt(cx, cy)) continue;
            int n = Rand.Next(Bal.BeastHerdMin, Bal.BeastHerdMax + 1);
            for (int i = 0; i < n; i++)
                Beasts.Add(new Beast(cx + 0.5f + Rand.Next(-3, 4), cy + 0.5f + Rand.Next(-3, 4)));
            AddLog(Loc.T($"A grazer herd wanders past the colony ({n} beasts)."), Pal.TextDim);
            return;
        }
    }

    /// <summary>MADDOG iter-4: hunting damage. A hurt beast panics and runs.</summary>
    public void DamageBeast(Beast b, float dmg, float fromX, float fromY)
    {
        if (b.Hp <= 0) return;
        b.Hp -= dmg;
        b.FlashT = 0.12f;
        b.FleeT = Bal.BeastFleeSec;
        Fx.Add(new Particle(2, b.PosX, b.PosY, 0, 0, 0.3f, Pal.Warn));
        if (b.Hp <= 0)
        {
            Carcasses.Add(new Carcass(b.PosX, b.PosY, Bal.BeastFood));
            HuntedOnce = true;
            StatBeastsHunted++;
            AddLog(Loc.T("A grazer is down - its carcass is worth butchering."), Pal.Good);
        }
    }

    // ------------------------------------------------- RECLAMATION: blight

    /// <summary>Chunk-level blight level. Beyond the sim ring it is always 1:
    /// the far world belongs to the Silence.</summary>
    /// <summary>Hub chunk - the blight rings the COLONY, not the origin,
    /// so it follows wherever the player chose to land.</summary>
    public (int cx, int cy) HubChunk()
    {
        var hc = HubRef;
        return ((int)MathF.Floor((hc?.X ?? 0) / 32f), (int)MathF.Floor((hc?.Y ?? 0) / 32f));
    }

    public float BlightAt(int cx, int cy)
    {
        if (Blight.TryGetValue(World.Key(cx, cy), out var v)) return v;
        var (hcx, hcy) = HubChunk();
        return Math.Max(Math.Abs(cx - hcx), Math.Abs(cy - hcy)) >= Bal.BlightChunkRange ? 1f : 0f;
    }

    /// <summary>Dithered tile test: chunk level + a per-tile hash so the
    /// frontier looks organic instead of a hard chunk edge.</summary>
    public bool TileBlighted(int x, int y)
    {
        int cx = (int)MathF.Floor(x / 32f), cy = (int)MathF.Floor(y / 32f);
        float lvl = BlightAt(cx, cy);
        if (lvl <= 0.45f) return false;
        int h = ((x * 73856093) ^ (y * 19349663)) & 7;
        return lvl + (h - 3.5f) * 0.05f > Bal.BlightThreshold;
    }

    /// <summary>One blight step: spread from stronger neighbors, cleanse near
    /// civilization (wards). Public so tests can drive it directly.</summary>
    public void BlightTick()
    {
        // wards: the ground civilization holds
        var ward = new HashSet<long>();
        foreach (var b in Builds)
        {
            int r = b is Hub or Lamp ? 2 : 1;
            int cx = (int)MathF.Floor(b.X / 32f), cy = (int)MathF.Floor(b.Y / 32f);
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    ward.Add(World.Key(cx + dx, cy + dy));
        }

        int R = Bal.BlightChunkRange;
        var (hcx, hcy) = HubChunk();
        for (int cx = hcx - R; cx <= hcx + R; cx++)
            for (int cy = hcy - R; cy <= hcy + R; cy++)
            {
                long key = World.Key(cx, cy);
                float cur = BlightAt(cx, cy);
                float target;
                if (ward.Contains(key)) target = 0f;
                else
                {
                    float nb = MathF.Max(
                        MathF.Max(BlightAt(cx + 1, cy), BlightAt(cx - 1, cy)),
                        MathF.Max(BlightAt(cx, cy + 1), BlightAt(cx, cy - 1)));
                    target = MathF.Max(0f, nb - Bal.BlightStep);
                }
                float rate = target > cur
                    ? Bal.BlightSpreadBase * Bal.BiomeSpreadMul(BiomeKind)
                    : Bal.BlightRetreat * (Hope ? 3f : 1f);
                float next = cur + Math.Clamp(target - cur, -rate, rate);
                float implicitV = Math.Max(Math.Abs(cx - hcx), Math.Abs(cy - hcy)) >= R ? 1f : 0f;
                // prune only when truly back at the default (epsilon BELOW the
                // spread step, or spreading chunks get stuck at zero forever)
                if (MathF.Abs(next - implicitV) < 0.004f) Blight.Remove(key);
                else Blight[key] = next;
            }

        // first sighting near the colony is a chronicle moment
        if (!_blightChron)
            foreach (var b in Builds)
            {
                int cx = (int)MathF.Floor(b.X / 32f), cy = (int)MathF.Floor(b.Y / 32f);
                if (BlightAt(cx, cy) > 0.35f)
                {
                    _blightChron = true;
                    AddChron("The blight crept within sight of the colony. Push it back with light.");
                    break;
                }
            }
    }

    /// <summary>A blighted, enemy-walkable tile near the colony - raiders
    /// sometimes step out of the blight itself.</summary>
    private Point? BlightSpawnSpot()
    {
        // NOTE: only chunks within ~3 of the hub count - the implicit far
        // field (dist >= 14) is level 1.0 and would teleport raiders ~500
        // tiles out, where their A* dies and they path-storm every frame.
        var (hcx0, hcy0) = HubChunk();
        for (int tries = 0; tries < 12; tries++)
        {
            int cx = hcx0 + Rand.Next(-3, 4), cy = hcy0 + Rand.Next(-3, 4);
            if (BlightAt(cx, cy) < 0.6f) continue;
            int x = cx * Chunk.S + Rand.Next(0, Chunk.S), y = cy * Chunk.S + Rand.Next(0, Chunk.S);
            if (World.InBounds(x, y) && World.WalkEnemy(x, y)) return new Point(x, y);
        }
        return null;
    }

    // ------------------------------------------------- RECLAMATION: chapters

    public string ObjectiveText() => Chapter switch
    {
        0 => Loc.T($"Chapter 1 - Survivors: survive the first raid"),
        1 => Loc.T($"Chapter 2 - Industry: reach {Bal.Ch2Wealth} wealth or build a Fab T2"),
        2 => Loc.T("Chapter 3 - The Reclamation: excavate the Ark wreck"),
        3 => FinaleCleared ? Loc.T("The choice is yours.") : Loc.T("Chapter 4 - The Choice: the Silence answers - hold together"),
        _ => "",
    };

    private void AdvanceChapter(int ch)
    {
        if (ch <= Chapter) return;
        Chapter = ch;
        AddChron(ch switch
        {
            1 => "The first raid broke on the walls. The colony stopped surviving and started building.",
            2 => "The furnaces never cooled. This is an industry now.",
            3 => "The Ark is open. Whatever is coming, it knows we are here.",
            _ => "The Silence is broken.",
        });
    }

    private void UpdateChapters()
    {
        if (Chapter == 0 && ((Wave > 0 && Foes.Count == 0) || Day >= 8)) AdvanceChapter(1);
        if (Chapter == 1 && (Wealth >= Bal.Ch2Wealth || Builds.Any(b => b is FabT2))) AdvanceChapter(2);
        if (Chapter == 3 && FinalePending && Foes.Count == 0)
        {
            FinalePending = false;
            FinaleCleared = true;
            Won = true;
            AddChron("The Silence is broken. The colony stands in the morning light.");
        }
    }

    /// <summary>The ark is open: start the countdown to the finale.</summary>
    public void OnArkOpened()
    {
        AdvanceChapter(3);
        FinaleAt = Time + Bal.FinaleDelaySec;
        AddLog("THE ARK IS OPEN. Something in the wastes has gone very quiet.", Pal.Accent);
    }

    /// <summary>The last stand: everything the Silence has left.</summary>
    private void SpawnFinale()
    {
        FinalePending = true;
        Wave++;
        int n = Math.Max(10, 8 + Wave * 2);
        float hpBonus = Wave * Bal.RaiderHpPerWave;
        var hc = HubRef.CenterTile;
        float hcX = hc.X + HubRef.W / 2f, hcY = hc.Y + HubRef.H / 2f;
        int placed = 0; float sx = 0, sy = 0;
        for (int i = 0; i < n; i++)
        {
            int x = 0, y = 0; bool ok = false;
            var bs = BlightSpawnSpot();
            if (bs.HasValue && World.WalkEnemy(bs.Value.X, bs.Value.Y)) { x = bs.Value.X; y = bs.Value.Y; ok = true; }
            for (int tries = 0; tries < 24 && !ok; tries++)
            {
                double ang = Rand.NextDouble() * Math.PI * 2;
                float dd = 26f + (float)Rand.NextDouble() * 10f;
                x = (int)(hcX + Math.Cos(ang) * dd);
                y = (int)(hcY + Math.Sin(ang) * dd);
                ok = World.WalkEnemy(x, y);
            }
            if (!ok) continue;
            sx += x; sy += y; placed++;
            Foes.Add(new Raider(x + .5f, y + .5f, i < 2, hpBonus) { Sapper = i % 2 == 0 });
        }
        if (placed > 0) { RaidPingX = sx / placed + .5f; RaidPingY = sy / placed + .5f; }
        RaidBannerT = 8f;
        AddLog($"THE SILENCE ANSWERS: {placed} horrors, coming to end it.", Pal.Bad);
        AddChron("Night fell, and everything the Silence had left came with it.");
    }

    /// <summary>MADDOG iter-9 (G13): a caravan walks in from the wastes.</summary>
    public void TraderArrives()
    {
        if (Trader != null) return;
        var hc = HubRef.CenterTile;
        var park = Trader.ParkSpot(this);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float ang = (float)(Rand.NextDouble() * Math.PI * 2);
            int x = (int)(hc.X + MathF.Cos(ang) * Bal.NewcomerEdgeDist);
            int y = (int)(hc.Y + MathF.Sin(ang) * Bal.NewcomerEdgeDist);
            if (!World.InBounds(x, y)) continue;
            var path = World.FindPath(x, y, park.x, park.y, enemy: false);
            if (path == null) continue;
            Trader = new Trader { PosX = x + 0.5f, PosY = y + 0.5f, Path = path, PathIdx = 0 };
            AddLog(Loc.T("A trade caravan was spotted at the map edge."), Pal.Accent);
            if (!_traderChron)
            {
                _traderChron = true;
                AddChron("The first caravan found us. Word of the colony is spreading.");
            }
            return;
        }
    }

    /// <summary>Execute deal i against the parked caravan (command-driven).</summary>
    public void DoTrade(int i)
    {
        if (Trader == null || !Trader.Arrived) return;
        if (i < 0 || i >= Bal.TradeDeals.Length) return;
        var d = Bal.TradeDeals[i];
        if (Trader.SoldDeals[i] >= d.Max) return;
        if (HubRef.Stock[(int)d.Give] < d.GiveN) return;
        HubRef.Stock[(int)d.Give] -= d.GiveN;
        Trader.SoldDeals[i]++;
        if (d.Recruit)
        {
            var c = SpawnNewcomer();
            AddLog(Loc.T($"The caravan hands over a signed contract - {c.Name} joins the colony!"), Pal.Good);
        }
        else
        {
            HubRef.Stock[(int)d.Get] += d.GetN;
            AddLog(Loc.T($"Traded {d.GiveN} {Bal.ItemName(d.Give)} for {d.GetN} {Bal.ItemName(d.Get)}."), Pal.TextDim);
        }
    }

    public void QueueTrade(int deal) =>
        CmdQueue.Enqueue(new SimCmd { Type = CmdType.Trade, I0 = deal });

    private void UpdateReveal()
    {
        foreach (var b in Builds)
            World.RevealAround(b.X, b.Y, b is Turret or Watchtower ? 2 : 1);
        foreach (var c in Cols)
            if (c.Hp > 0) World.RevealAround((int)c.PosX, (int)c.PosY, 1);
        foreach (var t in Trains)
            World.RevealAround((int)t.PosX, (int)t.PosY, 1);
    }

    /// <summary>Machines standing idle without their operator get staffed.</summary>
    private void AutoAssignWorkers()
    {
        if (!Has(Tech.Automation))
            foreach (var b in Builds)
                if (b is MachineBase m && m.NeedsWorker && m.Operator == null && m is not Lab and not Watchtower)
                    AssignOperator(m);
        foreach (var b in Builds)
            if (b is MachineBase mb && mb is Lab or Watchtower)
                AssignOperator(mb);
        AssignBuilders();
        AssignMiners();
        _haulScanT -= 0.05f;
        if (_haulScanT <= 0f) { _haulScanT = 0.5f; AssignHaulers(); }
        AssignRepairers();
        AssignGatherers();
        AssignExcavators();
    }

    private void AssignBuilders()
    {
        foreach (var bp in Blueprints.ToArray())
        {
            if (bp.Builder != null && bp.Builder.Hp > 0) continue;
            Colonist? best = null; int bpri = int.MaxValue, bskill = -1; float bd = float.MaxValue;
            foreach (var c in Cols)
            {
                if (c.Hp <= 0 || c.Drafted || c.Arriving) continue;
                if (c.Job != null || c.BuildJob != null || c.MineJob != null || c.HaulTarget != null) continue;
                int p = c.Priorities[(int)WorkType.Construct];
                if (p >= 4) continue;
                int sk = c.Stats[Bal.StatOf(WorkType.Construct)];
                float d = MathF.Abs(c.PosX - (bp.X + 0.5f)) + MathF.Abs(c.PosY - (bp.Y + 0.5f));
                if (p < bpri || (p == bpri && (sk > bskill || (sk == bskill && d < bd))))
                { bpri = p; bskill = sk; bd = d; best = c; }
            }
            if (best == null) continue;
            var spot = AdjacentSpot(bp.X, bp.Y, bp.W, bp.H, best.PosX, best.PosY);
            if (spot == null) continue;                       // unreachable right now
            var path = World.FindPath((int)best.PosX, (int)best.PosY, spot.Value.X, spot.Value.Y, enemy: false);
            if (path == null) continue;
            best.BuildJob = bp; bp.Builder = best;
            best.Path = path; best.PathIdx = 0;
            best.State = ColState.GoBuild;
        }
    }

    /// <summary>HAULING: every half second, machines with low input buffers
/// and orphaned ground piles attract a hauler (Haul priority x distance).
/// Sources: hub stock, storage silos, dropped piles.</summary>
private void AssignHaulers()
{
    // machine resupply
    foreach (var m in Builds)
    {
        if (m is not MachineBase mb || mb.BrokenDown) continue;
        foreach (var kind in mb.HaulNeeds())
        {
            if (mb.In.TryGetValue(kind, out var have) && have > 2) continue;
            if (Cols.Any(c => c.Hp > 0 && c.HaulTarget == mb && c.HaulKind == kind)) continue;
            int hubN = HubRef.Stock[(int)kind];
            StorageSilo? silo = null;
            foreach (var b in Builds)
                if (b is StorageSilo s && s.StoredKind == kind && s.N > 0) { silo = s; break; }
            ItemPile? pile = null;
            foreach (var p in ItemPiles)
                if (p.Kind == kind && p.N > 0) { pile = p; break; }
            if (hubN <= 0 && silo == null && pile == null) continue;
            var best = FreeHauler(kind, mb.X + mb.W / 2f, mb.Y + mb.H / 2f);
            if (best != null)
            {
                // source preference: hub stock, then dropped piles, then silos
                var usePile = hubN > 0 ? null : pile;
                var useSilo = hubN > 0 || usePile != null ? null : silo;
                best.StartHaul(this, mb, kind, usePile, useSilo);
            }
        }
    }

    // orphan piles: one per scan gets carried to the hub/stockpile
    foreach (var p in ItemPiles.ToArray())
    {
        if (p.N <= 0) continue;
        if (Cols.Any(c => c.Hp > 0 && c.FetchPile == p)) continue;
        var best = FreeHauler(p.Kind, p.X, p.Y);
        best?.StartHaul(this, null, p.Kind, p, null);
        break;
    }
}

private Colonist? FreeHauler(ItemKind kind, float fx, float fy)
{
    Colonist? best = null; int bpri = int.MaxValue; float bd = float.MaxValue;
    foreach (var c in Cols)
    {
        if (c.Hp <= 0 || c.Drafted || c.Arriving) continue;
        if (c.Job != null || c.BuildJob != null || c.MineJob != null || c.RepairJob != null ||
            c.GatherJob != null || c.ExcavJob != null || c.HaulTarget != null) continue;
        if (c.CarryN > 0 && c.CarryKind != (int)kind) continue;
        if (c.CarryN >= Bal.PawnCarryCap) continue;
        int p = c.Priorities[(int)WorkType.Haul];
        if (p >= 4) continue;
        float d = MathF.Abs(c.PosX - fx) + MathF.Abs(c.PosY - fy);
        if (p < bpri || (p == bpri && d < bd)) { bpri = p; bd = d; best = c; }
    }
    return best;
}

private void AssignMiners()
    {
        foreach (var mo in MineOrders.ToArray())
        {
            if (mo.Miner != null && mo.Miner.Hp > 0) continue;
            var want = ItemOfTerrain(World.Cell(mo.X, mo.Y).T);
            Colonist? best = null; int bpri = int.MaxValue, bskill = -1; float bd = float.MaxValue;
            foreach (var c in Cols)
            {
                if (c.Hp <= 0 || c.Drafted || c.Arriving) continue;
                if (c.Job != null || c.BuildJob != null || c.MineJob != null || c.HaulTarget != null) continue;
                // PAWN INVENTORY: full pawns deliver first; a different
                // cargo type may not mix onto this order
                if (c.CarryN >= Bal.PawnCarryCap) continue;
                if (c.CarryN > 0 && c.CarryKind != (int)want) continue;
                int p = c.Priorities[(int)WorkType.Mine];
                if (p >= 4) continue;
                int sk = c.Stats[Bal.StatOf(WorkType.Mine)];
                float d = MathF.Abs(c.PosX - (mo.X + 0.5f)) + MathF.Abs(c.PosY - (mo.Y + 0.5f));
                if (p < bpri || (p == bpri && (sk > bskill || (sk == bskill && d < bd))))
                { bpri = p; bskill = sk; bd = d; best = c; }
            }
            if (best == null) continue;
            var spot = AdjacentSpot(mo.X, mo.Y, 1, 1, best.PosX, best.PosY);
            if (spot == null) continue;
            var path = World.FindPath((int)best.PosX, (int)best.PosY, spot.Value.X, spot.Value.Y, enemy: false);
            if (path == null) continue;
            best.MineJob = mo; mo.Miner = best;
            best.Path = path; best.PathIdx = 0;
            best.State = ColState.GoMine;
        }
    }

    /// <summary>MADDOG iter-2: damaged buildings attract a repairer
    /// (priority × skill × distance, same pattern as builders/miners).
    /// Needs at least 1 Stone in stock — patching isn't free.</summary>
    private void AssignRepairers()
    {
        foreach (var b in Builds.ToArray())
        {
            // ORGANISM: a BROKEN machine attracts repairers even at full HP
            // (wear maintenance is free; battle damage still bills Stone).
            // Note: worn-but-running machines do NOT - chasing them across the
            // map was a pathfinding stampede (see MADDOG notes).
            bool wornOut = b is MachineBase m0 && m0.BrokenDown;
            if (b.Hp <= 0 || (b.Hp >= b.MaxHp - 0.5f && !wornOut)) continue;
            if (b.Repairer != null && b.Repairer.Hp > 0) continue;
            Colonist? best = null; int bpri = int.MaxValue, bskill = -1; float bd = float.MaxValue;
            foreach (var c in Cols)
            {
                if (c.Hp <= 0 || c.Drafted || c.Arriving) continue;
                if (c.Job != null || c.BuildJob != null || c.MineJob != null || c.RepairJob != null || c.HaulTarget != null) continue;
                int p = c.Priorities[(int)WorkType.Repair];
                if (p >= 4) continue;
                int sk = c.Stats[Bal.StatOf(WorkType.Repair)];
                float d = MathF.Abs(c.PosX - (b.X + 0.5f)) + MathF.Abs(c.PosY - (b.Y + 0.5f));
                if (p < bpri || (p == bpri && (sk > bskill || (sk == bskill && d < bd))))
                { bpri = p; bskill = sk; bd = d; best = c; }
            }
            if (best == null) continue;
            var spot = AdjacentSpot(b.X, b.Y, b.W, b.H, best.PosX, best.PosY);
            if (spot == null) continue;
            var path = World.FindPath((int)best.PosX, (int)best.PosY, spot.Value.X, spot.Value.Y, enemy: false);
            if (path == null) continue;
            best.RepairJob = b; b.Repairer = best;
            best.Path = path; best.PathIdx = 0;
            best.State = ColState.GoRepair;
        }
    }

    // ------------------------------------------------------------ weather

    /// <summary>Deterministic weather transitions (seeded Rand, Bal-tunable).</summary>
    private void RollWeather()
    {
        var prev = Weather;
        double roll = Rand.NextDouble();
        switch (Weather)
        {
            case WeatherKind.Clear:
                if (roll < Bal.WeatherRainChance)
                { Weather = WeatherKind.Rain; WeatherT = Rand.Next(Bal.WeatherRainMinSec, Bal.WeatherRainMaxSec); }
                else WeatherT = Rand.Next(Bal.WeatherClearMinSec, Bal.WeatherClearMaxSec);
                break;
            case WeatherKind.Rain:
                if (roll < Bal.WeatherStormChance)
                { Weather = WeatherKind.Storm; WeatherT = Rand.Next(Bal.WeatherStormMinSec, Bal.WeatherStormMaxSec); }
                else { Weather = WeatherKind.Clear; WeatherT = Rand.Next(Bal.WeatherClearMinSec, Bal.WeatherClearMaxSec); }
                break;
            default:
                Weather = WeatherKind.Clear;
                WeatherT = Rand.Next(Bal.WeatherClearMinSec, Bal.WeatherClearMaxSec);
                break;
        }
        if (Weather != prev)
        {
            if (Weather == WeatherKind.Storm && !_stormChron)
            {
                _stormChron = true;
                AddChron("The first storm rolled over the colony and the turbines sang all night.");
            }
            AddLog(Loc.T(Weather switch
            {
                WeatherKind.Rain => "Rain drifts over the colony - solar dips, crops drink.",
                WeatherKind.Storm => "A storm front rolls in - turbines sing, solar gutters out.",
                _ => "The sky clears.",
            }), Weather == WeatherKind.Clear ? Pal.TextDim : Pal.Accent);
        }
    }

    /// <summary>RECLAMATION: the ark attracts miners once Chapter 2 begins.</summary>
    private void AssignExcavators()
    {
        if (Chapter < 2) return;
        var ark = Builds.FirstOrDefault(b => b is ArkWreck a && a.Dig < Bal.ArkDigWork) as ArkWreck;
        if (ark == null) return;
        if (ark.Excavator != null && ark.Excavator.Hp > 0) return;
        Colonist? best = null; int bpri = int.MaxValue, bskill = -1; float bd = float.MaxValue;
        foreach (var c in Cols)
        {
            if (c.Hp <= 0 || c.Drafted || c.Arriving) continue;
            if (c.Job != null || c.BuildJob != null || c.MineJob != null || c.RepairJob != null || c.HaulTarget != null
                || c.GatherJob != null || c.ExcavJob != null) continue;
            int p = c.Priorities[(int)WorkType.Mine];
            if (p >= 4) continue;
            int sk = c.Stats[Bal.StatOf(WorkType.Mine)];
            float d = MathF.Abs(c.PosX - (ark.X + 1.5f)) + MathF.Abs(c.PosY - (ark.Y + 1.5f));
            if (p < bpri || (p == bpri && (sk > bskill || (sk == bskill && d < bd))))
            { bpri = p; bskill = sk; bd = d; best = c; }
        }
        if (best == null) return;
        var spot = AdjacentSpot(ark.X, ark.Y, ark.W, ark.H, best.PosX, best.PosY);
        if (spot == null) return;
        var path = World.FindPath((int)best.PosX, (int)best.PosY, spot.Value.X, spot.Value.Y, enemy: false);
        if (path == null) return;
        best.ExcavJob = ark; ark.Excavator = best;
        best.Path = path; best.PathIdx = 0;
        best.State = ColState.GoExcavate;
    }

    /// <summary>MADDOG iter-4: carcasses attract a gatherer (Mine priority —
    /// "grab things from the world"), who butchers + hauls them as Food.</summary>
    private void AssignGatherers()
    {
        foreach (var car in Carcasses.ToArray())
        {
            if (car.Food <= 0 || car.Gatherer != null && car.Gatherer.Hp > 0) continue;
            Colonist? best = null; int bpri = int.MaxValue, bskill = -1; float bd = float.MaxValue;
            foreach (var c in Cols)
            {
                if (c.Hp <= 0 || c.Drafted || c.Arriving) continue;
                if (c.Job != null || c.BuildJob != null || c.MineJob != null || c.RepairJob != null || c.GatherJob != null || c.HaulTarget != null) continue;
                // PAWN INVENTORY: full pawns deliver first; only matching
                // Food cargo may gather more
                if (c.CarryN >= Bal.PawnCarryCap) continue;
                if (c.CarryN > 0 && c.CarryKind != (int)ItemKind.Food) continue;
                int p = c.Priorities[(int)WorkType.Mine];
                if (p >= 4) continue;
                int sk = c.Stats[Bal.StatOf(WorkType.Mine)];
                float d = MathF.Abs(c.PosX - car.X) + MathF.Abs(c.PosY - car.Y);
                if (p < bpri || (p == bpri && (sk > bskill || (sk == bskill && d < bd))))
                { bpri = p; bskill = sk; bd = d; best = c; }
            }
            if (best == null) continue;
            var spot = AdjacentSpot((int)car.X, (int)car.Y, 1, 1, best.PosX, best.PosY);
            if (spot == null) continue;
            var path = World.FindPath((int)best.PosX, (int)best.PosY, spot.Value.X, spot.Value.Y, enemy: false);
            if (path == null) continue;
            best.GatherJob = car; car.Gatherer = best;
            best.Path = path; best.PathIdx = 0;
            best.State = ColState.GoGather;
        }
    }

    /// <summary>Advance the onboarding hints: a stage is done when its
    /// condition holds (or the player dismissed it). Current = first open.</summary>
    public void UpdateHints()
    {
        for (int i = 0; i < HintStageCount; i++)
        {
            if ((HintsDone & (1 << i)) != 0) continue;
            if (HintConditionMet(i)) HintsDone |= 1 << i;
        }
    }

    public void MarkHintDone(int i)
    {
        if (i >= 0 && i < HintStageCount) HintsDone |= 1 << i;
    }

    public int HintStage()
    {
        for (int i = 0; i < HintStageCount; i++)
            if ((HintsDone & (1 << i)) == 0) return i;
        return -1;
    }

    private bool HintConditionMet(int i) => i switch
    {
        0 => _selSeen,
        1 => MineOrders.Count > 0 || Builds.Any(b => b is Drill or DeepDrill),
        2 => Builds.Any(b => b.Kind is BuildKind.Smelter or BuildKind.PrimitiveFurnace or BuildKind.Fabricator),
        3 => TechDone.Any(t => t) || TechLevels.Any(l => l > 0),
        4 => Builds.Any(b => b.Kind is BuildKind.CropPlot or BuildKind.BioProcessor),
        5 => Builds.Any(b => b.Kind is BuildKind.Wall or BuildKind.Door or BuildKind.Turret or BuildKind.HeavyTurret),
        6 => HuntedOnce,
        _ => true,
    };

    /// <summary>UI hook: the player opened a colonist panel once.</summary>
    public void NotifySelected() => _selSeen = true;

    // ---------------------------------------------------------------- raids

    private void UpdateRaids(float dt)
    {
        // grace: threat only builds once the colony is worth raiding AND the
        // opening minutes are behind you (protects fresh starts, tests, and
        // dev-subsidized sandboxes alike)
        if (Wealth < Bal.GraceWealth || Time < Bal.GraceTime)
        {
            Threat = Math.Max(0, Threat - dt * 2f);
            return;
        }
        float mul = Story switch
        {
            Storyteller.Merciless => 1.5f,
            Storyteller.Random => _randThreatMul,
            _ => 1f,
        };
        // soft-capped wealth: a huge stockpile shouldn't mean a raid a minute
        Threat += Math.Min(Wealth, Bal.ThreatWealthCap) * Bal.ThreatPerWealth
                  * World.P.Aggression * Bal.BiomeAggro(BiomeKind) * mul * dt;
        if (Threat >= NextRaidAt)
            SpawnWave();
    }

    public void SpawnWave(bool mini = false)
    {
        if (!mini)
        {
            Wave++;
            Threat = 0;
            NextRaidAt = Bal.FirstRaidAt * MathF.Pow(Bal.RaidGrowth, Wave);
        }
        else Threat *= 0.5f;

        int n = (int)MathF.Round(Bal.WaveBudget(Wealth) * (mini ? 0.5f : 1f));
        // pollution makes the wildlife angrier AND aims them at the source
        float pollBoost = Math.Min(Bal.RaidPollBoostCap, TotalPollution / 4000f);
        n = (int)MathF.Round(n * (1f + pollBoost) * (IsNight ? 1.15f : 1f));
        n = Math.Max(2, n);

        float hpBonus = Wave * Bal.RaiderHpPerWave;
        bool apex = !mini && Wave % 4 == 0;

        float hcX = HubRef.X + HubRef.W / 2f, hcY = HubRef.Y + HubRef.H / 2f;
        double baseAng = Rand.NextDouble() * Math.PI * 2;

        // bias the approach direction toward the pollution centroid
        if (TotalPollution > 50)
        {
            float vx = 0, vy = 0;
            foreach (var kv in World.AllChunks)
            {
                World.Unpack(kv.Key, out var ccx, out var ccy);
                float sum = 0;
                for (int i = 0; i < kv.Value.Pol.Length; i++) sum += kv.Value.Pol[i];
                if (sum <= 0.01f) continue;
                float wx = (ccx + 0.5f) * Chunk.S - hcX, wy = (ccy + 0.5f) * Chunk.S - hcY;
                float len = MathF.Sqrt(wx * wx + wy * wy);
                if (len < 1) continue;
                vx += wx / len * sum; vy += wy / len * sum;
            }
            if (vx * vx + vy * vy > 1f)
                baseAng = Math.Atan2(vy, vx);
        }

        float dist = mini
            ? 38f
            : Math.Min(Bal.RaidSpawnMax, Bal.RaidSpawnBase + Wave * Bal.RaidSpawnPerWave);

        float sx = 0, sy = 0;
        int placed = 0, sappers = 0;
        for (int i = 0; i < n; i++)
        {
            int x = 0, y = 0;
            bool ok = false;
            var bspot = BlightSpawnSpot();
            if (bspot.HasValue && World.WalkEnemy(bspot.Value.X, bspot.Value.Y)) { x = bspot.Value.X; y = bspot.Value.Y; ok = true; }
            for (int tries = 0; tries < 24 && !ok; tries++)
            {
                double ang = baseAng + (Rand.NextDouble() * 2 - 1) * 0.35;
                float dd = dist + (float)(Rand.NextDouble() * 2 - 1) * 6f;
                x = (int)(hcX + Math.Cos(ang) * dd);
                y = (int)(hcY + Math.Sin(ang) * dd);
                ok = World.WalkEnemy(x, y);
            }
            if (!ok) continue;
            sx += x; sy += y; placed++;
            bool sapper = !mini && Wave >= Bal.SapperMinWave
                && sappers <= placed / 2
                && Rand.NextDouble() < Bal.SapperChance;
            if (sapper) sappers++;
            Foes.Add(new Raider(x + .5f, y + .5f, apex && i == 0, hpBonus) { Sapper = sapper });
        }
        if (placed > 0) { RaidPingX = sx / placed + .5f; RaidPingY = sy / placed + .5f; }
        RaidBannerT = 5.5f;

        AddLog(apex ? $"RAID {Wave}: something huge approaches..."
                    : $"{(mini ? "Skirmish" : $"RAID {Wave}")}: {placed} hostiles drawn by your industry!", Pal.Bad);
        // MADDOG iter-6 (G8): raids under cover of darkness
        if (IsNight && !mini)
            AddLog(Loc.T("NIGHT RAID - unlit turrets fire slower. Lamps keep them accurate."), Pal.Warn);
        if (sappers > 0)
            AddLog(Loc.T($"{sappers} of them carry breaching tools - they will go straight for the walls!"), Pal.Warn);
        RecentCombat = 4f;
    }

    // ---------------------------------------------------------- storyteller

    private void UpdateStoryteller(float dt)
    {
        if (Story == Storyteller.Random)
        {
            _randThreatMul = Math.Clamp(_randThreatMul + (Rand.NextSingle() - 0.5f) * 0.1f * dt, 0.5f, 2f);
        }

        _eventT -= dt;
        if (_eventT > 0) return;
        _eventT = Rand.Next(80, 170);

        double roll = Rand.NextDouble();
        switch (Story)
        {
            case Storyteller.Merciless:
                if (roll < 0.35 && Wave > 0) { SpawnWave(mini: true); return; }
                break;
        }

        // MADDOG iter-9: caravans roll as their own event type
        if (roll >= 0.55 && roll < 0.80 && Story != Storyteller.Merciless && Trader == null)
        {
            TraderArrives();
            return;
        }
        if (roll < 0.55 || Story == Storyteller.Builder)
        {
            if (Cols.Count < 12)
            {
                SpawnNewcomer();
                AddLog("A wanderer spotted the colony — they're walking in from the wastes.", Pal.Good);
            }
            else
            {
                HubRef.Stock[(int)ItemKind.IronPlate] += 30;
                HubRef.Stock[(int)ItemKind.CopperPlate] += 10;
                AddLog("A supply pod landed nearby: +30 iron / +10 copper plates.", Pal.Good);
            }
        }
        else
        {
            HubRef.Stock[(int)ItemKind.IronPlate] += 30;
            HubRef.Stock[(int)ItemKind.CopperPlate] += 10;
            AddLog("A supply pod landed nearby: +30 iron / +10 copper plates.", Pal.Good);
        }
    }

    // -------------------------------------------------------------- alerts

    private void RebuildAlerts()
    {
        Alerts.Clear();
        void Add(string text, Color col, float x, float y) =>
            Alerts.Add(new AlertLine { Text = text, Col = col, X = x, Y = y });

        if (RecentCombat > 0 && HubRef.Hp < HubRef.MaxHp * 0.995f)
            Add("Hub under attack!", Pal.Bad, HubRef.X + 1.5f, HubRef.Y + 1.5f);

        if (Trader is { Arrived: true })
            Add($"Trader here - {Trader.WindowT:0}s - click the cart to barter", Pal.Accent,
                Trader.PosX, Trader.PosY);

        // RECLAMATION alerts
        var (hcx, hcy) = HubChunk();
        for (int dx = -4; dx <= 4 && dx < 100; dx++)
            for (int dy = -4; dy <= 4; dy++)
                if (BlightAt(hcx + dx, hcy + dy) > 0.45f)
                { Add("The blight nears the Hub - lamps push it back", Pal.Warn, HubRef.X + 1.5f, HubRef.Y + 1.5f); dx = 100; break; }
        if (Chapter == 2 && Builds.FirstOrDefault(b => b is ArkWreck a && a.Dig < Bal.ArkDigWork) is ArkWreck aw)
            Add($"Ark wreck - send miners ({(int)(aw.DigFrac * 100)}%)", Pal.Accent, aw.X + 1.5f, aw.Y + 1.5f);

        foreach (var c in Cols)
            if (c.Hp < c.MaxHp * 0.35f)
                Add($"{c.Name} is badly wounded", Pal.Bad, c.PosX, c.PosY);

        if (HubRef.Stock[(int)ItemKind.Food] == 0)
            Add("No Food at the Hub", Pal.Warn, HubRef.X + 1.5f, HubRef.Y + 1.5f);

        foreach (var gr in Grids)
            if (gr.Demand > gr.Supply + 0.5f && gr.Nodes.Count > 0)
                Add($"Power grid brownout ({(int)gr.Supply}/{(int)gr.Demand})", Pal.Warn,
                    gr.Nodes[0].X, gr.Nodes[0].Y);

        foreach (var b in Builds)
        {
            if (b is MachineBase mbr && mbr.BrokenDown)
                Add($"{Bal.Name(b.Kind)} BROKEN DOWN", Pal.Bad, b.X, b.Y);
            if (b is MachineBase m && m.NeedsWorker && m.Operator == null &&
                (!Has(Tech.Automation) || m is Lab or Watchtower))
                Add($"{Bal.Name(b.Kind)} needs an operator", Pal.Warn, b.X, b.Y);
            else if (b is Turret t && t.Shots == 0)
                Add("Turret out of ammo", Pal.Warn, b.X, b.Y);
            else if (b is Lab lab && lab.In.GetValueOrDefault(ItemKind.SciencePack) == 0 && lab.Reserve <= 0.01f)
                Add("Lab out of Science Packs", Pal.Warn, b.X, b.Y);
            else if (b is SteamEngine se && se.CurrentOutput <= 0 && GridOf(se) != null)
                Add("Steam engine idle — no steam", Pal.Warn, b.X, b.Y);
        }

        foreach (var t in Trains)
            if (t.Target != null && t.Path == null)
                Add("Train has no route to its station", Pal.Warn, t.PosX, t.PosY);

        foreach (var p in Prisoners)
            if (!p.Fed)
                Add($"Prisoner {p.Name} is starving", Pal.Warn, p.X, p.Y);

        if (Alerts.Count > 8) Alerts.RemoveRange(8, Alerts.Count - 8);
    }

    // ------------------------------------------------------------- research

    public void AddResearch(float pts)
    {
        if (ActiveTech == Tech.None) return;
        int i = (int)ActiveTech;
        if (LeveledTech.IsLeveled(ActiveTech))
        {
            TechProg[i] += pts;
            while (TechProg[i] >= Bal.TechCost(ActiveTech, TechLevels[i]))
            {
                TechProg[i] -= Bal.TechCost(ActiveTech, TechLevels[i]);
                TechLevels[i]++;
                AddLog($"{Bal.TechName(ActiveTech)} is now level {TechLevels[i]}!", Pal.Good);
                if (TechLevels[i] >= LeveledTech.MiningProdCap)
                {
                    TechDone[i] = true;
                    break;
                }
            }
            return;
        }
        if (TechDone[i]) return;
        TechProg[i] += pts;
        if (TechProg[i] >= Bal.TechCost(ActiveTech))
        {
            TechDone[i] = true;
            Complete(ActiveTech);
        }
    }

    private void Complete(Tech t)
    {
        AddLog($"RESEARCH COMPLETE: {Bal.TechName(t)}", Pal.Good);
        if (t == Tech.Automation)
        {
            int freed = 0;
            foreach (var b in Builds)
                if (b is MachineBase m && m is not Lab && m is not Watchtower && m.Operator != null)
                {
                    m.Operator.Job = null;
                    m.Operator.State = ColState.Idle;
                    m.Operator = null;
                    freed++;
                }
            if (freed > 0)
                AddLog($"{freed} colonist(s) liberated from manual machine duty.", Pal.Good);
        }
    }

    public bool SelectTech(Tech t)
    {
        if (t == Tech.None || TechDone[(int)t]) return false;
        var pre = Bal.TechPrereq(t);
        if (pre != Tech.None && !Has(pre)) return false;
        ActiveTech = t;
        return true;
    }

    // ------------------------------------------------------------ particles

    public void SpawnLaser(float ax, float ay, float bx, float by) =>
        SpawnLaser(ax, ay, bx, by, Pal.C(140, 230, 255));

    public void SpawnLaser(float ax, float ay, float bx, float by, Color col) =>
        Fx.Add(new Particle(0, ax, ay, bx, by, 0.09f, col));

    public void SpawnSpark(float x, float y, Color col) =>
        Fx.Add(new Particle(1, x, y, 0, 0, 0.3f, col));

    /// <summary>A logistic drone flying from A to B (visual).</summary>
    public void DroneFlight(float ax, float ay, float bx, float by) =>
        Fx.Add(new Particle(3, ax, ay, bx, by, 0.6f, Pal.C(230, 230, 160)));

    public void BuildingTouched(Building b) { /* hook for anim/telemetry */ }

    // -------------------------------------------------------------------- ui

    // MADDOG iter-6: permanent event history behind the fading log (L key)
    public readonly List<LogLine> History = new();
    public const int HistoryCap = 120;

    // SOULS: the colony chronicle - the run, narrated (C key)
    public readonly List<(int Day, string Text)> Chronicle = new();
    public const int ChronicleCap = 200;
    private int _nextCid = 1;
    private bool _traderChron, _stormChron, _breachChron;

    // RECLAMATION: the blight, the ark, the chapters, the ending
    public readonly Dictionary<long, float> Blight = new();   // chunkKey -> 0..1
    private float _blightT;
    private bool _blightChron;
    public int Chapter;                       // 0 Survivors, 1 Industry, 2 Reclamation, 3 The Choice
    public float FinaleAt = -1f;              // >0: the Silence is coming
    public bool FinalePending, FinaleCleared, Hope;
    public int EndingChosen;                  // 0 none, 1 signaled home, 2 stayed

    public void AddChron(string text)
    {
        Chronicle.Insert(0, (Day, Loc.T(text)));
        if (Chronicle.Count > ChronicleCap) Chronicle.RemoveAt(Chronicle.Count - 1);
    }

    // MADDOG iter-7 (M7): colony statistics + wealth history
    public int StatKills, StatBeastsHunted, StatBuilt, StatMined, StatMeals;
    public readonly List<float> WealthHist = new();
    private int _lastDay;

    public void AddLog(string text, Color col)
    {
        Log.Insert(0, new LogLine(text, col));
        if (Log.Count > 6) Log.RemoveAt(Log.Count - 1);
        History.Insert(0, new LogLine(text, col));
        if (History.Count > HistoryCap) History.RemoveAt(History.Count - 1);
    }

    public void AddLogThrottled(string text, Color col)
    {
        if (_throttleMsg > 0 && _lastThrottled == text) return;
        _throttleMsg = 6f;
        _lastThrottled = text;
        AddLog(text, col);
    }

    public int CountFreeColonists()
    {
        int n = 0;
        foreach (var c in Cols) if (c.Hp > 0 && c.Job == null && c.State != ColState.Flee) n++;
        return n;
    }

    // ------------------------------------------------------ command layer
    //  Player intents are queued and applied deterministically at the top of
    //  Update() — the seam a lockstep multiplayer or replay system plugs into.

    public void QueuePlace(BuildKind k, int x, int y, Dir f) =>
        CmdQueue.Enqueue(new SimCmd { Type = CmdType.Place, X = x, Y = y, I0 = (int)k, Face = f });

    public void QueueMine(int x, int y) =>
        CmdQueue.Enqueue(new SimCmd { Type = CmdType.Mine, X = x, Y = y });

    private void ToggleMineOrder(int x, int y)
    {
        if (!World.InBounds(x, y)) return;
        var key = ((long)x << 32) ^ (uint)y;
        if (_mineAt.TryGetValue(key, out var existing)) { CancelMineOrder(existing); return; }
        if (World.Cell(x, y).B != null) return;                 // can't mine under buildings
        if (_bpAt.ContainsKey(key)) return;                     // or under a blueprint
        var t = World.Cell(x, y).T;
        if (t is not (Terrain.Rock or Terrain.IronOre or Terrain.CopperOre
                      or Terrain.Crystal or Terrain.Flora or Terrain.Tree)) return;
        var order = new MineOrder
        {
            X = x, Y = y,
            CyclesLeft = t == Terrain.Rock ? Bal.RockCyclesBeforeDeplete : int.MaxValue,
        };
        MineOrders.Add(order);
        _mineAt[key] = order;
    }

    /// <summary>Item a hand-mining cycle on this terrain yields (used for
    /// pawn-inventory cargo matching and mining itself).</summary>
    public ItemKind ItemOfTerrain(Terrain t) => t switch
    {
        Terrain.Rock => ItemKind.Stone,
        Terrain.IronOre => ItemKind.IronOre,
        Terrain.CopperOre => ItemKind.CopperOre,
        Terrain.Crystal => ItemKind.Crystal,
        _ => ItemKind.Biomass,
    };

    /// <summary>Drop cargo as a ground pile (merges with a pile already there).</summary>
    public void DropPile(float x, float y, ItemKind k, int n)
    {
        var ex = ItemPiles.FirstOrDefault(p => p.X == x && p.Y == y && p.Kind == k);
        if (ex != null) { ex.N += n; return; }
        ItemPiles.Add(new ItemPile { X = x, Y = y, Kind = k, N = n });
    }

    public void QueueStockpile(int x0, int y0, int x1, int y1) =>
        CmdQueue.Enqueue(new SimCmd { Type = CmdType.Stockpile, X = x0, Y = y0, I0 = x1, I1 = y1 });

    private static long PKey(int x, int y) => ((long)x << 32) ^ (uint)y;

    /// <summary>Toggle a rectangle of ground tiles into (or out of) the
    /// stockpile zone. Loaded tiles can't be unzoned.</summary>
    private void ToggleStockpile(int x0, int y0, int x1, int y1)
    {
        int ax = Math.Min(x0, x1), bx = Math.Max(x0, x1);
        int ay = Math.Min(y0, y1), by = Math.Max(y0, y1);
        if (bx - ax > 64 || by - ay > 64) return;                  // sanity
        bool anyFree = false;
        for (int x = ax; x <= bx; x++)
            for (int y = ay; y <= by; y++)
            {
                if (!World.InBounds(x, y)) continue;
                var t = World.Cell(x, y);
                if (t.B != null || t.T is Terrain.Water or Terrain.Rock) continue;
                if (!PileZones.Contains(PKey(x, y))) anyFree = true;
            }
        bool add = anyFree;                                        // toggle semantics
        for (int x = ax; x <= bx; x++)
            for (int y = ay; y <= by; y++)
            {
                if (!World.InBounds(x, y)) continue;
                long key = PKey(x, y);
                if (add)
                {
                    var t = World.Cell(x, y);
                    if (t.B != null || t.T is Terrain.Water or Terrain.Rock) continue;
                    PileZones.Add(key);
                }
                else if (!PileCells.ContainsKey(key)) PileZones.Remove(key);   // keep loaded tiles
            }
    }

    /// <summary>Nearest zone tile that can take this item (same kind or empty).</summary>
    public (int x, int y)? NearestStockpileCell(float fx, float fy, ItemKind k, float maxDist = 30f)
    {
        (int x, int y)? best = null; float bd = maxDist;
        foreach (var key in PileZones)
        {
            int x = (int)(key >> 32), y = (int)(key & 0xFFFFFFFFL);
            if (PileCells.TryGetValue(key, out var cell) && (cell.Kind != k || cell.N >= Bal.PileTileCap)) continue;
            float d = MathF.Abs(x + .5f - fx) + Mathf_Abs(y + .5f - fy);
            if (d < bd) { bd = d; best = (x, y); }
        }
        return best;
    }

    private static float Mathf_Abs(float v) => v < 0 ? -v : v;

    /// <summary>Deposit into a zone tile; false when it wouldn't fit.</summary>
    public bool DepositStockpile(int x, int y, ItemKind k, int n)
    {
        long key = PKey(x, y);
        if (!PileZones.Contains(key)) return false;
        if (PileCells.TryGetValue(key, out var cell))
        {
            if (cell.Kind != k || cell.N + n > Bal.PileTileCap) return false;
            cell.N += n;
            return true;
        }
        PileCells[key] = new ItemPile { X = x, Y = y, Kind = k, N = n };
        return true;
    }

    public void CancelMineOrder(MineOrder o)
    {
        if (o.Miner != null)
        {
            if (o.Miner.MineJob == o) o.Miner.MineJob = null;
            if (o.Miner.State is ColState.Mining or ColState.GoMine)
                o.Miner.State = ColState.Idle;
            o.Miner = null;
        }
        MineOrders.Remove(o);
        _mineAt.Remove(((long)o.X << 32) ^ (uint)o.Y);
    }

    /// <summary>Nearest walkable tile on the ring around a footprint.</summary>
    public Point? AdjacentSpot(int tx, int ty, int fw, int fh, float fx, float fy)
    {
        Point? best = null; float bd = float.MaxValue;
        for (int x = tx - 1; x <= tx + fw; x++)
            for (int y = ty - 1; y <= ty + fh; y++)
            {
                bool edge = x < tx || x >= tx + fw || y < ty || y >= ty + fh;
                if (!edge || !World.InBounds(x, y)) continue;
                var cell = World.Cell(x, y);
                if (cell.B != null || cell.T == Terrain.Water || cell.T == Terrain.Rock) continue;
                float d = MathF.Abs(x + 0.5f - fx) + MathF.Abs(y + 0.5f - fy);
                if (d < bd) { bd = d; best = new Point(x, y); }
            }
        return best;
    }
    public void QueueBulldoze(int x, int y) =>
        CmdQueue.Enqueue(new SimCmd { Type = CmdType.Bulldoze, X = x, Y = y });
    public void QueueRecipe(int x, int y, int recipe) =>
        CmdQueue.Enqueue(new SimCmd { Type = CmdType.SetRecipe, X = x, Y = y, I0 = recipe });
    public void QueueFilter(int x, int y, int item) =>
        CmdQueue.Enqueue(new SimCmd { Type = CmdType.SetFilter, X = x, Y = y, I0 = item });
    public void QueueRally(int x, int y, int rx, int ry) =>
        CmdQueue.Enqueue(new SimCmd { Type = CmdType.SetRally, X = x, Y = y, I0 = rx, I1 = ry });
    public void QueueSelectTech(Tech t) =>
        CmdQueue.Enqueue(new SimCmd { Type = CmdType.SelectTech, I0 = (int)t });
    public void QueueCapture(int x, int y) =>
        CmdQueue.Enqueue(new SimCmd { Type = CmdType.Capture, X = x, Y = y });
    public void QueueDevGive(ItemKind k, int n) =>
        CmdQueue.Enqueue(new SimCmd { Type = CmdType.DevGive, I0 = (int)k, I1 = n });
    public void QueueDevRaid() => CmdQueue.Enqueue(new SimCmd { Type = CmdType.DevRaid });
    public void QueueDevResearch() => CmdQueue.Enqueue(new SimCmd { Type = CmdType.DevResearch });
    public void QueueDevReveal() => CmdQueue.Enqueue(new SimCmd { Type = CmdType.DevReveal });
    public void QueueToggleGod() => CmdQueue.Enqueue(new SimCmd { Type = CmdType.ToggleGod });

    private void ExecuteCmds()
    {
        while (CmdQueue.TryDequeue(out var c))
        {
            CmdLog.Add(c.ToString());
            ApplyCmd(c);
        }
    }

    private void ApplyCmd(SimCmd c)
    {
        switch (c.Type)
        {
            case CmdType.Place:
                TryPlace((BuildKind)c.I0, c.X, c.Y, c.Face, out _);
                return;
            case CmdType.Mine:
                ToggleMineOrder(c.X, c.Y);
                break;
                case CmdType.Stockpile:
                    ToggleStockpile(c.X, c.Y, c.I0, c.I1);
                    break;
            case CmdType.Bulldoze:
                Bulldoze(c.X, c.Y);
                break;
            case CmdType.SetRecipe:
                if (World.InBounds(c.X, c.Y) && World.Cell(c.X, c.Y).B is Fabricator f) f.Recipe = c.I0;
                break;
            case CmdType.SetFilter:
                if (!World.InBounds(c.X, c.Y)) break;
                var fb = World.Cell(c.X, c.Y).B;
                if (fb is FilterSplitter fs) fs.Filter = (ItemKind)c.I0;
                else if (fb is Inserter ins) ins.Filter = c.I0 >= (int)ItemKind.AdvPart + 1 ? null : (ItemKind)c.I0;
                break;
            case CmdType.SetRally:
                if (World.InBounds(c.X, c.Y) && World.Cell(c.X, c.Y).B is BotFactory bf)
                { bf.RallyX = c.I0; bf.RallyY = c.I1; }
                break;
            case CmdType.SelectTech:
                SelectTech((Tech)c.I0);
                break;
            case CmdType.Capture:
            {
                Raider? best = null; float bd = 4f;
                foreach (var r in Foes)
                    if (r.Downed && !r.Captured)
                    {
                        float d = MathF.Abs(r.PosX - c.X) + MathF.Abs(r.PosY - c.Y);
                        if (d < bd) { bd = d; best = r; }
                    }
                if (best != null) StartCapture(best);
                break;
            }
            case CmdType.Trade:
                DoTrade(c.I0);
                break;
            case CmdType.DevGive:
                HubRef.Stock[c.I0] += c.I1;
                AddLog($"[DEV] +{c.I1} {Bal.ItemName((ItemKind)c.I0)}", Pal.Accent);
                break;
            case CmdType.DevRaid:
                AddLog("[DEV] Spawning raid.", Pal.Accent);
                SpawnWave();
                break;
            case CmdType.DevResearch:
                if (ActiveTech != Tech.None)
                {
                    TechProg[(int)ActiveTech] = Bal.TechCost(ActiveTech, TechLevels[(int)ActiveTech]);
                    AddResearch(0.01f);
                    AddLog($"[DEV] Finished {Bal.TechName(ActiveTech)}.", Pal.Accent);
                }
                break;
            case CmdType.DevReveal:
                World.RevealAround(HubRef.X + 1, HubRef.Y + 1, 14);
                AddLog("[DEV] Map revealed.", Pal.Accent);
                break;
            case CmdType.ToggleGod:
                GodMode = !GodMode;
                AddLog($"[DEV] God mode {(GodMode ? "ON" : "OFF")}.", Pal.Accent);
                break;
        }
    }

    /// <summary>State hash — identical sims produce identical checksums.</summary>
    public long Checksum()
    {
        unchecked
        {
            long h = 2166136261L;
            void Mix(long v) { h = (h ^ v) * 16777619L; }
            Mix((long)(Time * 1000));
            Mix((long)(Wealth * 10));
            Mix(Builds.Count);
            foreach (var b in Builds) Mix(b.X * 100003L + b.Y * 7L + (int)b.Kind);
            Mix(Cols.Count);
            foreach (var c in Cols) Mix((long)(c.PosX * 8) * 31 + (long)(c.PosY * 8) + c.Stats.Sum());
            foreach (var kv in HubRef.Stock) Mix(kv);
            Mix(Wave); Mix((long)(Threat * 100));
            Mix(Foes.Count); Mix(Trains.Count); Mix(Bots.Count); Mix(Prisoners.Count);
            Mix(Blueprints.Count);
            foreach (var bp in Blueprints) Mix(bp.X * 7L + bp.Y * 13L + (int)bp.Kind + (long)(bp.Work * 10));
            Mix(MineOrders.Count);
            foreach (var mo in MineOrders) Mix(mo.X * 17L + mo.Y * 29L + mo.CyclesLeft);
            foreach (var t in Trains) Mix((long)(t.PosX * 8) * 31 + (long)(t.PosY * 8) + t.CargoCount);
            return h;
        }
    }

    // ------------------------------------------------------------ save/load

    public GameSave ToSave(string displayName)
    {
        var s = new GameSave
        {
            GenSeed = World.P.Seed,
            BiomeSave = World.P.Biome,
            OreFreq = World.P.OreFreq,
            OreRich = World.P.OreRich,
            RockDensity = World.P.RockDensity,
            Aggression = World.P.Aggression,
            Revealed = World.RevealedKeys(),
            Time = Time,
            Weather = (int)Weather,
            WeatherT = WeatherT,
            HintsDone = HintsDone,
            Chronicle = Chronicle.Select(cl => $"{cl.Day}|{cl.Text}").ToList(),
            Chapter = Chapter, FinaleAt = FinaleAt,
            FinalePending = FinalePending, FinaleCleared = FinaleCleared,
            Hope = Hope, EndingChosen = EndingChosen,
            Blight = Blight.Select(kv => $"{kv.Key}:{kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}").ToList(),
            TraderHere = Trader != null,
            TraderX = Trader?.PosX ?? 0, TraderY = Trader?.PosY ?? 0,
            TraderArrived = Trader?.Arrived ?? false,
            TraderWindow = Trader?.WindowT ?? 0,
            TraderSold = Trader?.SoldDeals?.ToList(),
            StatKills = StatKills, StatBeastsHunted = StatBeastsHunted,
            StatBuilt = StatBuilt, StatMined = StatMined, StatMeals = StatMeals,
            WealthHist = WealthHist.ToList(),
            ActiveTech = (int)ActiveTech,
            Threat = Threat,
            NextRaidAt = NextRaidAt,
            Wave = Wave,
            Story = (int)Story,
            GodMode = GodMode,
            Meta = new SaveMeta
            {
                Name = displayName,
                SavedAtUtc = DateTime.UtcNow.ToString("o"),
                Day = Day,
                Colonists = Cols.Count,
                Wealth = (int)Wealth,
                Parts = HubRef.Stock[(int)ItemKind.AdvPart],
            },
        };
        s.Chunks = World.DirtyChunkRles().Select(kv => new ChunkDto { Key = kv.key, Rle = kv.rle }).ToList();

        // Phase 2: persist ore richness for chunks that have any initialized
        foreach (var kv in World.AllChunks)
        {
            string? ore = null;
            foreach (var t in kv.Value.Tiles)
                if (t.Ore != 0) { ore = World.OreRle(kv.Value); break; }
            if (ore != null)
            {
                var dto = s.Chunks.FirstOrDefault(c => c.Key == kv.Key);
                if (dto != null) dto.OreRle = ore;
                else s.Chunks.Add(new ChunkDto { Key = kv.Key, Rle = "", OreRle = ore });
            }
        }
        s.Pollution = new List<ChunkDto>();
        foreach (var kv in World.AllChunks)
        {
            float sum = 0;
            for (int i = 0; i < kv.Value.Pol.Length; i++) sum += kv.Value.Pol[i];
            if (sum > 0.05f)
                s.Pollution.Add(new ChunkDto { Key = kv.Key, Rle = World.PollRle(kv.Value) });
        }

        for (int i = 0; i < Bal.TechCount; i++)
        { s.TechDone[i] = TechDone[i]; s.TechProg[i] = TechProg[i]; s.TechLevels![i] = TechLevels[i]; }

        foreach (var b in Builds)
        {
            var d = new BuildingDto
            {
                Kind = (int)b.Kind,
                X = b.X, Y = b.Y, Face = (int)b.Face, Hp = b.Hp,
                Dig = b is ArkWreck a0 ? a0.Dig : 0f,
                Wear = b is MachineBase m0 ? m0.Wear : 0f,
                BrokenDown = b is MachineBase m1 && m1.BrokenDown,
            };
            switch (b)
            {
                case Hub hub: d.HubStock = (int[])hub.Stock.Clone(); break;
                case Fabricator f: d.Recipe = f.Recipe; break;
                case Turret t: d.Shots = t.Shots; break;
                case Splitter sp: d.Held = sp.Held.HasValue ? (int)sp.Held.Value : -1; break;
                case FilterSplitter fs:
                    d.Filter = (int)fs.Filter;
                    d.Held = fs.Held.HasValue ? (int)fs.Held.Value : -1;
                    break;
                case StorageCrate sc: d.Storage = sc.Items.Select(i => (int)i).ToArray(); break;
                case TrainStop st:
                    d.StationIn = st.InBuffer.Select(i => (int)i).ToArray();
                    d.StationOut = st.OutBuffer.Select(i => (int)i).ToArray();
                    break;
                case DronePort dp: d.Drones = dp.Drones; break;
                case Boiler bo: d.Fuel = bo.Fuel; break;
                case ShieldGen sg: d.ShieldHp = sg.ShieldHp; break;
                case BotFactory bf: d.RallyX = bf.RallyX; d.RallyY = bf.RallyY; d.Deployed = bf.Deployed; break;
                case Junction j:
                    d.BeltKinds = j.Main.Select(i => (int)i.Kind).ToArray();
                    d.BeltProgs = j.Main.Select(i => i.Prog).ToArray();
                    d.CrossKinds = j.Cross.Select(i => (int)i.Kind).ToArray();
                    d.CrossProgs = j.Cross.Select(i => i.Prog).ToArray();
                    break;
                case Belt belt:
                    d.BeltKinds = belt.Lane.Select(i => (int)i.Kind).ToArray();
                    d.BeltProgs = belt.Lane.Select(i => i.Prog).ToArray();
                    d.BendIn = (int?)belt.BendIn;
                    break;
                case Inserter ins:
                    d.InsFilter = (int?)ins.Filter ?? -1;
                    break;
                case StorageSilo sl:
                    d.SiloKind = (int?)sl.StoredKind ?? -1;
                    d.SiloN = sl.N;
                    break;
            }
            if (b is Lab lab) d.Reserve = lab.Reserve;
            if (b is MachineBase m)
            {
                d.InBuf = new int[Bal.ItemCount];
                foreach (var kv in m.In) d.InBuf[(int)kv.Key] = kv.Value;
                d.OutBuf = m.Out.Select(i => (int)i).ToArray();
            }
            s.Buildings.Add(d);
        }

        foreach (var c in Cols)
        {
            s.Colonists.Add(new ColonistDto
            {
                Name = c.Name,
                Traits = c.Traits.ToArray(),
                Stats = (int[])c.Stats.Clone(),
                Xp = (float[])c.Xp.Clone(),
                Passions = (int[])c.Passions.Clone(),
                Priorities = (int[])c.Priorities.Clone(),
                Hp = c.Hp,
                Hunger = c.Hunger,
                Rest_ = c.Rest,
                Morale = c.Morale,
                X = c.PosX,
                Y = c.PosY,
                JobBuildingIndex = c.Job != null ? Builds.IndexOf(c.Job) : -1,
                Stance = (int)c.Stance,
                Drafted = c.Drafted,
                Arriving = c.Arriving,
                AteWell = c.AteWellT,
                Cid = c.Cid,
                CarryKind = c.CarryKind,
                CarryN = c.CarryN,
                GriefT = c.GriefT, CatharsisT = c.CatharsisT, StressT = c.StressT,
                OpCids = c.Opinions.Keys.ToArray(),
                OpVals = c.Opinions.Values.ToArray(),
            });
        }

        foreach (var bp in Blueprints)
            s.Blueprints.Add(new BlueprintDto
            {
                Kind = (int)bp.Kind, X = bp.X, Y = bp.Y, Face = (int)bp.Face,
                W = bp.W, H = bp.H, Work = bp.Work, WorkMax = bp.WorkMax,
            });

        foreach (var mo in MineOrders)
            s.MineOrders.Add(new MineDto { X = mo.X, Y = mo.Y, Cycles = mo.CyclesLeft });

        s.ItemPiles = ItemPiles.Select(p => new PileDto { X = p.X, Y = p.Y, Kind = (int)p.Kind, N = p.N }).ToList();
        s.PileZones = PileZones.ToList();
        s.PileCells = PileCells.Select(kv => new PileCellDto { Key = kv.Key, Kind = (int)kv.Value.Kind, N = kv.Value.N }).ToList();

        foreach (var r in Foes)
            s.Raiders.Add(new RaiderDto
            {
                X = r.PosX, Y = r.PosY, Hp = r.Hp, MaxHp = r.MaxHp, Dps = r.Dps, Speed = r.Speed,
                Apex = r.Apex, Downed = r.Downed, Sapper = r.Sapper,
            });

        foreach (var b in Beasts)                     // MADDOG iter-4: wildlife
            s.Beasts.Add(new BeastDto { X = b.PosX, Y = b.PosY, Hp = b.Hp, HomeX = b.HomeX, HomeY = b.HomeY });
        foreach (var c in Carcasses)
            s.Carcasses.Add(new CarcassDto { X = c.X, Y = c.Y, Food = c.Food, T = c.T });

        var stations = Builds.OfType<TrainStop>().ToList();
        foreach (var t in Trains)
        {
            var td = new TrainDto
            {
                X = t.PosX, Y = t.PosY, Angle = t.Angle, CargoCount = t.CargoCount,
                Wait = t.Wait, Busy = t.BusyExchange,
                TargetIndex = t.Target != null ? stations.IndexOf(t.Target) : -1,
            };
            foreach (var kv in t.Cargo) td.Cargo.Add(new CargoDto { Kind = (int)kv.Key, N = kv.Value });
            s.Trains.Add(td);
        }

        foreach (var b in Bots)
            s.Bots.Add(new BotDto
            {
                X = b.X, Y = b.Y, Hp = b.Hp,
                HomeIndex = b.Home != null ? Builds.IndexOf(b.Home) : -1,
            });

        foreach (var p in Prisoners)
            s.Prisoners.Add(new PrisonerDto
            {
                Name = p.Name, X = p.X, Y = p.Y, Recruit = p.Recruit, Fed = p.Fed,
                FoodTimer = p.FoodTimer,
            });

        return s;
    }

    public void LoadFrom(GameSave s)
    {
        _stockPrimed = false;      // avoid a fake rate spike across the save boundary
        var p = s.GenSeed is long seed
            ? new GenParams
            {
                Seed = seed,
                Biome = s.BiomeSave,
                OreFreq = s.OreFreq ?? 1f,
                OreRich = s.OreRich ?? 1f,
                RockDensity = s.RockDensity ?? 1f,
                Aggression = s.Aggression ?? 1f,
            }
            : new GenParams();
        World = new World(p);
        Rand = new Random(unchecked((int)(p.Seed ^ (p.Seed >> 32)) ^ (int)(s.Time * 1000)));

        if (s.GenSeed is long)
        {
            if (s.Revealed != null)
                foreach (var k in s.Revealed) World.RevealKey(k);
            if (s.Chunks != null)
                foreach (var c in s.Chunks)
                {
                    World.ApplyChunkRle(c.Key, c.Rle);
                    if (c.OreRle != null) World.ApplyOreRle(c.Key, c.OreRle);
                }
            if (s.Pollution != null)
                foreach (var c in s.Pollution) World.ApplyPollRle(c.Key, c.Rle);
            // recompute the scalar the sim keeps hot (next pollution pass will
            // refresh it anyway, but saves shouldn't load showing 0 smog)
            TotalPollution = 0;
            foreach (var kv in World.AllChunks)
            {
                var pol = kv.Value.Pol;
                for (int i = 0; i < pol.Length; i++) TotalPollution += pol[i];
            }
        }
        else if (!string.IsNullOrEmpty(s.TerrainRle) && s.W > 0 && s.H > 0)
        {
            World.ImportRleGrid(s.W, s.H, s.TerrainRle);
        }

        Builds.Clear(); Cols.Clear(); Foes.Clear(); Trains.Clear(); Bots.Clear();
        Prisoners.Clear(); Fx.Clear(); Log.Clear(); Alerts.Clear(); History.Clear();
        TrainRes.Clear(); CmdQueue.Clear(); CmdLog.Clear();

        Time = s.Time;
        Weather = (WeatherKind)Math.Clamp(s.Weather, 0, 2);
        WeatherT = s.WeatherT > 0 ? s.WeatherT : Rand.Next(Bal.WeatherClearMinSec, Bal.WeatherClearMaxSec);
        HintsDone = s.HintsDone;
        Chapter = s.Chapter; FinaleAt = s.FinaleAt;
        FinalePending = s.FinalePending; FinaleCleared = s.FinaleCleared;
        Hope = s.Hope; EndingChosen = s.EndingChosen;
        Blight.Clear();
        if (s.Blight != null)
            foreach (var e in s.Blight)
            {
                int c = e.IndexOf(':');
                if (c > 0 && long.TryParse(e[..c], out long k) &&
                    float.TryParse(e[(c + 1)..], out float v))
                    Blight[k] = v;
            }
        Chronicle.Clear();
        if (s.Chronicle != null)
            foreach (var line in s.Chronicle)
            {
                int bar = line.IndexOf('|');
                if (bar > 0 && int.TryParse(line[..bar], out int cd))
                    Chronicle.Add((cd, line[(bar + 1)..]));
            }
        Trader = null;
        if (s.TraderHere)
        {
            Trader = new Trader { PosX = s.TraderX, PosY = s.TraderY, Arrived = s.TraderArrived, WindowT = s.TraderWindow };
            if (s.TraderSold != null)
                for (int i = 0; i < Math.Min(s.TraderSold.Count, Trader.SoldDeals.Length); i++)
                    Trader.SoldDeals[i] = s.TraderSold[i];
            if (!s.TraderArrived)
            {
                var p2 = Trader.ParkSpot(this);
                Trader.Path = World.FindPath((int)s.TraderX, (int)s.TraderY, p2.x, p2.y, enemy: false);
                Trader.PathIdx = 0;
            }
        }
        StatKills = s.StatKills; StatBeastsHunted = s.StatBeastsHunted;
        StatBuilt = s.StatBuilt; StatMined = s.StatMined; StatMeals = s.StatMeals;
        WealthHist.Clear();
        if (s.WealthHist != null) WealthHist.AddRange(s.WealthHist);
        _lastDay = Day;
        ActiveTech = (Tech)s.ActiveTech;
        Threat = s.Threat;
        NextRaidAt = s.NextRaidAt;
        Wave = s.Wave;
        Story = (Storyteller)(s.Story ?? 0);
        GodMode = s.GodMode;
        Won = Lost = ContinueAfterWin = false;
        RecentCombat = 0; RaidBannerT = 0; DebugWealthBonus = 0; TotalPollution = 0;
        for (int i = 0; i < Bal.TechCount; i++)
        {
            TechDone[i] = s.TechDone != null && i < s.TechDone.Length && s.TechDone[i];
            TechProg[i] = s.TechProg != null && i < s.TechProg.Length ? s.TechProg[i] : 0;
            TechLevels[i] = s.TechLevels != null && i < s.TechLevels.Length ? s.TechLevels[i] : 0;
        }

        foreach (var d in s.Buildings)
        {
            var kind = (BuildKind)d.Kind;
            Building b = kind == BuildKind.Hub
                ? new Hub()
                : Building.Create(kind, d.X, d.Y, (Dir)d.Face);
            b.X = d.X; b.Y = d.Y; b.Face = (Dir)d.Face; b.Hp = d.Hp;
            if (b is ArkWreck aw) { aw.Dig = d.Dig; }
            if (b is MachineBase mb) { mb.Wear = d.Wear; mb.BrokenDown = d.BrokenDown; }
            switch (b)
            {
                case Hub hub:
                    HubRef = hub;
                    if (d.HubStock != null) Array.Copy(d.HubStock, hub.Stock, Math.Min(hub.Stock.Length, d.HubStock.Length));
                    break;
                case Fabricator f: f.Recipe = d.Recipe; break;
                case Turret t: t.Shots = d.Shots; break;
                case Splitter sp: sp.Held = d.Held >= 0 ? (ItemKind)d.Held : null; break;
                case FilterSplitter fs:
                    fs.Filter = (ItemKind)d.Filter;
                    fs.Held = d.Held >= 0 ? (ItemKind)d.Held : null;
                    break;
                case StorageCrate sc:
                    if (d.Storage != null) foreach (var i in d.Storage) sc.Items.Add((ItemKind)i);
                    break;
                case TrainStop st:
                    if (d.StationIn != null) foreach (var i in d.StationIn) st.InBuffer.Add((ItemKind)i);
                    if (d.StationOut != null) foreach (var i in d.StationOut) st.OutBuffer.Add((ItemKind)i);
                    break;
                case DronePort dp: dp.Drones = d.Drones; break;
                case Boiler bo: bo.Fuel = d.Fuel; break;
                case ShieldGen sg: sg.ShieldHp = d.ShieldHp; break;
                case BotFactory bf:
                    if (d.RallyX != 0 || d.RallyY != 0) { bf.RallyX = d.RallyX; bf.RallyY = d.RallyY; }
                    bf.Deployed = d.Deployed;
                    break;
                case Junction j:
                    if (d.BeltKinds != null)
                        for (int i = 0; i < d.BeltKinds.Length; i++)
                            j.Main.Add(new BeltItem((ItemKind)d.BeltKinds[i], d.BeltProgs![i]));
                    if (d.CrossKinds != null)
                        for (int i = 0; i < d.CrossKinds.Length; i++)
                            j.Cross.Add(new BeltItem((ItemKind)d.CrossKinds[i], d.CrossProgs![i]));
                    break;
                case Belt belt:
                    if (d.BeltKinds != null)
                        for (int i = 0; i < d.BeltKinds.Length; i++)
                            belt.Lane.Add(new BeltItem((ItemKind)d.BeltKinds[i], d.BeltProgs![i]));
                    if (d.BendIn is int bendIn) belt.BendIn = (Dir)bendIn;
                    break;
                case Inserter ins:
                    if (d.InsFilter >= 0) ins.Filter = (ItemKind)d.InsFilter;
                    break;
                case StorageSilo sl:
                    if (d.SiloKind >= 0) sl.StoredKind = (ItemKind)d.SiloKind;
                    sl.N = d.SiloN;
                    break;
            }
            if (b is Lab lab) lab.Reserve = d.Reserve;
            if (b is MachineBase m)
            {
                if (d.InBuf != null)
                    for (int i = 0; i < Bal.ItemCount && i < d.InBuf.Length; i++)
                        if (d.InBuf[i] > 0) m.In[(ItemKind)i] = d.InBuf[i];
                if (d.OutBuf != null)
                    foreach (var i in d.OutBuf) m.Out.Add((ItemKind)i);
            }
            Builds.Add(b);
            if (b.Kind == BuildKind.ElevatedRail) World.SetBuilding2(b);
            else World.SetBuilding(b);
        }

        foreach (var d in s.Colonists)
        {
            var c = new Colonist(d.X, d.Y, Rand) { Name = d.Name, Hp = d.Hp, Hunger = d.Hunger, Rest = d.Rest_, Morale = d.Morale };
            c.Traits.Clear();
            c.Traits.AddRange(d.Traits);
            Array.Copy(d.Stats, c.Stats, 6);
            if (d.Xp != null) Array.Copy(d.Xp, c.Xp, 6);
            if (d.Passions != null) Array.Copy(d.Passions, c.Passions, 2);
            if (d.Priorities != null) Array.Copy(d.Priorities, c.Priorities, Math.Min(Bal.WorkCount, d.Priorities.Length));
            if (d.Stance is 0 or 1 or 2) c.Stance = (Stance)d.Stance;
            c.Drafted = d.Drafted;
            c.Arriving = d.Arriving;
            c.AteWellT = d.AteWell;
            c.Cid = d.Cid > 0 ? d.Cid : _nextCid++;
            c.CarryKind = d.CarryKind; c.CarryN = d.CarryN;
            if (c.Cid >= _nextCid) _nextCid = c.Cid + 1;
            c.GriefT = d.GriefT; c.CatharsisT = d.CatharsisT; c.StressT = d.StressT;
            if (d.OpCids != null && d.OpVals != null)
                for (int i = 0; i < Math.Min(d.OpCids.Length, d.OpVals.Length); i++)
                    c.Opinions[d.OpCids[i]] = d.OpVals[i];
            if (c.Arriving) c.Path = World.FindPath((int)c.PosX, (int)c.PosY, (int)HubRef.CenterTile.X, (int)HubRef.CenterTile.Y + 3, enemy: false);
            c.DeriveCosmetics();
            if (d.JobBuildingIndex >= 0 && d.JobBuildingIndex < Builds.Count &&
                Builds[d.JobBuildingIndex] is MachineBase m)
            {
                c.Job = m;
                m.Operator = c;
                c.State = ColState.Idle;
            }
            Cols.Add(c);
        }

        Blueprints.Clear(); _bpAt.Clear();
        foreach (var d in s.Blueprints)
        {
            var bp = new Blueprint
            {
                Kind = (BuildKind)d.Kind, X = d.X, Y = d.Y, Face = (Dir)d.Face,
                W = Math.Max(1, d.W), H = Math.Max(1, d.H),
                Work = d.Work, WorkMax = d.WorkMax <= 0 ? 1 : d.WorkMax,
            };
            if (bp.W != Bal.FootW(bp.Kind) || bp.H != Bal.FootH(bp.Kind))
            { bp.W = Bal.FootW(bp.Kind); bp.H = Bal.FootH(bp.Kind); }
            Blueprints.Add(bp);
            for (int dx = 0; dx < bp.W; dx++)
                for (int dy = 0; dy < bp.H; dy++)
                    _bpAt[((long)(bp.X + dx) << 32) ^ (uint)(bp.Y + dy)] = bp;
        }

        MineOrders.Clear(); _mineAt.Clear();
        foreach (var d in s.MineOrders)
        {
            var mo = new MineOrder { X = d.X, Y = d.Y, CyclesLeft = d.Cycles };
            MineOrders.Add(mo);
            _mineAt[((long)mo.X << 32) ^ (uint)mo.Y] = mo;
        }

        ItemPiles.Clear(); PileZones.Clear(); PileCells.Clear();
        if (s.ItemPiles != null)
            foreach (var pd in s.ItemPiles)
                ItemPiles.Add(new ItemPile { X = pd.X, Y = pd.Y, Kind = (ItemKind)pd.Kind, N = pd.N });
        if (s.PileZones != null)
            foreach (var z in s.PileZones) PileZones.Add(z);
        if (s.PileCells != null)
            foreach (var pc in s.PileCells)
                PileCells[pc.Key] = new ItemPile { Kind = (ItemKind)pc.Kind, N = pc.N };

        foreach (var d in s.Raiders)
        {
            var r = new Raider(d.X, d.Y, d.Apex, 0) { Dps = d.Dps, Speed = d.Speed, Downed = d.Downed, Sapper = d.Sapper };
            if (d.MaxHp > 1) r.MaxHp = d.MaxHp;
            r.Hp = Math.Min(d.Hp, r.MaxHp);
            Foes.Add(r);
        }

        Beasts.Clear(); Carcasses.Clear();            // MADDOG iter-4: wildlife
        foreach (var d in s.Beasts)
        {
            var b = new Beast(d.X, d.Y) { HomeX = d.HomeX, HomeY = d.HomeY };
            b.Hp = Math.Min(d.Hp, b.MaxHp);
            Beasts.Add(b);
        }
        foreach (var d in s.Carcasses)
            Carcasses.Add(new Carcass(d.X, d.Y, d.Food) { T = d.T > 0 ? d.T : 300f });

        var stations = Builds.OfType<TrainStop>().ToList();
        foreach (var d in s.Trains)
        {
            var t = new Train
            {
                PosX = d.X, PosY = d.Y, Angle = d.Angle, CargoCount = d.CargoCount,
                Wait = d.Wait, BusyExchange = d.Busy,
                Target = d.TargetIndex >= 0 && d.TargetIndex < stations.Count ? stations[d.TargetIndex] : null,
            };
            foreach (var kv in d.Cargo) t.Cargo[(ItemKind)kv.Kind] = kv.N;
            Trains.Add(t);
        }

        foreach (var d in s.Bots)
        {
            var home = d.HomeIndex >= 0 && d.HomeIndex < Builds.Count ? Builds[d.HomeIndex] as BotFactory : null;
            if (home == null) continue;
            Bots.Add(new GuardBot { X = d.X, Y = d.Y, Hp = d.Hp, Home = home });
        }

        foreach (var d in s.Prisoners)
        {
            Prisoners.Add(new Prisoner(d.X, d.Y, Rand)
            {
                Name = d.Name, Recruit = d.Recruit, Fed = d.Fed, FoodTimer = d.FoodTimer,
            });
        }

        Grids.Clear(); _gridOf.Clear(); PowerDirty = true;
        Fluids.Clear(); _fluidOf.Clear(); FluidDirty = true;
        ComputeWealth();
        AddLog($"Loaded '{s.Meta.Name}' — Day {s.Meta.Day}.", Pal.Accent);
    }
}
