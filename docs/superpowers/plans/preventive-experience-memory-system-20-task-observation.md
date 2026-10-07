# 预防型经验系统：激活后 20 个相关任务观察报告

日期：2026-10-08。任务：M-EXP-OBSERVE-20-01。观察基线：1b5f0cb3d386abc57f12af4cebf7b6122e3461bb。

## 结论与授权边界

**stop：停止门禁晋级与设计、内容、数值域扩展。** 恰好 20 项的读取边界、误报和 token 成本通过，但没有可回指“原计划采用错误路线→预检命中→写前改变路线”的预防案例。正确路线与相关提醒同时出现，不能证明提醒避免了返工；独立整理已经产生真实维护工作，尚不能证明其成本不高于避免的返工。不能仅凭低成本或新增经验判为 continue。

本轮只形成观察报告并完成本卡。当前固定入口明确禁止非 QueueMaintenance 移除具名前置、提升、重排或关闭后继，优先于原观察卡的直接解锁／归档动作。因此六张门禁／扩域卡、关闭卡及其 backlog 引用保持原状；正式结果进入 master 后，QueueMaintenance 应按 stop 结论处理六张“观察未授权实施”终态及关闭卡前置。报告不授权实现这些卡的内容，也不在本轮停用既有预检、删除索引、经验或规则。

事实依据：批准规格 `docs/superpowers/specs/2026-08-24-preventive-experience-memory-system-design.md#效果指标与停止条件`、`#第三阶段：门禁与扩域`；只读基线 `docs/superpowers/plans/preventive-experience-memory-system-read-only-trial-report.md`。基线十项未重新回放。

## 固定样本与证据口径

激活锚点 b86e05310a07c753dda5299bc17f20e4eca7e905，2026-09-01 19:34:17 +08:00。按首次 completed 归档提交时间取五个技术域的前 20 项；截止 8c2350d9014b7177d84e1c63e80dc97c052bf24e。Git 根目录新增归档的 schema/domain 元数据核对得到 20 项；九项 content 卡没有显式技术工作流标识，未纳入，未读取其正文。整理卡关联的两条来源候选只用于其成本回链，不加入样本。

U-GZ-ART-BATTLEFIELD-01 首次 completed 是 195df642cd6b9f0a179ae162399cd2c3041bf0a8（03:01:35）；旧观察卡写的 72b7e85f10be56ba6da70153fbfac37f6c6723c1（20:35:25）是同卡视觉返修。用首次完成计数，它仍在 BASELINE 与 UI 合同之间，未重复计数，也未更换样本。A-GZ-UI-FONT-ASSET-01 使用后续手动预检和正式完成，未将失败自动 run 计作完成。

每任务固定一份已执行的成功预检作为主表；P 为私有预检文件，S 为原始会话 CommandExecution 的成功返回。P 的 runId/schemaVersion 为存储封套，不计成本；还原预检的十字段短 JSON（含 status=ok），保留字段顺序、正文及全部数字。UTF-8 字节数不含尾部换行；token 代理为 ceil(字节数/4)，是与基线一致的跨模型观察代理，不是实际 tokenizer 或本轮全部上下文。重复尝试不新增 taskId，已留存的重复输出另记下界。

## 逐任务观测

所有行 notice=0、gates=0、门禁发现=0、无关高置信命中=0、已证实路线纠正=0、所选 matcher 返回为 ok；零命中如实保留。候选数以首次完成归档为准。

