# 预防型错误经验记忆系统实施规划

日期：2026-08-25

父项：`M-EXP-PREFLIGHT-01`

唯一规格：`docs/superpowers/specs/2026-08-24-preventive-experience-memory-system-design.md`

批准基线提交：`ccb1127bcd78d6bb192b4527a449acda41cfc782`

每日候选采集修订提交：`4bfc261d18fc609422c398dce1202948350d7e58`

规划状态：每日候选修订与 25 张原子子卡的完整任务图已落库；基础工具已于 2026-08-26 经 Codex 复审通过并归档，10 张种子核验卡均已达到真实终态，其中 8 张晋级 active、两张因证据不足完成为不晋级；只读试运行已解锁为 ready，其后续尚未实施

## 1. 规划结论

`M-EXP-PREFLIGHT-01` 是不可调度汇总父项，不进入 ready 队列。唯一关闭入口是 `M-EXP-PREFLIGHT-CLOSE-01`；关闭卡在全部已知阶段出口满足后，同一管理提交归档父项和自身。系统实施由 25 张原子子卡组成，严格按以下依赖推进：

```text
M-EXP-PREFLIGHT-01A 基础工具（基础阶段唯一叶子；已完成并归档）
  └─ 10 张独立种子核验卡
       └─ M-EXP-PREFLIGHT-TRIAL-01 冻结样本只读试运行
            └─ M-EXP-TASK-SCHEMA2-01 schema 1/2 兼容检查
                 └─ M-EXP-L0-CONTRACT-01 L0/runnable/强制合同
                      ├─ M-EXP-HOURLY-PREFLIGHT-01 小时双 owner 接入
                      └─ M-EXP-DAILY-COLLECTOR-01 每日确定性候选收集器
                           └─（两者同时完成）M-EXP-READY-SCHEMA2-ACTIVATE-01 原子激活
                                └─ M-EXP-OBSERVE-20-01 20 任务观察
                                     ├─ 3 张独立门禁卡
                                     └─ 3 张非技术域开放决策卡
                                          └─ M-EXP-PREFLIGHT-CLOSE-01
                                               └─ 关闭 M-EXP-PREFLIGHT-01
```

不存在覆盖全系统的执行卡。小时 worker 不拆卡；每日收集器只机械建立“一候选一整理卡”，不做语义拆分。当前 M-EXP 图没有 ready 叶子，现有 ready 队列中的其他任务保持原相对顺序；本轮不领取任何一项。

## 2. 完整任务清单与依赖

