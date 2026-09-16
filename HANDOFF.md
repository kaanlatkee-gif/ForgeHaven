# FORGEHAVEN — AGENT HANDOFF DOCUMENT

> Written 2026-09-16 at the end of a long development session (v0.0.64).
> Purpose: let a fresh agent (or human collaborator) pick this project up
> with zero lost context and carry it to GitHub + continue development.
> Read this file fully before touching anything.

---

## 1. WHAT THIS PROJECT IS

**ForgeHaven** is a single-player colony-factory survival game prototype —
think RimWorld (needs, moods, raids, pawns) × Factorio (belts, machines,
inserters) × Mindustry (power, turrets, simple combat), set on a hostile
alien planet where your crew crash-lands and must repair their ark.

Hard technical identity — do not fight these:
- **C# / .NET 8, WinForms + GDI+** (`System.Drawing`). No engine, no GPU.
- **Everything is baked in code**: every sprite is procedurally drawn at
  startup into bitmaps ("bakes"). There are NO required art files.
- **Asset override system**: any baked sprite can be replaced by dropping a
  PNG in `assets/` (auto-exported templates on first run). Supports hi-res
  (192px stays native, bilinear downscale), frame strips, per-direction
  files, and Minecraft-style gray-template tinting.
- **Headless sim tests** (`SimTests` project) — 334 checks in this branch (316 v0.0.64 baseline + conveyor IO tests), no GDI+.
- Single solution: `ForgeHavenPrototype.sln`
  - `src/ForgeHaven/` — the game
  - `src/SimTests/` — the test suite (top-level statements, runs green in ~10s)

Previous stable baseline: **v0.0.64, build 0 errors / 0 warnings, SimTests 316/316.** This branch adds conveyor IO tests; expect 334 checks once the SDK is available.

---

## 2. THE USER — HOW TO WORK WITH THEM

- Turkish, in İzmir. Develops on a PC (RTX 4060 Mobile + i5-13500H);
  sometimes writes from mobile and **cannot test** in those sessions.
  English is a second language — keep prose plain and friendly.
- **Delivery method is copy-paste**: at the end of each working session,
  give him the exact list of changed `.cs` files to copy into his local
  project. (Once on GitHub this may change to commits — confirm with him.)
- **He tunes the constants.** Implement mechanics with named tunables in
  `Bal` (Types.cs) and let him adjust; don't hardcode balance.
- **Propose an idea list before any major new feature.** Standing rule. He
  picks, then says "yes" / "do it" — that's the green light.
- **No placeholder stand-ins** for requested features (two past incidents:
  flora-sprite reuse, predefined scenarios instead of map selection).
- **Human translations only** if localization strings are added (Loc.cs) —
  never machine-translate.
- Versioning is `v0.0.XY`, one bump per delivered session. Each session gets
  a section in `DELIVERY.md` (changelog, delivered to him via the file
  viewer). `ROADMAP.md` holds the idea candidate bank (20 rated ideas).
- Quality gate before any delivery: **0 errors / 0 warnings + all SimTests
  green**, then delete `bin/`/`obj/` from the workspace.
- Art style: **chunky pixelated** — he rejected smooth/anti-aliased art as
  clashing. Pixel-art bakes with rectangles, no AA where possible.
- Cinematics must "breathe" (multiple seconds, never sub-second).
- Perf target: **60 FPS on his mid-range laptop**. He suggested lazy
  rendering / LOD thinking. (Not yet met — see open items.)

---

## 3. IMMEDIATE TASK: MOVE TO GITHUB

The user wants this project on GitHub. This chat cannot connect to GitHub;
a future agent may have that ability. Suggested approach:

1. `git init` inside `ForgeHavenPrototype/` (the workspace root contains
   only this project).
2. `.gitignore`: `bin/`, `obj/`, `.vs/`, `*.user`, `.idea/`. Keep the repo
   clean of build output (bin/obj are always deleted before delivery anyway).
3. **Ask the user about `assets/`**: the folder mixes (a) auto-exported
   template PNGs — regenerable, safe to ignore — and (b) HIS OWN painted
   art — irreplaceable. Recommend: commit everything in `assets/` (it's
   ~small) OR ask him which files are his. Do not silently delete.
4. `DELIVERY.md` + `ROADMAP.md` are project history — recommend committing
   them (and this HANDOFF.md).
5. First commit suggestion: "ForgeHaven prototype v0.0.64 — colony-factory
   survival, C#/WinForms/GDI+, 316 sim tests".