| 序号 | taskId | domain | must_read／命中 | 必读字符 | 短 JSON 字节 | token 代理 | 新候选 |
|---|---|---|---|---:|---:|---:|---:|
| 1 | A-CHAR-PORTRAIT-INTERFACE-01A | management | 0／无 | 0 | 307 | 77 | 0 |
| 2 | U-COMBAT-RNG-01 | unity | 0／无 | 0 | 293 | 74 | 0 |
| 3 | M-HOURLY-PATH-01 | automation | 0／无 | 0 | 294 | 74 | 0 |
| 4 | M-HOURLY-CHECK-01 | automation | 0／无 | 0 | 295 | 74 | 0 |
| 5 | D-COMBAT-PARITY-01 | battlesim | 0／无 | 0 | 296 | 74 | 0 |
| 6 | M-HANDOFF-CLOSE-AUTH-01 | automation | 0／无 | 0 | 301 | 76 | 0 |
| 7 | M-HANDOFF-ARCHIVE-01 | management | 0／无 | 0 | 298 | 75 | 0 |
| 8 | M-EXP-PROMOTE-A242521B1028 | automation | 0／无 | 0 | 304 | 76 | 0 |
| 9 | U-COMBAT-PROJECTION-01 | unity | 0／无 | 0 | 300 | 75 | 0 |
| 10 | U-COMBAT-LOADOUT-01 | unity | 0／无 | 0 | 297 | 75 | 0 |
| 11 | M-CURRENT-FACTS-01 | management | 0／无 | 0 | 296 | 74 | 0 |
| 12 | N-COMBAT-PARITY-01 | battlesim | 1／EXP-BS-001 | 193 | 827 | 207 | 0 |
| 13 | U-GZ-BATTLEFIELD-BASELINE-01 | unity | 1／EXP-UNITY-001 | 316 | 1090 | 273 | 0 |
| 14 | U-GZ-ART-BATTLEFIELD-01 | unity | 1／EXP-UNITY-001 | 316 | 1085 | 272 | 0 |
| 15 | D-GZ-UI-V6-CONTRACT-01 | management | 0／无 | 0 | 300 | 75 | 0 |
| 16 | TQ-074 | unity | 0／无 | 0 | 284 | 71 | 0 |
| 17 | M-EXP-PROMOTE-EB7E3647997B | management | 0／无 | 0 | 304 | 76 | 0 |
| 18 | N-FPD-STAGE-03 | battlesim | 1／EXP-BS-001 | 193 | 823 | 206 | 1 |
| 19 | D-FPD-SPELL-LEARN-01 | data | 0／无 | 0 | 298 | 75 | 0 |
| 20 | A-GZ-UI-FONT-ASSET-01 | unity | 0／无 | 0 | 299 | 75 | 0 |

## Git 与原始执行绑定

下表的完整 SHA、digest、输入锚点及来源是复算输入。所有输入与完成归档均为 schema 2；matched/gates 相等，explicitRefs 均为空。活动卡可能在完成时已被删除，不能用当前任务卡或完成状态的 digest 替代执行前输入。

