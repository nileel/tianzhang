using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class ConfigCsvRow
    {
        public int PhysicalLine { get; }
        public IReadOnlyDictionary<string, string> Cells { get; }
        public string this[string column] => Cells[column];
        internal ConfigCsvRow(int physicalLine, IDictionary<string, string> cells)
        { PhysicalLine = physicalLine; Cells = new ReadOnlyDictionary<string, string>(cells); }
    }

    public sealed class ConfigCellEdit
    {
        public int PhysicalLine { get; }
        public int ColumnIndex { get; }
        public string Value { get; }
        public ConfigCellEdit(int physicalLine, int columnIndex, string value)
        { PhysicalLine = physicalLine; ColumnIndex = columnIndex; Value = value ?? ""; }
    }

    public sealed class CsvConfigDocument
    {
        public sealed class Cell
        {
            public int Start { get; }
            public string Raw { get; }
            public string Value { get; }
            internal Cell(int start, string raw, string value) { Start = start; Raw = raw; Value = value; }
        }

        public sealed class Line
        {
            public int Index { get; }
            public string Text { get; }
            public string Ending { get; }
            public bool IsTrivia => string.IsNullOrWhiteSpace(Text) || Text.TrimStart().StartsWith("#", StringComparison.Ordinal);
            public IReadOnlyList<Cell> Cells { get; }
            public bool MalformedQuotes { get; }
            internal Line(int index, string text, string ending)
            {
                Index = index; Text = text; Ending = ending;
                var cells = new List<Cell>();
                var quoted = false;
                var quoteCount = 0;
                var start = 0;
                for (var i = 0; i <= text.Length; i++)
                {
                    if (i < text.Length && text[i] == '"') { quoted = !quoted; quoteCount++; }
                    if (i != text.Length && (text[i] != ',' || quoted)) continue;
                    var raw = text.Substring(start, i - start);
                    // Only a single outer pair is supported; RFC escaped quotes are forbidden.
                    if (quoteCount != 0 && (quoteCount != 2 || raw.Length < 2 || raw[0] != '"' || raw[raw.Length - 1] != '"'))
                        MalformedQuotes = true;
                    cells.Add(new Cell(start, raw, CsvTableReader.ParseRow(raw)[0]));
                    start = i + 1;
                    quoteCount = 0;
                }
                if (quoted) MalformedQuotes = true;
                Cells = cells.AsReadOnly();
            }
        }

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly byte[] originalBytes;
        public byte[] OriginalBytes => (byte[])originalBytes.Clone();
        public string BaselineHash { get; }
        public bool HasBom { get; }
        public IReadOnlyList<Line> Lines { get; }

        private CsvConfigDocument(byte[] bytes)
        {
            originalBytes = (byte[])bytes.Clone();
            BaselineHash = ComputeHash(bytes);
            HasBom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
            var text = Utf8.GetString(bytes, HasBom ? 3 : 0, bytes.Length - (HasBom ? 3 : 0));
            var lines = new List<Line>();
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '\r' && text[i] != '\n') continue;
                var ending = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : text[i].ToString();
                lines.Add(new Line(lines.Count, text.Substring(start, i - start), ending));
                i += ending.Length - 1;
                start = i + 1;
            }
            if (start < text.Length) lines.Add(new Line(lines.Count, text.Substring(start), ""));
            Lines = lines.AsReadOnly();
        }

        public static CsvConfigDocument Load(byte[] bytes) => new CsvConfigDocument(bytes ?? throw new ArgumentNullException(nameof(bytes)));

        public static string ComputeHash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public IReadOnlyList<ConfigIssue> ValidateStructure(ConfigTableDefinition definition)
        {
            var issues = new List<ConfigIssue>();
            var data = Lines.Where(line => !line.IsTrivia).ToArray();
            void Error(Line line, string message, string code) => issues.Add(new ConfigIssue(
                definition.TableId, line == null ? "" : $"物理行{line.Index + 1}", "", message, code));
            if (definition.ExactColumns.Count == 0 || definition.ExactColumns.Distinct(StringComparer.Ordinal).Count() != definition.ExactColumns.Count)
                Error(null, "表的精确列声明缺失或重复。", "column_contract");
            foreach (var line in data)
            {
                if (line.MalformedQuotes) Error(line, "引号格式不受现有CSV读取器支持。", "csv_quotes");
                if (line.Cells.Count != definition.ExactColumns.Count) Error(line, "列宽与字段合同不一致。", "csv_width");
            }
            if (definition.HeaderMode != ConfigHeaderMode.None)
            {
                if (data.Length == 0) Error(null, "缺少表头。", "csv_header");
                else
                {
                    if (definition.HeaderMode == ConfigHeaderMode.FirstPhysicalLine && data[0].Index != 0)
                        Error(data[0], "表头必须位于物理第0行。", "csv_header_position");
                    if (!data[0].Cells.Select(cell => cell.Value).SequenceEqual(definition.ExactColumns))
                        Error(data[0], "表头存在未知、重复、缺失或顺序不符的列。", "csv_columns");
                }
            }
            return issues.AsReadOnly();
        }

        public IReadOnlyList<ConfigCsvRow> GetRows(ConfigTableDefinition definition)
        {
            if (ValidateStructure(definition).Count > 0) throw new InvalidDataException("CSV结构未通过合同校验。");
            var lines = Lines.Where(line => !line.IsTrivia);
            if (definition.HeaderMode != ConfigHeaderMode.None) lines = lines.Skip(1);
            return lines.Select(line => new ConfigCsvRow(line.Index,
                definition.ExactColumns.Select((column, index) => new { column, index })
                    .ToDictionary(pair => pair.column, pair => line.Cells[pair.index].Value, StringComparer.Ordinal))).ToArray();
        }

        public byte[] EncodeEdits(IEnumerable<ConfigCellEdit> edits)
        {
            var replacements = new Dictionary<(int line, int column), string>();
            foreach (var edit in edits ?? Array.Empty<ConfigCellEdit>())
            {
                if (edit.PhysicalLine < 0 || edit.PhysicalLine >= Lines.Count) throw new ArgumentOutOfRangeException(nameof(edits));
                var line = Lines[edit.PhysicalLine];
                if (line.IsTrivia || line.MalformedQuotes) throw new InvalidDataException("注释或畸形行不能编辑。");
                if (edit.ColumnIndex < 0 || edit.ColumnIndex >= line.Cells.Count) throw new ArgumentOutOfRangeException(nameof(edits));
                ValidateToken(edit.Value);
                if (!replacements.TryAdd((edit.PhysicalLine, edit.ColumnIndex), edit.Value))
                    throw new InvalidDataException("同一单元格出现重复编辑。");
            }
            if (replacements.Count == 0) return OriginalBytes;
            var output = new StringBuilder();
            foreach (var line in Lines)
            {
                if (line.IsTrivia) output.Append(line.Text);
                else
                {
                    for (var i = 0; i < line.Cells.Count; i++)
                    {
                        if (i > 0) output.Append(',');
                        var cell = line.Cells[i];
                        output.Append(replacements.TryGetValue((line.Index, i), out var value) && value != cell.Value
                            ? (value.Contains(",") ? $"\"{value}\"" : value) : cell.Raw);
                    }
                }
                output.Append(line.Ending);
            }
            var encoded = Utf8.GetBytes(output.ToString());
            return HasBom ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(encoded).ToArray() : encoded;
        }

        public static void ValidateToken(string value)
        {
            if (value == null || value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
                throw new ArgumentException("输入不得包含双引号或换行。");
        }
    }
}
