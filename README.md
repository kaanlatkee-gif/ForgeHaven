# ForgeHaven — Prototype Slice (v0.4 "Logistics & War")

**Build the machine. Don't forget the people inside it.**

A top-down 2D colony sim + automation game: RimWorld-style colonists living
inside a Factorio/Mindustry-style factory, on a hostile alien world.
Pure **C# / WinForms / GDI+** — no engine, no assets, no external packages.
Every sprite is generated in code at startup. Opens straight in
Visual Studio 2022.

---

## Run it (Visual Studio 2022)

1. Open `ForgeHavenPrototype.sln`.
2. Set `ForgeHaven` as the startup project.
3. F5.

Requires the **.NET 8 SDK** with the *“.NET desktop development”* workload.

## Run it (CLI)

```bash
dotnet run --project src/ForgeHaven
```

## Headless simulation tests

```bash
dotnet build ForgeHavenPrototype.sln -c Release
dotnet run --project src/SimTests -c Release
# ==== 72 passed, 0 failed ====
```

---

## What's new in v0.4

### Trains & rails
- **Rail** tiles (drag-paint like belts) and **Train Stations**; drop a
  **Locomotive** on any rail and it shuttles between stations, loading one
  station's belt-fed buffer and unloading into the other (40-stack hold).
- Per-tile **block signals**: two trains never enter the same rail tile.
- **Elevated Rails** (B2 layer) — cross belts and ground rail freely,
  locked behind *Fast Logistics*.

### Logistic drones & construction bots
- Craft **Logistic Drones** (fabricator recipe) and belt them into a
  **Drone Port**: drones ferry crate→machine deliveries across an 18-tile
  footprint and **auto-repair damaged buildings** (1 iron plate per patch).

### Fluids: the steam power loop
- **Pump** (must sit on the start lake) → **Pipes** → **Boiler** (belt-fed
  biomass, steam exits its facing side) → **Steam Engine** = up to **+90
  power**. **Tanks** buffer 2,000 units. Separate water/steam networks —
  boilers convert, they don't connect.

### Belt toolkit
- **Merger** — actively pulls from machines/crates/stations behind, left
  and right, and pushes out its facing side.
- **Filter Splitter** — set an item filter (click to cycle): matches exit
  the side, everything else goes straight.
- **Inserter** — swinging arm, lifts from the tile behind onto the tile
  ahead (crate → machine without a belt).

### Power & clockwork
- **Day/night cycle** (3-minute days): solar output tracks the sun, dawn
  and dusk ramps, colonists prefer to sleep at night. Night lighting with
  warm lamp/hub glow pools.
- **Assembler II / III** (Assembly II & III research): 2× and 4× crafting
  speed fabricator tiers.
- **Mining Productivity** — the first repeatable (leveled) tech: +10% drill
  speed per level, 8 levels, rising pack costs.

### War
- **Pollution** (F4 overlay): industry emits, flora scrubs, wind diffuses —
  and raid waves use the smog cloud to aim at your colony.
- **Spike Traps** (wear out), single-use **IEDs**, and the **Shield
  Generator** — a 300hp bubble that soaks building damage in a 4-tile
  radius before walls ever get scratched.
- **War Bots**: craft them, belt them into a **Bot Factory**, set a rally
  point — hovering guards engage anything within 11 tiles of the flag.
- **Prisoners**: turrets sometimes *down* raiders instead of killing them.
  Click the body → **Capture** → a colonist drags it to the Hub brig.
  Feed prisoners and their recruit meter climbs (faster with high colony
  SOC) until they join as a colonist.

### Storytellers & pacing
- Pick **Basil the Builder**, **Randy the Random** or **The Merciless** on
  the world-gen screen. They shape raid sizing/pacing and fire random
  events (wanderer joins, solar flicker, mad wildlife).
- **Alerts panel**: clickable chips (low power, dry turrets, starvation,
  train with no route…) that jump the camera to the problem.
- **Time controls**: `,`/`.` step 0.5× → 4×; live speed readout.

### RimWorld colonist depth
- **Skills & XP**: six stats (STR INT DEX END SOC CRE) grow with work;
  every colonist has two **passions** (2× XP) shown with a heart.
- **Work priority screen (P)**: 1–4 priorities per colonist per work type;
  job assignment respects them.
- Morale now reacts to night work, combat proximity and industrial
  surroundings.

### Deterministic sim core (#69 groundwork)
- **Every player action flows through a command queue** (place, bulldoze,
  recipe, filter, rally, tech, capture, dev cheats) that is applied at a
  fixed point in the update loop and journaled.
- Same seed + same command list = identical checksums (verified by tests)
  — the foundation for replays and lockstep multiplayer.
- **Dev menu** (#70): toggle it in **Settings**; then the in-game menu gets
  give-items, finish research, reveal map, spawn raid, god mode.

### Everything from v0.3 still in
Open world + world-gen screen + fog of war, local power grids with
batteries and F1/F2/F3 overlays, belt junction & overflow router, express
belts, iron/copper plate economy, science-pack research screen, ghost
rotation UX, JSON saves/autosave, wealth-scaled ring raids, med beds,
morale/flow, win/lose states.

---

## Controls

| Key / mouse | Action |
|---|---|
| WASD / arrows / middle-drag | Pan camera |
| Mouse wheel | Zoom |
| 1–5 | Open Architect category drawers |
| Left click / drag | Build (paint belts/rails/pipes/walls), select, place |
| **R** | Rotate ghost (chevron shows output) |
| **X** | Bulldoze (50% plate refund) |
| **T** / **P** | Research screen / Work priorities |
| **F1 / F2 / F3 / F4** | Power / Defense / Logistics / Pollution overlays |
| **M** | Minimap toggle (click minimap to jump) |
| Alt (hold) | Detail card for hovered thing |
| Space | Pause |
| `,` / `.` | Slower / faster (0.5× → 4×) |
| Esc / right click | Cancel / pause menu |

## The loop

Ore → Smelter → Plates → Fabricator → Ammo / Science / Drones / War Bots /
Adv. Parts → Hub. Water → Boiler → Steam → +90 power. Trains haul between
outposts. Deliver **10 Advanced Parts** to the Hub to win. Wealth provokes
raids; **pollution aims them** — scrub with flora, or arm the perimeter and
capture the survivors.

## Project layout

```
ForgeHavenPrototype.sln
src/ForgeHaven/           the game (WinForms, net8.0-windows)
  Types.cs                enums, balance, catalog, palettes, day/night curve
  World.cs                chunked open world, gen params, fog, A*, pollution
  Buildings.cs            belts, junction, router, machines, poles, turrets,
                          trains' stations, pipes/boiler/engine, drones…
  Colonist.cs             needs, traits, skills/XP, priorities, capture duty
  Enemies.cs              raiders (ring spawns, downed/captured states)
  Units.cs                trains, guard bots, prisoners + COMMAND LAYER
  Game.cs                 economy, grids, fluids, pollution, raids,
                          storyteller, prisoners, save/restore, checksum
  Persistence.cs          settings + v4 JSON save DTOs & file IO
  Sprites.cs              all textures baked in code
  Renderer.cs             world, overlays, night light, minimap, panels
  MainForm.cs             input → commands, time controls, UI model
src/SimTests/             headless sim test harness (72 checks)
```

## Roadmap candidates

Blueprints, weather hazards, ore depletion + outposts, colonist social
layer, governance, production analytics, JSON modding, the Corruption
endgame threat — and multiplayer/replay on top of the command layer.

*Saves live in `%LocalAppData%\ForgeHaven\saves` (v0.2/v0.3 saves still load).*