<!-- evidence-start -->
| 序号 | taskId | 首次完成 SHA | 完成时间 +08:00 | 原始预检 | Git 输入锚点 | taskCardDigest | index |
|---|---|---|---|---|---|---|---|
| 1 | A-CHAR-PORTRAIT-INTERFACE-01A | f6509656d1bd8fb7181ef8ca976de4d4d126dda8 | 2026-09-03T08:20:52+08:00 | P:2cc68093-a4c1-49c9-8aa3-80ff32b8f677 | 770db36c1a4f9f1a7c386b066bb1e2149fd4ea48 | ceea84a6f97530d922ced93e107f05f17f3ef1e62d9fc20c2b59f1bd56de893f | I1 |
| 2 | U-COMBAT-RNG-01 | 0ea9998640c09903626d7fcf2b30bd964a37aac5 | 2026-09-05T11:47:09+08:00 | S1:111 | 8000b4ee9eb1a1cbb638c0d614b9027f07ac324c | fd52803fe8c2ad486ef4c7bc54501d94ba1d03133e595d7101480d29f9c87f8d | I1 |
| 3 | M-HOURLY-PATH-01 | 482b5f5ee2bf25914f164ee1fb91fb51107e14ea | 2026-09-05T11:54:34+08:00 | S2:147 | 02a33ffa546994ed6aaa0d5bb71a5cb1c156381e | 0762a7d2f90caa16f4af438cdabccabace74750e8c53ab4c01052e76821bd7eb | I1 |
| 4 | M-HOURLY-CHECK-01 | 49543bf8a516f38d4acbd52a0eb3762cb506f18e | 2026-09-05T12:03:36+08:00 | S3:126 | R1@02a33ffa546994ed6aaa0d5bb71a5cb1c156381e | c309e4bf888962f8ad0c6ef9dbb4649c4d457b2539f74831b92c034d10f5216d | I1 |
| 5 | D-COMBAT-PARITY-01 | b06580779cfec64fed6fbd44702770a66b1f4698 | 2026-09-05T12:14:53+08:00 | S1:584 | 02a33ffa546994ed6aaa0d5bb71a5cb1c156381e | a85e6e89d0bd64fbb563129a0f8a5931a7a2dc2344ff96b0fe3000efd7bcd49d | I1 |
| 6 | M-HANDOFF-CLOSE-AUTH-01 | 72e6b1d664b476e42c88e52bd3a0a30b76ea9d1b | 2026-09-05T12:23:41+08:00 | S4:434 | R2@49543bf8a516f38d4acbd52a0eb3762cb506f18e | c4109517e5f308eaa1a108af0cc5c0ed9312c81f90e11973903e059245df0b73 | I1 |
| 7 | M-HANDOFF-ARCHIVE-01 | d3ff117d42e6627299f90db5d398577ec263a08d | 2026-09-07T20:28:27+08:00 | P:372d4d06-2d84-41e0-b02f-0f268c692937 | 02a33ffa546994ed6aaa0d5bb71a5cb1c156381e | 47ff3cc20b89d77f26d194f3c50159b6c155ebcdd9fb3f9c1f750884de1b87e3 | I1 |
| 8 | M-EXP-PROMOTE-A242521B1028 | d9daa7e33b33127f1713b0c78966cc2f815b0b56 | 2026-09-07T21:20:38+08:00 | P:84dd4c85-170a-4b77-b282-58ec0afbfd65 | fdd2f939acf3724bd3b3dff1c3594c6aafac5a58 | b48fe0663521763dadc05119aae3c94ab4d350826f68356c211773710af806bc | I1 |
| 9 | U-COMBAT-PROJECTION-01 | 78e48d1c8df1175b8e4b7a37dd5e4fea462037b0 | 2026-09-07T22:24:44+08:00 | P:e6835e20-2ae3-4696-a375-7ab9ede3f7e6 | b06580779cfec64fed6fbd44702770a66b1f4698 | 737d77661e11144183275437e7db746502b9dbd47c0fc6defa914143f0173dec | I1 |
| 10 | U-COMBAT-LOADOUT-01 | bd4599289ba69247f0f55cace7ca87a9b48e2683 | 2026-09-08T05:24:55+08:00 | P:3fef1aef-f586-4946-a9af-a77baecc91f3 | 24b37eb912e06a3670a43c58653bef74e04bda74 | e511bcf9b55ffc4122b0fdd5689498c0555e738c29234ad74face858dc080d2f | I2 |
| 11 | M-CURRENT-FACTS-01 | dfa94dc2d9a53800b42756efde238496d2f69a5c | 2026-09-08T06:21:12+08:00 | P:fb274af4-fcfb-4651-b341-da1b507a6c7b | 76b85db42169171e811db3d530bcf83c22ea2f94 | 5c71764dd120342022ab162d5eedbe4bf57491996ce72409ba5a6d589baea821 | I2 |
| 12 | N-COMBAT-PARITY-01 | abe6e78d02669cd7d4a1fafebc4ad6b3b1200a92 | 2026-09-08T13:27:44+08:00 | P:48e49ea0-d974-443b-86ad-93eccd752656 | a83b7e442ec39bfd799cc861fcc07a50d313198d | 0ac8fab973909c96101fad789e315d5d4e7f454b602543e277c9bdf53ce07be2 | I2 |
| 13 | U-GZ-BATTLEFIELD-BASELINE-01 | 8fd58edfb975d651939efacdc7b063773f4ab925 | 2026-09-20T01:51:33+08:00 | P:ea53e37a-8714-4d05-a14a-b60d24108d3b | 52179a4f53cff4bdbb65a5f8e02089215d6f9f76 | 39d32a6f3c0a268bf94efd67138e86789e3bf931c0d5aec45e2a28982f25d08f | I2 |
| 14 | U-GZ-ART-BATTLEFIELD-01 | 195df642cd6b9f0a179ae162399cd2c3041bf0a8 | 2026-09-20T03:01:35+08:00 | S5:56 | R3@195df642cd6b9f0a179ae162399cd2c3041bf0a8 | db94adca1c4f7272605af1adaa15bc526ac096b93d03d7bff5c385d5687a3fff | I2 |
| 15 | D-GZ-UI-V6-CONTRACT-01 | c45272e8b1965a9d9d4ee29a960afa38359ddf8f | 2026-09-20T22:45:13+08:00 | P:d22b8b32-dc22-49c9-bbc0-2c571cceb7b2 | 07ca3cfa47d44f3dadce6c262f90e9a9df053d6e | 7ff3088f18ad44849ab25c315e06e31e1c10baa04859bedcfa14e545f03b7ab0 | I2 |
| 16 | TQ-074 | 1e1a7aa7fc1b698659474c1abb13cc979349269f | 2026-09-21T10:35:10+08:00 | P:83c04866-c3e6-40e6-8ff6-7541e30a7923 | d28f913f340ac493e6bff3077956a044f330c90b | 069953b4dcd9eff309ab9941fbdc45179cc264f5b1fc362e89037a942e277497 | I2 |
| 17 | M-EXP-PROMOTE-EB7E3647997B | 7c199fdb92ea8a2a2bebf5cfb9404d0571adbca3 | 2026-09-22T00:25:31+08:00 | P:95b9c428-8dd6-4a37-a542-311d3d80949c | 4148058d42fc23117ff3123a28e31aacddcaffee | 95f88b903e790461840e4566c98ddc843a58080bd7cf9820409bef57fccf09e9 | I2 |
| 18 | N-FPD-STAGE-03 | 03211cdd5945b4bc11f4c5f90232650f2d710588 | 2026-09-27T17:46:35+08:00 | P:f2308234-860a-41b1-915c-75072a8242c1 | 6303a8e75b20a37e44ca4151c620f81825482c2a | 9b5f0e8379c817b87049d39ea071acd5f57769812114e932ede6f06a18e9c4cd | I3 |
| 19 | D-FPD-SPELL-LEARN-01 | 17006b836df9e2c14090c855c6e9e702e1cda82f | 2026-09-27T18:28:03+08:00 | P:8e09dc28-2a9b-4b6d-aff9-009aa8b84638 | 6303a8e75b20a37e44ca4151c620f81825482c2a | 31d64cae2025c7f87893bfea6d349e0c7c25a69d19facbbec7d40db07c1266be | I3 |
| 20 | A-GZ-UI-FONT-ASSET-01 | 8c2350d9014b7177d84e1c63e80dc97c052bf24e | 2026-09-27T20:35:09+08:00 | S6:217 | 209e903d53a2009e2f1f3077628f930f0e0d8a16 | 73dc21725d54bb1aaa76f6368a53f9215c4d76b3689bde9e2598fd8354a369ec | I3 |
<!-- evidence-end -->

