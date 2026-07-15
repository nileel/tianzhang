# PowerShell 7 唯一运行时契约设计

## 1. 背景与结论

飞书决策通道实施前的基线验证发现：`tools/check-automation-workflow.ps1` 在 PowerShell 7 下通过，但按 `AGENTS.md` 现有命令调用 `powershell -File` 时，会落到系统自带 Windows PowerShell 5.1。该脚本使用无 BOM UTF-8 和中文正则字面量，5.1 按本机旧代码页解码后产生语法错误。

这不是脚本逻辑回归，而是项目运行时规则互相冲突：`开发管理/自动工作流规则.txt` 已要求自动工作流使用 `pwsh`，但 `AGENTS.md`、`CLAUDE.md`、部分活动管理文件、任务列表、测试脚本和飞书实施计划仍保留 `powershell -File`。

项目从本规格起只支持 PowerShell 7。所有活动规则、自动化入口、验证命令和脚本间调用统一使用 `pwsh -NoProfile -ExecutionPolicy Bypass -File ...`；不得再调用 `powershell` 或 `powershell.exe` 运行项目脚本。

## 2. 目标

1. 把 PowerShell 7 设为项目脚本唯一受支持运行时。
2. 消除活动规则和可执行测试中的 Windows PowerShell 5.1 调用。
3. 让关键公开入口在错误运行时下明确失败，而不是依赖乱码或偶然语法错误。
4. 用静态检查防止 `powershell -File` 重新进入活动事实源或脚本。
5. 不改写历史归档中的旧验证记录。

## 3. 非目标

- 不承诺 PowerShell 5.1 兼容性。
- 不为所有历史 `.ps1` 文件批量添加 BOM。
- 不机械改写 `开发管理/任务归档/`、`开发管理/AI合作归档/` 或已经完成的旧规格/旧计划。
- 不把 Markdown 代码围栏语言标记 `powershell`、普通名词“PowerShell”或类名/函数名视为运行时调用。
- 不改变现有脚本的业务行为、参数或退出码语义。

## 4. 方案比较

### 4.1 只替换 AGENTS.md 中的两条命令

改动最小，但活动任务列表、测试脚本和计划仍可重新调用 5.1，也没有回归门禁，不能形成底层契约。

### 4.2 活动入口统一并增加静态门禁（采用）

统一权威规则、当前队列、活动任务列表、当前实施计划和脚本间调用；关键入口声明 PowerShell 7 要求；新增 fixture 测试和仓库静态扫描。范围足以关闭问题，同时不篡改历史记录。

### 4.3 全仓历史文本机械替换

表面最彻底，但会修改过去真实使用 5.1 的验证证据，引入大面积无业务价值差异，因此拒绝。

## 5. 运行时契约

### 5.1 唯一调用形式

项目文档和自动化生成的独立进程命令统一为：

```text
pwsh -NoProfile -ExecutionPolicy Bypass -File <script> <arguments>
```

已在 PowerShell 7 进程内调用同一仓库脚本时，可以使用调用运算符：

```text
& tools/example.ps1 <arguments>
```

前提是父入口已经通过版本门禁。不得用 `powershell`、`powershell.exe`、PATH 别名或 Windows PowerShell 的绝对路径运行项目脚本。

### 5.2 版本门禁

以下高频或自动化公开入口添加 `#requires -Version 7.0`：

- `tools/automation-controller.ps1`
- `tools/automation-controller-state.ps1`
- `tools/check-automation-workflow.ps1`
- `tools/check-review-text.ps1`
- `tools/check-data-chain.ps1`
- `tools/check-pending-whitespace.ps1`
- `tools/run-unity-editmode-tests.ps1`

脚本内部需要启动子 PowerShell 时，必须使用当前 PowerShell 7 进程路径或显式解析 `pwsh`，并验证子进程 major version 不低于 7；不得硬编码 `powershell.exe`。

### 5.3 活动事实源

