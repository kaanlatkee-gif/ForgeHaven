using System.Drawing;

namespace ForgeHaven;

// ---------------------------------------------------------------------------
//  Shared enums, helpers, balance constants, descriptions and palettes.
//  Everything gameplay-tunable lives in Bal so it is easy to tweak.
// ---------------------------------------------------------------------------

public enum Terrain { Ground, IronOre, Crystal, Flora, Rock, CopperOre, Water }

public enum Dir { Right = 0, Down = 1, Left = 2, Up = 3 }

public enum ItemKind
{
    IronOre = 0, Crystal = 1, Biomass = 2, IronPlate = 3, Ammo = 4, Food = 5,
    AdvPart = 6, CopperOre = 7, CopperPlate = 8, SciencePack = 9,
    Drone = 10, WarBot = 11,
}

public enum BuildKind
{
    // Logistics
    Belt, FastBelt, Splitter, Junction, OverflowRouter, Merger, FilterSplitter,
    Inserter, Rail, ElevatedRail, TrainStop, Locomotive, DronePort,
    // Production
    Drill, DeepDrill, Smelter, Fabricator, FabT2, FabT3, BioProcessor, StorageCrate,
    // Power (& fluids)
    PowerPole, Reactor, SolarPanel, WindTurbine, Battery,
    Pipe, Pump, Tank, Boiler, SteamEngine,
    // Defense
    Wall, Turret, HeavyTurret, Watchtower, SpikeTrap, IED, ShieldGen, BotFactory,
    // Colony
    Hab, MessTable, Lamp, Garden, MedBed, Lab,
    // system
    Hub,
}

public enum BuildCategory { Logistics, Production, Power, Defense, Colony }

public enum Tech
{
    None = -1, Automation = 0, Overclock = 1, Optics = 2,
    FastLogistics = 3, DeepMining = 4, HeavyOrdnance = 5,
    MiningProd = 6, Assembly2 = 7, Assembly3 = 8,
}

/// <summary>Leveled (repeatable) techs. Index matches (int)Tech.</summary>
public static class LeveledTech
{
    public static bool IsLeveled(Tech t) => t == Tech.MiningProd;
    public const int MiningProdCap = 8;
}

public enum ColState
{
    Idle, GoWork, Working, GoEat, Eating, GoSleep, Sleeping, Flee, Heal,
    GoCapture, Escort,
}

public enum ToolKind { None, Build, Bulldoze }

public enum AppState { MainMenu, Playing, WorldGen }

/// <summary>Toggled map view layers (F1..F4).</summary>
public enum OverlayMode { None, Power, Defense, Logistics, Pollution }

/// <summary>RimWorld-style work categories for the priority screen.</summary>
public enum WorkType { Mine = 0, Smelt = 1, Craft = 2, Bio = 3, Research = 4, Defense = 5 }

public enum FluidKind { None, Water, Steam }

/// <summary>AI storyteller personality (raid pacing & events).</summary>
public enum Storyteller { Builder = 0, Random = 1, Merciless = 2 }

public static class DirU
{
    public static readonly int[] Dx = { 1, 0, -1, 0 };
    public static readonly int[] Dy = { 0, 1, 0, -1 };

    public static Dir Opposite(Dir d) => (Dir)(((int)d + 2) & 3);
    public static Dir TurnRight(Dir d) => (Dir)(((int)d + 1) & 3);
    public static Dir TurnLeft(Dir d) => (Dir)(((int)d + 3) & 3);

    public static (int x, int y) Step(int x, int y, Dir d) => (x + Dx[(int)d], y + Dy[(int)d]);
}

public static class Rng
{
    public static readonly Random Shared = new();   // VISUAL-ONLY randomness
    public static float Next(float a, float b) => a + (float)Shared.NextDouble() * (b - a);
    public static int Next(int a, int b) => Shared.Next(a, b);
    public static bool Chance(float p) => Shared.NextDouble() < p;
}

/// <summary>Balance / tuning constants. Tweak freely.</summary>
public static class Bal
{
    public const int TechCount = 9;
    public const int WorkCount = 6;
    public const int ItemCount = 12;          // ItemKind values
    public const int PartsToWin = 10;
    public const float DayLengthSec = 180f;

    // ------------------------------------------------------- build catalog

    public static BuildCategory CategoryOf(BuildKind k) => k switch
    {
        BuildKind.Belt or BuildKind.FastBelt or BuildKind.Splitter or BuildKind.Junction
            or BuildKind.OverflowRouter or BuildKind.Merger or BuildKind.FilterSplitter
            or BuildKind.Inserter or BuildKind.Rail or BuildKind.ElevatedRail
            or BuildKind.TrainStop or BuildKind.Locomotive or BuildKind.DronePort
            => BuildCategory.Logistics,
        BuildKind.Drill or BuildKind.DeepDrill or BuildKind.Smelter or BuildKind.Fabricator
            or BuildKind.FabT2 or BuildKind.FabT3 or BuildKind.BioProcessor
            or BuildKind.StorageCrate => BuildCategory.Production,
        BuildKind.PowerPole or BuildKind.Reactor or BuildKind.SolarPanel or BuildKind.WindTurbine
            or BuildKind.Battery or BuildKind.Pipe or BuildKind.Pump or BuildKind.Tank
            or BuildKind.Boiler or BuildKind.SteamEngine => BuildCategory.Power,
        BuildKind.Wall or BuildKind.Turret or BuildKind.HeavyTurret or BuildKind.Watchtower
            or BuildKind.SpikeTrap or BuildKind.IED or BuildKind.ShieldGen or BuildKind.BotFactory
            => BuildCategory.Defense,
        BuildKind.Hab or BuildKind.MessTable or BuildKind.Lamp or BuildKind.Garden
            or BuildKind.MedBed or BuildKind.Lab => BuildCategory.Colony,
        _ => BuildCategory.Colony,
    };