P:<runId> = `C:/Users/WINDOWS/.codex/automation-state/tzg-hourly-controller-runtime/preflight-results/<runId>.json`，只读历史证据，不调用 runtime。S 文件根为 `C:/Users/WINDOWS/.codex/sessions/`：

- S1：`2026/09/05/rollout-2026-09-05T11-29-43-01a06f9d-6db6-7882-8c41-991923fc2157.jsonl`。
- S2：`2026/09/05/rollout-2026-09-05T11-30-45-01a06f9e-61a5-7282-afc5-81bfc78f358d.jsonl`。
- S3：`2026/09/05/rollout-2026-09-05T11-30-25-01a06f9e-10cd-75d0-92ca-e8aab38de566.jsonl`。
- S4：`2026/09/05/rollout-2026-09-05T11-31-04-01a06f9e-a8a8-7441-b393-cf7e65bcede8.jsonl`。
- S5：`2026/09/20/rollout-2026-09-20T02-34-39-01a0baf2-f4d1-7412-a212-859b826633bd.jsonl`。
- S6：`2026/09/27/rollout-2026-09-27T20-20-05-01a0e2ce-e697-76d3-8b6a-f1491700e826.jsonl`。

预检 ordinal→首项业务写入 ordinal：S1 的 RNG 111→219、PARITY 584→850；S2 147→257；S3 126→173；S4 434→516；S5 56→125；S6 217→269。均为同会话真实预检，预检 exit=0、status=ok。ready 状态准备不算业务写入。S5 ordinal 27 保存执行前卡全文，可交叉核对 digest；S6 自动 run fd6d3668 的相同 digest 只作交叉核对。

- I1：indexDigest `d46db414950ffe1f9ab8baa482bf8b8bc872feeed571eed704edf551e02b2769`；从 `b86e05310a07c753dda5299bc17f20e4eca7e905:开发管理/经验库/风险索引.json` 去 BOM、规范 LF 后 SHA-256 重算。
- I2：indexDigest `4d35e55ef2280ee9ae58b122a4cefcde1f4013186fda2a4497a82ee26e9a102c`；从 `d9daa7e33b33127f1713b0c78966cc2f815b0b56:开发管理/经验库/风险索引.json` 去 BOM、规范 LF 后 SHA-256 重算。
- I3：indexDigest `86caf96d5b879903527cb2e687338fc076bfbea554a851b9a44cd6d93d7f2bdf`；从 `7c199fdb92ea8a2a2bebf5cfb9404d0571adbca3:开发管理/经验库/风险索引.json` 去 BOM、规范 LF 后 SHA-256 重算。

普通 Git 输入锚点直接读取 `开发管理/任务卡/<taskId>.txt`。三个未单独提交的准备输入按原始记录还原：R1 从 02a33ffa 的 frozen 卡，将 dispatchState 改 ready、stateReason 替换为 S3 ordinal 117 原文；R2 从 49543bf8 的 frozen 卡按 S4 ordinal 424 同样替换；R3 从 195df642 的首次归档去掉“完成证据”节，改回 ready 与 S5 ordinal 27 的原 stateReason。下方复算代码列出三个原值，不猜测字段。三个 SHA-256 均与原执行返回相等。

## 汇总、基线与预防价值

| 指标 | 实测 | 阈值／判断 |
|---|---:|---|
| 合格任务／零命中 | 20／16 | 恰好 20，无重复或跳号 |
| 高置信命中 | 4（两次 EXP-BS-001、两次 EXP-UNITY-001） | 逐任务判定相关 |
| must_read 最大值 | 1 | ≤3，通过 |
| 必读正文最大／合计 | 316／1018 字符 | 单项≤600，通过 |
| 高置信误报 | 0/4，0% | <15%，通过；样本小，不外推 |
| token 代理合计／平均／最大 | 2154／107.70／273 | 平均≤1000，通过 |
| 主表短 JSON 合计 | 8591 UTF-8 字节 | 可从原始返回重算 |
| notice／gate／gate 发现 | 0／0／0 | 未将普通验证算成经验门禁收益 |
| 已观察 matcher 自阻断 | 主表 0/20 | 不代表所有未留存失败尝试为零 |
| 可观察错误路线纠正 | 0 个确证案例 | continue 必需条件未满足 |
| 新候选 | 1/20 | 只在 N-FPD-STAGE-03 首次完成产生 |

