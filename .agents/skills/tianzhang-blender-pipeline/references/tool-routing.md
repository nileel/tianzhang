# Blender 工具路由与验证边界

## 一次只选一条通道

| 请求 | MCP 服务 | 首选工具 | 能证明什么 |
| --- | --- | --- | --- |
| 查看当前场景、对象或缺失文件 | `blender_lab` | `get_objects_summary`、`get_object_detail_summary`、`get_blendfile_summary_*` | 当前 Blender 会话返回的场景状态 |
| 截图或定位界面 | `blender_lab` | `get_screenshot_*`、`jump_to_*` | 当前窗口可见状态与导航结果 |
| 修改当前场景 | `blender_lab` | `execute_blender_code` | 代码在当前 Blender 会话中执行；仍需任务级验证 |
| 无界面验证 FBX 回导 | `blender_headless_qa` | `blender_verify_fbx_reimport` | FBX 能被指定 Blender 回导，以及工具返回的对象/命名检查 |
| 定位 Blender 可执行文件 | `blender_headless_qa` | `blender_locate` | 本机 Blender 路径与版本发现结果 |

不要在同一任务中把另一条通道当作自动兜底。当前通道失败时，先确认根因并报告；只有用户或任务事实明确改变方案时再切换。

## `blender_lab` 允许工具

- `execute_blender_code`
- `get_blendfile_summary_datablocks`
- `get_blendfile_summary_missing_files`
- `get_blendfile_summary_of_linked_libraries`
- `get_blendfile_summary_path_info`
- `get_blendfile_summary_usage_guess`
- `get_object_detail_summary`
- `get_objects_summary`
- `get_python_api_docs`
- `get_screenshot_of_area_as_image`
- `get_screenshot_of_window_as_image`
- `get_screenshot_of_window_as_json`
- `jump_to_tab_by_name`
- `jump_to_tab_by_space_type`
- `jump_to_view3d_object_by_name`
- `jump_to_view3d_object_data_by_name`
- `search_api_docs`
- `search_manual_docs`

排除所有名称以 `_for_cli` 结尾的工具，以及 `render_thumbnail_to_path`、`render_viewport_to_path`。这些工具会扩大本地文件写入面，当前组合不需要它们。

## `blender_headless_qa` 允许工具

- `blender_locate`
- `blender_verify_fbx_reimport`

排除 `blender_run_script`。当前场景脚本只由官方 `blender_lab` 的 `execute_blender_code` 承担，避免出现第二条任意代码执行路线。

## 事实源路由

- 项目通用约束：仓库根目录 `AGENTS.md`。
- 当前任务：`开发管理/当前任务队列.txt` 和仍存在的 `开发管理/任务卡/<ID>.txt`。
- 完成历史：仅在追溯时读取 `开发管理/任务归档/<ID>.txt`。
- 设计与设定：按对象读取 `docs/` 的直接原文。
- Blender 当前行为：当前 `.blend`、场景只读结果和本次工具返回。
- Unity 当前行为：`src/` 中的实际实现及任务指定的 Unity 验证。

设计稿、交接、摘要、索引和旧对话只提供线索，不能替代上述当前事实。

## 验证层级

1. MCP 可连接：只证明服务启动和协议正常。
2. `blender_verify_fbx_reimport`：只证明指定 Blender 能回导，以及返回的对象数量、名称等检查；不证明美术质量、骨骼语义或 Unity 运行表现。
3. 任务业务验收：仍须执行任务卡指定的 Blender 验证器、factory roundtrip、截图证据、Unity 导入/运行检查或其他入口。

不要因为前一层通过而省略后一层。

## 立即停止的情况

- 活跃任务、归档和设计事实互相冲突。
- 目标路径不在任务允许范围，或包含未获授权的现有改动。
- 操作将覆盖、删除、上传或外发项目资产而用户未明确授权。
- 需要启用未列入允许清单的任意代码、渲染落盘或外部提供商工具。
- 同一根因开始需要连续补丁、第二套工作流或超出任务停止条件。
