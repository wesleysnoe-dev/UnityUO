# Local test server vs live (uosmallz.duckdns.org)

Unity UO is developed against the local test server on this PC (127.0.0.1:2593) only. Before any test against live, everything below must be resolved or accepted, and this file updated. Nothing here has ever connected to the live server.

Checked 2026-11-05 on this PC.

## Client data: local server vs the live client release

| | Local server reads | Live client (UO Smallz v21) reads |
|---|---|---|
| Folder | `uo-modernuo\UOData\7.0.23.1\7.0.23.1` | `AppData\Local\UOSmallz\UOData\7.0.23.1\7.0.23.1` |
| art, gumps, tiledata, hues, multis, anims, sounds, cliloc and the other 331 files | identical | identical |
| `map0.mul`, `staidx0.mul`, `statics0.mul` (Felucca) | **different** | **different** |
| `map2.mul`, `staidx2.mul`, `statics2.mul` (Ilshenar) | **different** | **different** |

- **Local server's maps:** carry the 10/17 merge (`map-merge-log.txt`): Solen hives, Minax and Council of Mages strongholds, Khaldun decoration.
- **Live client's maps:** carry the 10/22 Magincia paste (`magincia-paste.txt`, 1480 blocks) and the 11/2 Ilshenar rockslide removal (`rockslides-applied.txt`, 310 statics).
- **Effect:** the local server and the client disagree about terrain in those areas, so movement there can be rejected locally. Britain is not affected; all 401 baked placements check out (`uoclient bakecheck`).
- **To decide:** which maps the live server runs. Then make the local server's copies match them.

## The client's own additions (not in the data files)

These come from the client folder (`AppData\Local\UOSmallz\Client`), and Unity UO now reads them:
- `ExternalImages`: 166 art PNGs, including the shard items 0x4C00–0x4C39 and 0xFE00–0xFE17, plus the CorvaeOboro scrolls and reagents.
- `TileData/*.csv`: 72 shard tiledata rows.
- `EquipConv/*.csv`: 116 equipment conversions.
- `HDArt`: the 2x art pack.

Two things to confirm:
- **Custom item range.** The shard block reserved by client patch 0008 is 0x4C00–0x4CFF, and 0x4C00–0x4C39 are in use today. The range given as 0x4C38–0x4CFF only covers the Gilded Fancy Hooded Robe (0x4C38/0x4C39) onward.
- **Draft items.** The imported clothing (`outlands-clothing.csv`, 0x4C0A–0x4C37) is marked "DRAFT, not released" in its own file, yet it ships in the v21 client folder.

Not read yet: the HD animation packs (`HDArt/anim/*.uoanim`, about 5 GB), a format of the UO Smallz patches.

## Server build and configuration

| | Local | Live |
|---|---|---|
| ModernUO | upstream `e7f85d4` (2026-08-23), plus 17 modified files and custom folders, not committed (CustomBots, Live, Pricing, Zones, …) | **unknown** |
| Install record | `uo-offline-version.json`: repo `wesleysnoe-dev/uo-offline-dev`, sha `unknown` | **unknown** |
| `expansion.json` | Id 5; T2A, UOR and AOS on; maps Felucca, Ilshenar and Malas; housing AOS; MobileStatusVersion 6 | **unknown** |
| `modernuo.json` | listener 127.0.0.1:2593, AOS movement delays (400/200/200/100 ms), client verification on (kick), bots, auctions and other UO Offline settings | **unknown** |
| Client version | 7.0.23.1 (TazUO, and Unity UO) | live client v21: 7.0.23.1 |

**Needed from the live box** to fill in the blanks:
- `git -C <ModernUO> rev-parse HEAD`
- `git -C <ModernUO> status --short`, and `git diff`, or the patch set it was built from
- `Distribution/Configuration/expansion.json` and `modernuo.json`, with secrets removed
- the hashes of its UO data folder: `sha256sum *.mul *.uop *.def`

## Packets added without live traffic (2026-11-05)

`0xD8`, `0x76`, `0x99` and `0xAB` were written against ModernUO's source on this PC and checked only with injected packets in `DotNet/UOClient.Tests`. To confirm on a server:
- **`0xD8` custom houses:** the floor grids are laid out over the foundation's multi.mul bounds plus one row of steps, as ModernUO's `GetEmptyFoundation` builds them. A live server running another ModernUO revision should be checked with a customized house in view (walls and floors in the right tiles, stairs at the right height).
- **`0x99` placement:** the offsets are ModernUO's deed offsets (for example 0, 4, 0 for small houses). Check that the preview lines up with where the house actually lands.
- **`0x76`:** ModernUO sends it on facet changes; check it with a moongate or a recall to another facet.
- **`0xAB` / `0xAC`:** ModernUO neither sends `0xAB` nor handles `0xAC`. If the live shard has a custom script that uses it, confirm its layout.
- **`0xBA` quest arrow (added later the same night, same caveat):** use Tracking on a nearby creature. The arrow should point toward it, follow it as it moves (ModernUO resends it), and disappear when tracking ends. The target has no z, so the on-screen marker sits at the player's height.
- **`0xB8` profile (same caveat):** click the scroll on your own and on another player's paperdoll. ModernUO only answers for players within 12 tiles that you can see (nothing for NPCs). Your own should show the account age as the footer. Editing the profile (request mode 1) is not sent yet; ModernUO has no profile context-menu entry, so the paperdoll scroll is the only way in.
- **`0x90`/`0xF5`/`0x56` map items (same caveat):** data only, nothing is drawn yet. Our default client (7.0.23.1) gets `0xF5` (21 bytes, with the facet), not `0x90`. Double-click a treasure or city map and check `GameSession.Maps` for the bounds and pins. ModernUO's `SendMapSetEditable` always sends editable = 1 (it ignores its argument), so the editable flag is not reliable.
- **`0xBC` season (same caveat):** ModernUO sends it at login and on every facet change (each facet's season comes from the shard's map definitions). Change facets and check that the HUD's time line names the season (nothing is shown for summer) and the light takes a slight tint. The art does not change (no bare winter trees), and the play-sound flag is ignored.
- **`0xDF` buffs (same caveat):** cast Bless, Night Sight or Protection on yourself. Each should show as a chip under the status bars with its cliloc name and a m:ss countdown, and disappear when ModernUO sends the remove. ModernUO only sends these when the shard has `BuffInfo.Enabled` on and the client supports buff icons. Text only: the buff-icon gumps are not drawn, and the description and arguments (`Buff.Description`, `Buff.Args`) are kept but not shown yet.

## Unity UO's own settings

- The server is a setting. It's 127.0.0.1:2593 unless `UserSettings/unity-uo-settings.json` or `-server` says otherwise.
- The CLI's online test prints "NOT the local test server" when pointed anywhere else.
