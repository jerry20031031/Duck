# CPU / Grey Shadow verification — 2026-10-06

## Initial verification scope

The initial request was verification only: no production scripts were changed
by that verification. The follow-up repairs are recorded separately below.
The opt-in editor harness is `Assets/Editor/Tests/Unit1AIVerification.cs`.
It runs from **Tools > UNIT1 Tests > AI Movement Pickup Combat Verification**,
uses Fusion Single with the actual UNIT1 scene and CPU/Grey Shadow prefabs,
and returns to the previous editor scene after stopping Play.
It refuses to run while scenes have unsaved changes.
Normal Play does not enable the harness or its combat suppression.

## Results

- Navigation regression: **2,186 assertions passed**.
- First run: all six actors moved. CPU 2 travelled 165.4 m but had no confirmed
  pickup after 60 seconds; the other five had picked up wands. This is an
  observation, not a confirmed pickup bug or a diagnosed cause.
- Second run: three CPU and three Grey Shadows all moved and obtained actual
  networked wand ownership in the 90-second measurement phase. Travel was
  161.2 / 61.9 / 230.1 m for the CPU actors, and 156.3 / 139.6 / 40.9 m for
  the Grey Shadows. These are accumulated horizontal displacements sampled
  once per simulation second, not straight-line distances or full path lengths.
- Combat was suppressed only during the movement/pickup measurement to keep
  actors alive. Targets, routes, positions and pickups were not forced in that
  phase. All six scene wands had distinct valid holders at its end.
- With normal AI combat restored for 20 seconds, CPU health changed, two CPU
  actors died, and one Grey Shadow changed faction through a real hit.
- Controlled fixtures confirmed CPU death stops its pending strike, releases
  its wand, freezes its root and replaces its body with a nonblocking grave.

## Two defects reproduced before the follow-up repair

1. **Grey cast survives conversion and damages a new ally.** A neutral Grey
   Shadow starts its real production windup coroutine against a red CPU.
   A real red CPU wand hit converts the Grey Shadow to red during windup.
   The queued cast still reduces the red CPU's health from **3 to 2**.
   `EnemyDuckAI.ResolveShadowCastAfterWindup` does not recheck the current
   faction relationship; `ConvertToFaction` does not invalidate that cast.
2. **CPU strike survives match completion.** A red CPU starts its production
   strike windup against a blue CPU. The real director's networked
   `MatchFinished` state is then set before impact. The blue CPU still loses
   health from **3 to 2**. `Unit1BotDuck.ResolveStrikeAfterWindup` does not
   recheck match completion, although its main AI tick stops after completion.

The controlled fixtures position disposable actors, acquire a real wand and
invoke the existing production windup coroutine; they do not represent a
full natural match. The full verification runs them after the natural
movement/combat phases; the dedicated timing regression runs them independently.

## Follow-up repair (user approved)

Only the two confirmed combat defects were placed in repair scope. The user
explicitly chose to defer the separate pickup/navigation issue.

- `EnemyDuckAI` now uses a local cast generation to invalidate all three
  queued spell types on conversion or disable. Conversion also clears the
  previous faction's targets, route and windup movement lock, and requests
  immediate replanning, retaining the wand and spell cooldown.
- A Grey Shadow rechecks the target's current faction before impact, including
  the case where the target becomes an ally without the caster converting.
- CPU and Grey Shadow damage, crystal and guardian windup resolvers all
  recheck match completion before damage, repair, shielding or cast effects.
- The editor harness now also exposes **Tools > UNIT1 Tests > AI Combat
  Timing Regression**, using disposable fixtures in the actual UNIT1 scene
  and local Fusion Single runner.

The timing regression includes positive controls: ordinary live-match hostile
CPU and converted Grey Shadow strikes must still cause real health loss.
It also checks that conversion invalidates the old cast even when the old
target remains an enemy of the new faction, rather than merely checking for
friendly fire.

### Final combat timing regression result: PASS

The final independent run passed in Unity after compilation:

- Real-hit conversion during a pending cast: ally health **3 -> 3**.
- Conversion cancels pending damage, crystal and guardian casts, even when
  the old target remains hostile to the caster's new faction.
