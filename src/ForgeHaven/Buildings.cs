using System.Drawing;

namespace ForgeHaven;

// ---------------------------------------------------------------------------
//  Buildings: belts, routing, rails, inserters, machines, fluids, defenses,
//  drone ports, shield generators, war-bot factories, the hub...
// ---------------------------------------------------------------------------

public abstract class Building
{
    public BuildKind Kind;
    public int X, Y, W = 1, H = 1;

    /// <summary>Power wiring/coverage radii (substations override).</summary>
    public virtual float WireRange => Bal.PoleWire;
    public virtual float CoverRange => Bal.PoleCover;

    /// <summary>Rotate a PLACED building 90° (counter-clockwise with ccw).
    /// Curves keep their bend; only square footprints may rotate.</summary>
    public void Rotate(bool ccw = false)
    {
        Face = ccw ? DirU.TurnLeft(Face) : DirU.TurnRight(Face);
        if (this is Belt cb && cb.BendIn is Dir bin)
            cb.BendIn = ccw ? DirU.TurnLeft(bin) : DirU.TurnRight(bin);
    }
    public Dir Face = Dir.Right;
    public float Hp, MaxHp = 100;
    public Colonist? Repairer;        // MADDOG iter-2: who is patching this up

    public virtual bool Industrial =>
        Kind is BuildKind.Drill or BuildKind.DeepDrill or BuildKind.Smelter or BuildKind.Fabricator
            or BuildKind.FabT2 or BuildKind.FabT3 or BuildKind.BioProcessor or BuildKind.Reactor
            or BuildKind.Boiler or BuildKind.Turret or BuildKind.HeavyTurret or BuildKind.Lab;

    public virtual int Comfort => Kind switch
    {
        BuildKind.Garden => 6,
        BuildKind.MessTable => 8,
        BuildKind.Lamp => 3,
        _ => 0,
    };

    public int PowerDemand => Bal.PowerDemand(Kind);

    public virtual void Update(Game g, float dt) { }

    public virtual bool AcceptItem(Game g, ItemKind k) => false;
    public virtual bool AcceptItem(Game g, ItemKind k, Dir fromDir) => AcceptItem(g, k);

    public PointF CenterPxWorld => new(X + W / 2f, Y + H / 2f);

