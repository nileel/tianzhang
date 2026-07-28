# 自动化提交元数据契约收敛设计

## 背景与根因

2026-07-29 的 `C-GZ-ENEMY-01` 外部责任方已经完成业务修改，创建了 `businessCommit=a3ff18281f5411a7cb6d0102c60c7f38f372fcc3` 与 `handoffCommit=2b8ab433ad177fdc7339dde6bc5a0cc3524d145c`，并把同一任务转换为 `codex_review/codex/ready`。业务提交的 `Verify` 行缺少强制子字段 `后续=`，控制器因 `external_commit_metadata_invalid` 将该轮记录为失败。

直接遗漏来自外部责任方，但根因是同一提交元数据契约分散在多个组件：

- `tools/automation-finalize-commit.ps1` 只完整校验 `Plain`，对 `Result`、`Impact` 和 `Verify` 只检查非空与单行。
- `tools/invoke-codex-responsibility.ps1` 自行读取并校验同一组字段。
- `tools/send-feishu-notification.ps1` 再次解析同一组字段。
- `tools/invoke-external-responsibility.ps1` 只核对终态与提交 SHA，不核验实际业务提交元数据。
- 每小时控制器根据 prompt 临时拼装第四套 Git 与正则检查。

生产端和消费端没有共享同一契约，导致无效提交先被创建，再由更外层控制器判定失败。通知展示字段因此反向影响已经完成的业务提交、任务投影和交接闭环。

## 已确认决定

1. 不改写 `a3ff182`、`2b8ab43` 或任何既有提交历史。
2. 不修改 runtime 中已经记录的历史失败结果。
3. `C-GZ-ENEMY-01` 继续按当前 `codex_review` 队列投影进入 Codex 复审。
4. 选择单一元数据契约方案，不采用仅补 prompt 的临时修补，也不放宽提交元数据要求。

## 目标

1. 让自动化提交元数据只有一份可执行契约。
2. 在 finalizer 创建提交前拒绝缺失或非法的九个子字段。
3. 让 Codex 固定调用器、外部 wrapper 和飞书发送器复用同一解析与校验实现。
4. 让外部 wrapper 只在实际业务提交元数据合法时返回 `completed`。
5. 让每小时控制器恢复为薄路由，不再临时拼装提交元数据正则。
6. 保持通知失败不改变已经关闭的业务终态。

## 非目标

- 不修改单写入租约、runtime schema、恢复协议或阻塞指纹。
- 不修改固定队列顺序、任务卡生命周期、owner 映射或 Codex 独立复审边界。
- 不合并 `businessCommit` 与 `handoffCommit`，不取消双提交。
- 不新增重试层、兼容分支、第二套状态机、队列或通知渠道。
- 不运行真实外部业务任务，不发送飞书生产金丝雀。
- 不修改自动化名称、周期、模型、工作目录或启用状态。

## 单一契约 helper

新增一个纯 PowerShell helper。它只接收完整提交正文以及可选的预期 Task、State，不自行调用 Git，不提交、不通知、不读取任务卡、不修改租约。

helper 负责：

1. 要求 `Automation`、`Task`、`State`、`Result`、`Impact`、`Verify`、`Plain` 每项恰好出现一次。
2. 核对 `Automation=tzg-hourly-controller`。
3. 在调用者提供预期值时精确核对 Task 与 State。
4. 校验以下单行结构：
   - `Result: 问题=<原问题>；完成=<具体交付>`
   - `Impact: 影响=<实际行为变化>；边界=<明确未涉及范围>`
   - `Verify: 验证=<关键检查与结果>；后续=<解锁项、剩余依赖或下一状态>`
   - `Plain: 发生=<负责人短句>；影响=<负责人短句>；需要=<负责人短句>`
5. 要求九个子字段全部非空，拒绝换行与控制字符。
6. 保持专业字段现有长度上限和三个通俗字段各 200 个 Unicode code point 的上限。
7. 成功时返回结构化对象，供通知渲染直接消费；失败时抛出稳定且不包含私有内容的错误。

