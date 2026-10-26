using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Editor.ConfigEditor;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Tests.EditMode
{
    public sealed class D6FlightContentTests
    {
        [Test]
        public void EnvironmentEditorPreservesAllFlightAndSceneObjectSourceTokens()
        {
            var provider = new EnvironmentTable(); var definition = provider.GetTables().Single();
            var bytes = File.ReadAllBytes(Path.Combine(Application.dataPath, "DataConfig/EnvironmentProfiles.csv"));
            var session = new ConfigDraftSession(new[] { definition }, new System.Collections.Generic.Dictionary<string, byte[]> { { definition.TableId, bytes } });
            Assert.IsTrue(definition.IsAdapted); Assert.AreEqual(16, definition.ExactColumns.Count);
            Assert.IsEmpty(provider.ValidateDraft(session.Snapshot()).Select(issue => issue.ChineseReason));
            CollectionAssert.AreEqual(bytes, session.EncodeCandidate(definition.TableId));
            session.SetValue(definition.TableId, "env_guanzhong_jindan_demo", "spiritRecoveryEnvironment", ConfigValue.Scalar("blessed_land"));
            var updated = CsvConfigDocument.Load(session.EncodeCandidate(definition.TableId)).GetRows(definition);
            var original = CsvConfigDocument.Load(bytes).GetRows(definition);
            foreach (string column in new[] { "flightSpace", "sceneObjects", "battlefieldCells", "directedEdgeOverrides" })
                CollectionAssert.AreEqual(original.Select(row => row[column]), updated.Select(row => row[column]), column);
        }

        [Test]
        public void ImportedFlightRequiresCompleteCellAndDirectedEdgeCoverage()
        {
            var lines = File.ReadAllLines(Path.Combine(Application.dataPath, "DataConfig/EnvironmentProfiles.csv"));
            var profiles = WorldContentImporter.ParseEnvironmentProfiles(lines, "production");
            var demo = profiles.Single(p => p.profileId == "env_guanzhong_jindan_demo");
            Assert.AreEqual(144, demo.flightSpace.cells.Length); Assert.AreEqual(760, demo.flightSpace.edges.Length);
            Assert.IsTrue(demo.flightSpace.TryValidate(demo.battlefieldCells, demo.directedEdges, out _));
            demo.flightSpace.edges[0] = demo.flightSpace.edges[1];
            Assert.IsFalse(demo.flightSpace.TryValidate(demo.battlefieldCells, demo.directedEdges, out _));
            Assert.IsNull(profiles.Single(p => p.profileId == "env_guanzhong_wild").flightSpace);
        }

        [Test]
        public void RehearsalCommandCatalogRejectsMissingFlightActionEvenWhenIdsAreUnique()
        {
            var catalog = JindanDemoContentImporter.ReadPreview();
            try
            {
                var action = catalog.commands.Single(c => c.kind == "FLIGHT_ASCEND"); action.kind = "FLIGHT_DESCEND";
                Assert.IsFalse(JindanDemoCatalogValidator.Validate(catalog, false, out var reason));
                Assert.AreEqual("JD_DEMO_FLIGHT_COMMAND_SET", reason);
            }
            finally { JindanDemoContentImporter.DestroyPreview(catalog); }
        }
    }
}
