# 每小时自动工作流编排层重建设计

## 1. 决策摘要

停止对现有自动工作流编排层继续打补丁。保留已经真实验证的飞书通信链路与 Git 写入安全底座，旁路重建控制器、状态机、任务选择、范围清单、标题、诊断、决策编排和自动化提示词。新旧控制器不得同时拥有写权限；新控制器通过离线测试、真实只读演练和人工确认后，才替换现有 `tzg-hourly-controller`。

本文件是后续新对话的权威设计入口。实施不得根据旧规格自行扩大或缩减范围。

## 2. 当前事实基线

### 2.1 仓库与自动化状态

- 记录日期：2026-07-17。
- 当前 `master` HEAD：`90754e9560596f09ae2790d0b831efa1cf705a77`。
- `tzg-hourly-controller`：`PAUSED`，每小时第 15 分钟调度，模型 `gpt-5.6-terra`，推理强度 `high`。
- 控制器本机状态：schema v8、`IDLE`、无租约、无 `pendingDecision`；最后错误为 `Workspace Check failed: baseline_changed`。
- 飞书桥接计划任务：`Running / CONNECTED / TEXT_REPLY_READY`，当前仅一个主目录实例。
- 工作区已有三项用户未跟踪内容，必须保持原样：远程附件目录中的图片，以及两份 2026-07-15 金丹规格文档。

### 2.2 触发重建的真实运行证据

- Codex 任务：`019f6c15-7640-7a22-a4a3-867439ad869a`。
- 控制器 runId：`b4c2ae2f-f094-4cdf-9b61-e34ab6a7166f`。
- 运行时间：2026-07-17 01:59–02:03（Asia/Hong_Kong）。
- 标题失败：提示词读取不存在的 `turn.thread_id`；实际存在并一致的是顶层 `threadId` 与 `x-codex-turn-metadata.thread_id`。
- 范围失败：TQ-057 的双倍率决定要求 CSV、导入器、`SpellData`、`CombatResolver`、测试与既有资产迁移，但模型登记的 `expectedPaths` 漏掉运行时代码、导入器、测试及多数迁移对象。
- 命令契约失败：`InspectCandidate` 只允许 `rg`、`rg --files`、`Get-Content`、`git status`、`git diff` 和任务卡检查，模型仍调用了 `Get-ChildItem`。
- 诊断失败：workspace guard 返回 `baseline_changed` 后，控制器丢弃了具体变化路径。
- 安全结果：workspace guard 阻止写入；项目文件未修改、无提交、状态回到 `IDLE`。

这些问题横跨提示词、语义范围、工具约束和协议诊断，不能再作为单点缺陷修补。

## 3. 必须保留的组件

### 3.1 飞书通信链路

以下内容按现有行为保留，只允许为适配新控制器增加窄接口，不重写内部协议：

- `tools/feishu-decision-bridge/`
- `tools/setup-feishu-decision-channel.ps1`
- `tools/install-feishu-decision-bridge.ps1`
- `tools/start-feishu-decision-bridge.ps1`
- `tools/private-path-acl.ps1`
- 飞书私有配置、配对身份、签名密钥、状态目录与计划任务

必须继续支持：完整方案正文、短按钮、卡片自定义输入、严格格式普通文本、自定义文本 1–1000 Unicode code point、安全字符校验、签名 inbox、首个有效回复胜出、重复幂等、冲突 quarantine 和单实例长连接。

### 3.2 Git 安全底座

以下组件作为黑盒安全原语保留；新控制器只能调用公开动作，不复制其内部逻辑：

- `tools/automation-workspace-guard.ps1`
- `tools/automation-finalize-commit.ps1`
- `tools/check-pending-whitespace.ps1`

现有 guard 在真实事故中正确阻止了不安全写入。重建不得弱化 baseline、路径隔离、恢复证据、`git commit --only`、禁止 stash/reset/checkout/clean 和路径外人工改动保护。

## 4. 必须重建的组件

以下内容不能作为新编排层实现基础，只可作为迁移事实和回归参照：

- `tools/automation-controller.ps1`
- `tools/automation-controller-state.ps1`
- `tools/automation-decision-status.ps1`
- 现有控制器测试中与旧状态机耦合的部分
- `开发管理/自动工作流控制器提示词.txt`
- `开发管理/自动工作流规则.txt` 中旧编排协议章节
- 自动化 memory 的旧运行策略

旧文件在切换完成前不得删除。新实现必须旁路建设、使用独立目录和独立本机状态文件；完成迁移后再归档或删除旧实现。

