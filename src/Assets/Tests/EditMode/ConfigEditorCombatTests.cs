using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Editor;
using TianZhang.Editor.ConfigEditor;
using TianZhang.Spatial;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests.EditMode
{
    public sealed class ConfigEditorCombatTests
    {
        private static readonly string[] Ids = { "RealmCombatGrowth", "CombatEffectTierRules", "CombatActionEconomyRules", "AttackProfiles", "Spells", "Skills", "CharacterCreationPointBuy" };
        private static ConfigTableDefinition[] Definitions() => new CombatRuleTables().GetTables().Concat(new CombatAbilityTables().GetTables()).Concat(new PointBuyTable().GetTables()).ToArray();
        private static byte[] Source(string id) => File.ReadAllBytes(Path.Combine(Application.dataPath, "DataConfig/" + id + ".csv"));
        private static ConfigDraftSession Session(params ConfigTableDefinition[] definitions) => new ConfigDraftSession(definitions.Length == 0 ? Definitions() : definitions,
            (definitions.Length == 0 ? Definitions() : definitions).ToDictionary(d => d.TableId, d => Source(d.TableId)));
        private static ConfigDraftSession WithCell(string id, string column, string raw, int rowIndex = 0)
        {
            var definitions = Definitions();
            var bytes = definitions.ToDictionary(d => d.TableId, d => Source(d.TableId));
            var d = definitions.Single(v => v.TableId == id);
            var doc = CsvConfigDocument.Load(bytes[id]);
            var row = doc.GetRows(d).ElementAt(rowIndex);
            bytes[id] = doc.EncodeEdits(new[] { new ConfigCellEdit(row.PhysicalLine, Array.IndexOf(d.ExactColumns.ToArray(), column), raw) });
            return new ConfigDraftSession(definitions, bytes);
        }

        [Test]
        public void SevenProductionSchemasHaveCompleteChineseStructuredMetadataAndLosslessRoundTrips()
        {
            var session = Session();
            foreach (var table in session.Snapshot().Tables.Values)
            {
                Assert.That(table.Definition.IsAdapted, Is.True);
                Assert.That(table.Issues, Is.Empty);
                var header = table.Document.Lines.First(l => !l.IsTrivia).Cells.Select(c => c.Value).ToArray();
                CollectionAssert.AreEqual(header, table.Definition.ExactColumns);
                foreach (var f in table.Definition.Fields) AssertMetadata(f);
                foreach (var row in table.Rows)
                    Assert.That(table.Definition.Codec.EncodeRow(row.Value).Keys, Is.EquivalentTo(header));
                CollectionAssert.AreEqual(Source(table.Definition.TableId), session.EncodeCandidate(table.Definition.TableId));
                var issues = table.Definition.Codec.ValidateDraft(session.Snapshot());
                if (table.Definition.TableId == "Skills")
                    Assert.That(issues.All(i => i.FieldPath == "specialEffectDesc" && i.ErrorCode == "combat_editor_reference_unknown"), Is.True, Describe(issues));
                else Assert.That(issues, Is.Empty, Describe(issues));
            }
            Assert.That(ConfigTableRegistry.Discover().Tables.Where(t => Ids.Contains(t.TableId)).All(t => t.IsAdapted), Is.True);
            Assert.That(session.Snapshot().Tables["CombatActionEconomyRules"].Rows.Count, Is.EqualTo(52));
        }
        private static string Describe(IEnumerable<ConfigIssue> issues) => string.Join("\n", issues.Select(i => i.TableId + "/" + i.RowKey + "/" + i.FieldPath + ": " + i.ChineseReason));
        private static void AssertMetadata(ConfigFieldDefinition f)
        {
            Assert.That(f.ChineseName.Any(c => c >= 0x4e00 && c <= 0x9fff), Is.True, f.Column);
            if (f.Control == ConfigControl.Integer || f.Control == ConfigControl.Decimal) Assert.That(f.Unit, Is.Not.Empty, f.Column);
            if (!f.Editable) Assert.That(f.ReadOnlyReason, Is.Not.Empty);
            if (f.Control == ConfigControl.List) { Assert.That(f.Element, Is.Not.Null); AssertMetadata(f.Element); }
            if (f.Control == ConfigControl.Group) foreach (var child in f.Children) AssertMetadata(child);
            if (f.Control == ConfigControl.Enum) foreach (var choice in f.Choices()) Assert.That(choice.ChineseName.Any(c => c >= 0x4e00 && c <= 0x9fff), Is.True, choice.Id);
        }

        [Test]
        public void EachEditableProductionFieldRoundTripsWithoutChangingAnotherCell()
        {
            var session = Session();
            foreach (var table in session.Snapshot().Tables.Values)
                foreach (var row in table.Rows)
                    foreach (var field in table.Definition.Fields.Where(f => f.IsEditable(row.Value)))
                        foreach (var edit in Alternatives(field, row.Value.At(field.Column), field.Column))
                        {
                            session.UndoDraft();
                            session.SetValue(table.Definition.TableId, row.Key, edit.Key, edit.Value);
                            var candidate = CsvConfigDocument.Load(session.EncodeCandidate(table.Definition.TableId));
                            var saved = candidate.GetRows(table.Definition).Single(r => table.Definition.RowKey(r) == row.Key);
                            var decoded = table.Definition.Codec.DecodeRow(saved);
                            Assert.That(decoded.At(edit.Key), Is.EqualTo(edit.Value), table.Definition.TableId + "/" + edit.Key);
                            foreach (var c in table.Definition.ExactColumns.Where(c => c != field.Column))
                                Assert.That(saved[c], Is.EqualTo(table.SourceRows[row.Key][c]), c);
                        }
            session.UndoDraft();
            foreach (var id in Ids) CollectionAssert.AreEqual(Source(id), session.EncodeCandidate(id));
        }
        private static IEnumerable<KeyValuePair<string, ConfigValue>> Alternatives(ConfigFieldDefinition f, ConfigValue value, string path)
        {
            if (f.Control == ConfigControl.Group)
                foreach (var child in f.Children)
                    foreach (var edit in Alternatives(child, value.At(child.Column), path + "/" + child.Column)) yield return edit;
            else if (f.Control == ConfigControl.List)
            {
                for (int i = 0; i < value.Items.Count; i++)
                    foreach (var edit in Alternatives(f.Element, value.Items[i], path + "/" + i)) yield return edit;
                yield return new KeyValuePair<string, ConfigValue>(path, ConfigValue.List(value.Items.Concat(new[] { f.NewElement() })));
            }
            else
            {
                string next = f.Control == ConfigControl.Integer ? "17" : f.Control == ConfigControl.Decimal ? "1.25" : f.Control == ConfigControl.Boolean ? value.RawToken == "1" ? "0" : "1" :
                    f.Control == ConfigControl.Enum || f.Control == ConfigControl.Reference ? f.Choices().Last().Id : "status_example";
                yield return new KeyValuePair<string, ConfigValue>(path, ConfigValue.Scalar(next));
            }
        }

        [Test]
        public void FixedPointPrecisionWeightsFractionsAndOrdinaryTerrainUseExistingRules()
        {
            var session = Session();
            var table = session.Snapshot().Tables["RealmCombatGrowth"];
            var ap = table.Definition.Fields.Single(f => f.Column == "actionPointMaximumUnits");
            Assert.That(ConfigFieldGUI.FromDisplayNumber(ap, "100.001").RawToken, Is.EqualTo("100001"));
            Assert.Throws<ArgumentException>(() => ConfigFieldGUI.FromDisplayNumber(ap, "0.0001"));
            Assert.Throws<ArgumentException>(() => ConfigFieldGUI.FromDisplayNumber(ap, "2147483.648"));
            var efficiency = table.Definition.Fields.Single(f => f.Column == "baseMovementEfficiencyMilliUnits");
            Assert.That(ConfigFieldGUI.FromDisplayNumber(efficiency, "3").RawToken, Is.EqualTo("3000"));
            var cells = new Dictionary<HexCoord, SpatialCellRules> { [new HexCoord(0, 0)] = new SpatialCellRules(0, false, false, false, 0), [new HexCoord(1, 0)] = new SpatialCellRules(0, false, false, false, 0) };
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules> { [new SpatialDirectedEdge(new HexCoord(0, 0), new HexCoord(1, 0))] = new SpatialEdgeRules(1000, true, true) };
            var board = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(1000, 16));
            Assert.That(board.QueryMovementPath(new HexCoord(0, 0), new HexCoord(1, 0), 100000, 3000).ActionPointCostUnits, Is.EqualTo(33333));
            var fraction = WithCell("CombatActionEconomyRules", "movementBudgetNumerator", "2");
            var key = fraction.Snapshot().Tables["CombatActionEconomyRules"].Rows.Keys.First();
            fraction.SetValue("CombatActionEconomyRules", key, "movementBudgetDenominator", ConfigValue.Scalar("8"));
            var fractionIssues = fraction.Snapshot().Tables["CombatActionEconomyRules"].Definition.Codec.ValidateDraft(fraction.Snapshot());
            Assert.That(fractionIssues, Is.Empty, Describe(fractionIssues));
            var saved = CsvConfigDocument.Load(fraction.EncodeCandidate("CombatActionEconomyRules")).GetRows(fraction.Snapshot().Tables["CombatActionEconomyRules"].Definition).First();
            Assert.That(saved["movementBudgetNumerator"], Is.EqualTo("2")); Assert.That(saved["movementBudgetDenominator"], Is.EqualTo("8"));
            var badWeights = WithCell("RealmCombatGrowth", "movementNodeWeightNumerators", "1|1", 1);
            Assert.That(badWeights.Snapshot().Tables["RealmCombatGrowth"].Definition.Codec.ValidateDraft(badWeights.Snapshot()).Any(i => i.ErrorCode == "combat_rule_movement_weights_invalid"), Is.True);
        }

        [TestCase("CombatActionEconomyRules", "actionPointFactorDenominator", "0", 1)]
        [TestCase("CombatActionEconomyRules", "repeatFirstPermille", "-1", 0)]
        [TestCase("CombatActionEconomyRules", "actionPointCostOverrideUnits", "0", 1)]
        [TestCase("CombatActionEconomyRules", "spiritCostOverride", "-1", 1)]
        [TestCase("RealmCombatGrowth", "baseMovementEfficiencyMilliUnits", "0", 0)]
        [TestCase("CombatEffectTierRules", "spiritBudgetReference", "", 0)]
        [TestCase("AttackProfiles", "minimumNetCostUnits", "0", 0)]
        [TestCase("AttackProfiles", "efficiencyDenominator", "0", 0)]
        [TestCase("AttackProfiles", "controlChancePermille", "1001", 0)]
        [TestCase("Spells", "stunChance", "101", 0)]
        [TestCase("Spells", "type", "7", 0)]
        [TestCase("Skills", "damageMultiplier", "Infinity", 0)]
        [TestCase("Skills", "minRange", "9", 0)]
        [TestCase("CharacterCreationPointBuy", "movementEfficiencyMilliUnits", "0", 0)]
        [TestCase("CharacterCreationPointBuy", "costPerLevel", "2147483648", 0)]
        public void IllegalInputsAreLocatedAndPreserved(string id, string column, string raw, int row)
        {
            var session = WithCell(id, column, raw, row);
            Assert.That(session.Snapshot().Tables[id].Definition.Codec.ValidateDraft(session.Snapshot()), Is.Not.Empty);
            Assert.That(session.IsDirty, Is.False);
        }

        [Test]
        public void EmptyAndExplicitZeroOverridesRemainDifferentAndBranchColumnsStayEmpty()
        {
            var session = Session();
            var table = session.Snapshot().Tables["CombatActionEconomyRules"];
            var key = table.Rows.Keys.First(k => table.Rows[k].At("recordKind").RawToken == "test_action");
            Assert.That(table.Rows[key].At("spiritCostOverride").HasValue, Is.False);
            session.SetValue(table.Definition.TableId, key, "spiritCostOverride", ConfigValue.Scalar("0"));
            var overrideIssues = table.Definition.Codec.ValidateDraft(session.Snapshot());
            Assert.That(overrideIssues, Is.Empty, Describe(overrideIssues));
            Assert.That(session.Snapshot().Tables[table.Definition.TableId].Rows[key].At("spiritCostOverride").HasValue, Is.True);
            Assert.Throws<InvalidOperationException>(() => session.SetValue(table.Definition.TableId, key, "repeatFirstPermille", ConfigValue.Scalar("0")));
            Assert.Throws<InvalidOperationException>(() => session.SetValue("RealmCombatGrowth", "realm_fanren", "legacyBaseMovementEfficiencyMilliUnits", ConfigValue.Scalar("3000")));
            session.SetValue(table.Definition.TableId, key, "spiritCostOverride", ConfigValue.Scalar(""));
            Assert.That(session.IsDirty, Is.False);
        }

        [Test]
        public void FanrenMirrorsAndPointBuyGroupEditsHaveOneEntryAndStableRangeKeys()
        {
            var definitions = Definitions().Concat(new CharacterTables().GetTables().Where(t => t.TableId == "Characters" || t.TableId == "Enemies")).ToArray();
            var session = Session(definitions);
            var original = session.Snapshot();
            var characterKey = original.Tables["Characters"].Rows.Keys.First();
            session.SetValue("Characters", characterKey, "realmId", ConfigValue.Scalar("realm_fanren"));
            session.SetValue("RealmCombatGrowth", "realm_fanren", "baseMovementEfficiencyMilliUnits", ConfigValue.Scalar("3000"));
            var growthCodec = original.Tables["RealmCombatGrowth"].Definition.Codec;
            var links = growthCodec.GetLinkedEdits(session.Snapshot());
            Assert.That(links.Count(e => e.TableId == "CharacterCreationPointBuy"), Is.EqualTo(3));
            Assert.That(links.Any(e => e.TableId == "Characters"), Is.True);
            session.ApplyLinkedEdits(links);
            Assert.That(original.Tables["CharacterCreationPointBuy"].Definition.Codec.ValidateDraft(session.Snapshot()), Is.Empty);
            Assert.That(session.Snapshot().Tables["RealmCombatGrowth"].Rows["realm_fanren"].At("legacyBaseMovementEfficiencyMilliUnits").RawToken, Is.EqualTo("2000"));
            session.UndoDraft();
            var point = original.Tables["CharacterCreationPointBuy"];
            var keys = point.Rows.Keys.ToArray();
            Assert.That(point.Definition.Fields.Single(f => f.Column == "purchasePointLimit").IsEditable(point.Rows[keys[0]]), Is.True);
            Assert.That(point.Definition.Fields.Single(f => f.Column == "purchasePointLimit").IsEditable(point.Rows[keys[1]]), Is.False);
            Assert.Throws<InvalidOperationException>(() => session.SetValue("CharacterCreationPointBuy", keys[0], "movementEfficiencyMilliUnits", ConfigValue.Scalar("4000")));
            session.SetValue("CharacterCreationPointBuy", keys[0], "purchasePointLimit", ConfigValue.Scalar("26"));
            session.ApplyLinkedEdits(point.Definition.Codec.GetLinkedEdits(session.Snapshot()));
            Assert.That(session.GetEdits().Count, Is.EqualTo(3));
            Assert.That(point.Definition.Codec.ValidateDraft(session.Snapshot()), Is.Empty);
            session.UndoDraft(); session.SetValue("CharacterCreationPointBuy", keys[0], "fromValue", ConfigValue.Scalar("5"));
            var encoded = CsvConfigDocument.Load(session.EncodeCandidate("CharacterCreationPointBuy"));
            CollectionAssert.AreEqual(keys, encoded.GetRows(point.Definition).Select(point.Definition.RowKey));
            Assert.That(session.GetEdits().Count, Is.EqualTo(1));
        }

        [Test]
        public void ExistingDeepFixtureUsesStructuredSetsRootsAndKeepsHistoricalNegativeCase()
        {
            var definitions = Definitions();
            var attack = definitions.Single(t => t.TableId == "AttackProfiles");
            var lines = File.ReadAllLines(Path.Combine(Application.dataPath, "Tests/EditMode/Fixtures/AttackProfiles.fixture.csv"));
            var upgraded = lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#")).Select(l =>
                l + (l.StartsWith("attackProfileId,", StringComparison.Ordinal) ? ",attackSchemaVersion,unarmedFamilyId,unarmedRealmId,unarmedPhysicalAttackCapacity" : ",2,,,")).ToArray();
            var bytes = definitions.ToDictionary(d => d.TableId, d => Source(d.TableId));
            bytes["AttackProfiles"] = Encoding.UTF8.GetBytes(string.Join("\n", upgraded));
            var session = new ConfigDraftSession(definitions, bytes);
            foreach (var row in session.Snapshot().Tables["AttackProfiles"].Rows.Where(r => r.Key != "basic_fixture_unarmed"))
                foreach (var field in attack.Fields.Where(f => f.IsEditable(row.Value)))
                    foreach (var edit in Alternatives(field, row.Value.At(field.Column), field.Column))
                    {
                        session.UndoDraft(); session.SetValue("AttackProfiles", row.Key, edit.Key, edit.Value);
                        var roundTrip = CsvConfigDocument.Load(session.EncodeCandidate("AttackProfiles")).GetRows(attack).Single(r => r["attackProfileId"] == row.Key);
                        Assert.That(attack.Codec.DecodeRow(roundTrip).At(edit.Key), Is.EqualTo(edit.Value), row.Key + "/" + edit.Key);
                    }
            session.UndoDraft();
            var circle = session.Snapshot().Tables["AttackProfiles"].Rows["art_fixture_circle"];
            Assert.That(circle.At("areaAllowedFactions/enemy").RawToken, Is.EqualTo("1"));
            session.SetValue("AttackProfiles", "art_fixture_circle", "areaAllowedFactions/ally", ConfigValue.Scalar("1"));
            var saved = CsvConfigDocument.Load(session.EncodeCandidate("AttackProfiles")).GetRows(attack).Single(r => r["attackProfileId"] == "art_fixture_circle");
            Assert.That(saved["areaAllowedFactions"], Is.EqualTo("ally,enemy"));
            session.SetValue("AttackProfiles", "art_fixture_circle", "elementRequirementId", ConfigValue.List(new[] { ConfigValue.Scalar("water"), ConfigValue.Scalar("wind") }));
            Assert.That(attack.Codec.EncodeRow(session.Snapshot().Tables["AttackProfiles"].Rows["art_fixture_circle"])["elementRequirementId"], Is.EqualTo("element_water_or_wind"));
            Assert.That(attack.Codec.ValidateDraft(session.Snapshot()), Is.Not.Empty, "历史退役身体攻击仍须被领域解析器拒绝");
            var candidateLines = Encoding.UTF8.GetString(session.EncodeCandidate("AttackProfiles")).Split('\n').Where(l => !l.StartsWith("basic_fixture_unarmed,", StringComparison.Ordinal)).ToArray();
            var languageKeys = session.Snapshot().Tables["AttackProfiles"].Rows.Values.Select(v => v.At("displayNameKey").RawToken).Concat(new[] { "special_fixture_divine" }).ToArray();
            Assert.That(CombatContentImporter.TryBuildAttackProfileProjection(candidateLines, languageKeys, out var profiles, out var reason), Is.True, reason);
            foreach (var profile in profiles) UnityEngine.Object.DestroyImmediate(profile);
            Assert.That(CombatContentImporter.TryBuildAttackProfileProjection(upgraded, languageKeys, out _, out var historicalReason), Is.False);
            StringAssert.StartsWith("basic_attack_binding_kind_invalid", historicalReason);
            var badSet = WithCell("AttackProfiles", "areaAllowedFactions", "enemy,unknown", 0);
            Assert.That(attack.Codec.ValidateDraft(badSet.Snapshot()), Is.Not.Empty);
        }

        [Test]
        public void DerivedPreviewReadsTheCandidateRulesAndNeverWritesDeclarationColumns()
        {
            var session = Session();
            var baseline = CombatAbilityTables.GetAttackDerivedPreview(session.Snapshot(), "spell_guanshiji");
            StringAssert.Contains("只读派生预览", baseline);
            session.SetValue("AttackProfiles", "spell_guanshiji", "efficiencyNumerator", ConfigValue.Scalar("2"));
            Assert.That(CombatAbilityTables.GetAttackDerivedPreview(session.Snapshot(), "spell_guanshiji"), Is.Not.EqualTo(baseline));
            Assert.That(session.GetEdits().Count, Is.EqualTo(1));
            Assert.That(session.Snapshot().Tables["AttackProfiles"].Rows["spell_guanshiji"].At("physicalDamageMultiplier").HasValue, Is.False);
            Assert.Throws<InvalidOperationException>(() => session.SetValue("AttackProfiles", "spell_guanshiji", "physicalDamageMultiplier", ConfigValue.Scalar("1")));
            session.UndoDraft(); CollectionAssert.AreEqual(Source("AttackProfiles"), session.EncodeCandidate("AttackProfiles"));
        }

        [Test]
        public void InvalidBaselinesAreNotSilentlyRepairedAndMirrorFormattingIsNotNormalized()
        {
            var mismatch = WithCell("CharacterCreationPointBuy", "purchasePointLimit", "99", 1);
            Assert.That(mismatch.Snapshot().Tables["CharacterCreationPointBuy"].Definition.Codec.GetLinkedEdits(mismatch.Snapshot()), Is.Empty);
            Assert.That(mismatch.Snapshot().Tables["CharacterCreationPointBuy"].Definition.Codec.ValidateDraft(mismatch.Snapshot()), Is.Not.Empty);
            var zeroMirror = WithCell("CharacterCreationPointBuy", "movementEfficiencyMilliUnits", "0");
            Assert.That(zeroMirror.Snapshot().Tables["RealmCombatGrowth"].Definition.Codec.GetLinkedEdits(zeroMirror.Snapshot()), Is.Empty);
            var session = Session();
            session.SetValue("RealmCombatGrowth", "realm_fanren", "baseMovementEfficiencyMilliUnits", ConfigValue.Scalar("02000"));
            Assert.That(session.Snapshot().Tables["RealmCombatGrowth"].Definition.Codec.GetLinkedEdits(session.Snapshot()), Is.Empty);
            Assert.That(session.GetEdits().Count, Is.EqualTo(1));
        }

        [Test]
        public void LegacyDecimalsCannotChangeMeaningUnderTheImportCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var session = Session();
                var issues = session.Snapshot().Tables["Spells"].Definition.Codec.ValidateDraft(session.Snapshot());
                Assert.That(issues.Any(i => i.ChineseReason.Contains("format") || i.ChineseReason.Contains("格式") || i.ErrorCode == "combat_editor_import_culture_mismatch"), Is.True, Describe(issues));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void ImportDescriptionsReportExactFrozenClosureWithoutCallingImporters()
        {
            var defs = Definitions();
            foreach (var table in defs)
            {
                Assert.That(table.Codec.GetImportSteps(Array.Empty<string>()), Is.Empty);
                var steps = table.Codec.GetImportSteps(new[] { table.TableId });
                Assert.That(steps, Is.Not.Empty);
                Assert.That(steps.All(s => s.InputPaths.Count > 0 && s.ProductPaths.Count > 0 && !s.ProductPaths.Any(p => p.Contains('*'))), Is.True);
            }
            CollectionAssert.AreEqual(new[] { "CombatContentImporter.ImportAttackProfiles", "CharacterContentImporter.ImportCharacterDefinitions", "SettlementContentImporter.ImportContentCatalog", "SettlementContentImporter.ImportCharacterCreationPointBuy" },
                defs[0].Codec.GetImportSteps(new[] { defs[0].TableId }).Select(s => s.Method));
        }

        [UnityTest]
        public IEnumerator RealEditorWindowSelectsDrawsEditsAndUndoesEveryTableWithoutDiskWrites()
        {
            var paths = Ids.Select(id => Path.Combine(Application.dataPath, "DataConfig/" + id + ".csv"))
                .Concat(new[] { Path.Combine(Application.dataPath, "Data/CombatRuleSet.asset"), Path.Combine(Application.dataPath, "Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset") }).ToArray();
            var hashes = paths.ToDictionary(p => p, p => CsvConfigDocument.ComputeHash(File.ReadAllBytes(p)));
            var window = ConfigEditorWindow.Open();
            try
            {
                foreach (var id in Ids)
                {
                    window.SelectTable(id); yield return null;
                    window.SendEvent(new Event { type = EventType.Layout }); window.SendEvent(new Event { type = EventType.Repaint });
                    Assert.That(window.SelectedRowKey, Is.Not.Empty);
                    string field = id == "RealmCombatGrowth" ? "baseMovementEfficiencyMilliUnits" : id == "CombatEffectTierRules" ? "spiritBudgetReference" :
                        id == "CombatActionEconomyRules" ? "repeatFirstPermille" : id == "AttackProfiles" ? "actionPointCostUnits" : id == "CharacterCreationPointBuy" ? "purchasePointLimit" : "mpCost";
                    window.SetSelectedValue(field, ConfigValue.Scalar("3000"));
                    window.Repaint(); yield return null;
                    Assert.That(window.hasUnsavedChanges, Is.True);
                    Assert.That(window.SaveEnabled, Is.False);
                    window.UndoCurrentDraft();
                    Assert.That(window.hasUnsavedChanges, Is.False);
                    CollectionAssert.AreEqual(Source(id), window.Session.EncodeCandidate(id));
                }
                window.SelectTable("CharacterCreationPointBuy");
                PointBuyTable.OpenFanrenSource(); yield return null;
                Assert.That(window.SelectedTableId, Is.EqualTo("RealmCombatGrowth"));
                Assert.That(window.SelectedRowKey, Is.EqualTo("realm_fanren"));
            }
            finally { window.DiscardChanges(); window.Close(); }
            foreach (var p in paths) Assert.That(CsvConfigDocument.ComputeHash(File.ReadAllBytes(p)), Is.EqualTo(hashes[p]), p);
        }
    }
}
