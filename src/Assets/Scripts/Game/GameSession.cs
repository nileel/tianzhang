using System.Collections.Generic;
using TianZhang.Content;
using UnityEngine;
using TianZhang.Cultivation;
using TianZhang.Entity;

namespace TianZhang.Game
{
    /// <summary>
    /// 游戏会话存档（跨场景持久单例）
    /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：新增文件
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        public const int InitialWorldYear = 387;
        public const string InitialWorldSeasonId = "autumn";
        public const string InitialWorldTimeOfDayId = "dawn";

        private static GameSession instance;
        private readonly InventoryGrantService inventoryGrantService = new InventoryGrantService();
        private readonly BountyRuntime bountyRuntime = new BountyRuntime();

        public static GameSession Instance
        {
            get
            {
                if (instance == null)
                    instance = UnityEngine.Object.FindFirstObjectByType<GameSession>();
                return instance;
            }
            private set => instance = value;
        }

        public CharacterData PlayerProfile { get; private set; }
        public string CurrentWorldNodeId { get; private set; } = "jiangzuo_hub";
        public int WorldYear { get; private set; } = InitialWorldYear;
        public string WorldSeasonId { get; private set; } = InitialWorldSeasonId;
        public int WorldDay { get; private set; } = 1;
        public string WorldTimeOfDayId { get; private set; } = InitialWorldTimeOfDayId;
        public QuestStateStore QuestStates { get; } = new QuestStateStore();
        public InventoryStateStore InventoryStates { get; } = new InventoryStateStore();
        public NpcStateStore NpcStates { get; } = new NpcStateStore();
        /// <summary>
        /// 本次会话的悬赏实例状态；存档与恢复由 U-BOUNTY-01B 负责。
        /// </summary>
        public BountyStateStore BountyStates { get; } = new BountyStateStore();
        public FoundationPurpleMansionSaveData PlayerFoundationPurpleMansionSaveData { get; private set; }

        /// <summary>
        /// 当前据点/副本 ID（用于返回流转时传递上下文）。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：新增字段
        /// </summary>
        public string CurrentSettlementId { get; private set; }
        public string CurrentAdventureId { get; private set; }

        public SceneReturnTarget LastReturnTarget { get; private set; }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void SetPlayerProfile(CharacterData profile)
        {
            PlayerProfile = profile;
            ResetFoundationPurpleMansionSaveData();
        }

        public void BeginNewGame(CharacterData profile, string startNodeId)
        {
            PlayerProfile = profile;
            ResetFoundationPurpleMansionSaveData();
            CurrentWorldNodeId = string.IsNullOrEmpty(startNodeId) ? "jiangzuo_hub" : startNodeId;
            ResetWorldTime();
            ResetStateSnapshots();
            CurrentSettlementId = null;
            CurrentAdventureId = null;
            LastReturnTarget = default;
        }

        public void ClearSession()
        {
            PlayerProfile = null;
            PlayerFoundationPurpleMansionSaveData = null;
            CurrentWorldNodeId = "jiangzuo_hub";
            ResetWorldTime();
            ResetStateSnapshots();
            CurrentSettlementId = null;
            CurrentAdventureId = null;
            LastReturnTarget = default;
        }

        /// <summary>
        /// 仅由显式游戏行为调用的世界时间推进入口。场景切换与战斗返回不会调用它。
        /// </summary>
        public void AdvanceWorldDay()
        {
            checked
            {
                WorldDay++;
            }
        }

        /// <summary>
        /// 由世界、资源、境界目标、寿元或环境事件调用的单个 NPC 重算入口。
        /// 它从不由 <see cref="AdvanceWorldDay"/> 扫描全部 NPC，也不接收玩家排程输入。
        /// </summary>
        public NpcCultivationActionRecalculationResult RecalculateNpcCultivation(
            string npcId,
            NpcCultivationActionCoordinator coordinator,
            NpcCultivationActionRecalculationRequest request)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                throw new System.ArgumentException("NPC ID must not be empty.", nameof(npcId));
            if (coordinator == null)
                throw new System.ArgumentNullException(nameof(coordinator));
            if (request == null)
                throw new System.ArgumentNullException(nameof(request));
            if (!NpcStates.TryGet(npcId, out NpcStateSnapshot npc))
                return NpcCultivationActionRecalculationResult.Rejected(
                    NpcCultivationActionCoordinator.NpcNotFound);
            string failureReason = null;
            if (npc.FoundationPurpleMansionState == null ||
                !FoundationPurpleMansionRuntimeState.TryRestore(
                    npc.FoundationPurpleMansionState,
                    out FoundationPurpleMansionRuntimeState runtimeState,
                    out failureReason))
            {
                return NpcCultivationActionRecalculationResult.Rejected(
                    failureReason ?? NpcCultivationActionCoordinator.MissingCultivationState);
            }

            NpcCultivationActionRecalculationResult result = coordinator.Recalculate(
                runtimeState,
                request);
            if (result.Succeeded)
            {
                NpcStates.Set(new NpcStateSnapshot(
                    npc.NpcId,
                    npc.WorldNodeId,
                    npc.Steps,
                    runtimeState.CaptureSaveData()));
            }

