# Unity 只读运行时探针设计

状态：已实施 / 待集成
日期：2026-08-05
适用范围：当前打开的 Unity 项目 `src/`，以及从仓库根目录发起查询的 PowerShell 客户端

## 目标

让本地 AI 或自动化客户端无需操作 Unity 界面即可读取当前 Editor 和 Play Mode 的运行时状态，包括播放状态、已加载场景、运行时层级、组件列表、Transform 与 Inspector 可序列化字段。探针严格只读，不进入或退出 Play Mode，不选择、创建、修改或销毁对象，也不调用任意项目方法。

## 方案选择与前置约束

采用 `src/Library/UnityRuntimeProbe/` 下的 JSON 请求/响应协议。

- 相比本地 HTTP/TCP，它不增加监听端口、后台服务、认证或端口生命周期。
- 相比启动 BatchMode，它能够查询当前已经打开的 Editor 内存状态，也不会与项目锁冲突。
- `Library/` 已由根 `.gitignore` 忽略，运行请求、响应和临时文件不会成为项目资产或 Git 改动。

硬前置条件：Unity Editor 必须已经打开客户端 `ProjectPath` 指向的同一个项目，并且探针脚本已经编译完成。打开其他项目的 Editor 不会轮询该 `ProjectPath/Library`，因此客户端必须超时失败，不能把“系统中存在 Unity 进程”当成连接成功。

## 进程与项目互认

不新增第四种协议动作；`status` 同时承担握手。客户端每次调用都校验 Editor 身份：

1. 客户端将 `ProjectPath` 解析为规范化绝对路径，并从 `ProjectSettings/ProjectSettings.asset` 读取 32 位十六进制 `productGUID`。
2. `Status` 调用直接发送一次 `status` 请求；`Hierarchy` 和 `Inspect` 调用必须先发送一次独立的 `status` 请求，握手通过后才发送实际请求。
3. Editor 的每个响应都回显 `processId`、`processStartTimeUtc`、Editor 侧绝对 `projectPath`、从该项目 `ProjectSettings/ProjectSettings.asset` 读取并规范化为小写的 32 位 `productGUID`，以及 `Application.unityVersion`。Unity 6 的 `PlayerSettings.productGUID` 是 `System.Guid` 表示，与 `ProjectSettings.asset` 的序列化文本不是可直接比较的同一字符串，因此身份字段不得由 `PlayerSettings.productGUID.ToString()` 生成。
4. 客户端要求响应的规范化 `projectPath` 与请求 `ProjectPath` 在 Windows 上使用 `OrdinalIgnoreCase` 完全相等；将响应与本地 ProjectSettings 的 `projectGuid` 都转为小写后完全相等；并确认 `processId` 当前仍是同一启动时间的 `Unity` 进程。Editor 将 `Process.StartTime.ToUniversalTime()` 以 ISO-8601 round-trip 格式写入响应；客户端必须使用 `(Get-Process -Id <pid>).StartTime.ToUniversalTime()`，再将两者解析为 UTC ticks 比较，禁止比较本地时间字符串。
5. 非 Status 请求的最终响应还必须与握手响应具有完全相同的 `processId`、`processStartTimeUtc`、`projectPath` 和 `projectGuid`；中途 Editor 重启则返回 `editor_changed`，不输出对象数据。

任一身份检查失败均返回非零退出码。由此，文件存在只表示传输发生，只有“项目路径 + 项目 GUID + Unity PID + 进程启动时间”一致才表示连接到了目标 Editor。

两端使用等价的 `NormalizeProjectPath`：先对输入字符串执行 `TrimEnd()` 去掉尾部空白，再调用 `Path.GetFullPath`；随后把 `Path.AltDirectorySeparatorChar`（Windows 上为 `/`）全部替换为 `Path.DirectorySeparatorChar`（Windows 上为 `\`）；最后在不删除卷根分隔符的前提下去掉多余尾部分隔符。Unity 侧先从 `Application.dataPath` 取父目录再执行该函数，PowerShell 侧对解析后的 `ProjectPath` 执行同一过程。不得直接比较 `Application.dataPath` 风格的正斜杠字符串与 PowerShell 反斜杠字符串。`productGUID` 两端均先验证为 32 位十六进制，再用 `ToLowerInvariant()` 归一化。

## 组件与边界

### Editor 端

新增 `src/Assets/Scripts/Editor/UnityRuntimeProbe.cs` 及对应 `.meta`。该文件由现有 `TianZhang.Editor` asmdef 收纳；该 asmdef 已经依赖 `TianZhang.Gameplay`，但探针源码只允许引用 System、UnityEditor、UnityEngine 及其子命名空间，不得引用 Foundation、Domain、Combat、Gameplay 或任何项目业务类型。文件头固定写入 `// Editor-only diagnostics: do not reference project assemblies or business types.`，首版不为单个诊断文件新增独立 asmdef；该约束由验证节的机械检查兜底，不只依赖注释或 code review。