| ID | 单一结果 | route / owner | priority | domain / stage | blockedBy |
|---|---|---|---|---|---|
| `M-EXP-PREFLIGHT-01A` | 空风险索引、经验卡模板、只读 matcher 与 fixture 测试 | `external_execute / deepseek` | P1 | automation / implementation | — |
| `M-EXP-SEED-PWSH7-01` | 晋级 `EXP-AUTO-001` | `codex_execute / codex` | P1 | automation / implementation | 01A |
| `M-EXP-SEED-BS-BUILD-RUN-01` | 晋级 `EXP-BS-001` | `codex_execute / codex` | P1 | battlesim / implementation | 01A |
| `M-EXP-SEED-BS-SYMMETRY-01` | 晋级 `EXP-BS-003` | `codex_execute / codex` | P1 | battlesim / implementation | 01A |
| `M-EXP-SEED-BS-RESIST-KEYS-01` | 晋级 `EXP-BS-004` | `codex_execute / codex` | P1 | battlesim / implementation | 01A |
| `M-EXP-SEED-BS-TIMEOUT-01` | 晋级 `EXP-BS-005` | `codex_execute / codex` | P1 | battlesim / implementation | 01A |
| `M-EXP-SEED-UNITY-SCENE-BUILD-01` | 晋级 `EXP-UNITY-001` | `codex_execute / codex` | P1 | unity / implementation | 01A |
| `M-EXP-SEED-UNITY-ASMDEF-01` | 晋级 `EXP-UNITY-002` | `codex_execute / codex` | P1 | unity / implementation | 01A |
| `M-EXP-SEED-UNITY-META-01` | 独立核验是否晋级 `EXP-UNITY-003` | `codex_execute / codex` | P1 | unity / implementation | 01A |
| `M-EXP-SEED-UNITY-NAMESPACE-01` | 独立核验是否晋级 `EXP-UNITY-004` | `codex_execute / codex` | P1 | unity / implementation | 01A |
| `M-EXP-SEED-MGMT-WORKTREE-01` | 晋级 `EXP-MGMT-001` | `codex_execute / codex` | P1 | management / implementation | 01A |
| `M-EXP-PREFLIGHT-TRIAL-01` | 10 个冻结样本的命中、误报、字符与 token 代理报告 | `codex_execute / codex` | P1 | management / verification | 10 张种子核验卡达到真实终态且 active 为 8～12 |
| `M-EXP-TASK-SCHEMA2-01` | 检查器接受 schema 1/2 并校验 schema 2 投影，仍允许 schema 1 ready | `external_execute / deepseek` | P1 | management / implementation | TRIAL |
| `M-EXP-L0-CONTRACT-01` | 更新 AGENTS、状态规则、自动规则的写前预检与收尾候选稳定合同 | `codex_execute / codex` | P1 | management / design | SCHEMA2 |
| `M-EXP-HOURLY-PREFLIGHT-01` | 共享入口产生一次预检并传给 Codex/DeepSeek wrapper | `external_execute / deepseek` | P1 | automation / implementation | L0 |
| `M-EXP-DAILY-COLLECTOR-01` | 每日确定性收集 pending、逐候选机械制卡、同提交回链与 00:10 薄触发 | `codex_execute / codex` | P1 | automation / implementation | L0 |
| `M-EXP-READY-SCHEMA2-ACTIVATE-01` | 同一提交迁移全部当时 ready 卡并拒绝 schema 1 ready | `external_execute / deepseek` | P1 | management / migration | HOURLY、DAILY-COLLECTOR |
| `M-EXP-OBSERVE-20-01` | 激活后前 20 个相关任务及候选收集／整理成本的 continue/stop 报告 | `codex_execute / codex` | P2 | management / verification | ACTIVATE |
| `M-EXP-GATE-PWSH7-01` | `EXP-AUTO-001` 绑定现有 PowerShell 7 gate | `external_execute / deepseek` | P2 | automation / implementation | OBSERVE |
| `M-EXP-GATE-BS-TIMEOUT-01` | `EXP-BS-005` 绑定 BattleSim 顺序超时 self-test gate | `external_execute / deepseek` | P2 | battlesim / implementation | OBSERVE |
| `M-EXP-GATE-UNITY-ASMDEF-01` | 为程序集检查器补正反 fixture 并升级 `EXP-UNITY-002` | `external_execute / deepseek` | P2 | unity / implementation | OBSERVE |
| `M-EXP-EXT-DESIGN-01` | 设计经验域 open/hold/stop 决策 | `codex_execute / codex` | P2 | management / decision | OBSERVE |
| `M-EXP-EXT-CONTENT-01` | 内容经验域 open/hold/stop 决策 | `codex_execute / codex` | P2 | content / decision | OBSERVE |
| `M-EXP-EXT-NUMERIC-01` | 数值经验域 open/hold/stop 决策 | `codex_execute / codex` | P2 | battlesim / decision | OBSERVE |
| `M-EXP-PREFLIGHT-CLOSE-01` | 验证全部出口并归档自身与父项 | `codex_execute / codex` | P2 | management / verification | 3 gate + 3 extension |

每张卡的完整 `expectedPaths`、直接事实源、精确必查范围、实施范围、禁止项、验证、完成条件与停止条件以 `开发管理/任务卡/<ID>.txt` 为唯一事实。DeepSeek 卡均是已冻结、可机器验收的实现切片，正式结果必须转同 ID 的 `codex_review/codex/ready` 并由 Codex 独立复审；没有因任务困难改派主责。每日 collector 同时需要 Codex app automation 的创建与 view 签收、共享集成控制和外部配置写入，因此按现行职责固定 `codex_execute/codex`；它不是按实现难度改派，也不得被拆成新的 runtime 或小时 owner 分支。

