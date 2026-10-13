using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TianZhang.Editor.ConfigEditor
{
    // Only the three cultivation adapters share this presentation grammar. Domain rules stay in the importer.
    internal sealed class CultivationShape
    {
        internal ConfigFieldDefinition Field;
        internal Func<string, ConfigValue> Read;
        internal Func<ConfigValue, string> Write;
        internal string Encode(ConfigValue value)
        {
            // Preserve untouched tokens, including none/empty, numeric spelling and internal whitespace.
            if (value.Kind != ConfigNodeKind.Scalar && Read(value.RawToken).Equals(value)) return value.RawToken;
            return Write(value);
        }
    }

    internal static class CultivationFields
    {
        internal static CultivationShape Text(string id, string name, bool editable = true) => new CultivationShape
        {
            Field = new ConfigFieldDefinition { Column = id, ChineseName = name, Control = ConfigControl.Text,
                Editable = editable, AllowEmpty = true, ReservedDelimiters = "|~+:,@\r\n", ReadOnlyReason = "稳定标识、版本或冻结事实，不可修改" },
            Read = ConfigValue.Scalar, Write = v => v.RawToken
        };
        internal static CultivationShape Number(string id, string name, bool integer = true, string unit = "")
        {
            var s = Text(id, name); s.Field.Control = integer ? ConfigControl.Integer : ConfigControl.Decimal; s.Field.Unit = unit; return s;
        }
        internal static CultivationShape Enum(string id, string name, params string[] pairs)
        {
            var s = Text(id, name); s.Field.Control = ConfigControl.Enum;
            s.Field.Choices = () => Enumerable.Range(0, pairs.Length / 2).Select(i => new ConfigChoice(pairs[i * 2], pairs[i * 2 + 1])).ToArray();
            return s;
        }
        internal static CultivationShape Lock(CultivationShape s, string reason = "派生或冻结字段，只读")
        { s.Field.Editable = false; s.Field.ReadOnlyReason = reason; return s; }
        internal static CultivationShape Reference(string id, string name, Func<ConfigFieldContext, IEnumerable<string>> source, bool none = false)
        {
            var s = Text(id, name); s.Field.Control = ConfigControl.Reference;
            s.Field.DraftChoices = c => source(c).Concat(none ? new[] { "none" } : Array.Empty<string>())
                .Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).Select(x => new ConfigChoice(x, x == "none" ? "无" : name)).ToArray();
            return s;
        }
        internal static CultivationShape External(string id, string name)
        {
            var s = Text(id, name);
            s.Field.Help = "外部引用：当前领域仅验证标识格式；未接入权威目录，不能伪造候选。";
            return s;
        }
        internal static IEnumerable<string> RowIds(ConfigFieldContext c, string list, string key) =>
            c.Row.Members.TryGetValue(list, out var values) ? values.Items.Select(v => v.At(key).RawToken) : Array.Empty<string>();
        internal static CultivationShape List(string id, string name, char separator, CultivationShape element, string initial, string empty = "none")
        {
            var s = new CultivationShape();
            s.Field = new ConfigFieldDefinition { Column = id, ChineseName = name, Control = ConfigControl.List, Editable = true,
                AllowEmpty = true, EmptyMeaning = "无条目", Element = element.Field, NewElement = () => element.Read(initial) };
            s.Read = raw => ConfigValue.List(IsNone(raw) ? Array.Empty<ConfigValue>() : raw.Split(separator).Select(x => element.Read(x.Trim())), raw, !IsNone(raw));
            s.Write = v => v.Items.Count == 0 ? empty : string.Join(separator.ToString(), v.Items.Select(element.Encode));
            return s;
        }
        internal static CultivationShape Group(string id, string name, char separator, params CultivationShape[] children)
        {
            return Record(id, name, separator, children, _ => children.Select(c => c.Field.Column).ToArray());
        }
        // A tagged record has one visible union of fields; inactive payloads must remain empty.
        internal static CultivationShape Record(string id, string name, char separator, CultivationShape[] children, Func<string[], string[]> layout)
        {
            var s = new CultivationShape();
            s.Field = new ConfigFieldDefinition { Column = id, ChineseName = name, Control = ConfigControl.Group,
                Editable = true, AllowEmpty = true, Children = children.Select(c => c.Field).ToArray() };
            s.Read = raw =>
            {
                if (raw.Length == 0) return ConfigValue.Group(children.ToDictionary(c => c.Field.Column, c => c.Read("")), raw, false);
                var parts = raw.Split(separator); var keys = layout(parts);
                if (parts.Length != keys.Length) throw new InvalidDataException(name + "的结构段数不匹配，拒绝丢弃未知内容。");
                var values = children.ToDictionary(c => c.Field.Column, c => c.Read(""), StringComparer.Ordinal);
                for (var i = 0; i < keys.Length; i++) values[keys[i]] = children.Single(c => c.Field.Column == keys[i]).Read(parts[i]);
                return ConfigValue.Group(values, raw);
            };
            s.Write = value =>
            {
                var tokens = children.Select(c => c.Encode(value.Members[c.Field.Column])).ToArray();
                var keys = layout(tokens);
                foreach (var child in children.Where(c => !keys.Contains(c.Field.Column)))
                    if (!IsNone(child.Encode(value.Members[child.Field.Column]))) throw new InvalidDataException(name + "包含不适用于当前分支的值，请先清空后切换状态。");
                return string.Join(separator.ToString(), keys.Select(key => children.Single(c => c.Field.Column == key).Encode(value.Members[key])));
            };
            return s;
        }
        internal static bool IsNone(string raw) => string.IsNullOrEmpty(raw) || raw == "none";
        internal static string Token(ConfigValue row, string field) => row.Members.TryGetValue(field, out var value) ? value.RawToken : "";
        internal static string[] Lines(ConfigTableDefinition definition, IEnumerable<ConfigValue> rows) =>
            new[] { string.Join(",", definition.ExactColumns) }.Concat(rows.Select(row =>
            {
                var encoded = definition.Codec.EncodeRow(row);
                return string.Join(",", definition.ExactColumns.Select(c => Quote(encoded[c])));
            })).ToArray();
        internal static string Quote(string token) => token.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? token : "\"" + token.Replace("\"", "\"\"") + "\"";
        internal static void Destroy(IEnumerable<UnityEngine.Object> values)
        { foreach (var value in values) UnityEngine.Object.DestroyImmediate(value); }
    }

    public abstract class CultivationTableCodec : IConfigRowCodec
    {
        internal CultivationShape[] Shapes;
        public ConfigTableDefinition Definition { get; protected set; }
        protected bool AllowFixtures;
        internal void Initialize(string id, string name, string key, CultivationShape[] shapes)
        {
            Shapes = shapes;
            Definition = new ConfigTableDefinition { TableId = id, ChineseName = name, CsvPath = "Assets/DataConfig/" + id + ".csv",
                ExactColumns = shapes.Select(s => s.Field.Column).ToArray(), Fields = shapes.Select(s => s.Field).ToArray(),
                RowKey = row => row[key], RowLabel = row => name + "（" + row[key] + "）", Codec = this, ReadOnlyReason = "" };
        }
        public ConfigValue DecodeRow(ConfigCsvRow row) => ConfigValue.Group(Shapes.ToDictionary(s => s.Field.Column, s => s.Read(row[s.Field.Column])));
        public IReadOnlyDictionary<string, string> EncodeRow(ConfigValue row) => Shapes.ToDictionary(s => s.Field.Column, s => s.Encode(row.Members[s.Field.Column]));
        public virtual IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot) => Array.Empty<ConfigEdit>();
        public abstract IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> changedTables);
        protected abstract void ValidateDomain(ConfigValue row, string[] lines, ConfigDraftSnapshot snapshot);
        public virtual IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot)
        {
            var issues = new List<ConfigIssue>();
            if (!snapshot.Tables.TryGetValue(Definition.TableId, out var table)) return issues;
            foreach (var pair in table.Rows)
            {
                var row = pair.Value;
                bool fixture = !CultivationFields.IsNone(CultivationFields.Token(row, "fixtureId"));
                bool negative = AllowFixtures && fixture && CultivationFields.Token(row, "expect") == "REJECT";
                try
                {
                    // Expected negative fixtures are checked by the original parser, not rewritten into valid characters.
                    if (!negative) ValidateFields(snapshot, pair.Key, row, issues);
                    ValidateDomain(row, CultivationFields.Lines(Definition, new[] { row }), snapshot);
                    if (negative) issues.Add(Issue(pair.Key, "expect", "负例意外通过原领域校验。", "fixture_unexpected_accept"));
                }
                catch (Exception e) when (e is InvalidDataException || e is ArgumentException || e is FormatException || e is OverflowException)
                {
                    if (!negative) issues.Add(Issue(pair.Key, "", "原领域校验失败：" + e.Message, e.Message.Split(':')[0]));
                }
            }
            return issues;
        }
        internal void ValidateFields(ConfigDraftSnapshot snapshot, string key, ConfigValue row, List<ConfigIssue> issues)
        {
            foreach (var field in Definition.Fields)
                Visit(field, row.Members[field.Column], new ConfigFieldContext(snapshot, Definition.TableId, key, row, field.Column));
            void Visit(ConfigFieldDefinition field, ConfigValue value, ConfigFieldContext context)
            {
                if (field.Control == ConfigControl.Group) { foreach (var child in field.Children) Visit(child, value.Members[child.Column], context.Child(child.Column)); return; }
                if (field.Control == ConfigControl.List) { for (var i = 0; i < value.Items.Count; i++) Visit(field.Element, value.Items[i], context.Child(i.ToString())); return; }
                try { ConfigFieldGUI.ValidateValue(field, value, context); }
                catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException)
                { issues.Add(Issue(key, context.Path, e.Message, "field_value")); }
            }
        }
        protected ConfigIssue Issue(string key, string path, string message, string code) => new ConfigIssue(Definition.TableId, key, path, message, code);
    }
}
