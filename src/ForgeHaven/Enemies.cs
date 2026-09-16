using System.Drawing;

namespace ForgeHaven;

// ---------------------------------------------------------------------------
//  Raiders: alien wildlife waves that march on the Hub. Wealth-scaled waves,
//  ring spawning on the open world, downed/captured states for the prisoner
//  system.
// ---------------------------------------------------------------------------

public sealed class Raider
{
    public float PosX, PosY;
    public float Hp, MaxHp;
    public float Dps, Speed;
    public bool Apex;
    public List<Point>? Path;
    public int PathIdx;
    public float Repath;
    public float AttackCd;

    // combat feedback
    public float FlashT;
    public float LungeT, LungeDx, LungeDy;
    public float WalkPhase;
    public float FaceAngle;

    // prisoner system
    public bool Downed;          // helpless, bleeds out unless captured
    public bool Captured;        // being escorted / interned
    public Colonist? Captor;

    // MADDOG iter-8: sappers deliberately breach walls/doors
    public bool Sapper;
    public Building? SapTarget;

    public Raider(float x, float y, bool apex, float hpBonus)
    {
        PosX = x; PosY = y; Apex = apex;
        MaxHp = Hp = (apex ? Bal.ApexHp : Bal.RaiderHp) + hpBonus;
        Dps = apex ? Bal.ApexDps : Bal.RaiderDps;
        Speed = apex ? Bal.ApexSpeed : Bal.RaiderSpeed;
    }

    public static Raider? Nearest(Game g, float x, float y, float range)
    {
        Raider? best = null; float bd = float.MaxValue;
        foreach (var r in g.Foes)
        {
            if (r.Hp <= 0 || r.Downed) continue;
            float dx = r.PosX - x, dy = r.PosY - y;
            float d = dx * dx + dy * dy;
            if (d < range * range && d < bd) { bd = d; best = r; }
        }
        return best;
    }

