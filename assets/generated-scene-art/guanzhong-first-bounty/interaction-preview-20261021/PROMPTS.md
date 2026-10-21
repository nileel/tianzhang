# 生成提示词

工具：内置 image_gen；编辑参考为同目录 reference-approved-layout.png。输出为非透明背景。每张独立生成，术法目标图另做一次定向边框修订。

## 共用约束

```text
Use case: precise-object-edit / UI interaction-state mockup.
Edit the provided approved 《天章》 screenshot to create ONE full-screen 16:9 interaction-state preview. Preserve the exact approved mountain environment, distant cliffs/clouds, warm sunlight, dark foreground pine silhouettes, 100-hex tactical battlefield footprint, perspective, tile size, four obstacles, character identities and elegant compact jade interface. It is a tactical game screenshot, not a diagram sheet. No collage, no headings outside the game, no watermark. Do not redesign the UI or change its panel positions.
COMMON REQUIRED HUD CORRECTION:
At the bottom, keep nine icon actions with labels 普攻 / 贯石击 / 聚灵术 / 飞星指 / 移动 / 防御 / 换术 / 换装 / 丹药, followed by distinct 结束行动.
Clearly show the real costs for the three arts on EVERY image: under 贯石击 show two small crisp lines "50 AP" and "12 MP"; under 聚灵术 show "25 AP" and "0 MP"; under 飞星指 show "50 AP" and "10 MP". Correct the old image's wrong 40 AP for 贯石击. The icon can be slightly smaller to fit the two cost lines, keeping the existing shallow bar. MP numbers must be as legible as AP. 普攻 is 40 AP, 防御 50 AP. Avoid adding guessed costs to the other actions.
Upper-left resources remain 生命571/571, 灵力180/180, AP100/100 because these states are previews before commitment. Preserve clean readable Chinese typography, ivory text, deep blue-teal translucent cards, thin antique-gold borders.
IMPORTANT GRID OVERLAYS:
Any hex outline MUST lie precisely on a visible existing hex tile TOP surface in the same perspective, tracing that actual tile's six edges. No floating rings at rock height, no circular indicators replacing hex outlines, no giant regular hex polygon drawn across multiple cells, no new tiles, no thick neon slabs. Border emphasis and very subtle tint must preserve terrain texture. Keep unit silhouettes, health bars and tile edges clear. Do not obscure the play area with decorative effects.
```

## 01 移动范围

```text
STATE: MOVEMENT RANGE AND HOVERED DESTINATION.
Keep player at the existing lower-left starting tile, both beasts at upper-right. Highlight the actual reachable open tiles near the player with restrained blue-cyan individual hex BORDER outlines, forming a contiguous irregular region near the lower-left corner. Approximately twelve reachable cells; exclude the four rock-occupied cells, enemy cells and tiles beyond the movement range. Do not highlight the whole board. The player's current cell has a quiet distinct white rim.
Show a thin pale-gold path connecting THREE successive adjacent walkable tile centers from the player's origin along the left-side open corridor toward the back of the board. End at a destination tile exactly three hex steps away, with a brighter GOLD hex border and a small pointer cursor near that destination. Make the destination visually distinct from the blue reachable cells. Path avoids all rocks.
Highlight the "移动" action with a jade selected state.
Above the bottom action bar, place a restrained shallow movement preview card on the lower-right open area; do not cover the player or destination. Text exactly:
"移动预览"
"路径 3 格 · 消耗 75 AP · 剩余 25 AP"
"点击地块移动 · 右键 / Esc 取消"
A small "取消" control only. No confirmation button: clicking a legal tile commits movement.
Keep upper-left current AP 100/100 until the action is committed. Small discreet border legend "可移动" in cyan and "目的地" in gold can sit within the preview card if space allows. No enemy detail panel and no spell targeting overlay in this movement-only image.
```

## 02 术法目标选取

