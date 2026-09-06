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
    public readonly List<Train> Trains = new();
    public readonly List<GuardBot> Bots = new();
    public readonly List<Prisoner> Prisoners = new();
    public readonly List<Particle> Fx = new();
    public readonly List<LogLine> Log = new();
    public readonly List<AlertLine> Alerts = new();
    public Hub HubRef = null!;

    /// <summary>Seeded RNG — ALL simulation randomness flows from here so the
    /// game is deterministic (multiplayer/replay groundwork).</summary>
    public Random Rand = new();

    public float Time;
    public int Day => (int)(Time / Bal.DayLengthSec) + 1;
    public float DayFrac01 => (Time % Bal.DayLengthSec) / Bal.DayLengthSec;
    public float Sunlight => Bal.Sunlight(DayFrac01);
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
        var par = p ?? new GenParams();
        if (seed.HasValue) par.Seed = seed.Value;
        World = new World(par.Clone());
        Rand = new Random(unchecked((int)(par.Seed ^ (par.Seed >> 32))));
        Story = story;

        Builds.Clear(); Cols.Clear(); Foes.Clear(); Trains.Clear(); Bots.Clear();
        Prisoners.Clear(); Fx.Clear(); Log.Clear(); Alerts.Clear();
        CmdQueue.Clear(); CmdLog.Clear(); TrainRes.Clear();
        Time = 0; Wave = 0; Threat = 0; NextRaidAt = Bal.FirstRaidAt;
        RecentCombat = 0; RaidBannerT = 0; Wealth = 0; DebugWealthBonus = 0;
        TotalPollution = 0; _randThreatMul = 1f; _eventT = 100f;
        Won = Lost = ContinueAfterWin = GodMode = false;
        for (int i = 0; i < Bal.TechCount; i++) { TechDone[i] = false; TechProg[i] = 0; TechLevels[i] = 0; }
        ActiveTech = Tech.Automation;
        Grids.Clear(); _gridOf.Clear(); PowerDirty = true;
        Fluids.Clear(); _fluidOf.Clear(); _boilerIn.Clear(); _boilerOut.Clear(); FluidDirty = true;

        var hub = new Hub { X = 0, Y = 0 };
        HubRef = hub;
        Builds.Add(hub);
        World.SetBuilding(hub);

        StampStart(World);

        hub.Stock[(int)ItemKind.IronPlate] = Bal.StartIronPlates;
        hub.Stock[(int)ItemKind.CopperPlate] = Bal.StartCopperPlates;
        hub.Stock[(int)ItemKind.Food] = Bal.StartFood;

        PlaceFree(new Hab(), 1, -2);

        for (int i = 0; i < 6; i++)
            Cols.Add(new Colonist(1.5f + Rand.Next(-2, 3), 4.5f + Rand.Next(-1, 2), Rand));

        World.RevealAround(1, 1, 2);

        AddLog("The ark is gone. Six survivors. One hub.", Pal.Accent);
        AddLog($"Goal: deliver {Bal.PartsToWin} Advanced Parts to the Hub.", Pal.Accent);
        AddLog($"Storyteller: {StoryName()}. [T] research · [P] work · F1-F4 views.", Pal.TextDim);
    }

    public static void StampStart(World w)
    {
        w.SetTerrainCircle(1, 1, 7.5f, Terrain.Ground);
        w.SetTerrainCircle(-11, 5, 3.6f, Terrain.IronOre);
        w.SetTerrainCircle(11, -2, 3.0f, Terrain.CopperOre);
        w.SetTerrainCircle(-3, 10, 3.4f, Terrain.Flora);
        w.SetTerrainCircle(13, -11, 2.4f, Terrain.Crystal);
        w.SetTerrainCircle(12, 6, 3.6f, Terrain.Water);     // lake for the steam loop
    }

    public string StoryName() => Story switch
    {
        Storyteller.Random => "Randy the Random",
        Storyteller.Merciless => "The Merciless",
        _ => "Basil the Builder",
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
        HubRef.Stock[(int)ItemKind.CopperPlate] >= Bal.CostCopper(k);

    private void PayMaterials(BuildKind k)
    {
        HubRef.Stock[(int)ItemKind.IronPlate] -= Bal.CostIron(k);
        HubRef.Stock[(int)ItemKind.CopperPlate] -= Bal.CostCopper(k);
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

        if (kind is BuildKind.Drill or BuildKind.DeepDrill)
        {
            if (tile.T is not (Terrain.IronOre or Terrain.CopperOre or Terrain.Crystal or Terrain.Flora))
            { reason = "Drills must sit on iron, copper, crystal or glowing flora"; return false; }
        }
        else if (tile.T == Terrain.Rock) { reason = "Solid rock"; return false; }

        if (kind == BuildKind.Locomotive)
        {
            if (!World.IsRailAt(x, y)) { reason = "Place locomotives on rail or a station"; return false; }
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
        return true;
    }

    public void Bulldoze(int x, int y)
    {
        if (!World.InBounds(x, y)) return;
        var tile = World.Cell(x, y);
        var b = tile.B;
        bool elevated = false;
        if (b == null && tile.B2 != null) { b = tile.B2; elevated = true; }
        if (b == null || b is Hub) return;

        int fe = Bal.CostIron(b.Kind) / 2, cu = Bal.CostCopper(b.Kind) / 2;
        HubRef.Stock[(int)ItemKind.IronPlate] += fe;
        HubRef.Stock[(int)ItemKind.CopperPlate] += cu;

        if (b is BotFactory f)
            foreach (var bot in Bots.Where(bt => bt.Home == f).ToList()) KillBot(bot);

        AddLog($"{Bal.Name(b.Kind)} scrapped (+{fe} Fe / +{cu} Cu plates).", Pal.TextDim);
        DestroyBuilding(b, silent: true, elevated);
    }

    public void DestroyBuilding(Building b, bool silent, bool elevated = false)
    {
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
        Colonist? best = null; int bp = int.MaxValue; float bd = float.MaxValue;
        foreach (var c in Cols)
        {
            if (c.Hp <= 0 || c.Job != null) continue;
            int p = c.Priorities[(int)wt];
            if (p >= 4) continue;                       // 4 = never
            float d = Math.Abs(c.PosX - (m.X + .5f)) + Math.Abs(c.PosY - (m.Y + .5f));
            if (p < bp || (p == bp && d < bd)) { bp = p; bd = d; best = c; }
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
        var c = HubRef.CenterTile;
        var col = new Colonist(c.X + Rand.Next(-1, 2), c.Y + 3, Rand);
        Cols.Add(col);
        AddLog($"{p.Name} has joined the colony!", Pal.Good);
        SpawnSpark(c.X, c.Y + 3, Pal.Good);
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
        float wire2 = Bal.PoleWire * Bal.PoleWire;
        for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
            {
                var a = nodes[i]; var b = nodes[j];
                float dx = a.X + a.W / 2f - (b.X + b.W / 2f);
                float dy = a.Y + a.H / 2f - (b.Y + b.H / 2f);
                if (dx * dx + dy * dy <= wire2)
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
                float cover = n is Hub ? Bal.HubCover : Bal.PoleCover;
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
                if (c.Tiles[i].T == Terrain.Flora && c.Pol[i] > 0)
                    c.Pol[i] = Math.Max(0, c.Pol[i] - Bal.PollFloraAbsorb * dt);
        }
    }

    // ---------------------------------------------------------------  loop

    public void Update(float dt)
    {
        if (Lost || (Won && !ContinueAfterWin)) return;

        ExecuteCmds();

        Time += dt;
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
        foreach (var c in Cols.ToArray()) c.Update(this, dt);
        Cols.RemoveAll(c => c.Hp <= 0);
        foreach (var r in Foes.ToArray()) r.Update(this, dt);
        Foes.RemoveAll(r => r.Hp <= 0);

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
    }

    // ---------------------------------------------------------------- raids

    private void UpdateRaids(float dt)
    {
        if (Wealth < Bal.GraceWealth)
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
        Threat += Wealth * Bal.ThreatPerWealth * World.P.Aggression * mul * dt;
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
        int placed = 0;
        for (int i = 0; i < n; i++)
        {
            int x = 0, y = 0;
            bool ok = false;
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
            Foes.Add(new Raider(x + .5f, y + .5f, apex && i == 0, hpBonus));
        }
        if (placed > 0) { RaidPingX = sx / placed + .5f; RaidPingY = sy / placed + .5f; }
        RaidBannerT = 5.5f;

        AddLog(apex ? $"RAID {Wave}: something huge approaches..."
                    : $"{(mini ? "Skirmish" : $"RAID {Wave}")}: {placed} hostiles drawn by your industry!", Pal.Bad);
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

        if (roll < 0.55 || Story == Storyteller.Builder)
        {
            if (Cols.Count < 12)
            {
                var c = HubRef.CenterTile;
                Cols.Add(new Colonist(c.X + Rand.Next(-1, 2), c.Y + 3, Rand));
                AddLog("A wanderer arrives and asks to join the colony.", Pal.Good);
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

    public void AddLog(string text, Color col)
    {
        Log.Insert(0, new LogLine(text, col));
        if (Log.Count > 6) Log.RemoveAt(Log.Count - 1);
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
                break;
            case CmdType.Bulldoze:
                Bulldoze(c.X, c.Y);
                break;
            case CmdType.SetRecipe:
                if (World.InBounds(c.X, c.Y) && World.Cell(c.X, c.Y).B is Fabricator f) f.Recipe = c.I0;
                break;
            case CmdType.SetFilter:
                if (World.InBounds(c.X, c.Y) && World.Cell(c.X, c.Y).B is FilterSplitter fs) fs.Filter = (ItemKind)c.I0;
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
            OreFreq = World.P.OreFreq,
            OreRich = World.P.OreRich,
            RockDensity = World.P.RockDensity,
            Aggression = World.P.Aggression,
            Revealed = World.RevealedKeys(),
            Time = Time,
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
            });
        }

        foreach (var r in Foes)
            s.Raiders.Add(new RaiderDto
            {
                X = r.PosX, Y = r.PosY, Hp = r.Hp, MaxHp = r.MaxHp, Dps = r.Dps, Speed = r.Speed,
                Apex = r.Apex, Downed = r.Downed,
            });

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
        var p = s.GenSeed is long seed
            ? new GenParams
            {
                Seed = seed,
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
                foreach (var c in s.Chunks) World.ApplyChunkRle(c.Key, c.Rle);
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
        Prisoners.Clear(); Fx.Clear(); Log.Clear(); Alerts.Clear();
        TrainRes.Clear(); CmdQueue.Clear(); CmdLog.Clear();

        Time = s.Time;
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
                case Belt belt when d.BeltKinds != null:
                    for (int i = 0; i < d.BeltKinds.Length; i++)
                        belt.Lane.Add(new BeltItem((ItemKind)d.BeltKinds[i], d.BeltProgs![i]));
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
            if (d.Priorities != null) Array.Copy(d.Priorities, c.Priorities, Bal.WorkCount);
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

        foreach (var d in s.Raiders)
        {
            var r = new Raider(d.X, d.Y, d.Apex, 0) { Dps = d.Dps, Speed = d.Speed, Downed = d.Downed };
            if (d.MaxHp > 1) r.MaxHp = d.MaxHp;
            r.Hp = Math.Min(d.Hp, r.MaxHp);
            Foes.Add(r);
        }

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
