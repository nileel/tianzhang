using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Editor.ConfigEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests
{
    public sealed class ConfigEditorCoreTests
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static byte[] Bytes(string text, bool bom = false) =>
            bom ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(Utf8.GetBytes(text)).ToArray() : Utf8.GetBytes(text);

        [TestCase(false)]
        [TestCase(true)]
        public void CsvPreservesTriviaMixedEndingsQuotesEmptyTailAndUntouchedCells(bool bom)
        {
            const string source = "#注释\r\n\nkey,name,value,tail\r\na,\"中文,名称\",0010,\rb, 原空格 ,0,";
            var bytes = Bytes(source, bom);
            var document = CsvConfigDocument.Load(bytes);
            CollectionAssert.AreEqual(bytes, document.EncodeEdits(Array.Empty<ConfigCellEdit>()));
            CollectionAssert.AreEqual(bytes, document.EncodeEdits(new[] { new ConfigCellEdit(3, 2, "0010") }));
            Assert.AreEqual("中文,名称", document.Lines[3].Cells[1].Value);
            Assert.AreEqual("", document.Lines[4].Cells[3].Value);
            Assert.AreEqual("\r", document.Lines[3].Ending);
            var edited = document.EncodeEdits(new[] { new ConfigCellEdit(3, 2, "11"), new ConfigCellEdit(4, 1, "新,名称") });
            CollectionAssert.AreEqual(Bytes(source.Replace("0010", "11").Replace(" 原空格 ", "\"新,名称\""), bom), edited);
            CollectionAssert.AreEqual(bytes, document.OriginalBytes);
            var external = document.OriginalBytes;
            external[0] = 0;
            CollectionAssert.AreEqual(bytes, document.OriginalBytes);
        }

        [TestCase("")]
        [TestCase("\n")]
        [TestCase("\r\n\r\n")]
        [TestCase("#只有注释\r")]
        public void EmptyAndTriviaOnlyFilesRoundTrip(string source)
        {
            var bytes = Bytes(source);
            CollectionAssert.AreEqual(bytes, CsvConfigDocument.Load(bytes).EncodeEdits(null));
        }

        [TestCase("id,id\na,b\n", "csv_columns")]
        [TestCase("id,unknown\na,b\n", "csv_columns")]
        [TestCase("id,name\na\n", "csv_width")]
        [TestCase("id,name\na,\"bad\n", "csv_quotes")]
        [TestCase("id,name\na,\"escaped\"\"quote\"\n", "csv_quotes")]
        [TestCase("id,name\na,x\"y\"\n", "csv_quotes")]
        public void UnknownDuplicateShortAndMalformedRowsFailStructure(string text, string errorCode)
        {
            var definition = new ConfigTableDefinition { TableId = "sample", ExactColumns = new[] { "id", "name" } };
            var document = CsvConfigDocument.Load(Bytes(text));
            Assert.That(document.ValidateStructure(definition).Any(issue => issue.ErrorCode == errorCode));
            Assert.Throws<InvalidDataException>(() => document.GetRows(definition));
            CollectionAssert.AreEqual(Bytes(text), document.EncodeEdits(null));
        }

        [TestCase("双\"引号")]
        [TestCase("跨\n行")]
        [TestCase("跨\r行")]
        public void EncodingRejectsUnsupportedLiteralInput(string token)
        {
            var document = CsvConfigDocument.Load(Bytes("id,name\na,b\n"));
            Assert.Throws<ArgumentException>(() => document.EncodeEdits(new[] { new ConfigCellEdit(1, 1, token) }));
        }

        [Test]
        public void CsvRejectsDuplicateEditsTriviaAndInvalidUtf8()
        {
            var document = CsvConfigDocument.Load(Bytes("#注释\nid,name\na,b\n"));
            Assert.Throws<InvalidDataException>(() => document.EncodeEdits(new[] { new ConfigCellEdit(0, 0, "x") }));
            Assert.Throws<InvalidDataException>(() => document.EncodeEdits(new[] { new ConfigCellEdit(2, 1, "x"), new ConfigCellEdit(2, 1, "y") }));
            Assert.Throws<DecoderFallbackException>(() => CsvConfigDocument.Load(new byte[] { 0xff }));
        }

        [Test]
        public void LanguageHasNoHeaderAndAdventureRequiresPhysicalFirstLine()
        {
            var language = new ConfigTableDefinition { TableId = "Language", HeaderMode = ConfigHeaderMode.None, ExactColumns = new[] { "id", "text" } };
            var document = CsvConfigDocument.Load(Bytes("#文本\r\nfaction_taiyidaoting,太一道庭\nsecond,第二条"));
            Assert.AreEqual(2, document.GetRows(language).Count);
            Assert.AreEqual("faction_taiyidaoting", document.GetRows(language)[0]["id"]);
            var adventure = new ConfigTableDefinition { TableId = "Adventures", HeaderMode = ConfigHeaderMode.FirstPhysicalLine, ExactColumns = new[] { "id", "name" } };
            Assert.That(CsvConfigDocument.Load(Bytes("#注释\nid,name\n")).ValidateStructure(adventure)
                .Any(issue => issue.ErrorCode == "csv_header_position"));
            Assert.AreEqual(0, CsvConfigDocument.Load(Bytes("id,name\n")).GetRows(adventure).Count);
        }

        [Test]
        public void AllTwentyTwoProductionSourcesRoundTripWithoutWritingOrDraftEdits()
        {
            var registry = ConfigTableRegistry.Discover();
            Assert.AreEqual(22, registry.Tables.Count);
            var session = registry.OpenSession();
            foreach (var table in registry.Tables)
            {
                var source = File.ReadAllBytes(Path.Combine(Application.dataPath, "DataConfig", table.TableId + ".csv"));
                CollectionAssert.AreEqual(source, session.EncodeCandidate(table.TableId), table.TableId);
                var document = session.Snapshot().Tables[table.TableId].Document;
                foreach (var line in document.Lines.Where(line => !line.IsTrivia))
                {
                    Assert.IsFalse(line.MalformedQuotes, table.TableId + " / " + line.Index);
                    CollectionAssert.AreEqual(CsvTableReader.ParseRow(line.Text), line.Cells.Select(cell => cell.Value).ToArray());
                }
            }
            Assert.IsFalse(session.IsDirty);
            Assert.IsEmpty(session.GetEdits());
        }

        [Test]
        public void DraftSnapshotUndoPreserveBaselineAndOnlyChangedColumnIsEncoded()
        {
            var definition = FixtureDefinition();
            var bytes = FixtureBytes();
            var session = FixtureSession(definition, bytes);
            var snapshot = session.Snapshot();
            session.SetValue("fixture", "a", "amount", ConfigValue.Scalar("1501"));
            Assert.IsTrue(session.IsDirty);
            Assert.AreEqual("001000", snapshot.Tables["fixture"].Rows["a"].At("amount").RawToken);
            Assert.IsFalse(session.CanClose(false));
            Assert.IsTrue(session.CanClose(true));
            Assert.AreEqual(1, session.GetEdits().Count);
            CollectionAssert.AreEqual(Bytes(Utf8.GetString(bytes).Replace("001000", "1501")), session.EncodeCandidate("fixture"));
            Assert.AreEqual(CsvConfigDocument.ComputeHash(bytes), session.Snapshot().Tables["fixture"].Document.BaselineHash);
            session.UndoDraft();
            Assert.IsFalse(session.IsDirty);
            CollectionAssert.AreEqual(bytes, session.EncodeCandidate("fixture"));
        }

        [Test]
        public void DraftListAddRemoveGroupAndReferenceRetainStructuredIdentity()
        {
            var definition = FixtureDefinition();
            var session = FixtureSession(definition, FixtureBytes());
            var items = session.Snapshot().Tables["fixture"].Rows["a"].At("list").Items;
            session.SetValue("fixture", "a", "list", ConfigValue.List(items.Concat(new[] { ConfigValue.Scalar("ref_2") })));
            session.SetValue("fixture", "a", "group/left", ConfigValue.Scalar("4"));
            session.SetValue("fixture", "a", "reference", ConfigValue.Scalar("ref_2"));
            var row = CsvConfigDocument.Load(session.EncodeCandidate("fixture")).GetRows(definition)[0];
            Assert.AreEqual("ref_1|ref_2", row["list"]);
            Assert.AreEqual("4~2", row["group"]);
            Assert.AreEqual("ref_2", row["reference"]);
            session.SetValue("fixture", "a", "list", ConfigValue.List(Array.Empty<ConfigValue>(), hasValue: false));
            Assert.AreEqual("", CsvConfigDocument.Load(session.EncodeCandidate("fixture")).GetRows(definition)[0]["list"]);
            session.UndoDraft();
            Assert.IsFalse(session.IsDirty);
            Assert.AreEqual("相同中文（ref_1）", definition.Fields.Single(field => field.Column == "reference").Choices()[0].DisplayName);
            Assert.AreEqual("相同中文（ref_2）", definition.Fields.Single(field => field.Column == "reference").Choices()[1].DisplayName);
        }

        [Test]
        public void EmptyZeroAndNoneRemainDifferentAndNumbersConvertExactly()
        {
            var field = NumberField("amount");
            Assert.IsFalse(ConfigValue.Scalar("").HasValue);
            Assert.IsTrue(ConfigValue.Scalar("0").HasValue);
            Assert.IsTrue(ConfigValue.Scalar("none").HasValue);
            Assert.AreEqual(1.501m, ConfigFieldGUI.DisplayNumber(field, ConfigValue.Scalar("1501")));
            Assert.AreEqual("1501", ConfigFieldGUI.FromDisplayNumber(field, "1.501").RawToken);
            Assert.Throws<ArgumentException>(() => ConfigFieldGUI.FromDisplayNumber(field, "1.0001"));
            Assert.Throws<ArgumentException>(() => ConfigFieldGUI.FromDisplayNumber(field, "2147483.648"));
            Assert.Throws<ArgumentException>(() => ConfigFieldGUI.FromDisplayNumber(field, "NaN"));
            Assert.Throws<ArgumentException>(() => ConfigFieldGUI.FromDisplayNumber(field, "Infinity"));
            var session = FixtureSession(FixtureDefinition(), FixtureBytes());
            session.SetValue("fixture", "a", "amount", ConfigValue.Scalar(""));
            Assert.AreEqual("", CsvConfigDocument.Load(session.EncodeCandidate("fixture")).GetRows(FixtureDefinition())[0]["amount"]);
            session.SetValue("fixture", "a", "amount", ConfigValue.Scalar("0"));
            Assert.AreEqual("0", CsvConfigDocument.Load(session.EncodeCandidate("fixture")).GetRows(FixtureDefinition())[0]["amount"]);
        }

        [Test]
        public void DraftRejectsReadOnlyConditionalUnknownReferenceAndReservedDelimiter()
        {
            var definition = FixtureDefinition();
            var session = FixtureSession(definition, FixtureBytes());
            Assert.Throws<InvalidOperationException>(() => session.SetValue("fixture", "a", "id", ConfigValue.Scalar("new")));
            Assert.Throws<ArgumentException>(() => session.SetValue("fixture", "a", "reference", ConfigValue.Scalar("unknown")));
            Assert.Throws<ArgumentException>(() => session.SetValue("fixture", "a", "list/0", ConfigValue.Scalar("ref_1|ref_2")));
            var text = definition.Fields.Single(field => field.Column == "text");
            text.CanEdit = _ => false;
            Assert.Throws<InvalidOperationException>(() => session.SetValue("fixture", "a", "text", ConfigValue.Scalar("新")));
            Assert.IsFalse(session.IsDirty);
        }

        [Test]
        public void SavedBaselineUpdatesOneDocumentAndUndoReturnsToLatestSave()
        {
            var definition = FixtureDefinition();
            var session = FixtureSession(definition, FixtureBytes());
            session.SetValue("fixture", "a", "amount", ConfigValue.Scalar("1501"));
            var candidate = session.EncodeCandidate("fixture");
            Assert.Throws<InvalidDataException>(() => session.AcceptSavedBaseline("fixture", FixtureBytes()));
            session.AcceptSavedBaseline("fixture", candidate);
            session.RecordApplicationEvidence("fixture输入hash核验");
            Assert.IsFalse(session.IsDirty);
            session.SetValue("fixture", "a", "amount", ConfigValue.Scalar("2000"));
            Assert.Throws<InvalidOperationException>(() => session.RecordApplicationEvidence("不能记录"));
            session.UndoDraft();
            Assert.AreEqual("1501", session.Snapshot().Tables["fixture"].Rows["a"].At("amount").RawToken);
            CollectionAssert.AreEqual(candidate, session.EncodeCandidate("fixture"));
        }

        [Test]
        public void DuplicateRowsBlockEditsAndDuplicateTableRegistrationIsRejected()
        {
            var definition = FixtureDefinition();
            Assert.Throws<InvalidDataException>(() => new ConfigTableRegistry(new[] { definition, definition }));
            var bytes = Bytes("id,text,amount,reference,list,group\na,原文,1,ref_1,ref_1,1~2\na,重复,2,ref_1,ref_1,1~2\n");
            var session = FixtureSession(definition, bytes);
            Assert.IsFalse(session.CanSave);
            Assert.That(session.ValidateDraft().Any(issue => issue.ErrorCode == "row_key"));
            Assert.Throws<InvalidOperationException>(() => session.SetValue("fixture", "a", "text", ConfigValue.Scalar("新")));
        }

        [Test]
        public void EnumBooleanAndReadOnlyGroupChildrenHaveExplicitContracts()
        {
            var enumeration = new ConfigFieldDefinition
            {
                Column = "scope", ChineseName = "类型", Control = ConfigControl.Enum, Editable = true,
                Choices = () => new[] { new ConfigChoice("none", "无"), new ConfigChoice("player", "玩家") }
            };
            Assert.DoesNotThrow(() => ConfigFieldGUI.ValidateValue(enumeration, ConfigValue.Scalar("none")));
            Assert.Throws<ArgumentException>(() => ConfigFieldGUI.ValidateValue(enumeration, ConfigValue.Scalar("")));
            Assert.Throws<ArgumentException>(() => ConfigFieldGUI.ValidateValue(enumeration, ConfigValue.Scalar("unknown")));
            var boolean = new ConfigFieldDefinition { Control = ConfigControl.Boolean, ChineseName = "是否启用", Editable = true };
            Assert.DoesNotThrow(() => ConfigFieldGUI.ValidateValue(boolean, ConfigValue.Scalar("0")));
            Assert.DoesNotThrow(() => ConfigFieldGUI.ValidateValue(boolean, ConfigValue.Scalar("1")));
            Assert.Throws<ArgumentException>(() => ConfigFieldGUI.ValidateValue(boolean, ConfigValue.Scalar("true")));
            var definition = FixtureDefinition();
            definition.Fields.Single(field => field.Column == "group").Children[1].Editable = false;
            var session = FixtureSession(definition, FixtureBytes());
            var before = session.Snapshot().Tables["fixture"].Rows["a"].At("group");
            Assert.Throws<InvalidOperationException>(() => session.SetValue("fixture", "a", "group", before.With("right", ConfigValue.Scalar("9"))));
            session.SetValue("fixture", "a", "group", before.With("left", ConfigValue.Scalar("9")));
            Assert.AreEqual("9~2", CsvConfigDocument.Load(session.EncodeCandidate("fixture")).GetRows(definition)[0]["group"]);
        }

        [Test]
        public void PartialSaveKeepsOtherDocumentDirtyAndItsOriginalHash()
        {
            var first = FixtureDefinition();
            var second = FixtureDefinition();
            second.TableId = "second";
            var session = new ConfigDraftSession(new[] { first, second }, new Dictionary<string, byte[]>
                { ["fixture"] = FixtureBytes(), ["second"] = FixtureBytes() });
            session.SetValue("fixture", "a", "amount", ConfigValue.Scalar("2000"));
            session.SetValue("second", "a", "amount", ConfigValue.Scalar("3000"));
            session.AcceptSavedBaseline("fixture", session.EncodeCandidate("fixture"));
            Assert.IsTrue(session.IsDirty);
            Assert.AreEqual("second", session.GetEdits().Single().TableId);
            Assert.AreEqual(CsvConfigDocument.ComputeHash(FixtureBytes()), session.Snapshot().Tables["second"].Document.BaselineHash);
            session.UndoDraft();
            Assert.IsFalse(session.IsDirty);
            Assert.AreEqual("2000", session.Snapshot().Tables["fixture"].Rows["a"].At("amount").RawToken);
            Assert.AreEqual("001000", session.Snapshot().Tables["second"].Rows["a"].At("amount").RawToken);
        }

        [UnityTest]
        public IEnumerator ActualWindowOpensSelectsEditsRendersUndoesAndGuardsClose()
        {
            var window = ConfigEditorWindow.Open();
            try
            {
                Assert.IsNotNull(window.Session);
                Assert.AreEqual("RealmCombatGrowth", window.SelectedTableId);
                Assert.IsFalse(window.SaveEnabled);
                window.SelectTable("FoundationPurpleMansionStates");
                Assert.AreEqual("", window.SelectedRowKey);
                window.Repaint();
                yield return null;
                window.SendEvent(new Event { type = EventType.Layout });
                window.SendEvent(new Event { type = EventType.Repaint });
                var definition = FixtureDefinition();
                window.Initialize(new ConfigTableRegistry(new[] { definition }), FixtureSession(definition, FixtureBytes()));
                window.SelectRow("b");
                Assert.AreEqual("b", window.SelectedRowKey);
                window.SelectRow("a");
                window.SetSelectedValue("amount", ConfigFieldGUI.FromDisplayNumber(NumberField("amount"), "1.501"));
                window.Repaint();
                yield return null;
                window.SendEvent(new Event { type = EventType.Layout });
                window.SendEvent(new Event { type = EventType.Repaint });
                Assert.IsTrue(window.hasUnsavedChanges);
                Assert.IsFalse(window.SaveEnabled);
                Assert.That(window.saveChangesMessage, Does.Contain("继续编辑"));
                Assert.IsFalse(window.TryClose(() => false));
                Assert.IsTrue(window != null);
                window.SaveChanges();
                Assert.IsTrue(window.hasUnsavedChanges, "未接入APPLY不得伪装保存或关闭。");
                window.UndoCurrentDraft();
                Assert.IsFalse(window.hasUnsavedChanges);
                CollectionAssert.AreEqual(FixtureBytes(), window.Session.EncodeCandidate("fixture"));
                window.SetSelectedValue("amount", ConfigValue.Scalar("2000"));
                Assert.IsTrue(window.TryClose(() => true));
                Assert.IsTrue(window == null);
            }
            finally
            {
                if (window != null) { window.DiscardChanges(); window.Close(); }
            }
        }

        [Test]
        public void CoreLivesInExistingEditorOnlyAssembly()
        {
            Assert.AreEqual("TianZhang.Editor", typeof(ConfigEditorWindow).Assembly.GetName().Name);
            var asmdef = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Editor/TianZhang.Editor.asmdef"));
            Assert.That(asmdef, Does.Contain("\"Editor\""));
            var menu = typeof(ConfigEditorWindow).GetMethod(nameof(ConfigEditorWindow.Open)).GetCustomAttribute<MenuItem>();
            Assert.AreEqual("天章/配置编辑器", menu.menuItem);
        }

        [Test]
        public void DraftChoiceContextUsesProspectiveRowAndExactSessionWithoutGlobalWindow()
        {
            var definition = FixtureDefinition();
            var reference = definition.Fields.Single(f => f.Column == "reference");
            reference.DraftChoices = context =>
            {
                Assert.AreEqual("fixture", context.TableId);
                Assert.That(context.Path, Is.EqualTo("reference").Or.EqualTo("list/0"));
                return context.Row.At("list").Items.Select(v => new ConfigChoice(v.RawToken, "当前行引用")).ToArray();
            };
            var session = FixtureSession(definition, FixtureBytes());
            session.SetValue("fixture", "a", "reference", ConfigValue.Scalar("ref_1"));
            Assert.Throws<ArgumentException>(() => session.SetValue("fixture", "b", "reference", ConfigValue.Scalar("ref_1")));
            session.SetValue("fixture", "a", "list", ConfigValue.List(new[] { ConfigValue.Scalar("ref_2") }));
            session.SetValue("fixture", "a", "reference", ConfigValue.Scalar("ref_2"));
            session.UndoDraft();
            Assert.Throws<ArgumentException>(() => session.SetValue("fixture", "a", "reference", ConfigValue.Scalar("ref_2")));
            Assert.IsEmpty(reference.GetChoices(null), "缺少上下文不能借用静态候选或其他窗口");
        }

        private static byte[] FixtureBytes() => Bytes("#隔离内存样例\r\nid,text,amount,reference,list,group\na,\"原,文\",001000,ref_1,ref_1,1~2\rb,次条,0,ref_2,,3~4");

        private static ConfigDraftSession FixtureSession(ConfigTableDefinition definition, byte[] bytes) =>
            new ConfigDraftSession(new[] { definition }, new Dictionary<string, byte[]> { [definition.TableId] = bytes });

        private static ConfigFieldDefinition NumberField(string column) => new ConfigFieldDefinition
        {
            Column = column, ChineseName = "行动力", Unit = "行动力", Control = ConfigControl.Integer,
            Editable = true, AllowEmpty = true, Scale = 1000m
        };

        private static ConfigTableDefinition FixtureDefinition()
        {
            Func<IReadOnlyList<ConfigChoice>> choices = () => new[] { new ConfigChoice("ref_1", "相同中文"), new ConfigChoice("ref_2", "相同中文") };
            var reference = new ConfigFieldDefinition { Column = "reference", ChineseName = "中文引用", Control = ConfigControl.Reference, Editable = true, Choices = choices, ReservedDelimiters = "|" };
            var scalar = new ConfigFieldDefinition { Column = "left", ChineseName = "数值", Unit = "点", Control = ConfigControl.Integer, Editable = true };
            return new ConfigTableDefinition
            {
                TableId = "fixture", ChineseName = "隔离测试表", CsvPath = "memory-only.csv", ExactColumns = new[] { "id", "text", "amount", "reference", "list", "group" },
                RowKey = row => row["id"], RowLabel = row => "条目（" + row["id"] + "）", Codec = new FixtureCodec(),
                Fields = new[]
                {
                    new ConfigFieldDefinition { Column = "id", ChineseName = "稳定身份", ReadOnlyReason = "稳定键不可修改" },
                    new ConfigFieldDefinition { Column = "text", ChineseName = "说明文本", Editable = true },
                    NumberField("amount"), reference,
                    new ConfigFieldDefinition { Column = "list", ChineseName = "引用列表", Control = ConfigControl.List, Editable = true, AllowEmpty = true, Element = reference, NewElement = () => ConfigValue.Scalar("ref_1") },
                    new ConfigFieldDefinition { Column = "group", ChineseName = "数值分组", Control = ConfigControl.Group, Editable = true, Children = new[]
                        { scalar, new ConfigFieldDefinition { Column = "right", ChineseName = "右侧数值", Unit = "点", Control = ConfigControl.Integer, Editable = true } } }
                }
            };
        }

        // Does not implement a provider: TypeCache must never discover test fixtures as production.
        private sealed class FixtureCodec : IConfigRowCodec
        {
            public ConfigValue DecodeRow(ConfigCsvRow row)
            {
                var values = row.Cells.ToDictionary(pair => pair.Key, pair => ConfigValue.Scalar(pair.Value));
                values["list"] = ConfigValue.List(row["list"].Length == 0 ? Array.Empty<ConfigValue>() : row["list"].Split('|').Select(ConfigValue.Scalar).ToArray(), row["list"], row["list"].Length > 0);
                var group = row["group"].Split('~');
                values["group"] = ConfigValue.Group(new Dictionary<string, ConfigValue> { ["left"] = ConfigValue.Scalar(group[0]), ["right"] = ConfigValue.Scalar(group[1]) }, row["group"]);
                return ConfigValue.Group(values);
            }
            public IReadOnlyDictionary<string, string> EncodeRow(ConfigValue row)
            {
                var values = row.Members.ToDictionary(pair => pair.Key, pair => pair.Value.RawToken);
                values["amount"] = row.At("amount").HasValue ? int.Parse(row.At("amount").RawToken).ToString() : "";
                values["list"] = string.Join("|", row.At("list").Items.Select(item => item.RawToken));
                values["group"] = row.At("group/left").RawToken + "~" + row.At("group/right").RawToken;
                return values;
            }
            public IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot) => Array.Empty<ConfigIssue>();
            public IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot) => Array.Empty<ConfigEdit>();
            public IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> changedTables) => Array.Empty<ConfigImportStep>();
        }
    }
}
