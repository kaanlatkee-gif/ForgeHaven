using System.Drawing;
using System.Text;

namespace ForgeHaven;

/// <summary>A single map tile. B2 is the ELEVATED layer (elevated rails only).</summary>
public sealed class Tile
{
    public Terrain T = Terrain.Ground;
    public Building? B;
    public Building? B2;
    public int Ore;                // Phase 2: richness units left (0 = uninit for ore tiles)
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
    public int Biome;                 // NEW WORLDS (Biome enum; 0 = Verdant)
    public int LandingX, LandingY;    // chosen landing site (world tiles; 0,0 = default)
    public float ForestMul = 1f;      // TERRAIN PASS: player sliders
    public float WaterMul = 1f;

    public GenParams Clone() => new()
    {
        Seed = Seed,
        OreFreq = OreFreq,
        OreRich = OreRich,
        RockDensity = RockDensity,
        Aggression = Aggression,
        Biome = Biome,
        LandingX = LandingX,
        LandingY = LandingY,
        ForestMul = ForestMul,
        WaterMul = WaterMul,
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

    /// <summary>Bumped on every terrain change — the renderer's terrain
    /// cache watches this to know when chunk bitmaps went stale.</summary>
    public long Serial;

    /// <summary>Ore units left on a tile. Lazily initialized per ore tile
    /// with GenParams.OreRich-scaled variance (deterministic position hash),
    /// so old saves and stamped patches work without migration.</summary>
    public int OreAt(int x, int y)
    {
        var t = Cell(x, y);
        if (t.T is not (Terrain.IronOre or Terrain.CopperOre or Terrain.Crystal or Terrain.Flora or Terrain.Tree))
            return 0;
        if (t.Ore == 0)
        {
            long h = ((long)x * 374761393 + (long)y * 668265263) & 0x7FFFFFFF;
            float var = 0.7f + (h % 1000) / 1000f * 0.6f;      // 0.7..1.3
            t.Ore = (int)(Bal.OrePerTile * P.OreRich * var);
            if (t.Ore < 1) t.Ore = 1;
        }
        return t.Ore;
    }

    /// <summary>Take one unit of ore from a tile; Ground when empty.</summary>
    public bool DepleteOre(int x, int y)
    {
        var t = Cell(x, y);
        if (t.Ore == 0) t.Ore = OreAt(x, y);
        if (t.Ore <= 0) return false;
        t.Ore--;
        if (t.Ore == 0) SetTerrain(x, y, Terrain.Ground);
        return true;
    }

    public void SetTerrain(int x, int y, Terrain t)
    {
        Serial++;
        var c = ChunkAt(x >> 5, y >> 5);
        c.Tiles[(x & 31) + (y & 31) * CS].T = t;
        c.Dirty = true;
    }

    /// <summary>Turn any boxed-in water tiles with no water neighbours back
    /// to ground (guaranteed-paint areas can strand pieces of a noise lake).
    /// Swept to a fixed point so capes collapse fully.</summary>
    public void DemoteOrphanWater(int x0, int y0, int x1, int y1)
    {
        for (int round = 0; round < 4; round++)
        {
            bool changed = false;
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    if (!InBounds(x, y) || Cell(x, y).T != Terrain.Water) continue;
                    bool any = (InBounds(x + 1, y) && Cell(x + 1, y).T == Terrain.Water)
                            || (InBounds(x - 1, y) && Cell(x - 1, y).T == Terrain.Water)
                            || (InBounds(x, y + 1) && Cell(x, y + 1).T == Terrain.Water)
                            || (InBounds(x, y - 1) && Cell(x, y - 1).T == Terrain.Water);
                    if (!any) { SetTerrain(x, y, Terrain.Ground); changed = true; }
                }
            if (!changed) break;
        }
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

    // --------------------------------------------- coherent value noise --
    // Pure function of (seed, x, y): identical across chunk loads, so
    // forests and lakes span chunk borders without any cross-chunk state.

    private static float Lattice(long seed, int x, int y)
    {
        ulong h = (ulong)seed;
        h ^= (ulong)x * 0x9E3779B97F4A7C15UL + (ulong)y * 0xC2B2AE3D27D4EB4FUL + 0x165667B19E3779F9UL;
        h ^= h >> 33; h *= 0xFF51AFD7ED558CCDUL;
        h ^= h >> 33; h *= 0xC4CEB9FE1A85EC53UL;
        h ^= h >> 33;
        return h / (float)ulong.MaxValue;
    }

    /// <summary>Smooth two-octave value noise in [0,1].</summary>
    public static float Noise(long seed, float x, float y)
    {
        float Layer(float fx, float fy)
        {
            int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float a = Lattice(seed, x0, y0), b = Lattice(seed, x0 + 1, y0);
            float c = Lattice(seed, x0, y0 + 1), d = Lattice(seed, x0 + 1, y0 + 1);
            return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
        }
        return Layer(x, y) * 0.78f + Layer(x * 2.17f + 31.4f, y * 2.17f - 17.2f) * 0.22f;
    }

    private void GenChunk(Chunk c, int cx, int cy)
    {
        ulong h = (ulong)P.Seed ^ ((ulong)(long)cx * 0x9E3779B97F4A7C15UL) ^ ((ulong)(long)cy * 0xC2B2AE3D27D4EB4FUL);
        var rng = new Random(unchecked((int)(h ^ (h >> 32))));

        // NEW WORLDS: the biome bends the whole table
        float ironMul = 1f, floraMul = 1f, waterMul = 1f, rockMul = 1f, crysMul = 1f;
        switch ((Biome)P.Biome)
        {
            case Biome.Volcanic:
                crysMul = 1.8f; waterMul = 0.4f; rockMul = 1.45f; floraMul = 0.7f;
                break;
            case Biome.Glacial:
                waterMul = 0.5f; ironMul = 1.35f; floraMul = 0.55f; rockMul = 1.2f;
                break;
            case Biome.Fungal:
                floraMul = 1.9f; crysMul = 0.6f;
                break;
        }
        // ore & rock stay as rich patches (that's what ore IS)...
        Blob(c, rng, Terrain.IronOre, 0.90f * P.OreFreq * ironMul, 2.0f * P.OreRich, 4.0f * P.OreRich);
        Blob(c, rng, Terrain.CopperOre, 0.75f * P.OreFreq, 1.6f * P.OreRich, 3.0f * P.OreRich);
        Blob(c, rng, Terrain.Crystal, 0.35f * P.OreFreq * crysMul, 1.0f * P.OreRich, 2.2f * P.OreRich);
        Blob(c, rng, Terrain.Rock, 1.15f * P.RockDensity * rockMul, 2.0f, 4.5f);

        // ...but forests and water are LANDSCAPE now: coherent noise across
        // chunk borders, with the biome bending the thresholds.
        long fSeed = P.Seed ^ 0x5EEDF0E5L, wSeed = P.Seed ^ 0x1A7E2BE7L;
        // sliders + biome bend the thresholds (higher thr = less terrain)
        float fThr = Bal.ForestNoiseThr - (floraMul - 1f) * 0.16f - (P.ForestMul - 1f) * 0.13f;
        float wThr = Bal.WaterNoiseThr + (waterMul < 1f ? (1f - waterMul) * 0.20f : 0f)
                   - (P.WaterMul - 1f) * 0.10f;
        bool Watery(int wx, int wy) => Noise(wSeed, wx * Bal.WaterNoiseFreq, wy * Bal.WaterNoiseFreq) > wThr;
        // pass 1 candidate: deep noise with at least 2 noisy neighbours.
        // pass 2: a candidate only floods if a neighbouring candidate does
        // too - orphans are impossible, and it is a pure function of (seed,
        // world coords) so it holds across chunk borders.
        bool Candidate(int wx, int wy)
        {
            if (!Watery(wx, wy)) return false;
            int nb = (Watery(wx + 1, wy) ? 1 : 0) + (Watery(wx - 1, wy) ? 1 : 0)
                   + (Watery(wx, wy + 1) ? 1 : 0) + (Watery(wx, wy - 1) ? 1 : 0);
            return nb >= 2;
        }
        for (int x = 0; x < Chunk.S; x++)
            for (int y = 0; y < Chunk.S; y++)
            {
                int wx = cx * Chunk.S + x, wy = cy * Chunk.S + y;
                var t = c.Tiles[x + y * Chunk.S].T;
                if (t == Terrain.Ground && P.ForestMul > 0f
                    && Noise(fSeed, wx * Bal.ForestNoiseFreq, wy * Bal.ForestNoiseFreq) > fThr)
                    t = Terrain.Tree;
                // water floods everything but bare rock (lakes may drown
                // ore veins - same as the old blob generator did)
                if (t is not Terrain.Rock && P.WaterMul > 0f
                    && Candidate(wx, wy)
                    && (Candidate(wx + 1, wy) || Candidate(wx - 1, wy)
                        || Candidate(wx, wy + 1) || Candidate(wx, wy - 1)))
                    t = Terrain.Water;
                c.Tiles[x + y * Chunk.S].T = t;
            }

        // pass 3: demote any water tile whose four ASSIGNED neighbours are
        // dry - real terrain where the neighbour chunk exists, a noise
        // estimate where it does not. Swept to a fixed point so demoting a
        // peninsula tile can't strand the tile behind it.
        for (int round = 0; round < 4; round++)
        {
            bool changed = false;
            for (int x = 0; x < Chunk.S; x++)
                for (int y = 0; y < Chunk.S; y++)
                {
                    if (c.Tiles[x + y * Chunk.S].T != Terrain.Water) continue;
                    int wx = cx * Chunk.S + x, wy = cy * Chunk.S + y;
                    bool any = false;
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = wx + dx, ny = wy + dy;
                        int lx = nx - cx * Chunk.S, ly = ny - cy * Chunk.S;
                        if (lx >= 0 && lx < Chunk.S && ly >= 0 && ly < Chunk.S)
                            any |= c.Tiles[lx + ly * Chunk.S].T == Terrain.Water;
                        else
                        {
                            var nch = ChunkOrNull((int)MathF.Floor(nx / 32f), (int)MathF.Floor(ny / 32f));
                            any |= nch == null ? Candidate(nx, ny)
                                  : nch.Tiles[(nx & 31) + (ny & 31) * Chunk.S].T == Terrain.Water;
                        }
                    }
                    if (!any) { c.Tiles[x + y * Chunk.S].T = Terrain.Ground; changed = true; }
                }
            if (!changed) break;
        }

        // this chunk is final now: re-sweep the adjacent border strips of any
        // ALREADY-GENERATED neighbours - their pass-3 may have kept a tile on
        // an optimistic estimate of this chunk that we just contradicted
        for (int side = 0; side < 4; side++)
        {
            int ox = side == 0 ? cx + 1 : side == 1 ? cx - 1 : cx;
            int oy = side == 2 ? cy + 1 : side == 3 ? cy - 1 : cy;
            var nch = ChunkOrNull(ox, oy);
            if (nch == null) continue;
            int bx0 = side == 0 ? 0 : side == 1 ? Chunk.S - 1 : 0;      // strip in the neighbour
            int bx1 = side == 0 ? 1 : side == 1 ? Chunk.S : Chunk.S;
            int by0 = side == 2 ? 0 : side == 3 ? Chunk.S - 1 : 0;
            int by1 = side == 2 ? 1 : side == 3 ? Chunk.S : Chunk.S;
            for (int x = bx0; x < bx1; x++)
                for (int y = by0; y < by1; y++)
                {
                    if (nch.Tiles[x + y * Chunk.S].T != Terrain.Water) continue;
                    int wx = ox * Chunk.S + x, wy = oy * Chunk.S + y;
                    bool any = false;
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = wx + dx, ny = wy + dy;
                        var ch2 = ChunkOrNull((int)MathF.Floor(nx / 32f), (int)MathF.Floor(ny / 32f));
                        if (ch2 != null) any |= ch2.Tiles[(nx & 31) + (ny & 31) * Chunk.S].T == Terrain.Water;
                    }
                    if (!any) nch.Tiles[x + y * Chunk.S].T = Terrain.Ground;
                }
        }
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

    /// <summary>RLE of per-tile ore units ("count:amount," runs).</summary>
    public string OreRle(Chunk c)
    {
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < c.Tiles.Length)
        {
            int v = c.Tiles[i].Ore;
            int n = 1;
            while (i + n < c.Tiles.Length && c.Tiles[i + n].Ore == v) n++;
            sb.Append(n).Append(':').Append(v).Append(',');
            i += n;
        }
        return sb.ToString();
    }

    public void ApplyOreRle(long key, string rle)
    {
        Unpack(key, out var cx, out var cy);
        var c = ChunkAt(cx, cy);
        int i = 0;
        foreach (var run in rle.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = run.Split(':');
            int count = int.Parse(parts[0]);
            int amount = int.Parse(parts[1]);
            for (int k = 0; k < count && i < c.Tiles.Length; k++, i++)
                c.Tiles[i].Ore = amount;
        }
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
        if (!IsWalkableBuilding(t.B) && t.B is not (Belt or Garden or Hub or Hab or Door)) return false;
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