    public static Building Create(BuildKind kind, int x, int y, Dir face)
    {
        Building b = kind switch
        {
            BuildKind.Belt => new Belt(),
            BuildKind.FastBelt => new FastBelt(),
            BuildKind.Splitter => new Splitter(),
            BuildKind.Junction => new Junction(),
            BuildKind.OverflowRouter => new OverflowRouter(),
            BuildKind.Merger => new Merger(),
            BuildKind.FilterSplitter => new FilterSplitter(),
            BuildKind.Inserter => new Inserter(),
            BuildKind.LongInserter => new LongInserter(),
            BuildKind.BlastDrill => new BlastDrill(),
            BuildKind.IndustrialFurnace => new IndustrialFurnace(),
            BuildKind.StorageSilo => new StorageSilo(),
            BuildKind.Assembler => new Assembler(),
            BuildKind.Greenhouse => new Greenhouse(),
            BuildKind.Substation => new Substation(),
            BuildKind.Rail => new Rail(),
            BuildKind.ElevatedRail => new ElevatedRail(),
            BuildKind.TrainStop => new TrainStop(),
            BuildKind.DronePort => new DronePort(),
            BuildKind.Drill => new Drill(),
            BuildKind.DeepDrill => new DeepDrill(),
            BuildKind.Smelter => new Smelter(),
            BuildKind.PrimitiveFurnace => new PrimitiveFurnace(),
            BuildKind.CropPlot => new CropPlot(),
            BuildKind.Fabricator => new Fabricator(),
            BuildKind.FabT2 => new FabT2(),
            BuildKind.FabT3 => new FabT3(),
            BuildKind.BioProcessor => new BioProcessor(),
            BuildKind.StorageCrate => new StorageCrate(),
            BuildKind.Reactor => new Reactor(),
            BuildKind.SolarPanel => new SolarPanel(),
            BuildKind.WindTurbine => new WindTurbine(),
            BuildKind.Battery => new Battery(),
            BuildKind.PowerPole => new PowerPole(),
            BuildKind.Pipe => new Pipe(),
            BuildKind.Pump => new Pump(),
            BuildKind.Tank => new Tank(),
            BuildKind.Boiler => new Boiler(),
            BuildKind.SteamEngine => new SteamEngine(),
            BuildKind.Wall => new Wall(),
            BuildKind.Door => new Door(),
            BuildKind.ArkWreck => new ArkWreck(),
            BuildKind.Turret => new Turret(),
            BuildKind.HeavyTurret => new HeavyTurret(),
            BuildKind.Watchtower => new Watchtower(),
            BuildKind.SpikeTrap => new SpikeTrap(),
            BuildKind.IED => new IED(),
            BuildKind.ShieldGen => new ShieldGen(),
            BuildKind.BotFactory => new BotFactory(),
            BuildKind.Hab => new Hab(),
            BuildKind.MessTable => new MessTable(),
            BuildKind.Lamp => new Lamp(),
            BuildKind.Garden => new Garden(),
            BuildKind.MedBed => new MedBed(),
            BuildKind.Lab => new Lab(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        b.Kind = kind; b.X = x; b.Y = y; b.Face = face;
        b.Hp = b.MaxHp = kind switch
        {
            BuildKind.Wall => 220,
            BuildKind.Door => 170,
            BuildKind.Turret => 140,
            BuildKind.HeavyTurret => 220,
            BuildKind.Reactor => 160,
            BuildKind.Boiler or BuildKind.SteamEngine => 150,
            BuildKind.ShieldGen => 180,
            BuildKind.BotFactory => 170,
            BuildKind.Watchtower => 130,
            _ => 100,
        };
        return b;
    }
}

// ------------------------------------------------------------------  Belt --

public struct BeltItem
{
    public ItemKind Kind;
    public float Prog;
    public Dir? Entry;      // side the item entered from (perpendicular = slides in)
    public BeltItem(ItemKind k, float p) { Kind = k; Prog = p; Entry = null; }
    public BeltItem(ItemKind k, float p, Dir? entry) { Kind = k; Prog = p; Entry = entry; }
}

public static class LaneSim
{
    public static bool CanAccept(List<BeltItem> lane) =>
        lane.Count < 4 && (lane.Count == 0 || lane[^1].Prog >= Bal.BeltGap);

    public static void Advance(List<BeltItem> lane, float spd, float dt)
    {
        for (int i = 0; i < lane.Count; i++)
        {
            var it = lane[i];
            float cap = i == 0 ? 1f : lane[i - 1].Prog - Bal.BeltGap;
            float np = it.Prog + dt * spd;
            if (np > cap) np = cap;
            if (np < it.Prog) np = it.Prog;
            it.Prog = np;
            lane[i] = it;
        }
    }
}

public class Belt : Building
{
    public readonly List<BeltItem> Lane = new();
    public virtual float SpeedMul => 1f;

    /// <summary>Curved belt: side the flow enters from (null = straight).
    /// Face stays the outlet; items ride inlet->center->outlet. Set by
    /// drag-turning a belt line, kept purely as path/render info.</summary>
    public Dir? BendIn;

    public bool TryReceive(ItemKind k, Dir? entry = null)
    {
        if (!LaneSim.CanAccept(Lane)) return false;
        Lane.Add(new BeltItem(k, 0f, entry));
        return true;
    }

    public override bool AcceptItem(Game g, ItemKind k) => TryReceive(k);

    public override bool AcceptItem(Game g, ItemKind k, Dir fromDir)
    {
        // MINDUSTRY IO: conveyors define whether an adjacent machine side is
        // input or output. A belt accepts from behind or from either side,
        // but rejects items dumped into its FRONT (that would flow straight
        // back into the source block and clog/loop). Side deliveries remember
        // where they came from so the item visibly slides in.
        if (fromDir == DirU.Opposite(Face)) return false;
        bool side = fromDir != Face;
        return TryReceive(k, side ? DirU.Opposite(fromDir) : null);
    }

    public override void Update(Game g, float dt)
    {
        DeriveBend(g);
        float spd = g.BeltSpeed * SpeedMul;
        LaneSim.Advance(Lane, spd, dt);

        if (Lane.Count > 0 && Lane[0].Prog >= 1f - 0.0001f)
        {
            var (tx, ty) = DirU.Step(X, Y, Face);
            if (!g.World.InBounds(tx, ty)) return;
            var b = g.World.Cell(tx, ty).B;
            if (b == null || b == this) return;
            if (b.AcceptItem(g, Lane[0].Kind, Face))
                Lane.RemoveAt(0);
        }
    }

    public const int SideLeftMask = 1;
    public const int SideRightMask = 2;

    public static int SideMaskFor(Dir face, Dir side)
    {
        int f = (int)face, s = (int)side;
        int cross = DirU.Dx[f] * DirU.Dy[s] - DirU.Dy[f] * DirU.Dx[s];
        return cross < 0 ? SideLeftMask : SideRightMask;
    }

    /// <summary>Which perpendicular conveyor feeds touch this belt: bit 1 =
    /// left of travel, bit 2 = right of travel. Used for the straight merge
    /// variants: left-only, right-only, or both sides.</summary>
    public int SideInputMask(Game g)
    {
        int f = (int)Face, mask = 0;
        for (int s = 0; s < 4; s++)
        {
            if (s == f || s == (f + 2) % 4) continue;
            int nx = X + DirU.Dx[s], ny = Y + DirU.Dy[s];
            if (!g.World.InBounds(nx, ny)) continue;
            if (g.World.Cell(nx, ny).B is Belt ob && (int)ob.Face == (s + 2) % 4)
                mask |= SideMaskFor(Face, (Dir)s);
        }
        return mask;
    }

    /// <summary>MINDUSTRY AUTO-CURVE: a belt with exactly ONE perpendicular
    /// feeder (and no aligned back feeder) curves from the feeder into its
    /// facing — sprite AND item path. Back+side or two-side inputs stay
    /// straight (the merge overlay shows the join). Re-derived every tick,
    /// so rotating/extending lines updates the curves on its own.</summary>
    public void DeriveBend(Game g)
    {
        int f = (int)Face;
        Dir? bend = null; int perp = 0;
        for (int s = 0; s < 4; s++)
        {
            if (s == f || s == (f + 2) % 4) continue;
            int nx = X + DirU.Dx[s], ny = Y + DirU.Dy[s];
            if (!g.World.InBounds(nx, ny)) continue;
            if (g.World.Cell(nx, ny).B is Belt ob && (int)ob.Face == (s + 2) % 4) { bend = (Dir)s; perp++; }
        }
        int bx = X + DirU.Dx[(f + 2) % 4], by = Y + DirU.Dy[(f + 2) % 4];
        bool backFeed = g.World.InBounds(bx, by) && g.World.Cell(bx, by).B is Belt bb && (int)bb.Face == f;
        BendIn = perp == 1 && !backFeed ? bend : null;
    }
}

public sealed class FastBelt : Belt
{
    public override float SpeedMul => Bal.FastBeltMul;
}

// --------------------------------------------------------------  Splitter --

public sealed class Splitter : Building
{
    public ItemKind? Held;
    private int _rotate;

    public override bool AcceptItem(Game g, ItemKind k)
    {
        if (Held != null) return false;
        Held = k;
        return true;
    }

    public override void Update(Game g, float dt)
    {
        if (Held == null) return;
        for (int i = 0; i < 3; i++)
        {
            int idx = (_rotate + i) % 3;
            Dir d = idx switch
            {
                0 => Face,
                1 => DirU.TurnRight(Face),
                _ => DirU.TurnLeft(Face),
            };
            var (tx, ty) = DirU.Step(X, Y, d);
            if (!g.World.InBounds(tx, ty)) continue;
            var b = g.World.Cell(tx, ty).B;
            if (b == null || b == this) continue;
            if (b.AcceptItem(g, Held.Value, d))
            {
                Held = null;
                _rotate = (idx + 1) % 3;
                return;
            }
        }
    }
}

/// <summary>Filtered routing: matching items exit RIGHT, the rest go straight
/// (left only as backup). Select the building to cycle the filter.</summary>
public sealed class FilterSplitter : Building
{
    public ItemKind Filter = ItemKind.IronOre;
    public ItemKind? Held;

    public override bool AcceptItem(Game g, ItemKind k)
    {
        if (Held != null) return false;
        Held = k;
        return true;
    }

    public override void Update(Game g, float dt)
    {
        if (Held == null) return;
        bool match = Held == Filter;
        Dir first = match ? DirU.TurnRight(Face) : Face;
        Dir second = match ? Face : DirU.TurnLeft(Face);
        foreach (var d in new[] { first, second })
        {
            var (tx, ty) = DirU.Step(X, Y, d);
            if (!g.World.InBounds(tx, ty)) continue;
            var b = g.World.Cell(tx, ty).B;
            if (b == null || b == this) continue;
            if (b.AcceptItem(g, Held.Value, d))
            {
                Held = null;
                return;
            }
        }
    }
}

// --------------------------------------------------------------  Junction --

public sealed class Junction : Building
{
    public readonly List<BeltItem> Main = new();
    public readonly List<BeltItem> Cross = new();

    public Dir CrossDir => DirU.TurnRight(Face);

    public override bool AcceptItem(Game g, ItemKind k, Dir fromDir)
    {
        var lane = fromDir == Face ? Main : fromDir == CrossDir ? Cross : null;
        if (lane == null || !LaneSim.CanAccept(lane)) return false;
        lane.Add(new BeltItem(k, 0f));
        return true;
    }

    public override void Update(Game g, float dt)
    {
        float spd = g.BeltSpeed;
        LaneSim.Advance(Main, spd, dt);
        LaneSim.Advance(Cross, spd, dt);
        TransferFront(g, Main, Face);
        TransferFront(g, Cross, CrossDir);
    }

    private void TransferFront(Game g, List<BeltItem> lane, Dir d)
    {
        if (lane.Count == 0 || lane[0].Prog < 1f - 0.0001f) return;
        var (tx, ty) = DirU.Step(X, Y, d);
        if (!g.World.InBounds(tx, ty)) return;
        var b = g.World.Cell(tx, ty).B;
        if (b == null || b == this) return;
        if (b.AcceptItem(g, lane[0].Kind, d))
            lane.RemoveAt(0);
    }
}

// --------------------------------------------------------  OverflowRouter --

public sealed class OverflowRouter : Building
{
    public ItemKind? Held;
    private int _side;

    public override bool AcceptItem(Game g, ItemKind k)
    {
        if (Held != null) return false;
        Held = k;
        return true;
    }

    public override void Update(Game g, float dt)
    {
        if (Held == null) return;

        var (fx, fy) = DirU.Step(X, Y, Face);
        if (g.World.InBounds(fx, fy))
        {
            var fb = g.World.Cell(fx, fy).B;
            if (fb != null && fb != this && fb.AcceptItem(g, Held.Value, Face))
            {
                Held = null;
                return;
            }
        }

        for (int i = 0; i < 2; i++)
        {
            int idx = (_side + i) & 1;
            Dir d = idx == 0 ? DirU.TurnRight(Face) : DirU.TurnLeft(Face);
            var (tx, ty) = DirU.Step(X, Y, d);
            if (!g.World.InBounds(tx, ty)) continue;
            var b = g.World.Cell(tx, ty).B;
            if (b == null || b == this) continue;
            if (b.AcceptItem(g, Held.Value, d))
            {
                Held = null;
                _side = 1 - idx;
                return;
            }
        }
    }
}

// ----------------------------------------------------------------  Merger --

/// <summary>Actively PULLS items from machines/crates behind, left and right,
/// and pushes them out of its facing side (onto a belt usually).</summary>
public sealed class Merger : Building
{
    public ItemKind? Held;
    private float _pullCd;

    public override void Update(Game g, float dt)
    {
        // push held item forward
        if (Held != null)
        {
            var (fx, fy) = DirU.Step(X, Y, Face);
            if (g.World.InBounds(fx, fy))
            {
                var fb = g.World.Cell(fx, fy).B;
                if (fb != null && fb != this && fb.AcceptItem(g, Held.Value, Face))
                {
                    Held = null;
                    _pullCd = 0.1f;
                }
            }
            return;
        }

        _pullCd -= dt;
        if (_pullCd > 0) return;
        _pullCd = 0.4f;

        foreach (var d in new[] { DirU.Opposite(Face), DirU.TurnLeft(Face), DirU.TurnRight(Face) })
        {
            var (sx, sy) = DirU.Step(X, Y, d);
            if (!g.World.InBounds(sx, sy)) continue;
            var src = g.World.Cell(sx, sy).B;
            ItemKind? taken = null;
            switch (src)
            {
                case MachineBase m when m.Out.Count > 0:
                    taken = m.Out[0]; m.Out.RemoveAt(0); break;
                case StorageCrate c when c.Items.Count > 0:
                    taken = c.Items[0]; c.Items.RemoveAt(0); break;
                case TrainStop st when st.OutBuffer.Count > 0:
                    taken = st.OutBuffer[0]; st.OutBuffer.RemoveAt(0); break;
            }
            if (taken != null) { Held = taken; return; }
        }
    }
}

// --------------------------------------------------------------  Inserter --

/// <summary>Swinging arm: lifts from the tile behind it onto the tile ahead.</summary>
/// <summary>INSERTER REWORK: a real arm with phases instead of a teleport.
/// Idle -> swing to source -> grab -> swing to sink -> drop. The claw
/// visibly carries the item across the arc; throughput = one item per
/// cycle (slower than direct machine->belt output, by design).
/// Optional per-inserter filter (cycle on select, like the filter splitter).</summary>
public class Inserter : Building
{
    public ItemKind? Held;           // item riding in the claw
    public ItemKind? Filter;         // null = accept everything
    public float Arm;                // 0 = over source, 1 = over sink
    public int Phase;                // 0 idle, 1 to-source, 2 grab, 3 to-sink, 4 drop-retry
    public float T;                  // phase progress (seconds)
    private float _retry;

    public virtual int Reach => 1;                       // tiles to the source
    public virtual float Cycle => Bal.InserterCycle;     // full swing, seconds

    private (int x, int y) Source(Game g)
    {
        var d = DirU.Opposite(Face);
        return (X + DirU.Dx[(int)d] * Reach, Y + DirU.Dy[(int)d] * Reach);
    }
    private (int x, int y) Sink(Game g) => DirU.Step(X, Y, Face);

    private bool Wanted(ItemKind k) => Filter == null || Filter == k;

    private bool TryGrab(Game g)
    {
        var (sx, sy) = Source(g);
        if (!g.World.InBounds(sx, sy)) return false;
        var src = g.World.Cell(sx, sy).B;
        switch (src)
        {
            case Belt b when b.Lane.Count > 0 && b.Lane[0].Prog > 0.35f && Wanted(b.Lane[0].Kind):
                Held = b.Lane[0].Kind; b.Lane.RemoveAt(0); return true;
            case MachineBase m when m.Out.Count > 0 && Wanted(m.Out[0]):
                Held = m.Out[0]; m.Out.RemoveAt(0); return true;
            case StorageCrate c when c.Items.Count > 0 && Wanted(c.Items[0]):
                Held = c.Items[0]; c.Items.RemoveAt(0); return true;
            case TrainStop st when st.OutBuffer.Count > 0 && Wanted(st.OutBuffer[0]):
                Held = st.OutBuffer[0]; st.OutBuffer.RemoveAt(0); return true;
            case StorageSilo s when s.N > 0 && Wanted(s.StoredKind!.Value):
                Held = s.StoredKind; s.N--; if (s.N == 0) s.StoredKind = null; return true;
        }
        return false;
    }

    private bool TryDrop(Game g)
    {
        if (Held == null) return true;
        var (tx, ty) = Sink(g);
        if (!g.World.InBounds(tx, ty)) return false;
        var tgt = g.World.Cell(tx, ty).B;
        return tgt != null && tgt != this && tgt.AcceptItem(g, Held.Value, Face);
    }

    public override void Update(Game g, float dt)
    {
        float half = Cycle * 0.5f;
        switch (Phase)
        {
            case 0: // resting over the SOURCE: grab the moment something is there
                _retry -= dt;
                if (_retry > 0) break;
                _retry = 0.25f;
                if (TryGrab(g)) { Phase = 3; T = 0; }      // item rides the claw across
                break;

            case 1: // swinging back to the source, empty-handed
                T += dt;
                Arm = MathF.Max(0f, 1f - T / half);
                if (T >= half) { Phase = 0; T = 0; }
                break;

            case 3: // swinging to the SINK, carrying
                T += dt;
                Arm = MathF.Min(1f, T / half);
                if (T >= half) { Phase = 4; T = 0; _retry = 0; }
                break;

            case 4: // over the sink: drop (retry while blocked, arm stays extended)
                _retry -= dt;
                if (_retry > 0) break;
                _retry = 0.3f;
                if (TryDrop(g)) { Held = null; Phase = 1; T = 0; }
                break;
        }
    }
}

/// <summary>Long-armed inserter: grabs from 2 tiles away (middle lane),
/// slightly slower swing.</summary>
public sealed class LongInserter : Inserter
{
    public override int Reach => 2;
    public override float Cycle => Bal.LongInserterCycle;
}

// ------------------------------------------------------------  Rail stuff --

public sealed class Rail : Building { }
public sealed class ElevatedRail : Building { }

/// <summary>Train station: belts load items in; trains unload; a slow ejector
/// pushes the unloaded cargo onto the belt on the facing side.</summary>
public sealed class TrainStop : Building
{
    public readonly List<ItemKind> InBuffer = new();    // belts -> train
    public readonly List<ItemKind> OutBuffer = new();   // train -> belts
    public string StationName = "Station";
    private float _eject;

    public override bool AcceptItem(Game g, ItemKind k)
    {
        if (InBuffer.Count >= Bal.StationCap) return false;
        InBuffer.Add(k);
        return true;
    }

    public override void Update(Game g, float dt)
    {
        if (OutBuffer.Count == 0) return;
        _eject -= dt;
        if (_eject > 0) return;
        _eject = 0.35f;
        var (tx, ty) = DirU.Step(X, Y, Face);
        if (!g.World.InBounds(tx, ty)) return;
        var b = g.World.Cell(tx, ty).B;
        if (b != null && b.AcceptItem(g, OutBuffer[0], Face))
            OutBuffer.RemoveAt(0);
    }
}

// ------------------------------------------------------------  DronePort --

/// <summary>Logistic + repair bot coverage. Belt Logistic Drone items in to
/// grow the fleet; drones then ferry crate->machine items and patch damage.</summary>
public sealed class DronePort : Building
{
    public int Drones;
    private float _deliverT, _repairT;

    public bool Covers(float x, float y) =>
        MathF.Abs(x - (X + .5f)) <= Bal.DronePortCover && MathF.Abs(y - (Y + .5f)) <= Bal.DronePortCover;

    public override bool AcceptItem(Game g, ItemKind k)
    {
        if (k != ItemKind.Drone) return false;
        Drones++;
        return true;
    }

    public override void Update(Game g, float dt)
    {
        if (Drones <= 0) return;

        _deliverT -= dt;
        if (_deliverT <= 0)
        {
            _deliverT = Bal.DroneDeliveryEvery;
            TryDeliver(g);
        }

        _repairT -= dt;
        if (_repairT <= 0)
        {
            _repairT = Bal.DroneRepairEvery;
            TryRepair(g);
        }
    }

    private void ForEachCovered(Game g, Action<Building> act)
    {
        foreach (var b in g.Builds)
            if (b != this && Covers(b.X + .5f, b.Y + .5f))
                act(b);
    }

    private void TryDeliver(Game g)
    {
        StorageCrate? provider = null;
        ItemKind pick = default;
        ForEachCovered(g, b =>
        {
            if (provider != null || b is not StorageCrate c || c.Items.Count == 0) return;
            provider = c; pick = c.Items[0];
        });
        if (provider == null) return;

        MachineBase? needy = null;
        ForEachCovered(g, b =>
        {
            if (needy != null || b is not MachineBase m || m.In.Count > 0) return;
            needy = m;
        });
        if (needy == null) return;

        var crate = provider!;
        crate.Items.RemoveAt(0);
        if (needy!.AcceptItem(g, pick))
        {
            g.DroneFlight(crate.X + .5f, crate.Y + .5f, needy.X + .5f, needy.Y + .5f);
            g.BuildingTouched(this);
        }
        else crate.Items.Insert(0, pick); // put it back
    }

    private void TryRepair(Game g)
    {
        Building? hurt = null;
        ForEachCovered(g, b => { if (hurt == null && b.Hp < b.MaxHp - 1f) hurt = b; });
        if (hurt == null) return;

        // need an iron plate from a covered crate (or the hub stock as fallback)
        StorageCrate? plateCrate = null;
        ForEachCovered(g, b => { if (plateCrate == null && b is StorageCrate c && c.Items.Contains(ItemKind.IronPlate)) plateCrate = c; });
        if (plateCrate == null)
        {
            if (g.HubRef.Stock[(int)ItemKind.IronPlate] <= 0) return;
            g.HubRef.Stock[(int)ItemKind.IronPlate]--;
        }
        else plateCrate!.Items.Remove(ItemKind.IronPlate);

        hurt!.Hp = Math.Min(hurt.MaxHp, hurt.Hp + Bal.RepairPerPlate);
        g.DroneFlight(X + .5f, Y + .5f, hurt.X + .5f, hurt.Y + .5f);
        g.SpawnSpark(hurt.X + .5f, hurt.Y + .5f, Pal.Good);
    }
}

// ---------------------------------------------------------------  Machine --

public abstract class MachineBase : Building
{
    public readonly Dictionary<ItemKind, int> In = new();
    public readonly List<ItemKind> Out = new();
    public float Prog;
    public bool Busy;
    public Colonist? Operator;
    public float Wear;                          // ORGANISM: 0..100 use fatigue
    public bool BrokenDown;                     // ORGANISM: stopped until repaired
    private int _dumpCursor;                    // MINDUSTRY IO: round-robin adjacent outputs

    public virtual float CraftTime => 3f;
    public virtual bool NeedsWorker => true;

    public abstract bool HasInputs();
    public abstract void ConsumeInputs();
    public abstract ItemKind OutputOf();

    /// <summary>HAULING: items a hauler should resupply when the input
    /// buffer runs low (simple machines only - recipe crafters manage
    /// their own input UI).</summary>
    public virtual ItemKind[] HaulNeeds() => Array.Empty<ItemKind>();

    protected int InCount(ItemKind k) => In.TryGetValue(k, out var v) ? v : 0;
    protected int InTotal()
    {
        int s = 0; foreach (var kv in In) s += kv.Value; return s;
    }

    protected bool AcceptIntoBuffer(ItemKind k)
    {
        if (InTotal() >= Bal.InBufCap) return false;
        In[k] = InCount(k) + 1;
        return true;
    }

    /// <summary>Called when a craft cycle finishes (after the output is
    /// buffered). Drills use it to deplete the ore tile under them.</summary>
    protected virtual void OnCraftComplete(Game g) { }

    public virtual float SpeedFactor(Game g)
    {
        if (BrokenDown) return 0f;                     // ORGANISM: dead until repaired
        float f = g.PowerFracFor(this);
        if (Operator != null) f *= Operator.WorkSpeedFor(Kind);
        else if (NeedsWorker && !g.Has(Tech.Automation)) return 0f;
        if (g.Has(Tech.Overclock)) f *= 1.25f;
        f *= 1f - MathF.Min(Wear, 90f) * 0.003f;        // worn machines run slow
        return f;
    }

    public override void Update(Game g, float dt)
    {
        TryPushOut(g);
        float f = SpeedFactor(g);
        if (f <= 0.001f) return;

        if (Busy)
        {
            Prog += dt * f;
            if (Prog >= CraftTime)
            {
                Busy = false; Prog = 0;
                if (Out.Count < Bal.OutBufCap) Out.Add(OutputOf());
                // ORGANISM: every cycle ages the machine
                Wear = MathF.Min(100f, Wear + Bal.WearPerCraft);
                if (Wear >= 100f && !BrokenDown)
                {
                    BrokenDown = true;
                    g.AddLog($"{Bal.Name(Kind)} has broken down - it needs a repairer.", Pal.Bad);
                }
                OnCraftComplete(g);
            }
        }
        else if (Out.Count < Bal.OutBufCap && HasInputs())
        {
            ConsumeInputs();
            Busy = true;
        }
    }

    protected void TryPushOut(Game g)
    {
        if (Out.Count == 0) return;

        // MINDUSTRY IO: production/mining blocks have no fixed item port.
        // Every tile touching the footprint can be input OR output; the
        // adjacent transport/building decides whether it can accept the item.
        // Valid outputs are attempted round-robin so two belts touching the
        // same machine split production instead of one side starving.
        var targets = new List<(Building b, Dir fromDir)>();
        var seen = new HashSet<Building>();

        void AddTarget(int tx, int ty, Dir fromDir)
        {
            if (!g.World.InBounds(tx, ty)) return;
            var b = g.World.Cell(tx, ty).B;
            if (b == null || b == this || !seen.Add(b)) return;
            targets.Add((b, fromDir));
        }

        for (int x = X; x < X + W; x++) AddTarget(x, Y - 1, Dir.Up);
        for (int y = Y; y < Y + H; y++) AddTarget(X + W, y, Dir.Right);
        for (int x = X; x < X + W; x++) AddTarget(x, Y + H, Dir.Down);
        for (int y = Y; y < Y + H; y++) AddTarget(X - 1, y, Dir.Left);

        if (targets.Count == 0) return;
        if (_dumpCursor >= targets.Count) _dumpCursor %= targets.Count;

        int start = _dumpCursor;
        for (int i = 0; i < targets.Count; i++)
        {
            int idx = (start + i) % targets.Count;
            var (b, fromDir) = targets[idx];
            if (b.AcceptItem(g, Out[0], fromDir))
            {
                Out.RemoveAt(0);
                _dumpCursor = (idx + 1) % targets.Count;
                return;
            }
        }
    }

    public float Craft01 => CraftTime <= 0 ? 0 : Prog / CraftTime;

    /// <summary>ORGANISM: one-glance machine health for the production view.</summary>
    public string StatusText(Game g)
    {
        if (BrokenDown) return "BROKEN DOWN";
        if (Wear >= 70f) return $"WORN { (int)Wear }%";
        if (NeedsWorker && Operator == null && !g.Has(Tech.Automation)) return "UNSTAFFED";
        if (PowerDemand > 0 && g.PowerFracFor(this) < 0.2f) return "NO POWER";
        if (Busy) return "RUNNING";
        if (Out.Count >= Bal.OutBufCap) return "OUTPUT FULL";
        return HasInputs() ? "READY" : "STARVED";
    }
}

// -------------------------------------------------------  ore production --

public class Drill : MachineBase
{
    public Drill() { W = 2; H = 2; }            // multiblock: covers 2x2 tiles

    /// <summary>Resource mined: first ore tile under the 2x2 footprint.</summary>
    public virtual ItemKind Resource(Game g)
    {
        for (int dx = 0; dx < W; dx++)
            for (int dy = 0; dy < H; dy++)
            {
                if (!g.World.InBounds(X + dx, Y + dy)) continue;
                var t = g.World.Cell(X + dx, Y + dy).T;
                if (t == Terrain.IronOre) return ItemKind.IronOre;
                if (t == Terrain.CopperOre) return ItemKind.CopperOre;
                if (t == Terrain.Crystal) return ItemKind.Crystal;
                if (t == Terrain.Flora) return ItemKind.Biomass;   // trees are hand-felled collectibles
            }
        return ItemKind.IronOre;
    }

    public override float CraftTime => Bal.DrillTime;
    public override bool HasInputs() => true;
    public override void ConsumeInputs() { }

    public ItemKind PendingOutput = ItemKind.IronOre;
    public override ItemKind OutputOf() => PendingOutput;

    public override void Update(Game g, float dt)
    {
        PendingOutput = Resource(g);
        base.Update(g, dt);
    }

    protected override void OnCraftComplete(Game g)
    {
        // Phase 2: deposits are finite — every output eats one unit from a
        // matching tile under the footprint (deep drills: the 4x4 area).
        int x0 = this is DeepDrill ? X - 1 : X, x1 = this is DeepDrill ? X + W : X + W - 1;
        int y0 = this is DeepDrill ? Y - 1 : Y, y1 = this is DeepDrill ? Y + H : Y + H - 1;
        var want = PendingOutput;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            {
                if (!g.World.InBounds(x, y)) continue;
                var t = g.World.Cell(x, y).T;
                var yields = t switch
                {
                    Terrain.IronOre => ItemKind.IronOre,
                    Terrain.CopperOre => ItemKind.CopperOre,
                    Terrain.Crystal => ItemKind.Crystal,
                    Terrain.Flora => ItemKind.Biomass,
                    _ => (ItemKind)(-1),
                };
                if (yields == want && g.World.OreAt(x, y) > 0)
                {
                    g.World.DepleteOre(x, y);
                    return;
                }
            }
    }

    /// <summary>Mining Productivity research: +10% drill speed per level.</summary>
    public override float SpeedFactor(Game g)
    {
        float f = base.SpeedFactor(g);
        return f * (1f + 0.10f * g.TechLevel(Tech.MiningProd));
    }

    public override bool AcceptItem(Game g, ItemKind k) => false;
}

// -------------------------------------------------  multiblock machines ---

/// <summary>BLAST DRILL (3x3): Mindustry-style graduation machine. Triple
/// throughput, big power draw, mines the whole 3x3 footprint.</summary>
public sealed class BlastDrill : Drill
{
    public BlastDrill() { W = 3; H = 3; }
    public override float CraftTime => Bal.DrillTime * 0.38f;
}

/// <summary>INDUSTRIAL FURNACE (2x3): a fast, hungry smelter - the mid-game
/// plate bottleneck breaker.</summary>
public sealed class IndustrialFurnace : MachineBase
{
    private ItemKind _pendingOut = ItemKind.IronPlate;

    public IndustrialFurnace() { W = 2; H = 3; }

    public override float CraftTime => Bal.SmeltTime * 0.5f;
    public override bool HasInputs() =>
        InCount(ItemKind.IronOre) >= 1 || InCount(ItemKind.CopperOre) >= 1;

    public override void ConsumeInputs()
    {
        if (InCount(ItemKind.IronOre) >= 1) { In[ItemKind.IronOre]--; _pendingOut = ItemKind.IronPlate; }
        else { In[ItemKind.CopperOre]--; _pendingOut = ItemKind.CopperPlate; }
    }

    public override ItemKind OutputOf() => _pendingOut;
    public override ItemKind[] HaulNeeds() => new[] { ItemKind.IronOre, ItemKind.CopperOre };

    public override bool AcceptItem(Game g, ItemKind k) =>
        (k == ItemKind.IronOre || k == ItemKind.CopperOre) && AcceptIntoBuffer(k);
}

/// <summary>STORAGE SILO (2x2): 300 units of ONE item type. Belts/inserters
/// in, inserters + haulers out. The machine-food pantry.</summary>
public sealed class StorageSilo : Building
{
    public StorageSilo() { W = 2; H = 2; }
    public ItemKind? StoredKind;
    public int N;

    public override bool AcceptItem(Game g, ItemKind k)
    {
        if (StoredKind == null) StoredKind = k;
        if (k != StoredKind || N >= Bal.SiloCap) return false;
        N++;
        return true;
    }

    /// <summary>Pull one unit out (inserters + haulers).</summary>
    public bool TakeOne(out ItemKind k)
    {
        k = StoredKind ?? ItemKind.IronOre;
        if (N <= 0) return false;
        N--;
        if (N == 0) StoredKind = null;
        return true;
    }
}

/// <summary>ASSEMBLER (3x3): T2 crafter - Gears + Circuits into Advanced
/// Parts at scale. Worker + power.</summary>
public sealed class Assembler : MachineBase
{
    public Assembler() { W = 3; H = 3; }

    public override float CraftTime => 6f;
    public override bool HasInputs() =>
        InCount(ItemKind.Gear) >= 1 && InCount(ItemKind.Circuit) >= 1;

    public override void ConsumeInputs()
    {
        In[ItemKind.Gear]--; In[ItemKind.Circuit]--;
    }

    public override ItemKind OutputOf() => ItemKind.AdvPart;
    public override ItemKind[] HaulNeeds() => new[] { ItemKind.Gear, ItemKind.Circuit };

    public override bool AcceptItem(Game g, ItemKind k) =>
        (k == ItemKind.Gear || k == ItemKind.Circuit) && AcceptIntoBuffer(k);
}

/// <summary>GREENHOUSE (3x3): 1 Biomass seed + power grows into 4 Biomass -
/// renewable "wood", closes the loop with felled trees.</summary>
public sealed class Greenhouse : MachineBase
{
    public Greenhouse() { W = 3; H = 3; }

    public override float CraftTime => 12f;
    public override bool HasInputs() => InCount(ItemKind.Biomass) >= 1;
    public override void ConsumeInputs() => In[ItemKind.Biomass]--;
    public override ItemKind OutputOf() => ItemKind.Biomass;

    protected override void OnCraftComplete(Game g)
    {
        // the seed returns three extra sprouts
        for (int i = 0; i < 3 && Out.Count < Bal.OutBufCap; i++) Out.Add(ItemKind.Biomass);
    }

    public override ItemKind[] HaulNeeds() => new[] { ItemKind.Biomass };

    public override bool AcceptItem(Game g, ItemKind k) =>
        k == ItemKind.Biomass && AcceptIntoBuffer(k);
}

public sealed class DeepDrill : Drill
{
    public override float CraftTime => Bal.DeepDrillTime;

    /// <summary>Deep drills mine a 4x4 AREA around their 2x2 footprint —
    /// they do not need ore directly under every foot tile.</summary>
    public override ItemKind Resource(Game g)
    {
        for (int dx = -1; dx <= W; dx++)
            for (int dy = -1; dy <= H; dy++)
            {
                if (!g.World.InBounds(X + dx, Y + dy)) continue;
                var t = g.World.Cell(X + dx, Y + dy).T;
                if (t == Terrain.IronOre) return ItemKind.IronOre;
                if (t == Terrain.CopperOre) return ItemKind.CopperOre;
                if (t == Terrain.Crystal) return ItemKind.Crystal;
                if (t is (Terrain.Flora or Terrain.Tree)) return ItemKind.Biomass;
            }
        return ItemKind.IronOre;
    }
}

public sealed class Smelter : MachineBase
{
    private ItemKind _pendingOut = ItemKind.IronPlate;

    protected override void OnCraftComplete(Game g)
    {
        // ORGANISM: every smelt leaves slag - ignore it and the line backs up
        if (Out.Count < Bal.OutBufCap) Out.Add(ItemKind.Slag);
    }

    public override float CraftTime => Bal.SmeltTime;
    public override bool HasInputs() =>
        InCount(ItemKind.IronOre) >= 1 || InCount(ItemKind.CopperOre) >= 1;

    public override void ConsumeInputs()
    {
        if (InCount(ItemKind.IronOre) >= 1) { In[ItemKind.IronOre]--; _pendingOut = ItemKind.IronPlate; }
        else { In[ItemKind.CopperOre]--; _pendingOut = ItemKind.CopperPlate; }
    }

    public override ItemKind OutputOf() => _pendingOut;
    public override ItemKind[] HaulNeeds() => new[] { ItemKind.IronOre, ItemKind.CopperOre };

    public override bool AcceptItem(Game g, ItemKind k) =>
        (k == ItemKind.IronOre || k == ItemKind.CopperOre) && AcceptIntoBuffer(k);
}

/// <summary>Phase 1 anti-softlock: stone firebox, hand-smelts ore into
/// plates with a worker and NO power. Slower than a powered Smelter.</summary>
public sealed class PrimitiveFurnace : MachineBase
{
    private ItemKind _pendingOut = ItemKind.IronPlate;

    public override float CraftTime => Bal.PrimitiveSmeltTime;
    public override bool HasInputs() =>
        InCount(ItemKind.IronOre) >= 1 || InCount(ItemKind.CopperOre) >= 1;

    public override void ConsumeInputs()
    {
        if (InCount(ItemKind.IronOre) >= 1) { In[ItemKind.IronOre]--; _pendingOut = ItemKind.IronPlate; }
        else { In[ItemKind.CopperOre]--; _pendingOut = ItemKind.CopperPlate; }
    }

    public override ItemKind OutputOf() => _pendingOut;

    public override bool AcceptItem(Game g, ItemKind k) =>
        (k == ItemKind.IronOre || k == ItemKind.CopperOre) && AcceptIntoBuffer(k);
}

public class Fabricator : MachineBase
{
    /// <summary>0 Ammo, 1 AdvPart, 2 SciencePack, 3 Drone, 4 WarBot.</summary>
    public int Recipe;

    public virtual float CraftMul => 1f;

    public override float CraftTime => Recipe switch
    {
        1 => Bal.PartTime,
        2 => Bal.SciTime,
        3 => Bal.DroneTime,
        4 => Bal.WarBotTime,
        5 => Bal.SlagTime,          // ORGANISM: slag recycle
        6 => Bal.GearTime,
        7 => Bal.CircuitTime,
        _ => Bal.AmmoTime,
    } * CraftMul;

    public override bool HasInputs() => Recipe switch
    {
        1 => InCount(ItemKind.Gear) >= 2 && InCount(ItemKind.Circuit) >= 1,      // ORGANISM: deep chain
        2 => InCount(ItemKind.IronPlate) >= 1 && InCount(ItemKind.CopperPlate) >= 1,
        3 => InCount(ItemKind.IronPlate) >= 1 && InCount(ItemKind.CopperPlate) >= 1,
        4 => InCount(ItemKind.IronPlate) >= 2 && InCount(ItemKind.CopperPlate) >= 1,
        5 => InCount(ItemKind.Slag) >= 3,
        6 => InCount(ItemKind.IronPlate) >= 2,
        7 => InCount(ItemKind.CopperPlate) >= 1 && InCount(ItemKind.Crystal) >= 1,
        _ => InCount(ItemKind.IronPlate) >= 1,
    };

    public override void ConsumeInputs()
    {
        switch (Recipe)
        {
            case 1: In[ItemKind.Gear] -= 2; In[ItemKind.Circuit] -= 1; break;
            case 2: In[ItemKind.IronPlate]--; In[ItemKind.CopperPlate]--; break;
            case 3: In[ItemKind.IronPlate]--; In[ItemKind.CopperPlate]--; break;
            case 4: In[ItemKind.IronPlate] -= 2; In[ItemKind.CopperPlate]--; break;
            case 5: In[ItemKind.Slag] -= 3; break;
            case 6: In[ItemKind.IronPlate] -= 2; break;
            case 7: In[ItemKind.CopperPlate]--; In[ItemKind.Crystal] -= 1; break;
            default: In[ItemKind.IronPlate]--; break;
        }
    }

    public override ItemKind OutputOf() => Recipe switch
    {
        1 => ItemKind.AdvPart,
        2 => ItemKind.SciencePack,
        3 => ItemKind.Drone,
        4 => ItemKind.WarBot,
        5 => ItemKind.Stone,
        6 => ItemKind.Gear,
        7 => ItemKind.Circuit,
        _ => ItemKind.Ammo,
    };

    protected override void OnCraftComplete(Game g)
    {
        // ORGANISM: slag recycling yields a second stone
        if (Recipe == 5 && Out.Count < Bal.OutBufCap) Out.Add(ItemKind.Stone);
    }

    public override bool AcceptItem(Game g, ItemKind k) =>
        (k == ItemKind.IronPlate
        || (Recipe == 1 && (k == ItemKind.Gear || k == ItemKind.Circuit))
        || (Recipe is 2 or 3 or 4 && k == ItemKind.CopperPlate)
        || (Recipe == 5 && k == ItemKind.Slag)
        || (Recipe == 7 && (k == ItemKind.CopperPlate || k == ItemKind.Crystal)))
        && AcceptIntoBuffer(k);

    public string RecipeName => Recipe switch
    {
        1 => "Adv. Part",
        2 => "Science Pack",
        3 => "Drone",
        4 => "War Bot",
        5 => "Stone (recycled)",
        6 => "Gear",
        7 => "Circuit",
        _ => "Ammo Crate",
    };
}

public sealed class FabT2 : Fabricator { public override float CraftMul => 0.5f; }
public sealed class FabT3 : Fabricator { public override float CraftMul => 0.25f; }

public sealed class BioProcessor : MachineBase
{
    public override float CraftTime => Bal.BioTime;
    public override bool HasInputs() => InCount(ItemKind.Biomass) >= 1;
    public override void ConsumeInputs() => In[ItemKind.Biomass]--;
    public override ItemKind OutputOf() => ItemKind.Food;
    public override bool AcceptItem(Game g, ItemKind k) => k == ItemKind.Biomass && AcceptIntoBuffer(k);
}

/// <summary>Phase 2 farming: a farmer grows Food from piped water and
/// sunlight. No biomass input, no power — the colony's food backbone.</summary>
public sealed class CropPlot : MachineBase
{
    public override float CraftTime => Bal.CropTime;
    public override bool NeedsWorker => true;

    /// <summary>Water network touching the plot (any of the 4 neighbors).</summary>
    private PipeNet? WaterNet(Game g)
    {
        for (int d = 0; d < 4; d++)
        {
            var (nx, ny) = DirU.Step(X, Y, (Dir)d);
            if (!g.World.InBounds(nx, ny)) continue;
            if (g.World.Cell(nx, ny).B is Building nb && nb is Pipe or Tank or Pump or SteamEngine or Boiler)
            {
                var net = g.FluidOf(nb);
                if (net?.Kind == FluidKind.Water) return net;
            }
        }
        return null;
    }

    public override void Update(Game g, float dt)
    {
        TryPushOut(g);
        float f = SpeedFactor(g);
        if (f <= 0.001f) return;

        if (Busy)
        {
            Prog += dt * f;
            if (Prog >= CraftTime)
            {
                Busy = false; Prog = 0;
                for (int i = 0; i < Bal.CropFoodPerCycle; i++)
                    if (Out.Count < Bal.OutBufCap) Out.Add(ItemKind.Food);
                OnCraftComplete(g);
            }
        }
        else if (Out.Count < Bal.OutBufCap)
        {
            var net = WaterNet(g);
            if (net != null && net.Amount >= Bal.CropWaterPerCycle)
            {
                net.Amount -= Bal.CropWaterPerCycle;
                Busy = true;
            }
        }
    }

    /// <summary>Crops drink the sun and the rain: growth follows daylight,
    /// with a boost while it rains (MADDOG iter-3).</summary>
    public override float SpeedFactor(Game g)
    {
        if (g.TileBlighted(X, Y)) return 0f;                 // nothing grows in the blight
        float f = (g.Sunlight * 0.6f + 0.4f) * g.CropMul;   // 0.4 at night .. 1.0 at noon
        if (Operator != null) f *= Operator.WorkSpeedFor(Kind);
        else if (!g.Has(Tech.Automation)) return 0f;
        return f;
    }

    public override bool HasInputs() => false;     // water is a fluid, not a belt item
    public override void ConsumeInputs() { }
    public override ItemKind OutputOf() => ItemKind.Food;
    public override bool AcceptItem(Game g, ItemKind k) => false;
}

/// <summary>Buffers items; slowly re-emits them onto its output belt.</summary>
public sealed class StorageCrate : Building
{
    public readonly List<ItemKind> Items = new();
    private float _eject;

    public override bool AcceptItem(Game g, ItemKind k)
    {
        if (Items.Count >= Bal.StorageCap) return false;
        Items.Add(k);
        return true;
    }

    public override void Update(Game g, float dt)
    {
        if (Items.Count == 0) return;
        _eject -= dt;
        if (_eject > 0) return;
        _eject = Bal.StorageEjectEvery;
        var (tx, ty) = DirU.Step(X, Y, Face);
        if (!g.World.InBounds(tx, ty)) return;
        var b = g.World.Cell(tx, ty).B;
        if (b != null && b.AcceptItem(g, Items[0], Face))
            Items.RemoveAt(0);
    }
}

/// <summary>Research lab — always manned, burns belt-fed Science Packs.</summary>
public sealed class Lab : MachineBase
{
    public override bool NeedsWorker => true;
    public float Reserve;

    public override bool AcceptItem(Game g, ItemKind k) =>
        k == ItemKind.SciencePack && AcceptIntoBuffer(k);

    public override bool HasInputs() => InCount(ItemKind.SciencePack) >= 1;
    public override void ConsumeInputs() { }
    public override ItemKind OutputOf() => ItemKind.SciencePack;

    public override void Update(Game g, float dt)
    {
        if (g.ActiveTech == Tech.None || g.Has(g.ActiveTech)) return;
        if (Operator == null) return;

        float f = g.PowerFracFor(this) * Operator.WorkSpeedFor(Kind);
        if (f <= 0.01f) return;

        if (Reserve <= 0.01f && InCount(ItemKind.SciencePack) > 0)
        {
            In[ItemKind.SciencePack]--;
            Reserve += Bal.PackPoints;
        }

        if (Reserve > 0)
        {
            float add = Math.Min(Bal.LabRate * f * dt, Reserve);
            g.AddResearch(add);
            Reserve -= add;
        }
    }
}

// --------------------------------------------------------------  fluids ---

public sealed class Pipe : Building { }

/// <summary>Must be placed ON water; feeds its pipe network 20 water/s.</summary>
public sealed class Pump : Building { }

public sealed class Tank : Building { }

/// <summary>Burns belt-fed Biomass; the fluid tick moves water from networks
/// touching its back/left/right into the network on its FACE side as steam.</summary>
public sealed class Boiler : Building
{
    public float Fuel;   // buffered biomass (in craft-input terms)

    public override bool AcceptItem(Game g, ItemKind k)
    {
        if (k != ItemKind.Biomass || Fuel >= 10f) return false;
        Fuel += 1f;
        return true;
    }
}

/// <summary>Steam consumer; CurrentOutput is set by the fluid tick each step
/// (up to +90 power while steam lasts).</summary>
public sealed class SteamEngine : Building
{
    public float CurrentOutput;
}

// ----------------------------------------------------------------  combat --

public class Turret : Building
{
    public int Shots;
    public float Cd;
    public Raider? Target;
    public float LungeT, LungeDx, LungeDy;
    private bool _warnedEmpty;

    public virtual int MagCap => Bal.TurretMagCap;
    public virtual float CooldownTime => Bal.TurretCd;
    public virtual float Damage(Game g) => g.Has(Tech.Optics) ? Bal.TurretDmgUp : Bal.TurretDmg;
    public virtual float Range(Game g) => g.Has(Tech.Optics) ? Bal.TurretRangeUp : Bal.TurretRange;

    public override bool AcceptItem(Game g, ItemKind k)
    {
        if (k != ItemKind.Ammo || Shots > MagCap - Bal.TurretShotsPerCrate) return false;
        Shots += Bal.TurretShotsPerCrate;
        _warnedEmpty = false;
        return true;
    }

    public override void Update(Game g, float dt)
    {
        Cd -= dt;
        LungeT = Math.Max(0, LungeT - dt);
        float range = Range(g);

        if (Target == null || Target.Hp <= 0 || Target.Downed || Dist(Target) > range)
            Target = Raider.Nearest(g, X + .5f, Y + .5f, range);

        if (Target == null) return;

        if (Cd <= 0)
        {
            bool powered = g.PowerFracFor(this) > 0.2f;
            if (Shots <= 0)
            {
                if (!_warnedEmpty)
                {
                    _warnedEmpty = true;
                    g.AddLog($"{Bal.Name(Kind)} ran dry — belt Ammo Crates to it!", Pal.Warn);
                }
            }
            else if (powered)
            {
                Shots--;
                Cd = CooldownTime * g.TurretCdMul(this);   // MADDOG iter-6: dark = slow
                float dmg = Damage(g);
                float tx = Target.PosX, ty = Target.PosY;
                g.DamageRaider(Target, dmg, X + .5f, Y + .5f);
                float dx = tx - (X + .5f), dy = ty - (Y + .5f);
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 0.01f) { LungeDx = -dx / len; LungeDy = -dy / len; }
                LungeT = 0.14f;
                g.SpawnLaser(X + 0.5f, Y + 0.5f, tx, ty);
            }
            else Cd = CooldownTime;
        }
    }

    public float Dist(Raider r) =>
        MathF.Sqrt((r.PosX - (X + .5f)) * (r.PosX - (X + .5f)) + (r.PosY - (Y + .5f)) * (r.PosY - (Y + .5f)));
}

public sealed class HeavyTurret : Turret
{
    public override int MagCap => Bal.HeavyMagCap;
    public override float CooldownTime => Bal.HeavyCd;
    public override float Damage(Game g) => g.Has(Tech.Optics) ? Bal.HeavyDmgUp : Bal.HeavyDmg;
    public override float Range(Game g) => g.Has(Tech.Optics) ? Bal.HeavyRangeUp : Bal.HeavyRange;
}

/// <summary>A colonist-manned gun: needs an operator, no ammo.</summary>
public sealed class Watchtower : MachineBase
{
    public Raider? Target;
    public float Cd;
    public float LungeT, LungeDx, LungeDy;

