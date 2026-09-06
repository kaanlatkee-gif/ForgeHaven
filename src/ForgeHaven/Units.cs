using System.Drawing;

namespace ForgeHaven;

// ---------------------------------------------------------------------------
//  Units: things that move through the world but aren't colonists or raiders —
//  trains, guard bots, prisoners — plus the deterministic command layer that
//  multiplayer/replay will be built on (same seed + same commands = same game).
// ---------------------------------------------------------------------------

/// <summary>Cargo-hauling train. Shuttles between stations along rails;
/// per-tile reservation acts as a simple block signal.</summary>
public sealed class Train
{
    public float PosX, PosY;
    public float Angle;                        // radians, render facing
    public List<Point>? Path;
    public int PathIdx;
    public readonly Dictionary<ItemKind, int> Cargo = new();
    public int CargoCount;
    public TrainStop? Target;
    public float Wait;
    public bool BusyExchange;                  // loading/unloading phase
    public int ResX = int.MinValue, ResY = int.MinValue;
    private bool _loadedHere;                  // unload phase complete for this stop

    public void Update(Game g, float dt)
    {
        // pick a target if idle
        if (Target == null || !g.Builds.Contains(Target))
        {
            Target = NextStation(g);
            if (Target == null) return;
            Path = g.RailPath((int)PosX, (int)PosY, Target.X, Target.Y);
            PathIdx = 0;
            if (Path == null) return;          // no track yet: wait
        }

        if (BusyExchange)
        {
            Wait -= dt;
            Exchange(g);
            if (Wait <= 0 && ExchangeDone(g))
            {
                BusyExchange = false;
                _loadedHere = false;
                var next = NextStation(g);
                if (next == null) { Target = null; return; }
                Target = next;
                Path = g.RailPath((int)PosX, (int)PosY, Target.X, Target.Y);
                PathIdx = 0;
            }
            return;
        }

        if (Path == null || PathIdx >= Path.Count)
        {
            // arrived (or path lost): begin station exchange
            BusyExchange = true;
            Wait = Bal.TrainMinWait;
            _loadedHere = false;
            return;
        }

        var nextPt = Path[PathIdx];
        // block signal: never enter a tile reserved by another train
        if (g.TrainRes.TryGetValue((nextPt.X, nextPt.Y), out var other) && other != this)
            return;

        if (ResX != int.MinValue) g.TrainRes.Remove((ResX, ResY));
        ResX = nextPt.X; ResY = nextPt.Y;
        g.TrainRes[(ResX, ResY)] = this;

        float tx = nextPt.X + 0.5f, ty = nextPt.Y + 0.5f;
        float dx = tx - PosX, dy = ty - PosY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float step = Bal.TrainSpeed * dt;
        Angle = MathF.Atan2(dy, dx);
        if (dist <= step)
        {
            PosX = tx; PosY = ty;
            PathIdx++;
        }
        else
        {
            PosX += dx / dist * step;
            PosY += dy / dist * step;
        }
    }

    private TrainStop? NextStation(Game g)
    {
        TrainStop? best = null; float bd = float.MaxValue;
        foreach (var b in g.Builds)
        {
            if (b is not TrainStop st || st == Target) continue;
            float d = MathF.Abs(st.X - PosX) + Mathf_AbsHack(st.Y - PosY);
            if (d < bd) { bd = d; best = st; }
        }
        return best;
    }

    private static float Mathf_AbsHack(float v) => v < 0 ? -v : v;

    private void Exchange(Game g)
    {
        var st = Target!;

        // phase 1: unload arriving cargo -> station out buffer (one per tick)
        if (!_loadedHere)
        {
            foreach (var kv in Cargo)
            {
                if (st.OutBuffer.Count >= Bal.StationCap) break;
                if (kv.Value <= 0) continue;
                st.OutBuffer.Add(kv.Key);
                Cargo[kv.Key] = kv.Value - 1;
                CargoCount--;
                break;
            }
            // unload phase done once the hold is empty (or the buffer is stuffed)
            if (CargoCount == 0 || st.OutBuffer.Count >= Bal.StationCap)
                _loadedHere = true;
            else
                return;   // keep unloading before touching the in-buffer
        }

        // phase 2: load station in buffer -> cargo
        if (st.InBuffer.Count > 0 && CargoCount < Bal.TrainCap)
        {
            var k = st.InBuffer[0];
            st.InBuffer.RemoveAt(0);
            Cargo[k] = Cargo.GetValueOrDefault(k) + 1;
            CargoCount++;
        }
    }

