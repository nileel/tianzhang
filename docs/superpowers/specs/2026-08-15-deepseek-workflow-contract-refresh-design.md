# DeepSeek 工作流合同收口设计

> 日期：2026-08-15
> 状态：方案已获负责人批准，按本文实施
> 目标：收口 DeepSeek V4 Pro 0813 迁移后残留的触发器、提交边界、任务模板与交接事实漂移

## 一、结论

本次采用“修复活动合同、退役重复模板、保持实时入口暂停”的最小方案：

1. `deepseek-hourly-trigger` 改用当前 Desktop 工具提供的 `exec_command + write_stdin` 前台等待链路，不再调用已退出当前工具合同的 `shell_command`。
2. DeepSeek 正式结果统一为一个原子提交，同时包含业务变化、`pending_review` 任务投影和交接证据；删除活动规则与状态文件中的双提交旧口径。
3. 四份 `DeepSeek任务卡-*` 不再承担活动执行规则，改为短小退役指引页；任务卡、`AGENTS.md`、`DeepSeek工作提示词.txt` 和领域规范成为唯一活动入口。
4. 清除 `AI合作沟通.txt` 中已经完成并归档任务的孤立交接。
5. 静态检查和聚焦测试必须能拒绝上述旧合同；实时 DeepSeek automation 只通过 automation 管理能力同步 prompt，保持 `PAUSED` 和其他字段不变。

该方案不修改 schema 5、共享 PowerShell 编排内核、owner adapter、DeepSeek gateway、任务选择、candidate、正式集成、通知或恢复机制。

## 二、已确认事实与根因

### 2.1 三次迁移没有同步覆盖全部所有者

- 2026-08-03 的已批准精简设计把 DeepSeek 的 `businessCommit + handoffCommit` 收敛为一个原子正式提交；当前 `tools/invoke-hourly-owner.ps1` 也通过 `cherry-pick --no-commit` 合并 candidate、任务投影与 `AI合作沟通.txt`，再调用一次 finalizer。
- 2026-08-13 的能力校准已把 `CLAUDE.md`、`DeepSeek工作提示词.txt`、`tools/hourly-owner-adapter.ps1` 和 `tools/invoke-deepseek-responsibility.ps1` 从 `deepseek-v4-flash` 切换为 `deepseek-v4-pro / DeepSeek V4 Pro 0813`。
- 2026-08-15 的 Codex 薄触发器修复把 `codex-hourly-worker` 从 `shell_command` 迁为 `exec_command + write_stdin`，但未同步修改姊妹 DeepSeek 触发器。

因此根因不是 DeepSeek 执行内核仍绑定旧模型，而是不同日期的局部迁移没有对全部重复文档、提示词与测试锚点做同一轮收口。

### 2.2 DeepSeek 触发器使用已失效的工具调用合同

`开发管理/DeepSeek小时触发提示词.txt` 当前要求在 `functions.exec` 内调用 `tools.shell_command`，并设置 `timeout_ms=3060000`。当前 Desktop 工具合同实际暴露 `tools.exec_command` 和 `tools.write_stdin`；Codex 薄触发器已经使用该链路。

实时 `deepseek-hourly-trigger` prompt 与版本化提示词精确一致，因此不是单独的实时配置漂移。现有 `tools/check-automation-workflow.ps1` 只验证 DeepSeek prompt 含共享入口、owner、memory 和展示锚点，没有要求新调用链，也没有拒绝 `shell_command`，所以当前错误合同仍能返回 `check-automation-workflow: OK`。

### 2.3 提交边界存在活动文本矛盾

以下活动事实已经一致采用单个原子正式提交：

- `开发管理/自动工作流规则.txt`；
- `CLAUDE.md`；
- `开发管理/DeepSeek工作提示词.txt`；
- `开发管理/AI协作规则.txt` 的“两个独立小时责任链”和“交接”段；
- `tools/invoke-hourly-owner.ps1` 的正式重放与 finalizer 调用。

但 `开发管理/AI协作规则.txt` 的 DeepSeek 角色例外仍写“外部两提交边界”，`开发管理/自动工作流状态.txt` 仍写连续 `businessCommit` 与 `handoffCommit`。这两处属于未随 2026-08-03 原子提交迁移删除的活动旧口径。

### 2.4 四份模板成为重复且漂移的事实源

四份模板最后一次实质维护停留在 2026-06-17 至 2026-06-19：