    public override bool HasInputs() => false;
    public override void ConsumeInputs() { }
    public override ItemKind OutputOf() => ItemKind.IronOre;

    public override void Update(Game g, float dt)
    {
        Cd -= dt;
        LungeT = Math.Max(0, LungeT - dt);
        if (Operator == null) return;

        if (Target == null || Target.Hp <= 0 || Target.Downed ||
            MathF.Sqrt((Target.PosX - (X + .5f)) * (Target.PosX - (X + .5f)) +
                       (Target.PosY - (Y + .5f)) * (Target.PosY - (Y + .5f))) > Bal.TowerRange)
            Target = Raider.Nearest(g, X + .5f, Y + .5f, Bal.TowerRange);

        if (Target != null && Cd <= 0)
        {
            Cd = Bal.TowerCd * g.TurretCdMul(this);        // MADDOG iter-6: dark = slow
            float skillMul = 0.6f + Operator.Stats[2] * 0.07f;
            float tx = Target.PosX, ty = Target.PosY;
            g.DamageRaider(Target, Bal.TowerDmg * skillMul, X + .5f, Y + .5f);
            float dx = tx - (X + .5f), dy = ty - (Y + .5f);
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 0.01f) { LungeDx = dx / len; LungeDy = dy / len; }
            LungeT = 0.14f;
            g.SpawnLaser(X + 0.5f, Y - 0.1f, tx, ty, Pal.C(255, 210, 140));
        }
    }
}

