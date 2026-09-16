# FORGEHAVEN — LEVEL-UP SESSION DELIVERY

All four bets are in the **original project** (`/home/user/ForgeHavenPrototype`).
The MadDog folder is retired — everything it contained that was worth keeping
(the 9 tested iterations: doors, repair, weather, wildlife, hints, night raids,
stats, sappers, caravans) was folded in first, then the four level-jumps were
built on top of it.

**Quality gate: Release build 0 errors / 0 warnings · SimTests 222/222** (101 at
the start of the MadDog session, 176 after it, 222 now).

## The four bets, as the player meets them

### 1. SOULS — colonists are people
- **Opinions** between every pair of colonists: proximity + shared passions,
  **battle bonds** (drafted together in combat), **shared meals** at mess
  tables; pessimists occasionally insult. Friends (+45) and rivals (−30)
  change mood when nearby; **a friend's death causes grief** (−18 mood, 8 min)
  and is chronicled with the mourner's name.
- **Mental breaks**: morale under 20 for ~45s risks a break — *dazed wandering*,
  *tantrum* (smashes your furniture), or rare *berserk* (attacks a colonist,
  stops before real harm). Breaks end in **catharsis** (+10 mood, morale reset).
- **The Chronicle [C]**: the run as a story — first fire, friendships, deaths
  and mourning, first storm, the first caravan, walls breached, blight
  sightings, the ark, the finale, your ending. Saved with the game.
- Bug fixed along the way: colonists killed by raiders **silently vanished**
  without the death log/grief (early-return in Update). Deaths now process
  from the update loop — no more silent deaths.

### 2. THE RECLAMATION — the world has a spine
- **The Blight**: corruption creeps in from the wastes at chunk resolution
  (purple wash on the map). It **slows colonists 30%, kills crops, and raiders
  sometimes step out of it**. Civilization pushes back: the Hub and every Lamp
  ward the ground around them (Gardens/Habs too, smaller). Choose "stay" at the
  end and Hope makes the blight recede 3×.
- **Chapters** (always-visible objective chip): 1 Survivors → 2 Industry →
  3 The Reclamation → 4 The Choice.
- **The Ark Wreck**: a 3×3 hull spawns 42–58 tiles out at worldgen (procedural
  sprite, marked by a clickable alert). Chapter 3 sends **miners to excavate
  it** (a real job with STR-scaled dig speed).
