using NUnit.Framework;
using TianZhang.Combat;
using UnityEngine;

namespace TianZhang.Tests.EditMode
{
    public class SpellDamageMultiplierTests
    {
        [Test]
        public void SpellDataStoresPhysicalAndSoulDamageMultipliersIndependently()
        {
            var spell = ScriptableObject.CreateInstance<SpellData>();
            try
            {
                spell.physicalDamageMultiplier = 0.8f;
                spell.soulDamageMultiplier = 1.2f;

                Assert.AreEqual(0.8f, spell.physicalDamageMultiplier);
                Assert.AreEqual(1.2f, spell.soulDamageMultiplier);
            }
            finally
            {
                Object.DestroyImmediate(spell);
            }
        }
    }
}
