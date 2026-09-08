# 苻渊 · 单方向 2D 分层骨骼小样

2026-09-08。**可观看、可编辑的试验已完成；当前宽袖表现未通过。** 本次实际使用 Unity 官方骨骼蒙皮，不是旧 `.spine` 栅格时间线，也不是生成序列帧。正式效果由用户观看判断。

## 先观看

- [最终隔离比较版 v2：1920×1080 / 24 fps / 9.5 秒](unity-pilot-v2.mp4)
- [v2 缩放动图：1280×720 / 12 fps](unity-pilot-v2.gif)（检查原生战棋尺寸请看上方 MP4）
- [原始绑定 v1 完整录屏](unity-pilot-v1.mp4)
- 运行 `pwsh -NoProfile -File artifacts/fuyuan-2d-skinning-pilot/PlaySample.ps1` 打开可互动播放器。可以暂停、重新施法；提供袖网格显示开关。

左右画面是**同一个角色、同一个时刻、相同观察方向**。左侧近景 orthographicSize=0.90；右侧复用项目相机 position=(0,8,-10)、Euler=(38,0,0)、orthographicSize=6.2、near=.1/far=60。右侧 viewport 宽度缩小，屏幕高度仍为1080，角色原生像素高度没有被放大。画面没有角色特效。

录屏来自 Unity Standalone Player 的 `WaitForEndOfFrame → ScreenCapture.CaptureScreenshotAsTexture`。每版实际连续录得255帧；v2交付视频只截取前9.5秒，完整包含待机、施法和回待机，避免末尾进入下一次施法。没有补帧、AI生成帧或图像变形后制。首个隐藏窗口捕获失败，改为正常可见播放器后两版录制成功；失败日志仍在保留工作区。

## 编辑源

运行 `OpenEditor.ps1`，或用 Unity Hub 打开本目录 `UnityProject`（Unity **6000.3.18f1**），再打开：

`Assets/FuyuanPilot/Scenes/FuyuanSkinningPilot.unity`

- Prefab：`Assets/FuyuanPilot/Prefabs/FuYuan_Direction1.prefab`
- 17根骨骼在Prefab的`Root`下；11个图层对象挂官方`SpriteSkin`，共4401个网格顶点。
- 图层与可编辑网格/权重：`Assets/FuyuanPilot/Art/*.png`及同名`.meta`。选PNG → Sprite Editor → Skinning Editor，可编辑骨骼、网格及权重。**必须保留 `.meta`**，网格权重保存在其中。
- 动画：`Assets/FuyuanPilot/Animation/Idle.anim`、`Cast.anim`、`Fuyuan.controller`，均为标准Unity AnimationClip/AnimatorController。Animation窗口可编辑骨骼曲线。
- 作者脚本：`Assets/FuyuanPilot/Editor/PilotRigBuilder.cs`、`PilotAnimationAuthor.cs`；运行/取证：`Runtime/PilotPlayback.cs`。菜单`Fuyuan Pilot/Rebuild Editable Sample`会按脚本重新生成覆盖实验资产，手工编辑之后不要直接重建。
- v1施法曲线备份：`cast-v1-before-helper-isolation.anim`，它位于工程外用于复核，不是第三个动画。
- 源部件、统一画布图层和组装信息：仓库`assets/generated-character-art/fuyuan-2d-skinning-pilot/`，包括`layers/`、`assembled-layers/`、`extraction.json`、`assembly.json`、`prompts.md`。

原始AI母图共12部件，11部件参加绑定。所谓独立内衬被模型画成了衣领，未采用；袖片本身已画有连续内衬。躯干只截取颈到腰带，独立前后袍摆覆盖下半身，避免生成母图重复的玉坠。静态组装后才制作绑定。

## 实际结果

| 检查 | 结果 |
|---|---|
| 官方骨骼/网格真实运行 | 两版255帧×11层全部有当前变形顶点；无图片切帧 |
| 身份稳定 | 同一头、手和服装纹理持续使用；未见逐帧脸型或纹样变换。静态后发/颈部拼接感仍在 |
| 大动作 | 近肩最高转角-160°，抬臂→前挥→停留→收势。手腕实测x=-.150～.380、y=.689～1.132，未减小动作掩盖问题 |
| 战棋尺寸 | 起始实测投影约71.33×116.13px，高度符合既有battle样例；动作轮廓有变化，但遮脸降低身份可读性 |
| 脚锚 | 两版最大脚骨与脚网格漂移均为0 |
| 回待机 | 实测3.083秒进入Cast，6.917秒回Idle；回待机边界手腕相邻帧位移约0.000183 world。未见明显跳回，但未把此值当全网格连续性的完整证明 |
| 躯干、袍摆、头发 | 无可见三角翻面；呼吸与轻微跟随能运行 |
| 手腕/袖根 | 抽查帧未见手完全脱离袖口；严重袖折挡住部分连接区，不能据此宣称全动作关节验收通过 |
| 近袖 v1 | 最多178个可见三角翻面，最小有符号面积比-3.938；明显纹理塌陷和纸片折叠 |
| 近袖 v2 | 最多3个可见三角翻面，最小面积比-0.087；翻折显著减少，但袖腹随肩翻起到脸前/头顶，缺少自然下垂 |
| 远袖 v1→v2 | 最多31→0个可见三角翻面；不代表近袖或整套表现通过 |
| 遮挡与袖腹 | **未通过**。v2明显遮脸，宽袖像整片翻转的袋子；没有靠改窄袖、缩小动作、遮住问题或加特效判成功 |

