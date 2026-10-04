# 自动提交器 Unity `.meta` 空值尾随空格修复设计

日期：2026-08-25
状态：已批准并实施
范围：共享自动提交器与直接回归测试；不处置现有活动 run。

## 问题

`tools/check-pending-whitespace.ps1` 已把下列三种 Unity `.meta` 空值行识别为合法语义，并拒绝其他普通尾随空格：

- `  userData: `
- `  assetBundleName: `
- `  assetBundleVariant: `

但 `tools/automation-finalize-commit.ps1` 随后仍以 Git 默认 whitespace 配置运行 `git diff --cached --check`。Git 将上述合法行报告为 `blank-at-eol`，导致候选阶段若临时覆盖 Git 配置便可提交，而共享入口在正常配置下正式重放时失败。当前失败 run 的直接结果是 `hourly_formal_commit_failed`。

## 决策

只修改 `tools/automation-finalize-commit.ps1` 内现有的暂存差异检查，使该 Git 子进程显式使用：

```text
-c core.whitespace=-blank-at-eol
```

配置作为该次 Git 调用的命令参数传入，不写环境变量，不修改系统、仓库或 worktree Git 配置。调用仍在现有 `check-pending-whitespace.ps1` 成功之后执行。

由此形成单一门禁口径：

1. 专用检查器逐文件拒绝普通尾随空格，并只豁免三种精确的 Unity `.meta` 空值行。
2. Git 检查不再重复报告 `blank-at-eol`，但继续执行其余已启用的 whitespace 检查。
3. 候选提交和共享入口正式提交调用同一 finalizer，不再需要调用方提供临时配置。

## 不采用的方案

- 不解析并过滤 `git diff --check` 文本输出；该方案依赖 Git 输出格式，增加脆弱的路径和行文本匹配。
- 不在 `.gitattributes` 中对全部 `.meta` 设置宽泛豁免；这会把本地提交语义扩散到整个仓库，并可能放过其他非法 `.meta` 尾随空格。
- 不删除三种空值行的尾随空格；项目现有 Unity 事实明确要求保留这些字节。
- 不保留候选责任方使用的环境变量绕过；调用方不应改变共享提交合同。

## 修改边界

允许修改：

- `tools/automation-finalize-commit.ps1`
- `tools/test-automation-finalize-commit.ps1`
- 本设计文档

不修改：

- `tools/check-pending-whitespace.ps1` 及其既有精确豁免逻辑
- `tools/invoke-hourly-owner.ps1`、runtime、自动化配置和通知逻辑
- 任何任务卡、队列、Unity 业务资产或候选提交
- run `39b0fe6a-30fd-4372-bb5c-246b8311a4a9` 的状态、worktree、branch、index 或提交

## 回归测试

在 `tools/test-automation-finalize-commit.ps1` 增加 finalizer 级夹具，证明：

1. 包含三种精确空值行的 `.meta` 文件可以由 finalizer 正常提交，提交后字节保持不变。
2. 普通文本文件的行尾空格仍被 finalizer 拒绝。
3. `.meta` 中错误扩展名、错误缩进、额外尾随空格或非允许键等相似形态仍被拒绝；可复用专用检查器现有边界，不另建第二套解析器。
4. finalizer 不留下 `core.whitespace` 的系统、仓库或 worktree 配置变化。

最小验证集：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-pending-whitespace.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-finalizer-invocation.ps1
```

随后对本轮预期路径运行 `tools/check-pending-whitespace.ps1`，暂存后运行 `git diff --cached --check`。本轮不改设定正文、CSV 或 Unity 数据链，不运行 `check-data-chain.ps1`、Unity 测试或 BattleSim。

## 完成条件

- 上述三项定向测试全部通过。
- 合法 Unity `.meta` 空值行在 finalizer 中通过且字节不变。
- 普通尾随空格及非法 `.meta` 相似形态继续失败。
- 没有持久 Git 配置变化，没有扩大文件或 runtime 职责。
- 当前失败 run 仍保持原样；其人工收口属于独立、后续明确授权的任务。
