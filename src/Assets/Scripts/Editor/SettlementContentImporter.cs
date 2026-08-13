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
using TianZhang.Tactical;
using TianZhang.World;

namespace TianZhang.Editor
{
    /// <summary>Owns the complete settlement content import pipeline.</summary>
    public static class SettlementContentImporter
    {
        [MenuItem("天章/内容/导入据点定义")]
        public static void Import()
        {
            ImportContentCatalog();
            ImportCharterSites();
            ImportCharacterCreationPointBuy();
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
                Debug.Log("[SettlementContentImporter] 正式内容目录导入完成");
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

                var columns = CsvTableReader.ParseRow(line);
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
            var headerIndex = CsvTableReader.FindHeaderIndex(lines);
            if (headerIndex < 0)
                throw new InvalidDataException($"{sourceName} has no header row.");

            var headers = CsvTableReader.ParseRow(lines[headerIndex]);
            CsvTableReader.RequireExactColumns(headers, sourceName, expectedColumns);
            var rows = new List<string[]>();
            for (var index = headerIndex + 1; index < lines.Length; index++)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;
                var columns = CsvTableReader.ParseRow(line);
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
            return CsvTableReader.GetRequiredValue(table.Headers, row, fieldName, table.SourceName);
        }

        private static string Value(ContentCsvTable table, string[] row, string fieldName)
        {
            return CsvTableReader.GetValueOrDefault(table.Headers, row, fieldName, string.Empty);
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

            int headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
            if (headerLineIndex < 0)
                throw CharterError("CHARTER_SITE_TABLE_INVALID", sourceName, "has no header row.");

            var headers = CsvTableReader.FindHeader(lines);
            CsvTableReader.RequireExactColumns(headers, sourceName, CharterSiteColumns);
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

                    var columns = CsvTableReader.ParseRow(line);
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

        [MenuItem("天章/导入角色创建点购配置")]
        public static void ImportCharacterCreationPointBuy()
        {
            string path = "Assets/DataConfig/CharacterCreationPointBuy.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }

            var lines = File.ReadAllLines(path);
            var headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
            var headers = CsvTableReader.FindHeader(lines);
            CsvTableReader.RequireColumns(headers, path,
                "configId", "purchasePointLimit", "minValue", "baseValue", "maxValue",
                "fromValue", "toValue", "costPerLevel");

            var rows = lines
                .Skip(headerLineIndex + 1)
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                .Select(CsvTableReader.ParseRow)
                .Where(cols => cols.Length >= headers.Length
                    && CsvTableReader.GetRequiredValue(headers, cols, "configId", path) == "default")
                .ToArray();

            if (rows.Length == 0)
            {
                Debug.LogError("[SettlementContentImporter] CharacterCreationPointBuy.csv missing default config rows.");
                return;
            }

            string assetPath = "Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset";
            AssetCommitter.EnsureDirectory(assetPath);

            var asset = AssetDatabase.LoadAssetAtPath<CharacterCreationPointBuyConfig>(assetPath);
            bool isNew = asset == null;
            if (isNew)
                asset = ScriptableObject.CreateInstance<CharacterCreationPointBuyConfig>();

            var first = rows[0];
            asset.purchasePointLimit = int.Parse(CsvTableReader.GetRequiredValue(headers, first, "purchasePointLimit", path));
            asset.minValue = int.Parse(CsvTableReader.GetRequiredValue(headers, first, "minValue", path));
            asset.baseValue = int.Parse(CsvTableReader.GetRequiredValue(headers, first, "baseValue", path));
            asset.maxValue = int.Parse(CsvTableReader.GetRequiredValue(headers, first, "maxValue", path));
            asset.costRanges = rows.Select(cols => new CharacterCreationPointBuyConfig.CostRange
            {
                fromValue = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "fromValue", path)),
                toValue = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "toValue", path)),
                costPerLevel = int.Parse(CsvTableReader.GetRequiredValue(headers, cols, "costPerLevel", path))
            }).ToArray();

            if (isNew)
                AssetDatabase.CreateAsset(asset, assetPath);
            else
                EditorUtility.SetDirty(asset);

            Debug.Log($"  角色创建点购配置: {asset.purchasePointLimit}点 ← {assetPath}");
        }
        [MenuItem("天章/导入敌人配置")]
        public static void ImportEnemies()
        {
            ImportContentCatalog();
        }

        private static CharterRuleStaticCatalogData LoadCharterRuleStaticCatalog()
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

        private static string GetRequiredCharterColumnValue(
            string[] headers,
            string[] columns,
            string name,
            string sourceName)
        {
            var value = CsvTableReader.GetRequiredValue(headers, columns, name, sourceName);
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
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ||
                value.Any(character =>
                    !char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '.'))
            {
                throw CharterError(failureCode, sourceName, $"has invalid reference '{value}' in '{fieldName}'.");
            }
        }

        private static InvalidDataException CharterError(string code, string sourceName, string message) =>
            new InvalidDataException($"{code}: {sourceName} {message}");
    }
}
