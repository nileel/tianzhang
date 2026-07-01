using UnityEngine;
using TianZhang.Game;

namespace TianZhang.Adventure
{
    public enum AdventureSceneState
    {
        Loading,
        Exploration,
        Combat,
        Returning,
    }

    public class AdventureSceneController : MonoBehaviour
    {
        public AdventureSceneState CurrentState { get; private set; } = AdventureSceneState.Loading;

        private void Start()
        {
            MarkExplorationReady();
            Debug.Log("[AdventureScene] started");
        }

        public void MarkExplorationReady()
        {
            if (CurrentState != AdventureSceneState.Returning)
                CurrentState = AdventureSceneState.Exploration;
        }

        public void BeginEncounter()
        {
            if (CurrentState != AdventureSceneState.Returning)
                CurrentState = AdventureSceneState.Combat;
        }

        public void CompleteEncounter()
        {
            if (CurrentState == AdventureSceneState.Combat)
                CurrentState = AdventureSceneState.Exploration;
        }

        public void MarkReturning()
        {
            CurrentState = AdventureSceneState.Returning;
        }

        public void ReturnToSource()
        {
            MarkReturning();
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.ReturnToPreviousScene();
        }

        public void ReturnToWorld()
        {
            MarkReturning();
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterWorld(
                    GameSession.Instance?.CurrentWorldNodeId ?? "jiangzuo_hub");
        }
    }
}