- A target changing to the caster's team during windup receives no damage.
- Match completion suppresses all six CPU/Grey Shadow cast types: CPU target
  health **3 -> 3**, no crystal damage, no friendly-crystal repair or shield,
  and no caster shield.
- Positive control: live-match CPU and converted Grey Shadow attacks still
  inflict the expected damage.
- CPU death/grave/wand release/root freeze and pending-strike suppression pass.
- Navigation regression still passes **2,186 assertions**.

The full follow-up attempt and first timing-only attempt initially stopped
on test-fixture issues, not additional production repairs: interpolated
Rigidbody teleports needed immediate Transform synchronization before pickup,
and the positive-control hit's normal immunity needed to expire before the
separate lethal-hit test. Both fixture issues were corrected and the entire
independent combat timing regression was rerun successfully.

The final run ends with `[AI-CHECK] COMBAT TIMING REGRESSION PASS`.

### Pickup/navigation observation deferred during the combat repair

In the follow-up natural phase, CPU 2 travelled **223.9 m** without a pickup
after 90 seconds. The other five actors obtained wands. The remaining
**水晶守護杖 2**, at **(16.55, 1.10, 28.00)**, was available, but the read-only
route probe from CPU 2 returned **routeExists=False, routePoints=0**.
CPU 2 later obtained a wand during the 20-second combat phase after wands were
released by defeated actors. It was therefore not permanently immobile or
unable to pick up any wand. The reason for the remaining wand's unreachable
route was not diagnosed or repaired during that combat-only request.

## Pickup/navigation follow-up (user approved)

The subsequent request explicitly approved repairing the deferred pickup issue.
The saved UNIT1 geometry and actual CPU/Grey Shadow collider envelopes were
inspected without moving a wand, deleting scenery or saving the temporary scene.

### Diagnosed cause

The shared scanner's calibrated capsule radius is approximately **0.615 m**.
The centre of **水晶守護杖 2**, at **(16.55, 1.10, 28.00)**, overlaps the
low fences `gate (38)` and `gate (36)` for that capsule. It is not a valid
standing position. However, the wand's actual pickup validation permits a
**1.5 m horizontal radius** and **2.2 m vertical difference**.

Both AI callers previously passed **0.78 m** as their navigation arrival
distance. The scanner therefore rejected a reachable interaction because
it required an unnecessarily close approach to the blocked wand centre.
Read-only probes from the previously failing CPU location and all three CPU
spawn locations returned **zero route points** for that wand at 0.78 m.
At **1.4 m**, they returned **14 / 37 / 33 / 36 route points**, ending at a
clear cell near **(16.57, -0.39, 29.08)**, approximately **1.08 m** horizontally
from the wand. All other five scene wands had routes at both distances.

### Repair

- `Unit1WandPickup.NavigationArrivalDistance` derives the AI approach radius
  from that wand's serialized pickup distance, minus a 0.1 m safety margin.
  CPU and Grey Shadow use this same property, rather than separate 0.78 m
  thresholds. Pickup authority, ownership and height validation are unchanged.
- The shared scanner recognizes a clear position already inside the requested
  interaction radius as successful arrival even when the target centre is
  obstructed. Continuous capsule edge checks and the actual movement motor
  remain enabled. This is not a direct-movement or teleport fallback.
- Editor navigation regression now reproduces a fence beside a wand and
  verifies arrival through the real capsule motor, including a subsequent
  already-arrived route query. **2,597 assertions pass**.
- **Tools > UNIT1 Tests > AI Wand Pickup Regression** loads the actual UNIT1
  scene with Fusion Single. Disposable frozen holders genuinely claim the
  other five wands. The tested CPU and Grey Shadow, separately, must autonomously
  select the remaining guardian wand, move from the previously failing location
  and obtain its real networked ownership. Their routes and pickups are not
  forced; combat alone is suppressed in this test phase.

### Dedicated pickup regression result: PASS

