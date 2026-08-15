# 《天章》Blender MCP 与项目专用 Skill 集成设计

> 日期：2026-08-15
> 状态：方案 A 已获用户选择；本文待用户复核后方可实施
> 范围：只建立 Blender 辅助操作与 FBX 质检能力，不生产、修改或导入正式角色资产

## 一、决策摘要

采用三层最小组合：

1. Blender Lab 官方 MCP `v1.0.0` 负责连接正在运行的 Blender，读取场景、截图、导航并在明确授权后执行 Blender Python。
2. 仓库级 `tianzhang-blender-pipeline` skill 负责把项目事实源、工具选择、安全边界和停止条件应用到每次 Blender／FBX 任务。
3. `ellmos-blender-use-mcp@0.1.0-alpha.4` 只负责无界面定位 Blender 与 FBX 重导检查，不承担实时编辑，也不开放其任意脚本工具。

不安装 `arjun988/blender-skills` 全量包，不接入 Meshy、Tripo、Poly Haven 或其他远程资产服务，不以 `ahujasid/blender-mcp` 等社区实时控制器替代官方 MCP。现有项目规则已经冻结 Blender `-Y` 正面、FBX `-Z Forward / Y Up`、T Pose、单位、原点和 39 骨合同，通用技能包中的不同约定不能成为第二事实源。

## 二、事实基线与当前阻塞

### 2.1 本机与工具事实

- Blender 已固定为 `D:\Tools\Blender\5.2.0\blender.exe`，版本 `5.2.0 LTS`，build hash `fbe6228777e7`。
- Blender 自带 Python 位于 `D:\Tools\Blender\5.2.0\5.2\python\bin\python.exe`，当前为 Python `3.13.13`，具备 `pip` 与 `venv`。
- 当前 Codex 尚未配置 Blender MCP，仓库中也没有 `.agents/skills/tianzhang-blender-pipeline/`。
- Node.js 与 `npx` 已可用；本机 npm 默认 registry 是第三方镜像，因此本集成不得继承该默认值，ellmos 安装／启动必须显式使用 `https://registry.npmjs.org`。
- Blender Lab 官方 MCP `v1.0.0` 发布物包含 Blender 扩展 `mcp-1.0.0.zip` 与 MCP 服务端 `blender-1.0.0.mcpb`。

### 2.2 项目事实与阻塞

- `A-CHAR-HUMANOID-RIG-01` 仍为 `dispatchState=blocked`。阻塞原因是 Git 忽略的 `assets/source/` 与 linked worktree 清理之间尚无获批的原始资产持久化、编辑所有权边界，不是 Blender 安装问题。
- 本集成不得改变该任务卡、队列、route、owner 或 dispatchState，也不得借工具冒烟测试创建正式 `.blend`、FBX 或证据文件。
- 项目空间与导出合同仍以 `docs/superpowers/specs/2026-08-08-humanoid-evaluation-rig-design.md` 为准：Blender `-Y` 正面、`+Z` 向上、`1 unit = 1 m`、双脚底部中心原点、T Pose；FBX 使用 `Forward=-Z`、`Up=Y`。
- `src/Assets/` 不在本集成写入范围内。所有冒烟产物只能位于明确的临时目录。

## 三、候选方案与取舍

### 3.1 方案 A：官方实时 MCP + 项目 Skill + ellmos 质检

优点是交互编辑、项目规则和确定性 FBX 重导各有单一职责；实时会话和无界面质检互不冒充。缺点是需要两个 MCP 条目，并且 Blender Lab 扩展需要本地 TCP 与 Blender Online Access。

本设计采用此方案。

### 3.2 方案 B：官方实时 MCP + 项目 Skill

组件更少，但 FBX 往返需要继续依赖手写命令或官方任意代码工具，重复验证的结构化输出较弱。

### 3.3 方案 C：ellmos 质检 + 项目 Skill

无常驻 socket，安全面更窄，但不能检查当前 Blender UI、截图或进行实时场景编辑，不满足本次“调用 Blender”的完整目标。

## 四、架构与职责

```text
用户任务
  -> 仓库级 tianzhang-blender-pipeline skill
       -> 读取项目事实源与当前任务卡
       -> 只读/实时编辑 -> Blender Lab MCP -> localhost:9876 -> Blender 5.2 扩展
       -> FBX 重导质检 -> ellmos MCP -> blender.exe --background
       -> 命中资产所有权或正式 Unity 边界 -> 停止并报告
```

