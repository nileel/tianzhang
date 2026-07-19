# 金丹位格证位领域内核 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不提前生产 51 份位格内容、不接 UI 的前提下，实现可测试、可存档、确定性的金丹证位领域内核，证明永久道证履历、知识遮蔽、驻地证位、并行争位、原子绑定与 NPC 硬门槛能够按已确认规格共同成立。

**Architecture:** 新内核放入现有 `TianZhang.Domain` assembly，通过 `Cultivation/TianZhang.Domain.asmref` 归属领域层，不依赖场景、UI、战斗控制器或 Editor importer。玩法系统只提交已验证的行为事件；领域内核维护履历、尝试、位格版本和绑定事务；世界时间、设施、事件、UI、CSV importer 与 NPC 调度在后续独立计划中通过明确端口接入。

**Tech Stack:** Unity 6 / C#、现有 `TianZhang.Domain` assembly、NUnit EditMode tests、PowerShell 7、Git、项目文本与空白检查脚本。

---

## 权威来源与执行边界

- 权威设计：`docs/superpowers/specs/2026-07-19-jindan-position-proof-and-contestation-design.md`。
- 当前金丹结构事实源：`docs/基础设定/元婴锚点与金丹位格设定.txt`、`docs/基础设定/元婴锚点与金丹位格矩阵.txt`、`docs/基础设定/金丹基础效果装配与冲突规则.txt`。
- 本计划只实现“领域内核 + 火·源位测试夹具 + 正式规则摘要”。测试夹具用于证明接口，不是最终数值，也不进入生产 CSV/asset。
- 本计划不创建 worktree。执行前只检查当前目录和目标路径；若目标路径出现其他任务改动，停止并报告，不覆盖、不 stash、不 reset、不 clean。
- 未经用户当次明确确认，不暂存、不提交、不推送。每个 Task 的提交步骤是授权后的可执行命令；没有授权时跳过该步骤并保持改动未暂存。
- 不修改当前已有的术法资产、CSV、`CombatResolver.cs`、`SpellData.cs`、`DataConfigImporter.cs`、`tools/check-data-chain.ps1`、`开发管理/当前任务队列.txt` 或 `设计总结.txt`。
- 本计划不运行 BattleSim，因为不确定适配度、周期、事件强度、战斗参数或成功率数值；后续出现这些数值时必须按项目规则运行 BattleSim。
- 每完成一个 Task，运行该 Task 规定的最小验证并汇报结果，再继续下一 Task。

## 子项目拆分

已确认规格横跨五个可独立验收子项目，本计划只执行第 1 项：

1. **领域内核（本计划）**：履历、知识投影、尝试状态机、争位协调、原子绑定、快照、NPC 硬门槛。
2. **51 档案与数据链**：17 条道路共享指标、51 份最低条件与标志性成就、CSV、importer、asset 和数据链检查。
3. **世界时间与证位事件**：驻地边界、快进、事件暂停、设施/资源端口和正式 `AdventureScene` 接入。
4. **玩家界面与情报传播**：未知/方向/完整知识显示、天地异象、空位情报、动态预估和临界争位界面。
5. **NPC 调度与数值验证**：持久 NPC 事件驱动重算、主观风险模型、寿元阈值、BattleSim 与完整验收。

后四项必须在本计划的公开类型和测试通过后分别使用 `superpowers:writing-plans` 编写，不得在执行本计划时顺手扩张。

## 文件职责锁定

### 新建领域文件

| 文件 | 唯一职责 |
|---|---|
| `src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs` | 枚举、档案定义、最低条件、行为事件与只读结果类型 |
| `src/Assets/Scripts/Cultivation/JindanProof/DaoProofLedger.cs` | 永久履历、事件幂等、防刷键、共享指标与标志性成就 |
| `src/Assets/Scripts/Cultivation/JindanProof/JindanProofKnowledge.cs` | 知识等级与不可泄密的玩家视图投影 |
| `src/Assets/Scripts/Cultivation/JindanProof/JindanProofAttempt.cs` | 单个候选人的驻地证位状态、进度与致命中断 |
| `src/Assets/Scripts/Cultivation/JindanProof/JindanProofCoordinator.cs` | 同一空位的世界 tick 关闭、并行完成、临界争位与其他尝试失效 |
| `src/Assets/Scripts/Cultivation/JindanProof/JindanPositionRegistry.cs` | 位格版本、空缺状态、唯一占据、核心与主承载原子绑定 |
| `src/Assets/Scripts/Cultivation/JindanProof/JindanProofSnapshot.cs` | 履历、尝试、位格、核心和已处理事件的可序列化快照 |
| `src/Assets/Scripts/Cultivation/JindanProof/NpcJindanProofPolicy.cs` | 持久 NPC 亲自证位的硬门槛与主观风险阈值 |

新目录保留 Unity 生成的 `src/Assets/Scripts/Cultivation/JindanProof.meta`；每个新 `.cs` 文件同时创建 Unity 生成的相邻 `.meta`。不得手写随机 GUID 后再覆盖已有 GUID。

### 新建测试文件

| 文件 | 唯一职责 |
|---|---|
| `src/Assets/Tests/EditMode/JindanProofTestFixtures.cs` | 火·源位最小档案、行为事件、空位和核心测试构造器 |
| `src/Assets/Tests/EditMode/DaoProofLedgerTests.cs` | 永久记录、幂等、共享推进与防刷 |
| `src/Assets/Tests/EditMode/JindanProofKnowledgeTests.cs` | 三档知识显示与未知信息零泄漏 |
| `src/Assets/Tests/EditMode/JindanProofAttemptTests.cs` | 0% 可启动、驻地推进、致命中断和永久记录保留 |
| `src/Assets/Tests/EditMode/JindanProofCoordinatorTests.cs` | 并行争位、同 tick 临界争位和确定性继续阶段 |
| `src/Assets/Tests/EditMode/JindanPositionRegistryTests.cs` | 唯一占据、版本 CAS、第一位建核与后续位不换核 |
| `src/Assets/Tests/EditMode/JindanProofSnapshotTests.cs` | 存读档不重复事件、扣费、进度或绑定 |
| `src/Assets/Tests/EditMode/NpcJindanProofPolicyTests.cs` | 紫府圆满、最低条件、主观知识、寿元与风险门槛 |
| `src/Assets/Tests/EditMode/JindanProofAcceptanceTests.cs` | 领域内核范围内的端到端验收场景 |

### 新建或修改文档

| 文件 | 责任 |
|---|---|
| Create `docs/基础设定/金丹位格证位与争位规则.txt` | 把已确认规格转成正式、短而专责的规则事实源 |
| Modify `docs/基础设定/元婴锚点与金丹位格设定.txt` | 引用新事实源，并删除“继承/敕封/夺取可直接取得稳定占据”的歧义 |
| Modify `docs/基础设定/修行境界.txt` | 明确三个阶段各自需要完成对应证位，不从筑基重修 |
| Modify `docs/基础设定/境界特性.txt` | 明确来源只提供起点优势，稳定绑定仍由完整证位建立 |

## 固定公共类型契约

所有任务统一使用以下名称，不得在后续步骤另起同义类型：

```csharp
namespace TianZhang.Cultivation.JindanProof
{
    public enum JindanSeatType { Source, Transformation, Domain }
    public enum ProofRequirementType { SharedMetric, SignatureAchievement }
    public enum ProofKnowledgeLevel { Unknown, RoadDirection, FullProfile }
    public enum JindanPositionVisibility { Public, FactionKnown, Rumored, Hidden }
    public enum ProofAttemptStatus
    {
        Active,
        AwaitingRegularTickClose,
        CriticalContest,
        AwaitingCriticalTickClose,
        ReadyToBind,
        Interrupted,
        Invalidated,
        Bound
    }

    public enum ProofRepeatPolicy { UniqueEvent, OncePerTarget, OncePerContext }
}
```

ID 统一为非空、区分大小写的 `string`；世界时间结算键统一为 `long worldTick`；位格版本统一为非负 `long version`；计数统一为非负 `int`。所有公开构造器对空 ID、负数和重复 requirement ID 失败关闭并抛出 `ArgumentException`。

---

### Task 0: 重确认工作区、目标路径与验证入口

**Files:**

- Read: `AGENTS.md`
- Read: `docs/superpowers/specs/2026-07-19-jindan-position-proof-and-contestation-design.md`
- Read: `docs/superpowers/plans/2026-07-19-jindan-position-proof-domain-kernel.md`
- Read: `开发管理/开发-技术经验.txt`
- Read: `开发管理/设计-当前状态.txt`

- [ ] **Step 1: 检查分支、HEAD、worktree 和完整状态**

Run:

```powershell
git branch --show-current
git rev-parse HEAD
git worktree list --porcelain
git status --short --branch
```

Expected: 当前目录仍为 `D:\天章游戏开发`；不新建 worktree；记录当前分支与 HEAD。脏改可以存在，但不得命中本计划 `Files` 表中的目标路径。

- [ ] **Step 2: 对目标路径做重叠检查**

Run:

```powershell
git status --short -- `
  'docs/基础设定/金丹位格证位与争位规则.txt' `
  'docs/基础设定/元婴锚点与金丹位格设定.txt' `
  'docs/基础设定/修行境界.txt' `
  'docs/基础设定/境界特性.txt' `
  'src/Assets/Scripts/Cultivation/JindanProof' `
  'src/Assets/Tests/EditMode/JindanProof*' `
  'src/Assets/Tests/EditMode/DaoProofLedgerTests.cs' `
  'src/Assets/Tests/EditMode/NpcJindanProofPolicyTests.cs'
```

Expected: 空输出。若非空，停止并逐项报告，不覆盖现有修改。

- [ ] **Step 3: 确认 Unity 和 PowerShell 7 验证入口**

Run:

```powershell
pwsh --version
Test-Path 'C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe'
Test-Path 'tools/run-unity-editmode-tests.ps1'
```

Expected: PowerShell 为 7.x；两个 `Test-Path` 都返回 `True`。

- [ ] **Step 4: 汇报基线，不写文件**

报告分支、HEAD、目标路径是否干净、工作树中需要保护的无关改动。Task 0 不暂存、不提交。

---

### Task 1: 建立证位领域契约与火·源位测试夹具

**Files:**

- Create: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs`
- Create: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs.meta`
- Create: `src/Assets/Scripts/Cultivation/JindanProof.meta`
- Create: `src/Assets/Tests/EditMode/JindanProofTestFixtures.cs`
- Create: `src/Assets/Tests/EditMode/JindanProofTestFixtures.cs.meta`
- Create: `src/Assets/Tests/EditMode/JindanProofDefinitionTests.cs`
- Create: `src/Assets/Tests/EditMode/JindanProofDefinitionTests.cs.meta`

- [ ] **Step 1: 写失败测试，锁定 ID、条件和火·源位夹具**

Create `JindanProofDefinitionTests.cs`:

```csharp
using System;
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class JindanProofDefinitionTests
    {
        [Test]
        public void FireSourceFixtureContainsSharedMetricsAndSignatureAchievement()
        {
            JindanProofProfileDefinition profile = JindanProofTestFixtures.FireSourceProfile();

            Assert.That(profile.ProfileId, Is.EqualTo("jindan_fire_source"));
            Assert.That(profile.RoadId, Is.EqualTo("fire"));
            Assert.That(profile.SeatType, Is.EqualTo(JindanSeatType.Source));
            Assert.That(profile.Requirements, Has.Count.EqualTo(3));
            Assert.That(profile.RegularProgressTarget, Is.EqualTo(100));
            Assert.That(profile.CriticalProgressTarget, Is.EqualTo(20));
        }

        [Test]
        public void DuplicateRequirementIdsFailClosed()
        {
            var requirement = new JindanProofRequirement(
                "fire_seed_count", ProofRequirementType.SharedMetric, 3);

            Assert.Throws<ArgumentException>(() => new JindanProofProfileDefinition(
                "jindan_fire_source",
                "fire",
                JindanSeatType.Source,
                new[] { requirement, requirement },
                100,
                20));
        }
    }
}
```

- [ ] **Step 2: 运行测试并确认红灯来自缺少领域类型**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1
```

Expected: FAIL；Unity 编译错误只指向 `JindanProofProfileDefinition`、`JindanProofRequirement` 或夹具尚不存在。若现有无关代码先失败，停止并报告基线失败。

- [ ] **Step 3: 写最小领域定义**

Create `JindanProofDefinitions.cs` with the fixed enums above and these complete public contracts:

```csharp
using System;
using System.Collections.Generic;

namespace TianZhang.Cultivation.JindanProof
{
    public enum JindanSeatType { Source, Transformation, Domain }
    public enum ProofRequirementType { SharedMetric, SignatureAchievement }
    public enum ProofKnowledgeLevel { Unknown, RoadDirection, FullProfile }
    public enum JindanPositionVisibility { Public, FactionKnown, Rumored, Hidden }
    public enum ProofAttemptStatus
    {
        Active,
        AwaitingRegularTickClose,
        CriticalContest,
        AwaitingCriticalTickClose,
        ReadyToBind,
        Interrupted,
        Invalidated,
        Bound
    }
    public enum ProofRepeatPolicy { UniqueEvent, OncePerTarget, OncePerContext }

    public sealed class JindanProofRequirement
    {
        public string RecordId { get; }
        public ProofRequirementType Type { get; }
        public int MinimumValue { get; }

        public JindanProofRequirement(
            string recordId,
            ProofRequirementType type,
            int minimumValue)
        {
            if (string.IsNullOrWhiteSpace(recordId))
                throw new ArgumentException("Requirement record ID is required.", nameof(recordId));
            if (minimumValue <= 0)
                throw new ArgumentOutOfRangeException(nameof(minimumValue));

            RecordId = recordId;
            Type = type;
            MinimumValue = minimumValue;
        }
    }

    public sealed class JindanProofProfileDefinition
    {
        private readonly List<JindanProofRequirement> requirements;

        public string ProfileId { get; }
        public string RoadId { get; }
        public JindanSeatType SeatType { get; }
        public IReadOnlyList<JindanProofRequirement> Requirements => requirements;
        public int RegularProgressTarget { get; }
        public int CriticalProgressTarget { get; }

        public JindanProofProfileDefinition(
            string profileId,
            string roadId,
            JindanSeatType seatType,
            IEnumerable<JindanProofRequirement> requirements,
            int regularProgressTarget,
            int criticalProgressTarget)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("Profile ID is required.", nameof(profileId));
            if (string.IsNullOrWhiteSpace(roadId))
                throw new ArgumentException("Road ID is required.", nameof(roadId));
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));
            if (regularProgressTarget <= 0)
                throw new ArgumentOutOfRangeException(nameof(regularProgressTarget));
            if (criticalProgressTarget <= 0)
                throw new ArgumentOutOfRangeException(nameof(criticalProgressTarget));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            this.requirements = new List<JindanProofRequirement>();
            foreach (JindanProofRequirement requirement in requirements)
            {
                if (requirement == null || !seen.Add(requirement.RecordId))
                    throw new ArgumentException("Requirements must be non-null and unique.", nameof(requirements));
                this.requirements.Add(requirement);
            }
            if (this.requirements.Count == 0)
                throw new ArgumentException("At least one requirement is required.", nameof(requirements));

            ProfileId = profileId;
            RoadId = roadId;
            SeatType = seatType;
            RegularProgressTarget = regularProgressTarget;
            CriticalProgressTarget = criticalProgressTarget;
        }
    }
}
```

Create `JindanProofTestFixtures.cs`:

```csharp
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    internal static class JindanProofTestFixtures
    {
        internal static JindanProofProfileDefinition FireSourceProfile()
        {
            return new JindanProofProfileDefinition(
                "jindan_fire_source",
                "fire",
                JindanSeatType.Source,
                new[]
                {
                    new JindanProofRequirement(
                        "fire_seed_count", ProofRequirementType.SharedMetric, 3),
                    new JindanProofRequirement(
                        "valid_ignition_count", ProofRequirementType.SharedMetric, 5),
                    new JindanProofRequirement(
                        "fire_source_precise_ignition", ProofRequirementType.SignatureAchievement, 1)
                },
                100,
                20);
        }
    }
}
```

- [ ] **Step 4: 运行 EditMode 并确认契约绿灯**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1
```

