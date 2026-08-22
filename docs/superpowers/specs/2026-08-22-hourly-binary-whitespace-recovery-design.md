# 每小时自动化二进制空白误报修复与失败 run 恢复设计

日期：2026-08-22

## 问题与已证事实

`A-CHAR-STATIC-CHESS-FUYUAN-BLENDER-01` 已生成并验证 Blender 静态候选，但在候选提交前被 `automation-finalize-commit.ps1` 调用的空白检查阻止。`check-pending-whitespace.ps1` 虽以扩展名识别 `.blend`、`.fbx` 为非文本文件，仍对它们执行 `ReadAllLines` 和逐行尾部空白匹配，因而把二进制字节组合误报为行尾空白。原失败会话记录了候选 `.blend` 86 处、`.fbx` 68 处，共 154 处；候选已按失败合同删除，当前仍可用保留的原始 FBX 复现同类伪阳性机制，但不把历史计数当作修复后的验收输入。

失败责任方随后按普通失败合同恢复了工作树。当前 schema 5 run 为 `attention_required`，没有 candidate commit、candidate result 或 canonical 证据；旧 owner worktree 与 candidate branch 仍保留，主分支没有收到该任务的业务变化。

## 目标

- 空白检查只读取项目已声明的文本扩展；非文本文件只完成路径存在性与文件类型核验，不读取、修复或改写内容。
- 现有白名单遗漏的已跟踪文本格式 `.mjs`、`.mat`、`.py`、`.js`、`.csproj`、`.config`、`.gitignore`、`.gitattributes`、`.vbs` 纳入同一文本扩展列表，避免修复二进制误报时缩窄文本门禁覆盖。
- 文本文件的缺失路径、目录路径、行尾空白检测、`-Fix` 和 `.meta` 语义空值例外保持不变。
- 补充回归测试，证明二进制文件不会误报且字节不变，同时证明文本门禁没有被放宽。
- 修复集成后，精确关闭现有空失败 run、清理其旧隔离现场，并通过正式共享入口重新执行苻渊任务。

## 非目标

- 不修改 `automation-finalize-commit.ps1` 的 expected paths、暂存、提交或元数据合同。
- 不用 Git attributes、内容嗅探、白名单复制或额外兼容分支建立第二套文件分类机制。
- 不修改苻渊任务卡、原始素材、Blender 业务逻辑或产物格式。
- 不把旧失败 run 标记为成功，不恢复旧模型会话，不增加自动重试层。

## 选定方案

先把仓库已跟踪但被旧列表遗漏的文本扩展 `.mjs`、`.mat`、`.py`、`.js`、`.csproj`、`.config`、`.gitignore`、`.gitattributes`、`.vbs` 补入现有 `$textExtensions`。随后在扩展名判断后增加单一边界：若扩展名不在 `$textExtensions` 中，直接进入下一路径。该路径仍须先通过存在性和“必须是文件”的校验，但不会调用 `ReadAllLines`。

跳过判断固定放在 `$checkedCount++` 之后、第一次 `ReadAllLines` 之前，以保留现有成功输出格式和“输入文件数”计数口径，避免影响依赖输出文本的现有测试与调用方。`automation-finalize-commit.ps1` 继续把完整的现存变更文件集合交给检查器，由检查器作为唯一文件类型边界决定是否扫描内容。

非文本文件在无 `-Fix` 和有 `-Fix` 时都只做路径核验并跳过内容；现有“`-Fix` 下发现非文本伪行尾空白后报错但不修改”的分支因此成为不可达代码，实施时直接删除，不保留第二种非文本行为。

相比只在 finalizer 过滤，这能同时修复直接调用检查器的场景，且不复制扩展列表；相比 Git attributes 或内容探测，它不增加新的事实源或分类复杂度。

## 修改范围

- `tools/check-pending-whitespace.ps1`：补齐仓库现有文本扩展，跳过非文本文件的内容读取与行尾空白匹配，并删除不可达的非文本 `-Fix` 报错分支。
- `tools/test-check-pending-whitespace.ps1`：在现有 missing path、`.meta` 语义空值、BOM、换行和字节稳定性覆盖上，增加遗漏文本扩展与 `.blend`、`.fbx` 二进制字节不变回归。
- 本设计文档：记录根因、停止条件、验证和恢复顺序。