Both actors started at **(1.51, -0.49, 22.12)**, selected `SeekWand` with
**水晶守護杖 2**, and obtained it near **(15.52, -0.49, 29.08)**. The test
also requires actual displacement greater than 5 m and `HasWand` together
with `IsHeldBy(actor.Object)`, not merely a drawn route or an animation.
The run ends with `[AI-CHECK] WAND PICKUP REGRESSION PASS`.

### Full post-repair regression result: PASS

After refreshing and compiling the final scripts in Unity, the full opt-in
verification was rerun with three actual CPU and three Grey Shadow prefabs.
All six obtained real wand ownership within the 90-second natural movement
phase, with no forced goals, routes, positions or pickups for those actors.
Accumulated sampled horizontal travel was **51.9 / 237.3 / 240.8 m** for CPU
**1 / 2 / 3**. Grey Shadow **1033 / 1034 / 1032** travelled **31.4 / 226.0 /
172.2 m**. All six scene wands had distinct holders; guardian wand 2 was held
by object **1037**, rather than remaining incorrectly unreachable.

Normal combat then ran for 20 seconds: two CPU actors died and one Grey Shadow
converted through real combat. The controlled timing probes also passed:
conversion cancels all three queued cast types, newly allied targets receive
no pending damage, match completion suppresses all six CPU/Grey cast types,
ordinary hostile strikes still cause damage, and CPU death creates a
nonblocking grave, releases the wand, freezes the root and suppresses a pending
strike. The run ends with **`[AI-CHECK] DONE total issues=0`**.

The harness stopped Play and restored the previous MainMenu editor scene.
No production scene, prefab, fence or wand placement was edited for this repair.

## Random flat NPC spawns (user approved)

The next request approved random CPU and Grey Shadow spawn positions, preferably
on flat ground. `Unit1GameDirector` now uses the same safe spawn sampler for both
types; humans retain their original spawn positions. NPC yaw is randomized.
Only the existing authoritative director creates the network objects; clients
receive the resulting positions through Fusion, rather than drawing their own.

`Unit1RouteScanner.TryFindSpawnPoint` derives the capsule footprint and feet
offset from the actual prefab collider, checks the collision grid, and samples
the centre plus eight surrounding foot-support points. The spawn surface must
have slope <= **12 degrees**, height spread <= **0.12 m**, and remain within
**0.3 m** of the level's ground plane; the body also needs static collision
clearance. Root height puts the capsule feet **0.04 m** above the highest support
sample, rather than reusing an arbitrary marker height.

A cached ground-supported connectivity flood uses the existing continuous
capsule edge checks to exclude isolated or enclosed ground that cannot connect
to a usable authored spawn area. Random rejection sampling avoids biasing all
spawns toward the first clear cell of a row. A bounded exhaustive pass is a
validated fallback; no ground or no space produces a warning, not an unchecked
spawn. CPU count records the number actually created.

The play-area rectangle spans the authored human/Grey spawn markers with a
**12 m margin**. This prevents a very large foundation collider from generating
opponents hundreds of metres away from the authored level. Selected NPCs keep
at least **8 m** from earlier NPC selections, existing ducks, all reserved human
spawn locations and faction-crystal locations. The director Inspector exposes
`minimumNpcSpawnSpacing`, default 8 m; no scene or prefab placement was changed.

### Spawn geometry and randomness regression: PASS

**Tools > UNIT1 Tests > AI Random Flat Spawn Regression** samples six batches of
three Grey and three CPU positions using the actual UNIT1 scene and prefab
colliders, restoring the previous scene and random state afterward. The final
run produced **36 distinct positions from 36 samples**, with **1,171 assertions**
passing for independent capsule overlap, nine-point ground support, spacing and
play-area limits. The run ends with `[SPAWN-CHECK] PASS samples=36 distinct=36`.

The synthetic navigation suite now also checks random supported spawns, steep
slopes, floor edges, obstacles, previous-selection spacing, an isolated flat
island, saturated spacing and a scene without any ground. Together with the
existing movement and pickup tests, **3,073 assertions pass**.

