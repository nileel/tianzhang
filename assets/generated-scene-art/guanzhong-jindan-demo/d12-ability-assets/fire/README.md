# D12.3 长明火域资源包

## 2026-11-17 重做：弯曲灵火

本轮按已确认方向完成风格化火焰资源及Unity样例重做。新证据写入 `evidence/redesign-20261117/<1920|1366>/`，原 `evidence/1920`、`evidence/1366` 保留为下方旧版历史。

[新版默认镜头视频与关键帧](index.html) · [新版1080p记录](evidence/redesign-20261117/1920/runtime-proof.txt) · [新版768p记录](evidence/redesign-20261117/1366/runtime-proof.txt)

- `LowFire.shader` 用连贯弯曲火舌和弧形缺口形成外轮廓，以橙红外焰、金黄中层和浅黄内焰分层；小火舌向上脱离消散。形状为本包原创解析 HLSL，不借用参考展示图，也不依赖噪声碎点、屏幕折射或新插件。
- 每格仍为3簇、6张交叉承载片，单片宽0.44米、三簇承载高度0.64／0.76／0.57米。可见轮廓由 shader 裁出；这些是资源尺寸，不是实测屏幕可见尺寸。足迹保留在本格内，根仍为实际支承格心+0.025米；不修改格位、地形或碰撞。
- `FireCenter/Intact/Glow/CenterSpiritFlame` 复用同一网格和材质，以0.48倍尺寸置于原碑体最高点附近，随原 `Glow` 开关。展开后中心灵火表示主域有效，**不表示地表已经点火**；中心受损仍存在，毁中心随主域撤去。
- `FireAssetSample.Sync` 仍以真实地表／来源／Main／tails 决定低火资格，写 `_Strength=1/.65` 和 `_Opacity`；原0.25秒淡出、水灭回执及本人周期验证保留。
- 新原速视频全程用38°、orthographic size5默认镜头；size3只保留近景补图。运行记录新增每格及中心的 hierarchy、源网格／shader、world bounds、`Camera.WorldToScreenPoint` 投影范围、最终可见像素范围和实际 writer；需结合默认镜头短片检查弯曲与消散，不能用非零像素替代观感核验。

生成与Capture入口沿用下文，逐项串行执行。新JPEG目录为 `src/Logs/d12-fire-redesign-20261117/<宽度>/`；`export-video.ps1` 默认只生成本轮视频。

### 本轮验证记录

Unity 6000.3.18f1编译、重建保存重开、1920×1080与1366×768实际命令样例均PASS。默认镜头三格火贡献8381／4515像素，中心灵火686／394像素；实际世界／屏幕包围与writer见运行记录。展开不点地表火→后续本人结束3格燃烧，中心600→225仍有效→0撤主域与中心灵火，合法余火2→1→0；receipt6恰好两份蒸汽，水灭两格为湿、剩一格自然灭为焦，蒸汽尾效残留0。两版视频均30fps、9秒，已查看默认与近景关键帧及连续火舌运动序列。正式View／V3 HUD接线与用户最终画面审阅仍属后续。

范围与复核：仅Fire资源包及包内样例，引用现有Water资源。References loaded：runtime-owner-proof、unity-validation、ui-and-visual-assets、ai-workflows；Project maps loaded：UNITY_STRUCTURE.md、UNITY_STRUCTURE.content.md，结合当前源码和独立asmdef核实。未改战斗规则、正式场景或共享渲染配置。

## 旧版交付与验证（重做前历史）

2026-11-17。交付低火、有限余烬、全棋盘弱暖边、暖纹中心与焦地共5个 Prefab，直接引用 D12.2 的湿地和灭火蒸汽。

[双分辨率短片与关键帧](index.html) · [1080p运行记录](evidence/1920/runtime-proof.txt) · [768p运行记录](evidence/1366/runtime-proof.txt) · [素材来源](source/README.md)

本包仅验证正式地图尺度下的独立资源样例。生产 `CombatSurfaceView`、`JindanFireDomainView` 与 V3 HUD 接线仍属 D12 集成。

### 交付与接线

Unity 目录：`src/Assets/Art/VFX/Guanzhong/Jindan/Fire/`。

| Prefab | 用法 |
|---|---|
| FireCenter | 原支点碑与派生暖纹，Main有效时开Glow；中心受损仍亮，毁中心撤去Intact，不留封关断碑 |
| WholeBoardWarmEdge | 每个实际棋格的窄暖边，主范围有效时显示；与实际燃烧集合分开 |
| LowFire | 每格3簇低火、6张交叉特效片，最高0.34米；`_Strength=1`为主域，`.65`为合法余火 |
| FiniteEmbers | 一次4粒余烬，最多0.65秒；供熄灭或收势短促表现，不能决定规则期限 |
| ScorchedGround | 自然燃尽的焦地，复用原WaterHex和MudBank派生 |