以下文件中的可执行命令必须统一为 `pwsh`：

- `AGENTS.md`
- `CLAUDE.md`
- `开发管理/开发-技术经验.txt`
- `开发管理/状态与建议维护规则.txt`
- `开发管理/自动工作流规则.txt`
- `开发管理/自动工作流控制器提示词.txt`
- `开发管理/当前任务队列.txt`
- `开发管理/任务列表/*.txt`
- `docs/superpowers/plans/2026-07-15-feishu-decision-channel-implementation.md`

`开发管理/开发-技术经验.txt` 明确写入硬规则：PowerShell 7 是唯一受支持版本；系统自带 5.1 只作为 Windows 组件存在，不得用于项目命令。

## 6. 静态门禁

新增 `tools/check-pwsh-runtime.ps1`，它只扫描明确列出的活动事实源和 `tools/**/*.ps1`，不递归扫描历史归档。

对 Markdown/txt 事实源，检查器按行识别真正的外部进程调用形态，例如：

```text
powershell -File ...
powershell -ExecutionPolicy Bypass -File ...
powershell.exe -NoProfile -File ...
& powershell ...
```

以下内容不得误报：

- Markdown 围栏语言标记 ```` ```powershell ````；
- 普通描述 `PowerShell 7`；
- 函数名 `Invoke-ChildPowerShell`。

对 `.ps1`，检查器使用 PowerShell AST parser，遍历 `CommandAst` 并拒绝静态命令名为 `powershell` 或 `powershell.exe` 的实际调用；字符串 fixture、注释、正则文本、函数名和普通描述不会被当作命令。脚本存在 parser error 时同样失败关闭。

检查器同时确认活动事实源包含唯一运行时声明，并确认关键入口首个有效声明区存在 `#requires -Version 7.0`。发现违规时输出项目相对路径、行号和固定 ASCII 错误类别，退出非零。

## 7. TDD 与验证

新增 `tools/test-check-pwsh-runtime.ps1`，先写失败 fixture 并观察红灯：

1. `powershell -File` 被拒绝。
2. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File` 被拒绝。
3. `& powershell` 被拒绝。
4. `pwsh -NoProfile -ExecutionPolicy Bypass -File` 通过。
5. Markdown ` ```powershell ` 和普通名词不误报。
6. 历史归档中的旧命令不参与活动扫描。
7. 缺少 `#requires -Version 7.0` 的关键入口被拒绝。

实现后按以下顺序验证：

```text
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-pwsh-runtime.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pwsh-runtime.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
```

随后重新运行飞书计划要求的 controller/state/decision-status 基线。任何组合验证必须逐项保存退出码，不能让后一条成功命令掩盖前一条非零退出码。

## 8. 与飞书实施的关系

该契约作为飞书计划的 Task 0，在任何 Node 桥接或控制器代码修改之前完成。飞书实施计划的技术栈改为只支持 PowerShell 7，并把全部 `powershell -File` 示例改为 `pwsh -NoProfile -ExecutionPolicy Bypass -File`。

PowerShell 7 契约通过两阶段子代理复审并提交后，重新取得干净基线；随后才恢复原 Task 1 的现场备份与迁移工作。

## 9. 回滚

若静态门禁误报，先暂停后续实现并修正扫描文件集或调用正则，不得通过恢复 `powershell` 命令绕过。若某个现有脚本在 PowerShell 7 下失败，按脚本真实兼容问题单独修复；不得以回退 5.1 作为解决方案。

## 10. 验收标准

- 活动规则和脚本不存在 Windows PowerShell 5.1 的项目脚本调用。
- `pwsh` 下原有基线和新门禁测试全部通过。
- 关键公开入口声明 PowerShell 7 最低版本。
- 静态门禁能拒绝 5.1 调用并允许文档语言标记与普通描述。
- 历史归档未被改写。
- 自动化仍保持暂停，隔离分支之外的用户改动保持不变。
