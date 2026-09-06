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

        // 2) Path toward the hub perimeter.
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
