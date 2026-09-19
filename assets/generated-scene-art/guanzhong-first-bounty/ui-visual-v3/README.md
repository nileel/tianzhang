# 天章 · 关中 UI 视觉修订 v3

2026-09-20，D-GZ-PRODUCT-UI-UX-01 的本次人工设计交付。**可操作原型与显示检查已完成；视觉结果待用户判断。** 不构成正式 Unity 接入、整屏美术批准或下游解锁。

## 打开与审阅

- [交互原型](http://127.0.0.1:8774/assets/generated-scene-art/guanzhong-first-bounty/ui-visual-v3/)
- [可操作组件与规范](http://127.0.0.1:8774/assets/generated-scene-art/guanzhong-first-bounty/ui-visual-v3/components.html)
- [第二版原型对照](http://127.0.0.1:8767/assets/generated-scene-art/guanzhong-first-bounty/lookdev-v2/)，旧工作区保持原状。

本版工作区：`D:/天章游戏开发/.worktrees/gz-ui-visual-v3-20260920`，分支 `codex/gz-ui-visual-v3-20260920`。基于主线 `dd6770a7` 新建，未把旧分支代码或任务状态整体搬回主线。用户提供的旧路径少了主目录与 `.worktrees` 之间的分隔符，已依据 Git worktree 注册定位到实际旧工作区。

若服务关闭，在本版 worktree 根目录运行：

```powershell
python -m http.server 8774 --bind 127.0.0.1
```

必须通过 HTTP 打开，以加载 ES module、three.js 和 JSON；直接双击 HTML 不受支持。无需 npm 安装，无外部 CDN、字体下载、生成式图像或付费服务。

审阅顺序：城镇默认 → 悬赏板 → 接取 → 前往关中野外 → 移动/确认 → 普攻预览 → 取消或确认。普攻的演示伤害为 126，七次确认可展示胜利 → 返城 → 一次领奖。下方“状态检查”可直接查看其他状态，它属于审阅工具。所有状态仅保存在内存中，刷新或“重置演示”会清除。

## 本轮设计结果

保留 v2 已接受的信息架构、按需展开文书、服务入口、战斗行动顺序与底部动作区。旧版的主要问题是背景、面板、文字处在接近的浅灰绿中，文字/动作边界弱，字体和控件层级单薄。

本版以墨蓝漆面承载常驻信息，以暖纸承载悬赏正文；楷体用于题字，微软雅黑用于动作原因与数值信息。头像采用拱顶框，动作采用统一线描图标、独立边框及菱形选中点。玉色只负责行动/友方/选中，朱砂只落在印章和敌方，旧铜仅作细线分隔。背景继续是原 SVG 构图占位，只调整显示明暗以检查 UI；没有制作正式城镇背景。

完整值、尺寸、组件状态和制作拆分见 [VISUAL-SPEC.md](VISUAL-SPEC.md)；可视样例见 [components.html](components.html)。文字、按钮、数字、资源条、印章汉字与状态均由独立 HTML/CSS/JS 绘制。12 个 SVG symbol 是本轮直接绘制的可编辑有限图标，不含烘焙文字。

| 关键画面 | 1920宽（1080高游戏画布） | 1280宽（720高游戏画布） |
| --- | --- | --- |
| 城镇默认 | [查看](screenshots/city-default-1920.png) | [查看](screenshots/city-default-1280.png) |
| 悬赏详情 | [查看](screenshots/city-bounty-1920.png) | [查看](screenshots/city-bounty-1280.png) |
| 战斗待行动 | [查看](screenshots/battle-idle-1920.png) | [查看](screenshots/battle-idle-1280.png) |
| 攻击预览 | [查看](screenshots/battle-preview-1920.png) | [查看](screenshots/battle-preview-1280.png) |

另有 [超距禁用](screenshots/battle-out-of-range-1920.png)、[可领奖](screenshots/city-reward-1280.png)、[组件规范](screenshots/components.png)。截图来自浏览器真实渲染，保留外部审阅工具栏，文件尺寸为1920×1220或1280×860；没有合成或重绘。组件页为完整长页截图。局部裁取接口曾导出异常空画面，回读发现后已全部替换为完整浏览器捕获并逐张查看；未为截图改动页面实现。

## 沿用的成果与时间语义

- v1 整屏被否决；本版未复用其生产参数或六份 Sol 提示词。
- v2 用户“除风格以外都还可以”保留有效：结构、交互和真实三维高差已接受；“太淡太素，缺少仙侠气韵”是本轮处理的问题。旧 README 尾部的笼统待审句子不推翻后续明确接受。
- v5 自然草土边缘、青绿地表、青蓝岩壁已采用；9 月 20 日 Tripo 地块 `67743c70`、石阶 `1555965a` 已作为初始地形采用。接受地形不等于接受 UI 或整屏。
- `battlefield.js`、`scene-data.json`、three@0.180.0 与 v2 逐字节一致。战斗保留真实 WebGL 网格、三级高差、石阶与单位尺度代理；没有重做地形，也没有将 Tripo 静态展示伪称为正式玩法接入。UI 以石青/玉色与已采用方向协调，当前原型的几何仍是 v2 阅读样板。
- 头像继续引用仓库批准透明母版 `assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.png`；只用 CSS 裁切，不重新生成人物。

## 本轮实际验证

- 在 Codex 内置浏览器实际操作；1920×1220 和 1280×860 浏览器视口分别容纳 1920×1080、1280×720 的 16:9 游戏画布及外部审阅条。
- 两种宽度检查默认/文书/待行动/预览；主要文字与确认按钮可见可操作，无页面横向裁切。1280 下最小辅助字约 10.7 px，关键正文约 13.3–14.7 px；正式 Unity 仍需独立分辨率与字体验收。
- 接取 → 出城 → 超距禁用 → 沿阶移动 → 预览 → 取消保持敌方 839/839 → 确认演示攻击。生命依次为 713、587、461、335、209、83、胜利；只是既有演示状态，不形成数值结论。
- 胜利返城显示目标 1/1、奖励劣质灵石×3；领取后退出活动条目，再打开无二次奖励按钮。
- 行动顺序可点击查看敌方信息，术法快捷键显示“尚未装配术法”，战报可开闭；格线选中与取消后的显示一致。
- 状态检查从胜利切失败/配置错误，敌方显示恢复，文案对应正确；组件页可点击查看选中和确认反馈。
- 浏览器 error/warn 记录为空；JS 语法、相对资产存在性、源文件/截图哈希与 whitespace 检查见 manifest。没有运行 Unity/BattleSim，因为本轮未改它们的代码、数据或正式资产。

实际检查中修正：提示遮住预览、近距离单位名牌重叠、透明顶部 header 拦截行动顺序点击；并对沿用状态代码做局部校正，避免格线按钮残留选中、结果检查态继承已消失敌人、已完成悬赏再次出城仍显示未完成目标。没有添加第二套战斗规则或地形机制。

## 当前主线与正式接入缺口

以 `dd6770a7` 静态代码/任务核查为准，不能用旧设计说明替代：

1. 主线没有 D-GZ-PRODUCT-UI-UX-01 卡。主线 A-GZ-ART-LOOKDEV-01 仍记旧 v1 拒绝与 blocked；U-GZ-ART-UI-01 仍依赖它。本轮只在独立新目录更新设计记录，不覆盖主线卡、不改调度、不自行归档或批准。
2. `CombatPresentationContracts.cs / CombatHudSnapshot` 仍缺 CT、行动序列、地形/单位选择、范围/路径、预计伤害/命中与禁用原因。`EncounterCoordinator.Present` 是快照生产者，`CombatHudPresenter` → `CombatHudView/CombatActionBarView` 是当前显示链，`CombatCommandInput` 点击即提交。正式预览应消费规则查询，UI 不复算规则。
3. 城镇真实 owner 是 `SettlementSceneBuilder`、`SettlementView` 与 `BountyBoardView`；后者 `UpdateButtons` 目前只按 hasCurrent 同时启用接取和领奖，并非本原型四态按钮投影。需在 U 卡正式实施前重核这些路径。
4. 主线已完成 `AdventureSceneBuilder.BuildGuanzhongBattlefield` 的基础功能地面，旧多层 `VisualBaselineBoard` 默认不活动。正式场景仍使用 0 高，规则入口 `CombatEntryAdapter` 创建 469 格默认高度，`AdventureUnitSpawner` 的 Y 固定，`SpatialQueryBoard` 对异高返回 HeightRuleUnconfigured。不能把“基础功能地面完成”写成“Tripo 或高差已接入”。
5. U-GZ-ART-UI-01 的旧 expectedPaths 未覆盖以上全部运行时 owner 和相关 PlayMode 检查；最终视觉接受后重新核定范围。正式城景、图集/切分、Unity 字体、地形通行、角色表现、声音/VFX、整流程实机美术验收仍独立推进，非本轮完成项。

## 协作、范围与来源

本轮是用户明确授权的 blocked 设计续作，未从 ready 队列领取任务。开始时 schema 5 Show 显示两个 owner run 均为空，integrationLockStatus=none；仍创建最新主线的专用 worktree 以保留旧地形实验和人工改动。L0 的“手动选中 ready 卡”前置不适用于本次非 ready 续作；没有伪造缺失主线卡的 riskPreflight。

复杂度为中等：视觉所有者集中在两个页面，但旧分支与主线事实有时间差。主智能体统一实施与浏览器复核；只读辅助 `/root/current_ui_facts` 核对主线 owner 和局部状态回归。请求的辅助模型/强度为 gpt-5.6-sol / high，工具仅返回任务标识，实际模型未由元数据核实。辅助没有业务写入、提交、任务或 runtime 操作。

来源：旧工作区基线 `8b735866` 的 lookdev-v2 可编辑源、v2 设计规格、D 设计卡与 9 月 20 日两份 Tripo README；本轮明确用户指令；主线当前相关任务与源码。three.js 保留原 MIT license。所有原型数字、范围、行动顺序仍是演示口径。
