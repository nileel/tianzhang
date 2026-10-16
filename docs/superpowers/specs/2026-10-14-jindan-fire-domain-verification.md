# 赤野燎原 Unity 场景：独立费用与容量复验

日期：2026-10-16。复验卡：`N-JD-COMBO-FIRE-BUDGET-01`。本记录是在 U 的正式提交 `251275e27652d1bfb7dbfdc9d173dbf66aef8a55` 上、当前隔离 worktree 重新运行；不是重用 U candidate 或旧渡火引线 fixture。

## 固定输入与同版核对

U 提交相对于本轮工作树的合同 §3 十二个 Unity 资源内容一致（`git diff --quiet 251275e2 -- <12 paths>` 返回成功）。代表性 SHA-256 如下；其余 six 个 meta 同样由上述逐路径比较覆盖。

| 资源 | SHA-256 |
|---|---|
| `JindanFireDomainScenarioState.cs` | `f6450c823ecd28e5a8cd5e509b10dbef4fe2a0f170f1db15c97da2568b0a6140` |
| `JindanFireDomainScenarioController.cs` | `b9b0be1a8eac4713e770fe76d928e87f2c33fc5d11bc4c9465ca261d967f21de` |
| `JindanFireDomainScenarioSceneBuilder.cs` | `f9655afb7562a6d0bcdc3cdbbba989e5c88f2354fdf4c2f5cf79f2053d9fc6fd` |
| `JindanFireDomainScenario.unity` | `1c392013b1485ab2c6bd0a68e4aca62b69c462db3eebb16556b6a8183b2db84e` |
| EditMode 直接测试 | `55175a42c8c757f3ebc90fd480e3a0b7cd08e8116a392fc8e4745ed787437bfb` |
| PlayMode 场景直接测试 | `0c425b019951742d1341e7eaf97ea31e45a6b4af3a431808b4733d91e146b6cc` |

冻结且只读的验收输入也逐项相符：宽袖 Blender 为 576092 bytes / `5773beefefd1c4de02ba80b3f3b12f39bdd084adfe600477adeb9730ca255f65`；石甲兽 FBX 为 24929388 bytes / `36ba2de48926b5c1aee6f2692b51891c2d48c87c2be08331db62dc8baf470700`；BaseColor 为 22800947 bytes / `7499200a1b372963b190a0aa6e489811aa76f94c849c1cd610cfe96c3e8037ac`。

## 实际复验

Unity 6000.3.18f1 在当前 worktree 实际加载 `Assets/Tests/Scenes/JindanFireDomainScenario.unity`。运行命令严格使用合同 §7：完整 EditMode（无 TestFilter）及指定 `TianZhang.Tests.PlayMode.JindanFireDomainScenarioPlayModeTests` 的有图形 PlayMode，随后运行程序集边界检查。

| 范围 | 实际结果 | 原始输出 SHA-256 |
|---|---|---|
| 完整 EditMode | 1341 / 1341 通过，0 失败、0 inconclusive、0 跳过 | XML `fa49fa96076a3a95f1c57ba29e4cabc551acdcdd1550fadf8737e761d7e99ad4`；runner log `3d3ae0de81c24854d9824aa1957344bc45f8766f30467522e1681d6fe6dfe634` |
| 指定 PlayMode | 12 / 12 通过，0 失败、0 inconclusive、0 跳过 | XML `c7fbf05185449e4edb062b34d9aad20c7398343a6e1be2f18f8c34214b00db85`；log `0d82a04abc68791f8456e056795e5a6bc6a05c2cdda0af2a06771149a585625b` |
| 程序集边界 | `check-unity-assembly-boundaries.ps1`：19 assemblies、2 asmrefs，通过 | 不修改 asmdef |

原始结果由本轮 Unity runner 产生；Unity 退出会清理 worktree `Temp`，EditMode 回落 XML 位于本机 LocalLow 的实际 TestResults，PlayMode XML/log 在 runner 返回时按合同校验。日志中的复验断言包括 `E01_BuildTwiceReopenAndResolveSerializedConfigurationAndReferences`、`E02_PreviewIsPureDeterministicAndSnapshotsAreDetached`，每个 E01–E13 正常和反例测试均为 Passed；退出日志以 `CLEANUP: fixture unloaded; original active scene restored; no controller or live capacity remains.` 收束。

| 矩阵 | 本轮实际断言的关键前后值 / 反例 |
|---|---|
| E01 | Builder 连续构建并重开；唯一控制器、19 格、3 单位与四个正式 BuildSettings 不变。 |
| E02 | 默认显化并开域后行动 6→4、灵力 60→47、地表 3/7、环境 1/1；无自然支点为行动 3、灵力 45，燃料仍实付。 |
| E03 | 第 1 周期新增 `(-1,-1),(0,-1)`：灵力 39、预算 6、覆盖 5；第 2 周期新增 `(0,-2),(1,-2)`：灵力 31、预算 4、覆盖 7；第 3 周期仅维护：灵力 29、预算 4。 |
| E04 | 三阵营进入火格均 30→26、停留后 24；友方穿两格为 30→22，绕行仍 30；占位冲突拒绝且不扣血。 |
| E05 | 扑灭后地表登记即时减少且当周期可无伤穿过；下周期复燃才实付扩张 2＋余火 1／节点，余额不足时保持缺口、扩张为 0。 |
| E06–E07 | 水、隔绝、断燃料即时局部熄火且不能跳燃；传播预算 0、容量满、覆盖 7 与仅够一节点均分别限流，不发生部分扣费。 |
| E08–E10 | 显化／开域任一行动、灵力、燃料、容量或档案前置不足都原子拒绝；维护不足不续期；两个位置始终映射同一组合实例，退出释放两个承载与环境而不提前清火。 |
| E11–E12 | 中心攻击、主动关闭、源位退出、界位退出均停止新费用，仅留下已付普通火和最多两周期余火；战斗结束、禁用及场景卸载幂等清空容量、引用与尾效且不退款。 |
| E13 | 输入改变后旧预览以 `stale_preview` 拒绝，重新预览才提交；按钮与测试均调用同一控制器。 |

## 独立画面与恢复

本轮 PlayMode 在真实 Game view（1920×1080）重新取得 expansion 快照，随后复制为独立证据 [2026-10-14-jindan-fire-domain-independent.png](../evidence/2026-10-14-jindan-fire-domain-independent.png)。SHA-256 为 `d6b46ed42366affbe47b92b758135d3ef34923cd52836704c03ab4f61489afdb`。画面显示 cycle 1、行动 4、灵力 39、地表 5/7、环境 1/1、传播预算 6，以及两格本周期扩张；它与快照数字一致，且不代替上表的数值断言。

测试后再次比较 U 的十二项 Unity 资源与正式提交，仍完全一致；`EditorBuildSettings.asset` 无差异，工作树除本卡允许的文档、图片和生命周期文件外无持久化反例或 Unity 资源变化。该结果只证明隔离场景功能，不证明正式玩家接入、有魂敌人神魂通道或角色数值平衡；未以 BattleSim 代替场景复验。