    public void Update(Game g, float dt)
    {
        if (Hp <= 0) return;

        if (Captured) return;   // the captor moves us (Escort state)

        if (Downed)
        {
            // bleed out slowly unless someone comes to collect us
            Hp -= Bal.DownedBleed * dt;
            if (Hp <= 0) g.AddLog("A downed horror bled out.", Pal.TextDim);
            return;
        }

        AttackCd -= dt;
        Repath -= dt;
        FlashT = Math.Max(0, FlashT - dt);
        LungeT = Math.Max(0, LungeT - dt);
        WalkPhase += dt * Speed * 3f;

        // 1) Maul adjacent colonists (they will fight back — see Colonist).
        Colonist? meal = null;
        foreach (var c in g.Cols)
        {
            if (c.Hp <= 0) continue;
            float d = Math.Abs(c.PosX - PosX) + Math.Abs(c.PosY - PosY);
            if (d < 1.2f) { meal = c; break; }
        }
        if (meal != null)
        {
            if (AttackCd <= 0)
            {
                AttackCd = 0.9f;
                StartLunge(meal.PosX, meal.PosY);
                g.DamageColonist(meal, Dps * 0.9f, PosX, PosY);
                g.RecentCombat = 2f;
                g.SpawnSpark(meal.PosX, meal.PosY, Pal.Enemy);
            }
            return;
        }

        // 1.5) MADDOG iter-8: sappers beeline for the nearest wall/door and
        // smash it even when a path around exists - walls alone are not safety.
        if (Sapper && SapperTick(g, dt)) return;

        // 2) Path toward the hub perimeter.
        // (bugfix: a raider with no path used to re-run A* EVERY frame until
        // it found one - cheap for one raider, a stampede for a whole wave)
        if (Path == null && Repath > 0) { AttackBlocked(g, dt); return; }
        if (Repath <= 0 || Path == null || PathIdx >= Path.Count)
        {
            Repath = 2f + (float)g.Rand.NextDouble();
            var hub = g.HubRef;
            int ringLen = 2 * hub.W + 2 * hub.H;
            int ring = g.Rand.Next(0, ringLen);
            int tx, ty;
            if (ring < hub.W) { tx = hub.X + ring; ty = hub.Y; }
            else if (ring < hub.W + hub.H) { tx = hub.X + hub.W - 1; ty = hub.Y + (ring - hub.W); }
            else if (ring < 2 * hub.W + hub.H) { tx = hub.X + hub.W - 1 - (ring - hub.W - hub.H); ty = hub.Y + hub.H - 1; }
            else { tx = hub.X; ty = hub.Y + hub.H - 1 - (ring - 2 * hub.W - hub.H); }
            Path = g.World.FindPath((int)PosX, (int)PosY, tx, ty, enemy: true);
            PathIdx = 0;
            if (Path == null) { AttackBlocked(g, dt); return; }
        }

        if (Path != null && PathIdx < Path.Count)
        {
            var next = Path[PathIdx];
            var tile = g.World.Cell(next.X, next.Y);
            if (!g.World.WalkEnemy(next.X, next.Y))
            {
                float dx = next.X + 0.5f - PosX, dy = next.Y + 0.5f - PosY;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (tile.B != null && d < 1.7f)
                {
                    if (AttackCd <= 0)
                    {
                        AttackCd = 0.8f;
                        StartLunge(next.X + 0.5f, next.Y + 0.5f);
                        g.DamageBuilding(tile.B, Dps * 0.8f);
                        g.RecentCombat = 2f;
                        g.SpawnSpark(next.X + 0.5f, next.Y + 0.5f, Pal.Bad);
                    }
                }
                else if (d > 1.3f)
                {
                    float approach = Speed * dt;
                    PosX += dx / d * approach;
                    PosY += dy / d * approach;
                    FaceAngle = MathF.Atan2(dy, dx);
                }
                return;
            }

            float txp = next.X + 0.5f, typ = next.Y + 0.5f;
            float ddx = txp - PosX, ddy = typ - PosY;
            float dist = MathF.Sqrt(ddx * ddx + ddy * ddy);
            float step = Speed * dt;
            FaceAngle = MathF.Atan2(ddy, ddx);
            if (dist <= step) { PosX = txp; PosY = typ; PathIdx++; }
            else { PosX += ddx / dist * step; PosY += ddy / dist * step; }
        }
    }

    /// <summary>MADDOG iter-8: pick the nearest wall/door, walk to it, break
    /// it. Falls back to normal ring-marching when nothing fortified remains.</summary>
    private bool SapperTick(Game g, float dt)
    {
        if (SapTarget == null || SapTarget.Hp <= 0)
        {
            SapTarget = null;
            float bd = float.MaxValue;
            foreach (var b in g.Builds)
            {
                if (b.Hp <= 0 || b is not (Wall or Door)) continue;
                float d = MathF.Abs(b.X + 0.5f - PosX) + MathF.Abs(b.Y + 0.5f - PosY);
                if (d < bd) { bd = d; SapTarget = b; }
            }
            if (SapTarget == null) { Sapper = false; return false; }   // nothing to breach
            Path = null;                                            // replan at the new target
        }

        var t = SapTarget;
        float dist = MathF.Abs(t.X + t.W / 2f - PosX) + MathF.Abs(t.Y + t.H / 2f - PosY);

        if (dist < 2.1f)
        {
            // swing at the wall segment
            if (AttackCd <= 0)
            {
                AttackCd = 0.8f;
                StartLunge(t.X + t.W / 2f, t.Y + t.H / 2f);
                g.DamageBuilding(t, Dps * 0.9f);         // breaching tools hit harder
                g.RecentCombat = 2f;
                g.SpawnSpark(t.X + 0.5f, t.Y + 0.5f, Pal.Bad);
            }
            return true;
        }

        if (Repath <= 0 || Path == null || PathIdx >= Path.Count)
        {
            Repath = 1.5f + (float)g.Rand.NextDouble();
            // goal = walkable tile ADJACENT to the wall footprint (the wall
            // tile itself is unwalkable and would dead-end the A*)
            int gx = -1, gy = -1;
            float gBest = float.MaxValue;
            for (int ox = -1; ox <= t.W; ox++)
                for (int oy = -1; oy <= t.H; oy++)
                {
                    if (ox >= 0 && ox < t.W && oy >= 0 && oy < t.H) continue;   // inside footprint
                    int ax = t.X + ox, ay = t.Y + oy;
                    if (!g.World.WalkEnemy(ax, ay)) continue;
                    float dd = MathF.Abs(ax + 0.5f - PosX) + MathF.Abs(ay + 0.5f - PosY);
                    if (dd < gBest) { gBest = dd; gx = ax; gy = ay; }
                }
            Path = gx >= 0
                ? g.World.FindPath((int)PosX, (int)PosY, gx, gy, enemy: true)
                : null;
            PathIdx = 0;
            if (Path == null)
            {
                // can't route to it: walk straight at it, chewing as we go
                float dx = t.X + 0.5f - PosX, dy = t.Y + 0.5f - PosY;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 0.01f)
                {
                    PosX += dx / len * Speed * dt;
                    PosY += dy / len * Speed * dt;
                    FaceAngle = MathF.Atan2(dy, dx);
                }
                return true;
            }
        }

