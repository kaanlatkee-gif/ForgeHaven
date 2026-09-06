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
    public Dir Face = Dir.Right;
    public float Hp, MaxHp = 100;

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
            BuildKind.Rail => new Rail(),
            BuildKind.ElevatedRail => new ElevatedRail(),
            BuildKind.TrainStop => new TrainStop(),
            BuildKind.DronePort => new DronePort(),
            BuildKind.Drill => new Drill(),
            BuildKind.DeepDrill => new DeepDrill(),
            BuildKind.Smelter => new Smelter(),
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
    public BeltItem(ItemKind k, float p) { Kind = k; Prog = p; }
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

    public bool TryReceive(ItemKind k)
    {
        if (!LaneSim.CanAccept(Lane)) return false;
        Lane.Add(new BeltItem(k, 0f));
        return true;
    }

    public override bool AcceptItem(Game g, ItemKind k) => TryReceive(k);

    public override void Update(Game g, float dt)
    {
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
public sealed class Inserter : Building
{
    public ItemKind? Held;
    public float Cd;
    public float Swing;          // 0..1 animation

    public override void Update(Game g, float dt)
    {
        Cd -= dt;
        Swing = Math.Max(0, Swing - dt * 2.2f);

        // push held item to the target ahead
        if (Held != null)
        {
            var (fx, fy) = DirU.Step(X, Y, Face);
            if (g.World.InBounds(fx, fy))
            {
                var tgt = g.World.Cell(fx, fy).B;
                if (tgt != null && tgt != this && tgt.AcceptItem(g, Held.Value, Face))
                {
                    Held = null;
                    Swing = 1f;
                    return;
                }
            }
            if (Cd > 0) return;
        }

        if (Cd > 0 || Held != null) return;
        Cd = Bal.InserterEvery;

        var (sx, sy) = DirU.Step(X, Y, DirU.Opposite(Face));
        if (!g.World.InBounds(sx, sy)) return;
        var src = g.World.Cell(sx, sy).B;
        switch (src)
        {
            case Belt b when b.Lane.Count > 0 && b.Lane[0].Prog > 0.35f:
                Held = b.Lane[0].Kind; b.Lane.RemoveAt(0); Swing = 1f; break;
            case MachineBase m when m.Out.Count > 0:
                Held = m.Out[0]; m.Out.RemoveAt(0); Swing = 1f; break;
            case StorageCrate c when c.Items.Count > 0:
                Held = c.Items[0]; c.Items.RemoveAt(0); Swing = 1f; break;
            case TrainStop st when st.OutBuffer.Count > 0:
                Held = st.OutBuffer[0]; st.OutBuffer.RemoveAt(0); Swing = 1f; break;
        }
    }
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

    public virtual float CraftTime => 3f;
    public virtual bool NeedsWorker => true;

    public abstract bool HasInputs();
    public abstract void ConsumeInputs();
    public abstract ItemKind OutputOf();

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

    public virtual float SpeedFactor(Game g)
    {
        float f = g.PowerFracFor(this);
        if (Operator != null) f *= Operator.WorkSpeedFor(Kind);
        else if (NeedsWorker && !g.Has(Tech.Automation)) return 0f;
        if (g.Has(Tech.Overclock)) f *= 1.25f;
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
        var (tx, ty) = DirU.Step(X, Y, Face);
        if (!g.World.InBounds(tx, ty)) return;
        var b = g.World.Cell(tx, ty).B;
        if (b != null && b != this && b.AcceptItem(g, Out[0], Face))
            Out.RemoveAt(0);
    }

    public float Craft01 => CraftTime <= 0 ? 0 : Prog / CraftTime;
}

// -------------------------------------------------------  ore production --

public class Drill : MachineBase
{
    public ItemKind Resource(Game g)
    {
        var t = g.World.Cell(X, Y).T;
        return t switch
        {
            Terrain.IronOre => ItemKind.IronOre,
            Terrain.CopperOre => ItemKind.CopperOre,
            Terrain.Crystal => ItemKind.Crystal,
            Terrain.Flora => ItemKind.Biomass,
            _ => ItemKind.IronOre,
        };
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

    /// <summary>Mining Productivity research: +10% drill speed per level.</summary>
    public override float SpeedFactor(Game g)
    {
        float f = base.SpeedFactor(g);
        return f * (1f + 0.10f * g.TechLevel(Tech.MiningProd));
    }

    public override bool AcceptItem(Game g, ItemKind k) => false;
}

public sealed class DeepDrill : Drill
{
    public override float CraftTime => Bal.DeepDrillTime;
}

public sealed class Smelter : MachineBase
{
    private ItemKind _pendingOut = ItemKind.IronPlate;

    public override float CraftTime => Bal.SmeltTime;
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
        _ => Bal.AmmoTime,
    } * CraftMul;

    public override bool HasInputs() => Recipe switch
    {
        1 => InCount(ItemKind.IronPlate) >= 2 && InCount(ItemKind.Crystal) >= 1,
        2 => InCount(ItemKind.IronPlate) >= 1 && InCount(ItemKind.CopperPlate) >= 1,
        3 => InCount(ItemKind.IronPlate) >= 1 && InCount(ItemKind.CopperPlate) >= 1,
        4 => InCount(ItemKind.IronPlate) >= 2 && InCount(ItemKind.CopperPlate) >= 1,
        _ => InCount(ItemKind.IronPlate) >= 1,
    };

    public override void ConsumeInputs()
    {
        switch (Recipe)
        {
            case 1: In[ItemKind.IronPlate] -= 2; In[ItemKind.Crystal] -= 1; break;
            case 2: In[ItemKind.IronPlate]--; In[ItemKind.CopperPlate]--; break;
            case 3: In[ItemKind.IronPlate]--; In[ItemKind.CopperPlate]--; break;
            case 4: In[ItemKind.IronPlate] -= 2; In[ItemKind.CopperPlate]--; break;
            default: In[ItemKind.IronPlate]--; break;
        }
    }

    public override ItemKind OutputOf() => Recipe switch
    {
        1 => ItemKind.AdvPart,
        2 => ItemKind.SciencePack,
        3 => ItemKind.Drone,
        4 => ItemKind.WarBot,
        _ => ItemKind.Ammo,
    };

    public override bool AcceptItem(Game g, ItemKind k) =>
        (k == ItemKind.IronPlate
        || (Recipe == 1 && k == ItemKind.Crystal)
        || (Recipe is 2 or 3 or 4 && k == ItemKind.CopperPlate))
        && AcceptIntoBuffer(k);

    public string RecipeName => Recipe switch
    {
        1 => "Adv. Part",
        2 => "Science Pack",
        3 => "Drone",
        4 => "War Bot",
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
                Cd = CooldownTime;
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
            Cd = Bal.TowerCd;
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
    public float Output(Game g) => Bal.SolarSupply * g.Sunlight;
}

public sealed class WindTurbine : Building
{
    public float Phase => ((X * 12_989_897u) ^ (Y * 3_242_178_233u)) % 628 / 100f;

    public float Output(Game g) =>
        Bal.WindMin + (Bal.WindMax - Bal.WindMin) *
        (0.5f + 0.5f * MathF.Sin(g.Time * 0.21f + Phase) * MathF.Sin(g.Time * 0.043f + Phase * 2f));
}

public sealed class Battery : Building { }
public sealed class PowerPole : Building { }
public sealed class Wall : Building { }
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

    public PointF CenterTile => new(X + 1.5f, Y + 1.5f);

    public override bool AcceptItem(Game g, ItemKind k)
    {
        Stock[(int)k]++;
        return true;
    }
}
