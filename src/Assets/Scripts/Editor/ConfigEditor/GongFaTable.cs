using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class GongFaTable : IConfigTableProvider, IConfigRowCodec
    {
        private const string Id = "GongFa";
        private static readonly string[] GrowthColumns = { "hp", "mp", "physAtk", "magAtk", "physDef", "magDef", "reaction", "movePoints", "mindGrowth" };
        private static readonly string[] GrowthNames = { "生命", "灵力", "物攻", "法攻", "物防", "法防", "反应", "移动点", "神识" };
        private static readonly string[] ChapterColumns = { "soulShieldRate", "hitRate", "blockRate", "critRate", "critDamage", "dodgeRate", "magAtkBonus", "magDefBonus" };
        private static readonly string[] ChapterNames = { "魂盾率", "命中率", "格挡率", "暴击率", "暴伤", "闪避率", "法攻", "法防" };

        public IEnumerable<ConfigTableDefinition> GetTables()
        {
            var fields = new List<ConfigFieldDefinition> { CharacterEditorFields.Lock(CharacterEditorFields.Text("name", "功法ID"), "功法和资产稳定身份"),
                CharacterEditorFields.Language("affiliation", "门派", "faction_"), CharacterEditorFields.Language("grade", "品级", "grade_"),
                Element("elementMain", "主属性"), Element("elementSub", "辅属性") };
            var stars = new[] { "RootBone", "Physique", "Spirit", "Mind", "Reaction", "Talent", "Fortune" };
            var names = new[] { "根骨", "体魄", "灵力", "神识", "反应", "资质", "福缘" };
            for (int i = 0; i < stars.Length; i++)
            {
                var field = CharacterEditorFields.Number("star" + stars[i], names[i] + "倾向", "星", true);
                field.Help = "字段定义为1至5星"; fields.Add(field);
            }
            var growthChildren = new List<ConfigFieldDefinition> { CharacterEditorFields.Language("realmId", "境界", "realm_"),
                CharacterEditorFields.Enum("stageCode", "阶段码", "0", "无阶段", "10", "筑基", "20", "开府", "30", "圆满") };
            for (int i = 0; i < GrowthColumns.Length; i++) growthChildren.Add(CharacterEditorFields.Number(GrowthColumns[i], "每小级" + GrowthNames[i] + "增量", "原始增量"));
            fields.Add(List("growth", "成长列表", growthChildren.ToArray(), false));
            var chapterChildren = new List<ConfigFieldDefinition> { CharacterEditorFields.Language("chapterNameKey", "篇章名"), CharacterEditorFields.Language("realmKey", "解锁境界", "realm_") };
            for (int i = 0; i < ChapterColumns.Length; i++) chapterChildren.Add(CharacterEditorFields.Number(ChapterColumns[i], ChapterNames[i] + "加成", "加成原值"));
            var effect = CharacterEditorFields.Language("specialEffectKey", "特殊效果文本"); effect.AllowEmpty = true; chapterChildren.Add(effect);
            fields.Add(List("chapters", "篇章列表", chapterChildren.ToArray(), true));
            fields.Add(CharacterEditorFields.Enum("contentScope", "内容范围", "player", "玩家内容", "reserved", "预留内容"));
            yield return CharacterEditorFields.Table(Id, "功法", "name", fields.ToArray(), this);
        }

        private static ConfigFieldDefinition Element(string column, string name)
        {
            var field = CharacterEditorFields.Language(column, name, "element_");
            // Attribute IDs are literal element names, not root requirements or combinations.
            field.Choices = () => CharacterEditorFields.Rows("Language").Where(r => r["id"].StartsWith("element_", StringComparison.Ordinal) &&
                r["id"].Substring(8).IndexOf('_') < 0).Select(r => new ConfigChoice(r["id"], r["zh_CN"])).ToArray();
            return field;
        }

        private static ConfigFieldDefinition List(string column, string name, ConfigFieldDefinition[] children, bool empty)
        {
            return new ConfigFieldDefinition { Column = column, ChineseName = name, Control = ConfigControl.List, Editable = true, AllowEmpty = empty,
                EmptyMeaning = "无篇章", Element = new ConfigFieldDefinition { ChineseName = column == "growth" ? "境界成长" : "篇章", Control = ConfigControl.Group,
                    Editable = true, Children = children }, NewElement = () => ConfigValue.Group(children.ToDictionary(c => c.Column, c =>
                        ConfigValue.Scalar(c.AllowEmpty ? "" : c.Control == ConfigControl.Reference || c.Control == ConfigControl.Enum ? c.Choices().First().Id : "0"))),
                Help = column == "growth" ? "顺序保留；只有筑基可选10、20、30阶段；同一境界阶段不重复" : "按原值编辑八项加成；清空表示无篇章" };
        }

        public ConfigValue DecodeRow(ConfigCsvRow row)
        {
            var values = row.Cells.ToDictionary(v => v.Key, v => ConfigValue.Scalar(v.Value));
            values["growth"] = CharacterEditorFields.DecodeList(row["growth"], DecodeGrowth);
            values["chapters"] = CharacterEditorFields.DecodeList(row["chapters"], DecodeChapter);
            return ConfigValue.Group(values);
        }

        private static ConfigValue DecodeGrowth(string token)
        {
            var parts = token.Split(':');
            if (parts.Length != 2) throw new InvalidDataException("GONGFA_GROWTH_RECORD_INVALID: 成长条目必须含境界与九项增量");
            var key = parts[0].Split('@'); var numbers = parts[1].Split('/');
            if (key.Length > 2 || numbers.Length != 9) throw new InvalidDataException("GONGFA_GROWTH_VALUES_INVALID: 成长子字段数量不正确");
            var members = new Dictionary<string, ConfigValue> { ["realmId"] = ConfigValue.Scalar(key[0]), ["stageCode"] = ConfigValue.Scalar(key.Length == 2 ? key[1] : "0") };
            for (int i = 0; i < numbers.Length; i++) members[GrowthColumns[i]] = ConfigValue.Scalar(numbers[i]);
            return ConfigValue.Group(members, token);
        }

        private static ConfigValue DecodeChapter(string token)
        {
            var parts = token.Split(':');
            if (parts.Length != 10 && parts.Length != 11) throw new InvalidDataException("GONGFA_CHAPTER_RECORD_INVALID: 篇章必须为10或11个子字段");
            var members = new Dictionary<string, ConfigValue> { ["chapterNameKey"] = ConfigValue.Scalar(parts[0]), ["realmKey"] = ConfigValue.Scalar(parts[1]) };
            for (int i = 0; i < ChapterColumns.Length; i++) members[ChapterColumns[i]] = ConfigValue.Scalar(parts[i + 2]);
            members["specialEffectKey"] = ConfigValue.Scalar(parts.Length == 11 ? parts[10] : "");
            return ConfigValue.Group(members, token);
        }

        public IReadOnlyDictionary<string, string> EncodeRow(ConfigValue row)
        {
            var values = row.Members.ToDictionary(v => v.Key, v => v.Value.RawToken);
            values["growth"] = string.Join("|", row.At("growth").Items.Select(item => item.At("realmId").RawToken +
                (item.At("stageCode").RawToken == "0" ? "" : "@" + item.At("stageCode").RawToken) + ":" +
                string.Join("/", GrowthColumns.Select(c => item.At(c).RawToken))));
            values["chapters"] = string.Join("|", row.At("chapters").Items.Select(item => item.At("chapterNameKey").RawToken + ":" + item.At("realmKey").RawToken + ":" +
                string.Join(":", ChapterColumns.Select(c => item.At(c).RawToken)) +
                (item.At("specialEffectKey").HasValue || item.RawToken.Split(':').Length == 11 ? ":" + item.At("specialEffectKey").RawToken : "")));
            return values;
        }

        public IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot)
        {
            if (!snapshot.Tables.TryGetValue(Id, out var table)) return Array.Empty<ConfigIssue>();
            var issues = CharacterEditorFields.ValidateFields(table, snapshot);
            foreach (var row in table.Rows)
            {
                foreach (var field in table.Definition.Fields.Where(f => f.Column.StartsWith("star", StringComparison.Ordinal)))
                    if (!int.TryParse(row.Value.At(field.Column).RawToken, out var stars) || stars < 1 || stars > 5)
                        issues.Add(new ConfigIssue(Id, row.Key, field.Column, "倾向星数必须为1至5", "gongfa_star_invalid"));
                try { CultivationContentImporter.ParseGongFaGrowth(EncodeRow(row.Value)["growth"], Id); }
                catch (InvalidDataException e)
                {
                    var code = e.Message.Split(':')[0];
                    issues.Add(new ConfigIssue(Id, row.Key, "growth", "原功法成长校验：" + e.Message, code));
                }
            }
            return issues;
        }
        public IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot) => Array.Empty<ConfigEdit>();
        public IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> changedTables) =>
            changedTables.Contains(Id) ? CharacterImportDescriptions.For(Id) : Array.Empty<ConfigImportStep>();
    }

    // Declarative descriptions for APPLY. No importer is invoked by an adapter.
    internal static class CharacterImportDescriptions
    {
        private static string Csv(string id) => "Assets/DataConfig/" + id + ".csv";
        private static string[] Products(string table, string key, string directory, string prefix) =>
            CharacterEditorFields.Rows(table).Select(r => "Assets/Data/" + directory + "/" + prefix + AssetCommitter.SanitizeName(r[key]) + ".asset").Distinct().ToArray();
        private static ConfigImportStep Step(string method, IEnumerable<string> inputs, IEnumerable<string> products) =>
            new ConfigImportStep { Method = method, InputPaths = inputs.Distinct().ToArray(), ProductPaths = products.Distinct().ToArray() };
        internal static IReadOnlyList<ConfigImportStep> For(string id)
        {
            var steps = new List<ConfigImportStep>();
            if (id == "Language") steps.Add(Step("CombatContentImporter.ImportAttackProfiles",
                new[] { "Language", "AttackProfiles", "RealmCombatGrowth", "CombatEffectTierRules", "CombatActionEconomyRules" }.Select(Csv)
                    .Concat(new[] { "Assets/Data/CombatRuleSet.asset", "Assets/Data/ContentCatalog/ContentCatalog.asset" }),
                Products("AttackProfiles", "attackProfileId", "AttackProfiles", "AttackProfile_").Concat(new[] { "Assets/Data/CombatRuleSet.asset", "Assets/Data/ContentCatalog/ContentCatalog.asset" })));
            if (id == "GongFa" || id == "Language") steps.Add(Step("CultivationContentImporter.ImportGongFa",
                new[] { Csv("GongFa"), Csv("Language") }, Products("GongFa", "name", "GongFa", "GongFa_")));
            if (id == "Language")
            {
                steps.Add(Step("CombatContentImporter.ImportSpells", new[] { Csv("Spells"), Csv("Language") }, Products("Spells", "name", "Spells", "Spell_")));
                steps.Add(Step("CombatContentImporter.ImportSkills", new[] { Csv("Skills"), Csv("Language") }, Products("Skills", "name", "Skills", "Skill_")));
            }
            if (id == "Characters" || id == "AppearanceProfiles" || id == "Language") steps.Add(Step("CharacterContentImporter.ImportCharacterDefinitions",
                new[] { "Characters", "AppearanceProfiles", "Language", "AttackProfiles", "RealmCombatGrowth", "CombatEffectTierRules", "CombatActionEconomyRules" }.Select(Csv)
                    .Concat(new[] { "Assets/Data/CombatRuleSet.asset", "Assets/Data/Characters/Char_Player.asset", "Assets/Data/Characters/Char_Enemy.asset" })
                    .Concat(Products("AttackProfiles", "attackProfileId", "AttackProfiles", "AttackProfile_")),
                Products("Characters", "name", "Characters", "Char_").Concat(new[] { "Assets/Data/AppearanceProfiles/AppearanceProfile_none.asset",
                    "Assets/Data/Characters/Char_Player.asset", "Assets/Data/Characters/Char_Enemy.asset" })));
            if (id == "Enemies" || id == "Items" || id == "AppearanceProfiles" || id == "Language") steps.Add(ContentStep());
            if (id == "Language")
            {
                steps.Add(Step("SettlementContentImporter.ImportCharterSites",
                    new[] { Csv("CharterSites"), Csv("Language"), "Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset", "Assets/Data/ContentCatalog/ContentCatalog.asset" }
                        .Concat(Products("CharterRuleDefinitions", "ruleEntryId", "CharterRuleDefinitions", "CharterRuleDefinition_")),
                    Products("CharterSites", "siteId", "CharterSites", "CharterSite_").Concat(new[] { "Assets/Data/ContentCatalog/ContentCatalog.asset" })));
                steps.Add(Step("AdventureContentImporter.Import", new[] { Csv("Adventures"), Csv("Language"), "Assets/Data/ContentCatalog/ContentCatalog.asset" },
                    Products("Adventures", "adventureId", "Adventures", "AdventureMap_").Concat(new[] { "Assets/Data/ContentCatalog/ContentCatalog.asset" })));
            }
            return steps;
        }
        private static ConfigImportStep ContentStep() => Step("SettlementContentImporter.ImportContentCatalog",
            new[] { "Language", "Settlements", "Items", "Bounties", "Enemies", "AttackProfiles", "RealmCombatGrowth", "CombatEffectTierRules", "CombatActionEconomyRules" }.Select(Csv)
                .Concat(new[] { "Assets/Data/AppearanceProfiles/AppearanceProfile_none.asset", "Assets/Data/CombatRuleSet.asset", "Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset" })
                .Concat(Products("AttackProfiles", "attackProfileId", "AttackProfiles", "AttackProfile_"))
                .Concat(Products("CharterRuleDefinitions", "ruleEntryId", "CharterRuleDefinitions", "CharterRuleDefinition_")),
            Products("Items", "itemId", "Items", "Item_").Concat(Products("Enemies", "name", "Characters", "Char_Enemy_"))
                .Concat(new[] { "Assets/Data/ContentCatalog/ContentCatalog.asset", "Assets/Data/Settlements/Settlement_guanzhong_city.asset",
                    "Assets/Data/Enemies/Enemy_enemy_shijiahou.asset", "Assets/Data/Bounties/Bounty_bounty_guanzhong_shijiahou.asset" }));
    }
}