### 4.1 Blender Lab 官方 MCP

官方 MCP 的数据链为 Codex `stdio` -> `blender-mcp` -> 本机 TCP -> Blender 扩展。服务端可读取场景摘要、对象、缺失文件、链接库、Python API 文档和截图，也能执行 Blender Python。

该组件只承担：

- 当前 Blender 场景的只读检查与截图；
- 视图、工作区和对象定位；
- 用户已授权范围内的临时场景或正式任务场景编辑；
- 必要时渲染临时缩略图或视口证据。

`execute_blender_code` 视为任意本地代码执行能力。官方扩展中的 weak sandbox 不是项目安全边界，不能替代路径核对、用户授权和项目停止条件。

### 4.2 项目专用 Skill

skill 固定放在：

```text
.agents/skills/tianzhang-blender-pipeline/
```

首版只包含：

```text
SKILL.md
agents/openai.yaml
references/tool-routing.md
```

不在首版增加包装脚本、重试层、自动导出器或第二套状态。skill 使用 `skill-creator` 的初始化与校验工具生成基本结构，再做最小内容修改。

skill 的职责是：

1. 仅在《天章》仓库内处理 Blender、FBX、人形评估骨架、3D 资产修整或重导验证时触发。
2. 先按 `AGENTS.md` 和任务卡的“必查范围”定位事实源，不把骨名、动作帧、路径等大段事实复制进 skill。
3. 将请求分成只读检查、实时编辑、FBX 重导质检三类，并选择唯一对应工具。
4. 每次写入前解析绝对目标路径，检查当前任务授权、活动 run、集成锁和正式资产阻塞。
5. 命中 `assets/source/` 持久化阻塞、`src/Assets/` 禁止项、导出合同不成立或第二流程需求时立即停止，不增加兼容分支。
6. 报告实际工具、输入、输出、验证和残留风险；不以 MCP 成功响应替代 Blender／FBX／Unity 的业务验收。

### 4.3 ellmos Headless QA MCP

ellmos 只开放：

- `blender_locate`
- `blender_verify_fbx_reimport`

禁用 `blender_run_script`。任意 Blender Python 已由官方 MCP 在更严格的项目路由下承担，无需复制同类入口。

ellmos 每次调用独立启动 `blender.exe --background`，用于验证 FBX 能否重导，并返回网格、材质和命名前缀等结构化结果。它不能证明项目 39 骨层级、rest pose、轴向、动作范围或 Unity Humanoid 映射；这些仍由目标任务卡指定的 `TZ_Validate`、factory startup 回读和临时 Unity 验收证明。

## 五、版本、来源与安装边界

### 5.1 固定版本与完整性

| 组件 | 固定来源 | 固定版本／完整性 |
|---|---|---|
| Blender Lab 扩展 | `https://projects.blender.org/lab/blender_mcp/releases/tag/v1.0.0` | `mcp-1.0.0.zip`，SHA-256 `838C3449F01015C861290658AE67F122F0846F7882F60A5DFDA0EF7E6A9B8403` |
| Blender Lab MCP 服务端 | 同一官方 release | `blender-1.0.0.mcpb`，SHA-256 `93B070B1DF82F57B1E7678B88B6BAE28D06F105CD23FF6A4E0CC5F538BEE2450` |
| ellmos QA MCP | `https://registry.npmjs.org/ellmos-blender-use-mcp` | `0.1.0-alpha.4`，integrity `sha512-7w8ljmIJcqZHQKGGR2Xl+T1Fa9gAdinTn6O5QjUNjihXe74lUmlpYupCoZ3cJsH9uaWaDjSVHRsjMVKcsNgn8Q==` |

实现时重新下载并核对，不信任本次设计研究产生的临时副本。任何哈希或 integrity 不一致都停止。

### 5.2 仓库内变更

实施阶段允许新增的仓库路径仅为：

```text
.agents/skills/tianzhang-blender-pipeline/SKILL.md
.agents/skills/tianzhang-blender-pipeline/agents/openai.yaml
.agents/skills/tianzhang-blender-pipeline/references/tool-routing.md
```

本设计文档不批准创建 `.codex/config.toml`、修改 `AGENTS.md`、修改任务卡／队列，或加入生成资产。

### 5.3 本机变更

允许的机器级变更为：

