# V4 · 紧凑战斗 UI 素材（2026-10-21）

本目录完成 V4 的素材与样式交付，供 V5–V7 接入。沿用 [10 月 21 日参考](../interaction-preview-20261021/README.md) 的布局，延续既有 UI v5/v6 的矢量线稿与面板做法。**未修改 Unity 代码、场景或共享皮肤；校样不是运行截图。**

- [素材总览](evidence/sheet-1720.png)／[可打开的校样](index.html)
- [1920×1080 双窗与十动作](evidence/layout-1920.png)／[1366×768](evidence/layout-1366.png)
- [图集](compact-ui-atlas.png)／[切片与样式清单](manifest.json)／[头像提示词](PROMPTS.md)
- [检查结果](evidence/validation.json)：32 个切片、3 个头像引用、3 张校样，错误为 0。

## 素材与身份

| 用途 | 文件／切片名 | 约束 |
|---|---|---|
| 普攻 | icon_attack、icon_unarmed | 剑与徒手；显示名称来自当前武器／攻击档案，不把所有普攻固定为铁剑 |
| 贯石击 | icon_guanshiji | 贯穿岩块的线稿，表示贯坚；不暗示新土属性或群体陨石 |
| 聚灵术、飞星指 | icon_julingshu、icon_feixingzhi | 聚气回环、星芒 |
| 移动、防御 | icon_move、icon_defend | 靴、盾 |
| 换术、换装、丹药 | icon_switch_art、icon_equipment、icon_medicine | 卷轴、衣装、丹瓶；实际名称／次数／数量独立 |
| 条件神通 | icon_divine | 保留独立入口，不固定为某个未装备能力 |
| 辅助 | icon_close、icon_cancel、icon_end_turn、icon_log | 关闭、取消、结束、战报 |
| 面板／按钮 | panel_dark、button_end、button_normal/hover/pressed/selected/disabled/cooldown | 九宫格；中心无文字 |
| 头像框与轴 | frame_normal、frame_selected、timeline_current | 金边表示查看选择；青蓝侧线表示当前行动，二者可同时存在 |
| 资源／遮罩 | bar_track、fill_health/mana/ap、cooldown_shade、divider | 填充值与冷却覆盖由运行时控制 |

每个切片有可编辑的 `vectors/*.svg` 与可直接导入的 `sprites/*.png`（120×120）。图集包含相同像素，不要把 SVG 依赖引入 Unity。

- 玩家：直接复用 `src/Assets/Art/UI/Guanzhong/FormalPlayer_Default_Portrait.png`（512×512 RGBA），不复制、不重绘。透明像素的 RGB 含绿色，但可见像素无绿幕；无需抠绿 Shader。
- 石甲兽：`portraits/enemy_shijiahou.png` → `enemy_shijiahou`／`combat_enemy_shijiahou_v1`，暖褐、向左、闭口。
- 裂石甲兽：`portraits/enemy_lieshijia.png` → `enemy_lieshijia`／`combat_enemy_lieshijia_v1`，冷蓝、向右、微张口。现用批准 v2 网格与石甲兽相同，独立材质乘色为 (.42,.56,.82,1)，没有新增角翼、水晶或发光能力。
- 两张新头像均为原生透明 PNG，1254×1254；头与顶角完整，肩部允许到下边缘，作为方形头像内裁切。行动轴和详情使用同一源、同一比例，`Preserve Aspect`，避免为详情另选另一身份图片。
- 根据本次用户反馈，撤下偏写实首稿，最终稿减少皮肤微纹理、概括岩甲明暗，以现用玩家头像为手绘风格参照。48／64 px 小样见总览；此为已制作方案，不代替用户最终审美判断。

## 导入与切片

V5 建议复制图集与两张敌人头像到独立 `Assets/Art/UI/Guanzhong/CombatCompact/`。不得覆盖旧 `Guanzhong_UI_Atlas` 或改聚落共享皮肤。V4 本次不在 src 中导入或生成 .meta。