6. A README.md is a natural follow-up offer (use section 1 of this file as
   the basis) — but ask first, per the no-unrequested-files rule.

---

## 4. BUILD & TEST ENVIRONMENT (SANDBOX QUIRKS)

The dev sandbox (Linux) has no GUI, so the game itself can't run there —
only the test suite. Known traps:

- **The dotnet SDK at `/var/tmp/dotnet` gets wiped between sessions.**
  Reinstall:
  ```
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir /var/tmp/dotnet
  export PATH=/var/tmp/dotnet:$PATH DOTNET_CLI_HOME=/var/tmp NUGET_PACKAGES=/var/tmp/nuget
  ```
- Build: `dotnet build ForgeHavenPrototype.sln -c Release`
- Test: `dotnet run --project src/SimTests -c Release`
- **GDI+ cannot initialize in the sandbox** (TypeInitializationException).
  SimTests therefore EXCLUDES Sprites.cs / Renderer.cs / MainForm.cs via a
  "portability canary" — never reference those files from tests.
- Verify the SDK still exists before believing a "0 errors" build — a dead
  `dotnet` makes grep count zero issues falsely (this bit us once).
- Windows-only APIs are fine (the game runs on his PC only).

---

## 5. FILE MAP (what lives where)

| File | Contents |
|---|---|
| `Types.cs` | All enums (**append-only — save compat!**), `Bal` constants (costs, footprints, power, all tunables), `DirU` helpers, `MineOrder`, `ItemPile`, `DirFromDelta`, palette color helpers |
| `Units.cs` | `CmdType` (command queue enum), `SimCmd`, `Trader`, enemies/beasts units |
| `Buildings.cs` | `Building` base (`Create` factory, `Rotate`, `W/H` footprint, virtual `WireRange`/`CoverRange`), `Belt` (+`BendIn`, `DeriveBend` auto-curve, `BeltItem.Entry`), `Inserter` (state machine: `Phase/Arm/Held/Filter`, `Reach`), `LongInserter`, splitters/junction/merger, `MachineBase` (In/Out buffers, Mindustry-style adjacent round-robin `TryPushOut`, `HaulNeeds`), `Drill`/`DeepDrill`/`BlastDrill`, `Smelter`/`IndustrialFurnace`/`PrimitiveFurnace`, `StorageCrate`/`StorageSilo`, `Assembler`, `Greenhouse`, `Substation`, `PowerPole` (unsealed on purpose), `TrainStop`, `DronePort`, turrets, etc. |
| `Colonist.cs` | Pawn sim: needs/mood, all `ColState` states (incl. `GoFetch`/`GoDeliver`), pawn inventory (`CarryKind`/`CarryN`, `Take`, `StartDeliver`, `StartHaul`), manual mining/gathering into inventory, death cargo-drop, `DeriveCosmetics` (name-hash tones incl. `BeltTone`) |
| `Game.cs` | `TryPlace` (footprint + terrain rules + material payment), job assigners (`AssignMiners/Gatherers/Repairers/Haulers`, `FreeHauler`), stockpile zones (`PileZones`/`PileCells`, `Toggle/Deposit/NearestStockpileCell`), `DropPile`, `ItemOfTerrain`, power grids (`RebuildGrids` — union-find over poles/hub with per-node ranges), full save/load DTO mapping |
| `Renderer.cs` | `Camera`, terrain chunk cache (LOD, fog-gated), `DrawTileContent`, `DrawBuildings` (per-building branches, deferred item z-pass, belt frames/curves/merge overlays), `DrawPilesAndZones`, `DrawTrees` (fog-culled, detail cutoff), pawn draw (bilinear, shoulder cargo bundle), animated inserter arm, no fixed machine item port, `DrawSpriteSmart`/`DrawBuildingSprite` (hi-res-aware filtering), hover tooltips + Alt cards |
| `Sprites.cs` | `S = 36` px/tile. All procedural bakes, asset loading (`TryAsset` native hi-res, `TryStrip` frame strips, `ApplyDirSet`), pawn gray-template tinting (`ApplyTones` + magic keys), belt/curve frame bakes, multiblock bakes at native size (2S, 3S), auto-export of templates to `assets/` (only-if-missing) |
| `MainForm.cs` | Input & UI: tool bar (build/bulldoze/mine/▦ stockpile), drag-paint with belt drag-rotation + corner curves, **R = rotate hovered building** (priority: drafted colonist > hovered building > tool facing; Shift = CCW), speed `,`/`.`, autosave (3 rotating slots), crash-landing cinematic (6.0 s), worldgen screen with sliders |
| `Persistence.cs` | `Settings` + `GameSave` DTOs (schema `Version = 4`), save file discovery |
| `UiTypes.cs` | `ViewState` (incl. `PileFrom`/`PileTo` preview), UI button/layout types |
| `SimTests/TestMain.cs` | 334 checks currently (316 baseline + conveyor IO tests), top-level statements, `Section`/`Check` helpers |
| `DELIVERY.md` / `ROADMAP.md` | Session changelog / idea bank v3 (20 rated ideas) |
| `assets/` | User's art overrides + auto-exported templates (see §3) |