    private bool ExchangeDone(Game g)
    {
        var st = Target!;
        bool unloadDone = _loadedHere;                       // unload phase finished
        bool loadDone = st.InBuffer.Count == 0 || CargoCount >= Bal.TrainCap;
        return unloadDone && loadDone;
    }
}

/// <summary>Factory-deployed guard drone: patrols the rally point and engages
/// raiders that come near it.</summary>
public sealed class GuardBot
{
    public float X, Y;
    public float Hp = Bal.BotHp;
    public BotFactory Home = null!;
    public float Cd;
    public Raider? Target;
    public float WalkPhase;
    public float FaceAngle;

    public float RallyX => Home.RallyX + 0.5f;
    public float RallyY => Home.RallyY + 0.5f;

    public void Update(Game g, float dt)
    {
        Cd -= dt;
        WalkPhase += dt * 6f;

        if (Target == null || Target.Hp <= 0 || Target.Downed)
            Target = Raider.Nearest(g, RallyX, RallyY, Bal.BotAggro);

        float tx, ty;
        if (Target != null) { tx = Target.PosX; ty = Target.PosY; }
        else { tx = RallyX; ty = RallyY; }

        float dx = tx - X, dy = ty - Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        FaceAngle = MathF.Atan2(dy, dx);

        float want = Target != null ? Bal.BotRange : 0.4f;
        if (dist > want)
        {
            float step = Bal.BotSpeed * dt;
            X += dx / dist * step;
            Y += dy / dist * step;
        }

        if (Target != null && dist <= Bal.BotRange + 0.2f && Cd <= 0)
        {
            Cd = 0.4f;
            g.DamageRaider(Target, Bal.BotDps * 0.4f, X, Y);
            g.SpawnLaser(X, Y, Target.PosX, Target.PosY, Pal.BotCol);
        }

        if (Hp <= 0) g.KillBot(this);
    }
}

/// <summary>A captured raider held at the Hub brig, waiting to be recruited.</summary>
public sealed class Prisoner
{
    public string Name;
    public float X, Y;              // visual spot near the hub
    public float Recruit;
    public float FoodTimer = Bal.PrisonerFoodEvery;
    public bool Fed = true;

    private static readonly string[] Names =
        { "Grix", "Vex", "Karn", "Thal", "Muro", "Skarn", "Zeel", "Orthus", "Nix", "Quill" };

    public Prisoner(float x, float y, Random rng)
    {
        X = x; Y = y;
        Name = Names[rng.Next(Names.Length)];
    }

    public void Update(Game g, float dt)
    {
        FoodTimer -= dt;
        if (FoodTimer <= 0)
        {
            FoodTimer = Bal.PrisonerFoodEvery;
            Fed = g.HubRef.Stock[(int)ItemKind.Food] > 0;
            if (Fed) g.HubRef.Stock[(int)ItemKind.Food]--;
        }

        float socAvg = 0;
        foreach (var c in g.Cols) socAvg += c.Stats[4];
        socAvg = g.Cols.Count == 0 ? 5 : socAvg / g.Cols.Count;

        Recruit += dt * (Bal.PrisonerRecruitRate + socAvg * Bal.PrisonerRecruitSoc) * (Fed ? 1f : 0.25f);
        if (Recruit >= 1f) g.RecruitPrisoner(this);
    }
}

// --------------------------------------------------------- command layer --
//  Every mutating player intent goes through this queue and is applied at a
//  fixed point in Update(). The sim is fully seeded/deterministic, so the
//  same seed + same command list replays identically — the groundwork a
//  lockstep multiplayer or replay system needs.

public enum CmdType
{
    Place, Bulldoze, SetRecipe, SetFilter, SetRally, SelectTech, Capture,
    DevGive, DevRaid, DevResearch, DevReveal, ToggleGod,
}

public sealed class SimCmd
{
    public CmdType Type;
    public int X, Y;          // world tile / point
    public int I0, I1;        // kind / recipe / item index / counts...
    public Dir Face;

    public override string ToString() => $"{Type}{{{X},{Y},{I0},{I1},{Face}}}";
}