            return result;
        }

        public void SetWorldNode(string nodeId)
        {
            CurrentWorldNodeId = string.IsNullOrEmpty(nodeId) ? "jiangzuo_hub" : nodeId;
        }

        /// <summary>
        /// 设置当前据点 ID（进入 Settlement 前由 SceneFlowManager 调用）。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：新增方法
        /// </summary>
        public void SetSettlementId(string settlementId)
        {
            CurrentSettlementId = string.IsNullOrEmpty(settlementId) ? null : settlementId;
        }

        /// <summary>
        /// 设置当前副本 ID（进入 Adventure 前由 SceneFlowManager 调用）。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：新增方法
        /// </summary>
        public void SetAdventureId(string adventureId)
        {
            CurrentAdventureId = string.IsNullOrEmpty(adventureId) ? null : adventureId;
        }

        public void SetReturnTarget(SceneReturnTarget target)
        {
            LastReturnTarget = target;
        }

        public void CapturePlayerFoundationPurpleMansionState(Character player)
        {
            PlayerFoundationPurpleMansionSaveData = player == null ||
                !player.HasFoundationPurpleMansionState
                ? null
                : player.CaptureFoundationPurpleMansionSaveData();
        }

        public bool ApplyPlayerFoundationPurpleMansionState(Character player)
        {
            if (player == null)
                throw new System.ArgumentNullException(nameof(player));
            if (PlayerFoundationPurpleMansionSaveData == null)
                return false;

            player.RestoreFoundationPurpleMansionSaveData(PlayerFoundationPurpleMansionSaveData);
            return true;
        }

        public GameSessionSaveData CaptureSaveData()
        {
            return GameSessionSnapshot.Capture(this);
        }

        public InventoryGrantResult GrantItems(
            ContentCatalogData catalog,
            IReadOnlyList<InventoryGrantRequest> requests)
        {
            return inventoryGrantService.Grant(InventoryStates, catalog, requests);
        }

        /// <summary>
        /// 在玩家当前据点接取悬赏；只接受位于 issuerSettlementId 且状态为 Available 的悬赏。
        /// </summary>
        public BountyActionResult AcceptBounty(ContentCatalogData catalog, string bountyId)
        {
            return bountyRuntime.Accept(BountyStates, catalog, bountyId, CurrentSettlementId);
        }

        /// <summary>
        /// 由 Adventure 胜利结算点提交结构化 adventureId + enemyId；同一场胜利至多推进一次。
        /// </summary>
        public BountyActionResult RecordBountyDefeat(
            ContentCatalogData catalog,
            string adventureId,
            string enemyId)
        {
            return bountyRuntime.RecordDefeat(BountyStates, catalog, adventureId, enemyId);
        }

        /// <summary>
        /// 领奖：先完整校验奖励与堆叠，再一次性替换库存与 Claimed 状态；任一失败两者均不变。
        /// </summary>
        public BountyActionResult ClaimBounty(ContentCatalogData catalog, string bountyId)
        {
            return bountyRuntime.Claim(BountyStates, catalog, InventoryStates, bountyId);
        }

        /// <summary>
        /// 查询悬赏实例；无实例时返回 Available。
        /// </summary>
        public BountyStateSnapshot GetBountyState(string bountyId)
        {
            return bountyRuntime.GetState(BountyStates, bountyId);
        }

        public void RestoreSaveData(GameSessionSaveData data)
        {
            GameSessionRestoredState restored = GameSessionSnapshot.Restore(data);

            QuestStates.ReplaceAll(restored.Quests);
            InventoryStates.ReplaceAll(restored.Inventory);
            NpcStates.ReplaceAll(restored.Npcs);
            CurrentWorldNodeId = restored.CurrentWorldNodeId;
            WorldYear = restored.WorldYear;
            WorldSeasonId = restored.WorldSeasonId;
            WorldDay = restored.WorldDay;
            WorldTimeOfDayId = restored.WorldTimeOfDayId;
            CurrentSettlementId = restored.CurrentSettlementId;
            CurrentAdventureId = restored.CurrentAdventureId;
            LastReturnTarget = restored.LastReturnTarget;
            PlayerFoundationPurpleMansionSaveData = restored.PlayerFoundationPurpleMansionSaveData;
        }

        private void ResetWorldTime()
        {
            WorldYear = InitialWorldYear;
            WorldSeasonId = InitialWorldSeasonId;
            WorldDay = 1;
            WorldTimeOfDayId = InitialWorldTimeOfDayId;
        }

        private void ResetStateSnapshots()
        {
            QuestStates.Clear();
            InventoryStates.Clear();
            NpcStates.Clear();
            BountyStates.Clear();
        }

        private void ResetFoundationPurpleMansionSaveData()
        {
            PlayerFoundationPurpleMansionSaveData = null;
            if (PlayerProfile == null || PlayerProfile.foundationPurpleMansionState == null)
                return;

            if (!FoundationPurpleMansionRuntimeState.TryCreate(
                    PlayerProfile.foundationPurpleMansionState,
                    out FoundationPurpleMansionRuntimeState runtimeState,
                    out string failureReason))
            {
                throw new System.InvalidOperationException(failureReason);
            }

            PlayerFoundationPurpleMansionSaveData = runtimeState.CaptureSaveData();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