The full Fusion Single verification additionally checks actual generated
objects before movement: `[SPAWN-CHECK] live six NPCs flat ground / body
clearance / 8m spacing PASS`.

### Random-spawn live movement/pickup/combat result: PASS

With the final scripts compiled, the real director generated three Grey
Shadows and three CPU ducks at randomized positions. All six naturally moved
and obtained real wand ownership during the 90-second phase: CPU **1 / 2 / 3**
travelled **224.4 / 30.0 / 65.0 m**; Grey **1033 / 1034 / 1032** travelled
**97.4 / 261.7 / 38.0 m**. These remain accumulated sampled displacements,
not distances from spawn to pickup.

Normal combat then produced two CPU deaths and two real faction changes for
one Grey Shadow. All controlled conversion, match-end cast, live hostile-hit,
death/grave/drop/root-freeze and pending-strike probes passed. The final run
ends with **`[AI-CHECK] DONE total issues=0`** and returns to MainMenu edit mode.
It does not force the six observed actors' goals, routes, positions or pickups.

## Population increase: six Grey Shadows and nine wands

The approved density change raises the existing UNIT1 director's serialized
`enemyCount` from **3 to 6**, with the same default in the script and builder.
CPU still fills the human participant count to four; its population rule is
unchanged. The safe random flat-ground spawn sampler remains shared by both.

The focused **Tools > UNIT1 Content > Apply 6 Shadows and 9 Wands** upgrade
appends one Swift, Heavy and Guardian wand to the actual scene, for **three of
each ability**. It does not run the full content builder or recreate any map,
prefab, marker or crystal. New wand roots are at:

| Wand | World position |
| --- | --- |
| 星光快攻杖 3 | (1.51, 1.10, 54.17) |
| 虛空重擊杖 3 | (43.34, 1.10, 47.48) |
| 水晶守護杖 3 | (26.61, 1.10, 94.32) |

A before/after comparison of every serialized scene block found **no removed
objects**. Of 2,745 original blocks, only the wand container's child list, the
director's count/spacing, and the original six pickups' newly serialized
existing vertical-distance default changed. All map geometry, original six
wand transforms and authored spawn/crystal transforms were unchanged. The
three new NetworkObjects have their pickup behaviours baked into the saved
scene. Re-running the upgrade skips existing new-wand names.

### Nine-NPC spawn geometry: PASS

The real-map random-spawn regression now samples six batches of **six Grey
and three CPU**. It produced **54 distinct positions from 54 samples**, with
**1,837 assertions** for flat support, body clearance, 8 m spacing and play-area
limits: `[SPAWN-CHECK] PASS samples=54 distinct=54 assertions=1837`.

### Expanded live verification: pending

The full local Fusion harness now requires three CPU, six Grey and nine valid
network wands, with three per ability, before observing natural movement and
pickup. Its nine-NPC phase has **not yet completed**. A temporary compilation
failure in the concurrently added settings UI was subsequently resolved, but
the next edit-mode wand diagnostic accessed unspawned Fusion ownership and
threw. That diagnostic has been corrected to validate serialized pickup
distances without reading network state. Unity also displayed a native
application-error dialog during that attempt; further testing is awaiting
permission to close/reopen the editor. No nine-NPC live PASS is claimed here.

The earlier six-NPC results above are historical evidence, not verification of
this larger population or the three new wand pickup paths. In the actual
one-human match, three CPU plus six Grey means ten actors competing for nine
wands; a temporarily unarmed actor is intentional scarcity, not a failed spawn.

## Limits

This verifies local Fusion Single execution, not remote Shared clients,
late joins, authority transfer or host migration. The initial intermittent
remaining-wand failure is now reproduced geometrically and repaired, with
real CPU and Grey Shadow pickup confirmed for that specific target. This
still is **not an all-clear for every AI behavior or multiplayer synchronization**.

Evidence: `[WAND-DIAG]`, `[AI-REGRESSION]` and `[AI-CHECK]` entries in the
current editor's `Logs/unit1-ai-live-editor.log`. Historical runs are retained;
the second initial run's `DONE total issues=2` refers to the combat defects
recorded above, not a successful post-repair result.
