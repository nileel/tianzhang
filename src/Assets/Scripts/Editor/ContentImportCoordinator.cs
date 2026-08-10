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
using TianZhang.Game.CharacterCreation;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Tactical;
using TianZhang.World;

namespace TianZhang.Editor
{
    /// <summary>
    /// CSV 配置导入工具（v3 — 按列名/表头解析）
    /// ⚠️ 已修改/未审核；修改方：Claude Code
    /// 从 Assets/DataConfig/*.csv 读取数据，通过 Language.csv 解析文本 ID
    /// 生成 ScriptableObject .asset 文件
    /// v3 变更：所有导入器不再依赖硬编码列序，改为按表头列名读取；Characters/Enemies 同步升级。
    /// </summary>
    public class ContentImportCoordinator : EditorWindow
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
            ImportNpcCultivationActionWeightProfiles();
            ImportFoundationPurpleMansionStates();
            ImportJindanStaticStates();
            ImportCharterRuleDefinitions();
            ImportCharterSites();
            ImportGongFa();
            ImportSpells();
            ImportSkills();
            ImportCharacters();
            ImportContentCatalog();
            ImportCharacterCreationPointBuy();
            ImportEnvironmentProfiles();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ContentImportCoordinator] 全部配置导入完成");
        }

        private static readonly string[] ContentSettlementColumns =
        {
            "settlementId", "displayNameKey", "contentScope", "settlementType", "regionId",
            "ownerFactionId", "visualThemeId", "features", "adventureEntranceIds"
        };

        private static readonly string[] ContentItemColumns =
        {
            "itemId", "displayNameKey", "descriptionKey", "contentScope", "itemCategory", "maxStack"
        };

        private static readonly string[] ContentBountyColumns =
        {
            "bountyId", "titleKey", "descriptionKey", "contentScope", "issuerSettlementId",
            "objectiveType", "targetEnemyId", "requiredCount", "allowedAdventureId", "rewardEntries", "repeatPolicy"
        };

        private static readonly string[] ContentEnemyColumns =
        {
            "name", "type", "aiType", "realm", "realmMultiplier", "rootBone", "physique", "spirit", "mind",
            "reaction", "talent", "blockRate", "blockReduction", "soulShieldRate", "soulShieldReduction",
            "dodgeRate", "critRate", "critDamage", "hitRateBonus", "equippedSpells", "dropTable", "description",
            "contentScope", "dropEntries", "unarmedBasicAttackProfileId"
        };

        private static readonly string[] CharterSiteColumns =
        {
            "siteId", "displayNameKey", "settlementId",
            "passageCapabilityId", "passageOperatorId", "passageTargetId", "passageProtocolState",
            "passageStructureState", "passagePowerState", "interactionTimeProfileId", "recognitionTiming",
            "operationTiming", "cancellationPolicy",
            "facilityId", "sealRelicId", "sealManagerId", "sealBeneficiaryId", "sealAuthorizationVersionId",
            "ruleEntryId", "ruleEntryOccupancyId", "nodeOccupancyId",
            "jindanConflictEventId", "jindanChallengeEventId",
            "grantId", "grantDefinitionVersion", "grantTargetVariableId", "grantChallengerId",
            "grantQualificationSource", "grantAllowedOperationId", "grantTargetId", "grantScopeId",
            "grantBeneficiaryId", "grantRealityAnchorId", "grantResourceLedgerRef", "grantCapacityLedgerRef",
            "grantChallengeRuleTier", "grantEffectiveAtTick", "grantExpiresAtTick", "grantIsRevoked",
            "grantRevocationReason", "grantDisplaySource",
            "leftCandidateId", "leftCandidateTargetVariableId", "leftCandidateTargetId",
            "leftCandidateHasVariableAuthority", "leftCandidateHasLegalTarget", "leftCandidatePositionRank",
            "leftCandidateRealityAnchorRank", "leftCandidateAlreadyPaidCost",
            "leftCandidateHasActiveContinuousCarrier", "leftCandidateConflictReserve", "leftCandidatePulseCost",
            "leftCandidateSettlementCooldown",
            "rightCandidateId", "rightCandidateTargetVariableId", "rightCandidateTargetId",
            "rightCandidateHasVariableAuthority", "rightCandidateHasLegalTarget", "rightCandidatePositionRank",
            "rightCandidateRealityAnchorRank", "rightCandidateAlreadyPaidCost",
            "rightCandidateHasActiveContinuousCarrier", "rightCandidateConflictReserve", "rightCandidatePulseCost",
            "rightCandidateSettlementCooldown",
            "charterCandidateId",
            "yuanyingConflictEventId", "yuanyingTargetVariableId", "yuanyingTargetId", "yuanyingScopeId",
            "yuanyingRealityAnchorId"
        };

        private const string ContentScopeProduction = "content_scope_production";
        private const string GuanzhongScope = "guanzhong";
        private const string GuanzhongSettlementId = "guanzhong_city";
        private const string ShijiahouEnemyId = "enemy_shijiahou";
        private const string ShijiahouBountyId = "bounty_guanzhong_shijiahou";

        // 旧水驿站点契约的固定自有语义：通行能力、交互时间档案、门禁可操作状态与交互时序。
        private const string CharterSiteId = "charter_site_old_water_station";
        private const string CharterSiteDisplayNameKey = "charter_site_old_water_station";
        private const string CharterSiteDisplayNameText = "旧水驿";
        private const string CharterPassageCapabilityId = "capability_kaihe_jiuzhang_v1";
        private const string CharterGateProtocolState = "compatible";
        private const string CharterGateStructureState = "intact";
        private const string CharterGatePowerState = "available";
        private const string CharterInteractionTimeProfileId = "interaction_time_old_water_station_gate_v1";
        private const string CharterRecognitionTiming = "instant";
        private const string CharterOperationTiming = "sustained_guided";
        private const string CharterCancellationPolicy = "no_commit_on_cancel";
        private const string CharterSiteAssetPath = "Assets/Data/CharterSites/CharterSite_charter_site_old_water_station.asset";

        public sealed class ContentCatalogImportPreview
        {
            public SettlementData[] settlements = Array.Empty<SettlementData>();
            public EnemyData[] enemies = Array.Empty<EnemyData>();
            public ItemData[] items = Array.Empty<ItemData>();
            public BountyData[] bounties = Array.Empty<BountyData>();
            public CharacterData[] enemyTemplates = Array.Empty<CharacterData>();
            public string[] enemyTemplateIds = Array.Empty<string>();

            private void DestroyTransientAssets(UnityEngine.Object[] assets)
            {
                foreach (var asset in assets)
                {
                    if (asset != null && !AssetDatabase.Contains(asset))
                        UnityEngine.Object.DestroyImmediate(asset);
                }
            }

            internal void DestroyTransientAssets()
            {
                DestroyTransientAssets(settlements);
                DestroyTransientAssets(enemies);
                DestroyTransientAssets(items);
                DestroyTransientAssets(bounties);
                DestroyTransientAssets(enemyTemplates);
            }
        }

        private sealed class ContentCsvTable
        {
            public string SourceName;
            public string[] Headers;
            public List<string[]> Rows;
        }

        /// <summary>
        /// Builds all four formal content projections in memory. This is intentionally public for
        /// EditMode fixtures; it never writes an asset or changes the AssetDatabase.
        /// </summary>
        public static ContentCatalogImportPreview ParseContentCatalog(
            string[] languageLines,
            string[] settlementLines,
            string[] itemLines,
            string[] bountyLines,
            string[] enemyLines)
        {
            return ParseContentCatalog(
                languageLines, "Language.csv",
                settlementLines, "Settlements.csv",
                itemLines, "Items.csv",
                bountyLines, "Bounties.csv",
                enemyLines, "Enemies.csv");
        }

        [MenuItem("天章/导入正式内容目录")]
        public static void ImportContentCatalog()
        {
            const string languagePath = "Assets/DataConfig/Language.csv";
            const string settlementPath = "Assets/DataConfig/Settlements.csv";
            const string itemPath = "Assets/DataConfig/Items.csv";
            const string bountyPath = "Assets/DataConfig/Bounties.csv";
            const string enemyPath = "Assets/DataConfig/Enemies.csv";

            var preview = ParseContentCatalog(
                ReadRequiredContentFile(languagePath), languagePath,
                ReadRequiredContentFile(settlementPath), settlementPath,
                ReadRequiredContentFile(itemPath), itemPath,
                ReadRequiredContentFile(bountyPath), bountyPath,
                ReadRequiredContentFile(enemyPath), enemyPath);

            try
            {
                ValidateContentAssetLocations(preview);
                CommitContentCatalog(preview);
                Debug.Log("[ContentImportCoordinator] 正式内容目录导入完成");
            }
            finally
            {
                preview.DestroyTransientAssets();
            }
        }

        private static string[] ReadRequiredContentFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Content catalog CSV was not found: {path}", path);

            return File.ReadAllLines(path);
        }

        private static ContentCatalogImportPreview ParseContentCatalog(
            string[] languageLines,
            string languageSource,
            string[] settlementLines,
            string settlementSource,
            string[] itemLines,
            string itemSource,
            string[] bountyLines,
            string bountySource,
            string[] enemyLines,
            string enemySource)
        {
            var language = ParseContentLanguage(languageLines, languageSource);
            ValidateApprovedContentLanguage(language, languageSource);

            var preview = new ContentCatalogImportPreview
            {
                settlements = ParseSettlementRows(settlementLines, settlementSource, language),
                items = ParseItemRows(itemLines, itemSource, language),
            };

            try
            {
                var enemyResult = ParseEnemyRows(enemyLines, enemySource, language);
                preview.enemies = enemyResult.enemies;
                preview.enemyTemplates = enemyResult.templates;
                preview.enemyTemplateIds = enemyResult.templateIds;
                preview.bounties = ParseBountyRows(bountyLines, bountySource, language);
                ValidateContentCatalogReferences(preview, bountySource, enemySource);
                return preview;
            }
            catch
            {
                preview.DestroyTransientAssets();
                throw;
            }
        }

        private static Dictionary<string, string> ParseContentLanguage(string[] lines, string sourceName)
        {
            if (lines == null)
                throw new InvalidDataException($"{sourceName} has no rows.");

            var language = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;

                var columns = ParseCSV(line);
                if (columns.Length != 2)
                    throw new InvalidDataException($"{sourceName} language row must have exactly two columns.");

                var key = columns[0].Trim();
                var text = columns[1].Trim();
                RequireStableId(key, sourceName, "language key");
                if (string.IsNullOrEmpty(text) || !language.TryAdd(key, text))
                    throw new InvalidDataException($"{sourceName} has an empty or duplicate language key '{key}'.");
            }

            if (language.Count == 0)
                throw new InvalidDataException($"{sourceName} has no language rows.");

            return language;
        }

        private static void ValidateApprovedContentLanguage(Dictionary<string, string> language, string sourceName)
        {
            RequireLanguageText(language, "settlement_guanzhong_city", "关中城", sourceName);
            RequireLanguageText(language, "settlement_feature_bounty_board", "悬赏板", sourceName);
            RequireLanguageText(language, "item_lingshi_low_description", "劣质灵石是稳定 ID 为 `item_lingshi_low` 的基础资源。", sourceName);
            RequireLanguageText(language, "item_shijia_piece_description", "石甲碎片是稳定 ID 为 `item_shijia_piece` 的妖兽材料。", sourceName);
            RequireLanguageText(language, "bounty_guanzhong_shijiahou_title", "石甲兽悬赏 · 一次性除害令", sourceName);
            RequireLanguageText(language, "bounty_guanzhong_shijiahou_description", "黄土旧道石甲兽阻路伤人，击杀有赏。——坊市联盟", sourceName);
        }

        private static void RequireLanguageText(
            Dictionary<string, string> language,
            string key,
            string expectedText,
            string sourceName)
        {
            if (!language.TryGetValue(key, out var actualText) || !string.Equals(actualText, expectedText, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{sourceName} key '{key}' must preserve its approved production text exactly.");
            }
        }

        private static SettlementData[] ParseSettlementRows(
            string[] lines,
            string sourceName,
            Dictionary<string, string> language)
        {
            var table = ReadContentTable(lines, sourceName, ContentSettlementColumns);
            var settlements = new List<SettlementData>();
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in table.Rows)
            {
                var settlementId = Required(table, row, "settlementId");
                RequireStableId(settlementId, sourceName, "settlementId");
                if (!ids.Add(settlementId))
                    throw new InvalidDataException($"{sourceName} contains duplicate settlementId '{settlementId}'.");

                var settlement = ScriptableObject.CreateInstance<SettlementData>();
                settlement.settlementId = settlementId;
                settlement.displayNameKey = RequiredLanguageKey(language, Required(table, row, "displayNameKey"), sourceName, "displayNameKey");
                settlement.contentScope = Required(table, row, "contentScope");
                settlement.settlementType = Required(table, row, "settlementType");
                settlement.regionId = Required(table, row, "regionId");
                settlement.ownerFactionId = Required(table, row, "ownerFactionId");
                settlement.visualThemeId = Required(table, row, "visualThemeId");
                settlement.features = ParseSettlementFeatures(Required(table, row, "features"), language, sourceName);
                settlement.adventureEntranceIds = ParseStableIdList(Required(table, row, "adventureEntranceIds"), sourceName, "adventureEntranceIds");
                ValidateApprovedSettlement(settlement, sourceName);
                settlements.Add(settlement);
            }

            if (settlements.Count != 1 || settlements[0].settlementId != GuanzhongSettlementId)
                throw new InvalidDataException($"{sourceName} must contain only the approved '{GuanzhongSettlementId}' production row.");

            return settlements.ToArray();
        }

        private static SettlementFeatureData[] ParseSettlementFeatures(
            string rawFeatures,
            Dictionary<string, string> language,
            string sourceName)
        {
            var features = new List<SettlementFeatureData>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rawFeature in rawFeatures.Split('|'))
            {
                var fields = rawFeature.Split('~');
                if (fields.Length != 4)
                    throw new InvalidDataException($"{sourceName} feature entry '{rawFeature}' must have four fields.");

                var feature = new SettlementFeatureData
                {
                    featureId = fields[0].Trim(),
                    displayNameKey = RequiredLanguageKey(language, fields[1].Trim(), sourceName, "feature displayNameKey"),
                    availability = fields[2].Trim(),
                    disabledReasonKey = fields[3].Trim(),
                };

                RequireStableId(feature.featureId, sourceName, "featureId");
                if (!ids.Add(feature.featureId))
                    throw new InvalidDataException($"{sourceName} contains duplicate featureId '{feature.featureId}'.");
                if (feature.availability != "enabled" && feature.availability != "disabled")
                    throw new InvalidDataException($"{sourceName} feature '{feature.featureId}' has invalid availability.");
                if (feature.availability == "enabled" && !string.IsNullOrEmpty(feature.disabledReasonKey))
                    throw new InvalidDataException($"{sourceName} enabled feature '{feature.featureId}' must not define a disabled reason.");
                if (feature.availability == "disabled")
                    feature.disabledReasonKey = RequiredLanguageKey(language, feature.disabledReasonKey, sourceName, "disabledReasonKey");
                features.Add(feature);
            }

            if (features.Count == 0)
                throw new InvalidDataException($"{sourceName} features must not be empty.");

            return features.ToArray();
        }

        private static void ValidateApprovedSettlement(SettlementData settlement, string sourceName)
        {
            if (settlement.settlementId != GuanzhongSettlementId ||
                settlement.displayNameKey != "settlement_guanzhong_city" ||
                settlement.contentScope != ContentScopeProduction ||
                settlement.settlementType != "settlement_type_city" ||
                settlement.regionId != GuanzhongScope ||
                settlement.ownerFactionId != "faction_neutral" ||
                settlement.visualThemeId != "visual_theme_loess_city" ||
                settlement.features.Length != 1 ||
                settlement.features[0].featureId != "bounty_board" ||
                settlement.features[0].displayNameKey != "settlement_feature_bounty_board" ||
                settlement.features[0].availability != "enabled" ||
                settlement.adventureEntranceIds.Length != 1 ||
                settlement.adventureEntranceIds[0] != "guanzhong_wild")
            {
                throw new InvalidDataException($"{sourceName} does not match the approved '{GuanzhongSettlementId}' projection.");
            }
        }

        private static ItemData[] ParseItemRows(
            string[] lines,
            string sourceName,
            Dictionary<string, string> language)
        {
            var table = ReadContentTable(lines, sourceName, ContentItemColumns);
            var items = new List<ItemData>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in table.Rows)
            {
                var item = ScriptableObject.CreateInstance<ItemData>();
                item.itemId = Required(table, row, "itemId");
                RequireStableId(item.itemId, sourceName, "itemId");
                if (!ids.Add(item.itemId))
                    throw new InvalidDataException($"{sourceName} contains duplicate itemId '{item.itemId}'.");
                item.displayNameKey = RequiredLanguageKey(language, Required(table, row, "displayNameKey"), sourceName, "displayNameKey");
                item.descriptionKey = RequiredLanguageKey(language, Required(table, row, "descriptionKey"), sourceName, "descriptionKey");
                item.contentScope = Required(table, row, "contentScope");
                item.itemCategory = Required(table, row, "itemCategory");
                item.maxStack = ParsePositiveInteger(Required(table, row, "maxStack"), sourceName, "maxStack");
                ValidateApprovedItem(item, sourceName);
                items.Add(item);
            }

            if (items.Count != 2 || !ids.SetEquals(new[] { "item_lingshi_low", "item_shijia_piece" }))
                throw new InvalidDataException($"{sourceName} must contain only the two approved formal item rows.");

            return items.OrderBy(item => item.itemId, StringComparer.Ordinal).ToArray();
        }

        private static void ValidateApprovedItem(ItemData item, string sourceName)
        {
            if (item.contentScope != ContentScopeProduction || item.maxStack != 99)
                throw new InvalidDataException($"{sourceName} item '{item.itemId}' has an unapproved production scope or maxStack.");

            if (item.itemId == "item_lingshi_low" &&
                item.displayNameKey == "item_lingshi_low" &&
                item.descriptionKey == "item_lingshi_low_description" &&
                item.itemCategory == "basic_resource")
                return;
            if (item.itemId == "item_shijia_piece" &&
                item.displayNameKey == "item_shijia_piece" &&
                item.descriptionKey == "item_shijia_piece_description" &&
                item.itemCategory == "monster_material")
                return;

            throw new InvalidDataException($"{sourceName} item '{item.itemId}' does not match an approved production projection.");
        }

        private static (EnemyData[] enemies, CharacterData[] templates, string[] templateIds) ParseEnemyRows(
            string[] lines,
            string sourceName,
            Dictionary<string, string> language)
        {
            var table = ReadContentTable(lines, sourceName, ContentEnemyColumns);
            var templates = new List<CharacterData>();
            var templateIds = new List<string>();
            var enemies = new List<EnemyData>();
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in table.Rows)
            {
                var enemyId = Required(table, row, "name");
                RequireStableId(enemyId, sourceName, "enemyId");
                if (!ids.Add(enemyId))
                    throw new InvalidDataException($"{sourceName} contains duplicate enemyId '{enemyId}'.");

                var template = BuildEnemyTemplate(table, row, language, sourceName);
                templates.Add(template);
                templateIds.Add(enemyId);
                var contentScope = Value(table, row, "contentScope");
                var dropEntries = Value(table, row, "dropEntries");
                if (string.IsNullOrEmpty(contentScope) && string.IsNullOrEmpty(dropEntries))
                    continue;
                if (enemyId != ShijiahouEnemyId || contentScope != GuanzhongScope)
                    throw new InvalidDataException($"{sourceName} may only project '{ShijiahouEnemyId}' into the formal catalog.");

                var enemy = ScriptableObject.CreateInstance<EnemyData>();
                enemy.enemyId = enemyId;
                enemy.displayNameKey = RequiredLanguageKey(language, enemyId, sourceName, "enemy displayNameKey");
                enemy.descriptionKey = RequiredLanguageKey(language, Required(table, row, "description"), sourceName, "enemy descriptionKey");
                enemy.contentScope = contentScope;
                enemy.enemyTypeId = Required(table, row, "type");
                enemy.aiProfileId = Required(table, row, "aiType");
                enemy.realmId = Required(table, row, "realm");
                enemy.combatTemplate = template;
                enemy.dropEntries = ParseEnemyDropEntries(dropEntries, sourceName);
                ValidateApprovedEnemy(enemy, sourceName);
                enemies.Add(enemy);
            }

            if (enemies.Count != 1 || enemies[0].enemyId != ShijiahouEnemyId)
                throw new InvalidDataException($"{sourceName} must include exactly one approved '{ShijiahouEnemyId}' content projection.");

            return (enemies.ToArray(), templates.ToArray(), templateIds.ToArray());
        }

        private static CharacterData BuildEnemyTemplate(
            ContentCsvTable table,
            string[] row,
            Dictionary<string, string> language,
            string sourceName)
        {
            var template = ScriptableObject.CreateInstance<CharacterData>();
            var displayNameKey = RequiredLanguageKey(language, Required(table, row, "name"), sourceName, "enemy displayNameKey");
            template.charName = language[displayNameKey];
            template.realmMultiplier = ParseFloat(Required(table, row, "realmMultiplier"), sourceName, "realmMultiplier");
            template.rootBone = ParseInteger(Required(table, row, "rootBone"), sourceName, "rootBone");
            template.physique = ParseInteger(Required(table, row, "physique"), sourceName, "physique");
            template.spirit = ParseInteger(Required(table, row, "spirit"), sourceName, "spirit");
            template.mind = ParseInteger(Required(table, row, "mind"), sourceName, "mind");
            template.reaction = ParseInteger(Required(table, row, "reaction"), sourceName, "reaction");
            template.talent = ParseInteger(Required(table, row, "talent"), sourceName, "talent");
            template.blockRate = ParseFloat(Required(table, row, "blockRate"), sourceName, "blockRate");
            template.blockReduction = ParseFloat(Required(table, row, "blockReduction"), sourceName, "blockReduction");
            template.soulShieldRate = ParseFloat(Required(table, row, "soulShieldRate"), sourceName, "soulShieldRate");
            template.soulShieldReduction = ParseFloat(Required(table, row, "soulShieldReduction"), sourceName, "soulShieldReduction");
            template.dodgeRate = ParseFloat(Required(table, row, "dodgeRate"), sourceName, "dodgeRate");
            template.critRate = ParseFloat(Required(table, row, "critRate"), sourceName, "critRate");
            template.critDamage = ParseFloat(Required(table, row, "critDamage"), sourceName, "critDamage");
            template.hitRateBonus = ParseFloat(Required(table, row, "hitRateBonus"), sourceName, "hitRateBonus");
            template.equippedSpells = ParseOptionalStableIdList(Value(table, row, "equippedSpells"), sourceName, "equippedSpells");
            template.equippedSkills = Array.Empty<string>();
            // 敌人模板基础攻击外键由本表显式给出；石甲兽行引用生产 basic_unarmed 档案。
            template.unarmedBasicAttackProfileId = Value(table, row, "unarmedBasicAttackProfileId");
            return template;
        }

        private static EnemyDropEntry[] ParseEnemyDropEntries(string rawEntries, string sourceName)
        {
            if (string.IsNullOrWhiteSpace(rawEntries))
                throw new InvalidDataException($"{sourceName} formal enemy dropEntries must not be empty.");

            var entries = new List<EnemyDropEntry>();
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rawEntry in rawEntries.Split('|'))
            {
                var fields = rawEntry.Split('@');
                if (fields.Length != 3)
                    throw new InvalidDataException($"{sourceName} drop entry '{rawEntry}' must use itemId@chance@quantity.");
                var itemId = fields[0].Trim();
                RequireStableId(itemId, sourceName, "drop itemId");
                if (!itemIds.Add(itemId))
                    throw new InvalidDataException($"{sourceName} contains duplicate drop itemId '{itemId}'.");
                var chance = ParsePercent(fields[1].Trim(), sourceName, "dropChancePercent");
                var quantity = ParsePositiveInteger(fields[2].Trim(), sourceName, "drop quantity");
                entries.Add(new EnemyDropEntry
                {
                    itemId = itemId,
                    dropChancePercent = chance,
                    quantity = quantity,
                });
            }

            return entries.ToArray();
        }

        private static void ValidateApprovedEnemy(EnemyData enemy, string sourceName)
        {
            if (enemy.enemyId != ShijiahouEnemyId ||
                enemy.displayNameKey != ShijiahouEnemyId ||
                enemy.descriptionKey != "desc_enemy_shijiahou" ||
                enemy.contentScope != GuanzhongScope ||
                enemy.enemyTypeId != "type_yaoshou" ||
                enemy.aiProfileId != "ai_melee" ||
                enemy.realmId != "realm_lianqi" ||
                enemy.dropEntries.Length != 2 ||
                enemy.combatTemplate == null ||
                enemy.combatTemplate.unarmedBasicAttackProfileId !=
                CharacterCreationCatalog.BasicUnarmedAttackProfileId)
            {
                throw new InvalidDataException($"{sourceName} does not match the approved '{ShijiahouEnemyId}' projection.");
            }

            var drops = enemy.dropEntries.ToDictionary(entry => entry.itemId, StringComparer.Ordinal);
            if (!drops.TryGetValue("item_shijia_piece", out var piece) || piece.dropChancePercent != 100 || piece.quantity != 1 ||
                !drops.TryGetValue("item_lingshi_low", out var lingshi) || lingshi.dropChancePercent != 50 || lingshi.quantity != 1)
            {
                throw new InvalidDataException($"{sourceName} '{ShijiahouEnemyId}' drop parameters differ from the approved decision.");
            }
        }

        private static BountyData[] ParseBountyRows(
            string[] lines,
            string sourceName,
            Dictionary<string, string> language)
        {
            var table = ReadContentTable(lines, sourceName, ContentBountyColumns);
            var bounties = new List<BountyData>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in table.Rows)
            {
                var bounty = ScriptableObject.CreateInstance<BountyData>();
                bounty.bountyId = Required(table, row, "bountyId");
                RequireStableId(bounty.bountyId, sourceName, "bountyId");
                if (!ids.Add(bounty.bountyId))
                    throw new InvalidDataException($"{sourceName} contains duplicate bountyId '{bounty.bountyId}'.");
                bounty.titleKey = RequiredLanguageKey(language, Required(table, row, "titleKey"), sourceName, "titleKey");
                bounty.descriptionKey = RequiredLanguageKey(language, Required(table, row, "descriptionKey"), sourceName, "descriptionKey");
                bounty.contentScope = Required(table, row, "contentScope");
                bounty.issuerSettlementId = Required(table, row, "issuerSettlementId");
                bounty.objectiveType = Required(table, row, "objectiveType");
                bounty.targetEnemyId = Required(table, row, "targetEnemyId");
                bounty.requiredCount = ParsePositiveInteger(Required(table, row, "requiredCount"), sourceName, "requiredCount");
                bounty.allowedAdventureId = Required(table, row, "allowedAdventureId");
                bounty.rewardEntries = ParseBountyRewardEntries(Required(table, row, "rewardEntries"), sourceName);
                bounty.repeatPolicy = Required(table, row, "repeatPolicy");
                ValidateApprovedBounty(bounty, sourceName);
                bounties.Add(bounty);
            }

            if (bounties.Count != 1 || bounties[0].bountyId != ShijiahouBountyId)
                throw new InvalidDataException($"{sourceName} must contain only the approved '{ShijiahouBountyId}' production row.");

            return bounties.ToArray();
        }

        private static BountyRewardEntry[] ParseBountyRewardEntries(string rawEntries, string sourceName)
        {
            var entries = new List<BountyRewardEntry>();
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rawEntry in rawEntries.Split('|'))
            {
                var fields = rawEntry.Split('@');
                if (fields.Length != 2)
                    throw new InvalidDataException($"{sourceName} reward entry '{rawEntry}' must use itemId@quantity.");
                var itemId = fields[0].Trim();
                RequireStableId(itemId, sourceName, "reward itemId");
                if (!itemIds.Add(itemId))
                    throw new InvalidDataException($"{sourceName} contains duplicate reward itemId '{itemId}'.");
                entries.Add(new BountyRewardEntry
                {
                    itemId = itemId,
                    quantity = ParsePositiveInteger(fields[1].Trim(), sourceName, "reward quantity"),
                });
            }

            if (entries.Count == 0)
                throw new InvalidDataException($"{sourceName} rewardEntries must not be empty.");
            return entries.ToArray();
        }

        private static void ValidateApprovedBounty(BountyData bounty, string sourceName)
        {
            if (bounty.bountyId != ShijiahouBountyId ||
                bounty.titleKey != "bounty_guanzhong_shijiahou_title" ||
                bounty.descriptionKey != "bounty_guanzhong_shijiahou_description" ||
                bounty.contentScope != ContentScopeProduction ||
                bounty.issuerSettlementId != GuanzhongSettlementId ||
                bounty.objectiveType != "defeat_enemy" ||
                bounty.targetEnemyId != ShijiahouEnemyId ||
                bounty.requiredCount != 1 ||
                bounty.allowedAdventureId != "guanzhong_wild" ||
                bounty.repeatPolicy != "one_time" ||
                bounty.rewardEntries.Length != 1 ||
                bounty.rewardEntries[0].itemId != "item_lingshi_low" ||
                bounty.rewardEntries[0].quantity != 3)
            {
                throw new InvalidDataException($"{sourceName} does not match the approved '{ShijiahouBountyId}' projection.");
            }
        }

        private static void ValidateContentCatalogReferences(
            ContentCatalogImportPreview preview,
            string bountySource,
            string enemySource)
        {
            var settlementIds = new HashSet<string>(preview.settlements.Select(value => value.settlementId), StringComparer.Ordinal);
            var enemyIds = new HashSet<string>(preview.enemies.Select(value => value.enemyId), StringComparer.Ordinal);
            var itemsById = preview.items.ToDictionary(value => value.itemId, StringComparer.Ordinal);

            foreach (var enemy in preview.enemies)
            {
                if (enemy.combatTemplate == null || !enemyIds.Contains(enemy.enemyId))
                    throw new InvalidDataException($"{enemySource} formal enemy projection is missing its combat template.");
                foreach (var drop in enemy.dropEntries)
                {
                    if (!itemsById.TryGetValue(drop.itemId, out var item) || drop.quantity > item.maxStack)
                        throw new InvalidDataException($"{enemySource} drop '{drop.itemId}' has an unresolved item or invalid quantity.");
                }
            }

            foreach (var bounty in preview.bounties)
            {
                if (!settlementIds.Contains(bounty.issuerSettlementId) ||
                    !enemyIds.Contains(bounty.targetEnemyId) ||
                    bounty.allowedAdventureId != "guanzhong_wild")
                {
                    throw new InvalidDataException($"{bountySource} has an unresolved formal reference.");
                }
                foreach (var reward in bounty.rewardEntries)
                {
                    if (!itemsById.TryGetValue(reward.itemId, out var item) || reward.quantity > item.maxStack)
                        throw new InvalidDataException($"{bountySource} reward '{reward.itemId}' has an unresolved item or invalid quantity.");
                }
            }
        }

        private static ContentCsvTable ReadContentTable(string[] lines, string sourceName, string[] expectedColumns)
        {
            if (lines == null)
                throw new InvalidDataException($"{sourceName} has no rows.");
            var headerIndex = FindHeaderIndex(lines);
            if (headerIndex < 0)
                throw new InvalidDataException($"{sourceName} has no header row.");

            var headers = ParseCSV(lines[headerIndex]);
            RequireExactColumns(headers, sourceName, expectedColumns);
            var rows = new List<string[]>();
            for (var index = headerIndex + 1; index < lines.Length; index++)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;
                var columns = ParseCSV(line);
                if (columns.Length != headers.Length)
                {
                    throw new InvalidDataException(
                        $"{sourceName} row {index + 1} has {columns.Length} columns; expected exactly {headers.Length}.");
                }
                rows.Add(columns);
            }

            if (rows.Count == 0)
                throw new InvalidDataException($"{sourceName} has no production rows.");
            return new ContentCsvTable { SourceName = sourceName, Headers = headers, Rows = rows };
        }

        private static string Required(ContentCsvTable table, string[] row, string fieldName)
        {
            return GetRequiredColumnValue(table.Headers, row, fieldName, table.SourceName);
        }

        private static string Value(ContentCsvTable table, string[] row, string fieldName)
        {
            return GetColumnValueOrDefault(table.Headers, row, fieldName, string.Empty);
        }

        private static string RequiredLanguageKey(
            Dictionary<string, string> language,
            string key,
            string sourceName,
            string fieldName)
        {
            RequireStableId(key, sourceName, fieldName);
            if (!language.ContainsKey(key))
                throw new InvalidDataException($"{sourceName} {fieldName} '{key}' is not present in Language.csv.");
            return key;
        }

        private static string[] ParseStableIdList(string rawValue, string sourceName, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                throw new InvalidDataException($"{sourceName} {fieldName} must not be empty.");
            return ParseOptionalStableIdList(rawValue, sourceName, fieldName);
        }

        private static string[] ParseOptionalStableIdList(string rawValue, string sourceName, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return Array.Empty<string>();
            var values = rawValue.Split('|').Select(value => value.Trim()).ToArray();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                RequireStableId(value, sourceName, fieldName);
                if (!unique.Add(value))
                    throw new InvalidDataException($"{sourceName} {fieldName} contains duplicate ID '{value}'.");
            }
            return values;
        }

        private static int ParseInteger(string value, string sourceName, string fieldName)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                throw new InvalidDataException($"{sourceName} {fieldName} '{value}' is not an integer.");
            return result;
        }

        private static int ParsePositiveInteger(string value, string sourceName, string fieldName)
        {
            var result = ParseInteger(value, sourceName, fieldName);
            if (result <= 0)
                throw new InvalidDataException($"{sourceName} {fieldName} must be a positive integer.");
            return result;
        }

        private static int ParsePercent(string value, string sourceName, string fieldName)
        {
            var result = ParseInteger(value, sourceName, fieldName);
            if (result < 0 || result > 100)
                throw new InvalidDataException($"{sourceName} {fieldName} must be between 0 and 100.");
            return result;
        }

        private static float ParseFloat(string value, string sourceName, string fieldName)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ||
                float.IsNaN(result) || float.IsInfinity(result))
            {
                throw new InvalidDataException($"{sourceName} {fieldName} '{value}' is not a finite number.");
            }
            return result;
        }

        private static void RequireStableId(string value, string sourceName, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"{sourceName} {fieldName} must not be empty.");
            foreach (var character in value)
            {
                if (!(character >= 'a' && character <= 'z') &&
                    !(character >= '0' && character <= '9') &&
                    character != '_')
                {
                    throw new InvalidDataException($"{sourceName} {fieldName} '{value}' must be a lowercase ASCII stable ID.");
                }
            }
        }

        private static void ValidateContentAssetLocations(ContentCatalogImportPreview preview)
        {
            ValidateContentAssetLocation(preview.settlements[0],
                "Assets/Data/Settlements/Settlement_guanzhong_city.asset", value => value.settlementId);
            ValidateContentAssetLocation(preview.enemies[0],
                "Assets/Data/Enemies/Enemy_enemy_shijiahou.asset", value => value.enemyId);
            foreach (var item in preview.items)
            {
                ValidateContentAssetLocation(item, $"Assets/Data/Items/Item_{item.itemId}.asset", value => value.itemId);
            }
            ValidateContentAssetLocation(preview.bounties[0],
                "Assets/Data/Bounties/Bounty_bounty_guanzhong_shijiahou.asset", value => value.bountyId);
            ValidateCatalogAssetLocation();

            for (var index = 0; index < preview.enemyTemplateIds.Length; index++)
            {
                var characterPath = $"Assets/Data/Characters/Char_Enemy_{preview.enemyTemplateIds[index]}.asset";
                var characterAsset = AssetDatabase.LoadMainAssetAtPath(characterPath);
                if (characterAsset != null && !(characterAsset is CharacterData))
                    throw new InvalidDataException($"{characterPath} conflicts with a non-CharacterData asset.");
            }
        }

        private static void ValidateContentAssetLocation<T>(T source, string expectedPath, Func<T, string> getId)
            where T : ScriptableObject
        {
            var expectedId = getId(source);
            var atExpectedPath = AssetDatabase.LoadMainAssetAtPath(expectedPath);
            if (atExpectedPath != null && !(atExpectedPath is T))
                throw new InvalidDataException($"{expectedPath} conflicts with a different asset type.");

            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null)
                    continue;
                var assetId = getId(asset);
                if (assetId == expectedId && path != expectedPath)
                    throw new InvalidDataException($"{typeof(T).Name} ID '{expectedId}' has a conflicting asset path '{path}'.");
                if (path == expectedPath && !string.IsNullOrEmpty(assetId) && assetId != expectedId)
                    throw new InvalidDataException($"{expectedPath} has a conflicting serialized ID '{assetId}'.");
            }
        }

        private static void ValidateCatalogAssetLocation()
        {
            const string catalogPath = "Assets/Data/ContentCatalog/ContentCatalog.asset";
            var atExpectedPath = AssetDatabase.LoadMainAssetAtPath(catalogPath);
            if (atExpectedPath != null && !(atExpectedPath is ContentCatalogData))
                throw new InvalidDataException($"{catalogPath} conflicts with a different asset type.");
            foreach (var guid in AssetDatabase.FindAssets("t:ContentCatalogData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path != catalogPath)
                    throw new InvalidDataException($"ContentCatalogData must use the single approved asset path; found '{path}'.");
            }
        }

        private static void CommitContentCatalog(ContentCatalogImportPreview preview)
        {
            const string characterPath = "Assets/Data/Characters/Char_Enemy_enemy_shijiahou.asset";
            var existingCharacterGuid = AssetDatabase.AssetPathToGUID(characterPath);
            AssetDatabase.StartAssetEditing();
            try
            {
                var templates = new Dictionary<string, CharacterData>(StringComparer.Ordinal);
                for (var index = 0; index < preview.enemyTemplates.Length; index++)
                {
                    var enemyId = preview.enemyTemplateIds[index];
                    var templatePath = $"Assets/Data/Characters/Char_Enemy_{enemyId}.asset";
                    templates[enemyId] = UpsertCharacterTemplate(preview.enemyTemplates[index], templatePath);
                }

                var settlements = new[]
                {
                    UpsertContentAsset(preview.settlements[0],
                        "Assets/Data/Settlements/Settlement_guanzhong_city.asset", CopySettlement)
                };
                var items = preview.items
                    .Select(item => UpsertContentAsset(item, $"Assets/Data/Items/Item_{item.itemId}.asset", CopyItem))
                    .OrderBy(item => item.itemId, StringComparer.Ordinal)
                    .ToArray();

                preview.enemies[0].combatTemplate = templates[ShijiahouEnemyId];
                var enemies = new[]
                {
                    UpsertContentAsset(preview.enemies[0],
                        "Assets/Data/Enemies/Enemy_enemy_shijiahou.asset", CopyEnemy)
                };
                var bounties = new[]
                {
                    UpsertContentAsset(preview.bounties[0],
                        "Assets/Data/Bounties/Bounty_bounty_guanzhong_shijiahou.asset", CopyBounty)
                };
                var catalog = UpsertCatalogAsset("Assets/Data/ContentCatalog/ContentCatalog.asset");
                catalog.ReplaceEntries(settlements, enemies, items, bounties);
                // 内容目录只保存唯一静态目录的单一引用；缺失或非法时导入失败关闭。
                catalog.SetCharterRuleStaticCatalog(LoadCharterRuleStaticCatalog());
                EditorUtility.SetDirty(catalog);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!string.IsNullOrEmpty(existingCharacterGuid) &&
                AssetDatabase.AssetPathToGUID(characterPath) != existingCharacterGuid)
            {
                throw new InvalidDataException($"{characterPath} changed GUID during content import.");
            }
        }

        private static CharacterData UpsertCharacterTemplate(CharacterData source, string assetPath)
        {
            var target = AssetDatabase.LoadAssetAtPath<CharacterData>(assetPath);
            if (target == null)
            {
                EnsureAssetDirectory(assetPath);
                AssetDatabase.CreateAsset(source, assetPath);
                return source;
            }

            if (CharacterTemplateMatches(source, target))
                return target;

            target.charName = source.charName;
            target.realmMultiplier = source.realmMultiplier;
            target.rootBone = source.rootBone;
            target.physique = source.physique;
            target.spirit = source.spirit;
            target.mind = source.mind;
            target.reaction = source.reaction;
            target.talent = source.talent;
            target.blockRate = source.blockRate;
            target.blockReduction = source.blockReduction;
            target.soulShieldRate = source.soulShieldRate;
            target.soulShieldReduction = source.soulShieldReduction;
            target.dodgeRate = source.dodgeRate;
            target.critRate = source.critRate;
            target.critDamage = source.critDamage;
            target.hitRateBonus = source.hitRateBonus;
            target.equippedSpells = (string[])source.equippedSpells.Clone();
            target.equippedSkills = Array.Empty<string>();
            target.unarmedBasicAttackProfileId = source.unarmedBasicAttackProfileId;
            EditorUtility.SetDirty(target);
            return target;
        }

        private static bool CharacterTemplateMatches(CharacterData source, CharacterData target)
        {
            return target.charName == source.charName &&
                   target.realmMultiplier == source.realmMultiplier &&
                   target.rootBone == source.rootBone &&
                   target.physique == source.physique &&
                   target.spirit == source.spirit &&
                   target.mind == source.mind &&
                   target.reaction == source.reaction &&
                   target.talent == source.talent &&
                   target.blockRate == source.blockRate &&
                   target.blockReduction == source.blockReduction &&
                   target.soulShieldRate == source.soulShieldRate &&
                   target.soulShieldReduction == source.soulShieldReduction &&
                   target.dodgeRate == source.dodgeRate &&
                   target.critRate == source.critRate &&
                   target.critDamage == source.critDamage &&
                   target.hitRateBonus == source.hitRateBonus &&
                   target.equippedSpells.SequenceEqual(source.equippedSpells) &&
                   target.equippedSkills.SequenceEqual(source.equippedSkills) &&
                   target.unarmedBasicAttackProfileId == source.unarmedBasicAttackProfileId;
        }

        private static T UpsertContentAsset<T>(T source, string assetPath, Action<T, T> copy)
            where T : ScriptableObject
        {
            var target = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (target == null)
            {
                EnsureAssetDirectory(assetPath);
                AssetDatabase.CreateAsset(source, assetPath);
                return source;
            }

            copy(source, target);
            EditorUtility.SetDirty(target);
            return target;
        }

        private static ContentCatalogData UpsertCatalogAsset(string assetPath)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(assetPath);
            if (catalog != null)
                return catalog;
            catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            EnsureAssetDirectory(assetPath);
            AssetDatabase.CreateAsset(catalog, assetPath);
            return catalog;
        }

        private static void CopySettlement(SettlementData source, SettlementData target)
        {
            target.settlementId = source.settlementId;
            target.displayNameKey = source.displayNameKey;
            target.contentScope = source.contentScope;
            target.settlementType = source.settlementType;
            target.regionId = source.regionId;
            target.ownerFactionId = source.ownerFactionId;
            target.visualThemeId = source.visualThemeId;
            target.features = source.features.Select(feature => new SettlementFeatureData
            {
                featureId = feature.featureId,
                displayNameKey = feature.displayNameKey,
                availability = feature.availability,
                disabledReasonKey = feature.disabledReasonKey,
            }).ToArray();
            target.adventureEntranceIds = (string[])source.adventureEntranceIds.Clone();
        }

        private static void CopyEnemy(EnemyData source, EnemyData target)
        {
            target.enemyId = source.enemyId;
            target.displayNameKey = source.displayNameKey;
            target.descriptionKey = source.descriptionKey;
            target.contentScope = source.contentScope;
            target.enemyTypeId = source.enemyTypeId;
            target.aiProfileId = source.aiProfileId;
            target.realmId = source.realmId;
            target.combatTemplate = source.combatTemplate;
            target.dropEntries = source.dropEntries.Select(entry => new EnemyDropEntry
            {
                itemId = entry.itemId,
                dropChancePercent = entry.dropChancePercent,
                quantity = entry.quantity,
            }).ToArray();
        }

        private static void CopyItem(ItemData source, ItemData target)
        {
            target.itemId = source.itemId;
            target.displayNameKey = source.displayNameKey;
            target.descriptionKey = source.descriptionKey;
            target.contentScope = source.contentScope;
            target.itemCategory = source.itemCategory;
            target.maxStack = source.maxStack;
        }

        private static void CopyBounty(BountyData source, BountyData target)
        {
            target.bountyId = source.bountyId;
            target.titleKey = source.titleKey;
            target.descriptionKey = source.descriptionKey;
            target.contentScope = source.contentScope;
            target.issuerSettlementId = source.issuerSettlementId;
            target.objectiveType = source.objectiveType;
            target.targetEnemyId = source.targetEnemyId;
            target.requiredCount = source.requiredCount;
            target.allowedAdventureId = source.allowedAdventureId;
            target.rewardEntries = source.rewardEntries.Select(entry => new BountyRewardEntry
            {
                itemId = entry.itemId,
                quantity = entry.quantity,
            }).ToArray();
            target.repeatPolicy = source.repeatPolicy;
        }

        private static void EnsureAssetDirectory(string assetPath)
        {
            var segments = Path.GetDirectoryName(assetPath).Replace("\\", "/").Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
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
                    string assetPath = $"Assets/Data/NpcCultivationActionWeightProfiles/NpcCultivationActionWeightProfile_{SanitizeName(profile.profileId)}.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<NpcCultivationActionWeightProfileData>(assetPath);
                    bool isNew = asset == null;
                    if (isNew)
                    {
                        asset = ScriptableObject.CreateInstance<NpcCultivationActionWeightProfileData>();
                        EnsureDirectory(assetPath);
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

            int headerLineIndex = FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw new InvalidDataException($"{sourceName} has no header row.");

            string[] headers = FindHeader(lines);
            RequireExactColumns(headers, sourceName, NpcCultivationActionWeightColumns);
            int hashIndex = Array.IndexOf(headers, "sourceContentHash");
            var rows = new List<string[]>();
            for (int index = headerLineIndex + 1; index < lines.Length; index++)
            {
                string line = lines[index];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;
                string[] columns = ParseCSV(line);
                if (columns.Length != headers.Length)
                    throw new InvalidDataException($"{sourceName} row {index + 1} has {columns.Length} columns; expected {headers.Length}.");
                rows.Add(columns);
            }
            if (rows.Count == 0)
                throw new InvalidDataException($"{sourceName} has no data rows.");

            string contentHash = ComputeNpcCultivationSourceHash(headers, rows, hashIndex);
            var result = new List<NpcCultivationActionWeightProfileData>();
            foreach (var group in rows.GroupBy(row => GetRequiredColumnValue(headers, row, "profileId", sourceName), StringComparer.Ordinal))
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
            GetColumnValueOrDefault(headers, columns, name, defaultValue);

        private static string NpcRequired(string[] headers, string[] columns, string name, string sourceName) =>
            GetRequiredColumnValue(headers, columns, name, sourceName);

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

        [MenuItem("天章/导入环境档案配置")]
        public static void ImportEnvironmentProfiles()
        {
            const string path = "Assets/DataConfig/EnvironmentProfiles.csv";
            if (!File.Exists(path))
                throw new FileNotFoundException($"Environment profile CSV was not found: {path}", path);

            var profiles = ParseEnvironmentProfiles(File.ReadAllLines(path), path);
            foreach (var profile in profiles)
            {
                string assetPath = $"Assets/Data/EnvironmentProfiles/EnvironmentProfile_{SanitizeName(profile.profileId)}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(assetPath);
                bool isNew = asset == null;
                if (isNew)
                {
                    asset = ScriptableObject.CreateInstance<EnvironmentProfileAsset>();
                    EnsureDirectory(assetPath);
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

            int headerLineIndex = FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw new InvalidDataException($"{sourceName} has no header row.");

            var headers = FindHeader(lines);
            RequireExactColumns(headers, sourceName, EnvironmentProfileColumns);
            var profiles = new List<EnvironmentProfileDefinition>();
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
                        $"Assets/Data/CharterRuleDefinitions/CharterRuleDefinition_{SanitizeName(definition.ruleEntryId)}.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<CharterRuleDefinitionData>(assetPath);
                    bool isNew = asset == null;
                    if (isNew)
                    {
                        asset = ScriptableObject.CreateInstance<CharterRuleDefinitionData>();
                        EnsureDirectory(assetPath);
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

            int headerLineIndex = FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw CharterError("CHARTER_TABLE_INVALID", sourceName, "has no header row.");

            var headers = FindHeader(lines);
            RequireExactColumns(headers, sourceName, CharterRuleDefinitionColumns);
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

                    var columns = ParseCSV(line);
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
            string value = GetRequiredColumnValue(headers, columns, name, sourceName);
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

        /// <summary>
        /// Imports the single approved charter site production chain
        /// <c>CharterSites.csv -> CharterSiteData asset -> ContentCatalogData reference</c>. The whole
        /// table, its cross-table references and the shared conflict decision are validated in memory
        /// before any asset is written.
        /// </summary>
        [MenuItem("天章/导入册界站点契约")]
        public static void ImportCharterSites()
        {
            const string path = "Assets/DataConfig/CharterSites.csv";
            if (!File.Exists(path))
                throw new FileNotFoundException($"Charter site CSV was not found: {path}", path);

            var language = ParseContentLanguage(
                ReadRequiredContentFile("Assets/DataConfig/Language.csv"),
                "Language.csv");
            RequireLanguageText(language, CharterSiteDisplayNameKey, CharterSiteDisplayNameText, "Language.csv");

            // The site row resolves cross-contract references only through the single approved
            // static catalog; an invalid catalog fails closed before any site row can import.
            CharterRuleStaticCatalogData staticCatalog = LoadCharterRuleStaticCatalog();
            if (!staticCatalog.TryValidateDefinitions(out string catalogReason))
            {
                throw CharterError(
                    "CHARTER_REFERENCE_CATALOG_UNDECLARED",
                    path,
                    $"the approved static catalog is invalid: {catalogReason}");
            }

            var sites = ParseCharterSites(
                File.ReadAllLines(path),
                path,
                language,
                staticCatalog.ReferenceCatalog,
                staticCatalog.Definitions);
            if (sites[0].jindanGrant.definitionVersion != staticCatalog.DefinitionCatalogVersion)
            {
                throw CharterError(
                    "CHARTER_SITE_GRANT_INVALID",
                    path,
                    $"grant definitionVersion '{sites[0].jindanGrant.definitionVersion}' must equal the static catalog version '{staticCatalog.DefinitionCatalogVersion}'.");
            }

            try
            {
                var site = UpsertContentAsset(sites[0], CharterSiteAssetPath, CopyCharterSite);
                var catalog = UpsertCatalogAsset("Assets/Data/ContentCatalog/ContentCatalog.asset");
                catalog.SetCharterSites(new[] { site });
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                foreach (var site in sites)
                    UnityEngine.Object.DestroyImmediate(site);
            }
        }

        /// <summary>
        /// Parses and validates the complete charter site table before any asset can be written.
        /// Cross-table references (settlement, display key, relic, authorization, rule entry, world
        /// variable, node and the conflict grant directory) and the shared conflict decision must
        /// all resolve; a single violation fails the whole table closed.
        /// </summary>
        public static CharterSiteData[] ParseCharterSites(
            string[] lines,
            string sourceName,
            Dictionary<string, string> language,
            CharterRuleReferenceCatalog referenceCatalog,
            CharterRuleDefinitionData[] definitions)
        {
            if (lines == null)
                throw CharterError("CHARTER_SITE_TABLE_INVALID", sourceName, "has no rows.");

            int headerLineIndex = FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw CharterError("CHARTER_SITE_TABLE_INVALID", sourceName, "has no header row.");

            var headers = FindHeader(lines);
            RequireExactColumns(headers, sourceName, CharterSiteColumns);
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

            var sites = new List<CharterSiteData>();
            try
            {
                for (int index = headerLineIndex + 1; index < lines.Length; index++)
                {
                    string line = lines[index];
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;

                    var columns = ParseCSV(line);
                    if (columns.Length != headers.Length)
                    {
                        throw CharterError(
                            "CHARTER_SITE_TABLE_INVALID",
                            $"{sourceName} row {index + 1}",
                            $"has {columns.Length} columns; expected {headers.Length}.");
                    }

                    var site = ParseCharterSiteRow(
                        headers, columns, $"{sourceName} row {index + 1}", language, referenceCatalog, definitions);
                    sites.Add(site);
                }

                if (sites.Count != 1 || sites[0].siteId != CharterSiteId)
                {
                    throw CharterError(
                        "CHARTER_SITE_NOT_UNIQUE",
                        sourceName,
                        $"must contain only the approved '{CharterSiteId}' production row.");
                }

                return sites.ToArray();
            }
            catch
            {
                foreach (var site in sites)
                    UnityEngine.Object.DestroyImmediate(site);
                throw;
            }
        }

        private static CharterSiteData ParseCharterSiteRow(
            string[] headers,
            string[] columns,
            string sourceName,
            Dictionary<string, string> language,
            CharterRuleReferenceCatalog catalog,
            CharterRuleDefinitionData[] definitions)
        {
            var site = ScriptableObject.CreateInstance<CharterSiteData>();
            try
            {
                // 站点身份
                site.siteId = GetRequiredCharterColumnValue(headers, columns, "siteId", sourceName);
                RequireCharterReference(site.siteId, sourceName, "siteId", "CHARTER_SITE_TABLE_INVALID");
                site.displayNameKey = RequiredLanguageKey(
                    language,
                    GetRequiredCharterColumnValue(headers, columns, "displayNameKey", sourceName),
                    sourceName,
                    "displayNameKey");
                site.settlementId = GetRequiredCharterColumnValue(headers, columns, "settlementId", sourceName);
                RequireCharterReference(site.settlementId, sourceName, "settlementId", "CHARTER_SITE_UNKNOWN_SETTLEMENT");
                if (site.settlementId != GuanzhongSettlementId)
                {
                    throw CharterError(
                        "CHARTER_SITE_UNKNOWN_SETTLEMENT",
                        sourceName,
                        $"references unknown settlement '{site.settlementId}'.");
                }

                // 通行：固定能力、固定交互时间档案与显式可操作的门禁状态。
                site.passageCapabilityId = GetRequiredCharterColumnValue(headers, columns, "passageCapabilityId", sourceName);
                RequireCharterReference(site.passageCapabilityId, sourceName, "passageCapabilityId", "CHARTER_SITE_CAPABILITY_MISMATCH");
                if (site.passageCapabilityId != CharterPassageCapabilityId)
                {
                    throw CharterError(
                        "CHARTER_SITE_CAPABILITY_MISMATCH",
                        sourceName,
                        $"passageCapabilityId must be the approved '{CharterPassageCapabilityId}'.");
                }
                site.passageOperatorId = GetRequiredCharterColumnValue(headers, columns, "passageOperatorId", sourceName);
                RequireCharterReference(site.passageOperatorId, sourceName, "passageOperatorId", "CHARTER_SITE_TABLE_INVALID");
                site.passageTargetId = GetRequiredCharterColumnValue(headers, columns, "passageTargetId", sourceName);
                RequireCharterReference(site.passageTargetId, sourceName, "passageTargetId", "CHARTER_SITE_TABLE_INVALID");
                site.passageProtocolState = GetRequiredCharterColumnValue(headers, columns, "passageProtocolState", sourceName);
                site.passageStructureState = GetRequiredCharterColumnValue(headers, columns, "passageStructureState", sourceName);
                site.passagePowerState = GetRequiredCharterColumnValue(headers, columns, "passagePowerState", sourceName);
                if (site.passageProtocolState != CharterGateProtocolState ||
                    site.passageStructureState != CharterGateStructureState ||
                    site.passagePowerState != CharterGatePowerState)
                {
                    throw CharterError(
                        "CHARTER_SITE_GATE_NOT_OPERABLE",
                        sourceName,
                        "the declared gate must be protocol compatible, structurally intact and powered.");
                }
                site.interactionTimeProfileId = GetRequiredCharterColumnValue(headers, columns, "interactionTimeProfileId", sourceName);
                RequireCharterReference(site.interactionTimeProfileId, sourceName, "interactionTimeProfileId", "CHARTER_SITE_TIME_PROFILE_MISMATCH");
                if (site.interactionTimeProfileId != CharterInteractionTimeProfileId)
                {
                    throw CharterError(
                        "CHARTER_SITE_TIME_PROFILE_MISMATCH",
                        sourceName,
                        $"interactionTimeProfileId must be the approved '{CharterInteractionTimeProfileId}'.");
                }
                site.recognitionTiming = GetRequiredCharterColumnValue(headers, columns, "recognitionTiming", sourceName);
                site.operationTiming = GetRequiredCharterColumnValue(headers, columns, "operationTiming", sourceName);
                site.cancellationPolicy = GetRequiredCharterColumnValue(headers, columns, "cancellationPolicy", sourceName);
                if (site.recognitionTiming != CharterRecognitionTiming ||
                    site.operationTiming != CharterOperationTiming ||
                    site.cancellationPolicy != CharterCancellationPolicy)
                {
                    throw CharterError(
                        "CHARTER_SITE_TIMING_SEMANTICS_INVALID",
                        sourceName,
                        "the interaction time profile must declare instant recognition, sustained guided operation and no commit on cancel.");
                }

                // 管理：太玄界印与设施职责；通行资格不能自动成为管理资格。
                site.facilityId = GetRequiredCharterColumnValue(headers, columns, "facilityId", sourceName);
                RequireCharterReference(site.facilityId, sourceName, "facilityId", "CHARTER_SITE_TABLE_INVALID");
                site.sealRelicId = GetRequiredCharterColumnValue(headers, columns, "sealRelicId", sourceName);
                RequireCharterReference(site.sealRelicId, sourceName, "sealRelicId", "CHARTER_SITE_UNKNOWN_RELIC_REFERENCE");
                if (!catalog.ContainsRelic(site.sealRelicId))
                {
                    throw CharterError(
                        "CHARTER_SITE_UNKNOWN_RELIC_REFERENCE",
                        sourceName,
                        $"references unknown relic '{site.sealRelicId}'.");
                }
                site.sealManagerId = GetRequiredCharterColumnValue(headers, columns, "sealManagerId", sourceName);
                RequireCharterReference(site.sealManagerId, sourceName, "sealManagerId", "CHARTER_SITE_MANAGER_INVALID");
                site.sealBeneficiaryId = GetRequiredCharterColumnValue(headers, columns, "sealBeneficiaryId", sourceName);
                RequireCharterReference(site.sealBeneficiaryId, sourceName, "sealBeneficiaryId", "CHARTER_SITE_BENEFICIARY_MISSING");
                if (string.Equals(site.sealManagerId, site.passageOperatorId, StringComparison.Ordinal))
                {
                    throw CharterError(
                        "CHARTER_SITE_MANAGER_INVALID",
                        sourceName,
                        "passage qualification cannot grant management qualification.");
                }
                site.sealAuthorizationVersionId = GetRequiredCharterColumnValue(headers, columns, "sealAuthorizationVersionId", sourceName);
                RequireCharterReference(site.sealAuthorizationVersionId, sourceName, "sealAuthorizationVersionId", "CHARTER_SITE_UNKNOWN_AUTHORIZATION_REFERENCE");
                if (!catalog.ContainsOrganizationAuthorizationVersion(site.sealAuthorizationVersionId))
                {
                    throw CharterError(
                        "CHARTER_SITE_UNKNOWN_AUTHORIZATION_REFERENCE",
                        sourceName,
                        $"references unknown organization authorization version '{site.sealAuthorizationVersionId}'.");
                }

                // 册界：条目必须由静态目录解析，占用 ID 为本站点自有身份。
                site.ruleEntryId = GetRequiredCharterColumnValue(headers, columns, "ruleEntryId", sourceName);
                RequireCharterReference(site.ruleEntryId, sourceName, "ruleEntryId", "CHARTER_SITE_UNKNOWN_RULE_ENTRY_REFERENCE");
                CharterRuleDefinitionData definition = definitions == null
                    ? null
                    : definitions.FirstOrDefault(value => value != null &&
                        string.Equals(value.ruleEntryId, site.ruleEntryId, StringComparison.Ordinal));
                if (definition == null || !catalog.ContainsRuleEntry(site.ruleEntryId))
                {
                    throw CharterError(
                        "CHARTER_SITE_UNKNOWN_RULE_ENTRY_REFERENCE",
                        sourceName,
                        $"references unknown rule entry '{site.ruleEntryId}'.");
                }
                site.ruleEntryOccupancyId = GetRequiredCharterColumnValue(headers, columns, "ruleEntryOccupancyId", sourceName);
                RequireCharterReference(site.ruleEntryOccupancyId, sourceName, "ruleEntryOccupancyId", "CHARTER_SITE_TABLE_INVALID");
                site.nodeOccupancyId = GetRequiredCharterColumnValue(headers, columns, "nodeOccupancyId", sourceName);
                RequireCharterReference(site.nodeOccupancyId, sourceName, "nodeOccupancyId", "CHARTER_SITE_TABLE_INVALID");

                // 金丹样例：版本化 grant、左右候选与册界侧唯一绑定。
                site.jindanConflictEventId = GetRequiredCharterColumnValue(headers, columns, "jindanConflictEventId", sourceName);
                RequireCharterReference(site.jindanConflictEventId, sourceName, "jindanConflictEventId", "CHARTER_SITE_TABLE_INVALID");
                site.jindanChallengeEventId = GetRequiredCharterColumnValue(headers, columns, "jindanChallengeEventId", sourceName);
                RequireCharterReference(site.jindanChallengeEventId, sourceName, "jindanChallengeEventId", "CHARTER_SITE_TABLE_INVALID");
                site.jindanGrant = ParseCharterSiteGrant(headers, columns, sourceName, site, definition, catalog);
                site.leftCandidate = ParseCharterSiteCandidate(headers, columns, sourceName, "left", site.jindanGrant);
                site.rightCandidate = ParseCharterSiteCandidate(headers, columns, sourceName, "right", site.jindanGrant);
                site.charterCandidateId = GetRequiredCharterColumnValue(headers, columns, "charterCandidateId", sourceName);
                RequireCharterReference(site.charterCandidateId, sourceName, "charterCandidateId", "CHARTER_SITE_CHARTER_SIDE_UNDECLARED");
                if (string.Equals(site.leftCandidate.candidateId, site.rightCandidate.candidateId, StringComparison.Ordinal) ||
                    (!string.Equals(site.charterCandidateId, site.leftCandidate.candidateId, StringComparison.Ordinal) &&
                     !string.Equals(site.charterCandidateId, site.rightCandidate.candidateId, StringComparison.Ordinal)))
                {
                    throw CharterError(
                        "CHARTER_SITE_CHARTER_SIDE_UNDECLARED",
                        sourceName,
                        "the charter side must uniquely bind one distinct candidate id.");
                }

                // 元婴样例：只携带受锚身份，不夹带金丹候选、grant 或可覆盖结果。
                site.yuanyingConflictEventId = GetRequiredCharterColumnValue(headers, columns, "yuanyingConflictEventId", sourceName);
                RequireCharterReference(site.yuanyingConflictEventId, sourceName, "yuanyingConflictEventId", "CHARTER_SITE_YUANYING_INVALID");
                site.yuanyingTargetVariableId = GetRequiredCharterColumnValue(headers, columns, "yuanyingTargetVariableId", sourceName);
                RequireCharterReference(site.yuanyingTargetVariableId, sourceName, "yuanyingTargetVariableId", "CHARTER_SITE_YUANYING_INVALID");
                if (!catalog.ContainsWorldVariable(site.yuanyingTargetVariableId))
                {
                    throw CharterError(
                        "CHARTER_SITE_UNKNOWN_WORLD_VARIABLE_REFERENCE",
                        sourceName,
                        $"references unknown world variable '{site.yuanyingTargetVariableId}'.");
                }
                site.yuanyingTargetId = GetRequiredCharterColumnValue(headers, columns, "yuanyingTargetId", sourceName);
                RequireCharterReference(site.yuanyingTargetId, sourceName, "yuanyingTargetId", "CHARTER_SITE_YUANYING_INVALID");
                if (!catalog.ContainsNode(site.yuanyingTargetId))
                {
                    throw CharterError(
                        "CHARTER_SITE_UNKNOWN_NODE_REFERENCE",
                        sourceName,
                        $"references unknown node '{site.yuanyingTargetId}'.");
                }
                site.yuanyingScopeId = GetRequiredCharterColumnValue(headers, columns, "yuanyingScopeId", sourceName);
                RequireCharterReference(site.yuanyingScopeId, sourceName, "yuanyingScopeId", "CHARTER_SITE_YUANYING_INVALID");
                site.yuanyingRealityAnchorId = GetRequiredCharterColumnValue(headers, columns, "yuanyingRealityAnchorId", sourceName);
                RequireCharterReference(site.yuanyingRealityAnchorId, sourceName, "yuanyingRealityAnchorId", "CHARTER_SITE_YUANYING_INVALID");

                // 导入验证用同一 shared 决定消费完整 grant、请求与左右候选：册界侧必须稳定未获胜。
                RequireStableCharterSideNotWon(site, sourceName);

                return site;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(site);
                throw;
            }
        }

        private static CharterSiteCrossTierChallengeGrantData ParseCharterSiteGrant(
            string[] headers,
            string[] columns,
            string sourceName,
            CharterSiteData site,
            CharterRuleDefinitionData definition,
            CharterRuleReferenceCatalog catalog)
        {
            var grant = new CharterSiteCrossTierChallengeGrantData
            {
                grantId = GetRequiredCharterColumnValue(headers, columns, "grantId", sourceName),
                definitionVersion = ParseCharterSiteInteger(headers, columns, "grantDefinitionVersion", sourceName),
                targetVariableId = GetRequiredCharterColumnValue(headers, columns, "grantTargetVariableId", sourceName),
                challengerId = GetRequiredCharterColumnValue(headers, columns, "grantChallengerId", sourceName),
                qualificationSource = GetRequiredCharterColumnValue(headers, columns, "grantQualificationSource", sourceName),
                allowedOperationId = GetRequiredCharterColumnValue(headers, columns, "grantAllowedOperationId", sourceName),
                targetId = GetRequiredCharterColumnValue(headers, columns, "grantTargetId", sourceName),
                scopeId = GetRequiredCharterColumnValue(headers, columns, "grantScopeId", sourceName),
                beneficiaryId = GetRequiredCharterColumnValue(headers, columns, "grantBeneficiaryId", sourceName),
                realityAnchorId = GetRequiredCharterColumnValue(headers, columns, "grantRealityAnchorId", sourceName),
                resourceLedgerRef = GetRequiredCharterColumnValue(headers, columns, "grantResourceLedgerRef", sourceName),
                capacityLedgerRef = GetRequiredCharterColumnValue(headers, columns, "grantCapacityLedgerRef", sourceName),
                challengeRuleTier = ParseCharterSiteInteger(headers, columns, "grantChallengeRuleTier", sourceName),
                effectiveAtTick = ParseCharterSiteInteger(headers, columns, "grantEffectiveAtTick", sourceName),
                expiresAtTick = ParseCharterSiteInteger(headers, columns, "grantExpiresAtTick", sourceName),
                isRevoked = ParseCharterSiteBool(headers, columns, "grantIsRevoked", sourceName),
                revocationReason = ParseCharterSiteOptionalReference(headers, columns, "grantRevocationReason", sourceName),
                displaySource = GetRequiredCharterColumnValue(headers, columns, "grantDisplaySource", sourceName),
            };

            foreach (string value in new[]
            {
                grant.grantId, grant.targetVariableId, grant.challengerId, grant.qualificationSource,
                grant.allowedOperationId, grant.targetId, grant.scopeId, grant.beneficiaryId,
                grant.realityAnchorId, grant.resourceLedgerRef, grant.capacityLedgerRef, grant.displaySource,
            })
            {
                RequireCharterReference(value, sourceName, "grant field", "CHARTER_SITE_GRANT_INVALID");
            }

            if (grant.definitionVersion <= 0 || grant.challengeRuleTier <= 0 ||
                grant.effectiveAtTick < 0 || grant.expiresAtTick < grant.effectiveAtTick)
            {
                throw CharterError(
                    "CHARTER_SITE_GRANT_INVALID",
                    sourceName,
                    "grant version, tier and effect/expiry ticks must form a valid explicit window.");
            }
            if (grant.isRevoked && string.IsNullOrEmpty(grant.revocationReason))
            {
                throw CharterError(
                    "CHARTER_SITE_GRANT_INVALID",
                    sourceName,
                    "a revoked grant must declare its revocation reason.");
            }
            if (!grant.isRevoked && !string.IsNullOrEmpty(grant.revocationReason))
            {
                throw CharterError(
                    "CHARTER_SITE_GRANT_INVALID",
                    sourceName,
                    "an active grant must not declare a revocation reason.");
            }
            if (!string.Equals(grant.beneficiaryId, site.sealBeneficiaryId, StringComparison.Ordinal))
            {
                throw CharterError(
                    "CHARTER_SITE_GRANT_INVALID",
                    sourceName,
                    "grant beneficiary must equal the site seal beneficiary.");
            }

            // grantId 必须已经列在水府地纪冲突档案的跨阶资格目录中，不另建授权来源。
            CharterConflictReference conflict = catalog.FindConflict(definition.conflictProfileId);
            if (conflict == null ||
                !conflict.crossTierChallengeGrantIds.Contains(grant.grantId, StringComparer.Ordinal))
            {
                throw CharterError(
                    "CHARTER_SITE_UNKNOWN_GRANT_REFERENCE",
                    sourceName,
                    $"grantId '{grant.grantId}' is not listed in the '{definition.conflictProfileId}' conflict directory.");
            }
            if (!catalog.ContainsWorldVariable(grant.targetVariableId))
            {
                throw CharterError(
                    "CHARTER_SITE_UNKNOWN_WORLD_VARIABLE_REFERENCE",
                    sourceName,
                    $"references unknown world variable '{grant.targetVariableId}'.");
            }
            if (!catalog.ContainsNode(grant.targetId))
            {
                throw CharterError(
                    "CHARTER_SITE_UNKNOWN_NODE_REFERENCE",
                    sourceName,
                    $"references unknown node '{grant.targetId}'.");
            }
            // 来源必须是共享枚举的显式成员名，不允许数字或未知字面量。
            if (grant.qualificationSource != nameof(CrossTierChallengeSourceKind.JindanProtection) &&
                grant.qualificationSource != nameof(CrossTierChallengeSourceKind.YuanyingOrthodoxy) &&
                grant.qualificationSource != nameof(CrossTierChallengeSourceKind.DedicatedGreatFormation) &&
                grant.qualificationSource != nameof(CrossTierChallengeSourceKind.NarrativeRelic))
            {
                throw CharterError(
                    "CHARTER_SITE_GRANT_INVALID",
                    sourceName,
                    $"has unknown qualificationSource '{grant.qualificationSource}'.");
            }

            return grant;
        }

        private static CharterSiteRuleConflictCandidateData ParseCharterSiteCandidate(
            string[] headers,
            string[] columns,
            string sourceName,
            string side,
            CharterSiteCrossTierChallengeGrantData grant)
        {
            string prefix = side + "Candidate";
            var candidate = new CharterSiteRuleConflictCandidateData
            {
                candidateId = GetRequiredCharterColumnValue(headers, columns, prefix + "Id", sourceName),
                targetVariableId = GetRequiredCharterColumnValue(headers, columns, prefix + "TargetVariableId", sourceName),
                targetId = GetRequiredCharterColumnValue(headers, columns, prefix + "TargetId", sourceName),
                hasVariableAuthority = ParseCharterSiteBool(headers, columns, prefix + "HasVariableAuthority", sourceName),
                hasLegalTarget = ParseCharterSiteBool(headers, columns, prefix + "HasLegalTarget", sourceName),
                positionRank = ParseCharterSiteInteger(headers, columns, prefix + "PositionRank", sourceName),
                realityAnchorRank = ParseCharterSiteInteger(headers, columns, prefix + "RealityAnchorRank", sourceName),
                alreadyPaidCost = ParseCharterSiteInteger(headers, columns, prefix + "AlreadyPaidCost", sourceName),
                hasActiveContinuousCarrier = ParseCharterSiteBool(headers, columns, prefix + "HasActiveContinuousCarrier", sourceName),
                conflictReserve = ParseCharterSiteInteger(headers, columns, prefix + "ConflictReserve", sourceName),
                pulseCost = ParseCharterSiteInteger(headers, columns, prefix + "PulseCost", sourceName),
                settlementCooldown = ParseCharterSiteInteger(headers, columns, prefix + "SettlementCooldown", sourceName),
            };

            RequireCharterReference(candidate.candidateId, sourceName, side + " candidate", "CHARTER_SITE_CANDIDATE_INVALID");
            if (!string.Equals(candidate.targetVariableId, grant.targetVariableId, StringComparison.Ordinal) ||
                !string.Equals(candidate.targetId, grant.targetId, StringComparison.Ordinal))
            {
                throw CharterError(
                    "CHARTER_SITE_CANDIDATE_MISMATCH",
                    sourceName,
                    $"{side} candidate must match the grant variable and target.");
            }
            if (candidate.positionRank < 0 || candidate.realityAnchorRank < 0 || candidate.alreadyPaidCost < 0 ||
                candidate.conflictReserve < 0 || candidate.pulseCost <= 0 || candidate.settlementCooldown < 0)
            {
                throw CharterError(
                    "CHARTER_SITE_CANDIDATE_INVALID",
                    sourceName,
                    $"{side} candidate has an invalid numeric field.");
            }

            return candidate;
        }

        /// <summary>
        /// Consumes the same shared conflict decision the player runtime uses: a complete versioned
        /// grant, a versioned challenge request and both candidates enter one
        /// <see cref="RuleConflictInstance.Decide"/>. The row is rejected when the deterministic
        /// winner is the charter side, the decision is neutral/rejected, or the grant does not
        /// authorize the challenge — never forced in UI or runtime.
        /// </summary>
        private static void RequireStableCharterSideNotWon(CharterSiteData site, string sourceName)
        {
            var grant = new CrossTierChallengeGrant(
                site.jindanGrant.grantId,
                site.jindanGrant.definitionVersion,
                site.jindanGrant.targetVariableId,
                site.jindanGrant.challengerId,
                ParseCharterSiteSourceKind(site.jindanGrant.qualificationSource, sourceName),
                site.jindanGrant.allowedOperationId,
                site.jindanGrant.targetId,
                site.jindanGrant.scopeId,
                site.jindanGrant.beneficiaryId,
                site.jindanGrant.realityAnchorId,
                site.jindanGrant.resourceLedgerRef,
                site.jindanGrant.capacityLedgerRef,
                site.jindanGrant.challengeRuleTier,
                site.jindanGrant.effectiveAtTick,
                site.jindanGrant.expiresAtTick,
                site.jindanGrant.isRevoked,
                site.jindanGrant.revocationReason,
                site.jindanGrant.displaySource);
            var archive = new CrossTierChallengeArchive(new[] { grant });
            var request = new CrossTierChallengeRequest(
                site.jindanChallengeEventId,
                grant.GrantId,
                grant.DefinitionVersion,
                grant.TargetVariableId,
                grant.ChallengerId,
                grant.EffectiveAtTick);
            var instance = new RuleConflictInstance(
                RuleConflictInstance.ContractVersionV1,
                site.jindanConflictEventId,
                RuleConflictKind.JindanSameVariable,
                site.ruleEntryId,
                grant.TargetVariableId,
                grant.AllowedOperationId,
                grant.TargetId,
                grant.ScopeId,
                grant.BeneficiaryId,
                grant.RealityAnchorId,
                grant.ResourceLedgerRef,
                grant.CapacityLedgerRef,
                grant.EffectiveAtTick,
                BuildCharterSiteConflictCandidate(site.leftCandidate),
                BuildCharterSiteConflictCandidate(site.rightCandidate),
                request);

            RuleConflictDecision decision = instance.Decide(archive);
            if (decision.Outcome != RuleConflictOutcome.LeftWins && decision.Outcome != RuleConflictOutcome.RightWins)
            {
                throw CharterError(
                    "CHARTER_SITE_CONFLICT_NOT_STABLE",
                    sourceName,
                    $"shared decision returned {decision.Outcome}; the charter side must deterministically not win.");
            }
            if (decision.CrossTierAuthorization == null || !decision.CrossTierAuthorization.IsEligible)
            {
                throw CharterError(
                    "CHARTER_SITE_CONFLICT_NOT_STABLE",
                    sourceName,
                    $"the versioned grant did not authorize the challenge: {decision.Reason}");
            }
            if (string.Equals(decision.WinnerCandidateId, site.charterCandidateId, StringComparison.Ordinal))
            {
                throw CharterError(
                    "CHARTER_SITE_CONFLICT_NOT_STABLE",
                    sourceName,
                    $"shared decision winner '{decision.WinnerCandidateId}' is the charter side.");
            }
        }

        private static RuleConflictCandidate BuildCharterSiteConflictCandidate(CharterSiteRuleConflictCandidateData data)
        {
            return new RuleConflictCandidate(
                data.candidateId,
                data.targetVariableId,
                data.targetId,
                data.hasVariableAuthority,
                data.hasLegalTarget,
                data.positionRank,
                data.realityAnchorRank,
                data.alreadyPaidCost,
                data.hasActiveContinuousCarrier,
                data.conflictReserve,
                data.pulseCost,
                data.settlementCooldown);
        }

        private static CrossTierChallengeSourceKind ParseCharterSiteSourceKind(string value, string sourceName)
        {
            if (value == nameof(CrossTierChallengeSourceKind.JindanProtection))
                return CrossTierChallengeSourceKind.JindanProtection;
            if (value == nameof(CrossTierChallengeSourceKind.YuanyingOrthodoxy))
                return CrossTierChallengeSourceKind.YuanyingOrthodoxy;
            if (value == nameof(CrossTierChallengeSourceKind.DedicatedGreatFormation))
                return CrossTierChallengeSourceKind.DedicatedGreatFormation;
            if (value == nameof(CrossTierChallengeSourceKind.NarrativeRelic))
                return CrossTierChallengeSourceKind.NarrativeRelic;
            throw CharterError("CHARTER_SITE_GRANT_INVALID", sourceName, $"has unknown qualificationSource '{value}'.");
        }

        private static int ParseCharterSiteInteger(string[] headers, string[] columns, string name, string sourceName)
        {
            string raw = GetRequiredCharterColumnValue(headers, columns, name, sourceName);
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw CharterError("CHARTER_SITE_TABLE_INVALID", sourceName, $"has invalid integer '{raw}' in '{name}'.");
            return value;
        }

        private static bool ParseCharterSiteBool(string[] headers, string[] columns, string name, string sourceName)
        {
            string raw = GetRequiredCharterColumnValue(headers, columns, name, sourceName);
            if (raw == "true")
                return true;
            if (raw == "false")
                return false;
            throw CharterError("CHARTER_SITE_TABLE_INVALID", sourceName, $"has invalid boolean '{raw}' in '{name}'.");
        }

        private static string ParseCharterSiteOptionalReference(string[] headers, string[] columns, string name, string sourceName)
        {
            string raw = GetRequiredCharterColumnValue(headers, columns, name, sourceName);
            if (string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            RequireCharterReference(raw, sourceName, name, "CHARTER_SITE_GRANT_INVALID");
            return raw;
        }

        private static void CopyCharterSite(CharterSiteData source, CharterSiteData target)
        {
            target.siteId = source.siteId;
            target.displayNameKey = source.displayNameKey;
            target.settlementId = source.settlementId;
            target.passageCapabilityId = source.passageCapabilityId;
            target.passageOperatorId = source.passageOperatorId;
            target.passageTargetId = source.passageTargetId;
            target.passageProtocolState = source.passageProtocolState;
            target.passageStructureState = source.passageStructureState;
            target.passagePowerState = source.passagePowerState;
            target.interactionTimeProfileId = source.interactionTimeProfileId;
            target.recognitionTiming = source.recognitionTiming;
            target.operationTiming = source.operationTiming;
            target.cancellationPolicy = source.cancellationPolicy;
            target.facilityId = source.facilityId;
            target.sealRelicId = source.sealRelicId;
            target.sealManagerId = source.sealManagerId;
            target.sealBeneficiaryId = source.sealBeneficiaryId;
            target.sealAuthorizationVersionId = source.sealAuthorizationVersionId;
            target.ruleEntryId = source.ruleEntryId;
            target.ruleEntryOccupancyId = source.ruleEntryOccupancyId;
            target.nodeOccupancyId = source.nodeOccupancyId;
            target.jindanConflictEventId = source.jindanConflictEventId;
            target.jindanChallengeEventId = source.jindanChallengeEventId;
            target.jindanGrant = CopyCharterSiteGrant(source.jindanGrant);
            target.leftCandidate = CopyCharterSiteCandidate(source.leftCandidate);
            target.rightCandidate = CopyCharterSiteCandidate(source.rightCandidate);
            target.charterCandidateId = source.charterCandidateId;
            target.yuanyingConflictEventId = source.yuanyingConflictEventId;
            target.yuanyingTargetVariableId = source.yuanyingTargetVariableId;
            target.yuanyingTargetId = source.yuanyingTargetId;
            target.yuanyingScopeId = source.yuanyingScopeId;
            target.yuanyingRealityAnchorId = source.yuanyingRealityAnchorId;
        }

        private static CharterSiteCrossTierChallengeGrantData CopyCharterSiteGrant(CharterSiteCrossTierChallengeGrantData source)
        {
            return new CharterSiteCrossTierChallengeGrantData
            {
                grantId = source.grantId,
                definitionVersion = source.definitionVersion,
                targetVariableId = source.targetVariableId,
                challengerId = source.challengerId,
                qualificationSource = source.qualificationSource,
                allowedOperationId = source.allowedOperationId,
                targetId = source.targetId,
                scopeId = source.scopeId,
                beneficiaryId = source.beneficiaryId,
                realityAnchorId = source.realityAnchorId,
                resourceLedgerRef = source.resourceLedgerRef,
                capacityLedgerRef = source.capacityLedgerRef,
                challengeRuleTier = source.challengeRuleTier,
                effectiveAtTick = source.effectiveAtTick,
                expiresAtTick = source.expiresAtTick,
                isRevoked = source.isRevoked,
                revocationReason = source.revocationReason,
                displaySource = source.displaySource,
            };
        }

        private static CharterSiteRuleConflictCandidateData CopyCharterSiteCandidate(CharterSiteRuleConflictCandidateData source)
        {
            return new CharterSiteRuleConflictCandidateData
            {
                candidateId = source.candidateId,
                targetVariableId = source.targetVariableId,
                targetId = source.targetId,
                hasVariableAuthority = source.hasVariableAuthority,
                hasLegalTarget = source.hasLegalTarget,
                positionRank = source.positionRank,
                realityAnchorRank = source.realityAnchorRank,
                alreadyPaidCost = source.alreadyPaidCost,
                hasActiveContinuousCarrier = source.hasActiveContinuousCarrier,
                conflictReserve = source.conflictReserve,
                pulseCost = source.pulseCost,
                settlementCooldown = source.settlementCooldown,
            };
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
                        $"Assets/Data/JindanStaticStates/JindanStaticState_{SanitizeName(state.characterId)}.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<JindanStaticStateData>(assetPath);
                    bool isNew = asset == null;
                    if (isNew)
                    {
                        asset = ScriptableObject.CreateInstance<JindanStaticStateData>();
                        EnsureDirectory(assetPath);
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

            int headerLineIndex = FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw JindanError("JD_TABLE_INVALID", sourceName, "has no header row.");

            var headers = FindHeader(lines);
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

                    var columns = ParseCSV(line);
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
            RequireExactColumns(headers, sourceName, JindanStaticColumns);
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
            int index = FindColumnIndex(headers, name);
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
                        $"Assets/Data/FoundationPurpleMansionStates/FoundationPurpleMansionState_{SanitizeName(state.characterId)}.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<FoundationPurpleMansionStateData>(assetPath);
                    bool isNew = asset == null;
                    if (isNew)
                    {
                        asset = ScriptableObject.CreateInstance<FoundationPurpleMansionStateData>();
                        EnsureDirectory(assetPath);
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

            int headerLineIndex = FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw FoundationError("FPM_TABLE_INVALID", sourceName, "has no header row.");

            var headers = FindHeader(lines);
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

                    var columns = ParseCSV(line);
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
            RequireExactColumns(headers, sourceName, FoundationPurpleMansionColumns);
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
            int index = FindColumnIndex(headers, name);
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

        [MenuItem("天章/导入角色创建点购配置")]
        public static void ImportCharacterCreationPointBuy()
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
                Debug.LogError("[ContentImportCoordinator] CharacterCreationPointBuy.csv missing default config rows.");
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
        public static void ImportGongFa()
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
                        EnsureDirectory(assetPath);
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

            int headerLineIndex = FindHeaderIndex(lines);
            if (headerLineIndex < 0)
            {
                reason = "attack_profile_header_missing";
                return false;
            }

            var headers = ParseCSV(lines[headerLineIndex]);
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

                var columns = ParseCSV(line);
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
        public static void ImportCharacterDefinitions()
        {
            ImportCharacters();
        }

        private static void ImportCharacters()
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
        public static void ImportEnemies()
        {
            ImportContentCatalog();
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
