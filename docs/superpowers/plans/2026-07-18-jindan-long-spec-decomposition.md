# 金丹长规格拆分迁移 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task in the current workspace. Do not create a worktree by default: the authoritative migration source is currently untracked and would not appear in a new worktree. Use superpowers:subagent-driven-development only if the user explicitly requests subagents.

**Goal:** 将 `docs/superpowers/specs/2026-07-15-jindan-seventeen-road-base-effects-table.md` 的全部有效规则迁入短而专责的事实源，将评审记录、旧口径冲突、内容生产和实现缺口转成明确任务，验证覆盖后移除原长规格。

**Architecture:** 采用“规则事实源、内容矩阵、后续任务”三层拆分。战场公共规则不留在金丹文件中；金丹结构、装配冲突与51项效果分别归档；评论审查和自检只作为迁移验收依据，不复制成新的事实源。现有已审核旧矩阵先保全自辟元婴内容，再原子切换到六识／因果新口径，避免新旧事实同时有效。

**Tech Stack:** Markdown/TXT 事实源、PowerShell 7、`rg`、Git、项目文本检查脚本。

---

## 执行边界

- 原长规格当前为未跟踪文件。所有迁移步骤必须增量读取，禁止覆盖重建；最终删除前必须完成本计划的覆盖断言。
- 本轮属于已批准规则迁移，不新增剧情、人物、功法、术法或神通，因而不突破内容冻结；具体名称、组合配方、现象原型和素材仍进入后续任务。
- 不修改任何倍率、CT、资源、目标数或概率，不宣称数值平衡；因此文档迁移本身不运行 BattleSim。后续数值任务必须按项目规则运行 BattleSim。
- 旧矩阵中的自辟元婴方向不得随矩阵重写丢失，须先迁入独立事实源。
- `DS-JINDAN-NAMES-20260718-01` 只负责51项效果正式名称，不得修改效果功能、位格规则或原矩阵。
- 默认不暂存、不提交。若用户在执行对话中明确授权提交，每个任务只暂存并提交该任务条目中 `Files` 列出的明确路径，不得带入工作树中的无关改动；提交时使用 `git commit --only` 并逐项列出这些路径。

## 文件职责锁定

### 新建规则事实源

| 文件 | 唯一职责 | 来源章节 |
|---|---|---|
| `docs/基础设定/战场空间与环境规则.txt` | 六角拓扑、加权距离、环境供给、场景物、2.5D高度、格边、地表、现象、掩体、体型与移动形态 | 1.1、1.3～1.8、1.15、1.17 |
| `docs/基础设定/五行显化与环境交互规则.txt` | 五行事件、显化载体、植物、火种、重力、磁阵、潮汐 | 1.2、1.9～1.13 |
| `docs/基础设定/战斗状态与时空因果结算规则.txt` | 临时状态、局部回溯、因果嫁接、因价、果先因后 | 1.14、1.16、1.18～1.20 |
| `docs/基础设定/金丹基础效果装配与冲突规则.txt` | 装配、组合、跨路兼容、同阶QTE、压阶、山河运行、显示字段、跨效果边界 | 1.21、1.24～1.27、1.29、八 |
| `docs/基础设定/自辟元婴方向设定.txt` | 保全旧矩阵已有的自辟元婴方向及其边界；不混入51项金丹效果 | 旧矩阵第二节 |

### 重写或同步现有事实源

| 文件 | 迁移后的职责 |
|---|---|
| `docs/基础设定/元婴锚点与金丹位格设定.txt` | 十七道路、三种真实位格、三阶段、主承载、不可换丹枢核心、位格丧失死亡、元婴锚点与三位开放 |
| `docs/基础设定/元婴锚点与金丹位格矩阵.txt` | 十七道路变量边界与51个 `effect_id` 的源／化／界版本；不再保存自辟元婴方向或旧诗性席位名 |
| `docs/基础设定/金丹位格批量生产模板.txt` | 引用新道路、三字段、效果ID、主承载、核心不换和命名任务，不再使用旧51席口径 |
| `docs/基础设定/战斗系统.txt` | 保留CTB、基础行动和攻击流程；公共空间、环境、状态与特殊结算改为引用新事实源，不重复维护冲突规则 |
| `docs/基础设定/灵根设定.txt` | 删除变异属性外圈额外±5%，只继承母属性关系与具体技能特性 |
| `docs/基础设定/修行境界.txt`、`docs/基础设定/境界特性.txt` | 同步金丹一／二／三真实位格、位格丧失死亡、核心不可换、元婴原子升格 |
| `docs/基础设定/角色数值设计.txt` | 保留旧BattleSim数值快照但标明不能验证新三位、装配、QTE和死亡规则 |

