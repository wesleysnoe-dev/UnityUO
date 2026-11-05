# WorldDemo.cs Refactor Plan

Target: `Assets/UOClient.Unity/Runtime/WorldDemo.cs` (115,318 bytes, 2,301 lines, assembly `UOClient.Unity`).
Status: **plan only. Nothing in this document has been implemented.**

---

## 1. Summary

`WorldDemo` is one `MonoBehaviour` doing about 14 jobs: settings and paths, login, the online session wiring,
world and entity views, multis, effects, input and movement, camera, lighting, the HUD bridge, overhead text,
tooltips, cursor, IMGUI chat, and two long automated capture tours. The file also holds three unrelated
top-level types (`FrameSections`, `FrameStats`, `ItemView`).

Packet parsing is **not** in WorldDemo. It lives in `UOClient.Core/GameSession*.cs` (partial class,
`_handlers[0xNN] = ...` registered in the constructor plus `Register*Handlers()` per partial). WorldDemo only
subscribes to the `GameSession` / `World` events those handlers raise (around 35 subscriptions, mostly in
`SetUpUi` and `StartOnline`). So the refactor is about separating the **presentation and wiring** layer. It
does not touch the protocol layer. New packets (section 8) add a Core handler and event, plus one small
subscriber class on the Unity side.

Goal: `WorldDemo` becomes a thin composition root of about 300 lines. It owns the lifecycle (`Start`, `Update`,
`OnGUI`, `OnDestroy`), creates the subsystems, and calls them in a fixed order. Its serialized fields, its
class name, its namespace and its `.meta` GUID stay unchanged.

---

## 2. Current responsibility map

Line numbers are from commit `2aad159`. Sizes are approximate.

| # | Lines | ~Lines | Responsibility | Members |
|---|-------|-------|----------------|---------|
| A | 1–27 | 27 | usings, class doc | |
| B | 29–94 | 66 | **Settings I/O** | `CaptureRequestFile`, `bakedFolder`, `online`, `accountFile`, `Settings`, `SettingsPath`, `ReadSettingsFile` (public static, used by editor), `ApplyOverrides` (public static, used by editor), `LoadSettings`, `SaveSettings` |
| C | 96–147 | 52 | **Serialized fields and core state** | `map`, `centerX/Y`, `scale`, `characterName`, `loadRadius`, `zoom`; `_data`, `_shader`, `_world`, `_mobileArt`, `_avatar`, `_walk`, `_camera`, `_hud`, flags; online dictionaries `_others`, `_otherLook`, `_items`; `_typing`/`_chat`; time-of-day table `Times` |
| D | 149–229 | 81 | **Start / boot**: camera setup, data and overrides, shader, art, choice between offline, login screen and autologin, capture trigger | `Start` |
| E | 231–261 | 31 | **Art and world construction** | `SetUpArt`, `CreateWorld` |
| F | 263–288 | 26 | **Offline mode** | `StartOffline`, `StartOfflineAt` |
| G | 290–414 | 125 | **Login and account files** (reads and writes credential JSON) | `_login`, `AccountPath(For)`, `ShowLoginScreen`, `SaveAccount`, `ReadAccountVersion`, `ServerFromArguments`, `Quit`, `ReadAccount` |
| H | 416–504 | 89 | **Session to UI event wiring** (paperdoll, container, gump, lift reject, hotkeys, target cursor, war, damage, journal overheads, context menu, shop, prompt, spellbook, effects, audio) | `SetUpUi`, `Sound` |
| I | 506–570 | 65 | **Effects** (world effects plus IMGUI lightning) | `_effects`, `StartEffect`, `LocateSerial`, `UpdateEffects`, `DrawLightning` |
| J | 572–629 | 58 | **UI commands and options facade** (used by `UI.OptionsWindow`) | `_quitAfterLogout`, `CutZ`, `_chatLog`, `OnUiCommand`, `Hotkeys`, `ShowNames/ShowHud/ShowJournal/AutoRoofs/Zoom` |
| K | 631–678 | 48 | **Target cursor** | `UpdateCursor`, `Crosshair`, cursor cache |
| L | 680–742 | 63 | **Overhead text** | `Overhead` class, `AddOverhead`, `OverheadAnchor`, `UpdateOverheads` |
| M | 744–825 | 82 | **Hover and tooltips** (IMGUI `GUI.Window`) | `UpdateHover`, `DrawTooltip`, `TooltipWindowId` |
| N | 827–935 | 109 | **World mouse/keyboard input and picking** | `HandleUiInput`, `PickAt` |
| O | 937–943 | 7 | `AccountFile` DTO (credential schema) | |
| P | 945–985 | 41 | **Online start** (session create, event subscriptions, connect or fall back to offline) | `StartOnline` |
| Q | 987–1075 | 89 | **Entity sync: mobiles** | `OnEnteredWorld`, `OnMobileUpdated`, `OnMobileMoved`, `OnEntityRemoved` |
| R | 1077–1093 | 17 | Drag and drop into the world | `DropInWorld` |
| S | 1095–1177 | 83 | **Entity sync: items and multis** | `IsNoDraw`, `_multis`, `ShowMulti`, `RemoveMulti`, `RenderMultis`, `OnItemUpdated`, `LookOf` |
| T | 1179–1279 | 101 | **Frame loop and minimap** | `Update`, `_sections`, `UpdateMinimap` |
| U | 1281–1377 | 97 | **Movement input and step pacing** | `HandleInput` (T/R/H, zoom, WASD, right-mouse), `TryStep`, `_nextStepAt`, `DirectionFromScreen` (public static) |
| V | 1379–1426 | 48 | **Camera and lighting** | `UpdateCamera`, `UpdateLighting`, `ApplyLighting` |
| W | 1428–1476 | 49 | **HUD model update** (stats, name labels, health bars) | `UpdateHud` |
| X | 1478–1548 | 71 | **OnGUI and chat line** | `OnGUI`, `DrawChat` |
| Y | 1550–1565 | 16 | Spawn helper | `FindTile` |
| Z | 1567–2020 | **454** | **CaptureTour**: unattended test script (route, windows, vendor buy/sell, injected house and effects, drag, gumps, macros, report) | `CaptureTour`, `BodyReport` |
| AA | 2022–2057 | 36 | **LoginTour** | `LoginCaptureRequestFile`, `LoginTour`, `WaitFrames` |
| AB | 2059–2091 | 33 | **Project root and paths file** | `PathsFileName` (public, used by `PlayerBuilder`), `PathsFile`, `ProjectRoot` |
| AC | 2093–2106 | 14 | Teardown | `OnDestroy` |
| AD | 2108–2125 | 18 | `TimeOfDay` struct | |
| AE | 2128–2192 | 65 | `FrameSections` (top-level public class) | |
| AF | 2194–2219 | 26 | `FrameStats` (top-level public class) | |
| AG | 2221–2300 | 80 | `ItemView` (top-level public class) | |

