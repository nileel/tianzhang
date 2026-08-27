# 预防型经验记忆系统冻结样本只读试运行报告

## 结论

- 结论：收窄后复跑通过。十张冻结样本共有 3 个高置信命中，3 个均与实际工作相关，无关命中率为 `0/3，0.0%`，低于批准规格的 `<15%` 阈值。
- active 数量、必读条数／字符数、token 代理成本和十次匹配器可靠性也全部通过；`M-EXP-PREFLIGHT-TRIAL-01` 可以完成归档，并解锁 `M-EXP-TASK-SCHEMA2-01`。
- 本轮只消费 `M-EXP-PREFLIGHT-NARROW-UNITY-01` 与 `M-EXP-PREFLIGHT-NARROW-BS-01` 已归档的索引／经验卡收窄结果；未修改匹配器、冻结样本、任务卡 schema、规则或自动化入口。

## 回放方法与冻结性

- 冻结提交：`ccb1127bcd78d6bb192b4527a449acda41cfc782`；样本集合与首次试运行完全相同。
- 对指定的十个 `开发管理/任务归档/<ID>.txt` 逐一执行 `git show <冻结提交>:<路径>`，将标准输出按字节写入项目外临时目录；临时目录只包含当前 `风险索引.json`、其引用的 L2 经验卡和当前冻结任务卡。
- 每个物化文本在匹配前后分别计算 SHA-256，并与只读匹配器返回的 `taskCardDigest` 比较；十项三方均逐字节一致。回放使用 `tools/get-experience-risk-preflight.ps1 -RepositoryRoot <临时目录> -TaskCardPath <归档路径>`，未执行 gate 命令。
- 十次运行完成后已删除精确临时目录；冻结归档、索引和工作树未被回放过程改写。
- 本次索引 digest：`d46db414950ffe1f9ab8baa482bf8b8bc872feeed571eed704edf551e02b2769`。

## 逐样本结果

| 冻结任务 | SHA-256 | must_read | notice | gates | 必读字符数 | 短 JSON UTF-8 字节 | token 代理 | 高置信命中判断 |
|---|---|---|---|---|---:|---:|---:|---|
| `A-AUTOMATION-FUYUAN-INPUT-MATERIALIZATION-01` | `e0a719d71e5ccca06dea2995a3e10d153da49f0c5ebc2b971612e17bdf9b131a` | 无 | 无 | 无 | 0 | 322 | 81 | 无命中 |
| `U-URP-PREFLIGHT-01` | `72dc64cbf63b6ab336a757e9a14ba8b444286597dbd0ec458be6dace22915a55` | 无 | 无 | 无 | 0 | 296 | 74 | 无命中 |
| `U-URP-MIGRATE-01` | `2aa06f20f05ffca99dd3b0c45b65cd9a14fc4433c00988ddfc134e642da13d0c` | 无 | 无 | 无 | 0 | 294 | 74 | 无命中；仅只读引用 Adventure Builder、未调用精确 Build／SaveScene 或场景重建动作，原 `EXP-UNITY-001` 误报已消失。 |
| `U-ARCH-REBUILD-01D-R1` | `ca75a04c7f6b9aad2ab883143c3d3c5c1c3f51ca6e4e48ceff342b0af55eece1` | `EXP-UNITY-002`（466 字，`开发管理/经验库/经验卡/EXP-UNITY-002.txt#开工前`） | 无 | 无 | 466 | 1178 | 295 | 相关：任务直接修改 `TianZhang.Features.Adventure.asmdef`，并要求核验程序集边界和直接引用。 |
| `U-CHAR-2D-TACTICAL-PROTO-01` | `1b7c37a5c96049a8cd149860fb7696ec9fb9008fa1c685f3b097dd870a753af5` | `EXP-UNITY-001`（316 字，`开发管理/经验库/经验卡/EXP-UNITY-001.txt#开工前`） | 无 | 无 | 316 | 1089 | 273 | 相关：隔离视觉矩阵修改 `AdventureScene.unity`，并以精确 `VisualBaselineBuilder` 保持结构和保存链。 |
| `N-SUPPRESS-01A` | `29582433ab865de1e110e0ce51d49ac4e3e2462999d78a3f295f1ecfa08af673` | 无 | 无 | 无 | 0 | 292 | 73 | 无命中 |
| `N-SEAT-01A` | `83026db6c71cc595cfce1e09dbdb3224760f8333af7d84e68d51aa98d39139a0` | 无 | 无 | 无 | 0 | 288 | 72 | 无命中 |
| `N-GROUP-02C` | `95d978216ba177bd47f1eb0910113493b7e4d91d82f0e7135aa2f19bc3b1f15e` | `EXP-BS-001`（193 字，`开发管理/经验库/经验卡/EXP-BS-001.txt#开工前`） | 无 | 无 | 193 | 820 | 205 | 相关：任务修改 BattleSim `Combat.cs` 且明确要求 Release build 后以 `--no-build` 运行；纯 `Simulate2v2Detailed` 不再命中 1v1 `EXP-BS-003`。 |
| `C-KB-IDX-BASE-01` | `448ab246bb337d31a99b52fa12ebeaf470ccef0c5d347ebc459b35db1f3c8091` | 无 | 无 | 无 | 0 | 294 | 74 | 无命中 |
| `D-CHAR-STATIC3D-MOTION-REFERENCE-01` | `da29fbff11bbdea8e7a3a52e927b8592c0d0db3ce5d4a11e8b1737edcbacc48c` | 无 | 无 | 无 | 0 | 313 | 79 | 无命中 |

## 汇总与阈值

| 指标 | 实测 | 阈值 | 结果 |
|---|---:|---:|---|
| active 种子数 | 8 | 8～12 | 通过 |
| 单样本 must_read 上限 | 1 | ≤3 | 通过 |
| 单样本必读正文最大字符数 | 466 | ≤600 | 通过 |
| 高置信无关命中率 | 0/3，0.0% | <15% | 通过 |
| 平均 token 代理 | 130.0 | ≤1000 | 通过 |
| 匹配器失败数 | 0/10 | 0 | 通过 |
| notice 与 gates | 全部为 0 | 仅记录 | 通过 |

短 JSON 字节数不含输出结尾换行，token 代理按每样本 `ceil(UTF-8 字节数 / 4)` 计算；十项 token 代理合计为 1300，平均为 130.0。它只用于跨模型成本观察，不是精确 tokenizer。

## 与首次试运行的差异

- 首次试运行为 5 个高置信命中、2 个无关，误报率 `40.0%`；本次为 3 个高置信命中、0 个无关，误报率 `0.0%`。
- `U-URP-MIGRATE-01` 不再因裸 `Builder` 命中 `EXP-UNITY-001`；既有相关正例 `U-CHAR-2D-TACTICAL-PROTO-01` 仍由精确 `VisualBaselineBuilder` 命中。
- `N-GROUP-02C` 不再因 `Simulate2v2Detailed` 子串命中 `EXP-BS-003`，但相关的 `EXP-BS-001` 保持命中。
- 样本 ID、冻结提交和十个 SHA-256 均未变化；差异只来自当前索引与两张 L2 经验卡的批准收窄。

## 后续边界

- 本报告全部阈值通过，允许归档 `M-EXP-PREFLIGHT-TRIAL-01`，并只解除 `M-EXP-TASK-SCHEMA2-01` 的该项前置。
- 后续 schema 1／2 支持仍由独立卡实施；本轮不修改检查器、规则、小时入口或任何真实 schema 1 ready 卡。
