using System;

namespace TianZhang.Gameplay.Contracts
{
    public static class GameplaySceneNames
    {
        public const string StartMenu = "StartMenuScene";
        public const string World = "WorldScene";
        public const string Settlement = "SettlementScene";
        public const string Adventure = "AdventureScene";
    }

    [Serializable]
    public struct SceneReturnTarget
    {
        public string SceneName;
        public string WorldNodeId;
        public string SettlementId;

        public static SceneReturnTarget World(string nodeId)
        {
            return new SceneReturnTarget
            {
                SceneName = GameplaySceneNames.World,
                WorldNodeId = nodeId,
            };
        }

        public static SceneReturnTarget Settlement(string settlementId)
        {
            return new SceneReturnTarget
            {
                SceneName = GameplaySceneNames.Settlement,
                SettlementId = settlementId,
            };
        }
    }

    public sealed class NavigationStateSnapshot
    {
        public NavigationStateSnapshot(
            string worldNodeId,
            string settlementId,
            string adventureId,
            SceneReturnTarget returnTarget)
        {
            WorldNodeId = string.IsNullOrWhiteSpace(worldNodeId) ? "jiangzuo_hub" : worldNodeId;
            SettlementId = string.IsNullOrWhiteSpace(settlementId) ? null : settlementId;
            AdventureId = string.IsNullOrWhiteSpace(adventureId) ? null : adventureId;
            ReturnTarget = returnTarget;
        }

        public string WorldNodeId { get; }
        public string SettlementId { get; }
        public string AdventureId { get; }
        public SceneReturnTarget ReturnTarget { get; }
    }

    public interface INavigationUseCase
    {
        NavigationStateSnapshot Navigation { get; }
        string EnterWorld(string nodeId);
        string EnterSettlement(string settlementId);
        string EnterAdventure(string adventureId, SceneReturnTarget returnTarget);
        string ReturnToPreviousScene();
    }
}
