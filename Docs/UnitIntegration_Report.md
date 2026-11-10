# Unit Integration Report

Written for the person doing the next merge, which adds the real world map. It records what the unit integration work brought in, where it lives, how it is tuned, and what is still open. Paths are relative to the repository root unless they start with a drive letter.

## Assets

### Models and prefabs

Each unit has its own folder under `Client/Assets/Art` with the FBX, a `PF_` prefab, its materials and a `Textures` subfolder.

| Unit | Folder | FBX | Prefab | Textures |
|---|---|---|---|---|
| Soldier | `Units/Soldier` | `Soldier_Idle.fbx` (mesh and avatar), `Soldier_Run.fbx`, `Soldier_Fire.fbx`, `Soldier_Dead.fbx` | `PF_Soldier` | BaseColor, TeamMask |
| Rifle | `Units/Rifle` | `Rifle.fbx` (global scale 1.3) | none, nested in `PF_Soldier` under `RifleSocket` | BaseColor, Normal |
| Tank | `Units/Tank` | `Tank.fbx` | `PF_Tank` | BaseColor, Normal, TeamMask |
| HQ | `Buildings/HQ` | `HQ.fbx` | `PF_HQ` | BaseColor, Normal, TeamMask |
| Tower | `Buildings/Tower` | `Tower.fbx` | `PF_Tower` | BaseColor, Normal, TeamMask |
| Wall | `Buildings/Wall` | `Wall.fbx` | `PF_Wall` | BaseColor, Normal, TeamMask |

Tower and HQ (and the Blender export script in `D:\repository\RTSAssets\Tools\export_counter_rotated.py`) were exported counter rotated so Unity imports them with zero rotation: root +90 X, children -90 X, -Z forward, Y up, no baked space transform.

Unit icons rendered from these prefabs live in `Client/Assets/Art/Icons/Icon_<Name>.png`.

### Materials and team color

- Every unit keeps its original URP Lit material `M_<Unit>.mat`. The FBX importer still maps to it through `externalObjects`, so re-importing the FBX does not lose it.
- Each prefab overrides its renderer with `M_<Unit>_Team.mat`, which uses the shader graph `Client/Assets/Art/Shaders/SG_TeamColorLit.shadergraph`. The Rifle has no team variant and is not in the Soldier's `TeamColor` renderer list.
- Shader properties: `_BaseMap`, `_BumpMap`, `_TeamMask`, `_TeamColor` (default RGB 21, 69, 148), `_PanelLuminance` (0 to 1), `_Metallic` 0, `_Smoothness` 0.2.
- The graph computes `L = dot(base.rgb, (0.2126, 0.7152, 0.0722))`, `tinted = saturate(TeamColor * L / _PanelLuminance)`, `BaseColor = lerp(base, tinted, TeamMask.r)`. Panel luminance is the mean luminance of the masked panel texels, so the team color reproduces at full strength on an average panel texel.

Per material panel luminance, which is the locked `Lref` from the mask tool:

| Material | `_PanelLuminance` |
|---|---|
| `M_Soldier_Team` | 0.07151 |
| `M_Tank_Team` | 0.06528 |
| `M_HQ_Team` | 0.09782 |
| `M_Tower_Team` | 0.10420 |
| `M_Wall_Team` | 0.07904 |

Masks are `<Unit>_TeamMask.png` next to each base color. They were generated with `D:\repository\RTSAssets\Tools\team_mask.py` (HSV window, no blur or dilation). The locked window settings per asset are in `D:\repository\RTSAssets\Tools\TeamMask_LockedSettings.txt`, which also lists the known limits: some solid panel texels clamp under yellow, pale emblem outlines stay light blue, HQ weathering is splotchy.

Runtime side, in `Client/Assets/Scripts/Rendering` (namespace `RTS`):

- `TeamColor` sits on every unit prefab with a renderer list. `SetTeamColor(Color)` swaps each renderer's shared material for a cached variant.
- `TeamColorMaterialCache` keeps one material per base material and color, named `<base>_RRGGBBAA`, and is cleared on subsystem registration. Nothing instantiates materials per unit.
- Player colors come from the server, `Server/Multiplayer.Server/PlayerRegistry.cs`: blue, red, green, yellow, purple, cyan, orange, white.

## Runtime data

Both assets are in `Client/Assets/Resources` and loaded with `Resources.Load` at startup. A missing one is a logged error.

### UnitVisualSet.asset

One `UnitVisualEntry` per unit, found by `Name`. Fields: `Name`, `Prefab`, `Icon`, `IconYaw`, `IconZoom`, `Scale`, `ColliderCenter`, `ColliderSize` (model space, before `Scale`), `MuzzleAttach`, `MuzzleOffset`, `TurretPivotName`, `TurretTurnRate`, `DeathClipSpeed`, `RunClipSpeed`, `CorpseHoldSeconds`.

All entries have `IconZoom` 1, `TurretTurnRate` 180 and `DeathClipSpeed` 1.