    public static string CategoryName(BuildCategory c) => c switch
    {
        BuildCategory.Logistics => "Logistics",
        BuildCategory.Production => "Production",
        BuildCategory.Power => "Power",
        BuildCategory.Defense => "Defense",
        BuildCategory.Colony => "Colony",
        _ => "?",
    };

    /// <summary>Research required before a building can be placed.</summary>
    public static Tech TechGate(BuildKind k) => k switch
    {
        BuildKind.FastBelt => Tech.FastLogistics,
        BuildKind.ElevatedRail => Tech.FastLogistics,
        BuildKind.DeepDrill => Tech.DeepMining,
        BuildKind.HeavyTurret => Tech.HeavyOrdnance,
        BuildKind.FabT2 => Tech.Assembly2,
        BuildKind.FabT3 => Tech.Assembly3,
        _ => Tech.None,
    };

    // -------------------------------------------------------- build costs
    //  Buildings are paid for with REAL MATERIALS from Hub stock.

    public static int CostIron(BuildKind k) => k switch
    {
        BuildKind.Belt => 1,
        BuildKind.FastBelt => 2,
        BuildKind.Splitter => 2,
        BuildKind.Junction => 2,
        BuildKind.OverflowRouter => 3,
        BuildKind.Merger => 2,
        BuildKind.FilterSplitter => 3,
        BuildKind.Inserter => 1,
        BuildKind.Rail => 1,
        BuildKind.ElevatedRail => 2,
        BuildKind.TrainStop => 4,
        BuildKind.Locomotive => 16,
        BuildKind.DronePort => 4,
        BuildKind.Drill => 3,
        BuildKind.DeepDrill => 6,
        BuildKind.Smelter => 4,
        BuildKind.Fabricator => 5,
        BuildKind.FabT2 => 10,
        BuildKind.FabT3 => 16,
        BuildKind.BioProcessor => 4,
        BuildKind.StorageCrate => 1,
        BuildKind.Reactor => 5,
        BuildKind.SolarPanel => 3,
        BuildKind.WindTurbine => 4,
        BuildKind.Battery => 2,
        BuildKind.PowerPole => 1,
        BuildKind.Pipe => 0,
        BuildKind.Pump => 2,
        BuildKind.Tank => 3,
        BuildKind.Boiler => 5,
        BuildKind.SteamEngine => 8,
        BuildKind.Wall => 1,
        BuildKind.Turret => 4,
        BuildKind.HeavyTurret => 8,
        BuildKind.Watchtower => 3,
        BuildKind.SpikeTrap => 2,
        BuildKind.IED => 2,
        BuildKind.ShieldGen => 10,
        BuildKind.BotFactory => 8,
        BuildKind.Hab => 3,
        BuildKind.MessTable => 2,
        BuildKind.Lamp => 1,
        BuildKind.Garden => 1,
        BuildKind.MedBed => 3,
        BuildKind.Lab => 4,
        _ => 0,
    };

    public static int CostCopper(BuildKind k) => k switch
    {
        BuildKind.FastBelt => 1,
        BuildKind.Junction => 1,
        BuildKind.Inserter => 1,
        BuildKind.ElevatedRail => 1,
        BuildKind.TrainStop => 2,
        BuildKind.Locomotive => 8,
        BuildKind.DronePort => 4,
        BuildKind.FilterSplitter => 1,
        BuildKind.FabT2 => 4,
        BuildKind.FabT3 => 8,
        BuildKind.Reactor => 5,
        BuildKind.SolarPanel => 2,
        BuildKind.WindTurbine => 2,
        BuildKind.Battery => 4,
        BuildKind.PowerPole => 2,
        BuildKind.Pipe => 1,
        BuildKind.Pump => 4,
        BuildKind.Tank => 3,
        BuildKind.Boiler => 2,
        BuildKind.SteamEngine => 4,
        BuildKind.Turret => 2,
        BuildKind.HeavyTurret => 4,
        BuildKind.Watchtower => 1,
        BuildKind.IED => 1,
        BuildKind.ShieldGen => 6,
        BuildKind.BotFactory => 4,
        BuildKind.Lamp => 1,
        BuildKind.MedBed => 2,
        BuildKind.Lab => 4,
        BuildKind.Fabricator => 2,
        _ => 0,
    };

    public static string CostText(BuildKind k)
    {
        int fe = CostIron(k), cu = CostCopper(k);
        if (fe == 0 && cu == 0) return "free";
        var parts = new List<string>();
        if (fe > 0) parts.Add($"{fe} Iron Pl");
        if (cu > 0) parts.Add($"{cu} Copper Pl");
        return string.Join(" + ", parts);
    }

    /// <summary>Rough material value — feeds colony wealth (raid scaling).</summary>
    public static int CostWealth(BuildKind k) => CostIron(k) * 2 + CostCopper(k) * 4 + 6;

    // Hub stock the crash survivors start with.
    public const int StartIronPlates = 60;
    public const int StartCopperPlates = 24;
    public const int StartFood = 30;

