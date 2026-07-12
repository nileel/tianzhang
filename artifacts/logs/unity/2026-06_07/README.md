# Unity 验证日志归档（2026-06 至 2026-07）

本目录保存 Unity 6 升级、TQ-016 与 TQ-019 的批处理编译和 EditMode 测试历史输出，以及一次通用 batchmode 验证。它们是当时的诊断证据，不代表当前构建状态；请以重新运行的测试与提交记录为准。

## 文件分组

- `unity6-upgrade.log`：Unity 6 升级验证。
- `unity-tq016-*.log`：TQ-016 从失败（`red`）到通过（`green`）的验证链。
- `unity-test-tq019*.log`：TQ-019 的 EditMode 测试及重跑记录。
- `unity-batchmode-verify.log`：通用 Unity batchmode 验证。

## 后续约定

临时 Unity 日志应直接输出到 `artifacts/logs/unity/YYYY-MM/`。只有需要保留可复现诊断证据的日志才保留；`.log` 文件继续由根目录 `.gitignore` 忽略，说明文件可提交。
