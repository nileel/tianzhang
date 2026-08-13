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
    /// <summary>Owns the complete world content import pipeline.</summary>
    public static class WorldContentImporter
    {
        [MenuItem("天章/内容/导入世界定义")]
        public static void Import()
        {
            ImportEnvironmentProfiles();
            ImportCharterRuleDefinitions();
            AdventureContentImporter.Import();
        }

        private static readonly string[] EnvironmentProfileColumns =
        {
            "profileId",
            "directedEdges",
            "surfacePrototypeRefs",
            "phenomenonChannels",
            "phenomenonPairs",
            "elementRelationRefs",
        };

        private static readonly string[] CharterRuleDefinitionColumns =
        {
            "ruleEntryId",
            "displayName",
            "ruleFamily",
            "relationElement",
            "compatiblePhenomena",
            "positiveCommit",
            "negativeCommit",
            "requiredAuthority",
            "requiredNodeTypes",
            "scopeType",
            "scopeTierCap",
            "anchorNodeIds",
            "propagationBoundaryProfileId",
            "currentCoverageSet",
            "affectedWorldVariables",
            "conflictProfileId",
            "failurePolicy",
            "worldEventOutputs",
        };

        private static readonly EnvironmentPhenomenonChannel[] EnvironmentPhenomenonChannels =
        {
            EnvironmentPhenomenonChannel.Airflow,
            EnvironmentPhenomenonChannel.Visibility,
            EnvironmentPhenomenonChannel.Temperature,
            EnvironmentPhenomenonChannel.Precipitation,
            EnvironmentPhenomenonChannel.SuspendedHazard,
            EnvironmentPhenomenonChannel.CloudDischarge,
        };

        private static readonly string[] ElementRelationReferences =
        {
            "element_wood",
            "element_fire",
            "element_earth",
            "element_metal",
            "element_water",
        };

        [MenuItem("天章/导入环境档案配置")]
        public static void ImportEnvironmentProfiles()
        {
            const string path = "Assets/DataConfig/EnvironmentProfiles.csv";
            if (!File.Exists(path))
                throw new FileNotFoundException($"Environment profile CSV was not found: {path}", path);

            var profiles = ParseEnvironmentProfiles(File.ReadAllLines(path), path);
            foreach (var profile in profiles)
            {
                string assetPath = $"Assets/Data/EnvironmentProfiles/EnvironmentProfile_{AssetCommitter.SanitizeName(profile.profileId)}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(assetPath);
                bool isNew = asset == null;
                if (isNew)
                {
                    asset = ScriptableObject.CreateInstance<EnvironmentProfileAsset>();
                    AssetCommitter.EnsureDirectory(assetPath);
                }

                asset.Apply(profile);
                if (isNew)
                    AssetDatabase.CreateAsset(asset, assetPath);
                else
                    EditorUtility.SetDirty(asset);
            }
        }

        public static EnvironmentProfileDefinition[] ParseEnvironmentProfiles(string[] lines, string sourceName)
        {
            if (lines == null)
                throw new InvalidDataException($"{sourceName} has no rows.");

            int headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw new InvalidDataException($"{sourceName} has no header row.");

            var headers = CsvTableReader.FindHeader(lines);
            CsvTableReader.RequireExactColumns(headers, sourceName, EnvironmentProfileColumns);
            var profiles = new List<EnvironmentProfileDefinition>();
            var profileIds = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (int index = headerLineIndex + 1; index < lines.Length; index++)
                {
                    var line = lines[index];
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;

                    var cols = CsvTableReader.ParseRow(line);
                    if (cols.Length != headers.Length)
                    {
                        throw new InvalidDataException(
                            $"{sourceName} row {index + 1} has {cols.Length} columns; expected {headers.Length}.");
                    }

                    var profile = ParseEnvironmentProfileRow(headers, cols, $"{sourceName} row {index + 1}");
                    if (!profileIds.Add(profile.profileId))
                    {
                        throw new InvalidDataException($"{sourceName} has duplicate profileId '{profile.profileId}'.");
                    }

                    profiles.Add(profile);
                }

                return profiles.ToArray();
            }
            catch
            {
                throw;
            }
        }

        private static EnvironmentProfileDefinition ParseEnvironmentProfileRow(
            string[] headers,
            string[] cols,
            string sourceName)
        {
            string profileId = CsvTableReader.GetRequiredValue(headers, cols, "profileId", sourceName);
            ValidateReference(profileId, sourceName, "profileId");

            var directedEdges = ParseDirectedEdges(
                CsvTableReader.GetRequiredValue(headers, cols, "directedEdges", sourceName),
                sourceName,
                out int unitsPerRange,
                out int maxQueryRange);
            var surfacePrototypeRefs = ParseReferenceList(
                CsvTableReader.GetRequiredValue(headers, cols, "surfacePrototypeRefs", sourceName),
                '|',
                sourceName,
                "surfacePrototypeRefs");
            var channels = ParsePhenomenonChannels(
                CsvTableReader.GetRequiredValue(headers, cols, "phenomenonChannels", sourceName),
                sourceName,
                out var channelTypes);
            var pairs = ParsePhenomenonPairs(
                CsvTableReader.GetRequiredValue(headers, cols, "phenomenonPairs", sourceName),
                channelTypes,
                sourceName);
            var elementRelations = ParseElementRelationReferences(
                CsvTableReader.GetRequiredValue(headers, cols, "elementRelationRefs", sourceName),
                sourceName);

            var profile = new EnvironmentProfileDefinition();
            profile.profileId = profileId;
            profile.unitsPerRange = unitsPerRange;
            profile.maxQueryRange = maxQueryRange;
            profile.directedEdges = directedEdges;
            profile.surfacePrototypeRefs = surfacePrototypeRefs;
            profile.phenomenonChannels = channels;
            profile.phenomenonPairs = pairs;
            profile.elementRelationRefs = elementRelations;
            return profile;
        }

        private static EnvironmentDirectedEdge[] ParseDirectedEdges(
            string raw,
            string sourceName,
            out int unitsPerRange,
            out int maxQueryRange)
        {
            var sections = raw.Split(new[] { ';' }, StringSplitOptions.None);
            if (sections.Length != 3 ||
                !sections[0].StartsWith("unitsPerRange=", StringComparison.Ordinal) ||
                !sections[1].StartsWith("maxQueryRange=", StringComparison.Ordinal) ||
                !sections[2].StartsWith("edges=", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{sourceName} has invalid directedEdges query envelope '{raw}'.");
            }
            unitsPerRange = ParsePositiveInteger(
                sections[0].Substring("unitsPerRange=".Length),
                sourceName,
                "unitsPerRange");
            maxQueryRange = ParsePositiveInteger(
                sections[1].Substring("maxQueryRange=".Length),
                sourceName,
                "maxQueryRange");

            var edges = new List<EnvironmentDirectedEdge>();
            var seenEdges = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in SplitRequired(
                sections[2].Substring("edges=".Length),
                '|',
                sourceName,
                "directedEdges"))
            {
                var ruleParts = entry.Split(new[] { '@' }, StringSplitOptions.None);
                if (ruleParts.Length != 4)
                    throw new InvalidDataException($"{sourceName} has invalid directed edge rule '{entry}'.");

                var ends = ruleParts[0].Split(new[] { '>' }, StringSplitOptions.None);
                if (ends.Length != 2)
                    throw new InvalidDataException($"{sourceName} has invalid directed edge '{entry}'.");

                ParseHexCoordinate(ends[0], sourceName, out int fromQ, out int fromR);
                ParseHexCoordinate(ends[1], sourceName, out int toQ, out int toR);
                if (!AreTopologicalNeighbors(fromQ, fromR, toQ, toR))
                {
                    throw new InvalidDataException(
                        $"{sourceName} directed edge '{entry}' does not connect topological neighbors.");
                }

                string edgeKey = $"{fromQ}:{fromR}>{toQ}:{toR}";
                if (!seenEdges.Add(edgeKey))
                    throw new InvalidDataException($"{sourceName} has duplicate directed edge '{edgeKey}'.");

                int metricDistanceUnits = ParsePositiveInteger(
                    ruleParts[1], sourceName, $"directedEdges '{edgeKey}' metricDistanceUnits");
                bool allowsMovement = ParseBinaryFlag(
                    ruleParts[2], sourceName, $"directedEdges '{edgeKey}' allowsMovement");
                bool allowsEffects = ParseBinaryFlag(
                    ruleParts[3], sourceName, $"directedEdges '{edgeKey}' allowsEffects");

                edges.Add(new EnvironmentDirectedEdge
                {
                    fromQ = fromQ,
                    fromR = fromR,
                    toQ = toQ,
                    toR = toR,
                    metricDistanceUnits = metricDistanceUnits,
                    allowsMovement = allowsMovement,
                    allowsEffects = allowsEffects,
                });
            }

            return edges.ToArray();
        }

        private static bool ParseBinaryFlag(string raw, string sourceName, string fieldName)
        {
            if (raw == "1") return true;
            if (raw == "0") return false;
            throw new InvalidDataException($"{sourceName} has invalid binary flag '{raw}' in '{fieldName}'.");
        }

        private static void ParseHexCoordinate(string raw, string sourceName, out int q, out int r)
        {
            var values = raw.Split(new[] { ':' }, StringSplitOptions.None);
            if (values.Length != 2 || !int.TryParse(values[0], out q) || !int.TryParse(values[1], out r))
                throw new InvalidDataException($"{sourceName} has invalid hex coordinate '{raw}'.");
        }

        private static bool AreTopologicalNeighbors(int fromQ, int fromR, int toQ, int toR)
        {
            long deltaQ = toQ - (long)fromQ;
            long deltaR = toR - (long)fromR;
            return (Math.Abs(deltaQ) + Math.Abs(deltaR) + Math.Abs(deltaQ + deltaR)) / 2 == 1;
        }

        private static EnvironmentPhenomenonChannelData[] ParsePhenomenonChannels(
            string raw,
            string sourceName,
            out Dictionary<EnvironmentPhenomenonChannel, HashSet<string>> channelTypes)
        {
            var parsedChannelTypes = new Dictionary<EnvironmentPhenomenonChannel, HashSet<string>>();
            foreach (var entry in SplitRequired(raw, ';', sourceName, "phenomenonChannels"))
            {
                var values = entry.Split(new[] { '=' }, StringSplitOptions.None);
                if (values.Length != 2)
                    throw new InvalidDataException($"{sourceName} has invalid phenomenon channel '{entry}'.");

                var channel = ParsePhenomenonChannel(values[0], sourceName);
                if (parsedChannelTypes.ContainsKey(channel))
                    throw new InvalidDataException($"{sourceName} repeats phenomenon channel '{values[0]}'.");

                parsedChannelTypes[channel] = new HashSet<string>(
                    ParseReferenceList(values[1], '+', sourceName, $"phenomenonChannels:{values[0]}"),
                    StringComparer.Ordinal);
            }

            if (parsedChannelTypes.Count != EnvironmentPhenomenonChannels.Length ||
                EnvironmentPhenomenonChannels.Any(channel => !parsedChannelTypes.ContainsKey(channel)))
            {
                throw new InvalidDataException(
                    $"{sourceName} must declare each fixed phenomenon channel exactly once.");
            }

            channelTypes = parsedChannelTypes;
            return EnvironmentPhenomenonChannels
                .Select(channel => new EnvironmentPhenomenonChannelData
                {
                    channel = channel,
                    phenomenonTypeRefs = parsedChannelTypes[channel].OrderBy(reference => reference, StringComparer.Ordinal).ToArray(),
                })
                .ToArray();
        }

        private static EnvironmentPhenomenonPairing[] ParsePhenomenonPairs(
            string raw,
            IReadOnlyDictionary<EnvironmentPhenomenonChannel, HashSet<string>> channelTypes,
            string sourceName)
        {
            var pairs = new List<EnvironmentPhenomenonPairing>();
            var seenPairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in SplitRequired(raw, '|', sourceName, "phenomenonPairs"))
            {
                var channelAndPair = entry.Split(new[] { ':' }, StringSplitOptions.None);
                if (channelAndPair.Length != 2)
                    throw new InvalidDataException($"{sourceName} has invalid phenomenon pair '{entry}'.");

                var channel = ParsePhenomenonChannel(channelAndPair[0], sourceName);
                var pairAndResult = channelAndPair[1].Split(new[] { '>' }, StringSplitOptions.None);
                if (pairAndResult.Length != 2)
                    throw new InvalidDataException($"{sourceName} has invalid phenomenon pair '{entry}'.");

                var pairTypes = pairAndResult[0].Split(new[] { '+' }, StringSplitOptions.None);
                if (pairTypes.Length != 2 || string.Equals(pairTypes[0], pairTypes[1], StringComparison.Ordinal))
                    throw new InvalidDataException($"{sourceName} has invalid unordered phenomenon pair '{entry}'.");

                ValidatePhenomenonTypeReference(channel, pairTypes[0], channelTypes, sourceName);
                ValidatePhenomenonTypeReference(channel, pairTypes[1], channelTypes, sourceName);
                ValidatePhenomenonTypeReference(channel, pairAndResult[1], channelTypes, sourceName);

                string first = string.CompareOrdinal(pairTypes[0], pairTypes[1]) < 0 ? pairTypes[0] : pairTypes[1];
                string second = string.CompareOrdinal(pairTypes[0], pairTypes[1]) < 0 ? pairTypes[1] : pairTypes[0];
                string pairKey = $"{channel}:{first}+{second}";
                if (!seenPairs.Add(pairKey))
                {
                    throw new InvalidDataException(
                        $"{sourceName} repeats or reverses unordered phenomenon pair '{pairKey}'.");
                }

                pairs.Add(new EnvironmentPhenomenonPairing
                {
                    channel = channel,
                    firstTypeRef = first,
                    secondTypeRef = second,
                    resultTypeRef = pairAndResult[1],
                });
            }

            return pairs.ToArray();
        }

        private static void ValidatePhenomenonTypeReference(
            EnvironmentPhenomenonChannel channel,
            string reference,
            IReadOnlyDictionary<EnvironmentPhenomenonChannel, HashSet<string>> channelTypes,
            string sourceName)
        {
            ValidateReference(reference, sourceName, "phenomenonPairs");
            if (!channelTypes.TryGetValue(channel, out var references) || !references.Contains(reference))
            {
                throw new InvalidDataException(
                    $"{sourceName} phenomenon pair references unknown type '{reference}' in channel '{channel}'.");
            }
        }

        private static string[] ParseElementRelationReferences(string raw, string sourceName)
        {
            var references = ParseReferenceList(raw, '|', sourceName, "elementRelationRefs");
            var supplied = new HashSet<string>(references, StringComparer.Ordinal);
            if (supplied.Count != ElementRelationReferences.Length ||
                ElementRelationReferences.Any(reference => !supplied.Contains(reference)))
            {
                throw new InvalidDataException(
                    $"{sourceName} must reference each of the five fixed element relations exactly once.");
            }

            return ElementRelationReferences.ToArray();
        }

        private static string[] ParseReferenceList(string raw, char separator, string sourceName, string fieldName)
        {
            var values = SplitRequired(raw, separator, sourceName, fieldName);
            var references = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                ValidateReference(value, sourceName, fieldName);
                if (!references.Add(value))
                    throw new InvalidDataException($"{sourceName} repeats reference '{value}' in '{fieldName}'.");
            }

            return values;
        }

        private static string[] SplitRequired(string raw, char separator, string sourceName, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidDataException($"{sourceName} has empty required field '{fieldName}'.");

            var values = raw.Split(new[] { separator }, StringSplitOptions.None)
                .Select(value => value.Trim())
                .ToArray();
            if (values.Any(string.IsNullOrEmpty))
                throw new InvalidDataException($"{sourceName} has an empty entry in '{fieldName}'.");
            return values;
        }

        private static EnvironmentPhenomenonChannel ParsePhenomenonChannel(string raw, string sourceName)
        {
            return raw.Trim() switch
            {
                "airflow" => EnvironmentPhenomenonChannel.Airflow,
                "visibility" => EnvironmentPhenomenonChannel.Visibility,
                "temperature" => EnvironmentPhenomenonChannel.Temperature,
                "precipitation" => EnvironmentPhenomenonChannel.Precipitation,
                "suspendedHazard" => EnvironmentPhenomenonChannel.SuspendedHazard,
                "cloudDischarge" => EnvironmentPhenomenonChannel.CloudDischarge,
                _ => throw new InvalidDataException($"{sourceName} has unknown phenomenon channel '{raw}'."),
            };
        }

        private static void ValidateReference(string value, string sourceName, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
                !char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '.'))
            {
                throw new InvalidDataException($"{sourceName} has invalid reference '{value}' in '{fieldName}'.");
            }
        }

        [MenuItem("天章/导入册界规则定义配置")]
        public static void ImportCharterRuleDefinitions()
        {
            const string path = "Assets/DataConfig/CharterRuleDefinitions.csv";
            if (!File.Exists(path))
                throw new FileNotFoundException($"Charter rule definition CSV was not found: {path}", path);

            // The importer only reads the approved catalog from the single canonical static catalog
            // asset; it no longer holds a second hard-coded production directory.
            CharterRuleStaticCatalogData staticCatalog = LoadCharterRuleStaticCatalog();
            if (!staticCatalog.TryValidateDefinitions(out string catalogReason))
            {
                throw CharterError(
                    "CHARTER_REFERENCE_CATALOG_UNDECLARED",
                    path,
                    $"the approved static catalog is invalid: {catalogReason}");
            }

            var definitions = ParseCharterRuleDefinitions(
                File.ReadAllLines(path),
                path,
                staticCatalog.ReferenceCatalog);
            try
            {
                foreach (var definition in definitions)
                {
                    string assetPath =
                        $"Assets/Data/CharterRuleDefinitions/CharterRuleDefinition_{AssetCommitter.SanitizeName(definition.ruleEntryId)}.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<CharterRuleDefinitionData>(assetPath);
                    bool isNew = asset == null;
                    if (isNew)
                    {
                        asset = ScriptableObject.CreateInstance<CharterRuleDefinitionData>();
                        AssetCommitter.EnsureDirectory(assetPath);
                    }

                    CopyCharterRuleDefinition(definition, asset);
                    if (isNew)
                        AssetDatabase.CreateAsset(asset, assetPath);
                    else
                        EditorUtility.SetDirty(asset);
                }
            }
            finally
            {
                foreach (var definition in definitions)
                    UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        /// <summary>
        /// Loads the one approved production charter static catalog asset. It is the only place the
        /// importer may read the approved reference directory from; a missing or invalid asset fails
        /// closed before any CSV row can import.
        /// </summary>
        public static CharterRuleStaticCatalogData LoadCharterRuleStaticCatalog()
        {
            const string catalogPath = "Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset";
            var staticCatalog = AssetDatabase.LoadAssetAtPath<CharterRuleStaticCatalogData>(catalogPath);
            if (staticCatalog == null)
            {
                throw new InvalidDataException(
                    $"The single approved charter static catalog is missing: {catalogPath}");
            }

            return staticCatalog;
        }

        /// <summary>
        /// Parses and validates the complete definition table before import can write an asset.
        /// The caller supplies every external authority explicitly; this importer never infers one
        /// from display text, paths, enum defaults, EnvironmentProfileDefinition, or test fixtures.
        /// </summary>
        public static CharterRuleDefinitionData[] ParseCharterRuleDefinitions(
            string[] lines,
            string sourceName,
            CharterRuleReferenceCatalog referenceCatalog = null)
        {
            if (lines == null)
                throw CharterError("CHARTER_TABLE_INVALID", sourceName, "has no rows.");

            int headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw CharterError("CHARTER_TABLE_INVALID", sourceName, "has no header row.");

            var headers = CsvTableReader.FindHeader(lines);
            CsvTableReader.RequireExactColumns(headers, sourceName, CharterRuleDefinitionColumns);
            if (referenceCatalog == null || !referenceCatalog.HasDeclaredAuthority)
            {
                throw CharterError(
                    "CHARTER_REFERENCE_CATALOG_UNDECLARED",
                    sourceName,
                    "requires an explicit external reference catalog before a production row can import.");
            }
            if (!CharterRuleCatalogValidator.TryValidateCatalog(referenceCatalog, out string catalogReason))
            {
                throw CharterError(
                    MapCharterCatalogReason(catalogReason),
                    sourceName,
                    catalogReason);
            }

            var definitions = new List<CharterRuleDefinitionData>();
            var ruleEntryIds = new HashSet<string>(StringComparer.Ordinal);
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
                        throw CharterError(
                            "CHARTER_TABLE_INVALID",
                            $"{sourceName} row {index + 1}",
                            $"has {columns.Length} columns; expected {headers.Length}.");
                    }

                    var definition = ParseCharterRuleDefinitionRow(
                        headers,
                        columns,
                        $"{sourceName} row {index + 1}",
                        referenceCatalog);
                    if (!ruleEntryIds.Add(definition.ruleEntryId))
                    {
                        UnityEngine.Object.DestroyImmediate(definition);
                        throw CharterError("CHARTER_DUPLICATE_RULE_ENTRY", sourceName, $"repeats ruleEntryId '{definition.ruleEntryId}'.");
                    }

                    definitions.Add(definition);
                }

                return definitions.ToArray();
            }
            catch
            {
                foreach (var definition in definitions)
                    UnityEngine.Object.DestroyImmediate(definition);
                throw;
            }
        }

        private static CharterRuleDefinitionData ParseCharterRuleDefinitionRow(
            string[] headers,
            string[] columns,
            string sourceName,
            CharterRuleReferenceCatalog catalog)
        {
            var definition = ScriptableObject.CreateInstance<CharterRuleDefinitionData>();
            try
            {
                definition.ruleEntryId = GetRequiredCharterColumnValue(headers, columns, "ruleEntryId", sourceName);
                RequireCharterReference(definition.ruleEntryId, sourceName, "ruleEntryId", "CHARTER_TABLE_INVALID");
                definition.displayName = GetRequiredCharterColumnValue(headers, columns, "displayName", sourceName);
                RequireCharterReference(definition.displayName, sourceName, "displayName", "CHARTER_UNKNOWN_DISPLAY_NAME_REFERENCE");

                definition.ruleFamily = GetRequiredCharterColumnValue(headers, columns, "ruleFamily", sourceName);
                RequireCharterReference(definition.ruleFamily, sourceName, "ruleFamily", "CHARTER_UNKNOWN_RULE_FAMILY_REFERENCE");
                definition.relationElement = GetRequiredCharterColumnValue(headers, columns, "relationElement", sourceName);
                RequireCharterReference(definition.relationElement, sourceName, "relationElement", "CHARTER_UNKNOWN_RELATION_ELEMENT_REFERENCE");
                definition.compatiblePhenomena = ParseCharterReferenceList(
                    GetRequiredCharterColumnValue(headers, columns, "compatiblePhenomena", sourceName),
                    sourceName,
                    "compatiblePhenomena",
                    "CHARTER_UNKNOWN_PHENOMENON_REFERENCE");

                definition.positiveCommit = GetRequiredCharterColumnValue(headers, columns, "positiveCommit", sourceName);
                RequireCharterReference(definition.positiveCommit, sourceName, "positiveCommit", "CHARTER_ATOMIC_COMMIT_INCOMPLETE");
                definition.negativeCommit = GetRequiredCharterColumnValue(headers, columns, "negativeCommit", sourceName);
                RequireCharterReference(definition.negativeCommit, sourceName, "negativeCommit", "CHARTER_ATOMIC_COMMIT_INCOMPLETE");

                definition.requiredAuthority = GetRequiredCharterColumnValue(headers, columns, "requiredAuthority", sourceName);
                RequireCharterReference(definition.requiredAuthority, sourceName, "requiredAuthority", "CHARTER_UNKNOWN_AUTHORITY_REFERENCE");
                definition.requiredNodeTypes = ParseCharterReferenceList(
                    GetRequiredCharterColumnValue(headers, columns, "requiredNodeTypes", sourceName),
                    sourceName,
                    "requiredNodeTypes",
                    "CHARTER_UNKNOWN_NODE_TYPE_REFERENCE");
                definition.scopeType = ParseCharterScopeType(
                    GetRequiredCharterColumnValue(headers, columns, "scopeType", sourceName),
                    sourceName);
                definition.scopeTierCap = ParseCharterScopeTierCap(
                    GetRequiredCharterColumnValue(headers, columns, "scopeTierCap", sourceName),
                    sourceName);
                definition.anchorNodeIds = ParseCharterReferenceList(
                    GetRequiredCharterColumnValue(headers, columns, "anchorNodeIds", sourceName),
                    sourceName,
                    "anchorNodeIds",
                    "CHARTER_UNKNOWN_NODE_REFERENCE");

                definition.propagationBoundaryProfileId = GetRequiredCharterColumnValue(
                    headers, columns, "propagationBoundaryProfileId", sourceName);
                RequireCharterReference(definition.propagationBoundaryProfileId, sourceName, "propagationBoundaryProfileId", "CHARTER_UNKNOWN_BOUNDARY_REFERENCE");
                definition.currentCoverageSet = ParseCharterReferenceList(
                    GetRequiredCharterColumnValue(headers, columns, "currentCoverageSet", sourceName),
                    sourceName,
                    "currentCoverageSet",
                    "CHARTER_COVERAGE_OUT_OF_BOUNDARY");
                definition.affectedWorldVariables = ParseCharterReferenceList(
                    GetRequiredCharterColumnValue(headers, columns, "affectedWorldVariables", sourceName),
                    sourceName,
                    "affectedWorldVariables",
                    "CHARTER_UNKNOWN_VARIABLE_REFERENCE");
                definition.conflictProfileId = GetRequiredCharterColumnValue(headers, columns, "conflictProfileId", sourceName);
                RequireCharterReference(definition.conflictProfileId, sourceName, "conflictProfileId", "CHARTER_UNKNOWN_CONFLICT_REFERENCE");
                definition.failurePolicy = ParseCharterFailurePolicy(
                    GetRequiredCharterColumnValue(headers, columns, "failurePolicy", sourceName),
                    sourceName);
                definition.worldEventOutputs = ParseCharterWorldEventOutputs(
                    GetRequiredCharterColumnValue(headers, columns, "worldEventOutputs", sourceName),
                    sourceName);

                // 十八字段与全部外部引用由同一共享校验解析：导入器与玩家运行时调用同一实现，
                // 不保留第二份硬编码目录或 Editor-only 校验。
                if (!CharterRuleCatalogValidator.TryValidateDefinition(definition, catalog, out string reason))
                {
                    throw CharterError(
                        MapCharterCatalogReason(reason),
                        sourceName,
                        reason);
                }
                return definition;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(definition);
                throw;
            }
        }

        private static string MapCharterCatalogReason(string reason)
        {
            switch (reason)
            {
                case CharterRuleCatalogReasons.CatalogUndeclared:
                    return "CHARTER_REFERENCE_CATALOG_UNDECLARED";
                case CharterRuleCatalogReasons.DuplicateCatalogId:
                    return "CHARTER_REFERENCE_CATALOG_DUPLICATE_ID";
                case CharterRuleCatalogReasons.DuplicateRuleEntryId:
                    return "CHARTER_DUPLICATE_RULE_ENTRY";
                case CharterRuleCatalogReasons.UnknownRuleEntry:
                    return "CHARTER_UNKNOWN_RULE_ENTRY_REFERENCE";
                case CharterRuleCatalogReasons.UnknownDisplayNameKey:
                    return "CHARTER_UNKNOWN_DISPLAY_NAME_REFERENCE";
                case CharterRuleCatalogReasons.UnknownRuleFamily:
                    return "CHARTER_UNKNOWN_RULE_FAMILY_REFERENCE";
                case CharterRuleCatalogReasons.UnknownRelationElement:
                    return "CHARTER_UNKNOWN_RELATION_ELEMENT_REFERENCE";
                case CharterRuleCatalogReasons.UnknownPhenomenon:
                    return "CHARTER_UNKNOWN_PHENOMENON_REFERENCE";
                case CharterRuleCatalogReasons.AtomicCommitIncomplete:
                    return "CHARTER_ATOMIC_COMMIT_INCOMPLETE";
                case CharterRuleCatalogReasons.UnknownRealitySupply:
                    return "CHARTER_UNKNOWN_REALITY_SUPPLY_REFERENCE";
                case CharterRuleCatalogReasons.UnknownAuthority:
                    return "CHARTER_UNKNOWN_AUTHORITY_REFERENCE";
                case CharterRuleCatalogReasons.UnknownRelic:
                    return "CHARTER_UNKNOWN_RELIC_REFERENCE";
                case CharterRuleCatalogReasons.UnknownAuthorization:
                    return "CHARTER_UNKNOWN_AUTHORIZATION_REFERENCE";
                case CharterRuleCatalogReasons.UnknownNodeType:
                    return "CHARTER_UNKNOWN_NODE_TYPE_REFERENCE";
                case CharterRuleCatalogReasons.UnknownNode:
                    return "CHARTER_UNKNOWN_NODE_REFERENCE";
                case CharterRuleCatalogReasons.UnknownBoundary:
                    return "CHARTER_UNKNOWN_BOUNDARY_REFERENCE";
                case CharterRuleCatalogReasons.CoverageOutOfBoundary:
                    return "CHARTER_COVERAGE_OUT_OF_BOUNDARY";
                case CharterRuleCatalogReasons.UnknownVariable:
                    return "CHARTER_UNKNOWN_VARIABLE_REFERENCE";
                case CharterRuleCatalogReasons.UnknownConflict:
                    return "CHARTER_UNKNOWN_CONFLICT_REFERENCE";
                case CharterRuleCatalogReasons.UnknownWorldEvent:
                    return "CHARTER_UNKNOWN_EVENT_REFERENCE";
                case CharterRuleCatalogReasons.UnknownEnvironmentProfile:
                    return "CHARTER_UNKNOWN_ENVIRONMENT_PROFILE_REFERENCE";
                case CharterRuleCatalogReasons.InvalidScopeType:
                    return "CHARTER_SCOPE_INVALID";
                case CharterRuleCatalogReasons.InvalidScopeTierCap:
                    return "CHARTER_SCOPE_TIER_INVALID";
                case CharterRuleCatalogReasons.InvalidFailurePolicy:
                    return "CHARTER_FAILURE_POLICY_INVALID";
                default:
                    return "CHARTER_TABLE_INVALID";
            }
        }

        private static CharterWorldEventOutputData[] ParseCharterWorldEventOutputs(
            string raw,
            string sourceName)
        {
            var outputs = new List<CharterWorldEventOutputData>();
            foreach (string entry in SplitCharterRequired(raw, '|', sourceName, "worldEventOutputs", "CHARTER_TABLE_INVALID"))
            {
                string[] parts = entry.Split(new[] { '~' }, StringSplitOptions.None).Select(value => value.Trim()).ToArray();
                if (parts.Length != 2)
                    throw CharterError("CHARTER_TABLE_INVALID", sourceName, $"has invalid worldEventOutputs entry '{entry}'.");
                RequireCharterReference(parts[0], sourceName, "worldEventOutputs.eventId", "CHARTER_UNKNOWN_EVENT_REFERENCE");
                RequireCharterReference(parts[1], sourceName, "worldEventOutputs.environmentProfileId", "CHARTER_UNKNOWN_ENVIRONMENT_PROFILE_REFERENCE");
                outputs.Add(new CharterWorldEventOutputData
                {
                    eventId = parts[0],
                    environmentProfileId = parts[1],
                });
            }

            return outputs.ToArray();
        }

        private static string[] ParseCharterReferenceList(
            string raw,
            string sourceName,
            string fieldName,
            string failureCode)
        {
            var values = SplitCharterRequired(raw, '|', sourceName, fieldName, failureCode);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                RequireCharterReference(value, sourceName, fieldName, failureCode);
                if (!unique.Add(value))
                    throw CharterError(failureCode, sourceName, $"repeats '{value}' in '{fieldName}'.");
            }
            return values;
        }

        private static string[] SplitCharterRequired(
            string raw,
            char separator,
            string sourceName,
            string fieldName,
            string failureCode)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw CharterError(failureCode, sourceName, $"has empty required field '{fieldName}'.");
            string[] values = raw.Split(new[] { separator }, StringSplitOptions.None).Select(value => value.Trim()).ToArray();
            if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace))
                throw CharterError(failureCode, sourceName, $"has an invalid '{fieldName}' list.");
            return values;
        }

        private static CharterRuleScopeType ParseCharterScopeType(string raw, string sourceName)
        {
            return raw switch
            {
                "SINGLE_NODE" => CharterRuleScopeType.SingleNode,
                "CONNECTED_NODES" => CharterRuleScopeType.ConnectedNodes,
                "REGIONAL_HUB" => CharterRuleScopeType.RegionalHub,
                _ => throw CharterError("CHARTER_SCOPE_INVALID", sourceName, $"has unknown explicit scopeType '{raw}'."),
            };
        }

        private static CharterRuleScopeTierCap ParseCharterScopeTierCap(string raw, string sourceName)
        {
            return raw switch
            {
                "NODE" => CharterRuleScopeTierCap.Node,
                "AREA" => CharterRuleScopeTierCap.Area,
                "REGION" => CharterRuleScopeTierCap.Region,
                _ => throw CharterError("CHARTER_SCOPE_TIER_INVALID", sourceName, $"has unknown explicit scopeTierCap '{raw}'."),
            };
        }

        private static CharterRuleFailurePolicy ParseCharterFailurePolicy(string raw, string sourceName)
        {
            return raw switch
            {
                "REJECT" => CharterRuleFailurePolicy.Reject,
                "SUSPEND" => CharterRuleFailurePolicy.Suspend,
                "SAFE_DOWNGRADE" => CharterRuleFailurePolicy.SafeDowngrade,
                _ => throw CharterError("CHARTER_FAILURE_POLICY_INVALID", sourceName, $"has unknown explicit failurePolicy '{raw}'."),
            };
        }

        private static string GetRequiredCharterColumnValue(
            string[] headers,
            string[] columns,
            string name,
            string sourceName)
        {
            string value = CsvTableReader.GetRequiredValue(headers, columns, name, sourceName);
            if (string.IsNullOrWhiteSpace(value))
                throw CharterError("CHARTER_TABLE_INVALID", sourceName, $"has an empty required column '{name}'.");
            return value.Trim();
        }

        private static void RequireCharterReference(
            string value,
            string sourceName,
            string fieldName,
            string failureCode)
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) || value.Any(character =>
                !char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '.'))
            {
                throw CharterError(failureCode, sourceName, $"has invalid reference '{value}' in '{fieldName}'.");
            }
        }

        private static InvalidDataException CharterError(string code, string sourceName, string message)
        {
            return new InvalidDataException($"{code}: {sourceName} {message}");
        }

        private static void CopyCharterRuleDefinition(
            CharterRuleDefinitionData source,
            CharterRuleDefinitionData destination)
        {
            destination.ruleEntryId = source.ruleEntryId;
            destination.displayName = source.displayName;
            destination.ruleFamily = source.ruleFamily;
            destination.relationElement = source.relationElement;
            destination.compatiblePhenomena = source.compatiblePhenomena;
            destination.positiveCommit = source.positiveCommit;
            destination.negativeCommit = source.negativeCommit;
            destination.requiredAuthority = source.requiredAuthority;
            destination.requiredNodeTypes = source.requiredNodeTypes;
            destination.scopeType = source.scopeType;
            destination.scopeTierCap = source.scopeTierCap;
            destination.anchorNodeIds = source.anchorNodeIds;
            destination.propagationBoundaryProfileId = source.propagationBoundaryProfileId;
            destination.currentCoverageSet = source.currentCoverageSet;
            destination.affectedWorldVariables = source.affectedWorldVariables;
            destination.conflictProfileId = source.conflictProfileId;
            destination.failurePolicy = source.failurePolicy;
            destination.worldEventOutputs = source.worldEventOutputs;
        }

        private static int ParsePositiveInteger(string value, string sourceName, string fieldName)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                throw new InvalidDataException($"{sourceName} {fieldName} '{value}' is not an integer.");
            if (result <= 0)
                throw new InvalidDataException($"{sourceName} {fieldName} must be a positive integer.");
            return result;
        }
    }
}
