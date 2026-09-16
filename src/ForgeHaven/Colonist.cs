using System.Drawing;

namespace ForgeHaven;

// ---------------------------------------------------------------------------
//  Colonist: needs, traits, SKILLS (xp + passions), RimWorld-style work
//  priorities, job state machine, prisoner capture, self-defense melee.
// ---------------------------------------------------------------------------

public sealed class Colonist
{
    private static readonly string[] Names =
    {
        "Vera", "Idris", "Mina", "Koda", "Sable", "Ash", "Rune", "Petra",
        "Jax", "Nadia", "Theo", "Wren", "Oz", "Lena", "Bram", "Suri"
    };

    private static readonly string[] TraitPool =
    {
        "Optimist", "Pessimist", "Industrious", "Lazy", "Brave", "Genius", "Frugal", "Tough", "Loner", "Tinkerer"
    };

    public static readonly string[] StatNames = { "STR", "INT", "DEX", "END", "SOC", "CRE" };

    public string Name;
    public readonly List<string> Traits = new();
    public readonly int[] Stats = new int[6];      // STR INT DEX END SOC CRE — grow with XP now

    public float Hp;
    public float Hunger = 80;
    public float Rest = 85;
    public float Morale = 62;

    // skills: xp pool per stat + burning passions (learn double)
    public readonly float[] Xp = new float[6];
    public readonly int[] Passions = new int[2];
    public readonly int[] Priorities = new int[Bal.WorkCount];  // 1 best .. 4 never

    public float PosX, PosY;
    public List<Point>? Path;
    public int PathIdx;
    public ColState State = ColState.Idle;
    public MachineBase? Job;
    public float ActTimer;
    public float IdleTime;
    public bool InFlow;
    private bool _purposeWarned;
    private float _envTimer;
    private float _captureRepath;

    // combat / animation
    public float MeleeCd;
    public float LungeT, LungeDx, LungeDy;
    public float FlashT;
    public float BobT;
    public Dir FaceDir = Dir.Down;

    public MedBed? Bed;
    public Raider? CaptureTarget;      // prisoner being captured / escorted

    // Phase 1: draft / stances / manual labor / arrivals
    public bool Drafted;               // direct control (RimWorld style)
    public bool AutoEngage = true;     // drafted: fight nearby threats
    public Stance Stance = Stance.Flee; // non-drafted danger reaction
    public bool Arriving;              // walking in from the map edge
    public bool HasMoveOrder;          // drafted move order active
    public float OrderX, OrderY;
    public Raider? OrderFoe;           // drafted attack order
    public Blueprint? BuildJob;
    public MineOrder? MineJob;
    public Building? RepairJob;        // MADDOG iter-2: damaged building target
    public Carcass? GatherJob;         // MADDOG iter-4: carcass to butcher + haul
    public Beast? OrderBeast;          // MADDOG iter-4: drafted hunting order
    public ArkWreck? ExcavJob;         // RECLAMATION: digging out the ark
    private float _repairAcc;          // fractional HP repaired, for stone billing
    private float _noStoneT;           // anti-spam timer for the "no stone" log
    public float _scoutTimer;
    public float AteWellT;           // Phase 2: seconds of "proper meal" morale left
    private float _arriveRepath;     // throttled pathfinding while walking in
    private int _arriveFails;
    // SOULS: identity + social fabric + psyche
    public int Cid;                          // stable id across saves
    public readonly Dictionary<int, float> Opinions = new();   // cid -> opinion
    public float GriefT, CatharsisT, StressT;
    public float BreakT;                     // >0 = actively having a break
    public int BreakKind;                    // 0 dazed, 1 tantrum, 2 berserk
    private float _soulT, _breakWanderT;
    private float _eatRetryT;         // throttle when no path to food exists
    private float _breakDx, _breakDy;
    // transparency: the mood breakdown shown in the pawn panel (rebuilt 1x/sec)
    public readonly List<(string Key, float Delta)> MoodFactors = new();
    public float MoodTarget;         // last computed target (pre-lerp)

    // cosmetics (stable across save/load via name hash)
    public int ShirtTone, SkinTone, HairTone, BeltTone;

    public Colonist(float x, float y, Random rng)
    {
        Name = Names[rng.Next(0, Names.Length)];
        PosX = x; PosY = y;
        Hp = MaxHp;
        while (Traits.Count < 2)
        {
            var t = TraitPool[rng.Next(0, TraitPool.Length)];
            if (!Traits.Contains(t) &&
                !(t == "Optimist" && Traits.Contains("Pessimist")) &&
                !(t == "Pessimist" && Traits.Contains("Optimist")) &&
                !(t == "Industrious" && Traits.Contains("Lazy")) &&
                !(t == "Lazy" && Traits.Contains("Industrious")))
                Traits.Add(t);
        }
        for (int i = 0; i < 6; i++) Stats[i] = rng.Next(3, 9);
        Passions[0] = rng.Next(0, 6);
        do { Passions[1] = rng.Next(0, 6); } while (Passions[1] == Passions[0]);
        for (int i = 0; i < Bal.WorkCount; i++) Priorities[i] = 3;
        if (Has("Brave")) Stance = Stance.Fight;
        DeriveCosmetics();
    }

    public void DeriveCosmetics()
    {
        int h = 0;
        foreach (var ch in Name) h = h * 31 + ch;
        ShirtTone = Math.Abs(h) % 6;
        SkinTone = Math.Abs(h / 7) % 4;
        HairTone = Math.Abs(h / 29) % 6;
        BeltTone = Math.Abs(h / 53) % 6;
    }

    public float MaxHp => Has("Tough") ? 130 : Bal.ColHp;
    public bool Has(string trait) => Traits.Contains(trait);
    public bool IsPassion(int stat) => Passions[0] == stat || Passions[1] == stat;