- `开发管理/DeepSeek任务卡-局部代码实现.txt`；
- `开发管理/DeepSeek任务卡-批量设计内容.txt`；
- `开发管理/DeepSeek任务卡-文档清洗.txt`；
- `开发管理/DeepSeek任务卡-CSV数据链路.txt`。

当前已确认的漂移包括：

- 普通执行无条件读取 `审核入口.txt`，与当前按审核／返工路由才读取的最小上下文规则冲突；
- 局部代码模板仍以“小功能”限制 DeepSeek，而当前主责按原子边界与确定性验收分派，不按规模或跨文件数限制；
- 批量设计模板要求所有内容都填写倍率、冷却和旧“本命升格”字段，与当前功能术法例外及镇府神通、丹相、源化界术语不一致；
- CSV 与文档清洗模板复制全局检查清单，可能覆盖任务卡冻结的精确范围和最小充分验证。

继续更新四套重复规则会保留相同漂移结构。直接删除又会使历史计划、旧链接和管理目录迁移映射失效。因此采用“移出活动路由但保留退役指引页”。

### 2.5 当前交接存在孤立历史条目

`开发管理/AI合作沟通.txt` 同时声明“当前无待审核交接”和保留 `C-HS-YY-JD-01K` 的待复审条目。该任务已由 Codex 独立复审通过并移入 `开发管理/任务归档/C-HS-YY-JD-01K.txt`，当前队列为空。该条目不再是活动交接，应按现有交接规则移出当前文件；本轮不重写任务归档或历史提交。

## 三、方案比较

### 3.1 方案 A：退役模板并保留指引页（采用）

从 `DeepSeek工作提示词.txt` 删除四模板路由表，直接要求按任务卡与领域入口加载事实。四个原路径保留短指引页，明确已退役、不可作为执行规则，并指向当前权威入口。

优点是消除活动重复事实源，同时保持历史链接和未来目录迁移映射可解析；不需要扩大到历史计划或归档清洗。

### 3.2 方案 B：继续维护四份活动模板（不采用）

可以保留按任务类型的速查结构，但每次 `AGENTS.md`、领域规范、主责边界或验证规则变化时都必须同步四份文件。当前漂移已经证明该同步成本没有被可靠承担。

### 3.3 方案 C：直接删除四份模板（不采用）

能彻底减少文件数，但会制造旧链接断裂，并迫使本轮更新管理目录迁移映射和更多历史引用，扩大职责而没有额外运行收益。

## 四、详细设计

### 4.1 DeepSeek 薄触发器

`开发管理/DeepSeek小时触发提示词.txt` 保留当前角色与固定命令，只替换调用和等待合同：

1. 在一个长时间 `functions.exec` 中调用一次 `tools.exec_command`：
   - `cmd` 为现有 `pwsh ... invoke-hourly-owner.ps1 -Owner deepseek -Action RunOnce ... -OutputJson`；
   - `workdir` 保持 `D:\天章游戏开发`；
   - 首次 `yield_time_ms=30000`；
   - `max_output_tokens=10000`。
2. 累积 `processResult.output`。进程未退出时只对初次返回的同一整数 `session_id` 调用 `tools.write_stdin`，每次 `yield_time_ms=60000`。
3. 缺少合法 session id、非零退出或最终输出不是单个 JSON 时分别以稳定的 DeepSeek 触发层错误停止，不重启共享入口。
4. 外层 `functions.exec` 若返回 `Script running with cell ID ...`，仍只对同一 cell 调用 `wait`，每次不超过 60 秒。
5. 最终继续原样输出脚本 JSON，并追加恰好一个简短 `::inbox-item`；不设置业务 status 白名单，不读取队列、任务卡、runtime 或 automation 配置。

该变化只修复 Desktop 触发层。`tools/invoke-hourly-owner.ps1`、`tools/invoke-deepseek-responsibility.ps1` 与 adapter 不修改。

### 4.2 静态检查与聚焦测试

`tools/check-automation-workflow.ps1` 对 DeepSeek prompt 增加：

- 必须包含 `tools.exec_command`、`tools.write_stdin`、`yield_time_ms: 60000`、`Script running with cell ID`、非零退出检查和 JSON 解析；
- 必须拒绝 `shell_command`、`timeout_ms: 3060000` 和任何 automation 自管理 token；
- 继续保留共享入口、`-Owner deepseek`、不读业务事实、memory 与单一 inbox 展示锚点。

`tools/test-check-automation-workflow.ps1` 增加直接断言，证明：