/// <summary>Walkable spikes that chew raiders crossing them.</summary>
public sealed class SpikeTrap : Building
{
    public float Cd;

    public override void Update(Game g, float dt)
    {
        Cd -= dt;
        if (Cd > 0) return;
        var r = Raider.Nearest(g, X + .5f, Y + .5f, 0.85f);
        if (r == null) return;
        Cd = Bal.SpikeCd;
        g.DamageRaider(r, Bal.SpikeDmg, X + .5f, Y + .5f);
        Hp -= Bal.SpikeWear;
        g.SpawnSpark(r.PosX, r.PosY, Pal.Warn);
        if (Hp <= 0) g.DestroyBuilding(this, silent: false);
    }
}

/// <summary>Single-use blast mine.</summary>
public sealed class IED : Building
{
    public override void Update(Game g, float dt)
    {
        var r = Raider.Nearest(g, X + .5f, Y + .5f, 0.85f);
        if (r == null) return;
        g.Explode(X + .5f, Y + .5f, Bal.IedRadius, Bal.IedDmg);
        g.DestroyBuilding(this, silent: true);
    }
}

/// <summary>Projects a damage-absorbing bubble over nearby buildings.</summary>
public sealed class ShieldGen : Building
{
    public float ShieldHp = Bal.ShieldHp;
    public float SinceHit;