    public static string Name(BuildKind k) => k switch
    {
        BuildKind.Belt => "Conveyor Belt",
        BuildKind.FastBelt => "Express Belt",
        BuildKind.Splitter => "Splitter",
        BuildKind.Junction => "Belt Junction",
        BuildKind.OverflowRouter => "Overflow Router",
        BuildKind.Merger => "Lane Merger",
        BuildKind.FilterSplitter => "Filter Splitter",
        BuildKind.Inserter => "Inserter",
        BuildKind.Rail => "Rail",
        BuildKind.ElevatedRail => "Elevated Rail",
        BuildKind.TrainStop => "Train Station",
        BuildKind.Locomotive => "Locomotive",
        BuildKind.DronePort => "Drone Port",
        BuildKind.Drill => "Mine Drill",
        BuildKind.DeepDrill => "Deep-Core Drill",
        BuildKind.Smelter => "Smelter",
        BuildKind.Fabricator => "Fabricator",
        BuildKind.FabT2 => "Assembler II",
        BuildKind.FabT3 => "Assembler III",
        BuildKind.BioProcessor => "Bio-Processor",
        BuildKind.StorageCrate => "Storage Crate",
        BuildKind.Reactor => "Reactor",
        BuildKind.SolarPanel => "Solar Panel",
        BuildKind.WindTurbine => "Wind Turbine",
        BuildKind.Battery => "Battery Bank",
        BuildKind.PowerPole => "Power Pole",
        BuildKind.Pipe => "Pipe",
        BuildKind.Pump => "Water Pump",
        BuildKind.Tank => "Fluid Tank",
        BuildKind.Boiler => "Boiler",
        BuildKind.SteamEngine => "Steam Engine",
        BuildKind.Wall => "Wall",
        BuildKind.Turret => "Sentry Turret",
        BuildKind.HeavyTurret => "Heavy Turret",
        BuildKind.Watchtower => "Watchtower",
        BuildKind.SpikeTrap => "Spike Trap",
        BuildKind.IED => "IED",
        BuildKind.ShieldGen => "Shield Generator",
        BuildKind.BotFactory => "War-Bot Factory",
        BuildKind.Hab => "Hab Unit",
        BuildKind.MessTable => "Mess Table",
        BuildKind.Lamp => "Standing Lamp",
        BuildKind.Garden => "Garden",
        BuildKind.MedBed => "Med Bed",
        BuildKind.Lab => "Research Lab",
        BuildKind.Hub => "Colony Hub",
        _ => "?",
    };

    /// <summary>One-line functional description, shown in the build menu.</summary>
    public static string Blurb(BuildKind k) => k switch
    {
        BuildKind.Belt => "Moves items in one direction. Drag to paint long runs.",
        BuildKind.FastBelt => "Reinforced express conveyor — 80% faster than a standard belt.",
        BuildKind.Splitter => "Splits an incoming item flow round-robin across up to 3 outputs.",
        BuildKind.Junction => "Two belt lanes crossing without mixing. Main flow follows the arrow; cross flow enters from the arrow's left.",
        BuildKind.OverflowRouter => "Priority splitter: straight ahead first; sides only receive the overflow.",
        BuildKind.Merger => "Merges up to three incoming flows into one output lane (the facing).",
        BuildKind.FilterSplitter => "Splitter with a filter: matching items exit RIGHT, everything else goes straight. Select it to set the filter.",
        BuildKind.Inserter => "Swinging arm that lifts items from the tile behind it onto the tile it faces. Belts, machines, crates — anything.",
        BuildKind.Rail => "Train track. Drag to lay line; corners follow the drag like belts.",
        BuildKind.ElevatedRail => "Raised track: can be placed ON TOP of belts, pipes and other rails. Trains cross over without interfering.",
        BuildKind.TrainStop => "Station for the locomotive. Belts load items in; the train unloads onto belts on the facing side.",
        BuildKind.Locomotive => "Places a locomotive on track next to a station. It shuttles cargo between your stations automatically.",
        BuildKind.DronePort => "Houses Logistic Drones (belt them in). Drones ferry items from storage crates to hungry machines and auto-repair damaged buildings in range.",
        BuildKind.Drill => "Mines the deposit it stands on and pushes ore onto its output side.",
        BuildKind.DeepDrill => "Heavy drill: double output of a Mine Drill, double appetite for power.",
        BuildKind.Smelter => "Smelts Iron Ore into Iron Plates, or Copper Ore into Copper Plates.",
        BuildKind.Fabricator => "Crafts Ammo, Science Packs, Drones, War Bots or Advanced Parts — select to cycle recipes.",
        BuildKind.FabT2 => "Assembler, second tier: double crafting speed, more power draw.",
        BuildKind.FabT3 => "Assembler, third tier: quadruple crafting speed.",
        BuildKind.BioProcessor => "Processes biomass into edible Food packs.",
        BuildKind.StorageCrate => "Buffers up to 24 items, then slowly re-emits them onto its output belt.",
        BuildKind.Reactor => "Generates +100 flat power for the grid it is wired into.",
        BuildKind.SolarPanel => "Generates +40 power at noon, nothing at night. Charge batteries!",
        BuildKind.WindTurbine => "Generates 15-65 power, swaying with the alien wind. No fuel.",
        BuildKind.Battery => "Stores surplus power and bridges brownouts on its own grid (200 units).",
        BuildKind.PowerPole => "Carries power between machines. Wires to poles within 7 tiles; feeds everything within 5 tiles.",
        BuildKind.Pipe => "Carries water or steam. Auto-connects to adjacent pipes and fluid machines.",
        BuildKind.Pump => "Must sit ON water. Pump 20 water/s into the connected pipe network.",
        BuildKind.Tank => "Buffer for a pipe network: +2000 fluid capacity.",
        BuildKind.Boiler => "Burns belt-fed Biomass to convert network water into steam pushed out of its facing side. Needs separate in/out pipe runs.",
        BuildKind.SteamEngine => "Consumes steam from adjacent pipes; generates up to +90 power on its electric grid.",
        BuildKind.Wall => "Blocks aliens. Shape kill corridors and turret funnels.",
        BuildKind.Turret => "Automated gun. Pulls Ammo Crates from any belt it touches.",
        BuildKind.HeavyTurret => "Bigger automated gun: double damage, longer range, double magazine.",
        BuildKind.Watchtower => "A colonist-manned gun post. Needs no ammo — but a warm body.",
        BuildKind.SpikeTrap => "Walkable spikes: chew raiders that cross them. Wears out with use.",
        BuildKind.IED => "Hidden blast: detonates when a raider steps on it. One use — mind your colonists.",
        BuildKind.ShieldGen => "Projects a bubble (radius 4). While the bubble holds, buildings inside take no damage. Heavy power draw.",
        BuildKind.BotFactory => "Belt in War Bots to deploy guard drones (up to 8). Select the factory, then Set Rally to pick their patrol point.",
        BuildKind.Hab => "Colonists rest here. Keep it away from industry.",
        BuildKind.MessTable => "A place to sit like people. Soothes nearby colonists (morale aura).",
        BuildKind.Lamp => "A warm point of light. Matters at night.",
        BuildKind.Garden => "Green space. Best morale aura of the colony group.",
        BuildKind.MedBed => "Wounded colonists lie here and recover much faster.",
        BuildKind.Lab => "A researcher burns Science Packs into research points for the selected project.",
        BuildKind.Hub => "The heart of the colony: storage, canteen, brig and a small built-in power source.",
        _ => "",
    };