    public string PassionText => string.Join("/", new[]
        { StatNames[Passions[0]], StatNames[Passions[1]] });

    // -------------------------------------------------------------  update

    public void Update(Game g, float dt)
    {
        if (Hp <= 0) return;

        MeleeCd -= dt;
        LungeT = Math.Max(0, LungeT - dt);
        FlashT = Math.Max(0, FlashT - dt);
        AteWellT = MathF.Max(0, AteWellT - dt);

        // ---- needs decay & slow natural regen
        Hunger -= Bal.ColHungerRate * dt * (Has("Frugal") ? 0.75f : 1f);
        Rest -= Bal.ColRestRate * dt * (State is ColState.Sleeping or ColState.Heal ? 0 : 1);
        if (Hunger <= 0) { Hunger = 0; Hp -= 1.5f * dt; }
        else if (Hp < MaxHp && State != ColState.Flee) Hp = Math.Min(MaxHp, Hp + Bal.ColRegenRate * dt);

        if (Hp <= 0) { Die(g); return; }

        // ---- SOULS: stress, social life, and the occasional break
        SoulTick(g, dt);
        if (BreakT > 0) { BreakTick(g, dt); return; }

        // ---- self-defense: anything chewing on us gets punched back
        Raider? touching = null;
        foreach (var r in g.Foes)
        {
            if (r.Hp <= 0 || r.Downed) continue;
            float d = Math.Abs(r.PosX - PosX) + Math.Abs(r.PosY - PosY);
            if (d < 1.3f) { touching = r; break; }
        }
        if (touching != null)
        {
            MeleeAttack(g, touching);
        }

        // ---- Phase 1: newcomers walk in from the edge, charting the fog
        if (Arriving) { ArrivingTick(g, dt); return; }

        // ---- Phase 1: drafted = direct control (orders, auto-engage, scout)
        if (Drafted) { DraftedTick(g, dt); return; }

        // ---- stance: how this pawn reacts to danger (non-drafted)
        if (Stance == Stance.Fight)
        {
            var near = Raider.Nearest(g, PosX, PosY, 2.6f);
            if (near != null)
            {
                float d = Math.Abs(near.PosX - PosX) + Math.Abs(near.PosY - PosY);
                if (d < 1.4f) MeleeAttack(g, near);
                else
                {
                    float dx = near.PosX - PosX, dy = near.PosY - PosY;
                    float len = MathF.Sqrt(dx * dx + dy * dy);
                    PosX += dx / len * Bal.ColSpeed * 0.8f * dt;
                    PosY += dy / len * Bal.ColSpeed * 0.8f * dt;
                    FaceDir = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? Dir.Right : Dir.Left)
                                                          : (dy > 0 ? Dir.Down : Dir.Up);
                    g.RecentCombat = 1f;
                    return;
                }
            }
        }

        // ---- flee check (Flee stance)
        bool danger = false;
        if (Stance == Stance.Flee)
        {
            foreach (var r in g.Foes)
                if (r.Hp > 0 && !r.Downed && Math.Abs(r.PosX - PosX) + Math.Abs(r.PosY - PosY) < 5f) { danger = true; break; }
        }
        if (danger && State != ColState.Flee)
        {
            BedRelease();
            State = ColState.Flee;
            PathToNearestHubTile(g);
        }