低火根放实际支承格心+0.025米；燃烧资格读真实地表、来源、Main/tails。暖边与地表用本包 `FireSampleSupport` 按原台阶条带和曲坡12等分贴合；不改地形、不造碰撞。低火样例只在H2平地燃烧，不宣称斜坡火苗已验收。

`_Opacity`供0.25秒视觉淡出；粒子、纹理运动只用表现秒数，最多2个本人结束周期的余火期限完全由规则决定。中心破坏先关闭全部主范围暖边，再保留真实燃烧格的弱低火。水火组合引用同一LowFire，不另做一套。

灭火只消费已提交 `CombatSurfaceSettlement.Changes` 的 Burning→Wet，并按`Sequence`去重；预览不播放蒸汽。直接实例化`Water/Prefabs/ExtinguishSteam.prefab`，原蒸汽材质和粒子参数不改。湿／浅／深也直接引用Water资源，自然结束只将仍由火域拥有的燃烧格变焦，不覆盖已被水改写的湿格。

### 真实命令样例

样例沿正式地图、合法第二段角色及原数值，明确配置独立初态：土修`(7,11)`、水修`(8,11)`、火修`(7,14)`；干燃料格`(7,13)`、`(8,12)`、`(8,13)`各4燃料。入场后只通过生产Preview/Execute推进，没有直接写耐久或余火周期。

| 阶段 | 实际结果 |
|---|---|
| 展开／展开当回合结束 | 中心600、主域有效、火0格、维护0次 |
| 后续本人结束 | 3格燃烧，维护1次；继续行动到土修时已维护2次 |
| 两次破垒 | 中心600→225→0；225时主域仍有效，0时主域撤去且保留3格余火、2周期 |
| 下一本人结束 | 余火剩1周期；此时全局tick仍为40，说明不能用tick或动画秒数代替本人周期 |
| 独立水修涨潮 | 回执6将两格Burning→Wet，蒸汽恰好2份；剩余一格燃烧 |
| 再次本人结束 | 剩余火变Scorched；两湿格保持Wet；无主域、低火或蒸汽残留 |

样例有近看size3、默认size5与144格全棋盘暖边图，镜头倾角38°。原28个静态水层在样例停用，之后按真实地表快照绘制。短片以60Hz表现推进、30fps相机帧导出，包含展示停顿，不推进额外战斗周期；没有正式操作HUD。

### 验证与复现

Unity 6000.3.18f1／URP17.3，Gamma；无深度／背景纹理依赖、全局渲染配置变化或新插件。Builder保存并重新打开样例验证引用；双分辨率Capture检查真实命令、三格上限、预览纯净、回执去重、周期、水灭／自然灭差异、材质支持与蒸汽尾效像素差。最终画面与日志见上方链接。

最终1920×1080／1366×768两次运行均PASS。低火同帧隐藏差5223／2627像素、暖边62521／28085像素、共用蒸汽330／156像素；蒸汽结束后两尺寸残留均为0。源低火网格运行时包围尺寸约0.58×0.34×0.56米，材质轮廓修正没有移动格位或放大模型。两段短片各270帧、30fps、9秒；关键状态逐图核对通过，独立只读复核范围为源码、关键帧及上述运行日志。

依次使用现有Unity批处理入口（同一工程不能同时打开另一编辑器）：

- `TianZhang.Art.Jindan.Fire.Editor.FireAssetBuilder.Build`，带`-quit`。
- `TianZhang.Art.Jindan.Fire.Editor.FireAssetCapture.Capture1920`，不带`-quit`。
- `TianZhang.Art.Jindan.Fire.Editor.FireAssetCapture.Capture1366`，不带`-quit`。

JPEG在忽略目录`src/Logs/d12-fire/<宽度>/`；运行`pwsh -NoProfile -ExecutionPolicy Bypass -File assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/fire/export-video.ps1`导出短片。

首次运行纠正了样例误放不可站立坡肩`(6,13)`及漏计转到水修前的火修本人结束周期；正式地图和规则未修改。复核画面后只改低火材质遮罩，避免火苗呈规整三角形。验证范围不包括正式HUD、输入、退出重入或尚未接线的View。

工作流读取了现有`UNITY_STRUCTURE.md`及content／assemblies图，结合当前源码定位；参考runtime-owner-proof、serialized-persistence、ui-and-visual-assets、project-structure-discovery、modular-architecture、ai-workflows、unity-validation。所有新增C#均少于500行。未修改共享材质、正式场景、CSV、战斗规则、角色或原有RockCluster改动。