Expected: `JindanProofDefinitionTests` 两项 PASS；全套 EditMode 无失败、跳过或 inconclusive。

- [ ] **Step 5: 检查本 Task 路径并汇报**

Run:

```powershell
git diff --check -- `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs' `
  'src/Assets/Tests/EditMode/JindanProofTestFixtures.cs' `
  'src/Assets/Tests/EditMode/JindanProofDefinitionTests.cs'
```

Expected: 空输出。报告新增类型、测试数量和无关工作树状态未变化。

- [ ] **Step 6: 仅在用户明确授权后提交本 Task**

```powershell
git add -- `
  'src/Assets/Scripts/Cultivation/JindanProof.meta' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs.meta' `
  'src/Assets/Tests/EditMode/JindanProofTestFixtures.cs' `
  'src/Assets/Tests/EditMode/JindanProofTestFixtures.cs.meta' `
  'src/Assets/Tests/EditMode/JindanProofDefinitionTests.cs' `
  'src/Assets/Tests/EditMode/JindanProofDefinitionTests.cs.meta'
git diff --cached --check
git commit --only -m "feat: define jindan proof domain contracts" -- `
  'src/Assets/Scripts/Cultivation/JindanProof.meta' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs.meta' `
  'src/Assets/Tests/EditMode/JindanProofTestFixtures.cs' `
  'src/Assets/Tests/EditMode/JindanProofTestFixtures.cs.meta' `
  'src/Assets/Tests/EditMode/JindanProofDefinitionTests.cs' `
  'src/Assets/Tests/EditMode/JindanProofDefinitionTests.cs.meta'
```

---

### Task 2: 实现永久道证履历、幂等与防刷

**Files:**

- Modify: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs`
- Create: `src/Assets/Scripts/Cultivation/JindanProof/DaoProofLedger.cs`
- Create: `src/Assets/Scripts/Cultivation/JindanProof/DaoProofLedger.cs.meta`
- Create: `src/Assets/Tests/EditMode/DaoProofLedgerTests.cs`
- Create: `src/Assets/Tests/EditMode/DaoProofLedgerTests.cs.meta`

- [ ] **Step 1: 写失败测试，覆盖一次行为推进多个指标、重放与目标防刷**

Create `DaoProofLedgerTests.cs`:

```csharp
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class DaoProofLedgerTests
    {
        [Test]
        public void OneAcceptedBehaviorCanAdvanceMultipleSharedMetricsAndAchievement()
        {
            var ledger = new DaoProofLedger("actor_player");
            var rules = JindanProofTestFixtures.FireRules();
            var behavior = JindanProofTestFixtures.FireBehavior(
                "event_1", "target_bandit_camp", "region_jiangzuo", 3,
                new[]
                {
                    new DaoProofContribution("fire_seed_count", 1),
                    new DaoProofContribution("valid_ignition_count", 2)
                },
                new[] { "fire_source_precise_ignition" });

            Assert.That(ledger.TryRecord(behavior, rules), Is.True);
            Assert.That(ledger.GetMetricValue("fire_seed_count"), Is.EqualTo(1));
            Assert.That(ledger.GetMetricValue("valid_ignition_count"), Is.EqualTo(2));
            Assert.That(ledger.HasAchievement("fire_source_precise_ignition"), Is.True);
        }

        [Test]
        public void ReplayedEventAndRepeatedTargetDoNotFarmProgress()
        {
            var ledger = new DaoProofLedger("actor_player");
            var rules = JindanProofTestFixtures.FireRules();
            var first = JindanProofTestFixtures.FireBehavior(
                "event_1", "target_dummy", "region_jiangzuo", 3,
                new[] { new DaoProofContribution("valid_ignition_count", 1) });
            var repeatedTarget = JindanProofTestFixtures.FireBehavior(
                "event_2", "target_dummy", "region_guanzhong", 3,
                new[] { new DaoProofContribution("valid_ignition_count", 1) });

            Assert.That(ledger.TryRecord(first, rules), Is.True);
            Assert.That(ledger.TryRecord(first, rules), Is.False);
            Assert.That(ledger.TryRecord(repeatedTarget, rules), Is.False);
            Assert.That(ledger.GetMetricValue("valid_ignition_count"), Is.EqualTo(1));
        }

        [Test]
        public void LowChallengeBehaviorDoesNotCount()
        {
            var ledger = new DaoProofLedger("actor_player");
            var behavior = JindanProofTestFixtures.FireBehavior(
                "event_low", "target_straw", "region_jiangzuo", 0,
                new[] { new DaoProofContribution("fire_seed_count", 1) });

            Assert.That(ledger.TryRecord(behavior, JindanProofTestFixtures.FireRules()), Is.False);
            Assert.That(ledger.GetMetricValue("fire_seed_count"), Is.Zero);
        }
    }
}
```

- [ ] **Step 2: 运行 EditMode 并确认缺少履历类型的红灯**

Run the existing Unity EditMode command. Expected: FAIL only because `DaoProofLedger`、`DaoProofBehaviorEvent`、`DaoProofContribution`、`DaoProofMetricRule` 或夹具方法尚不存在。

- [ ] **Step 3: 添加行为、规则与贡献契约**

Append these types to `JindanProofDefinitions.cs` inside the same namespace:

```csharp
public sealed class DaoProofContribution
{
    public string MetricId { get; }
    public int Amount { get; }

    public DaoProofContribution(string metricId, int amount)
    {
        if (string.IsNullOrWhiteSpace(metricId))
            throw new ArgumentException("Metric ID is required.", nameof(metricId));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        MetricId = metricId;
        Amount = amount;
    }
}

public sealed class DaoProofMetricRule
{
    public string MetricId { get; }
    public ProofRepeatPolicy RepeatPolicy { get; }
    public int MinimumChallengeTier { get; }

    public DaoProofMetricRule(
        string metricId,
        ProofRepeatPolicy repeatPolicy,
        int minimumChallengeTier)
    {
        if (string.IsNullOrWhiteSpace(metricId))
            throw new ArgumentException("Metric ID is required.", nameof(metricId));
        if (minimumChallengeTier < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumChallengeTier));
        MetricId = metricId;
        RepeatPolicy = repeatPolicy;
        MinimumChallengeTier = minimumChallengeTier;
    }
}

public sealed class DaoProofBehaviorEvent
{
    public string EventId { get; }
    public string ActorId { get; }
    public string TargetKey { get; }
    public string ContextKey { get; }
    public int ChallengeTier { get; }
    public IReadOnlyList<DaoProofContribution> Contributions { get; }
    public IReadOnlyList<string> AchievementIds { get; }

    public DaoProofBehaviorEvent(
        string eventId,
        string actorId,
        string targetKey,
        string contextKey,
        int challengeTier,
        IReadOnlyList<DaoProofContribution> contributions,
        IReadOnlyList<string> achievementIds)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("Event ID is required.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("Actor ID is required.", nameof(actorId));
        if (challengeTier < 0)
            throw new ArgumentOutOfRangeException(nameof(challengeTier));
        EventId = eventId;
        ActorId = actorId;
        TargetKey = targetKey ?? string.Empty;
        ContextKey = contextKey ?? string.Empty;
        ChallengeTier = challengeTier;
        Contributions = contributions ?? Array.Empty<DaoProofContribution>();
        AchievementIds = achievementIds ?? Array.Empty<string>();
    }
}
```

- [ ] **Step 4: 实现永久履历**

Create `DaoProofLedger.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace TianZhang.Cultivation.JindanProof
{
    public sealed class DaoProofLedger
    {
        private readonly Dictionary<string, int> metricValues =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> achievements =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> processedEventIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> repeatKeys =
            new HashSet<string>(StringComparer.Ordinal);

        public string ActorId { get; }

        public DaoProofLedger(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                throw new ArgumentException("Actor ID is required.", nameof(actorId));
            ActorId = actorId;
        }

        public bool TryRecord(
            DaoProofBehaviorEvent behavior,
            IReadOnlyDictionary<string, DaoProofMetricRule> rules)
        {
            if (behavior == null)
                throw new ArgumentNullException(nameof(behavior));
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));
            if (!string.Equals(ActorId, behavior.ActorId, StringComparison.Ordinal))
                return false;
            if (!processedEventIds.Add(behavior.EventId))
                return false;

            bool accepted = false;
            foreach (DaoProofContribution contribution in behavior.Contributions)
            {
                if (!rules.TryGetValue(contribution.MetricId, out DaoProofMetricRule rule))
                    continue;
                if (behavior.ChallengeTier < rule.MinimumChallengeTier)
                    continue;

                string repeatKey = BuildRepeatKey(rule, behavior);
                if (!repeatKeys.Add(repeatKey))
                    continue;

                metricValues.TryGetValue(contribution.MetricId, out int current);
                metricValues[contribution.MetricId] = checked(current + contribution.Amount);
                accepted = true;
            }

            if (accepted)
            {
                foreach (string achievementId in behavior.AchievementIds)
                {
                    if (!string.IsNullOrWhiteSpace(achievementId))
                        achievements.Add(achievementId);
                }
            }
            return accepted;
        }

        public int GetMetricValue(string metricId)
        {
            return metricValues.TryGetValue(metricId, out int value) ? value : 0;
        }

        public bool HasAchievement(string achievementId)
        {
            return achievements.Contains(achievementId);
        }

        public bool HasProcessedEvent(string eventId)
        {
            return processedEventIds.Contains(eventId);
        }

        private static string BuildRepeatKey(
            DaoProofMetricRule rule,
            DaoProofBehaviorEvent behavior)
        {
            switch (rule.RepeatPolicy)
            {
                case ProofRepeatPolicy.OncePerTarget:
                    return rule.MetricId + "|target|" + behavior.TargetKey;
                case ProofRepeatPolicy.OncePerContext:
                    return rule.MetricId + "|context|" + behavior.ContextKey;
                default:
                    return rule.MetricId + "|event|" + behavior.EventId;
            }
        }
    }
}
```

- [ ] **Step 5: 扩充火道路测试夹具**

Add complete helpers to `JindanProofTestFixtures.cs`:

```csharp
internal static System.Collections.Generic.IReadOnlyDictionary<string, DaoProofMetricRule> FireRules()
{
    return new System.Collections.Generic.Dictionary<string, DaoProofMetricRule>(System.StringComparer.Ordinal)
    {
        ["fire_seed_count"] = new DaoProofMetricRule(
            "fire_seed_count", ProofRepeatPolicy.OncePerContext, 1),
        ["valid_ignition_count"] = new DaoProofMetricRule(
            "valid_ignition_count", ProofRepeatPolicy.OncePerTarget, 1)
    };
}

internal static DaoProofBehaviorEvent FireBehavior(
    string eventId,
    string targetKey,
    string contextKey,
    int challengeTier,
    System.Collections.Generic.IReadOnlyList<DaoProofContribution> contributions,
    System.Collections.Generic.IReadOnlyList<string> achievementIds = null)
{
    return new DaoProofBehaviorEvent(
        eventId,
        "actor_player",
        targetKey,
        contextKey,
        challengeTier,
        contributions,
        achievementIds ?? System.Array.Empty<string>());
}
```

- [ ] **Step 6: 运行 EditMode 并确认履历绿灯**

Run the existing Unity EditMode command. Expected: `DaoProofLedgerTests` 三项 PASS；旧测试仍全部通过。

- [ ] **Step 7: 最小检查、汇报与授权后提交**

Run `git diff --check` against this Task's files. If the user authorizes a commit, stage only the three declared source/test paths and their `.meta`, run `git diff --cached --check`, then:

```powershell
git commit --only -m "feat: record permanent dao proof history" -- `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/DaoProofLedger.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/DaoProofLedger.cs.meta' `
  'src/Assets/Tests/EditMode/JindanProofTestFixtures.cs' `
  'src/Assets/Tests/EditMode/DaoProofLedgerTests.cs' `
  'src/Assets/Tests/EditMode/DaoProofLedgerTests.cs.meta'
```

---

### Task 3: 实现硬条件实时读取与知识遮蔽投影

**Files:**

- Modify: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs`
- Create: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofKnowledge.cs`
- Create: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofKnowledge.cs.meta`
- Create: `src/Assets/Tests/EditMode/JindanProofKnowledgeTests.cs`
- Create: `src/Assets/Tests/EditMode/JindanProofKnowledgeTests.cs.meta`

- [ ] **Step 1: 写失败测试，证明未知状态零泄漏且不保存资格证**

Create `JindanProofKnowledgeTests.cs`:

```csharp
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class JindanProofKnowledgeTests
    {
        [Test]
        public void UnknownKnowledgeRevealsNoConditionsProgressAdaptationOrForecast()
        {
            var ledger = JindanProofTestFixtures.EligibleFireLedger();
            JindanProofView view = JindanProofKnowledge.Project(
                JindanProofTestFixtures.FireSourceProfile(),
                ledger,
                ProofKnowledgeLevel.Unknown,
                88,
                73);

            Assert.That(view.RoadId, Is.Null);
            Assert.That(view.Requirements, Is.Empty);
            Assert.That(view.AdaptationPercent, Is.Null);
            Assert.That(view.EstimatedSuccessPercent, Is.Null);
            Assert.That(view.RevealsExactConditions, Is.False);
        }

        [Test]
        public void RoadDirectionKnowledgeShowsDirectionButNoNumbers()
        {
            JindanProofView view = JindanProofKnowledge.Project(
                JindanProofTestFixtures.FireSourceProfile(),
                new DaoProofLedger("actor_player"),
                ProofKnowledgeLevel.RoadDirection,
                88,
                73);

            Assert.That(view.RoadId, Is.EqualTo("fire"));
            Assert.That(view.Requirements, Is.Empty);
            Assert.That(view.AdaptationPercent, Is.Null);
            Assert.That(view.EstimatedSuccessPercent, Is.Null);
        }

        [Test]
        public void FullKnowledgeReadsCurrentLedgerAndExposesForecast()
        {
            var ledger = JindanProofTestFixtures.EligibleFireLedger();
            JindanProofView view = JindanProofKnowledge.Project(
                JindanProofTestFixtures.FireSourceProfile(),
                ledger,
                ProofKnowledgeLevel.FullProfile,
                88,
                73);

            Assert.That(view.Requirements, Has.Count.EqualTo(3));
            Assert.That(view.Requirements, Has.All.Matches<ProofRequirementView>(item => item.IsMet));
            Assert.That(view.AdaptationPercent, Is.EqualTo(88));
            Assert.That(view.EstimatedSuccessPercent, Is.EqualTo(73));
            Assert.That(view.RevealsExactConditions, Is.True);
        }

        [Test]
        public void EligibilityIsDerivedAndChangesWhenLedgerChanges()
        {
            var ledger = new DaoProofLedger("actor_player");
            JindanProofProfileDefinition profile = JindanProofTestFixtures.FireSourceProfile();

            Assert.That(JindanProofEligibility.Evaluate(profile, ledger).IsSatisfied, Is.False);
            JindanProofTestFixtures.FillEligibleFireLedger(ledger);
            Assert.That(JindanProofEligibility.Evaluate(profile, ledger).IsSatisfied, Is.True);
        }
    }
}
```

- [ ] **Step 2: 运行 EditMode 并确认知识/资格类型缺失的红灯**

Run the existing Unity EditMode command. Expected: FAIL only because `JindanProofView`、`ProofRequirementView`、`JindanProofKnowledge`、`JindanProofEligibility` 和夹具方法尚不存在。

- [ ] **Step 3: 添加只读资格与视图类型**

Append to `JindanProofDefinitions.cs`:

```csharp
public sealed class ProofEligibilityResult
{
    public bool IsSatisfied { get; }
    public IReadOnlyList<string> UnmetRequirementIds { get; }

    public ProofEligibilityResult(bool isSatisfied, IReadOnlyList<string> unmetRequirementIds)
    {
        IsSatisfied = isSatisfied;
        UnmetRequirementIds = unmetRequirementIds ?? Array.Empty<string>();
    }
}

public sealed class ProofRequirementView
{
    public string RecordId { get; }
    public int CurrentValue { get; }
    public int RequiredValue { get; }
    public bool IsMet { get; }

    public ProofRequirementView(string recordId, int currentValue, int requiredValue)
    {
        RecordId = recordId;
        CurrentValue = currentValue;
        RequiredValue = requiredValue;
        IsMet = currentValue >= requiredValue;
    }
}

public sealed class JindanProofView
{
    public string RoadId { get; }
    public IReadOnlyList<ProofRequirementView> Requirements { get; }
    public int? AdaptationPercent { get; }
    public int? EstimatedSuccessPercent { get; }
    public bool RevealsExactConditions { get; }

    public JindanProofView(
        string roadId,
        IReadOnlyList<ProofRequirementView> requirements,
        int? adaptationPercent,
        int? estimatedSuccessPercent,
        bool revealsExactConditions)
    {
        RoadId = roadId;
        Requirements = requirements ?? Array.Empty<ProofRequirementView>();
        AdaptationPercent = adaptationPercent;
        EstimatedSuccessPercent = estimatedSuccessPercent;
        RevealsExactConditions = revealsExactConditions;
    }
}
```

- [ ] **Step 4: 实现实时资格读取和知识投影**

Create `JindanProofKnowledge.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace TianZhang.Cultivation.JindanProof
{
    public static class JindanProofEligibility
    {
        public static ProofEligibilityResult Evaluate(
            JindanProofProfileDefinition profile,
            DaoProofLedger ledger)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));

            var unmet = new List<string>();
            foreach (JindanProofRequirement requirement in profile.Requirements)
            {
                int current = requirement.Type == ProofRequirementType.SharedMetric
                    ? ledger.GetMetricValue(requirement.RecordId)
                    : ledger.HasAchievement(requirement.RecordId) ? 1 : 0;
                if (current < requirement.MinimumValue)
                    unmet.Add(requirement.RecordId);
            }
            return new ProofEligibilityResult(unmet.Count == 0, unmet);
        }
    }

    public static class JindanProofKnowledge
    {
        public static JindanProofView Project(
            JindanProofProfileDefinition profile,
            DaoProofLedger ledger,
            ProofKnowledgeLevel knowledgeLevel,
            int adaptationPercent,
            int estimatedSuccessPercent)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));

            if (knowledgeLevel == ProofKnowledgeLevel.Unknown)
                return new JindanProofView(null, Array.Empty<ProofRequirementView>(), null, null, false);
            if (knowledgeLevel == ProofKnowledgeLevel.RoadDirection)
                return new JindanProofView(profile.RoadId, Array.Empty<ProofRequirementView>(), null, null, false);

            var requirements = new List<ProofRequirementView>();
            foreach (JindanProofRequirement requirement in profile.Requirements)
            {
                int current = requirement.Type == ProofRequirementType.SharedMetric
                    ? ledger.GetMetricValue(requirement.RecordId)
                    : ledger.HasAchievement(requirement.RecordId) ? 1 : 0;
                requirements.Add(new ProofRequirementView(
                    requirement.RecordId, current, requirement.MinimumValue));
            }

            return new JindanProofView(
                profile.RoadId,
                requirements,
                ClampPercent(adaptationPercent),
                ClampPercent(estimatedSuccessPercent),
                true);
        }

        private static int ClampPercent(int value)
        {
            return Math.Max(0, Math.Min(100, value));
        }
    }
}
```

The projector receives a forecast calculated from **known** risk only. It never reads hidden competitors, hidden requirements or a backend roll. The later UI plan may replace integers with labels, but must preserve the null/no-display contract.

- [ ] **Step 5: 扩充可满足条件的测试夹具**

Add to `JindanProofTestFixtures.cs`:

```csharp
internal static DaoProofLedger EligibleFireLedger()
{
    var ledger = new DaoProofLedger("actor_player");
    FillEligibleFireLedger(ledger);
    return ledger;
}

internal static void FillEligibleFireLedger(DaoProofLedger ledger)
{
    var rules = FireRules();
    ledger.TryRecord(FireBehavior(
        "eligible_1", "target_1", "context_1", 3,
        new[]
        {
            new DaoProofContribution("fire_seed_count", 3),
            new DaoProofContribution("valid_ignition_count", 3)
        }), rules);
    ledger.TryRecord(FireBehavior(
        "eligible_2", "target_2", "context_2", 3,
        new[] { new DaoProofContribution("valid_ignition_count", 2) },
        new[] { "fire_source_precise_ignition" }), rules);
}
```

- [ ] **Step 6: 运行 EditMode 并确认知识遮蔽绿灯**

Run the existing Unity EditMode command. Expected: `JindanProofKnowledgeTests` 四项 PASS；未知与方向知识的数值字段均为 null。

- [ ] **Step 7: 最小检查、汇报与授权后提交**

Run `git diff --check` against this Task's files. With explicit commit authorization, stage only declared files plus `.meta`, run `git diff --cached --check`, then commit:

```powershell
git commit --only -m "feat: hide unknown jindan proof information" -- `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofKnowledge.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofKnowledge.cs.meta' `
  'src/Assets/Tests/EditMode/JindanProofTestFixtures.cs' `
  'src/Assets/Tests/EditMode/JindanProofKnowledgeTests.cs' `
  'src/Assets/Tests/EditMode/JindanProofKnowledgeTests.cs.meta'
```

---

### Task 4: 实现单候选人驻地证位状态机与致命中断

**Files:**

- Create: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofAttempt.cs`
- Create: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofAttempt.cs.meta`
- Create: `src/Assets/Tests/EditMode/JindanProofAttemptTests.cs`
- Create: `src/Assets/Tests/EditMode/JindanProofAttemptTests.cs.meta`

- [ ] **Step 1: 写失败测试，覆盖 0% 可启动、硬条件阻止完成与致命中断**

Create `JindanProofAttemptTests.cs`:

```csharp
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class JindanProofAttemptTests
    {
        [Test]
        public void UnqualifiedPlayerCanStartButCannotReachTickClose()
        {
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_player");

            attempt.AdvanceRegular(100, hardRequirementsMet: false);

            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.Active));
            Assert.That(attempt.RegularProgress, Is.EqualTo(100));
        }

        [Test]
        public void QualifiedCandidateReachesRegularTickCloseWithoutRandomRoll()
        {
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_player");

            attempt.AdvanceRegular(100, hardRequirementsMet: true);

            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.AwaitingRegularTickClose));
        }

        [Test]
        public void FatalInterruptionClearsAttemptProgressOnly()
        {
            var ledger = JindanProofTestFixtures.EligibleFireLedger();
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_player");
            attempt.AdvanceRegular(60, hardRequirementsMet: true);

            attempt.FatalInterrupt("left_proof_boundary");

            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.Interrupted));
            Assert.That(attempt.RegularProgress, Is.Zero);
            Assert.That(attempt.CriticalProgress, Is.Zero);
            Assert.That(ledger.GetMetricValue("fire_seed_count"), Is.EqualTo(3));
            Assert.That(ledger.HasAchievement("fire_source_precise_ignition"), Is.True);
        }
    }
}
```

- [ ] **Step 2: 运行 EditMode 并确认缺少尝试类型的红灯**

Run the existing Unity EditMode command. Expected: FAIL only because `JindanProofAttempt` 和 `NewAttempt` 尚不存在。

- [ ] **Step 3: 实现单尝试状态机**

Create `JindanProofAttempt.cs`:

```csharp
using System;

namespace TianZhang.Cultivation.JindanProof
{
    public sealed class JindanProofAttempt
    {
        public string AttemptId { get; }
        public string PositionId { get; }
        public string ActorId { get; }
        public string ProfileId { get; }
        public string SiteId { get; }
        public string CarrierAbilityInstanceId { get; }
        public long ExpectedPositionVersion { get; }
        public int RegularProgressTarget { get; }
        public int CriticalProgressTarget { get; }
        public int RegularProgress { get; private set; }
        public int CriticalProgress { get; private set; }
        public int CriticalRound { get; private set; }
        public string InterruptionReason { get; private set; }
        public ProofAttemptStatus Status { get; private set; }

        public JindanProofAttempt(
            string attemptId,
            string positionId,
            string actorId,
            string profileId,
            string siteId,
            string carrierAbilityInstanceId,
            long expectedPositionVersion,
            int regularProgressTarget,
            int criticalProgressTarget)
        {
            RequireId(attemptId, nameof(attemptId));
            RequireId(positionId, nameof(positionId));
            RequireId(actorId, nameof(actorId));
            RequireId(profileId, nameof(profileId));
            RequireId(siteId, nameof(siteId));
            RequireId(carrierAbilityInstanceId, nameof(carrierAbilityInstanceId));
            if (expectedPositionVersion < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedPositionVersion));
            if (regularProgressTarget <= 0)
                throw new ArgumentOutOfRangeException(nameof(regularProgressTarget));
            if (criticalProgressTarget <= 0)
                throw new ArgumentOutOfRangeException(nameof(criticalProgressTarget));

            AttemptId = attemptId;
            PositionId = positionId;
            ActorId = actorId;
            ProfileId = profileId;
            SiteId = siteId;
            CarrierAbilityInstanceId = carrierAbilityInstanceId;
            ExpectedPositionVersion = expectedPositionVersion;
            RegularProgressTarget = regularProgressTarget;
            CriticalProgressTarget = criticalProgressTarget;
            Status = ProofAttemptStatus.Active;
        }

        public void AdvanceRegular(int amount, bool hardRequirementsMet)
        {
            if (Status != ProofAttemptStatus.Active)
                throw new InvalidOperationException("Only an active attempt can advance regular proof.");
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            RegularProgress = Math.Min(RegularProgressTarget, checked(RegularProgress + amount));
            if (RegularProgress == RegularProgressTarget && hardRequirementsMet)
                Status = ProofAttemptStatus.AwaitingRegularTickClose;
        }

        public void EnterCriticalContest()
        {
            if (Status != ProofAttemptStatus.AwaitingRegularTickClose)
                throw new InvalidOperationException("Critical contest requires regular completion.");
            Status = ProofAttemptStatus.CriticalContest;
            CriticalProgress = 0;
            CriticalRound = 1;
        }

        public void AdvanceCritical(int amount)
        {
            if (Status != ProofAttemptStatus.CriticalContest)
                throw new InvalidOperationException("Attempt is not in critical contest.");
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            CriticalProgress = Math.Min(CriticalProgressTarget, checked(CriticalProgress + amount));
            if (CriticalProgress == CriticalProgressTarget)
                Status = ProofAttemptStatus.AwaitingCriticalTickClose;
        }

        public void RestartCriticalRound()
        {
            if (Status != ProofAttemptStatus.AwaitingCriticalTickClose)
                throw new InvalidOperationException("Only simultaneous critical completion restarts a round.");
            Status = ProofAttemptStatus.CriticalContest;
            CriticalProgress = 0;
            CriticalRound = checked(CriticalRound + 1);
        }

        public void MarkReadyToBind()
        {
            if (Status != ProofAttemptStatus.AwaitingRegularTickClose &&
                Status != ProofAttemptStatus.AwaitingCriticalTickClose)
                throw new InvalidOperationException("Attempt has not completed a closable stage.");
            Status = ProofAttemptStatus.ReadyToBind;
        }

        public void FatalInterrupt(string reason)
        {
            RequireId(reason, nameof(reason));
            if (Status == ProofAttemptStatus.Bound ||
                Status == ProofAttemptStatus.Invalidated ||
                Status == ProofAttemptStatus.Interrupted)
                throw new InvalidOperationException("A terminal attempt cannot be interrupted again.");
            RegularProgress = 0;
            CriticalProgress = 0;
            InterruptionReason = reason;
            Status = ProofAttemptStatus.Interrupted;
        }

        public void Invalidate()
        {
            if (Status != ProofAttemptStatus.Bound)
                Status = ProofAttemptStatus.Invalidated;
        }

        public void MarkBound()
        {
            if (Status != ProofAttemptStatus.ReadyToBind)
                throw new InvalidOperationException("Only a ready attempt can bind.");
            Status = ProofAttemptStatus.Bound;
        }

        private static void RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty ID is required.", parameterName);
        }
    }
}
```

- [ ] **Step 4: 添加尝试夹具**

Add to `JindanProofTestFixtures.cs`:

```csharp
internal static JindanProofAttempt NewAttempt(string attemptId, string actorId = "actor_player")
{
    JindanProofProfileDefinition profile = FireSourceProfile();
    return new JindanProofAttempt(
        attemptId,
        "position_fire_source_01",
        actorId,
        profile.ProfileId,
        "site_fire_altar_01",
        "ability_fire_carrier_01_" + actorId,
        0,
        profile.RegularProgressTarget,
        profile.CriticalProgressTarget);
}
```

- [ ] **Step 5: 运行 EditMode 并确认状态机绿灯**

Run the existing Unity EditMode command. Expected: `JindanProofAttemptTests` 三项 PASS；未满足硬条件的进度可达到上限但状态不能离开 `Active`。

- [ ] **Step 6: 最小检查、汇报与授权后提交**

Run `git diff --check` against this Task's files. With explicit authorization, stage only declared files plus `.meta`, run cached check, then commit:

```powershell
git commit --only -m "feat: add stationary jindan proof attempt state" -- `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofAttempt.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofAttempt.cs.meta' `
  'src/Assets/Tests/EditMode/JindanProofTestFixtures.cs' `
  'src/Assets/Tests/EditMode/JindanProofAttemptTests.cs' `
  'src/Assets/Tests/EditMode/JindanProofAttemptTests.cs.meta'
```

---

### Task 5: 实现并行完成、临界争位与确定性 tick 关闭

**Files:**

- Modify: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs`
- Create: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofCoordinator.cs`
- Create: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofCoordinator.cs.meta`
- Create: `src/Assets/Tests/EditMode/JindanProofCoordinatorTests.cs`
- Create: `src/Assets/Tests/EditMode/JindanProofCoordinatorTests.cs.meta`

- [ ] **Step 1: 写失败测试，锁定“先关 tick、再判唯一/临界”的协议**

Create `JindanProofCoordinatorTests.cs`:

```csharp
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class JindanProofCoordinatorTests
    {
        [Test]
        public void OneRegularCompletionBecomesReadyOnlyWhenWorldTickCloses()
        {
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_a", "actor_a");
            coordinator.Register(attempt);
            attempt.AdvanceRegular(100, true);

            coordinator.SubmitRegularCompletion(attempt.AttemptId, 1000);
            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.AwaitingRegularTickClose));

            ProofTickResolution result = coordinator.CloseRegularTick(attempt.PositionId, 1000);
            Assert.That(result.Kind, Is.EqualTo(ProofTickResolutionKind.UniqueReady));
            Assert.That(result.UniqueAttemptId, Is.EqualTo(attempt.AttemptId));
            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.ReadyToBind));
        }

        [Test]
        public void SameTickRegularCompletionsEnterCriticalContestWithoutIdTiebreak()
        {
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt z = JindanProofTestFixtures.NewAttempt("z_attempt", "actor_z");
            JindanProofAttempt a = JindanProofTestFixtures.NewAttempt("a_attempt", "actor_a");
            coordinator.Register(z);
            coordinator.Register(a);
            z.AdvanceRegular(100, true);
            a.AdvanceRegular(100, true);
            coordinator.SubmitRegularCompletion(z.AttemptId, 2000);
            coordinator.SubmitRegularCompletion(a.AttemptId, 2000);

            ProofTickResolution result = coordinator.CloseRegularTick(z.PositionId, 2000);

            Assert.That(result.Kind, Is.EqualTo(ProofTickResolutionKind.CriticalContestContinues));
            Assert.That(result.UniqueAttemptId, Is.Null);
            Assert.That(z.Status, Is.EqualTo(ProofAttemptStatus.CriticalContest));
            Assert.That(a.Status, Is.EqualTo(ProofAttemptStatus.CriticalContest));
        }

        [Test]
        public void SameTickCriticalCompletionsStartAnotherRound()
        {
            var coordinator = JindanProofTestFixtures.CriticalContest(out JindanProofAttempt a, out JindanProofAttempt b);
            a.AdvanceCritical(20);
            b.AdvanceCritical(20);
            coordinator.SubmitCriticalCompletion(a.AttemptId, 3000);
            coordinator.SubmitCriticalCompletion(b.AttemptId, 3000);

            ProofTickResolution result = coordinator.CloseCriticalTick(a.PositionId, 3000);

            Assert.That(result.Kind, Is.EqualTo(ProofTickResolutionKind.CriticalContestContinues));
            Assert.That(a.CriticalRound, Is.EqualTo(2));
            Assert.That(b.CriticalRound, Is.EqualTo(2));
            Assert.That(a.Status, Is.EqualTo(ProofAttemptStatus.CriticalContest));
            Assert.That(b.Status, Is.EqualTo(ProofAttemptStatus.CriticalContest));
        }
    }
}
```