Rough totals: test tours about 490 lines (21%), entity/view sync about 190, input about 210, UI wiring and
IMGUI about 420, boot/login/settings about 330, render loop, camera and lighting about 200, separable
top-level types about 170.

### External API that must not break
- `WorldDemo.ReadSettingsFile(bool)`: `Editor/ArtBrowserWindow.cs:14`, `Editor/WorldPreviewWindow.cs:21`
- `WorldDemo.ApplyOverrides(UOData, ClientSettings)`: `ArtBrowserWindow.cs:108`, `WorldPreviewWindow.cs:281`
- `WorldDemo.PathsFileName`: `Editor/PlayerBuilder.cs:56`
- `WorldDemo.CaptureRequestFile`, `LoginCaptureRequestFile` (public consts; external tooling touches these files)
- `WorldDemo.DirectionFromScreen` (public static)
- `UI.OptionsWindow(WorldDemo game)` (`Runtime/UI/MiscWindows.cs:43-45`) uses `ShowNames`, `ShowHud`,
  `ShowJournal`, `AutoRoofs`, `Zoom`, `Settings`, `Sound`, `Hotkeys`, `CutZ`
- `AddComponent<WorldDemo>()` in `Editor/WorldDemoLauncher.cs:45`
- Scene `Assets/Scenes/WorldDemo.unity` binds script GUID `a9448e0cb60c29c4789ffdbf6ec49788`

---

## 3. Proposed classes and files

All new files go in `Assets/UOClient.Unity/Runtime/` (same asmdef, same namespace `UOClient.Unity`), in a
new subfolder `World/` and a new subfolder `Diagnostics/`. **No new asmdef** (see section 6.4). All the
new types are plain C# classes, not MonoBehaviours, except `Audio`, which already is one. That avoids new
script GUIDs, scene references and serialization concerns.

### 3.1 Shared context
**`World/GameContext.cs`**: `sealed class GameContext`. Holds the shared handles that every subsystem
currently reads from WorldDemo fields:
```csharp
public UOData Data; public Shader Shader; public int ArtScale; public Transform Root; public Camera Camera;
public MobileArt MobileArt; public WorldStreamer World; public Walkability Walk; public DemoHud Hud;
public GameSession Session;          // null offline
public Avatar Player;                // null before entering the world
public string CharacterName;
public static string ProjectRoot { get; }   // moved from WorldDemo (see 3.2)
```
This replaces the implicit "everything is a field" coupling with one explicit object. It is a stepping stone:
later steps can narrow each subsystem's constructor to only what it uses.