编辑器通过 `[InitializeOnLoad]` 注册 `EditorApplication.update` 回调，并遵守以下主线程约束：

- 最多每 100 毫秒轮询一次请求目录。
- 静态 `isProcessing` 互斥标记在处理前置位，并在 `finally` 中复位；标记为 true 时回调立即返回。
- 每轮最多清理 32 个过期、无效或孤儿通道文件，随后最多处理 1 个有效请求；并发客户端的请求按 `createdAtUtc`、再按 `requestId` 排序后串行执行。
- 一个请求完成后才允许下一次轮询，不并行执行场景遍历或 `SerializedObject` 读取。
- 没有请求时只做有界目录枚举，不读取播放状态、不遍历场景、不创建 `SerializedObject`。`status`、`hierarchy` 和 `inspect` 的状态/场景查询只在对应请求被选中后执行。

只支持三个白名单动作：

1. `status`：返回 `isPlaying`、`isPaused`、`isCompiling`、活动场景和全部已加载场景，不遍历 GameObject。
2. `hierarchy`：按可选场景名和名称片段查询当前已加载场景中的 GameObject，返回 instance ID、场景、完整层级路径、激活状态、hide flags 和组件类型。默认只返回 active-in-hierarchy 对象；`includeInactive` 可显式包含未激活对象。按场景加载顺序、根对象 sibling 顺序和深度优先层级顺序遍历，到达上限即停止；`maxResults` 默认为 100，范围为 1 到 200。
3. `inspect`：按 `instanceId`，或按“场景名 + 完整层级路径”定位一个对象。若路径不唯一则返回 `ambiguous_target`，不得猜选。返回 Transform 和各组件的 Inspector 可序列化属性。

`inspect` 使用 `SerializedObject`/`SerializedProperty` 读取 Inspector 数据，不遍历任意 CLR 属性、不执行 getter，也不开放方法调用。每个属性输出 `propertyPath`、`propertyType` 和有界值；对象引用只输出引用对象名称、类型和 instance ID，数组只输出长度、不展开元素。每个对象最多返回 32 个组件、每个组件最多 128 个属性、全部组件合计最多 512 个属性；单个字符串值最多 1024 个 UTF-16 code unit，超出部分截断。任何上限命中都在对应层级标记 `truncated: true`。

### PowerShell 客户端

