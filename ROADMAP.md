# FORGEHAVEN — Master Roadmap

Legend: ✅ done · 🔨 in progress this phase · ⬜ queued. IDs match the idea bank the dev picked from.

## Global design rules (standing orders)
- **Event pacing: minimum ~3 minutes of quiet between major events.** All world/event
  content routes through one EventDirector that enforces the gap (configurable in Bal).
  No machine-gun raids, back-to-back caravans, or daily disasters.
- **Humans translate.** Every new player-facing string goes through `Loc.T(...)` so
  fan translators get them in the language template. Never machine-translate.
- **Colonists do the work.** The player designates; pawns execute. Buildings are
  blueprints until a colonist builds them. Pawns pick jobs from priorities × skills.
- **Procedural texture first, generated/painted later.** New art starts code-baked,
  then gets the AI-generation / hand-paint pass (any resolution — dev shrinks by hand).
- Saves are disposable until v1.0: enum values only ever get APPENDED, but old saves
  may still degrade — acceptable, the dev restarts fresh each version anyway.

## Phase 1 — Colony Core Loop — COMPLETE ✅
| ID | Feature | Status |
|----|---------|--------|
| N12 | Architect menu: single-click category switching | ✅ |
| N13 | Settings: fullscreen, resolution, FPS limit, VSync(cap), pause-on-focus-loss, show-FPS | ✅ |
| Q20 | Perf overlay in dev menu (FPS, frame ms, LOD cache) | ✅ |
| N1 | Drag-box multi-select colonists (RimWorld style) | ✅ |
| N4 | Draft mode: direct control, right-click move/attack orders | ✅ |
| N14 | Drafted pawns clear fog of war (scouting) | ✅ |
| N16 | Drafted pawns: auto-fight nearby threats toggle | ✅ |
| N15 | Non-drafted pawns: Fight / Flee / Ignore stance | ✅ |
| N3 | Per-pawn panel with tabs: Overview / Health / Work / Combat | ✅ |
| N9 | Pawn panel moved to bottom-left | ✅ |
| N8 | Construction redesign: designation blueprints, colonists build over time | ✅ |
| N10 | Auto-assign: pawns take jobs by priority × skill suitability | ✅ |
| N20 | Multiblock buildings: Drill 2×2, Deep Drill 2×2 mining 4×4 area | ✅ |
| N17 | Manual ore gathering by pawns (slow, anti-softlock) | ✅ |
| N18 | Rocks mineable → Stone → Primitive Furnace (hand-smelting, no power) | ✅ |
| N11 | Newcomers/recruits arrive from the map edge and walk in | ✅ |
| N7 | Item textures on belts + resource menu icons (procedural → generated) | ✅ |
| N5/N6 | Resource bar → collapsible panel; info-box polish pass | ✅ |

## Phase 2 — Survival depth (in progress)
- [x] G3 Farming: Crop Plot — piped water + sunlight → Food (no biomass) ✅
- G4/G5 polish: DONE ✅ - hunger/mood/needs exist; food chain has TWO sources (BioProcessor + Crop Plot); food-mood shipped as Mess Table meal quality (eat within 6 tiles of a table = +6 morale for 180s, saved in ColonistDto.AteWell)
- [x] Q12 Ore patch tooltips — vein flood-fill, ~units + tile count ✅
- Q17 Auto-wire poles on placement
- New item textures wired everywhere (belt cargo, hub stock, recipes)

## Phase 3 — World & exploration
- [x] W16 Per-tile ore richness — finite deposits, drills deplete tiles to ground, saved in OreRle ✅ · W1 Biomes (desert/tundra/swamp/volcanic)
- W2 Rivers · W3 Cliffs+ramps · W17 Natural roads (pawn speed)
- W19 Choose landing site at worldgen · W9 Geothermal vents (free power)
- W5 Crystal regrowth · W13 Flora bloom events
- W20 Sky events (eclipses kill solar for a day) · W8 Scavenge sites (one-time loot)
- G1 Weather system (rain/solar, storms/wind, heat waves) · G2 Seasons
- W10 Weather fronts on minimap
- G15 Trucks & roads (vehicles pathing on paved tiles)

## Phase 4 — Events, factions & threat (all through EventDirector, min-gap enforced)
- G7 Named raider factions w/ territory · W7 Visible raider camps raids march from
- G8 Night raids (torches; lamps boost turret accuracy) · G12 Wildlife herds (huntable)
- G13 Trade caravans (barter) · G17 Ancient ruins mini-dungeons
- G18 Storyteller quests · G10 Bribe/truce (pay waves off)
- G20 Scenario goals / win conditions · W4 Cave layer (underground map)
- G6(unsynced) Sieges, G9 bosses, G19 adaptation — later, after core threat feels right