        switch (State)
        {
            case ColState.Flee:
                MoveAlong(g, dt, 1.25f);
                bool still = false;
                foreach (var r in g.Foes)
                    if (r.Hp > 0 && !r.Downed && Math.Abs(r.PosX - PosX) + Math.Abs(r.PosY - PosY) < 6.5f) { still = true; break; }
                if (!still) State = ColState.Idle;
                break;

            case ColState.Idle:
                IdleTick(g, dt);
                break;

            case ColState.GoWork:
                if (!MoveAlong(g, dt, 1f)) State = ColState.Working;
                break;

            case ColState.Working:
                WorkTick(g, dt);
                break;

            case ColState.GoEat:
                // bugfix: a NULL path used to count as "arrived" - pawns ate
                // hub food from across the map (teleport-eating)
                if (Path == null) { State = ColState.Idle; break; }
                if (!MoveAlong(g, dt, 1f)) { State = ColState.Eating; ActTimer = 1.2f; }
                break;

            case ColState.Eating:
                ActTimer -= dt;
                if (ActTimer <= 0)
                {
                    if (g.HubRef.Stock[(int)ItemKind.Food] > 0)
                    {
                        g.HubRef.Stock[(int)ItemKind.Food]--;
                        g.StatMeals++;
                        Hunger = Math.Min(100, Hunger + 70);
                        // Phase 2: a meal eaten near a Mess Table is a proper
                        // communal dinner - lifts morale for a while after
                        foreach (var mt in g.Builds)
                            if (mt is MessTable &&
                                MathF.Abs(mt.X + 0.5f - PosX) + MathF.Abs(mt.Y + 0.5f - PosY) <= Bal.MessTableMealRadius)
                            { AteWellT = Bal.MealWellDuration; break; }
                        // SOULS: dinner together is how friendships start
                        if (AteWellT > 0)
                            foreach (var o in g.Cols)
                                if (o != this && o.Hp > 0 && o.AteWellT > 0 &&
                                    MathF.Abs(o.PosX - PosX) + MathF.Abs(o.PosY - PosY) <= Bal.MessTableMealRadius)
                                {
                                    BumpOp(g, o, 4f);
                                    o.BumpOp(g, this, 4f);
                                }
                    }
                    else g.AddLogThrottled($"{Name} is hungry — no Food at the Hub!", Pal.Warn);
                    State = ColState.Idle;
                }
                break;

            case ColState.GoSleep:
                if (!MoveAlong(g, dt, 1f) && State == ColState.GoSleep) State = ColState.Sleeping;
                break;

            case ColState.Sleeping:
                Rest += 9f * dt;
                if (Rest >= 92) { Rest = 92; State = ColState.Idle; }
                break;

            case ColState.Heal:
                Hp = Math.Min(MaxHp, Hp + Bal.MedBedHealRate * dt);
                Rest = Math.Min(92, Rest + 5f * dt);
                if (Hp >= MaxHp * 0.8f) { BedRelease(); State = ColState.Idle; }
                break;

            case ColState.GoCapture:
                CaptureTick(g, dt);
                break;

            case ColState.Escort:
                EscortTick(g, dt);
                break;

            // ---- Phase 1: manual labor (blueprint construction / mining)
            case ColState.GoBuild:
                if (BuildJob == null || BuildJob.Builder != this) { BuildJob = null; State = ColState.Idle; break; }
                if (!MoveAlong(g, dt, 1f)) State = ColState.Building;
                break;

            case ColState.Building:
            {
                var bp = BuildJob;
                if (bp == null || bp.Builder != this) { BuildJob = null; State = ColState.Idle; break; }
                bp.Work -= dt * (1f + Stats[0] * 0.08f);          // STR swings faster
                if (bp.Work <= 0) g.CompleteBlueprint(bp);         // clears BuildJob + state
                break;
            }

            case ColState.GoMine:
                if (MineJob == null || MineJob.Miner != this) { MineJob = null; State = ColState.Idle; break; }
                if (!MoveAlong(g, dt, 1f))
                {
                    State = ColState.Mining;
                    ActTimer = g.World.Cell(MineJob.X, MineJob.Y).T == Terrain.Rock
                        ? Bal.HandMineRockTime : Bal.HandMineOreTime;
                }
                break;

            case ColState.Mining:
            {
                var mo = MineJob;
                if (mo == null || mo.Miner != this) { MineJob = null; State = ColState.Idle; break; }
                ActTimer -= dt;
                if (ActTimer > 0) break;
                var t = g.World.Cell(mo.X, mo.Y).T;
                if (t == Terrain.Rock)
                {
                    g.HubRef.Stock[(int)ItemKind.Stone] += Bal.RockStonesPerCycle;
                    g.StatMined++;
                    mo.CyclesLeft--;
                    ActTimer = Bal.HandMineRockTime;
                    if (mo.CyclesLeft <= 0)
                    {
                        g.World.SetTerrain(mo.X, mo.Y, Terrain.Ground);
                        g.CancelMineOrder(mo);                    // clears MineJob
                        State = ColState.Idle;
                    }
                }
                else
                {
                    var item = t switch
                    {
                        Terrain.IronOre => ItemKind.IronOre,
                        Terrain.CopperOre => ItemKind.CopperOre,
                        Terrain.Crystal => ItemKind.Crystal,
                        _ => ItemKind.Biomass,
                    };
                    g.HubRef.Stock[(int)item] += Bal.HandOreYield;
                    g.StatMined++;
                    ActTimer = Bal.HandMineOreTime;
                }
                break;
            }

            case ColState.GoRepair:
            {
                var b = RepairJob;
                if (b == null || b.Hp <= 0) { ClearRepair(g); break; }
                // a worn/broken machine at FULL HP is still a valid target
                bool worn = b is MachineBase wm && (wm.BrokenDown || wm.Wear >= 30f);
                if (b.Hp >= b.MaxHp && !worn) { ClearRepair(g); break; }
                if (!MoveAlong(g, dt, 1f)) State = ColState.Repairing;
                break;
            }

            case ColState.Repairing:
            {
                var b = RepairJob;
                if (b == null || b.Hp <= 0) { ClearRepair(g); break; }
                // ORGANISM: worn machines get maintenance first (free, fast)
                if (b is MachineBase worn && (worn.BrokenDown || worn.Wear >= 30f))
                {
                    worn.Wear = MathF.Max(0f, worn.Wear - Bal.WearFixRate * dt);
                    if (worn.BrokenDown && worn.Wear < 99f)
                    {
                        worn.BrokenDown = false;
                        g.AddLog($"{Name} got the {Bal.Name(b.Kind)} running again.", Pal.Good);
                    }
                    if (worn.Wear > 1f || b.Hp < b.MaxHp) break;   // still work to do
                }
                if (b.Hp >= b.MaxHp)                          // done
                {
                    if (b == g.HubRef) g.AddLog($"{Name} patched the Hub back to full.", Pal.Good);
                    ClearRepair(g);
                    break;
                }
                // stone is the patching material - billed in Bal.RepairHpPerStone chunks
                if (g.HubRef.Stock[(int)ItemKind.Stone] <= 0 && _repairAcc < Bal.RepairHpPerStone)
                {
                    if (_noStoneT <= 0)
                    {
                        _noStoneT = 20f;
                        g.AddLog($"{Name} can't repair — out of Stone.", Pal.Warn);
                    }
                    _noStoneT -= dt;
                    break;                                    // wait for stone
                }
                _noStoneT = 0;
                float rate = Bal.RepairRate * (1f + Stats[0] * 0.05f);   // STR swings faster
                float healed = MathF.Min(rate * dt, b.MaxHp - b.Hp);
                b.Hp += healed;
                _repairAcc += healed;
                while (_repairAcc >= Bal.RepairHpPerStone)
                {
                    if (g.HubRef.Stock[(int)ItemKind.Stone] <= 0) { _repairAcc = 0; break; }
                    g.HubRef.Stock[(int)ItemKind.Stone]--;
                    _repairAcc -= Bal.RepairHpPerStone;
                }
                if (g.Rand.NextDouble() < 0.06) g.SpawnSpark(PosX, PosY, Pal.Good);
                break;
            }

            case ColState.GoGather:
            {
                var car = GatherJob;
                if (car == null || car.Food <= 0) { ClearGather(g); break; }
                if (!MoveAlong(g, dt, 1f)) { State = ColState.Gathering; ActTimer = Bal.BeastGatherTime; }
                break;
            }

            case ColState.GoExcavate:
            {
                var ark = ExcavJob;
                if (ark == null || ark.Dig >= Bal.ArkDigWork) { ClearExcav(g); break; }
                if (!MoveAlong(g, dt, 1f)) State = ColState.Excavating;
                break;
            }

            case ColState.Excavating:
            {
                var ark = ExcavJob;
                if (ark == null || ark.Dig >= Bal.ArkDigWork) { ClearExcav(g); break; }
                ark.Dig += dt * (1f + Stats[0] * 0.06f);        // STR swings the pick
                if (g.Rand.NextDouble() < 0.05) g.SpawnSpark(PosX, PosY, Pal.Warn);
                if (ark.Dig >= Bal.ArkDigWork) { g.OnArkOpened(); ClearExcav(g); }
                break;
            }

            case ColState.Gathering:
            {
                var car = GatherJob;
                if (car == null || car.Food <= 0) { ClearGather(g); break; }
                ActTimer -= dt;
                if (ActTimer > 0) break;
                g.HubRef.Stock[(int)ItemKind.Food] += car.Food;
                g.AddLog($"{Name} hauled {car.Food} Food from a carcass.", Pal.Good);
                car.Food = 0;
                g.Carcasses.Remove(car);
                ClearGather(g);
                break;
            }
        }

