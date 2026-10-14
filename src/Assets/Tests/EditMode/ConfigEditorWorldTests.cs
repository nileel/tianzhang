using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Editor.ConfigEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests.EditMode
{
    public sealed class ConfigEditorWorldTests
    {
        private static readonly string[] WorldIds = { "Settlements", "Bounties", "Adventures", "EnvironmentProfiles" };
        private static ConfigTableDefinition Definition(string id) => ConfigTableRegistry.Discover().Tables.Single(t => t.TableId == id);
        private static byte[] Source(string id) => File.ReadAllBytes(Path.Combine(Application.dataPath, "DataConfig/" + id + ".csv"));
        private static ConfigDraftSession Session(string id = null, string column = null, string token = null)
        {
            var definitions = ConfigTableRegistry.Discover().Tables.Where(t => WorldIds.Contains(t.TableId) || new[] { "Language", "Items", "Enemies" }.Contains(t.TableId)).ToArray();
            var documents = definitions.ToDictionary(t => t.TableId, t => Source(t.TableId));
            if (column != null)
            {
                var definition = definitions.Single(t => t.TableId == id);
                var document = CsvConfigDocument.Load(documents[id]);
                var row = document.GetRows(definition).First();
                documents[id] = document.EncodeEdits(new[] { new ConfigCellEdit(row.PhysicalLine, definition.ExactColumns.ToList().IndexOf(column), token) });
            }
            return new ConfigDraftSession(definitions, documents);
        }
        private static string Key(ConfigDraftSession session, string id) => session.Snapshot().Tables[id].Rows.Keys.First();
        private static ConfigValue Row(ConfigDraftSession session, string id) => session.Snapshot().Tables[id].Rows[Key(session, id)];
        private static IReadOnlyList<ConfigIssue> Validate(ConfigDraftSession session, string id) => session.Snapshot().Tables[id].Definition.Codec.ValidateDraft(session.Snapshot());

        [Test]
        public void ProductionColumnsChineseControlsAndUneditedBytesAreComplete()
        {
            var session = Session();
            foreach (var id in WorldIds)
            {
                var definition = Definition(id);
                Assert.IsTrue(definition.IsAdapted, id);
                CollectionAssert.AreEqual(CsvConfigDocument.Load(Source(id)).GetRows(definition).First().Cells.Keys, definition.ExactColumns);
                foreach (var field in definition.Fields) CheckField(field);
                CollectionAssert.AreEqual(Source(id), session.EncodeCandidate(id), id);
                Assert.IsEmpty(Validate(session, id), id);
                foreach (var row in session.Snapshot().Tables[id].SourceRows.Values)
                {
                    var decoded = definition.Codec.DecodeRow(row);
                    var encoded = definition.Codec.EncodeRow(decoded);
                    var bytes = Encoding.UTF8.GetBytes(string.Join(",", definition.ExactColumns) + "\n" + string.Join(",", definition.ExactColumns.Select(c => encoded[c])) + "\n");
                    var second = definition.Codec.DecodeRow(CsvConfigDocument.Load(bytes).GetRows(definition).Single());
                    // Canonical collection order may differ, but a second encoding must be stable.
                    CollectionAssert.AreEquivalent(encoded, definition.Codec.EncodeRow(second));
                }
            }
            Assert.AreEqual(ConfigHeaderMode.FirstPhysicalLine, Definition("Adventures").HeaderMode);
            void CheckField(ConfigFieldDefinition field)
            {
                Assert.That(field.ChineseName.Any(c => c >= '\u4e00' && c <= '\u9fff'), field.Column);
                if (!field.Editable) Assert.IsNotEmpty(field.ReadOnlyReason, field.Column);
                if (field.Control == ConfigControl.Group) foreach (var child in field.Children) CheckField(child);
                if (field.Control == ConfigControl.List) { Assert.IsNotNull(field.Element); CheckField(field.Element); }
                if (field.Control == ConfigControl.Enum) foreach (var choice in field.Choices()) Assert.That(choice.ChineseName.Any(c => c >= '\u4e00' && c <= '\u9fff'));
            }
        }

        [Test]
        public void ApprovedAdditionalEnemySurvivesAdventureRoundTripAndCannotBeEdited()
        {
            var session = Session();
            var table = session.Snapshot().Tables["Adventures"];
            var row = table.Rows.Single(entry => entry.Key.EndsWith("/shijiahou_encounter", StringComparison.Ordinal));
            Assert.AreEqual("enemy_2@4:5", row.Value.At("additionalEnemies").RawToken);
            Assert.Throws<InvalidOperationException>(() => session.SetValue("Adventures", row.Key,
                "additionalEnemies", ConfigValue.Scalar("enemy_3@5:5")));
            CollectionAssert.AreEqual(Source("Adventures"), session.EncodeCandidate("Adventures"));
            Assert.IsEmpty(Validate(session, "Adventures"));
        }

        [Test]
        public void FixedTablesAndStableKeysRejectScalarAndNestedEdits()
        {
            var session = Session();
            foreach (var id in new[] { "Settlements", "Bounties" })
                foreach (var field in Definition(id).Fields)
                    Assert.Throws<InvalidOperationException>(() => session.SetValue(id, Key(session, id), field.Column, Row(session, id).At(field.Column)), field.Column);
            Assert.Throws<InvalidOperationException>(() => session.SetValue("Settlements", Key(session, "Settlements"), "features/0/availability", ConfigValue.Scalar("disabled")));
            Assert.Throws<InvalidOperationException>(() => session.SetValue("Bounties", Key(session, "Bounties"), "rewardEntries/0/quantity", ConfigValue.Scalar("4")));
            Assert.Throws<InvalidOperationException>(() => session.SetValue("EnvironmentProfiles", Key(session, "EnvironmentProfiles"), "elementRelationRefs/0", ConfigValue.Scalar("element_fire")));
            foreach (var column in new[] { "adventureId", "nodeId" })
                Assert.Throws<InvalidOperationException>(() => session.SetValue("Adventures", Key(session, "Adventures"), column, ConfigValue.Scalar("changed")));
            Assert.IsFalse(session.IsDirty);
        }

        [Test]
        public void AdventureGroupFieldsHaveOneOwnerMirrorOnlyTheirMapAndReturnToBaseline()
        {
            var session = Session();
            var table = session.Snapshot().Tables["Adventures"];
            var head = Key(session, "Adventures");
            var other = table.Rows.Keys.Last();
            Assert.Throws<InvalidOperationException>(() => session.SetValue("Adventures", other, "contentScope", ConfigValue.Scalar("scope_changed")));
            var codec = table.Definition.Codec;
            var baseline = Row(session, "Adventures");
            session.SetValue("Adventures", head, "contentScope", ConfigValue.Scalar("scope_changed"));
            session.SetValue("Adventures", head, "displayNameKey", ConfigValue.Scalar("settlement_guanzhong_city"));
            Assert.That(Validate(session, "Adventures").Any(i => i.ErrorCode == "adventure_group_mismatch"));
            var edits = codec.GetLinkedEdits(session.Snapshot());
            Assert.AreEqual(4, edits.Count);
            session.ApplyLinkedEdits(edits);
            Assert.IsEmpty(Validate(session, "Adventures"));
            Assert.IsEmpty(codec.GetLinkedEdits(session.Snapshot()));
            session.SetValue("Adventures", head, "contentScope", baseline.At("contentScope"));
            session.SetValue("Adventures", head, "displayNameKey", baseline.At("displayNameKey"));
            session.ApplyLinkedEdits(codec.GetLinkedEdits(session.Snapshot()));
            Assert.IsFalse(session.IsDirty);
            var invalid = Session("Adventures", "displayNameKey", "settlement_guanzhong_city");
            Assert.IsEmpty(codec.GetLinkedEdits(invalid.Snapshot()), "未编辑的非法分组不自动修复");
            Assert.IsNotEmpty(Validate(invalid, "Adventures"));
        }

        [Test]
        public void AdventureCoordinatesTypesAndFormalContentAreValidatedBeforeDiskWrites()
        {
            var session = Session();
            var key = Key(session, "Adventures");
            session.SetValue("Adventures", key, "q", ConfigValue.Scalar("-2"));
            session.SetValue("Adventures", key, "r", ConfigValue.Scalar("0"));
            Assert.IsEmpty(Validate(session, "Adventures"));
            var decoded = Definition("Adventures").Codec.DecodeRow(CsvConfigDocument.Load(session.EncodeCandidate("Adventures")).GetRows(Definition("Adventures")).First());
            Assert.AreEqual("-2", decoded.At("q").RawToken);
            Assert.Throws<OverflowException>(() => session.SetValue("Adventures", key, "q", ConfigValue.Scalar("2147483648")));
            Assert.Throws<ArgumentException>(() => session.SetValue("Adventures", key, "nodeTypeId", ConfigValue.Scalar("unknown")));
            session.UndoDraft();
            session.SetValue("Adventures", key, "r", ConfigValue.Scalar("2"));
            Assert.That(Validate(session, "Adventures").Any(i => i.ErrorCode == "adventure_duplicate_coordinate" && i.RowKey == "guanzhong_wild/return" && i.FieldPath == "q"));
            session.UndoDraft();
            session.SetValue("Adventures", key, "nodeTypeId", ConfigValue.Scalar("adventure_node_encounter"));
            Assert.That(Validate(session, "Adventures").Any(i => i.ErrorCode == "adventure_encounter_enemy_unresolved"));
            Assert.Throws<ArgumentException>(() => session.SetValue("Adventures", key, "contentId", ConfigValue.Scalar("enemy_yanweihu")), "旧测试敌人不能进入正式选择器");
            session.SetValue("Adventures", key, "contentId", ConfigValue.Scalar("enemy_shijiahou"));
            Assert.IsEmpty(Validate(session, "Adventures"));
            session.SetValue("Adventures", key, "nodeTypeId", ConfigValue.Scalar("adventure_node_start"));
            Assert.That(Validate(session, "Adventures").Any(i => i.FieldPath == "contentId"));
            Assert.AreEqual("enemy_shijiahou", Row(session, "Adventures").At("contentId").RawToken, "不静默清空非法内容");
        }

        [TestCase("Settlements", "features", "bounty_board~settlement_feature_bounty_board~enabled~desc_enemy_shijiahou", "features/0/disabledReasonKey")]
        [TestCase("Settlements", "adventureEntranceIds", "guanzhong_wild|guanzhong_wild", "adventureEntranceIds")]
        [TestCase("Bounties", "rewardEntries", "missing_item@3", "rewardEntries/0/itemId")]
        [TestCase("Bounties", "rewardEntries", "item_lingshi_low@100", "rewardEntries/0/quantity")]
        [TestCase("Bounties", "rewardEntries", "item_lingshi_low@0", "rewardEntries/0/quantity")]
        [TestCase("Bounties", "rewardEntries", "item_lingshi_low@3|item_lingshi_low@3", "rewardEntries/1/itemId")]
        [TestCase("Bounties", "rewardEntries", "", "rewardEntries")]
        [TestCase("Adventures", "q", "1.5", "q")]
        [TestCase("Adventures", "contentId", "enemy_shijiahou", "contentId")]
        [TestCase("EnvironmentProfiles", "queryLimits", "unitsPerRange=0;maxQueryRange=16", "queryLimits/unitsPerRange")]
        [TestCase("EnvironmentProfiles", "battlefieldCells", "0:0@0@0|0:0@1@0", "battlefieldCells/1/q")]
        [TestCase("EnvironmentProfiles", "battlefieldCells", "0:0@2@0", "battlefieldCells/0/blocksGroundMove")]
        [TestCase("EnvironmentProfiles", "battlefieldCells", "2147483648:0@0@0", "battlefieldCells/0/q")]
        [TestCase("EnvironmentProfiles", "battlefieldCells", "1:0@0@0|0:0@0@0", "battlefieldCells")]
        [TestCase("EnvironmentProfiles", "surfacePrototypeRefs", "surface_grassland|surface_grassland", "surfacePrototypeRefs")]
        [TestCase("EnvironmentProfiles", "phenomenonChannels", "airflow=wind+wind;visibility=mist+haze;temperature=heat+cold;precipitation=rain+drizzle;suspendedHazard=ash+dust;cloudDischarge=storm+lightning", "phenomenonChannels")]
        [TestCase("EnvironmentProfiles", "phenomenonPairs", "airflow:wind+gust>outside", "phenomenonPairs")]
        [TestCase("EnvironmentProfiles", "phenomenonPairs", "airflow:wind+gust>gust|airflow:gust+wind>wind", "phenomenonPairs")]
        [TestCase("EnvironmentProfiles", "phenomenonPairs", "airflow:wind+wind>wind", "phenomenonPairs")]
        [TestCase("EnvironmentProfiles", "elementRelationRefs", "element_wood|element_fire", "elementRelationRefs")]
        [TestCase("EnvironmentProfiles", "spiritRecoveryEnvironment", "unknown", "spiritRecoveryEnvironment")]
        public void InvalidSourceValuesKeepPreciseChineseLocationsAndDoNotRepairInput(string id, string column, string token, string path)
        {
            var session = Session(id, column, token);
            var issues = Validate(session, id);
            Assert.That(issues.Any(i => i.TableId == id && i.RowKey == Key(session, id) && i.FieldPath == path && i.ChineseReason.Any(c => c >= '\u4e00' && c <= '\u9fff')), string.Join("\n", issues.Select(i => i.FieldPath + ":" + i.ChineseReason)));
            Assert.IsFalse(session.IsDirty);
        }

        [Test]
        public void EnvironmentNestedEditsSortCellsPreserveFlagsChannelsResultsAndUntouchedTokens()
        {
            var session = Session();
            const string id = "EnvironmentProfiles";
            var key = Key(session, id);
            var original = Row(session, id);
            var cells = original.At("battlefieldCells").Items.Reverse().ToArray();
            session.SetValue(id, key, "battlefieldCells", ConfigValue.List(cells));
            session.SetValue(id, key, "battlefieldCells/0/blocksLineOfSight", ConfigValue.Scalar("1"));
            session.SetValue(id, key, "queryLimits/unitsPerRange", ConfigValue.Scalar("3"));
            session.SetValue(id, key, "spiritRecoveryEnvironment", ConfigValue.Scalar("blessed_land"));
            Assert.IsEmpty(Validate(session, id));
            var source = CsvConfigDocument.Load(session.EncodeCandidate(id)).GetRows(Definition(id)).Single();
            Assert.IsTrue(source["battlefieldCells"].StartsWith("0:0@0@0|0:1@0@0", StringComparison.Ordinal));
            Assert.IsTrue(source["battlefieldCells"].EndsWith("5:5@0@1", StringComparison.Ordinal));
            Assert.AreEqual("unitsPerRange=3;maxQueryRange=16", source["queryLimits"]);
            Assert.AreEqual("blessed_land", source["spiritRecoveryEnvironment"]);
            foreach (var column in new[] { "surfacePrototypeRefs", "phenomenonChannels", "phenomenonPairs", "elementRelationRefs" })
                Assert.AreEqual(session.Snapshot().Tables[id].SourceRows[key][column], source[column], column);
            var definition = Definition(id);
            session.SetValue(id, key, "phenomenonChannels/airflow", ConfigValue.List(original.At("phenomenonChannels/airflow").Items.Concat(new[] { ConfigValue.Scalar("breeze") })));
            session.SetValue(id, key, "phenomenonPairs/0/result", ConfigValue.Scalar("breeze"));
            Assert.IsEmpty(Validate(session, id));
            source = CsvConfigDocument.Load(session.EncodeCandidate(id)).GetRows(definition).Single();
            var projected = WorldContentImporter.ParseEnvironmentProfiles(new[] { string.Join(",", definition.ExactColumns), string.Join(",", definition.ExactColumns.Select(c => source[c])) }, id).Single();
            Assert.AreEqual(3, projected.unitsPerRange);
            Assert.That(projected.directedEdges.All(e => e.allowsMovement && e.allowsEffects && e.metricDistanceUnits == 3));
            Assert.That(projected.phenomenonPairs.Any(p => p.resultTypeRef == "breeze"));
            Assert.Throws<ArgumentException>(() => session.SetValue(id, key, "phenomenonPairs/0/result", ConfigValue.Scalar("wind+gust")));
            Assert.Throws<ArgumentException>(() => session.SetValue(id, key, "queryLimits/maxQueryRange", ConfigValue.Scalar("")));
            session.SetValue(id, key, "surfacePrototypeRefs", ConfigValue.List(original.At("surfacePrototypeRefs").Items.Reverse()));
            source = CsvConfigDocument.Load(session.EncodeCandidate(id)).GetRows(definition).Single();
            Assert.AreEqual("surface_loess|surface_grassland", source["surfacePrototypeRefs"], "没有排序约束的列表保留用户顺序");
            session.SetValue(id, key, "surfacePrototypeRefs", ConfigValue.List(original.At("surfacePrototypeRefs").Items.Take(1)));
            Assert.IsEmpty(Validate(session, id));
            var surface = definition.Fields.Single(f => f.Column == "surfacePrototypeRefs");
            session.SetValue(id, key, "surfacePrototypeRefs", ConfigValue.List(Row(session, id).At("surfacePrototypeRefs").Items.Concat(new[] { surface.NewElement() })));
            Assert.IsEmpty(Validate(session, id));
            session.UndoDraft();
            CollectionAssert.AreEqual(Source(id), session.EncodeCandidate(id));
        }

        [Test]
        public void RequiredListsAndMalformedEnvelopesFailWithoutDefaults()
        {
            foreach (var column in new[] { "battlefieldCells", "surfacePrototypeRefs", "phenomenonPairs" })
                Assert.IsNotEmpty(Validate(Session("EnvironmentProfiles", column, ""), "EnvironmentProfiles"), column);
            foreach (var pair in new[] { Tuple.Create("queryLimits", "maxQueryRange=16;unitsPerRange=2"), Tuple.Create("battlefieldCells", "0@0@0"),
                Tuple.Create("phenomenonChannels", "airflow=wind"), Tuple.Create("phenomenonPairs", "airflow:wind+gust") })
                Assert.Throws<InvalidDataException>(() => Session("EnvironmentProfiles", pair.Item1, pair.Item2), pair.Item1);
            Assert.Throws<InvalidDataException>(() => Session("Settlements", "features", "bounty_board~enabled"));
            Assert.Throws<InvalidDataException>(() => Session("Bounties", "rewardEntries", "item_lingshi_low"));
        }

        [Test]
        public void ValidationUsesCandidateReferenceDocumentsAndImportPlansOnlyDescribeExistingOutputs()
        {
            var session = Session();
            var items = session.Snapshot().Tables["Items"];
            session.ApplyLinkedEdits(new[] { new ConfigEdit("Items", items.Rows.Keys.First(), "maxStack", ConfigValue.Scalar("2")) });
            Assert.That(Validate(session, "Bounties").Any(i => i.FieldPath == "rewardEntries/0/quantity"));
            foreach (var id in WorldIds)
            {
                var steps = Definition(id).Codec.GetImportSteps(new[] { id });
                Assert.AreEqual(1, steps.Count, id);
                Assert.AreEqual(id == "Adventures" ? "AdventureContentImporter.Import" : id == "EnvironmentProfiles" ? "WorldContentImporter.ImportEnvironmentProfiles" : "SettlementContentImporter.ImportContentCatalog", steps[0].Method);
                foreach (var path in steps.SelectMany(s => s.InputPaths.Concat(s.ProductPaths)).Distinct())
                    Assert.IsTrue(File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path)), path);
                Assert.IsEmpty(Definition(id).Codec.GetImportSteps(new[] { "Characters" }));
            }
        }

        [UnityTest]
        public IEnumerator RealWindowRendersFixedTablesEditsNestedEnvironmentAndAdventureThenUndoesWithoutIO()
        {
            var root = Directory.GetParent(Application.dataPath).FullName;
            var paths = WorldIds.Select(id => "Assets/DataConfig/" + id + ".csv").Concat(new[] {
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset", "Assets/Data/ContentCatalog/ContentCatalog.asset",
                "Assets/Data/Adventures/AdventureMap_guanzhong_wild.asset", "Assets/Data/Settlements/Settlement_guanzhong_city.asset", "Assets/Data/Bounties/Bounty_bounty_guanzhong_shijiahou.asset" }).ToArray();
            var hashes = paths.Concat(paths.Select(p => p + ".meta")).ToDictionary(p => p, p => CsvConfigDocument.ComputeHash(File.ReadAllBytes(Path.Combine(root, p))));
            var assets = paths.Where(p => p.EndsWith(".asset", StringComparison.Ordinal)).Select(AssetDatabase.LoadMainAssetAtPath).ToArray();
            var dirty = assets.Select(EditorUtility.IsDirty).ToArray();
            var values = assets.Select(EditorJsonUtility.ToJson).ToArray();
            var window = ConfigEditorWindow.Open();
            try
            {
                Assert.IsNotNull(window.Session);
                foreach (var id in WorldIds)
                {
                    window.SelectTable(id);
                    if (id == "EnvironmentProfiles") window.SetSelectedValue("battlefieldCells/0/blocksLineOfSight", ConfigValue.Scalar("1"));
                    else if (id == "Adventures") window.SetSelectedValue("q", ConfigValue.Scalar("-1"));
                    else Assert.Throws<InvalidOperationException>(() => window.SetSelectedValue(id == "Settlements" ? "features/0/featureId" : "rewardEntries/0/quantity", ConfigValue.Scalar("changed")));
                    window.Repaint();
                    yield return null;
                    window.SendEvent(new Event { type = EventType.Layout });
                    window.SendEvent(new Event { type = EventType.Repaint });
                    Assert.IsFalse(window.SaveEnabled, "本组不能提供保存或导入服务");
                    Assert.IsEmpty(Validate(window.Session, id));
                    window.UndoCurrentDraft();
                    Assert.IsFalse(window.hasUnsavedChanges);
                    CollectionAssert.AreEqual(Source(id), window.Session.EncodeCandidate(id));
                }
                var emptyDefinition = Definition("EnvironmentProfiles");
                var emptyRegistry = new ConfigTableRegistry(new[] { emptyDefinition });
                window.Initialize(emptyRegistry, new ConfigDraftSession(new[] { emptyDefinition }, new Dictionary<string, byte[]> {
                    { "EnvironmentProfiles", Encoding.UTF8.GetBytes(string.Join(",", emptyDefinition.ExactColumns) + "\n") } }));
                window.SendEvent(new Event { type = EventType.Layout });
                window.SendEvent(new Event { type = EventType.Repaint });
                Assert.IsEmpty(window.SelectedRowKey);
                Assert.IsEmpty(window.Session.Snapshot().Tables["EnvironmentProfiles"].Rows);
                Assert.IsFalse(window.hasUnsavedChanges);
            }
            finally { window.DiscardChanges(); window.Close(); }
            foreach (var path in hashes.Keys) Assert.AreEqual(hashes[path], CsvConfigDocument.ComputeHash(File.ReadAllBytes(Path.Combine(root, path))), path);
            CollectionAssert.AreEqual(dirty, assets.Select(EditorUtility.IsDirty));
            CollectionAssert.AreEqual(values, assets.Select(EditorJsonUtility.ToJson));
        }
    }
}