调用者继续使用各自已有的 UTF-8 Git 读取函数取得提交正文。这样 helper 保持纯文本契约职责，不获得仓库访问、进程管理或控制面职责。

## 调用点收敛

### Finalizer

`tools/automation-finalize-commit.ps1` 继续构造固定提交正文，但删除局部字段正则。构造完成后、暂存和提交之前调用统一 helper。任何字段非法时立即失败，HEAD 与索引不得因本次 finalizer 调用发生变化。

### Codex 固定调用器

`tools/invoke-codex-responsibility.ps1` 保留提交枚举、唯一提交形状、工作区和任务卡后置条件。删除本地的 `Get-CommitMetadata` 与 `Test-NotificationMetadata` 重复实现，改为把提交正文交给统一 helper，并将失败映射到既有未核验提交类别，不新增 runtime 状态。

### 外部责任方 wrapper

`tools/invoke-external-responsibility.ps1` 的固定提示明确写出四行完整模板。收到 Claude CLI 的 `completed` 结构化终态后，wrapper 除了核对 identity、session 和完整提交 SHA，还读取 `businessCommit` 正文并通过统一 helper 核对：

- Task 等于当前 TaskId。
- State 等于 `pending_review`。
- Automation 与九个子字段全部合法。

实际提交元数据不合法时返回 `failed/external_commit_metadata_invalid`，不得把该终态规范化为 `completed`。wrapper 不关闭租约、不记录结果、不发送通知。

### 飞书通知

`tools/send-feishu-notification.ps1` 删除重复的提交字段正则，使用统一 helper 的结构化输出构造任务卡片。通知仍发生在业务终态关闭之后；渲染、输入或发送失败只记录脱敏投递状态，不得改变任务卡、提交、租约、recovery、category 或退出码。

### 每小时控制器

仓库控制器提示词与实时 automation prompt 改为：

- 外部 wrapper 返回 `completed` 已表示固定入口完成确定性的业务提交元数据校验。
- 控制器继续核对 owner 对应 identity、session、双提交父子关系、handoff 无 Automation 元数据、相对基线残留和 `ExternalPendingReview`。
- 控制器不再临时读取业务提交正文或拼装九个子字段正则。

控制器保留不信任外部模型的边界；它信任的是仓库中的固定 wrapper 与统一 helper，而不是模型正文。

## 正常数据流

1. 外部责任方形成四行结构化元数据。
2. finalizer 通过统一 helper 在提交前校验输入。
3. finalizer 创建 `businessCommit`。
4. 外部责任方只修改交接文件并创建 `handoffCommit`。
5. Claude CLI 返回结构化终态。
6. 外部 wrapper 读取实际 `businessCommit`，通过统一 helper 校验 Task、State 与九个子字段。
7. wrapper 只有在 identity、session、完整 SHA 和元数据全部合法时返回 `completed`。
8. 控制器核对提交父子关系、handoff、工作区残留和任务投影。
9. 控制器记录结果并释放租约。
10. 飞书发送器使用同一 helper 读取字段并发送通知；通知结果不反向修改业务终态。

Codex 路线同样在 finalizer 提交前和固定调用器提交后使用同一契约，保持生产端与可信消费端两道门禁，但不复制规则。

## 失败边界

- 提交前元数据错误：finalizer 拒绝，不创建无效提交。
- 外部责任方绕过 finalizer 或实际提交正文异常：wrapper 返回 `external_commit_metadata_invalid`。
- Codex 实际提交异常：固定调用器沿用既有未核验提交失败类别。
- 提交父子关系、工作区残留或任务投影异常：控制器沿用现有失败保全规则。
- 飞书字段映射、渲染或发送异常：只记录通知失败，不改变已经关闭的业务结果。
- 既有无效元数据提交、任务投影和历史 runtime 记录：保持不变，不 amend、不 rebase、不 reset。