新增 `tools/get-unity-runtime-snapshot.ps1`。`ProjectPath` 默认解析为脚本相邻的 `../src`，也允许显式传入；调用前要求该目录同时存在 `Assets/`、`ProjectSettings/ProjectSettings.asset` 和 `Library/`。公开调用形态：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/get-unity-runtime-snapshot.ps1 -Action Status
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/get-unity-runtime-snapshot.ps1 -Action Hierarchy -Scene AdventureScene -NameContains Player -IncludeInactive -MaxResults 100
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/get-unity-runtime-snapshot.ps1 -Action Inspect -InstanceId 12345
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/get-unity-runtime-snapshot.ps1 -Action Inspect -Scene AdventureScene -HierarchyPath /Root/Child
```

客户端生成小写 32 位十六进制 GUID 请求 ID，将请求先写入 `requests/.<requestId>.<clientPid>.tmp`，关闭文件后在同目录原子改名为正式请求。`TimeoutSeconds` 默认 5 秒、范围为 1 到 30 秒，并分别应用于握手请求和实际请求；每个请求自己的 `expiresAtUtc` 与该次等待截止时间一致，不增加自动重试层。

客户端等待匹配响应、完成身份校验后输出单个 JSON 对象。`finally` 只清理本请求 ID 对应的请求、响应和临时文件；超时时也清理自己的文件，不清理其他客户端文件。不同对话可以并发发起请求，但 Editor 端始终串行处理。所有删除路径统一采用幂等 `TryDeleteFile` 语义：文件或其父目录已经不存在时视为删除成功；捕获 `FileNotFoundException` 和 `DirectoryNotFoundException` 后继续，不能中断当前响应或清理流程，其他 I/O/权限异常才按真实错误处理。

## 文件名与请求协议

请求和响应固定为 `schemaVersion: 1`。

正式文件名：

```text
requests/<requestId>.json
responses/<requestId>.json
```

`requestId` 必须匹配正则 `^[0-9a-f]{32}$`，JSON 内的 `requestId` 必须与文件名完全一致，否则返回 `request_id_mismatch` 后删除请求。请求文件最大 64 KiB。

请求字段固定为：

```json
{
  "schemaVersion": 1,
  "requestId": "32位小写十六进制GUID",
  "clientProcessId": 1234,
  "createdAtUtc": "ISO-8601 round-trip UTC",
  "expiresAtUtc": "ISO-8601 round-trip UTC",
  "action": "status | hierarchy | inspect",
  "scene": null,
  "nameContains": null,
  "includeInactive": false,
  "maxResults": 100,
  "instanceId": null,
  "hierarchyPath": null
}
```

动作约束：

- `status` 不使用选择器字段。
- `hierarchy` 可使用 `scene`、`nameContains`、`includeInactive`、`maxResults`；名称比较使用 `OrdinalIgnoreCase`。
- `inspect` 必须且只能使用一种选择器：非零 `instanceId`，或同时提供非空 `scene` 与以 `/` 开头的 `hierarchyPath`。同时提供两种或两种都未提供均返回 `invalid_selector`。
- `expiresAtUtc` 必须晚于 `createdAtUtc`，且间隔为 1 到 30 秒；Editor 当前 UTC 时间超过它时直接删除请求，不执行 action，也不生成响应。

响应至少包含：

```text
schemaVersion
requestId
status: ok | error
generatedAtUtc
editor: processId, processStartTimeUtc, projectPath, projectGuid, unityVersion,
        isPlaying, isPaused, isCompiling, activeScene
