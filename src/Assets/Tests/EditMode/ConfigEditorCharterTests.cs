using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Editor.ConfigEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests
{
    public sealed class ConfigEditorCharterTests
    {
        private const string Rules = "CharterRuleDefinitions";
        private const string Sites = "CharterSites";
        private static ConfigTableDefinition Definition(string id) =>
            ConfigTableRegistry.Discover().Tables.Single(t => t.TableId == id);
        private static byte[] Source(string id) => File.ReadAllBytes(Path.Combine(Application.dataPath, "DataConfig/" + id + ".csv"));
        private static ConfigDraftSession Session(string changedTable = null, string column = null, string token = null)
        {
            var definitions = ConfigTableRegistry.Discover().Tables.Where(t => t.TableId == Rules || t.TableId == Sites || t.TableId == "Language").ToArray();
            var bytes = definitions.ToDictionary(t => t.TableId, t => Source(t.TableId));
            if (changedTable != null)
            {
                var definition = definitions.Single(t => t.TableId == changedTable);
                var document = CsvConfigDocument.Load(bytes[changedTable]);
                var row = document.GetRows(definition).Single();
                bytes[changedTable] = document.EncodeEdits(new[]
                    { new ConfigCellEdit(row.PhysicalLine, definition.ExactColumns.ToList().IndexOf(column), token) });
            }
            return new ConfigDraftSession(definitions, bytes);
        }
        private static string Key(ConfigDraftSession session, string table) => session.Snapshot().Tables[table].Rows.Keys.Single();
        private static ConfigValue Row(ConfigDraftSession session, string table) => session.Snapshot().Tables[table].Rows.Values.Single();

        [Test]
        public void ProvidersCoverEveryProductionColumnAndNestedFieldInChinese()
        {
            var session = Session();
            foreach (var id in new[] { Rules, Sites })
            {
                var table = Definition(id);
                Assert.IsTrue(table.IsAdapted);
                CollectionAssert.AreEqual(CsvTableReader.FindHeader(File.ReadAllLines(Path.Combine(Application.dataPath, "DataConfig/" + id + ".csv"))), table.ExactColumns);
                Assert.AreEqual(id == Rules ? 18 : 71, table.Fields.Count);
                foreach (var field in table.Fields) CheckField(field, Row(session, id));
                CollectionAssert.AreEqual(Source(id), session.EncodeCandidate(id));
                var sourceRow = session.Snapshot().Tables[id].SourceRows.Values.Single();
                var encoded = table.Codec.EncodeRow(table.Codec.DecodeRow(sourceRow));
                foreach (var column in table.ExactColumns) Assert.AreEqual(sourceRow[column], encoded[column], column);
            }
            var events = Definition(Rules).Fields.Single(f => f.Column == "worldEventOutputs");
            Assert.AreEqual(ConfigControl.List, events.Control);
            Assert.AreEqual(ConfigControl.Group, events.Element.Control);
            CollectionAssert.AreEqual(new[] { "eventId", "environmentProfileId" }, events.Element.Children.Select(f => f.Column));
            Assert.IsEmpty(session.ValidateDraft());
        }

        private static void CheckField(ConfigFieldDefinition field, ConfigValue row)
        {
            Assert.That(field.ChineseName, Does.Match("[\u4e00-\u9fff]"));
            if (!field.Editable) Assert.IsNotEmpty(field.ReadOnlyReason);
            if (field.Control == ConfigControl.Integer) Assert.IsNotEmpty(field.Unit);
            if (field.Control == ConfigControl.List) CheckField(field.Element, row);
            if (field.Control == ConfigControl.Group) foreach (var child in field.Children) CheckField(child, row);
            if (field.Control == ConfigControl.Reference || field.Control == ConfigControl.Enum)
            {
                var choices = field.Choices();
                Assert.IsNotEmpty(choices);
                Assert.AreEqual(choices.Count, choices.Select(c => c.Id).Distinct().Count());
                foreach (var choice in choices) Assert.That(choice.ChineseName, Does.Match("[\u4e00-\u9fff]"));
            }
        }

        [Test]
        public void EveryEditableScalarAndListRoundTripsWithoutChangingOtherColumns()
        {
            var session = Session();
            foreach (var id in new[] { Rules, Sites })
                foreach (var field in Definition(id).Fields)
                {
                    session.UndoDraft();
                    var row = Row(session, id);
                    if (!field.IsEditable(row)) continue;
                    var before = row.At(field.Column);
                    ConfigValue next;
                    if (before.Kind == ConfigNodeKind.List) next = ConfigValue.List(before.Items.Reverse());
                    else if (field.Control == ConfigControl.Integer)
                        next = ConfigValue.Scalar((int.Parse(before.RawToken, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture));
                    else if (field.Control == ConfigControl.Enum || field.Control == ConfigControl.Reference)
                        next = ConfigValue.Scalar(field.Choices().Last().Id);
                    else next = ConfigValue.Scalar(before.RawToken + "_edited");
                    session.SetValue(id, Key(session, id), field.Column, next);
                    var encoded = CsvConfigDocument.Load(session.EncodeCandidate(id)).GetRows(Definition(id)).Single();
                    var decoded = Definition(id).Codec.DecodeRow(encoded);
                    Assert.AreEqual(next, decoded.At(field.Column), field.Column);
                    foreach (var other in row.Members.Where(v => v.Key != field.Column))
                        Assert.AreEqual(other.Value, decoded.At(other.Key), field.Column + " must not alter " + other.Key);
                }
            session.UndoDraft();
            Assert.IsFalse(session.IsDirty);
        }

        [Test]
        public void AllLockedFieldsRejectEditsAndBooleansKeepTrueFalseProtocol()
        {
            var session = Session();
            foreach (var id in new[] { Rules, Sites })
                foreach (var field in Definition(id).Fields.Where(f => !f.IsEditable(Row(session, id))))
                    Assert.Throws<InvalidOperationException>(() => session.SetValue(id, Key(session, id), field.Column, ConfigValue.Scalar("changed")), field.Column);
            var boolColumns = new[] { "grantIsRevoked", "leftCandidateHasVariableAuthority", "leftCandidateHasLegalTarget",
                "leftCandidateHasActiveContinuousCarrier", "rightCandidateHasVariableAuthority", "rightCandidateHasLegalTarget", "rightCandidateHasActiveContinuousCarrier" };
            foreach (var column in boolColumns)
            {
                var field = Definition(Sites).Fields.Single(f => f.Column == column);
                CollectionAssert.AreEqual(new[] { "true", "false" }, field.Choices().Select(c => c.Id));
                Assert.Throws<ArgumentException>(() => session.SetValue(Sites, Key(session, Sites), column, ConfigValue.Scalar("1")));
                session.SetValue(Sites, Key(session, Sites), column, ConfigValue.Scalar("false"));
                Assert.AreEqual("false", CsvConfigDocument.Load(session.EncodeCandidate(Sites)).GetRows(Definition(Sites)).Single()[column]);
            }
        }

        [Test]
        public void NestedEventsCanBeRemovedAddedAndEditedWithoutDelimiterInput()
        {
            var session = Session();
            var key = Key(session, Rules);
            var original = Row(session, Rules).At("worldEventOutputs");
            session.SetValue(Rules, key, "worldEventOutputs", ConfigValue.List(original.Items.Skip(1)));
            Assert.IsEmpty(session.ValidateDraft());
            var field = Definition(Rules).Fields.Single(f => f.Column == "worldEventOutputs");
            var shortened = Row(session, Rules).At("worldEventOutputs");
            session.SetValue(Rules, key, "worldEventOutputs", ConfigValue.List(shortened.Items.Concat(new[] { field.NewElement() })));
            Assert.IsEmpty(session.ValidateDraft());
            session.SetValue(Rules, key, "worldEventOutputs/0/eventId", original.Items[0].At("eventId"));
            Assert.That(session.ValidateDraft().Any(i => i.ErrorCode == "CHARTER_UNKNOWN_EVENT_REFERENCE"));
            Assert.Throws<ArgumentException>(() => session.SetValue(Rules, key, "worldEventOutputs/0/eventId", ConfigValue.Scalar("a~b")));
            session.UndoDraft();
            CollectionAssert.AreEqual(Source(Rules), session.EncodeCandidate(Rules));
        }

        [Test]
        public void PositiveNegativeAndLeftRightRemainIndependent()
        {
            var session = Session();
            var positive = Row(session, Rules).At("positiveCommit");
            var negative = Row(session, Rules).At("negativeCommit");
            session.SetValue(Rules, Key(session, Rules), "positiveCommit", negative);
            session.SetValue(Rules, Key(session, Rules), "negativeCommit", positive);
            var decoded = Definition(Rules).Codec.DecodeRow(CsvConfigDocument.Load(session.EncodeCandidate(Rules)).GetRows(Definition(Rules)).Single());
            Assert.AreEqual(negative, decoded.At("positiveCommit"));
            Assert.AreEqual(positive, decoded.At("negativeCommit"));
            session.UndoDraft();
            session.SetValue(Sites, Key(session, Sites), "leftCandidatePositionRank", ConfigValue.Scalar("1"));
            Assert.That(session.ValidateDraft().Any(i => i.ErrorCode == "CHARTER_SITE_CONFLICT_NOT_STABLE"));
            session.SetValue(Sites, Key(session, Sites), "charterCandidateId", Row(session, Sites).At("leftCandidateId"));
            Assert.IsEmpty(session.ValidateDraft(), "右侧现在获胜；只改册界侧绑定，不固定赢家。");
            Assert.AreEqual("2", Row(session, Sites).At("rightCandidatePositionRank").RawToken);
        }

        [Test]
        public void MirrorsUseOnlyTheirDeclaredSourcesAndUndoAllTogether()
        {
            var session = Session();
            var baseline = Row(session, Sites);
            var codec = Definition(Sites).Codec;
            session.SetValue(Sites, Key(session, Sites), "sealBeneficiaryId", ConfigValue.Scalar("beneficiary_changed"));
            session.SetValue(Sites, Key(session, Sites), "grantTargetVariableId", ConfigValue.Scalar("wetland_waterline_state"));
            session.SetValue(Sites, Key(session, Sites), "grantTargetId", ConfigValue.Scalar("node_old_water_station_river_wetland"));
            var edits = codec.GetLinkedEdits(session.Snapshot());
            CollectionAssert.AreEquivalent(new[] { "grantBeneficiaryId", "leftCandidateTargetVariableId", "rightCandidateTargetVariableId",
                "leftCandidateTargetId", "rightCandidateTargetId" }, edits.Select(e => e.FieldPath));
            Assert.IsNotEmpty(session.ValidateDraft());
            session.ApplyLinkedEdits(edits);
            Assert.IsEmpty(session.ValidateDraft());
            Assert.IsEmpty(codec.GetLinkedEdits(session.Snapshot()));
            foreach (var source in new[] { "sealBeneficiaryId", "grantTargetVariableId", "grantTargetId" })
                session.SetValue(Sites, Key(session, Sites), source, baseline.At(source));
            Assert.AreEqual(5, codec.GetLinkedEdits(session.Snapshot()).Count);
            session.ApplyLinkedEdits(codec.GetLinkedEdits(session.Snapshot()));
            Assert.IsFalse(session.IsDirty);
            var invalid = Session(Sites, "rightCandidateTargetId", "node_old_water_station_charter");
            Assert.IsEmpty(codec.GetLinkedEdits(invalid.Snapshot()), "未编辑的非法镜像不得自动修复。");
            Assert.That(invalid.ValidateDraft().Any(i => i.ErrorCode == "CHARTER_SITE_CANDIDATE_MISMATCH"));
            session.UndoDraft();
            CollectionAssert.AreEqual(Source(Sites), session.EncodeCandidate(Sites));
        }

        [TestCase("grantEffectiveAtTick", "-1", "CHARTER_SITE_GRANT_INVALID")]
        [TestCase("grantExpiresAtTick", "-1", "CHARTER_SITE_GRANT_INVALID")]
        [TestCase("grantDefinitionVersion", "2", "CHARTER_SITE_GRANT_INVALID")]
        [TestCase("grantIsRevoked", "true", "CHARTER_SITE_GRANT_INVALID")]
        [TestCase("grantRevocationReason", "revoked_reason", "CHARTER_SITE_GRANT_INVALID")]
        [TestCase("grantRevocationReason", "", "CHARTER_EDITOR_FIELD_INVALID")]
        [TestCase("leftCandidatePulseCost", "0", "CHARTER_SITE_CANDIDATE_INVALID")]
        [TestCase("rightCandidateConflictReserve", "-1", "CHARTER_SITE_CANDIDATE_INVALID")]
        [TestCase("leftCandidatePositionRank", "2147483648", "CHARTER_SITE_TABLE_INVALID")]
        [TestCase("rightCandidatePositionRank", "1.5", "CHARTER_SITE_TABLE_INVALID")]
        [TestCase("leftCandidateHasLegalTarget", "1", "CHARTER_SITE_TABLE_INVALID")]
        [TestCase("rightCandidateTargetId", "node_old_water_station_charter", "CHARTER_SITE_CANDIDATE_MISMATCH")]
        [TestCase("operationTiming", "instant", "CHARTER_SITE_TIMING_SEMANTICS_INVALID")]
        [TestCase("cancellationPolicy", "commit_on_cancel", "CHARTER_SITE_TIMING_SEMANTICS_INVALID")]
        [TestCase("grantTargetVariableId", "unknown", "CHARTER_SITE_UNKNOWN_WORLD_VARIABLE_REFERENCE")]
        public void InvalidSiteRowsKeepOriginalDomainErrors(string column, string token, string code)
        {
            var session = Session(Sites, column, token);
            Assert.That(session.ValidateDraft().Any(i => i.ErrorCode == code), code);
            Assert.IsFalse(session.IsDirty, "校验不能修复非法输入。");
        }

        [TestCase("compatiblePhenomena", "rain|rain", "CHARTER_UNKNOWN_PHENOMENON_REFERENCE")]
        [TestCase("requiredNodeTypes", "node_type_charter||node_type_waterworks", "CHARTER_UNKNOWN_NODE_TYPE_REFERENCE")]
        [TestCase("positiveCommit", "unknown", "CHARTER_ATOMIC_COMMIT_INCOMPLETE")]
        [TestCase("negativeCommit", "none", "CHARTER_ATOMIC_COMMIT_INCOMPLETE")]
        [TestCase("currentCoverageSet", "outside", "CHARTER_COVERAGE_OUT_OF_BOUNDARY")]
        [TestCase("worldEventOutputs", "event_suifu_water_redistribution~unknown", "CHARTER_UNKNOWN_ENVIRONMENT_PROFILE_REFERENCE")]
        public void InvalidRuleRowsKeepOriginalDomainErrors(string column, string token, string code)
        {
            var session = Session(Rules, column, token);
            Assert.That(session.ValidateDraft().Any(i => i.ErrorCode == code), code);
            Assert.IsFalse(session.IsDirty);
        }

        [Test]
        public void RevocationNoneZeroAndTimingAreNeverDefaultedOrRepaired()
        {
            var session = Session();
            var key = Key(session, Sites);
            Assert.IsTrue(Row(session, Sites).At("grantEffectiveAtTick").HasValue);
            Assert.AreEqual("0", Row(session, Sites).At("grantEffectiveAtTick").RawToken);
            Assert.Throws<ArgumentException>(() => session.SetValue(Sites, key, "grantEffectiveAtTick", ConfigValue.Scalar("")));
            session.SetValue(Sites, key, "grantEffectiveAtTick", ConfigValue.Scalar("25"));
            session.SetValue(Sites, key, "grantExpiresAtTick", ConfigValue.Scalar("24"));
            Assert.IsNotEmpty(session.ValidateDraft());
            session.SetValue(Sites, key, "grantExpiresAtTick", ConfigValue.Scalar("26"));
            Assert.IsEmpty(session.ValidateDraft());
            session.SetValue(Sites, key, "grantIsRevoked", ConfigValue.Scalar("true"));
            Assert.AreEqual("none", Row(session, Sites).At("grantRevocationReason").RawToken);
            session.SetValue(Sites, key, "grantRevocationReason", ConfigValue.Scalar("reason_revoked"));
            Assert.That(session.ValidateDraft().Any(i => i.ErrorCode == "CHARTER_SITE_CONFLICT_NOT_STABLE"));
            Assert.AreEqual("reason_revoked", CsvConfigDocument.Load(session.EncodeCandidate(Sites)).GetRows(Definition(Sites)).Single()["grantRevocationReason"]);
        }

        [Test]
        public void IncompleteCompositeFailsClosedAndEmptyRulesDoNotUseStaleAssets()
        {
            Assert.Throws<InvalidDataException>(() => Session(Rules, "worldEventOutputs", "event_without_environment"));
            var definitions = new[] { Definition(Rules), Definition(Sites), ConfigTableRegistry.Discover().Tables.Single(t => t.TableId == "Language") };
            var bytes = definitions.ToDictionary(t => t.TableId, t => Source(t.TableId));
            bytes[Rules] = Encoding.UTF8.GetBytes(string.Join(",", Definition(Rules).ExactColumns) + "\n");
            var session = new ConfigDraftSession(definitions, bytes);
            Assert.IsEmpty(session.Snapshot().Tables[Rules].Rows);
            Assert.That(session.ValidateDraft().Any(i => i.ErrorCode == "CHARTER_SITE_UNKNOWN_RULE_ENTRY_REFERENCE"));
        }

        [Test]
        public void ImportPlanDeclaresExactMethodsDependenciesAndAllCurrentCatalogProducts()
        {
            var steps = Definition(Rules).Codec.GetImportSteps(new[] { Rules });
            CollectionAssert.AreEqual(new[] { "WorldContentImporter.ImportCharterRuleDefinitions", "SettlementContentImporter.ImportCharterSites",
                "SettlementContentImporter.ImportContentCatalog" }, steps.Select(s => s.Method));
            Assert.AreEqual(14, steps[2].ProductPaths.Count);
            foreach (var path in steps.SelectMany(s => s.InputPaths.Concat(s.ProductPaths)).Distinct())
                Assert.IsTrue(File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path)), path);
            Assert.AreEqual(1, Definition(Sites).Codec.GetImportSteps(new[] { Sites }).Count);
            Assert.IsEmpty(Definition(Rules).Codec.GetImportSteps(new[] { "Characters" }));
        }

        [UnityTest]
        public IEnumerator RealWindowEditsBothTablesRendersAndUndoesWithoutWritingAssetsOrGameState()
        {
            var paths = new[] { "Assets/DataConfig/CharterRuleDefinitions.csv", "Assets/DataConfig/CharterSites.csv",
                "Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset", "Assets/Data/CharterRuleDefinitions/CharterRuleDefinition_charter_entry_suifu_diji.asset",
                "Assets/Data/CharterSites/CharterSite_charter_site_old_water_station.asset", "Assets/Data/ContentCatalog/ContentCatalog.asset" };
            var root = Directory.GetParent(Application.dataPath).FullName;
            var hashes = paths.ToDictionary(p => p, p => CsvConfigDocument.ComputeHash(File.ReadAllBytes(Path.Combine(root, p))));
            var assets = paths.Where(p => p.EndsWith(".asset", StringComparison.Ordinal)).Select(AssetDatabase.LoadMainAssetAtPath).ToArray();
            var dirty = assets.Select(EditorUtility.IsDirty).ToArray();
            var values = assets.Select(EditorJsonUtility.ToJson).ToArray();
            var count = Resources.FindObjectsOfTypeAll<CharterSiteData>().Length;
            var window = ConfigEditorWindow.Open();
            try
            {
                Assert.IsNotNull(window.Session);
                foreach (var id in new[] { Rules, Sites })
                {
                    window.SelectTable(id);
                    Assert.IsNotEmpty(window.SelectedRowKey);
                    if (id == Rules) window.SetSelectedValue("failurePolicy", ConfigValue.Scalar("SUSPEND"));
                    else window.SetSelectedValue("leftCandidatePositionRank", ConfigValue.Scalar("4"));
                    window.Repaint();
                    yield return null;
                    window.SendEvent(new Event { type = EventType.Layout });
                    window.SendEvent(new Event { type = EventType.Repaint });
                    Assert.IsTrue(window.hasUnsavedChanges);
                    Assert.IsFalse(window.SaveEnabled);
                    Assert.IsEmpty(window.Session.ValidateDraft());
                    window.UndoCurrentDraft();
                    Assert.IsFalse(window.hasUnsavedChanges);
                    CollectionAssert.AreEqual(Source(id), window.Session.EncodeCandidate(id));
                }
            }
            finally { window.DiscardChanges(); window.Close(); }
            foreach (var path in paths) Assert.AreEqual(hashes[path], CsvConfigDocument.ComputeHash(File.ReadAllBytes(Path.Combine(root, path))), path);
            CollectionAssert.AreEqual(dirty, assets.Select(EditorUtility.IsDirty));
            CollectionAssert.AreEqual(values, assets.Select(EditorJsonUtility.ToJson));
            Assert.AreEqual(count, Resources.FindObjectsOfTypeAll<CharterSiteData>().Length, "临时领域投影必须释放。");
        }
    }
}
