# N-GROUP-02C0：每行动结算候选契约

状态：已锁定。本文为 N-GROUP-02C 的唯一结算候选契约；它不实现候选排序、范围伤害、协同走位或完整团队 AI。

## 目的与边界

N-GROUP-DEC-04 要在同一行动的候选之间依次比较：已证实可击杀敌人数、合法命中敌人数、主要目标当前 HP 和既有输入顺序。本契约只定义这些候选由谁生产、哪些事实在选择时可用，以及缺失结算证据的确定语义。

选择器只能消费行动生产者已经解析并显式交付的事实。它不得通过命中集合、攻击力、目标 HP 或局部伤害公式推演击杀，也不得调用或复制实际伤害结算。

## 现有所有者与形成时点

| 事实 | 唯一生产者 | 形成时点 | 选择期可观察性 |
|---|---|---|---|
| 技能、攻击与范围声明输入 | GameData 的 AttackProfile、ArtConfig、DivineConfig 和 AreaTargetingConfig | 战斗开始前的只读配置 | 可读，但不是结算结果 |
| 主要目标稳定 ID、输入顺序、行动位置与合法命中集合 | Combat.ResolveGroupActionPlan 及 Simulate2v2Detailed 的 plans/candidates 装配 | 本行动选择前，空间合法性已解析后 | 可读 |
| 原始伤害、格挡、闪避、暴击及随机功法后的实际 HP 变化 | Simulate2v2Detailed 的真实执行段；其防御判定只经 ApplyDefenses | SelectGroupTarget 已选定行动和主要目标后 | 不可作为同一行动的选择输入 |
| 已解析击杀集合与结算证据状态 | 同一行动的执行生产者 | 真实受击目标的 HP 和后续效果均已写入后 | 仅完成行动的观察/记录可读；当前选择期没有此事实 |

当前调用链是：

1. ResolveGroupActionPlan 为每个敌方主要目标解析移动位置与 AreaTargetingResult。
2. Simulate2v2Detailed 在建立 plans 和 GroupTargetCandidate 时取得主要目标索引、合法性和 AreaTargetingResult.HitTargetIndexes。
3. SelectGroupTarget 消费这些选择期事实并确定主要目标。
4. Simulate2v2Detailed 才依次调用 Dmg、ApplyDefenses、受击功法效果并写回 UnitState.HP。

ApplyDefenses 对格挡、闪避和暴击读取全局 RNG；随后不真自虚等效果也可能读取 RNG。因而第 4 步不得在第 2 步或第 3 步复制、预演或提前执行。当前范围命中索引仅是空间合法性结果；真实执行仍只对已选主要目标写入伤害，不能把该索引集合当作已结算多目标结果。

## 候选模型

每个行动生产者向选择器提供一个不可变的 GroupActionSettlementCandidate 快照。候选必须绑定到一个确定的行动实例；候选输入顺序不是从主要目标 ID 反推，也不得在选择器内重新排序或合并。

| 字段 | 语义与来源 |
|---|---|
| action instance | 当前回合与行动者的观察身份，仅用于溯源，不参与 N-GROUP-02C 的四层排序。 |
| primary target stable ID | 本行动的主要目标。当前 2v2 为 chars/units 输入数组中的稳定索引。 |
| legal hit target stable IDs | 空间、阵营和状态均合法的命中目标集合。非范围行动为已证实合法的主要目标单元素集合；范围行动来自 AreaTargetingResult.HitTargetIndexes。去重后按稳定输入顺序升序保存。 |
| candidate input order | 本候选进入同一行动比较集的固定零基序号。当前单目标候选由目标输入顺序提供；未来若同一主要目标存在不同的已解析行动/命中方案，必须各保留一个顺序，不能按主要目标折叠。 |
| settlement evidence | 严格二选一的 Resolved 或 Unavailable 证据变体，见下一节。 |

主要目标稳定 ID、合法命中集合和输入顺序都由行动生产者写入。选择器只读取它们；它不得重新调用空间查询来修正集合，也不得用候选外的信息覆盖输入顺序。

## 结算证据变体

### Resolved

Resolved 只可在真实行动已经执行并写回所有实际受影响目标后产生。它携带 resolved kill target stable IDs：

- 集合中的每个 ID 都必须是本行动实际结算后死亡的目标，按稳定输入顺序升序且无重复。
- 空集合是有效值，表示此行动已经完成并确认没有击杀任何目标。
- 集合不能由合法命中、原始 Dmg 结果、目标当前 HP 或未执行的功法效果推导。

### Unavailable

Unavailable 表示行动生产者尚未形成供当前选择使用的已解析击杀事实。它必须携带稳定原因 settlement_evidence_unavailable，且不携带空的已解析击杀集合。

Unavailable 在 N-GROUP-02C 的第一排序层贡献击杀证据数 0；该 0 仅表示“没有可用的已解析击杀证据”，绝不表示该行动实际无法击杀、实际结算为零伤害或已确认零击杀。选择观测必须保留 settlement_evidence_unavailable，不能把它改写为 Resolved 的空集合。

当前 Simulate2v2Detailed 的候选装配发生在真实伤害之前，所以本行动的所有选择期候选都必须是 Unavailable。真实执行结束后可以为已执行行动产出 Resolved 观察记录，但该记录不能回流改变刚刚完成的选择，也不能改变 RNG 消耗顺序。

## N-GROUP-02C 的消费规则

N-GROUP-02C 只可按下列方式读取本契约：

1. 若 settlement evidence 为 Resolved，第一层使用 resolved kill target stable IDs 的计数。
2. 若为 Unavailable，第一层使用证据数 0，并输出 settlement_evidence_unavailable。
3. 第二层始终使用 legal hit target stable IDs 的计数；它不能被当成击杀数。
4. 第三、四层仍分别使用主要目标当前 HP 和 candidate input order。

这只定义确定性消费语义，不授权 N-GROUP-02C 新增权重、随机评分、队友保护、站位安全、环境/冷却权重或协同走位。

## 必须保留的样例

| 场景 | 主要目标 | 合法命中 | 输入顺序 | 结算证据 | 第一层可用值 |
|---|---:|---|---:|---|---:|
| 已确认一名击杀 | 2 | [2, 3] | 0 | Resolved，击杀 [2] | 1 |
| 已确认零击杀 | 2 | [2] | 1 | Resolved，击杀 [] | 0，且已确认零击杀 |
| 选择期未形成结算 | 2 | [2, 3] | 2 | Unavailable，原因 settlement_evidence_unavailable | 0，仅缺少证据 |
| 相同主要目标、不同命中集合 A | 2 | [2] | 3 | Unavailable | 0 |
| 相同主要目标、不同命中集合 B | 2 | [2, 3] | 4 | Unavailable | 0 |

最后两行证明候选不得只以主要目标作键；第二层仍必须观察各自的合法命中集合。所有样例都不得调用 RNG，且不会改变真实行动执行。

## 实施与验证边界

后续将候选落入 Combat 或 GameData 时，必须保持上述字段的唯一生产者和时点。新增观察字段应能区分 Resolved 空集合与 Unavailable，且能输出 settlement_evidence_unavailable。测试至少覆盖本文件五个样例，并证明 Unavailable 路径不调用 ApplyDefenses、不读取 RNG、不会写入 UnitState 或改变行动执行结果。

本契约不修改现有 BattleSim 行为，也不声称任何伤害、胜率或平衡结论。
