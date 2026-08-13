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
    /// <summary>Owns the complete combat content import pipeline.</summary>
    public static class CombatContentImporter
    {
        [MenuItem("天章/内容/导入战斗定义")]
        public static void Import()
        {
            ImportAttackProfiles();
            ImportSpells();
            ImportSkills();
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
        private static readonly string[] AttackProfileColumns =
        {
            "attackProfileId", "displayNameKey", "profileKind", "basicBindingKind",
            "contentScope", "sourceAffiliation", "realmRequirementId", "elementRequirementId",
            "effectType", "damageElementId", "physicalDamageMultiplier", "soulDamageMultiplier",
            "healAmount", "buffMultiplier", "defensePenetration", "resourceKind", "resourceCost",
            "cooldownTicks", "minCastRange", "maxCastRange", "targetingMode", "areaCenterKind",
            "areaShapeKind", "areaRadius", "areaLength", "areaFanHalfAngleSteps", "areaFacing",
            "areaInnerRadius", "areaEffectBlockers", "areaAllowedFactions", "areaAllowedStates",
            "isDomain", "isBloodline", "specialEffectTextKey",
        };

        [MenuItem("天章/导入术法配置")]
        public static void ImportSpells()
        {
            _lang = null;
            string path = "Assets/DataConfig/Spells.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
            var headers = CsvTableReader.FindHeader(lines);
            CsvTableReader.RequireColumns(headers, path,
                "name", "type", "minRange", "maxRange", "mpCost",
                "cooldownTicks", "physicalDamageMultiplier", "soulDamageMultiplier", "healAmount",
                "cannotBlock", "cannotDodge", "penetratingShield", "stunChance",
                "realmReq", "elementReq", "element", "sourceAffiliation", "contentScope");

            foreach (var line in lines.Skip(headerLineIndex + 1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = CsvTableReader.ParseRow(line);
                if (cols.Length < headers.Length)
                {
                    Debug.LogWarning($"[Importer] {path} row has {cols.Length} columns, expected >= {headers.Length}; skipping");
                    continue;
                }

                string assetPath = $"Assets/Data/Spells/Spell_{AssetCommitter.SanitizeName(CsvTableReader.GetRequiredValue(headers, cols, "name", path))}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<SpellData>(assetPath);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<SpellData>();
                    AssetCommitter.EnsureDirectory(assetPath);
                    AssetDatabase.CreateAsset(asset, assetPath);
                }
                asset.spellName = T(CsvTableReader.GetRequiredValue(headers, cols, "name", path));
                asset.type = (SpellType)int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "type", path));
                asset.minRange = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "minRange", path));
                asset.maxRange = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "maxRange", path));
                asset.contentScope = GetRequiredContentScope(headers, cols, path);
                asset.realmRequirement = CsvTableReader.GetRequiredValue(headers, cols, "realmReq", path);
                asset.elementRequirement = CsvTableReader.GetRequiredValue(headers, cols, "elementReq", path);
                asset.sourceAffiliation = CsvTableReader.GetRequiredValue(headers, cols, "sourceAffiliation", path);
                asset.mpCost = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "mpCost", path));
                asset.cooldownTicks = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "cooldownTicks", path));
                asset.physicalDamageMultiplier = float.Parse(CsvTableReader.GetRequiredValue(headers, cols, "physicalDamageMultiplier", path));
                asset.soulDamageMultiplier = float.Parse(CsvTableReader.GetRequiredValue(headers, cols, "soulDamageMultiplier", path));
                asset.healAmount = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "healAmount", path));
                asset.cannotBlock = CsvTableReader.GetRequiredValue(headers, cols, "cannotBlock", path) == "1";
                asset.cannotDodge = CsvTableReader.GetRequiredValue(headers, cols, "cannotDodge", path) == "1";
                asset.penetratingShield = CsvTableReader.GetRequiredValue(headers, cols, "penetratingShield", path) == "1";
                asset.stunChance = float.Parse(CsvTableReader.GetRequiredValue(headers, cols, "stunChance", path));
                // 五行属性（从独立 element 列解析）
                asset.element = CombatElementFacts.ResolveElement(
                    CsvTableReader.GetRequiredValue(headers, cols, "element", path));

                EditorUtility.SetDirty(asset);
                Debug.Log($"  术法: {asset.spellName} ← {assetPath}");
            }

            AssetDatabase.SaveAssets();
        }

        [MenuItem("天章/导入攻击档案配置")]
        public static void ImportAttackProfiles()
        {
            const string path = "Assets/DataConfig/AttackProfiles.csv";
            if (!File.Exists(path))
            {
                Debug.LogError($"找不到 {path}");
                return;
            }

            _lang = null;
            var languageKeys = new HashSet<string>(LoadLanguage().Keys, StringComparer.Ordinal);
            if (!TryParseAttackProfiles(
                    File.ReadAllLines(path),
                    languageKeys,
                    path,
                    out var rows,
                    out string reason))
            {
                Debug.LogError($"[AttackProfiles] {reason}");
                return;
            }

            if (!ValidateCharacterBasicAttackBindings(rows, out reason) ||
                !ValidateAttackProfileAssetPaths(rows, out reason))
            {
                Debug.LogError($"[AttackProfiles] {reason}");
                return;
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var row in rows)
                {
                    string assetPath = $"Assets/Data/AttackProfiles/AttackProfile_{row.AttackProfileId}.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<AttackProfileData>(assetPath);
                    if (asset == null)
                    {
                        asset = ScriptableObject.CreateInstance<AttackProfileData>();
                        AssetCommitter.EnsureDirectory(assetPath);
                        AssetDatabase.CreateAsset(asset, assetPath);
                    }

                    row.CopyTo(asset);
                    EditorUtility.SetDirty(asset);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[AttackProfiles] 已严格导入 {rows.Count} 个攻击档案");
        }

        /// <summary>
        /// 供 EditMode 回归直接验证严格表结构；不写入任何 asset。
        /// </summary>
        public static bool TryValidateAttackProfileCsv(
            string[] lines,
            IReadOnlyCollection<string> languageKeys,
            out string reason)
        {
            return TryParseAttackProfiles(lines, languageKeys, "AttackProfiles.csv", out _, out reason);
        }

        /// <summary>
        /// 供 EditMode fixture 投影：按契约原表头严格解析后，把每行经
        /// <see cref="AttackProfileImportRow.CopyTo"/> 投影为内存 <see cref="AttackProfileData"/>，
        /// 不写入 AssetDatabase、不创建任何 asset。任一字段失败时整批失败并销毁全部临时对象。
        /// </summary>
        public static bool TryBuildAttackProfileProjection(
            string[] lines,
            IReadOnlyCollection<string> languageKeys,
            out AttackProfileData[] profiles,
            out string reason)
        {
            profiles = Array.Empty<AttackProfileData>();
            if (!TryParseAttackProfiles(lines, languageKeys, "AttackProfiles.fixture.csv", out var rows, out reason))
                return false;

            var created = new List<AttackProfileData>();
            try
            {
                foreach (var row in rows)
                {
                    var profile = ScriptableObject.CreateInstance<AttackProfileData>();
                    row.CopyTo(profile);
                    if (!profile.TryValidate(out string validationReason))
                        throw new InvalidDataException(validationReason);
                    created.Add(profile);
                }

                profiles = created.ToArray();
                reason = string.Empty;
                return true;
            }
            catch (InvalidDataException exception)
            {
                reason = exception.Message;
                foreach (var profile in created)
                    UnityEngine.Object.DestroyImmediate(profile);
                created.Clear();
                return false;
            }
        }

        /// <summary>
        /// 供 EditMode fixture 验证同 ID asset 冲突：按契约规范路径
        /// (Assets/Data/AttackProfiles/AttackProfile_&lt;id&gt;.asset) 扫描既有
        /// <see cref="AttackProfileData"/> asset，任一既有 asset 的 ID 与路径不一致即失败。
        /// 不写入任何 asset。
        /// </summary>
        public static bool TryValidateAttackProfileAssetIdProjection(
            IReadOnlyCollection<string> attackProfileIds,
            out string reason)
        {
            var canonicalPaths = attackProfileIds.ToDictionary(
                id => id,
                id => $"Assets/Data/AttackProfiles/AttackProfile_{id}.asset",
                StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:AttackProfileData"))
            {
                string existingPath = AssetDatabase.GUIDToAssetPath(guid);
                var existing = AssetDatabase.LoadAssetAtPath<AttackProfileData>(existingPath);
                if (existing != null && !string.IsNullOrWhiteSpace(existing.attackProfileId) &&
                    canonicalPaths.TryGetValue(existing.attackProfileId, out string canonicalPath) &&
                    !string.Equals(existingPath, canonicalPath, StringComparison.Ordinal))
                {
                    reason = "attack_profile_asset_id_duplicate";
                    return false;
                }
            }

            foreach (var id in attackProfileIds)
            {
                string path = $"Assets/Data/AttackProfiles/AttackProfile_{id}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<AttackProfileData>(path);
                if (existing != null && !string.IsNullOrEmpty(existing.attackProfileId) &&
                    !string.Equals(existing.attackProfileId, id, StringComparison.Ordinal))
                {
                    reason = "attack_profile_asset_id_conflict";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private sealed class AttackProfileImportRow
        {
            public string AttackProfileId;
            public string DisplayNameKey;
            public AttackProfileKind ProfileKind;
            public BasicAttackBindingKind BasicBindingKind;
            public string ContentScope;
            public string SourceAffiliation;
            public string RealmRequirementId;
            public string ElementRequirementId;
            public AttackEffectType EffectType;
            public string DamageElementId;
            public float PhysicalDamageMultiplier;
            public float SoulDamageMultiplier;
            public int HealAmount;
            public float BuffMultiplier;
            public float DefensePenetration;
            public AttackResourceKind ResourceKind;
            public int ResourceCost;
            public int CooldownTicks;
            public int MinCastRange;
            public int MaxCastRange;
            public AttackTargetingMode TargetingMode;
            public AttackAreaCenterKind AreaCenterKind;
            public AttackAreaShapeKind AreaShapeKind;
            public int AreaRadius;
            public int AreaLength;
            public int AreaFanHalfAngleSteps;
            public int AreaFacing = -1;
            public int AreaInnerRadius;
            public AttackAreaEffectBlocker AreaEffectBlockers;
            public AttackAreaTargetFaction AreaAllowedFactions;
            public AttackAreaTargetState AreaAllowedStates;
            public bool IsDomain;
            public bool IsBloodline;
            public string SpecialEffectTextKey;

            public void CopyTo(AttackProfileData asset)
            {
                asset.attackProfileId = AttackProfileId;
                asset.displayNameKey = DisplayNameKey;
                asset.profileKind = ProfileKind;
                asset.basicBindingKind = BasicBindingKind;
                asset.contentScope = ContentScope;
                asset.sourceAffiliation = SourceAffiliation;
                asset.realmRequirementId = RealmRequirementId;
                asset.elementRequirementId = ElementRequirementId;
                asset.effectType = EffectType;
                asset.damageElementId = DamageElementId;
                asset.physicalDamageMultiplier = PhysicalDamageMultiplier;
                asset.soulDamageMultiplier = SoulDamageMultiplier;
                asset.healAmount = HealAmount;
                asset.buffMultiplier = BuffMultiplier;
                asset.defensePenetration = DefensePenetration;
                asset.resourceKind = ResourceKind;
                asset.resourceCost = ResourceCost;
                asset.cooldownTicks = CooldownTicks;
                asset.minCastRange = MinCastRange;
                asset.maxCastRange = MaxCastRange;
                asset.targetingMode = TargetingMode;
                asset.areaCenterKind = AreaCenterKind;
                asset.areaShapeKind = AreaShapeKind;
                asset.areaRadius = AreaRadius;
                asset.areaLength = AreaLength;
                asset.areaFanHalfAngleSteps = AreaFanHalfAngleSteps;
                asset.areaFacing = AreaFacing;
                asset.areaInnerRadius = AreaInnerRadius;
                asset.areaEffectBlockers = AreaEffectBlockers;
                asset.areaAllowedFactions = AreaAllowedFactions;
                asset.areaAllowedStates = AreaAllowedStates;
                asset.isDomain = IsDomain;
                asset.isBloodline = IsBloodline;
                asset.specialEffectTextKey = SpecialEffectTextKey;
            }
        }

        private static bool TryParseAttackProfiles(
            string[] lines,
            IReadOnlyCollection<string> languageKeys,
            string sourceName,
            out List<AttackProfileImportRow> rows,
            out string reason)
        {
            rows = new List<AttackProfileImportRow>();
            reason = string.Empty;
            if (lines == null || languageKeys == null)
            {
                reason = "attack_profile_table_or_language_keys_missing";
                return false;
            }

            int headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
            if (headerLineIndex < 0)
            {
                reason = "attack_profile_header_missing";
                return false;
            }

            var headers = CsvTableReader.ParseRow(lines[headerLineIndex]);
            if (headers.Length != AttackProfileColumns.Length ||
                !headers.Select(value => value.Trim()).SequenceEqual(AttackProfileColumns, StringComparer.Ordinal))
            {
                reason = "attack_profile_header_not_exact";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int lineNumber = headerLineIndex + 1; lineNumber < lines.Length; lineNumber++)
            {
                string line = lines[lineNumber];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;

                var columns = CsvTableReader.ParseRow(line);
                if (columns.Length != headers.Length)
                {
                    reason = $"attack_profile_short_or_extra_row:line_{lineNumber + 1}";
                    return false;
                }

                try
                {
                    var row = ParseAttackProfileRow(headers, columns, sourceName, languageKeys);
                    if (!ids.Add(row.AttackProfileId))
                        throw new InvalidDataException("attack_profile_id_duplicate");
                    rows.Add(row);
                }
                catch (InvalidDataException exception)
                {
                    reason = $"{exception.Message}:line_{lineNumber + 1}";
                    rows.Clear();
                    return false;
                }
            }

            if (rows.Count == 0)
            {
                reason = "attack_profile_rows_missing";
                return false;
            }

            return true;
        }

        private static AttackProfileImportRow ParseAttackProfileRow(
            string[] headers,
            string[] columns,
            string sourceName,
            IReadOnlyCollection<string> languageKeys)
        {
            string Value(string name) => GetAttackProfileValue(headers, columns, name, sourceName);
            string Required(string name)
            {
                string value = Value(name);
                if (string.IsNullOrEmpty(value))
                    throw new InvalidDataException($"attack_profile_required_{name}_missing");
                return value;
            }
            void RequireEmpty(string name)
            {
                if (!string.IsNullOrEmpty(Value(name)))
                    throw new InvalidDataException($"attack_profile_{name}_must_be_empty");
            }

            var row = new AttackProfileImportRow
            {
                AttackProfileId = Required("attackProfileId"),
                DisplayNameKey = Required("displayNameKey"),
                ProfileKind = ParseAttackProfileKind(Required("profileKind")),
                EffectType = ParseAttackEffectType(Required("effectType")),
                ResourceKind = ParseAttackResourceKind(Required("resourceKind")),
                ResourceCost = ParseNonNegativeInt(Required("resourceCost"), "resourceCost"),
                CooldownTicks = ParseNonNegativeInt(Required("cooldownTicks"), "cooldownTicks"),
                MinCastRange = ParseNonNegativeInt(Required("minCastRange"), "minCastRange"),
                MaxCastRange = ParseNonNegativeInt(Required("maxCastRange"), "maxCastRange"),
                TargetingMode = ParseAttackTargetingMode(Required("targetingMode")),
            };

            if (!System.Text.RegularExpressions.Regex.IsMatch(row.AttackProfileId, "^[a-z][a-z0-9_]*$"))
                throw new InvalidDataException("attack_profile_id_invalid");
            if (!languageKeys.Contains(row.DisplayNameKey))
                throw new InvalidDataException("attack_profile_display_key_unknown");
            if (row.MaxCastRange < row.MinCastRange)
                throw new InvalidDataException("attack_profile_cast_range_invalid");

            if (row.ProfileKind == AttackProfileKind.Basic)
            {
                row.BasicBindingKind = ParseBasicAttackBindingKind(Required("basicBindingKind"));
                RequireEmpty("contentScope");
                RequireEmpty("sourceAffiliation");
                RequireEmpty("realmRequirementId");
                RequireEmpty("elementRequirementId");
                if (row.ResourceKind != AttackResourceKind.None || row.ResourceCost != 0 || row.CooldownTicks != 0)
                    throw new InvalidDataException("basic_attack_resource_or_cooldown_invalid");
            }
            else
            {
                RequireEmpty("basicBindingKind");
                row.ContentScope = Required("contentScope");
                row.SourceAffiliation = Required("sourceAffiliation");
                row.RealmRequirementId = Required("realmRequirementId");
                row.ElementRequirementId = Required("elementRequirementId");
                if (!ContentScopePolicy.IsKnown(row.ContentScope) ||
                    !IsKnownRealmRequirement(row.RealmRequirementId) ||
                    !IsKnownElementRequirement(row.ElementRequirementId))
                {
                    throw new InvalidDataException("attack_profile_requirement_reference_unknown");
                }
            }

            ParseAttackEffectFields(row, Value, Required, RequireEmpty);
            ParseAttackTargetingFields(row, Value, Required, RequireEmpty);

            if (row.ProfileKind == AttackProfileKind.Divine)
            {
                row.IsDomain = ParseStrictBool(Required("isDomain"), "isDomain");
                row.IsBloodline = ParseStrictBool(Required("isBloodline"), "isBloodline");
                row.SpecialEffectTextKey = Value("specialEffectTextKey");
                if (!string.IsNullOrEmpty(row.SpecialEffectTextKey) && !languageKeys.Contains(row.SpecialEffectTextKey))
                    throw new InvalidDataException("attack_profile_special_effect_key_unknown");
            }
            else
            {
                RequireEmpty("isDomain");
                RequireEmpty("isBloodline");
                RequireEmpty("specialEffectTextKey");
            }

            var probe = ScriptableObject.CreateInstance<AttackProfileData>();
            try
            {
                row.CopyTo(probe);
                if (!probe.TryValidate(out string validationReason))
                    throw new InvalidDataException(validationReason);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }

            return row;
        }

        private static void ParseAttackEffectFields(
            AttackProfileImportRow row,
            Func<string, string> value,
            Func<string, string> required,
            Action<string> requireEmpty)
        {
            bool physical = row.EffectType is AttackEffectType.Physical or AttackEffectType.Hybrid;
            bool magic = row.EffectType is AttackEffectType.Magic or AttackEffectType.Hybrid;
            if (physical)
                row.PhysicalDamageMultiplier = ParseNonNegativeFloat(required("physicalDamageMultiplier"), "physicalDamageMultiplier");
            else
                requireEmpty("physicalDamageMultiplier");
            if (magic)
                row.SoulDamageMultiplier = ParseNonNegativeFloat(required("soulDamageMultiplier"), "soulDamageMultiplier");
            else
                requireEmpty("soulDamageMultiplier");
            if (physical || magic)
            {
                row.DamageElementId = required("damageElementId");
                if (!IsKnownDamageElement(row.DamageElementId))
                    throw new InvalidDataException("attack_profile_damage_element_unknown");
                string penetration = value("defensePenetration");
                row.DefensePenetration = string.IsNullOrEmpty(penetration)
                    ? 0f
                    : ParseNonNegativeFloat(penetration, "defensePenetration");
            }
            else
            {
                requireEmpty("damageElementId");
                requireEmpty("defensePenetration");
            }

            if (row.EffectType == AttackEffectType.Heal)
                row.HealAmount = ParseNonNegativeInt(required("healAmount"), "healAmount");
            else
                requireEmpty("healAmount");
            if (row.EffectType is AttackEffectType.Buff or AttackEffectType.Debuff)
                row.BuffMultiplier = ParseNonNegativeFloat(required("buffMultiplier"), "buffMultiplier");
            else
                requireEmpty("buffMultiplier");
        }

        private static void ParseAttackTargetingFields(
            AttackProfileImportRow row,
            Func<string, string> value,
            Func<string, string> required,
            Action<string> requireEmpty)
        {
            if (row.TargetingMode == AttackTargetingMode.Single)
            {
                foreach (string column in AttackProfileColumns.SkipWhile(name => name != "areaCenterKind").Take(10))
                    requireEmpty(column);
                return;
            }

            row.AreaCenterKind = ParseAreaCenterKind(required("areaCenterKind"));
            row.AreaShapeKind = ParseAreaShapeKind(required("areaShapeKind"));
            row.AreaRadius = ParseNonNegativeInt(required("areaRadius"), "areaRadius");
            row.AreaLength = ParseNonNegativeInt(required("areaLength"), "areaLength");
            row.AreaFanHalfAngleSteps = ParseNonNegativeInt(required("areaFanHalfAngleSteps"), "areaFanHalfAngleSteps");
            row.AreaInnerRadius = ParseNonNegativeInt(required("areaInnerRadius"), "areaInnerRadius");
            row.AreaEffectBlockers = ParseAreaEffectBlockers(required("areaEffectBlockers"));
            row.AreaAllowedFactions = ParseAreaTargetFactions(required("areaAllowedFactions"));
            row.AreaAllowedStates = ParseAreaTargetStates(required("areaAllowedStates"));
            string facing = value("areaFacing");
            if (row.AreaShapeKind == AttackAreaShapeKind.Circle)
            {
                requireEmpty("areaFacing");
            }
            else
            {
                row.AreaFacing = ParseAreaFacing(required("areaFacing"));
            }
        }

        private static bool ValidateCharacterBasicAttackBindings(
            IReadOnlyList<AttackProfileImportRow> rows,
            out string reason)
        {
            var profiles = rows.ToDictionary(row => row.AttackProfileId, StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:CharacterData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var character = AssetDatabase.LoadAssetAtPath<CharacterData>(path);
                if (character == null)
                    continue;

                bool hasMain = !string.IsNullOrWhiteSpace(character.mainEquipmentBasicAttackProfileId);
                bool hasUnarmed = !string.IsNullOrWhiteSpace(character.unarmedBasicAttackProfileId);
                if (!hasMain && !hasUnarmed)
                    continue;
                if (hasMain == hasUnarmed)
                {
                    reason = "basic_attack_binding_missing_or_ambiguous";
                    return false;
                }

                string id = hasMain
                    ? character.mainEquipmentBasicAttackProfileId
                    : character.unarmedBasicAttackProfileId;
                if (!profiles.TryGetValue(id, out var profile))
                {
                    reason = "basic_attack_profile_not_found";
                    return false;
                }

                bool bindingMatches = profile.ProfileKind == AttackProfileKind.Basic &&
                    (hasMain && profile.BasicBindingKind == BasicAttackBindingKind.MainEquipment ||
                     hasUnarmed && profile.BasicBindingKind == BasicAttackBindingKind.UnarmedFallback);
                if (!bindingMatches)
                {
                    reason = "basic_attack_profile_binding_kind_invalid";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidateAttackProfileAssetPaths(
            IReadOnlyList<AttackProfileImportRow> rows,
            out string reason)
        {
            var canonicalPaths = rows.ToDictionary(
                row => row.AttackProfileId,
                row => $"Assets/Data/AttackProfiles/AttackProfile_{row.AttackProfileId}.asset",
                StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:AttackProfileData"))
            {
                string existingPath = AssetDatabase.GUIDToAssetPath(guid);
                var existing = AssetDatabase.LoadAssetAtPath<AttackProfileData>(existingPath);
                if (existing != null && !string.IsNullOrWhiteSpace(existing.attackProfileId) &&
                    canonicalPaths.TryGetValue(existing.attackProfileId, out string canonicalPath) &&
                    !string.Equals(existingPath, canonicalPath, StringComparison.Ordinal))
                {
                    reason = "attack_profile_asset_id_duplicate";
                    return false;
                }
            }

            foreach (var row in rows)
            {
                string path = $"Assets/Data/AttackProfiles/AttackProfile_{row.AttackProfileId}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<AttackProfileData>(path);
                if (existing != null && !string.IsNullOrEmpty(existing.attackProfileId) &&
                    !string.Equals(existing.attackProfileId, row.AttackProfileId, StringComparison.Ordinal))
                {
                    reason = "attack_profile_asset_id_conflict";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static string GetAttackProfileValue(string[] headers, string[] columns, string name, string sourceName)
        {
            int index = Array.IndexOf(headers, name);
            if (index < 0 || index >= columns.Length)
                throw new InvalidDataException($"attack_profile_{name}_missing_in_{sourceName}");
            return columns[index]?.Trim() ?? string.Empty;
        }

        private static AttackProfileKind ParseAttackProfileKind(string value) => value switch
        {
            "basic" => AttackProfileKind.Basic,
            "art" => AttackProfileKind.Art,
            "divine" => AttackProfileKind.Divine,
            _ => throw new InvalidDataException("attack_profile_kind_invalid"),
        };

        private static BasicAttackBindingKind ParseBasicAttackBindingKind(string value) => value switch
        {
            "main_equipment" => BasicAttackBindingKind.MainEquipment,
            "unarmed_fallback" => BasicAttackBindingKind.UnarmedFallback,
            _ => throw new InvalidDataException("basic_attack_binding_kind_invalid"),
        };

        private static AttackEffectType ParseAttackEffectType(string value) => value switch
        {
            "physical" => AttackEffectType.Physical,
            "magic" => AttackEffectType.Magic,
            "heal" => AttackEffectType.Heal,
            "buff" => AttackEffectType.Buff,
            "debuff" => AttackEffectType.Debuff,
            "movement" => AttackEffectType.Movement,
            "hybrid" => AttackEffectType.Hybrid,
            _ => throw new InvalidDataException("attack_profile_effect_type_invalid"),
        };

        private static AttackResourceKind ParseAttackResourceKind(string value) => value switch
        {
            "none" => AttackResourceKind.None,
            "mp" => AttackResourceKind.Mp,
            _ => throw new InvalidDataException("attack_profile_resource_kind_invalid"),
        };

        private static AttackTargetingMode ParseAttackTargetingMode(string value) => value switch
        {
            "single" => AttackTargetingMode.Single,
            "area" => AttackTargetingMode.Area,
            _ => throw new InvalidDataException("attack_profile_targeting_mode_invalid"),
        };

        private static AttackAreaCenterKind ParseAreaCenterKind(string value) => value switch
        {
            "caster" => AttackAreaCenterKind.Caster,
            "target_cell" => AttackAreaCenterKind.TargetCell,
            _ => throw new InvalidDataException("attack_profile_area_center_invalid"),
        };

        private static AttackAreaShapeKind ParseAreaShapeKind(string value) => value switch
        {
            "circle" => AttackAreaShapeKind.Circle,
            "line" => AttackAreaShapeKind.Line,
            "fan" => AttackAreaShapeKind.Fan,
            _ => throw new InvalidDataException("attack_profile_area_shape_invalid"),
        };

        private static AttackAreaEffectBlocker ParseAreaEffectBlockers(string value) => value switch
        {
            "none" => AttackAreaEffectBlocker.None,
            "directed_edge" => AttackAreaEffectBlocker.DirectedEdge,
            _ => throw new InvalidDataException("attack_profile_area_blocker_invalid"),
        };

        private static AttackAreaTargetFaction ParseAreaTargetFactions(string value)
        {
            return ParseSortedFlagSet(value, "areaAllowedFactions", new Dictionary<string, AttackAreaTargetFaction>
            {
                ["enemy"] = AttackAreaTargetFaction.Enemy,
                ["ally"] = AttackAreaTargetFaction.Ally,
                ["self"] = AttackAreaTargetFaction.Self,
            });
        }

        private static AttackAreaTargetState ParseAreaTargetStates(string value)
        {
            return ParseSortedFlagSet(value, "areaAllowedStates", new Dictionary<string, AttackAreaTargetState>
            {
                ["alive"] = AttackAreaTargetState.Alive,
                ["corpse"] = AttackAreaTargetState.Corpse,
            });
        }

        private static T ParseSortedFlagSet<T>(string value, string fieldName, Dictionary<string, T> allowed)
            where T : struct, Enum
        {
            var values = value.Split(',');
            if (values.Length == 0 || values.Any(string.IsNullOrEmpty) ||
                !values.SequenceEqual(values.OrderBy(item => item, StringComparer.Ordinal)))
            {
                throw new InvalidDataException($"attack_profile_{fieldName}_not_sorted");
            }

            long result = 0;
            foreach (string item in values)
            {
                if (!allowed.TryGetValue(item, out T parsed))
                    throw new InvalidDataException($"attack_profile_{fieldName}_invalid");
                result |= Convert.ToInt64(parsed, CultureInfo.InvariantCulture);
            }
            return (T)Enum.ToObject(typeof(T), result);
        }

        private static int ParseAreaFacing(string value) => value switch
        {
            "east" => 0,
            "north_east" => 1,
            "north_west" => 2,
            "west" => 3,
            "south_west" => 4,
            "south_east" => 5,
            _ => throw new InvalidDataException("attack_profile_area_facing_invalid"),
        };

        private static bool ParseStrictBool(string value, string fieldName) => value switch
        {
            "0" => false,
            "1" => true,
            _ => throw new InvalidDataException($"attack_profile_{fieldName}_invalid"),
        };

        private static int ParseNonNegativeInt(string value, string fieldName)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
                throw new InvalidDataException($"attack_profile_{fieldName}_invalid");
            return parsed;
        }

        private static float ParseNonNegativeFloat(string value, string fieldName)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ||
                float.IsNaN(parsed) || float.IsInfinity(parsed) || parsed < 0f)
            {
                throw new InvalidDataException($"attack_profile_{fieldName}_invalid");
            }
            return parsed;
        }

        private static bool IsKnownRealmRequirement(string value) => value is
            "realm_fanren" or "realm_lianqi" or "realm_zhuji" or "realm_jindan" or
            "realm_yuanying" or "realm_huashen";

        private static bool IsKnownElementRequirement(string value)
        {
            if (value == "element_none")
                return true;
            if (!value.StartsWith("element_", StringComparison.Ordinal))
                return false;
            return value.Substring("element_".Length)
                .Replace("_root", string.Empty)
                .Split(new[] { "_or_" }, StringSplitOptions.RemoveEmptyEntries)
                .All(part => IsKnownElementId("element_" + part));
        }

        private static bool IsKnownDamageElement(string value) => IsKnownElementId(value);

        private static bool IsKnownElementId(string value) => value is
            "element_metal" or "element_wood" or "element_water" or "element_fire" or
            "element_earth" or "element_wind" or "element_thunder" or "element_ice" or
            "element_dark" or "element_star" or "element_poison" or "element_chaos" or
            "element_none";

        [MenuItem("天章/导入神通配置")]
        public static void ImportSkills()
        {
            _lang = null;
            string path = "Assets/DataConfig/Skills.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
            var headers = CsvTableReader.FindHeader(lines);
            CsvTableReader.RequireColumns(headers, path,
                "name", "type", "minRange", "maxRange", "mpCost",
                "cooldownTicks", "damageMultiplier", "healAmount",
                "cannotBlock", "cannotDodge", "penetratingShield", "stunChance",
                "isDomain", "isBloodline", "specialEffectDesc",
                "element", "realmReq", "sourceAffiliation", "contentScope");

            foreach (var line in lines.Skip(headerLineIndex + 1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = CsvTableReader.ParseRow(line);
                if (cols.Length < headers.Length)
                {
                    Debug.LogWarning($"[Importer] {path} row has {cols.Length} columns, expected >= {headers.Length}; skipping");
                    continue;
                }

                string assetPath = $"Assets/Data/Skills/Skill_{AssetCommitter.SanitizeName(CsvTableReader.GetRequiredValue(headers, cols, "name", path))}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<DivineSkillData>(assetPath);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<DivineSkillData>();
                    AssetCommitter.EnsureDirectory(assetPath);
                    AssetDatabase.CreateAsset(asset, assetPath);
                }
                asset.skillName = T(CsvTableReader.GetRequiredValue(headers, cols, "name", path));
                asset.type = (SpellType)int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "type", path));
                asset.minRange = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "minRange", path));
                asset.maxRange = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "maxRange", path));
                asset.contentScope = GetRequiredContentScope(headers, cols, path);
                asset.realmRequirement = CsvTableReader.GetRequiredValue(headers, cols, "realmReq", path);
                asset.sourceAffiliation = CsvTableReader.GetRequiredValue(headers, cols, "sourceAffiliation", path);
                asset.mpCost = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "mpCost", path));
                asset.cooldownTicks = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "cooldownTicks", path));
                asset.damageMultiplier = float.Parse(CsvTableReader.GetRequiredValue(headers, cols, "damageMultiplier", path));
                asset.healAmount = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "healAmount", path));
                asset.cannotBlock = CsvTableReader.GetRequiredValue(headers, cols, "cannotBlock", path) == "1";
                asset.cannotDodge = CsvTableReader.GetRequiredValue(headers, cols, "cannotDodge", path) == "1";
                asset.penetratingShield = CsvTableReader.GetRequiredValue(headers, cols, "penetratingShield", path) == "1";
                asset.stunChance = float.Parse(CsvTableReader.GetRequiredValue(headers, cols, "stunChance", path));
                // 五行属性（从独立 element 列解析）
                asset.element = CombatElementFacts.ResolveElement(
                    CsvTableReader.GetRequiredValue(headers, cols, "element", path));
                asset.isDomain = CsvTableReader.GetRequiredValue(headers, cols, "isDomain", path) == "1";
                asset.isBloodline = CsvTableReader.GetRequiredValue(headers, cols, "isBloodline", path) == "1";
                asset.specialEffectDesc = T(CsvTableReader.GetRequiredValue(headers, cols, "specialEffectDesc", path));

                EditorUtility.SetDirty(asset);
                Debug.Log($"  神通: {asset.skillName} ← {assetPath}");
            }

            AssetDatabase.SaveAssets();
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
