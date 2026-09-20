# 《天章》UI v6 · 共用模板与战斗收尾

2026-09-20。用户已确认“OK，就先定这个了，合并然后推送一下远端”。本版战斗调整、共用模板与复用规格作为当前设计基线定稿；保留已接受 v5。该确认不扩大为正式 Unity 接入或最终美术资源验收。

- [战斗 v6](index.html)：右侧悬赏、76 宽行动轴；姓名与 CT 按需显示。
- [普通全屏／大小弹窗／列表／按钮样板](components.html)：点击物品、详录、整理确认；上方按钮或 K 换肤。
- [统一复用规格](REUSE-SPEC.md)、[资源清单](ui-kit/RESOURCE-MANIFEST.json)、[验证记录](VALIDATION.md)。
- [保留的 v5](../ui-visual-v5/index.html)、[城镇 v4](../ui-visual-v4/index.html)。

## 打开

从隔离工作区根目录通过 HTTP 提供文件。现有服务入口：

- http://127.0.0.1:8774/assets/generated-scene-art/guanzhong-first-bounty/ui-visual-v6/
- http://127.0.0.1:8774/assets/generated-scene-art/guanzhong-first-bounty/ui-visual-v6/components.html

若无服务，在该工作区根运行 `python -m http.server 8774 --bind 127.0.0.1`。不要直接双击 HTML（战斗使用模块和 fixture fetch）。不要在端口已占用时重复启动。

## 关键截图

| 文件 | 内容 |
|---|---|
| screenshots/01-battle-default-1920.jpg | 默认战斗与紧凑行动轴 |
| screenshots/02-battle-combined-1920.jpg | 悬赏、展开详情、预览与更多菜单同屏 |
| screenshots/03-battle-twelve-1280.jpg | 12 人、末端选择、按需身份与无效目标 |
| screenshots/04-template-jade-1920.jpg | 普通全屏、列表、纸面与按钮 |
| screenshots/05-template-proof-1920.jpg | 同一页面仅更换皮肤资源 |
| screenshots/06-large-dialog-1920.jpg | 大窗与长文 |
| screenshots/07-small-dialog-jade-1920.jpg | 小确认窗与保留的大窗 |
| screenshots/08-small-dialog-proof-1920.jpg | 已打开窗口原位换肤 |
| screenshots/09-button-states-1920.jpg | 状态与键盘焦点 |
| screenshots/10-template-long-1280.jpg | 长名称、正文内部滚动 |
| screenshots/11-template-empty-1280.jpg | 空分类 |

截图为浏览器 JPEG；战斗 1920 图含审阅条共 1920×1220，样板为 1920×1080，窄屏为 1280×860。原型上的审阅工具条不属于游戏 UI。

## 范围

v6 实施只在现有 UI worktree 的 ui-visual-v6/ 内写入。沿用 v5 数据和 WebGL 地形，未修改 v5、v4、地形、正式 Unity、任务卡、队列。共用套件仅含模板函数、CSS 和 10 张示范 SVG；没有完整 UI 框架。用户已授权将本版及其尚未进入主线的 v3/v4/v5 依赖历史合并并推送。

HANDOFF.md 保留为进入本轮时的草稿交接，不是当前完成记录；当前结果以本 README、规格、验证及实际代码为准。
