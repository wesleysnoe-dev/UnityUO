# Night log (2026-11-05)

Goal: make Unity UO playable as intended against the local ModernUO shard ("UO Offline"). I worked through the night on my own, in priority order, testing each piece live with the test character (`UserSettings/uoclient-account.json`). Nothing outside this project was changed: no server config, no accounts other than the test one, no pushes.

## Plan (priority order)
1. Targeting cursor, war mode, attacking, live hit/mana/stamina updates, speech over heads, single-click names, damage numbers
2. Creatures and mounts that are currently invisible (anim2–anim5, Bodyconv.def, UOP animation frames)
3. Context menus, vendor buy/sell, text prompts
4. Status and skills windows, paperdoll buttons, health bars
5. Spellbook, spell casting, hotkeys
6. Houses and boats (multis)
7. Sound effects and music
8. Login and character select screens (instead of the account file only)
9. Tooltips, graphical effects, journal tabs, logout, polish

## Progress
(newest last; everything below was checked live on the shard unless it says otherwise)

1. **Combat basics.**
   - Target cursor: a crosshair colored by intent. A click targets an object or the ground, and Esc cancels.
   - War mode: Tab or the paperdoll button.
   - Attack: double-click in war mode.
   - Live hits, mana and stamina.
   - Speech above heads: stacked, fading, sized by its real wrapped height.
   - Damage numbers.
   - Single-click names.
   - Checked live: Anatomy aimed at myself drew the server's "You know yourself quite well enough already".
2. **Paperdoll buttons.** All eight work: Help, Options, Log Out (confirm, save, quit), Journal, Skills, Chat, War/Peace and Status.
   - Skills window: 58 skills, use gems, and lock arrows that cycle up, down and locked.
   - Status window: the full stats block.
   - Options window: names, journal, auto-roofs, HUD, zoom, sound and music volume, and the macro list.
   - New windows step aside instead of stacking on top of each other.
3. **Health bars.** Shown under your opponent, anyone hurt, and yourself when hurt or in war mode.
4. **Creatures and mounts.** Animals and monsters used to be invisible.
   - New `AnimationLibrary` reads anim.mul through anim6.mul and follows `Bodyconv.def` (each expansion file has its own layout) and `Body.def` stand-ins.
   - Mounts use ModernUO's own mount table, taken from its source on this PC.
   - Riders use the mounted actions, and the mount plays its own walk, run or stand underneath.
   - Riding halves step time, as the server expects.
   - Seen live: birds, cats, dogs, a mounted player and body 0xB2.
5. **Context menus.** Shift+click in the world or in a container.
   - Read live: entries such as "Open Paperdoll, Talk", "Buy, Sell, Train …" and "Ask Destination, Accept Escort", with disabled ones greyed out.
6. **Vendors.** Buy and sell windows: icons, prices, stock, +/- and shift-click for all, totals against your gold, Accept and Clear.
   - Buy prices are paired with stock by the x tag ModernUO puts on each item.
   - Covered by unit tests. Not exercised live: no vendor stood within reach of the spawn point, and the one found was 12 tiles off, with Buy greyed out.
7. **Text prompts.** ASCII and Unicode (0x9A/0xC2) through a text box.
8. **Spellbooks.** The window lists the spells in each school (Magery circles with icons) and you click to cast. Not tested live: the test character has no book.
9. **Macros.** `UserSettings/unity-uo-hotkeys.json` is written with defaults on first run:
   - F2 bandage self, F4 last target, F5 target self, F6 Heal, F7 Magic Arrow, F8 Hiding, F9 Meditation, Ctrl+1 say Hail.
   - Actions: cast, skill, say, war/peace, lasttarget, targetself, bandageself, use <graphic>, backpack, paperdoll and the windows.
   - A queued self or last target answers the next cursor automatically.
10. **Tooltips.** Hover over an item in the world, a container or the paperdoll for 0.4 s to see its property list. Checked live: "1000 gold coin".
11. **Effects.** Moving missiles (with an explosion on arrival), effects in place or on a mobile, and lightning, animated from animdata.mul.
    - Fireball, sparkles and lightning were checked by injecting packets locally.
12. **Sound and music.** Positional sound effects from soundLegacyMUL.uop (40-byte header, 22 kHz PCM), and region music from `Music/Digital` with crossfades.
    - Checked live: the server's music cue played "britainpos".