- 图集 1024×512，8 列×4 行。每槽 128×128，中间有效切片 120×120，四边各 4 px 透明间隔。
- `manifest.json.slices` 提供每张切片完整的 `rectTopLeft`、Unity 左下原点 `rectUnityBottomLeft`、`borderLTRB` 与 pivot。Unity border 的 Vector4 顺序为 **left,bottom,right,top**；本包上下 border 相同，仍应按字段转换，不能把 top-left 的 y 直接写进 SpriteRect。
- Texture Type Sprite；图集 Multiple，头像 Single；Full Rect，100 PPU，sRGB，Alpha Is Transparency，Bilinear，Clamp，无 Mipmap，无压缩，Max Size 2048。
- 面板／按钮／框为 `Image.Type.Sliced`，border (24,24,24,24)；bar_track 为 4；divider 左右 4、上下 0。图标和头像为 Simple；资源填充为 Filled Horizontal、从左开始；cooldown_shade 可作为 Filled Radial360 遮罩。
- 图标 RGBA 已为米白色。正常 Image.color 用白色；禁用可用 alpha .5，不能再次乘米白导致不必要变暗。新面板使用不透明底，文字对比不受场景暖光影响；Screen Space Overlay UI 保持独立配色。
- 所有装饰 Image 的 Raycast Target 关闭；只让实际 Button／滚动视口承担输入。世界点击是否穿透由 V5/V6 实测。
- 图集和头像的切片名称就是本目录稳定交付名；正式 GUID 由 Unity 在 V5 导入时生成。

## 样式

| 状态 | 外观 | 内容／优先级 |
|---|---|---|
| 普通 | 深青 #193B46、灰玉边 | 米白图标与正文 |
| 悬停 | #28515A、浅玉边 | 只提亮，不改大小 |
| 按下 | #0B222B、内收暗面 | 不移动整个按钮，保留命中区域 |
| 选中 | #244D4C、金边 #D8BE80 | 表示当前选取动作／查看对象 |
| 禁用 | #24343B、灰边、图标半透明 | 原因文字仍清晰，例如灵力不足；由权威可执行状态决定 |
| 冷却 | #162F3C、冷灰蓝边 | 使用独立暗遮罩；叠加 RemainingCooldownTicks 的 CD 数字，单位为全局“刻”，不是回合；不能用固定图片数字 |

禁用／冷却决定可操作性；其上仍可保留独立 `frame_selected` 来表达查看选择。可操作时为按下 > 选中 > 悬停 > 普通。结束按钮使用 `button_end`，悬停沿用 hover、按下沿用 pressed、禁用沿用 disabled，不另建动作语义。

主文 #EFE9D3、次文 #C2D0CB、禁用原因 #A4B5B3、敌方／拒绝 #D99982。相对面板 #102D38 的正文对比约 11.87:1、次文 9.06:1、禁用文 6.76:1。HP #8FBE75、MP #68C5D9、AP #E6BF62；均同时显示标签和数值，不只靠颜色识别。

字体复用 `Assets/Art/UI/Guanzhong/Fonts/GuanzhongChinese.otf`（Source Han Serif SC），沿用项目 UnityEngine.UI.Text，不为 V4 更换 TMP／字体。1920 设计字号：地点 34、面板标题 28、正文／常规按钮 24、紧凑动作／费用／原因 22；小屏等比后最小约 15.6 px。字体实际字面高于字号，紧凑动作行高用 28，别按 22 高裁切。

外边距 32，面板内边距 20，标准间隔 16–24。图标总览 40 px、紧凑栏 28 px；图标只是内容，动作完整命中面积至少 124×140 设计像素。头像行动轴 78、详情 80、资源卡 104×120；图片保持比例，不拉伸。

## 布局与动态容量

`manifest.layout` 是左上原点的 1920×1080 **设计坐标**，不属于运行时坐标证明。V5 应从同一坐标依据布置父框、图像和文本，再核对 CanvasScaler、anchor、pivot 及运行时 writer；不要直接叠加旧 Builder 偏移。

