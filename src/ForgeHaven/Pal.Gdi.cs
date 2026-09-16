using System.Drawing;

namespace ForgeHaven;

// ---------------------------------------------------------------------------
//  GDI+ object cache — GAME PROJECT ONLY (System.Drawing.Common is
//  Windows-only at runtime). Cached pens/brushes are SHARED: never wrap
//  Pal.B()/Pal.P() results in `using`/Dispose — that kills every other
//  draw call using the same color (past bug, fixed twice).
// ---------------------------------------------------------------------------

public static partial class Pal
{
    private static readonly Dictionary<Color, SolidBrush> _brushes = new();
    private static readonly Dictionary<(Color, float), Pen> _pens = new();

    /// <summary>Cached solid brush for a color. Do NOT dispose.</summary>
    public static SolidBrush B(Color c)
    {
        if (!_brushes.TryGetValue(c, out var b)) { b = new SolidBrush(c); _brushes[c] = b; }
        return b;
    }

    /// <summary>Cached pen for a color+width. Do NOT dispose.</summary>
    public static Pen P(Color c, float w = 1f)
    {
        var key = (c, w);
        if (!_pens.TryGetValue(key, out var p)) { p = new Pen(c, w); _pens[key] = p; }
        return p;
    }
}
