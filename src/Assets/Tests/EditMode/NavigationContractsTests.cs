using NUnit.Framework;
using TianZhang.Gameplay.Contracts;

namespace TianZhang.Tests
{
    public sealed class NavigationContractsTests
    {
        [Test]
        public void ReturnTargetsCarryOnlyStableSceneContext()
        {
            SceneReturnTarget world = SceneReturnTarget.World("jiangzuo_hub");
            SceneReturnTarget settlement = SceneReturnTarget.Settlement("guanzhong_city");

            Assert.That(world.SceneName, Is.EqualTo(GameplaySceneNames.World));
            Assert.That(world.WorldNodeId, Is.EqualTo("jiangzuo_hub"));
            Assert.That(world.SettlementId, Is.Null);
            Assert.That(settlement.SceneName, Is.EqualTo(GameplaySceneNames.Settlement));
            Assert.That(settlement.SettlementId, Is.EqualTo("guanzhong_city"));
            Assert.That(settlement.WorldNodeId, Is.Null);
        }

        [Test]
        public void SnapshotNormalizesOptionalIdsWithoutMutableAliases()
        {
            var snapshot = new NavigationStateSnapshot("jiangzuo_hub", " ", "", default(SceneReturnTarget));

            Assert.That(snapshot.WorldNodeId, Is.EqualTo("jiangzuo_hub"));
            Assert.That(snapshot.SettlementId, Is.Null);
            Assert.That(snapshot.AdventureId, Is.Null);
        }
    }
}
