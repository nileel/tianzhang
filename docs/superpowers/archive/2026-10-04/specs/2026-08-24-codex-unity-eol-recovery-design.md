# Codex Unity 换行假脏与候选恢复设计

## 目标

为 Codex run `2bf1a212-badb-4063-b128-f7bbe5309c74` 保留并复用已验证的候选证据，按现有 schema 5 合同精确关闭失败 run，再从最新 `master` 在独立手动任务 worktree 中完成同一卡 `U-CHAR-STATIC3D-MATERIAL-CORRECT-01`。

同时消除已观察到的 Unity 行为——Windows linked worktree 以 CRLF checkout、Unity 再以 LF 重写文本——造成的 `codex_candidate_invalid`。修复不承诺覆盖未来工具主动写入 CRLF 的相反行为；遇到这类输出仍按内容哈希闸门人工判断。

全过程保持候选验收、失败停止、共享集成锁、通知和清理合同不变；不得新增 runtime 状态、重试、兼容分支、恢复对象或“忽略脏状态”的 wrapper 例外。

## 已证实根因与当前事实

- 仓库继承系统级 `core.autocrlf=true`，现有 `.gitattributes` 只声明美术二进制文件的 LFS 属性，没有 Unity 文本换行规则。
- 原 owner worktree 的 `FuYuan_StaticChess.prefab` 与 `ShaderGraphSettings.asset` 在 Unity 验证后由 CRLF 变为 LF。两文件当前原始 blob 哈希与 candidate HEAD 完全相同，`git diff --quiet HEAD -- <path>` 返回 `0`，但索引 stat 尺寸仍对应 CRLF，`git status --porcelain` 因此报告两个 `.M`。
- candidate HEAD `2fe9b054aae5b259fdbeefe6e9879b077052e179` 是冻结 base `ef850185c898bf975d8a3a5818d176f817d3d0dc` 的唯一直接后继，12 个 changed paths 全部落在任务卡 `expectedPaths` 内。共享 wrapper 因 status 非空在 `completed` 闸门正确返回 `codex_candidate_invalid`。
- 候选还缺任务卡要求的 `dotnet build src/TianZhang.EditModeTests.csproj --no-restore` 成功证据。失败原因是 fresh linked worktree 尚未生成被忽略的 `src/Temp/obj/TianZhang.EditModeTests/project.assets.json`，不是源码编译错误。
- `master` 已从冻结 base 前进到 `686de2bfa5412f739efaeeba27c37e9f401c90ff`。新提交修改 5 个路径，其中 3 个与 candidate 重叠：视觉方向规格、场景与 Unity 任务列表、当前任务队列。任何恢复步骤都不得再把冻结 base 当成 formal base。
- schema 5 的 `attention_required` 没有到 `candidate_ready`、`canonical_ready` 或 `integrated` 的合法迁移。旧 run 不能伪装成成功恢复；只能保留现场后用现有 `CompleteRun(failed)` 合同关闭，再以普通手动任务完成业务结果。

## 方案选择

采用“声明式 Git 属性 + 失败 run 精确关闭 + 最新事实上的手动完成”。

不修改 candidate wrapper 来忽略无文本 diff 的 `.M`，因为这会削弱干净 worktree 闸门；不在每轮自动化中增加索引刷新或重试，因为这会新增隐式流程；不对 candidate 整体 `cherry-pick`，因为最新 `master` 已与机械投影文件形成真实冲突。

## 第一阶段：持久换行规则与设计落库

实施前把当前设计 worktree fast-forward 到当时最新 `master`；本规格初次修订时已从 `ef850185` fast-forward 到 `686de2bf`。若执行时 `master` 再次前进，只允许在 `.gitattributes` 和本规格都未与新提交冲突时重新 fast-forward；否则停止并复核。

在仓库根 `.gitattributes` 的现有 LFS 规则之前增加：

```gitattributes
src/** text=auto eol=lf
```

该规则只为 `src/` 下 Git 判定为文本的文件固定 LF。其后的现有 LFS 规则继续对已列出的美术二进制扩展设置 `-text`，因此二进制资产不参与换行转换。

本设计文档与 `.gitattributes` 形成同一个、基于当时最新 `master` 的路径限定管理提交。该提交不得包含 renormalize 内容变化或其他路径，并通过 `tools/invoke-project-integration.ps1` 的最新 HEAD、授权路径、主工作区冲突和 fast-forward 闸门集成。主工作区现有 `StartMenuScene.unity` 改动保持不动。

不修改 PowerShell 共享入口、candidate wrapper、finalizer、runtime schema 或任务选择逻辑。

## 第二阶段：候选合同预校验与旧 run 关闭

证据来源必须分开：

- 固定小时入口或 `invoke-hourly-owner.ps1` 的 existing-run 结果只核对 owner、taskId、runId、state 和脱敏原因。
- 私有完整字段通过 `hourly-automation-lease.ps1 -Action Show -RepositoryRoot <root>` 与只读 `runtime.json` 交叉核对。
- Git worktree、branch、HEAD、父链和 status 直接从仓库与原 owner worktree 核对；任务 route、owner、ready 状态和 digest 从当前任务卡及队列核对。

