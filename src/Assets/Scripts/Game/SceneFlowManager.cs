using UnityEngine;
using UnityEngine.SceneManagement;
using TianZhang.Entity;

namespace TianZhang.Game
{
    /// <summary>
    /// 场景流转管理器（跨场景持久单例）
    /// 第一轮只做场景名字符串传递与玩家档案传递，不改CTB/战斗/冷却/槽位语义。
    /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：新增文件
    /// </summary>
    public class SceneFlowManager : MonoBehaviour
    {
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
            EnsureSession();
        }

        public void StartNewGame(CharacterData profile)
        {
            EnsureSession().BeginNewGame(profile, "jiangzuo_hub");
            EnterWorld("jiangzuo_hub");
        }

        public void EnterWorld(string nodeId)
        {
            EnsureSession().SetWorldNode(nodeId);
            SceneManager.LoadScene("WorldScene");
        }

        /// <summary>
        /// 进入据点场景（持久化 settlementId 到 GameSession）。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：TQ-014-DS-05 返工 — 补 ID 持久化
        /// </summary>
        public void EnterSettlement(string settlementId)
        {
            var session = EnsureSession();
            session.SetSettlementId(settlementId);
            session.SetReturnTarget(SceneReturnTarget.World(session.CurrentWorldNodeId));
            SceneManager.LoadScene("SettlementScene");
        }

        /// <summary>
        /// 进入副本/战斗场景（持久化 adventureId 到 GameSession）。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：TQ-014-DS-05 返工 — 补 ID 持久化
        /// </summary>
        public void EnterAdventure(string adventureId, SceneReturnTarget returnTarget)
        {
            var session = EnsureSession();
            session.SetAdventureId(adventureId);
            session.SetReturnTarget(returnTarget);
            SceneManager.LoadScene("AdventureScene");
        }

        public void ReturnToPreviousScene()
        {
            var target = EnsureSession().LastReturnTarget;
            if (target.SceneName == "SettlementScene")
                SceneManager.LoadScene("SettlementScene");
            else
                SceneManager.LoadScene("WorldScene");
        }

        public void ReturnToMainMenu()
        {
            EnsureSession().ClearSession();
            SceneManager.LoadScene("StartMenuScene");
        }

        private static GameSession EnsureSession()
        {
            if (GameSession.Instance != null)
                return GameSession.Instance;

            var go = new GameObject("GameSession");
            return go.AddComponent<GameSession>();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
