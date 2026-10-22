using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Editor;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class AdventureContentImporterTests
    {
        [Test]
        public void ApprovedRowsProduceOneOrderedExtensibleMap()
        {
            IReadOnlyList<AdventureMapData> maps = AdventureContentImporter.Parse(new[]
            {
                "adventureId,displayNameKey,contentScope,nodeId,nodeTypeId,q,r,contentId,additionalEnemies,enemyFacing",
                "guanzhong_wild,adventure_guanzhong_wild,content_scope_production,start,adventure_node_start,0,0,,,",
                "guanzhong_wild,adventure_guanzhong_wild,content_scope_production,resource_a,adventure_node_resource,1,0,item_lingshi_low,,",
                "guanzhong_wild,adventure_guanzhong_wild,content_scope_production,return,adventure_node_return,0,1,,,",
            }, "fixture");
            try
            {
                Assert.AreEqual(1, maps.Count);
                Assert.AreEqual("guanzhong_wild", maps[0].adventureId);
                Assert.AreEqual("content_scope_production", maps[0].contentScope);
                Assert.AreEqual("adventure_node_start", maps[0].nodes[0].nodeTypeId);
                Assert.AreEqual("adventure_node_resource", maps[0].nodes[1].nodeTypeId);
                Assert.AreEqual("item_lingshi_low", maps[0].nodes[1].contentId);
                Assert.AreEqual("adventure_node_return", maps[0].nodes[2].nodeTypeId);
            }
            finally
            {
                foreach (AdventureMapData map in maps) UnityEngine.Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void DuplicateIdsAndCoordinatesFailBeforeAssetsAreWritten()
        {
            string[] header =
            {
                "adventureId,displayNameKey,contentScope,nodeId,nodeTypeId,q,r,contentId,additionalEnemies,enemyFacing",
                "map_a,map_a_name,content_scope_production,start,adventure_node_start,0,0,,,",
            };
            Assert.Throws<InvalidOperationException>(() => AdventureContentImporter.Parse(new[]
            {
                header[0], header[1],
                "map_a,map_a_name,content_scope_production,start,adventure_node_return,0,1,,,",
            }, "duplicate-id"));
            Assert.Throws<InvalidOperationException>(() => AdventureContentImporter.Parse(new[]
            {
                header[0], header[1],
                "map_a,map_a_name,content_scope_production,return,adventure_node_return,0,0,,,",
            }, "duplicate-coordinate"));
        }

        [TestCase("")]
        [TestCase("-1")]
        [TestCase("6")]
        [TestCase("2147483648")]
        [TestCase("north")]
        public void EncounterRequiresExplicitFacing(string facing)
        {
            const string header = "adventureId,displayNameKey,contentScope,nodeId,nodeTypeId,q,r,contentId,additionalEnemies,enemyFacing";
            Assert.Throws<InvalidOperationException>(() => AdventureContentImporter.Parse(new[] { header,
                "test,name,scope,encounter,adventure_node_encounter,3,5,enemy_shijiahou,," + facing }, "fixture"));
        }

        [Test]
        public void AdventureSchemaAndNonEncounterFacingFailClosed()
        {
            const string header = "adventureId,displayNameKey,contentScope,nodeId,nodeTypeId,q,r,contentId,additionalEnemies,enemyFacing";
            const string row = "test,name,scope,start,adventure_node_start,0,0,,,";
            foreach (string invalidHeader in new[] { header.Replace(",enemyFacing", ""), header + ",unknown", header.Replace("enemyFacing", "q") })
                Assert.Throws<InvalidOperationException>(() => AdventureContentImporter.Parse(new[] { invalidHeader, row }, "fixture"));
            Assert.Throws<InvalidOperationException>(() => AdventureContentImporter.Parse(new[] { header, row + "0" }, "fixture"));
        }

        [Test]
        public void ImportIsIdempotentAndCatalogResolvesCommittedMap()
        {
            AdventureContentImporter.Import();
            const string path = "Assets/Data/Adventures/AdventureMap_guanzhong_wild.asset";
            string guid = AssetDatabase.AssetPathToGUID(path);
            AdventureContentImporter.Import();

            Assert.IsFalse(string.IsNullOrWhiteSpace(guid));
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
            ContentCatalogData catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                AdventureContentImporter.CatalogPath);
            Assert.IsTrue(catalog.TryGetAdventureMap("guanzhong_wild", out AdventureMapData map));
            Assert.AreEqual(4, map.nodes.Length);
            var main = map.nodes.Single(node => node.nodeId == "shijiahou_encounter");
            Assert.AreEqual(9, main.q);
            Assert.AreEqual(8, main.r);
            Assert.AreEqual(2, main.enemyFacing);
            Assert.AreEqual(8, main.additionalEnemies[0].q);
            Assert.AreEqual(8, main.additionalEnemies[0].r);
            Assert.AreEqual(2, main.additionalEnemies[0].facing);
            Assert.IsTrue(map.nodes.Any(node => node.nodeId == "lieshijia_encounter" && node.contentId == "enemy_lieshijia"));
        }
    }
}
