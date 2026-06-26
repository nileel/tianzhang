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

        public void SetReturnTarget(SceneReturnTarget target)
        {
            LastReturnTarget = target;
        }
    }
}
