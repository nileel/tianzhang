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
        public static SceneFlowManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null)
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
            EnsureSession().SetPlayerProfile(profile);
            EnterWorld("jiangzuo_hub");
        }

        public void EnterWorld(string nodeId)
        {
            EnsureSession().SetWorldNode(nodeId);
            SceneManager.LoadScene("WorldScene");
        }

        /// <summary>
        /// 进入据点场景。settlementId 当前不持久化到 GameSession；
        /// SettlementSceneController 启动时使用自身硬编码定义列表定位据点。
        /// ID 持久化方案延后至 TQ-014 Task 7（开始菜单迁移）统一处理。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：补 ID 传递边界注释
        /// </summary>
        public void EnterSettlement(string settlementId)
        {
            EnsureSession().SetReturnTarget(SceneReturnTarget.World(EnsureSession().CurrentWorldNodeId));
            SceneManager.LoadScene("SettlementScene");
        }

        /// <summary>
        /// 进入副本/战斗场景。adventureId 当前不持久化到 GameSession；
        /// AdventureScene 启动时由 AdventureSceneController 按传入的 returnTarget 恢复上下文。
        /// ID 持久化方案延后至 TQ-014 Task 7（开始菜单迁移）统一处理。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：补 ID 传递边界注释
        /// </summary>
        public void EnterAdventure(string adventureId, SceneReturnTarget returnTarget)
        {
            EnsureSession().SetReturnTarget(returnTarget);
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

        private static GameSession EnsureSession()
        {
            if (GameSession.Instance != null)
                return GameSession.Instance;

            var go = new GameObject("GameSession");
            return go.AddComponent<GameSession>();
        }
    }
}
