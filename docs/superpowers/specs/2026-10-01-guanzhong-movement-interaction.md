# 关中移动交互与行动阶段合同

## 范围与事实

本合同只冻结关中正式战斗中玩家一回合的移动、行动、输入和表现边界，供 `U-GZ-PLAYTEST-MOVE-RULE-01` 与 `U-GZ-PLAYTEST-MOVE-UI-01` 实施。它不改变六角拓扑、空间查询、攻击数值、CTB阈值、角色资源、相机或底座几何；36格四障碍和起点继续以 `2026-10-01-guanzhong-board-obstacles.md` 为准。

当前实现已经提供同一空间查询的 `FindReachable` 与 `QueryMovement`：后者返回完整 `MovementPath` 和移动成本，且已经拒绝目标占格、无效目的地、越界、障碍、边阻挡及超出剩余移力。`CombatActionResolver.ResolveMove` 会把位置直接改为终点；`CombatCommandService.Execute` 又会对全部非 `Wait` 命令调用 `TurnScheduler.ConsumeAction`，而 `EncounterCoordinator.ExecutePlayer` 把任意成功命令设为 `playerActed=true`。因此成功移动目前同时错误耗尽CT并结束玩家回合；展示投影也只传起点和终点，静态3D provider 随即把根节点传送到终点。

## 权威状态与结算

每个已就绪单位开始一个行动回合时拥有两项独立且都未消耗的权利：当回合初始移力和一次行动权。`CombatantSnapshot` 必须把构造时的移力保存为不可变 `MaximumMovePoints`，并把可变 `MovePoints` 作为本回合余额；`CombatSession` 在调度器交出一个存活单位时重置余额为该最大值。行动权只在攻击、术法、神通、道具/装备（后续已有命令接入时）或防御实际成功后消耗。移动每格按 `QueryMovement` 返回的 `MovementCost` 扣除余额；不得重算路径、距离或障碍规则。

玩家可按以下有限状态机操作：

| 状态 | 可成功提交 | 成功后 | 不允许 |
| --- | --- | --- | --- |
| `MoveThenAction`（开局） | 移动、一次行动、待机 | 移动留在本状态；行动转 `ActionThenMove`；待机立即结束 | 第二次行动、动画中再提交 |
| `ActionThenMove` | 剩余移力的移动、结束行动 | 移动留在本状态；结束行动结算CT | 第二次行动、待机替代已用行动 |
| `Finished` | 无 | CT已按动作冷却结算，转交调度器 | 任何玩家命令 |

防御、基础攻击、术法和神通均为“一次行动”；使用后仍允许把未用移力走完。`Wait` 是不移动且不执行行动的明确结束：只保留既有50% CT规则，不附加冷却。为使“行动后保留移动”可结束，既有命令边界新增显式 `RequestEndTurn`／`EndTurn`；它只在行动权已经消耗时合法，结算先前动作记录的冷却惩罚，不产生第二次行动或额外冷却。移力耗尽不会自动结束，以便玩家仍可执行一次行动；行动后移力为零可自动完成，也可由同一结算路径完成。死亡、会话结束或敌方回合前必须取消未完成的玩家输入。

一次合法移动必须原子地：以同一 `QueryMovement` 验证出完整路径和成本、扣除该成本、提交终点、按最后一段方向更新六向朝向，并返回原始完整路径。任何拒绝（空格、原地、障碍、占格、越界、边阻挡、超预算、非当前玩家、回合已结束或动作锁）都不得改位置、朝向、移力、CT或行动阶段。连续移动从已提交终点及剩余移力再次查询；不可把预览或鼠标悬停写入战斗状态。

`CombatSession` 是行动阶段和移力账本的唯一权威，`CombatCommandService` 是CT结算边界，`EncounterCoordinator` 只据权威阶段控制协程是否等待。`ICombatCommandHandler` 继续是表现到玩法的唯一跨模块命令面；输入和HUD不得自行推导合法格、扣移力或调用调度器。敌方仍由既有 `CombatLegalActionService` 和策略消费行动；本卡不改变其策略。

## 输入、预览与表现

移动入口仅在当前玩家处于可移动阶段且不在演出锁中启用。点击移动按钮进入选格，不提交命令；可达高光只枚举与将要提交使用相同 `FindReachable(actor.Position, remainingMovePoints, occupied)` 的结果。悬停可显示从同一查询取得的候选路径和成本；点击可走格只把该目的地设为待确认，确认才调用 `RequestMove`。取消、点击空处、点击当前格、点击障碍/敌人/不可达格均清除预览或保持选格，绝不提交。

EventSystem 指针位于UI时，格子拾取不运行；动作演出期间移动入口、确认和取消均锁定。相机平移后每次拾取使用当前相机和棋盘坐标，不能缓存旧屏幕坐标。动作栏继续通过 `CombatCommandInput` 发送已有攻击/待机命令，并新增结束行动控件；每次HUD快照以权威阶段决定移动、行动和结束控件是否可用。

一次成功移动的表现事件携带完整 `MovementPath`、起点、终点和最终朝向。`Static3DCombatUnitPresentationProvider` 按相邻路径段顺序移动根节点：每段朝向该段方向，按固定、可量化的格/秒速度插值，起止格准确，末段停在权威终点。根节点移动时现有子物体（body和底座）随行；不得用瞬移、缩放补偿、新骨骼或新的寻路规则伪装动画。演出锁在完整路径结束才释放；过期、重复或非相邻路径事件安全拒绝，不覆盖新会话或已清理单位。

## 实施切片与验证

`U-GZ-PLAYTEST-MOVE-RULE-01` 只实现权威阶段、`EndTurn`、移力扣除、完整路径事件数据和协调器回合守卫。它修改 `CombatSession`、`CombatantSnapshot`、`CombatCommand`、`CombatActionResult`、`CombatActionResolver`、`CombatCommandService`、`CombatLegalActionService`、`EncounterCoordinator`、`ICombatCommandHandler`、`CombatPresentationContracts` 及其直接运行时测试；不接输入、场景或动画。

`U-GZ-PLAYTEST-MOVE-UI-01` 在前卡、相机卡和底座落地卡均完成后，才接动作栏、HUD、格子拾取、高光、预览、确认/取消、`CombatMovementView`、静态3D路径动画、Installer、Builder、Adventure场景和PlayMode验证。它消费权威可达/路径事件，不重写规则。底座卡尚未明确“上抬0.25”的几何参照，故UI卡继续阻塞，不能以本合同猜测补偿。

规则测试至少覆盖不同成本、障碍/占格/边界、连续移动、移力耗尽、移动后攻击、攻击后移动、显式结束、待机、取消、重复命令、死亡与敌方行动。UI PlayMode在正式36格四障碍场地验证高光集合与权威查询一致、绕行、超预算拒绝、UI抢占、相机平移后拾取、逐段运动、最终位置/朝向/尺度/底座随行和运行时Game View。数值公式不变，本合同不运行BattleSim。

## 非目标与停止条件

不新增移动模式选择、全局UI框架、骨骼/行走动画、地形例外、强制位移规则或其他地区。若实现需要改变地图合同、空间查询所有者、CTB规则、攻击数值、底座几何含义，或无法取得正式PlayMode/Game View证据，应停止并回到相应有界任务，不以输入层补丁替代权威结算。