---

## 6. CORE SYSTEM RULES (the invariants)

- **Mindustry-style machine IO**: production/mining blocks have no fixed
  item input/output ports. Every tile touching the footprint can be input or
  output; conveyors decide by direction (they reject their front side) and
  machines round-robin across accepting adjacent belts/buildings. Footprints
  come from `Bal.FootW/FootH` (IndustrialFurnace is 2x3, the first
  non-square).
- **Belt auto-curving**: `Belt.DeriveBend` runs each tick — exactly one
  perpendicular feeder + no back feeder ⇒ the belt renders/simulates as a
  curve (Mindustry-style). Drag-painting corners sets `BendIn` too; R-rotate
  turns curve inlet+outlet together.
- **Belt items + merge variants**: items carry the exact side they arrived
  from (`BeltItem.Entry`). `Bal.BeltItemOffset` draws entry edge → center →
  facing edge, with Entry taking priority over `BendIn` so left/right/both
  side merges animate honestly. Straight belts render left-only, right-only
  or both-side merge overlays. All belt/junction items draw in a second pass
  AFTER all buildings (z-order fix).
- **Pawn inventory**: 12 units, ONE item type per trip (`PawnCarryCap`).
  Manual mining, tree felling (trees are collectibles — drills reject
  forest), carcass gathering all ride in inventory to hub/stockpile. Full
  pawns deliver before taking new jobs.
- **Hauling**: `WorkType.Haul` (own priority). Every 0.5 s `AssignHaulers`
  scans machines with low input (`HaulNeeds()`) + orphan ground piles.
  Source preference: hub stock → dropped piles → silos.
- **Stockpile zones**: ▦ tool, drag rect, toggle semantics; one item type
  per tile, `PileTileCap = 50`; loaded tiles can't be unzoned.
- **Inserters**: phase machine (rest@source → grab → carry → drop →
  return), 1 item / `InserterCycle` (1.0 s; long = 1.25 s, reach 2).
  Slower than direct machine→belt ON PURPOSE. Grab filter cycled on select.
- **Power**: union-find grids over poles/hub/substations; per-node
  `WireRange`/`CoverRange` virtuals (substation 14/9, pole 7/5, hub /8).
- **Pawn art**: gray templates tinted per pawn; magic near-white keys
  `#FFF7F0` shirt / `#F0F7FF` skin / `#F2FFF1` hair / `#FFE8F0` belt
  (tolerance 6); any OTHER color passes through untouched; whole-pawn mode
  via `pawn_<dir>.png`. Composited at 3x (108 px), drawn bilinear.
- **Hi-res assets**: >48 px sources stay native at load; `DrawSpriteSmart`
  picks Bilinear (hi-res) vs NearestNeighbor (baked pixel-art) per draw.

---

## 7. VERSION HISTORY (one-line-per-session)

- v0.0.53 (231): flora shortcut fix, forest coherence tuning.
- v0.0.54 (242): 5 tree variants + tree walking, landing-site selection,
  crash-landing cinematic, hub redesign, 20-idea bank.
- v0.0.55 (248): cinematic hub-texture bug (hub is `Sprites.Hub`, NOT in
  `_buildings`!), 6 s pacing, pixel-art BakeTree, forest/water defaults
  28%/16% + worldgen sliders, fog culling, far-zoom detail cutoff.
- v0.0.56 (258): belt drag-rotate, belt animation (4/5-frame strips),
  side-merge markers, item z-order fix, 3x pawn composition + bilinear.
- v0.0.57 (261): gray-template pawn tinting (Minecraft-style).
- v0.0.58 (262): waist-pack belt layer, own 6-color palette, `BeltTone`.
- v0.0.59 (268): multiblock output fix attempt #1 (edge middle — later
  superseded), drill re-baked 2x2, ArkWreck sprite (was pink Missing),
  drag-curves, belt/curve frame strips (paint ONE rotation), magic tint
  keys + whole-pawn mode, loader keeps hi-res native.