基线十项：3 个高置信命中、0/3 误报、必读最大 466 字、平均 token 130.0、matcher 0/10；本轮主表读取成本同样通过，不能据此证明业务返工下降。

四个相关命中的判定依据及收益边界：

- N-COMBAT-PARITY-01 修改 BattleSim Combat.cs，卡中明确 Release build 后 run，EXP-BS-001 相关；完成证据证明共同 JSON 和直接调用链通过。无证据表明原先打算 run 旧程序集、再被预检纠正。
- N-FPD-STAGE-03 修改实际成长所有者并运行 build／BattleSim，EXP-BS-001 相关；后续实际 HP 分配与凡人节点索引错误仍需人工收口，不能把原有 build 顺序提醒算作避免该返工。新候选来自实际失败与修复，属于事后收集。
- U-GZ-BATTLEFIELD-BASELINE-01 的冻结实施范围已要求原 AdventureSceneBuilder 保存重开、禁止手写 YAML；EXP-UNITY-001 相关，完成记录确实使用原链。缺少“准备手写→预检纠正”的前后证据。
- U-GZ-ART-BATTLEFIELD-01 使用同一 Builder／正式场景保存链，EXP-UNITY-001 相关；S5 ordinal 89 在预检后确认原所有者，未记录替换错误路线。随后真实截图首次渲染异常、背景遮挡和用户视觉返修保留，不能说预检避免了视觉返工。

其余十六项没有高置信命中。U-GZ-BATTLEFIELD-BASELINE 的完整 EditMode 中既有宽袖试验缺 Blender 文件失败、N-FPD 的返工、字体自动 run 的 official_font_download_timeout 都保留为任务失败／限制，未伪装成 matcher 错误或收益。四个命中的原始返回均正常，未找到持续 matcher 自阻断证据；也未通过补关键词、兼容分支或新索引改善统计。

重复成本边界：上述 13 个 P 来源任务共有 27 份私有预检输出（每项留存量见其 runId 文件），再加七份主表手动输出及字体失败自动 run 的一份输出，共 35 份已找到返回；这是留存下界，不是完整尝试日志。重复输出均保持同一任务的命中与字符数，但不能将每项主表成本等同于所有重试／复审成本。这些留存返回的 token 代理合计3801，除以20项为190.05的留存成本下界；未留存调用成本未知，不将它冒充全量平均。复算代码打印该下界，不混入主表单份均值。

## 候选收集、失败与独立整理成本

首次完成的 20 项只有 N-FPD-STAGE-03-C01 一条新候选；另外 19 项不存在候选节，记为真实零。M-EXP-PROMOTE-A242521B1028 与 M-EXP-PROMOTE-EB7E3647997B 本身在样本内，因此只补读其各自具名来源候选；来源任务 M-QUEUE-MAINT-READY-SCHEMA2-GUARD-01 与 C-JD-LORE-MIGRATE-01C 不加到 20 项分母。三条都是从本来已证实的事实标记，不以新模型重判原任务来收集。

| 来源候选 | pending 正式提交／时间 | collected 同提交制卡与回链／时间 | 延迟 | 独立整理正式提交 |
|---|---|---|---|---|
| CAND-M-QUEUE-MAINT-READY-SCHEMA2-GUARD-01-01 | 200cddba202dadcc972c7504ac6543725c3d1d01／09-01 11:58:54 | fdd2f939acf3724bd3b3dff1c3594c6aafac5a58／09-07 20:11:26 | 547952 秒（6天8时12分32秒） | d9daa7e33b33127f1713b0c78966cc2f815b0b56 |
| CAND-C-JD-LORE-MIGRATE-01C-01 | d0b2156f06a87f1b8f5874a27c152675ff42f96d／09-21 21:59:23 | 4148058d42fc23117ff3123a28e31aacddcaffee／09-22 00:12:21 | 7978 秒（2时12分58秒） | 7c199fdb92ea8a2a2bebf5cfb9404d0571adbca3 |
| N-FPD-STAGE-03-C01 | 03211cdd5945b4bc11f4c5f90232650f2d710588／09-27 17:46:35 | d865db5ffb54cfdea05cfcf7ab901be7f1e34403／09-28 00:13:11 | 23196 秒（6时26分36秒） | 492c349fd2e7fd6fd4763ea77d013a72233c229a |