### 转入任务而非事实源的内容

| 原内容 | 目标任务位置 |
|---|---|
| 第二节评论审查、9.2内部防错、10.1结论、十一节自检 | 本计划的覆盖与验证步骤；不复制到事实源 |
| 9.1旧设定冲突和下游人物／剧情重判 | `开发管理/任务列表/内容设计任务.txt` |
| BattleSim、Unity、CSV/asset尚未接入项 | `开发管理/任务列表/数值与战斗任务.txt`、`场景与Unity任务.txt`、`数据链路任务.txt` |
| 51项正式名称、具体组合配方、区域现象配对、音画素材 | 内容设计任务；名称任务继续引用 `DS-JINDAN-NAMES-20260718-01` |

---

### Task 1: 锁定迁移基线与覆盖清单

**Files:**
- Read: `docs/superpowers/specs/2026-07-15-jindan-seventeen-road-base-effects-table.md`
- Read: `docs/基础设定/元婴锚点与金丹位格设定.txt`
- Read: `docs/基础设定/元婴锚点与金丹位格矩阵.txt`
- Read: `docs/基础设定/战斗系统.txt`
- Read: `开发管理/设计-当前状态.txt`

- [ ] **Step 1: 确认原长规格仍为未跟踪且未被其他改动覆盖**

Run:

```powershell
git status --short -- "docs/superpowers/specs/2026-07-15-jindan-seventeen-road-base-effects-table.md"
```

Expected: exactly one `??` line.

- [ ] **Step 2: 记录原文件哈希、行数、29个一级子节和51个效果行**

Run:

```powershell
$p = 'docs/superpowers/specs/2026-07-15-jindan-seventeen-road-base-effects-table.md'
$text = Get-Content -LiteralPath $p -Raw
"sha256=$((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash)"
"lines=$((Get-Content -LiteralPath $p).Count)"
"ruleSections=$([regex]::Matches($text, '(?m)^### 1\.\d+ ').Count)"
"effectRows=$([regex]::Matches($text, '(?m)^\| (?:WOOD|FIRE|EARTH|METAL|WATER|YIN|YANG|SPACE|TIME|SIGHT|HEARING|BREATH|INGEST|BODY|ACTION|CAUSE|RESULT)_[A-Z_]+ \|').Count)"
```

Expected: `ruleSections=29`, `effectRows=51`; save the hash in the execution commentary or task log.

- [ ] **Step 3: 输出现有旧口径影响文件清单**

Run:

```powershell
rg -l -S "五蕴|色分支|受分支|想分支|行分支|识分支|业分支|缘分支|轮回分支|一源、多化、多界|多个化位|多个界位|报春根|周天焰|枯荣林|51席|51 席" docs --glob '!docs/superpowers/**' | Sort-Object
```

Expected: the list includes core facts, batch template, selected cultivation content and selected story/NPC files. Do not edit the story/NPC files in this migration slice; Task 7 records them precisely.

- [ ] **Step 4: 建立本轮预期路径白名单**

Expected writable paths are exactly:

```text
docs/基础设定/战场空间与环境规则.txt
docs/基础设定/五行显化与环境交互规则.txt
docs/基础设定/战斗状态与时空因果结算规则.txt
docs/基础设定/金丹基础效果装配与冲突规则.txt
docs/基础设定/自辟元婴方向设定.txt
docs/基础设定/元婴锚点与金丹位格设定.txt
docs/基础设定/元婴锚点与金丹位格矩阵.txt
docs/基础设定/金丹位格批量生产模板.txt
docs/基础设定/战斗系统.txt
docs/基础设定/灵根设定.txt
docs/基础设定/修行境界.txt
docs/基础设定/境界特性.txt
docs/基础设定/角色数值设计.txt
docs/剧情/剧情生产规范.txt
docs/剧情/普通NPC设计模板.txt
开发管理/NPC叙事字段检查清单.txt
开发管理/普通NPC首批候选清单.txt
开发管理/任务列表/数值与战斗任务.txt
开发管理/任务列表/场景与Unity任务.txt
开发管理/任务列表/数据链路任务.txt
开发管理/任务列表/内容设计任务.txt
开发管理/AI合作沟通.txt
开发管理/C-LORE-01-旧口径迁移映射表.txt
开发管理/设计-当前状态.txt
docs/superpowers/specs/2026-07-15-jindan-seventeen-road-base-effects-table.md
```

The source spec is writable only for the final explicit deletion. Any other writable path stops execution for user review.