| 区域 | x / y / 宽 / 高 |
|---|---|
| 左上资源卡 | 32 / 24 / 416 / 188 |
| 左侧行动轴 | 32 / 232 / 104 / 512 |
| 地点 | 740 / 24 / 440 / 64 |
| 撤离 | 1696 / 24 / 192 / 60 |
| 悬赏 | 1536 / 112 / 352 / 112 |
| 详情 | 1536 / 232 / 352 / 376 |
| 预览 | 1536 / 632 / 352 / 236 |
| 底栏 | 160 / 892 / 1728 / 164 |

两窗上下排列，相隔 24；预览距底栏 24。底栏比原实现更浅，HP/MP/AP 留在左上；结束按钮与动作视口相隔 32，独立在最右，战报位于其下。资源卡、轴、详情、预览、底栏互不重叠。校样在 1366×768 以 min(width/1920,height/1080) 等比缩放、居中。

- 九个常用动作是默认内容，不是槽位上限。校样包含第十个条件神通；增加内容时动作视口横向滚动或采用项目既有更多入口，不能删除条件动作或挤压独立结束按钮。
- 不显示 1–9。动态动作名最多两行；更长时省略并在预览中完整展示，禁止把整条费用文本缩到不可读。预览的长原因／正文在固定内部区域滚动，关闭／取消保持固定可点。
- 费用按字段分行，数值与 AP/MP 不拆开。AP/MP、CD、数量、次数、名称都是运行时文字；示例的长武器名只测容量，不新增物品。
- 贯石击 50 AP/12 MP，飞星指 50 AP/10 MP，聚灵术自身 25 AP/0 MP、他人 25 AP/20 MP。以当前 `CombatHudSnapshot`／报价为准，素材绝不提供计费逻辑。预览区分当前 AP 与预计余额。
- 详情只显示当前公开字段，伤害／命中／能力保留“未探明”。行动轴当前与选择分别用青线／金框；头像依据单位 ID，不按队列顺序、敌方阵营或暖冷猜测身份。

## 接续入口与验证边界

核对过的当前 owner：`AdventureSceneBuilder.BuildCombatLayout/BuildCombatTimeline`；`CombatActionBarView.Present/PresentArt/PresentAction`；`CombatTimelineView.Render`；`CombatTargetView.Present`。右上撤离还须同步 `AdventureHudPresenter.ApplyReturnEntryLayout`。旧共享 `SettlementSceneBuilder.ApplyButtonSkin` 不负责本次独立战斗样式；不要直接改它来换整项目皮肤。

`build.mjs` 使用本机 sharp 将可编辑 SVG 导出为 PNG 与图集，按原始 RGBA 像素拷贝，避免透明合成取整差；不会写入 Unity。复建需 Node 与 sharp。检查使用 sharp、Playwright 和本机 Edge；运行 `node verify.mjs`。仅 HTML 样式变化时可用 `node verify.mjs --visual-only`，保留已通过的素材检查。

已检查 PNG 解码／真实 Alpha／头像顶角、32 个切片像素一致、Unity y 转换、字体加载、小尺寸目视、两分辨率面板边界／重叠／文字溢出与布局图。已检查两窗同时显示、十动作、长名称、六态。冷却暗遮罩与数字是接入用分层资源；校样数字只是静态样例。

未运行 Unity 编译／PlayMode、Builder 重建、真实点击、报价或战斗测试，因为本次没有改动其输入；这些由 V5–V7 按对应项完成。未改 docs／CSV／数据链、玩法、模型、材质、聚落皮肤或旧管理流程。

技能参考：imagegen；unity-agent-workflows 的 ui-and-visual-assets、runtime-owner-proof。工程地图：UNITY_STRUCTURE.md、UNITY_STRUCTURE.ui.md。最终整体效果仍需在 V5–V8 的真实 Game View 中核对。
