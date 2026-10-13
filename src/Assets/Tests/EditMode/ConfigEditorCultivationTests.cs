using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Editor.ConfigEditor;
using TianZhang.Entity;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests.EditMode
{
    public sealed class ConfigEditorCultivationTests
    {
        private static readonly string[] Ids = { FoundationTable.Id, JindanTable.Id, NpcPolicyTable.Id };
        private static byte[] Source(string id) => File.ReadAllBytes(Path.Combine(Application.dataPath, "DataConfig", id + ".csv"));
        private static byte[] Fixture(string id) => File.ReadAllBytes(Path.Combine(Application.dataPath, "Tests/EditMode/Fixtures", id + ".fixture.csv"));
        private static ConfigDraftSession Session(ConfigTableDefinition definition, byte[] bytes) => new ConfigDraftSession(new[] { definition }, new Dictionary<string, byte[]> { [definition.TableId] = bytes });
        private static ConfigCsvRow FirstFixture(ConfigTableDefinition definition) => CsvConfigDocument.Load(Fixture(definition.TableId)).GetRows(definition).First();
        private static string Csv(string v) => v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? v : "\"" + v.Replace("\"", "\"\"") + "\"";
        private static byte[] Bytes(ConfigTableDefinition d, params IReadOnlyDictionary<string, string>[] rows) => Encoding.UTF8.GetBytes(string.Join(",", d.ExactColumns) + "\n" + string.Join("\n", rows.Select(r => string.Join(",", d.ExactColumns.Select(c => Csv(r[c]))))) + "\n");
        private static string[] Lines(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        private static void Destroy(IEnumerable<UnityEngine.Object> values) { foreach (var v in values) UnityEngine.Object.DestroyImmediate(v); }
        private static ConfigValue Row(ConfigDraftSession s, string id, string key) => s.Snapshot().Tables[id].Rows[key];

        [Test]
        public void ProductionSchemasChineseControlsAndNoEditBytesAreComplete()
        {
            var registry = ConfigTableRegistry.Discover();
            var session = registry.OpenSession();
            foreach (var id in Ids)
            {
                var d = registry.Tables.Single(t => t.TableId == id);
                Assert.IsTrue(d.IsAdapted, id);
                CollectionAssert.AreEqual(CsvTableReader.FindHeader(Lines(Source(id))), d.ExactColumns, id);
                foreach (var field in d.Fields) Check(field);
                CollectionAssert.AreEqual(Source(id), session.EncodeCandidate(id));
                Assert.IsEmpty(d.Codec.ValidateDraft(session.Snapshot()), id);
            }
            Assert.IsEmpty(session.Snapshot().Tables[FoundationTable.Id].Rows);
            Assert.IsEmpty(session.Snapshot().Tables[JindanTable.Id].Rows);
            void Check(ConfigFieldDefinition f)
            {
                Assert.IsTrue(f.ChineseName.Any(c => c >= '\u4e00' && c <= '\u9fff'), f.Column);
                if (!f.Editable) Assert.IsNotEmpty(f.ReadOnlyReason);
                foreach (var child in f.Children) Check(child);
                if (f.Element != null) Check(f.Element);
            }
        }

        [Test]
        public void AllFrozenFixturesRoundTripWithoutChangingOriginalParserOutcomes()
        {
            var foundation = new FoundationTable(true);
            var fd = foundation.Definition;
            var foundations = CsvConfigDocument.Load(Fixture(FoundationTable.Id)).GetRows(fd).ToArray();
            foreach (var source in foundations)
            {
                var row = foundation.DecodeRow(source); var encoded = foundation.EncodeRow(row);
                CollectionAssert.AreEquivalent(source.Cells, encoded, source["fixtureId"]);
                string before = Outcome(() => CultivationContentImporter.ParseFoundationPurpleMansionStates(Lines(Bytes(fd, source.Cells)), "fixture", true));
                string after = Outcome(() => CultivationContentImporter.ParseFoundationPurpleMansionStates(Lines(Bytes(fd, encoded)), "fixture", true));
                Assert.AreEqual(before, after, source["fixtureId"]);
                Assert.IsEmpty(foundation.ValidateDraft(Session(fd, Bytes(fd, source.Cells)).Snapshot()), source["fixtureId"]);
            }
            var jd = new JindanTable().Definition;
            foreach (var source in CsvConfigDocument.Load(Fixture(JindanTable.Id)).GetRows(jd))
            {
                var f = foundations.Single(r => r["fixtureId"] == source["fixtureId"]);
                var states = CultivationContentImporter.ParseFoundationPurpleMansionStates(Lines(Bytes(fd, f.Cells)), "fixture", true);
                try
                {
                    var catalog = Catalog(states[0]);
                    var codec = new JindanTable(_ => catalog);
                    var encoded = codec.EncodeRow(codec.DecodeRow(source));
                    CollectionAssert.AreEquivalent(source.Cells, encoded, source["fixtureId"]);
                    string before = Outcome(() => CultivationContentImporter.ParseJindanStaticStates(Lines(Bytes(jd, source.Cells)), "fixture", catalog));
                    string after = Outcome(() => CultivationContentImporter.ParseJindanStaticStates(Lines(Bytes(jd, encoded)), "fixture", catalog));
                    Assert.AreEqual(before, after, source["fixtureId"]);
                    // The legacy-column fixture's header mutation belongs to its original parser test.
                    if (source["fixtureId"] != "jd.invalid.legacy-or-display-string")
                        Assert.IsEmpty(codec.ValidateDraft(Session(codec.Definition, Bytes(jd, source.Cells)).Snapshot()), source["fixtureId"]);
                }
                finally { Destroy(states); }
            }
        }
        private static string Outcome(Func<UnityEngine.Object[]> action)
        {
            try { var values = action(); Destroy(values); return "ACCEPT"; }
            catch (InvalidDataException e) { return e.Message.Split(':')[0]; }
        }

        [Test]
        public void DraftReferencesUseCurrentRoleWithoutWindowAndUndoRemovesNewChoices()
        {
            var codec = new FoundationTable(true); var d = codec.Definition;
            var first = FirstFixture(d).Cells.ToDictionary(p => p.Key, p => p.Value);
            first["jindanLock"] = "PRE_JINDAN";
            var second = new Dictionary<string, string>(first); second["characterId"] = "other_character";
            var s = Session(d, Bytes(d, first, second)); var key = first["characterId"];
            var initial = Row(s, FoundationTable.Id, key);
            var newEffect = initial.At("effectBindings/0").With("effectBindingId", ConfigValue.Scalar("new_effect"))
                .With("carrierKind", ConfigValue.Scalar("GUARDIAN_ABILITY")).With("carrierId", ConfigValue.Scalar("guardian_ming"));
            s.SetValue(FoundationTable.Id, key, "effectBindings", ConfigValue.List(initial.At("effectBindings").Items.Concat(new[] { newEffect })));
            s.SetValue(FoundationTable.Id, key, "guardianAbilities/0/effectBindingIds", ConfigValue.List(new[] { ConfigValue.Scalar("new_effect") }));
            Assert.Throws<ArgumentException>(() => s.SetValue(FoundationTable.Id, "other_character", "guardianAbilities/0/effectBindingIds", ConfigValue.List(new[] { ConfigValue.Scalar("new_effect") })));
            Assert.IsEmpty(codec.ValidateDraft(s.Snapshot()));
            s.UndoDraft();
            Assert.Throws<ArgumentException>(() => s.SetValue(FoundationTable.Id, key, "guardianAbilities/0/effectBindingIds", ConfigValue.List(new[] { ConfigValue.Scalar("new_effect") })));
            Assert.IsFalse(s.IsDirty);
        }

        [Test]
        public void EmbryoActionRetreatAndBranchChangesPreserveDataAndFrozenFacts()
        {
            var codec = new FoundationTable(true); var d = codec.Definition;
            var values = FirstFixture(d).Cells.ToDictionary(p => p.Key, p => p.Value);
            var frozen = Session(d, Bytes(d, values)); var key = values["characterId"];
            Assert.Throws<InvalidOperationException>(() => frozen.SetValue(FoundationTable.Id, key, "jindanLock/stageCode", ConfigValue.Scalar("20")));
            values["jindanLock"] = "PRE_JINDAN"; values["stageId"] = "MANSION"; values["stageCode"] = "20";
            values["naturalMansionCapacity"] = "2"; values["currentMansionCarryingCapacity"] = "2"; values["completionMansionCapacity"] = "0";
            values["mansionStates"] = values["mansionStates"].Replace("HUN~NOT_BUILT", "HUN~EMBRYO~embryo_hun~fixture_spell_hun~fixture_upgrade_hun~20~progress_hun~action_hun");
            values["cultivationActionState"] = "action_hun~MANSION_EMBRYO_NURTURE~PAUSED~embryo_hun~fixture_cycle~fixture_boundary~none~progress_hun~fixture_numeric";
            values["closedRetreatPlan"] = "action_hun~embryo_hun~MANUAL_PAUSE";
            values["fixtureOnlyNumericProfile"] = "fixture_phase_boundaries~fixture_carrying~2";
            var s = Session(d, Bytes(d, values));
            Assert.IsEmpty(codec.ValidateDraft(s.Snapshot()));
            s.SetValue(FoundationTable.Id, key, "mansionStates/1/continuousProgress", ConfigValue.Scalar("0"));
            s.SetValue(FoundationTable.Id, key, "closedRetreatPlan/0/stopConditions", ConfigValue.List(new[] { ConfigValue.Scalar("MANUAL_PAUSE"), ConfigValue.Scalar("TARGET_COMPLETED") }));
            Assert.IsEmpty(codec.ValidateDraft(s.Snapshot()));
            var edited = CsvConfigDocument.Load(s.EncodeCandidate(FoundationTable.Id)).GetRows(d).Single();
            Assert.That(edited["mansionStates"], Does.Contain("~0~progress_hun~action_hun"));
            Assert.That(edited["closedRetreatPlan"], Does.EndWith("MANUAL_PAUSE+TARGET_COMPLETED"));
            s.SetValue(FoundationTable.Id, key, "mansionStates/1/state", ConfigValue.Scalar("NOT_BUILT"));
            Assert.Throws<InvalidDataException>(() => s.EncodeCandidate(FoundationTable.Id), "切换分支不得丢弃府胚字段");
            s.UndoDraft();
            s.SetValue(FoundationTable.Id, key, "closedRetreatPlan/0/targetRef", ConfigValue.Scalar("other_target"));
            Assert.IsNotEmpty(codec.ValidateDraft(s.Snapshot()), "闭关必须与行动目标一致");
            s.UndoDraft(); CollectionAssert.AreEqual(Bytes(d, values), s.EncodeCandidate(FoundationTable.Id));
        }

        [Test]
        public void JindanStructuredEditsRespectCarrierAuthorityVersionAndFrozenInputs()
        {
            var fd = new FoundationTable(true).Definition; var foundation = FirstFixture(fd);
            var states = CultivationContentImporter.ParseFoundationPurpleMansionStates(Lines(Bytes(fd, foundation.Cells)), "fixture", true);
            try
            {
                var codec = new JindanTable(_ => Catalog(states[0])); var d = codec.Definition;
                var source = CsvConfigDocument.Load(Fixture(JindanTable.Id)).GetRows(d).Single(r => r["fixtureId"] == foundation["fixtureId"]);
                var s = Session(d, Bytes(d, source.Cells)); var key = source["characterId"];
                s.SetValue(JindanTable.Id, key, "danxiang/0/danxiangNameKey", ConfigValue.Scalar("new_name_key"));
                Assert.IsEmpty(codec.ValidateDraft(s.Snapshot()));
                Assert.Throws<ArgumentException>(() => s.SetValue(JindanTable.Id, key, "stablePositionBindings/0/primaryCarrierAbilityInstanceId", ConfigValue.Scalar("other_guardian")));
                Assert.Throws<InvalidOperationException>(() => s.SetValue(JindanTable.Id, key, "mansionInputs/0/state", ConfigValue.Scalar("NOT_BUILT")));
                s.SetValue(JindanTable.Id, key, "stablePositionBindings/0/expectedPositionVersion", ConfigValue.Scalar("99"));
                Assert.IsNotEmpty(codec.ValidateDraft(s.Snapshot()));
                s.UndoDraft(); CollectionAssert.AreEqual(Bytes(d, source.Cells), s.EncodeCandidate(JindanTable.Id));
            }
            finally { Destroy(states); }
        }

        [Test]
        public void NpcEditsHashAllManifestsAndPreservesOrderSparseColumnsAndOriginalValidation()
        {
            var codec = new NpcPolicyTable(); var d = codec.Definition; var s = Session(d, Source(NpcPolicyTable.Id));
            Assert.IsEmpty(codec.GetLinkedEdits(s.Snapshot()));
            var action = s.Snapshot().Tables[NpcPolicyTable.Id].Rows.First(p => p.Value.At("recordKind").RawToken == "ACTION");
            s.SetValue(NpcPolicyTable.Id, action.Key, "baseWeight", ConfigValue.Scalar("91"));
            Assert.IsNotEmpty(codec.ValidateDraft(s.Snapshot()), "旧hash必须失败");
            var edits = codec.GetLinkedEdits(s.Snapshot()); Assert.IsNotEmpty(edits);
            s.ApplyLinkedEdits(edits); Assert.IsEmpty(codec.ValidateDraft(s.Snapshot()));
            Assert.IsEmpty(codec.GetLinkedEdits(s.Snapshot()));
            Destroy(CultivationContentImporter.ParseNpcCultivationActionWeightProfiles(Lines(s.EncodeCandidate(NpcPolicyTable.Id)), "edited"));
            var rows = CsvConfigDocument.Load(s.EncodeCandidate(NpcPolicyTable.Id)).GetRows(d).ToArray();
            CollectionAssert.AreEqual(CsvConfigDocument.Load(Source(NpcPolicyTable.Id)).GetRows(d).Select(d.RowKey), rows.Select(d.RowKey));
            Assert.IsTrue(rows.Where(r => r["recordKind"] != "ACTION").All(r => r["enabled"] == ""));
            Assert.Throws<InvalidOperationException>(() => s.SetValue(NpcPolicyTable.Id, action.Key, "sourceContentHash", ConfigValue.Scalar("bad")));
            var segment = s.Snapshot().Tables[NpcPolicyTable.Id].Rows.First(p => p.Value.At("recordKind").RawToken == "DIMINISHING_POLICY");
            s.SetValue(NpcPolicyTable.Id, segment.Key, "segments/0/bounds/lower", ConfigValue.Scalar("1"));
            s.ApplyLinkedEdits(codec.GetLinkedEdits(s.Snapshot())); Assert.IsNotEmpty(codec.ValidateDraft(s.Snapshot()));
            s.UndoDraft(); CollectionAssert.AreEqual(Source(NpcPolicyTable.Id), s.EncodeCandidate(NpcPolicyTable.Id));
        }

        [Test]
        public void UnknownSchemasRecordKindsAndFixtureProductionLeakageAreRejected()
        {
            var npc = new NpcPolicyTable(); var d = npc.Definition; var doc = CsvConfigDocument.Load(Source(NpcPolicyTable.Id)); var row = doc.GetRows(d).First();
            foreach (var edit in new[] { new ConfigCellEdit(row.PhysicalLine, 1, "99"), new ConfigCellEdit(row.PhysicalLine, 5, "NEW_KIND") })
                Assert.IsNotEmpty(npc.ValidateDraft(Session(d, doc.EncodeEdits(new[] { edit })).Snapshot()));
            var foundation = new FoundationTable();
            var source = FirstFixture(foundation.Definition);
            Assert.IsTrue(foundation.ValidateDraft(Session(foundation.Definition, Bytes(foundation.Definition, source.Cells)).Snapshot()).Any(i => i.ErrorCode == "FPM_FIXTURE_IN_PRODUCTION"));
            var legacy = source.Cells.ToDictionary(p => p.Key, p => p.Value); legacy["schemaVersion"] = "99";
            Assert.IsTrue(new FoundationTable(true).ValidateDraft(Session(foundation.Definition, Bytes(foundation.Definition, legacy)).Snapshot()).Any(i => i.ErrorCode == "FPM_UNKNOWN_SCHEMA"));
        }

        [UnityTest]
        public IEnumerator RealWindowEditsNpcAndDeepFixtureUndoesAndRendersProductionEmptyStatesWithoutIO()
        {
            var files = Ids.Select(id => Path.Combine(Application.dataPath, "DataConfig", id + ".csv")).ToArray();
            var hashes = files.Concat(files.Select(p => p + ".meta")).ToDictionary(p => p, p => CsvConfigDocument.ComputeHash(File.ReadAllBytes(p)));
            var window = ConfigEditorWindow.Open();
            try
            {
                Assert.IsNotNull(window.Session);
                foreach (var id in new[] { FoundationTable.Id, JindanTable.Id })
                { window.SelectTable(id); Assert.AreEqual("", window.SelectedRowKey); window.Repaint(); yield return null; yield return null; window.SendEvent(new Event { type = EventType.Repaint }); }
                window.SelectTable(NpcPolicyTable.Id);
                var action = window.Session.Snapshot().Tables[NpcPolicyTable.Id].Rows.First(p => p.Value.At("recordKind").RawToken == "ACTION");
                window.SelectRow(action.Key); window.SetSelectedValue("baseWeight", ConfigValue.Scalar("91"));
                window.Repaint(); yield return null; yield return null;
                window.SendEvent(new Event { type = EventType.Repaint });
                Assert.IsTrue(window.Session.IsDirty); Assert.IsFalse(window.SaveEnabled);
                window.UndoCurrentDraft(); Assert.IsFalse(window.Session.IsDirty);
                var codec = new FoundationTable(true); var d = codec.Definition; var values = FirstFixture(d).Cells.ToDictionary(p => p.Key, p => p.Value); values["jindanLock"] = "PRE_JINDAN";
                var session = Session(d, Bytes(d, values)); window.Initialize(new ConfigTableRegistry(new[] { d }), session);
                window.SetSelectedValue("effectBindings/0/parameters/0/value", ConfigValue.Scalar("other_numeric"));
                window.Repaint(); yield return null; yield return null;
                window.SendEvent(new Event { type = EventType.Repaint });
                Assert.AreEqual("other_numeric", Row(session, FoundationTable.Id, window.SelectedRowKey).At("effectBindings/0/parameters/0/value").RawToken);
                window.UndoCurrentDraft(); Assert.IsFalse(session.IsDirty);
                foreach (var pair in hashes) Assert.AreEqual(pair.Value, CsvConfigDocument.ComputeHash(File.ReadAllBytes(pair.Key)), pair.Key);
                LogAssert.NoUnexpectedReceived();
            }
            finally { window.UndoCurrentDraft(); window.Close(); }
        }

        private static JindanStaticReferenceCatalog Catalog(FoundationPurpleMansionStateData foundation)
        {
            var ids = new[] { "source", "transformation", "domain" };
            var guardians = new[] { "guardian_ming", "guardian_hun", "guardian_shi" };
            return new JindanStaticReferenceCatalog {
                foundationPurpleMansionStates = new[] { foundation },
                roads = ids.Select(id => new JindanRoadReference { roadId = "road_" + id, baseEffectCandidateIds = new[] { "effect_" + id, "effect_" + id + "_two", "effect_" + id + "_three" } }).ToArray(),
                positions = ids.Select((id, i) => new JindanPositionReference { positionId = "position_" + id, version = 1, roadId = "road_" + id,
                    positionType = (JindanStaticPositionType)i, proofProfileId = "proof_" + id }).ToArray(),
                compatibilityProfiles = ids.Select((id, i) => new JindanCompatibilityReference { compatibilityProfileId = "compat_" + id, roadId = "road_" + id,
                    positionId = "position_" + id, equippedBaseEffectId = "effect_" + id, primaryCarrierAbilityInstanceId = guardians[i], auxiliaryCarrierAbilityInstanceIds = Array.Empty<string>() }).ToArray(),
                danxingDefinitionIds = new[] { "fixture_danxing" }, danxiangPresentationProfileIds = new[] { "profile_danxiang" },
                ledgerReferences = foundation.mansionStates.Where(m => m.state == PurpleMansionBuildState.Complete).Select(m => "ledger_" + m.mansionKind.ToString().ToLowerInvariant() + "_resource").Concat(new[] { "conflict_ming" }).ToArray(),
                conflictCostProfileIds = new[] { "conflict_cost_ming" } };
        }
    }
}
