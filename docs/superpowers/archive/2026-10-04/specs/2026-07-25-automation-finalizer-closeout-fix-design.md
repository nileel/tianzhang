# 自动化归档提交与闭环判定修复设计

状态：已批准，已实施。

## 问题

Codex 自动化责任方归档任务卡时，活跃卡删除与归档卡新增可能被 Git 识别为重命名。`automation-finalize-commit.ps1` 在暂存后按默认重命名检测校验逐路径变化，可能只看到重命名目标而拒绝源路径；失败后已经暂存的删除又会被当前 `Test-PathChanged` 忽略。责任方因此可能把一次任务闭环拆成两个 Automation 提交。

`invoke-codex-responsibility.ps1` 只接受每轮恰好一个新 Automation 提交，但其 `rev-list` 输出没有在 `-split` 前加括号。出现多个提交时，多个 SHA 会作为一个多行字符串进入正则并被全部过滤，最终误报 `no_verified_outcome`，而不是现有契约定义的 `unverified_commit_shape`。

## 约束与成功标准

- 保持每轮责任方恰好一个 Automation 提交的既有约束。
- 不扩展 finalizer 参数，不引入新的归档协议、状态或重试层。
- 活跃任务卡删除与归档卡新增必须能够在同一次 finalizer 调用中提交。
- 多提交仍然不得判定为成功，但必须稳定报告 `unverified_commit_shape`。
- 不修改已经完成的任务内容、任务卡生命周期语义或 automation runtime。

## 设计

### Finalizer 路径判定

`Test-PathChanged` 先直接检查目标路径的 staged diff 与 unstaged diff。只在两者均无变化时，才通过 `git ls-files` 与文件存在性判断未跟踪新增。这样即使删除已经进入索引、文件在工作区中不存在，也会被识别为待提交变化。

限定路径中仍有 unstaged 差异或属于未跟踪新增的文件继续使用现有 `git add -- <paths>` 暂存；已经从索引删除且工作区不存在的源路径不重复暂存，保留其现有 staged 删除。暂存后的逐路径校验固定对 `git diff --cached --name-only` 使用 `--no-renames`。删除源和新增目标因此分别出现，并能逐项对应 `ExpectedPaths`。提交仍使用现有 `git commit --only` 与路径集合，不改变无关暂存项隔离。

### 固定调用器提交枚举

先完整调用 `Invoke-GitText` 取得 `rev-list` 文本，再对返回值执行 `-split '\r?\n'`。除此之外不改变提交元数据筛选与成功条件：

- 一个匹配提交、工作区无新增残留且任务后置条件通过：成功。
- 一个或多个新提交但不满足上述唯一形状：`unverified_commit_shape`。
- 没有提交、恢复或工作区变化：沿用 `no_verified_outcome`。

## 错误处理

本修复不增加自动恢复。Finalizer 仍在校验失败时返回非零；固定调用器仍保留现场并按现有分支记录结果。修复的目标是消除已确认的错误判定，使正常归档一次成功，并使违规多提交得到准确分类。

## 测试

在 `test-automation-finalize-commit.ps1` 增加高相似度任务卡归档夹具：删除活跃卡、新增归档卡，并让删除预先进入索引。断言一次 finalizer 调用成功、同一提交同时包含删除与新增、索引中没有相关残留。

在 `test-invoke-codex-responsibility.ps1` 增加责任方产生两个新提交且工作区干净的夹具。断言固定调用器返回非成功、`detailCode=unverified_commit_shape`，并把同一分类写入 runtime。

实施后只运行：

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1`
- 本轮预期路径的 `tools/check-pending-whitespace.ps1`
- `git diff --check`

## 非目标

- 不放宽单提交约束。
- 不修改自动化 schedule、prompt 或状态。
- 不返工已归档任务或改写历史提交。
- 不重构 finalizer、固定调用器或任务生命周期。