- **The finale**: 90s after the ark opens, **THE SILENCE ANSWERS** — a big wave
  (apexes + sappers, spawning from the blight if it's near). Survive it and the
  colony wins — with a **choice on the win screen**: `SIGNAL HOME` (rescue-is-
  coming epilogue) or `STAY — remake this world` (keep playing, Hope spreads).

### 3. THE ORGANISM — the factory has a metabolism
- **Slag**: every smelt also produces Slag. Ignore it and the output buffer
  backs up and the smelter stalls. Recycle it: `3 Slag → 2 Stone` (new recipe).
- **Deep production chain**: `Gear (2 iron)` and `Circuit (1 copper + 1
  crystal)` recipes; **AdvParts now need 2 Gears + 1 Circuit** — the win
  condition is a real factory, not a stockpile dump.
- **Wear & breakdowns**: every craft ages a machine; worn machines run up to
  30% slower, at 100% they **BREAK DOWN** (stop + red alert). Repairers
  restart them (wear maintenance is free; battle damage still bills Stone).
- **Production view [F5]**: status pips over every machine + a panel of
  RUNNING / STARVED / BLOCKED / NO POWER / WORN / BROKEN counts.

### 4. NEW WORLDS — replay variance
- Landing-site picker on the worldgen screen: **Verdant / Volcanic / Glacial /
  Fungal** — each bends terrain generation, sun (Glacial ×0.85), threat
  (Fungal ×1.15), blight spread (Fungal ×1.5, Glacial ×0.7), and writes its
  own opening line in the Chronicle. Persisted in saves.

## Engineering notes
- All enums **appended only** (BuildKind.ArkWreck, ColState.GoExcavate/
  Excavating, CmdType.Trade, WeatherKind, Biome, ItemKind.Slag/Gear/Circuit,
  WorkType.Repair already) — old saves load, new fields default safely.
- All tunables in `Bal` (blight rates, wear rates, deal of the finale, biome
  multipliers, break thresholds...). Every new string through `Loc.T`.
- Sim layer stays portable (no Windows APIs); 46 new headless tests.
- Two real engine bugs fixed: the **raider path-storm** (raiders with no path
  re-ran A* every frame — the suite went from 5s to 4+ minutes until we found
  it) and the **blight prune freeze** (epsilon ≥ spread step = stuck at zero).

## Changed files (copy to your PC project)
`Game.cs`, `Colonist.cs`, `Buildings.cs`, `Enemies.cs`, `Units.cs`, `Types.cs`,
`World.cs`, `Renderer.cs`, `Sprites.cs`, `MainForm.cs`, `UiTypes.cs`,
`Persistence.cs`, `src/SimTests/TestMain.cs`, `ROADMAP.md`.

## PLAYTEST RESPONSE SESSION (v0.0.53)

**Bugs fixed from playtest reports:**
- **Architect menu needed 2 clicks** to switch category: the live handler
  assigned `_cat` *before* comparing it, so the "different category?" check
  was always false and the first click just closed the tray. One click now.
- **"Pawns never mine"** — two causes: (1) new games started at MIDNIGHT
  (day-frac 0 = deep night), so everyone slept through your first orders;
  games now start at dawn (`Bal.DayStartFrac 0.25`). (2) The V-tool only
  ordered the first tile of a drag; dragging now paints mine orders
  continuously like the hint says.
- **Static starting seed**: the world-gen screen now rerolls the seed every
  time it opens.
- **Teleport-eating (engine bug found while fixing the above)**: a colonist
  whose path to the hub was null "arrived" instantly and ate hub food from
  across the map. Null-path eaters now wait and retry with a bigger
  pathfinding budget.

**Terrain rebuilt — landscape, not patches:**
- **Forests** and **water** are generated from coherent two-octave value
  noise (pure function of seed + world coords, so features span chunk
  borders with zero cross-chunk state). Forests are one or two huge rolling
  bodies (~90% of trees in the largest connected body on the test seed),
  tuned per biome (Fungal blooms, Glacial is sparse taiga).
- **Water**: noise lakes that never bury ore-rock, with a three-pass orphan
  cleanup (candidate -> mutual support -> fixed-point demote, plus a re-sweep
  of neighbour borders when a new chunk generates). No more detached
  1-tile puddles; the guaranteed starting lake and the spawn plaza also run
  through the orphan sweep.
- The spawn plaza is bigger (r=11): guaranteed dry build space next to the
  hub regardless of biome.

**Style pass:** every box in the game got the same design language —
rounded corners, vertical gradient fills, soft two-layer drop shadows,
accent hairlines, hover glow rings on buttons, tinted rounded alert chips,
and the build tooltip now matches the panels.

**Landing cinematic:** creating a world now glides the camera from high
orbit down to the colony over 3 seconds — letterbox bars, a drop pod with a
flickering thruster, a growing ground shadow, dust rings on touchdown, and
the FORGEHAVEN title. Any click or key skips it.

**Forest visuals fixed — real trees, not the flora mineral.** Forests
previously reused `Terrain.Flora` (the glowing spore resource sprite), so
noise forests looked like mineral fields. There is now a dedicated
`Terrain.Tree` (appended enum — saves stay compatible): three procedurally
baked tree variants (two pine silhouettes with lean differences, one round
broadleaf; hashed per-tile so dense forests don't tile), dark-forest-green on
the minimap, "Forest" tile tooltip. Mechanically trees behave like the old
flora: walkable, drillable for Biomass, hand-minable, deplete per tile,
absorb pollution — and the spawn area gets a guaranteed tree grove for early
biomass. Custom art drops in at `assets/terrain/tree0..2.png` (exported
automatically on first run). Old saves keep their flora tiles untouched.

**Quality gate: 0 errors / 0 warnings, SimTests 231/231** (was 225).

## SESSION v0.0.54 — TREES, LANDING SITE, CRASH CINEMATIC, HUB

- **Trees, for real this time** — 2-tile-tall sprites with 5 variants (cold
  pine / warm pine / broadleaf / birch / burnt snag, biome-biased mixes),
  per-tile jitter and scale so blocks never look tiled, drawn in a dynamic
  z-order pass. Collision is tile-soft: forests are walkable at 80% speed
  (Bal.TreeWalkMul) — the trunk feels small without a sub-tile collision
  system. Hand-art drops in at assets/terrain/tree0..4.png.
- **Pick your landing site** — click anywhere on the world-gen preview map;
  the whole colony (hub, plaza, guaranteed ore/grove/lake, ark wreck,
  colonists) spawns relative to your pick, and the blight now rings the
  colony instead of the map origin. A reticle marks your choice.
- **Crash-landing cinematic** — the hub itself falls from orbit: twin
  thrusters, accelerating drop, impact flash, screen shake, dust rings, and
  the six survivors pop out of the ramp one by one. Sim is frozen until the
  dust settles; any click/key skips.
- **Hub redesigned** — a crash-landed landing craft: octagonal hull, four
  piston legs (one bent from the crash), glowing reactor with radial vanes,
  dorsal antenna and beacon, half-open cargo ramp spilling warm light,
  hazard chevrons, FH-06 hull ID. Exports to PNG for your own art pass.
- 11 new tests (landing-site placement, hub-relative blight, tree walk
  contract). **Gate: 0 errors / 0 warnings, SimTests 242/242.**
- 20-idea candidate bank for the next session lives in ROADMAP.md.

## SESSION v0.0.55 — PLAYTEST FIXES (cinematic, trees, density, fog, perf)

- **Falling hub had no texture** — the cinematic drew `Building(Hub)` (an
  unregistered sprite -> hot-pink "missing" checker). It now uses the real
  `Sprites.Hub`. The placed hub was never affected; this was cinematic-only.
- **Cinematic pacing** — 6.0s total (was ~3.6 and finished in a blink at
  speed): fall 42% (~2.5s) -> impact shake 12% -> survivors pop out one by
  one over the last 46%. Still skippable with any click/key.
- **Trees redesigned as pixel art** — blocky 12x24 logical-pixel grid (3px
  pixels), rectangles only, no anti-aliasing, hard palette edges with
  dithered highlights: matches the rest of the tileset. All 5 variants
  rebuilt this way (cold pine / warm pine / broadleaf / birch / burnt snag).
- **Map density retuned + sliders** — defaults down from ~60% combined to
  ~28% forest / ~16% water on Verdant, and two new world-gen sliders:
  FOREST DENSITY and WATER & LAKES (0 = none at all, 1 = default, 2 = heavy;
  forest 0/28/49%, water 0/16/36% on the test seed). Contract-tested.
- **Trees respect fog of war now** — unrevealed chunks draw (and pay for)
  zero trees. Previously the tree pass painted over the fog fill entirely.
- **Render perf** — fog culling removes most tree draw calls (the majority
  of the map is fogged), plus a detail cutoff: below zoom 10px/tile trees
  draw as plain unscaled sprites with no jitter math. This is the cheap
  half of the pipeline rethink; the deep pass (baking trees into the chunk
  cache with dirty flags) is still open — see candidate bank.
- **Gate: 0 errors / 0 warnings, SimTests 248/248** (density-slider contract
  tests added).

## SESSION v0.0.56 — BELT UX & PAWN RENDERING

- **Drag to rotate** — dragging belts (or rails) now rotates them toward the
  drag direction, Mindustry-style: start a line going right, curve the drag
  upward and the new tiles face up. Diagonals snap to the dominant axis.
- **Belt animation** — rollers scroll along the flow (4-frame loop on belts,
  5 on fast belts; ~7.7 fps). The clock freezes while the game is paused.
- **Mindustry-style side-merge markers** — a belt fed from the side draws a
  chevron curve where the flows join (left/right of travel, all 4 facings).
  Straight chains still render plain.
- **Item z-order fixed** — items transferring belt -> belt used to sink under
  the receiving belt's sprite. All belt/junction items now draw in a second
  pass on top of every building.
- **Pawns: smooth, not blocky** — pawn sprites are now composed at 3x
  resolution (108px) and drawn with bilinear filtering, so they stay smooth
  at every zoom instead of magnifying a 36px pixel grid.
- **Yes, you can drop in hi-res textures** — and they now look right:
  - PAWNS: on first run the game exports every part template to
    `assets/units/pawns/` (body_s0..5, head_sk0..3_<dir>, hair_h0..5_<dir>,
    face_<dir>, shadow). Paint over them at ANY resolution (108px or higher
    recommended) — the compositor downscales smoothly.
  - BUILDINGS: any asset override larger than the baked sprite now draws
    with bilinear downscaling instead of nearest-neighbor crunch.
- 10 new tests (drag-facing contract, side-feeder sim facts).
  **Gate: 0 errors / 0 warnings, SimTests 258/258.**

## SESSION v0.0.57 — GRAY-TEMPLATE PAWN TINTING (Minecraft-style)

One grayscale texture per part — the code recolors it per pawn, so you paint
**9 files instead of 43**:

- **How it works**: on first run the game exports gray templates to
  `assets/units/pawns/`: `body.png`, `head_<dir>.png` (up/down/left/right),
  `hair_<dir>.png`, `face_<dir>.png`, `shadow.png`.
  Paint them in **grayscale at any resolution** (108px+ recommended):
  - **white** -> takes the pawn's full tone color
  - **darker grays** -> shaded versions of that color (178 gray = 70% shade)
  - alpha is preserved; black stays black (good for outlines)
- At load, each template is multiplied by the pawn's palette color:
  body <- shirt palette (6 colors), head <- skin palette (4), hair <- hair
  palette (6). Tinted results are cached, so it's a one-time cost.
- The palettes are plain constants in `Sprites.cs` (`Shirts`, `Skins`,
  `Hairs`) if you want to retune the game's colors themselves.
- **Housekeeping**: if you ran v0.0.56, the old toned exports
  (`body_s*.png`, `head_sk*.png`, `hair_h*.png`) are now ignored —
  delete them from `assets/units/pawns/` to avoid confusion.
- 3 new tests pin the tone bounds (6/4/6) the tint palettes depend on.
  **Gate: 0 errors / 0 warnings, SimTests 261/261.**

## SESSION v0.0.58 — WAIST-PACK BELT LAYER

The shaded waistband that was baked into the body is now its own tinted part,
exactly like the others:

- **New template**: `assets/units/pawns/belt.png` (gray, tinted by its own
  6-color waist-pack palette — leather, slate, olive, tan, burgundy, steel).
  Default bake: waistband + side pouch with a flap. Paint it like any other
  template: white = full belt color, grays = shading.
- Belt color is **independent** of shirt/skin/hair — every pawn rolls a
  `BeltTone` from its name hash, so two colonists in the same shirt can
  still carry different packs.
- The belt palette is `Belts` in `Sprites.cs` next to `Shirts`/`Skins`/`Hairs`
  if you want to retune the colors.
- NOTE: the old `body.png` (with the shaded waistband baked in) still works —
  the pack draws over it. Delete `body.png` once if you want the clean
  seam-free body template re-exported.
- 1 new test (belt tone spread). **Gate: 0 errors / 0 warnings, SimTests 262/262.**

## SESSION v0.0.59 — CURVED BELTS, TINT KEYS, MULTIBLOCK FIXES

### MAJOR BUG FIX: multiblock output
Drills (and any WxH machine) computed their output cell by stepping from the
ORIGIN CORNER tile — a 2x2 drill facing right/down pushed into ITSELF (the
target tile was inside its own footprint), so it could never feed a belt.
Output now leaves from the MIDDLE of the facing footprint edge, all 4
facings, any building size. Contract-tested for right + down.

### Multiblock graphic overhaul
- **Drill** re-baked at true 2x2 (64px): base plate, tread scuffs, corner
  bolts, gantry, hub ring + big drill bit, hazard rim on the deep drill,
  and output ports marked on every edge middle. No more stretched 32px.
- **ArkWreck** (3x3 landmark) had NO sprite at all — it was rendering as
  the pink Missing checker. Now a proper crashed-hull bake at 96px.
- Asset overrides for multiblocks: paint `buildings/drill.png` at 64px (or
  higher) — it loads native and downscales cleanly.

### Curved belts (Mindustry-style)
- Drag a belt line around a corner: the corner tile now becomes a CURVE
  (in from where the drag came, out along the new direction) instead of
  two straights snapping 90°. Reversals stay straight.
- Curves are fully animated (rollers scroll around the arc) and items
  visibly ride the bend (inlet -> center -> outlet).
- **Painting curves — your one-rotation idea, exactly:**
  - `assets/buildings/belt.png` — straight belt strip, painted facing
    RIGHT as a horizontal frame strip (frame size = image height).
  - `assets/buildings/belt_curve.png` — the CANONICAL curve: input from
    the NORTH, output EAST. The game rotates it into the other 3 inlets
    and mirrors it for the counter-clockwise turns — 8 variants, 1 file.
  - Same for `fastbelt.png` / `fastbelt_curve.png`. Static single squares
    also work (1 frame = no animation).
  - Frame strips for straight belts are exported as examples on first run.
- ⚠️ HOUSEKEEPING: old exports `belt_right/down/left/up.png` and
  `fastbelt_*.png` from earlier versions now load as STATIC single frames —
  delete them from `assets/buildings/` so the animated defaults shine
  through (the new `belt.png` strip export replaces them).

### Magic-key tinting (your near-white idea — implemented)
- **Colored pixels now pass through untouched** in gray templates: paint a
  metal buckle, camo patch or colored outline into body.png and it stays
  exactly as painted. Only neutral grays/whites take the tone color.
- **4 magic keys** (look white, code sees the difference, ±6 tolerance):
  - `#FFF7F0` shirt  ·  `#F0F7FF` skin  ·  `#F2FFF1` hair  ·  `#FFE8F0` belt
  - Usable inside ANY part template (e.g. paint a skin-key collar in
    body.png -> it takes the pawn's skin tone) — one file, multiple palettes.
- **Whole-pawn mode**: `assets/units/pawns/pawn_TEMPLATE_<dir>.png` is
  exported as a reference — copy it to `pawn_<dir>.png` and repaint to
  switch that direction to the ONE-FILE workflow: keys mark the tint
  regions, everything else (face, outlines, gear) stays as painted. Parts
  still work per-direction when no pawn_<dir>.png exists.
- Latent bug fixed on the way: loaded assets were being crushed to 32px at
  load time — hi-res part art would have arrived blurry. Large art now
  stays native.

### Water animation — needs your call (no change yet)
Confirmed: water is STATIC. The 3 frames exist but water tiles are baked
into the cached terrain chunks, so cycling means either redrawing water
every frame (your FPS budget) or rebuilding chunks a few times a second.
Say the word and I'll wire it with the cheapest option (periodic chunk
refresh) and we measure.

6 new tests (multiblock output x2, curve sim x4).
**Gate: 0 errors / 0 warnings, SimTests 268/268.**

## SESSION v0.0.60 — ROTATE PLACED BUILDINGS

- **Hover a placed building + press R** (empty hand, nothing selected) to
  rotate it 90° clockwise; **Shift+R** rotates counter-clockwise. Works on
  belts, machines, splitters, drills — any square footprint (1x1, 2x2, 3x3).
  The hover tooltip now says "— R rotates" so it's discoverable.
- **Curves rotate as a unit**: a curved belt keeps its bend (inlet and
  outlet turn together), so you can spin a corner into any orientation.
- **Drills re-target on rotate**: the output port follows the new facing —
  rotate a drill and it feeds the belt on the new edge (tested).
- R priority: colonist selected -> draft toggle (unchanged); empty hand
  over a building -> rotates the building; otherwise -> tool facing
  (unchanged). With an active build tool, R still flips the TOOL facing
  so you never rotate the wrong thing by accident.
- **Fix on the way**: curved-belt bends were NOT saved — a save/load cycle
  flattened curves back to straights. `BendIn` now persists with the save.
- 5 new tests (rotate cw/ccw, curve preservation, drill re-target).
  **Gate: 0 errors / 0 warnings, SimTests 273/273.**

## SESSION v0.0.61 — AUTO-CURVING BELTS (playtest feedback)

Both symptoms had one root: curves only existed when you *dragged* a corner.

- **AUTO-CURVE (Mindustry behavior)**: feed a belt from a perpendicular
  direction and the receiving belt now curves automatically — sprite and
  item path both. Exactly one side feeder + no back feeder = curve;
  back+side or two-side inputs stay straight (merge overlay). Re-derived
  every tick: rotate or extend lines and the curves follow on their own.
- **Teleport fixed**: an item entering a belt at a different rotation used
  to pop in at the belt's back edge (a diagonal jump). It now enters from
  the side it arrived — on a curve it rides inlet -> center -> outlet, and
  on a merge it slides in from the feeder's side (each item remembers its
  entry side).
- **Curve texture rebuilt**: the curve band was half as thick as the
  straight belts (r 11..21 vs 5..27), so joints stepped. Now the same
  thickness with rails lining up flush, rollers crossing the full band,
  chevron on the centerline.
- 5 new tests (auto-curve, chain stays straight, merge stays straight,
  entry side recorded + used).
  **Gate: 0 errors / 0 warnings, SimTests 278/278.**

## SESSION v0.0.62 — FULL HI-RES ASSET SUPPORT (192px packs etc.)

Answer to "can I import a 192x192 asset pack, drawn correctly AND native
resolution?": **yes, everywhere now.** The loader already kept >48px art
native (v0.0.59), but several draw paths were still hard-wired to
nearest-neighbor, which would have crunched hi-res sources. All 27 draw
sites now pick their filtering from the source size:

- **Baked pixel-art (36px, or 72/108px multiblock bakes)** → NearestNeighbor,
  crisp exactly as before - zero visual change to the default look.
- **Anything larger (your 192px pack)** → Bilinear downscale, clean at every
  zoom, and the bitmap stays at its ORIGINAL resolution in memory (no
  pre-crush to 36px). Downsampling happens only at draw time.
- Covered: all buildings (incl. hub, inserter arm, multiblocks with their
  own S x W threshold), belt + curve frame strips, rails, terrain tiles
  (ground/decor/ore/water/flora/rock — chunk cache AND direct path share
  the same code), and trees.
- One nuance: PAWN PART files are composited onto a 108px master (3x
  supersample) - a 192px body.png is downscaled smoothly to that master,
  which is full quality at any zoom the game uses. WHOLE-PAWN files
  (pawn_<dir>.png) skip compositing entirely and stay 192 native.
- Perf: the check is one integer compare per draw for baked art; the
  interpolation switch only happens for genuinely hi-res sources.
  **Gate: 0 errors / 0 warnings, SimTests 278/278.**

## SESSION v0.0.63 — PAWN INVENTORY & TREE COLLECTIBLES

Trees are no longer ore patches, and manual hauling is now physical:

### Trees = collectibles
- Drills no longer accept forest (placement rule, mining yield, everything)
  — flora patches stay drill-able as before.
- Mark a tree with the mine tool [V]: a pawn walks over, fells it in one
  cut (1.8s), and the wood (8 Biomass) goes into the pawn's INVENTORY —
  then the pawn carries it to the hub by hand. Tooltip updated to say so.

### Pawn inventory (new mechanic)
- Every pawn carries up to **12 units of one cargo type** (`PawnCarryCap`).
- **No more teleports**: hand-mined ore, quarried stone, felled trees and
  carcass food all ride in the pawn's inventory to the hub. Big carcasses
  need multiple trips (the remainder waits).
- Pawns deliver when full or when their source runs out; idle pawns with
  cargo retry delivery automatically. Mine/gather orders are released
  (not cancelled) at pickup time, so the same pawn usually resumes right
  after dropping the load.
- Assignment guards: full pawns deliver first, and cargo types never mix.
- **Visible**: a little cargo bundle rides on the pawn's shoulder, the
  pawn panel (health tab) shows "Carrying: 8 x Biomass", and each deposit
  gets a log line ("X hauled 12 Iron Ore to the hub.").
- Cargo persists in saves.
- Machines are unchanged: drills/smelters still output through their own
  buffers to belts/inserters — the inventory is the *manual* transport.
- Known v1 limitation: a pawn killed mid-carry drops nothing (cargo lost).
- 8 new tests (felling, no-teleport contract, delivery, carry cap,
  single-cargo rule, drill placement both ways).
  **Gate: 0 errors / 0 warnings, SimTests 286/286.**

## SESSION v0.0.64 — THE BIG LOGISTICS BATCH

### Multiblock outputs — properly fixed this time
The v0.0.59 fix still picked ONE "middle" tile of the facing edge (and the
art claimed a center that even-sized edges don't have). Now machines output
from **every tile of the facing edge** — a belt anywhere along the edge
works, first accepting tile wins. The port is drawn live on the FACING edge
(rotating with R), so the art never lies about where items exit.

### Inserter rework
No more teleporting: a real arm with phases (rest at source -> grab ->
carry across -> drop -> return), a two-segment arm with a claw that
**visibly carries the item icon**, eased motion, blocked-drop retry with
the arm staying extended. Throughput = one item per swing cycle (1.0s) —
deliberately slower than direct machine->belt output. Per-inserter **grab
filter** (select + CYCLE GRAB FILTER). **Long-armed inserter**: grabs from
2 tiles away (1.25s cycle).

### Pawn inventory follow-ups (all three approved items)
- **Death drops**: a pawn killed mid-carry drops their cargo as a ground
  pile (icon + count badge) that haulers pick back up.
- **Generic hauling**: new Haul work type with its own priority slider.
  Machines with low input buffers (smelter, industrial furnace, assembler,
  greenhouse) attract a hauler; sources: hub stock, then dropped piles,
  then silos. Full pawns deliver first; cargo types never mix.
- **Stockpile zones**: new tool button (▦) — drag a rectangle of ground to
  designate a storage zone (re-drag to unmark; loaded tiles stay). One
  item type per tile, 50 per tile. Pawns prefer the nearest zone tile over
  the hub when delivering. Rendered with contents + counts.

### Six new multiblock buildings (Factorio/Mindsight-inspired)
- **Blast Drill** (3x3) — big ore area, ~2.6x drill throughput, 30 power.
- **Industrial Furnace** (2x3, first non-square footprint) — fast smelter,
  brick firebox + twin chimneys + glowing maw.
- **Storage Silo** (2x2) — 300 of one item type; inserters and haulers
  read from it, belts/inserters fill it.
- **Assembler** (3x3) — Gears + Circuits into Advanced Parts at scale.
- **Greenhouse** (3x3) — 1 Biomass seed + 15 power grows into 4 Biomass:
  renewable wood, closes the felled-tree loop.
- **Substation** (2x2) — power pole with 14-tile wire range + 9-tile
  coverage; one replaces a field of small poles.
Framework: W x H footprints everywhere (placement, occupancy, edges);
non-square footprints can't be R-rotated yet (by design for now).

30 new tests (edge outputs, arm carry/drop/filter/long-reach, death drop,
hub resupply, zones, and every new building).
**Gate: 0 errors / 0 warnings, SimTests 316/316.**

Known follow-ups (deliberately not in this batch): blueprint materials
still teleport at placement (escrow + haul-to-blueprint is its own
feature); zone contents UI (per-tile filters) is v2.

## Suggested next steps
- Balance pass: blight pacing vs. lamp cost, finale size, wear rate, trade
  deals (all in `Bal`).
- In-game feel check: the win-screen buttons, F5 pips, blight overlay and
  biome chips need your eyes (I can build, not play).
- Loc: new strings are English keys — `assets/lang/*.txt` files need human
  translation when you're ready.
