using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests.EditMode
{
    public sealed class CombatAttackVfxCatalogTests
    {
        private readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();

        [TearDown]
        public void DestroyTemporary()
        {
            foreach (UnityEngine.Object value in temporary)
                if (value != null) UnityEngine.Object.DestroyImmediate(value);
            temporary.Clear();
        }

        [Test]
        public void CatalogCoversTheTwoConfiguredAttacksAndEveryIdExists()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CombatAttackVfxCatalogData>(
                CombatAttackVfxBuilder.CatalogPath);
            Assert.IsNotNull(catalog, "攻击特效目录必须由构建器生成。");
            Assert.IsTrue(catalog.TryValidate(out string reason), reason);

            Assert.IsTrue(catalog.TryGet("basic_iron_sword", out CombatAttackVfxEntry slash));
            Assert.AreEqual(CombatAttackVfxKind.Slash, slash.kind);
            Assert.Greater(slash.durationSeconds, 0f);
            Assert.IsTrue(catalog.TryGet("spell_feixingzhi", out CombatAttackVfxEntry star));
            Assert.AreEqual(CombatAttackVfxKind.Projectile, star.kind);
            Assert.Greater(star.durationSeconds, 0f);

            var known = new HashSet<string>(File.ReadAllLines("Assets/DataConfig/AttackProfiles.csv")
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                .Skip(1)
                .Select(line => line.Split(',')[0].Trim()), StringComparer.Ordinal);
            foreach (CombatAttackVfxEntry entry in catalog.entries)
                Assert.IsTrue(known.Contains(entry.attackProfileId), "未登记的攻击档案：" + entry.attackProfileId);
        }

        [Test]
        public void CatalogRejectsDuplicateEmptyAndOutOfRangeEntries()
        {
            CombatAttackVfxCatalogData duplicate = CreateCatalog(new[]
            {
                new CombatAttackVfxEntry { attackProfileId = "basic_iron_sword", scale = 1f, durationSeconds = 0.3f },
                new CombatAttackVfxEntry { attackProfileId = "basic_iron_sword", scale = 1f, durationSeconds = 0.3f },
            });
            Assert.IsFalse(duplicate.TryValidate(out string duplicateReason));
            StringAssert.Contains("attack_vfx_catalog_entry_invalid", duplicateReason);

            CombatAttackVfxCatalogData emptyId = CreateCatalog(new[]
            {
                new CombatAttackVfxEntry { attackProfileId = " ", scale = 1f, durationSeconds = 0.3f },
            });
            Assert.IsFalse(emptyId.TryValidate(out _));

            CombatAttackVfxCatalogData badScale = CreateCatalog(new[]
            {
                new CombatAttackVfxEntry { attackProfileId = "basic_iron_sword", scale = 0f, durationSeconds = 0.3f },
            });
            Assert.IsFalse(badScale.TryValidate(out _));

            CombatAttackVfxCatalogData empty = CreateCatalog(Array.Empty<CombatAttackVfxEntry>());
            Assert.IsFalse(empty.TryValidate(out _));
            Assert.IsFalse(empty.TryGet("basic_iron_sword", out _));
        }

        private CombatAttackVfxCatalogData CreateCatalog(CombatAttackVfxEntry[] entries)
        {
            CombatAttackVfxCatalogData catalog = ScriptableObject.CreateInstance<CombatAttackVfxCatalogData>();
            catalog.entries = entries;
            temporary.Add(catalog);
            return catalog;
        }
    }
}
