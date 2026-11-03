# 来源、改编范围与本地入口

## 核对范围

本 skill 根据本对话、苻渊工程中实际保存的图像提示词、本机 Charakuru 1.1.1 的技能文档和实现编写，核对日期为 2026-11-03。模板为中文方法说明及可替换的英文改编稿。未把本地示例参数视作外部服务的永久规则。

本次源包根目录为 `D:/Tools/Charakuru/charakuru-1.1.1-2026-11-02T09-36-05-842Z`，下文用 `K` 指代。示例工程为 `K/projects/4e5377ea-a0d9-46e8-8b1a-74859c9db6d7/project.charakit`。这两个路径用于追溯本次提炼，下次制作从用户指定工程重新定位。

| 提炼内容 | 源包内位置 |
|---|---|
| 部件四方向、每次一张、正面先定 | `skills/character-parts-images/SKILL.md` 第 26 行起，`references/prompts.md` |
| 头含完整发型、颈归身体、本人左右、T 姿势 | `skills/character-parts-images/references/part-contract.md` 第 5–39 行 |
| 全身比例参考原为三张 | `skills/character-three-views/SKILL.md`，`references/prompts.md`，`references/delivery.md` |
| 原形内部提升色层与材质 | `skills/character-material-brushup/references/prompts.md` 与 `painting.md` |
| 多视图候选、粗分割、分类、Smart UV、重贴图 | `skills/tripo-agent-workflow/SKILL.md` 第 25 行起 |
| 五组贴图及实际网格参考提示词 | `skills/tripo-agent-workflow/references/retexture-images.md` |
| 部件和返件的配置字段 | `skills/tripo-agent-workflow/references/commands.md` |
| 保留分离部件、按面恢复 UV／材质 | `scripts/production.py` 的 inventory、export_texture_group、restore_uv、assemble |
| 真实下载文件与来源核对 | `skills/tripo-studio-download/SKILL.md`，`scripts/receive.mjs` 位于该 skill 下 |
| 权重转移及静止姿势调整 | `skills/avatar-weight-transfer/SKILL.md`，`references/body-workflow.md` |
| 物理末端、center、碰撞与实际 VRM 验证 | `skills/avatar-spring-setup/SKILL.md` |
| 可选表情后处理 | `skills/avatar-expression-authoring/SKILL.md` |
| 应用复制给 Agent 的提示词装配 | `public/session-prompts.mjs`，`public/session-prompt-localized.mjs` |

## 苻渊实际提示词与纠错经验

工程内 `work/image-request-8a454cd8/head/prompts.json` 和 `body/prompts.json` 保存了八张部件图的实际文案，`balance-register.json` 记录全身比例参考的修改。

- 宽袖侧视图明确了肩高的手与垂下的布料，以及远手、远鞋自然遮挡。这部分成为本 skill 的可选宽袖段。
- 原 body 文案曾写三颗玉珠，后续修正指出原设计为两颗。保留的是从原画核对数量的方法，不采用旧文案中的数量。
- 侧面头部偏大时，使用已采用正面与头顶、下巴、肩、腕、腰、鞋底地标修正。具体百分比仅用于原图。
- 原八图是 1254×1254 RGBA，实际透明背景成立，但身体正面左右余白未达到提示词中的 6%。按 alpha≥128 检查，左右约 1.5%。本 skill 因而要求检查输出，不能将生成目标当验证结果。
- 原提示词没有明确掌向。本 skill 新制 VRM 角色的掌向条款来自本轮手指修复经验，需要与目标动作体系核对。

第 42 版静止袖型恢复的证据位于 [restored-sleeves-qa.json](../../../../assets/generated-character-art/fuyuan-tripo-p2-tpose/charakuru-sleeves/restored-sleeves-qa.json)，第 44 版手、靴筒与袖摆验证位于 [outfit-motion-qa.json](../../../../assets/generated-character-art/fuyuan-tripo-p2-tpose/charakuru-sleeves/outfit-motion-qa.json)。其中 `Backup_Body` 与 `Body` 的 20,052 顶点对应只适用于该次核验过的网格，骨名、链数、毫米误差不能直接套到新角色。

## 与上游原流程的差异

- 《天章》采用同一资产的四视图先行。上游全身三视图用于部件完成后的比例叠加，其原生登记槽为 front、side、back，side 是本人右侧。额外左视图存入本地四视图素材，不能臆造原生第四槽。
- 上游候选阶段每组生成四个。本 skill 按当前任务候选数及累计预算执行，先复用已有资源。
- 上游材质模板偏 VTuber／Live2D。本 skill 保留材质表达方法，画风、年龄与设计服从当前角色。
- 图像拆件、网格连通分割、语义组、贴图组、物理组分别说明。上游实际脚本执行哪种操作，以当前实现为准。
- 动作验证补入本轮实测方法与残余缺陷。Charakuru 技术自动检查通过不代表穿插已全部消除。

## 本地工程入口

若当前任务来自应用复制的 Agent 请求，先按该请求运行 `scripts/agent-session.mjs begin`，读取返回的保存状态、正本位置及请求类型。请求内的 URL 和 ID 都使用当次值。正常完成按当前会话调用 progress／complete，失效时查看应用当前状态。

独立制作可直接使用本 skill 的模板与检查方法。使用 Charakuru 的登记命令时，先读取包内 `skills/charakit-project/SKILL.md` 与阶段命令说明；新任务不复用过去的请求 ID。

核对当前实现时发现以下版本细节，已在本 skill 中说明。

- `scripts/tripo-project.mjs` 文件首注释声称总写新文件，实际约定允许输入输出同一正本。当前 `scripts/project-write.mjs` 核对项目与原内容，再写回。
- `skills/charakit-project/references/project-format.md` 第 50 行声称 Blend 不嵌包，同文第 84 行及 `scripts/update-model.mjs`、`public/source-blend.mjs` 实现确认 springs 阶段保存于 `documents/source-blend/<sha>.blend`。继续编辑时核对当前 sourceBlend 的版本和哈希。
- `server-production.mjs` 的 prepare 登记会删除所有组的 returned，并重置绑定；`public/production-files.mjs` 的 addReturn 也重置绑定。`public/production.mjs` 的 assemble 预检要求每组有效返件。整套 production 路线不能充当保留现有成果的单材质修改入口。

`scripts/export-segmentation.py` 与 `scripts/production.py` 含清空场景操作，必须作为独立后台入口使用。本文没有移植包内工具到新 skill，也没有将这些入口视为可直接注入用户活动 Blender 的代码。

表情属于按需后处理。若用户要求，可读取包内 avatar-expression-authoring，以中性脸为基准，分别处理眼、上下唇和眉，再检查侧面与中间强度。新制中性模型或单纯拆件任务不默认增加整套表情。