## 3. 种子选择与证据边界

首轮选择 10 个候选，覆盖 PowerShell、BattleSim、Unity 和管理工作流。选择只使用 `开发管理/开发-技术经验.txt` 的直接章节、当前代码/测试/规则和已点名提交，不批量扫描归档。

| 经验 ID | 风险 | 直接证据 | 初始级别 | 特殊停止条件 |
|---|---|---|---|---|
| `EXP-AUTO-001` | 非 canonical PowerShell 入口 | PowerShell 7 规则、checker 正反 fixture | must_read | 当前 checker/规则冲突 |
| `EXP-BS-001` | 修改后未先 build 再 run | AGENTS、技术经验、当前 csproj/入口 | must_read | 当前 build/run 失败 |
| `EXP-BS-003` | 战斗机制只接入单侧 | `Combat.Simulate` 双侧分支、自测 | must_read | 双侧结构已消失或触发过宽 |
| `EXP-BS-004` | 1v1/2v2 抗性键不一致 | Character 初始化、两条 Combat 消费链 | must_read | 已改为强类型所有者 |
| `EXP-BS-005` | 超时默认偏向 A/左侧 | DuelTurnLimit 与交换顺序自测 | must_read | 当前自测失败或所有者变化 |
| `EXP-UNITY-001` | 手写场景 YAML | SceneBuildSupport、四类 builder、场景测试 | must_read | 正式场景不再由 builder 管理 |
| `EXP-UNITY-002` | 手改生成 csproj/误判 asmdef | asmdef/asmref、程序集 checker | must_read | Assembly-CSharp 恢复或所有者变化 |
| `EXP-UNITY-003` | 单行 `.meta` 不能导入 | 未审核章节仅作线索；必须核验 `ab4f882` 和当前 importer | must_read | 直接根因、边界或失效条件任一不能证明 |
| `EXP-UNITY-004` | namespace 与引擎类型同名 | CS0118 条目、`git log -S` 直接提交、当前 Spatial 所有者 | must_read | 找不到直接因果提交 |
| `EXP-MGMT-001` | 共享工作区/无锁集成覆盖人工改动 | AGENTS、AI 协作、自动规则、集成锁脚本/测试 | must_read + explicit_only | 规则与脚本冲突 |

`EXP-UNITY-003` 与 `EXP-UNITY-004` 不享有数量豁免，也不因证据不足伪造 active。若任务卡批准的直接事实源已经穷尽，核验卡以“已核验、不晋级”完成归档，不写正式卡或索引项，并在同一状态切片移除试运行卡中的该前置；仍有具名、可取得且尚未核验的来源时才保持 blocked，且 `stateReason` 必须点名来源。不晋级理由记录在任务卡正文／`stateReason` 与完成归档，不增加 schema 字段。全部 10 张种子核验卡达到真实终态且 active 仍为 8～12 条时才可开始试运行。

## 4. 冻结试运行样本与阈值

试运行使用批准提交 `ccb1127b` 中以下 10 张 completed 任务卡：

1. `A-AUTOMATION-FUYUAN-INPUT-MATERIALIZATION-01`
2. `U-URP-PREFLIGHT-01`
3. `U-URP-MIGRATE-01`
4. `U-ARCH-REBUILD-01D-R1`
5. `U-CHAR-2D-TACTICAL-PROTO-01`
6. `N-SUPPRESS-01A`
7. `N-SEAT-01A`
8. `N-GROUP-02C`
9. `C-KB-IDX-BASE-01`
10. `D-CHAR-STATIC3D-MOTION-REFERENCE-01`

以 `git show ccb1127b:开发管理/任务归档/<ID>.txt` 读取并在项目外临时目录物化，源归档始终只读。最后两项提供明确的内容/设计低相关对照；前三类覆盖 automation、unity、battlesim 的路径和符号。

试运行通过必须同时满足：

