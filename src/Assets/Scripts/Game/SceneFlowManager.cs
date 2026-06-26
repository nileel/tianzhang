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

        public void EnterSettlement(string settlementId)
        {
            EnsureSession().SetReturnTarget(SceneReturnTarget.World(EnsureSession().CurrentWorldNodeId));
            SceneManager.LoadScene("SettlementScene");
        }

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