数值详见[汇总](metrics-summary.json)及`captures-v1/runtime-evidence.json`、`captures-v2/runtime-evidence.json`。三角判定以绑定姿态纹理alpha采样筛选，不等于每帧可见像素完整缺陷检测；无翻面不保证纹理或剪影美观。

## 已证实原因与未知项

v1、v2的人物、相机、状态时间和主肩肘腕位置逐帧相同。唯一干预是把近袖腹/袖口下缘、远袖腹从独立世界轨迹改为随肩的烘焙曲线，延迟0.025秒；素材、权重、主动作幅度和排序未变。近袖178→3、远袖31→0，支持**原辅助骨轨迹与主臂运动冲突是大规模翻折的主要原因**。

v2又证明：跟随肩部旋转会把原本向下的整个袖腹翻到上方。剩余问题涉及辅助骨运动方式、权重分区与当前单整片宽袖表达，尚未分离验证；**不能全部归为素材缺失，更不能宣称只补画就能解决。** 现有排序令整片近袖压在脸前，换排序也不能修复错误下垂形态。

按用户停止条件保留这两版，不继续叠加补丁。没有证据证明路线整体不可行，但本小样的“宽袖自然”尚未成立。

## 实际成本与下一步

全轮墙钟约40～45分钟（香港时间14:09左右开始、14:53左右交付；边界为估计）。并行只读核查与主线程有重叠，不能相加当作工时。

| 阶段 | 本轮观测 |
|---|---|
| 事实、工具、隔离检查 | 约8分钟；未改Unity版本或正式src |
| AI素材制作与分层组装 | 约10分钟；内置ImageGen共2次，工具等待91.2秒+68.6秒；一次RGB假透明失败及一次背景/结构修正 |
| 绑定和动画 | 合计约6～7分钟；骨骼、网格/权重、动画单项未独立计时，准确拆分未知 |
| 运行诊断及返工 | 约10分钟；一次编译List→array修正，一次隐藏窗口录屏失败后可见窗口重录，一次辅助骨曲线隔离比较 |
| 交付整理 | 约6～9分钟；以上是AI会话窗口，不能外推人工生产工时 |
| 人工补画 | 本轮0次；尚未证明必须人工补画。若继续，最小待验证素材面是一侧近袖的前/后片和袖根覆盖区，不是整角色重做或整套外包 |
| 费用 | 未采购软件、插件或素材；未调用额外付费外部生成服务。内置ImageGen计费/credits未提供，准确费用未知 |

**暂不建议进入第二方向。** 若继续，先锁定同方向、一侧近袖和相同的大幅动作，验证抬臂/挥出两种极限姿势的袖前后片、袖根固定区域与袖腹下垂控制，再做连续运动。重分片与人工补画都只是待验证办法；六方向、批量角色、换装、URP正式集成成本均未知。

## 隔离、实现与验证范围

- 只写两个新目录：本实验目录与`assets/generated-character-art/fuyuan-2d-skinning-pilot/`；正式`src`、Adventure、战斗规则、存档和视觉路线未改。
- 新工程使用Built-in的Sprites/Default，复用实际项目镜头、角色尺寸和格位合同；本轮没有验证正式URP集成。
- Unity官方包`com.unity.2d.animation 13.0.5`声明最低Unity6000.3，依赖记录在`Packages/manifest.json`与`packages-lock.json`。[官方SpriteSkin说明](https://docs.unity3d.com/Packages/com.unity.2d.animation@13.0/manual/SpriteSkin.html)、[官方Sprite Data Provider](https://docs.unity3d.com/Packages/com.unity.2d.sprite@1.0/manual/DataProvider.html)。
- 实际所有者：实验Scene→Prefab→Animator标准曲线→17骨Transform→11个官方SpriteSkin；没有正式系统调用者。PNG meta保存了骨、网格、权重，构建从已保存场景生成Player并实际播放。
- Unity CLI `-executeMethod FuyuanPilot.Editor.PilotRigBuilder.BuildPlayer`：最终两版均退出0；最终播放器255帧连续捕获完成，未见C#异常；RGBA导入层11/11，四角均alpha=0。
- MP4完整解码成功；提交前运行项目`check-pending-whitespace.ps1`和`git diff --cached --check`。Unity生成meta的空字段尾空格按Git检查规范清理，骨骼/网格数值与GUID未改。
- 项目地图已读：`UNITY_STRUCTURE.md`、`UNITY_STRUCTURE.assemblies.md`。技能已读：unity-agent-workflows、imagegen、brainstorming；相关引用已读：project-structure-discovery、modular-architecture、ai-workflows、runtime-owner-proof、visible-object-identity、asset-source-lock、ui-and-visual-assets、serialized-persistence、unity-validation、runtime-numeric-proof、runtime-visible-output。
- 原始510张PNG、构建/播放器日志和可运行二进制在保留worktree及本地Build中；Git只保留视频、汇总/逐帧数值证据和可编辑源。worktree：`.worktrees/fuyuan-2d-skinning-pilot`。
