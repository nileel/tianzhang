using UnityEngine;
using TianZhang.Entity;
using UnityEngine.InputSystem;

namespace TianZhang.Game
{
    /// <summary>
    /// 游戏入口管理器
    /// 负责：场景加载、全局状态、调试工具
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("调试")]
        public bool showDebugLog = true;
        public bool autoBattle; // 自动战斗模式（AI vs AI）

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (showDebugLog)
                Debug.Log("天章 — 六角格CTB战棋原型启动");
        }

        /// <summary>门派选择后启动游戏（由 SectSelectionManager 调用）</summary>
        public void StartGameWithSect(CharacterData charData)
        {
            Debug.Log($"[GameManager] Starting game with sect: {charData.charName}, 功法: {charData.gongFaName}");
            PlayerCharData = charData;

            // ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：SceneFlowManager兼容桥接
            var flow = SceneFlowManager.Instance;
            if (flow != null)
                flow.StartNewGame(charData);
            else if (GameSession.Instance != null)
                GameSession.Instance.BeginNewGame(charData, SceneFlowManager.ResolveStartNodeId(charData));

            // SectSelectionManager handles player reconfiguration after scene loads
        }

        /// <summary>玩家门派选择后的角色数据</summary>
        [HideInInspector] public CharacterData PlayerCharData;

        private void Update()
        {
            // 全局快捷键
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
    }
}
