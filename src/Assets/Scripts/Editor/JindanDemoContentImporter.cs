using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TianZhang.Cultivation;
using TianZhang.Entity;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Editor
{
    /// <summary>Registers D1 sources, compiles the finite contracts and imports the existing identity roots.</summary>
    public static class JindanDemoContentImporter
    {
        public const string CatalogPath = "Assets/Data/JindanDemo/JindanDemoCatalog.asset";
        public static readonly string[] DefinitionTables =
        {
            "JindanDemoPresets", "JindanDemoSeats", "JindanDemoAbilities", "JindanDemoCommands",
            "JindanDemoContracts", "JindanDemoLoadoutTuning", "JindanDemoSegments",
        };
        public static string[] DefinitionPaths => DefinitionTables.Select(t => "Assets/DataConfig/" + t + ".csv").ToArray();
        public static string FoundationPath(string id) => "Assets/Data/FoundationPurpleMansionStates/FoundationPurpleMansionState_" + AssetCommitter.SanitizeName(id) + ".asset";
        public static string StaticPath(string id) => "Assets/Data/JindanStaticStates/JindanStaticState_" + AssetCommitter.SanitizeName(id) + ".asset";

        public static JindanDemoCatalogData ReadDefinitions()
        {
            var catalog = JindanDemoCsv.ParseDefinitions(ReadTable);
            try { ValidateSources(catalog); return catalog; }
            catch { UnityEngine.Object.DestroyImmediate(catalog); throw; }
        }

        public static JindanStaticReferenceCatalog ReadReferenceCatalog(FoundationPurpleMansionStateData[] foundations)
        {
            var catalog = ReadDefinitions();
            try
            {
                foreach (var state in foundations) JindanDemoCatalogValidator.ValidateFoundation(catalog, state);
                return JindanDemoCompatibilityCompiler.BuildReferences(catalog, foundations);
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }

        public static FoundationPurpleMansionStateData[] ParseFoundationStates(string[] lines)
        {
            var catalog = ReadDefinitions();
            try { return CultivationContentImporter.ParseFoundationPurpleMansionStates(lines,
                "FoundationPurpleMansionStates", false, catalog); }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }

        public static JindanStaticStateData[] ParseStaticStates(string[] lines, FoundationPurpleMansionStateData[] foundations)
        {
            var catalog = ReadDefinitions();
            try
            {
                foreach (var state in foundations) JindanDemoCatalogValidator.ValidateFoundation(catalog, state);
                var references = JindanDemoCompatibilityCompiler.BuildReferences(catalog, foundations);
                return CultivationContentImporter.ParseJindanStaticStates(JindanDemoCsv.MaterializeCompatibility(lines, catalog),
                    "JindanStaticStates", references, false);
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }

        public static JindanDemoCatalogData ReadPreview(string[] foundationLines = null, string[] staticLines = null)
        {
            var catalog = ReadDefinitions();
            FoundationPurpleMansionStateData[] foundations = null;
            JindanStaticStateData[] states = null;
            try
            {
                foundations = CultivationContentImporter.ParseFoundationPurpleMansionStates(
                    foundationLines ?? ReadTable("FoundationPurpleMansionStates"), "FoundationPurpleMansionStates", false, catalog);
                states = CultivationContentImporter.ParseJindanStaticStates(
                    JindanDemoCsv.MaterializeCompatibility(staticLines ?? ReadTable("JindanStaticStates"), catalog),
                    "JindanStaticStates", JindanDemoCompatibilityCompiler.BuildReferences(catalog, foundations), false);
                foreach (var preset in catalog.presets)
                {
                    preset.foundationState = foundations.Single(f => f.characterId == preset.characterId);
                    preset.jindanState = states.Single(s => s.characterId == preset.characterId);
                    preset.sourceCharacterDefinition = Required<CharacterData>("Assets/Data/Characters/Char_" + preset.sourceCharacterDefinitionId + ".asset");
                    preset.sourceGongFa = Required<GongFaGrowthData>("Assets/Data/GongFa/GongFa_" + preset.sourceGongFaId + ".asset");
                }
                if (!catalog.Validate(out string reason)) throw new InvalidDataException(reason);
                return catalog;
            }
            catch
            {
                DestroyTransient(foundations); DestroyTransient(states);
                UnityEngine.Object.DestroyImmediate(catalog); throw;
            }
        }

        [MenuItem("天章/内容/导入金丹演武目录")]
        public static void Import()
        {
            var preview = ReadPreview();
            var foundations = preview.presets.Select(p => p.foundationState).ToArray();
            var states = preview.presets.Select(p => p.jindanState).ToArray();
            try
            {
                // Complete source, identity and contract validation precedes all asset writes.
                CultivationContentImporter.CommitFoundationPurpleMansionStates(foundations);
                CultivationContentImporter.CommitJindanStaticStates(states);
                foreach (var preset in preview.presets)
                {
                    preset.foundationState = Required<FoundationPurpleMansionStateData>(FoundationPath(preset.characterId));
                    preset.jindanState = Required<JindanStaticStateData>(StaticPath(preset.characterId));
                }
                var existing = AssetDatabase.LoadAssetAtPath<JindanDemoCatalogData>(CatalogPath);
                if (existing == null)
                {
                    AssetCommitter.EnsureDirectory(CatalogPath);
                    existing = ScriptableObject.CreateInstance<JindanDemoCatalogData>();
                    EditorUtility.CopySerialized(preview, existing);
                    AssetDatabase.CreateAsset(existing, CatalogPath);
                }
                else { EditorUtility.CopySerialized(preview, existing); EditorUtility.SetDirty(existing); }
                AssetDatabase.SaveAssets();
                Debug.Log("[JindanDemoContentImporter] Imported five fixed presets and eight compiled contracts; only SPACE_SCALE is enabled.");
            }
            finally
            {
                DestroyTransient(foundations); DestroyTransient(states);
                UnityEngine.Object.DestroyImmediate(preview);
            }
        }

        public static void DestroyPreview(JindanDemoCatalogData preview)
        {
            if (preview == null) return;
            DestroyTransient(preview.presets?.Select(p => p.foundationState));
            DestroyTransient(preview.presets?.Select(p => p.jindanState));
            if (!EditorUtility.IsPersistent(preview)) UnityEngine.Object.DestroyImmediate(preview);
        }

        public static string[] IdentityProductPaths(bool foundations)
        {
            var catalog = ReadDefinitions();
            try { return catalog.presets.Select(p => foundations ? FoundationPath(p.characterId) : StaticPath(p.characterId)).ToArray(); }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }

        private static void ValidateSources(JindanDemoCatalogData catalog)
        {
            var language = ReadTable("Language").Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"))
                .Select(CsvTableReader.ParseRow).ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);
            var gongfas = Rows("GongFa").ToDictionary(r => r["name"], StringComparer.Ordinal);
            var characters = Rows("Characters").Select(r => r["name"]).ToHashSet(StringComparer.Ordinal);
            foreach (var ability in catalog.abilities)
            {
                string source = Source(ability.sourceDocument);
                Require(source.Contains(ability.definitionId), "SOURCE_DEFINITION_ID");
                Require(language.ContainsKey(ability.displayNameKey), "LANGUAGE_KEY");
            }
            foreach (var seat in catalog.seats)
            {
                string source = Source(seat.sourceDocument);
                Require(source.Contains(seat.positionId) && source.Contains(seat.positionVersion), "POSITION_SOURCE");
            }
            foreach (var preset in catalog.presets)
            {
                Require(gongfas.ContainsKey(preset.sourceGongFaId) && characters.Contains(preset.sourceCharacterDefinitionId), "CHARACTER_SOURCE");
                // The selected existing scattered-cultivator source explicitly permits every root.
                string gongfa = Source(preset.sourceGongFaDocument);
                Require(gongfa.Contains(language[preset.sourceGongFaId]) && gongfa.Contains("任意灵根") &&
                        gongfa.Contains("凡品或以上"), "GONGFA_ROOT_QUALIFICATION");
                Require(language.ContainsKey(preset.displayNameKey) && language.ContainsKey(preset.danxiangNameKey), "LANGUAGE_KEY");
            }
            foreach (var command in catalog.commands)
            {
                Require(Source(command.sourceDocument).Contains(command.abilityDefinitionId), "COMMAND_SOURCE");
                Require(language.ContainsKey(command.displayNameKey) && language.ContainsKey(command.additionalRulesKey) &&
                        (command.enabled || language.ContainsKey(command.unavailableReasonKey)), "LANGUAGE_KEY");
            }
            foreach (var segment in catalog.segments) Require(language.ContainsKey(segment.displayNameKey), "LANGUAGE_KEY");
            ValidateScaleDocument(catalog);
        }

        private static void ValidateScaleDocument(JindanDemoCatalogData catalog)
        {
            var deploy = catalog.GetCommand("demo_20261024_scale_deploy");
            var close = catalog.GetCommand("demo_20261024_scale_terminate");
            string source = Source(deploy.sourceDocument);
            Require(source.Contains($"合计报价{deploy.actionPointCostUnits / 1000}AP／{deploy.manaCost}MP／CT{deploy.ctPenalty}／CD{deploy.cooldownTicks}tick") &&
                    source.Contains($"额外支付{deploy.remoteOriginManaCost}MP") &&
                    source.Contains($"方位标耐久{deploy.supportDurability}") &&
                    source.Contains($"原子支付{deploy.upkeepMana}MP") &&
                    source.Contains($"{close.actionPointCostUnits / 1000}AP／MP{close.manaCost}／CT{close.ctPenalty}／CD{close.cooldownTicks}"), "SCALE_SOURCE_PRICE");
        }

        private static Dictionary<string, string>[] Rows(string table)
        {
            string[] lines = ReadTable(table), headers = CsvTableReader.FindHeader(lines);
            return lines.Skip(CsvTableReader.FindHeaderIndex(lines) + 1)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"))
                .Select(CsvTableReader.ParseRow).Select(c => headers.Select((h, i) => (h, i))
                    .ToDictionary(x => x.h, x => c[x.i], StringComparer.Ordinal)).ToArray();
        }

        private static string Source(string relative)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string path = Path.GetFullPath(Path.Combine(root, relative));
            Require(path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path), "MISSING_SOURCE_DOCUMENT");
            return File.ReadAllText(path);
        }

        private static string[] ReadTable(string table) => File.ReadAllLines("Assets/DataConfig/" + table + ".csv");
        private static T Required<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path)
            ?? throw new InvalidDataException("JD_DEMO_MISSING_ASSET: " + path);
        private static void DestroyTransient<T>(IEnumerable<T> objects) where T : UnityEngine.Object
        {
            if (objects == null) return;
            foreach (var value in objects) if (value != null && !EditorUtility.IsPersistent(value)) UnityEngine.Object.DestroyImmediate(value);
        }
        private static void Require(bool value, string reason)
        { if (!value) throw new InvalidDataException("JD_DEMO_" + reason); }
    }
}