- [ ] **Step 2: 运行 EditMode 并确认协调器类型缺失的红灯**

Run the existing Unity EditMode command. Expected: FAIL only because协调器、tick 结果类型和临界夹具尚不存在。

- [ ] **Step 3: 添加 tick 关闭结果类型**

Append to `JindanProofDefinitions.cs`:

```csharp
public enum ProofTickResolutionKind
{
    NoCompletion,
    UniqueReady,
    CriticalContestContinues
}

public sealed class ProofTickResolution
{
    public ProofTickResolutionKind Kind { get; }
    public string UniqueAttemptId { get; }
    public IReadOnlyList<string> ParticipantAttemptIds { get; }

    public ProofTickResolution(
        ProofTickResolutionKind kind,
        string uniqueAttemptId,
        IReadOnlyList<string> participantAttemptIds)
    {
        Kind = kind;
        UniqueAttemptId = uniqueAttemptId;
        ParticipantAttemptIds = participantAttemptIds ?? Array.Empty<string>();
    }
}
```

- [ ] **Step 4: 实现世界 tick 关闭协调器**

Create `JindanProofCoordinator.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace TianZhang.Cultivation.JindanProof
{
    public sealed class JindanProofCoordinator
    {
        private readonly Dictionary<string, JindanProofAttempt> attempts =
            new Dictionary<string, JindanProofAttempt>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> regularCompletions =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> criticalCompletions =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public void Register(JindanProofAttempt attempt)
        {
            if (attempt == null) throw new ArgumentNullException(nameof(attempt));
            if (!attempts.TryAdd(attempt.AttemptId, attempt))
                throw new ArgumentException("Attempt ID already exists.", nameof(attempt));
        }

        public JindanProofAttempt GetAttempt(string attemptId)
        {
            return attempts.TryGetValue(attemptId, out JindanProofAttempt attempt) ? attempt : null;
        }

        public void SubmitRegularCompletion(string attemptId, long worldTick)
        {
            JindanProofAttempt attempt = RequireAttempt(attemptId);
            if (attempt.Status != ProofAttemptStatus.AwaitingRegularTickClose)
                throw new InvalidOperationException("Attempt has not completed the regular stage.");
            AddCompletion(regularCompletions, attempt.PositionId, worldTick, attemptId);
        }

        public void SubmitCriticalCompletion(string attemptId, long worldTick)
        {
            JindanProofAttempt attempt = RequireAttempt(attemptId);
            if (attempt.Status != ProofAttemptStatus.AwaitingCriticalTickClose)
                throw new InvalidOperationException("Attempt has not completed the critical stage.");
            AddCompletion(criticalCompletions, attempt.PositionId, worldTick, attemptId);
        }

        public ProofTickResolution CloseRegularTick(string positionId, long worldTick)
        {
            List<JindanProofAttempt> completed = TakeCompletions(
                regularCompletions, positionId, worldTick);
            if (completed.Count == 1)
            {
                completed[0].MarkReadyToBind();
                return Unique(completed[0]);
            }
            if (completed.Count > 1)
            {
                foreach (JindanProofAttempt attempt in completed)
                    attempt.EnterCriticalContest();
                return Continued(completed);
            }
            return Empty();
        }

        public ProofTickResolution CloseCriticalTick(string positionId, long worldTick)
        {
            List<JindanProofAttempt> completed = TakeCompletions(
                criticalCompletions, positionId, worldTick);
            if (completed.Count == 1)
            {
                completed[0].MarkReadyToBind();
                return Unique(completed[0]);
            }
            if (completed.Count > 1)
            {
                foreach (JindanProofAttempt attempt in completed)
                    attempt.RestartCriticalRound();
                return Continued(completed);
            }
            return Empty();
        }

        public void InvalidateOthers(string positionId, string winningAttemptId)
        {
            foreach (JindanProofAttempt attempt in attempts.Values)
            {
                if (string.Equals(attempt.PositionId, positionId, StringComparison.Ordinal) &&
                    !string.Equals(attempt.AttemptId, winningAttemptId, StringComparison.Ordinal))
                    attempt.Invalidate();
            }
        }

        private JindanProofAttempt RequireAttempt(string attemptId)
        {
            if (!attempts.TryGetValue(attemptId, out JindanProofAttempt attempt))
                throw new KeyNotFoundException("Unknown attempt: " + attemptId);
            return attempt;
        }

        private static void AddCompletion(
            IDictionary<string, List<string>> store,
            string positionId,
            long worldTick,
            string attemptId)
        {
            string key = positionId + "|" + worldTick;
            if (!store.TryGetValue(key, out List<string> values))
            {
                values = new List<string>();
                store.Add(key, values);
            }
            if (!values.Contains(attemptId))
                values.Add(attemptId);
        }

        private List<JindanProofAttempt> TakeCompletions(
            IDictionary<string, List<string>> store,
            string positionId,
            long worldTick)
        {
            string key = positionId + "|" + worldTick;
            if (!store.TryGetValue(key, out List<string> values))
                return new List<JindanProofAttempt>();
            store.Remove(key);
            var result = new List<JindanProofAttempt>();
            foreach (string attemptId in values)
                result.Add(RequireAttempt(attemptId));
            return result;
        }

        private static ProofTickResolution Unique(JindanProofAttempt attempt)
        {
            return new ProofTickResolution(
                ProofTickResolutionKind.UniqueReady,
                attempt.AttemptId,
                new[] { attempt.AttemptId });
        }

        private static ProofTickResolution Continued(IReadOnlyList<JindanProofAttempt> attempts)
        {
            var ids = new List<string>();
            foreach (JindanProofAttempt attempt in attempts)
                ids.Add(attempt.AttemptId);
            ids.Sort(StringComparer.Ordinal);
            return new ProofTickResolution(
                ProofTickResolutionKind.CriticalContestContinues, null, ids);
        }

        private static ProofTickResolution Empty()
        {
            return new ProofTickResolution(
                ProofTickResolutionKind.NoCompletion, null, Array.Empty<string>());
        }
    }
}
```

Sorting is only for stable result presentation. Winner selection never reads sorted order, actor ID, attempt ID, registration order, random number or load order.

- [ ] **Step 5: 添加临界争位夹具**

Add to `JindanProofTestFixtures.cs`:

```csharp
internal static JindanProofCoordinator CriticalContest(
    out JindanProofAttempt a,
    out JindanProofAttempt b)
{
    var coordinator = new JindanProofCoordinator();
    a = NewAttempt("attempt_a", "actor_a");
    b = NewAttempt("attempt_b", "actor_b");
    coordinator.Register(a);
    coordinator.Register(b);
    a.AdvanceRegular(100, true);
    b.AdvanceRegular(100, true);
    coordinator.SubmitRegularCompletion(a.AttemptId, 2000);
    coordinator.SubmitRegularCompletion(b.AttemptId, 2000);
    coordinator.CloseRegularTick(a.PositionId, 2000);
    return coordinator;
}
```

- [ ] **Step 6: 运行 EditMode 并确认争位协调绿灯**

Run the existing Unity EditMode command. Expected: `JindanProofCoordinatorTests` 三项 PASS；同时完成者不自动产生 winner。

- [ ] **Step 7: 最小检查、汇报与授权后提交**

Run `git diff --check` against this Task's files. With explicit authorization, stage only declared files plus `.meta`, run cached check, then commit:

```powershell
git commit --only -m "feat: coordinate deterministic jindan contests" -- `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofDefinitions.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofCoordinator.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofCoordinator.cs.meta' `
  'src/Assets/Tests/EditMode/JindanProofTestFixtures.cs' `
  'src/Assets/Tests/EditMode/JindanProofCoordinatorTests.cs' `
  'src/Assets/Tests/EditMode/JindanProofCoordinatorTests.cs.meta'
