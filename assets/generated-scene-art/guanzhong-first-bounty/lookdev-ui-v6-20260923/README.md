# 关中首场：当前 UI 与地块示意图

- 生成日期：2026-09-23。
- 用户要求：按照现在的 UI 和地块设计重新生成城镇、战斗两张示意图。
- 生成方式：内置 imagegen，各生成一次；原始生成文件保留于 Codex generated_images。
- 状态：供用户审阅的设计示意，不代表 Unity 实机效果、正式资源验收或任务审批。图片内示例文案、数值、奖励、术法栏与背景装饰不作为设计事实源。
- 新版另存，旧版 `../lookdev/` 保留。

## 图片

- [城镇与悬赏](city-and-bounty-v2.png)
- [战斗与 HUD](battle-and-hud-v2.png)
- [完整提示词](prompts.md)

## 参考来源

- 城镇结构：`../ui-visual-v4/screenshots/city-bounty-1920.jpg`；v6 延用的城镇布局。
- 战斗结构：`../ui-visual-v6/screenshots/01-battle-default-1920.jpg`。
- 通用 UI 材质：`../ui-visual-v6/screenshots/04-template-jade-1920.jpg`。
- 当前地块：`assets/source/environments/guanzhong-first-bounty/initial-v1/unity-unoccluded-fuyuan-detail.png`（仓库根目录相对路径，本地原始素材）。青绿草面、自然边缘、青蓝岩壁；参考中的测试角色和红色胶囊不沿用。
- 默认男主：`assets/generated-character-art/dialogue-transparent/formal-player-default-male-v1.png`（仓库根目录相对路径）。
- 场景事实：`docs/剧情/据点/关中城.txt`；地块验收记录：`开发管理/关中首场战场美术验收记录.txt`。

## 本次目视核对

- 城镇保留左上人物信息、中上城名与出城、右侧悬赏文书及场景入口标签。
- 战斗保留窄行动轴、底部招式栏与草顶岩壁地块；移除参考中的测试胶囊。
- 两图未带外部评审工具栏。生成的场景细节、背景和单位造型仍需美术评审，不能从示意图反推已实现内容。