执行基线必须精确为：

- owner=`codex`，taskId=`U-CHAR-STATIC3D-MATERIAL-CORRECT-01`，runId=`2bf1a212-badb-4063-b128-f7bbe5309c74`；
- state=`attention_required`，recoveryReason=`Codex responsibility ended with failed/codex_candidate_invalid`；
- baseCommit=`ef850185c898bf975d8a3a5818d176f817d3d0dc`，taskCardDigest=`eac73a6cc30dca1055721587adaf158ce4be19efb482de47fba5855acffe387d`；
- runtime 中 candidateCommit、candidateResult、canonicalBranch、canonicalBase、canonicalHead、sessionKind、sessionId 均为 `null`；
- candidate branch 仍指向 `2fe9b054...`，原 owner worktree 已注册且没有相关活动进程；DeepSeek run 为空、集成锁空闲。

在关闭 run 前，先 dot-source `tools/automation-commit-metadata.ps1`，对 candidate commit message 调用 `ConvertFrom-TzgAutomationCommitMessage -ExpectedTask U-CHAR-STATIC3D-MATERIAL-CORRECT-01 -ExpectedState completed`。解析失败、字段不全或身份不符即停止；该预校验只证明消息合同格式，不把候选声明当成尚未补齐的验证证据。

随后再次证明两个 `.M` 的原始 blob 哈希等于 candidate HEAD，并只对这些已证明内容相同的路径执行 `git update-index --really-refresh`。首次调用可能以退出码 `1` 打印 `<path>: needs update`，这表示发现并刷新旧 stat，不单独判为失败；以第二次 status 为空、索引 stat 尺寸已更新且 blob 哈希仍相同作为成功条件。任一内容不同都停止，不执行 reset、checkout、clean 或纳入候选提交。

在 `开发管理/自动工作流状态.txt` 的后续管理记录中保留原 run、candidate branch／SHA、worktree 路径、失败原因和人工恢复所有权。旧 owner worktree 与 candidate branch 作为证据保留，不复用为手动任务 worktree，也不自动清理。

确认 runtime 的 candidate／canonical 字段仍全空后，按 empty-attention 精确合同调用 `CompleteRun -CompletionCategory failed`，并传入完全一致的 `ExpectedRecoveryReason`。关闭失败 run 不发送成功通知；若返回值不是 `RUN_COMPLETED`，停止。

## 第三阶段：在最新事实之上完成同一卡

旧 run 关闭后，从当时最新 `master` 创建独立手动任务 worktree 和 `codex/` 分支。任务卡必须仍为 `codex_execute/codex/ready`，且没有新的 owner run 占用同一 taskId。

不整体 cherry-pick candidate。按以下类别复用证据：

### 实质内容

- 对 JPEG、JPEG `.meta`、材质、`VisualBaselineBuilder.cs`、`VisualBaselineEditorTests.cs` 和验证记录，从 candidate 提取精确 patch 或 blob。
- 对与最新 `master` 重叠的视觉方向规格，只应用 candidate 在 BaseColor 修正章节的独立语义增量，并证明 `686de2bf` 新增的两条 pipeline 用户授权事实仍完整保留。若实质 hunk 不能无冲突合并，停止。
- 验证记录必须按本轮实际重验结果更新；不得保留“dotnet build 未通过”后又把任务标为完成的矛盾表述。

### 机械投影与任务生命周期

这些文件从手动任务 worktree 的最新 base 重新派生，不采用 candidate 的整文件版本：

- `开发管理/当前任务队列.txt`：只移除 `U-CHAR-STATIC3D-MATERIAL-CORRECT-01` 行，保留 `686de2bf` 新加入的两张 pipeline ready 行及其顺序。
- `开发管理/任务列表/场景与Unity任务.txt`：移除已完成 U-CHAR 行；仅从静态 3D pilot 的 blocker 中移除 U-CHAR，保留 `D-CHAR-STATIC3D-MOTION-PIPELINE-01` 及其最新 ready／用户授权事实。
- `开发管理/任务卡/A-CHAR-STATIC3D-MOTION-PILOT-01.txt`：只移除 U-CHAR blocker，继续保留 pipeline blocker，stateReason 与最新用户授权一致。
- 当前 U-CHAR 任务卡以最新 active card 为输入，结合实际验证形成 completed 归档；删除 active path并新增归档 path，不覆盖其他任务事实。

手动任务 worktree 创建时记录 formal base `M`。所有机械投影只从 `M` 派生；最终调用 `invoke-project-integration.ps1` 时传 `ExpectedMainHead=M`，由共享锁再次证明 `master` 未变化。若锁内发现 HEAD 变化，集成脚本必须拒绝，本轮停止并重新读取事实；不得把基于旧投影的提交集成，也不得自动重试。

formal task diff 的真实路径必须仍精确等于 candidate 的 12 个授权路径；第一阶段已进入 parent 的 `.gitattributes` 和本规格不属于 task diff。