### 3.2 Boot and configuration
| File | Type | Responsibility | Public API |
|------|------|----------------|-----------|
| `ClientPaths.cs` | `static class ClientPaths` | Project root plus paths file (block AB) | `ProjectRoot`, `PathsFileName` const, `Resolve(string relative)` |
| `ClientSettingsStore.cs` | `static class ClientSettingsStore` | Block B | `SettingsPath`, `ReadSettingsFile(bool)`, `Load()` (file plus args), `Save(ClientSettings)`, `ApplyOverrides(UOData, ClientSettings)` |
| `AccountStore.cs` | `sealed class AccountStore` | Blocks G (file part) and O. **Owns all credential-file I/O**; keeps the per-host isolation rule (`AccountPathFor`) | `AccountStore(string accountFile)`, `PathFor(host)`, `Remembered(host) -> (account, password)`, `Save(host, account, password, remember)`, `ReadVersion(host)`, `ReadLogin(ClientSettings) -> LoginSettings` |
| `LaunchOptions.cs` | `readonly struct LaunchOptions` | Command-line parsing scattered through `Start` and `ServerFromArguments` | `Unattended`, `CaptureFromCommandLine`, `AutoLogin`, `OfflineAt (map,x,y)?`, `ServerFromArguments` |

`WorldDemo` keeps `ReadSettingsFile`, `ApplyOverrides`, `PathsFileName`, `CaptureRequestFile` and
`LoginCaptureRequestFile` as **forwarding members** (`=> ClientSettingsStore.ReadSettingsFile(...)`), marked
`[Obsolete]` only after the editor callers have been switched over. That keeps the editor assembly compiling at every step.

### 3.3 Login flow
**`World/LoginFlow.cs`**: `sealed class LoginFlow`. Block G minus file I/O. Builds `UI.LoginScreen`, wires
`QuitPressed`, `RememberedFor`, and `LoginPressed`, and saves the host and port to settings.
API: `LoginFlow(GameContext, AccountStore, ClientSettings, Action<LoginSettings> startOnline)`, `Show()`,
`Screen` (the `LoginScreen`), `Attach(GameSession)`, `Failed(string)`, `Draw()`.

### 3.4 Entity views
**`World/EntityViews.cs`**: `sealed class EntityViews : IDisposable`. Blocks Q and S, plus `LookOf` and `IsNoDraw`.
Owns `_others`, `_otherLook`, `_items`, `_multis`.
API:
```csharp
void Attach(GameSession s);   // subscribes MobileUpdated/MobileMoved/EntityRemoved/ItemUpdated
void Detach();
void SpawnPlayer(Core.Mobile p); // the avatar part of OnEnteredWorld
void SyncAll();                  // replay Mobiles/Items after entering
void Tick(float dt); void Render(bool hideUpperMultiParts);
Vector3Int? Locate(uint serial); // was LocateSerial
uint PickAt(Vector2 screen);     // was PickAt (needs Camera)
Rect? DrawnRect(uint serial);    // for OverheadAnchor
IReadOnlyDictionary<uint, Avatar> Others; int ItemCount; int MultiCount; int MultiParts(uint);
string BodyReport();
```

**`World/ItemView.cs`**: move the top-level `ItemView` class unchanged (block AG).

### 3.5 Movement and input
| File | Type | From | API |
|------|------|------|-----|
| `World/PlayerMovement.cs` | `sealed class PlayerMovement` | U (TryStep, `_nextStepAt`, DirectionFromScreen, WASD/right-mouse intent) | `Update(bool inputBlocked)`, `TryStep(int dir, bool run) -> bool` (used by the capture tour), `static DirectionFromScreen(Vector2)` |
| `World/WorldInput.cs` | `sealed class WorldInput` | N (HandleUiInput, world clicks, targeting), R (DropInWorld), T/R/H/zoom part of U | `Update()`, `DropInWorld(UI.DraggedItem, Vector2)`; raises `ToggleTime`, `ToggleRoofs` or calls into `ViewSettings` |
| `World/ChatLine.cs` | `sealed class ChatLine` | `_typing`, `_chat`, DrawChat (X) | `bool Typing`, `Open()`, `OnGUI()` |

`_typing` is read by `HandleInput`, `HandleUiInput` and `OnUiCommand("chat")`. It moves to `ChatLine.Typing`,
and the others read it through a reference passed in. Never copy it.