## 5. 不得丢失的业务状态

### 5.1 TQ-057 当前决策流

新控制器必须迁移并冻结以下已批准事实，不能重新询问、默认改选或只迁移字母：

1. `DEC-20260715-35ACB87E6C10`：选择 B。保留 11 份古修术法文档，并补齐 CSV、语言键和 Unity asset。
2. `DEC-20260715-75D7BA2AF210`：选择 A。为所有攻击术法增加明确的 `physicalDamageMultiplier` 与 `soulDamageMultiplier`，迁移现有单通道数据；实现影响至少包含 `Spells.csv`、`DataConfigImporter.cs`、`SpellData.cs`、`CombatResolver.cs`、相关测试和全部既有术法资产。
3. `realm_lianshen` 缺失语言键：已选择补齐语言键并保留有效引用。
4. 六部仍含有效低阶成长段的功法：只删除废止的 `realm_lianxu` 段并同步 asset，保留其他内容。
5. 仍引用已删除或未注册境界的无效数据：已批准删除相应 CSV 行、文档和 Unity asset。

新状态必须记录问题、完整选择正文、影响摘要、来源、解决时间和迁移来源；不得只保存 A/B 字母。provider、目标、tenant、配对身份、message/event 标识和证据哈希不进入项目文件或模型输出。

### 5.2 历史状态保全

- 旧 schema v8 状态文件先复制为只读备份，再执行一次性导入。
- 导入器必须幂等；同一旧状态重复导入得到完全相同的新状态。
- 导入成功前不修改旧状态；新控制器切换完成前旧状态保持可回滚。
- `开发管理/自动工作流状态.txt` 继续作为项目负责人可见摘要，但不作为机器状态源。

## 6. 新架构

### 6.1 分层

新编排层分为六个独立边界：

1. **启动器**：读取并校验模型与当前 Codex 任务 ID，只负责调用控制器 `Start`。
2. **任务注册表**：保存机器可读的任务身份、状态、主责、依赖、允许根目录、必读事实源和必跑检查。
3. **只读发现网关**：提供受限的搜索、列举、读取和已登记检查，不接受任意 Shell 字符串。
4. **工作清单验证器**：验证任务、决策覆盖、预期路径、逐路径意图、检查集合和 baseline。
5. **状态机与恢复器**：管理租约、阶段、恢复证据、决策等待和失败关闭。
6. **提交器与通信适配器**：分别调用现有 Git 安全底座和飞书桥接，不共享内部状态。

### 6.2 机器可读任务注册表

新增版本控制中的 JSON 注册表。每个可执行任务必须包含：

```json
{
  "taskId": "TQ-057",
  "title": "D-TRUST-02：清理现存数据矛盾",
  "status": "pending",
  "executor": "codex",
  "dependencies": ["TQ-056"],
  "requiredSources": ["开发管理/当前任务队列.txt"],
  "allowedRoots": ["src/Assets/DataConfig", "src/Assets/Data", "src/Assets/Scripts", "src/Assets/Tests", "开发管理"],
  "discoveryChecks": ["data-chain"],
  "requiredChecks": ["data-chain", "unity-editmode", "pending-whitespace", "cached-diff-check"],
  "completionEvidence": ["数据链路无未批准错误", "双倍率运行时和迁移测试通过"]
}
```

Markdown 任务队列继续供人阅读；检查脚本必须验证 JSON 与 Markdown 的任务 ID、状态、主责和依赖一致。控制器只从 JSON 选择任务，不解析 Markdown 表格推导协议枚举。

### 6.3 只读发现网关

控制器提供以下固定动作：

- `DiscoverRead`：读取注册表允许的项目相对文件，限制大小并拒绝符号链接逃逸。
- `DiscoverSearch`：在允许根目录内执行固定参数的 `rg` 搜索。
- `DiscoverList`：在允许根目录内列举项目相对文件。
- `DiscoverCheck`：只能运行注册表 `discoveryChecks` 中的检查 ID，由控制器映射到固定命令。

自动化提示词不得在发现阶段直接调用 Shell。所有发现动作写入本机 `discoveryLog`；`SubmitManifest` 必须证明必读事实源和 `discoveryChecks` 已经经过网关。最终 `requiredChecks` 只在授权修改完成后由控制器统一执行，相关输入未变化时不重复运行同一检查。未登记动作默认拒绝。

若模型绕过网关直接修改项目，后续 baseline CAS 必须失败并列出具体变化路径；控制器不得自动重拍基线掩盖变化。

### 6.4 结构化工作清单

