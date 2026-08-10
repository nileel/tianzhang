using System;
using System.Collections.Generic;
using System.IO;

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
    }
}