    /// <summary>Requirements line, shown under the blurb in menus/tooltips.</summary>
    public static string Needs(BuildKind k) => k switch
    {
        BuildKind.FastBelt => "Needs: research: Fast Logistics",
        BuildKind.ElevatedRail => "Needs: research: Fast Logistics",
        BuildKind.DeepDrill => "Needs: ore tile · 14 power · research: Deep Mining",
        BuildKind.FabT2 => "Needs: 12 power · research: Assembly II · belt-fed plates",
        BuildKind.FabT3 => "Needs: 16 power · research: Assembly III · belt-fed plates",
        BuildKind.Pump => "Needs: a Water tile",
        BuildKind.Boiler => "Needs: water pipes in · steam pipes out (facing) · belt-fed Biomass",
        BuildKind.SteamEngine => "Needs: piped steam · wiring to a Power Pole",
        BuildKind.Pipe or BuildKind.Tank => "Needs: connection to a pump network",
        BuildKind.Locomotive => "Needs: rail/station tile under it",
        BuildKind.TrainStop => "Needs: rails reaching another station",
        BuildKind.DronePort => "Needs: 4 power · belt-fed Logistic Drones",
        BuildKind.Inserter => "Needs: 1 power · source behind · destination ahead",
        BuildKind.FilterSplitter => "Needs: nothing · select to set filter",
        BuildKind.ShieldGen => "Needs: 20 power · wired grid",
        BuildKind.BotFactory => "Needs: 6 power · belt-fed War Bots",
        BuildKind.SpikeTrap => "Needs: nothing · raiders can walk over it",
        BuildKind.IED => "Needs: nothing · single use",
        BuildKind.Drill => "Needs: ore / crystal / flora tile · 6 power · operator (until Automation)",
        BuildKind.Smelter => "Needs: 8 power · belt-fed Iron or Copper ore · operator",
        BuildKind.Fabricator => "Needs: 10 power · belt-fed plates · operator",
        BuildKind.BioProcessor => "Needs: 6 power · belt-fed Biomass · operator",
        BuildKind.StorageCrate => "Needs: items pushed in by belts",
        BuildKind.Reactor => "Needs: wiring — reach a Power Pole",
        BuildKind.SolarPanel => "Needs: wiring — reach a Power Pole",
        BuildKind.WindTurbine => "Needs: wiring — reach a Power Pole",
        BuildKind.Battery => "Needs: wiring — reach a Power Pole",
        BuildKind.PowerPole => "Needs: nothing — wires itself to nearby poles",
        BuildKind.Rail => "Needs: nothing",
        BuildKind.Merger => "Needs: flows pushed into its back/left/right",
        BuildKind.Wall => "Needs: nothing",
        BuildKind.Turret => "Needs: 4 power · belt-fed Ammo Crates",
        BuildKind.HeavyTurret => "Needs: 8 power · belt-fed Ammo Crates · research: Heavy Ordnance",
        BuildKind.Watchtower => "Needs: a colonist operator (always manned)",
        BuildKind.Hab => "Needs: 2 power",
        BuildKind.MessTable => "Needs: nothing",
        BuildKind.Lamp => "Needs: 1 power",
        BuildKind.Garden => "Needs: nothing",
        BuildKind.MedBed => "Needs: 2 power",
        BuildKind.Lab => "Needs: 8 power · a researcher · belt-fed Science Packs",
        BuildKind.Hub => "Needs: to be defended",
        _ => "Needs: nothing",
    };

    // ---------------------------------------------------------------- power

    public static int PowerDemand(BuildKind k) => k switch
    {
        BuildKind.Drill => 6,
        BuildKind.DeepDrill => 14,
        BuildKind.Smelter => 8,
        BuildKind.Fabricator => 10,
        BuildKind.FabT2 => 12,
        BuildKind.FabT3 => 16,
        BuildKind.BioProcessor => 6,
        BuildKind.Turret => 4,
        BuildKind.HeavyTurret => 8,
        BuildKind.Hab => 2,
        BuildKind.Lab => 8,
        BuildKind.Lamp => 1,
        BuildKind.MedBed => 2,
        BuildKind.Hub => 8,
        BuildKind.Inserter => 1,
        BuildKind.DronePort => 4,
        BuildKind.ShieldGen => 20,
        BuildKind.TrainStop => 2,
        BuildKind.BotFactory => 6,
        _ => 0,
    };
    public const int ReactorSupply = 100;
    public const int SolarSupply = 40;
    public const int BaseSupply = 30;         // the Hub's internal generator
    public const float WindMin = 15f, WindMax = 65f;
    public const float BatteryCap = 200f;
    public const float BatteryRate = 50f;