---

### Task 2: 拆出三份公共战斗规则事实源

**Files:**
- Create: `docs/基础设定/战场空间与环境规则.txt`
- Create: `docs/基础设定/五行显化与环境交互规则.txt`
- Create: `docs/基础设定/战斗状态与时空因果结算规则.txt`
- Modify: `docs/基础设定/战斗系统.txt`

- [ ] **Step 1: 新建《战场空间与环境规则》并按固定目录迁移**

Required headings:

```text
# 战场空间与环境规则
## 一、六角拓扑与加权距离
## 二、战场环境供给与生成
## 三、主要场景交互物
## 四、2.5D高度与有向格边
## 五、地表状态
## 六、区域现象通道
## 七、低位掩体与实体障碍
## 八、单锚点体型与移动形态
```

Move the normative content of source sections 1.1, 1.3～1.8, 1.15 and 1.17. Preserve fixed topology versus weighted distance, one occupant per hex column, directed edge types, single surface slot, six phenomenon channels, deterministic reaction order, cover/obstacle separation and mutually exclusive movement modes. Do not copy review dialogue.

- [ ] **Step 2: 新建《五行显化与环境交互规则》并按固定目录迁移**

Required headings:

```text
# 五行显化与环境交互规则
## 一、五行事件与灵域
## 二、显化配方与运行载体
## 三、生发演化
## 四、升烈蔓延与火种
## 五、地载重力
## 六、磁极从革与百兵磁阵
## 七、潮汐易地
```

Move sections 1.2 and 1.9～1.13. Keep all fixed failure boundaries: no permanent post-battle resources, no equipment-weight subsystem, no temporary item units/pathfinding, no full-map fluid, no arbitrary evolution tree and no free special fire seed.

- [ ] **Step 3: 新建《战斗状态与时空因果结算规则》并按固定目录迁移**

Required headings:

```text
# 战斗状态与时空因果结算规则
## 一、临时状态极性、来源与保护
## 二、角色局部状态回溯
## 三、因果嫁接与自动响应
## 四、因价增减
## 五、果先因后
```

Move sections 1.14, 1.16 and 1.18～1.20. Preserve the three status polarities, separate stance/process/task/life carriers, checkpoint whitelist, external-cost replay, protected history, six post-event types, shared automatic response capacity, configurable cost bands and the reversible/collateral debt boundary.

- [ ] **Step 4: 缩减《战斗系统》中的冲突章节并改为权威引用**

Keep CTB, basic action and attack flow. Replace the hard-coded terrain table, scalar distance assumptions, old concealment thresholds and generic buff/debuff stacking text with short summaries and direct references to the three new files. Do not silently keep two authoritative terrain/status systems.

- [ ] **Step 5: 验证公共规则文件目录和禁止项**

Run:

```powershell
rg -n "^#|^## " "docs/基础设定/战场空间与环境规则.txt" "docs/基础设定/五行显化与环境交互规则.txt" "docs/基础设定/战斗状态与时空因果结算规则.txt"
rg -n "动态三维占格|全图流体|装备重量|临时物品单位|NEUTRAL|全战场倒放|随机触发顺序" "docs/基础设定/战场空间与环境规则.txt" "docs/基础设定/五行显化与环境交互规则.txt" "docs/基础设定/战斗状态与时空因果结算规则.txt"
```

Expected: required headings exist; each forbidden architecture appears only as an explicit prohibition, never as an approved mechanism.

- [ ] **Step 6: Optional commit gate**

If and only if the user authorized commits, stage and commit only the four Task 2 paths with message `docs: split shared combat rule facts`.

---

### Task 3: 迁移金丹结构与保全自辟元婴方向

**Files:**
- Modify: `docs/基础设定/元婴锚点与金丹位格设定.txt`
- Create: `docs/基础设定/自辟元婴方向设定.txt`

- [ ] **Step 1: 先从旧矩阵迁出全部自辟元婴方向**

Copy the complete normative content of old matrix section two into `自辟元婴方向设定.txt`, preserving each direction's effect, boundary, cost, suggested relation and narrative position. Add a source note that these are self-founded Yuanying directions, not the 17 existing roads and not the 51 Jindan effects.

- [ ] **Step 2: 重写《元婴锚点与金丹位格设定》的道路与位格总则**

Required structure:

```text
# 元婴锚点与金丹位格设定
## 一、境界与版本边界
## 二、十七条道路
## 三、源位、化位、界位
## 四、金丹三阶段与真实位格
## 五、紫府神通主承载与丹枢核心
## 六、位格丧失与丹毁死亡
## 七、元婴道路锚点与三位受控开放
## 八、自辟元婴边界
```

