# 外部自动化终态契约修复设计

## 背景与根因

2026-07-28 22:15 的 `C-GZ-CITY-01` 外部责任方实际创建了完整的 `businessCommit` 与 `handoffCommit`，并把任务卡和当前队列转为 `codex_review/codex/ready`。DeepSeek V4 Pro 最终通过 Claude CLI `StructuredOutput` 返回的却是两个七位短提交号：

- `businessCommit=0a9e847`
- `handoffCommit=d9e95fc`

`tools/invoke-external-responsibility.ps1` 的后置校验只接受 40—64 位小写十六进制完整 SHA，因此将本次终态记录为 `failed/external_invalid_terminal`。当前 JSON Schema 只把两个字段声明为普通字符串，固定提示也没有明确禁止短 SHA，导致生成合同与消费合同不一致。

同一业务提交还把 `开发管理/任务列表/内容设计任务.txt` 中的任务行保留为 `主责=deepseek`，并使用自定义的“待复审”状态文本；任务卡和当前队列已经是 `owner=codex`、`dispatchState=ready`。因此 `tools/check-task-cards.ps1 -Postcondition ExternalPendingReview` 会继续以 `backlog projection mismatch` 拒绝该任务。

## 目标

1. 让 Claude CLI 的结构化输出在生成阶段就只能产生当前 SHA-1 仓库使用的完整 40 位提交 SHA，并保留 wrapper 的既有防御性校验。
2. 禁止 wrapper 自动扩展或猜测短 SHA，保持终态严格、可审计、失败关闭。
3. 让外部责任方在创建业务提交前自行证明任务卡、当前队列和 backlog 的待复审投影一致。
4. 修正 `C-GZ-CITY-01` 当前 backlog 投影，使既有业务成果可以进入 Codex 复审，不重新执行外部业务。

## 非目标

- 不重新运行或重写 `C-GZ-CITY-01` 的内容生产。
- 不修改既有业务提交或交接提交，不 amend、不 rebase、不 reset。
- 不放宽 SHA 校验，不新增短 SHA 兼容分支或 `git rev-parse` 自动补全。
- 不新增重试层、恢复状态、队列、runtime 字段或第二套终态解析。
- 不修改实时 automation TOML、调度周期、owner 映射、租约或飞书通知。
- 不改变外层控制器对 `ExternalPendingReview` 的最终核验职责。

## 选定方案

### 一、收紧结构化输出合同

仓库对象格式已由 `git rev-parse --show-object-format` 核验为 `sha1`。在 `New-TerminalSchema` 中将 `businessCommit` 与 `handoffCommit` 约束为最小长度 40、最大长度 40，且以 40 位小写十六进制字符结尾的字符串。精确长度使开头无需 `^` 锚点，避免 Windows `claude.cmd` 的多层命令解析吞掉该字符。固定责任方提示同时明确：

- 必须返回完整的 40 位小写十六进制提交 SHA。
- 不得返回 Git 短提交号。

wrapper 现有的 40—64 位 `Assert-CommitSha` 保持不变，作为 JSON Schema 之后的第二道校验和未来对象格式的防御边界。当前生成合同按仓库实际 SHA-1 格式精确要求 40 位。任何短 SHA、空值、非十六进制文本或两个相同提交号仍返回 `failed/external_invalid_terminal`，不得尝试补全。

### 二、在业务提交前验证任务投影

固定提示在“同卡转换”之后、“创建 businessCommit”之前，要求外部责任方运行：

`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -RepositoryRoot <RepositoryRoot> -TaskId <TaskId> -Postcondition ExternalPendingReview -OutputJson`

只有该检查返回 `status=ok` 时才可创建双提交。检查失败时应修正同一授权路径内的任务卡、当前队列或 source backlog；不得通过自定义状态文本绕过 canonical 投影，也不得先提交后再建立补丁提交。

外层控制器仍在收到 `completed` 后运行同一后置条件。该外层检查属于不信任外部责任方的最终门禁；责任方内检查用于避免把已知无效投影写入业务提交，二者职责不同。

### 三、纠正现有任务投影

将 `开发管理/任务列表/内容设计任务.txt` 的 `C-GZ-CITY-01` 行改为：

- 主责：`codex`
- 状态投影：`已排队`

任务卡中的 `stateReason` 和 `开发管理/AI合作沟通.txt` 继续保存“已修改／未审核、等待 Codex 复审”的语义，不在 backlog 的 canonical 投影字段中另造状态。

不修改任务卡正文、当前队列、业务内容、两笔既有提交或 runtime 的历史失败结果。

## 数据流与失败边界

1. 外部责任方更新业务文件和同一任务的任务卡、当前队列、backlog。
2. 外部责任方运行 `ExternalPendingReview`；失败则在授权范围内纠正，尚不创建提交。
3. 外部责任方创建 `businessCommit`，再只修改交接文件并创建 `handoffCommit`。
4. Claude CLI JSON Schema 要求返回两个完整 SHA；wrapper 再以既有正则复核。
5. 外层控制器核验提交父子关系、元数据、工作区残留和 `ExternalPendingReview`，之后才记录成功并释放租约。

如果 provider 无法按 Schema 生成合法终态，wrapper 按既有失败路径返回稳定错误；不从模型正文、Git HEAD 或交接文件猜测提交号。

## 实施范围

仅修改：

1. `tools/invoke-external-responsibility.ps1`
2. `tools/test-invoke-external-responsibility.ps1`
3. `开发管理/任务列表/内容设计任务.txt`
4. 本设计文档

若现有静态合同测试因固定提示新增约束而要求同步精确断言，只修改直接命中的测试文件，不改生产 automation 配置或其他业务路径。

## 验证

最小充分验证集：

1. PowerShell parser 检查。
2. `tools/test-invoke-external-responsibility.ps1`：
   - 断言传给 Claude CLI 的 JSON Schema 对两个提交字段包含 40 位完整 SHA 末尾正则、40 位最小长度和 40 位最大长度。
   - 断言固定提示包含完整 SHA 要求和 `ExternalPendingReview` 预提交命令。
   - 增加短 SHA 终态用例，证明 wrapper 拒绝短 SHA且不自动补全。
   - 保持现有合法 completed、identity、session、租约和失败关闭用例通过。
3. `tools/check-task-cards.ps1 -TaskId C-GZ-CITY-01 -Postcondition ExternalPendingReview -OutputJson`，证明当前任务投影恢复一致。
4. `tools/test-check-task-cards.ps1`，证明 canonical backlog 规则未被放宽。
5. 自动工作流静态检查及其直接测试，确认固定入口、核心规则和实时 prompt 合同未被破坏。
6. 对本轮预期路径运行待提交空白检查、审核文本检查与 `git diff --check`。

不运行真实外部业务 canary，不再次执行 `C-GZ-CITY-01`，不重跑与本修复无关的领域验证。

## 完成条件

- Claude CLI 结构化输出合同与 wrapper 消费合同都只接受完整 SHA。
- 短 SHA 用例稳定返回 `external_invalid_terminal`，没有自动补全路径。
- 外部责任方提示在业务提交前要求通过 `ExternalPendingReview`。
- `C-GZ-CITY-01` 当前任务卡、队列和 backlog 投影通过后置条件检查。
- 工作区没有无关改动；自动化租约、recovery、runtime 历史结果和实时配置均未改变。
