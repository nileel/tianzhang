using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class CombatAbilityTables : IConfigTableProvider
    {
        [MenuItem("天章/配置编辑器预览/当前攻击档案派生值")]
        public static void PreviewSelectedAttack()
        {
            var window = ConfigEditorWindow.Open();
            if (window.SelectedTableId != "AttackProfiles" || window.SelectedRowKey == "")
            { EditorUtility.DisplayDialog("攻击档案派生预览", "请先在配置编辑器选择已有攻击档案。", "关闭"); return; }
            try { EditorUtility.DisplayDialog("攻击档案派生预览", GetAttackDerivedPreview(window.Session.Snapshot(), window.SelectedRowKey), "关闭"); }
            catch (InvalidDataException e) { EditorUtility.DisplayDialog("派生预览未通过校验", e.Message, "关闭"); }
        }
        public static string GetAttackDerivedPreview(ConfigDraftSnapshot snapshot, string rowKey)
        {
            var profiles = CombatAbilityCodec.Project(snapshot);
            try
            {
                var profile = profiles.Single(p => p.attackProfileId == rowKey);
                return "只读派生预览，不写回声明列：物理伤害 " + profile.physicalDamageMultiplier.ToString("G9", CultureInfo.InvariantCulture) +
                    " 倍；魂伤 " + profile.soulDamageMultiplier.ToString("G9", CultureInfo.InvariantCulture) + " 倍；重用冷却 " + profile.reuseCooldownTicks + " 游戏刻。";
            }
            finally { foreach (var profile in profiles) UnityEngine.Object.DestroyImmediate(profile); }
        }
        private static readonly string[] SpellColumns = "name,type,minRange,maxRange,mpCost,cooldownTicks,healAmount,cannotBlock,cannotDodge,penetratingShield,stunChance,realmReq,elementReq,element,sourceAffiliation,contentScope,physicalDamageMultiplier,soulDamageMultiplier".Split(',');
        private static readonly string[] SkillColumns = "name,type,minRange,maxRange,mpCost,cooldownTicks,damageMultiplier,healAmount,cannotBlock,cannotDodge,penetratingShield,stunChance,isDomain,isBloodline,specialEffectDesc,element,realmReq,sourceAffiliation,contentScope".Split(',');
        private static readonly string[] Elements = "metal,wood,water,fire,earth,wind,thunder,ice,dark,star,poison,chaos,none".Split(',');
        private static readonly string[] ElementNames = "金|木|水|火|土|风|雷|冰|暗|星|毒|混沌|无".Split('|');
        private static readonly string[] AttackLabels = "攻击档案ID|显示名称|档案类型|普攻绑定|内容范围|来源阵营|境界要求|任一灵根要求|效果类型|伤害元素|物理伤害倍数|魂伤倍数|治疗量|旧增益倍数|防御穿透值|资源类型|资源费用|基础行动力费|净费下限|行动条惩罚|重用冷却|最小施展距离|最大施展距离|目标方式|区域中心|区域形状|外半径|长度|扇形半角档数|方向|内半径|效果阻挡|允许阵营|允许状态|是否领域|是否血脉|特殊效果说明|自身回灵倍数|最低回灵点|对他人回灵费用|对他人回灵倍数|规则版本|效果层级|冷却类别|效果预算|效率分子|效率分母|换算分子|换算分母|权重分子|权重分母|状态ID|作用属性|相对变化|持续自身回合结束次数|叠层上限|刷新策略|控制概率|是否心神对抗|攻击结构版本|身体攻击族|身体境界|身体物攻容量".Split('|');

        public IEnumerable<ConfigTableDefinition> GetTables()
        {
            yield return AttackTable();
            yield return LegacyTable(false);
            yield return LegacyTable(true);
        }
        private static ConfigTableDefinition LegacyTable(bool skill)
        {
            var columns = skill ? SkillColumns : SpellColumns;
            var names = (skill ? "名称ID|效果类型|最小距离|最大距离|灵力费用|冷却|伤害倍数|治疗量|不可格挡|不可闪避|穿透护盾|眩晕概率|领域|血脉|特殊说明|作用元素|境界要求|所属来源|内容范围" :
                "名称ID|效果类型|最小距离|最大距离|灵力费用|冷却|治疗量|不可格挡|不可闪避|穿透护盾|眩晕概率|境界要求|任一灵根要求|作用元素|所属来源|内容范围|物伤倍数|魂伤倍数").Split('|');
            var fields = columns.Select((c, i) => Field(c, names[i], false)).ToArray();
            var id = skill ? "Skills" : "Spells";
            var table = CombatEditorFields.Table(id, skill ? "神通" : "术法", columns, fields, new CombatAbilityCodec(id), "name");
            table.GroupingFields = new[] { "contentScope", "realmReq" };
            return table;
        }
        private static ConfigTableDefinition AttackTable()
        {
            var fields = CombatRuleSet.AttackColumns.Select((c, i) => Field(c, AttackLabels[i], true)).ToArray();
            foreach (var f in fields)
            {
                string c = f.Column;
                if (new[] { "attackProfileId", "profileKind", "basicBindingKind", "ruleSetVersion", "attackSchemaVersion", "unarmedFamilyId", "unarmedRealmId" }.Contains(c))
                    CombatEditorFields.Lock(f, "稳定身份、版本或身体绑定组合，不能修改");
                if (c == "buffMultiplier") CombatEditorFields.Lock(f, "旧增益列，现行要求为空");
                if (c == "statusStackLimit" || c == "statusRefreshPolicy") CombatEditorFields.Lock(f, "状态批准值固定为一层与替换更强；非状态保持空");
                if (new[] { "contentScope", "sourceAffiliation", "realmRequirementId", "elementRequirementId" }.Contains(c))
                    f.CanEdit = r => Value(r, "profileKind") != "basic";
                if (c == "displayNameKey" || c == "minCastRange" || c == "maxCastRange")
                    f.CanEdit = r => Value(r, "effectBudgetMode") != "fixed_unarmed";
                if (c == "physicalDamageMultiplier") f.CanEdit = r => Physical(r) && Value(r, "effectBudgetMode") != "damage_budget";
                if (c == "soulDamageMultiplier") f.CanEdit = r => Soul(r) && Value(r, "effectBudgetMode") != "damage_budget";
                if (c == "physicalDamageMultiplier" || c == "soulDamageMultiplier")
                {
                    f.ReadOnlyReason = "伤害预算模式由规则派生；不适用列保持空，不将派生值写回CSV";
                    f.Help = "只读数值预览：天章 → 配置编辑器预览 → 当前攻击档案派生值；采用当前草稿与原领域解析器。";
                }
                if (c == "damageElementId" || c == "defensePenetration" || c.StartsWith("control", StringComparison.Ordinal)) f.CanEdit = Damage;
                if (c == "healAmount") f.CanEdit = r => Value(r, "effectType") == "heal";
                if (new[] { "spiritRecoveryMultiplier", "minimumSpiritRecovery", "otherTargetResourceCost", "otherTargetRecoveryMultiplier" }.Contains(c))
                    f.CanEdit = r => Value(r, "effectType") == "restore_spirit";
                if (c == "unarmedPhysicalAttackCapacity") f.CanEdit = r => Value(r, "effectBudgetMode") == "fixed_unarmed";
                if (c.EndsWith("Numerator", StringComparison.Ordinal) || c.EndsWith("Denominator", StringComparison.Ordinal))
                {
                    f.CanEdit = r => Value(r, "effectBudgetMode") == "damage_budget";
                    f.Unit = "无量纲分数"; f.Help = "正整数分子与分母，未改值保持原分数，不自动约分。";
                }
                if (c.StartsWith("area", StringComparison.Ordinal)) f.CanEdit = r => Value(r, "targetingMode") == "area" &&
                    (c != "areaFacing" || Value(r, "areaShapeKind") is "line" or "fan");
                if (c.StartsWith("status", StringComparison.Ordinal)) f.CanEdit = r => Value(r, "effectType") == "buff" || Value(r, "effectType") == "debuff";
                if (new[] { "isDomain", "isBloodline", "specialEffectTextKey" }.Contains(c)) f.CanEdit = r => Value(r, "profileKind") == "divine";
                if (f.CanEdit != null && f.ReadOnlyReason == "尚未接入字段适配") f.ReadOnlyReason = "当前分支不适用或身体映射固定；保持原声明与空值";
            }
            var table = CombatEditorFields.Table("AttackProfiles", "攻击档案", CombatRuleSet.AttackColumns, fields, new CombatAbilityCodec("AttackProfiles"), "attackProfileId");
            table.RowLabel = row => CombatEditorFields.Name(row["displayNameKey"]) + "（" + row["attackProfileId"] + "）";
            table.GroupingFields = new[] { "profileKind", "effectBudgetMode" };
            return table;
        }
        private static string Value(ConfigValue r, string c) => r.At(c).RawToken;
        private static bool Physical(ConfigValue r) => Value(r, "effectType") == "physical" || Value(r, "effectType") == "hybrid";
        private static bool Soul(ConfigValue r) => Value(r, "effectType") == "magic" || Value(r, "effectType") == "hybrid";
        private static bool Damage(ConfigValue r) => Physical(r) || Soul(r);
        private static ConfigFieldDefinition Field(string c, string name, bool attack)
        {
            ConfigFieldDefinition f;
            switch (c)
            {
                case "name": f = CombatEditorFields.Lock(CombatEditorFields.Reference(c, name, "Language", "id"), "名称ID也是资产身份，不可修改"); break;
                case "attackProfileId": case "ruleSetVersion": case "unarmedFamilyId": case "statusId": f = CombatEditorFields.Text(c, name, attack); break;
                case "displayNameKey": case "specialEffectTextKey": case "specialEffectDesc":
                    f = CombatEditorFields.Reference(c, name, "Language", "id", "", c == "specialEffectTextKey"); break;
                case "sourceAffiliation": f = CombatEditorFields.Reference(c, name, "Language", "id", "faction_", attack); break;
                case "realmRequirementId": case "realmReq": case "unarmedRealmId":
                    f = CombatEditorFields.Reference(c, name, "Language", "id", "realm_", attack); break;
                case "abilityTierId": f = CombatEditorFields.Reference(c, name, "CombatEffectTierRules", "abilityTierId", "", true); break;
                case "contentScope": f = CombatEditorFields.Scope(c, name); f.AllowEmpty = attack; break;
                case "elementReq": case "elementRequirementId": f = RootField(c, name, attack); break;
                case "element": case "damageElementId":
                    f = CombatEditorFields.Enum(c, name, Elements.SelectMany((e, i) => new[] { "element_" + e, ElementNames[i] })
                        .Concat(c == "element" ? Elements.Take(12).SelectMany((e, i) => new[] { "element_" + e + "_root", ElementNames[i] + "灵根" }) : Array.Empty<string>()).ToArray());
                    f.AllowEmpty = attack; break;
                case "profileKind": f = CombatEditorFields.Enum(c, name, "basic", "普攻", "art", "术法", "divine", "神通"); break;
                case "basicBindingKind": f = CombatEditorFields.Enum(c, name, "main_equipment", "主手", "unarmed_body", "身体"); f.AllowEmpty = true; break;
                case "type": f = CombatEditorFields.Enum(c, name, "0", "物理", "1", "法术", "2", "治疗", "3", "增益", "4", "减益", "5", "移动", "6", "混合"); break;
                case "effectType": f = CombatEditorFields.Enum(c, name, "physical", "物理", "magic", "法术", "hybrid", "混合", "heal", "治疗", "buff", "增益", "debuff", "减益", "restore_spirit", "回灵"); break;
                case "resourceKind": f = CombatEditorFields.Enum(c, name, "none", "无", "mp", "灵力"); break;
                case "targetingMode": f = CombatEditorFields.Enum(c, name, "single", "单体", "area", "区域"); break;
                case "cooldownClass": f = new ConfigFieldDefinition { Column = c, ChineseName = name, Control = ConfigControl.Enum, Editable = true, Choices = CombatEditorFields.CooldownChoices }; break;
                case "effectBudgetMode": f = CombatEditorFields.Enum(c, name, "fixed_weapon", "武器固定", "fixed_unarmed", "身体固定", "damage_budget", "伤害预算", "prototype_restore", "恢复原型", "status_only", "状态", "fixed_output", "固定输出"); break;
                case "areaCenterKind": f = CombatEditorFields.Enum(c, name, "caster", "施法者", "target_cell", "目标格"); f.AllowEmpty = true; break;
                case "areaShapeKind": f = CombatEditorFields.Enum(c, name, "circle", "圆", "line", "线", "fan", "扇", "forward_pair", "纵向两格", "adjacent_pair", "相邻两格"); f.AllowEmpty = true; break;
                case "areaFacing": f = CombatEditorFields.Enum(c, name, "east", "东", "north_east", "东北", "north_west", "西北", "west", "西", "south_west", "西南", "south_east", "东南"); f.AllowEmpty = true; break;
                case "areaEffectBlockers": f = CombatEditorFields.Enum(c, name, "none", "无", "directed_edge", "有向边"); f.AllowEmpty = true; break;
                case "areaAllowedFactions": f = SetField(c, name, "ally", "友方", "enemy", "敌方", "self", "自身"); break;
                case "areaAllowedStates": f = SetField(c, name, "alive", "存活", "corpse", "尸体"); break;
                case "statusAttributeId": f = CombatEditorFields.Enum(c, name, "physical_attack", "物攻", "soul_attack", "魂攻", "physical_defense", "物防", "soul_defense", "魂防"); f.AllowEmpty = true; break;
                case "statusRefreshPolicy": f = CombatEditorFields.Enum(c, name, "replace_stronger", "替换更强"); f.AllowEmpty = true; break;
                case "cannotBlock": case "cannotDodge": case "penetratingShield": case "isDomain": case "isBloodline": case "controlUsesMindContest":
                    f = new ConfigFieldDefinition { Column = c, ChineseName = name, Control = ConfigControl.Boolean, Editable = true, AllowEmpty = attack }; break;
                default:
                    bool decimalValue = c.EndsWith("Multiplier", StringComparison.Ordinal) || c == "defensePenetration" || c == "stunChance";
                    string unit = decimalValue ? c == "stunChance" ? "百分点" : c == "defensePenetration" ? "穿透值" : "倍" :
                        c.EndsWith("Permille", StringComparison.Ordinal) ? "千分比" : c.EndsWith("Ticks", StringComparison.Ordinal) ? "游戏刻" :
                        c.EndsWith("Range", StringComparison.Ordinal) || c.StartsWith("area", StringComparison.Ordinal) ? "空间单位" : "点";
                    f = CombatEditorFields.Number(c, name, unit, !decimalValue, attack);
                    if (c.EndsWith("Units", StringComparison.Ordinal)) { f.Scale = 1000m; f.Unit = "行动力"; }
                    if (c == "statusDurationOwnTurnEnds") f.Unit = "自身回合结束次数";
                    if (c == "areaFanHalfAngleSteps") f.Unit = "角度档";
                    break;
            }
            if (attack && new[] { "attackProfileId", "displayNameKey", "profileKind", "effectType", "resourceKind", "resourceCost", "actionPointCostUnits", "minimumNetCostUnits", "ctPenalty", "minCastRange", "maxCastRange", "targetingMode", "ruleSetVersion", "cooldownClass", "effectBudgetMode", "attackSchemaVersion" }.Contains(c)) f.AllowEmpty = false;
            if (c == "ruleSetVersion") f.ReservedDelimiters = "";
            if (c == "reuseCooldownTicks") f.EmptyMeaning = "未指定：使用层级公式；零：显式无冷却";
            if (c == "defensePenetration") f.EmptyMeaning = "未指定按零计算，保留与显式零的区别";
            return f;
        }
        private static ConfigFieldDefinition RootField(string c, string name, bool empty)
        {
            var element = CombatEditorFields.Enum("root", "灵根选择", Elements.SelectMany((e, i) => new[] { e, ElementNames[i] == "无" ? "无要求" : ElementNames[i] })
                .Concat(Elements.Take(12).SelectMany((e, i) => new[] { e + "_root", ElementNames[i] + "灵根声明" })).ToArray());
            var field = CombatEditorFields.List(c, name, element, "none");
            field.AllowEmpty = empty; field.Help = "组内任一灵根满足要求；无要求与不适用空值不同。保留灵根声明原身份与顺序。";
            return field;
        }
        private static ConfigFieldDefinition SetField(string c, string name, params string[] pairs) => new ConfigFieldDefinition
        { Column = c, ChineseName = name, Control = ConfigControl.Group, Editable = true, AllowEmpty = true,
            Children = Enumerable.Range(0, pairs.Length / 2).Select(i => new ConfigFieldDefinition
            { Column = pairs[i * 2], ChineseName = pairs[i * 2 + 1], Control = ConfigControl.Boolean, Editable = true }).ToArray(),
            Help = "复选各项；仅编辑后按协议顺序输出，未改CSV词元不重排。" };
    }

    internal sealed class CombatAbilityCodec : CombatScalarCodec
    {
        internal CombatAbilityCodec(string id) : base(id) { }
        public override ConfigValue DecodeRow(ConfigCsvRow row)
        {
            var value = base.DecodeRow(row);
            foreach (string c in new[] { "elementReq", "elementRequirementId" }.Where(row.Cells.ContainsKey))
            {
                string raw = row[c];
                var parts = raw == "" ? Array.Empty<string>() : raw.StartsWith("element_", StringComparison.Ordinal) ? raw.Substring(8).Split(new[] { "_or_" }, StringSplitOptions.None) : new[] { raw };
                value = value.With(c, ConfigValue.List(parts.Select(ConfigValue.Scalar), raw, raw != ""));
            }
            foreach (string c in new[] { "areaAllowedFactions", "areaAllowedStates" }.Where(row.Cells.ContainsKey))
            {
                var allowed = c == "areaAllowedFactions" ? new[] { "ally", "enemy", "self" } : new[] { "alive", "corpse" };
                var selected = row[c].Split(',');
                value = value.With(c, ConfigValue.Group(allowed.ToDictionary(k => k, k => ConfigValue.Scalar(selected.Contains(k) ? "1" : "0")), row[c]));
            }
            return value;
        }
        public override IReadOnlyDictionary<string, string> EncodeRow(ConfigValue row)
        {
            var values = base.EncodeRow(row).ToDictionary(v => v.Key, v => v.Value);
            foreach (string c in new[] { "elementReq", "elementRequirementId" }.Where(row.Members.ContainsKey))
                values[c] = row.At(c).Items.Count == 0 ? "" : "element_" + string.Join("_or_", row.At(c).Items.Select(v => v.RawToken));
            foreach (string c in new[] { "areaAllowedFactions", "areaAllowedStates" }.Where(row.Members.ContainsKey))
            {
                var value = row.At(c);
                var original = value.RawToken.Split(',');
                values[c] = value.Members.All(v => original.Contains(v.Key) == (v.Value.RawToken == "1")) ? value.RawToken :
                    string.Join(",", value.Members.Where(v => v.Value.RawToken == "1").Select(v => v.Key).OrderBy(k => k, StringComparer.Ordinal));
            }
            return values;
        }
        public override IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot)
        {
            var issues = base.ValidateDraft(snapshot).ToList();
            if (issues.Count != 0 || !snapshot.Tables.TryGetValue(Id, out var table)) return issues;
            if (Id == "AttackProfiles")
            {
                CombatEditorFields.Check(issues, Id, "", "", "攻击档案领域校验失败", () =>
                {
                    var profiles = Project(snapshot);
                    foreach (var profile in profiles) UnityEngine.Object.DestroyImmediate(profile);
                });
                return issues;
            }
            foreach (var row in table.Rows)
                CombatEditorFields.Check(issues, Id, row.Key, "", "术能字段校验失败", () =>
                {
                    var values = EncodeRow(row.Value);
                    int min = int.Parse(values["minRange"], CultureInfo.InvariantCulture), max = int.Parse(values["maxRange"], CultureInfo.InvariantCulture);
                    if (min < 0 || max < min) throw new InvalidDataException("combat_editor_cast_range_invalid");
                    foreach (string c in new[] { "mpCost", "cooldownTicks", "healAmount" })
                        if (int.Parse(values[c], CultureInfo.InvariantCulture) < 0) throw new InvalidDataException("combat_editor_nonnegative_required");
                    foreach (var field in table.Definition.Fields.Where(f => f.Control == ConfigControl.Decimal))
                    {
                        float invariant = float.Parse(values[field.Column], CultureInfo.InvariantCulture);
                        if (float.Parse(values[field.Column], CultureInfo.CurrentCulture) != invariant) throw new InvalidDataException("combat_editor_import_culture_mismatch");
                        if (invariant < 0 || field.Column == "stunChance" && invariant > 100) throw new InvalidDataException("combat_editor_legacy_number_range");
                    }
                    if (CombatElementFacts.ResolveElement(values["element"]) == "" && values["element"] != "element_none") throw new InvalidDataException("combat_editor_element_unknown");
                });
            return issues;
        }
        internal static AttackProfileData[] Project(ConfigDraftSnapshot snapshot)
        {
            var table = snapshot.Tables["AttackProfiles"];
            // Use source tokens for unchanged fields; a bad set must not be normalized away.
            if (table.Issues.Count != 0) throw new InvalidDataException("attack_profile_header_not_exact");
            if (!CombatContentImporter.TryBuildAttackProfileProjection(new CombatAbilityCodec("AttackProfiles").Lines(table),
                CombatEditorFields.Rows("Language", snapshot).Select(r => r["id"]).ToArray(), out var profiles, out var reason, CombatEditorFields.Rules(snapshot)))
                throw new InvalidDataException(reason);
            return profiles;
        }
        private string[] Lines(ConfigDraftTableSnapshot table) => new[] { CombatEditorFields.CsvLine(table.Definition.ExactColumns) }.Concat(table.Rows.Select(row =>
        {
            var encoded = EncodeRow(row.Value);
            var original = DecodeRow(table.SourceRows[row.Key]);
            return CombatEditorFields.CsvLine(table.Definition.ExactColumns.Select(c => row.Value.At(c).Equals(original.At(c)) ? table.SourceRows[row.Key][c] : encoded[c]));
        })).ToArray();
    }
}