```

---

### Task 6: 实现版本化位格与原子核心/主承载绑定

**Files:**

- Create: `src/Assets/Scripts/Cultivation/JindanProof/JindanPositionRegistry.cs`
- Create: `src/Assets/Scripts/Cultivation/JindanProof/JindanPositionRegistry.cs.meta`
- Create: `src/Assets/Tests/EditMode/JindanPositionRegistryTests.cs`
- Create: `src/Assets/Tests/EditMode/JindanPositionRegistryTests.cs.meta`
- Modify: `src/Assets/Tests/EditMode/JindanProofTestFixtures.cs`

- [ ] **Step 1: 写失败测试，覆盖首次建核、后续不换核与版本竞争**

Create `JindanPositionRegistryTests.cs`:

```csharp
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class JindanPositionRegistryTests
    {
        [Test]
        public void FirstSeatAtomicallyCreatesCoreAndCarrierBinding()
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            var core = new JindanCoreState("actor_player");
            var attempt = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_first", "actor_player", "position_fire_source_01",
                "jindan_fire_source", "ability_source_actor_player", 0);

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(attempt.AttemptId, "core_actor_player", true, true),
                JindanProofTestFixtures.FireSourceProfile(),
                JindanProofTestFixtures.EligibleFireLedger(),
                core,
                coordinator);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(core.CoreBindingId, Is.EqualTo("core_actor_player"));
            Assert.That(core.SeatBindings, Has.Count.EqualTo(1));
            Assert.That(registry.Get("position_fire_source_01").HolderActorId, Is.EqualTo("actor_player"));
            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.Bound));
        }

        [Test]
        public void AdditionalSeatKeepsOriginalCoreBindingId()
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSourceAndTransformation();
            var core = new JindanCoreState("actor_player");
            JindanProofTestFixtures.BindFirstSeat(registry, core, coordinator);
            var attempt = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_second", "actor_player", "position_fire_transformation_01",
                "jindan_fire_transformation", "ability_transformation_actor_player", 0);

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(attempt.AttemptId, null, true, true),
                JindanProofTestFixtures.FireTransformationProfile(),
                JindanProofTestFixtures.EligibleFireTransformationLedger(),
                core,
                coordinator);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(core.CoreBindingId, Is.EqualTo("core_actor_player"));
            Assert.That(core.SeatBindings, Has.Count.EqualTo(2));
        }

        [Test]
        public void StaleVersionFailsWithoutPartialBinding()
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            registry.Get("position_fire_source_01").AdvanceVersionForWorldChange();
            var core = new JindanCoreState("actor_player");
            var attempt = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_stale", "actor_player", "position_fire_source_01",
                "jindan_fire_source", "ability_source_actor_player", 0);

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(attempt.AttemptId, "core_actor_player", true, true),
                JindanProofTestFixtures.FireSourceProfile(),
                JindanProofTestFixtures.EligibleFireLedger(),
                core,
                coordinator);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(JindanBindFailureReason.StalePositionVersion));
            Assert.That(core.CoreBindingId, Is.Null);
            Assert.That(core.SeatBindings, Is.Empty);
            Assert.That(registry.Get("position_fire_source_01").HolderActorId, Is.Null);
            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.ReadyToBind));
        }

        [Test]
        public void SuccessfulBindInvalidatesEveryOtherAttemptForThePosition()
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            var winner = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_winner", "actor_player", "position_fire_source_01",
                "jindan_fire_source", "ability_winner", 0);
            var loser = JindanProofTestFixtures.NewAttempt("attempt_loser", "actor_loser");
            coordinator.Register(loser);

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(winner.AttemptId, "core_actor_player", true, true),
                JindanProofTestFixtures.FireSourceProfile(),
                JindanProofTestFixtures.EligibleFireLedger(),
                new JindanCoreState("actor_player"),
                coordinator);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(loser.Status, Is.EqualTo(ProofAttemptStatus.Invalidated));
        }
    }
}
```

- [ ] **Step 2: 运行 EditMode 并确认绑定类型缺失的红灯**

Run the existing Unity EditMode command. Expected: FAIL only because位格、核心、绑定请求/结果和夹具方法尚不存在。

- [ ] **Step 3: 实现位格、核心和原子绑定事务**

Create `JindanPositionRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace TianZhang.Cultivation.JindanProof
{
    public enum JindanBindFailureReason
    {
        None,
        AttemptNotReady,
        PositionUnavailable,
        StalePositionVersion,
        PreconditionsNotMet,
        CoreInvariantViolation
    }

    public sealed class JindanBindResult
    {
        public bool Succeeded { get; }
        public JindanBindFailureReason FailureReason { get; }

        public JindanBindResult(bool succeeded, JindanBindFailureReason failureReason)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
        }
    }

    public sealed class JindanBindRequest
    {
        public string AttemptId { get; }
        public string NewCoreBindingId { get; }
        public bool SiteStillValid { get; }
        public bool CarrierStillCompatible { get; }

        public JindanBindRequest(
            string attemptId,
            string newCoreBindingId,
            bool siteStillValid,
            bool carrierStillCompatible)
        {
            if (string.IsNullOrWhiteSpace(attemptId))
                throw new ArgumentException("Attempt ID is required.", nameof(attemptId));
            AttemptId = attemptId;
            NewCoreBindingId = string.IsNullOrWhiteSpace(newCoreBindingId) ? null : newCoreBindingId;
            SiteStillValid = siteStillValid;
            CarrierStillCompatible = carrierStillCompatible;
        }
    }

    public sealed class JindanPositionRecord
    {
        public string PositionId { get; }
        public string ProfileId { get; }
        public JindanSeatType SeatType { get; }
        public JindanPositionVisibility Visibility { get; private set; }
        public string HolderActorId { get; private set; }
        public long Version { get; private set; }

        public JindanPositionRecord(
            string positionId,
            string profileId,
            JindanSeatType seatType,
            JindanPositionVisibility visibility,
            long version = 0)
        {
            RequireId(positionId, nameof(positionId));
            RequireId(profileId, nameof(profileId));
            if (version < 0) throw new ArgumentOutOfRangeException(nameof(version));
            PositionId = positionId;
            ProfileId = profileId;
            SeatType = seatType;
            Visibility = visibility;
            Version = version;
        }

        public void AdvanceVersionForWorldChange()
        {
            Version = checked(Version + 1);
        }

        internal void Bind(string actorId)
        {
            RequireId(actorId, nameof(actorId));
            HolderActorId = actorId;
            Version = checked(Version + 1);
        }

        private static void RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty ID is required.", parameterName);
        }
    }

    public sealed class SeatCarrierBinding
    {
        public string PositionId { get; }
        public JindanSeatType SeatType { get; }
        public string CarrierAbilityInstanceId { get; }

        public SeatCarrierBinding(
            string positionId,
            JindanSeatType seatType,
            string carrierAbilityInstanceId)
        {
            PositionId = positionId;
            SeatType = seatType;
            CarrierAbilityInstanceId = carrierAbilityInstanceId;
        }
    }

    public sealed class JindanCoreState
    {
        private readonly List<SeatCarrierBinding> seatBindings = new List<SeatCarrierBinding>();

        public string ActorId { get; }
        public string CoreBindingId { get; private set; }
        public IReadOnlyList<SeatCarrierBinding> SeatBindings => seatBindings;

        public JindanCoreState(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                throw new ArgumentException("Actor ID is required.", nameof(actorId));
            ActorId = actorId;
        }

        internal bool CanAdd(
            JindanPositionRecord position,
            JindanProofAttempt attempt,
            string newCoreBindingId)
        {
            if (!string.Equals(ActorId, attempt.ActorId, StringComparison.Ordinal)) return false;
            if (CoreBindingId == null && string.IsNullOrWhiteSpace(newCoreBindingId)) return false;
            if (CoreBindingId != null && !string.IsNullOrWhiteSpace(newCoreBindingId)) return false;
            foreach (SeatCarrierBinding binding in seatBindings)
            {
                if (binding.SeatType == position.SeatType) return false;
                if (string.Equals(binding.CarrierAbilityInstanceId,
                    attempt.CarrierAbilityInstanceId, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        internal void Add(
            JindanPositionRecord position,
            JindanProofAttempt attempt,
            string newCoreBindingId)
        {
            if (CoreBindingId == null)
                CoreBindingId = newCoreBindingId;
            seatBindings.Add(new SeatCarrierBinding(
                position.PositionId,
                position.SeatType,
                attempt.CarrierAbilityInstanceId));
        }
    }

    public sealed class JindanPositionRegistry
    {
        private readonly Dictionary<string, JindanPositionRecord> positions =
            new Dictionary<string, JindanPositionRecord>(StringComparer.Ordinal);

        public void Add(JindanPositionRecord position)
        {
            if (position == null) throw new ArgumentNullException(nameof(position));
            if (!positions.TryAdd(position.PositionId, position))
                throw new ArgumentException("Position ID already exists.", nameof(position));
        }

        public JindanPositionRecord Get(string positionId)
        {
            return positions.TryGetValue(positionId, out JindanPositionRecord position)
                ? position
                : null;
        }

        public JindanBindResult TryBind(
            JindanBindRequest request,
            JindanProofProfileDefinition profile,
            DaoProofLedger ledger,
            JindanCoreState core,
            JindanProofCoordinator coordinator)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (core == null) throw new ArgumentNullException(nameof(core));
            if (coordinator == null) throw new ArgumentNullException(nameof(coordinator));

            JindanProofAttempt attempt = coordinator.GetAttempt(request.AttemptId);
            if (attempt == null || attempt.Status != ProofAttemptStatus.ReadyToBind)
                return Failed(JindanBindFailureReason.AttemptNotReady);
            JindanPositionRecord position = Get(attempt.PositionId);
            if (position == null || position.HolderActorId != null)
                return Failed(JindanBindFailureReason.PositionUnavailable);
            if (position.Version != attempt.ExpectedPositionVersion)
                return Failed(JindanBindFailureReason.StalePositionVersion);
            if (!string.Equals(position.ProfileId, profile.ProfileId, StringComparison.Ordinal) ||
                position.SeatType != profile.SeatType ||
                !string.Equals(ledger.ActorId, attempt.ActorId, StringComparison.Ordinal) ||
                !request.SiteStillValid ||
                !request.CarrierStillCompatible ||
                !JindanProofEligibility.Evaluate(profile, ledger).IsSatisfied)
                return Failed(JindanBindFailureReason.PreconditionsNotMet);
            if (!core.CanAdd(position, attempt, request.NewCoreBindingId))
                return Failed(JindanBindFailureReason.CoreInvariantViolation);

            core.Add(position, attempt, request.NewCoreBindingId);
            position.Bind(attempt.ActorId);
            attempt.MarkBound();
            coordinator.InvalidateOthers(position.PositionId, attempt.AttemptId);
            return new JindanBindResult(true, JindanBindFailureReason.None);
        }

        private static JindanBindResult Failed(JindanBindFailureReason reason)
        {
            return new JindanBindResult(false, reason);
        }
    }
}
```

All validation finishes before the first mutation. `PreconditionsNotMet` deliberately merges hard-history, site and carrier failures so an unknown profile cannot be reverse engineered through error codes.

- [ ] **Step 4: 添加第二位、空位、ready attempt 与首次绑定夹具**

Add these helpers to `JindanProofTestFixtures.cs`:

```csharp
internal static JindanProofProfileDefinition FireTransformationProfile()
{
    return new JindanProofProfileDefinition(
        "jindan_fire_transformation",
        "fire",
        JindanSeatType.Transformation,
        new[]
        {
            new JindanProofRequirement("fire_seed_count", ProofRequirementType.SharedMetric, 3),
            new JindanProofRequirement("valid_ignition_count", ProofRequirementType.SharedMetric, 5),
            new JindanProofRequirement(
                "fire_transformation_filtered_spread",
                ProofRequirementType.SignatureAchievement,
                1)
        },
        100,
        20);
}

internal static DaoProofLedger EligibleFireTransformationLedger()
{
    DaoProofLedger ledger = EligibleFireLedger();
    ledger.TryRecord(FireBehavior(
        "eligible_transformation", "target_3", "context_3", 3,
        new[] { new DaoProofContribution("fire_seed_count", 1) },
        new[] { "fire_transformation_filtered_spread" }), FireRules());
    return ledger;
}

internal static JindanPositionRegistry RegistryWithVacantFireSource()
{
    var registry = new JindanPositionRegistry();
    registry.Add(new JindanPositionRecord(
        "position_fire_source_01",
        "jindan_fire_source",
        JindanSeatType.Source,
        JindanPositionVisibility.Hidden));
    return registry;
}

internal static JindanPositionRegistry RegistryWithVacantFireSourceAndTransformation()
{
    JindanPositionRegistry registry = RegistryWithVacantFireSource();
    registry.Add(new JindanPositionRecord(
        "position_fire_transformation_01",
        "jindan_fire_transformation",
        JindanSeatType.Transformation,
        JindanPositionVisibility.Public));
    return registry;
}

internal static JindanProofAttempt ReadyAttempt(
    JindanProofCoordinator coordinator,
    string attemptId,
    string actorId,
    string positionId,
    string profileId,
    string carrierId,
    long expectedVersion)
{
    var attempt = new JindanProofAttempt(
        attemptId, positionId, actorId, profileId,
        "site_" + positionId, carrierId, expectedVersion, 100, 20);
    coordinator.Register(attempt);
    attempt.AdvanceRegular(100, true);
    coordinator.SubmitRegularCompletion(attemptId, 100);
    coordinator.CloseRegularTick(positionId, 100);
    return attempt;
}

internal static void BindFirstSeat(
    JindanPositionRegistry registry,
    JindanCoreState core,
    JindanProofCoordinator coordinator)
{
    JindanProofAttempt first = ReadyAttempt(
        coordinator, "attempt_first", "actor_player", "position_fire_source_01",
        "jindan_fire_source", "ability_source_actor_player", 0);
    JindanBindResult result = registry.TryBind(
        new JindanBindRequest(first.AttemptId, "core_actor_player", true, true),
        FireSourceProfile(), EligibleFireLedger(), core, coordinator);
    if (!result.Succeeded)
        throw new System.InvalidOperationException("Fixture failed to bind first seat.");
}
```

- [ ] **Step 5: 运行 EditMode 并确认原子绑定绿灯**

Run the existing Unity EditMode command. Expected: `JindanPositionRegistryTests` 四项 PASS；失败请求不改变核心、位格或 attempt。

- [ ] **Step 6: 最小检查、汇报与授权后提交**

Run `git diff --check` against this Task's files. With explicit authorization, stage only declared files plus `.meta`, run cached check, then commit:

```powershell
git commit --only -m "feat: bind jindan positions atomically" -- `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanPositionRegistry.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanPositionRegistry.cs.meta' `
  'src/Assets/Tests/EditMode/JindanProofTestFixtures.cs' `
  'src/Assets/Tests/EditMode/JindanPositionRegistryTests.cs' `
  'src/Assets/Tests/EditMode/JindanPositionRegistryTests.cs.meta'
```

---

### Task 7: 实现 NPC 亲自证位硬门槛与主观风险策略

**Files:**

- Create: `src/Assets/Scripts/Cultivation/JindanProof/NpcJindanProofPolicy.cs`
- Create: `src/Assets/Scripts/Cultivation/JindanProof/NpcJindanProofPolicy.cs.meta`
- Create: `src/Assets/Tests/EditMode/NpcJindanProofPolicyTests.cs`
- Create: `src/Assets/Tests/EditMode/NpcJindanProofPolicyTests.cs.meta`

- [ ] **Step 1: 写失败测试，证明寿元不能绕过紫府或最低条件**

Create `NpcJindanProofPolicyTests.cs`:

```csharp
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class NpcJindanProofPolicyTests
    {
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void LifespanPressureNeverBypassesRealmOrProofRequirements(
            bool purpleMansionComplete,
            bool hardRequirementsMet)
        {
            NpcProofDecisionInput input = JindanProofTestFixtures.ReadyNpcInput();
            input.IsPurpleMansionComplete = purpleMansionComplete;
            input.HardRequirementsMet = hardRequirementsMet;
            input.DaysOfLifeRemaining = 1;
            input.SubjectiveSuccessPercent = 100;

            NpcProofDecision decision = JindanProofTestFixtures.NpcPolicy().Evaluate(input);

            Assert.That(decision.ShouldStart, Is.False);
            Assert.That(decision.Reason, Is.EqualTo(NpcProofDecisionReason.HardGateFailed));
        }

        [Test]
        public void LowSubjectiveChanceStopsHealthyNpcButNotDyingNpc()
        {
            NpcProofDecisionInput healthy = JindanProofTestFixtures.ReadyNpcInput();
            healthy.SubjectiveSuccessPercent = 45;
            healthy.DaysOfLifeRemaining = 2000;
            NpcProofDecisionInput dying = JindanProofTestFixtures.ReadyNpcInput();
            dying.SubjectiveSuccessPercent = 45;
            dying.DaysOfLifeRemaining = 30;

            Assert.That(JindanProofTestFixtures.NpcPolicy().Evaluate(healthy).ShouldStart, Is.False);
            Assert.That(JindanProofTestFixtures.NpcPolicy().Evaluate(dying).ShouldStart, Is.True);
        }

        [Test]
        public void PolicyHasNoBackendTruthInput()
        {
            var input = JindanProofTestFixtures.ReadyNpcInput();
            input.SubjectiveSuccessPercent = 10;

            NpcProofDecision decision = JindanProofTestFixtures.NpcPolicy().Evaluate(input);

            Assert.That(decision.ShouldStart, Is.False);
            Assert.That(decision.Reason, Is.EqualTo(NpcProofDecisionReason.SubjectiveRiskTooHigh));
        }
    }
}
```

- [ ] **Step 2: 运行 EditMode 并确认 NPC 策略类型缺失的红灯**

Run the existing Unity EditMode command. Expected: FAIL only because NPC 决策类型和夹具尚不存在。

- [ ] **Step 3: 实现纯函数 NPC 策略**

Create `NpcJindanProofPolicy.cs`:

```csharp
using System;

namespace TianZhang.Cultivation.JindanProof
{
    public enum NpcRiskDisposition { Cautious, Normal, Bold }
    public enum NpcProofDecisionReason
    {
        Ready,
        HardGateFailed,
        SubjectiveRiskTooHigh
    }

    public sealed class NpcProofDecisionInput
    {
        public bool IsPersistentNpc;
        public bool IsPurpleMansionComplete;
        public bool HardRequirementsMet;
        public bool KnowsVacancy;
        public bool KnowsUsableSite;
        public bool HasCompatibleCarrier;
        public bool HasFacilities;
        public bool HasResources;
        public bool HasGuard;
        public bool HasHigherPrioritySurvivalDuty;
        public NpcRiskDisposition RiskDisposition;
        public int SubjectiveSuccessPercent;
        public int DaysOfLifeRemaining;
    }

    public sealed class NpcProofDecision
    {
        public bool ShouldStart { get; }
        public NpcProofDecisionReason Reason { get; }
        public int RequiredSubjectivePercent { get; }

        public NpcProofDecision(
            bool shouldStart,
            NpcProofDecisionReason reason,
            int requiredSubjectivePercent)
        {
            ShouldStart = shouldStart;
            Reason = reason;
            RequiredSubjectivePercent = requiredSubjectivePercent;
        }
    }

    public sealed class NpcJindanProofPolicy
    {
        private readonly int cautiousThreshold;
        private readonly int normalThreshold;
        private readonly int boldThreshold;
        private readonly int lifespanDangerDays;
        private readonly int lifespanThresholdReduction;

        public NpcJindanProofPolicy(
            int cautiousThreshold,
            int normalThreshold,
            int boldThreshold,
            int lifespanDangerDays,
            int lifespanThresholdReduction)
        {
            this.cautiousThreshold = ValidatePercent(cautiousThreshold, nameof(cautiousThreshold));
            this.normalThreshold = ValidatePercent(normalThreshold, nameof(normalThreshold));
            this.boldThreshold = ValidatePercent(boldThreshold, nameof(boldThreshold));
            if (lifespanDangerDays < 0)
                throw new ArgumentOutOfRangeException(nameof(lifespanDangerDays));
            this.lifespanDangerDays = lifespanDangerDays;
            this.lifespanThresholdReduction =
                ValidatePercent(lifespanThresholdReduction, nameof(lifespanThresholdReduction));
        }

        public NpcProofDecision Evaluate(NpcProofDecisionInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            int threshold = BaseThreshold(input.RiskDisposition);
            if (input.DaysOfLifeRemaining >= 0 && input.DaysOfLifeRemaining <= lifespanDangerDays)
                threshold = Math.Max(1, threshold - lifespanThresholdReduction);

            bool hardGate =
                input.IsPersistentNpc &&
                input.IsPurpleMansionComplete &&
                input.HardRequirementsMet &&
                input.KnowsVacancy &&
                input.KnowsUsableSite &&
                input.HasCompatibleCarrier &&
                input.HasFacilities &&
                input.HasResources &&
                input.HasGuard &&
                !input.HasHigherPrioritySurvivalDuty;

            if (!hardGate)
                return new NpcProofDecision(false, NpcProofDecisionReason.HardGateFailed, threshold);
            if (input.SubjectiveSuccessPercent < threshold)
                return new NpcProofDecision(false, NpcProofDecisionReason.SubjectiveRiskTooHigh, threshold);
            return new NpcProofDecision(true, NpcProofDecisionReason.Ready, threshold);
        }

        private int BaseThreshold(NpcRiskDisposition disposition)
        {
            switch (disposition)
            {
                case NpcRiskDisposition.Cautious: return cautiousThreshold;
                case NpcRiskDisposition.Bold: return boldThreshold;
                default: return normalThreshold;
            }
        }

        private static int ValidatePercent(int value, string parameterName)
        {
            if (value < 0 || value > 100)
                throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }
}
```

The input intentionally contains only the NPC's subjective forecast. There is no field for hidden true success, hidden rival progress or unknown requirements.

- [ ] **Step 4: 添加满足硬门槛的 NPC 夹具**

Add to `JindanProofTestFixtures.cs`:

```csharp
internal static NpcProofDecisionInput ReadyNpcInput()
{
    return new NpcProofDecisionInput
    {
        IsPersistentNpc = true,
        IsPurpleMansionComplete = true,
        HardRequirementsMet = true,
        KnowsVacancy = true,
        KnowsUsableSite = true,
        HasCompatibleCarrier = true,
        HasFacilities = true,
        HasResources = true,
        HasGuard = true,
        HasHigherPrioritySurvivalDuty = false,
        RiskDisposition = NpcRiskDisposition.Normal,
        SubjectiveSuccessPercent = 80,
        DaysOfLifeRemaining = 2000
    };
}

internal static NpcJindanProofPolicy NpcPolicy()
{
    return new NpcJindanProofPolicy(
        cautiousThreshold: 70,
        normalThreshold: 55,
        boldThreshold: 40,
        lifespanDangerDays: 180,
        lifespanThresholdReduction: 20);
}
```

The numeric values above are test-fixture inputs only. The production values must come from the later data-pipeline plan and be validated with the required balance workflow before a runtime asset is created.

- [ ] **Step 5: 运行 EditMode 并确认 NPC 策略绿灯**

Run the existing Unity EditMode command. Expected: `NpcJindanProofPolicyTests` 四个 case 全部 PASS；寿元只降低风险阈值。

- [ ] **Step 6: 最小检查、汇报与授权后提交**

Run `git diff --check` against this Task's files. With explicit authorization, stage only declared files plus `.meta`, run cached check, then commit:

```powershell
git commit --only -m "feat: gate npc jindan proof attempts" -- `
  'src/Assets/Scripts/Cultivation/JindanProof/NpcJindanProofPolicy.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/NpcJindanProofPolicy.cs.meta' `
  'src/Assets/Tests/EditMode/JindanProofTestFixtures.cs' `
  'src/Assets/Tests/EditMode/NpcJindanProofPolicyTests.cs' `
  'src/Assets/Tests/EditMode/NpcJindanProofPolicyTests.cs.meta'
```

---

### Task 8: 实现领域快照与读档幂等

**Files:**

- Modify: `src/Assets/Scripts/Cultivation/JindanProof/DaoProofLedger.cs`
- Modify: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofAttempt.cs`
- Modify: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofCoordinator.cs`
- Modify: `src/Assets/Scripts/Cultivation/JindanProof/JindanPositionRegistry.cs`
- Create: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofSnapshot.cs`
- Create: `src/Assets/Scripts/Cultivation/JindanProof/JindanProofSnapshot.cs.meta`
- Create: `src/Assets/Tests/EditMode/JindanProofSnapshotTests.cs`
- Create: `src/Assets/Tests/EditMode/JindanProofSnapshotTests.cs.meta`

- [ ] **Step 1: 写失败测试，覆盖事件重放、进行中进度和待关闭 tick**

Create `JindanProofSnapshotTests.cs`:

```csharp
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class JindanProofSnapshotTests
    {
        [Test]
        public void RoundTripPreservesLedgerProgressAndProcessedEventIds()
        {
            DaoProofLedger ledger = JindanProofTestFixtures.EligibleFireLedger();
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_save");
            coordinator.Register(attempt);
            attempt.AdvanceRegular(60, true);
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            var core = new JindanCoreState("actor_player");

            string json = JsonUtility.ToJson(JindanProofSnapshot.Capture(
                new[] { ledger }, coordinator, registry, new[] { core }));
            JindanProofRestoredState restored = JindanProofSnapshot.Restore(
                JsonUtility.FromJson<JindanProofSaveData>(json));

            Assert.That(restored.GetLedger("actor_player").GetMetricValue("fire_seed_count"), Is.EqualTo(3));
            Assert.That(restored.Coordinator.GetAttempt("attempt_save").RegularProgress, Is.EqualTo(60));
            Assert.That(restored.GetLedger("actor_player").TryRecord(
                JindanProofTestFixtures.FireBehavior(
                    "eligible_1", "target_new", "context_new", 3,
                    new[] { new DaoProofContribution("fire_seed_count", 99) }),
                JindanProofTestFixtures.FireRules()), Is.False);
        }

        [Test]
        public void RoundTripPreservesPendingTickAndClosesItExactlyOnce()
        {
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_pending");
            coordinator.Register(attempt);
            attempt.AdvanceRegular(100, true);
            coordinator.SubmitRegularCompletion(attempt.AttemptId, 9000);
            JindanProofSaveData save = JindanProofSnapshot.Capture(
                new[] { JindanProofTestFixtures.EligibleFireLedger() },
                coordinator,
                JindanProofTestFixtures.RegistryWithVacantFireSource(),
                new[] { new JindanCoreState("actor_player") });

            JindanProofRestoredState restored = JindanProofSnapshot.Restore(save);
            ProofTickResolution first = restored.Coordinator.CloseRegularTick(
                attempt.PositionId, 9000);
            ProofTickResolution second = restored.Coordinator.CloseRegularTick(
                attempt.PositionId, 9000);

            Assert.That(first.Kind, Is.EqualTo(ProofTickResolutionKind.UniqueReady));
            Assert.That(second.Kind, Is.EqualTo(ProofTickResolutionKind.NoCompletion));
        }

        [Test]
        public void BoundSeatRoundTripKeepsCoreAndPositionVersion()
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            var core = new JindanCoreState("actor_player");
            JindanProofTestFixtures.BindFirstSeat(registry, core, coordinator);

            JindanProofRestoredState restored = JindanProofSnapshot.Restore(
                JindanProofSnapshot.Capture(
                    new[] { JindanProofTestFixtures.EligibleFireLedger() },
                    coordinator,
                    registry,
                    new[] { core }));

            Assert.That(restored.GetCore("actor_player").CoreBindingId, Is.EqualTo("core_actor_player"));
            Assert.That(restored.GetCore("actor_player").SeatBindings, Has.Count.EqualTo(1));
            Assert.That(restored.Registry.Get("position_fire_source_01").HolderActorId,
                Is.EqualTo("actor_player"));
            Assert.That(restored.Registry.Get("position_fire_source_01").Version, Is.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: 运行 EditMode 并确认快照类型缺失的红灯**

Run the existing Unity EditMode command. Expected: FAIL only because save DTO、capture/restore 和状态导出方法尚不存在。

- [ ] **Step 3: 创建显式、JsonUtility 可序列化的 DTO**

Create the first half of `JindanProofSnapshot.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace TianZhang.Cultivation.JindanProof
{
    [Serializable]
    public sealed class MetricValueSaveData
    {
        public string id;
        public int value;
    }

    [Serializable]
    public sealed class DaoProofLedgerSaveData
    {
        public string actorId;
        public List<MetricValueSaveData> metrics = new List<MetricValueSaveData>();
        public List<string> achievements = new List<string>();
        public List<string> processedEventIds = new List<string>();
        public List<string> repeatKeys = new List<string>();
    }

    [Serializable]
    public sealed class JindanProofAttemptSaveData
    {
        public string attemptId;
        public string positionId;
        public string actorId;
        public string profileId;
        public string siteId;
        public string carrierAbilityInstanceId;
        public long expectedPositionVersion;
        public int regularProgressTarget;
        public int criticalProgressTarget;
        public int regularProgress;
        public int criticalProgress;
        public int criticalRound;
        public string interruptionReason;
        public ProofAttemptStatus status;
    }

    [Serializable]
    public sealed class ProofCompletionSaveData
    {
        public string positionId;
        public long worldTick;
        public List<string> attemptIds = new List<string>();
    }

    [Serializable]
    public sealed class JindanPositionSaveData
    {
        public string positionId;
        public string profileId;
        public JindanSeatType seatType;
        public JindanPositionVisibility visibility;
        public string holderActorId;
        public long version;
    }

    [Serializable]
    public sealed class SeatCarrierBindingSaveData
    {
        public string positionId;
        public JindanSeatType seatType;
        public string carrierAbilityInstanceId;
    }

    [Serializable]
    public sealed class JindanCoreSaveData
    {
        public string actorId;
        public string coreBindingId;
        public List<SeatCarrierBindingSaveData> seatBindings =
            new List<SeatCarrierBindingSaveData>();
    }

    [Serializable]
    public sealed class JindanProofSaveData
    {
        public int schemaVersion = 1;
        public List<DaoProofLedgerSaveData> ledgers =
            new List<DaoProofLedgerSaveData>();
        public List<JindanProofAttemptSaveData> attempts =
            new List<JindanProofAttemptSaveData>();
        public List<ProofCompletionSaveData> regularCompletions =
            new List<ProofCompletionSaveData>();
        public List<ProofCompletionSaveData> criticalCompletions =
            new List<ProofCompletionSaveData>();
        public List<JindanPositionSaveData> positions =
            new List<JindanPositionSaveData>();
        public List<JindanCoreSaveData> cores =
            new List<JindanCoreSaveData>();
    }

    public sealed class JindanProofRestoredState
    {
        private readonly Dictionary<string, DaoProofLedger> ledgers =
            new Dictionary<string, DaoProofLedger>(StringComparer.Ordinal);
        private readonly Dictionary<string, JindanCoreState> cores =
            new Dictionary<string, JindanCoreState>(StringComparer.Ordinal);

        public JindanProofCoordinator Coordinator { get; }
        public JindanPositionRegistry Registry { get; }

        public JindanProofRestoredState(
            IReadOnlyList<DaoProofLedger> ledgers,
            JindanProofCoordinator coordinator,
            JindanPositionRegistry registry,
            IReadOnlyList<JindanCoreState> cores)
        {
            Coordinator = coordinator;
            Registry = registry;
            foreach (DaoProofLedger ledger in ledgers)
            {
                if (!this.ledgers.TryAdd(ledger.ActorId, ledger))
                    throw new ArgumentException("Duplicate ledger actor ID.", nameof(ledgers));
            }
            foreach (JindanCoreState core in cores)
            {
                if (!this.cores.TryAdd(core.ActorId, core))
                    throw new ArgumentException("Duplicate core actor ID.", nameof(cores));
            }
        }

        public DaoProofLedger GetLedger(string actorId)
        {
            return ledgers.TryGetValue(actorId, out DaoProofLedger ledger) ? ledger : null;
        }

        public JindanCoreState GetCore(string actorId)
        {
            return cores.TryGetValue(actorId, out JindanCoreState core) ? core : null;
        }
    }
}
```

All collection fields are initialized because `JsonUtility` does not support dictionaries and can return null for absent legacy fields. Restore must reject a null root, unsupported `schemaVersion`, empty IDs, duplicate IDs, negative versions and negative progress.

- [ ] **Step 4: 给每个领域对象添加精确导出/恢复方法**

Add these method signatures and bodies to their owning classes. They are `internal` so the snapshot adapter can access them without exposing mutation to gameplay code.

In `DaoProofLedger`:

```csharp
internal DaoProofLedgerSaveData CaptureState()
{
    var data = new DaoProofLedgerSaveData { actorId = ActorId };
    foreach (KeyValuePair<string, int> pair in metricValues)
        data.metrics.Add(new MetricValueSaveData { id = pair.Key, value = pair.Value });
    data.metrics.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
    data.achievements.AddRange(achievements);
    data.achievements.Sort(StringComparer.Ordinal);
    data.processedEventIds.AddRange(processedEventIds);
    data.processedEventIds.Sort(StringComparer.Ordinal);
    data.repeatKeys.AddRange(repeatKeys);
    data.repeatKeys.Sort(StringComparer.Ordinal);
    return data;
}

internal static DaoProofLedger RestoreState(DaoProofLedgerSaveData data)
{
    if (data == null) throw new ArgumentNullException(nameof(data));
    var ledger = new DaoProofLedger(data.actorId);
    foreach (MetricValueSaveData metric in data.metrics ?? new List<MetricValueSaveData>())
    {
        if (string.IsNullOrWhiteSpace(metric.id) || metric.value < 0 ||
            ledger.metricValues.ContainsKey(metric.id))
            throw new ArgumentException("Invalid metric snapshot.", nameof(data));
        ledger.metricValues.Add(metric.id, metric.value);
    }
    foreach (string id in data.achievements ?? new List<string>())
        if (string.IsNullOrWhiteSpace(id) || !ledger.achievements.Add(id))
            throw new ArgumentException("Invalid or duplicate achievement.", nameof(data));
    foreach (string id in data.processedEventIds ?? new List<string>())
        if (string.IsNullOrWhiteSpace(id) || !ledger.processedEventIds.Add(id))
            throw new ArgumentException("Invalid or duplicate event.", nameof(data));
    foreach (string id in data.repeatKeys ?? new List<string>())
        if (string.IsNullOrWhiteSpace(id) || !ledger.repeatKeys.Add(id))
            throw new ArgumentException("Invalid or duplicate repeat key.", nameof(data));
    return ledger;
}
```

In `JindanProofAttempt`:

```csharp
internal JindanProofAttemptSaveData CaptureState()
{
    return new JindanProofAttemptSaveData
    {
        attemptId = AttemptId,
        positionId = PositionId,
        actorId = ActorId,
        profileId = ProfileId,
        siteId = SiteId,
        carrierAbilityInstanceId = CarrierAbilityInstanceId,
        expectedPositionVersion = ExpectedPositionVersion,
        regularProgressTarget = RegularProgressTarget,
        criticalProgressTarget = CriticalProgressTarget,
        regularProgress = RegularProgress,
        criticalProgress = CriticalProgress,
        criticalRound = CriticalRound,
        interruptionReason = InterruptionReason,
        status = Status
    };
}

internal static JindanProofAttempt RestoreState(JindanProofAttemptSaveData data)
{
    if (data == null) throw new ArgumentNullException(nameof(data));
    var attempt = new JindanProofAttempt(
        data.attemptId, data.positionId, data.actorId, data.profileId,
        data.siteId, data.carrierAbilityInstanceId, data.expectedPositionVersion,
        data.regularProgressTarget, data.criticalProgressTarget);
    if (!Enum.IsDefined(typeof(ProofAttemptStatus), data.status) ||
        data.regularProgress < 0 || data.regularProgress > data.regularProgressTarget ||
        data.criticalProgress < 0 || data.criticalProgress > data.criticalProgressTarget ||
        data.criticalRound < 0)
        throw new ArgumentException("Invalid attempt progress snapshot.", nameof(data));
    attempt.RegularProgress = data.regularProgress;
    attempt.CriticalProgress = data.criticalProgress;
    attempt.CriticalRound = data.criticalRound;
    attempt.InterruptionReason = data.interruptionReason;
    attempt.Status = data.status;
    return attempt;
}
```

In `JindanProofCoordinator`, replace the two completion dictionary declarations, `AddCompletion` and `TakeCompletions` from Task 5 with this complete typed-key implementation, then add the capture/restore helpers in the same block:

```csharp
private readonly struct CompletionKey : IEquatable<CompletionKey>
{
    public readonly string PositionId;
    public readonly long WorldTick;

    public CompletionKey(string positionId, long worldTick)
    {
        PositionId = positionId;
        WorldTick = worldTick;
    }

    public bool Equals(CompletionKey other)
    {
        return WorldTick == other.WorldTick &&
            string.Equals(PositionId, other.PositionId, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is CompletionKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return ((PositionId != null ? StringComparer.Ordinal.GetHashCode(PositionId) : 0) * 397) ^
            WorldTick.GetHashCode();
    }
}

private readonly Dictionary<CompletionKey, List<string>> regularCompletions =
    new Dictionary<CompletionKey, List<string>>();
private readonly Dictionary<CompletionKey, List<string>> criticalCompletions =
    new Dictionary<CompletionKey, List<string>>();

private static void AddCompletion(
    IDictionary<CompletionKey, List<string>> store,
    string positionId,
    long worldTick,
    string attemptId)
{
    if (worldTick < 0) throw new ArgumentOutOfRangeException(nameof(worldTick));
    var key = new CompletionKey(positionId, worldTick);
    if (!store.TryGetValue(key, out List<string> values))
    {
        values = new List<string>();
        store.Add(key, values);
    }
    if (!values.Contains(attemptId))
        values.Add(attemptId);
}

private List<JindanProofAttempt> TakeCompletions(
    IDictionary<CompletionKey, List<string>> store,
    string positionId,
    long worldTick)
{
    var key = new CompletionKey(positionId, worldTick);
    if (!store.TryGetValue(key, out List<string> values))
        return new List<JindanProofAttempt>();
    store.Remove(key);
    var result = new List<JindanProofAttempt>();
    foreach (string attemptId in values)
        result.Add(RequireAttempt(attemptId));
    return result;
}

internal void CaptureState(
    List<JindanProofAttemptSaveData> attemptData,
    List<ProofCompletionSaveData> regularData,
    List<ProofCompletionSaveData> criticalData)
{
    foreach (JindanProofAttempt attempt in attempts.Values)
        attemptData.Add(attempt.CaptureState());
    attemptData.Sort((a, b) => string.CompareOrdinal(a.attemptId, b.attemptId));
    CaptureCompletions(regularCompletions, regularData);
    CaptureCompletions(criticalCompletions, criticalData);
}

private static void CaptureCompletions(
    IReadOnlyDictionary<CompletionKey, List<string>> source,
    List<ProofCompletionSaveData> destination)
{
    foreach (KeyValuePair<CompletionKey, List<string>> pair in source)
    {
        var item = new ProofCompletionSaveData
        {
            positionId = pair.Key.PositionId,
            worldTick = pair.Key.WorldTick,
            attemptIds = new List<string>(pair.Value)
        };
        item.attemptIds.Sort(StringComparer.Ordinal);
        destination.Add(item);
    }
    destination.Sort((a, b) =>
    {
        int positionOrder = string.CompareOrdinal(a.positionId, b.positionId);
        return positionOrder != 0 ? positionOrder : a.worldTick.CompareTo(b.worldTick);
    });
}

internal static JindanProofCoordinator RestoreState(
    IReadOnlyList<JindanProofAttemptSaveData> attemptData,
    IReadOnlyList<ProofCompletionSaveData> regularData,
    IReadOnlyList<ProofCompletionSaveData> criticalData)
{
    var coordinator = new JindanProofCoordinator();
    foreach (JindanProofAttemptSaveData data in attemptData ?? Array.Empty<JindanProofAttemptSaveData>())
        coordinator.Register(JindanProofAttempt.RestoreState(data));
    coordinator.RestoreCompletions(
        coordinator.regularCompletions,
        regularData,
        ProofAttemptStatus.AwaitingRegularTickClose);
    coordinator.RestoreCompletions(
        coordinator.criticalCompletions,
        criticalData,
        ProofAttemptStatus.AwaitingCriticalTickClose);
    return coordinator;
}

private void RestoreCompletions(
    IDictionary<CompletionKey, List<string>> destination,
    IReadOnlyList<ProofCompletionSaveData> source,
    ProofAttemptStatus expectedStatus)
{
    foreach (ProofCompletionSaveData item in source ?? Array.Empty<ProofCompletionSaveData>())
    {
        if (item == null || string.IsNullOrWhiteSpace(item.positionId) ||
            item.worldTick < 0 || item.attemptIds == null || item.attemptIds.Count == 0)
            throw new ArgumentException("Invalid completion snapshot.", nameof(source));
        var key = new CompletionKey(item.positionId, item.worldTick);
        if (destination.ContainsKey(key))
            throw new ArgumentException("Duplicate completion key.", nameof(source));

        var restoredIds = new List<string>();
        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string attemptId in item.attemptIds)
        {
            if (!uniqueIds.Add(attemptId) ||
                !attempts.TryGetValue(attemptId, out JindanProofAttempt attempt) ||
                attempt.Status != expectedStatus ||
                !string.Equals(attempt.PositionId, item.positionId, StringComparison.Ordinal))
                throw new ArgumentException("Invalid completion attempt.", nameof(source));
            restoredIds.Add(attemptId);
        }
        destination.Add(key, restoredIds);
    }
}
```

In `JindanPositionRecord`, add:

```csharp
internal JindanPositionSaveData CaptureState()
{
    return new JindanPositionSaveData
    {
        positionId = PositionId,
        profileId = ProfileId,
        seatType = SeatType,
        visibility = Visibility,
        holderActorId = HolderActorId,
        version = Version
    };
}

internal static JindanPositionRecord RestoreState(JindanPositionSaveData data)
{
    if (data == null) throw new ArgumentNullException(nameof(data));
    var position = new JindanPositionRecord(
        data.positionId, data.profileId, data.seatType, data.visibility, data.version);
    if (!string.IsNullOrWhiteSpace(data.holderActorId))
        position.HolderActorId = data.holderActorId;
    return position;
}
```

In `JindanPositionRegistry`, add capture/restore:

```csharp
internal List<JindanPositionSaveData> CaptureState()
{
    var data = new List<JindanPositionSaveData>();
    foreach (JindanPositionRecord position in positions.Values)
        data.Add(position.CaptureState());
    data.Sort((a, b) => string.CompareOrdinal(a.positionId, b.positionId));
    return data;
}

internal static JindanPositionRegistry RestoreState(
    IReadOnlyList<JindanPositionSaveData> data)
{
    var registry = new JindanPositionRegistry();
    foreach (JindanPositionSaveData item in data ?? Array.Empty<JindanPositionSaveData>())
        registry.Add(JindanPositionRecord.RestoreState(item));
    return registry;
}
```

In `JindanCoreState`, add:

```csharp
internal JindanCoreSaveData CaptureState()
{
    var data = new JindanCoreSaveData { actorId = ActorId, coreBindingId = CoreBindingId };
    foreach (SeatCarrierBinding binding in seatBindings)
    {
        data.seatBindings.Add(new SeatCarrierBindingSaveData
        {
            positionId = binding.PositionId,
            seatType = binding.SeatType,
            carrierAbilityInstanceId = binding.CarrierAbilityInstanceId
        });
    }
    return data;
}

internal static JindanCoreState RestoreState(JindanCoreSaveData data)
{
    if (data == null) throw new ArgumentNullException(nameof(data));
    var sourceBindings = data.seatBindings ?? new List<SeatCarrierBindingSaveData>();
    bool hasCore = !string.IsNullOrWhiteSpace(data.coreBindingId);
    if ((!hasCore && sourceBindings.Count != 0) ||
        (hasCore && (sourceBindings.Count < 1 || sourceBindings.Count > 3)))
        throw new ArgumentException("Core and seat count are inconsistent.", nameof(data));

    var seatTypes = new HashSet<JindanSeatType>();
    var positionIds = new HashSet<string>(StringComparer.Ordinal);
    var carrierIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (SeatCarrierBindingSaveData binding in sourceBindings)
    {
        if (binding == null || !Enum.IsDefined(typeof(JindanSeatType), binding.seatType) ||
            string.IsNullOrWhiteSpace(binding.positionId) ||
            string.IsNullOrWhiteSpace(binding.carrierAbilityInstanceId) ||
            !seatTypes.Add(binding.seatType) ||
            !positionIds.Add(binding.positionId) ||
            !carrierIds.Add(binding.carrierAbilityInstanceId))
            throw new ArgumentException("Invalid or duplicate seat binding.", nameof(data));
    }

    var core = new JindanCoreState(data.actorId) { CoreBindingId = data.coreBindingId };
    foreach (SeatCarrierBindingSaveData binding in sourceBindings)
        core.seatBindings.Add(new SeatCarrierBinding(
            binding.positionId, binding.seatType, binding.carrierAbilityInstanceId));
    return core;
}
```

- [ ] **Step 5: 实现快照编排与 schema 失败关闭**

Append the second half to `JindanProofSnapshot.cs`:

```csharp
namespace TianZhang.Cultivation.JindanProof
{
    public static class JindanProofSnapshot
    {
        public const int CurrentSchemaVersion = 1;

        public static JindanProofSaveData Capture(
            IReadOnlyList<DaoProofLedger> ledgers,
            JindanProofCoordinator coordinator,
            JindanPositionRegistry registry,
            IReadOnlyList<JindanCoreState> cores)
        {
            if (ledgers == null) throw new System.ArgumentNullException(nameof(ledgers));
            if (coordinator == null) throw new System.ArgumentNullException(nameof(coordinator));
            if (registry == null) throw new System.ArgumentNullException(nameof(registry));
            if (cores == null) throw new System.ArgumentNullException(nameof(cores));
            var data = new JindanProofSaveData
            {
                schemaVersion = CurrentSchemaVersion,
                positions = registry.CaptureState()
            };
            foreach (DaoProofLedger ledger in ledgers)
                data.ledgers.Add(ledger.CaptureState());
            data.ledgers.Sort((a, b) => string.CompareOrdinal(a.actorId, b.actorId));
            foreach (JindanCoreState core in cores)
                data.cores.Add(core.CaptureState());
            data.cores.Sort((a, b) => string.CompareOrdinal(a.actorId, b.actorId));
            coordinator.CaptureState(
                data.attempts, data.regularCompletions, data.criticalCompletions);
            return data;
        }

        public static JindanProofRestoredState Restore(JindanProofSaveData data)
        {
            if (data == null) throw new System.ArgumentNullException(nameof(data));
            if (data.schemaVersion != CurrentSchemaVersion)
                throw new System.NotSupportedException(
                    "Unsupported jindan proof save schema: " + data.schemaVersion);
            var ledgers = new System.Collections.Generic.List<DaoProofLedger>();
            foreach (DaoProofLedgerSaveData ledger in
                data.ledgers ?? new System.Collections.Generic.List<DaoProofLedgerSaveData>())
                ledgers.Add(DaoProofLedger.RestoreState(ledger));
            var cores = new System.Collections.Generic.List<JindanCoreState>();
            foreach (JindanCoreSaveData core in
                data.cores ?? new System.Collections.Generic.List<JindanCoreSaveData>())
                cores.Add(JindanCoreState.RestoreState(core));
            return new JindanProofRestoredState(
                ledgers,
                JindanProofCoordinator.RestoreState(
                    data.attempts, data.regularCompletions, data.criticalCompletions),
                JindanPositionRegistry.RestoreState(data.positions),
                cores);
        }
    }
}
```

- [ ] **Step 6: 运行 EditMode 并确认快照绿灯**

Run the existing Unity EditMode command. Expected: `JindanProofSnapshotTests` 三项 PASS；同一待关闭 tick 只产生一次 resolution，已处理行为事件读档后仍不能重放。

- [ ] **Step 7: 最小检查、汇报与授权后提交**

Run `git diff --check` against all Task 8 files. With explicit authorization, stage only declared files plus `.meta`, run cached check, then commit:

```powershell
git commit --only -m "feat: persist jindan proof domain state" -- `
  'src/Assets/Scripts/Cultivation/JindanProof/DaoProofLedger.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofAttempt.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofCoordinator.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanPositionRegistry.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofSnapshot.cs' `
  'src/Assets/Scripts/Cultivation/JindanProof/JindanProofSnapshot.cs.meta' `
  'src/Assets/Tests/EditMode/JindanProofSnapshotTests.cs' `
  'src/Assets/Tests/EditMode/JindanProofSnapshotTests.cs.meta'
```

---

### Task 9: 建立正式证位规则事实源并消除旧取得口径

**Files:**

- Create: `docs/基础设定/金丹位格证位与争位规则.txt`
- Modify: `docs/基础设定/元婴锚点与金丹位格设定.txt`
- Modify: `docs/基础设定/修行境界.txt`
- Modify: `docs/基础设定/境界特性.txt`

- [ ] **Step 1: 写事实源断言并确认当前为红灯**

Run:

```powershell
$proofRule = 'docs/基础设定/金丹位格证位与争位规则.txt'
if (-not (Test-Path -LiteralPath $proofRule)) { throw 'MISSING_PROOF_RULE' }
$text = Get-Content -LiteralPath $proofRule -Raw -Encoding UTF8
@(
  '不创建“资格证”',
  '未满足任一硬性最低条件时，后台成功率固定为 `0%`',
  '证位没有最终随机抽签',
  '第一名完成稳定绑定者取得位格',
  '同一世界结算时刻',
  '寿元将尽不能绕过紫府圆满或最低道证条件'
) | ForEach-Object {
  if (-not $text.Contains($_)) { throw "MISSING_RULE: $_" }
}
```

Expected: FAIL with `MISSING_PROOF_RULE` because the formal fact source has not been created.

- [ ] **Step 2: 创建短而专责的正式规则事实源**

Create `docs/基础设定/金丹位格证位与争位规则.txt` with this complete content:

```text
# 金丹位格证位与争位规则

✅ 已审核（Codex；根据 2026-07-19 已确认设计建立正式事实源）

> 本文只负责金丹真实位格的道证条件、知识显示、空位情报、驻地证位、争位、绑定与 NPC 决策。十七道路及五十一个基础效果以《元婴锚点与金丹位格矩阵》为准；装配、兼容与冲突以《金丹基础效果装配与冲突规则》为准；金丹结构、主承载、核心与丹毁以《元婴锚点与金丹位格设定》为准。

## 一、唯一稳定取得方式

1. 稳定真实位格只有一种最终取得方式：候选人完成对应位格的完整证位，并通过版本化原子绑定。
2. 继承、敕封、夺取、暂寄、公开空位、隐秘发现与独立应位只提供情报、地点、设施、资源、政治准入或准备先手，不能转移个人履历、个人证位进度、适配度或稳定绑定。
3. 前任死亡或位格合法释放后，位格先进入空缺状态，不自动落到继承人、受封者或击杀者身上。
4. 筑基角色取得第一位时，真实位格、第一主承载与唯一 `JindanCoreBinding` 原子建立。已有金丹取得第二、第三位时只追加真实位格与独立主承载，原核心不得更换或重建。
5. 不创建“资格证”“已获资格”或同类永久资格对象；每次判断都直接读取角色当前的永久道证履历与对应位格档案。

## 二、道证履历与最低条件

1. 角色从游戏开始后台积累永久道证履历，不要求预先知道道路、位格或条件。
2. 每条道路定义源位、化位、界位共享的长期行为指标；每个“道路 × 位格类型”再定义独立标志性成就，共形成五十一份证位档案。
3. 最低条件同时要求道路共享指标达到阈值，以及对应位格的标志性成就全部完成。
4. 标志性成就必须声明具体动作、合法目标、战斗／地区／环境条件、完成判定、禁止行为、失败条件和防刷规则，不得只写“理解”“证明”或“掌握”。
5. 每个共享指标声明有效目标、最低风险、来源、重复键与衰减／去重方式。同一无风险目标、训练假人或自造循环不能无限贡献履历。
6. 同一次合法行为可以推进多个相关位格的道路共享指标；标志性成就只推进明确绑定的位格档案。
7. 已记录履历是角色历史，不因媒介消耗、证位失败、致命中断或取得位格而清空。
8. 未满足任一硬性最低条件时，后台成功率固定为 `0%`，但玩家仍可启动证位；该尝试永远不能完成最终绑定。

## 三、知识、情报与显示

1. 位格条件知识分为未知、道路方向已知、完整条件已知三级。后台履历记录不受知识等级影响。
2. 未知时不显示条件、履历进度、适配度或预计成功率；道路方向已知时只显示方向与已知风险，不显示精确条件、阈值或百分比；完整条件已知时才显示可靠知识覆盖的条件、进度、适配度来源与动态预估。
3. 失败只提供世界内征兆，不自动解锁条件、阈值、错误码或后台成功率。日志、UI、AI 行为和错误返回均不得旁路泄密。
4. 空位情报与证位条件知识分开记录。空位情报分为天下皆知、势力内部知晓、传闻和隐秘。
5. 隐秘空位的发现者获得准备先手；正式证位开始后持续天地异象逐步暴露地点、道路方向与进程征兆，但不自动揭示精确条件。

## 四、驻地证位与时间推进

1. 启动证位必须选择真实世界地点，准备合法现实支点、证位核心、兼容主承载、材料、供能、维护资源与护道力量。
2. 候选人必须停留在证位范围内，世界时间快速推进；世界 NPC、势力、寿元、地区与任务状态继续正常变化。
3. 位格事件、天地异象升级、设施／供能失稳、新候选人加入、敌对干扰或世界状态变化会暂停快进，并进入对应战棋、设施、资源或规则操作。
4. 候选人可以在证位范围内亲自战斗、操作设施和修复证位核心。
5. 主动离开、被强制驱离、证位核心被毁、主承载被合法切断、档案关键阶段彻底失败或主动终止均为致命中断。
6. 致命中断清空本次个人证位进度，不清空永久道证履历、标志性成就、角色修为或已固化为世界状态的公共工程。未固化材料与临时准备按事件结果结算，不自动返还。
7. 证位没有最终随机抽签；完成全部阶段、处理全部关键事件并成功提交最终稳定绑定时必定成功。动态预计成功率只是当前已知风险的预测。

## 五、并行争位与外部干扰

1. 多名候选人可对同一空位并行证位，各自维护履历、主承载、支点、资源、事件和个人进度。后来者从零开始，不继承先行者个人进度。
2. 第一名完成稳定绑定者取得位格。绑定成功后其他候选人的本轮个人进度立即失效；其永久履历和已固化公共工程按各自规则保留。
3. 任何角色或势力均可帮助、袭击、封锁、泄密、截断供给、破坏支点或支援其他候选人。无资格者可以干扰，却不能完成稳定绑定。
4. 同一世界结算时刻有两名或更多候选人完成常规阶段时，进入临界争位；不得读取随机数、NPC ID、注册顺序、加载顺序或隐藏优先级决定胜者。
5. 临界争位者继续维持结构、支付资源、守护己方并干扰对手，进入额外稳定阶段。若额外阶段仍同刻完成，则继续下一稳定轮，直到出现唯一完成者并通过原子绑定。

## 六、适配度边界

1. 适配度来自超过最低要求的有效履历、标志性成就质量、目标／地区／挑战多样性，以及成位后继续履行该位格要求的行为。
2. 适配度采用封顶与边际递减，可影响证位速度、事件容错、资源压力、主承载失稳余量，以及成位后的维护成本、展开效率与安全重铸难度。
3. 适配度不得改变同阶规则优先级、位格权限、合法目标、冲突裁定或原子绑定先后。它可在成位后继续提高，不形成永久锁死的金丹品质档。

## 七、NPC 决策

1. 只有具备持久成长、履历、寿元、知识和资源记录的 NPC 运行完整证位决策；普通背景 NPC 不逐个模拟。
2. NPC 亲自证位必须同时满足：紫府圆满、全部最低道证条件、知道空位、知道可用地点、具备兼容主承载／支点／设施／资源／护道准备，且没有更高优先级生存危机或强制职责。
3. NPC 只使用自身知识、情报可信度、已知竞争者、准备、路程、资源、仇敌、动机和风险偏好估算主观胜算，不得读取后台真实成功率、未知条件或他人隐藏进度。
4. 成功预估低于自身风险阈值时，NPC 不亲自证位。寿元将尽只能降低风险阈值，不能绕过紫府圆满或最低道证条件。
5. 不满足亲自证位条件的 NPC 仍可支援、破坏、封锁、调查或采取政治行动。
6. 决策只在空位出现、可靠情报更新、天地异象升级、竞争者加入、世界状态重大变化或寿元进入危险阶段时重算，不进行全世界每日扫描。

## 八、原子性与存读档

1. 每次证位绑定具体位格实例和启动时版本。最终提交重新检查空缺、版本、地点、现实支点、主承载、最低条件、关键事件与唯一占据。
2. 任一检查失败不写入部分占据、半个 `SeatCarrierBinding`、新核心或个人成功状态。
3. 第一位建核与后续位追加遵守同一原子事务；后续位不得提交新的核心 ID。
4. 存档保存世界时间、已处理行为与里程碑、资源支付、设施状态、候选进度、临界轮次、位格版本、占据、核心和主承载绑定。
5. 读档不得重复累计履历、重复生成事件、重复扣费、重复推进、重复关闭世界 tick 或产生双重占据。
```

- [ ] **Step 3: 重接金丹结构事实源**

In `docs/基础设定/元婴锚点与金丹位格设定.txt`:

1. Extend the opening responsibility note with: `真实位格的证位条件、知识显示、空位、争位与取得流程以 docs/基础设定/金丹位格证位与争位规则.txt 为准。`
2. Replace “夺取、继承、敕封、暂寄、自辟只表示占据方式或状态” with “夺取、继承、敕封、暂寄与独立应位只表示证位起点、权利来源或过渡状态；它们不跳过个人完整证位。自辟元婴仍走独立晋升事务”。
3. Replace “取得位格只解锁候选资格” with “满足最低条件只使最终绑定从后台 `0%` 变为可完成；稳定真实位格仍须完成完整证位”。
4. In section four, state that second and third seats reuse the existing `JindanCoreBinding` and do not restart Foundation cultivation.
5. In section seven, change grant/inheritance wording so a grant provides access, site, facilities, reliable knowledge, resources and protection; it never establishes stable occupancy before the grantee completes proof.

- [ ] **Step 4: 同步境界和境界特性口径**

In `docs/基础设定/修行境界.txt`, replace the occupation-source bullets with:

```text
位格取得：
  - 三个阶段都必须完成目标位格的完整证位；筑基期与金丹期共享永久道证履历，取得第二、第三位不重新从筑基修行
  - 夺取、继承、敕封、暂寄、公开／隐秘空位与独立应位只改变情报、地点、设施、资源、政治准入和准备先手
  - 未满足目标档案任一最低条件时，后台成功率为0%；满足条件后仍须驻地完成全部阶段和原子稳定绑定
  - 三种真实位格的取得顺序自由；后续位只追加独立主承载，唯一 `JindanCoreBinding` 全生命周期保持不变
```

In `docs/基础设定/境界特性.txt`, replace the source/status and transfer rules with:

```text
1. 夺取、继承、敕封、暂寄与独立应位描述证位起点、权利来源或过渡状态，不创建第四位格，也不直接建立稳定占据。
2. 原持有者死亡或合法释放后，位格先进入版本化空缺；继承人、受封者、击杀者和独立候选人都必须用自己的履历、主承载、支点与资源完成证位。
3. 多名候选人可并行争位；第一名通过原子绑定者取得位格，同刻完成者进入临界争位，不使用随机或隐藏顺序裁定。
4. 既有金丹取得后续位时不得换核或重结金丹；只追加新的 `SeatCarrierBinding`。
5. 稳定真实位格已经建立后的丧失与丹毁死亡规则保持不变。
```

- [ ] **Step 5: 运行事实源断言和项目文本检查**

Re-run Step 1. Expected: no exception. Then run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths `
  'docs/基础设定/金丹位格证位与争位规则.txt', `
  'docs/基础设定/元婴锚点与金丹位格设定.txt', `
  'docs/基础设定/修行境界.txt', `
  'docs/基础设定/境界特性.txt'
```

Expected: `check-review-text: OK (4 files)`.

- [ ] **Step 6: 搜索旧的直接取得口径**

Run:

```powershell
rg -n '继承具体席位|授予的使用权或临时承位|新持有者.*建立稳定占据|取得位格只解锁候选资格' `
  'docs/基础设定/元婴锚点与金丹位格设定.txt' `
  'docs/基础设定/修行境界.txt' `
  'docs/基础设定/境界特性.txt'
```

Expected: 空输出。若命中，逐条改成“提供证位起点优势但不得跳过完整证位”的现行口径。

- [ ] **Step 7: 最小检查、汇报与授权后提交**

Run `git diff --check` against the four document paths. With explicit authorization, run the pending-whitespace check before staging, stage only the four paths, run cached check, then commit:

```powershell
git commit --only -m "docs: establish jindan proof rules" -- `
  'docs/基础设定/金丹位格证位与争位规则.txt' `
  'docs/基础设定/元婴锚点与金丹位格设定.txt' `
  'docs/基础设定/修行境界.txt' `
  'docs/基础设定/境界特性.txt'
```

---

### Task 10: 增加领域端到端验收并完成覆盖审计

**Files:**

- Create: `src/Assets/Tests/EditMode/JindanProofAcceptanceTests.cs`
- Create: `src/Assets/Tests/EditMode/JindanProofAcceptanceTests.cs.meta`
- Verify: all files declared in this plan

- [ ] **Step 1: 写端到端验收测试**

Create `JindanProofAcceptanceTests.cs`:

```csharp
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class JindanProofAcceptanceTests
    {
        [Test]
        public void EarlyHiddenHistoryAppearsAfterKnowledgeAndCanPowerFirstBinding()
        {
            DaoProofLedger ledger = JindanProofTestFixtures.EligibleFireLedger();
            Assert.That(JindanProofKnowledge.Project(
                JindanProofTestFixtures.FireSourceProfile(), ledger,
                ProofKnowledgeLevel.Unknown, 80, 70).Requirements, Is.Empty);
            Assert.That(JindanProofKnowledge.Project(
                JindanProofTestFixtures.FireSourceProfile(), ledger,
                ProofKnowledgeLevel.FullProfile, 80, 70).Requirements,
                Has.All.Matches<ProofRequirementView>(item => item.IsMet));

            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            var core = new JindanCoreState("actor_player");
            JindanProofAttempt attempt = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_acceptance", "actor_player",
                "position_fire_source_01", "jindan_fire_source", "ability_acceptance", 0);

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(attempt.AttemptId, "core_acceptance", true, true),
                JindanProofTestFixtures.FireSourceProfile(), ledger, core, coordinator);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(core.CoreBindingId, Is.EqualTo("core_acceptance"));
        }

        [Test]
        public void UnqualifiedAttemptNeverBindsEvenWithValidSiteAndCarrier()
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            var attempt = JindanProofTestFixtures.NewAttempt("attempt_unqualified");
            coordinator.Register(attempt);
            attempt.AdvanceRegular(100, false);

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(attempt.AttemptId, "core_invalid", true, true),
                JindanProofTestFixtures.FireSourceProfile(),
                new DaoProofLedger("actor_player"),
                new JindanCoreState("actor_player"),
                coordinator);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(JindanBindFailureReason.AttemptNotReady));
        }

        [Test]
        public void LaterUniqueCriticalCompletionWinsWithoutRandomOrIdPriority()
        {
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt zAttempt = JindanProofTestFixtures.NewAttempt(
                "z_attempt", "actor_z");
            JindanProofAttempt aAttempt = JindanProofTestFixtures.NewAttempt(
                "a_attempt", "actor_a");
            coordinator.Register(zAttempt);
            coordinator.Register(aAttempt);
            zAttempt.AdvanceRegular(100, true);
            aAttempt.AdvanceRegular(100, true);
            coordinator.SubmitRegularCompletion(zAttempt.AttemptId, 6000);
            coordinator.SubmitRegularCompletion(aAttempt.AttemptId, 6000);
            coordinator.CloseRegularTick(zAttempt.PositionId, 6000);
            zAttempt.AdvanceCritical(20);
            coordinator.SubmitCriticalCompletion(zAttempt.AttemptId, 7000);

            ProofTickResolution result = coordinator.CloseCriticalTick(
                zAttempt.PositionId, 7000);

            Assert.That(result.Kind, Is.EqualTo(ProofTickResolutionKind.UniqueReady));
            Assert.That(result.UniqueAttemptId, Is.EqualTo(zAttempt.AttemptId));
            Assert.That(aAttempt.Status, Is.EqualTo(ProofAttemptStatus.CriticalContest));
        }

        [Test]
        public void AdaptationAndForecastCannotChangeEligibilityOrBindPriority()
        {
            var emptyLedger = new DaoProofLedger("actor_player");
            JindanProofProfileDefinition profile = JindanProofTestFixtures.FireSourceProfile();

            JindanProofView view = JindanProofKnowledge.Project(
                profile, emptyLedger, ProofKnowledgeLevel.FullProfile, 100, 100);

            Assert.That(view.AdaptationPercent, Is.EqualTo(100));
            Assert.That(view.EstimatedSuccessPercent, Is.EqualTo(100));
            Assert.That(JindanProofEligibility.Evaluate(profile, emptyLedger).IsSatisfied, Is.False);
        }
    }
}
```

- [ ] **Step 2: 运行完整 EditMode 验收**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1
```