    public const float PoleCover = 5f;
    public const float HubCover = 8f;
    public const float PoleWire = 7f;

    // --------------------------------------------------------------- fluids

    public const float PumpRate = 20f;        // water/s into the network
    public const float BoilerRate = 20f;      // water->steam /s
    public const float BoilerBiomassPerWater = 0.05f; // biomass burned per water converted
    public const float EngineSteamRate = 30f; // steam/s consumed at full output
    public const float EnginePower = 90f;     // power at full steam
    public const float PipeCap = 100f;        // fluid capacity per pipe segment
    public const float TankCap = 2000f;

    // -------------------------------------------------------------- trains

    public const float TrainSpeed = 3.2f;     // tiles/s
    public const int TrainCap = 40;           // item stacks
    public const int StationCap = 30;         // station buffer size
    public const float TrainMinWait = 4f;     // seconds at a station minimum

    // --------------------------------------------------------------- drones

    public const float DronePortCover = 9f;
    public const float DroneDeliveryEvery = 0.8f;
    public const float DroneRepairEvery = 1.6f;
    public const float RepairPerPlate = 30f;  // hp restored per iron plate

    // ---------------------------------------------------------- war bots

    public const int BotCap = 8;              // per factory
    public const float BotHp = 80f;
    public const float BotDps = 9f;
    public const float BotRange = 3.6f;
    public const float BotAggro = 11f;
    public const float BotSpeed = 2.6f;

    // ------------------------------------------------------------- defense

    public const float TurretCd = 0.55f;
    public const int TurretShotsPerCrate = 10;
    public const int TurretMagCap = 40;
    public const float TurretDmg = 7f;
    public const float TurretDmgUp = 11f;
    public const float TurretRange = 6.2f;
    public const float TurretRangeUp = 7.6f;

    public const float HeavyCd = 0.8f;
    public const int HeavyMagCap = 80;
    public const float HeavyDmg = 14f;
    public const float HeavyDmgUp = 22f;
    public const float HeavyRange = 7.5f;
    public const float HeavyRangeUp = 9f;

    public const float TowerCd = 0.9f;
    public const float TowerDmg = 6f;
    public const float TowerRange = 7f;

    public const float SpikeDmg = 12f;        // per trigger
    public const float SpikeCd = 0.5f;
    public const float SpikeWear = 8f;        // trap hp lost per trigger
    public const float IedDmg = 70f;
    public const float IedRadius = 2.6f;

    public const float ShieldHp = 300f;
    public const float ShieldRegen = 14f;
    public const float ShieldDelay = 3f;      // after last hit
    public const float ShieldRadius = 4f;

    // ---------------------------------------------------------------- raid

    public const float GraceWealth = 800f;
    public const float ThreatPerWealth = 0.0016f;
    public const float FirstRaidAt = 140f;
    public const float RaidGrowth = 1.45f;

    public const float RaidSpawnBase = 55f;
    public const float RaidSpawnPerWave = 4f;
    public const float RaidSpawnMax = 90f;

    public const float RaiderHp = 26f;
    public const float RaiderHpPerWave = 5f;
    public const float RaiderDps = 5f;
    public const float RaiderSpeed = 2.0f;
    public const float ApexHp = 220f;
    public const float ApexDps = 18f;
    public const float ApexSpeed = 1.5f;

    public const float DownedAtFrac = 0.18f;  // hp fraction where raiders go down
    public const float DownedChance = 0.55f;
    public const float DownedBleed = 0.06f;   // hp/s while uncaptured

    public static float WaveBudget(float wealth) => Math.Clamp(3 + wealth / 400f, 3, 16);

    // ------------------------------------------------------------ pollution

    public const float PollDiffuse = 0.06f;     // spread rate toward neighbors
    public const float PollFloraAbsorb = 0.02f; // per flora tile /s
    public const float PollMaxTile = 8f;        // display/storage clamp
    public const float RaidPollBoostCap = 0.6f; // max wave-size bonus from pollution

    public static float PollEmit(BuildKind k) => k switch
    {
        BuildKind.Reactor => 0.30f,
        BuildKind.Boiler => 0.15f,
        BuildKind.Smelter => 0.10f,
        BuildKind.DeepDrill => 0.09f,
        BuildKind.FabT2 => 0.12f,
        BuildKind.FabT3 => 0.16f,
        BuildKind.Fabricator => 0.08f,
        BuildKind.Drill => 0.05f,
        BuildKind.BioProcessor => 0.04f,
        BuildKind.TrainStop => 0.02f,
        _ => 0f,
    };

    // ------------------------------------------------------------ colonists

    public const float ColHp = 90f;
    public const float ColSpeed = 3.2f;
    public const float ColHungerRate = 0.5f;
    public const float ColRestRate = 0.35f;
    public const float ColMeleeCd = 0.85f;
    public const float ColRegenRate = 0.4f;
    public const float MedBedHealRate = 4f;
    public const float FlowMorale = 84f;
    public const float FlowSpeedBonus = 1.3f;

    // XP & passions
    public const int XpCap = 20;               // max skill level
    public const float XpWorkRate = 2.2f;      // xp/s while working
    public const float PassionMul = 2f;

    // prisoners
    public const float PrisonerFoodEvery = 120f;
    public const float PrisonerRecruitRate = 0.010f;   // base fraction/s
    public const float PrisonerRecruitSoc = 0.0012f;   // per avg SOC point

    // ----------------------------------------------------------- day/night

