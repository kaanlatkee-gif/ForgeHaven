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

    // cosmetics (stable across save/load via name hash)
    public int ShirtTone, SkinTone, HairTone;

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
        DeriveCosmetics();
    }

    public void DeriveCosmetics()
    {
        int h = 0;
        foreach (var ch in Name) h = h * 31 + ch;
        ShirtTone = Math.Abs(h) % 6;
        SkinTone = Math.Abs(h / 7) % 4;
        HairTone = Math.Abs(h / 29) % 6;
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

        // ---- needs decay & slow natural regen
        Hunger -= Bal.ColHungerRate * dt * (Has("Frugal") ? 0.75f : 1f);
        Rest -= Bal.ColRestRate * dt * (State is ColState.Sleeping or ColState.Heal ? 0 : 1);
        if (Hunger <= 0) { Hunger = 0; Hp -= 1.5f * dt; }
        else if (Hp < MaxHp && State != ColState.Flee) Hp = Math.Min(MaxHp, Hp + Bal.ColRegenRate * dt);

        if (Hp <= 0) { g.AddLog($"{Name} has died.", Pal.Bad); g.ReleaseFromJob(this); return; }

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
        else if (Has("Brave"))
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

        // ---- flee check (non-brave)
        bool danger = false;
        if (!Has("Brave"))
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
                if (!MoveAlong(g, dt, 1f)) { State = ColState.Eating; ActTimer = 1.2f; }
                break;

            case ColState.Eating:
                ActTimer -= dt;
                if (ActTimer <= 0)
                {
                    if (g.HubRef.Stock[(int)ItemKind.Food] > 0)
                    {
                        g.HubRef.Stock[(int)ItemKind.Food]--;
                        Hunger = Math.Min(100, Hunger + 70);
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
        }

        // ---- morale model
        _envTimer -= dt;
        if (_envTimer <= 0)
        {
            _envTimer = 1f;
            UpdateMorale(g);
        }
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
        if (Hunger < 28) { State = ColState.GoEat; PathToNearestHubTile(g); return; }

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

    private void BedRelease()
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

    private void PathToNearestHubTile(Game g)
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
        Path = g.World.FindPath((int)PosX, (int)PosY, bestX, bestY, enemy: false);
        PathIdx = 0;
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

        if (State == ColState.Working) target += 18;
        else if (State == ColState.Idle) target -= 14;
        if (State == ColState.Flee) target -= 20;
        if (State == ColState.Heal) target += 6;
        if (State is ColState.Escort or ColState.GoCapture) target += 4; // purpose!

        // night shift grinds people down
        if (g.IsNight && State is not (ColState.Sleeping or ColState.Heal)) target -= 6;

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
        target += Math.Min(comfort, 16);
        target -= Math.Min(ind, 5) * 5f;

        if (Has("Optimist")) target += 12;
        if (Has("Pessimist")) target -= 10;
        if (Hunger < 30) target -= 10;
        if (Rest < 25) target -= 8;
        if (Hp < MaxHp * 0.6f) target -= 8;
        if (g.RecentCombat > 0) target -= 15;

        target = Math.Clamp(target, 5, 96);
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
