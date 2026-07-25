using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using TianZhang.Entity;
using TianZhang.Combat;
using TianZhang.Cultivation;
using TianZhang.Game.CharacterCreation;
using TianZhang.Tactical;

namespace TianZhang.Editor
{
    /// <summary>
    /// CSV 配置导入工具（v3 — 按列名/表头解析）
    /// ⚠️ 已修改/未审核；修改方：Claude Code
    /// 从 Assets/DataConfig/*.csv 读取数据，通过 Language.csv 解析文本 ID
    /// 生成 ScriptableObject .asset 文件
    /// v3 变更：所有导入器不再依赖硬编码列序，改为按表头列名读取；Characters/Enemies 同步升级。
    /// </summary>
    public class DataConfigImporter : EditorWindow
    {
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
                var cols = ParseCSV(line);
                if (cols.Length >= 2 && !string.IsNullOrEmpty(cols[0]))
                    _lang[cols[0]] = cols[1];
            }
            Debug.Log($"[Importer] Loaded {_lang.Count} language entries");
            return _lang;
        }

        /// <summary>解析文本 ID → 显示文本</summary>
        static string T(string id) => LoadLanguage().TryGetValue(id, out var text) ? text : id;

        [MenuItem("天章/导入全部配置")]
        static void ImportAll()
        {
            _lang = null; LoadLanguage();
            ImportGongFa();
            ImportSpells();
            ImportSkills();
            ImportCharacters();
            ImportEnemies();
            ImportCharacterCreationPointBuy();
            ImportEnvironmentProfiles();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DataConfigImporter] 全部配置导入完成");
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
            try
            {
                foreach (var profile in profiles)
                {
                    string assetPath = $"Assets/Data/EnvironmentProfiles/EnvironmentProfile_{SanitizeName(profile.profileId)}.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(assetPath);
                    bool isNew = asset == null;
                    if (isNew)
                    {
                        asset = ScriptableObject.CreateInstance<EnvironmentProfileData>();
                        EnsureDirectory(assetPath);
                    }

                    CopyEnvironmentProfile(profile, asset);
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

        public static EnvironmentProfileData[] ParseEnvironmentProfiles(string[] lines, string sourceName)
        {
            if (lines == null)
                throw new InvalidDataException($"{sourceName} has no rows.");

            int headerLineIndex = FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw new InvalidDataException($"{sourceName} has no header row.");

            var headers = FindHeader(lines);
            RequireExactColumns(headers, sourceName, EnvironmentProfileColumns);
            var profiles = new List<EnvironmentProfileData>();
            var profileIds = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (int index = headerLineIndex + 1; index < lines.Length; index++)
                {
                    var line = lines[index];
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;

                    var cols = ParseCSV(line);
                    if (cols.Length != headers.Length)
                    {
                        throw new InvalidDataException(
                            $"{sourceName} row {index + 1} has {cols.Length} columns; expected {headers.Length}.");
                    }

                    var profile = ParseEnvironmentProfileRow(headers, cols, $"{sourceName} row {index + 1}");
                    if (!profileIds.Add(profile.profileId))
                    {
                        UnityEngine.Object.DestroyImmediate(profile);
                        throw new InvalidDataException($"{sourceName} has duplicate profileId '{profile.profileId}'.");
                    }

                    profiles.Add(profile);
                }

                return profiles.ToArray();
            }
            catch
            {
                foreach (var profile in profiles)
                    UnityEngine.Object.DestroyImmediate(profile);
                throw;
            }
        }

        private static EnvironmentProfileData ParseEnvironmentProfileRow(
            string[] headers,
            string[] cols,
            string sourceName)
        {
            string profileId = GetRequiredColumnValue(headers, cols, "profileId", sourceName);
            ValidateReference(profileId, sourceName, "profileId");

            var directedEdges = ParseDirectedEdges(
                GetRequiredColumnValue(headers, cols, "directedEdges", sourceName),
                sourceName,
                out int unitsPerRange,
                out int maxQueryRange);
            var surfacePrototypeRefs = ParseReferenceList(
                GetRequiredColumnValue(headers, cols, "surfacePrototypeRefs", sourceName),
                '|',
                sourceName,
                "surfacePrototypeRefs");
            var channels = ParsePhenomenonChannels(
                GetRequiredColumnValue(headers, cols, "phenomenonChannels", sourceName),
                sourceName,
                out var channelTypes);
            var pairs = ParsePhenomenonPairs(
                GetRequiredColumnValue(headers, cols, "phenomenonPairs", sourceName),
                channelTypes,
                sourceName);
            var elementRelations = ParseElementRelationReferences(
                GetRequiredColumnValue(headers, cols, "elementRelationRefs", sourceName),
                sourceName);

            var profile = ScriptableObject.CreateInstance<EnvironmentProfileData>();
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

        private static int ParsePositiveInteger(string raw, string sourceName, string fieldName)
        {
            if (!int.TryParse(raw, out int value) || value < 1)
                throw new InvalidDataException($"{sourceName} has invalid positive integer '{raw}' in '{fieldName}'.");
            return value;
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

        private static void CopyEnvironmentProfile(EnvironmentProfileData source, EnvironmentProfileData destination)
        {
            destination.profileId = source.profileId;
            destination.unitsPerRange = source.unitsPerRange;
            destination.maxQueryRange = source.maxQueryRange;
            destination.directedEdges = source.directedEdges;
            destination.surfacePrototypeRefs = source.surfacePrototypeRefs;
            destination.phenomenonChannels = source.phenomenonChannels;
            destination.phenomenonPairs = source.phenomenonPairs;
            destination.elementRelationRefs = source.elementRelationRefs;
        }

        [MenuItem("天章/导入角色创建点购配置")]
        static void ImportCharacterCreationPointBuy()
        {
            string path = "Assets/DataConfig/CharacterCreationPointBuy.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }

            var lines = File.ReadAllLines(path);
            var headerLineIndex = FindHeaderIndex(lines);
            var headers = FindHeader(lines);
            RequireColumns(headers, path,
                "configId", "purchasePointLimit", "minValue", "baseValue", "maxValue",
                "fromValue", "toValue", "costPerLevel");

            var rows = lines
                .Skip(headerLineIndex + 1)
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                .Select(ParseCSV)
                .Where(cols => cols.Length >= headers.Length
                    && GetRequiredColumnValue(headers, cols, "configId", path) == "default")
                .ToArray();

            if (rows.Length == 0)
            {
                Debug.LogError("[DataConfigImporter] CharacterCreationPointBuy.csv missing default config rows.");
                return;
            }

            string assetPath = "Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset";
            EnsureDirectory(assetPath);

            var asset = AssetDatabase.LoadAssetAtPath<CharacterCreationPointBuyConfig>(assetPath);
            bool isNew = asset == null;
            if (isNew)
                asset = ScriptableObject.CreateInstance<CharacterCreationPointBuyConfig>();

            var first = rows[0];
            asset.purchasePointLimit = int.Parse(GetRequiredColumnValue(headers, first, "purchasePointLimit", path));
            asset.minValue = int.Parse(GetRequiredColumnValue(headers, first, "minValue", path));
            asset.baseValue = int.Parse(GetRequiredColumnValue(headers, first, "baseValue", path));
            asset.maxValue = int.Parse(GetRequiredColumnValue(headers, first, "maxValue", path));
            asset.costRanges = rows.Select(cols => new CharacterCreationPointBuyConfig.CostRange
            {
                fromValue = int.Parse(GetRequiredColumnValue(headers, cols, "fromValue", path)),
                toValue = int.Parse(GetRequiredColumnValue(headers, cols, "toValue", path)),
                costPerLevel = int.Parse(GetRequiredColumnValue(headers, cols, "costPerLevel", path))
            }).ToArray();

            if (isNew)
                AssetDatabase.CreateAsset(asset, assetPath);
            else
                EditorUtility.SetDirty(asset);

            Debug.Log($"  角色创建点购配置: {asset.purchasePointLimit}点 ← {assetPath}");
        }

        [MenuItem("天章/导入功法配置")]
        static void ImportGongFa()
        {
            _lang = null;
            string path = "Assets/DataConfig/GongFa.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headerLineIndex = FindHeaderIndex(lines);
            var headers = FindHeader(lines);
            RequireColumns(headers, path,
                "name", "affiliation", "grade", "elementMain", "elementSub",
                "starRootBone", "starPhysique", "starSpirit", "starMind",
                "starReaction", "starTalent", "starFortune", "growth", "contentScope");

            foreach (var line in lines.Skip(headerLineIndex + 1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = ParseCSV(line);
                if (cols.Length < headers.Length)
                {
                    Debug.LogWarning($"[Importer] {path} row has {cols.Length} columns, expected >= {headers.Length}; skipping");
                    continue;
                }

                var asset = ScriptableObject.CreateInstance<GongFaGrowthData>();
                asset.gongFaName = T(GetRequiredColumnValue(headers, cols, "name", path));
                asset.affiliation = T(GetRequiredColumnValue(headers, cols, "affiliation", path));
                asset.grade = T(GetRequiredColumnValue(headers, cols, "grade", path));
                asset.elementMain = T(GetRequiredColumnValue(headers, cols, "elementMain", path));
                asset.elementSub = T(GetRequiredColumnValue(headers, cols, "elementSub", path));
                asset.contentScope = GetRequiredContentScope(headers, cols, path);
                asset.starRootBone = int.Parse(GetRequiredColumnValue(headers, cols, "starRootBone", path));
                asset.starPhysique = int.Parse(GetRequiredColumnValue(headers, cols, "starPhysique", path));
                asset.starSpirit = int.Parse(GetRequiredColumnValue(headers, cols, "starSpirit", path));
                asset.starMind = int.Parse(GetRequiredColumnValue(headers, cols, "starMind", path));
                asset.starReaction = int.Parse(GetRequiredColumnValue(headers, cols, "starReaction", path));
                asset.starTalent = int.Parse(GetRequiredColumnValue(headers, cols, "starTalent", path));
                asset.starFortune = int.Parse(GetRequiredColumnValue(headers, cols, "starFortune", path));

                // 境界成长表
                var growthRaw = GetRequiredColumnValue(headers, cols, "growth", path);
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
                var chaptersRaw = GetColumnValueOrDefault(headers, cols, "chapters", "");
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
                string assetPath = $"Assets/Data/GongFa/GongFa_{SanitizeName(GetRequiredColumnValue(headers, cols, "name", path))}.asset";
                EnsureDirectory(assetPath);
                AssetDatabase.CreateAsset(asset, assetPath);
                Debug.Log($"  功法: {asset.gongFaName} ← {assetPath}");
            }
        }

        [MenuItem("天章/导入术法配置")]
        public static void ImportSpells()
        {
            _lang = null;
            string path = "Assets/DataConfig/Spells.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headerLineIndex = FindHeaderIndex(lines);
            var headers = FindHeader(lines);
            RequireColumns(headers, path,
                "name", "type", "minRange", "maxRange", "mpCost",
                "cooldownTicks", "physicalDamageMultiplier", "soulDamageMultiplier", "healAmount",
                "cannotBlock", "cannotDodge", "penetratingShield", "stunChance",
                "realmReq", "elementReq", "element", "sourceAffiliation", "contentScope");

            foreach (var line in lines.Skip(headerLineIndex + 1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = ParseCSV(line);
                if (cols.Length < headers.Length)
                {
                    Debug.LogWarning($"[Importer] {path} row has {cols.Length} columns, expected >= {headers.Length}; skipping");
                    continue;
                }

                string assetPath = $"Assets/Data/Spells/Spell_{SanitizeName(GetRequiredColumnValue(headers, cols, "name", path))}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<SpellData>(assetPath);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<SpellData>();
                    EnsureDirectory(assetPath);
                    AssetDatabase.CreateAsset(asset, assetPath);
                }
                asset.spellName = T(GetRequiredColumnValue(headers, cols, "name", path));
                asset.type = (SpellType)int.Parse(GetRequiredColumnValue(headers, cols, "type", path));
                asset.minRange = int.Parse(GetRequiredColumnValue(headers, cols, "minRange", path));
                asset.maxRange = int.Parse(GetRequiredColumnValue(headers, cols, "maxRange", path));
                asset.contentScope = GetRequiredContentScope(headers, cols, path);
                asset.realmRequirement = GetRequiredColumnValue(headers, cols, "realmReq", path);
                asset.elementRequirement = GetRequiredColumnValue(headers, cols, "elementReq", path);
                asset.sourceAffiliation = GetRequiredColumnValue(headers, cols, "sourceAffiliation", path);
                asset.mpCost = int.Parse(GetRequiredColumnValue(headers, cols, "mpCost", path));
                asset.cooldownTicks = int.Parse(GetRequiredColumnValue(headers, cols, "cooldownTicks", path));
                asset.physicalDamageMultiplier = float.Parse(GetRequiredColumnValue(headers, cols, "physicalDamageMultiplier", path));
                asset.soulDamageMultiplier = float.Parse(GetRequiredColumnValue(headers, cols, "soulDamageMultiplier", path));
                asset.healAmount = int.Parse(GetRequiredColumnValue(headers, cols, "healAmount", path));
                asset.cannotBlock = GetRequiredColumnValue(headers, cols, "cannotBlock", path) == "1";
                asset.cannotDodge = GetRequiredColumnValue(headers, cols, "cannotDodge", path) == "1";
                asset.penetratingShield = GetRequiredColumnValue(headers, cols, "penetratingShield", path) == "1";
                asset.stunChance = float.Parse(GetRequiredColumnValue(headers, cols, "stunChance", path));
                // 五行属性（从独立 element 列解析）
                asset.element = TianZhang.Combat.DamageCalculator.ResolveElement(
                    GetRequiredColumnValue(headers, cols, "element", path));

                EditorUtility.SetDirty(asset);
                Debug.Log($"  术法: {asset.spellName} ← {assetPath}");
            }

            AssetDatabase.SaveAssets();
        }

        [MenuItem("天章/导入神通配置")]
        public static void ImportSkills()
        {
            _lang = null;
            string path = "Assets/DataConfig/Skills.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headerLineIndex = FindHeaderIndex(lines);
            var headers = FindHeader(lines);
            RequireColumns(headers, path,
                "name", "type", "minRange", "maxRange", "mpCost",
                "cooldownTicks", "damageMultiplier", "healAmount",
                "cannotBlock", "cannotDodge", "penetratingShield", "stunChance",
                "isDomain", "isBloodline", "specialEffectDesc",
                "element", "realmReq", "sourceAffiliation", "contentScope");

            foreach (var line in lines.Skip(headerLineIndex + 1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = ParseCSV(line);
                if (cols.Length < headers.Length)
                {
                    Debug.LogWarning($"[Importer] {path} row has {cols.Length} columns, expected >= {headers.Length}; skipping");
                    continue;
                }

                string assetPath = $"Assets/Data/Skills/Skill_{SanitizeName(GetRequiredColumnValue(headers, cols, "name", path))}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<DivineSkillData>(assetPath);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<DivineSkillData>();
                    EnsureDirectory(assetPath);
                    AssetDatabase.CreateAsset(asset, assetPath);
                }
                asset.skillName = T(GetRequiredColumnValue(headers, cols, "name", path));
                asset.type = (SpellType)int.Parse(GetRequiredColumnValue(headers, cols, "type", path));
                asset.minRange = int.Parse(GetRequiredColumnValue(headers, cols, "minRange", path));
                asset.maxRange = int.Parse(GetRequiredColumnValue(headers, cols, "maxRange", path));
                asset.contentScope = GetRequiredContentScope(headers, cols, path);
                asset.realmRequirement = GetRequiredColumnValue(headers, cols, "realmReq", path);
                asset.sourceAffiliation = GetRequiredColumnValue(headers, cols, "sourceAffiliation", path);
                asset.mpCost = int.Parse(GetRequiredColumnValue(headers, cols, "mpCost", path));
                asset.cooldownTicks = int.Parse(GetRequiredColumnValue(headers, cols, "cooldownTicks", path));
                asset.damageMultiplier = float.Parse(GetRequiredColumnValue(headers, cols, "damageMultiplier", path));
                asset.healAmount = int.Parse(GetRequiredColumnValue(headers, cols, "healAmount", path));
                asset.cannotBlock = GetRequiredColumnValue(headers, cols, "cannotBlock", path) == "1";
                asset.cannotDodge = GetRequiredColumnValue(headers, cols, "cannotDodge", path) == "1";
                asset.penetratingShield = GetRequiredColumnValue(headers, cols, "penetratingShield", path) == "1";
                asset.stunChance = float.Parse(GetRequiredColumnValue(headers, cols, "stunChance", path));
                // 五行属性（从独立 element 列解析）
                asset.element = TianZhang.Combat.DamageCalculator.ResolveElement(
                    GetRequiredColumnValue(headers, cols, "element", path));
                asset.isDomain = GetRequiredColumnValue(headers, cols, "isDomain", path) == "1";
                asset.isBloodline = GetRequiredColumnValue(headers, cols, "isBloodline", path) == "1";
                asset.specialEffectDesc = T(GetRequiredColumnValue(headers, cols, "specialEffectDesc", path));

                EditorUtility.SetDirty(asset);
                Debug.Log($"  神通: {asset.skillName} ← {assetPath}");
            }

            AssetDatabase.SaveAssets();
        }

        [MenuItem("天章/导入角色配置")]
        static void ImportCharacters()
        {
            _lang = null;
            string path = "Assets/DataConfig/Characters.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headerLineIndex = FindHeaderIndex(lines);
            var headers = FindHeader(lines);
            RequireColumns(headers, path,
                "name", "realmMultiplier", "rootBone", "physique", "spirit", "mind",
                "reaction", "talent", "blockRate", "blockReduction",
                "soulShieldRate", "soulShieldReduction", "dodgeRate",
                "critRate", "critDamage", "hitRateBonus", "gongFaName",
                "equippedSpells", "equippedSkills");

            foreach (var line in lines.Skip(headerLineIndex + 1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = ParseCSV(line);
                if (cols.Length < headers.Length)
                {
                    Debug.LogWarning($"[Importer] {path} row has {cols.Length} columns, expected >= {headers.Length}; skipping");
                    continue;
                }

                var asset = ScriptableObject.CreateInstance<CharacterData>();
                asset.charName = T(GetRequiredColumnValue(headers, cols, "name", path));
                asset.realmMultiplier = float.Parse(GetRequiredColumnValue(headers, cols, "realmMultiplier", path));
                asset.rootBone = int.Parse(GetRequiredColumnValue(headers, cols, "rootBone", path));
                asset.physique = int.Parse(GetRequiredColumnValue(headers, cols, "physique", path));
                asset.spirit = int.Parse(GetRequiredColumnValue(headers, cols, "spirit", path));
                asset.mind = int.Parse(GetRequiredColumnValue(headers, cols, "mind", path));
                asset.reaction = int.Parse(GetRequiredColumnValue(headers, cols, "reaction", path));
                asset.talent = int.Parse(GetRequiredColumnValue(headers, cols, "talent", path));
                asset.blockRate = float.Parse(GetRequiredColumnValue(headers, cols, "blockRate", path));
                asset.blockReduction = float.Parse(GetRequiredColumnValue(headers, cols, "blockReduction", path));
                asset.soulShieldRate = float.Parse(GetRequiredColumnValue(headers, cols, "soulShieldRate", path));
                asset.soulShieldReduction = float.Parse(GetRequiredColumnValue(headers, cols, "soulShieldReduction", path));
                asset.dodgeRate = float.Parse(GetRequiredColumnValue(headers, cols, "dodgeRate", path));
                asset.critRate = float.Parse(GetRequiredColumnValue(headers, cols, "critRate", path));
                asset.critDamage = float.Parse(GetRequiredColumnValue(headers, cols, "critDamage", path));
                asset.hitRateBonus = float.Parse(GetRequiredColumnValue(headers, cols, "hitRateBonus", path));
                asset.gongFaName = T(GetRequiredColumnValue(headers, cols, "gongFaName", path));
                var equippedSpellsRaw = GetColumnValueOrDefault(headers, cols, "equippedSpells", "");
                asset.equippedSpells = equippedSpellsRaw.Length > 0 ? equippedSpellsRaw.Split('|') : new string[0];
                var equippedSkillsRaw = GetColumnValueOrDefault(headers, cols, "equippedSkills", "");
                asset.equippedSkills = equippedSkillsRaw.Length > 0 ? equippedSkillsRaw.Split('|') : new string[0];

                string assetPath = $"Assets/Data/Characters/Char_{SanitizeName(GetRequiredColumnValue(headers, cols, "name", path))}.asset";
                EnsureDirectory(assetPath);
                AssetDatabase.CreateAsset(asset, assetPath);
                Debug.Log($"  角色: {asset.charName} ← {assetPath}");
            }
        }

        [MenuItem("天章/导入敌人配置")]
        static void ImportEnemies()
        {
            _lang = null;
            string path = "Assets/DataConfig/Enemies.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headerLineIndex = FindHeaderIndex(lines);
            var headers = FindHeader(lines);
            RequireColumns(headers, path,
                "name", "realmMultiplier", "rootBone", "physique", "spirit", "mind",
                "reaction", "talent", "blockRate", "blockReduction",
                "soulShieldRate", "soulShieldReduction", "dodgeRate",
                "critRate", "critDamage", "hitRateBonus", "equippedSpells");

            foreach (var line in lines.Skip(headerLineIndex + 1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = ParseCSV(line);
                if (cols.Length < headers.Length)
                {
                    Debug.LogWarning($"[Importer] {path} row has {cols.Length} columns, expected >= {headers.Length}; skipping");
                    continue;
                }

                var asset = ScriptableObject.CreateInstance<CharacterData>();
                asset.charName = T(GetRequiredColumnValue(headers, cols, "name", path));
                asset.realmMultiplier = float.Parse(GetRequiredColumnValue(headers, cols, "realmMultiplier", path));
                asset.rootBone = int.Parse(GetRequiredColumnValue(headers, cols, "rootBone", path));
                asset.physique = int.Parse(GetRequiredColumnValue(headers, cols, "physique", path));
                asset.spirit = int.Parse(GetRequiredColumnValue(headers, cols, "spirit", path));
                asset.mind = int.Parse(GetRequiredColumnValue(headers, cols, "mind", path));
                asset.reaction = int.Parse(GetRequiredColumnValue(headers, cols, "reaction", path));
                asset.talent = int.Parse(GetRequiredColumnValue(headers, cols, "talent", path));
                asset.blockRate = float.Parse(GetRequiredColumnValue(headers, cols, "blockRate", path));
                asset.blockReduction = float.Parse(GetRequiredColumnValue(headers, cols, "blockReduction", path));
                asset.soulShieldRate = float.Parse(GetRequiredColumnValue(headers, cols, "soulShieldRate", path));
                asset.soulShieldReduction = float.Parse(GetRequiredColumnValue(headers, cols, "soulShieldReduction", path));
                asset.dodgeRate = float.Parse(GetRequiredColumnValue(headers, cols, "dodgeRate", path));
                asset.critRate = float.Parse(GetRequiredColumnValue(headers, cols, "critRate", path));
                asset.critDamage = float.Parse(GetRequiredColumnValue(headers, cols, "critDamage", path));
                asset.hitRateBonus = float.Parse(GetRequiredColumnValue(headers, cols, "hitRateBonus", path));
                var enemySpellsRaw = GetColumnValueOrDefault(headers, cols, "equippedSpells", "");
                asset.equippedSpells = enemySpellsRaw.Length > 0 ? enemySpellsRaw.Split('|') : new string[0];
                asset.equippedSkills = new string[0];

                string assetPath = $"Assets/Data/Characters/Char_Enemy_{SanitizeName(GetRequiredColumnValue(headers, cols, "name", path))}.asset";
                EnsureDirectory(assetPath);
                AssetDatabase.CreateAsset(asset, assetPath);
                Debug.Log($"  敌人: {asset.charName} ← {assetPath}");
            }
        }

        // ═══ 工具方法 ═══

        static string[] ParseCSV(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            string current = "";
            foreach (char c in line)
            {
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (c == ',' && !inQuotes) { result.Add(current); current = ""; continue; }
                current += c;
            }
            result.Add(current);
            return result.ToArray();
        }

        static string[] FindHeader(string[] lines)
        {
            int headerLineIndex = FindHeaderIndex(lines);
            return headerLineIndex >= 0 ? ParseCSV(lines[headerLineIndex]) : Array.Empty<string>();
        }

        static int FindHeaderIndex(string[] lines)
        {
            if (lines == null)
                return -1;

            for (int index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                return index;
            }

            return -1;
        }

        public static string GetColumnValueOrDefault(
            string[] headers,
            string[] cols,
            string columnName,
            string defaultValue)
        {
            if (headers == null || cols == null)
                return defaultValue;

            int index = FindColumnIndex(headers, columnName);
            if (index < 0 || index >= cols.Length)
                return defaultValue;

            var value = cols[index]?.Trim();
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        public static string GetRequiredColumnValue(
            string[] headers,
            string[] cols,
            string columnName,
            string sourceName)
        {
            int index = FindColumnIndex(headers, columnName);
            if (index < 0)
                throw new InvalidDataException($"{sourceName} missing required column '{columnName}'.");
            if (cols == null || index >= cols.Length)
                throw new InvalidDataException($"{sourceName} row missing required column '{columnName}'.");

            var value = cols[index]?.Trim();
            if (string.IsNullOrEmpty(value))
                throw new InvalidDataException($"{sourceName} row has empty required column '{columnName}'.");

            return value;
        }

        public static string GetRequiredContentScope(
            string[] headers,
            string[] cols,
            string sourceName)
        {
            var contentScope = GetRequiredColumnValue(headers, cols, "contentScope", sourceName);
            if (!ContentScopePolicy.IsKnown(contentScope))
            {
                throw new InvalidDataException(
                    $"{sourceName} has invalid contentScope '{contentScope}'; expected player or reserved.");
            }

            return contentScope;
        }

        static void RequireColumns(string[] headers, string sourceName, params string[] columnNames)
        {
            foreach (var columnName in columnNames)
            {
                if (FindColumnIndex(headers, columnName) < 0)
                    throw new InvalidDataException($"{sourceName} missing required column '{columnName}'.");
            }
        }

        static void RequireExactColumns(string[] headers, string sourceName, params string[] columnNames)
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
                string normalized = header?.Trim();
                if (!seen.Add(normalized) || !columnNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"{sourceName} has duplicate or unknown column '{header}'.");
                }
            }
        }

        static int FindColumnIndex(string[] headers, string columnName)
        {
            if (headers == null)
                return -1;

            return Array.FindIndex(headers, header =>
                string.Equals(header?.Trim(), columnName, StringComparison.OrdinalIgnoreCase));
        }

        static string SanitizeName(string name)
        {
            return name.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
        }

        static void EnsureDirectory(string assetPath)
        {
            string dir = Path.GetDirectoryName(assetPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
