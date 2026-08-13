using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TianZhang.Editor
{
    /// <summary>Domain-neutral CSV row reader. It has no content field knowledge.</summary>
    public static class CsvTableReader
    {
        public static string[] ReadRequired(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("CSV was not found.", path);
            return File.ReadAllLines(path);
        }

        public static string[] ParseRow(string line)
        {
            var values = new List<string>();
            var current = string.Empty;
            var quoted = false;
            foreach (var character in line ?? string.Empty)
            {
                if (character == '"') { quoted = !quoted; continue; }
                if (character == ',' && !quoted) { values.Add(current); current = string.Empty; continue; }
                current += character;
            }
            values.Add(current);
            return values.ToArray();
        }

        public static int FindHeaderIndex(string[] lines)
        {
            if (lines == null)
                return -1;

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                    return index;
            }

            return -1;
        }

        public static string[] FindHeader(string[] lines)
        {
            var index = FindHeaderIndex(lines);
            return index >= 0 ? ParseRow(lines[index]) : Array.Empty<string>();
        }

        public static string GetValueOrDefault(
            string[] headers,
            string[] columns,
            string columnName,
            string defaultValue)
        {
            if (headers == null || columns == null)
                return defaultValue;

            var index = FindColumnIndex(headers, columnName);
            if (index < 0 || index >= columns.Length)
                return defaultValue;

            var value = columns[index]?.Trim();
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        public static string GetRequiredValue(
            string[] headers,
            string[] columns,
            string columnName,
            string sourceName)
        {
            var index = FindColumnIndex(headers, columnName);
            if (index < 0)
                throw new InvalidDataException($"{sourceName} missing required column '{columnName}'.");
            if (columns == null || index >= columns.Length)
                throw new InvalidDataException($"{sourceName} row missing required column '{columnName}'.");

            var value = columns[index]?.Trim();
            if (string.IsNullOrEmpty(value))
                throw new InvalidDataException($"{sourceName} row has empty required column '{columnName}'.");

            return value;
        }

        public static void RequireColumns(string[] headers, string sourceName, params string[] columnNames)
        {
            foreach (var columnName in columnNames)
            {
                if (FindColumnIndex(headers, columnName) < 0)
                    throw new InvalidDataException($"{sourceName} missing required column '{columnName}'.");
            }
        }

        public static void RequireExactColumns(string[] headers, string sourceName, params string[] columnNames)
        {
            RequireColumns(headers, sourceName, columnNames);
            if (headers.Length != columnNames.Length)
            {
                throw new InvalidDataException(
                    $"{sourceName} has {headers.Length} columns; expected exactly {columnNames.Length}.");
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                var normalized = header?.Trim();
                if (!seen.Add(normalized) || !columnNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"{sourceName} has duplicate or unknown column '{header}'.");
                }
            }
        }

        public static int FindColumnIndex(string[] headers, string columnName)
        {
            if (headers == null)
                return -1;

            return Array.FindIndex(headers, header =>
                string.Equals(header?.Trim(), columnName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