- Codex 与 DeepSeek 两个薄触发器都使用当前执行／轮询工具合同；
- DeepSeek 旧调用链会使测试失败；
- 临时 automation fixture 与版本化 prompt 精确一致时检查通过。

检查器同时增加活动文本反回归：

- `开发管理/AI协作规则.txt` 与 `开发管理/自动工作流状态.txt` 不得再包含“外部两提交边界”或当前 DeepSeek 正式结果仍为连续 `businessCommit`／`handoffCommit` 的表述；
- 历史归档、旧设计与旧提交说明不进入该拒绝范围。

### 4.3 单提交口径收口

只修改两处已确认旧口径：

- `开发管理/AI协作规则.txt` 的 DeepSeek 角色例外改为：所有修改标为未审核；自动外部执行由共享入口形成一个原子正式提交，手动执行遵守当前任务卡和交接合同。
- `开发管理/自动工作流状态.txt` 的架构事实改为：DeepSeek 正式结果为一个原子提交，交接记录不再是独立 `handoffCommit`；审核前不解锁依赖。

2026-08-02 的 `deepseek-v4-flash` canary 行属于明确日期的历史事实，保留不改。旧设计、任务归档和提交历史中的双提交或 V4 Flash 记录也不批量改写。

### 4.4 DeepSeek 活动入口与模板退役

`开发管理/DeepSeek工作提示词.txt` 的手动任务路由改为：

- 选中合法任务后，以同 ID 任务卡冻结的 `必读`、`必查范围`、`expectedPaths`、停止条件和验证为本轮直接入口；
- 领域入口继续由 `AGENTS.md` 路由到当前设计规范、技术经验、BattleSim、Unity 或自动化规则；
- 不再按任务类型读取四份 DeepSeek 模板。

四份模板均改为不超过必要长度的退役指引页，包含：

1. 明确“已退役，不是活动执行规则”；
2. 当前替代入口：`AGENTS.md`、`开发管理/DeepSeek工作提示词.txt`、选中任务卡及其领域事实源；
3. 历史引用仍可定位本页，但不得从本页推导必读范围、字段、验证或主责。

退役页不复制任何领域字段、审核入口、验证命令、模型身份或提交合同。

现有管理目录映射与影响面清单只同步四个原路径的用途说明：标记为退役指引且不参与活动路由；不改变既定目标路径、迁移批次或目录重组范围。

### 4.5 孤立交接清理

`开发管理/AI合作沟通.txt` 删除 `DSH-C-HS-YY-JD-01K-961911ab` 整个条目，保留文件头、“当前交接队列”和当前无待审核交接的准确说明。

删除依据必须在实施时再次核验：

- 活跃任务卡路径不存在；
- 归档卡 `dispatchState=completed`；
- 当前队列没有该 taskId 或任何 `codex_review` 行；
- 归档状态记录 Codex 独立复审通过。

任一事实变化则停止该项清理，不改写交接以掩盖新状态。

### 4.6 实时 automation 配置

版本化提交正式合入 `master` 后，通过 automation 管理能力更新现有 `deepseek-hourly-trigger`：

- prompt 替换为已通过聚焦测试的版本化文本；
- `status` 保持 `PAUSED`；
- `id`、kind、名称、schedule、model、reasoning effort、notification policy、execution environment、project 与 cwd 保持不变；
- 不直接编辑 `automation.toml`，不修改 `codex-hourly-worker`、日报或周报。

更新后只读核对规范化 prompt、长度与 SHA-256，并再次运行生产 automation 一致性检查。若 automation 管理能力不可用或字段不能完整保真，停止实时部署并保持 `PAUSED`，不以直接文件编辑绕过。

## 五、预期版本化路径

- `开发管理/DeepSeek小时触发提示词.txt`
- `开发管理/DeepSeek工作提示词.txt`
- `开发管理/DeepSeek任务卡-局部代码实现.txt`
- `开发管理/DeepSeek任务卡-批量设计内容.txt`
- `开发管理/DeepSeek任务卡-文档清洗.txt`
- `开发管理/DeepSeek任务卡-CSV数据链路.txt`
- `开发管理/AI协作规则.txt`
- `开发管理/自动工作流状态.txt`
- `开发管理/AI合作沟通.txt`
- `开发管理/管理目录唯一旧新路径映射与批准输入.txt`
- `开发管理/管理目录重组影响面清单.txt`
- `tools/check-automation-workflow.ps1`
- `tools/test-check-automation-workflow.ps1`
- `docs/superpowers/specs/2026-08-15-deepseek-workflow-contract-refresh-design.md`

