using System.Drawing;
using System.Text;
using System.Text.Json;

namespace ForgeHaven;

// ---------------------------------------------------------------------------
//  Persistence: settings + full JSON save/load. Saves land in
//  %LocalAppData%/ForgeHaven/saves as plain JSON (mod-friendly).
//
//  v4 format: seed + gen params + dirty chunks + pollution RLE + trains,
//  bots, prisoners, skills, work priorities, storyteller.
// ---------------------------------------------------------------------------

public sealed class Settings
{
    public bool Autosave { get; set; } = true;
    public int AutosaveEverySec { get; set; } = 120;
    public int MasterVolume { get; set; } = 80;      // reserved for the audio update
    public bool ShowTileGrid { get; set; } = false;
    public bool ScreenShake { get; set; } = true;    // reserved
    public bool ShowDevMenu { get; set; } = false;   // v0.4: cheat/debug menu gate

    private static string DirPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForgeHaven");

    public static string SettingsPath => Path.Combine(DirPath, "settings.json");

    public static Settings Instance { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                Instance = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings();
        }
        catch { Instance = new Settings(); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(DirPath);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Instance, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}

// -------------------------------------------------------------- save DTOs --

public sealed class SaveMeta
{
    public int Version { get; set; } = 4;
    public string Name { get; set; } = "";
    public string SavedAtUtc { get; set; } = "";
    public int Day { get; set; }
    public int Colonists { get; set; }
    public int Wealth { get; set; }
    public int Parts { get; set; }
}

public sealed class ChunkDto
{
    public long Key { get; set; }
    public string Rle { get; set; } = "";
}

public sealed class BuildingDto
{
    public int Kind { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Face { get; set; }
    public float Hp { get; set; }
    public int Recipe { get; set; }
    public int Shots { get; set; }
    public int[]? InBuf { get; set; }
    public int[]? OutBuf { get; set; }
    public int[]? BeltKinds { get; set; }
    public float[]? BeltProgs { get; set; }
    public int[]? CrossKinds { get; set; }
    public float[]? CrossProgs { get; set; }
    public int Held { get; set; } = -1;
    public int Filter { get; set; } = -1;
    public int[]? Storage { get; set; }
    public int[]? StationIn { get; set; }
    public int[]? StationOut { get; set; }
    public int Drones { get; set; }
    public float Fuel { get; set; }
    public float ShieldHp { get; set; } = -1;
    public int RallyX { get; set; }
    public int RallyY { get; set; }
    public int Deployed { get; set; }
    public int[]? HubStock { get; set; }
    public float Reserve { get; set; }
}

public sealed class ColonistDto
{
    public string Name { get; set; } = "";
    public string[] Traits { get; set; } = Array.Empty<string>();
    public int[] Stats { get; set; } = new int[6];
    public float[]? Xp { get; set; }
    public int[]? Passions { get; set; }
    public int[]? Priorities { get; set; }
    public float Hp { get; set; }
    public float Hunger { get; set; }
    public float Rest_ { get; set; }
    public float Morale { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int JobBuildingIndex { get; set; } = -1;
}

public sealed class RaiderDto
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Hp { get; set; }
    public float MaxHp { get; set; }
    public float Dps { get; set; }
    public float Speed { get; set; }
    public bool Apex { get; set; }
    public bool Downed { get; set; }
}

public sealed class CargoDto
{
    public int Kind { get; set; }
    public int N { get; set; }
}

public sealed class TrainDto
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Angle { get; set; }
    public int CargoCount { get; set; }
    public float Wait { get; set; }
    public bool Busy { get; set; }
    public int TargetIndex { get; set; } = -1;
    public List<CargoDto> Cargo { get; set; } = new();
}

public sealed class BotDto
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Hp { get; set; }
    public int HomeIndex { get; set; } = -1;
}

public sealed class PrisonerDto
{
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Recruit { get; set; }
    public bool Fed { get; set; } = true;
    public float FoodTimer { get; set; }
}

public sealed class GameSave
{
    public SaveMeta Meta { get; set; } = new();

    // world generation
    public long? GenSeed { get; set; }
    public float? OreFreq { get; set; }
    public float? OreRich { get; set; }
    public float? RockDensity { get; set; }
    public float? Aggression { get; set; }
    public List<long>? Revealed { get; set; }
    public List<ChunkDto>? Chunks { get; set; }
    public List<ChunkDto>? Pollution { get; set; }

    // legacy v0.2 fixed-size map
    public int W { get; set; }
    public int H { get; set; }
    public string TerrainRle { get; set; } = "";

    public List<BuildingDto> Buildings { get; set; } = new();
    public List<ColonistDto> Colonists { get; set; } = new();
    public List<RaiderDto> Raiders { get; set; } = new();
    public List<TrainDto> Trains { get; set; } = new();
    public List<BotDto> Bots { get; set; } = new();
    public List<PrisonerDto> Prisoners { get; set; } = new();

    public float Time { get; set; }
    public bool[] TechDone { get; set; } = new bool[Bal.TechCount];
    public float[] TechProg { get; set; } = new float[Bal.TechCount];
    public int[]? TechLevels { get; set; } = new int[Bal.TechCount];
    public int ActiveTech { get; set; }
    public float Threat { get; set; }
    public float NextRaidAt { get; set; }
    public int Wave { get; set; }
    public int? Story { get; set; }
    public bool GodMode { get; set; }
    public float BatteryStored { get; set; }        // legacy
}

public static class SaveSystem
{
    public static string SaveDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForgeHaven", "saves");

    public static List<(string path, SaveMeta meta)> ListSaves()
    {
        var list = new List<(string path, SaveMeta meta)>();
        try
        {
            if (!Directory.Exists(SaveDir)) return list;
            foreach (var f in Directory.GetFiles(SaveDir, "*.json"))
            {
                try
                {
                    var s = JsonSerializer.Deserialize<GameSave>(File.ReadAllText(f));
                    if (s != null) list.Add((f, s.Meta));
                }
                catch { }
            }
        }
        catch { }
        list.Sort((a, b) => string.CompareOrdinal(b.meta.SavedAtUtc, a.meta.SavedAtUtc));
        return list;
    }

    public static string? Save(Game g, string fileName, string displayName)
    {
        try
        {
            Directory.CreateDirectory(SaveDir);
            var dto = g.ToSave(displayName);
            var path = Path.Combine(SaveDir, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(dto));
            return path;
        }
        catch (Exception ex)
        {
            g.AddLog("Save failed: " + ex.Message, Pal.Bad);
            return null;
        }
    }

    public static GameSave? LoadFile(string path)
    {
        try { return JsonSerializer.Deserialize<GameSave>(File.ReadAllText(path)); }
        catch { return null; }
    }
}