模型完成发现后，以 ACL 受限的用户级 JSON 请求提交工作清单，不通过长 PowerShell 参数传递。清单固定包含：

- schemaVersion
- runId、taskId、实际模型、当前任务 ID
- 已读取 source 的路径与内容哈希
- 每个已解决 decisionId 的 `decisionCoverage`
- 完整 `expectedPaths`
- 每个路径的 `intendedChange`
- 所有最终必跑 `requiredChecks`
- 预期完成证据

`decisionCoverage` 必须把每个决定映射到一个或多个 `expectedPaths` 和具体实施说明。所有覆盖路径必须同时存在于 `expectedPaths`，所有 `expectedPaths` 必须落在任务注册表 `allowedRoots` 内。缺少任一已决策约束、必读事实源、必跑检查或逐路径意图时，控制器拒绝授权。

### 6.5 决策范围契约

新建决策时，每个选项除正文外必须同时保存机器可读的范围契约：

- affectedRoots
- requiredChecks
- migrationFacts
- compatibilityFacts

飞书卡片仍只展示人类可读正文。负责人回复后，控制器把所选选项正文和范围契约一起冻结到决策账本。自定义回复不能自动生成写权限；模型必须基于自定义正文提出新的工作清单，控制器在首次生产执行前要求负责人通过飞书确认该清单范围。

旧 v8 决策由迁移器补入一次性范围契约，事实源是本文件、`开发管理/自动工作流状态.txt`、原决定问题与影响摘要。

### 6.6 状态机

新状态使用独立 schema v1 和独立文件，阶段固定为：

```text
IDLE
  -> DISCOVERING
  -> AUTHORIZED
  -> MUTATING
  -> VERIFYING
  -> COMMITTED
  -> IDLE
```

决策分支固定为 `WAITING_DECISION` 和 `IMPLEMENTATION_PENDING`。每次状态迁移由控制器执行，不接受模型提交内部枚举。模型只能调用当前响应中的 `nextAction`。

状态结果始终返回稳定字段：`ok`、`action`、`runId`、`taskId`、`phase`、`nextAction`、`errorCode`、`changedPaths`、`requiredSources`、`requiredChecks` 和脱敏 `decisionConstraints`。

### 6.7 标题

启动提示只执行一段固定 Node REPL 读取：

- 模型：`x-codex-turn-metadata.model`
- 主任务 ID：顶层 `threadId`
- 交叉校验：`x-codex-turn-metadata.thread_id`

控制器 `Start` 接收两个任务 ID并验证非空 UUID和逐字一致。选定任务后由控制器调用标题助手，模型不再自行决定字段、拼标题或调用 helper。标题失败只记诊断，不影响任务执行。

### 6.8 验证与提交

- 注册表中的 requiredChecks 由控制器在 `VERIFYING` 阶段执行，模型不能声称代跑。
- 模型可请求额外检查，但不能删除注册表检查。
- 每项检查记录固定命令 ID、退出码、时间和输出摘要，不保存私密值。
- 全部检查通过后，控制器调用现有 `automation-finalize-commit.ps1`。
- 提交器只处理清单中实际变化的路径；提交后再次调用 workspace guard 验证路径外基线不变。
- 任一检查失败、路径外变化或 HEAD 变化均失败关闭，不创建部分提交。

## 7. 新文件边界

新实现放在独立目录 `tools/hourly-controller-v2/`，不得在建设阶段修改旧控制器行为。建议职责如下：

- `controller.ps1`：唯一外部入口和状态迁移路由。
- `protocol.psm1`：请求/响应 schema、规范化与脱敏。
- `registry.psm1`：任务注册表读取和 Markdown 一致性校验。
- `discovery.psm1`：四个只读发现动作。
- `manifest.psm1`：工作清单验证与 decision coverage。
- `state.psm1`：新状态、租约、恢复和幂等迁移。
- `title.psm1`：任务 ID 校验与标题助手调用。
- `decision-adapter.psm1`：新决策账本与现有飞书桥接适配。
- `verification.psm1`：固定检查 ID 映射和执行证据。
- `tests/`：模块、协议、故障注入与端到端 fixture。

项目侧新增：

- `开发管理/自动工作流任务注册表.json`
- 新的薄启动提示词
- 重写后的自动工作流规则
- v8 到新状态的迁移说明和切换记录

## 8. 建设与切换顺序

### 阶段 A：冻结与证据