### 3.6 Presentation
| File | Type | From | API |
|------|------|------|-----|
| `World/CameraRig.cs` | `sealed class CameraRig` | camera setup in `Start` and `UpdateCamera` (V) | `CameraRig(Camera)`, `static Camera EnsureMainCamera()`, `float Zoom`, `Follow(Vector2 anchorPixels, WorldStreamer, int artScale)` |
| `World/Lighting.cs` | `sealed class Lighting` (+ `TimeOfDay` struct) | `Times`, `_timeOverride`, `_timeCurrent`, `UpdateLighting`, `ApplyLighting` (V, AD) | `CycleOverride(bool online) -> string message`, `Update(GameSession, Vector2 lightPos, int artScale, DemoHud)`, `static ApplyDefault()` |
| `World/RoofController.cs` | `sealed class RoofController` | `_roofsForced`, `_manualRoofs`, roof/cut-Z block in `Update` | `bool AutoRoofs`, `Toggle(bool currentlyVisible) -> string`, `Apply(WorldStreamer, Walkability, Avatar) -> bool hideUpperMultis`, `int? CutZ` |
| `World/EffectsLayer.cs` | `sealed class EffectsLayer : IDisposable` | I | `Start(GraphicEffect)`, `Update(WorldStreamer)`, `DrawLightningGUI(UI.GumpArt, Camera)`, `int Count` |
| `World/OverheadText.cs` | `sealed class OverheadText` | L | `Add(uint serial, string, Color, int size, float life, bool rises)`, `Update(DemoHud, Func<uint, Rect?> anchor, Camera)`, `int Count` |
| `World/HudPresenter.cs` | `sealed class HudPresenter` | W | `Update(GameSession, Avatar player, IReadOnlyDictionary<uint, Avatar> others, Camera, WorldStreamer, int artScale)` |
| `World/MinimapUpdater.cs` | `sealed class MinimapUpdater` | `_radarTask`, `UpdateMinimap` | `Update(UOData, int map, int x, int y, DemoHud)` |
| `UI/TargetCursor.cs` | `sealed class TargetCursor` | K | `Update(GameSession, UI.UiManager)` |
| `UI/HoverTooltip.cs` | `sealed class HoverTooltip` | M | `UpdateHover(UI.UiManager, Func<Vector2,uint> pick)`, `OnGUI(GameSession, UI.UiManager)`, `ForceHover(uint, Vector2)` and `ClearForced()` (for the tour) |

### 3.7 Session to UI wiring
**`World/SessionUiBinder.cs`**: `sealed class SessionUiBinder : IDisposable`. Block H plus `OnUiCommand` (J).
Creates `UiManager`, `ChatLog`, `TopBarWindow`, `Hotkeys`, and `Audio`, and subscribes to every session event that
opens a window or writes a log line. It keeps each subscription as a named delegate so `Dispose()` can
unsubscribe everything. Today nothing unsubscribes; the code relies on `_session.Dispose()`.
API: `Attach(GameSession)`, `UiManager Ui`, `UI.ChatLog ChatLog`, `Hotkeys Hotkeys`, `Audio Sound`,
`RunCommand(string)`, `event Action LogoutConfirmed`.

New packet features attach here, or in sibling binders (section 8).

### 3.8 Diagnostics and automation (biggest single win)
| File | Type | From |
|------|------|------|
| `Diagnostics/FrameSections.cs` | move as is | AE |
| `Diagnostics/FrameStats.cs` | move as is | AF |
| `Diagnostics/CaptureTour.cs` | `sealed class CaptureTour` | Z (454 lines). Split internally into one `IEnumerator` per "set" (`RunRoute`, `WindowsSet`, `ButtonsSet`, `ShopSet`, `EffectsAndHouseSet`, `TooltipSet`, `DragSet`, `GumpSet`, `MacroSet`), each appending to a `StringBuilder` report |
| `Diagnostics/LoginTour.cs` | `sealed class LoginTour` | AA |
| `Diagnostics/CoroutineUtil.cs` | `static WaitFrames(int)` | |

`CaptureTour` needs read access to a lot of state: entity counts, multis, effects count, overheads count, `TryStep`,
`RunCommand`, forced hover, `_frames`, `_sections`. Give it the `GameContext` plus an `ICaptureHost` interface
that WorldDemo implements explicitly. Then the tour never reaches into private fields:
```csharp
internal interface ICaptureHost {
    bool Ready { get; } string LoadError { get; } int? CutZ { get; }
    bool TryStep(int dir, bool run); void RunCommand(string cmd);
    FrameStats Frames { get; } FrameSections Sections { get; }
    EntityViews Entities { get; } EffectsLayer Effects { get; } OverheadText Overheads { get; }
    HoverTooltip Hover { get; } SessionUiBinder Ui { get; }
}
```
WorldDemo still starts the coroutine (`StartCoroutine(new CaptureTour(ctx, this).Run(quit))`), because only a
MonoBehaviour can host it.

### 3.9 What remains in WorldDemo.cs (~300 lines)
Serialized fields (unchanged names), `Settings`, the options facade properties, `Sound`, `Hotkeys`, `CutZ`,
forwarding statics, `Start` (compose), `Update` (ordered calls with `_sections.Begin`), `OnGUI` (ordered draw
calls), `OnDestroy` (dispose in reverse order), `StartOnline`, `StartOffline`, `StartOfflineAt`,
`OnEnteredWorld` (thin), `ICaptureHost` explicit implementation.

---

## 4. Dependency and coupling issues

1. **Shared mutable state.** `_avatar`, `_world`, `_walk`, `_session` are reassigned at runtime
   (`CreateWorld` disposes and rebuilds `_world`; a map change rebuilds `_walk`; `OnEnteredWorld` rebuilds `_avatar`;
   `LoginTour` and the login-failure path null `_session`). Any extracted class that **caches** these at
   construction will hold a stale or disposed instance. Rule: subsystems read them through `GameContext` on
   every call, never in constructors. `GameContext` is the single writer.
2. **The `_walk` map-change rebuild** (`Update` around L1205) silently drops multi parts registered through
   `AddMultiPart`. That is an existing latent bug. Preserve the behavior during the refactor and log it as a
   follow-up: re-register the multis after a map change.