- active 种子为 8～12；
- 每个普通原子样本 `must_read <= 3`；
- 每样本全部必读 `## 开工前` 正文合计 `<= 600` Unicode 字符；
- 高置信误报率 `< 15%`，定义为“被 Codex 判为无关的 must_read/gate 命中数 ÷ 全部 must_read/gate 命中数”；分母为 0 时报告 0/0，不隐藏；
- 平均 token 代理 `<= 1000`；代理固定为 `ceil(返回短 JSON UTF-8 字节数 / 4)`，只作跨模型成本观察，不宣称精确 tokenizer；
- 10 次回放无未解释 matcher 失败，不把失败伪装为零命中。

任一条件失败，TRIAL 保持 blocked，schema 及后续全部不解锁。

## 5. 分阶段入口与出口

### 阶段 A：基础工具

- 入口：修订规格与当前 schema/owner 无冲突；01A 是基础阶段唯一执行叶子。
- 出口：空索引、模板、只读 matcher、10 类 fixture 测试通过；没有真实经验。
- 当前事实：正式提交 `bbae65133a1ff791a86b31f37e99a111d4679110` 已经 Codex 独立复审通过并归档；基础阶段出口满足。

### 阶段 B：种子经验

- 入口：01A 经 Codex 复审归档。
- 出口：每条独立卡分别形成“证据充分并 active”或“批准事实源已穷尽、已核验但不晋级”的真实终态；后一种结果完成归档但不创建经验卡或索引项。只有仍有具名可取得来源时保持 blocked。
- 失败：全部种子达到真实终态后 active 少于 8 条时，TRIAL 转 `pending_decision`，由用户选择建立有界补充种子规划或终止首期 initiative；不得长期 blocked，也不得把已完成核验的卡退回 blocked。

### 阶段 C：只读试运行

- 入口：10 张种子卡终态完成且 active 数仍在 8～12。
- 出口：冻结报告满足全部硬阈值。
- 失败：停止 schema/L0/自动化路径，不调整样本或上限。

### 阶段 D：schema 支持

- 入口：TRIAL 通过。
- 出口：schema 1/2 均合法，schema 2 投影实时校验，schema 1 ready 暂时仍合法。
- 失败：不修改真实任务卡。

### 阶段 E：L0 规则

- 入口：schema 支持经复审。
- 出口：AGENTS、状态规则、自动规则对手动、runnable、共享 owner、DeepSeek provisional、Codex 复审／自产 pending、失败关闭和无额外模型调用无矛盾。
- 失败：不修改小时脚本，不建立每日 collector 或薄触发。

### 阶段 F：小时接入与每日收集两个独立切片

- 共同入口：L0 完成；schema 支持已由 L0 的具名前置保证，不为两个下游重复建立旁路依赖。
- 小时出口：共享入口在 owner worktree 中只运行一次预检；Codex/DeepSeek 首条提示收到同一绑定结果；queue maintenance 不接入；失败不启动模型。
- 每日出口：香港时间 `00:10` 的唯一薄触发只调用确定性收集入口；pending 逐条生成独立整理卡并在同提交 collected／回链；零候选无写入，任一失败整批无提交且下一日自然补收。
- 失败：小时侧沿用 attention_required；每日侧保持来源 pending。两侧均不新增 schema 5 状态、第二 runtime／索引、重试层或 owner 专属分支。

### 阶段 G：ready 激活迁移

- 入口：小时双 owner 集成经 Codex 复审，且每日收集器、测试和唯一薄触发均已完成验证。
- 出口：同一提交升级全部当时 ready 卡并开始拒绝 schema 1 ready。
- 特殊入口检查：执行时任何 ready 卡路径不在激活卡冻结的完整当前已知路径上界中，立即停止并重新冻结授权；不得使用通配。

### 阶段 H：20 任务观察

- 入口：激活归档后，按完成提交时间收集前 20 个符合卡内固定规则的相关任务。
- 出口：`continue` 需要保持原试运行阈值、无持续 matcher 阻断、无持续重复制卡／收集失败、整理维护成本不高于避免的返工，并至少有一个可观察预防价值案例；否则 `stop`。
- stop 行为：六张门禁/扩域卡以“观察未授权实施”归档，直接进入关闭，不实施它们。

