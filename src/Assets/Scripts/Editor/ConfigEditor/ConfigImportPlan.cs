using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TianZhang.Editor.ConfigEditor
{
    // One operation's disposable projections. CSV and the existing importers remain authoritative.
    public sealed class ConfigImportPlan : IDisposable
    {
        public sealed class Step
        {
            public ConfigImportStep Description { get; internal set; }
            public IReadOnlyDictionary<string, ScriptableObject> Products { get; internal set; }
            public Action Import { get; internal set; }
        }
        private const string Catalog = "Assets/Data/ContentCatalog/ContentCatalog.asset";
        private const string RuleAsset = "Assets/Data/CombatRuleSet.asset";
        private static readonly string[] Order = {
            "CultivationContentImporter.ImportNpcCultivationActionWeightProfiles",
            "CultivationContentImporter.ImportFoundationPurpleMansionStates", "CultivationContentImporter.ImportJindanStaticStates",
            "WorldContentImporter.ImportCharterRuleDefinitions", "CombatContentImporter.ImportAttackProfiles",
            "CultivationContentImporter.ImportGongFa", "CombatContentImporter.ImportSpells", "CombatContentImporter.ImportSkills",
            "CharacterContentImporter.ImportCharacterDefinitions", "SettlementContentImporter.ImportContentCatalog",
            "SettlementContentImporter.ImportCharterSites", "SettlementContentImporter.ImportCharacterCreationPointBuy",
            "WorldContentImporter.ImportEnvironmentProfiles", "AdventureContentImporter.Import" };
        private readonly List<ScriptableObject> owned = new List<ScriptableObject>();
        private readonly Dictionary<string, ScriptableObject> latest = new Dictionary<string, ScriptableObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> sources;
        private readonly Dictionary<string, string> language;
        private readonly CombatRuleSet rules;
        private readonly CombatRuleSetData ruleProjection;
        private readonly List<Step> steps = new List<Step>();
        public IReadOnlyList<Step> Steps => steps;
        public IReadOnlyDictionary<string, string> Guids { get; private set; }

        private ConfigImportPlan(ConfigDraftSession session)
        {
            sources = session.Snapshot().Tables.Values.ToDictionary(t => t.Definition.CsvPath,
                t => session.EncodeCandidate(t.Definition.TableId), StringComparer.Ordinal);
            language = CombatContentImporter.TokenizeRuleTable(Lines("Language")).Where(r => r.Length >= 2 && r[0] != "name")
                .ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);
            var ruleSources = new[] { "RealmCombatGrowth", "CombatEffectTierRules", "CombatActionEconomyRules", "AttackProfiles" }
                .ToDictionary(id => "src/Assets/DataConfig/" + id + ".csv", id => sources["Assets/DataConfig/" + id + ".csv"]);
            rules = CombatRuleSet.Parse(Tokens("RealmCombatGrowth"), Tokens("CombatEffectTierRules"), Tokens("CombatActionEconomyRules"),
                CombatRuleSet.ComputeSourceDigest(ruleSources)).WithUnarmedRows(CombatRuleSet.ParseUnarmedRows(Tokens("AttackProfiles")));
            ruleProjection = Own(CombatContentImporter.PreviewCombatRuleSet(rules));
        }

        public static ConfigImportPlan Build(ConfigDraftSession session, IReadOnlyCollection<string> changedTables)
        {
            var plan = new ConfigImportPlan(session);
            try
            {
                var descriptions = session.Snapshot().Tables.Values.Select(t => t.Definition.Codec).Distinct()
                    .SelectMany(c => c.GetImportSteps(changedTables)).GroupBy(s => s.Method).Select(g => new ConfigImportStep {
                        Method = g.Key, InputPaths = g.SelectMany(s => s.InputPaths).Distinct(StringComparer.Ordinal).ToArray(),
                        ProductPaths = g.SelectMany(s => s.ProductPaths).Distinct(StringComparer.Ordinal).ToArray() }).ToArray();
                foreach (var description in descriptions)
                    if (!Order.Contains(description.Method)) throw new InvalidDataException("未授权导入步骤：" + description.Method);
                foreach (var description in descriptions.OrderBy(d => Array.IndexOf(Order, d.Method))) plan.BuildStep(description);
                var paths = descriptions.SelectMany(d => d.InputPaths.Concat(d.ProductPaths)).Where(p => p.EndsWith(".asset", StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal).ToArray();
                plan.Guids = paths.ToDictionary(p => p, p => {
                    if (AssetDatabase.LoadMainAssetAtPath(p) == null || string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p)))
                        throw new InvalidDataException("必需资产缺失：" + p);
                    return AssetDatabase.AssetPathToGUID(p);
                }, StringComparer.Ordinal);
                return plan;
            }
            catch { plan.Dispose(); throw; }
        }

        private string[] Lines(string id)
        {
            using var reader = new StreamReader(new MemoryStream(sources["Assets/DataConfig/" + id + ".csv"]), Encoding.UTF8, true);
            var lines = new List<string>(); string line;
            while ((line = reader.ReadLine()) != null) lines.Add(line);
            return lines.ToArray();
        }
        private string[][] Tokens(string id) => CombatContentImporter.TokenizeRuleTable(Lines(id));
        private T Own<T>(T asset) where T : ScriptableObject { owned.Add(asset); return asset; }
        private static T Required<T>(string path) where T : ScriptableObject => AssetDatabase.LoadAssetAtPath<T>(path)
            ?? throw new InvalidDataException("必需资产缺失或类型错误：" + path);
        private T Clone<T>(string path) where T : ScriptableObject => Own(Object.Instantiate(
            latest.TryGetValue(path, out var value) ? (T)value : Required<T>(path)));
        private void BuildStep(ConfigImportStep description)
        {
            var products = new Dictionary<string, ScriptableObject>(StringComparer.Ordinal);
            void Add(string path, ScriptableObject asset)
            {
                if (!owned.Contains(asset)) owned.Add(asset);
                var original = AssetDatabase.LoadMainAssetAtPath(path);
                if (original == null || original.GetType() != asset.GetType()) throw new InvalidDataException("产物缺失或类型不符：" + path);
                products.Add(path, asset); latest[path] = asset;
            }
            void AddMany<T>(Dictionary<string, T> values) where T : ScriptableObject
            { foreach (var value in values.Values) Own(value); foreach (var pair in values) Add(pair.Key, pair.Value); }
            Action import;
            switch (description.Method)
            {
                case "CombatContentImporter.ImportAttackProfiles":
                    if (!CombatContentImporter.TryBuildAttackProfileProjection(Lines("AttackProfiles"), language.Keys.ToArray(), out var attacks, out var reason, rules))
                        throw new InvalidDataException(reason);
                    foreach (var attack in attacks) Own(attack);
                    if (!CombatContentImporter.TryValidateAttackProfileAssetIdProjection(attacks.Select(a => a.attackProfileId).ToArray(), out reason))
                        throw new InvalidDataException(reason);
                    foreach (var attack in attacks) Add("Assets/Data/AttackProfiles/AttackProfile_" + attack.attackProfileId + ".asset", attack);
                    Add(RuleAsset, ruleProjection);
                    var combatCatalog = Clone<ContentCatalogData>(Catalog);
                    combatCatalog.SetCombatRuleSet(Required<CombatRuleSetData>(RuleAsset)); Add(Catalog, combatCatalog);
                    import = CombatContentImporter.ImportAttackProfiles; break;
                case "CombatContentImporter.ImportSpells":
                    AddMany(CombatContentImporter.PreviewSpells(Lines("Spells"), language)); import = CombatContentImporter.ImportSpells; break;
                case "CombatContentImporter.ImportSkills":
                    AddMany(CombatContentImporter.PreviewSkills(Lines("Skills"), language)); import = CombatContentImporter.ImportSkills; break;
                case "CultivationContentImporter.ImportGongFa":
                    AddMany(CultivationContentImporter.PreviewGongFa(Lines("GongFa"), language)); import = CultivationContentImporter.ImportGongFa; break;
                case "CharacterContentImporter.ImportCharacterDefinitions":
                    var appearances = CharacterContentImporter.ParseAppearanceProfileDefinitions(Lines("AppearanceProfiles"), "AppearanceProfiles");
                    foreach (var value in appearances) Own(value);
                    foreach (var value in appearances) Add("Assets/Data/AppearanceProfiles/AppearanceProfile_" + value.appearanceProfileId + ".asset", value);
                    var characters = CharacterContentImporter.ParseCharacterDefinitions(Lines("Characters"), "Characters", ruleProjection, language);
                    foreach (var value in characters) Own(value);
                    var rows = Tokens("Characters"); var nameIndex = Array.IndexOf(rows[0], "name");
                    for (int i = 0; i < characters.Length; i++) Add("Assets/Data/Characters/Char_" + AssetCommitter.SanitizeName(rows[i + 1][nameIndex]) + ".asset", characters[i]);
                    foreach (var input in new[] { ("Player", "realm_fanren"), ("Enemy", "realm_lianqi") })
                    {
                        var path = "Assets/Data/Characters/Char_" + input.Item1 + ".asset";
                        var target = Clone<CharacterData>(path);
                        CharacterContentImporter.ProjectIndependentCharacter(target, input.Item2, ruleProjection); Add(path, target);
                    }
                    import = CharacterContentImporter.ImportCharacterDefinitions; break;
                case "SettlementContentImporter.ImportCharacterCreationPointBuy":
                    var point = SettlementContentImporter.PreviewCharacterCreationPointBuy(Lines("CharacterCreationPointBuy"), ruleProjection);
                    if (point == null) throw new InvalidDataException("缺少创角default配置。");
                    Add("Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset", point);
                    import = SettlementContentImporter.ImportCharacterCreationPointBuy; break;
                case "SettlementContentImporter.ImportContentCatalog":
                    BuildContent(Add); import = SettlementContentImporter.ImportContentCatalog; break;
                case "SettlementContentImporter.ImportCharterSites":
                    var staticCatalog = WorldContentImporter.LoadCharterRuleStaticCatalog();
                    if (!staticCatalog.TryValidateDefinitions(out reason)) throw new InvalidDataException(reason);
                    var sites = SettlementContentImporter.PreviewCharterSites(Lines("CharterSites"), Lines("Language"));
                    foreach (var site in sites) Own(site);
                    foreach (var site in sites) Add("Assets/Data/CharterSites/CharterSite_" + site.siteId + ".asset", site);
                    var siteCatalog = Clone<ContentCatalogData>(Catalog);
                    siteCatalog.SetCharterSites(sites.Select(s => Required<CharterSiteData>("Assets/Data/CharterSites/CharterSite_" + s.siteId + ".asset")).ToArray());
                    Add(Catalog, siteCatalog); import = SettlementContentImporter.ImportCharterSites; break;
                case "WorldContentImporter.ImportCharterRuleDefinitions":
                    var authority = WorldContentImporter.LoadCharterRuleStaticCatalog();
                    if (!authority.TryValidateDefinitions(out reason)) throw new InvalidDataException(reason);
                    var definitions = WorldContentImporter.ParseCharterRuleDefinitions(Lines("CharterRuleDefinitions"), "CharterRuleDefinitions", authority.ReferenceCatalog);
                    foreach (var value in definitions) Own(value);
                    foreach (var value in definitions) Add("Assets/Data/CharterRuleDefinitions/CharterRuleDefinition_" + AssetCommitter.SanitizeName(value.ruleEntryId) + ".asset", value);
                    import = WorldContentImporter.ImportCharterRuleDefinitions; break;
                case "WorldContentImporter.ImportEnvironmentProfiles":
                    foreach (var value in WorldContentImporter.ParseEnvironmentProfiles(Lines("EnvironmentProfiles"), "EnvironmentProfiles"))
                    {
                        var asset = Own(ScriptableObject.CreateInstance<EnvironmentProfileAsset>()); asset.Apply(value);
                        if (!asset.TryCreateDefinition(out _, out reason)) throw new InvalidDataException(reason);
                        Add("Assets/Data/EnvironmentProfiles/EnvironmentProfile_" + AssetCommitter.SanitizeName(value.profileId) + ".asset", asset);
                    }
                    import = WorldContentImporter.ImportEnvironmentProfiles; break;
                case "AdventureContentImporter.Import":
                    var maps = AdventureContentImporter.Parse(Lines("Adventures"), "Adventures");
                    foreach (var map in maps) Own(map);
                    foreach (var map in maps) Add("Assets/Data/Adventures/AdventureMap_" + map.adventureId + ".asset", map);
                    var adventureCatalog = Clone<ContentCatalogData>(Catalog);
                    adventureCatalog.SetAdventureMaps(maps.Select(m => Required<AdventureMapData>("Assets/Data/Adventures/AdventureMap_" + m.adventureId + ".asset")).ToArray());
                    Add(Catalog, adventureCatalog); import = AdventureContentImporter.Import; break;
                case "CultivationContentImporter.ImportNpcCultivationActionWeightProfiles":
                    var policies = CultivationContentImporter.ParseNpcCultivationActionWeightProfiles(Lines("NpcCultivationActionWeightProfiles"), "NpcCultivationActionWeightProfiles");
                    foreach (var value in policies) Own(value);
                    foreach (var value in policies) Add("Assets/Data/NpcCultivationActionWeightProfiles/NpcCultivationActionWeightProfile_" + AssetCommitter.SanitizeName(value.profileId) + ".asset", value);
                    import = CultivationContentImporter.ImportNpcCultivationActionWeightProfiles; break;
                case "CultivationContentImporter.ImportFoundationPurpleMansionStates":
                    var foundation = CultivationContentImporter.ParseFoundationPurpleMansionStates(Lines("FoundationPurpleMansionStates"), "FoundationPurpleMansionStates", false);
                    foreach (var value in foundation) Own(value);
                    if (foundation.Length != 0) throw new InvalidDataException("生产道基表不允许新增角色行。");
                    import = CultivationContentImporter.ImportFoundationPurpleMansionStates; break;
                case "CultivationContentImporter.ImportJindanStaticStates":
                    var jindan = CultivationContentImporter.ParseJindanStaticStates(Lines("JindanStaticStates"), "JindanStaticStates", new JindanStaticReferenceCatalog(), false);
                    foreach (var value in jindan) Own(value);
                    if (jindan.Length != 0) throw new InvalidDataException("生产金丹表不允许新增角色行。");
                    import = CultivationContentImporter.ImportJindanStaticStates; break;
                default: throw new InvalidDataException("未知导入步骤。");
            }
            if (!new HashSet<string>(products.Keys, StringComparer.Ordinal).SetEquals(description.ProductPaths))
                throw new InvalidDataException("导入产物与领域清单不一致：" + description.Method);
            steps.Add(new Step { Description = description, Products = products, Import = import });
        }

        private void BuildContent(Action<string, ScriptableObject> add)
        {
            var preview = SettlementContentImporter.ParseContentCatalog(Lines("Language"), Lines("Settlements"), Lines("Items"), Lines("Bounties"), Lines("Enemies"), ruleProjection);
            try
            {
                SettlementContentImporter.ValidateContentAssetLocations(preview);
                var appearances = SettlementContentImporter.LoadAppearanceProfiles();
                var attackCandidates = latest.Values.OfType<AttackProfileData>().ToDictionary(a => a.attackProfileId);
                var equipment = SettlementContentImporter.LoadEquipmentProfiles(preview, Lines("Language"), Lines("AttackProfiles"), rules,
                    attackCandidates.Count == 0 ? null : attackCandidates);
                for (int i = 0; i < preview.enemyTemplates.Length; i++)
                {
                    var path = "Assets/Data/Characters/Char_Enemy_" + preview.enemyTemplateIds[i] + ".asset";
                    var clone = Clone<CharacterData>(path); SettlementContentImporter.CopyCharacterTemplate(preview.enemyTemplates[i], clone); add(path, clone);
                }
                foreach (var value in preview.settlements)
                { var path = "Assets/Data/Settlements/Settlement_" + value.settlementId + ".asset"; var clone = Clone<SettlementData>(path); SettlementContentImporter.CopySettlement(value, clone); add(path, clone); }
                foreach (var value in preview.items)
                { var path = "Assets/Data/Items/Item_" + value.itemId + ".asset"; var clone = Clone<ItemData>(path); SettlementContentImporter.CopyItem(value, clone); add(path, clone); }
                foreach (var value in preview.enemies)
                {
                    value.combatTemplate = Required<CharacterData>("Assets/Data/Characters/Char_Enemy_" + value.enemyId + ".asset");
                    var path = "Assets/Data/Enemies/Enemy_" + value.enemyId + ".asset"; var clone = Clone<EnemyData>(path);
                    SettlementContentImporter.CopyEnemy(value, clone); add(path, clone);
                }
                foreach (var value in preview.bounties)
                { var path = "Assets/Data/Bounties/Bounty_" + value.bountyId + ".asset"; var clone = Clone<BountyData>(path); SettlementContentImporter.CopyBounty(value, clone); add(path, clone); }
                var catalog = Clone<ContentCatalogData>(Catalog);
                catalog.ReplaceEntries(preview.settlements.Select(v => Required<SettlementData>("Assets/Data/Settlements/Settlement_" + v.settlementId + ".asset")).ToArray(),
                    preview.enemies.Select(v => Required<EnemyData>("Assets/Data/Enemies/Enemy_" + v.enemyId + ".asset")).ToArray(),
                    preview.items.OrderBy(v => v.itemId, StringComparer.Ordinal).Select(v => Required<ItemData>("Assets/Data/Items/Item_" + v.itemId + ".asset")).ToArray(),
                    preview.bounties.Select(v => Required<BountyData>("Assets/Data/Bounties/Bounty_" + v.bountyId + ".asset")).ToArray());
                catalog.SetAppearanceProfiles(appearances); catalog.SetEquipmentAttackProfiles(equipment);
                var authority = WorldContentImporter.LoadCharterRuleStaticCatalog();
                if (!authority.TryValidateDefinitions(out var reason)) throw new InvalidDataException(reason);
                catalog.SetCharterRuleStaticCatalog(authority); add(Catalog, catalog);
            }
            finally { preview.DestroyTransientAssets(); }
        }

        public void Verify(Step step)
        {
            foreach (var pair in step.Products)
            {
                var actual = AssetDatabase.LoadMainAssetAtPath(pair.Key);
                if (actual == null || actual.GetType() != pair.Value.GetType() || AssetDatabase.AssetPathToGUID(pair.Key) != Guids[pair.Key])
                    throw new InvalidDataException("产物类型、GUID或数量错误：" + pair.Key);
                using var expected = new SerializedObject(pair.Value);
                using var found = new SerializedObject(actual);
                var property = expected.GetIterator();
                bool enterChildren = true;
                while (property.Next(enterChildren))
                {
                    enterChildren = true;
                    // Type, script and GUID are checked separately; transient instance metadata is not config.
                    if (property.propertyPath == "m_Name" || property.propertyPath == "m_EditorClassIdentifier" ||
                        property.propertyPath == "m_ObjectHideFlags") { enterChildren = false; continue; }
                    var target = found.FindProperty(property.propertyPath);
                    if (target == null || !SerializedProperty.DataEquals(property, target))
                        throw new InvalidDataException("产物字段不符：" + pair.Key + " / " + property.propertyPath);
                }
                if (actual is AttackProfileData attack && !attack.TryValidate(out var attackReason)) throw new InvalidDataException(attackReason);
                if (actual is CombatRuleSetData rule && !rule.TryValidateIdentity(out var ruleReason)) throw new InvalidDataException(ruleReason);
                if (actual is AppearanceProfileData appearance && !appearance.TryValidate(out var appearanceReason)) throw new InvalidDataException(appearanceReason);
                if (actual is EnvironmentProfileAsset environment && !environment.TryCreateDefinition(out _, out var environmentReason)) throw new InvalidDataException(environmentReason);
            }
        }
        public void Dispose() { foreach (var asset in owned.Distinct()) if (asset != null && !AssetDatabase.Contains(asset)) Object.DestroyImmediate(asset); owned.Clear(); }
    }
}
