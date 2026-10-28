using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Editor
{
    /// <summary>Strict scalar/list projection for the explicitly typed demo definition tables.</summary>
    public static class JindanDemoCsv
    {
        public static T[] Parse<T>(string[] lines, string source) where T : new()
        {
            if (lines == null) throw new InvalidDataException("JD_DEMO_MISSING_TABLE: " + source);
            int first = CsvTableReader.FindHeaderIndex(lines);
            if (first < 0) throw new InvalidDataException("JD_DEMO_MISSING_HEADER: " + source);
            string[] headers = CsvTableReader.FindHeader(lines);
            var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)).ToArray();
            if (headers.Distinct(StringComparer.Ordinal).Count() != headers.Length ||
                !new HashSet<string>(fields.Select(f => f.Name), StringComparer.Ordinal).SetEquals(headers))
                throw new InvalidDataException("JD_DEMO_HEADER_MISMATCH: " + source);
            var rows = new List<T>();
            for (int i = first + 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].TrimStart().StartsWith("#")) continue;
                string[] values = CsvTableReader.ParseRow(lines[i]);
                if (values.Length != headers.Length) throw new InvalidDataException("JD_DEMO_ROW_WIDTH: " + source);
                var row = new T();
                foreach (var field in fields)
                {
                    string value = values[Array.IndexOf(headers, field.Name)];
                    if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("JD_DEMO_MISSING_VALUE: " + source + "/" + field.Name);
                    field.SetValue(row, ConvertValue(field.FieldType, value, source));
                }
                rows.Add(row);
            }
            return rows.ToArray();
        }

        public static JindanDemoCatalogData ParseDefinitions(Func<string, string[]> read)
        {
            var catalog = ScriptableObject.CreateInstance<JindanDemoCatalogData>();
            try
            {
                catalog.contentVersion = "demo_20261024_v1";
                catalog.presets = Parse<JindanDemoPresetDefinition>(read("JindanDemoPresets"), "JindanDemoPresets");
                catalog.seats = Parse<JindanDemoSeatDefinition>(read("JindanDemoSeats"), "JindanDemoSeats");
                catalog.abilities = Parse<JindanDemoAbilityDefinition>(read("JindanDemoAbilities"), "JindanDemoAbilities");
                catalog.commands = Parse<JindanDemoCommandDefinition>(read("JindanDemoCommands"), "JindanDemoCommands");
                catalog.contracts = Parse<JindanDemoCompatibilityContract>(read("JindanDemoContracts"), "JindanDemoContracts");
                catalog.segments = Parse<JindanDemoSegmentDefinition>(read("JindanDemoSegments"), "JindanDemoSegments");
                catalog.loadoutTuning = Parse<JindanDemoLoadoutTuning>(read("JindanDemoLoadoutTuning"), "JindanDemoLoadoutTuning").Single();
                catalog.demolition = Parse<JindanDemoDemolitionDefinition>(read("JindanDemoDemolition"), "JindanDemoDemolition").Single();
                catalog.surfaceRules = Parse<JindanDemoSurfaceRules>(read("JindanDemoSurfaceRules"), "JindanDemoSurfaceRules").Single();
                catalog.surfaceCells = Parse<JindanDemoSurfaceCell>(read("JindanDemoSurfaceCells"), "JindanDemoSurfaceCells");
                catalog.surfaceFires = Parse<JindanDemoSurfaceFire>(read("JindanDemoSurfaceFires"), "JindanDemoSurfaceFires");
                catalog.sealRules = Parse<JindanDemoSealRules>(read("JindanDemoSealRules"), "JindanDemoSealRules").Single();
                catalog.fireDomainRules = Parse<JindanDemoFireDomainRules>(read("JindanDemoFireDomainRules"), "JindanDemoFireDomainRules").Single();
                catalog.visibilityRules = Parse<JindanDemoVisibilityRules>(read("JindanDemoVisibilityRules"), "JindanDemoVisibilityRules").Single();
                foreach (var contract in catalog.contracts)
                {
                    if (contract.compatibilityProfileId != "compile")
                        throw new InvalidDataException("JD_DEMO_COMPILED_ID_NOT_INPUT");
                    contract.compatibilityProfileId = JindanDemoCompatibilityCompiler.Compile(catalog, contract);
                }
                if (!JindanDemoCatalogValidator.Validate(catalog, false, out string reason)) throw new InvalidDataException(reason);
                return catalog;
            }
            catch { UnityEngine.Object.DestroyImmediate(catalog); throw; }
        }

        public static string[] MaterializeCompatibility(string[] lines, JindanDemoCatalogData catalog)
        {
            // This is a compiler reference in authoring CSV; persistent assets contain only the validated hash.
            return lines.Select(line =>
            {
                foreach (var contract in catalog.contracts)
                    line = line.Replace("compile_" + contract.contractId, contract.compatibilityProfileId);
                if (line.Contains("compile_")) throw new InvalidDataException("JD_DEMO_UNKNOWN_COMPILE_REFERENCE");
                return line;
            }).ToArray();
        }

        private static object ConvertValue(Type type, string value, string source)
        {
            if (type == typeof(string)) return value == "none" ? string.Empty : value;
            if (type == typeof(string[])) return value == "none" ? Array.Empty<string>() : value.Split('|');
            if (type == typeof(int[])) return value == "none" ? Array.Empty<int>() : value.Split('|').Select(ParseInt).ToArray();
            if (type == typeof(int)) return ParseInt(value);
            if (type == typeof(float) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) &&
                !float.IsInfinity(number) && !float.IsNaN(number)) return number;
            if (type == typeof(JindanDemoSegmentActor[]))
                return value.Split('|').Select(token =>
                {
                    string[] parts = token.Split('~');
                    if (parts.Length != 5) throw new InvalidDataException("JD_DEMO_SEGMENT_ACTOR");
                    return new JindanDemoSegmentActor { characterId = parts[0], team = ParseInt(parts[1]),
                        q = ParseInt(parts[2]), r = ParseInt(parts[3]), facing = ParseInt(parts[4]) };
                }).ToArray();
            if (type == typeof(bool) && (value == "true" || value == "false")) return value == "true";
            if (type.IsEnum && Enum.TryParse(type, value, false, out object parsed) && Enum.IsDefined(type, parsed)) return parsed;
            throw new InvalidDataException("JD_DEMO_INVALID_VALUE: " + source + "/" + value);
        }

        private static int ParseInt(string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                throw new InvalidDataException("JD_DEMO_INVALID_INTEGER: " + value);
            return parsed;
        }
    }
}