3. **Update order is load-bearing.** Current order: network → login-failure/logout check → map-change →
   chunks → minimap → ready gate → keyboard/move input → UI input → avatar tick → roofs/cutZ (sets the
   `_UOCutZ` global) → `mobileArt.Flush` → render avatar/others/items/multis → effects → camera → lighting →
   HUD → overheads → cursor. Effects and overheads read `LastRect`, which `Render` sets the same frame.
   The camera moves **after** rendering, so screen projections in HUD and overheads use the post-move camera.
   Keep this exact sequence in `WorldDemo.Update`. Do **not** split it across several MonoBehaviour `Update`s,
   because Unity's execution order between components is undefined without `[DefaultExecutionOrder]`.
4. **Event-handler lifetime.** About 35 lambdas subscribe to `GameSession` and `World` events and capture
   `this`. None are removed. That is fine while one session lives for the object's lifetime, but the
   login-failure path (`_session.Dispose(); _session = null` and then a new `StartOnline`) creates a **second**
   `SetUpUi`, a new `UiManager` and a new `Hotkeys`. If any long-lived object (for example `Audio`, added as a
   component once) is subscribed, it doubles. The extracted binders must implement `Dispose` and unsubscribe.
5. **Packet handlers run on the main thread** inside `_session.Update` with a 4 ms budget
   (`GameSession.HandleBudgetMs`). World events fire synchronously from there, so view creation such as
   `new Avatar` or `new ItemView` happens inside the "network" timing section. Moving code doesn't change this.
   Don't introduce `Task.Run` in subscribers, because Unity objects can only be touched on the main thread.
   (`PacketLogger` is the one exception: it is called on network threads.)
6. **IMGUI coupling.** `OnGUI` runs several times per frame (Layout plus Repaint plus input events). Draw order
   matters: lightning → HUD → `_ui.OnGUI()` → tooltip (`GUI.Window` id `0x7EEF`, brought to front) → chat line.
   `DrawChat` consumes `Event.current` for Enter and Escape, and `HandleUiInput` reads `Input.GetKeyDown(Escape)`
   in `Update`. Two input systems handle the same key. Keep both paths in place and do not unify them during
   the refactor. `GUI.Window` IDs must stay unique across `UiManager` windows and the tooltip, so put the ID constant
   somewhere shared (`UI.UiManager.TooltipWindowId`).
7. **`OptionsWindow` takes `WorldDemo`.** That is a concrete dependency from UI on the composition root. Step 12
   introduces `IGameOptions` (the facade properties) and makes `OptionsWindow` depend on that.
8. **`DemoHud` is a sink for everything.** Many subsystems write to `_hud.Log`, `_hud.Overheads`,
   `_hud.Labels` and `_hud.HealthBars`. That's acceptable. Pass `DemoHud` explicitly and leave its API alone.
9. **Statics.** `ProjectRoot` cache, `ItemView.PageMaterials` (static `Dictionary<Texture, Material>`),
   `UI.WorldMapWindow.Ready(...)` cache. See section 6.5.
10. **The capture tour injects fake packets** (`_session.Inject(...)` for 0xF3, 0x1D, 0x70, 0x54). It is the only
    place that exercises the packet path end to end without the server sending those packets. Keep it working as
    the regression harness. It is also the template for testing the new packets.

---

## 5. Safe incremental extraction order

Every step is one commit and compiles on its own. Behavior stays identical (pure move, then rewire). Do the steps in
this order; the low-risk, high-size moves come first.