## Phase 5 — Meta & polish (dev called this "most important")
- M15 Wire ALL remaining strings through Loc.T (logs, help, worldgen) — do early, continuously
- M11 Rotating autosaves (keep 3) + crash recovery · M12 Replay (command log is deterministic)
- M7 Statistics screen · M6 Achievements · M14 Difficulty presets
- M1 In-game codex/wiki · M2 Tutorial chain (first 10 minutes scripted)
- M18 Animated water & belts · M20 Title screen shows latest colony
- M8 Photo mode · M9 Colorblind palettes + UI scaling · M13 Benchmark map
- M16 Sound pass · M17 Music (day/night themes)
- M3 Scenario editor · M4 Data-driven modding · M5 Formal asset packs · M10 Gamepad

## Phase 6 — Remaining QoL
- Q1 Blueprint copy-paste of placed layouts · Q2 Drag-line building (partially exists:
  belts/walls/pipes/rails drag-paint; extend to all linear kinds + preview)

## Pacing & testing gates (every phase)
1. `dotnet build` 0 errors/0 warnings · all sim tests green (currently 76, grows each phase)
2. Headless tests cover new sim systems; visuals verified by the dev in-game
3. Workspace stays lean: toolchain in /var/tmp, bin/obj wiped after every build
4. Report ends with the exact list of files the dev must re-copy

## MADDOG autonomous additions (ForgeHaven-MadDog fork)
- ITER-1 transparency ✅ — pawn panel mood breakdown (top factors + target %),
  stock panel net/day rates, blueprint hover tooltip (builder/progress)
- ITER-2 defense depth ✅ — Door (colonists pass, raiders smash), Repair job
  (WorkType.Repair, stone-billed), rotating autosaves (keep 3)
- ITER-3 weather G1 ✅ — rain/storm cycles: solar dip, wind surge, crop boost, overlay + lightning
- ITER-4 wildlife G12 ✅ — grazer herds, drafted hunting, carcass hauling; turret range rings
- ITER-5 onboarding M2-lite ✅ — 7-stage contextual hint chain, auto-advancing, dismissable
- ITER-6 night raids G8 ✅ — unlit turrets fire slower at night; lamps restore accuracy; event history [L]
- ITER-7 statistics M7 ✅ — threat meter in top bar, stats screen [S] with wealth sparkline
- ITER-8 sappers ✅ — wave 2+ raiders target walls/doors deliberately (G6-lite)
- ITER-9 trade caravans G13 ✅ — parked barter board, 8 deals incl. colonist recruit; replay-safe commands
- Session quality gate: build 0 errors/0 warnings, SimTests 176/176

## LEVEL-UP SESSION (the four bets, in the main project)
- **SOULS** ✅ — colonists are people: opinion web (friends/rivals, battle bonds,
  shared meals), grief when friends die, mental breaks (dazed / tantrum /
  berserk) from prolonged misery with catharsis after, and the CHRONICLE [C] -
  the run narrated. Also fixed a silent-death bug (raider kills never logged).
- **THE RECLAMATION** ✅ — the world pushes back: chunk-level BLIGHT creeps in
  from the wastes (wards: Hub/Lamps cleanse; it slows colonists, kills crops,
  and raiders sometimes step out of it). Four CHAPTERS with an always-visible
  objective chip: survive the first raid -> industry -> EXCAVATE THE ARK WRECK
  (a world-placed 3x3 wreck with a dig job) -> THE SILENCE ANSWERS (finale
  wave) -> win with a CHOICE: signal home or stay (Hope: blight recedes 3x,
  colony morale +5).
- **THE ORGANISM** ✅ — the factory has a metabolism: smelters belch SLAG that
  must be recycled (3 slag -> 2 stone) or the line backs up; GEAR + CIRCUIT
  recipes form a real production chain (AdvParts now need 2 gears + 1 circuit);
  machines accumulate WEAR, run slow, BREAK DOWN, and need repairers;
  PRODUCTION view [F5] with status pips over every machine.
- **NEW WORLDS** ✅ — pick your landing site at worldgen: Verdant / Volcanic
  (crystal-rich, dry) / Glacial (weak sun, iron-rich) / Fungal (flora
  everywhere, blight spreads 50% faster, angrier wildlife). Biome persists in
  saves, tints terrain gen, sun, threat, blight, and the chronicle's opening.
- Quality gate: 0 errors / 0 warnings, SimTests 222/222 (was 176).