1. 确认 `tzg-hourly-controller` 为 `PAUSED`。
2. 记录 HEAD、工作区基线、旧 schema v8 状态摘要和飞书健康状态。
3. 复制旧状态为只读备份，记录 SHA-256；不得把私有状态提交到 Git。
4. 为本设计列出的五项 TQ-057 决策建立迁移 fixture。

### 阶段 B：离线重建

1. 在独立分支和 linked worktree 中建设 `hourly-controller-v2`。
2. 先写失败测试，再实现协议、注册表、发现网关、工作清单、状态机、标题、决策适配和验证。
3. 所有测试使用临时仓库、临时状态和假飞书传输；不得触碰生产状态。
4. 对保留组件运行现有回归，证明飞书和 Git 安全底座行为未变化。

### 阶段 C：迁移演练

1. 对旧状态备份运行迁移器，输出新状态到临时路径。
2. 重复运行迁移并比较字节级结果，证明幂等。
3. 验证 TQ-057 的五项决定、完整正文和范围契约全部存在。
4. 使用真实仓库执行 `plan-only`，只生成 TQ-057 工作清单，不授权写入。
5. 把清单交由负责人审阅；路径或决策覆盖不完整时返回实现阶段修正。

### 阶段 D：真实只读金丝雀

1. 使用当前 Codex 任务 ID验证标题读取和恢复。
2. `Start -> Discover* -> SubmitManifest(planOnly=true)` 全程只读。
3. 故意制造 fixture 基线变化，确认结果列出精确 `changedPaths`。
4. 确认没有项目写入、租约残留、私有标识输出或第二个写入控制器。

### 阶段 E：切换

1. 合并已验证分支到 `master` 并在合并结果上复验。
2. 使用 Codex 自动化管理能力或人工界面更新现有 `tzg-hourly-controller`；不得编辑私有 TOML。
3. 先保持 `PAUSED`，手动触发一次 `plan-only` 并核对标题和 TQ-057 清单。
4. 负责人明确确认清单后，开启一次受控写入运行。
5. 写入运行通过且提交正确后才恢复每小时调度。

### 阶段 F：观察与退役

1. 连续三次真实运行满足验收标准后，归档旧控制器、旧状态脚本和旧提示词。
2. 旧 schema v8 私有备份至少保留到 TQ-057 完成并通过复核。
3. 退役提交不得删除飞书桥接或 Git 安全底座。

## 9. 回滚

- 新控制器未产生项目提交：停用新提示词，保持自动化暂停，删除新状态文件即可；旧状态未改动。
- 新控制器已产生提交但验收失败：停止调度，不自动 reset/revert；由人工审查提交并创建显式修复或 revert。
- 飞书适配失败：保持新控制器暂停，继续保留独立运行的桥接任务和签名证据，不回退到 Gmail。
- 状态迁移失败：丢弃临时新状态，从只读 v8 备份重新演练；不得手工修补生产状态。

## 10. 验收标准

全部条件同时满足才允许恢复小时调度：

1. 新旧编排层不会同时写入；WF1、WF3、WF4 继续暂停。
2. 标题真实金丝雀成功，任务 ID来自两个当前有效元数据字段。
3. TQ-057 五项决定完整迁移，双倍率影响范围不再漏掉运行时、导入器、测试或既有资产。
4. 有效清单依赖的发现证据只能来自固定网关；工作清单缺失任何事实源、决定覆盖、路径意图或 required check 时确定性拒绝。
5. baseline/HEAD/path 变化返回精确项目相对 `changedPaths` 并安全回到 `IDLE`。
6. requiredChecks 由控制器执行并记录，全部通过后才能路径限定提交。
7. 飞书选项、卡片输入和严格文本三条回执回归通过，Gmail 不进入活动路由。
8. 故障注入后没有项目外泄、私有标识输出、残留租约、部分提交或自动重拍基线。
9. 合并后的 `master` 重跑全部相关测试并通过。
10. 首次 TQ-057 写入前，负责人审阅并明确批准 plan-only 工作清单。

## 11. 新对话执行入口

新对话必须按以下顺序开始：

1. 完整读取本设计文件。
2. 读取随后生成的实施计划，不自行重新设计。
3. 读取 `AGENTS.md`、`开发管理/自动工作流规则.txt`、`开发管理/自动工作流状态.txt`、`开发管理/当前任务队列.txt` 和 `开发管理/AI协作规则.txt`。
4. 只在独立 linked worktree 中实施，不触碰三项用户未跟踪内容。
5. 第一阶段只建设和测试旁路 v2；不得更新、启用或手动触发生产自动化。
6. 遇到设计与事实源冲突时停止实施并向负责人请求决定，不得以旧控制器行为替代本设计。