    public override void Update(Game g, float dt)
    {
        SinceHit += dt;
        if (ShieldHp > 0 && SinceHit >= Bal.ShieldDelay)
            ShieldHp = Math.Min(Bal.ShieldHp, ShieldHp + Bal.ShieldRegen * dt);
    }

    /// <summary>Try to absorb building damage inside the bubble. True = absorbed.</summary>
    public bool TryAbsorb(Building victim, float dmg)
    {
        if (ShieldHp <= 0) return false;
        if (MathF.Abs(victim.X - X) > Bal.ShieldRadius || MathF.Abs(victim.Y - Y) > Bal.ShieldRadius)
            return false;
        ShieldHp -= dmg;
        SinceHit = 0;
        return true;
    }
}

/// <summary>Deploy guard bots from belt-fed War Bot items.</summary>
public sealed class BotFactory : Building
{
    public int RallyX, RallyY;      // patrol point (defaults to the factory)
    public int Deployed;

    public override bool AcceptItem(Game g, ItemKind k)
    {
        if (k != ItemKind.WarBot || Deployed >= Bal.BotCap) return false;
        Deployed++;
        g.SpawnGuardBot(this);
        return true;
    }
}

// -----------------------------------------------------------------  misc --

public sealed class Reactor : Building { }

public sealed class SolarPanel : Building
{
    /// <summary>Scales with the day/night sun curve.</summary>
    public float Output(Game g) => Bal.SolarSupply * g.Sunlight * g.SolarMul;
}