Use five elements + six consciousnesses + yin/yang + space/time + cause/result. Each road has source/change/domain position types and three effect candidates; effects are equipped, not permanently bound to positions. Include one/two/three real positions by Jindan stage, one distinct primary carrier per stable position, immutable `JindanCoreBinding`, protected-history death on real-position/core loss, safe reforge boundaries and atomic Yuanying promotion exceptions.

- [ ] **Step 3: 删除旧“一源、多化、多界”和五蕴／业缘轮回的现行事实口径**

The self-founded Yuanying file may mention historical or suggested relationships, but the current Jindan road list must not contain the old 17-road taxonomy, old poetic seat names or expandable multiple change/domain seats.

- [ ] **Step 4: 验证结构规则**

Run:

```powershell
rg -n "六识|见识|闻识|息识|食识|身识|意识|因道路|果道路|JindanCoreBinding|丹毁死亡|YuanyingRoadAnchor" "docs/基础设定/元婴锚点与金丹位格设定.txt"
rg -n "一源、多化、多界|五蕴：色、受、想、行、识|因果：业、缘、轮回|报春根|周天焰|枯荣林" "docs/基础设定/元婴锚点与金丹位格设定.txt"
```

Expected: the first query finds every required concept; the second query returns no matches.

- [ ] **Step 5: Optional commit gate**

If authorized, commit only the two Task 3 paths with message `docs: migrate jindan and yuanying structure rules`.

---

### Task 4: 重建51效果矩阵并拆出装配冲突规则

**Files:**
- Modify: `docs/基础设定/元婴锚点与金丹位格矩阵.txt`
- Create: `docs/基础设定/金丹基础效果装配与冲突规则.txt`
- Modify: `docs/基础设定/金丹位格批量生产模板.txt`

- [ ] **Step 1: 将旧矩阵改成十七道路与51效果的唯一事实源**

The matrix must contain:

```text
## 一、矩阵口径与字段
## 二、道路变量与不可越界项
## 三、五行十五项效果
## 四、阴阳与宇宙十二项效果
## 五、六识十八项效果
## 六、因果六项效果
```

Each effect row keeps `effect_id`, current functional alias, source version, change version, domain version, tactical presentation and mountain/river application. Keep names explicitly provisional until `DS-JINDAN-NAMES-20260718-01` is reviewed. Do not write a position name into the effect-name field.

- [ ] **Step 2: 新建《金丹基础效果装配与冲突规则》**

Required headings:

```text
# 金丹基础效果装配与冲突规则
## 一、每位格一项效果与切换事务
## 二、同效纵向嵌套与异效组合
## 三、跨道路静态兼容契约
## 四、同阶冲突与可选领域对抗
## 五、压阶、降级与跨阶挑战资格
## 六、山河规则运行与灵石维护
## 七、金丹、位格、效果三字段
## 八、相似效果的权限边界索引
```

Move sections 1.21, 1.24～1.27, 1.29 and section eight. Preserve deterministic comparison order, no random tie-break, optional hold/QTE/skip, unique resource ledger, explicit `comboProfileId`, static `PositionCompatibilityContract`, `CrossTierChallengeGrant`, spirit-stone-only routine upkeep and world-state-commit boundary.

- [ ] **Step 3: 更新批量生产模板**

Replace old “51 seats/formal poetic seat names” with:

- Jindan name = world rule field.
- Position name = source/change/domain type field.
- Effect name = `effect_id` display field.
- One effect per real position.
- One distinct Purple Mansion primary carrier per stable position.
- Same effect may nest across positions; different effects require `comboProfileId`.
- Dan-shu core cannot be rebound; real-position loss causes death.
- Formal effect names remain assigned to the DeepSeek task and may not be invented during batch production.

- [ ] **Step 4: 验证17道路、51效果和三版本完整性**

Run:

```powershell
$p = 'docs/基础设定/元婴锚点与金丹位格矩阵.txt'
$text = Get-Content -LiteralPath $p -Raw
$ids = [regex]::Matches($text, '(?m)^\| ((?:WOOD|FIRE|EARTH|METAL|WATER|YIN|YANG|SPACE|TIME|SIGHT|HEARING|BREATH|INGEST|BODY|ACTION|CAUSE|RESULT)_[A-Z_]+) \|') | ForEach-Object { $_.Groups[1].Value }
if ($ids.Count -ne 51) { throw "effect count: $($ids.Count)" }
if (($ids | Sort-Object -Unique).Count -ne 51) { throw 'duplicate effect_id' }
'MATRIX_OK effectIds=51'
```