Expected: all existing and new EditMode tests PASS with zero failed, skipped or inconclusive. Record total/passed counts from the result XML.

- [ ] **Step 3: 运行 plan/spec 覆盖断言**

Run:

```powershell
$plan = Get-Content -LiteralPath 'docs/superpowers/plans/2026-07-19-jindan-position-proof-domain-kernel.md' -Raw -Encoding UTF8
$spec = Get-Content -LiteralPath 'docs/superpowers/specs/2026-07-19-jindan-position-proof-and-contestation-design.md' -Raw -Encoding UTF8
@(
  '永久道证履历',
  '知识遮蔽',
  '致命中断',
  '临界争位',
  '原子绑定',
  'NPC 亲自证位硬门槛',
  '读档幂等'
) | ForEach-Object {
  if (-not $plan.Contains($_)) { throw "PLAN_COVERAGE_MISSING: $_" }
}
if (-not $spec.Contains('最低验收场景')) { throw 'SPEC_ACCEPTANCE_SECTION_MISSING' }
```

Expected: no exception.

- [ ] **Step 4: 运行文本、空白和目标路径检查**

Set the exact pipe-separated `expectedPaths` to every file created or modified by Tasks 1–10, then run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths `
  'docs/基础设定/金丹位格证位与争位规则.txt', `
  'docs/基础设定/元婴锚点与金丹位格设定.txt', `
  'docs/基础设定/修行境界.txt', `
  'docs/基础设定/境界特性.txt'
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths $expectedPaths
git diff --check -- $expectedPaths.Split('|')
```

Expected: all checks report OK or empty output. Do not include any unrelated dirty path in `expectedPaths`.

- [ ] **Step 5: 对照规格逐节记录本计划覆盖与明确延期**

Record this matrix in the Task report:

| 规格范围 | 本计划证据 | 状态 |
|---|---|---|
| 永久履历、共享指标、成就、防刷 | Tasks 2–3 tests | 内核完成 |
| 未知/方向/完整知识显示 | Task 3 tests | 内核完成 |
| 空位可见度、版本和唯一占据 | Task 6 tests | 内核完成 |
| 0% 可启动、致命中断清零 | Task 4 tests | 内核完成 |
| 世界快进、设施、事件暂停 | 子项目 3 | 本计划不接场景 |
| 并行完成、临界争位 | Task 5 tests | 内核完成 |
| 原子绑定、第一位建核、后续不换核 | Task 6 tests | 内核完成 |
| 存读档幂等 | Task 8 tests | 内核完成 |
| NPC 硬门槛与主观风险 | Task 7 tests | 纯策略完成；事件驱动调度属于子项目 5 |
| 17 道路/51 档案 | 子项目 2 | 本计划只用火·源位测试夹具 |
| 适配度数值、周期和事件强度 | 子项目 5 + BattleSim | 本计划只锁定不影响权限/优先级 |
| 玩家 UI、天地异象和情报传播 | 子项目 4 | 本计划只提供不可泄密视图契约 |

Do not state that the entire confirmed spec is implemented. The valid completion claim is: “金丹证位领域内核完成，场景、51 档案、UI 和 NPC 调度仍按拆分子项目实施”。

- [ ] **Step 6: 检查无关工作树未被纳入**

Run:

```powershell
git status --short
git diff --name-only
git diff --cached --name-only
```

Expected: all pre-existing unrelated changes remain present and unstaged unless their own task independently handled them; the cached list is empty when no commit authorization was given.

- [ ] **Step 7: 仅在用户明确授权后提交验收测试**

```powershell
git add -- `
  'src/Assets/Tests/EditMode/JindanProofAcceptanceTests.cs' `
  'src/Assets/Tests/EditMode/JindanProofAcceptanceTests.cs.meta'