## 第四阶段：验证与正式集成

先运行：

```powershell
dotnet restore src/TianZhang.EditModeTests.csproj --ignore-failed-sources
dotnet build src/TianZhang.EditModeTests.csproj --no-restore
```

restore 只生成被根 `.gitignore` 中 `[Tt]emp/` 覆盖的 `src/Temp/obj` 产物，不得进入 Git。随后运行任务卡全部其余验证，并补充固定镜头可见性证明；若无法证明黑、灰、金服饰主色可辨认，则不宣称完成。

formalizer 的干净闸门前，对任何任务授权集合外的 tracked `.M` 使用统一的 stat 假脏程序：必须同时满足 `git diff --quiet HEAD -- <path>` 返回 `0`、工作区原始 blob 哈希等于 HEAD blob、没有 staged diff，才允许 `git update-index --really-refresh -- <path>`。首次 `needs update` 按前述 postcondition 判断；任一真实内容差异、未跟踪文件或授权外 staged 路径都立即停止。该程序是本次人工恢复证据步骤，不加入自动化 workflow。

验证清单：

- 来源 JPEG 与 Unity 目标的字节数及 SHA-256 精确一致。
- `dotnet restore src/TianZhang.EditModeTests.csproj --ignore-failed-sources`。
- `dotnet build src/TianZhang.EditModeTests.csproj --no-restore`。
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1`。
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-asset-versioning.ps1`。
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1 -FailOnMissingAssets`。
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-unity-assembly-boundaries.ps1`。
- 固定镜头输出证明苻渊服饰黑、灰、金主色可辨认，材质 `_BaseMap` 精确引用批准 Texture。
- `tools/check-pending-whitespace.ps1`、`git diff --cached --check` 和最终空 `git status --porcelain`。

验证全部通过后，用既有 `automation-finalize-commit.ps1 -RequireAutomationMetadata` 形成一个正式 Codex 提交。提交的 Result／Impact／Verify／Plain 必须按本轮实际证据重写并由 metadata parser 再次校验；不得照抄候选中未覆盖 dotnet build 和固定镜头证明的摘要。

集成前再次读取任务卡、队列、两个 owner run、主工作区路径冲突和集成锁；调用 `invoke-project-integration.ps1`，只允许从记录的 `M` fast-forward 到 formal SHA。主工作区 `StartMenuScene.unity` 不在 formal paths 中，必须保持原样。

## 第五阶段：收口、状态记录与通知

证明 `master` 已包含 formal SHA、任务归档和队列／backlog／pilot 投影正确后：

- 原失败 run 已在第二阶段关闭，不重复调用 `CompleteRun`，不伪造 integrated runtime 状态。
- 使用原 taskId、原 runId 和已进入 `master` 的 formal SHA 发送一次 `TaskOutcome -Status completed`；只有 provider 接受才记录已投递，不重试。
- 以单独的路径限定管理提交更新 `开发管理/自动工作流状态.txt`“当前人工阻塞与恢复所有权”，记录换行根因、旧 run 的 failed 精确关闭、证据保留位置、formal SHA、验证结果和通知结果。该提交同样通过共享集成锁；不与业务 formal commit 混合。
- 手动任务 worktree 仅按普通精确清理合同处理；旧 owner worktree 与 candidate branch 继续保留为失败证据，后续若要删除必须另做 cleanup proof。

## 持久规则验证

- `git check-attr text eol -- src/Assets/Art/Characters/StaticChess/FuYuan/FuYuan_StaticChess.prefab src/ProjectSettings/ShaderGraphSettings.asset` 必须返回 `text: auto`、`eol: lf`。
- 对现有 LFS 美术二进制路径运行 `git check-attr filter text eol`，必须保持 `filter: lfs`、`text: unset`。
- 运行 `git ls-files --eol src/`，所有 Git 判定为文本的 tracked blob 不得出现 `i/crlf` 或 `i/mixed`；无换行文本允许保持原状。
- 对 `src/` 下现有非 LFS 二进制条目复验 text 属性和 Git 二进制判定，必须保持不参与换行转换。
- `.gitattributes` 管理提交不得携带任何 renormalize 内容变化或其他路径。

## 停止条件

- 假脏文件任一原始 blob 哈希不再等于对应 HEAD，或授权外路径存在真实 diff。
- runtime、任务卡 digest、route、owner、ready 投影、candidate branch／SHA、候选 metadata 或主工作区冲突证据发生不兼容变化。
- `.gitattributes` 导致 LFS 或其他二进制文件被识别为文本，或产生预期外 renormalize 差异。
- 最新 `master` 的实质增量无法与 candidate 语义无冲突合并，或机械投影不能从同一 formal base 唯一派生。
- restore、build、Unity 测试、资产检查、数据链路、程序集边界或固定镜头证明任一失败。
- 恢复需要修改 wrapper、放宽干净 worktree 闸门、添加 runtime 迁移、自动重试、第二 recovery 对象或额外兼容状态。

命中任一停止条件都保留现场并报告精确证据，不继续叠加补丁。