| Field | Soldier | Tank | HQ | Tower | Wall |
|---|---|---|---|---|---|
| Scale | 1.5 | 2.6 | 5 | 3 | 5 |
| IconYaw | 15 | -35 | -35 | -35 | -35 |
| ColliderCenter | (0, 0.5, 0) | (0, 0.264, 0.117) | (0, 0.350, 0) | (0, 0.463, 0.064) | (0, 0.150, 0) |
| ColliderSize | (0.45, 1, 0.45) | (0.537, 0.528, 1) | (1, 0.701, 0.974) | (0.927, 0.925, 1) | (1, 0.299, 0.160) |
| MuzzleAttach | empty (rifle tip) | TurretPivot | none | TurretPivot | none |
| MuzzleOffset | 0 | (0, 0.13, 0.6) | 0 | (0, 0.15, 0.56) | 0 |
| TurretPivotName | none | TurretPivot | none | TurretPivot | none |
| RunClipSpeed | 1.3 | 1 | 1 | 1 | 1 |
| CorpseHoldSeconds | 3 | 0 | 0 | 0 | 0 |

Notes:

- An empty `MuzzleAttach` puts the muzzle at the tip of a child named `Rifle` if there is one, otherwise the unit has no muzzle.
- The Soldier `Scale` in the asset is 1.5 while the editor default in `CreateUnitVisualSet.cs` is 1.1. The refresh never overwrites `Scale`, so the asset value wins. Decide which one is right when retuning against the map.
- Collider sizes for Tank, HQ, Tower and Wall were taken from combined renderer bounds; the Soldier's is hand set.

### FxSet.asset

Prefab references `muzzleSmall`, `muzzleTank`, `tracer`, `shell`, `impactSmall`, `impactTank`, `explosion`, `dustPuff`, plus `tracerSpeed` 120 units per second and `shellTravelTime` 0.2 seconds. Prefabs, materials and textures live under `Client/Assets/Art/FX`. The explosion prefab is authored for a footprint radius of 1.5 and the client scales its root by `FootprintRadius / 1.5` of the unit named in the event.

## Editor tools

All under the Unity menu `Tools/RTS`, source in `Client/Assets/Editor`.

| Menu item | What it does | Rerun when |
|---|---|---|
| Create Unit Visual Set | Creates or refreshes `Resources/UnitVisualSet.asset`. Always rewrites `Prefab`. Fills turret, death, run, corpse and icon fields only where empty or zero. Adds missing entries. | A prefab is renamed or replaced, or a new unit type is added. Hand tuned values survive. |
| Render Unit Icons | Renders each entry's prefab in a preview scene (Idle frame 0, FOV 25, pitch 35, per entry yaw and zoom) to `Art/Icons/Icon_<Name>.png` at 256 px, imports as a sprite, assigns `Icon`. | A model, material or `IconYaw` changes. |
| Report Prefab Bounds | Writes `Logs/rts_prefab_report.txt` with bounds, renderers, materials, hierarchy, animators and facing hints for every `PF_` prefab. | Diagnosing a prefab after import. Never required. |
| Generate FX | Writes the FX textures, materials and prefabs under `Art/FX` and `Resources/FxSet.asset`. Overwrites in place, GUIDs stay stable. | The FX look needs retuning. Edits made by hand to the generated prefabs are lost. |

## Gameplay types and stats

`UnitType` (byte): HQ 1, Soldier 2, Tank 3, Tower 4, Wall 5. `IsStructure()` is true for HQ, Tower and Wall.

`UnitDefs` (`Client/Assets/Scripts/Protocol/UnitDefs.cs`, shared with the server):

| Unit | MaxHealth | Speed | Range | AttackCooldown | Damage | DeathDuration | FootprintRadius |
|---|---|---|---|---|---|---|---|
| HQ | 500 | 0 | 0 | 0 | 0 | 2 | 2.5 |
| Tower | 300 | 0 | 10 | 0.25 | 4 | 2 | 1.5 |
| Wall | 400 | 0 | 0 | 0 | 0 | 2 | 2.5 |
| Soldier | 50 | 6 | 6 | 0.5 | 5 | 2 | 0.4 |
| Tank | 200 | 3 | 9 | 1.5 | 30 | 1.5 | 1.3 |

Derived at 20 ticks per second: death ticks 40 (Tank 30), cooldown ticks Tower 5, Soldier 10, Tank 30. Firing ticks are a pure function of tick and entity id (`AttackSchedule`), so the client predicts shots without extra network data.

Rules in `Server/Multiplayer.Server/World.cs`:

- Tower targeting: keep the current target while it is an enemy, not a structure, not dying and within range 10. Otherwise take the nearest such unit by XZ distance. Towers never target structures. HQ and Wall never attack.
- A target that disappears or starts dying is cleared. In range: Attacking, damage on firing ticks. Out of range: MovingToAttack in a straight line.
- Placement checks, in order: must be a structure, one HQ per player, Tower and Wall need an HQ, inside the 50 unit half extent, no circle overlap with any entity's footprint. Each player is capped at 50 entities.
- Production: only an HQ builds, only Soldier or Tank, spawned on rings around the HQ footprint.