public sealed class WindTurbine : Building
{
    public float Phase => ((X * 12_989_897u) ^ (Y * 3_242_178_233u)) % 628 / 100f;

    public float Output(Game g) =>
        (Bal.WindMin + (Bal.WindMax - Bal.WindMin) *
        (0.5f + 0.5f * MathF.Sin(g.Time * 0.21f + Phase) * MathF.Sin(g.Time * 0.043f + Phase * 2f)))
        * g.WindMul;
}

public sealed class Battery : Building { }
public class PowerPole : Building { }

/// <summary>SUBSTATION (2x2): a power pole with a big wire range and a big
/// coverage radius - one substation replaces a field of small poles.</summary>
public sealed class Substation : PowerPole
{
    public Substation() { W = 2; H = 2; }
    public override float WireRange => Bal.SubWire;
    public override float CoverRange => Bal.SubCover;
}
public sealed class Wall : Building { }

/// <summary>MADDOG iter-2: colonist-passable, raider-blocking entry. Raiders
/// chew it open exactly like a wall (Enemies attacks any path blocker).</summary>
public sealed class Door : Building
{
    public override int Comfort => 2;   // a door beats a hole in the wall
}

/// <summary>RECLAMATION: the wreck of the ark. World-placed at generation,
/// never buildable. Colonists excavate it (Mine priority); opening it ends
/// Chapter 2 and summons the finale.</summary>
public sealed class ArkWreck : Building
{
    public float Dig;                                // 0..Bal.ArkDigWork
    public float DigFrac => Math.Clamp(Dig / Bal.ArkDigWork, 0, 1);
    public Colonist? Excavator;
    public ArkWreck() { Kind = BuildKind.ArkWreck; W = 3; H = 3; }
}
/// <summary>Note: ctors must set Kind — buildings are not always created via
/// Building.Create (the starting Hab goes through PlaceFree), and the Kind
/// field defaults to BuildKind.Belt (ordinal 0).</summary>
public sealed class Hab : Building
{
    public Hab() { Kind = BuildKind.Hab; }
}
public sealed class MessTable : Building { }
public sealed class Lamp : Building { }
public sealed class Garden : Building { }

public sealed class MedBed : Building
{
    public Colonist? Occupant;
}

public sealed class Hub : Building
{
    public readonly int[] Stock = new int[Bal.ItemCount];

    public Hub()
    {
        Kind = BuildKind.Hub;
        W = 3; H = 3;
        MaxHp = Hp = 500;
    }
    public override float CoverRange => Bal.HubCover;

    public PointF CenterTile => new(X + 1.5f, Y + 1.5f);

    public override bool AcceptItem(Game g, ItemKind k)
    {
        Stock[(int)k]++;
        return true;
    }
}