1. 在 Blender 5.2 用户扩展中从已校验的 `mcp-1.0.0.zip` 安装并启用官方 `MCP` 扩展。
2. 在 `C:\Users\WINDOWS\.codex\mcp-servers\blender-lab\1.0.0\` 解包已校验的 MCPB，并使用 Blender 自带 Python 建立独立 `.venv`、安装服务端及其依赖；安装完成后记录 `pip freeze`。
3. 在用户级 `C:\Users\WINDOWS\.codex\config.toml` 新增 `blender_lab` 与 `blender_headless_qa` 两个 MCP 条目，保留所有现有配置。
4. ellmos 通过固定版本的 `npx` 命令启动，显式把 registry 设为 `https://registry.npmjs.org`，并把 `BLENDER_EXE` 固定为项目已验证路径。

不修改系统 PATH、npm 全局 registry、默认 Python、Blender 安装目录或 Unity 项目。

## 六、连接与审批安全合同

### 6.1 Blender 扩展

- Host 固定 `localhost`，端口固定官方默认 `9876`；不得绑定 `0.0.0.0`、局域网地址或公网地址。
- 官方扩展默认 Auto Start 为开；安装后必须关闭，只有当前 Blender 辅助任务期间人工启动，任务完成后停止。
- 扩展要求 Blender Online Access。实施时必须明确展示这一前置及影响；只为当前会话启用，不能把它解释为允许远程资产下载或联网生成。
- 默认关闭请求日志；只有排障时临时开启，避免把场景信息长期写入终端日志。

### 6.2 Codex MCP 配置

- `blender_lab` 设置 `default_tools_approval_mode = "writes"`；官方 `execute_blender_code` 已声明 destructive hint，只读摘要与截图工具已声明 read-only hint，按工具注解区分能避免每次只读检查都弹出审批。
- `blender_lab` 使用 allowlist，只开放当前 Blender 实例的摘要、对象、文档、截图、导航与 `execute_blender_code`；不开放所有 `_for_cli` 工具和两个写路径渲染工具，避免它绕过 ellmos 分工或依赖不充分的 read-only 注解写盘。
- `blender_headless_qa` 使用 allowlist，只暴露 `blender_locate` 与 `blender_verify_fbx_reimport`。
- MCP 注解与审批模式只提供外层保护；skill 仍必须独立检查绝对路径、正式资产边界和用户请求范围。
- 两个 MCP 都不接收 API key、云资产凭据或项目私密生成素材。
- MCP 连接失败时只报告根因；不自动安装第三个控制器、不开放额外端口、不切换导出预设。

### 6.3 文件写入

