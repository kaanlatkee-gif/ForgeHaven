using System.Text;

namespace ForgeHaven;

/// <summary>
/// Tiny human-translation layer. Keys are the exact English source strings;
/// translations live in <c>assets/lang/&lt;code&gt;.txt</c> as
/// <c>English text=Translation</c> lines (one per line, '#' starts a comment).
/// Missing keys fall back to English, so a half-finished file is fine.
/// The game never machine-translates anything — humans fill the files.
/// </summary>
public static class Loc
{
    private static readonly Dictionary<string, string> _map = new();
    private static readonly HashSet<string> _seen = new();   // keys T() was asked for
    public static string Current { get; private set; } = "en";

    /// <summary>Nice display names for common codes; unknown codes show raw.</summary>
    private static string Native(string code) => code switch
    {
        "en" => "English",
        "ru" => "Русский",
        "tr" => "Türkçe",
        "de" => "Deutsch",
        "fr" => "Français",
        "es" => "Español",
        "pl" => "Polski",
        "pt" => "Português",
        "uk" => "Українська",
        _ => code.ToUpperInvariant(),
    };

    public static string DisplayName(string code) => Native(code);

    /// <summary>Translate: returns the mapped string, or the English key itself.</summary>
    public static string T(string s)
    {
        _seen.Add(s);
        return _map.TryGetValue(s, out var v) ? v : s;
    }

    private static string LangDir
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "assets", "lang");
            // also accept the source-tree copy so devs can edit without a build
            if (!Directory.Exists(dir))
            {
                var walk = new DirectoryInfo(AppContext.BaseDirectory);
                while (walk != null && walk.Name != "src") walk = walk.Parent;
                if (walk?.Parent != null)
                    dir = Path.Combine(walk.Parent.FullName, "src", "ForgeHaven", "assets", "lang");
            }
            return dir;
        }
    }

    /// <summary>"en" plus every code that has a file in assets/lang/.</summary>
    public static List<string> Available()
    {
        var codes = new List<string> { "en" };
        try
        {
            if (Directory.Exists(LangDir))
                foreach (var f in Directory.GetFiles(LangDir, "*.txt"))
                {
                    var c = Path.GetFileNameWithoutExtension(f);
                    if (!c.Equals("template", StringComparison.OrdinalIgnoreCase) &&
                        !c.Equals("README", StringComparison.OrdinalIgnoreCase) &&
                        !codes.Contains(c))
                        codes.Add(c);
                }
        }
        catch { /* best effort */ }
        return codes;
    }

    /// <summary>Load a language file ("en" or missing file = English fallback).</summary>
    public static void Load(string code)
    {
        _map.Clear();
        Current = code;
        if (code == "en" || string.IsNullOrEmpty(code)) return;
        try
        {
            var path = Path.Combine(LangDir, code + ".txt");
            if (!File.Exists(path)) return;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;                      // "key=value", split on FIRST '='
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                if (key.Length > 0 && val.Length > 0) _map[key] = val;
            }
        }
        catch { /* broken file = English fallback */ }
    }

    /// <summary>How many strings the current language file translates.</summary>
    public static int LoadedCount => _map.Count;

    /// <summary>
    /// Write assets/lang/template.txt listing every string the game has asked
    /// for (plus every name/blurb warm-up) as "English=" lines for a human
    /// to translate. Dual-writes to the exe folder and the source tree,
    /// never overwrites an existing file. Returns (path, string count).
    /// </summary>
    public static (string Path, int Count) ExportTemplate()
    {
        // warm up: make sure every name/blurb is registered as a seen key
        foreach (BuildKind k in Enum.GetValues<BuildKind>()) { Bal.Name(k); Bal.Blurb(k); }
        foreach (ItemKind i in Enum.GetValues<ItemKind>()) Bal.ItemName(i);
        foreach (Tech t in Enum.GetValues<Tech>()) { Bal.TechName(t); Bal.TechBlurb(t); }

        var sb = new StringBuilder();
        sb.AppendLine("# ForgeHaven language template");
        sb.AppendLine("# 1. Copy this file next to itself as <code>.txt (e.g. ru.txt for Russian)");
        sb.AppendLine("# 2. Translate the RIGHT side of each '='. Keep the left side untouched.");
        sb.AppendLine("# 3. Lines starting with '#' are comments. Missing lines fall back to English.");
        sb.AppendLine($"# Generated {DateTime.UtcNow:yyyy-MM-dd} - {_seen.Count} strings");
        sb.AppendLine();
        foreach (var s in _seen.OrderBy(s => s, StringComparer.Ordinal))
        {
            var safe = s.Replace("\n", " ");
            sb.Append(safe).Append('=').AppendLine();
        }

        int count = _seen.Count;
        var targets = new List<string>();
        var exe = Path.Combine(AppContext.BaseDirectory, "assets", "lang");
        targets.Add(exe);
        var walk = new DirectoryInfo(AppContext.BaseDirectory);
        while (walk != null && walk.Name != "src") walk = walk.Parent;
        if (walk?.Parent != null)
            targets.Add(Path.Combine(walk.Parent.FullName, "src", "ForgeHaven", "assets", "lang"));

        string written = "";
        foreach (var dir in targets)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "template.txt");
                if (!File.Exists(path)) { File.WriteAllText(path, sb.ToString()); written = path; }
                else if (written == "") written = path;
            }
            catch { /* best effort */ }
        }
        return (written, count);
    }
}