时区均 +08:00。样本内新增候选延迟为 23196 秒；含两条关联来源的平均为 (547952+7978+23196)/3 = 193042 秒，不混成 20 项平均。三次 collector 正式提交的 Task=DAILY-EXPERIENCE-COLLECTOR、State=completed 与各自来源从 pending→collected、整理 active 卡首次出现均一致。来源 candidate digest 与独立卡登记分别为 eeb69a071c6beff2c1d9c8cecaead5add43e0e3bc65d9d24763a4b617a83b270、4497e83b80825e92c95f2e4c9b90c8ffd8ba0a6f1aebf5b8572b558f382c09d8、23168b8bdb627d0db1c3d3ac37c57608edb9267490a46c1bfcc5077af111fa15。

每条来源只关联一张稳定整理卡，三张首次创建均由对应 collected 提交完成；Git 路径历史与三条 promotionTaskId 回链没有重复制卡，**这三条观察范围内重复制卡=0、截至基线遗留 pending=0**。不是对全库或每天运行的承诺。

收集失败不是零：关联第一条来源的实际生产触发失败至少 1 次。原始记录 `2026/09/06/rollout-2026-09-06T02-28-00-01a072d3-d5e3-7d32-9ec7-5993063e774a.jsonl` ordinal 237 的只读 thread history 绑定 turn 01a07255-ed89-7151-aae0-fd98a21acb25（startedAt=1788624629），CommandExecution exit=1，6508 ms，终态为 failed/daily_collector_candidate_invalid，base=6406bbb56e1a8883a3a531074d3208b35a383596。重复读取同 turn 不增加失败数。

同会话 ordinal 676 的主仓库收集执行 exit=0，完成终态包含 M-EXP-PROMOTE-A242521B1028、fdd2f939、cleanup=completed，证明更早 pending 被同一确定性入口补收。但该成功发生在人工修正后，不能称为“无需干预的下一日自动恢复”。同会话诊断副本 ordinal 572 的 worktree_create_failed、605 的 commit_failed 是两次诊断失败，636 的三候选成功也是诊断副本结果，分别保留且不计为主仓库重复卡／成功数。另两条来源的生产失败终态没有取得完整日志，计数记“未完整可知”，不写 0；Git 已证明其正式收集成功。未扫描其他历史候选填补缺口。

独立整理结果全部 promoted：三张归档分别回链 EXP-AUTO-002、EXP-MGMT-002、EXP-BS-002。维护范围均为一个语义裁决任务：核验直接根因、既有经验去重、适用／排除／失效边界、创建经验、索引登记、来源回链、任务归档与队列/backlog 收口；不新增 gate。

| 独立整理卡 | 模型语义复核工作 | 人工复核／实际 token 与工时 | 文件维护成本（不压缩移动） | collected→完成窗口 |
|---|---|---|---|---:|
| M-EXP-PROMOTE-A242521B1028 | 1 张独立 Codex 裁决卡；根因、去重与边界核验 | 未登记人工次数／真实 token／活跃分钟，不能填零 | 7 路径；新增131行、删除74行；新增25行经验卡及28行索引 | 4152 秒 |
| M-EXP-PROMOTE-EB7E3647997B | 1 张独立 Codex 裁决卡；归档后置检查与既有隔离经验去重 | 同上；归档记录单模型无需辅助 | 7 路径；新增125行、删除74行；新增25行经验卡及22行索引 | 790 秒 |
| M-EXP-PROMOTE-AECAA859638A | 1 张独立 Codex 裁决卡；实际分配／索引根因与四种既有经验去重 | 同上；归档记录未调用辅助 | 7 路径；新增143行、删除74行；新增31行经验卡及34行索引 | 105043 秒 |

窗口是提交时间差，含排队／空闲，不是工作耗时。七路径分别为来源归档、整理活动卡删除、整理归档新增、经验卡新增、风险索引、管理 backlog、当前队列；不把 rename 的默认六文件 diffstat 当作实际七路径。三条累计 3 个语义任务、21 个路径操作、399 行新增与222行删除；其中 1 个语义任务来自样本内新候选，2 个来自样本内整理卡的关联来源。collector 失败调查／修正有额外真实成本，但缺少可归属本三条的完整人工/model 计量，保持未知。由于确证避免返工为 0 个案例，无法满足“整理维护成本不高于避免返工”的 continue 证明。

## 本轮能力、复算与验证

