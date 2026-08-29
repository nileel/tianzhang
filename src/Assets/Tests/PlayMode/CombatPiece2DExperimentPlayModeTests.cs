using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed class CombatPiece2DExperimentPlayModeTests
    {
        private const string PrefabPath =
            "Assets/Art/Characters/TacticalSprites/FuYuanBattle/FuYuan_BattleAnimationSprite.prefab";

        [UnityTest]
        public IEnumerator ExperimentSceneConsumesOnlyTheReadOnlyLifecycleAndEventContract()
        {
            var experimentRoot = new GameObject("CombatPiece2DExperimentPlayModeInput");
            experimentRoot.SetActive(false);
            BattleAnimationSpriteCombatUnitPresentationAdapter adapter =
                experimentRoot.AddComponent<BattleAnimationSpriteCombatUnitPresentationAdapter>();
            adapter.ConfigureExperimentPrefab(LoadEditorExperimentPrefab());
            experimentRoot.SetActive(true);
            yield return null;

            var player = new CombatUnitPresentationDescriptor(
                "experiment_player", BattleAnimationSpriteCombatUnitPresentationAdapter.ExperimentProfileId,
                CombatUnitDisplayFaction.Player, new CombatUnitPresentationHex(0, 0), 0);
            var enemy = new CombatUnitPresentationDescriptor(
                "experiment_enemy", BattleAnimationSpriteCombatUnitPresentationAdapter.ExperimentProfileId,
                CombatUnitDisplayFaction.Enemy, new CombatUnitPresentationHex(1, -1), 3);
            adapter.Prepare(new[] { player });
            adapter.Spawn(enemy);
            Assert.AreEqual(2, adapter.ActiveCombatantCount);

            CombatUnitPresentationTargetResult target =
                new CombatUnitPresentationTargetResult(enemy.CombatantId, 17, false);
            foreach (CombatUnitPresentationEvent presentationEvent in
                     (CombatUnitPresentationEvent[])Enum.GetValues(typeof(CombatUnitPresentationEvent)))
            for (int direction = 0; direction < BattleAnimationSpritePresentationController.DirectionCount; direction++)
            {
                CombatUnitPresentationHex end = presentationEvent == CombatUnitPresentationEvent.Move
                    ? new CombatUnitPresentationHex(direction - 2, 1)
                    : player.Position;
                var projection = new CombatUnitPresentationEventProjection(
                    player.CombatantId, presentationEvent, player.Position, end, direction,
                    new[] { target });
                adapter.Present(projection);
                Assert.IsTrue(adapter.TryGetController(player.CombatantId,
                    out BattleAnimationSpritePresentationController controller));
                Assert.AreEqual(direction, controller.ActiveDirection);

                if (presentationEvent != CombatUnitPresentationEvent.Idle)
                {
                    controller.Tick(BattleAnimationSpritePresentationController.FrameDuration + 0.01f);
                    controller.Tick(BattleAnimationSpritePresentationController.FrameDuration + 0.01f);
                    controller.Tick(BattleAnimationSpritePresentationController.FrameDuration + 0.01f);
                    yield return null;
                    Assert.IsFalse(controller.IsPresenting);
                }

                if (presentationEvent == CombatUnitPresentationEvent.Move)
                    Assert.Less(Vector3.Distance(BattleAnimationSpriteCombatUnitPresentationAdapter.HexToWorld(end),
                        controller.transform.position), 0.0001f);
                Assert.AreSame(target, projection.TargetResults[0]);
                Assert.AreEqual(17, projection.TargetResults[0].FinalDamage);
            }

            adapter.Remove(player.CombatantId);
            Assert.AreEqual(1, adapter.ActiveCombatantCount);
            adapter.Clear();
            Assert.Zero(adapter.ActiveCombatantCount);
            UnityEngine.Object.Destroy(experimentRoot);
            yield return null;
        }

        private static GameObject LoadEditorExperimentPrefab()
        {
            Type assetDatabaseType = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
            Assert.IsNotNull(assetDatabaseType, "PlayMode validation requires the editor asset database.");
            MethodInfo loadAsset = assetDatabaseType.GetMethod("LoadAssetAtPath", new[] { typeof(string), typeof(Type) });
            Assert.IsNotNull(loadAsset);
            GameObject prefab = loadAsset.Invoke(null, new object[] { PrefabPath, typeof(GameObject) }) as GameObject;
            Assert.IsNotNull(prefab, PrefabPath);
            return prefab;
        }
    }
}