    //  DayFrac 0..1: dawn 0.20-0.30, day 0.30-0.70, dusk 0.70-0.80, night rest.
    public static float Sunlight(float dayFrac)
    {
        if (dayFrac is >= 0.30f and < 0.70f) return 1f;
        if (dayFrac < 0.20f || dayFrac >= 0.80f) return 0f;
        if (dayFrac < 0.30f) return (dayFrac - 0.20f) / 0.10f;
        return (0.80f - dayFrac) / 0.10f;
    }

    // -------------------------------------------------------------- machines

    public const float DrillTime = 2.6f;
    public const float DeepDrillTime = 1.3f;
    public const float SmeltTime = 3.2f;
    public const float AmmoTime = 2.5f;
    public const float PartTime = 8.0f;
    public const float SciTime = 4.0f;
    public const float DroneTime = 5.0f;
    public const float WarBotTime = 6.0f;
    public const float BioTime = 3.0f;
    public const int InBufCap = 6;
    public const int OutBufCap = 4;

    public const float BeltSpeed = 0.9f;
    public const float BeltSpeedFast = 1.6f;
    public const float BeltGap = 0.62f;
    public const float FastBeltMul = 1.8f;

    public const float InserterEvery = 0.85f;   // seconds per item swung

    public const int StorageCap = 24;
    public const float StorageEjectEvery = 1.2f;

    public static int ItemWealth(ItemKind k) => k switch
    {
        ItemKind.IronOre => 1,
        ItemKind.CopperOre => 1,
        ItemKind.Crystal => 3,
        ItemKind.Biomass => 1,
        ItemKind.IronPlate => 2,
        ItemKind.CopperPlate => 2,
        ItemKind.Ammo => 2,
        ItemKind.Food => 2,
        ItemKind.SciencePack => 4,
        ItemKind.Drone => 8,
        ItemKind.WarBot => 10,
        ItemKind.AdvPart => 50,
        _ => 0,
    };

    public static string ItemName(ItemKind k) => k switch
    {
        ItemKind.IronOre => "Iron Ore",
        ItemKind.Crystal => "Crystal",
        ItemKind.Biomass => "Biomass",
        ItemKind.IronPlate => "Iron Plate",
        ItemKind.Ammo => "Ammo Crate",
        ItemKind.Food => "Food",
        ItemKind.AdvPart => "Adv. Part",
        ItemKind.CopperOre => "Copper Ore",
        ItemKind.CopperPlate => "Copper Plate",
        ItemKind.SciencePack => "Science Pack",
        ItemKind.Drone => "Logistic Drone",
        ItemKind.WarBot => "War Bot",
        _ => "?",
    };

    public static string ItemDesc(ItemKind k) => k switch
    {
        ItemKind.CopperPlate => "Conductive plate. Power wiring, turrets, science.",
        ItemKind.SciencePack => "Consumed by the Research Lab to drive projects.",
        ItemKind.Drone => "Belt into a Drone Port: ferries items and repairs buildings.",
        ItemKind.WarBot => "Belt into a War-Bot Factory to deploy a guard drone.",
        ItemKind.IronOre => "Raw ore. Smelt it into plates.",
        ItemKind.Crystal => "Exotic crystal. Used for Advanced Parts.",
        ItemKind.Biomass => "Harvested alien flora. Food, or boiler fuel.",
        ItemKind.IronPlate => "Refined plate. The backbone of everything you build.",
        ItemKind.Ammo => "Feeds Sentry and Heavy turrets via belts.",
        ItemKind.Food => "Colonists eat one pack from the Hub when hungry.",
        ItemKind.AdvPart => "Victory component. Deliver 10 to the Hub.",
        ItemKind.CopperOre => "Raw ore with a teal patina. Smelt into Copper Plates.",
        _ => "",
    };

    // --------------------------------------------------------------- work

    public static string WorkName(WorkType w) => w switch
    {
        WorkType.Mine => "Mining",
        WorkType.Smelt => "Smelting",
        WorkType.Craft => "Crafting",
        WorkType.Bio => "Farming",
        WorkType.Research => "Research",
        WorkType.Defense => "Defense",
        _ => "?",
    };

    /// <summary>Which work category operates a building (null = none).</summary>
    public static WorkType? WorkOf(BuildKind k) => k switch
    {
        BuildKind.Drill or BuildKind.DeepDrill => WorkType.Mine,
        BuildKind.Smelter => WorkType.Smelt,
        BuildKind.Fabricator or BuildKind.FabT2 or BuildKind.FabT3 => WorkType.Craft,
        BuildKind.BioProcessor => WorkType.Bio,
        BuildKind.Lab => WorkType.Research,
        BuildKind.Watchtower => WorkType.Defense,
        _ => null,
    };

    /// <summary>Stat index used by a work type: STR INT DEX END SOC CRE.</summary>
    public static int StatOf(WorkType w) => w switch
    {
        WorkType.Mine => 0,
        WorkType.Research => 1,
        WorkType.Craft => 2,
        WorkType.Bio => 3,
        WorkType.Defense => 2,
        _ => 2,
    };

    // -------------------------------------------------------------- research

    public const float PackPoints = 10f;
    public const float LabRate = 2.2f;

    public static int TechCost(Tech t, int level = 0) => t switch
    {
        Tech.Automation => 150,
        Tech.Overclock => 120,
        Tech.Optics => 120,
        Tech.FastLogistics => 100,
        Tech.DeepMining => 160,
        Tech.HeavyOrdnance => 140,
        Tech.MiningProd => 160 + level * 90,     // leveled: pricier each time
        Tech.Assembly2 => 180,
        Tech.Assembly3 => 300,
        _ => 999,
    };
    public static int PackCost(Tech t, int level = 0) => (int)Math.Ceiling(TechCost(t, level) / PackPoints);