scenes
objects
error: null | { code, message }
```

Editor 先写入 `responses/.<requestId>.<editorPid>.tmp`，关闭文件后原子改名为正式响应；响应落盘后删除对应请求。未知 JSON 字段可忽略，未知 schema 或 action 必须失败关闭。Hierarchy 只接受 `Scene.IsValid && Scene.isLoaded` 的对象，因此不会把 Project 资产当成场景实例。Editor 删除已响应请求、客户端 finally/超时清理、过期请求清理以及 60 秒孤儿文件清理均调用同一幂等删除语义，允许多个删除方竞争同一路径。

损坏、超大、文件名非法或时间字段非法的请求不得进入场景查询；若文件名足以获得可信 request ID，则返回协议错误后删除，否则只记录有界 Editor 警告并删除。客户端崩溃遗留的正式响应和 `.tmp` 文件在最后写入时间超过 60 秒后也视为孤儿文件。每轮在所有通道目录合计最多清理 32 个过期、无效或孤儿文件，防止 Editor 恢复后一次处理大量积压。

## `.meta` 生成与提交

实现不依赖主工作区 Unity 自动生成 `.meta`。在隔离 worktree 中先生成新的随机 32 位十六进制 GUID，使用标准 `MonoImporter` 格式创建 `UnityRuntimeProbe.cs.meta`，再通过仓库级 `rg` 确认该 GUID 未出现在其他 `.meta`、场景、Prefab 或资产中。`.cs` 与 `.cs.meta` 必须在同一提交中进入 Git；合并后 Unity 使用已提交 GUID 导入，不产生新的未跟踪 `.meta`。

## 运行时所有者与非侵入性

探针属于现有 `src/Assets/Scripts/Editor/` 编辑器工具边界，只读取 `EditorApplication`、`SceneManager`、`ProjectSettings.asset` 和当前加载对象。虽然承载它的 Editor assembly 具有既存 Gameplay 编译依赖，但探针不使用该依赖，也不成为任何游戏场景、Prefab、`GameSession`、`SceneFlowManager`、`AdventureSceneController` 或 Runtime assembly 的依赖。

不会修改场景 dirty 状态、Prefab override、Selection、Play Mode、时间缩放、输入、相机、UI 或存档。运行时文件全部位于 `Library/`；关闭 Unity 后这些文件可以直接丢弃。

## 错误与限制

- Editor 未打开同一项目、脚本尚未编译或程序集正在重载：客户端以 `editor_not_connected` 超时失败。
- Editor 身份与 `ProjectPath`/ProjectSettings 不一致：返回 `editor_identity_mismatch`；握手后 Editor 改变：返回 `editor_changed`。
- Editor 正在编译：回调恢复前不会响应；请求超过 `expiresAtUtc` 后由 Editor 丢弃，客户端到期清理自己的文件并失败。本实现不增加重试。
- 找不到对象：返回 `target_not_found`；路径重复：返回 `ambiguous_target` 和有界候选，不自动选择。
- 非 Inspector 序列化的私有运行时字段、托管对象深层图、静态字段和属性 getter 不在首版范围。
- 本探针提供运行时数值证据，但不能替代 Game View 截图对最终视觉输出的验证。

## 实施文件

计划只修改或新增：

- `src/Assets/Scripts/Editor/UnityRuntimeProbe.cs`
- `src/Assets/Scripts/Editor/UnityRuntimeProbe.cs.meta`
- `tools/get-unity-runtime-snapshot.ps1`
- `tools/test-get-unity-runtime-snapshot.ps1`
- 本设计稿

不修改场景、Prefab、asmdef、Gameplay 代码、项目配置或自动工作流。

## 验证

1. PowerShell 协议测试使用假响应端覆盖原子请求、字段/文件名一致性、正斜杠与反斜杠项目路径等价、`productGUID` 大小写等价、本地 StartTime 转 UTC 后等价、错误项目/PID/启动时间拒绝、Editor 不可用超时、错误响应、过期请求、两个并发请求 ID 隔离、多个删除方竞争同一文件以及客户端 finally 清理。
2. `dotnet build src/TianZhang.EditModeTests.csproj` 只验证现有 Foundation、Domain、Combat、Gameplay、Editor 与测试程序集的编译链；它不证明 `SerializedObject` 对当前 Editor 实例的读取结果、主线程耗时、文件轮询或 Play Mode 行为。
3. 合并到当前 Editor 所在主工作区并等待脚本编译后，运行 `Status`，核对回显的项目路径、ProjectSettings `productGUID`、Unity PID 与进程启动时间；随后运行 `Hierarchy`，再用其 instance ID 运行 `Inspect`。实际 Editor 冒烟测试才作为文件通道和 `SerializedObject` 行为证据。
4. 在 Edit Mode 与 Play Mode 分别执行一次 `Status`、`Hierarchy`、`Inspect`；若本轮无法进入其中一种状态，必须明确报告该状态未验证，不能由编译通过替代。
5. 并发启动两个客户端请求，确认 Editor 串行返回且身份一致；放置一个已过期请求，确认它被删除而未执行。
6. 查询前后核对目标场景不产生 dirty 状态，`git status --short` 不出现 `Library/` 或场景/Prefab 改动。
7. 在 `tools/test-get-unity-runtime-snapshot.ps1` 中对 `src/Assets/Scripts/Editor/UnityRuntimeProbe.cs` 执行机械边界检查：用 `rg '^\s*using\s+'` 取得全部 using 行，只允许 `System`、`UnityEditor`、`UnityEngine` 根命名空间及其子命名空间；`using static` 的根命名空间同样受该白名单约束，不允许别名或 `using static` 绕过；再用 `rg '\bTianZhang(?:\.|\b)'` 确认文件中不存在当前项目的 `TianZhang.*` 业务命名空间引用。任一命中或无法执行 `rg` 都使测试失败，不能以编译通过或 code review 替代。
8. 核对新 `.meta` GUID 唯一；对预期路径运行 `tools/check-pending-whitespace.ps1`，并运行 `git diff --check`。

## 完成标准

当前打开的目标 Unity Editor 无需 computer use，即可通过一条 PowerShell 命令返回通过项目与进程互认的机器可读实时状态；并发和过期请求行为确定；失败时有明确原因；所有查询保持只读，且不触碰非请求系统。
