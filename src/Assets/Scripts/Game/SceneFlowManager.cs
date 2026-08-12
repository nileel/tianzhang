using UnityEngine;
using UnityEngine.SceneManagement;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Game.CharacterCreation;
using TianZhang.Gameplay.Contracts;

namespace TianZhang.Game
{
    /// <summary>
    /// 场景流转管理器（跨场景持久单例）
    /// 第一轮只做场景名字符串传递与玩家档案传递，不改CTB/战斗/冷却/槽位语义。
    /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：新增文件
    /// </summary>
    public class SceneFlowManager : MonoBehaviour
    {
        public const string LegacyOriginFallbackStartNodeId = "jiangzuo_hub";

        private static SceneFlowManager instance;

        public static SceneFlowManager Instance
        {
            get
            {
                if (instance == null)
                    instance = UnityEngine.Object.FindFirstObjectByType<SceneFlowManager>();
                return instance;
            }
            private set => instance = value;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            GameBootstrap.RequireRuntime();
        }

        public void StartNewGame(CharacterData profile)
        {
            SceneManager.LoadScene(PrepareNewGame(profile));
        }

        public string PrepareNewGame(CharacterData profile)
        {
            var startNodeId = ResolveStartNodeId(profile);
            GameBootstrap.RequireRuntime().BeginNewGame(
                CharacterRuntimeProfile.FromDefinition("player", profile),
                CultivationState.FromDefinition(profile == null ? null : profile.foundationPurpleMansionState),
                startNodeId);
            return GameBootstrap.RequireRuntime().EnterWorld(startNodeId);
        }

        public static string ResolveStartNodeId(CharacterData profile)
        {
            var origin = profile == null ? null : CharacterCreationCatalog.FindOrigin(profile.originId);
            if (origin != null && !string.IsNullOrWhiteSpace(origin.StartNodeId))
                return origin.StartNodeId;

            var originId = profile == null || string.IsNullOrWhiteSpace(profile.originId)
                ? "(missing)"
                : profile.originId;
            Debug.LogWarning($"[SceneFlow] Unknown or legacy origin '{originId}'; using fallback start node '{LegacyOriginFallbackStartNodeId}' without changing the profile.");
            return LegacyOriginFallbackStartNodeId;
        }

        public void EnterWorld(string nodeId)
        {
            SceneManager.LoadScene(PrepareWorldEntry(nodeId));
        }

        /// <summary>
        /// 进入据点场景（持久化 settlementId 到 GameRuntime 导航状态）。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：TQ-014-DS-05 返工 — 补 ID 持久化
        /// </summary>
        public void EnterSettlement(string settlementId)
        {
            SceneManager.LoadScene(PrepareSettlementEntry(settlementId));
        }

        /// <summary>
        /// 进入副本/战斗场景（持久化 adventureId 到 GameRuntime 导航状态）。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：TQ-014-DS-05 返工 — 补 ID 持久化
        /// </summary>
        public void EnterAdventure(string adventureId, SceneReturnTarget returnTarget)
        {
            SceneManager.LoadScene(PrepareAdventureEntry(adventureId, returnTarget));
        }

        public void ReturnToPreviousScene()
        {
            SceneManager.LoadScene(PrepareReturnToPreviousScene());
        }

        public string PrepareWorldEntry(string nodeId)
        {
            return GameBootstrap.RequireRuntime().EnterWorld(nodeId);
        }

        public string PrepareSettlementEntry(string settlementId)
        {
            return GameBootstrap.RequireRuntime().EnterSettlement(settlementId);
        }

        public string PrepareAdventureEntry(string adventureId, SceneReturnTarget returnTarget)
        {
            return GameBootstrap.RequireRuntime().EnterAdventure(adventureId, returnTarget);
        }

        public string PrepareReturnToPreviousScene()
        {
            return GameBootstrap.RequireRuntime().ReturnToPreviousScene();
        }

        public void ReturnToMainMenu()
        {
            GameBootstrap.RequireRuntime().Clear();
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