        if (Path != null && PathIdx < Path.Count)
        {
            var next = Path[PathIdx];
            if (!g.World.WalkEnemy(next.X, next.Y))
            {
                var blocker = g.World.Cell(next.X, next.Y).B;
                if (blocker != null && AttackCd <= 0)
                {
                    AttackCd = 0.8f;
                    StartLunge(next.X + 0.5f, next.Y + 0.5f);
                    g.DamageBuilding(blocker, Dps * 0.9f);
                    g.RecentCombat = 2f;
                }
                return true;
            }
            float txp = next.X + 0.5f, typ = next.Y + 0.5f;
            float ddx = txp - PosX, ddy = typ - PosY;
            float step = MathF.Sqrt(ddx * ddx + ddy * ddy);
            FaceAngle = MathF.Atan2(ddy, ddx);
            if (step <= Speed * dt) { PosX = txp; PosY = typ; PathIdx++; }
            else { PosX += ddx / step * Speed * dt; PosY += ddy / step * Speed * dt; }
        }
        return true;
    }

    public void StartLunge(float tx, float ty)
    {
        float dx = tx - PosX, dy = ty - PosY;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.01f) return;
        LungeDx = dx / len; LungeDy = dy / len;
        LungeT = 0.18f;
    }

    /// <summary>No path found: demolish the nearest thing in reach, or march
    /// straight at the Hub (open-world fallback).</summary>
    private void AttackBlocked(Game g, float dt)
    {
        if (AttackCd > 0) { MarchTowardHub(g, dt); return; }
        int px = (int)PosX, py = (int)PosY;
        Building? target = null;
        for (int x = px - 1; x <= px + 1; x++)
            for (int y = py - 1; y <= py + 1; y++)
                if (g.World.InBounds(x, y) && g.World.Cell(x, y).B != null)
                {
                    target = g.World.Cell(x, y).B;
                    break;
                }
        if (target != null)
        {
            AttackCd = 0.8f;
            StartLunge(target.X + target.W / 2f, target.Y + target.H / 2f);
            g.DamageBuilding(target, Dps * 0.8f);
            g.RecentCombat = 2f;
            g.SpawnSpark(target.X + 0.5f, target.Y + 0.5f, Pal.Bad);
        }
        else
        {
            MarchTowardHub(g, dt);
            Repath = 0.5f;
        }
    }

    private void MarchTowardHub(Game g, float dt)
    {
        var hub = g.HubRef;
        float dx = hub.X + hub.W / 2f - PosX, dy = hub.Y + hub.H / 2f - PosY;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.01f) return;
        FaceAngle = MathF.Atan2(dy, dx);
        PosX += dx / len * Speed * dt;
        PosY += dy / len * Speed * dt;
    }
}