不增加自动修复、自动重试、历史重写或从相邻文本猜测缺失字段的行为。

## 修改范围

预计修改：

1. 新增统一提交元数据 helper。
2. 新增 helper 直接测试。
3. `tools/automation-finalize-commit.ps1`
4. `tools/test-automation-finalize-commit.ps1`
5. `tools/invoke-codex-responsibility.ps1`
6. `tools/test-invoke-codex-responsibility.ps1`
7. `tools/invoke-external-responsibility.ps1`
8. `tools/test-invoke-external-responsibility.ps1`
9. `tools/send-feishu-notification.ps1`
10. `tools/test-send-feishu-notification.ps1`
11. `tools/check-automation-workflow.ps1`
12. `tools/test-check-automation-workflow.ps1`
13. `开发管理/自动工作流规则.txt`
14. `开发管理/自动工作流控制器提示词.txt`
15. 本设计文档

实现完成并通过仓库内验证后，通过 Codex 自动化管理能力同步 `tzg-hourly-controller` 的完整现有配置，只替换已批准的 prompt 内容并保留其他字段。

若实施发现必须修改上述范围之外的 runtime、恢复、队列、任务卡、飞书私有状态或外部 CLI 权限合同，立即停止并重新确认根因，不继续叠加补丁。

## 验证

### Helper

- 合法 `completed` 与 `pending_review` 元数据返回正确结构化字段。
- `Automation`、Task、State、Result、Impact、Verify、Plain 任一缺失或重复时失败。
- 九个子字段分别缺失时失败。
- Task 或 State 与预期不一致时失败。
- 字段包含换行、控制字符或超过现有长度边界时失败。
- Unicode code point 计数保持三个通俗字段各不超过 200。

### Finalizer

- 合法元数据继续创建单一、路径限定的 Automation 提交。
- 缺少 `后续=` 等非法元数据在提交前失败。
- 失败时 HEAD 不变化，本轮目标路径不被新增暂存。
- 保持既有归档删除、新增、路径隔离和无关索引测试通过。

### Codex 固定调用器

- 合法元数据提交正常关闭。
- 非法或重复元数据不能成为已核验成功。
- 任务卡、唯一提交、工作区和 runtime 既有测试保持通过。

### 外部 wrapper

- 固定提示包含四行精确模板。
- 合法 `businessCommit` 能返回 `completed`。
- 缺少任一子字段的实际业务提交返回 `external_commit_metadata_invalid`。
- identity、session、完整 SHA、owner、租约与任务投影既有门禁保持通过。

### 飞书

- 合法元数据映射出的目标、完成、影响、边界、验证、后续和三个通俗字段与现有卡片一致。
- 非法元数据只使通知入口失败，不修改业务终态。
- 既有幂等、脱敏、决定桥隔离和通知发送回归保持通过。

### 工作流与文本

- 自动工作流静态检查证明所有调用点引用统一 helper。
- 静态检查证明控制器 prompt 不再要求现场拼装九字段正则。
- 仓库控制器 prompt 与实时 automation prompt 完全一致。
- 运行审核文本检查、目标路径待提交空白检查和 `git diff --check`。

不运行真实外部任务、不重复执行 `C-GZ-ENEMY-01`、不发送飞书生产消息、不运行与本修复无关的 Unity、BattleSim 或数据链验证。

## 完成条件

- 提交元数据结构只有一份可执行实现。
- Finalizer 在创建提交前拒绝缺失 `后续=` 或其他子字段。
- Codex、外部 wrapper 和飞书发送器不再保存独立的字段正则。
- 外部 wrapper 只为元数据合法的实际业务提交返回 `completed`。
- 控制器不再现场拼装提交元数据解析脚本。
- 通知失败不改变业务结果。
- 既有提交、任务卡、队列和 runtime 历史均未改写。
- 自动化仍为原名称、周期、模型、工作目录与启用状态。
