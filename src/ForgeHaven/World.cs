using System.Drawing;
using System.Text;

namespace ForgeHaven;

/// <summary>A single map tile. B2 is the ELEVATED layer (elevated rails only).</summary>
public sealed class Tile
{
    public Terrain T = Terrain.Ground;
    public Building? B;
    public Building? B2;
}

/// <summary>
///  World-generation settings, editable on the "Generate World" screen and
///  stored inside every save so the same world regenerates on load.
/// </summary>
public sealed class GenParams
{
    public long Seed;
    public float OreFreq = 1f;
    public float OreRich = 1f;
    public float RockDensity = 1f;
    public float Aggression = 1f;

    public GenParams Clone() => new()
    {
        Seed = Seed,
        OreFreq = OreFreq,
        OreRich = OreRich,
        RockDensity = RockDensity,
        Aggression = Aggression,
    };
}

/// <summary>A 32x32 lazily-generated slab of the open world.</summary>
public sealed class Chunk
{
    public const int S = 32;
    public readonly Tile[] Tiles = Create();
    public readonly float[] Pol = new float[S * S];   // pollution per tile
    public bool Revealed;
    public bool Dirty;

    private static Tile[] Create()
    {
        var t = new Tile[S * S];
        for (int i = 0; i < t.Length; i++) t[i] = new Tile();
        return t;
    }
}

/// <summary>
///  OPEN WORLD tile grid: deterministic 32x32 chunks from (seed, params),
///  fog of war, per-tile pollution, dictionary-based A*.
/// </summary>
public sealed class World
{
    public const int CS = Chunk.S;

    public readonly GenParams P;
    private readonly Dictionary<long, Chunk> _chunks = new();

    public World(GenParams p) { P = p; }

    public IEnumerable<KeyValuePair<long, Chunk>> AllChunks => _chunks;

    public static long Key(int cx, int cy) => (long)cx << 32 | (uint)cy;
    public static void Unpack(long key, out int cx, out int cy)
    {
        cx = (int)(key >> 32);
        cy = (int)(key & 0xFFFFFFFFL);
    }

    public Chunk ChunkAt(int cx, int cy)
    {
        var k = Key(cx, cy);
        if (_chunks.TryGetValue(k, out var c)) return c;
        c = new Chunk();
        GenChunk(c, cx, cy);
        _chunks[k] = c;
        return c;
    }

    public Chunk? ChunkOrNull(int cx, int cy) =>
        _chunks.TryGetValue(Key(cx, cy), out var c) ? c : null;

    public bool InBounds(int x, int y) => x > -500_000 && y > -500_000 && x < 500_000 && y < 500_000;

    public Tile Cell(int x, int y)
    {
        var c = ChunkAt(x >> 5, y >> 5);
        return c.Tiles[(x & 31) + (y & 31) * CS];
    }

    public float PollAt(int x, int y)
    {
        var c = ChunkOrNull(x >> 5, y >> 5);
        return c?.Pol[(x & 31) + (y & 31) * CS] ?? 0f;
    }

    public void AddPoll(int x, int y, float amt)
    {
        var c = ChunkAt(x >> 5, y >> 5);
        int i = (x & 31) + (y & 31) * CS;
        c.Pol[i] = Math.Min(Bal.PollMaxTile, c.Pol[i] + amt);
    }

    public void SetTerrain(int x, int y, Terrain t)
    {
        var c = ChunkAt(x >> 5, y >> 5);
        c.Tiles[(x & 31) + (y & 31) * CS].T = t;
        c.Dirty = true;
    }