// ---------------------------------------------------------------------------
//  MADDOG iter-4: wildlife (G12). Peaceful grazers that wander the map in
//  small herds; drafted pawns can hunt them, carcasses are hauled as Food.
// ---------------------------------------------------------------------------

/// <summary>A passive grazer. Flees when hurt, never fights back.</summary>
public sealed class Beast
{
    public float PosX, PosY;
    public float Hp, MaxHp;
    public float Speed;
    public float HomeX, HomeY;        // herd anchor: wandering stays near it
    public float WanderT, FleeT;
    public float WalkPhase, FaceAngle, FlashT;

    public Beast(float x, float y)
    {
        PosX = x; PosY = y; HomeX = x; HomeY = y;
        MaxHp = Hp = Bal.BeastHp;
        Speed = Bal.BeastSpeed;
        WanderT = 1f;
    }

    /// <summary>Can a beast stand on this tile? (plain ground, no buildings)</summary>
    private static bool Standable(Game g, int x, int y)
    {
        if (!g.World.InBounds(x, y)) return false;
        var t = g.World.Cell(x, y);
        return t.T is not (Terrain.Rock or Terrain.Water) && t.B == null
               && !g.TileBlighted(x, y);
    }

    public void Update(Game g, float dt)
    {
        if (Hp <= 0) return;
        FlashT = MathF.Max(0, FlashT - dt);
        FleeT = MathF.Max(0, FleeT - dt);

        float dirX = 0, dirY = 0;
        if (FleeT > 0)
        {
            // panic: straight away from the nearest colonist
            Colonist? scary = null; float bd = float.MaxValue;
            foreach (var c in g.Cols)
            {
                if (c.Hp <= 0) continue;
                float d = MathF.Abs(c.PosX - PosX) + MathF.Abs(c.PosY - PosY);
                if (d < bd) { bd = d; scary = c; }
            }
            if (scary != null)
            {
                float dx = PosX - scary.PosX, dy = PosY - scary.PosY;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 0.01f) { dirX = dx / len; dirY = dy / len; }
            }
        }
        else
        {
            // graze: every few seconds, drift one tile toward a spot near home
            WanderT -= dt;
            if (WanderT <= 0)
            {
                WanderT = Bal.BeastWanderMin +
                    (float)g.Rand.NextDouble() * (Bal.BeastWanderMax - Bal.BeastWanderMin);
                float ang = (float)(g.Rand.NextDouble() * Math.PI * 2);
                float tx = HomeX + MathF.Cos(ang) * 9f, ty = HomeY + MathF.Sin(ang) * 9f;
                float dx = tx - PosX, dy = ty - PosY;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 0.5f) { dirX = dx / len; dirY = dy / len; }
            }
        }

        if (dirX == 0 && dirY == 0) return;
        float sp = (FleeT > 0 ? Speed * 1.6f : Speed * 0.55f) * dt;
        float nx = PosX + dirX * sp, ny = PosY + dirY * sp;
        if (Standable(g, (int)nx, (int)ny)) { PosX = nx; PosY = ny; WalkPhase += dt * 6f; }
        else if (Standable(g, (int)(PosX + dirY * sp), (int)(PosY - dirX * sp)))
        { PosX += dirY * sp; PosY -= dirX * sp; WalkPhase += dt * 6f; }   // sidestep
        FaceAngle = MathF.Atan2(dirY, dirX);
    }
}

/// <summary>A fallen grazer: decays unless a colonist hauls it as Food.</summary>
public sealed class Carcass
{
    public float X, Y;
    public int Food;
    public float T;                   // seconds before it rots away
    public Colonist? Gatherer;        // who claimed the haul

    public Carcass(float x, float y, int food)
    { X = x; Y = y; Food = food; T = 300f; }
}