Expected: `MATRIX_OK effectIds=51`.

- [ ] **Step 5: 验证旧矩阵内容已迁出而非丢失**

Run:

```powershell
rg -n "太一道庭|剑修自辟|轮回|宿慧|苻渊|万灵真形" "docs/基础设定/自辟元婴方向设定.txt"
rg -n "报春根|周天焰|枯荣林|五蕴|业、缘、轮回" "docs/基础设定/元婴锚点与金丹位格矩阵.txt"
```

Expected: all self-founded directions are found in the dedicated file; the second query returns no current-matrix matches.

- [ ] **Step 6: Optional commit gate**

If authorized, commit only the three Task 4 paths with message `docs: establish jindan effect matrix and conflict rules`.

---

### Task 5: 同步直接依赖的基础事实源

**Files:**
- Modify: `docs/基础设定/灵根设定.txt`
- Modify: `docs/基础设定/修行境界.txt`
- Modify: `docs/基础设定/境界特性.txt`
- Modify: `docs/基础设定/角色数值设计.txt`

- [ ] **Step 1: 移除变异属性外圈额外±5%**

In `灵根设定.txt`, retain mother-attribute relationship and each skill's explicit special property. Remove the universal extra +5%/-5% outer-ring rule and its examples. Update the formula/example text so it no longer adds the removed layer.

- [ ] **Step 2: 同步修行境界的金丹三阶段**

State that early/middle/perfect Jindan gain one/two/three real positions in a free order, share `JINDAN` rule tier, and never auto-suppress each other by stage. Real-position loss means death, not stage regression. Legal Yuanying promotion atomically elevates the whole structure.

- [ ] **Step 3: 同步境界特性的位格、主承载与核心**

Replace one-source/multi-change/multi-domain and old matrix references. Add one primary Purple Mansion ability per stable real position, shared auxiliary ledger, safe-cave carrier/mansion-slot reforge and immutable Dan-shu core.

- [ ] **Step 4: 限定角色数值设计中的旧BattleSim快照**

Do not alter historical numbers. Add an explicit coverage note: current BattleSim results do not contain the 51-effect loadout, three real positions, compatibility compiler, QTE, deterministic resource contest, real-position death, mountain/river ledger or valid Jindan samples for those systems. Existing G2 conclusions remain valid only for their old implemented model.

- [ ] **Step 5: 验证直接事实源不再宣称旧结构有效**

Run:

```powershell
rg -n "一源、多化、多界|多个化位|多个界位|报春根|周天焰|枯荣林|外圈修正额外 \+5%|外圈修正额外 -5%|失去位格.*回退" "docs/基础设定/灵根设定.txt" "docs/基础设定/修行境界.txt" "docs/基础设定/境界特性.txt" "docs/基础设定/角色数值设计.txt"
```

Expected: no active-rule matches. Historical passages may remain only if explicitly labeled historical and excluded from current behavior.

- [ ] **Step 6: Optional commit gate**

If authorized, commit only the four Task 5 paths with message `docs: align realm and element facts with jindan rules`.

---

### Task 6: 把未实现机制和内容生产转成分线任务

**Files:**
- Modify: `开发管理/任务列表/数值与战斗任务.txt`
- Modify: `开发管理/任务列表/场景与Unity任务.txt`
- Modify: `开发管理/任务列表/数据链路任务.txt`
- Modify: `开发管理/任务列表/内容设计任务.txt`
- Modify: `开发管理/AI合作沟通.txt`

- [ ] **Step 1: 数值与战斗任务增加三个切片**

Add these exact backlog IDs without placing them in `当前任务队列.txt`:

| ID | State | Scope |
|---|---|---|
| `N-JD-RULE-01` | blocked by Jindan schema and valid samples | three positions, loadout, carrier ledgers, deterministic conflict, optional QTE/skip, cross-tier profiles and real-position death; all tunables in config |
| `N-ENV-01` | blocked by `N-DIST-01` | directed edges, weighted distance, cover/obstacles, surfaces and six phenomenon channels |
| `N-STATE-01` | pending after schema | status polarities/carriers, local rewind, causal response capacity and debt invariants |

Update existing `N-SEAT-01` and `N-SUPPRESS-01` summaries so they no longer describe random victory or trigger probability; point them to deterministic eligibility and conflict resolution.

- [ ] **Step 2: 场景与Unity任务增加两个切片**

Add `U-JD-RULE-01` for loadout/compatibility/conflict UI/QTE and `U-ENV-RULE-01` for battlefield environment rendering and interaction. Both remain blocked by data schema and BattleSim fixtures; neither may silently hard-code tuning values.

