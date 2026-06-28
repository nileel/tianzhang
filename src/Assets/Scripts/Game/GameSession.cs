using UnityEngine;
using TianZhang.Entity;

namespace TianZhang.Game
{
    /// <summary>
    /// 游戏会话存档（跨场景持久单例）
    /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：新增文件
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }

        public CharacterData PlayerProfile { get; private set; }
        public string CurrentWorldNodeId { get; private set; } = "jiangzuo_hub";

        /// <summary>
        /// 当前据点/副本 ID（用于返回流转时传递上下文）。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：新增字段
        /// </summary>
        public string CurrentSettlementId { get; private set; }
        public string CurrentAdventureId { get; private set; }

        public SceneReturnTarget LastReturnTarget { get; private set; }

        private void Awake()
        {
            if (Instance != null)
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
    }
}