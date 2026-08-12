using System;
using System.Collections.Generic;
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
                "adventureId,displayNameKey,contentScope,nodeId,nodeTypeId,q,r,contentId",
                "guanzhong_wild,adventure_guanzhong_wild,content_scope_production,start,adventure_node_start,0,0,",
                "guanzhong_wild,adventure_guanzhong_wild,content_scope_production,resource_a,adventure_node_resource,1,0,item_lingshi_low",
                "guanzhong_wild,adventure_guanzhong_wild,content_scope_production,return,adventure_node_return,0,1,",
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
                "adventureId,displayNameKey,contentScope,nodeId,nodeTypeId,q,r,contentId",
                "map_a,map_a_name,content_scope_production,start,adventure_node_start,0,0,",
            };
            Assert.Throws<InvalidOperationException>(() => AdventureContentImporter.Parse(new[]
            {
                header[0], header[1],
                "map_a,map_a_name,content_scope_production,start,adventure_node_return,0,1,",
            }, "duplicate-id"));
            Assert.Throws<InvalidOperationException>(() => AdventureContentImporter.Parse(new[]
            {
                header[0], header[1],
                "map_a,map_a_name,content_scope_production,return,adventure_node_return,0,0,",
            }, "duplicate-coordinate"));
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
            Assert.AreEqual(3, map.nodes.Length);
        }
    }
}