## Networking changes made in this work

The protocol constant is `ProtocolVersion.Current` in `Client/Assets/Scripts/Protocol/ProtocolVersion.cs`. The server compiles the same Protocol folder through a linked include. Nothing checks the constant in the handshake yet.

| Version | Change |
|---|---|
| 4 | Game events: `GameEventMessage` batch (message id 21), `EntityDied` with `DeathCause`, `Explosion` with `ExplosionType`, and the `Dying` action state. |
| 5 | `Structure` split into HQ, Tower and Wall. `EntityState` gained a `Yaw` byte. `PlaceStructureRequest` gained `UnitType` and `Yaw`. |
| 6 | `ExplosionEvent` gained a `UnitType` byte. `ExplosionType.UnitDestroyed` added. Tanks emit an Explosion on their death tick, structures at removal, soldiers none. The client takes explosion scale from the event's unit type. |

Server tick loop (`GameServer.Run`): each tick has an absolute Stopwatch deadline at `origin + index * step`. The loop sleeps until 2 ms before the deadline, corrected by the worst sleep overshoot seen so far, then spin waits. If it falls more than 2 ticks behind it resyncs the origin instead of catching up. `--tick-stats` logs interval mean, min, max and standard deviation every 10 seconds. A tick polls input, integrates, broadcasts events (reliable ordered) and the snapshot (sequenced, or reliable unordered when larger than one MTU).

Client timeline:

- `SnapshotBuffer` keeps 8 snapshots keyed by server tick and estimates the clock offset as an exponential average of receive time minus tick time, resetting on a jump over 0.25 s. It brackets the render tick between two snapshots and never extrapolates.
- `RenderClock` renders at `(now - offset) / tickLength - 2` ticks, so the view is 100 ms behind the newest state. Positions are interpolated, discrete fields come from the earlier sample, and animation reads the later one so it leads movement by a tick.
- `EventQueue` holds events sorted by tick and releases them when the render clock reaches their tick, so deaths and explosions play with the state they belong to. Events older than the first snapshot seen are dropped.

F3 toggles the debug overlay (`DebugOverlay.cs`). Bottom left: clock offset, delay in ticks and ms, starved frames; events queued, released, dropped; the last released event with its tick. Each unit gets a label with id, type, owner, action state, target, health, and ticks until its next shot.

## Animation

Soldier only. The other units have no clips and die through a procedural collapse (shrink, spin, roll over 90 percent of the death ticks).

- Controller `Client/Assets/Art/Units/Soldier/AC_Soldier.controller`. Parameters `State` (int) and `Dead` (bool). States Idle, Run, Fire, Dead. Every transition starts from Any State with no exit time: Idle on `State == 0`, Run on 1, Fire on 2, all while not dead, blend 0.15 s; Dead on `Dead`, blend 0.32 s.
- `UnitVisual` maps action states to `State`: Idle 0, Moving and MovingToAttack 1, Attacking 2. A swap between Run and Fire applies only after the new state has been requested for 0.2 s continuously, so a chase that flickers between closing and firing does not restart the clips every tick. Idle applies at once.
- `animator.speed` is `RunClipSpeed` (1.3) while Run plays, otherwise 1. On Dying the `Dead` bool is set, speed becomes `DeathClipSpeed` (1), and the dust puff FX plays at the feet.
- Corpses: on entity removal the model is reparented to the scene root as `Corpse <id>` and `CorpseTimer` waits for the Dead state to finish (or clip length plus 1 s as a fallback), holds for `CorpseHoldSeconds` (3), then destroys it. No sink or fade.
- Clip import settings as they are now: all four soldier FBX files are Humanoid, Idle creates the avatar and the others copy it. Idle, Run and Fire loop, Dead does not. Only `Soldier_Fire.fbx` has root rotation baked into the pose based on the original orientation. `Soldier_Idle.fbx` does not, although the commit that polished the animation says it should. If the soldier twists while idling, bake Idle the same way. `Wall.fbx` has its rig set to None.

## Known gaps

- Walls do not block movement. Movement is a straight line with no collision; footprints are only checked at placement and spawn.
- Placed structures cannot be edited or rotated after placement, and there is no touch input, so this is doubly missing on mobile. Rotation is only the R key or Rotate button before placement, 0 or 90 degrees.
- No destroyed models. Structures shrink and spin then explode as FX, the soldier corpse is removed after its hold.
- No sound. The only audio object is the camera's `AudioListener`.
- Icons are not team colored. They are rendered in the editor with the default blue team color.
- UI is built in code with the built in `LegacyRuntime.ttf`, no styling pass.
- Unit scale is visual only and independent of the server footprint radius. The current values were tuned on the flat test ground and may need retuning against the real world map, along with the Soldier 1.5 versus 1.1 question above.
