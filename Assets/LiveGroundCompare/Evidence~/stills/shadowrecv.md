# LiveGroundCompare sprite shadow receive evidence (isolated fixture, TEST only)

- Captured: 2026-11-06 03:32:50 -08:00; Unity 6000.6.8f1; Direct3D12 / NVIDIA GeForce RTX 5080; color space Gamma; batch mode True
- Fixture: `G:\UOClient\Assets\LiveGroundCompare\Evidence~\shadowRecvChunk.json`: map 0 chunk (1424, 1712): 235 static columns, 348 static quads (322 upright, 26 ground), atlas sha256 bf7c057adb03601a, ground sha256 e31cbc830591e267; map0 land/statics + tiledata + anim body 400 via UOInstallFixture; statics = IsoCompositor.BuildDrawList (production placements, scale 1)
- Camera: orthographic along +Z over Unity XY, 1 world pixel per output pixel (1024x1024), centre (0, -292). Terrain = TerrainLit on the chunk's corner grid in the sprites' plane (same normals, art, mask); drawn first, sprites over it with depth cleared (no hill occlusion here).

## Equivalence (keyword off)
- 4aa51a6 WorldSprite (compiled in memory from WorldSprite.4aa51a6.shader.txt) vs shipped WorldSprite, keyword off, same mesh (feet in UV3): 0/1048576 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel
- Shipped, keyword off: mesh with UV3 feet vs the same quads without UV3: 0/1048576 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel
- Shipped: keyword off vs keyword on with an empty ceiling (every texel NoCeiling): 0/1048576 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel

## Receive
- Statics, keyword off vs on: 53209/1048576 px changed (48817 by > 8/255), mean |delta| 0.778/255 per channel; 53209 px darker with on
- partial foot probe world px (-6402, 69440): off (24,24,24) (body art (24,24,24), visible True), on (17,17,17): GPU ratio 0.708, CPU mirror (SpriteShadow) 0.700 -> MATCH
- partial head probe world px (-6403, 69386): off (181,181,181) (body art (181,181,181), visible True), on (181,181,181): GPU ratio 1.000, CPU mirror (SpriteShadow) 1.000 -> MATCH
- shadow foot probe world px (-6424, 69418): off (24,24,24) (body art (24,24,24), visible True), on (17,17,17): GPU ratio 0.708, CPU mirror (SpriteShadow) 0.700 -> MATCH
- shadow head probe world px (-6425, 69364): off (181,181,181) (body art (181,181,181), visible True), on (127,127,127): GPU ratio 0.702, CPU mirror (SpriteShadow) 0.700 -> MATCH
- lit foot probe world px (-6402, 69484): off (24,24,24) (body art (24,24,24), visible True), on (24,24,24): GPU ratio 1.000, CPU mirror (SpriteShadow) 1.000 -> MATCH
- lit head probe world px (-6403, 69430): off (181,181,181) (body art (181,181,181), visible True), on (181,181,181): GPU ratio 1.000, CPU mirror (SpriteShadow) 1.000 -> MATCH
- Probes: all match (tolerance 0.02; 8-bit output, RHalf ceiling)
- Night + player light, keyword off vs on: 53767/1048576 px changed (17994 by > 8/255), mean |delta| 0.318/255 per channel

## Stills
- `shadowrecv_off.png`: keyword off (production path): terrain CPU mask only, sprites unshadowed (sha256 87a7869e5c24b0d6)
- `shadowrecv_on.png`: keyword on: statics receive the CPU shadow ceiling (terrain + map static columns) (sha256 4725c01d644f2f20)
- `shadowrecv_diff_x4.png`: |on - off| x4 (sha256 9d6242dad4d69766)
- `shadowrecv_ceiling.png`: the chunk's shadow ceiling above its ground (black: none, white: >= 40 Z above), 4 px per tile, tile x right, tile y down (sha256 e3b94c9b03e615dd)
- `shadowrecv_mobile_partial.png`: body 400 action 4 dir 1 at tile (1434, 1725, 20), keyword on (sha256 daca58d8542be47e)
- `shadowrecv_mobile_partial_zoom.png`: x4 crop around the body: left keyword off, right keyword on (sha256 b27b0f86b08c8bca)
- `shadowrecv_mobile_shadow.png`: body 400 action 4 dir 1 at tile (1433, 1725, 20), keyword on (sha256 53b731fd24a1f166)
- `shadowrecv_mobile_shadow_zoom.png`: x4 crop around the body: left keyword off, right keyword on (sha256 ed93dc0044110f49)
- `shadowrecv_mobile_lit.png`: body 400 action 4 dir 1 at tile (1435, 1726, 20), keyword on (sha256 880527883d512352)
- `shadowrecv_mobile_lit_zoom.png`: x4 crop around the body: left keyword off, right keyword on (sha256 5375888bcd172a35)
- `shadowrecv_night_on.png`: night ambient (0.32, 0.34, 0.48) + player light r=140 px at the partial body, keyword on (sha256 9d046073d08b6de9)
- `shadowrecv_night_off.png`: same night, keyword off (sha256 d0f96d3a881d8334)

Limits: one chunk (rays leaving it count as lit, as the terrain mask); billboard reconstruction (a sprite is a vertical plane through its foot tile centre; overhangs and roofs are not modelled separately); mobiles/items do not cast; terrain sorting against sprites is not exercised here (see Phase C sort stills). Not a Unity shadow map.