本轮为固定证据归纳、无新增语义冲突；按任务卡执行 gpt-6.1-sol 单主模型，模型依据共享入口启动证明，不另启责任会话。原生辅助工具存在，但口径与结论相互依赖，无独立复杂工作面，未调用辅助。Git、PowerShell 7.6.5、Python 3.12、历史预检及 JSONL 只读能力已实际使用；业务写前验证当前 run/taskId、任务卡 digest 97a4e70038584a76ae6150ecc95eb5b2aec63e9b465dbccf9b256a8d6a78cf88、index digest 0384c35146b6d50fae0239637156added4431498deffe85ac261d95d49d48942 与共享绑定相等，零命中、无必读或门禁入口。

下方 Python 只复算已冻结记录与 Git 文本，既不回放 matcher，也不执行 collector、Unity 或 BattleSim。未更改业务输入，无需重跑那些历史验证。必须的管理文本检查及 whitespace 结果写入本轮唯一 candidate 的 Verify 元数据。

```python
import json, pathlib, subprocess, hashlib, re
report = pathlib.Path("docs/superpowers/plans/preventive-experience-memory-system-20-task-observation.md").read_text(encoding="utf-8")
PR = pathlib.Path("C:/Users/WINDOWS/.codex/automation-state/tzg-hourly-controller-runtime/preflight-results")
SR = pathlib.Path("C:/Users/WINDOWS/.codex/sessions")
SESS = {'S1': '2026/09/05/rollout-2026-09-05T11-29-43-01a06f9d-6db6-7882-8c41-991923fc2157.jsonl', 'S2': '2026/09/05/rollout-2026-09-05T11-30-45-01a06f9e-61a5-7282-afc5-81bfc78f358d.jsonl', 'S3': '2026/09/05/rollout-2026-09-05T11-30-25-01a06f9e-10cd-75d0-92ca-e8aab38de566.jsonl', 'S4': '2026/09/05/rollout-2026-09-05T11-31-04-01a06f9e-a8a8-7441-b393-cf7e65bcede8.jsonl', 'S5': '2026/09/20/rollout-2026-09-20T02-34-39-01a0baf2-f4d1-7412-a212-859b826633bd.jsonl', 'S6': '2026/09/27/rollout-2026-09-27T20-20-05-01a0e2ce-e697-76d3-8b6a-f1491700e826.jsonl'}
INDEX = {'I1': ('b86e05310a07c753dda5299bc17f20e4eca7e905', 'd46db414950ffe1f9ab8baa482bf8b8bc872feeed571eed704edf551e02b2769'), 'I2': ('d9daa7e33b33127f1713b0c78966cc2f815b0b56', '4d35e55ef2280ee9ae58b122a4cefcde1f4013186fda2a4497a82ee26e9a102c'), 'I3': ('7c199fdb92ea8a2a2bebf5cfb9404d0571adbca3', '86caf96d5b879903527cb2e687338fc076bfbea554a851b9a44cd6d93d7f2bdf')}
REASONS = {'R1': '用户已授权本次普通手动会话解冻；两个写入定时器保持暂停，schema 5 无活动 run 且集成锁空闲，仅在本卡专用 worktree 内实施与 canary。', 'R2': 'M-HOURLY-CHECK-01 已完成并通过双侧 canary；负责人已在指定 Sol 手动会话授权解冻。两个写入定时器已通过 app automation 保持暂停，schema 5 两侧无活动 run，现由本手动会话隔离实施。', 'R3': '2026-09-20：用户明确采用67743c70地块及1555965a石阶并要求执行；本卡缩为已选初始资源的六格平地接入，BASELINE已完成，不等待整屏LOOKDEV或其他地形。石阶只形成备用Prefab，高差通行另行落实。'}
def git(*args): return subprocess.check_output(["git", "-c", "core.quotepath=false", *args]).decode("utf-8-sig")
def norm(t): return t.lstrip("\ufeff").replace("\r\n", "\n").replace("\r", "\n")
def digest(t): return hashlib.sha256(norm(t).encode()).hexdigest()
def meta(t): return json.loads(t.split("---TASK-META---")[1].split("---TASK-BODY---")[0])
def proof(key):
    kind, ident = key.split(":", 1)
    if kind == "P":
        p = json.loads((PR / (ident + ".json")).read_text(encoding="utf-8-sig"))
        return {"status": "ok", **{k:p[k] for k in ["taskId","taskCardDigest","indexDigest","matched","notice","mustRead","gates","gatePointers","mustReadChars"]}}
    for line in (SR / SESS[kind]).open(encoding="utf-8"):
        o = json.loads(line)
        if o.get("ordinal") == int(ident):
            it = o["payload"]["item"]; assert it["exit_code"] == 0
            for ln in it["stdout"].splitlines():
                if ln.startswith('{"status":"ok"'): return json.loads(ln)
    raise AssertionError(key)
section = report.split("<!-- evidence-start -->")[1].split("<!-- evidence-end -->")[0]
rows = [[x.strip() for x in line.strip().strip("|").split("|")] for line in section.splitlines() if re.match(r"^\| \d+ \|", line)]
ids=[]; tokens=[]; chars=[]; counts=[]; times=[]; new_candidates=0
for n,task,completion,time,key,ref,want,index in rows:
    ids.append(task); times.append(time); p=proof(key)
    if "@" not in ref: text=norm(git("show", ref+":开发管理/任务卡/"+task+".txt"))
    else:
        fix,sha=ref.split("@"); folder="任务归档" if fix=="R3" else "任务卡"
        text=norm(git("show",sha+":开发管理/"+folder+"/"+task+".txt")); m=meta(text)
        text=text.replace('"dispatchState": "'+m["dispatchState"]+'"','"dispatchState": "ready"')
        text=text.replace(json.dumps(m["stateReason"],ensure_ascii=False),json.dumps(REASONS[fix],ensure_ascii=False))
        if fix=="R3": text=text.split("\n## 完成证据",1)[0].rstrip("\n")+"\n"
    m=meta(text); arc=git("show",completion+":开发管理/任务归档/"+task+".txt"); a=meta(arc)
    assert digest(text)==want==p["taskCardDigest"]
    assert p["taskId"]==task and p["status"]=="ok" and m["schemaVersion"]==a["schemaVersion"]==2
    assert a["dispatchState"]=="completed" and m["riskPreflight"]["explicitRefs"]==a["riskPreflight"]["explicitRefs"]==[]
    assert m["riskPreflight"]["matched"]==a["riskPreflight"]["matched"]==p["matched"]
    assert m["riskPreflight"]["gates"]==a["riskPreflight"]["gates"]==p["gates"]==[]
    assert digest(git("show",INDEX[index][0]+":开发管理/经验库/风险索引.json"))==INDEX[index][1]==p["indexDigest"]
    assert sum(len(x["body"]) for x in p["mustRead"])==p["mustReadChars"]
    assert all(len(x["body"])==x["chars"] for x in p["mustRead"])
    size=len(json.dumps(p,ensure_ascii=False,separators=(",",":")).encode()); token=(size+3)//4
    tokens.append(token); chars.append(p["mustReadChars"]); counts.append(len(p["mustRead"]))
    produced=len(re.findall(r"(?m)^### ",arc.split("## 经验候选",1)[1])) if "## 经验候选" in arc else 0
    new_candidates+=produced
    assert git("show","-s","--format=%cI",completion).strip()==time
    print(n,task,counts[-1],chars[-1],size,token,produced)
assert len(rows)==len(set(ids))==20 and times==sorted(times)
log=git("log","--reverse","--format=%H","--name-only","--no-renames","--diff-filter=A","b86e05310a07c753dda5299bc17f20e4eca7e905..8c2350d9014b7177d84e1c63e80dc97c052bf24e","--",":(glob)开发管理/任务归档/*.txt")
fixed=[]
for ln in log.splitlines():
    if re.fullmatch(r"[0-9a-f]{40}",ln): sha=ln
    elif ln.endswith(".txt"):
        t=git("show",sha+":"+ln)
        if "---TASK-META---" not in t: continue
        m=meta(t)
        if m["domain"] in {"unity","battlesim","data","management","automation"} and m["schemaVersion"]==2 and m["dispatchState"]=="completed": fixed.append(m["id"])
assert fixed==ids
assert max(counts)<=3 and max(chars)<=600 and sum(tokens)/20<=1000 and new_candidates==1
print("SUMMARY",sum(tokens),sum(tokens)/20,max(tokens),sum(chars),new_candidates)
retained=[proof("P:"+f.stem) for f in PR.glob("*.json") if json.loads(f.read_text(encoding="utf-8-sig")).get("taskId") in set(ids)]
retained += [proof(r[4]) for r in rows if not r[4].startswith("P:")]
print("RETAINED-LOWER-BOUND",len(retained),sum((len(json.dumps(p,ensure_ascii=False,separators=(",",":")).encode())+3)//4 for p in retained))
```

候选与维护成本复算：分别对三条来源路径执行 `git log --format=%H|%cI -- <路径>`，从 pending、collected、promoted 三次提交读取候选字段；对 collected 提交读取同 ID 活动整理卡并核对首次新增；对整理完成执行 `git diff --no-renames --name-only <SHA>^ <SHA>`、`git diff --no-renames --numstat <SHA>^ <SHA>`，分别求路径数和行数。时间差使用 Git `%cI` 解析后相减。候选字段完整性、失败终态以及原始记录中的人工/model 成本缺失，不能由时间差或 diff 行数替代。

验收入口：`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1`；`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths docs,开发管理`；对本轮实际改动运行 pending-whitespace，暂存后 cached diff 检查由规定 finalizer 执行。docs 报告变化按项目规则执行数据链检查。未满足的收益证明如实保留为 stop，不为完成报告修改阈值或增加机制。