不修改共享编排脚本、adapter、runtime、任务卡、队列、backlog、审核归档、业务 docs、CSV、Unity 或 BattleSim。

## 六、隔离、提交与集成

本任务属于自动化控制面变更，实施固定使用专用手动 worktree 和 `codex/` 前缀分支，即使开始实施时主工作区干净且无活动 run，也不直接在主工作区编辑。

实施前和合并前均调用 schema 5 `Show`，要求：

- `runs.codex=null`；
- `runs.deepseek=null`；
- `integrationLockStatus=none`。

实施只形成一个路径限定提交。暂存前对实际变化路径运行 `tools/check-pending-whitespace.ps1`，暂存后运行 `git diff --cached --check`。合并前重新检查主工作区 staged、unstaged、untracked 路径与待合并路径不冲突，只通过 `tools/invoke-project-integration.ps1` 持有项目集成锁执行 fast-forward。

任一 owner run、集成锁、主工作区路径冲突、非 fast-forward 或事实变化均停止集成，不 stash、不覆盖、不自动解冲突。

## 七、验证矩阵

### 7.1 版本化聚焦验证

1. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1`
2. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理`
3. 对本轮实际路径运行 `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 ...`
4. `git diff --check`
5. 暂存后 `git diff --cached --check`

聚焦测试必须直接证明旧 DeepSeek `shell_command` prompt 被拒绝，而不是只证明新 prompt 含若干通用关键词。

本轮不修改 CSV、asset、Unity、BattleSim 或数值参数，因此不运行 `check-data-chain.ps1`、Unity 测试或 BattleSim。相关输入未变化，不重复既有 DeepSeek gateway canary。

### 7.2 集成后实时配置验证

1. 通过 automation 管理能力读取并更新 `deepseek-hourly-trigger`，保持 `PAUSED` 和非 prompt 字段不变。
2. 在最新 `master` 运行 `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -RequireLegacyRetired`，要求版本化与生产 automation prompt 精确一致。
3. 独立核对更新前后非 prompt 字段、prompt 规范化文本、长度与 SHA-256。
4. 再次执行 schema 5 `Show`，确认两个 owner run 为空且集成锁空闲。

本次不立即运行暂停中的 DeepSeek automation，也不把它恢复为 `ACTIVE`。触发层工具链由静态合同测试和实时 prompt 一致性证明；是否恢复和执行真实周期属于后续负责人决定。

## 八、停止条件与回滚

出现以下任一情况立即停止，不叠加兼容补丁：

- 修复需要修改共享 runtime、owner adapter、Git 集成或恢复机制；
- 新 DeepSeek prompt 需要 `shell_command` 兼容分支、后台进程、第二 runtime 或重试层；
- 四份模板退役导致必须批量改写历史计划、归档或业务事实；
- 孤立交接的任务事实不再是已完成归档；
- 聚焦测试不能直接拒绝旧 prompt；
- 实时配置更新需要直接编辑 TOML，或无法保持非 prompt 字段与 `PAUSED` 状态；
- 任一 owner run 非空、集成锁被持有、主工作区相关路径冲突或集成不是 fast-forward。

版本化提交在实时部署前提供单一回滚边界。实时 prompt 更新失败时保持 automation 暂停；若已更新但一致性检查失败，只通过 automation 管理能力恢复更新前完整 prompt，仍保持 `PAUSED`，不回滚 schema 5 或共享入口。

## 九、完成条件

1. DeepSeek 版本化与实时 prompt 都使用单次 `exec_command`、同一 session 的 `write_stdin` 轮询和外层同一 cell 等待，不含 `shell_command` 或旧长超时。
2. 静态检查和聚焦测试能够拒绝旧 DeepSeek 触发合同。
3. 所有活动规则、状态和实际脚本对 DeepSeek 单一原子正式提交描述一致。
4. `DeepSeek工作提示词.txt` 不再路由到四份旧模板，四个原路径只保留退役指引。
5. 当前交接文件不再包含已经完成归档的 `C-HS-YY-JD-01K` 待复审条目。
6. 唯一版本化提交通过项目集成锁 fast-forward 到 `master`；主工作区无关改动不受影响。
7. 实时 `deepseek-hourly-trigger` prompt 与版本化文本精确一致，配置仍为 `PAUSED`，其他字段不变。
8. schema 5 两个 owner run 为空、集成锁空闲；没有新增 runtime、重试、兼容入口或后台组件。