| Step | Change | Risk | Lines moved out |
|------|--------|------|-----------------|
| 0 | Baseline: record a clean compile and a capture-tour report (`Library/UOClient/demo_capture_done.txt`) plus screenshots against the local server. Fix the stale `uoPath` field in the scene YAML only if desired (harmless today). | none | 0 |
| 1 | Move `FrameSections`, `FrameStats`, `ItemView` to their own files. Same namespace, public, no code changes. | very low | ~170 |
| 2 | Extract `ClientPaths` (ProjectRoot, PathsFile). WorldDemo keeps `PathsFileName` as `public const string PathsFileName = ClientPaths.PathsFileName;` and a private `ProjectRoot => ClientPaths.ProjectRoot`. | low | ~35 |
| 3 | Extract `ClientSettingsStore`. WorldDemo's `ReadSettingsFile` and `ApplyOverrides` become one-line forwarders. Editor callers stay unchanged for now. | low | ~60 |
| 4 | Extract `AccountStore` and `LaunchOptions` (moves credential-file *code* only; credential files are not opened during the refactor). Keep the localhost-vs-shard path rule byte-identical; add a unit check in step 4b if tests exist. | medium (security-relevant) | ~110 |
| 5 | Extract `Lighting` + `TimeOfDay`, `CameraRig`, `MinimapUpdater`. Pure functions of their inputs. | low | ~110 |
| 6 | Extract `TargetCursor`, `OverheadText`, `HoverTooltip`. Keep `TooltipWindowId` value `0x7EEF`. | low | ~190 |
| 7 | Extract `EffectsLayer` (takes a `Func<uint, Vector3Int?> locate`). | low | ~65 |
| 8 | Introduce `GameContext` and switch the WorldDemo fields to `ctx.X`. Mechanical, but touches every method; do it in one commit with no other changes. | medium | 0 (rewire) |
| 9 | Extract `EntityViews` (Q, S, PickAt, LookOf, LocateSerial, BodyReport). The largest semantic move. Check that `OnEntityRemoved` still calls `RemoveMulti` and that `OnItemUpdated` still swaps single to multi. | medium | ~230 |
| 10 | Extract `PlayerMovement`, `WorldInput`, `ChatLine`, `RoofController`. Keep the `_typing` gate ordering. | medium | ~260 |
| 11 | Extract `HudPresenter`, `SessionUiBinder` (with unsubscribe-on-Dispose), `LoginFlow`. | medium | ~260 |
| 12 | Add `IGameOptions` (and `ICaptureHost`); `OptionsWindow(IGameOptions)`. WorldDemo implements both. | low | 0 |
| 13 | Move `CaptureTour` and `LoginTour` to `Diagnostics/`. Split `CaptureTour` into per-set coroutines. Its report text must stay **identical**, so diff the `demo_capture_done.txt` output against the step-0 baseline (except for timings and serials). | medium | ~490 |
| 14 | Switch the editor callers to `ClientSettingsStore`/`ClientPaths` and mark the WorldDemo forwarders `[Obsolete]` (keep them; external scripts may use them). | low | 0 |

Step 13 can be pulled forward to just after step 7 if `ICaptureHost` is introduced early. It is the biggest
readability win, but it touches the most state, so it is safer after `EntityViews` exists.

Rules for each step:
- Don't rename any serialized field, the `WorldDemo` class, or its namespace. Don't move `WorldDemo.cs` itself
  out of `Runtime/` (see 6.2).
- Moves first, rewiring second. Never mix a behavior change into an extraction commit.
- New files are created in the editor or with their `.meta` generated by Unity on import. Commit the
  `.meta` together with the `.cs`.

---

## 6. Unity-specific risks

### 6.1 Scene references and serialized fields
`Assets/Scenes/WorldDemo.unity` stores a `MonoBehaviour` with `m_Script: {guid: a9448e0cb60c29c4789ffdbf6ec49788}`
and values for `bakedFolder`, `centerX`, `zoom` and the other serialized fields. Renaming or removing a public field
silently drops the scene value (it reverts to the code default). If a rename is ever needed, use
`[FormerlySerializedAs("old")]`. Extracted subsystems are plain classes and are **not** serialized. Do not mark
them `[Serializable]` or give `WorldDemo` fields of their type that would be serialized: Unity would serialize
them inline and the scene would start carrying their state.
(The scene already carries a stale `uoPath` value for a removed field. That is harmless, and it shows the failure mode.)

### 6.2 MonoBehaviour GUIDs and .meta files
- The scene finds the script by the GUID in `WorldDemo.cs.meta`. **Never delete or regenerate that `.meta`.** Do not
  "move" the file by deleting it and creating a new one outside Unity. If it must move, use `git mv` for both
  `WorldDemo.cs` and `WorldDemo.cs.meta` together. Also, Unity requires a MonoBehaviour's class name to match its
  file name, so `WorldDemo` must stay in `WorldDemo.cs`.
- Every new `.cs` needs a `.meta`. Let Unity generate it on import (focus the editor, or `AssetDatabase.Refresh`),
  then commit both. If `.meta` files are missing from git, other checkouts get new GUIDs. That only matters for
  MonoBehaviours and ScriptableObjects, but stay consistent.
- `WorldDemoLauncher.CreateScene()` re-creates the scene with `AddComponent<WorldDemo>()`. That is a valid
  recovery path if the scene binding ever breaks, but it resets the serialized values to code defaults.

### 6.3 New MonoBehaviours
The plan adds none. If one is ever added (for example to give a subsystem its own `Update`), it needs a file named
after the class, it would change execution order (coupling item 3), and it would need to exist in the scene or be
added at runtime the way `Audio` is (`gameObject.AddComponent<Audio>()`).

### 6.4 Assembly definitions
- All new files stay under `Assets/UOClient.Unity/Runtime/` so they are in the `UOClient.Unity` asmdef
  (refs: IO, ArtPipeline, Net, Core). `UOClient.Editor` references `UOClient.Unity`, so the editor windows
  keep resolving `WorldDemo.*` and, after step 14, `ClientSettingsStore`.
- **Do not** put Unity-dependent code into `UOClient.Core` or `UOClient.Net`. Both are
  `"noEngineReferences": true`, so `UnityEngine` types won't compile there. Only the packet-parsing and
  world-model parts of the new packet work (section 8) go into Core.