        // ---- morale model
        _envTimer -= dt;
        if (_envTimer <= 0)
        {
            _envTimer = 1f;
            UpdateMorale(g);
        }
    }

    // ------------------------------------------------------ drafted mode

    /// <summary>Direct control: move/attack orders, optional auto-engage of
    /// nearby threats, and fog-of-war scouting while far from home.</summary>
    private void DraftedTick(Game g, float dt)
    {
        // scouting: drafted pawns chart the fog as they stand/move
        _scoutTimer -= dt;
        if (_scoutTimer <= 0)
        {
            _scoutTimer = 1f;
            g.World.RevealAround((int)PosX, (int)PosY, 0);
        }

        if (OrderFoe != null && OrderFoe.Hp > 0 && !OrderFoe.Downed)
        {
            float d = MathF.Abs(OrderFoe.PosX - PosX) + MathF.Abs(OrderFoe.PosY - PosY);
            if (d > Bal.DraftChaseLeash) OrderFoe = null;      // bugfix: no infinite chases
            else
            {
                if (d <= 1.3f) { MeleeAttack(g, OrderFoe); return; }
                float dx = OrderFoe.PosX - PosX, dy = OrderFoe.PosY - PosY;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                PosX += dx / len * Bal.ColSpeed * dt;
                PosY += dy / len * Bal.ColSpeed * dt;
                FaceDir = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? Dir.Right : Dir.Left)
                                                      : (dy > 0 ? Dir.Down : Dir.Up);
                g.RecentCombat = 1f;
                return;
            }
        }
        else OrderFoe = null;

        // MADDOG iter-4: hunting order - chase the grazer and swing
        if (OrderBeast != null && OrderBeast.Hp > 0)
        {
            float bd = MathF.Abs(OrderBeast.PosX - PosX) + MathF.Abs(OrderBeast.PosY - PosY);
            if (bd > Bal.DraftChaseLeash) OrderBeast = null;
            else
            {
                if (bd <= 1.3f) { MeleeAttackBeast(g, OrderBeast); return; }
                float bdx = OrderBeast.PosX - PosX, bdy = OrderBeast.PosY - PosY;
                float blen = MathF.Sqrt(bdx * bdx + bdy * bdy);
                PosX += bdx / blen * Bal.ColSpeed * dt;
                PosY += bdy / blen * Bal.ColSpeed * dt;
                FaceDir = Math.Abs(bdx) > Math.Abs(bdy) ? (bdx > 0 ? Dir.Right : Dir.Left)
                                                       : (bdy > 0 ? Dir.Down : Dir.Up);
                return;
            }
        }
        else OrderBeast = null;

        if (HasMoveOrder)
        {
            if (Path == null || PathIdx >= Path.Count) { HasMoveOrder = false; return; }
            MoveAlong(g, dt, 1.15f);                   // drafted pawns march briskly
            return;
        }

        if (AutoEngage)
        {
            var near = Raider.Nearest(g, PosX, PosY, Bal.DraftEngageRadius);
            if (near != null)
            {
                float d = MathF.Abs(near.PosX - PosX) + MathF.Abs(near.PosY - PosY);
                if (d <= 1.3f) { MeleeAttack(g, near); return; }
                float dx = near.PosX - PosX, dy = near.PosY - PosY;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                PosX += dx / len * Bal.ColSpeed * 0.9f * dt;
                PosY += dy / len * Bal.ColSpeed * 0.9f * dt;
                FaceDir = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? Dir.Right : Dir.Left)
                                                      : (dy > 0 ? Dir.Down : Dir.Up);
                g.RecentCombat = 1f;
            }
        }
    }

    /// <summary>Newcomer: walk the pre-computed path toward the Hub,
    /// revealing the chunks they cross.</summary>
    private void ArrivingTick(Game g, float dt)
    {
        if (Drafted) { Arriving = false; return; }     // player took control
        _scoutTimer -= dt;
        if (_scoutTimer <= 0)
        {
            _scoutTimer = 1f;
            g.World.RevealAround((int)PosX, (int)PosY, 0);
        }
        var hc = g.HubRef.CenterTile;
        if (MathF.Abs(PosX - hc.X) < 4.5f && MathF.Abs(PosY - hc.Y) < 4.5f)
        {
            Arriving = false;
            Path = null;
            g.AddLog($"{Name} has arrived at the colony.", Pal.Good);
            return;
        }
        if (Path == null || PathIdx >= Path.Count)
        {
            // bugfix: repathing every tick over a long unreachable route was
            // expensive; throttle it and give up gracefully after a while
            _arriveRepath -= dt;
            if (_arriveRepath <= 0)
            {
                _arriveRepath = 1.5f;
                // noise lakes can make the walk to the hub a long detour -
                // newcomers get a big search budget so they route around them
                Path = g.World.FindPath((int)PosX, (int)PosY, (int)hc.X, (int)hc.Y + 3,
                                        enemy: false, maxNodes: 30000);
                PathIdx = 0;
                if (Path == null && ++_arriveFails >= 8)
                {
                    Arriving = false;      // stranded wanderer: fend for yourself
                    g.AddLog($"{Name} lost the way to the colony.", Pal.Warn);
                }
            }
            if (Path == null) return;
        }
        _arriveFails = 0;
        MoveAlong(g, dt, 1f);
    }

    // ------------------------------------------------------- capture flow

    private void CaptureTick(Game g, float dt)
    {
        var r = CaptureTarget;
        if (r == null || r.Hp <= 0 || !r.Downed || r.Captured)
        { CaptureTarget = null; State = ColState.Idle; return; }

        _captureRepath -= dt;
        if (Path == null || PathIdx >= Path.Count || _captureRepath <= 0)
        {
            _captureRepath = 2f;
            Path = g.World.FindPath((int)PosX, (int)PosY, (int)r.PosX, (int)r.PosY, enemy: false);
            PathIdx = 0;
        }
        if (!MoveAlong(g, dt, 1.1f))
        {
            // reached the body — begin the escort
            r.Captured = true;
            State = ColState.Escort;
            PathToNearestHubTile(g);
        }
    }

    private void EscortTick(Game g, float dt)
    {
        var r = CaptureTarget;
        if (r == null || r.Hp <= 0) { CaptureTarget = null; State = ColState.Idle; return; }

        if (!MoveAlong(g, dt, 0.95f))
        {
            g.InternPrisoner(r, this);
            CaptureTarget = null;
            State = ColState.Idle;
            return;
        }
        // prisoner shuffles along behind the captor
        float dx = PosX - r.PosX, dy = PosY - r.PosY;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d > 0.8f)
        {
            r.PosX += dx / d * Bal.ColSpeed * 0.9f * dt;
            r.PosY += dy / d * Bal.ColSpeed * 0.9f * dt;
        }
    }

    /// <summary>MADDOG iter-4: same swing, different target - the grazer
    /// panics and runs instead of fighting back.</summary>
    private void MeleeAttackBeast(Game g, Beast b)
    {
        if (MeleeCd > 0) return;
        MeleeCd = Bal.ColMeleeCd;
        float dmg = 3f + Stats[0] * 0.8f;   // STR-driven
        float tx = b.PosX, ty = b.PosY;
        g.DamageBeast(b, dmg, PosX, PosY);
        FaceDir = Math.Abs(tx - PosX) > Math.Abs(ty - PosY)
            ? (tx > PosX ? Dir.Right : Dir.Left)
            : (ty > PosY ? Dir.Down : Dir.Up);
        float dx = tx - PosX, dy = ty - PosY;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len > 0.01f) { LungeDx = dx / len; LungeDy = dy / len; }
        LungeT = 0.16f;
        g.SpawnSpark(tx, ty, Pal.Warn);
    }

    private void MeleeAttack(Game g, Raider r)
    {
        if (MeleeCd > 0) return;
        MeleeCd = Bal.ColMeleeCd;
        float dmg = 3f + Stats[0] * 0.8f;   // STR-driven
        float tx = r.PosX, ty = r.PosY;
        g.DamageRaider(r, dmg, PosX, PosY);
        FaceDir = Math.Abs(tx - PosX) > Math.Abs(ty - PosY)
            ? (tx > PosX ? Dir.Right : Dir.Left)
            : (ty > PosY ? Dir.Down : Dir.Up);
        float dx = tx - PosX, dy = ty - PosY;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len > 0.01f) { LungeDx = dx / len; LungeDy = dy / len; }
        LungeT = 0.16f;
        g.RecentCombat = 1.5f;
        g.SpawnSpark(tx, ty, Pal.Colonist);
    }

    private void IdleTick(Game g, float dt)
    {
        IdleTime += dt;

        if (IdleTime > 30 && !_purposeWarned)
        {
            _purposeWarned = true;
            g.AddLog($"{Name} feels purposeless — assign work or build a Garden.", Pal.Warn);
        }

        // badly wounded? claim a med bed
        if (Hp < MaxHp * 0.45f)
        {
            var bed = FindFreeMedBed(g);
            if (bed != null)
            {
                Bed = bed; bed.Occupant = this;
                State = ColState.GoSleep;
                SleepTargetIsBed = true;
                Path = g.World.FindPath((int)PosX, (int)PosY, bed.X, bed.Y, enemy: false);
                PathIdx = 0;
                return;
            }
        }

        // urgent needs
        _eatRetryT -= dt;
        if (Hunger < 28 && _eatRetryT <= 0)
        {
            State = ColState.GoEat;
            if (!PathToNearestHubTile(g)) { State = ColState.Idle; _eatRetryT = 5f; }
            return;
        }

        // colonists prefer sleeping through the night
        if (Rest < (g.IsNight ? 55 : 22)) { State = ColState.GoSleep; SleepTargetIsBed = false; PathToSleepSpot(g); return; }

        if (Job != null)
        {
            var spot = Game.WorkSpot(g.World, Job, this);
            if (spot.HasValue)
            {
                State = ColState.GoWork;
                Path = g.World.FindPath((int)PosX, (int)PosY, spot.Value.X, spot.Value.Y, enemy: false);
                PathIdx = 0;
            }
            else { Job = null; g.AddLog($"{Name} can't reach their workstation.", Pal.Warn); }
            return;
        }

        // wander a little near gardens/hub
        if (g.Rand.NextDouble() < 0.006)
        {
            var t = PickWanderTarget(g);
            if (t.HasValue)
            {
                Path = g.World.FindPath((int)PosX, (int)PosY, t.Value.X, t.Value.Y, enemy: false);
                PathIdx = 0;
            }
        }
        MoveAlong(g, dt, 0.5f);
    }

    public bool SleepTargetIsBed;

    // ------------------------------------------------------------ SOULS --

    private bool _deathDone;

    /// <summary>Death processing: log, chronicle, friends grieve. Called from
    /// the needs check AND from the Game update loop (so combat deaths -
    /// which happen between updates - are never silently dropped).</summary>
    public void Die(Game g)
    {
        if (_deathDone) return;
        _deathDone = true;
        g.AddLog($"{Name} has died.", Pal.Bad);
        g.AddChron($"{Name} has died.");
        foreach (var o in g.Cols)
            if (o != this && o.Hp > 0 && o.GetOp(this) >= Bal.FriendAt)
            {
                o.GriefT = Bal.GriefSec;
                g.AddChron($"{o.Name} mourns {Name}.");
            }
        g.ReleaseFromJob(this);
    }

    public float GetOp(Colonist o) => Opinions.TryGetValue(o.Cid, out var v) ? v : 0f;

    /// <summary>Shift opinion of another colonist; logs friendships as they form.</summary>
    public void BumpOp(Game g, Colonist o, float d)
    {
        float was = GetOp(o);
        float now = Math.Clamp(was + d, -100f, 100f);
        Opinions[o.Cid] = now;
        if (was < Bal.FriendAt && now >= Bal.FriendAt && Cid < o.Cid)
            g.AddChron($"{Name} and {o.Name} have become friends.");
    }

    private bool SharesPassionWith(Colonist o)
    {
        for (int i = 0; i < 6; i++)
            if (IsPassion(i) && o.IsPassion(i)) return true;
        return false;
    }

    /// <summary>Runs for every colonist every frame (cheap inner logic is
    /// throttled to 2s): stress accrual, break rolls, social drift,
    /// grief/catharsis countdowns.</summary>
    private void SoulTick(Game g, float dt)
    {
        GriefT = MathF.Max(0, GriefT - dt);
        CatharsisT = MathF.Max(0, CatharsisT - dt);
        _soulT -= dt;
        if (_soulT > 0) return;
        _soulT = 2f;

        // stress: prolonged misery invites a break
        if (Morale < 20f && BreakT <= 0)
        {
            StressT += 2f;
            if (StressT >= Bal.BreakStressSec && g.Rand.NextDouble() < 0.35)
            {
                StressT = 0;
                double roll = g.Rand.NextDouble();
                BreakKind = roll < 0.60 ? 0 : roll < 0.95 ? 1 : 2;
                BreakT = BreakKind == 0 ? Bal.BreakDazedSec
                       : BreakKind == 1 ? Bal.BreakTantrumSec : Bal.BreakBerserkSec;
                _breakWanderT = 0;
                g.AddLog(BreakKind switch
                {
                    1 => $"{Name} SNAPS - they are lashing out at our own things!",
                    2 => $"{Name} has gone berserk - get clear!",
                    _ => $"{Name} wanders off, staring at nothing...",
                }, Pal.Bad);
                g.AddChron(BreakKind switch
                {
                    1 => $"{Name} broke - a tantrum smashed half a shift's work.",
                    2 => $"{Name} turned on their own.",
                    _ => $"{Name} wandered the wastes, hollow-eyed, until they came back.",
                });
            }
        }
        else StressT = MathF.Max(0, StressT - 1f);

        // social drift: proximity, shared passions, battle bonds, insults
        foreach (var o in g.Cols)
        {
            if (o == this || o.Hp <= 0) continue;
            if (MathF.Abs(o.PosX - PosX) + MathF.Abs(o.PosY - PosY) > Bal.SocialRadius) continue;
            float d = 0.4f;
            if (SharesPassionWith(o)) d += 0.3f;
            if (g.RecentCombat > 0 && Drafted && o.Drafted) d += 1.6f;   // bled together
            if (Traits.Contains("Pessimist") && g.Rand.NextDouble() < 0.12) d = -2f;
            BumpOp(g, o, d);
        }
    }

    /// <summary>A mental break in progress. Overrides all other behavior.</summary>
    private void BreakTick(Game g, float dt)
    {
        BreakT -= dt;
        if (BreakT <= 0)
        {
            BreakT = 0;
            CatharsisT = Bal.CatharsisSec;
            Morale = MathF.Max(Morale, 45f);       // the release helps
            State = ColState.Idle;
            return;
        }

        if (BreakKind == 1)
        {
            // tantrum: smash the nearest thing we built (never the Hub)
            Building? victim = null; float bd = 12f;
            foreach (var b in g.Builds)
            {
                if (b.Hp <= 0 || b is Hub) continue;
                float d = MathF.Abs(b.X + b.W / 2f - PosX) + MathF.Abs(b.Y + b.H / 2f - PosY);
                if (d < bd) { bd = d; victim = b; }
            }
            if (victim != null)
            {
                float dx = victim.X + victim.W / 2f - PosX, dy = victim.Y + victim.H / 2f - PosY;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 1.6f)
                {
                    PosX += dx / len * Bal.ColSpeed * 0.85f * dt;
                    PosY += dy / len * Bal.ColSpeed * 0.85f * dt;
                }
                else
                {
                    g.DamageBuilding(victim, 14f * dt);
                    if (g.Rand.NextDouble() < 0.1) g.SpawnSpark(PosX, PosY, Pal.Bad);
                }
                return;
            }
        }
        else if (BreakKind == 2)
        {
            // berserk: attack the nearest colonist, but stop before real harm
            Colonist? victim = null; float bd = 14f;
            foreach (var o in g.Cols)
            {
                if (o == this || o.Hp <= 0 || o.Hp < o.MaxHp * 0.35f) continue;
                float d = MathF.Abs(o.PosX - PosX) + MathF.Abs(o.PosY - PosY);
                if (d < bd) { bd = d; victim = o; }
            }
            if (victim != null)
            {
                float dx = victim.PosX - PosX, dy = victim.PosY - PosY;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 1.2f)
                {
                    PosX += dx / len * Bal.ColSpeed * 0.95f * dt;
                    PosY += dy / len * Bal.ColSpeed * 0.95f * dt;
                }
                else if (MeleeCd <= 0)
                {
                    MeleeCd = 0.8f;
                    victim.Hp -= 6f;
                    g.RecentCombat = 1f;
                    g.SpawnSpark(victim.PosX, victim.PosY, Pal.Bad);
                    if (victim.Hp < victim.MaxHp * 0.35f) BreakT = 0;   // shock snaps them out
                }
                return;
            }
        }

        // dazed: slow aimless wandering, no pathfinding (safe anywhere)
        _breakWanderT -= dt;
        if (_breakWanderT <= 0)
        {
            _breakWanderT = 1.5f + (float)g.Rand.NextDouble();
            float ang = (float)(g.Rand.NextDouble() * Math.PI * 2);
            _breakDx = MathF.Cos(ang); _breakDy = MathF.Sin(ang);
        }
        int nx = (int)(PosX + _breakDx * 0.4f * dt), ny = (int)(PosY + _breakDy * 0.4f * dt);
        if (g.World.WalkColonist(nx, ny))
        {
            PosX += _breakDx * 0.4f * dt;
            PosY += _breakDy * 0.4f * dt;
        }
        else _breakWanderT = 0;
    }

    /// <summary>Drop the excavation job from both sides.</summary>
    public void ClearExcav(Game g)
    {
        if (ExcavJob != null && ExcavJob.Excavator == this) ExcavJob.Excavator = null;
        ExcavJob = null;
        State = ColState.Idle;
    }

    /// <summary>Drop the gather job from both sides (colonist + carcass claim).</summary>
    public void ClearGather(Game g)
    {
        if (GatherJob != null && GatherJob.Gatherer == this) GatherJob.Gatherer = null;
        GatherJob = null;
        State = ColState.Idle;
    }

    /// <summary>Drop the repair job from both sides (colonist + building claim).</summary>
    public void ClearRepair(Game g)
    {
        if (RepairJob != null && RepairJob.Repairer == this) RepairJob.Repairer = null;
        RepairJob = null;
        _repairAcc = 0;
        State = ColState.Idle;
    }

    private MedBed? FindFreeMedBed(Game g)
    {
        MedBed? best = null; float bd = float.MaxValue;
        foreach (var b in g.Builds)
            if (b is MedBed m && (m.Occupant == null || m.Occupant == this))
            {
                float d = MathF.Abs(b.X + .5f - PosX) + MathF.Abs(b.Y + .5f - PosY);
                if (d < bd) { bd = d; best = m; }
            }
        return best;
    }

    public void BedRelease()
    {
        if (Bed != null && Bed.Occupant == this) Bed.Occupant = null;
        Bed = null;
        SleepTargetIsBed = false;
    }

    private void WorkTick(Game g, float dt)
    {
        IdleTime = 0;
        _purposeWarned = false;

        if (Hunger < 24 || Rest < 16 || Hp < MaxHp * 0.3f)
        {
            State = ColState.Idle;
            return;
        }
        if (Job == null) { State = ColState.Idle; return; }

        if (Job.Operator != this) Job.Operator = this;

        // ---- skill xp: the relevant stat for this machine's work type
        if (Bal.WorkOf(Job.Kind) is { } wt)
        {
            int stat = Bal.StatOf(wt);
            float mul = IsPassion(stat) ? Bal.PassionMul : 1f;
            Xp[stat] += dt * Bal.XpWorkRate * mul;
            while (Stats[stat] < Bal.XpCap && Xp[stat] >= XpNeeded(Stats[stat]))
            {
                Xp[stat] -= XpNeeded(Stats[stat]);
                Stats[stat]++;
                g.AddLog($"{Name} reached {StatNames[stat]} {Stats[stat]}{(IsPassion(stat) ? " (passion!)" : "")}.", Pal.Good);
            }
        }

        bool flowNow = Morale >= Bal.FlowMorale;
        if (flowNow && !InFlow)
            g.AddLog($"{Name} entered a FLOW state!", Pal.Good);
        InFlow = flowNow;
        if (InFlow && g.Rand.NextDouble() < 0.10f * dt)
            g.SpawnSpark(PosX, PosY, Pal.Colonist);
    }

    public static float XpNeeded(int level) => 60f + level * 30f;

    /// <summary>True while still travelling; false when the path is finished.</summary>
    private bool MoveAlong(Game g, float dt, float speedMul)
    {
        if (Path == null || PathIdx >= Path.Count) return false;
        if (g.TileBlighted((int)PosX, (int)PosY)) speedMul *= 0.7f;   // blight drags at you
        if (g.World.InBounds((int)PosX, (int)PosY) &&
            g.World.Cell((int)PosX, (int)PosY).T == Terrain.Tree) speedMul *= Bal.TreeWalkMul;   // pushing past trunks

        var target = Path[PathIdx];
        float tx = target.X + 0.5f, ty = target.Y + 0.5f;
        float dx = tx - PosX, dy = ty - PosY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float step = Bal.ColSpeed * (0.8f + Math.Min(Stats[2], 16) * 0.04f) * speedMul * dt;

        FaceDir = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? Dir.Right : Dir.Left)
                                              : (dy > 0 ? Dir.Down : Dir.Up);
        BobT += dt * speedMul * 7f;

        if (dist <= step)
        {
            PosX = tx; PosY = ty;
            PathIdx++;
            if (PathIdx >= Path.Count && State == ColState.GoSleep && SleepTargetIsBed && Bed != null)
                State = ColState.Heal;
            return PathIdx < Path.Count;
        }
        PosX += dx / dist * step;
        PosY += dy / dist * step;
        return true;
    }

    private bool PathToNearestHubTile(Game g)
    {
        var c = g.HubRef.CenterTile;
        int bestX = (int)c.X, bestY = (int)c.Y;
        float bd = float.MaxValue;
        for (int x = g.HubRef.X - 1; x <= g.HubRef.X + g.HubRef.W; x++)
            for (int y = g.HubRef.Y - 1; y <= g.HubRef.Y + g.HubRef.H; y++)
            {
                if (!g.World.WalkColonist(x, y)) continue;
                float d = MathF.Abs(x + .5f - PosX) + MathF.Abs(y + .5f - PosY);
                if (d < bd) { bd = d; bestX = x; bestY = y; }
            }
        Path = g.World.FindPath((int)PosX, (int)PosY, bestX, bestY, enemy: false, maxNodes: 24000);
        PathIdx = 0;
        return Path != null;
    }

    private void PathToSleepSpot(Game g)
    {
        Hab? hab = null; float bd = float.MaxValue;
        foreach (var b in g.Builds)
            if (b is Hab h)
            {
                float d = MathF.Abs(b.X + .5f - PosX) + MathF.Abs(b.Y + .5f - PosY);
                if (d < bd) { bd = d; hab = h; }
            }
        if (hab != null)
        {
            Path = g.World.FindPath((int)PosX, (int)PosY, hab.X, hab.Y, enemy: false);
            PathIdx = 0;
        }
        else PathToNearestHubTile(g);
    }

    private Point? PickWanderTarget(Game g)
    {
        for (int tries = 0; tries < 12; tries++)
        {
            int x = (int)PosX + g.Rand.Next(-9, 10);
            int y = (int)PosY + g.Rand.Next(-9, 10);
            if (!g.World.WalkColonist(x, y)) continue;
            if (g.World.Cell(x, y).B is Garden || g.Rand.NextDouble() < 0.4)
                return new Point(x, y);
        }
        return null;
    }

    private void UpdateMorale(Game g)
    {
        float target = 58f;
        MoodFactors.Clear();
        void Add(string key, float d) { target += d; if (d != 0) MoodFactors.Add((key, d)); }

        if (State == ColState.Working) Add("busy at work", 18);
        else if (State == ColState.Idle) Add("idle and bored", -14);
        if (State == ColState.Flee) Add("fleeing in terror", -20);
        if (State == ColState.Heal) Add("recovering in safety", 6);
        if (State is ColState.Escort or ColState.GoCapture) Add("has purpose", 4); // purpose!

        // night shift grinds people down
        if (g.IsNight && State is not (ColState.Sleeping or ColState.Heal)) Add("night shift", -6);

        int ind = 0, comfort = 0;
        int px = (int)PosX, py = (int)PosY;
        for (int x = px - 4; x <= px + 4; x++)
            for (int y = py - 4; y <= py + 4; y++)
            {
                if (!g.World.InBounds(x, y)) continue;
                var b = g.World.Cell(x, y).B;
                if (b == null) continue;
                if (b.Comfort > 0) comfort += b.Comfort;
                else if (b.Industrial) ind++;
            }
        Add("pleasant surroundings", Math.Min(comfort, 16));
        Add("grimy industry", -Math.Min(ind, 5) * 5f);

        if (AteWellT > 0) Add("had a proper meal", 6);   // still riding a good meal
        // SOULS: the people around you are the biggest lever on mood
        foreach (var o in g.Cols)
        {
            if (o == this || o.Hp <= 0) continue;
            if (MathF.Abs(o.PosX - PosX) + MathF.Abs(o.PosY - PosY) > Bal.SocialRadius) continue;
            float op = GetOp(o);
            if (op >= Bal.FriendAt) { Add("a friend nearby", 6); break; }
        }
        foreach (var o in g.Cols)
        {
            if (o == this || o.Hp <= 0) continue;
            if (MathF.Abs(o.PosX - PosX) + MathF.Abs(o.PosY - PosY) > Bal.SocialRadius) continue;
            if (GetOp(o) <= Bal.RivalAt) { Add("a rival nearby", -5); break; }
        }
        if (GriefT > 0) Add("grieving", -18);
        if (CatharsisT > 0) Add("a weight lifted", 10);
        if (g.Hope) Add("hope for this world", 5);
        if (Has("Optimist")) Add("optimist", 12);
        if (Has("Pessimist")) Add("pessimist", -10);
        if (Hunger < 30) Add("starving", -10);
        if (Rest < 25) Add("exhausted", -8);
        if (Hp < MaxHp * 0.6f) Add("in pain", -8);
        if (g.RecentCombat > 0) Add("recent combat", -15);

        MoodTarget = target = Math.Clamp(target, 5, 96);
        float rate = target < Morale ? 0.9f : 0.35f;
        Morale += (target - Morale) * rate;
    }

    /// <summary>Machine speed multiplier from morale, traits, stats, flow.</summary>
    public float WorkSpeedFor(BuildKind k)
    {
        float f = 0.65f + (Morale / 100f) * 0.55f;
        if (Has("Industrious")) f *= 1.15f;
        if (Has("Lazy")) f *= 0.85f;

        int stat = Bal.WorkOf(k) is { } wt ? Stats[Bal.StatOf(wt)] : 5;
        f *= 0.75f + Math.Min(stat, 16) * 0.05f;

        if (k == BuildKind.Lab && Has("Genius")) f *= 1.4f;
        if (InFlow) f *= Bal.FlowSpeedBonus;
        return f;
    }
}