```text
STATE: FEIXINGZHI SPELL RANGE AND LEGAL ENEMY TARGET HOVER, BEFORE CAST.
This depicts a later moment in the same battle, so move ONLY the player from the lower-left starting cell to an open middle-right board cell FOUR hex steps from the tan stone beast at the upper-right. Keep both beasts in their existing upper-right cells. Player has not cast yet; preserve full resource numbers as a scenario illustration.
Select the "飞星指" action with jade emphasis.
Show the spell's 1–5 hex cast distance from the player's new position with subtle pale cyan/blue individual tile-edge lines on the in-range hexes, clipped at the existing board perimeter. Empty ground inside cast distance is only RANGE, not itself a legal enemy target. Keep out-of-range cells unoutlined. Mark the player's cell with a quiet white hex outline.
The two living enemy-occupied hexes inside range have warm amber individual hex outlines indicating they can be targeted. The TAN 石甲兽 is the hovered target: its own single occupied hex has a brighter restrained GOLD double edge with tiny corner ticks and a small ivory cursor next to it. The blue-gray enemy keeps a thinner amber outline. Preserve both beasts' original red team bases and overhead HP bars.
No giant area explosion, no highlighted group damage, no radius around the target: 飞星指 affects only the ONE hovered enemy cell. No projectile or damage number yet. No movement path.
In the lower-right quiet space directly above the action bar, show the same elegant shallow preview card:
"飞星指 · 选择目标"
"目标：石甲兽 · 距离 4 格 · 可施放"
"施法范围 1–5 格 · 单体 · 50 AP / 10 MP"
"点击目标施放 · 右键 / Esc 取消"
Small "取消" control only, no confirm button. A tiny border key can say "施法范围" in cyan and "可选目标" in amber.
Bottom three art costs MUST show 贯石击50AP/12MP, 聚灵术25AP/0MP, 飞星指50AP/10MP. All text crisp and actually legible.
Keep the popup on the right beneath the selected beasts, not over them; leave main combat silhouettes and individual target hexes unobstructed. No detailed character card in this image.
```

### 目标边框定向修订

输入为 02 的首次生成稿；最终交付仅保留修订版。

```text
Make ONE very localized correction to this approved spell-targeting screenshot. Preserve the entire scene, characters, UI, every word and number, the bottom spell costs, blue range grid, player position and all other pixels as closely as possible.
ONLY FIX THE GOLD OUTLINE ON THE TAN BEAST'S TARGET CELL IN THE UPPER-RIGHT:
Currently a tall upright gold hexagon surrounds the monster's body. REMOVE that upright hexagon and its glow from above/along the beast's shoulders. Replace it with a single thin pale-gold SIX-SIDED GROUND-TILE BORDER, lying horizontally on the grass directly under the beast's FEET, following the same shallow foreshortened perspective as the adjacent cyan tile borders.
At this 1672 by 941 composition, the outline is approximately centered around x1340,y299, width80,height32, not a tall vertical frame. Its back edge is correctly occluded by the beast's feet/body; the front edge is visible below the feet. Absolutely no gold outline should climb around the beast's shell/head. No tall prism and no vertical selection box.
Keep the existing cursor beside the ground target, the health bars and red team indicator. The blue-gray beast remains as it is. Do not change any text or terrain, do not move anything, do not add panels or effects. Output the same single full screenshot.
```

## 03 角色详情

```text
STATE: SELECTED CHARACTER DETAILS POPUP, INSPECTION ONLY.
Keep the player at the original lower-left starting tile and both beasts at their original upper-right positions. Inspect/select the TAN stone beast 石甲兽, without entering attack or movement mode.
Show ONLY its occupied ground tile with a thin pale-gold hex outline laid FLAT on the grass at its FEET, in the same extremely foreshortened perspective as that tile. The outline must lie below the beast's body around the footprint, not form an upright hexagon framing the animal. Preserve its red team base. A small quiet gold highlight on the matching bottom portrait in the left initiative rail links the inspected unit to the card. No movement range or casting-range overlays.
On the RIGHT edge BELOW the bounty tracker and BELOW the two beasts, put a compact elegant deep teal character details popup occupying roughly 17% width and 43% height, beginning at approximately x=81% and y=34%, ending above the bottom hotbar. It must not overlap the selected creature or the player's actual tile. It should be an inspection card with a close X, not a modal that darkens or blocks the whole screen. No extra action buttons, no attack confirmation.
Card content:
Header "目标详情" with a small "×" close control.
Small portrait of the SAME TAN stone beast and name "石甲兽".
Secondary tag "敌方".
Two clear slim resource bars with labels and exact full values:
"生命 329 / 329"
"灵力 54 / 54"
Below a fine separator, three short readable info rows:
"伤害    未探明"
"命中    未探明"
"能力    未探明"
This is all the data the current game exposes for inspected units. Do not invent attack/defense/speed/AP stats, monster traits, resistance bars, level, rarity, buttons, descriptions or status effects. Avoid excessive empty space; make it polished and readable.
Keep all nine bottom actions neutral except normal UI states; no spell is being aimed. Crucially, show both AP and MP on all three art buttons exactly as in the shared instructions. Preserve the accepted scene, camera, lighting and UI layout apart from this new inspection popup and selected-ground-cell marker.
```