- [ ] **Step 3: 数据链路任务增加两个切片**

Add `D-JD-SCHEMA-01` for road/effect/position contracts, combo profiles, core/carrier bindings and presentation/localization keys. Add `D-ENV-SCHEMA-01` for directed edges, surface prototypes, phenomenon channels and pair tables. Both remain blocked by G3 completion and require failed-closed import validation.

- [ ] **Step 4: 内容设计任务增加四个切片**

Add:

- `C-JD-NAME-01`: 51 formal effect names, linked to `DS-JINDAN-NAMES-20260718-01`.
- `C-JD-COMBO-01`: explicit cross-effect recipes only; no free-tag synthesis.
- `C-ENV-PROFILE-01`: concrete surface/phenomenon prototypes and complete same-channel pair tables.
- `C-JD-LORE-MIGRATE-01`: re-evaluate story, NPC, cultivation and sect text that still uses five aggregates, karma/condition/reincarnation, old poetic seats or one-source/multi-change/multi-domain.

Keep them out of the current queue while content freeze or prerequisites apply.

- [ ] **Step 5: 收敛DeepSeek交接记录**

Update `DS-JINDAN-NAMES-20260718-01` to read the new matrix and three-field rule, not the source long spec. Preserve its pending status and the prohibition on modifying rule facts.

- [ ] **Step 6: 验证任务路由**

Run:

```powershell
rg -n "N-JD-RULE-01|N-ENV-01|N-STATE-01|U-JD-RULE-01|U-ENV-RULE-01|D-JD-SCHEMA-01|D-ENV-SCHEMA-01|C-JD-NAME-01|C-JD-COMBO-01|C-ENV-PROFILE-01|C-JD-LORE-MIGRATE-01|DS-JINDAN-NAMES-20260718-01" 开发管理/任务列表 开发管理/AI合作沟通.txt
rg -n "N-JD-RULE-01|U-JD-RULE-01|D-JD-SCHEMA-01|C-JD-NAME-01" 开发管理/当前任务队列.txt
```

Expected: all IDs appear in their backlog/communication files; the second query returns no matches.

- [ ] **Step 7: Optional commit gate**

If authorized, commit only the five Task 6 paths with message `docs: route jindan migration gaps into backlogs`.

---

### Task 7: 记录下游旧口径迁移范围

**Files:**
- Modify: `开发管理/任务列表/内容设计任务.txt`
- Modify: `开发管理/设计-当前状态.txt`
- Modify: `开发管理/C-LORE-01-旧口径迁移映射表.txt`

- [ ] **Step 1: 为 `C-JD-LORE-MIGRATE-01` 写入精确影响范围**

At minimum include these current files discovered by the baseline scan:

```text
docs/角色养成/功法/太虚观/空无般若经.txt
docs/角色养成/功法/太一道庭/抱元守一经.txt
docs/角色养成/术法/古修/守真.txt
docs/剧情/背景与重要NPC设计规范.txt
docs/剧情/设定补充/真形体系说明.txt
docs/剧情/世界背景故事.txt
docs/剧情/重要NPC/苻渊.txt
docs/剧情/重要NPC/卫长庚.txt
docs/剧情/重要NPC/谢凌沧.txt
docs/剧情/重要NPC/玄荒新路天才.txt
docs/剧情/重要NPC/姚观寂.txt
docs/剧情/重要NPC/祝融烈.txt
docs/剧情/主线/世界主线-后续拆分工作清单.txt
docs/剧情/主线/世界主线-总纲.txt
```

The task must require per-file semantic re-judgment; it may not mechanically map old road names to new ones. It remains a content task and follows the narrative/content rules when executed.

- [ ] **Step 2: 标记旧迁移映射表已被新口径超越**

Do not delete `C-LORE-01-旧口径迁移映射表.txt`. Add a short status note that its one-source/multi-change/multi-domain and old matrix assumptions are historical; current Jindan migration uses the new fact sources and `C-JD-LORE-MIGRATE-01`.

- [ ] **Step 3: 更新设计当前状态的已验证事实与风险**

Only after Tasks 2～6 validation, record:

- New fact-source paths and their responsibility.
- 17 roads / 51 effect IDs / one effect per real position.
- Jindan stage shares one rule tier; real-position loss means death; Dan-shu core cannot be rebound.
- BattleSim, Unity and data chain do not yet implement these rules.
- Downstream lore migration remains a tracked risk, not a completed fact.

- [ ] **Step 4: Optional commit gate**

