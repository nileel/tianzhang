# 预防型经验记忆系统冻结样本只读试运行报告

## 结论

- 结论：停止并收窄后再试运行。本次十张冻结样本的所有硬边界、成本和匹配器可靠性均通过，但 5 个高置信命中中有 2 个与实际工作无关，误报率为 `40.0%`，达到并超过批准规格的 `<15%` 阈值。
- 因此不解锁 `M-EXP-TASK-SCHEMA2-01`，不修改风险索引、经验卡、任务卡 schema、规则或自动化入口。

## 回放方法与冻结性

- 冻结提交：`ccb1127bcd78d6bb192b4527a449acda41cfc782`。
- 对指定的十个 `开发管理/任务归档/<ID>.txt` 逐一执行 `git show <冻结提交>:<路径>`，在项目外的临时目录按字节物化；临时目录只包含当前 `风险索引.json`、其引用的 L2 经验卡和冻结任务卡。
- 每个物化文本同时记录 SHA-256 与只读匹配器返回的 `taskCardDigest`；两者逐项一致。回放使用 `tools/get-experience-risk-preflight.ps1 -TaskCardPath <归档路径>`，未执行 gate 命令，未改写归档或索引。
- 试运行前工作树干净；回放期间仅写入项目外临时目录。十个冻结 blob 在回放后以同一提交和 SHA-256 复核，均未变化。
- 当前索引 digest：`038d9e0dea99d3be21fdc78579b9f6cd7b02631e2f49af27a4b7962ba53fe00d`。

## 逐样本结果

| 冻结任务 | SHA-256 | must_read | notice | gates | 必读字符数 | 短 JSON UTF-8 字节 | token 代理 | 高置信命中判断 |
|---|---|---|---|---|---:|---:|---:|---|
| `A-AUTOMATION-FUYUAN-INPUT-MATERIALIZATION-01` | `e0a719d71e5ccca06dea2995a3e10d153da49f0c5ebc2b971612e17bdf9b131a` | 无 | 无 | 无 | 0 | 322 | 81 | 无命中 |
| `U-URP-PREFLIGHT-01` | `72dc64cbf63b6ab336a757e9a14ba8b444286597dbd0ec458be6dace22915a55` | 无 | 无 | 无 | 0 | 296 | 74 | 无命中 |
| `U-URP-MIGRATE-01` | `2aa06f20f05ffca99dd3b0c45b65cd9a14fc4433c00988ddfc134e642da13d0c` | `EXP-UNITY-001`（253 字，`开发管理/经验库/经验卡/EXP-UNITY-001.txt#开工前`） | 无 | 无 | 253 | 955 | 239 | 无关：URP Converter 迁移虽列出正式场景，并在只读资产闸门中提到 Adventure Builder，但不修改场景结构、不会调用 builder 的保存链；该命中由宽泛的 `Builder` 文本触发。 |
| `U-ARCH-REBUILD-01D-R1` | `ca75a04c7f6b9aad2ab883143c3d3c5c1c3f51ca6e4e48ceff342b0af55eece1` | `EXP-UNITY-002`（466 字，`开发管理/经验库/经验卡/EXP-UNITY-002.txt#开工前`） | 无 | 无 | 466 | 1178 | 295 | 相关：任务直接修改 `TianZhang.Features.Adventure.asmdef`，并要求核验程序集边界和直接引用。 |
| `U-CHAR-2D-TACTICAL-PROTO-01` | `1b7c37a5c96049a8cd149860fb7696ec9fb9008fa1c685f3b097dd870a753af5` | `EXP-UNITY-001`（253 字，`开发管理/经验库/经验卡/EXP-UNITY-001.txt#开工前`） | 无 | 无 | 253 | 966 | 242 | 相关：隔离视觉矩阵仍修改 `AdventureScene.unity`，并以 `AdventureSceneBuilder` 和 `VisualBaselineBuilder` 保持结构和保存链。 |
| `N-SUPPRESS-01A` | `29582433ab865de1e110e0ce51d49ac4e3e2462999d78a3f295f1ecfa08af673` | 无 | 无 | 无 | 0 | 292 | 73 | 无命中 |
| `N-SEAT-01A` | `83026db6c71cc595cfce1e09dbdb3224760f8333af7d84e68d51aa98d39139a0` | 无 | 无 | 无 | 0 | 288 | 72 | 无命中 |
| `N-GROUP-02C` | `95d978216ba177bd47f1eb0910113493b7e4d91d82f0e7135aa2f19bc3b1f15e` | `EXP-BS-001`（193 字，`开发管理/经验库/经验卡/EXP-BS-001.txt#开工前`）；`EXP-BS-003`（139 字，`开发管理/经验库/经验卡/EXP-BS-003.txt#开工前`） | 无 | 无 | 332 | 1259 | 315 | `EXP-BS-001` 相关：任务修改 BattleSim `Combat.cs` 且明确要求 Release build 后以 `--no-build` 运行。`EXP-BS-003` 无关：工作限定于 2v2 候选优先级，并不修改 1v1 `Combat.Simulate` 的 A/B 对称状态分支；命中来自 `Simulate2v2Detailed` 的宽泛子串。 |
| `C-KB-IDX-BASE-01` | `448ab246bb337d31a99b52fa12ebeaf470ccef0c5d347ebc459b35db1f3c8091` | 无 | 无 | 无 | 0 | 294 | 74 | 无命中 |
| `D-CHAR-STATIC3D-MOTION-REFERENCE-01` | `da29fbff11bbdea8e7a3a52e927b8592c0d0db3ce5d4a11e8b1737edcbacc48c` | 无 | 无 | 无 | 0 | 313 | 79 | 无命中 |

## 汇总与阈值

| 指标 | 实测 | 阈值 | 结果 |
|---|---:|---:|---|
| active 种子数 | 8 | 8～12 | 通过 |
| 单样本 must_read 上限 | 2 | ≤3 | 通过 |
| 单样本必读正文最大字符数 | 466 | ≤600 | 通过 |
| 高置信无关命中率 | 2/5，40.0% | <15% | 不通过 |
| 平均 token 代理 | 154.4 | ≤1000 | 通过 |
| 匹配器失败数 | 0/10 | 0 | 通过 |
| notice 与 gates | 全部为 0 | 仅记录 | 通过 |

短 JSON 字节数不含输出结尾换行，token 代理按每样本 `ceil(UTF-8 字节数 / 4)` 计算；它只用于跨模型成本观察，不是精确 tokenizer。

## 后续边界

- 先依据本报告收窄 `EXP-UNITY-001` 对仅提及 Builder 的 URP 迁移任务的触发条件，以及 `EXP-BS-003` 对 `Simulate2v2Detailed` 的子串触发条件；不得通过新增关键词权重、兼容分支、第二索引或样本改写掩盖本次失败。
- 收窄必须由后续独立任务重新核验索引和经验卡；完成前，本卡保持 `blocked`，`M-EXP-TASK-SCHEMA2-01` 保持其现有 blocker 和 schema 1 状态。
