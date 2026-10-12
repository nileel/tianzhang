using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class ConfigDraftTableSnapshot
    {
        public ConfigTableDefinition Definition { get; }
        public CsvConfigDocument Document { get; }
        public IReadOnlyDictionary<string, ConfigValue> Rows { get; }
        public IReadOnlyDictionary<string, ConfigCsvRow> SourceRows { get; }
        public IReadOnlyList<ConfigIssue> Issues { get; }
        internal ConfigDraftTableSnapshot(ConfigTableDefinition definition, CsvConfigDocument document,
            IDictionary<string, ConfigValue> rows, IDictionary<string, ConfigCsvRow> sourceRows, IReadOnlyList<ConfigIssue> issues)
        {
            Definition = definition; Document = document;
            Rows = new ReadOnlyDictionary<string, ConfigValue>(new Dictionary<string, ConfigValue>(rows, StringComparer.Ordinal));
            SourceRows = new ReadOnlyDictionary<string, ConfigCsvRow>(new Dictionary<string, ConfigCsvRow>(sourceRows, StringComparer.Ordinal));
            Issues = issues;
        }
    }

    public sealed class ConfigDraftSnapshot
    {
        public IReadOnlyDictionary<string, ConfigDraftTableSnapshot> Tables { get; }
        public IReadOnlyList<ConfigEdit> Edits { get; }
        internal ConfigDraftSnapshot(IDictionary<string, ConfigDraftTableSnapshot> tables, IEnumerable<ConfigEdit> edits)
        {
            Tables = new ReadOnlyDictionary<string, ConfigDraftTableSnapshot>(tables);
            Edits = Array.AsReadOnly(edits.ToArray());
        }
    }

    public sealed class ConfigDraftSession
    {
        private sealed class TableDraft
        {
            public ConfigTableDefinition Definition;
            public CsvConfigDocument Document;
            public Dictionary<string, ConfigCsvRow> SourceRows = new Dictionary<string, ConfigCsvRow>(StringComparer.Ordinal);
            public Dictionary<string, ConfigValue> Baseline = new Dictionary<string, ConfigValue>(StringComparer.Ordinal);
            public Dictionary<string, ConfigValue> Rows = new Dictionary<string, ConfigValue>(StringComparer.Ordinal);
            public IReadOnlyList<ConfigIssue> Issues;
        }

        private readonly Dictionary<string, TableDraft> tables = new Dictionary<string, TableDraft>(StringComparer.Ordinal);
        public string LastApplicationEvidence { get; private set; } = "";
        public bool IsDirty => tables.Values.Any(table => table.Rows.Any(row => !row.Value.Equals(table.Baseline[row.Key])));
        public bool CanSave => tables.Values.All(table => table.Issues.Count == 0) &&
            tables.Values.Where(IsChanged).All(table => table.Definition.IsAdapted);
        public bool RequiresCloseConfirmation => IsDirty;
        public bool CanClose(bool discardDraft) => !IsDirty || discardDraft;

        public ConfigDraftSession(IEnumerable<ConfigTableDefinition> definitions, IReadOnlyDictionary<string, byte[]> documents)
        {
            foreach (var definition in definitions)
            {
                if (!documents.TryGetValue(definition.TableId, out var bytes))
                    throw new InvalidDataException($"缺少相关文档快照：{definition.ChineseName}");
                tables.Add(definition.TableId, CreateTable(definition, bytes));
            }
        }

        private static TableDraft CreateTable(ConfigTableDefinition definition, byte[] bytes)
        {
            var table = new TableDraft { Definition = definition, Document = CsvConfigDocument.Load(bytes) };
            table.Issues = definition.ExactColumns.Count > 0 ? table.Document.ValidateStructure(definition) : Array.Empty<ConfigIssue>();
            if (!definition.IsAdapted || table.Issues.Count > 0) return table;
            var issues = new List<ConfigIssue>();
            foreach (var row in table.Document.GetRows(definition))
            {
                var key = definition.RowKey(row);
                if (string.IsNullOrEmpty(key) || table.Rows.ContainsKey(key))
                {
                    issues.Add(new ConfigIssue(definition.TableId, key, "", "稳定行键为空或重复。", "row_key"));
                    continue;
                }
                var value = definition.Codec.DecodeRow(row);
                if (value == null || value.Kind != ConfigNodeKind.Group ||
                    !definition.ExactColumns.All(column => value.Members.ContainsKey(column)))
                    throw new InvalidDataException($"{definition.ChineseName}的编解码器未覆盖全部列。");
                table.SourceRows.Add(key, row);
                table.Baseline.Add(key, value);
                table.Rows.Add(key, value);
            }
            table.Issues = issues.AsReadOnly();
            return table;
        }

        public ConfigDraftSnapshot Snapshot()
        {
            return new ConfigDraftSnapshot(tables.ToDictionary(pair => pair.Key, pair =>
                new ConfigDraftTableSnapshot(pair.Value.Definition, pair.Value.Document, pair.Value.Rows, pair.Value.SourceRows, pair.Value.Issues),
                StringComparer.Ordinal), GetEdits());
        }

        public IReadOnlyList<ConfigEdit> GetEdits()
        {
            var edits = new List<ConfigEdit>();
            foreach (var pair in tables)
                foreach (var row in pair.Value.Rows)
                    foreach (var field in row.Value.Members)
                        if (!field.Value.Equals(pair.Value.Baseline[row.Key].Members[field.Key]))
                            edits.Add(new ConfigEdit(pair.Key, row.Key, field.Key, field.Value));
            return edits.AsReadOnly();
        }

        public void SetValue(string tableId, string rowKey, string fieldPath, ConfigValue value)
        {
            var table = tables[tableId];
            if (!table.Definition.IsAdapted || table.Issues.Count > 0) throw new InvalidOperationException("当前表尚不能编辑。");
            var row = table.Rows[rowKey];
            var segments = fieldPath.Split('/');
            var field = table.Definition.Fields.Single(item => item.Column == segments[0]);
            if (!field.IsEditable(row)) throw new InvalidOperationException(field.ReadOnlyReason);
            for (var i = 1; i < segments.Length; i++)
            {
                field = field.Control == ConfigControl.List ? field.Element : field.Children.Single(item => item.Column == segments[i]);
                if (field == null || !field.IsEditable(row)) throw new InvalidOperationException("字段为只读或尚未适配。");
            }
            if (row.At(fieldPath).Kind != value.Kind) throw new ArgumentException("字段结构类型不能改变。");
            ConfigFieldGUI.ValidateValue(field, value);
            CheckReadOnlyChildren(field, row.At(fieldPath), value, row);
            table.Rows[rowKey] = row.With(fieldPath, value);
        }

        private static void CheckReadOnlyChildren(ConfigFieldDefinition field, ConfigValue before, ConfigValue after, ConfigValue row)
        {
            if (!field.IsEditable(row) && !before.Equals(after)) throw new InvalidOperationException(field.ReadOnlyReason);
            if (field.Control == ConfigControl.Group)
                foreach (var child in field.Children)
                    CheckReadOnlyChildren(child, before.Members[child.Column], after.Members[child.Column], row);
            if (field.Control == ConfigControl.List && before.Items.Count == after.Items.Count)
                for (var i = 0; i < before.Items.Count; i++)
                    CheckReadOnlyChildren(field.Element, before.Items[i], after.Items[i], row);
        }

        // APPLY consumes only deterministic mirrors reported by the authoritative codecs.
        // These can target read-only derived fields; no disk IO occurs here.
        public void ApplyLinkedEdits(IEnumerable<ConfigEdit> edits)
        {
            foreach (var edit in edits)
            {
                var table = tables[edit.TableId];
                if (!table.Definition.IsAdapted || table.Issues.Count > 0) throw new InvalidOperationException("关联表未通过结构校验。");
                var row = table.Rows[edit.RowKey];
                if (row.At(edit.FieldPath).Kind != edit.Value.Kind) throw new ArgumentException("关联字段结构类型不能改变。");
                table.Rows[edit.RowKey] = row.With(edit.FieldPath, edit.Value);
            }
        }

        public IReadOnlyList<ConfigIssue> ValidateDraft()
        {
            var issues = tables.Values.SelectMany(table => table.Issues).ToList();
            var snapshot = Snapshot();
            foreach (var codec in tables.Values.Where(table => table.Definition.IsAdapted && table.Issues.Count == 0)
                .Select(table => table.Definition.Codec).Distinct())
                issues.AddRange(codec.ValidateDraft(snapshot));
            return issues.AsReadOnly();
        }

        public byte[] EncodeCandidate(string tableId)
        {
            var table = tables[tableId];
            if (!IsChanged(table)) return table.Document.OriginalBytes;
            if (table.Issues.Count > 0 || !table.Definition.IsAdapted) throw new InvalidOperationException("当前表不能编码保存候选。");
            var edits = new List<ConfigCellEdit>();
            foreach (var row in table.Rows.Where(row => !row.Value.Equals(table.Baseline[row.Key])))
            {
                var encoded = table.Definition.Codec.EncodeRow(row.Value);
                if (encoded.Count != table.Definition.ExactColumns.Count || !table.Definition.ExactColumns.All(encoded.ContainsKey))
                    throw new InvalidDataException("编解码器输出缺列或未知列。");
                for (var i = 0; i < table.Definition.ExactColumns.Count; i++)
                {
                    var column = table.Definition.ExactColumns[i];
                    // Do not normalize untouched fields, even if the codec emits normalized tokens.
                    if (!row.Value.Members[column].Equals(table.Baseline[row.Key].Members[column]))
                        edits.Add(new ConfigCellEdit(table.SourceRows[row.Key].PhysicalLine, i, encoded[column]));
                }
            }
            var bytes = table.Document.EncodeEdits(edits);
            var projected = CsvConfigDocument.Load(bytes);
            if (!projected.GetRows(table.Definition).Select(table.Definition.RowKey).SequenceEqual(table.SourceRows.Keys))
                throw new InvalidDataException("候选内容改变了稳定行键。");
            return bytes;
        }

        public void UndoDraft()
        {
            foreach (var table in tables.Values)
                table.Rows = new Dictionary<string, ConfigValue>(table.Baseline, StringComparer.Ordinal);
        }

        // Called by APPLY only after the exact candidate bytes were successfully written.
        public void AcceptSavedBaseline(string tableId, byte[] savedBytes)
        {
            if (!EncodeCandidate(tableId).SequenceEqual(savedBytes)) throw new InvalidDataException("已保存内容与当前候选不一致。");
            var replacement = CreateTable(tables[tableId].Definition, savedBytes);
            if (replacement.Issues.Count > 0 || !replacement.Rows.Keys.SequenceEqual(tables[tableId].Rows.Keys))
                throw new InvalidDataException("保存改变了结构或稳定行键。");
            tables[tableId] = replacement;
            LastApplicationEvidence = "";
        }

        public void RecordApplicationEvidence(string evidence)
        {
            if (IsDirty) throw new InvalidOperationException("未保存草稿不能记录应用成功。");
            LastApplicationEvidence = evidence ?? "";
        }

        private static bool IsChanged(TableDraft table) => table.Rows.Any(row => !row.Value.Equals(table.Baseline[row.Key]));
    }
}
