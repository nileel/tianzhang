using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TianZhang.Editor.ConfigEditor
{
    public enum ConfigHeaderMode { Header, FirstPhysicalLine, None }
    public enum ConfigNodeKind { Scalar, List, Group }
    public enum ConfigControl { Text, Integer, Decimal, Boolean, Enum, Reference, List, Group }

    // Values are immutable, so a snapshot cannot mutate the live draft or its baseline.
    public sealed class ConfigValue : IEquatable<ConfigValue>
    {
        public ConfigNodeKind Kind { get; }
        public string RawToken { get; }
        public bool HasValue { get; }
        public IReadOnlyList<ConfigValue> Items { get; }
        public IReadOnlyDictionary<string, ConfigValue> Members { get; }

        private ConfigValue(ConfigNodeKind kind, string rawToken, bool hasValue,
            IEnumerable<ConfigValue> items, IDictionary<string, ConfigValue> members)
        {
            Kind = kind;
            RawToken = rawToken ?? string.Empty;
            HasValue = hasValue;
            Items = Array.AsReadOnly((items ?? Array.Empty<ConfigValue>()).ToArray());
            Members = new ReadOnlyDictionary<string, ConfigValue>(
                new Dictionary<string, ConfigValue>(members ?? new Dictionary<string, ConfigValue>(), StringComparer.Ordinal));
        }

        public static ConfigValue Scalar(string token) =>
            new ConfigValue(ConfigNodeKind.Scalar, token, !string.IsNullOrEmpty(token), null, null);
        public static ConfigValue List(IEnumerable<ConfigValue> items, string rawToken = "", bool hasValue = true) =>
            new ConfigValue(ConfigNodeKind.List, rawToken, hasValue, items, null);
        public static ConfigValue Group(IDictionary<string, ConfigValue> members, string rawToken = "", bool hasValue = true) =>
            new ConfigValue(ConfigNodeKind.Group, rawToken, hasValue, null, members);

        // Field paths use '/' between named group members and zero-based list indices.
        public ConfigValue At(string fieldPath)
        {
            var value = this;
            foreach (var segment in fieldPath.Split('/'))
                value = value.Kind == ConfigNodeKind.Group ? value.Members[segment] : value.Items[int.Parse(segment)];
            return value;
        }

        public ConfigValue With(string fieldPath, ConfigValue replacement)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            return With(fieldPath.Split('/'), 0, replacement);
        }

        private ConfigValue With(string[] segments, int depth, ConfigValue replacement)
        {
            if (depth == segments.Length) return replacement;
            if (Kind == ConfigNodeKind.Group)
            {
                var copy = Members.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                copy[segments[depth]] = copy[segments[depth]].With(segments, depth + 1, replacement);
                return Group(copy, RawToken, HasValue);
            }
            if (Kind != ConfigNodeKind.List) throw new ArgumentException("字段路径穿过了标量。");
            var items = Items.ToArray();
            var index = int.Parse(segments[depth]);
            items[index] = items[index].With(segments, depth + 1, replacement);
            return List(items, RawToken, HasValue);
        }

        public bool Equals(ConfigValue other)
        {
            if (other == null || Kind != other.Kind || HasValue != other.HasValue) return false;
            if (Kind == ConfigNodeKind.Scalar) return RawToken == other.RawToken;
            if (Kind == ConfigNodeKind.List) return Items.SequenceEqual(other.Items);
            return Members.Count == other.Members.Count && Members.All(pair =>
                other.Members.TryGetValue(pair.Key, out var value) && pair.Value.Equals(value));
        }

        public override bool Equals(object obj) => Equals(obj as ConfigValue);
        public override int GetHashCode() => Kind == ConfigNodeKind.Scalar ? RawToken.GetHashCode() : (int)Kind;
    }

    public sealed class ConfigChoice
    {
        public string Id { get; }
        public string ChineseName { get; }
        public string DisplayName => $"{ChineseName}（{Id}）";
        public ConfigChoice(string id, string chineseName) { Id = id; ChineseName = chineseName; }
    }

    public sealed class ConfigFieldDefinition
    {
        public string Column { get; set; }
        public string ChineseName { get; set; }
        public string Unit { get; set; } = "";
        public ConfigControl Control { get; set; }
        public bool Editable { get; set; }
        public Func<ConfigValue, bool> CanEdit { get; set; }
        public string ReadOnlyReason { get; set; } = "尚未接入字段适配";
        public bool AllowEmpty { get; set; }
        public string EmptyMeaning { get; set; } = "未指定 / 不适用";
        public string Help { get; set; } = "";
        public string ReservedDelimiters { get; set; } = "";
        // Stored integer = displayed decimal * scale (AP and MilliUnits: 1000).
        public decimal Scale { get; set; } = 1m;
        public Func<IReadOnlyList<ConfigChoice>> Choices { get; set; }
        public IReadOnlyList<ConfigFieldDefinition> Children { get; set; } = Array.Empty<ConfigFieldDefinition>();
        public ConfigFieldDefinition Element { get; set; }
        public Func<ConfigValue> NewElement { get; set; }
        public bool IsEditable(ConfigValue row) => Editable && (CanEdit == null || CanEdit(row));
    }

    public sealed class ConfigTableDefinition
    {
        public string TableId { get; set; }
        public string ChineseName { get; set; }
        public string CsvPath { get; set; }
        public ConfigHeaderMode HeaderMode { get; set; } = ConfigHeaderMode.Header;
        public IReadOnlyList<string> ExactColumns { get; set; } = Array.Empty<string>();
        public Func<ConfigCsvRow, string> RowKey { get; set; }
        public Func<ConfigCsvRow, string> RowLabel { get; set; }
        public IReadOnlyList<string> GroupingFields { get; set; } = Array.Empty<string>();
        public IReadOnlyList<ConfigFieldDefinition> Fields { get; set; } = Array.Empty<ConfigFieldDefinition>();
        public IConfigRowCodec Codec { get; set; }
        public string ReadOnlyReason { get; set; } = "领域适配尚未接入";
        public bool IsAdapted => Codec != null && RowKey != null && ExactColumns.Count > 0 &&
            Fields.Count == ExactColumns.Count && ExactColumns.All(column => Fields.Count(field => field.Column == column) == 1);
    }

    public interface IConfigTableProvider { IEnumerable<ConfigTableDefinition> GetTables(); }

    public interface IConfigRowCodec
    {
        ConfigValue DecodeRow(ConfigCsvRow row);
        IReadOnlyDictionary<string, string> EncodeRow(ConfigValue row);
        IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot);
        IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot);
        IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> changedTables);
    }

    public sealed class ConfigIssue
    {
        public string TableId { get; }
        public string RowKey { get; }
        public string FieldPath { get; }
        public string ChineseReason { get; }
        public string ErrorCode { get; }
        public ConfigIssue(string tableId, string rowKey, string fieldPath, string chineseReason, string errorCode)
        {
            TableId = tableId; RowKey = rowKey; FieldPath = fieldPath;
            ChineseReason = chineseReason; ErrorCode = errorCode;
        }
    }

    public sealed class ConfigEdit
    {
        public string TableId { get; }
        public string RowKey { get; }
        public string FieldPath { get; }
        public ConfigValue Value { get; }
        public ConfigEdit(string tableId, string rowKey, string fieldPath, ConfigValue value)
        { TableId = tableId; RowKey = rowKey; FieldPath = fieldPath; Value = value; }
    }

    public sealed class ConfigImportStep
    {
        public string Method { get; set; }
        public IReadOnlyList<string> InputPaths { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> ProductPaths { get; set; } = Array.Empty<string>();
    }

    public interface IConfigApplyService
    {
        ConfigOperationResult ValidateAndSave(ConfigDraftSession session);
        ConfigOperationResult SaveAndApply(ConfigDraftSession session);
    }

    public sealed class ConfigOperationResult
    {
        public IReadOnlyList<string> SavedPaths { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> FailedSavePaths { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> AppliedPaths { get; set; } = Array.Empty<string>();
        public string FailedImportStep { get; set; } = "";
        public IReadOnlyList<ConfigIssue> Issues { get; set; } = Array.Empty<ConfigIssue>();
        public string Evidence { get; set; } = "";
    }
}