If authorized, commit only the Task 7 paths with message `docs: record downstream jindan migration scope`.

---

### Task 8: 重接事实源引用并做完整覆盖审计

**Files:**
- Modify: `docs/剧情/剧情生产规范.txt`
- Modify: `docs/剧情/普通NPC设计模板.txt`
- Modify: `开发管理/NPC叙事字段检查清单.txt`
- Modify: `开发管理/普通NPC首批候选清单.txt`
- Read: all fact files created or modified by Tasks 2～5.

- [ ] **Step 1: 更新直接引用职责**

Required reference routing:

- Structure and lifecycle → `元婴锚点与金丹位格设定.txt`.
- 17 roads and 51 effects → `元婴锚点与金丹位格矩阵.txt`.
- Loadout/compatibility/conflicts → `金丹基础效果装配与冲突规则.txt`.
- Self-founded Yuanying directions → `自辟元婴方向设定.txt`.
- Environment/topology → `战场空间与环境规则.txt`.
- Five-element interaction → `五行显化与环境交互规则.txt`.
- Status/time/causality → `战斗状态与时空因果结算规则.txt`.

Do not edit any other reference-bearing file in this task. Archived plans, task archives and downstream story/NPC content remain unchanged; unexpected current references are appended to `C-JD-LORE-MIGRATE-01` instead of expanding this migration slice.

- [ ] **Step 2: 验证每个原章节有唯一去向**

Use this exact coverage map:

```text
1.1 -> 战场空间与环境规则
1.2 -> 五行显化与环境交互规则
1.3-1.8 -> 战场空间与环境规则
1.9-1.13 -> 五行显化与环境交互规则
1.14 -> 战斗状态与时空因果结算规则
1.15 -> 战场空间与环境规则
1.16 -> 战斗状态与时空因果结算规则
1.17 -> 战场空间与环境规则
1.18-1.20 -> 战斗状态与时空因果结算规则
1.21 -> 金丹基础效果装配与冲突规则
1.22-1.23 -> 元婴锚点与金丹位格设定
1.24-1.27 -> 金丹基础效果装配与冲突规则
1.28 -> 元婴锚点与金丹位格设定
1.29 -> 金丹基础效果装配与冲突规则
第二节与9.2/10.1/十一节 -> 本计划验收断言
第三至七节 -> 元婴锚点与金丹位格矩阵
第八节 -> 金丹基础效果装配与冲突规则
9.1与10.2 -> 分线任务和DeepSeek交接
```

Any source paragraph not represented by one of these destinations blocks deletion.

- [ ] **Step 3: 扫描现行事实中的旧口径残留**

Run:

```powershell
rg -n -S "一源、多化、多界|五蕴：色、受、想、行、识|因果：业、缘、轮回|报春根|周天焰|枯荣林|功能性临时代号不能同时作为显示事实源" docs/基础设定 docs/剧情/剧情生产规范.txt
```

Expected: no active-rule matches. If downstream content still matches, it must be listed in `C-JD-LORE-MIGRATE-01` and in current-state risk; do not claim repository-wide lore migration complete.

- [ ] **Step 4: 检查新事实源之间无重复权威声明**

Manually verify each new file links outward for adjacent systems instead of copying their full rules. Especially check that `战斗系统.txt` no longer duplicates terrain/status tables and the Jindan matrix no longer contains self-founded Yuanying directions or loadout algorithms.

---

### Task 9: 最终验证并移除原长规格

**Files:**
- Delete after all gates pass: `docs/superpowers/specs/2026-07-15-jindan-seventeen-road-base-effects-table.md`
- Verify all paths modified by Tasks 2～8.

- [ ] **Step 1: 运行矩阵、字段和任务总断言**

Run:

```powershell
$matrix = Get-Content -LiteralPath 'docs/基础设定/元婴锚点与金丹位格矩阵.txt' -Raw
$ids = [regex]::Matches($matrix, '(?m)^\| ((?:WOOD|FIRE|EARTH|METAL|WATER|YIN|YANG|SPACE|TIME|SIGHT|HEARING|BREATH|INGEST|BODY|ACTION|CAUSE|RESULT)_[A-Z_]+) \|') | ForEach-Object { $_.Groups[1].Value }
if ($ids.Count -ne 51 -or ($ids | Sort-Object -Unique).Count -ne 51) { throw '51 effect_id coverage failed' }
$required = @(
  'docs/基础设定/战场空间与环境规则.txt',
  'docs/基础设定/五行显化与环境交互规则.txt',
  'docs/基础设定/战斗状态与时空因果结算规则.txt',
  'docs/基础设定/金丹基础效果装配与冲突规则.txt',
  'docs/基础设定/自辟元婴方向设定.txt'
)
foreach ($f in $required) { if (-not (Test-Path -LiteralPath $f)) { throw "missing $f" } }
'DECOMPOSITION_ASSERTIONS_OK'
```

