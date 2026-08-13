using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEditor;
using TianZhang.Entity;
using TianZhang.Content;
using TianZhang.Combat;
using TianZhang.Cultivation;
using TianZhang.Features.CharacterCreation;
using TianZhang.Game.CharacterCreation;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.World;

namespace TianZhang.Editor
{
    /// <summary>Owns the complete cultivation content import pipeline.</summary>
    public static class CultivationContentImporter
    {
        [MenuItem("天章/内容/导入修炼定义")]
        public static void Import()
        {
            ImportGongFa();
            ImportFoundationPurpleMansionStates();
            ImportJindanStaticStates();
            ImportNpcCultivationActionWeightProfiles();
        }

        private static Dictionary<string, string> _lang;

        /// <summary>加载语言表</summary>
        static Dictionary<string, string> LoadLanguage()
        {
            if (_lang != null) return _lang;
            _lang = new Dictionary<string, string>();
            string path = "Assets/DataConfig/Language.csv";
            if (!File.Exists(path)) { Debug.LogWarning($"[Importer] Language.csv not found, IDs will be used as-is"); return _lang; }
            var lines = File.ReadAllLines(path);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = CsvTableReader.ParseRow(line);
                if (cols.Length >= 2 && !string.IsNullOrEmpty(cols[0]))
                    _lang[cols[0]] = cols[1];
            }
            Debug.Log($"[Importer] Loaded {_lang.Count} language entries");
            return _lang;
        }

        /// <summary>解析文本 ID → 显示文本</summary>
        static string T(string id) => LoadLanguage().TryGetValue(id, out var text) ? text : id;
        private static readonly string[] FoundationPurpleMansionColumns =
        {
            "schemaId",
            "schemaVersion",
            "characterId",
            "foundationInstanceId",
            "foundationDefinitionId",
            "sourceGongFaId",
            "phase",
            "continuousProgress",
            "phaseBoundarySetId",
            "naturalMansionCapacity",
            "releasedNaturalCapacity",
            "expansionGrants",
            "expandedMansionCapacity",
            "totalMansionCapacity",
            "mansionStates",
            "effectBindings",
            "guardianAbilities",
            "enhancementNodes",
            "cultivationActionState",
            "closedRetreatPlan",
            "jindanLock",
            "fixtureId",
            "expect",
            "fixtureOnlyNumericProfile",
        };

        private static readonly string[] LegacyFoundationPurpleMansionColumns =
        {
            "developedMansions",
            "mansionBindings",
            "realmStage",
            "legacyDanJiType",
            "foundationGrade",
            "foundationStages",
        };

        private const string FoundationPurpleMansionSchemaId = "foundationPurpleMansionState";
        private const int FoundationPurpleMansionSchemaVersion = 1;

        private static readonly string[] JindanStaticColumns =
        {
            "schemaId",
            "schemaVersion",
            "characterId",
            "foundationPurpleMansionStateRef",
            "mansionInputs",
            "jindanCoreBinding",
            "danxiang",
            "stablePositionBindings",
            "abilityLedgerBindings",
            "fixtureId",
            "expect",
            "fixtureOnlyNumericProfile",
        };

        private static readonly string[] LegacyJindanStaticColumns =
        {
            "developedMansions",
            "mansionBindings",
            "realmStage",
            "legacyDanJiType",
            "displayName",
            "localizedName",
            "roadDisplayName",
            "positionDisplayName",
        };

        private const string JindanStaticSchemaId = "jindanStaticState";
        private const int JindanStaticSchemaVersion = 1;

        private static readonly string[] NpcCultivationActionWeightColumns =
        {
            "schemaId", "schemaVersion", "profileId", "sourceContentHash", "authorityKind", "recordKind", "recordId",
            "actionStableId", "legalityRuleSetRef", "baseWeight", "subjectiveRiskGateRef", "enabled", "sourceKind",
            "selectorRef", "priorityDelta", "applicationOrder", "capPolicyRef", "diminishingPolicyRef", "actionTotalCapPolicyRef",
            "scope", "minimum", "maximum", "appliesAfterSourceKind", "inputBasis", "activationThreshold", "segments",
            "outputBound", "tieBreakPolicy", "triggerStableId", "riskThresholdDelta", "knownEvidenceRefs", "riskAssessmentRef",
            "baseRiskThreshold", "lifespanCapPolicyRef",
        };

        [MenuItem("天章/导入 NPC 修炼行动权重")]
        public static void ImportNpcCultivationActionWeightProfiles()
        {
            const string path = "Assets/DataConfig/NpcCultivationActionWeightProfiles.csv";
            if (!File.Exists(path))
                throw new FileNotFoundException($"NPC cultivation action weight CSV was not found: {path}", path);

            var profiles = ParseNpcCultivationActionWeightProfiles(File.ReadAllLines(path), path);
            try
            {
                foreach (var profile in profiles)
                {
                    string assetPath = $"Assets/Data/NpcCultivationActionWeightProfiles/NpcCultivationActionWeightProfile_{AssetCommitter.SanitizeName(profile.profileId)}.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<NpcCultivationActionWeightProfileData>(assetPath);
                    bool isNew = asset == null;
                    if (isNew)
                    {
                        asset = ScriptableObject.CreateInstance<NpcCultivationActionWeightProfileData>();
                        AssetCommitter.EnsureDirectory(assetPath);
                    }

                    CopyNpcCultivationActionWeightProfile(profile, asset);
                    if (isNew)
                        AssetDatabase.CreateAsset(asset, assetPath);
                    else
                        EditorUtility.SetDirty(asset);
                }
            }
            finally
            {
                foreach (var profile in profiles)
                    UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        /// <summary>
        /// Parses the complete, single-authority source set before any asset can be created or updated.
        /// All record kinds remain in one profile so Unity and BattleSim can verify the same content hash.
        /// </summary>
        public static NpcCultivationActionWeightProfileData[] ParseNpcCultivationActionWeightProfiles(
            string[] lines,
            string sourceName)
        {
            if (lines == null)
                throw new InvalidDataException($"{sourceName} has no rows.");

            int headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw new InvalidDataException($"{sourceName} has no header row.");

            string[] headers = CsvTableReader.FindHeader(lines);
            CsvTableReader.RequireExactColumns(headers, sourceName, NpcCultivationActionWeightColumns);
            int hashIndex = Array.IndexOf(headers, "sourceContentHash");
            var rows = new List<string[]>();
            for (int index = headerLineIndex + 1; index < lines.Length; index++)
            {
                string line = lines[index];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;
                string[] columns = CsvTableReader.ParseRow(line);
                if (columns.Length != headers.Length)
                    throw new InvalidDataException($"{sourceName} row {index + 1} has {columns.Length} columns; expected {headers.Length}.");
                rows.Add(columns);
            }
            if (rows.Count == 0)
                throw new InvalidDataException($"{sourceName} has no data rows.");

            string contentHash = ComputeNpcCultivationSourceHash(headers, rows, hashIndex);
            var result = new List<NpcCultivationActionWeightProfileData>();
            foreach (var group in rows.GroupBy(row => CsvTableReader.GetRequiredValue(headers, row, "profileId", sourceName), StringComparer.Ordinal))
            {
                var profileRows = group.ToArray();
                string profileId = group.Key;
                var manifests = profileRows.Where(row => NpcValue(headers, row, "recordKind") == "MANIFEST").ToArray();
                if (manifests.Length != 1)
                    throw new InvalidDataException($"NPC_WEIGHT_DOUBLE_AUTHORITY: {profileId} must contain exactly one manifest.");

                string[] manifest = manifests[0];
                if (NpcRequired(headers, manifest, "schemaId", sourceName) != NpcCultivationActionWeightProfileRuntime.SchemaId ||
                    ParseNpcInteger(NpcRequired(headers, manifest, "schemaVersion", sourceName), sourceName, "schemaVersion") != NpcCultivationActionWeightProfileRuntime.SchemaVersion)
                {
                    throw new InvalidDataException($"NPC_WEIGHT_UNKNOWN_SCHEMA: {profileId} has an unsupported schema.");
                }
                if (NpcRequired(headers, manifest, "authorityKind", sourceName) != "CSV_SOURCE_SET" ||
                    NpcRequired(headers, manifest, "sourceContentHash", sourceName) != contentHash ||
                    NpcRequired(headers, manifest, "tieBreakPolicy", sourceName) != "LEXICOGRAPHIC_ASC")
                {
                    throw new InvalidDataException($"NPC_WEIGHT_DOUBLE_AUTHORITY: {profileId} has an invalid manifest authority or content hash.");
                }

                var profile = ScriptableObject.CreateInstance<NpcCultivationActionWeightProfileData>();
                try
                {
                    profile.schemaId = NpcCultivationActionWeightProfileRuntime.SchemaId;
                    profile.schemaVersion = NpcCultivationActionWeightProfileRuntime.SchemaVersion;
                    profile.profileId = profileId;
                    profile.sourceContentHash = contentHash;
                    profile.authorityKind = "CSV_SOURCE_SET";
                    profile.tieBreakPolicy = "LEXICOGRAPHIC_ASC";
                    profile.actionWeightRows = profileRows.Where(row => NpcValue(headers, row, "recordKind") == "ACTION")
                        .Select(row => new NpcCultivationActionWeightRecord
                        {
                            recordId = NpcRequired(headers, row, "recordId", sourceName),
                            actionStableId = NpcRequired(headers, row, "actionStableId", sourceName),
                            legalityRuleSetRef = NpcRequired(headers, row, "legalityRuleSetRef", sourceName),
                            baseWeight = ParseNpcFloat(NpcRequired(headers, row, "baseWeight", sourceName), sourceName, "baseWeight"),
                            subjectiveRiskGateRef = NpcValue(headers, row, "subjectiveRiskGateRef"),
                            enabled = ParseNpcBool(NpcRequired(headers, row, "enabled", sourceName), sourceName),
                            actionTotalCapPolicyRef = NpcRequired(headers, row, "actionTotalCapPolicyRef", sourceName),
                        }).ToArray();
                    profile.modifierRows = profileRows.Where(row => NpcValue(headers, row, "recordKind") == "MODIFIER")
                        .Select(row => new NpcCultivationWeightModifierRecord
                        {
                            modifierId = NpcRequired(headers, row, "recordId", sourceName),
                            sourceKind = NpcRequired(headers, row, "sourceKind", sourceName),
                            actionStableId = NpcRequired(headers, row, "actionStableId", sourceName),
                            selectorRef = NpcRequired(headers, row, "selectorRef", sourceName),
                            priorityDelta = ParseNpcFloat(NpcRequired(headers, row, "priorityDelta", sourceName), sourceName, "priorityDelta"),
                            applicationOrder = ParseNpcInteger(NpcRequired(headers, row, "applicationOrder", sourceName), sourceName, "applicationOrder"),
                            capPolicyRef = NpcRequired(headers, row, "capPolicyRef", sourceName),
                            diminishingPolicyRef = NpcRequired(headers, row, "diminishingPolicyRef", sourceName),
                            riskThresholdDelta = ParseNpcFloat(NpcValue(headers, row, "riskThresholdDelta", "0"), sourceName, "riskThresholdDelta"),
                        }).ToArray();
                    profile.capPolicies = profileRows.Where(row => NpcValue(headers, row, "recordKind") == "CAP_POLICY")
                        .Select(row => new NpcCultivationWeightCapPolicy
                        {
                            capPolicyId = NpcRequired(headers, row, "recordId", sourceName),
                            scope = NpcRequired(headers, row, "scope", sourceName),
                            minimum = ParseNpcFloat(NpcRequired(headers, row, "minimum", sourceName), sourceName, "minimum"),
                            maximum = ParseNpcFloat(NpcRequired(headers, row, "maximum", sourceName), sourceName, "maximum"),
                            appliesAfterSourceKind = NpcValue(headers, row, "appliesAfterSourceKind"),
                        }).ToArray();
                    profile.diminishingPolicies = profileRows.Where(row => NpcValue(headers, row, "recordKind") == "DIMINISHING_POLICY")
                        .Select(row => new NpcCultivationWeightDiminishingPolicy
                        {
                            diminishingPolicyId = NpcRequired(headers, row, "recordId", sourceName),
                            scope = NpcRequired(headers, row, "scope", sourceName),
                            inputBasis = NpcRequired(headers, row, "inputBasis", sourceName),
                            activationThreshold = ParseNpcFloat(NpcRequired(headers, row, "activationThreshold", sourceName), sourceName, "activationThreshold"),
                            segments = NpcRequired(headers, row, "segments", sourceName),
                            outputBound = ParseNpcFloat(NpcRequired(headers, row, "outputBound", sourceName), sourceName, "outputBound"),
                        }).ToArray();
                    profile.riskGates = profileRows.Where(row => NpcValue(headers, row, "recordKind") == "RISK_GATE")
                        .Select(row => new NpcCultivationRiskGate
                        {
                            riskGateRef = NpcRequired(headers, row, "recordId", sourceName),
                            knownEvidenceRefs = NpcRequired(headers, row, "knownEvidenceRefs", sourceName).Split('|'),
                            riskAssessmentRef = NpcRequired(headers, row, "riskAssessmentRef", sourceName),
                            baseRiskThreshold = ParseNpcFloat(NpcRequired(headers, row, "baseRiskThreshold", sourceName), sourceName, "baseRiskThreshold"),
                            lifespanCapPolicyRef = NpcRequired(headers, row, "lifespanCapPolicyRef", sourceName),
                        }).ToArray();
                    profile.recalculationTriggers = profileRows.Where(row => NpcValue(headers, row, "recordKind") == "TRIGGER")
                        .Select(row => new NpcCultivationRecalculationTrigger
                        {
                            triggerStableId = NpcRequired(headers, row, "triggerStableId", sourceName),
                        }).ToArray();

                    EnsureNpcUniqueRecords(profile, sourceName);
                    if (!NpcCultivationActionWeightProfileRuntime.TryCreate(profile, out _, out string failureReason))
                        throw new InvalidDataException($"{failureReason}: {profileId} did not form a valid runtime projection.");
                    foreach (var policy in profile.diminishingPolicies)
                        ValidateNpcDiminishingSegments(policy, sourceName);
                    result.Add(profile);
                }
                catch
                {
                    UnityEngine.Object.DestroyImmediate(profile);
                    throw;
                }
            }
            return result.ToArray();
        }

        private static void CopyNpcCultivationActionWeightProfile(
            NpcCultivationActionWeightProfileData source,
            NpcCultivationActionWeightProfileData destination)
        {
            destination.schemaId = source.schemaId;
            destination.schemaVersion = source.schemaVersion;
            destination.profileId = source.profileId;
            destination.sourceContentHash = source.sourceContentHash;
            destination.authorityKind = source.authorityKind;
            destination.tieBreakPolicy = source.tieBreakPolicy;
            destination.actionWeightRows = source.actionWeightRows;
            destination.modifierRows = source.modifierRows;
            destination.capPolicies = source.capPolicies;
            destination.diminishingPolicies = source.diminishingPolicies;
            destination.riskGates = source.riskGates;
            destination.recalculationTriggers = source.recalculationTriggers;
        }

        private static void EnsureNpcUniqueRecords(NpcCultivationActionWeightProfileData profile, string sourceName)
        {
            var recordIds = profile.actionWeightRows.Select(row => row.recordId)
                .Concat(profile.modifierRows.Select(row => row.modifierId))
                .Concat(profile.capPolicies.Select(row => row.capPolicyId))
                .Concat(profile.diminishingPolicies.Select(row => row.diminishingPolicyId))
                .Concat(profile.riskGates.Select(row => row.riskGateRef))
                .Concat(profile.recalculationTriggers.Select(row => row.triggerStableId))
                .ToArray();
            if (recordIds.Distinct(StringComparer.Ordinal).Count() != recordIds.Length)
                throw new InvalidDataException($"NPC_WEIGHT_DUPLICATE_RECORD: {sourceName} contains a duplicate stable record ID.");
        }

        private static void ValidateNpcDiminishingSegments(NpcCultivationWeightDiminishingPolicy policy, string sourceName)
        {
            float expectedLower = 0;
            foreach (string segment in policy.segments.Split('|'))
            {
                string[] parts = segment.Split('@');
                string[] bounds = parts[0].Split('-');
                if (parts.Length != 2 || bounds.Length != 2 ||
                    !float.TryParse(bounds[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float lower) ||
                    !float.TryParse(bounds[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float upper) ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float multiplier) ||
                    lower != expectedLower || upper <= lower || multiplier < 0)
                {
                    throw new InvalidDataException($"NPC_WEIGHT_INVALID_POLICY: {sourceName} has invalid diminishing segments for '{policy.diminishingPolicyId}'.");
                }
                expectedLower = upper;
            }
        }

        private static string ComputeNpcCultivationSourceHash(string[] headers, IEnumerable<string[]> rows, int hashIndex)
        {
            var canonicalRows = rows.Select(row =>
            {
                var copy = row.ToArray();
                copy[hashIndex] = string.Empty;
                return string.Join(",", copy);
            });
            string canonical = string.Join("\n", new[] { string.Join(",", headers) }.Concat(canonicalRows));
            byte[] bytes;
            using (var sha256 = SHA256.Create())
            {
                bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            }

            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string NpcValue(string[] headers, string[] columns, string name, string defaultValue = "") =>
            CsvTableReader.GetValueOrDefault(headers, columns, name, defaultValue);

        private static string NpcRequired(string[] headers, string[] columns, string name, string sourceName) =>
            CsvTableReader.GetRequiredValue(headers, columns, name, sourceName);

        private static int ParseNpcInteger(string value, string sourceName, string fieldName)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                throw new InvalidDataException($"NPC_WEIGHT_MISSING_EXPLICIT_VALUE: {sourceName} has invalid {fieldName}.");
            return result;
        }

        private static float ParseNpcFloat(string value, string sourceName, string fieldName)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) || float.IsNaN(result) || float.IsInfinity(result))
                throw new InvalidDataException($"NPC_WEIGHT_MISSING_EXPLICIT_VALUE: {sourceName} has invalid {fieldName}.");
            return result;
        }