### 阶段 I：独立门禁与扩域

- 入口：OBSERVE=`continue`。
- 出口：三个已知 gate 各自通过正反 fixture 和 Codex 复审；设计、内容、数值各自形成 open/hold/stop 决策。
- 域 open 只批准新的独立 initiative；当前父项不顺带创建域经验。

### 阶段 J：父项关闭

- 入口：三 gate + 三 domain 出口完成，或 OBSERVE=`stop` 已真实归档六项未授权实施。
- 出口：关闭卡验证最新系统行为，同一提交归档自身与父项。

## 6. 候选生命周期与自动化所有权

### 6.1 小时预检结果流

小时接入只允许一条数据流：

```text
owner worktree + taskId
  -> invoke-hourly-owner.ps1 调用 matcher 并验证 schema 2 投影
  -> StateRoot/preflight-results/<runId>.json（ACL 私有、绑定 digest）
  -> hourly-owner-adapter.ps1 形成非 queue maintenance 必填参数
  -> Codex/DeepSeek wrapper 启动模型前再次验证路径/runId/taskId/digest
  -> 首条责任提示直接包含 notice、must_read 正文和 gate 指针
```

adapter/wrapper 不决定是否预检、不重新匹配、不读取完整索引。任何 `experience_preflight_*` 失败在模型启动前进入现有 `attention_required`；不伪造零命中、不重试、不加 runtime 字段。

### 6.2 任务收尾候选生命周期

```text
DeepSeek 执行完成
  -> 同一任务卡 provisional
  -> Codex 复审删除／修正／保留
  -> 保留者在完成归档中 pending

Codex 自有任务完成
  -> 同一任务卡直接 pending

每日确定性收集
  -> 每条 pending 建立一张独立整理卡
  -> 同一提交将来源改为 collected 并回填 promotionTaskId
  -> 整理卡唯一裁决 promoted / duplicate / rejected
```

原任务的候选检查只使用本轮已经掌握的事实、diff 与验证，不增加模型调用；没有候选时不写空章节。原任务不查重、不分配正式经验 ID、不建整理卡、不改风险索引。候选状态与整理任务回链是唯一防重事实，不建立每日候选正文或第二候选索引。

候选 `经验领域` 保留原值；为遵守现行任务卡 domain 与经验 ID 枚举，机械投影为 `design -> management/MGMT`、`numeric -> battlesim/BS`，其他领域按规格既有映射。`design/content/numeric` 候选可以收集，但整理卡分别 blockedBy 对应域开放决策，开放前不入队、不语义整理。

### 6.3 每日收集与薄触发所有权

```text
Codex app 唯一 cron（香港时间 00:10）
  -> 前台调用 invoke-daily-experience-candidate-collector.ps1
  -> 最新 master 的独立 worktree 只读扫描 pending
  -> 字段/digest/ID/路径/投影确定性校验
  -> 逐候选机械制卡 + 来源 collected 回链
  -> 一个路径限定提交
  -> 现有进程持有型集成锁 + fast-forward
  -> 单一结构化终态
```

薄触发不读候选正文、不写 Git、不判断价值，只等待并原样报告。collector 不占用 `runs.codex/deepseek`、不调用小时 owner／candidate wrapper、不增加恢复状态或重试；零候选不建 worktree 提交，任一批内失败不产生正式提交，来源保持 pending 供下一日补收。整理卡是唯一语义裁决点，一候选一任务。

`M-EXP-DAILY-COLLECTOR-01` 固定 `codex_execute/codex`，因为其单一结果包含 Codex app automation 的外部配置创建／view 签收和共享集成控制；DeepSeek 不具备 automation 管理授权。小时预检实现仍保持 `external_execute/deepseek`，两者都由 L0 解锁，互不调用或复制 runtime。

## 7. 回滚边界