- 首次冒烟测试只使用 `D:\Temp\TianZhang-Blender\MCP-SMOKE-<timestamp>\` 下的新临时文件。
- 未保存场景、已存在目标、正式 `.blend`／FBX、递归删除和覆盖保存都必须先解析并展示绝对路径，再取得与请求相称的明确授权。
- 当前不得向 `assets/source/characters/humanoid-evaluation/` 或 `src/Assets/` 写入任何文件。
- 工具运行产生的临时文件只能在验证确切目录后清理；清理结果需报告是否可恢复。

## 七、实施顺序

1. 重新检查 `git status`、schema 5 `Show`、集成锁和现有 MCP 配置；发现路径冲突即停止。
2. 使用 `skill-creator` 在独立 worktree 初始化仓库级 skill，写入事实源路由、工具路由和停止条件，运行结构校验。
3. 从 Blender 官方 release 重新下载两个 `v1.0.0` 发布物并核对 SHA-256。
4. 使用 Blender UI 从磁盘安装官方扩展；确认 `localhost:9876`、关闭 Auto Start，并在临时场景中手动启动。
5. 用 Blender 自带 Python 创建隔离 venv，安装 MCPB 服务端；通过 `codex mcp add` 或等价的最小配置写入 `blender_lab`，再核对实际配置。
6. 以固定 npm 官方 registry 和固定版本写入 `blender_headless_qa`，设置 `BLENDER_EXE` 与工具 allowlist。
7. 重启或新开 Codex 任务，使新增 skill 和 MCP 工具重新发现；不得以当前任务未热加载为由修改第二套发现机制。
8. 完成临时场景与临时 FBX 冒烟验证，核对项目工作区零资产改动。
9. 只提交 skill 的三份仓库文件；机器级安装结果在交付说明中列出，不把用户配置或依赖复制进仓库。

## 八、验证与完成条件

### 8.1 Skill

- skill 初始化结构与 frontmatter 通过 `skill-creator` 校验。
- 新 Codex 任务能够发现该 skill；普通非 Blender 请求不会触发。
- 用三个代表性提示验证路由：当前场景只读检查、临时场景编辑、FBX 重导质检。
- 模拟正式人形输出请求时，skill 必须依据当前任务卡的资产持久化阻塞停止。

### 8.2 官方实时 MCP

- `codex mcp list/get` 显示 `blender_lab` 已启用，启动命令指向隔离 venv。
- Blender 5.2 中扩展已启用，Host/Port 正确，Auto Start 关闭。
- 在新建临时 `.blend` 中通过 MCP 读取场景摘要和对象列表。
- 在同一临时场景创建唯一命名对象 `TZ_MCP_SMOKE`、再次读取确认，再保存到临时目录；不得覆盖现有文件。
- 停止扩展后 MCP 连接应明确失败，不静默切换到其他 Blender 实例。

### 8.3 Headless QA MCP

- `blender_locate` 返回 `D:\Tools\Blender\5.2.0\blender.exe`。
- 使用临时场景导出一个临时 FBX，`blender_verify_fbx_reimport` 能重导并返回确定性 JSON。
- 结果只作为“FBX 可重导”的工具级证明，不宣称完成项目骨架或 Unity Humanoid 验收。

### 8.4 仓库与机器边界

- 仓库 diff 只包含本设计批准的 skill 路径；`assets/source/`、`src/Assets/`、任务卡和队列均无变化。
- 运行预期路径空白检查与 `git diff --cached --check`。
- 用户级 Codex 配置保留原 MCP 条目，Blender 安装目录内容未被修改。
- 完成结果明确列出版本、安装路径、配置条目、冒烟输入输出和仍然存在的 `A-CHAR-HUMANOID-RIG-01` 阻塞。

## 九、失败处理、停止条件与回滚

命中下列任一项立即停止，不叠加补丁：

1. 官方发布物哈希、ellmos integrity 或来源不一致。
2. Blender 扩展需要非 loopback 绑定、额外未批准插件或持久开放端口才能工作。
3. Blender Online Access 的实际影响无法确认，或用户不接受该前置。
4. 官方 MCP 服务端无法在 Blender 自带 Python 的隔离 venv 启动。
5. Codex 重启后仍不能发现 MCP 或仓库级 skill，且根因尚未查明。
6. 验证需要写入正式角色源目录、Unity 正式路径或解除当前任务阻塞。
7. ellmos 的两个 allowlist 工具不足以完成“FBX 可重导”证明；不得因此开放 `blender_run_script`。

回滚时按相反顺序执行：先从 Codex 配置移除两个新增 MCP 条目，再停用并卸载 Blender MCP 扩展，最后只删除已解析确认的 `C:\Users\WINDOWS\.codex\mcp-servers\blender-lab\1.0.0\`。仓库级 skill 通过对应 Git 提交回退；不删除其他 MCP、Blender 用户设置或 npm 缓存。

## 十、明确非目标

本文不批准：

- 建立 39 骨 Armature、校准体型或三条评估动作；
- 生成、修整、换绑或导入 Meshy／Tripo 候选；
- 解决 `assets/source/` 原始资产持久化与编辑所有权；
- 修改 Unity Avatar、Animator、Prefab、材质、场景或运行时代码；
- 建立自动重试、后台常驻 Blender、远程 Blender 服务或批量资产生产流水线；
- 把 MCP 工具冒烟成功视为角色资产业务验收。

## 十一、事实源与外部来源

项目事实源：

- `开发管理/任务卡/A-CHAR-HUMANOID-RIG-01.txt`
- `开发管理/任务归档/A-CHAR-BLENDER-SETUP-01.txt`
- `docs/superpowers/specs/2026-08-08-humanoid-evaluation-rig-design.md`
- `docs/superpowers/specs/2026-08-08-ai-3d-character-asset-platform-evaluation-design.md`
- `docs/资源管理/美术资源版本管理规范.md`

外部来源：

- Blender Lab MCP：`https://www.blender.org/lab/mcp-server/`
- Blender 官方仓库与 release：`https://projects.blender.org/lab/blender_mcp/releases/tag/v1.0.0`
- ellmos Blender Use MCP：`https://github.com/ellmos-ai/ellmos-blender-use-mcp`
- OpenAI Codex skills 最佳实践：`https://learn.chatgpt.com/guides/best-practices#turn-repeatable-work-into-skills`
- OpenAI Codex 配置参考：`https://learn.chatgpt.com/docs/config-file/config-reference#configtoml`
