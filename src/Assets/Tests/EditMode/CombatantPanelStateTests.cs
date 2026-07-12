using NUnit.Framework;
using TianZhang.Entity;

namespace TianZhang.Tests
{
    public class CombatantPanelStateTests
    {
        [Test]
        public void CharacterBuildsPanelStateWithoutExposingUiComponents()
        {
            var character = new Character
            {
                Name = "试炼修士",
                GongFaName = "九霄雷劫录",
                CurrentHP = 72,
                MaxHP = 120,
                CurrentMP = 18,
                MaxMP = 40
            };

            var state = character.BuildCombatantPanelState(0.75f, "雷", "战斗中");

            Assert.AreEqual("试炼修士", state.Name);
            Assert.AreEqual(72, state.CurrentHP);
            Assert.AreEqual(120, state.MaxHP);
            Assert.AreEqual(18, state.CurrentMP);
            Assert.AreEqual(40, state.MaxMP);
            Assert.AreEqual(0.75f, state.CTRatio);
            Assert.AreEqual("雷", state.Element);
            Assert.AreEqual("战斗中", state.Status);
            Assert.IsNotNull(state.Element);
        }
    }
}