        private static bool ParseNpcBool(string value, string sourceName)
        {
            if (value == "true") return true;
            if (value == "false") return false;
            throw new InvalidDataException($"NPC_WEIGHT_MISSING_EXPLICIT_VALUE: {sourceName} has invalid enabled flag.");
        }

        [MenuItem("天章/导入道基紫府状态配置")]
        [MenuItem("天章/导入金丹静态状态配置")]
        public static void ImportJindanStaticStates()
        {
            const string path = "Assets/DataConfig/JindanStaticStates.csv";
            if (!File.Exists(path))
                throw new FileNotFoundException($"Jindan static state CSV was not found: {path}", path);

            // No production authority table exists in this slice. A non-empty production table must
            // therefore fail closed instead of treating fixture identifiers as live content.
            var states = ParseJindanStaticStates(
                File.ReadAllLines(path),
                path,
                new JindanStaticReferenceCatalog(),
                allowFixtures: false);
            try
            {
                foreach (var state in states)
                {
                    string assetPath =
                        $"Assets/Data/JindanStaticStates/JindanStaticState_{AssetCommitter.SanitizeName(state.characterId)}.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<JindanStaticStateData>(assetPath);
                    bool isNew = asset == null;
                    if (isNew)
                    {
                        asset = ScriptableObject.CreateInstance<JindanStaticStateData>();
                        AssetCommitter.EnsureDirectory(assetPath);
                    }

                    CopyJindanStaticState(state, asset);
                    if (isNew)
                        AssetDatabase.CreateAsset(asset, assetPath);
                    else
                        EditorUtility.SetDirty(asset);
                }
            }
            finally
            {
                foreach (var state in states)
                    UnityEngine.Object.DestroyImmediate(state);
            }
        }