    public static Tech TechPrereq(Tech t) => t switch
    {
        Tech.FastLogistics => Tech.Overclock,
        Tech.DeepMining => Tech.Automation,
        Tech.HeavyOrdnance => Tech.Optics,
        Tech.MiningProd => Tech.Automation,
        Tech.Assembly2 => Tech.Automation,
        Tech.Assembly3 => Tech.Assembly2,
        _ => Tech.None,
    };

    public static string TechName(Tech t) => t switch
    {
        Tech.Automation => "Automation Protocol",
        Tech.Overclock => "Overclocked Belts",
        Tech.Optics => "Turret Optics",
        Tech.FastLogistics => "Fast Logistics",
        Tech.DeepMining => "Deep-Core Mining",
        Tech.HeavyOrdnance => "Heavy Ordnance",
        Tech.MiningProd => "Mining Productivity",
        Tech.Assembly2 => "Assembly II",
        Tech.Assembly3 => "Assembly III",
        _ => "-",
    };
    public static string TechBlurb(Tech t) => t switch
    {
        Tech.Automation => "Machines run unmanned. Operators are freed for higher work.",
        Tech.Overclock => "Belt speed +80%, all machines +25% speed.",
        Tech.Optics => "Turrets: +57% damage, +23% range.",
        Tech.FastLogistics => "Unlocks the Express Belt and Elevated Rails.",
        Tech.DeepMining => "Unlocks the Deep-Core Drill. Double ore, double power draw.",
        Tech.HeavyOrdnance => "Unlocks the Heavy Turret for late-wave defense.",
        Tech.MiningProd => "Repeatable: all drills +10% speed per level.",
        Tech.Assembly2 => "Unlocks the Assembler II (2x crafting speed).",
        Tech.Assembly3 => "Unlocks the Assembler III (4x crafting speed).",
        _ => "",
    };
}

/// <summary>Cached drawing resources.</summary>
public static class Pal
{
    public static readonly Color Bg = C(16, 20, 24);
    public static readonly Color GroundA = C(43, 47, 50);
    public static readonly Color GroundB = C(47, 51, 55);
    public static readonly Color Grid = C(46, 53, 62);
    public static readonly Color Rock = C(88, 92, 99);
    public static readonly Color IronOre = C(178, 106, 50);
    public static readonly Color CopperOre = C(52, 158, 158);
    public static readonly Color Crystal = C(154, 92, 255);
    public static readonly Color Flora = C(47, 216, 176);
    public static readonly Color Water = C(38, 84, 122);
    public static readonly Color BeltCol = C(52, 58, 66);
    public static readonly Color BeltEdge = C(84, 94, 106);
    public static readonly Color Text = C(215, 221, 228);
    public static readonly Color TextDim = C(140, 150, 160);
    public static readonly Color Accent = C(63, 167, 214);
    public static readonly Color Good = C(120, 220, 150);
    public static readonly Color Warn = C(240, 190, 90);
    public static readonly Color Bad = C(239, 90, 110);
    public static readonly Color Enemy = C(207, 60, 96);
    public static readonly Color Colonist = C(255, 209, 102);
    public static readonly Color Panel = C(24, 29, 35);
    public static readonly Color PanelLight = C(36, 43, 51);
    public static readonly Color GhostOk = C(110, 230, 140);
    public static readonly Color GhostBad = C(239, 90, 110);
    public static readonly Color HubCol = C(63, 167, 214);
    public static readonly Color Wire = C(214, 168, 90);
    public static readonly Color Fog = C(11, 13, 16);
    public static readonly Color Pollution = C(168, 92, 60);
    public static readonly Color BotCol = C(120, 200, 170);
    public static readonly Color TrainCol = C(150, 160, 175);