git diff --cached --check
git commit --only -m "test: cover jindan proof domain acceptance" -- `
  'src/Assets/Tests/EditMode/JindanProofAcceptanceTests.cs' `
  'src/Assets/Tests/EditMode/JindanProofAcceptanceTests.cs.meta'
```

No push is part of this plan.

---

## 完成条件

本计划仅在以下条件全部满足时完成：

1. 所有新领域类型位于 `TianZhang.Domain`，没有对 `TianZhang.Gameplay`、Combat、Editor 或 UI 的反向依赖。
2. 永久履历不保存资格证；同一合法行为可推进多个共享指标，事件重放和重复目标不能刷取。
3. 未知知识视图没有条件、进度、适配度或成功率泄漏。
4. 未满足硬条件的玩家可启动但不能进入最终绑定准备态。
5. 致命中断清空个人尝试进度但不清空永久履历。
6. 同 tick 完成者进入临界争位；同 tick 再完成就继续一轮，不使用随机或 ID。
7. 位格 CAS、唯一占据、第一位建核、第二/第三位不换核和失败无部分写入均有测试。
8. 读档保持事件、进度、临界轮次、位格版本、核心和绑定，且不会重复结算。
9. NPC 策略只读主观信息；寿元不能绕过紫府圆满或最低条件。
10. 正式事实源明确继承、敕封、夺取和暂寄只提供证位起点优势。
11. EditMode、新事实源检查、空白检查和目标路径检查全部通过。
12. 报告明确说明 51 档案、CSV/asset、世界快进、设施事件、UI、天地异象、NPC 调度和 BattleSim 尚属于后续子项目。