## SESSION v0.0.54 — TREES / LANDING SITE / CRASH CINEMATIC / HUB
- **Trees v2** ✅ — 2-tile-tall sprites (5 variants: cold pine, warm pine,
  broadleaf, birch, burnt snag), drawn in a dynamic pass with per-tile
  scale/offset jitter (biome-biased mixes), walkable with a 20% slow
  (Bal.TreeWalkMul) = small-trunk collision feel. Exported as
  assets/terrain/tree0..4.png for hand-art.
- **Landing site selection** ✅ — click the world-gen preview to pick where
  to settle; hub, plaza, guaranteed patches, grove, lake, ark and colonists
  all place relative to the site; the blight rings the COLONY, not the origin.
- **Crash-landing cinematic v2** ✅ — the hub itself falls from orbit with
  twin thrusters, impact flash, screen shake, dust rings, then the six
  survivors pop out one by one. Sim frozen until the dust settles; skippable.
- **Hub redesign** ✅ — crash-landed landing craft: octagonal hull, four
  piston legs (one bent from the crash), glowing reactor core with radial
  vanes, dorsal antenna + dish + beacon, half-open cargo ramp with warm
  light, hazard chevrons, FH-06 hull ID. Exports for hand-refinement.
- Gate: 0/0, SimTests 242/242.

## CANDIDATE BANK v3 — 20 IDEAS (post-v0.0.54)
Benefit/cost rated ●●● / ●●● (S = small, M = medium, L = large effort).
1. **Fire spread** — lightning/IED/molotovs ignite tiles; rain douses; burnt
   forests regrow. Combat + weather + forests all interact. ●●● / M
2. **Seasons** — year cycle: winter ice (raiders cross frozen lakes!),
   autumn biomass bounty, summer blight surge. ●●● / M-L
3. **Weather radar** — storm fronts as visible map cells with a 60s forecast
   chip; turrets misfire in storms. ●● / S
4. **Prisoner recruitment** — the capture flow exists; add wardens, loyalty
   ticks, conversion events, escape risk. ●●● / M
5. **Beast taming & ranches** — capture grazers, breed, hauling animals,
   trainer work type. ●●● / M
6. **Expeditions** — send pawns + supplies off-map to sister wrecks; days
   away, risk/reward, radio reports. ●●● / M
7. **Voidspore economy** — blighted tiles grow a rare harvestable; the
   antagonist becomes an endgame resource. ●● / S
8. **Power grid view** — draw the graph, battery drain order, consumer
   priorities; brownouts get legible. ●● / S-M
9. **Price drift** — caravans remember your sales; dumping iron crashes its
   price; sell-diversity rewards. ●● / S
10. **Rival colony** — an NPC colony on the map: trade, raids, territory
    friction, endgame alliance vs. Silence. ●●● / L
11. **Relationships v2** — rivals escalate to duels, friends to lovers;
    couples share beds; long-timeline children. ●●● / M-L
12. **Robotics v2** — WarBot loadouts, patrol routes, charging pads,
    hauler-drone logistics balance. ●● / M
13. **Sound pass (M16)** — day/night ambience, raid klaxon, landing rumble,
    UI ticks. GDI+-safe via NAudio. ●●● / M
14. **Escape menu + save-slot UI** — proper pause screen, slot browser with
    colony screenshots. ●● / S
15. **Tutorial campaign** — 3 scripted scenarios: logistics, defense, souls.
    The hint system is already 7 stages deep. ●●● / M
16. **Map pins & pings** — player-set markers, right-click pings, UI-off
    screenshot mode. ● / S
17. **Mod hooks** — buildings/recipes from JSON; the asset-override layer is
    already external. ●● / M
18. **Custom difficulty director** — separate threat/economy/story sliders in
    settings (self-tune like RimWorld custom). ●● / S
19. **Terraforming endgame** — after STAY: blight purge towers, biome
    restoration projects, a rebuild-the-ark mega-project. ●●● / L
20. **New Game+** — carry colonists (skills, relationships, opinions) into a
    new world; the Chronicle remembers the last run. ●●● / M
- **PLAYTEST RESPONSE SESSION** ✅ — architect-menu single click; midnight
  start -> dawn (pawns slept through first mining orders); V-drag paints
  orders; worldgen seed rerolls on open; teleport-eating engine bug;
  noise-generated forests & water (coherent across chunks, orphan-free,
  biome-tuned); full UI style pass (rounded/gradient/shadow/glow); 3s
  landing cinematic (skippable). Gate: 0/0, SimTests 231/231. Also fixed:
  raider path-storm (no-path raiders re-ran A* every frame), repair ping-pong
  on full-HP worn machines, blight prune epsilon freeze.