- Creating a sub-asmdef (for example `UOClient.Unity.Diagnostics`) is **not** recommended now. It would need
  `InternalsVisibleTo` or public types, it would make `CaptureTour` unable to see internal types, and it adds a
  dependency cycle risk (WorldDemo starts the tour, and the tour needs WorldDemo's host interface). Revisit only if
  diagnostics should be stripped from player builds. Use `#if UOCLIENT_DIAGNOSTICS` or `defineConstraints` then.
- Keep `"autoReferenced": true` unchanged.

### 6.5 Domain reload and statics
`ProjectSettings/EditorSettings.asset` has `m_EnterPlayModeOptionsEnabled: 1`, `m_EnterPlayModeOptions: 0`,
meaning domain and scene reload currently **still happen** on entering Play mode. If someone ever enables
"Disable Domain Reload", these statics leak across plays:
- `_projectRoot` cache (harmless)
- `ItemView.PageMaterials`: holds `Material`s whose textures were destroyed. The existing `m == null` check
  covers destroyed materials, but the dictionary still grows.
- `UI.WorldMapWindow` ready/cache statics
- static event subscriptions (none today in WorldDemo; keep it that way)

While moving these, add `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`
resets for each static cache. They cost nothing and make the code safe for either setting.

Script compilation causes a domain reload **during** Play mode if a file is saved while playing.
Unity's default ("Recompile and continue playing") would break the live `GameSession` socket and the background tasks
(`_radarTask`, chunk worker). Stop Play mode before saving each refactor step.

### 6.6 Background threads
`WorldStreamer` builds chunks on a worker, `MobileArt` builds art off-thread, and `MinimapUpdater` uses `Task.Run`.
Extraction must keep the `Dispose()` order in `OnDestroy` (session → avatars → items → world → gumpArt →
mobileArt → hud → data). Disposing `_data` before the workers stop would crash the worker or read freed
files. In the new layout the dispose order is explicit in `WorldDemo.OnDestroy`. Keep it in reverse
order of construction, and add `EntityViews` multis, `EffectsLayer`, and the binders to it. Today `_multis` and
`_effects` are **not** disposed in `OnDestroy` (a minor leak that scene unload hides).

---

## 7. How to verify each step

### 7.1 Compile
1. Save the step's files with the editor **not** in Play mode.
2. Focus the Unity editor (or touch `Library/UOClient/play-world-demo`, which `WorldDemoLauncher` polls, to trigger
   refresh and play).
3. Check the Editor log, `%LOCALAPPDATA%\Unity\Editor\Editor.log`:
   - search the lines after the latest `- Starting script compilation` / `Reloading assemblies` for
     `error CS` and `warning CS` in the touched files;
   - success is `Reloading assemblies after finished compiling` with no `error CS` lines, and no
     `The referenced script (Unknown) on this Behaviour is missing!` (that would mean a GUID or scene binding broke).
   - PowerShell: `Select-String -Path "$env:LOCALAPPDATA\Unity\Editor\Editor.log" -Pattern 'error CS|script .* is missing' | Select -Last 20`
4. Open `Assets/Scenes/WorldDemo.unity` in the inspector. The "UO World Demo" object must still show the
   `World Demo` component with its existing field values.

### 7.2 Play test: LOCAL server only
- Use only the local ModernUO test server (`127.0.0.1` / `localhost`). `AccountStore` already sends the test account
  only to localhost hosts, and the refactor must keep that rule. Launch with `-server 127.0.0.1` or check that
  `UserSettings/unity-uo-settings.json` points at localhost before pressing Play. **Never** run the
  capture tour against a live shard: it buys and sells from vendors, moves items, says text, and injects packets.
- Steps 1–8 (no semantic changes): run the **offline** demo first (`-offline 0 1467 1671`, or untick
  `online`). Then do an online smoke test: log in, walk across a chunk edge, open the paperdoll and backpack,
  shift-click a vendor, talk, T/R/H/F3, zoom, and log out.
- Steps 9–13: run the full **capture tour** (touch `Library/UOClient/capture-demo`, then play). Compare
  `Library/UOClient/demo_capture_done.txt` and the `ui_*.png` / `stream_*.png` screenshots with the step-0
  baseline. The report's structure and counts (windows, multis parts, effects running, house removed, drag
  result, macros) must match; timings and serials may differ.
- Login-flow steps (4, 11): run the login tour (`Library/UOClient/capture-login`) and compare
  `login_capture_done.txt`. Also test a wrong password and a refused connection (stop the local server) to check
  the return-to-login path, and confirm no duplicate event handlers by checking that journal lines are not doubled.
- F3 frame stats: compare avg and p99 with the baseline, because extra indirection should cost nothing measurable.

---

## 8. Interaction with the new packet handlers

Layering for every new packet:
1. **`UOClient.Core`** (no Unity): add the length in `UOClient.Net/PacketLengths.cs` if it is fixed or variable
   (unknown lengths desync the stream), add `_handlers[0xNN] = Handler` in a `Register*Handlers()` of the right
   `GameSession.*.cs` partial (or a new partial), parse with `PacketReader`, update the `World` model, and raise a C#
   `event`.
