using UnityEngine;
using TianZhang.Game;

namespace TianZhang.Adventure
{
    public class AdventureSceneController : MonoBehaviour
    {
        private void Start()
        {
            Debug.Log("[AdventureScene] started");
        }

        public void ReturnToSource()
        {
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.ReturnToPreviousScene();
        }

        public void ReturnToWorld()
        {
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterWorld(
                    GameSession.Instance?.CurrentWorldNodeId ?? "jiangzuo_hub");
        }
    }
}