## 测试设计

综合测试 `tools/test-check-pending-whitespace.ps1` 在其现有独立临时目录中增加四类夹具：

1. `.txt` 文件包含真实行尾空格和制表符。无 `-Fix` 时必须退出 1 且内容不变；使用 `-Fix` 后必须退出 0，行尾空白被移除。
2. `.mjs` 文件包含真实行尾空白，无 `-Fix` 时必须退出 1，证明新增到白名单的文本格式仍受门禁约束；`-Fix` 后必须退出 0。
3. `.blend` 和 `.fbx` 文件使用原始字节写入，刻意包含空格、制表符、CR、LF 和零字节组合。检查器无 `-Fix` 与有 `-Fix` 均必须退出 0。
4. 两个二进制文件在两次调用前后的 SHA-256 必须逐一相同。

必跑验证依次为：`tools/test-check-pending-whitespace.ps1`、`tools/tests/check-pending-whitespace.tests.ps1`、`tools/test-hourly-finalizer-invocation.ps1`。第一项证明 missing path、`.meta` 语义空值、字节稳定性、BOM、换行、新增文本扩展和二进制跳过；第二项保留既有轻量修复入口；第三项证明 finalizer 成功输出边界和进程内调用没有回归。提交前仅对本轮三条路径执行 pending-whitespace 检查，并运行 `git diff --cached --check`。

## 集成与恢复顺序

1. 在独立手动 worktree 实施、验证并形成仅含上述路径的提交。
2. 从主工作区重新读取 schema 5 `Show`、集成锁和主工作区状态；若目标路径冲突、锁被占用或事实变化则停止。
3. 通过 `tools/invoke-project-integration.ps1` 取得正式集成锁，将修复 fast-forward 到最新 `master`。
4. 重新核验旧 run 精确等于诊断现场：owner、runId、taskId、recoveryReason 和 worktree 路径匹配；candidate/canonical 字段全为空；旧 worktree 注册、分支、HEAD 和工作树状态均匹配记录。
5. 以 `CompletionCategory=failed`、原始 `ExpectedRecoveryReason` 和 `DetailCode=recovered_whitespace_false_positive` 调用 schema 5 `CompleteRun`。该动作只表示失败现场已人工处理，不表示苻渊任务成功。
6. runtime 成功关闭后，仅用普通非强制命令移除精确旧 worktree 和 candidate branch；清理拒绝时保留现场并停止，不使用 `--force`。
7. 通过当前请求 metadata 取得实际 Codex model，调用正式 `invoke-hourly-owner.ps1 -Owner codex -Action RunOnce`。共享入口重新 claim 仍为 ready 的苻渊任务并从最新 `master` 创建新 run。
8. 新 run 成功时沿现有流程完成 candidate、正式重放、集成、通知和清理；失败时保留新现场并报告，不自动再次运行。

## 停止条件

- 修复需要修改 finalizer、runtime schema、任务卡或增加第二套分类／重试机制。
- 主工作区目标路径有 staged、unstaged 或 untracked 冲突。
- 旧 run 不再是同一 `attention_required` 现场，出现 candidate/canonical 证据，旧 worktree 不干净，或分支／HEAD／路径不匹配。
- `CompleteRun` 不返回 `RUN_COMPLETED`，或旧 worktree 的普通清理被 Git 拒绝。
- 重新执行前任务不再保持原 route、owner、ready 状态，或 taskId 被其他 run 占用。

## 验收条件

- 文本空白门禁仍能失败关闭并可由 `-Fix` 修复。
- `.mjs` 回归证明旧列表遗漏的已跟踪文本格式在补入后仍受门禁约束；同组文本扩展只使用同一分类列表，不增加第二套判断。
- `.blend`、`.fbx` 不再产生伪行尾空白，且检查前后哈希不变。
- finalizer 现有调用测试通过，修复提交只包含授权路径。
- 旧失败 run 以 `failed` 精确关闭而非伪装成功，runtime 不再占用该 taskId。
- 苻渊任务由正式共享入口重新执行；本轮以其新的结构化终态为最终结果。
