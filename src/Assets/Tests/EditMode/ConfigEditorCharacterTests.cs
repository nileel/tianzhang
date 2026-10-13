using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Editor.ConfigEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests
{
    public sealed class ConfigEditorCharacterTests
    {
        private static readonly string[] Ids = { "Characters", "Enemies", "Items", "GongFa", "AppearanceProfiles", "Language" };
        private static ConfigTableDefinition Definition(string id) => ConfigTableRegistry.Discover().Tables.Single(t => t.TableId == id);
        private static byte[] Source(string id) => File.ReadAllBytes(Path.Combine(Application.dataPath, "DataConfig/" + id + ".csv"));
        private static ConfigDraftSession Session(string id = null, string column = null, string token = null, string key = null)
        {
            var definitions = Ids.Select(Definition).ToArray();
            var bytes = definitions.ToDictionary(t => t.TableId, t => Source(t.TableId));
            if (id != null)
            {
                var definition = Definition(id); var document = CsvConfigDocument.Load(bytes[id]);
                var row = document.GetRows(definition).First(r => key == null || definition.RowKey(r) == key);
                bytes[id] = document.EncodeEdits(new[] { new ConfigCellEdit(row.PhysicalLine, definition.ExactColumns.ToList().IndexOf(column), token) });
            }
            return new ConfigDraftSession(definitions, bytes);
        }

        [Test]
        public void SixTablesCoverEveryProductionColumnAndLosslesslyRoundTripAllRows()
        {
            var session = Session();
            foreach (var id in Ids)
            {
                var definition = Definition(id);
                Assert.IsTrue(definition.IsAdapted, id);
                var columns = id == "Language" ? new[] { "id", "zh_CN" } : CsvTableReader.FindHeader(File.ReadAllLines(Path.Combine(Application.dataPath, "DataConfig/" + id + ".csv")));
                CollectionAssert.AreEqual(columns, definition.ExactColumns, id);
                foreach (var field in definition.Fields) CheckField(field);
                foreach (var source in session.Snapshot().Tables[id].SourceRows.Values)
                {
                    var encoded = definition.Codec.EncodeRow(definition.Codec.DecodeRow(source));
                    foreach (var column in columns) Assert.AreEqual(source[column], encoded[column], id + "/" + column);
                }
                CollectionAssert.AreEqual(Source(id), session.EncodeCandidate(id));
            }
            var first = session.Snapshot().Tables["Language"].Rows.Keys.First();
            Assert.AreEqual("faction_taiyidaoting", first, "首个实体不是表头");
            Assert.AreEqual(11, Definition("GongFa").Fields.Single(f => f.Column == "growth").Element.Children.Count);
            Assert.AreEqual(11, Definition("GongFa").Fields.Single(f => f.Column == "chapters").Element.Children.Count);
        }

        private static void CheckField(ConfigFieldDefinition field)
        {
            Assert.That(field.ChineseName, Does.Match("[\u4e00-\u9fff]"));
            if (!field.Editable) Assert.IsNotEmpty(field.ReadOnlyReason);
            if (field.Control == ConfigControl.Integer || field.Control == ConfigControl.Decimal) Assert.IsNotEmpty(field.Unit);
            if (field.Control == ConfigControl.Group) foreach (var child in field.Children) CheckField(child);
            if (field.Control == ConfigControl.List) { Assert.IsNotNull(field.Element); CheckField(field.Element); }
            if (field.Control == ConfigControl.Reference || field.Control == ConfigControl.Enum)
            {
                var choices = field.Choices();
                Assert.IsNotEmpty(choices);
                Assert.AreEqual(choices.Count, choices.Select(c => c.Id).Distinct().Count());
                foreach (var choice in choices) Assert.That(choice.ChineseName, Does.Match("[\u4e00-\u9fff]"));
            }
        }

        [Test]
        public void EachEditableFieldIncludingEveryNestedChildRoundTripsWithoutChangingOtherColumns()
        {
            var session = Session();
            foreach (var id in Ids)
            {
                var definition = Definition(id); var table = session.Snapshot().Tables[id];
                var key = table.Rows.Keys.First();
                foreach (var field in definition.Fields)
                {
                    session.UndoDraft(); var before = session.Snapshot().Tables[id].Rows[key];
                    if (!field.IsEditable(before)) continue;
                    var candidates = Edits(field, before.At(field.Column), field.Column).ToArray();
                    foreach (var edit in candidates)
                    {
                        session.UndoDraft(); session.SetValue(id, key, edit.Key, edit.Value);
                        var encoded = CsvConfigDocument.Load(session.EncodeCandidate(id)).GetRows(definition).First(r => definition.RowKey(r) == key);
                        var decoded = definition.Codec.DecodeRow(encoded);
                        Assert.AreEqual(edit.Value, decoded.At(edit.Key), id + "/" + edit.Key);
                        foreach (var other in before.Members.Where(v => v.Key != field.Column)) Assert.AreEqual(other.Value, decoded.At(other.Key));
                    }
                }
                session.UndoDraft(); CollectionAssert.AreEqual(Source(id), session.EncodeCandidate(id));
            }
        }

        private static IEnumerable<KeyValuePair<string, ConfigValue>> Edits(ConfigFieldDefinition field, ConfigValue value, string path)
        {
            if (field.Control == ConfigControl.Group)
                foreach (var child in field.Children)
                    foreach (var edit in Edits(child, value.At(child.Column), path + "/" + child.Column)) yield return edit;
            else if (field.Control == ConfigControl.List)
            {
                for (int i = 0; i < value.Items.Count; i++)
                    foreach (var edit in Edits(field.Element, value.Items[i], path + "/" + i)) yield return edit;
                yield return new KeyValuePair<string, ConfigValue>(path, ConfigValue.List(value.Items.Concat(new[] { field.NewElement() })));
                if (field.AllowEmpty) yield return new KeyValuePair<string, ConfigValue>(path, ConfigValue.List(Array.Empty<ConfigValue>(), "", false));
            }
            else
            {
                string next = field.Control == ConfigControl.Integer ? "17" : field.Control == ConfigControl.Decimal ? "1.25" :
                    field.Control == ConfigControl.Reference || field.Control == ConfigControl.Enum ? field.Choices().Last().Id : "中文测试：/|@，文本";
                yield return new KeyValuePair<string, ConfigValue>(path, ConfigValue.Scalar(next));
            }
        }

        [Test]
        public void FormalAndLegacyDropsRemainSeparateReadOnlyStructures()
        {
            var session = Session(); var table = session.Snapshot().Tables["Enemies"];
            var row = table.Rows["enemy_shijiahou"];
            Assert.AreEqual("30", row.At("dropTable/0/legacyValue").RawToken);
            Assert.AreEqual("100", row.At("dropEntries/0/chance").RawToken);
            Assert.AreEqual("1", row.At("dropEntries/0/quantity").RawToken);
            CollectionAssert.AreEqual(new[] { "itemId", "legacyValue" }, row.At("dropTable/0").Members.Keys);
            CollectionAssert.AreEqual(new[] { "itemId", "chance", "quantity" }, row.At("dropEntries/0").Members.Keys);
            foreach (var path in new[] { "dropTable/0/legacyValue", "dropEntries/0/chance", "description", "realm" })
                Assert.Throws<InvalidOperationException>(() => session.SetValue("Enemies", "enemy_shijiahou", path, ConfigValue.Scalar("20")));
            foreach (var id in new[] { "Items", "AppearanceProfiles" })
                foreach (var field in Definition(id).Fields)
                    Assert.IsFalse(field.IsEditable(session.Snapshot().Tables[id].Rows.Values.First()));
            Assert.Throws<InvalidDataException>(() => Session("Enemies", "dropEntries", "item_lingshi_low:50"));
            Assert.Throws<InvalidDataException>(() => Session("Enemies", "dropTable", "item_lingshi_low@50@1"));
            CollectionAssert.AreEqual(Source("Enemies"), session.EncodeCandidate("Enemies"));
        }

        [TestCase("Characters", "rootBone", "2147483648")]
        [TestCase("Characters", "rootBone", "1.5")]
        [TestCase("Characters", "realmMultiplier", "NaN")]
        [TestCase("Characters", "critRate", "Infinity")]
        [TestCase("Characters", "gongFaName", "unknown_gongfa")]
        [TestCase("GongFa", "starMind", "6")]
        [TestCase("GongFa", "growth", "realm_lianqi:NaN/1/1/1/1/1/1/1/1")]
        [TestCase("GongFa", "growth", "realm_lianqi@10:1/1/1/1/1/1/1/1/1")]
        [TestCase("GongFa", "growth", "realm_lianqi:1/1/1/1/1/1/1/1/1|realm_lianqi:1/1/1/1/1/1/1/1/1")]
        [TestCase("GongFa", "chapters", "chapter_shouyi:realm_lianqi:NaN:0:0:0:0:0:0:0")]
        [TestCase("Language", "zh_CN", " ")]
        public void IllegalValuesProduceLocatedIssuesAndDoNotRewriteSource(string id, string column, string token)
        {
            var session = Session(id, column, token);
            Assert.That(session.ValidateDraft().Any(i => i.TableId == id && i.FieldPath == column && i.RowKey.Length > 0), column);
            Assert.IsFalse(session.IsDirty);
            Assert.IsNotEmpty(Source(id));
        }

        [Test]
        public void GongFaOptionalChaptersStagesAndOriginalGrowthErrorsArePreserved()
        {
            foreach (var token in new[] { "chapter_shouyi:realm_lianqi:0:0:0:0:0:0:0:0", "chapter_shouyi:realm_lianqi:0:0:0:0:0:0:0:0:" })
            {
                var session = Session("GongFa", "chapters", token); var table = session.Snapshot().Tables["GongFa"];
                Assert.AreEqual(token, table.Definition.Codec.EncodeRow(table.Rows.Values.First())["chapters"]);
            }
            foreach (var token in new[] { "chapter_shouyi:realm_lianqi:0", "chapter_shouyi:realm_lianqi:0:0:0:0:0:0:0:0:desc_none:extra" })
                Assert.Throws<InvalidDataException>(() => Session("GongFa", "chapters", token));
            var duplicate = Session("GongFa", "growth", "realm_zhuji@10:1/1/1/1/1/1/1/1/1|realm_zhuji@10:1/1/1/1/1/1/1/1/1");
            Assert.That(duplicate.ValidateDraft().Any(i => i.ErrorCode == "GONGFA_GROWTH_DUPLICATE_KEY"));
            var clear = Session(); clear.SetValue("GongFa", "gongfa_baoyuanshouyi", "chapters", ConfigValue.List(Array.Empty<ConfigValue>(), "", false));
            Assert.IsEmpty(Definition("GongFa").Codec.DecodeRow(CsvConfigDocument.Load(clear.EncodeCandidate("GongFa")).GetRows(Definition("GongFa")).First()).At("chapters").Items);
            clear.UndoDraft(); CollectionAssert.AreEqual(Source("GongFa"), clear.EncodeCandidate("GongFa"));
        }

        [Test]
        public void RealmMirrorsUseCandidateGrowthAndNeverRepairUneditedInvalidInput()
        {
            var session = Session(); var key = "char_taiyixiushi";
            session.SetValue("Characters", key, "realmId", ConfigValue.Scalar("realm_zhuji"));
            var codec = Definition("Characters").Codec; var edits = codec.GetLinkedEdits(session.Snapshot());
            Assert.AreEqual(1, edits.Count); Assert.AreEqual("actionPointMaximumUnits", edits.Single().FieldPath);
            Assert.AreEqual("300000", edits.Single().Value.RawToken);
            session.ApplyLinkedEdits(edits); Assert.IsEmpty(codec.ValidateDraft(session.Snapshot()));
            session.UndoDraft(); CollectionAssert.AreEqual(Source("Characters"), session.EncodeCandidate("Characters"));
            var invalid = Session("Characters", "actionPointMaximumUnits", "1");
            Assert.IsEmpty(codec.GetLinkedEdits(invalid.Snapshot()));
            Assert.That(codec.ValidateDraft(invalid.Snapshot()).Any(i => i.ErrorCode == "realm_growth_checksum"));
        }

        [Test]
        public void LanguageKeepsCommentsBomMixedEndingsCommaAndLockedTextAndDoesNotRenameIds()
        {
            var definition = Definition("Language");
            byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("# 分组\r\nid_one,同名\nid_two,同名\r\n# 尾注释\n")).ToArray();
            var session = new ConfigDraftSession(new[] { definition }, new Dictionary<string, byte[]> { ["Language"] = bytes });
            session.SetValue("Language", "id_one", "zh_CN", ConfigValue.Scalar("新名,含逗号：/|@"));
            var candidate = session.EncodeCandidate("Language");
            Assert.AreEqual("\uFEFF# 分组\r\nid_one,\"新名,含逗号：/|@\"\nid_two,同名\r\n# 尾注释\n", Encoding.UTF8.GetString(candidate));
            CollectionAssert.AreEqual(new[] { "id_one", "id_two" }, CsvConfigDocument.Load(candidate).GetRows(definition).Select(definition.RowKey));
            session.UndoDraft(); CollectionAssert.AreEqual(bytes, session.EncodeCandidate("Language"));
            var production = Session();
            foreach (var key in new[] { "settlement_guanzhong_city", "settlement_feature_bounty_board", "item_lingshi_low_description", "item_shijia_piece_description",
                "bounty_guanzhong_shijiahou_title", "bounty_guanzhong_shijiahou_description", "charter_site_old_water_station" })
                Assert.Throws<InvalidOperationException>(() => production.SetValue("Language", key, "zh_CN", ConfigValue.Scalar("修改")));
        }

        [Test]
        public void ImportDescriptionsCoverLanguageClosureInputsAndProductsWithoutCallingImporters()
        {
            var steps = Definition("Language").Codec.GetImportSteps(new[] { "Language" });
            CollectionAssert.AreEqual(new[] { "CombatContentImporter.ImportAttackProfiles", "CultivationContentImporter.ImportGongFa", "CombatContentImporter.ImportSpells",
                "CombatContentImporter.ImportSkills", "CharacterContentImporter.ImportCharacterDefinitions", "SettlementContentImporter.ImportContentCatalog",
                "SettlementContentImporter.ImportCharterSites", "AdventureContentImporter.Import" }, steps.Select(s => s.Method));
            var root = Directory.GetParent(Application.dataPath).FullName;
            foreach (var step in steps)
            {
                Assert.IsNotEmpty(step.InputPaths); Assert.IsNotEmpty(step.ProductPaths);
                foreach (var path in step.InputPaths.Concat(step.ProductPaths)) Assert.IsTrue(File.Exists(Path.Combine(root, path)), path);
                Assert.IsEmpty(step.ProductPaths.Where(p => p.Contains("*")));
            }
            Assert.AreEqual("SettlementContentImporter.ImportContentCatalog", Definition("Enemies").Codec.GetImportSteps(new[] { "Enemies" }).Single().Method);
            Assert.IsEmpty(Definition("Characters").Codec.GetImportSteps(new[] { "GongFa" }));
            CollectionAssert.IsSubsetOf(new[] { "Assets/Data/Characters/Char_Player.asset", "Assets/Data/Characters/Char_Enemy.asset" },
                steps.Single(s => s.Method == "CharacterContentImporter.ImportCharacterDefinitions").ProductPaths);
        }

        [Test]
        public void ExistingUndeclaredReferencesAreLocatedAndPreservedWithoutAutomaticSubstitution()
        {
            var session = Session(); var issues = session.ValidateDraft();
            Assert.That(issues.Any(i => i.TableId == "Enemies" && i.RowKey == "enemy_xueyingmoxiu" && i.FieldPath == "equippedSpells" && i.ChineseReason.Contains("spell_anshi")));
            Assert.AreEqual("spell_anshi", session.Snapshot().Tables["Enemies"].Rows["enemy_xueyingmoxiu"].At("equippedSpells/0").RawToken);
            foreach (var id in Ids) CollectionAssert.AreEqual(Source(id), session.EncodeCandidate(id));
        }

        [Test]
        public void LegacyChapterCultureMustRoundTripWhileGrowthUsesInvariantCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var growth = Session("GongFa", "growth", "realm_lianqi:0.5/1/1/1/1/1/1/1/1");
                Assert.IsFalse(Definition("GongFa").Codec.ValidateDraft(growth.Snapshot()).Any(i => i.FieldPath == "growth"));
                var chapter = Session("GongFa", "chapters", "chapter_shouyi:realm_lianqi:0.5:0:0:0:0:0:0:0");
                Assert.IsTrue(Definition("GongFa").Codec.ValidateDraft(chapter.Snapshot()).Any(i => i.FieldPath == "chapters"));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [UnityTest]
        public IEnumerator RealWindowRendersAllSixTablesEditsNestedValuesRefreshesReferencesAndUndoes()
        {
            var root = Directory.GetParent(Application.dataPath).FullName;
            var paths = Ids.Select(id => "Assets/DataConfig/" + id + ".csv").Concat(new[] {
                "Assets/Data/Characters/Char_char_taiyixiushi.asset", "Assets/Data/GongFa/GongFa_gongfa_baoyuanshouyi.asset",
                "Assets/Data/ContentCatalog/ContentCatalog.asset", "Assets/Data/AppearanceProfiles/AppearanceProfile_none.asset" }).ToArray();
            var hashes = paths.ToDictionary(p => p, p => CsvConfigDocument.ComputeHash(File.ReadAllBytes(Path.Combine(root, p))));
            var assets = paths.Where(p => p.EndsWith(".asset", StringComparison.Ordinal)).Select(AssetDatabase.LoadMainAssetAtPath).ToArray();
            var values = assets.Select(EditorJsonUtility.ToJson).ToArray(); var dirty = assets.Select(EditorUtility.IsDirty).ToArray();
            var window = ConfigEditorWindow.Open();
            try
            {
                foreach (var id in Ids)
                {
                    window.SelectTable(id); window.Repaint(); yield return null;
                    window.SendEvent(new Event { type = EventType.Layout }); window.SendEvent(new Event { type = EventType.Repaint });
                    Assert.IsNotEmpty(window.SelectedRowKey);
                    if (id == "Characters") window.SetSelectedValue("rootBone", ConfigValue.Scalar("11"));
                    if (id == "Enemies") window.SetSelectedValue("mind", ConfigValue.Scalar("7"));
                    if (id == "GongFa") window.SetSelectedValue("growth/0/hp", ConfigValue.Scalar("4"));
                    if (id == "Language")
                    {
                        window.SelectRow("gongfa_baoyuanshouyi"); window.SetSelectedValue("zh_CN", ConfigValue.Scalar("同名引用"));
                        window.SelectRow("gongfa_qiushuiyouxin"); window.SetSelectedValue("zh_CN", ConfigValue.Scalar("同名引用"));
                        var choices = Definition("Characters").Fields.Single(f => f.Column == "gongFaName").Choices().Where(c => c.ChineseName == "同名引用").ToArray();
                        Assert.AreEqual(2, choices.Length); Assert.AreNotEqual(choices[0].Id, choices[1].Id);
                    }
                    window.Repaint(); yield return null;
                    Assert.AreEqual(id != "Items" && id != "AppearanceProfiles", window.hasUnsavedChanges);
                    Assert.IsFalse(window.SaveEnabled);
                    window.UndoCurrentDraft(); Assert.IsFalse(window.hasUnsavedChanges);
                    CollectionAssert.AreEqual(Source(id), window.Session.EncodeCandidate(id));
                }
            }
            finally { window.DiscardChanges(); window.Close(); }
            foreach (var path in paths) Assert.AreEqual(hashes[path], CsvConfigDocument.ComputeHash(File.ReadAllBytes(Path.Combine(root, path))), path);
            CollectionAssert.AreEqual(values, assets.Select(EditorJsonUtility.ToJson)); CollectionAssert.AreEqual(dirty, assets.Select(EditorUtility.IsDirty));
        }
    }
}