| 切片 | 最小回滚单元 | 不允许的部分回滚 |
|---|---|---|
| 基础 | 01A 单一提交：空索引+模板+matcher+测试 | 保留无测试 matcher 或只有索引 |
| 单条种子 | 该 seed 的索引项+经验卡+状态提交 | 只删经验卡而留 active 索引，或反之 |
| 试运行 | 报告与任务状态 | 用改索引代替回滚报告结论 |
| schema 兼容 | checker+tests | 只保留 schema 2 解析但无投影校验 |
| L0 | 三份规则中的写前预检＋收尾候选同一合同提交 | 只更新 AGENTS、只更新自动规则，或保留预检而回退 provisional/pending 责任 |
| 小时接入 | owner+adapter+双 wrapper+全部测试 | 只接 Codex/DeepSeek 一侧；保留私有结果但移除验证 |
| 每日收集器 | 确定性入口+测试+唯一薄触发配置；项目提交和外部配置均回到未启用状态 | 只停触发而保留无所有者入口、只留触发而回退脚本、保留部分 collected 回链、引入补偿重试 |
| 激活 | checker 强制+当时全部 ready 卡的同一提交 | 只回退 checker 或只回退卡，产生混合状态 |
| 单个 gate | gate registry+经验 gateRefs+该 gate 测试 | 只留 gateRefs 或无正反 fixture |
| 域决策 | 单份 assessment+任务状态 | 决策文档 open 但在本卡顺带实现经验 |

任何回滚都不得删除已影响任务行为的稳定经验 ID；已发布经验按 `review_required/retired/supersededBy` 合同处理。已正式 collected 的来源与整理卡必须保持成对回链；不得把部分批次改回 pending 或靠第二索引补偿。collector 集成前失败没有正式提交，天然以原 pending 重跑，不存在补偿事务。

## 8. 验证矩阵

| 目标 | 必须证明 | 入口 |
|---|---|---|
| matcher schema/匹配 | 状态、枚举、三 triggerMode、路径归一化、限量、缺指针、只读 | `tools/test-get-experience-risk-preflight.ps1` |
| 任务卡投影 | schema 1/2、零命中、显式、旧投影、激活后 schema 1 ready 拒绝 | `tools/test-check-task-cards.ps1` |
| 试运行 | 10 个冻结 digest、误报、字符、token 代理、无工作树写入 | TRIAL report + Git blob/diff |
| L0 合同 | 三份规则调用时点、权威、失败、范围变化、DeepSeek provisional、Codex 复审／自产 pending、无额外模型调用一致 | `tools/check-review-text.ps1` + 人工逐条对照 |
| 小时 owner | worktree 后/args 前、一次结果、ACL/digest、失败不启动模型 | `tools/test-hourly-experience-preflight.ps1` |
| adapter/wrappers | queue maintenance 例外、双 owner 参数/提示、越界/失配拒绝 | owner adapter + 两 wrapper tests |
| 每日 collector | 零／一／多 pending、旧 pending 补收、provisional/collected 跳过、字段/digest/ID 冲突、逐卡生成、同提交回链、重复运行、防主工作区冲突／锁占用、失败整批无提交 | `tools/test-daily-experience-candidate-collector.ps1` |
| 每日薄触发 | 唯一项目 automation、本地前台、香港时间每日一次 00:10、只调用确定性入口、失败才通知、集成前 PAUSED／完成后 ACTIVE | Codex app automation view + 零候选 dry run |
| 整理卡投影 | 一候选一张 `codex_execute/codex/P2`；技术域按 runnable 入队，design/content/numeric 保持对应域 blocked；既有 ready 相对顺序不变 | collector fixtures + `tools/check-task-cards.ps1` |
| 激活 | 同一 ready 集合全部 schema 2、非 ready 未迁移、立即拒绝 schema 1 ready | checker JSON + per-card matcher |
| PowerShell gate | canonical allow / noncanonical reject | `tools/test-check-pwsh-runtime.ps1` |
| BattleSim gate | build、equal/asymmetric/swapped timeout | Release build + `--no-build` run |
| Unity asmdef gate | allow topology / illegal reverse or missing target | new fixture test + production checker |
| 管理投影 | 父项不入队、新 collector 非 ready、激活同时依赖 hourly/collector、依赖无环、backlog/queue 精确 | `tools/check-task-cards.ps1` |
| 文本与提交 | UTF-8、审核文本、路径隔离、空白和 staged diff | whitespace、review-text、`git diff --cached --check` |