    public void SetTerrainCircle(float cx, float cy, float r, Terrain t)
    {
        int x0 = (int)MathF.Floor(cx - r - 1), x1 = (int)MathF.Ceiling(cx + r + 1);
        int y0 = (int)MathF.Floor(cy - r - 1), y1 = (int)MathF.Ceiling(cy + r + 1);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            {
                float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d <= r - 0.7f || (d <= r + 0.6f && ((x * 31 + y * 17) % 7) < 3))
                    SetTerrain(x, y, t);
            }
    }

    // ---------------------------------------------------------- fog of war

    public bool RevealedAt(int x, int y) => ChunkOrNull(x >> 5, y >> 5)?.Revealed == true;

    public void RevealAround(int x, int y, int chunkRadius)
    {
        int cx = x >> 5, cy = y >> 5;
        for (int dx = -chunkRadius; dx <= chunkRadius; dx++)
            for (int dy = -chunkRadius; dy <= chunkRadius; dy++)
                ChunkAt(cx + dx, cy + dy).Revealed = true;
    }

    public void RevealKey(long key)
    {
        Unpack(key, out var cx, out var cy);
        ChunkAt(cx, cy).Revealed = true;
    }

    public List<long> RevealedKeys()
    {
        var list = new List<long>();
        foreach (var kv in _chunks)
            if (kv.Value.Revealed) list.Add(kv.Key);
        return list;
    }

    // --------------------------------------------------- chunk generation

    private void GenChunk(Chunk c, int cx, int cy)
    {
        ulong h = (ulong)P.Seed ^ ((ulong)(long)cx * 0x9E3779B97F4A7C15UL) ^ ((ulong)(long)cy * 0xC2B2AE3D27D4EB4FUL);
        var rng = new Random(unchecked((int)(h ^ (h >> 32))));

        Blob(c, rng, Terrain.IronOre, 0.90f * P.OreFreq, 2.0f * P.OreRich, 4.0f * P.OreRich);
        Blob(c, rng, Terrain.CopperOre, 0.75f * P.OreFreq, 1.6f * P.OreRich, 3.0f * P.OreRich);
        Blob(c, rng, Terrain.Crystal, 0.35f * P.OreFreq, 1.0f * P.OreRich, 2.2f * P.OreRich);
        Blob(c, rng, Terrain.Flora, 0.85f, 2.0f, 4.0f);
        Blob(c, rng, Terrain.Rock, 1.15f * P.RockDensity, 2.0f, 4.5f);
        Blob(c, rng, Terrain.Water, 0.30f, 3.0f, 6.5f);   // lakes
    }

    private static void Blob(Chunk c, Random rng, Terrain t, float rate, float rMin, float rMax)
    {
        int n = (int)rate;
        if (rng.NextDouble() < rate - n) n++;
        for (int i = 0; i < n; i++)
        {
            float bx = (float)(rng.NextDouble() * Chunk.S);
            float by = (float)(rng.NextDouble() * Chunk.S);
            float r = rMin + (float)rng.NextDouble() * Math.Max(0.01f, rMax - rMin);
            int x0 = Math.Max(0, (int)MathF.Floor(bx - r)), x1 = Math.Min(Chunk.S - 1, (int)MathF.Ceiling(bx + r));
            int y0 = Math.Max(0, (int)MathF.Floor(by - r)), y1 = Math.Min(Chunk.S - 1, (int)MathF.Ceiling(by + r));
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    float d = MathF.Sqrt((x - bx) * (x - bx) + (y - by) * (y - by));
                    if (d <= r + (float)rng.NextDouble() - 0.5f)
                        c.Tiles[x + y * Chunk.S].T = t;
                }
        }
    }

    // -------------------------------------------------------------- RLE io

    public string ChunkRle(Chunk c)
    {
        var sb = new StringBuilder();
        int run = 0; int cur = -1;
        for (int i = 0; i < c.Tiles.Length; i++)
        {
            int t = (int)c.Tiles[i].T;
            if (t == cur) run++;
            else
            {
                if (cur >= 0) sb.Append(run).Append(':').Append(cur).Append(',');
                cur = t; run = 1;
            }
        }
        sb.Append(run).Append(':').Append(cur);
        return sb.ToString();
    }

    public void ApplyChunkRle(long key, string rle)
    {
        Unpack(key, out var cx, out var cy);
        var c = ChunkAt(cx, cy);
        int i = 0;
        foreach (var run in rle.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = run.Split(':');
            int count = int.Parse(parts[0]);
            var terr = (Terrain)int.Parse(parts[1]);
            for (int k = 0; k < count && i < c.Tiles.Length; k++, i++)
                c.Tiles[i].T = terr;
        }
        c.Dirty = true;
    }

    /// <summary>Pollution quantized to bytes, RLE-encoded.</summary>
    public string PollRle(Chunk c)
    {
        var sb = new StringBuilder();
        int run = 0; int cur = -1;
        for (int i = 0; i < c.Pol.Length; i++)
        {
            int v = (int)(c.Pol[i] / Bal.PollMaxTile * 255f + 0.5f);
            if (v == cur) run++;
            else
            {
                if (cur >= 0) sb.Append(run).Append(':').Append(cur).Append(',');
                cur = v; run = 1;
            }
        }
        sb.Append(run).Append(':').Append(cur);
        return sb.ToString();
    }

    public void ApplyPollRle(long key, string rle)
    {
        Unpack(key, out var cx, out var cy);
        var c = ChunkAt(cx, cy);
        int i = 0;
        foreach (var run in rle.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = run.Split(':');
            int count = int.Parse(parts[0]);
            float v = int.Parse(parts[1]) / 255f * Bal.PollMaxTile;
            for (int k = 0; k < count && i < c.Pol.Length; k++, i++)
                c.Pol[i] = v;
        }
    }

    /// <summary>Legacy import: a fixed-size RLE rectangle (v0.2 saves).</summary>
    public void ImportRleGrid(int w, int h, string rle)
    {
        int ti = 0;
        foreach (var run in rle.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = run.Split(':');
            int count = int.Parse(parts[0]);
            var terr = (Terrain)int.Parse(parts[1]);
            for (int i = 0; i < count && ti < w * h; i++, ti++)
                SetTerrain(ti % w, ti / w, terr);
        }
        for (int cx = -1; cx <= (w >> 5) + 1; cx++)
            for (int cy = -1; cy <= (h >> 5) + 1; cy++)
                ChunkAt(cx, cy).Revealed = true;
    }

    public List<(long key, string rle)> DirtyChunkRles()
    {
        var list = new List<(long, string)>();
        foreach (var kv in _chunks)
            if (kv.Value.Dirty) list.Add((kv.Key, ChunkRle(kv.Value)));
        return list;
    }

    // ------------------------------------------------------------- buildings

    /// <summary>Traps are walkable by both colonists and raiders.</summary>
    public static bool IsWalkableBuilding(Building? b) =>
        b == null || b.Kind is BuildKind.SpikeTrap or BuildKind.IED;

    public bool WalkColonist(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        var t = Cell(x, y);
        if (t.T is Terrain.Rock or Terrain.Water) return false;
        if (!IsWalkableBuilding(t.B) && t.B is not (Belt or Garden or Hub or Hab)) return false;
        return true;   // elevated rail (B2) is overhead — always passable
    }

    public bool WalkEnemy(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        var t = Cell(x, y);
        if (t.T is Terrain.Rock or Terrain.Water) return false;
        return IsWalkableBuilding(t.B);
    }

    public void SetBuilding(Building b)
    {
        for (int x = b.X; x < b.X + b.W; x++)
            for (int y = b.Y; y < b.Y + b.H; y++)
                if (InBounds(x, y)) Cell(x, y).B = b;
    }

    public void ClearBuilding(Building b)
    {
        for (int x = b.X; x < b.X + b.W; x++)
            for (int y = b.Y; y < b.Y + b.H; y++)
                if (InBounds(x, y) && Cell(x, y).B == b) Cell(x, y).B = null;
    }

    /// <summary>Elevated rails live on the second layer, over anything.</summary>
    public void SetBuilding2(Building b)
    {
        for (int x = b.X; x < b.X + b.W; x++)
            for (int y = b.Y; y < b.Y + b.H; y++)
                if (InBounds(x, y)) Cell(x, y).B2 = b;
    }

    public void ClearBuilding2(Building b)
    {
        for (int x = b.X; x < b.X + b.W; x++)
            for (int y = b.Y; y < b.Y + b.H; y++)
                if (InBounds(x, y) && Cell(x, y).B2 == b) Cell(x, y).B2 = null;
    }

    /// <summary>A rail tile trains can travel over (ground rail or elevated).</summary>
    public bool IsRailAt(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        var t = Cell(x, y);
        return t.B is Rail or TrainStop || t.B2?.Kind == BuildKind.ElevatedRail;
    }

    // ---------------------------------------------------------------
    //  A* over the infinite grid (dictionary-based, node-capped).
    // ---------------------------------------------------------------

    public List<Point>? FindPath(int sx, int sy, int gx, int gy, bool enemy, int maxNodes = 9000)
    {
        if (!InBounds(sx, sy) || !InBounds(gx, gy)) return null;
        if (sx == gx && sy == gy) return new List<Point>();

        bool Passable(int x, int y)
        {
            if (x == gx && y == gy) return true;
            return enemy ? WalkEnemy(x, y) : WalkColonist(x, y);
        }

        if (!Passable(sx, sy))
        {
            bool fixedStart = false;
            for (int d = 0; d < 4 && !fixedStart; d++)
            {
                int nx = sx + DirU.Dx[d], ny = sy + DirU.Dy[d];
                if (InBounds(nx, ny) && (enemy ? WalkEnemy(nx, ny) : WalkColonist(nx, ny)))
                { sx = nx; sy = ny; fixedStart = true; }
            }
            if (!fixedStart) return null;
        }

        int minX = Math.Min(sx, gx) - 30, maxX = Math.Max(sx, gx) + 30;
        int minY = Math.Min(sy, gy) - 30, maxY = Math.Max(sy, gy) + 30;

        var gScore = new Dictionary<long, float>();
        var came = new Dictionary<long, int>();
        var open = new PriorityQueue<long, float>();

        static long K(int x, int y) => (long)x << 32 | (uint)y;
        float H_(int x, int y)
        {
            float dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
            return Math.Max(dx, dy) + 0.41f * Math.Min(dx, dy);
        }

        long sk = K(sx, sy);
        gScore[sk] = 0;
        open.Enqueue(sk, H_(sx, sy));
        int expanded = 0;
        long goalKey = K(gx, gy);
        bool found = false;

        while (open.Count > 0 && expanded < maxNodes)
        {
            var k = open.Dequeue();
            expanded++;
            int x = (int)(k >> 32), y = (int)(k & 0xFFFFFFFFL);
            if (k == goalKey) { found = true; break; }
            if (!gScore.TryGetValue(k, out var g0)) continue;

            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < minX || nx > maxX || ny < minY || ny > maxY) continue;
                    if (!Passable(nx, ny)) continue;
                    if (dx != 0 && dy != 0 &&
                        (!Passable(x + dx, y) || !Passable(x, y + dy)))
                        continue;

                    float cost = g0 + ((dx != 0 && dy != 0) ? 1.414f : 1f);
                    long nk = K(nx, ny);
                    if (cost < gScore.GetValueOrDefault(nk, float.MaxValue))
                    {
                        gScore[nk] = cost;
                        came[nk] = (dy + 1) * 3 + (dx + 1);
                        open.Enqueue(nk, cost + H_(nx, ny));
                    }
                }
        }

        if (!found) return null;

        var path = new List<Point>();
        int cx = gx, cy = gy;
        while (!(cx == sx && cy == sy))
        {
            path.Add(new Point(cx, cy));
            if (!came.TryGetValue(K(cx, cy), out var e) || e < 0) break;
            int pdx = e % 3 - 1, pdy = e / 3 - 1;
            cx -= pdx; cy -= pdy;
        }
        path.Reverse();
        return path;
    }
}