2. **`UOClient.Unity`**: subscribe in the matching binder (`SessionUiBinder` or a new small binder) and open a
   `UI.*Window` or update a view. **Never** add packet subscriptions straight to `WorldDemo` again. That is how it
   reached 115 KB.
3. **Test**: add a step to `CaptureTour` that `_session.Inject(...)`s a synthetic packet (the existing 0xF3,
   0x1D, 0x70 and 0x54 injections are the pattern), and/or trigger the real thing on the local server.

So do the refactor **before** these handlers, or at least through step 11 (`SessionUiBinder`, `EntityViews`).
Otherwise each new feature adds 30–100 lines to WorldDemo and makes every later extraction step conflict.
If a packet has to land first, put its Unity side in a new file from day one (for example
`World/QuestArrowBinder.cs`) and call it from WorldDemo with a single line.

| Packet | Meaning | Core side | Unity side (owner after refactor) | Notes |
|--------|---------|-----------|-----------------------------------|-------|
| **0xD8** | Custom house (compressed design per floor) | new `GameSession.Housing.cs`; decompress planes, `World.SetHouseDesign(serial, revision, components)`, event `HouseDesignChanged`; also handle 0xBF 0x1D (design revision) to request it | `EntityViews.ShowMulti` must prefer the custom design over `_data.Multis.Get(graphic)` for custom houses; the multi parts also go into `Walkability.AddMultiPart` | Largest one. Depends on step 9. Test by injecting a captured 0xD8 next to the fake 0xF3 house in the tour. |
| **0x76** | Server change (map/bounds switch) | handler updates `World` map bounds and position; event `ServerChanged` | `WorldDemo` composition: same path as the map-change block in `Update` (`World.SetMap` and rebuild `Walkability`), **plus** re-register multis (coupling issue 2) and clear `EntityViews` | Put the map-change logic into one method (`GameContext.ChangeMap`) during step 8 so 0x76 and the poll path share it. |
| **0x99** | Multi placement target (house/boat placement cursor) | extend `TargetRequest` with `MultiGraphic`, offsets; event via existing `TargetRequested` | `TargetCursor` plus `WorldInput` ground-target branch: draw a ghost multi (reuse `EntityViews` multi rendering with a translucent flag) under the cursor; reply with 0x6C | Depends on steps 6, 9 and 10. |
| **0xAB** | Gump text entry dialog | parse to `TextEntryRequest`; event `TextEntryRequested`; send 0xAC reply | `SessionUiBinder` → new `UI.TextEntryWindow` (like `PromptWindow`) | Must respect `UiManager.HasKeyboardFocus` so `ChatLine` and movement keys stay blocked. |
| **0xBA** | Quest arrow (on/off, x, y; newer clients add a serial) | `World.QuestArrow` state; event `QuestArrowChanged` | new `World/QuestArrow.cs` presenter (IMGUI arrow at the screen edge plus a minimap marker through `DemoHud`) | Length differs by client version (6 vs 10), so check `ClientVersion`. |
| **0xB8** | Character profile (request/response) | parse title, static and editable text; event `ProfileReceived`; send 0xB8 update | `SessionUiBinder` → `UI.ProfileWindow`; opened from the paperdoll's scroll button (`OnUiCommand("profile")`) | Editable field: keyboard focus again. |
| **0x56 / 0x90** | Map plot pins (0x56 add/insert/move/remove/clear/toggle edit) / map details (0x90; 0xF5 on newer clients) | `World.Maps[serial]` with bounds, size and pins; events `MapOpened`, `MapPinsChanged`; send 0x56 for edits | `SessionUiBinder` → `UI.MapItemWindow` (treasure/cartography maps), reuse `WorldMapWindow` rendering for the map image | Make the 0x90-vs-0xF5 choice once from `ClientVersion` in Core. |
| **0xBC** | Season change (season, play-sound flag) | `World.Season`; event `SeasonChanged` | `WorldStreamer` / `IsoCompositor` would need seasonal art substitution (land/static remap). Short term: log and store only. Long term: a `SeasonController` that invalidates chunks | Chunk rebuild touches the background worker. Do it after the refactor so a rebuild is a single `WorldStreamer` call. |

Also, before adding any of these: `GameSession.RecentPackets` and `SlowPackets` already provide packet tracing. The
capture report prints "slow packets", which shows whether a new handler and its subscribers go over the
4 ms budget.

---

## 9. Out of scope / follow-ups noticed
- `_walk` rebuild on map change loses registered multi parts (section 4, item 2).
- `_multis` and `_effects` are not disposed in `OnDestroy`.
- Two `JournalAdded` subscriptions (log and overhead) plus `ChatLog.Add`: consolidate in `SessionUiBinder`.
- Mojibake in the status strings (`Connecting to …`, `Loading Britannia…`, shown as `â€¦`): the source was saved with
  a mis-decoded ellipsis. Fix as its own commit, not inside a move.
- The duplicate `<summary>` doc comment on `ItemView.Z`.
- The scene's stale serialized `uoPath`.