    public static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);
    public static Color CA(int a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);

    public static Color Darken(Color c, float f) =>
        C((int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));
    public static Color Lighten(Color c, float f) =>
        C(c.R + (int)((255 - c.R) * f), c.G + (int)((255 - c.G) * f), c.B + (int)((255 - c.B) * f));

    public static Color ItemColor(ItemKind k) => k switch
    {
        ItemKind.IronOre => C(196, 120, 60),
        ItemKind.Crystal => C(170, 110, 255),
        ItemKind.Biomass => C(60, 220, 180),
        ItemKind.IronPlate => C(200, 200, 210),
        ItemKind.Ammo => C(255, 210, 90),
        ItemKind.Food => C(150, 220, 120),
        ItemKind.AdvPart => C(110, 200, 255),
        ItemKind.CopperOre => C(72, 168, 168),
        ItemKind.CopperPlate => C(222, 128, 88),
        ItemKind.SciencePack => C(140, 235, 245),
        ItemKind.Drone => C(230, 230, 160),
        ItemKind.WarBot => C(190, 120, 120),
        _ => Color.White,
    };

    public static Color BuildColor(BuildKind k) => k switch
    {
        BuildKind.Belt => BeltCol,
        BuildKind.FastBelt => C(66, 88, 120),
        BuildKind.Drill => C(120, 96, 64),
        BuildKind.DeepDrill => C(105, 82, 55),
        BuildKind.Smelter => C(150, 84, 60),
        BuildKind.Fabricator => C(96, 96, 128),
        BuildKind.FabT2 => C(110, 108, 150),
        BuildKind.FabT3 => C(126, 120, 175),
        BuildKind.BioProcessor => C(56, 120, 96),
        BuildKind.StorageCrate => C(120, 100, 70),
        BuildKind.Turret => C(96, 64, 84),
        BuildKind.HeavyTurret => C(116, 64, 74),
        BuildKind.Watchtower => C(122, 102, 70),
        BuildKind.Reactor => C(150, 130, 60),
        BuildKind.SolarPanel => C(60, 90, 140),
        BuildKind.WindTurbine => C(110, 120, 130),
        BuildKind.Battery => C(80, 130, 100),
        BuildKind.PowerPole => C(140, 110, 74),
        BuildKind.Pipe => C(110, 130, 135),
        BuildKind.Pump => C(70, 130, 150),
        BuildKind.Tank => C(120, 140, 145),
        BuildKind.Boiler => C(150, 100, 70),
        BuildKind.SteamEngine => C(130, 125, 110),
        BuildKind.Rail => C(96, 92, 88),
        BuildKind.ElevatedRail => C(120, 114, 108),
        BuildKind.TrainStop => C(90, 110, 140),
        BuildKind.Locomotive => TrainCol,
        BuildKind.DronePort => C(150, 145, 100),
        BuildKind.Inserter => C(170, 140, 90),
        BuildKind.Merger => C(74, 84, 96),
        BuildKind.FilterSplitter => C(96, 110, 90),
        BuildKind.SpikeTrap => C(110, 100, 90),
        BuildKind.IED => C(140, 90, 80),
        BuildKind.ShieldGen => C(90, 140, 190),
        BuildKind.BotFactory => C(110, 130, 120),
        BuildKind.Wall => C(96, 100, 110),
        BuildKind.Hab => C(70, 96, 120),
        BuildKind.MessTable => C(130, 100, 80),
        BuildKind.Lamp => C(180, 160, 90),
        BuildKind.Lab => C(96, 128, 150),
        BuildKind.Garden => C(48, 96, 64),
        BuildKind.MedBed => C(150, 160, 170),
        _ => Color.Gray,
    };

    private static readonly Dictionary<Color, SolidBrush> _brushes = new();
    private static readonly Dictionary<(Color, float), Pen> _pens = new();

    public static SolidBrush B(Color c)
    {
        if (!_brushes.TryGetValue(c, out var b)) { b = new SolidBrush(c); _brushes[c] = b; }
        return b;
    }

    public static Pen P(Color c, float w = 1f)
    {
        var key = (c, w);
        if (!_pens.TryGetValue(key, out var p)) { p = new Pen(c, w); _pens[key] = p; }
        return p;
    }
}

/// <summary>World/Fx particles (lasers, sparks, rings, drone trails).</summary>
public sealed class Particle
{
    public int Kind;             // 0 laser 1 spark 2 ring 3 drone-dot
    public float Ax, Ay, Bx, By;
    public float Ttl, Max;
    public Color Col;

    public Particle(int kind, float ax, float ay, float bx, float by, float ttl, Color col)
    {
        Kind = kind; Ax = ax; Ay = ay; Bx = bx; By = by; Ttl = ttl; Max = ttl; Col = col;
    }
}

public sealed class LogLine
{
    public string Text = "";
    public Color Col;
    public float T = 12f;

    public LogLine(string t, Color c) { Text = t; Col = c; }
}

/// <summary>An alert for the clickable alerts panel.</summary>
public sealed class AlertLine
{
    public string Text = "";
    public Color Col;
    public float X, Y;         // world position to jump to
}

/// <summary>A clickable UI rectangle, rebuilt every frame by MainForm.</summary>
public sealed class UiButton
{
    public Rectangle R;
    public string Id = "";
    public string Text = "";
    public string Sub = "";
    public bool Enabled = true;
    public bool Active;
    public Color? Tint;

    public UiButton(Rectangle r, string id, string text, string sub = "", bool enabled = true, bool active = false, Color? tint = null)
    {
        R = r; Id = id; Text = text; Sub = sub; Enabled = enabled; Active = active; Tint = tint;
    }
}

/// <summary>A horizontal click/drag slider (world-gen screen).</summary>
public sealed class UiSlider
{
    public Rectangle R;
    public string Id = "";
    public string Label = "";
    public float Min = 0f, Max = 1f, Value = 0.5f;
    public string Fmt = "0.00";

    public float Frac => Max <= Min ? 0 : (Value - Min) / (Max - Min);
}

/// <summary>A simple text field (seed entry).</summary>
public sealed class UiTextBox
{
    public Rectangle R;
    public string Id = "";
    public string Label = "";
    public string Text = "";
    public bool Focused;
}

/// <summary>Current selection (colonist, building, or raider).</summary>
public sealed class Selection
{
    public Colonist? Col;
    public Building? B;
    public Raider? Foe;
    public bool IsNone => Col == null && B == null && Foe == null;
    public void Clear() { Col = null; B = null; Foe = null; }
}

/// <summary>Everything the renderer needs about input/view state each frame.</summary>
public struct ViewState
{
    public AppState App;
    public ToolKind Tool;
    public BuildKind? ToolBuilding;
    public Dir ToolFacing;
    public int GhostX, GhostY;
    public bool GhostValid;
    public string GhostReason;
    public bool GhostVisible;
    public List<UiButton> Buttons;
    public Point Mouse;
    public string? Hover;
    public bool Paused;
    public float Speed;
    public bool ShowHelp;
    public bool AltDown;
    public bool ShowGrid;
    public BuildCategory? OpenCategory;
    public Selection Sel;
    public string? ModalTitle;

    public OverlayMode Ovl;
    public List<UiSlider> Sliders;
    public UiTextBox? SeedBox;
    public Image? GenPreview;
    public string GenCaption;
    public int Storyteller;                 // world-gen selection

    // v0.4 panels
    public List<AlertLine> Alerts;          // clickable alerts stack
    public bool ShowMinimap;
    public bool ShowWork;                   // work priority screen
    public bool ShowDebug;                  // dev menu panel
    public bool DevMode;                    // settings gate
    public float Sunlight;                  // 0..1 for night rendering
}