相关输入未变化时不重复同范围检查；DeepSeek 正式结果的 Codex 独立复审属于外部交接硬边界，不因已有 candidate 测试省略。

## 9. 父项关闭条件

`M-EXP-PREFLIGHT-CLOSE-01` 必须同时证明：

1. 父项从未进入 ready，全部实施都由原子子卡完成。
2. 基础 matcher 能对冻结路径/符号返回确定性、少量、可追溯结果，且不修改工作树/runtime。
3. 8～12 条 active 技术/工作流种子均有根因、适用、排除、失效和证据；证据不足者未为凑数 active。
4. L0 收尾合同已经生效：DeepSeek 只能写 provisional，Codex 复审保留时转 pending，Codex 自有完成任务直接 pending；候选只使用本轮已有事实，不增加模型调用，原任务不查重、不建正式经验／整理卡、不改风险索引。
5. 每日确定性收集器和香港时间 `00:10` 唯一薄触发已经实现并验证；pending 可逐条生成独立整理卡并在同一提交 collected／回链，零候选无写入，任一失败整批无提交且下一日自然补收。
6. 每条未来整理卡仍是独立语义任务；design/content/numeric 在对应域开放前保持 blocked，没有提前入队或形成正式经验。
7. 试运行和 20 任务观察都有可重算指标；观察同时覆盖候选产生量、pending→collected 延迟、重复制卡、收集失败与整理维护成本，原 continue/stop 阈值保持。
8. ready 卡已在一个提交中激活 schema 2，当前不存在 schema 1 ready；激活同时以小时接入和每日收集器完成为前置，历史 completed 和非 ready 迁移边界保持。
9. 手动 Codex/DeepSeek/Claude 与小时 Codex/DeepSeek 在写前不能绕过同一预检；queue maintenance 例外明确。
10. 零命中正常继续；非法索引、缺正文/门禁、过宽、冲突、范围扩大、matcher 失败和候选收集失败均按规格失败关闭。
11. 没有向量库、外部服务、第二索引／候选正文、长期 feature flag、新 schema 5 runtime 状态、重试层、小时 owner 分支或 owner 专属匹配分支。
12. OBSERVE=`continue` 时三个 gate 与三个域决策全部独立完成；OBSERVE=`stop` 时六项明确未授权实施并归档，不伪造门禁/扩域已完成。父卡与关闭卡同一提交归档，backlog/queue 无残留；任何后续域 open 进入新的独立 initiative。

## 10. 当前实施边界

- 基础工具已由正式提交 `bbae65133a1ff791a86b31f37e99a111d4679110` 完成 Codex 独立复审并归档。
- 10 张种子核验卡均已完成：8 张形成 8 条 active 经验；`M-EXP-SEED-UNITY-META-01` 与 `M-EXP-SEED-UNITY-NAMESPACE-01` 因批准的直接事实源穷尽而完成为不晋级，未创建经验卡或索引项。
- 尚未运行冻结试验或产生试运行／20 任务报告；尚未修改 `AGENTS.md`、任务 schema、自动工作流规则或小时脚本，任务收尾 provisional／pending 合同尚未生效。
- 尚未实现或运行 `M-EXP-DAILY-COLLECTOR-01`，未创建／启用每日候选 automation，未制任何整理卡或回链任何来源候选。
- 尚未迁移现有 ready 卡到 schema 2，未启用强制预检；尚未创建或激活 gate，也未开放设计、内容或数值经验域。

本管理切片已经实施修订过渡：规格与规划同步，两张“不晋级”种子卡归档，试运行卡的两个前置已移除并按 runnable 规则转为 ready，backlog 与队列同步完成。本切片没有运行试运行叶子；后续由独立 `M-EXP-PREFLIGHT-TRIAL-01` 执行切片处理冻结回放。
