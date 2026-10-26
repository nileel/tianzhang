using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Editor.ConfigEditor;
using UnityEngine;

namespace TianZhang.Tests.EditMode
{
    public sealed class D5ConfigEditorIntegrationTests
    {
        [Test]
        public void ProductionIdentityTablesResolveTogetherAndRejectStalePositionVersions()
        {
            // Exercise only these production adapters; unrelated registry discovery and GUI are not required.
            var foundation = new FoundationTable();
            var jindan = new JindanTable();
            var definitions = new[] { foundation.Definition, jindan.Definition };
            var sources = definitions.ToDictionary(d => d.TableId,
                d => File.ReadAllBytes(Path.Combine(Application.dataPath, "DataConfig", d.TableId + ".csv")));
            var session = new ConfigDraftSession(definitions, sources);
            var snapshot = session.Snapshot();

            foreach (var definition in definitions)
            {
                Assert.That(snapshot.Tables[definition.TableId].Rows.Count, Is.EqualTo(5), definition.TableId);
                Assert.That(snapshot.Tables[definition.TableId].Issues, Is.Empty, definition.TableId);
                Assert.That(definition.Codec.ValidateDraft(snapshot).Select(issue => issue.ErrorCode + ":" + issue.ChineseReason),
                    Is.Empty, definition.TableId);
                CollectionAssert.AreEqual(sources[definition.TableId], session.EncodeCandidate(definition.TableId));
            }

            string[] tableIds = definitions.Select(d => d.TableId).ToArray();
            var foundationStep = foundation.GetImportSteps(tableIds).Single();
            var jindanStep = jindan.GetImportSteps(tableIds).Single();
            Assert.That(foundationStep.Method, Is.EqualTo("JindanDemoContentImporter.Import"));
            Assert.That(jindanStep.Method, Is.EqualTo(foundationStep.Method));
            CollectionAssert.AreEquivalent(foundationStep.InputPaths, jindanStep.InputPaths);
            CollectionAssert.AreEquivalent(foundationStep.ProductPaths, jindanStep.ProductPaths);
            var expectedProducts = new HashSet<string> { JindanDemoContentImporter.CatalogPath };
            foreach (string characterId in snapshot.Tables[FoundationTable.Id].Rows.Keys)
            {
                expectedProducts.Add(JindanDemoContentImporter.FoundationPath(characterId));
                expectedProducts.Add(JindanDemoContentImporter.StaticPath(characterId));
            }
            Assert.That(expectedProducts.Count, Is.EqualTo(11));
            CollectionAssert.AreEquivalent(expectedProducts, foundationStep.ProductPaths);

            session.SetValue(JindanTable.Id, "demo_20261024_earth",
                "stablePositionBindings/0/expectedPositionVersion", ConfigValue.Scalar("99"));
            Assert.That(jindan.ValidateDraft(session.Snapshot()).Any(issue =>
                issue.ErrorCode == "JD_UNKNOWN_STATIC_REFERENCE"), Is.True);
            session.UndoDraft();
            Assert.That(session.IsDirty, Is.False);
            foreach (var definition in definitions)
            {
                Assert.That(definition.Codec.ValidateDraft(session.Snapshot()).Select(issue => issue.ErrorCode + ":" + issue.ChineseReason),
                    Is.Empty, definition.TableId);
                CollectionAssert.AreEqual(sources[definition.TableId], session.EncodeCandidate(definition.TableId));
            }
        }
    }
}