        /// <summary>
        /// Parses and validates the complete table before an import can create or update an asset.
        /// The catalog is an external authority boundary: production callers must supply real
        /// authorities, while EditMode fixtures may use an explicit in-memory catalog.
        /// </summary>
        public static JindanStaticStateData[] ParseJindanStaticStates(
            string[] lines,
            string sourceName,
            JindanStaticReferenceCatalog referenceCatalog = null,
            bool allowFixtures = true)
        {
            if (lines == null)
                throw JindanError("JD_TABLE_INVALID", sourceName, "has no rows.");

            int headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw JindanError("JD_TABLE_INVALID", sourceName, "has no header row.");

            var headers = CsvTableReader.FindHeader(lines);
            RequireJindanStaticColumns(headers, sourceName);
            var states = new List<JindanStaticStateData>();
            var characterIds = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (int index = headerLineIndex + 1; index < lines.Length; index++)
                {
                    string line = lines[index];
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;

                    var columns = CsvTableReader.ParseRow(line);
                    if (columns.Length != headers.Length)
                    {
                        throw JindanError(
                            "JD_TABLE_INVALID",
                            $"{sourceName} row {index + 1}",
                            $"has {columns.Length} columns; expected {headers.Length}.");
                    }

                    var state = ParseJindanStaticStateRow(
                        headers,
                        columns,
                        $"{sourceName} row {index + 1}",
                        referenceCatalog,
                        allowFixtures);
                    if (!characterIds.Add(state.characterId))
                    {
                        UnityEngine.Object.DestroyImmediate(state);
                        throw JindanError("JD_DUPLICATE_CHARACTER_ID", sourceName, $"repeats characterId '{state.characterId}'.");
                    }

                    states.Add(state);
                }

                return states.ToArray();
            }
            catch
            {
                foreach (var state in states)
                    UnityEngine.Object.DestroyImmediate(state);
                throw;
            }
        }

        private static JindanStaticStateData ParseJindanStaticStateRow(
            string[] headers,
            string[] columns,
            string sourceName,
            JindanStaticReferenceCatalog referenceCatalog,
            bool allowFixtures)
        {
            string fixtureId = GetJindanColumnValue(headers, columns, "fixtureId");
            string expectation = GetJindanColumnValue(headers, columns, "expect");
            string fixtureNumericProfile = GetJindanColumnValue(headers, columns, "fixtureOnlyNumericProfile");
            bool hasFixtureData = !IsNone(fixtureId) || !IsNone(expectation) || !IsNone(fixtureNumericProfile);
            if (!allowFixtures && hasFixtureData)
                throw JindanError("JD_FIXTURE_IN_PRODUCTION", sourceName, "contains fixture-only fields.");
            if (hasFixtureData)
            {
                RequireJindanReference(fixtureId, sourceName, "fixtureId", "JD_FIXTURE_INVALID");
                if (expectation != "ACCEPT" && expectation != "REJECT")
                    throw JindanError("JD_FIXTURE_INVALID", sourceName, "expect must be ACCEPT or REJECT.");
                RequireJindanReference(fixtureNumericProfile, sourceName, "fixtureOnlyNumericProfile", "JD_FIXTURE_INVALID");
            }

            string schemaId = GetRequiredJindanColumnValue(headers, columns, "schemaId", sourceName);
            int schemaVersion = ParseJindanInteger(
                GetRequiredJindanColumnValue(headers, columns, "schemaVersion", sourceName),
                sourceName,
                "schemaVersion");
            if (schemaId != JindanStaticSchemaId || schemaVersion != JindanStaticSchemaVersion)
            {
                throw JindanError(
                    "JD_UNKNOWN_SCHEMA",
                    sourceName,
                    $"requires {JindanStaticSchemaId} v{JindanStaticSchemaVersion}.");
            }

            var state = ScriptableObject.CreateInstance<JindanStaticStateData>();
            try
            {
                state.schemaId = schemaId;
                state.schemaVersion = schemaVersion;
                state.characterId = GetRequiredJindanColumnValue(headers, columns, "characterId", sourceName);
                state.foundationPurpleMansionStateRef = GetRequiredJindanColumnValue(
                    headers,
                    columns,
                    "foundationPurpleMansionStateRef",
                    sourceName);
                RequireJindanReference(state.characterId, sourceName, "characterId", "JD_UNKNOWN_STATIC_REFERENCE");
                RequireJindanReference(
                    state.foundationPurpleMansionStateRef,
                    sourceName,
                    "foundationPurpleMansionStateRef",
                    "JD_UNKNOWN_STATIC_REFERENCE");
                state.mansionInputs = ParseJindanMansionInputs(
                    GetRequiredJindanColumnValue(headers, columns, "mansionInputs", sourceName),
                    sourceName);
                state.jindanCoreBinding = ParseJindanCoreBinding(
                    GetRequiredJindanColumnValue(headers, columns, "jindanCoreBinding", sourceName),
                    sourceName);
                state.danxiang = ParseJindanDanxiang(
                    GetRequiredJindanColumnValue(headers, columns, "danxiang", sourceName),
                    sourceName);
                state.stablePositionBindings = ParseJindanStablePositionBindings(
                    GetRequiredJindanColumnValue(headers, columns, "stablePositionBindings", sourceName),
                    sourceName);
                state.abilityLedgerBindings = ParseJindanAbilityLedgerBindings(
                    GetRequiredJindanColumnValue(headers, columns, "abilityLedgerBindings", sourceName),
                    sourceName);

                ValidateJindanStaticState(state, referenceCatalog, sourceName);
                return state;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(state);
                throw;
            }
        }

        private static JindanMansionInput[] ParseJindanMansionInputs(string raw, string sourceName)
        {
            var inputs = new List<JindanMansionInput>();
            foreach (string item in SplitJindanList(raw, '|', sourceName, "mansionInputs", "JD_MANSION_INPUT_INCOMPLETE"))
            {
                string[] parts = item.Split(new[] { '~' }, StringSplitOptions.None);
                if (parts.Length != 2 && parts.Length != 8)
                    throw JindanError("JD_MANSION_INPUT_INCOMPLETE", sourceName, "has an invalid mansion input record.");

                var input = new JindanMansionInput
                {
                    mansionKind = ParseJindanMansionKind(parts[0], sourceName),
                    state = ParseJindanMansionState(parts[1], sourceName),
                };
                if (input.state == PurpleMansionBuildState.Complete)
                {
                    if (parts.Length != 8)
                        throw JindanError("JD_MANSION_INPUT_INCOMPLETE", sourceName, "a complete mansion input lacks its frozen fields.");
                    input.mansionInstanceId = parts[2];
                    input.mansionBodyEffectBindingId = parts[3];
                    input.guardianAbilityInstanceId = parts[4];
                    input.sourceSpellId = parts[5];
                    input.upgradePlanId = parts[6];
                    input.sourceSpellDisposition = parts[7];
                    RequireJindanReference(input.mansionInstanceId, sourceName, "mansionInstanceId", "JD_UNKNOWN_STATIC_REFERENCE");
                    RequireJindanReference(input.mansionBodyEffectBindingId, sourceName, "mansionBodyEffectBindingId", "JD_UNKNOWN_STATIC_REFERENCE");
                    RequireJindanReference(input.guardianAbilityInstanceId, sourceName, "guardianAbilityInstanceId", "JD_UNKNOWN_STATIC_REFERENCE");
                    RequireJindanReference(input.sourceSpellId, sourceName, "sourceSpellId", "JD_UNKNOWN_STATIC_REFERENCE");
                    RequireJindanReference(input.upgradePlanId, sourceName, "upgradePlanId", "JD_UNKNOWN_STATIC_REFERENCE");
                    if (input.sourceSpellDisposition != "RETAIN" && input.sourceSpellDisposition != "REPLACE" && input.sourceSpellDisposition != "INTERNALIZE")
                        throw JindanError("JD_FPM_INPUT_NOT_FORMED", sourceName, "has an invalid frozen source spell disposition.");
                }
                else if (parts.Length != 2 || input.state != PurpleMansionBuildState.NotBuilt)
                {
                    throw JindanError("JD_FPM_INPUT_NOT_FORMED", sourceName, "contains an unformed mansion input.");
                }

                inputs.Add(input);
            }

            return inputs.ToArray();
        }

        private static JindanCoreBindingData ParseJindanCoreBinding(string raw, string sourceName)
        {
            string[] parts = ParseSingleJindanRecord(raw, 5, sourceName, "jindanCoreBinding", "JD_CORE_NOT_UNIQUE");
            var binding = new JindanCoreBindingData
            {
                jindanCoreBindingId = parts[0],
                jindanInstanceId = parts[1],
                boundDanshuCoreId = parts[2],
                formationTransactionId = parts[3],
                formationVersion = ParsePositiveJindanInteger(parts[4], sourceName, "formationVersion", "JD_CORE_NOT_UNIQUE"),
            };
            RequireJindanReference(binding.jindanCoreBindingId, sourceName, "jindanCoreBindingId", "JD_CORE_NOT_UNIQUE");
            RequireJindanReference(binding.jindanInstanceId, sourceName, "jindanInstanceId", "JD_CORE_NOT_UNIQUE");
            RequireJindanReference(binding.boundDanshuCoreId, sourceName, "boundDanshuCoreId", "JD_CORE_NOT_UNIQUE");
            RequireJindanReference(binding.formationTransactionId, sourceName, "formationTransactionId", "JD_CORE_NOT_UNIQUE");
            return binding;
        }

        private static JindanDanxiangData ParseJindanDanxiang(string raw, string sourceName)
        {
            string[] parts = ParseSingleJindanRecord(raw, 5, sourceName, "danxiang", "JD_DANXIANG_NOT_UNIQUE");
            var danxiang = new JindanDanxiangData
            {
                danxiangInstanceId = parts[0],
                jindanInstanceId = parts[1],
                danxiangNameKey = parts[2],
                danxingDefinitionId = IsNone(parts[3]) ? null : parts[3],
                danxiangPresentationProfileId = parts[4],
            };
            RequireJindanReference(danxiang.danxiangInstanceId, sourceName, "danxiangInstanceId", "JD_DANXIANG_NOT_UNIQUE");
            RequireJindanReference(danxiang.jindanInstanceId, sourceName, "jindanInstanceId", "JD_DANXIANG_NOT_UNIQUE");
            RequireJindanReference(danxiang.danxiangNameKey, sourceName, "danxiangNameKey", "JD_LEGACY_OR_DISPLAY_FIELD");
            RequireJindanReference(danxiang.danxiangPresentationProfileId, sourceName, "danxiangPresentationProfileId", "JD_UNKNOWN_STATIC_REFERENCE");
            return danxiang;
        }

        private static JindanStablePositionBindingData[] ParseJindanStablePositionBindings(string raw, string sourceName)
        {
            var bindings = new List<JindanStablePositionBindingData>();
            foreach (string item in SplitJindanList(raw, '|', sourceName, "stablePositionBindings", "JD_STABLE_POSITION_LIMIT"))
            {
                string[] parts = item.Split(new[] { '~' }, StringSplitOptions.None);
                if (parts.Length != 9)
                    throw JindanError("JD_STABLE_POSITION_LIMIT", sourceName, "has an invalid stable position record.");
                var binding = new JindanStablePositionBindingData
                {
                    positionId = parts[0],
                    expectedPositionVersion = ParseNonNegativeJindanInteger(parts[1], sourceName, "expectedPositionVersion", "JD_UNKNOWN_STATIC_REFERENCE"),
                    roadId = parts[2],
                    positionType = ParseJindanPositionType(parts[3], sourceName),
                    proofProfileId = parts[4],
                    equippedBaseEffectId = parts[5],
                    compatibilityProfileId = parts[6],
                    primaryCarrierAbilityInstanceId = parts[7],
                    auxiliaryCarrierAbilityInstanceIds = IsNone(parts[8])
                        ? Array.Empty<string>()
                        : SplitJindanList(parts[8], '+', sourceName, "auxiliaryCarrierAbilityInstanceIds", "JD_CARRIER_REFERENCE_INVALID"),
                };
                foreach (string id in new[]
                {
                    binding.positionId,
                    binding.roadId,
                    binding.proofProfileId,
                    binding.equippedBaseEffectId,
                    binding.compatibilityProfileId,
                    binding.primaryCarrierAbilityInstanceId,
                }.Concat(binding.auxiliaryCarrierAbilityInstanceIds))
                {
                    RequireJindanReference(id, sourceName, "stablePositionBindings", "JD_UNKNOWN_STATIC_REFERENCE");
                }
                bindings.Add(binding);
            }

            return bindings.ToArray();
        }

        private static JindanAbilityLedgerBindingData[] ParseJindanAbilityLedgerBindings(string raw, string sourceName)
        {
            var bindings = new List<JindanAbilityLedgerBindingData>();
            foreach (string item in SplitJindanList(raw, '|', sourceName, "abilityLedgerBindings", "JD_ABILITY_LEDGER_OWNERSHIP_INVALID"))
            {
                string[] parts = item.Split(new[] { '~' }, StringSplitOptions.None);
                if (parts.Length != 7)
                    throw JindanError("JD_ABILITY_LEDGER_OWNERSHIP_INVALID", sourceName, "has an invalid ability ledger record.");
                var binding = new JindanAbilityLedgerBindingData
                {
                    abilityInstanceId = parts[0],
                    resourceDebitLedgerRef = NormalizeJindanOptionalReference(parts[1]),
                    cooldownLedgerRef = NormalizeJindanOptionalReference(parts[2]),
                    chargeLedgerRef = NormalizeJindanOptionalReference(parts[3]),
                    costLedgerRef = NormalizeJindanOptionalReference(parts[4]),
                    conflictReserveLedgerRef = NormalizeJindanOptionalReference(parts[5]),
                    conflictCostProfileId = NormalizeJindanOptionalReference(parts[6]),
                };
                RequireJindanReference(binding.abilityInstanceId, sourceName, "abilityInstanceId", "JD_ABILITY_LEDGER_OWNERSHIP_INVALID");
                bindings.Add(binding);
            }
            return bindings.ToArray();
        }

        private static void ValidateJindanStaticState(
            JindanStaticStateData state,
            JindanStaticReferenceCatalog catalog,
            string sourceName)
        {
            if (catalog == null || catalog.foundationPurpleMansionStates == null)
                throw JindanError("JD_UNKNOWN_STATIC_REFERENCE", sourceName, "has no declared static reference authority.");

            var foundation = catalog.foundationPurpleMansionStates.SingleOrDefault(value =>
                value != null && value.characterId == state.characterId &&
                value.foundationState != null &&
                value.foundationState.foundationInstanceId == state.foundationPurpleMansionStateRef);
            if (foundation == null)
                throw JindanError("JD_UNKNOWN_STATIC_REFERENCE", sourceName, "does not resolve foundationPurpleMansionStateRef for the same character.");
            if (foundation.jindanLock == null || foundation.jindanLock.status != JindanLockStatus.Formed ||
                foundation.foundationState.phase != FoundationPhase.Phase4 || foundation.mansionStates == null ||
                foundation.mansionStates.Any(mansion => mansion == null || mansion.state == PurpleMansionBuildState.Embryo) ||
                !foundation.mansionStates.Any(mansion => mansion.state == PurpleMansionBuildState.Complete))
            {
                throw JindanError("JD_FPM_INPUT_NOT_FORMED", sourceName, "does not reference a formed phase-4 foundation/purple mansion state.");
            }

            ValidateJindanMansionInputs(state, foundation, sourceName);
            ValidateJindanDanxiang(state, catalog, sourceName);
            ValidateJindanStablePositions(state, foundation, catalog, sourceName);
            ValidateJindanAbilityLedgers(state, foundation, catalog, sourceName);
        }

        private static void ValidateJindanMansionInputs(
            JindanStaticStateData state,
            FoundationPurpleMansionStateData foundation,
            string sourceName)
        {
            if (state.mansionInputs == null || state.mansionInputs.Length != 5 ||
                state.mansionInputs.Any(input => input == null) ||
                state.mansionInputs.Select(input => input.mansionKind).Distinct().Count() != 5 ||
                foundation.mansionStates.Length != 5)
            {
                throw JindanError("JD_MANSION_INPUT_INCOMPLETE", sourceName, "must contain each of the five mansion inputs exactly once.");
            }

            foreach (var input in state.mansionInputs)
            {
                var frozen = foundation.mansionStates.SingleOrDefault(mansion => mansion.mansionKind == input.mansionKind);
                if (frozen == null || frozen.state != input.state || input.state == PurpleMansionBuildState.Embryo)
                    throw JindanError("JD_FPM_INPUT_NOT_FORMED", sourceName, "does not mirror the formed foundation snapshot.");
                if (input.state != PurpleMansionBuildState.Complete)
                    continue;

                if (input.mansionInstanceId != frozen.mansionInstanceId ||
                    input.mansionBodyEffectBindingId != frozen.mansionBodyEffectBindingId ||
                    input.guardianAbilityInstanceId != frozen.guardianAbilityInstanceId ||
                    input.sourceSpellId != frozen.sourceSpellId ||
                    input.upgradePlanId != frozen.upgradePlanId ||
                    input.sourceSpellDisposition != frozen.sourceSpellDisposition)
                {
                    throw JindanError("JD_FPM_INPUT_NOT_FORMED", sourceName, "changes a frozen complete mansion input.");
                }
            }
        }

        private static void ValidateJindanDanxiang(
            JindanStaticStateData state,
            JindanStaticReferenceCatalog catalog,
            string sourceName)
        {
            if (state.jindanCoreBinding == null || state.danxiang == null ||
                state.jindanCoreBinding.jindanInstanceId != state.danxiang.jindanInstanceId)
            {
                throw JindanError("JD_DANXIANG_NOT_UNIQUE", sourceName, "must have one danxiang bound to the sole jindan instance.");
            }
            if (!catalog.ContainsDanxiangPresentationProfile(state.danxiang.danxiangPresentationProfileId) ||
                (!string.IsNullOrWhiteSpace(state.danxiang.danxingDefinitionId) &&
                 !catalog.ContainsDanxingDefinition(state.danxiang.danxingDefinitionId)))
            {
                throw JindanError("JD_UNKNOWN_STATIC_REFERENCE", sourceName, "has an unresolved danxiang reference.");
            }
        }

        private static void ValidateJindanStablePositions(
            JindanStaticStateData state,
            FoundationPurpleMansionStateData foundation,
            JindanStaticReferenceCatalog catalog,
            string sourceName)
        {
            if (state.stablePositionBindings == null || state.stablePositionBindings.Length < 1 ||
                state.stablePositionBindings.Length > 3 || state.stablePositionBindings.Any(binding => binding == null))
            {
                throw JindanError("JD_STABLE_POSITION_LIMIT", sourceName, "must contain one to three stable positions.");
            }

            if (state.stablePositionBindings.Select(binding => binding.positionId).Distinct(StringComparer.Ordinal).Count() != state.stablePositionBindings.Length ||
                state.stablePositionBindings.Select(binding => binding.positionType).Distinct().Count() != state.stablePositionBindings.Length)
            {
                throw JindanError("JD_STABLE_POSITION_LIMIT", sourceName, "duplicates a stable position or position type.");
            }

            var completeAbilityIds = new HashSet<string>(
                foundation.mansionStates.Where(mansion => mansion.state == PurpleMansionBuildState.Complete)
                    .Select(mansion => mansion.guardianAbilityInstanceId),
                StringComparer.Ordinal);
            var primaryCarrierIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in state.stablePositionBindings)
            {
                if (!primaryCarrierIds.Add(binding.primaryCarrierAbilityInstanceId))
                    throw JindanError("JD_PRIMARY_CARRIER_DUPLICATE", sourceName, "uses one primary carrier for multiple stable positions.");
                if (!completeAbilityIds.Contains(binding.primaryCarrierAbilityInstanceId) ||
                    binding.auxiliaryCarrierAbilityInstanceIds == null ||
                    binding.auxiliaryCarrierAbilityInstanceIds.Contains(binding.primaryCarrierAbilityInstanceId, StringComparer.Ordinal) ||
                    binding.auxiliaryCarrierAbilityInstanceIds.Distinct(StringComparer.Ordinal).Count() != binding.auxiliaryCarrierAbilityInstanceIds.Length ||
                    binding.auxiliaryCarrierAbilityInstanceIds.Any(id => !completeAbilityIds.Contains(id)))
                {
                    throw JindanError("JD_CARRIER_REFERENCE_INVALID", sourceName, "has an invalid primary or auxiliary carrier reference.");
                }

                var position = catalog.positions == null ? null : catalog.positions.SingleOrDefault(value =>
                    value != null && value.positionId == binding.positionId);
                var road = catalog.roads == null ? null : catalog.roads.SingleOrDefault(value =>
                    value != null && value.roadId == binding.roadId);
                bool hasKnownEffect = catalog.roads != null && catalog.roads.Any(value =>
                    value != null && value.baseEffectCandidateIds != null &&
                    value.baseEffectCandidateIds.Contains(binding.equippedBaseEffectId, StringComparer.Ordinal));
                if (position == null || road == null || position.version != binding.expectedPositionVersion ||
                    position.roadId != binding.roadId || position.positionType != binding.positionType ||
                    position.proofProfileId != binding.proofProfileId || road.baseEffectCandidateIds == null ||
                    !hasKnownEffect)
                {
                    throw JindanError("JD_UNKNOWN_STATIC_REFERENCE", sourceName, "has an unresolved road, effect, position, or proof profile reference.");
                }

                if (!road.baseEffectCandidateIds.Contains(binding.equippedBaseEffectId, StringComparer.Ordinal))
                    throw JindanError("JD_EFFECT_LOADOUT_INVALID", sourceName, "equips an effect outside its road candidates.");

                var compatibility = catalog.compatibilityProfiles == null ? null : catalog.compatibilityProfiles.SingleOrDefault(value =>
                    value != null && value.compatibilityProfileId == binding.compatibilityProfileId &&
                    value.roadId == binding.roadId && value.positionId == binding.positionId &&
                    value.equippedBaseEffectId == binding.equippedBaseEffectId &&
                    value.primaryCarrierAbilityInstanceId == binding.primaryCarrierAbilityInstanceId &&
                    value.auxiliaryCarrierAbilityInstanceIds != null &&
                    value.auxiliaryCarrierAbilityInstanceIds.SequenceEqual(binding.auxiliaryCarrierAbilityInstanceIds, StringComparer.Ordinal));
                if (compatibility == null)
                    throw JindanError("JD_UNKNOWN_STATIC_REFERENCE", sourceName, "has no unique compatible carrier profile.");
            }
        }

        private static void ValidateJindanAbilityLedgers(
            JindanStaticStateData state,
            FoundationPurpleMansionStateData foundation,
            JindanStaticReferenceCatalog catalog,
            string sourceName)
        {
            var completeAbilityIds = foundation.mansionStates
                .Where(mansion => mansion.state == PurpleMansionBuildState.Complete)
                .Select(mansion => mansion.guardianAbilityInstanceId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (state.abilityLedgerBindings == null || state.abilityLedgerBindings.Any(binding => binding == null) ||
                state.abilityLedgerBindings.Select(binding => binding.abilityInstanceId).Distinct(StringComparer.Ordinal).Count() != state.abilityLedgerBindings.Length ||
                !state.abilityLedgerBindings.Select(binding => binding.abilityInstanceId).OrderBy(id => id, StringComparer.Ordinal)
                    .SequenceEqual(completeAbilityIds, StringComparer.Ordinal))
            {
                throw JindanError("JD_ABILITY_LEDGER_OWNERSHIP_INVALID", sourceName, "must have exactly one ledger binding for every complete mansion guardian ability.");
            }

            var usedLedgerReferences = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in state.abilityLedgerBindings)
            {
                var ledgerRefs = new[]
                {
                    binding.resourceDebitLedgerRef,
                    binding.cooldownLedgerRef,
                    binding.chargeLedgerRef,
                    binding.costLedgerRef,
                    binding.conflictReserveLedgerRef,
                }.Where(id => !string.IsNullOrWhiteSpace(id));
                foreach (string ledgerRef in ledgerRefs)
                {
                    if (!catalog.ContainsLedgerReference(ledgerRef) || !usedLedgerReferences.Add(ledgerRef))
                    {
                        throw JindanError("JD_ABILITY_LEDGER_OWNERSHIP_INVALID", sourceName, "shares or cannot resolve an ability-owned mutable ledger.");
                    }
                }

                bool hasConflictReserve = !string.IsNullOrWhiteSpace(binding.conflictReserveLedgerRef);
                bool hasConflictCost = !string.IsNullOrWhiteSpace(binding.conflictCostProfileId);
                if (hasConflictReserve != hasConflictCost || !catalog.ContainsConflictCostProfile(binding.conflictCostProfileId))
                {
                    throw JindanError("JD_CONFLICT_REFERENCE_INVALID", sourceName, "has an invalid conflict reserve or cost profile reference.");
                }
            }
        }

        private static string[] ParseSingleJindanRecord(string raw, int expectedParts, string sourceName, string fieldName, string failureCode)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.IndexOf('|') >= 0)
                throw JindanError(failureCode, sourceName, $"has multiple or empty '{fieldName}' records.");
            string[] parts = raw.Split(new[] { '~' }, StringSplitOptions.None).Select(part => part.Trim()).ToArray();
            if (parts.Length != expectedParts)
                throw JindanError(failureCode, sourceName, $"has an invalid '{fieldName}' record.");
            return parts;
        }

        private static string[] SplitJindanList(string raw, char separator, string sourceName, string fieldName, string failureCode)
        {
            if (IsNone(raw))
                throw JindanError(failureCode, sourceName, $"has an empty '{fieldName}' list.");
            string[] values = raw.Split(new[] { separator }, StringSplitOptions.None).Select(value => value.Trim()).ToArray();
            if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace))
                throw JindanError(failureCode, sourceName, $"has an invalid '{fieldName}' list.");
            return values;
        }

        private static string NormalizeJindanOptionalReference(string raw)
        {
            return IsNone(raw) ? null : raw.Trim();
        }

        private static void RequireJindanStaticColumns(string[] headers, string sourceName)
        {
            if (headers.Any(header => LegacyJindanStaticColumns.Contains(header?.Trim(), StringComparer.OrdinalIgnoreCase)))
                throw JindanError("JD_LEGACY_OR_DISPLAY_FIELD", sourceName, "contains a legacy or display-text schema column.");
            CsvTableReader.RequireExactColumns(headers, sourceName, JindanStaticColumns);
        }

        private static string GetRequiredJindanColumnValue(string[] headers, string[] columns, string name, string sourceName)
        {
            string value = GetJindanColumnValue(headers, columns, name);
            if (IsNone(value))
                throw JindanError("JD_TABLE_INVALID", sourceName, $"has an empty required column '{name}'.");
            return value;
        }

        private static string GetJindanColumnValue(string[] headers, string[] columns, string name)
        {
            int index = CsvTableReader.FindColumnIndex(headers, name);
            return index >= 0 && index < columns.Length ? columns[index].Trim() : "";
        }

        private static PurpleMansionKind ParseJindanMansionKind(string raw, string sourceName)
        {
            return raw switch
            {
                "MING" => PurpleMansionKind.Ming,
                "HUN" => PurpleMansionKind.Hun,
                "SHI" => PurpleMansionKind.Shi,
                "WU" => PurpleMansionKind.Wu,
                "YUN" => PurpleMansionKind.Yun,
                _ => throw JindanError("JD_MANSION_INPUT_INCOMPLETE", sourceName, $"has unknown mansion kind '{raw}'."),
            };
        }

        private static PurpleMansionBuildState ParseJindanMansionState(string raw, string sourceName)
        {
            return raw switch
            {
                "NOT_BUILT" => PurpleMansionBuildState.NotBuilt,
                "EMBRYO" => PurpleMansionBuildState.Embryo,
                "COMPLETE" => PurpleMansionBuildState.Complete,
                _ => throw JindanError("JD_FPM_INPUT_NOT_FORMED", sourceName, $"has unknown mansion state '{raw}'."),
            };
        }

        private static JindanStaticPositionType ParseJindanPositionType(string raw, string sourceName)
        {
            return raw switch
            {
                "SOURCE" => JindanStaticPositionType.Source,
                "TRANSFORMATION" => JindanStaticPositionType.Transformation,
                "DOMAIN" => JindanStaticPositionType.Domain,
                _ => throw JindanError("JD_STABLE_POSITION_LIMIT", sourceName, $"has unknown position type '{raw}'."),
            };
        }

        private static int ParseJindanInteger(string raw, string sourceName, string fieldName)
        {
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw JindanError("JD_TABLE_INVALID", sourceName, $"has invalid integer '{raw}' in '{fieldName}'.");
            return value;
        }

        private static int ParsePositiveJindanInteger(string raw, string sourceName, string fieldName, string failureCode)
        {
            int value = ParseJindanInteger(raw, sourceName, fieldName);
            if (value < 1)
                throw JindanError(failureCode, sourceName, $"'{fieldName}' must be positive.");
            return value;
        }

        private static int ParseNonNegativeJindanInteger(string raw, string sourceName, string fieldName, string failureCode)
        {
            int value = ParseJindanInteger(raw, sourceName, fieldName);
            if (value < 0)
                throw JindanError(failureCode, sourceName, $"'{fieldName}' must not be negative.");
            return value;
        }

        private static void RequireJindanReference(string value, string sourceName, string fieldName, string failureCode)
        {
            if (IsNone(value) || value.Any(character =>
                !char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '.'))
            {
                throw JindanError(failureCode, sourceName, $"has invalid reference '{value}' in '{fieldName}'.");
            }
        }

        private static InvalidDataException JindanError(string code, string sourceName, string message)
        {
            return new InvalidDataException($"{code}: {sourceName} {message}");
        }

        private static void CopyJindanStaticState(JindanStaticStateData source, JindanStaticStateData destination)
        {
            destination.schemaId = source.schemaId;
            destination.schemaVersion = source.schemaVersion;
            destination.characterId = source.characterId;
            destination.foundationPurpleMansionStateRef = source.foundationPurpleMansionStateRef;
            destination.mansionInputs = source.mansionInputs;
            destination.jindanCoreBinding = source.jindanCoreBinding;
            destination.danxiang = source.danxiang;
            destination.stablePositionBindings = source.stablePositionBindings;
            destination.abilityLedgerBindings = source.abilityLedgerBindings;
        }

        public static void ImportFoundationPurpleMansionStates()
        {
            const string path = "Assets/DataConfig/FoundationPurpleMansionStates.csv";
            if (!File.Exists(path))
                throw new FileNotFoundException($"Foundation/Purple Mansion CSV was not found: {path}", path);

            var states = ParseFoundationPurpleMansionStates(File.ReadAllLines(path), path, allowFixtures: false);
            try
            {
                foreach (var state in states)
                {
                    string assetPath =
                        $"Assets/Data/FoundationPurpleMansionStates/FoundationPurpleMansionState_{AssetCommitter.SanitizeName(state.characterId)}.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<FoundationPurpleMansionStateData>(assetPath);
                    bool isNew = asset == null;
                    if (isNew)
                    {
                        asset = ScriptableObject.CreateInstance<FoundationPurpleMansionStateData>();
                        AssetCommitter.EnsureDirectory(assetPath);
                    }

                    CopyFoundationPurpleMansionState(state, asset);
                    if (isNew)
                        AssetDatabase.CreateAsset(asset, assetPath);
                    else
                        EditorUtility.SetDirty(asset);
                }
            }
            finally
            {
                foreach (var state in states)
                    UnityEngine.Object.DestroyImmediate(state);
            }
        }

        /// <summary>
        /// Parses the complete table before the importer creates or updates any persistent asset.
        /// Fixtures may opt into literal numeric profiles; production imports cannot.
        /// </summary>
        public static FoundationPurpleMansionStateData[] ParseFoundationPurpleMansionStates(
            string[] lines,
            string sourceName,
            bool allowFixtures = true)
        {
            if (lines == null)
                throw FoundationError("FPM_TABLE_INVALID", sourceName, "has no rows.");

            int headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw FoundationError("FPM_TABLE_INVALID", sourceName, "has no header row.");

            var headers = CsvTableReader.FindHeader(lines);
            RequireFoundationPurpleMansionColumns(headers, sourceName);
            var states = new List<FoundationPurpleMansionStateData>();
            var characterIds = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (int index = headerLineIndex + 1; index < lines.Length; index++)
                {
                    var line = lines[index];
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;

                    var columns = CsvTableReader.ParseRow(line);
                    if (columns.Length != headers.Length)
                    {
                        throw FoundationError(
                            "FPM_TABLE_INVALID",
                            $"{sourceName} row {index + 1}",
                            $"has {columns.Length} columns; expected {headers.Length}.");
                    }

                    var state = ParseFoundationPurpleMansionRow(
                        headers,
                        columns,
                        $"{sourceName} row {index + 1}",
                        allowFixtures);
                    if (!characterIds.Add(state.characterId))
                    {
                        UnityEngine.Object.DestroyImmediate(state);
                        throw FoundationError("FPM_DUPLICATE_CHARACTER_ID", sourceName, $"repeats characterId '{state.characterId}'.");
                    }

                    states.Add(state);
                }

                return states.ToArray();
            }
            catch
            {
                foreach (var state in states)
                    UnityEngine.Object.DestroyImmediate(state);
                throw;
            }
        }

        private static FoundationPurpleMansionStateData ParseFoundationPurpleMansionRow(
            string[] headers,
            string[] columns,
            string sourceName,
            bool allowFixtures)
        {
            string fixtureId = GetFoundationColumnValue(headers, columns, "fixtureId");
            string expectation = GetFoundationColumnValue(headers, columns, "expect");
            string fixtureNumericProfile = GetFoundationColumnValue(headers, columns, "fixtureOnlyNumericProfile");
            bool hasFixtureData = !IsNone(fixtureId) || !IsNone(expectation) || !IsNone(fixtureNumericProfile);
            if (!allowFixtures && hasFixtureData)
            {
                throw FoundationError(
                    "FPM_FIXTURE_IN_PRODUCTION",
                    sourceName,
                    "contains fixture-only fields.");
            }

            if (hasFixtureData)
            {
                RequireFoundationReference(fixtureId, sourceName, "fixtureId");
                if (expectation != "ACCEPT" && expectation != "REJECT")
                    throw FoundationError("FPM_FIXTURE_INVALID", sourceName, "expect must be ACCEPT or REJECT.");
                if (IsNone(fixtureNumericProfile))
                    throw FoundationError("FPM_FIXTURE_INVALID", sourceName, "fixtureOnlyNumericProfile is required for a fixture.");
            }

            string schemaId = GetRequiredFoundationColumnValue(headers, columns, "schemaId", sourceName);
            int schemaVersion = ParseFoundationInteger(
                GetRequiredFoundationColumnValue(headers, columns, "schemaVersion", sourceName),
                sourceName,
                "schemaVersion");
            if (schemaId != FoundationPurpleMansionSchemaId || schemaVersion != FoundationPurpleMansionSchemaVersion)
            {
                throw FoundationError(
                    "FPM_UNKNOWN_SCHEMA",
                    sourceName,
                    $"requires {FoundationPurpleMansionSchemaId} v{FoundationPurpleMansionSchemaVersion}.");
            }

            var foundation = new FoundationStateRecord
            {
                foundationInstanceId = GetRequiredFoundationColumnValue(headers, columns, "foundationInstanceId", sourceName),
                foundationDefinitionId = GetRequiredFoundationColumnValue(headers, columns, "foundationDefinitionId", sourceName),
                sourceGongFaId = GetRequiredFoundationColumnValue(headers, columns, "sourceGongFaId", sourceName),
                phase = ParseFoundationPhase(
                    GetRequiredFoundationColumnValue(headers, columns, "phase", sourceName),
                    sourceName),
                continuousProgress = ParseFoundationFloat(
                    GetRequiredFoundationColumnValue(headers, columns, "continuousProgress", sourceName),
                    sourceName,
                    "continuousProgress"),
                phaseBoundarySetId = GetRequiredFoundationColumnValue(headers, columns, "phaseBoundarySetId", sourceName),
                naturalMansionCapacity = ParseFoundationInteger(
                    GetRequiredFoundationColumnValue(headers, columns, "naturalMansionCapacity", sourceName),
                    sourceName,
                    "naturalMansionCapacity"),
                releasedNaturalCapacity = ParseFoundationInteger(
                    GetRequiredFoundationColumnValue(headers, columns, "releasedNaturalCapacity", sourceName),
                    sourceName,
                    "releasedNaturalCapacity"),
                expansionGrants = ParseFoundationExpansionGrants(
                    GetFoundationColumnValue(headers, columns, "expansionGrants"),
                    sourceName),
                expandedMansionCapacity = ParseFoundationInteger(
                    GetRequiredFoundationColumnValue(headers, columns, "expandedMansionCapacity", sourceName),
                    sourceName,
                    "expandedMansionCapacity"),
                totalMansionCapacity = ParseFoundationInteger(
                    GetRequiredFoundationColumnValue(headers, columns, "totalMansionCapacity", sourceName),
                    sourceName,
                    "totalMansionCapacity"),
            };

            RequireFoundationReference(foundation.foundationInstanceId, sourceName, "foundationInstanceId");
            RequireFoundationReference(foundation.foundationDefinitionId, sourceName, "foundationDefinitionId");
            RequireFoundationReference(foundation.sourceGongFaId, sourceName, "sourceGongFaId");
            RequireFoundationReference(foundation.phaseBoundarySetId, sourceName, "phaseBoundarySetId");
            if (foundation.naturalMansionCapacity < 0 || foundation.naturalMansionCapacity > 3)
                throw FoundationError("FPM_CAPACITY_OVERFLOW", sourceName, "naturalMansionCapacity must be in 0..3.");

            var state = ScriptableObject.CreateInstance<FoundationPurpleMansionStateData>();
            try
            {
                state.schemaId = schemaId;
                state.schemaVersion = schemaVersion;
                state.characterId = GetRequiredFoundationColumnValue(headers, columns, "characterId", sourceName);
                RequireFoundationReference(state.characterId, sourceName, "characterId");
                state.foundationState = foundation;
                state.mansionStates = ParsePurpleMansionStates(
                    GetRequiredFoundationColumnValue(headers, columns, "mansionStates", sourceName),
                    sourceName);
                state.effectBindings = ParseFoundationEffectBindings(
                    GetFoundationColumnValue(headers, columns, "effectBindings"),
                    sourceName);
                state.guardianAbilities = ParseGuardianAbilities(
                    GetFoundationColumnValue(headers, columns, "guardianAbilities"),
                    sourceName);
                state.enhancementNodes = ParseEnhancementNodes(
                    GetFoundationColumnValue(headers, columns, "enhancementNodes"),
                    sourceName);
                state.cultivationActionState = ParseCultivationActionState(
                    GetFoundationColumnValue(headers, columns, "cultivationActionState"),
                    sourceName);
                state.closedRetreatPlan = ParseClosedRetreatPlan(
                    GetFoundationColumnValue(headers, columns, "closedRetreatPlan"),
                    sourceName);
                state.jindanLock = ParseJindanLock(
                    GetRequiredFoundationColumnValue(headers, columns, "jindanLock", sourceName),
                    sourceName);

                ValidateFoundationPurpleMansionState(state, sourceName);
                ValidateFixturePhaseBoundary(state, fixtureId, fixtureNumericProfile, sourceName);
                return state;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(state);
                throw;
            }
        }

        private static void ValidateFoundationPurpleMansionState(
            FoundationPurpleMansionStateData state,
            string sourceName)
        {
            int releasedCapacity = Math.Min(
                state.foundationState.naturalMansionCapacity,
                FoundationPhaseIndex(state.foundationState.phase) - 1);
            int expandedCapacity = state.foundationState.expansionGrants.Length;
            int totalCapacity = releasedCapacity + expandedCapacity;
            if (state.foundationState.releasedNaturalCapacity != releasedCapacity ||
                state.foundationState.expandedMansionCapacity != expandedCapacity ||
                state.foundationState.totalMansionCapacity != totalCapacity)
            {
                throw FoundationError("FPM_CAPACITY_OVERFLOW", sourceName, "derived mansion capacity does not match the supplied values.");
            }

            int committedCapacity = state.mansionStates.Count(mansion =>
                mansion.state == PurpleMansionBuildState.Embryo || mansion.state == PurpleMansionBuildState.Complete);
            if (committedCapacity > totalCapacity)
                throw FoundationError("FPM_CAPACITY_OVERFLOW", sourceName, "mansion commitments exceed totalMansionCapacity.");

            var effectsById = state.effectBindings.ToDictionary(effect => effect.effectBindingId, StringComparer.Ordinal);
            var grantsById = state.foundationState.expansionGrants.ToDictionary(grant => grant.grantId, StringComparer.Ordinal);
            var mansionsByInstanceId = state.mansionStates
                .Where(mansion => mansion.state == PurpleMansionBuildState.Complete)
                .ToDictionary(mansion => mansion.mansionInstanceId, StringComparer.Ordinal);
            var guardiansById = state.guardianAbilities.ToDictionary(guardian => guardian.abilityInstanceId, StringComparer.Ordinal);
            var nodesById = state.enhancementNodes.ToDictionary(node => node.nodeId, StringComparer.Ordinal);

            ValidateEffectCarriers(state, effectsById, grantsById, mansionsByInstanceId, guardiansById, nodesById, sourceName);
            ValidateExpansionGrants(state, effectsById, sourceName);
            ValidateMansionCompletion(state, effectsById, guardiansById, sourceName);
            ValidateGuardianAbilities(state, effectsById, mansionsByInstanceId, sourceName);
            ValidateEnhancementNodes(state, effectsById, guardiansById, mansionsByInstanceId, sourceName);
            ValidateCultivationActionAndRetreat(state, sourceName);
            ValidateJindanLock(state, sourceName);
        }

        private static void ValidateEffectCarriers(
            FoundationPurpleMansionStateData state,
            IReadOnlyDictionary<string, FoundationEffectBinding> effectsById,
            IReadOnlyDictionary<string, FoundationExpansionGrant> grantsById,
            IReadOnlyDictionary<string, PurpleMansionStateRecord> mansionsByInstanceId,
            IReadOnlyDictionary<string, GuardianAbilityRecord> guardiansById,
            IReadOnlyDictionary<string, EnhancementNodeRecord> nodesById,
            string sourceName)
        {
            var ordersByCarrier = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var effect in state.effectBindings)
            {
                string carrierKey = $"{effect.carrierKind}:{effect.carrierId}";
                if (ordersByCarrier.TryGetValue(carrierKey, out int previousOrder) && effect.order <= previousOrder)
                {
                    throw FoundationError("FPM_INVALID_EFFECT_BINDING", sourceName, $"effect order is not increasing for '{carrierKey}'.");
                }
                ordersByCarrier[carrierKey] = effect.order;

                bool carrierExists = effect.carrierKind switch
                {
                    FoundationEffectCarrierKind.Foundation => effect.carrierId == state.foundationState.foundationInstanceId,
                    FoundationEffectCarrierKind.MansionBody => mansionsByInstanceId.ContainsKey(effect.carrierId),
                    FoundationEffectCarrierKind.GuardianAbility => guardiansById.ContainsKey(effect.carrierId),
                    FoundationEffectCarrierKind.EnhancementNode => nodesById.ContainsKey(effect.carrierId),
                    FoundationEffectCarrierKind.ExpansionGrant => grantsById.ContainsKey(effect.carrierId),
                    FoundationEffectCarrierKind.CultivationAction => state.cultivationActionState != null &&
                                                                  effect.carrierId == state.cultivationActionState.actionStateId,
                    _ => false,
                };
                if (!carrierExists)
                {
                    throw FoundationError(
                        "FPM_UNKNOWN_REFERENCE",
                        sourceName,
                        $"effect binding '{effect.effectBindingId}' has an unknown carrier '{carrierKey}'.");
                }

                foreach (var condition in effect.conditions)
                {
                    if (!condition.StartsWith("completeMansion:", StringComparison.Ordinal))
                        continue;

                    var requiredKind = ParsePurpleMansionKind(
                        condition.Substring("completeMansion:".Length),
                        sourceName);
                    if (!state.mansionStates.Any(mansion =>
                        mansion.mansionKind == requiredKind && mansion.state == PurpleMansionBuildState.Complete))
                    {
                        throw FoundationError(
                            "FPM_UNKNOWN_REFERENCE",
                            sourceName,
                            $"effect binding '{effect.effectBindingId}' requires a missing complete mansion.");
                    }
                }
            }
        }

        private static void ValidateExpansionGrants(
            FoundationPurpleMansionStateData state,
            IReadOnlyDictionary<string, FoundationEffectBinding> effectsById,
            string sourceName)
        {
            foreach (var grant in state.foundationState.expansionGrants)
            {
                if (!effectsById.TryGetValue(grant.capacityEffectBindingId, out var effect) ||
                    effect.carrierKind != FoundationEffectCarrierKind.ExpansionGrant ||
                    effect.carrierId != grant.grantId ||
                    effect.atomicEffectType != "MANSION_CAPACITY_PLUS_ONE")
                {
                    throw FoundationError(
                        "FPM_INVALID_EXPANSION_GRANT",
                        sourceName,
                        $"expansion grant '{grant.grantId}' lacks its permanent capacity +1 binding.");
                }
            }
        }

        private static void ValidateMansionCompletion(
            FoundationPurpleMansionStateData state,
            IReadOnlyDictionary<string, FoundationEffectBinding> effectsById,
            IReadOnlyDictionary<string, GuardianAbilityRecord> guardiansById,
            string sourceName)
        {
            var completeGuardianIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mansion in state.mansionStates)
            {
                if (mansion.state == PurpleMansionBuildState.NotBuilt)
                    continue;

                if (mansion.state == PurpleMansionBuildState.Embryo)
                {
                    if (!IsNone(mansion.mansionInstanceId) ||
                        !IsNone(mansion.mansionBodyEffectBindingId) ||
                        !IsNone(mansion.guardianAbilityInstanceId))
                    {
                        throw FoundationError("FPM_INVALID_EMBRYO", sourceName, "an embryo may not carry a mansion body or guardian ability.");
                    }
                    continue;
                }

                string requiredBindingId = RequiredMansionBodyBindingId(mansion.mansionKind);
                if (mansion.mansionBodyEffectBindingId != requiredBindingId ||
                    !effectsById.TryGetValue(mansion.mansionBodyEffectBindingId, out var bodyEffect) ||
                    bodyEffect.carrierKind != FoundationEffectCarrierKind.MansionBody ||
                    bodyEffect.carrierId != mansion.mansionInstanceId ||
                    !guardiansById.TryGetValue(mansion.guardianAbilityInstanceId, out var guardian) ||
                    !completeGuardianIds.Add(mansion.guardianAbilityInstanceId))
                {
                    throw FoundationError(
                        "FPM_COMPLETE_MISSING_BINDING",
                        sourceName,
                        $"complete {mansion.mansionKind} mansion lacks its one-to-one body binding or guardian ability.");
                }

                if (guardian.mansionInstanceId != mansion.mansionInstanceId ||
                    guardian.sourceSpellId != mansion.sourceSpellId ||
                    guardian.upgradePlanId != mansion.upgradePlanId ||
                    guardian.sourceSpellDisposition != mansion.sourceSpellDisposition)
                {
                    throw FoundationError(
                        "FPM_COMPLETE_MISSING_BINDING",
                        sourceName,
                        $"guardian ability '{guardian.abilityInstanceId}' does not match its complete mansion.");
                }
            }

            if (completeGuardianIds.Count != state.guardianAbilities.Length)
            {
                throw FoundationError("FPM_COMPLETE_MISSING_BINDING", sourceName, "an unbound guardian ability was supplied.");
            }
        }

        private static void ValidateGuardianAbilities(
            FoundationPurpleMansionStateData state,
            IReadOnlyDictionary<string, FoundationEffectBinding> effectsById,
            IReadOnlyDictionary<string, PurpleMansionStateRecord> mansionsByInstanceId,
            string sourceName)
        {
            foreach (var guardian in state.guardianAbilities)
            {
                if (!mansionsByInstanceId.ContainsKey(guardian.mansionInstanceId))
                {
                    throw FoundationError(
                        "FPM_COMPLETE_MISSING_BINDING",
                        sourceName,
                        $"guardian ability '{guardian.abilityInstanceId}' references an unknown mansion.");
                }

                foreach (var effectId in guardian.effectBindingIds)
                {
                    if (!effectsById.TryGetValue(effectId, out var effect) ||
                        effect.carrierKind != FoundationEffectCarrierKind.GuardianAbility ||
                        effect.carrierId != guardian.abilityInstanceId)
                    {
                        throw FoundationError(
                            "FPM_UNKNOWN_REFERENCE",
                            sourceName,
                            $"guardian ability '{guardian.abilityInstanceId}' has an invalid effect binding reference.");
                    }
                }
            }
        }

        private static void ValidateEnhancementNodes(
            FoundationPurpleMansionStateData state,
            IReadOnlyDictionary<string, FoundationEffectBinding> effectsById,
            IReadOnlyDictionary<string, GuardianAbilityRecord> guardiansById,
            IReadOnlyDictionary<string, PurpleMansionStateRecord> mansionsByInstanceId,
            string sourceName)
        {
            foreach (var node in state.enhancementNodes)
            {
                if (!guardiansById.TryGetValue(node.abilityInstanceId, out var guardian) ||
                    !mansionsByInstanceId.TryGetValue(guardian.mansionInstanceId, out var sourceMansion))
                {
                    throw FoundationError("FPM_UNKNOWN_REFERENCE", sourceName, $"enhancement node '{node.nodeId}' has an unknown guardian ability.");
                }

                foreach (var effectId in node.effectBindingIds)
                {
                    if (!effectsById.TryGetValue(effectId, out var effect) ||
                        effect.carrierKind != FoundationEffectCarrierKind.EnhancementNode ||
                        effect.carrierId != node.nodeId)
                    {
                        throw FoundationError("FPM_UNKNOWN_REFERENCE", sourceName, $"enhancement node '{node.nodeId}' has an invalid effect binding reference.");
                    }
                }

                if (node.nodeKind != EnhancementNodeKind.InterMansion)
                    continue;

                var requiredKinds = node.requirements
                    .Where(requirement => requirement.StartsWith("completeMansion:", StringComparison.Ordinal))
                    .Select(requirement => ParsePurpleMansionKind(requirement.Substring("completeMansion:".Length), sourceName))
                    .ToArray();
                if (requiredKinds.Length != 1 || requiredKinds[0] == sourceMansion.mansionKind)
                {
                    throw FoundationError("FPM_INVALID_ENHANCEMENT_NODE", sourceName, $"inter-mansion node '{node.nodeId}' lacks a different complete mansion requirement.");
                }
            }
        }

        private static void ValidateCultivationActionAndRetreat(FoundationPurpleMansionStateData state, string sourceName)
        {
            var action = state.cultivationActionState;
            var embryos = state.mansionStates.Where(mansion => mansion.state == PurpleMansionBuildState.Embryo).ToArray();
            if (embryos.Length > 0 && action == null)
                throw FoundationError("FPM_INVALID_ACTION", sourceName, "an embryo requires its related action state.");

            if (action != null)
            {
                bool targetMatches = action.actionKind switch
                {
                    CultivationActionKind.FoundationTrial => action.targetRef == state.foundationState.foundationDefinitionId,
                    CultivationActionKind.FoundationNurture => action.targetRef == state.foundationState.foundationInstanceId,
                    CultivationActionKind.MansionEmbryoNurture => embryos.Any(embryo =>
                        embryo.embryoId == action.targetRef && embryo.relatedActionStateId == action.actionStateId),
                    CultivationActionKind.MansionOpeningTrial => embryos.Any(embryo =>
                        embryo.embryoId == action.targetRef && embryo.relatedActionStateId == action.actionStateId),
                    _ => false,
                };
                if (!targetMatches)
                    throw FoundationError("FPM_INVALID_ACTION", sourceName, $"action '{action.actionStateId}' has an invalid targetRef.");

                foreach (var embryo in embryos)
                {
                    if (embryo.relatedActionStateId != action.actionStateId)
                        throw FoundationError("FPM_INVALID_ACTION", sourceName, $"embryo '{embryo.embryoId}' is not bound to the current action.");
                }
            }

            if (state.closedRetreatPlan == null)
                return;

            if (action == null ||
                state.closedRetreatPlan.actionStateId != action.actionStateId ||
                state.closedRetreatPlan.targetRef != action.targetRef ||
                action.status == CultivationActionStatus.Completed ||
                action.status == CultivationActionStatus.Failed ||
                action.status == CultivationActionStatus.Terminated)
            {
                throw FoundationError("FPM_INVALID_CLOSED_RETREAT", sourceName, "closed retreat must reference the current recoverable action and target.");
            }
        }

        private static void ValidateJindanLock(FoundationPurpleMansionStateData state, string sourceName)
        {
            if (state.jindanLock.status == JindanLockStatus.PreJindan)
            {
                if (state.jindanLock.formationSnapshot != null)
                    throw FoundationError("FPM_JINDAN_LOCK_MUTATION", sourceName, "PRE_JINDAN may not carry a formation snapshot.");
                return;
            }

            var snapshot = state.jindanLock.formationSnapshot;
            bool validFormedState = state.foundationState.phase == FoundationPhase.Phase4 &&
                                    state.mansionStates.Any(mansion => mansion.state == PurpleMansionBuildState.Complete) &&
                                    state.mansionStates.All(mansion => mansion.state != PurpleMansionBuildState.Embryo);
            if (!validFormedState || snapshot == null ||
                snapshot.foundationInstanceId != state.foundationState.foundationInstanceId ||
                snapshot.phase != state.foundationState.phase ||
                snapshot.naturalMansionCapacity != state.foundationState.naturalMansionCapacity)
            {
                throw FoundationError("FPM_JINDAN_LOCK_MUTATION", sourceName, "FORMED state does not match its irreversible snapshot.");
            }

            var currentGrantIds = state.foundationState.expansionGrants.Select(grant => grant.grantId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var snapshotGrantIds = snapshot.expansionGrantIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            if (!currentGrantIds.SequenceEqual(snapshotGrantIds, StringComparer.Ordinal) || snapshot.mansionStates.Length != 5)
                throw FoundationError("FPM_JINDAN_LOCK_MUTATION", sourceName, "FORMED state changed grants or mansion count after formation.");

            foreach (var mansion in state.mansionStates)
            {
                var recorded = snapshot.mansionStates.SingleOrDefault(value => value.mansionKind == mansion.mansionKind);
                if (recorded == null ||
                    recorded.state != mansion.state ||
                    recorded.mansionBodyEffectBindingId != mansion.mansionBodyEffectBindingId ||
                    recorded.guardianAbilityInstanceId != mansion.guardianAbilityInstanceId)
                {
                    throw FoundationError("FPM_JINDAN_LOCK_MUTATION", sourceName, "FORMED state changed a mansion, body binding, or guardian ability after formation.");
                }
            }
        }

        private static void ValidateFixturePhaseBoundary(
            FoundationPurpleMansionStateData state,
            string fixtureId,
            string fixtureNumericProfile,
            string sourceName)
        {
            if (IsNone(fixtureId))
            {
                if (!IsNone(fixtureNumericProfile))
                    throw FoundationError("FPM_FIXTURE_IN_PRODUCTION", sourceName, "fixtureOnlyNumericProfile requires fixtureId.");
                throw FoundationError("FPM_EXTERNAL_REFERENCE_UNRESOLVED", sourceName, "phaseBoundarySetId requires an external numeric profile.");
            }

            var parts = fixtureNumericProfile.Split(new[] { '~' }, StringSplitOptions.None);
            if (parts.Length != 4 || parts[0] != state.foundationState.phaseBoundarySetId)
                throw FoundationError("FPM_FIXTURE_INVALID", sourceName, "fixture numeric profile does not match phaseBoundarySetId.");

            float phase1Maximum = ParseFoundationFloat(parts[1], sourceName, "fixture phase 1 maximum");
            float phase2Maximum = ParseFoundationFloat(parts[2], sourceName, "fixture phase 2 maximum");
            float phase3Maximum = ParseFoundationFloat(parts[3], sourceName, "fixture phase 3 maximum");
            if (phase1Maximum >= phase2Maximum || phase2Maximum >= phase3Maximum)
                throw FoundationError("FPM_FIXTURE_INVALID", sourceName, "fixture phase boundaries must be strictly increasing.");

            FoundationPhase resolvedPhase = state.foundationState.continuousProgress <= phase1Maximum
                ? FoundationPhase.Phase1
                : state.foundationState.continuousProgress <= phase2Maximum
                    ? FoundationPhase.Phase2
                    : state.foundationState.continuousProgress <= phase3Maximum
                        ? FoundationPhase.Phase3
                        : FoundationPhase.Phase4;
            if (state.foundationState.phase != resolvedPhase)
                throw FoundationError("FPM_UNKNOWN_PHASE", sourceName, "phase disagrees with fixture phase boundaries.");
        }

        private static FoundationExpansionGrant[] ParseFoundationExpansionGrants(string raw, string sourceName)
        {
            var grants = new List<FoundationExpansionGrant>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in SplitFoundationList(raw, '|', sourceName, "expansionGrants"))
            {
                var parts = entry.Split(new[] { '~' }, StringSplitOptions.None);
                if (parts.Length != 3)
                    throw FoundationError("FPM_INVALID_EXPANSION_GRANT", sourceName, $"has invalid expansion grant '{entry}'.");
                foreach (var part in parts)
                    RequireFoundationReference(part, sourceName, "expansionGrants");
                if (!ids.Add(parts[0]))
                    throw FoundationError("FPM_INVALID_EXPANSION_GRANT", sourceName, $"repeats expansion grant '{parts[0]}'.");
                grants.Add(new FoundationExpansionGrant
                {
                    grantId = parts[0],
                    sourceItemId = parts[1],
                    capacityEffectBindingId = parts[2],
                });
            }

            if (grants.Count > 2)
                throw FoundationError("FPM_CAPACITY_OVERFLOW", sourceName, "allows at most two expansion grants.");
            return grants.ToArray();
        }

        private static PurpleMansionStateRecord[] ParsePurpleMansionStates(string raw, string sourceName)
        {
            var states = new List<PurpleMansionStateRecord>();
            var kinds = new HashSet<PurpleMansionKind>();
            foreach (var entry in SplitFoundationList(raw, '|', sourceName, "mansionStates", allowNone: false))
            {
                var parts = entry.Split(new[] { '~' }, StringSplitOptions.None);
                if (parts.Length < 2)
                    throw FoundationError("FPM_INVALID_MANSION_STATE", sourceName, $"has invalid mansion state '{entry}'.");

                var kind = ParsePurpleMansionKind(parts[0], sourceName);
                if (!kinds.Add(kind))
                    throw FoundationError("FPM_DUPLICATE_MANSION_KIND", sourceName, $"repeats mansion kind '{parts[0]}'.");
                var buildState = ParsePurpleMansionBuildState(parts[1], sourceName);
                var mansion = new PurpleMansionStateRecord
                {
                    mansionKind = kind,
                    state = buildState,
                };

                switch (buildState)
                {
                    case PurpleMansionBuildState.NotBuilt:
                        if (parts.Length != 2)
                            throw FoundationError("FPM_INVALID_MANSION_STATE", sourceName, $"NOT_BUILT mansion '{parts[0]}' has a payload.");
                        break;
                    case PurpleMansionBuildState.Embryo:
                        if (parts.Length != 8)
                            throw FoundationError("FPM_INVALID_EMBRYO", sourceName, $"EMBRYO mansion '{parts[0]}' has an invalid payload.");
                        mansion.embryoId = parts[2];
                        mansion.sourceSpellId = parts[3];
                        mansion.upgradePlanId = parts[4];
                        mansion.continuousProgress = ParseFoundationFloat(parts[5], sourceName, "embryo continuousProgress");
                        mansion.progressChannelId = parts[6];
                        mansion.relatedActionStateId = parts[7];
                        RequireFoundationReference(mansion.embryoId, sourceName, "embryoId");
                        RequireFoundationReference(mansion.sourceSpellId, sourceName, "sourceSpellId");
                        RequireFoundationReference(mansion.upgradePlanId, sourceName, "upgradePlanId");
                        RequireFoundationReference(mansion.progressChannelId, sourceName, "progressChannelId");
                        RequireFoundationReference(mansion.relatedActionStateId, sourceName, "relatedActionStateId");
                        break;
                    case PurpleMansionBuildState.Complete:
                        if (parts.Length != 8)
                            throw FoundationError("FPM_COMPLETE_MISSING_BINDING", sourceName, $"COMPLETE mansion '{parts[0]}' has an invalid payload.");
                        mansion.mansionInstanceId = parts[2];
                        mansion.mansionBodyEffectBindingId = parts[3];
                        mansion.guardianAbilityInstanceId = parts[4];
                        mansion.sourceSpellId = parts[5];
                        mansion.upgradePlanId = parts[6];
                        mansion.sourceSpellDisposition = ParseSourceSpellDisposition(parts[7], sourceName);
                        RequireFoundationReference(mansion.mansionInstanceId, sourceName, "mansionInstanceId");
                        RequireFoundationReference(mansion.mansionBodyEffectBindingId, sourceName, "mansionBodyEffectBindingId");
                        RequireFoundationReference(mansion.guardianAbilityInstanceId, sourceName, "guardianAbilityInstanceId");
                        RequireFoundationReference(mansion.sourceSpellId, sourceName, "sourceSpellId");
                        RequireFoundationReference(mansion.upgradePlanId, sourceName, "upgradePlanId");
                        break;
                }
                states.Add(mansion);
            }

            if (states.Count != 5 || kinds.Count != 5)
                throw FoundationError("FPM_DUPLICATE_MANSION_KIND", sourceName, "must declare each of the five mansion kinds exactly once.");
            return states.ToArray();
        }

        private static FoundationEffectBinding[] ParseFoundationEffectBindings(string raw, string sourceName)
        {
            if (ContainsRecursiveEffectSyntax(raw))
                throw FoundationError("FPM_RECURSIVE_EFFECT_BINDING", sourceName, "contains a recursive or packaged effect field.");

            var bindings = new List<FoundationEffectBinding>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in SplitFoundationList(raw, '|', sourceName, "effectBindings"))
            {
                var parts = entry.Split(new[] { '~' }, StringSplitOptions.None);
                if (parts.Length != 9)
                    throw FoundationError("FPM_INVALID_EFFECT_BINDING", sourceName, $"has invalid effect binding '{entry}'.");
                foreach (var index in new[] { 0, 2, 4, 6, 7 })
                    RequireFoundationReference(parts[index], sourceName, "effectBindings");
                if (!ids.Add(parts[0]) || parts[0] == parts[2])
                    throw FoundationError("FPM_RECURSIVE_EFFECT_BINDING", sourceName, $"has a duplicate or self-referencing binding '{parts[0]}'.");

                bindings.Add(new FoundationEffectBinding
                {
                    effectBindingId = parts[0],
                    carrierKind = ParseFoundationEffectCarrierKind(parts[1], sourceName),
                    carrierId = parts[2],
                    order = ParsePositiveFoundationInteger(parts[3], sourceName, "effect order"),
                    trigger = parts[4],
                    conditions = ParseFoundationConditions(parts[5], sourceName),
                    target = parts[6],
                    atomicEffectType = parts[7],
                    parameters = ParseFoundationParameters(parts[8], sourceName),
                });
            }
            return bindings.ToArray();
        }

        private static GuardianAbilityRecord[] ParseGuardianAbilities(string raw, string sourceName)
        {
            var abilities = new List<GuardianAbilityRecord>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in SplitFoundationList(raw, '|', sourceName, "guardianAbilities"))
            {
                var parts = entry.Split(new[] { '~' }, StringSplitOptions.None);
                if (parts.Length != 8)
                    throw FoundationError("FPM_COMPLETE_MISSING_BINDING", sourceName, $"has invalid guardian ability '{entry}'.");
                foreach (var index in new[] { 0, 1, 2, 3, 4 })
                    RequireFoundationReference(parts[index], sourceName, "guardianAbilities");
                if (!ids.Add(parts[0]))
                    throw FoundationError("FPM_COMPLETE_MISSING_BINDING", sourceName, $"repeats guardian ability '{parts[0]}'.");
                abilities.Add(new GuardianAbilityRecord
                {
                    abilityInstanceId = parts[0],
                    abilityDefinitionId = parts[1],
                    mansionInstanceId = parts[2],
                    sourceSpellId = parts[3],
                    upgradePlanId = parts[4],
                    sourceSpellDisposition = ParseSourceSpellDisposition(parts[5], sourceName),
                    form = ParseGuardianAbilityForm(parts[6], sourceName),
                    effectBindingIds = ParseFoundationReferenceList(parts[7], '+', sourceName, "guardian ability effectBindingIds"),
                });
            }
            return abilities.ToArray();
        }

        private static EnhancementNodeRecord[] ParseEnhancementNodes(string raw, string sourceName)
        {
            var nodes = new List<EnhancementNodeRecord>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in SplitFoundationList(raw, '|', sourceName, "enhancementNodes"))
            {
                var parts = entry.Split(new[] { '~' }, StringSplitOptions.None);
                if (parts.Length != 5)
                    throw FoundationError("FPM_INVALID_ENHANCEMENT_NODE", sourceName, $"has invalid enhancement node '{entry}'.");
                RequireFoundationReference(parts[0], sourceName, "nodeId");
                RequireFoundationReference(parts[1], sourceName, "abilityInstanceId");
                if (!ids.Add(parts[0]))
                    throw FoundationError("FPM_INVALID_ENHANCEMENT_NODE", sourceName, $"repeats node '{parts[0]}'.");
                var requirements = ParseFoundationConditions(parts[3], sourceName);
                if (requirements.Length == 0)
                    throw FoundationError("FPM_INVALID_ENHANCEMENT_NODE", sourceName, $"node '{parts[0]}' needs explicit requirements.");
                nodes.Add(new EnhancementNodeRecord
                {
                    nodeId = parts[0],
                    abilityInstanceId = parts[1],
                    nodeKind = ParseEnhancementNodeKind(parts[2], sourceName),
                    requirements = requirements,
                    effectBindingIds = ParseFoundationReferenceList(parts[4], '+', sourceName, "enhancement node effectBindingIds"),
                });
            }
            return nodes.ToArray();
        }

        private static CultivationActionStateRecord ParseCultivationActionState(string raw, string sourceName)
        {
            if (IsNone(raw))
                return null;
            var parts = raw.Split(new[] { '~' }, StringSplitOptions.None);
            if (parts.Length != 9)
                throw FoundationError("FPM_INVALID_ACTION", sourceName, "has an invalid cultivationActionState payload.");
            foreach (var index in new[] { 0, 3, 4, 5, 7 })
                RequireFoundationReference(parts[index], sourceName, "cultivationActionState");
            var numericProfileRefs = ParseFoundationReferenceList(parts[8], '+', sourceName, "numericProfileRefs");
            if (numericProfileRefs.Length == 0)
                throw FoundationError("FPM_INVALID_ACTION", sourceName, "cultivationActionState needs numericProfileRefs.");
            return new CultivationActionStateRecord
            {
                actionStateId = parts[0],
                actionKind = ParseCultivationActionKind(parts[1], sourceName),
                status = ParseCultivationActionStatus(parts[2], sourceName),
                targetRef = parts[3],
                fixedCycleDefinitionId = parts[4],
                lastStableBoundaryId = parts[5],
                committedCycleIds = ParseFoundationReferenceList(parts[6], '+', sourceName, "committedCycleIds"),
                progressChannelId = parts[7],
                numericProfileRefs = numericProfileRefs,
            };
        }

        private static ClosedRetreatPlanRecord ParseClosedRetreatPlan(string raw, string sourceName)
        {
            if (IsNone(raw))
                return null;
            var parts = raw.Split(new[] { '~' }, StringSplitOptions.None);
            if (parts.Length != 3)
                throw FoundationError("FPM_INVALID_CLOSED_RETREAT", sourceName, "has an invalid closedRetreatPlan payload.");
            RequireFoundationReference(parts[0], sourceName, "closedRetreatPlan.actionStateId");
            RequireFoundationReference(parts[1], sourceName, "closedRetreatPlan.targetRef");
            var stopConditions = ParseFoundationStopConditions(parts[2], sourceName);
            if (stopConditions.Length == 0)
                throw FoundationError("FPM_INVALID_CLOSED_RETREAT", sourceName, "needs explicit stop conditions.");
            return new ClosedRetreatPlanRecord
            {
                actionStateId = parts[0],
                targetRef = parts[1],
                stopConditions = stopConditions,
            };
        }

        private static JindanLockRecord ParseJindanLock(string raw, string sourceName)
        {
            if (raw == "PRE_JINDAN")
            {
                return new JindanLockRecord
                {
                    status = JindanLockStatus.PreJindan,
                    formationSnapshot = null,
                };
            }

            var parts = raw.Split(new[] { '~' }, StringSplitOptions.None);
            if (parts.Length != 6 || parts[0] != "FORMED")
                throw FoundationError("FPM_JINDAN_LOCK_MUTATION", sourceName, "has an invalid jindanLock payload.");
            RequireFoundationReference(parts[1], sourceName, "formationSnapshot.foundationInstanceId");
            var snapshots = new List<PurpleMansionSnapshot>();
            var kinds = new HashSet<PurpleMansionKind>();
            foreach (var entry in SplitFoundationList(parts[5], '+', sourceName, "formationSnapshot.mansionStates", allowNone: false))
            {
                var mansionParts = entry.Split(new[] { ':' }, StringSplitOptions.None);
                if (mansionParts.Length < 2 || mansionParts.Length > 4)
                    throw FoundationError("FPM_JINDAN_LOCK_MUTATION", sourceName, "has an invalid formation mansion snapshot.");
                var mansionKind = ParsePurpleMansionKind(mansionParts[0], sourceName);
                if (!kinds.Add(mansionKind))
                    throw FoundationError("FPM_JINDAN_LOCK_MUTATION", sourceName, "repeats a formation mansion snapshot.");
                var mansionState = ParsePurpleMansionBuildState(mansionParts[1], sourceName);
                if (mansionState == PurpleMansionBuildState.Complete && mansionParts.Length != 4 ||
                    mansionState != PurpleMansionBuildState.Complete && mansionParts.Length != 2)
                {
                    throw FoundationError("FPM_JINDAN_LOCK_MUTATION", sourceName, "has an incomplete formation mansion snapshot.");
                }
                if (mansionState == PurpleMansionBuildState.Complete)
                {
                    RequireFoundationReference(mansionParts[2], sourceName, "formationSnapshot.mansionBodyEffectBindingId");
                    RequireFoundationReference(mansionParts[3], sourceName, "formationSnapshot.guardianAbilityInstanceId");
                }
                snapshots.Add(new PurpleMansionSnapshot
                {
                    mansionKind = mansionKind,
                    state = mansionState,
                    mansionBodyEffectBindingId = mansionState == PurpleMansionBuildState.Complete ? mansionParts[2] : null,
                    guardianAbilityInstanceId = mansionState == PurpleMansionBuildState.Complete ? mansionParts[3] : null,
                });
            }

            return new JindanLockRecord
            {
                status = JindanLockStatus.Formed,
                formationSnapshot = new JindanFormationSnapshot
                {
                    foundationInstanceId = parts[1],
                    phase = ParseFoundationPhase(parts[2], sourceName),
                    naturalMansionCapacity = ParseFoundationInteger(parts[3], sourceName, "formationSnapshot.naturalMansionCapacity"),
                    expansionGrantIds = ParseFoundationReferenceList(parts[4], '+', sourceName, "formationSnapshot.expansionGrantIds"),
                    mansionStates = snapshots.ToArray(),
                },
            };
        }

        private static string[] ParseFoundationConditions(string raw, string sourceName)
        {
            var conditions = SplitFoundationList(raw, '+', sourceName, "conditions");
            foreach (var condition in conditions)
            {
                if (!condition.StartsWith("completeMansion:", StringComparison.Ordinal))
                {
                    RequireFoundationReference(condition, sourceName, "conditions");
                    continue;
                }
                ParsePurpleMansionKind(condition.Substring("completeMansion:".Length), sourceName);
            }
            return conditions;
        }

        private static string[] ParseFoundationParameters(string raw, string sourceName)
        {
            var parameters = SplitFoundationList(raw, '+', sourceName, "parameters");
            foreach (var parameter in parameters)
            {
                var parts = parameter.Split(new[] { ':' }, StringSplitOptions.None);
                if (parts.Length != 2)
                    throw FoundationError("FPM_INVALID_EFFECT_BINDING", sourceName, $"has invalid parameter '{parameter}'.");
                RequireFoundationReference(parts[0], sourceName, "parameter key");
                RequireFoundationReference(parts[1], sourceName, "parameter value");
            }
            return parameters;
        }

        private static string[] ParseFoundationReferenceList(string raw, char separator, string sourceName, string fieldName)
        {
            var references = SplitFoundationList(raw, separator, sourceName, fieldName);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in references)
            {
                RequireFoundationReference(reference, sourceName, fieldName);
                if (!ids.Add(reference))
                    throw FoundationError("FPM_UNKNOWN_REFERENCE", sourceName, $"repeats reference '{reference}' in '{fieldName}'.");
            }
            return references;
        }

        private static string[] ParseFoundationStopConditions(string raw, string sourceName)
        {
            var conditions = SplitFoundationList(raw, '+', sourceName, "stopConditions");
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "WAITING_RESPONSE",
                "INSUFFICIENT_NEXT_CYCLE_RESOURCES",
                "TARGET_COMPLETED",
                "ACTION_FAILED",
                "INJURY_UNRESOLVED",
                "ACTION_INVALIDATED",
                "PLAYER_GUARD",
                "CHAPTER_OR_UNLOCK_BOUNDARY",
                "MANUAL_PAUSE",
            };
            foreach (var condition in conditions)
            {
                if (!allowed.Contains(condition))
                    throw FoundationError("FPM_INVALID_CLOSED_RETREAT", sourceName, $"has unknown stop condition '{condition}'.");
            }
            return conditions;
        }

        private static string[] SplitFoundationList(
            string raw,
            char separator,
            string sourceName,
            string fieldName,
            bool allowNone = true)
        {
            if (IsNone(raw))
            {
                if (allowNone)
                    return Array.Empty<string>();
                throw FoundationError("FPM_TABLE_INVALID", sourceName, $"'{fieldName}' cannot be none.");
            }

            var values = raw.Split(new[] { separator }, StringSplitOptions.None)
                .Select(value => value.Trim())
                .ToArray();
            if (values.Any(string.IsNullOrEmpty))
                throw FoundationError("FPM_TABLE_INVALID", sourceName, $"'{fieldName}' contains an empty entry.");
            return values;
        }

        private static void RequireFoundationPurpleMansionColumns(string[] headers, string sourceName)
        {
            if (headers.Any(header => LegacyFoundationPurpleMansionColumns.Contains(header?.Trim(), StringComparer.OrdinalIgnoreCase)))
            {
                throw FoundationError("FPM_LEGACY_SCHEMA_MIXED", sourceName, "contains an old foundation or mansion schema column.");
            }
            CsvTableReader.RequireExactColumns(headers, sourceName, FoundationPurpleMansionColumns);
        }

        private static string GetRequiredFoundationColumnValue(string[] headers, string[] columns, string name, string sourceName)
        {
            string value = GetFoundationColumnValue(headers, columns, name);
            if (IsNone(value))
                throw FoundationError("FPM_TABLE_INVALID", sourceName, $"has an empty required column '{name}'.");
            return value;
        }

        private static string GetFoundationColumnValue(string[] headers, string[] columns, string name)
        {
            int index = CsvTableReader.FindColumnIndex(headers, name);
            return index >= 0 && index < columns.Length ? columns[index].Trim() : "";
        }

        private static FoundationPhase ParseFoundationPhase(string raw, string sourceName)
        {
            return raw switch
            {
                "PHASE_1" => FoundationPhase.Phase1,
                "PHASE_2" => FoundationPhase.Phase2,
                "PHASE_3" => FoundationPhase.Phase3,
                "PHASE_4" => FoundationPhase.Phase4,
                _ => throw FoundationError("FPM_UNKNOWN_PHASE", sourceName, $"has unknown phase '{raw}'."),
            };
        }

        private static int FoundationPhaseIndex(FoundationPhase phase)
        {
            return phase switch
            {
                FoundationPhase.Phase1 => 1,
                FoundationPhase.Phase2 => 2,
                FoundationPhase.Phase3 => 3,
                FoundationPhase.Phase4 => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(phase)),
            };
        }

        private static PurpleMansionKind ParsePurpleMansionKind(string raw, string sourceName)
        {
            return raw switch
            {
                "MING" => PurpleMansionKind.Ming,
                "HUN" => PurpleMansionKind.Hun,
                "SHI" => PurpleMansionKind.Shi,
                "WU" => PurpleMansionKind.Wu,
                "YUN" => PurpleMansionKind.Yun,
                _ => throw FoundationError("FPM_DUPLICATE_MANSION_KIND", sourceName, $"has unknown mansion kind '{raw}'."),
            };
        }

        private static PurpleMansionBuildState ParsePurpleMansionBuildState(string raw, string sourceName)
        {
            return raw switch
            {
                "NOT_BUILT" => PurpleMansionBuildState.NotBuilt,
                "EMBRYO" => PurpleMansionBuildState.Embryo,
                "COMPLETE" => PurpleMansionBuildState.Complete,
                _ => throw FoundationError("FPM_INVALID_MANSION_STATE", sourceName, $"has unknown mansion state '{raw}'."),
            };
        }

        private static FoundationEffectCarrierKind ParseFoundationEffectCarrierKind(string raw, string sourceName)
        {
            return raw switch
            {
                "FOUNDATION" => FoundationEffectCarrierKind.Foundation,
                "MANSION_BODY" => FoundationEffectCarrierKind.MansionBody,
                "GUARDIAN_ABILITY" => FoundationEffectCarrierKind.GuardianAbility,
                "ENHANCEMENT_NODE" => FoundationEffectCarrierKind.EnhancementNode,
                "EXPANSION_GRANT" => FoundationEffectCarrierKind.ExpansionGrant,
                "CULTIVATION_ACTION" => FoundationEffectCarrierKind.CultivationAction,
                _ => throw FoundationError("FPM_INVALID_EFFECT_BINDING", sourceName, $"has unknown carrier kind '{raw}'."),
            };
        }

        private static GuardianAbilityForm ParseGuardianAbilityForm(string raw, string sourceName)
        {
            return raw switch
            {
                "ACTIVE" => GuardianAbilityForm.Active,
                "PASSIVE" => GuardianAbilityForm.Passive,
                "TRIGGERED" => GuardianAbilityForm.Triggered,
                _ => throw FoundationError("FPM_COMPLETE_MISSING_BINDING", sourceName, $"has unknown guardian form '{raw}'."),
            };
        }

        private static EnhancementNodeKind ParseEnhancementNodeKind(string raw, string sourceName)
        {
            return raw switch
            {
                "BEHAVIOR" => EnhancementNodeKind.Behavior,
                "CULTIVATION" => EnhancementNodeKind.Cultivation,
                "RESOURCE" => EnhancementNodeKind.Resource,
                "INTER_MANSION" => EnhancementNodeKind.InterMansion,
                "SPECIAL" => EnhancementNodeKind.Special,
                _ => throw FoundationError("FPM_INVALID_ENHANCEMENT_NODE", sourceName, $"has unknown enhancement node kind '{raw}'."),
            };
        }

        private static CultivationActionKind ParseCultivationActionKind(string raw, string sourceName)
        {
            return raw switch
            {
                "FOUNDATION_TRIAL" => CultivationActionKind.FoundationTrial,
                "FOUNDATION_NURTURE" => CultivationActionKind.FoundationNurture,
                "MANSION_EMBRYO_NURTURE" => CultivationActionKind.MansionEmbryoNurture,
                "MANSION_OPENING_TRIAL" => CultivationActionKind.MansionOpeningTrial,
                _ => throw FoundationError("FPM_INVALID_ACTION", sourceName, $"has unknown action kind '{raw}'."),
            };
        }

        private static CultivationActionStatus ParseCultivationActionStatus(string raw, string sourceName)
        {
            return raw switch
            {
                "READY" => CultivationActionStatus.Ready,
                "ACTIVE" => CultivationActionStatus.Active,
                "PAUSED" => CultivationActionStatus.Paused,
                "COMPLETED" => CultivationActionStatus.Completed,
                "FAILED" => CultivationActionStatus.Failed,
                "TERMINATED" => CultivationActionStatus.Terminated,
                _ => throw FoundationError("FPM_INVALID_ACTION", sourceName, $"has unknown action status '{raw}'."),
            };
        }

        private static string ParseSourceSpellDisposition(string raw, string sourceName)
        {
            if (raw == "RETAIN" || raw == "REPLACE" || raw == "INTERNALIZE")
                return raw;
            throw FoundationError("FPM_COMPLETE_MISSING_BINDING", sourceName, $"has unknown source spell disposition '{raw}'.");
        }

        private static string RequiredMansionBodyBindingId(PurpleMansionKind kind)
        {
            return kind switch
            {
                PurpleMansionKind.Ming => "MANSION_BODY_MING_YUAN_HUIHU",
                PurpleMansionKind.Hun => "MANSION_BODY_HUN_LINGTAI_DINGPO",
                PurpleMansionKind.Shi => "MANSION_BODY_SHI_SHENGUAN_RUWEI",
                PurpleMansionKind.Wu => "MANSION_BODY_WU_WUJI_SHANCHENG",
                PurpleMansionKind.Yun => "MANSION_BODY_YUN_JIYUAN_SHIZHAO",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        private static int ParseFoundationInteger(string raw, string sourceName, string fieldName)
        {
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw FoundationError("FPM_TABLE_INVALID", sourceName, $"has invalid integer '{raw}' in '{fieldName}'.");
            return value;
        }

        private static int ParsePositiveFoundationInteger(string raw, string sourceName, string fieldName)
        {
            int value = ParseFoundationInteger(raw, sourceName, fieldName);
            if (value < 1)
                throw FoundationError("FPM_INVALID_EFFECT_BINDING", sourceName, $"'{fieldName}' must be positive.");
            return value;
        }

        private static float ParseFoundationFloat(string raw, string sourceName, string fieldName)
        {
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) ||
                float.IsInfinity(value))
            {
                throw FoundationError("FPM_TABLE_INVALID", sourceName, $"has invalid continuous value '{raw}' in '{fieldName}'.");
            }
            return value;
        }

        private static void RequireFoundationReference(string value, string sourceName, string fieldName)
        {
            if (IsNone(value) || value.Any(character =>
                !char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '.'))
            {
                throw FoundationError("FPM_UNKNOWN_REFERENCE", sourceName, $"has invalid reference '{value}' in '{fieldName}'.");
            }
        }

        private static bool ContainsRecursiveEffectSyntax(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return false;
            return raw.IndexOf("effectPackageId", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   raw.IndexOf("nestedEffectBindingIds", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   raw.IndexOf("children=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   raw.IndexOf("subEffects=", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsNone(string value)
        {
            return string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "none", StringComparison.Ordinal);
        }

        private static InvalidDataException FoundationError(string code, string sourceName, string message)
        {
            return new InvalidDataException($"{code}: {sourceName} {message}");
        }

        private static void CopyFoundationPurpleMansionState(
            FoundationPurpleMansionStateData source,
            FoundationPurpleMansionStateData destination)
        {
            destination.schemaId = source.schemaId;
            destination.schemaVersion = source.schemaVersion;
            destination.characterId = source.characterId;
            destination.foundationState = source.foundationState;
            destination.mansionStates = source.mansionStates;
            destination.effectBindings = source.effectBindings;
            destination.guardianAbilities = source.guardianAbilities;
            destination.enhancementNodes = source.enhancementNodes;
            destination.cultivationActionState = source.cultivationActionState;
            destination.closedRetreatPlan = source.closedRetreatPlan;
            destination.jindanLock = source.jindanLock;
        }
        [MenuItem("天章/导入功法配置")]
        public static void ImportGongFa()
        {
            _lang = null;
            string path = "Assets/DataConfig/GongFa.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
            var headers = CsvTableReader.FindHeader(lines);
            CsvTableReader.RequireColumns(headers, path,
                "name", "affiliation", "grade", "elementMain", "elementSub",
                "starRootBone", "starPhysique", "starSpirit", "starMind",
                "starReaction", "starTalent", "starFortune", "growth", "contentScope");

            foreach (var line in lines.Skip(headerLineIndex + 1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = CsvTableReader.ParseRow(line);
                if (cols.Length < headers.Length)
                {
                    Debug.LogWarning($"[Importer] {path} row has {cols.Length} columns, expected >= {headers.Length}; skipping");
                    continue;
                }

                var asset = ScriptableObject.CreateInstance<GongFaGrowthData>();
                asset.gongFaName = T(CsvTableReader.GetRequiredValue(headers, cols, "name", path));
                asset.affiliation = T(CsvTableReader.GetRequiredValue(headers, cols, "affiliation", path));
                asset.grade = T(CsvTableReader.GetRequiredValue(headers, cols, "grade", path));
                asset.elementMain = T(CsvTableReader.GetRequiredValue(headers, cols, "elementMain", path));
                asset.elementSub = T(CsvTableReader.GetRequiredValue(headers, cols, "elementSub", path));
                asset.contentScope = GetRequiredContentScope(headers, cols, path);
                asset.starRootBone = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "starRootBone", path));
                asset.starPhysique = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "starPhysique", path));
                asset.starSpirit = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "starSpirit", path));
                asset.starMind = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "starMind", path));
                asset.starReaction = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "starReaction", path));
                asset.starTalent = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "starTalent", path));
                asset.starFortune = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "starFortune", path));

                // 境界成长表
                var growthRaw = CsvTableReader.GetRequiredValue(headers, cols, "growth", path);
                var growthList = new List<GongFaGrowthData.SubGrowthPerRealm>();
                foreach (var realmEntry in growthRaw.Split('|'))
                {
                    var parts = realmEntry.Split(':');
                    if (parts.Length < 2) continue;
                    var values = parts[1].Split('/');
                    if (values.Length < 9) continue;
                    growthList.Add(new GongFaGrowthData.SubGrowthPerRealm
                    {
                        realm = T(parts[0]),
                        hp = float.Parse(values[0]),
                        mp = float.Parse(values[1]),
                        physAtk = float.Parse(values[2]),
                        magAtk = float.Parse(values[3]),
                        physDef = float.Parse(values[4]),
                        magDef = float.Parse(values[5]),
                        reaction = float.Parse(values[6]),
                        movePoints = float.Parse(values[7]),
                        mindGrowth = float.Parse(values[8])
                    });
                }
                asset.subGrowth = growthList.ToArray();

                // 篇章加成（chapters 列存在时解析，否则为空数组）
                var chaptersRaw = CsvTableReader.GetValueOrDefault(headers, cols, "chapters", "");
                if (!string.IsNullOrEmpty(chaptersRaw))
                {
                    var chapterList = new List<GongFaGrowthData.ChapterBonus>();
                    foreach (var chEntry in chaptersRaw.Split('|'))
                    {
                        var parts = chEntry.Split(':');
                        if (parts.Length < 10) continue;
                        chapterList.Add(new GongFaGrowthData.ChapterBonus
                        {
                            chapterName = T(parts[0]),
                            realm = T(parts[1]),
                            soulShieldRate = float.Parse(parts[2]),
                            hitRate = float.Parse(parts[3]),
                            blockRate = float.Parse(parts[4]),
                            critRate = float.Parse(parts[5]),
                            critDamage = float.Parse(parts[6]),
                            dodgeRate = float.Parse(parts[7]),
                            magAtkBonus = float.Parse(parts[8]),
                            magDefBonus = float.Parse(parts[9]),
                            specialEffect = parts.Length > 10 ? T(parts[10]) : ""
                        });
                    }
                    asset.chapters = chapterList.ToArray();
                }

                // 文件名用 ID（不用解析）
                string assetPath = $"Assets/Data/GongFa/GongFa_{AssetCommitter.SanitizeName(CsvTableReader.GetRequiredValue(headers, cols, "name", path))}.asset";
                string displayName = asset.gongFaName;
                AssetCommitter.EnsureDirectory(assetPath);
                var existing = AssetDatabase.LoadAssetAtPath<GongFaGrowthData>(assetPath);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(asset, assetPath);
                }
                else
                {
                    EditorUtility.CopySerialized(asset, existing);
                    EditorUtility.SetDirty(existing);
                    UnityEngine.Object.DestroyImmediate(asset);
                }
                Debug.Log($"  功法: {displayName} ← {assetPath}");
            }
        }


        private static string GetRequiredContentScope(
            string[] headers,
            string[] columns,
            string sourceName)
        {
            var contentScope = CsvTableReader.GetRequiredValue(headers, columns, "contentScope", sourceName);
            if (!ContentScopePolicy.IsKnown(contentScope))
            {
                throw new InvalidDataException(
                    $"{sourceName} has invalid contentScope '{contentScope}'; expected player or reserved.");
            }

            return contentScope;
        }
    }
}