- v0.0.60 (273): R-rotate placed buildings; BendIn save fix.
- v0.0.61 (278): auto-curving belts; item entry-side (teleport fix);
  curve band re-baked flush with straights.
- v0.0.62 (278): all 27 draw sites hi-res-aware (`DrawSpriteSmart`).
- v0.0.63 (286): pawn inventory + trees as collectibles; death-drop gap
  noted; drill placement rejects forest.
- v0.0.64 (316): edge outputs (all tiles), inserter rework (real arm +
  filter + LongInserter), death drops, Haul work type, stockpile zones,
  six multiblocks (Blast Drill 3x3, Industrial Furnace 2x3, Storage Silo
  2x2, Assembler 3x3, Greenhouse 3x3, Substation 2x2).

---

## 8. TRAPS & LESSONS (do not re-learn these)

- **Patch discipline**: this codebase is edited via scripted find/replace.
  ALWAYS verify every `ok:` line; a FAIL aborts mid-script and leaves a
  partial apply. After inserting methods, check brace balance — one real
  bug was a doc-comment continuation line missing `///` (CS1002 pointed at
  the comment, not the cause).
- **C# pattern scoping**: `if (b is BotFactory bf)` makes `bf` reserve the
  whole enclosing scope — CS0136 with an unrelated local of the same name.
- **Shadowing**: `StorageSilo.Kind` hid `Building.Kind` (CS0108) → renamed
  `StoredKind`. Watch for `Kind`/`W`/`H`/`Face` collisions on new classes.
- **Save compat**: enums are append-only; new Building fields need BOTH
  save and load DTO sides (BendIn was once saved-but-never-loaded).
- **Test-writing gotchas**: a blocked belt accepts nothing (advance it
  first); smelters consume inputs (use crate sinks for exact counts);
  grids rebuild on Update, not at placement; `FindOreTile` returns (0,0)
  when nothing found (seed-dependent!); haul tests need long sim windows
  (~90–220 s at dt 0.05 — that's normal, tests run fast in wall-time).
- **Sandbox**: SDK wipes (§4), GDI+ unusable, grep-exit-code hides "0
  issues" lies.
- **User-side quirks he already resolved himself**: a Visual Studio
  "project not recognized" scare (workspace was verified intact — ignore
  if it recurs); Chinese output once (his browser's translate — write
  English).
- **Water is intentionally static**: water tiles are baked into terrain
  chunk bitmaps; animating them costs chunk rebuilds — he hasn't approved
  the FPS trade. Don't "fix" it silently.

---

## 9. OPEN ITEMS / NEXT-STEP QUEUE

1. **GitHub migration** (§3) — the current task.
2. **Perf deep pass** — the standing unmet promise: 60 FPS target. Plan:
   bake trees into the chunk cache with dirty flags; batch GDI+ text.
   Ask the user for a profile first (tiles vs trees vs text).
3. **Blueprint materials still teleport** at placement — escrow + haul to
   blueprint is the designed follow-up (same class as the v0.0.63 fix).
4. **Non-square footprints can't be R-rotated** (deliberate v1).
5. **Water animation** — awaiting his call on the perf trade.
6. **Stockpile zone UI v2** (per-tile filters), zone contents list.
7. From the self-review punch list (he said "keep them in mind"): belt
   blockage feedback ("blocked — smelter full"), procedural audio (bake
   WAVs in code), routing-family consolidation playtest, save-state audit,
   five-beat onboarding. Candidate bank v3 lives in ROADMAP.md.
8. ROADMAP best-value ideas: fire spread, weather radar, price drift.

---

## 10. VERIFYING THE BASELINE (first thing a new agent should do)

```
# 1. SDK (see §4 if missing)
dotnet build ForgeHavenPrototype.sln -c Release   # expect 0 errors, 0 warnings
dotnet run --project src/SimTests -c Release      # expect: 334 passed, 0 failed
rm -rf src/ForgeHaven/{bin,obj} src/SimTests/{bin,obj}
```

If either gate fails, something changed since this document was written —
diff against DELIVERY.md's latest session section before assuming anything.

---

*Authored by the agent that built v0.0.53 → v0.0.64 with the user. The
user's word is final on scope, art, and balance. Keep the gates green.*