Expected: `DECOMPOSITION_ASSERTIONS_OK`.

- [ ] **Step 2: 运行文本与行尾检查**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 'docs/基础设定,docs/剧情/剧情生产规范.txt,docs/剧情/普通NPC设计模板.txt,开发管理/NPC叙事字段检查清单.txt,开发管理/普通NPC首批候选清单.txt,开发管理/任务列表,开发管理/AI合作沟通.txt,开发管理/C-LORE-01-旧口径迁移映射表.txt,开发管理/设计-当前状态.txt'
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'docs/基础设定/战场空间与环境规则.txt|docs/基础设定/五行显化与环境交互规则.txt|docs/基础设定/战斗状态与时空因果结算规则.txt|docs/基础设定/金丹基础效果装配与冲突规则.txt|docs/基础设定/自辟元婴方向设定.txt|docs/基础设定/元婴锚点与金丹位格设定.txt|docs/基础设定/元婴锚点与金丹位格矩阵.txt|docs/基础设定/金丹位格批量生产模板.txt|docs/基础设定/战斗系统.txt|docs/基础设定/灵根设定.txt|docs/基础设定/修行境界.txt|docs/基础设定/境界特性.txt|docs/基础设定/角色数值设计.txt|docs/剧情/剧情生产规范.txt|docs/剧情/普通NPC设计模板.txt|开发管理/NPC叙事字段检查清单.txt|开发管理/普通NPC首批候选清单.txt|开发管理/任务列表/数值与战斗任务.txt|开发管理/任务列表/场景与Unity任务.txt|开发管理/任务列表/数据链路任务.txt|开发管理/任务列表/内容设计任务.txt|开发管理/AI合作沟通.txt|开发管理/C-LORE-01-旧口径迁移映射表.txt|开发管理/设计-当前状态.txt'
git diff --check
```

Expected: all commands exit 0. The whitespace command already contains the exact writable-path list and must not be broadened to a directory or glob.

- [ ] **Step 3: 确认没有现行文件继续依赖原长规格**

Run:

```powershell
rg -n -S "2026-07-15-jindan-seventeen-road-base-effects-table\.md" docs 开发管理 --glob '!docs/superpowers/plans/2026-07-18-jindan-long-spec-decomposition.md'
```

Expected: no matches. Update the DeepSeek task and any current handoff before continuing.

- [ ] **Step 4: 删除原长规格**

Use `apply_patch` with an explicit `Delete File` patch for:

```text
D:\天章游戏开发\docs\superpowers\specs\2026-07-15-jindan-seventeen-road-base-effects-table.md
```

Do not use a recursive delete, wildcard or computed path. This deletion is allowed only after Steps 1～3 pass.

- [ ] **Step 5: 删除后重跑最小验证**

Run the Step 1 assertions again, then:

```powershell
git status --short
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 'docs/基础设定,开发管理/任务列表,开发管理/AI合作沟通.txt,开发管理/设计-当前状态.txt'
git diff --check
```

Expected: source spec is absent; all new facts and tasks remain; checks exit 0.

- [ ] **Step 6: 报告删除与恢复边界**

The handoff must state that the original was an untracked file and has been removed only after its content was migrated. Recovery of the exact old file depends on conversation history or any user backup; the migrated facts and tasks are preserved in the new files.

- [ ] **Step 7: Optional final commit gate**

If the user authorized commits, stage only the full migration path set, run `git diff --cached --check`, and create one final cutover commit with message `docs: cut over jindan rules to focused fact sources`. Otherwise leave all changes unstaged and report exact status.

---

## 完成条件

- 原29个规则子节均命中覆盖映射。
- 17条道路、51个唯一 `effect_id`、每项三种位格版本仍完整。
- 自辟元婴方向已迁入独立文件，没有随旧矩阵重写丢失。
- 战场公共规则不再重复塞在金丹文件中。
- 旧五蕴／业缘轮回／一源多化多界／诗性席位名不再作为现行金丹事实。
- 具体名称、组合、现象档案、数据、BattleSim、Unity和下游剧情迁移均有唯一任务入口。
- `设计-当前状态.txt` 明确新规则尚未接入BattleSim、Unity和数据链。
- 原长规格没有现行引用，并在验证通过后被删除。
- 所有文本、行尾和限定差异检查通过；没有暂存或提交无关文件。