13. **Login screens.**
    - A normal launch shows a login form over the ComfyUI mockup, pre-filled from the account file, with an optional "remember password".
    - Then the shard list (only when there is more than one), then character select (play, delete with a two-click confirm, new), then character creation with a paperdoll preview.
    - Tours and `-uoclient-autologin` still log in automatically.
    - Checked live up to the character list; nothing was created or deleted.
14. **Houses and boats.**
    - Fixed parts come from multi.mul (16-byte entries; doors, signs and planks arrive as separate items).
    - The parts join walkability, and upper floors and roof hide while you're inside.
    - Checked with a small house injected locally: 145 parts, removed cleanly.
    - Items now share one material per atlas page, so a house no longer costs about 150 material switches.
15. **Standalone player rebuilt.** The full tour passes in the player at 163 fps average, p99 7.9 ms.

## Known gaps and next steps
- **Vendors, prompts and spellbooks** have not run against the real server yet. Stand next to a vendor and Shift+click, Buy.
- **Customized houses** (0xD8 design packets) are not drawn yet, only classic multi layouts. Boats are redrawn whole each time they move.
- **UOP animation frames** (AnimationFrame*.uop) aren't read yet. Very new creatures and mounts (post-Stygian Abyss) may still be missing; Bodyconv covers the classic ones.
- **Unknown 160×50 gump.** The shard's small "Groups" gump at the top is shown as sent; its first draw costs about 30 ms (once per session).
- **Music hitch.** The first music cue costs about 130 ms once at login (audio and web-request warm-up).
- **Not done yet:** party, trade window (0x6F), bulletin boards, books, maps, the death screen tint, macros recorded in-game, and paperdoll profile scrolls.

## Session: four more incoming packets (2026-11-05, ~03:30–04:15)

One commit per packet. Each was built with `dotnet build` (Tests, Cli, EditorCheck) and passed `dotnet test` (38 tests, 9 new). Nothing logged in: the local server was only checked to be reachable, and every packet path was exercised by injecting packets laid out as ModernUO writes them.
1. **`0xD8` custom houses** (`GameSession.Housing.cs`, `CustomHouses.cs`).
   - Decodes the zlib planes and stair lists. Designs are cached per house and refreshed when `0xBF 0x1D` announces a new revision, by asking with `0xBF 0x1E`.
   - Begin and end of customizing (`0xBF 0x20`) are noted in the journal.
   - A house with a design is drawn from it instead of its multi.mul layout.
   - Unity compiled it cleanly.
2. **`0x76` server change** (`GameSession.cs`): moves the player, resizes the map, drops steps in flight, and reports it in the status line. Unity compiled it cleanly.
3. **`0x99` placement cursor** (`GameSession.Combat.cs`, `MultiPlacementGhost.cs`): previews the house or boat under the mouse at the tile minus the server's offset. A click answers with a `0x6C` ground target.
4. **`0xAB` text entry dialog** (`GameSession.Trade.cs`, `UI.TextEntryWindow`): caption, text or number box and help text, answered with `0xAC`. ModernUO never sends it, so the layout follows RunUO/POL and ClassicUO.

The Unity editor stopped refreshing on its own after the second packet. The Unity code for 3 and 4 was type-checked with `UOClient.EditorCheck` (the Unity 6000.6.8f1 assemblies). The `.meta` for `MultiPlacementGhost.cs` was written by hand. What still needs a live server is in LIVE_PARITY.md.

5. **`0xBA` quest arrow** (`GameSession.Tracking.cs`, `HudPresenter`/`DemoHud`): 6 or 10 bytes (tracked serial on 7.0.9+) as ModernUO's `SendArrow` writes it; a pulsing marker on the tile, or an arrow at the screen edge pointing toward it, cleared on active=0. Injected-packet tests only (46 tests pass), no login.
6. **`0xB8` profile** (`GameSession.Profile.cs`, `UI.ProfileWindow`, paperdoll scroll): serial, Latin-1 header, then UTF-16 footer and body as ModernUO's `SendDisplayProfile` writes them; the paperdoll scroll sends the mode-0 request (ModernUO has no profile context entry). Injected-packet test only (47 tests pass), no login.
