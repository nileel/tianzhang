using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Content;
using TianZhang.Entity;

namespace TianZhang.Character
{
    /// <summary>Character-module composition root. It does not own scene, UI, CTB or Cultivation state.</summary>
    public sealed class CharacterRuntimeProfile
    {
        public CharacterRuntimeProfile(CharacterIdentity identity, CharacterAttributes attributes, CharacterResources resources,
            AbilityLoadout abilityLoadout, CharacterProgressionRef progression, CharacterCombatModifiers combatModifiers,
            string mainEquipmentBasicAttackProfileId, string unarmedBasicAttackProfileId, string appearanceProfileId,
            string visibleRootId = null, string visibleRootGrade = null, string visibleRootElement = null,
            string mainEquipmentItemId = null, string mainEquipmentInstanceId = null, CombatRuleSetData growthRules = null,
            string unarmedAttackFamilyId = null)
        {
            Identity = identity ?? throw new System.ArgumentNullException(nameof(identity));
            Attributes = attributes ?? throw new System.ArgumentNullException(nameof(attributes));
            Resources = resources ?? throw new System.ArgumentNullException(nameof(resources));
            AbilityLoadout = abilityLoadout ?? throw new System.ArgumentNullException(nameof(abilityLoadout));
            Progression = progression ?? throw new System.ArgumentNullException(nameof(progression));
            CombatModifiers = combatModifiers ?? throw new System.ArgumentNullException(nameof(combatModifiers));
            MainEquipmentItemId = mainEquipmentItemId;
            MainEquipmentInstanceId = mainEquipmentInstanceId;
            MainEquipmentBasicAttackProfileId = mainEquipmentBasicAttackProfileId;
            UnarmedAttackFamilyId = unarmedAttackFamilyId;
            AppearanceProfileId = string.IsNullOrWhiteSpace(appearanceProfileId)
                ? throw new System.ArgumentException("Appearance profile ID is required.", nameof(appearanceProfileId))
                : appearanceProfileId;
            VisibleRootId = visibleRootId;
            VisibleRootGrade = visibleRootGrade;
            VisibleRootElement = visibleRootElement;
            this.growthRules = growthRules ?? throw new ArgumentNullException(nameof(growthRules));
            RestoreProgression(progression.Capture());
        }
        private readonly CombatRuleSetData growthRules;

        public CharacterIdentity Identity { get; } public CharacterAttributes Attributes { get; }
        public CharacterResources Resources { get; } public AbilityLoadout AbilityLoadout { get; }
        public CharacterProgressionRef Progression { get; } public CharacterCombatModifiers CombatModifiers { get; private set; }
        public string MainEquipmentItemId { get; private set; }
        public string MainEquipmentInstanceId { get; private set; }
        public string MainEquipmentBasicAttackProfileId { get; private set; }
        public string UnarmedBasicAttackProfileId { get; private set; }
        public string UnarmedAttackFamilyId { get; private set; }
        public string AppearanceProfileId { get; private set; }
        public string VisibleRootId { get; private set; }
        public string VisibleRootGrade { get; private set; }
        public string VisibleRootElement { get; private set; }

        public bool TryPrepareEquipmentChange(ContentCatalogData catalog, string slotId, string itemId,
            string instanceId, bool instanceOwned, out System.Action commit, out string reason)
        {
            commit = null;
            if (!ValidateEquipment(catalog, slotId, itemId, instanceId, instanceOwned, out ItemData item, out reason)) return false;
            if (MainEquipmentItemId == itemId && MainEquipmentInstanceId == instanceId)
            { reason = "equipment_no_change"; return false; }
            string profileId = item.equipmentBasicAttackProfileId;
            commit = () =>
            {
                MainEquipmentItemId = itemId;
                MainEquipmentInstanceId = instanceId;
                MainEquipmentBasicAttackProfileId = profileId;
            };
            return true;
        }

        public bool ValidateEquipmentState(ContentCatalogData catalog, bool instanceOwned)
        {
            if (string.IsNullOrEmpty(MainEquipmentItemId) && string.IsNullOrEmpty(MainEquipmentInstanceId))
                return string.IsNullOrEmpty(MainEquipmentBasicAttackProfileId);
            return
                ValidateEquipment(catalog, "main_hand", MainEquipmentItemId, MainEquipmentInstanceId, instanceOwned, out ItemData item, out _) &&
                MainEquipmentBasicAttackProfileId == item.equipmentBasicAttackProfileId;
        }

        private bool ValidateEquipment(ContentCatalogData catalog, string slotId, string itemId, string instanceId,
            bool instanceOwned, out ItemData item, out string reason)
        {
            item = null;
            reason = "equipment_slot_invalid";
            if (slotId != "main_hand") return false;
            reason = "equipment_definition_invalid";
            if (catalog == null || !catalog.TryGetEquipment(itemId, out item)) return false;
            reason = "equipment_instance_invalid";
            if (!string.Equals(instanceId, Identity.CharacterId + ":initial:" + itemId, System.StringComparison.Ordinal)) return false;
            reason = "equipment_not_owned";
            if (!instanceOwned) return false;
            reason = "equipment_qualification_invalid";
            if (ResolveRealmId(Progression.RealmStage) == "realm_fanren") return false;
            reason = null;
            return true;
        }

        public static CharacterRuntimeProfile FromDefinition(string characterId, CharacterData definition, CombatRuleSetData growthRules = null)
        {
            if (definition == null) throw new System.ArgumentNullException(nameof(definition));
            if (!string.IsNullOrEmpty(definition.unarmedBasicAttackProfileId))
                throw new ArgumentException("character_unarmed_binding_obsolete");
            CharacterAttributes attributes = CharacterAttributes.FromDefinition(definition, growthRules);
            CharacterCombatModifiers combatModifiers = CharacterCombatModifiers.FromDefinition(definition);
            var progression = new CharacterProgressionRef(definition.gongFaName, definition.realmStage,
                definition.realmMultiplier > 0f ? definition.realmMultiplier : 1f);
            CharacterDerivedAttributes derived = attributes.Derive(progression.RealmMultiplier, combatModifiers.ToAttributeBonuses());
            var loadout = new AbilityLoadout(definition.availableSpells, definition.availableSkills,
                definition.maxSpellSlots, definition.maxSkillSlots);
            foreach (string spell in definition.equippedSpells ?? new string[0]) loadout.TryEquipSpell(spell);
            foreach (string skill in definition.equippedSkills ?? new string[0]) loadout.TryEquipSkill(skill);
            return new CharacterRuntimeProfile(new CharacterIdentity(characterId, definition.charName), attributes,
                new CharacterResources(derived.MaxHealth, derived.MaxHealth, derived.MaxSpirit, derived.MaxSpirit), loadout, progression,
                combatModifiers,
                definition.mainEquipmentBasicAttackProfileId, definition.unarmedBasicAttackProfileId, AppearanceProfileData.NoneId,
                definition.visibleRootId, definition.visibleRootGrade, definition.visibleRootElement, growthRules: growthRules,
                unarmedAttackFamilyId: definition.unarmedAttackFamilyId);
        }

        public static CharacterRuntimeProfile FromSnapshot(CharacterStateSnapshot snapshot, CombatRuleSetData growthRules = null)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            var profile = new CharacterRuntimeProfile(
                new CharacterIdentity(snapshot.Identity.CharacterId, snapshot.Identity.DisplayName),
                new CharacterAttributes(
                    snapshot.Attributes.RootBone,
                    snapshot.Attributes.Physique,
                    snapshot.Attributes.Spirit,
                    snapshot.Attributes.Mind,
                    snapshot.Attributes.Reaction,
                    snapshot.Attributes.Talent,
                    snapshot.Attributes.Fortune,
                    snapshot.Attributes.ActionPointMaximumUnits,
                    snapshot.Attributes.MovementEfficiencyMilliUnits,
                    snapshot.Attributes.ActionEconomyInputsPresent),
                new CharacterResources(
                    snapshot.Resources.MaximumHealth,
                    snapshot.Resources.CurrentHealth,
                    snapshot.Resources.MaximumSpirit,
                    snapshot.Resources.CurrentSpirit),
                new AbilityLoadout(
                    snapshot.AbilityLoadout.KnownSpells,
                    snapshot.AbilityLoadout.KnownSkills,
                    snapshot.AbilityLoadout.SpellSlots,
                    snapshot.AbilityLoadout.SkillSlots),
                new CharacterProgressionRef(
                    snapshot.Progression.GongFaId,
                    snapshot.Progression.RealmStage,
                    snapshot.Progression.RealmMultiplier, snapshot.Progression.MovementGrowthInputsPresent, snapshot.Progression.CopySources()),
                snapshot.CombatModifiers,
                snapshot.MainEquipmentBasicAttackProfileId,
                snapshot.UnarmedBasicAttackProfileId,
                snapshot.AppearanceProfileId,
                snapshot.VisibleRootId, snapshot.VisibleRootGrade, snapshot.VisibleRootElement,
                snapshot.MainEquipmentItemId, snapshot.MainEquipmentInstanceId, growthRules, snapshot.UnarmedAttackFamilyId);
            profile.Restore(snapshot);
            return profile;
        }

        public CharacterStateSnapshot Capture()
        {
            return new CharacterStateSnapshot(
                Identity.Capture(), Attributes.Capture(), Resources.Capture(), AbilityLoadout.Capture(), Progression.Capture(),
                CombatModifiers,
                MainEquipmentBasicAttackProfileId, UnarmedBasicAttackProfileId, AppearanceProfileId,
                VisibleRootId, VisibleRootGrade, VisibleRootElement, MainEquipmentItemId, MainEquipmentInstanceId, UnarmedAttackFamilyId);
        }
        public void Restore(CharacterStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            RequireRestorable(snapshot);
            ProjectActionEconomy(growthRules, snapshot.Progression, out int ap, out int movement);
            RequirePlayerScope(snapshot.Identity.CharacterId, snapshot.Progression.RealmStage);
            string unarmed = ResolveUnarmedAttack(growthRules, snapshot.UnarmedAttackFamilyId, snapshot.Progression.RealmStage);
            if (snapshot.Identity.CharacterId == "player" && snapshot.UnarmedAttackFamilyId != "body_human" ||
                snapshot.UnarmedBasicAttackProfileId != unarmed)
                throw new ArgumentException("character_unarmed_binding_invalid");
            Identity.Restore(snapshot.Identity); Attributes.Restore(snapshot.Attributes); Resources.Restore(snapshot.Resources);
            AbilityLoadout.Restore(snapshot.AbilityLoadout); Progression.Restore(snapshot.Progression);
            Attributes.SetActionEconomy(ap, movement);
            CombatModifiers = snapshot.CombatModifiers;
            MainEquipmentItemId = snapshot.MainEquipmentItemId;
            MainEquipmentInstanceId = snapshot.MainEquipmentInstanceId;
            MainEquipmentBasicAttackProfileId = snapshot.MainEquipmentBasicAttackProfileId;
            UnarmedAttackFamilyId = snapshot.UnarmedAttackFamilyId;
            UnarmedBasicAttackProfileId = unarmed;
            AppearanceProfileId = snapshot.AppearanceProfileId;
            VisibleRootId = snapshot.VisibleRootId;
            VisibleRootGrade = snapshot.VisibleRootGrade;
            VisibleRootElement = snapshot.VisibleRootElement;
        }

        /// <summary>
        /// Full validation of the incoming aggregate before any live field is written, so a failed restore
        /// keeps identity, attributes, resources, loadout, progression and the long-term inputs untouched.
        /// </summary>
        private static void RequireRestorable(CharacterStateSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Identity.CharacterId) ||
                string.IsNullOrWhiteSpace(snapshot.Identity.DisplayName))
            {
                throw new System.ArgumentException("Character identity is invalid.", nameof(snapshot));
            }
            if (snapshot.Resources.MaximumHealth < 0 || snapshot.Resources.CurrentHealth < 0 ||
                snapshot.Resources.CurrentHealth > snapshot.Resources.MaximumHealth ||
                snapshot.Resources.MaximumSpirit < 0 || snapshot.Resources.CurrentSpirit < 0 ||
                snapshot.Resources.CurrentSpirit > snapshot.Resources.MaximumSpirit)
            {
                throw new System.ArgumentException("Character resources are invalid.", nameof(snapshot));
            }
            if (snapshot.AbilityLoadout.SpellSlots < 0 || snapshot.AbilityLoadout.SkillSlots < 0 ||
                !IsEquippedSubsetOfKnown(snapshot.AbilityLoadout.EquippedSpells, snapshot.AbilityLoadout.KnownSpells,
                    snapshot.AbilityLoadout.SpellSlots) ||
                !IsEquippedSubsetOfKnown(snapshot.AbilityLoadout.EquippedSkills, snapshot.AbilityLoadout.KnownSkills,
                    snapshot.AbilityLoadout.SkillSlots))
            {
                throw new System.ArgumentException("Character ability loadout is invalid.", nameof(snapshot));
            }
            if (!(snapshot.Progression.RealmMultiplier > 0f) ||
                float.IsNaN(snapshot.Progression.RealmMultiplier) ||
                float.IsInfinity(snapshot.Progression.RealmMultiplier))
            {
                throw new System.ArgumentException("Character progression is invalid.", nameof(snapshot));
            }
            CharacterAttributes.RequireActionPointMaximumUnits(snapshot.Attributes.ActionPointMaximumUnits);
            CharacterAttributes.RequireMovementEfficiencyMilliUnits(snapshot.Attributes.MovementEfficiencyMilliUnits);
            CharacterAttributes.RequireActionEconomyInputsPresent(snapshot.Attributes.ActionEconomyInputsPresent);
        }

        public void RestoreProgression(CharacterProgressionSnapshot progression)
        {
            ProjectActionEconomy(growthRules, progression, out int ap, out int movement);
            RequirePlayerScope(Identity.CharacterId, progression.RealmStage);
            string unarmed = ResolveUnarmedAttack(growthRules, UnarmedAttackFamilyId, progression.RealmStage);
            if (Identity.CharacterId == "player" && UnarmedAttackFamilyId != "body_human")
                throw new ArgumentException("character_unarmed_family_invalid");
            Progression.Restore(progression);
            Attributes.SetActionEconomy(ap, movement);
            UnarmedBasicAttackProfileId = unarmed;
        }

        public static string ResolveUnarmedAttack(CombatRuleSetData rules, string family, string stage)
        {
            if (rules == null || !rules.TryValidateIdentity(out _) || rules.unarmedRows.Length != 5)
                throw new ArgumentException("character_unarmed_mapping_invalid");
            string realm = ResolveRealmId(stage);
            var row = rules.unarmedRows.SingleOrDefault(value => value.familyId == family && value.realmId == realm);
            if (row == null) throw new ArgumentException("character_unarmed_source_invalid");
            return row.profileId;
        }

        private static readonly string[] RealmIds = { "realm_fanren", "realm_lianqi", "realm_zhuji", "realm_jindan", "realm_yuanying", "realm_huashen" };
        private static readonly string[] RealmNames = { "凡人", "练气", "筑基", "金丹", "元婴", "化神" };
        public static string ResolveRealmId(string stage)
        {
            for (int i = 0; i < RealmIds.Length; i++)
                if (stage == RealmIds[i] || stage == RealmNames[i]) return RealmIds[i];
            if (stage == "筑基开府" || stage == "开府" || stage == "筑基圆满") return RealmIds[2];
            if (stage == "金丹初结" || stage == "金丹温养" || stage == "金丹圆满") return RealmIds[3];
            string[] grades = { "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            if (grades.Any(grade => stage == "练气" + grade + "品")) return RealmIds[1];
            throw new ArgumentException("character_realm_unknown:" + stage);
        }
        public static string ResolveRealmStage(string stage) => RealmNames[Array.IndexOf(RealmIds, ResolveRealmId(stage))];

        private static int NodeLimit(string stage, int realmCount)
        {
            if (stage == "筑基开府" || stage == "开府" || stage == "金丹温养") return 2;
            if (stage == "金丹初结") return 1;
            string[] grades = { "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            for (int i = 0; i < grades.Length; i++) if (stage == "练气" + grades[i] + "品") return i + 1;
            return realmCount;
        }
        private static void RequirePlayerScope(string id, string stage)
        {
            if (id == "player") RequirePlayerRealm(stage);
        }
        public static void RequirePlayerRealm(string stage)
        {
            if (Array.IndexOf(RealmIds, ResolveRealmId(stage)) > 3)
                throw new ArgumentException("character_realm_reserved");
        }

        // Chapter identities and learning gates are the closed L2 set, not movePoints from old growth assets.
        private static readonly (string Id, string Name, int Minimum, string[] Chapters)[] GongFaFacts =
        {
            ("gongfa_jileiposhan", "疾雷破山经", 2, new[] { "", "", "淬锋", "惊雷", "破山", "万钧" }),
            ("gongfa_hanhongguangda", "含弘光大典", 1, new[] { "", "含章", "厚德", "载物", "含弘", "光大|品物" }),
            ("gongfa_baiwuqingyun", "白屋青云录", 0, new[] { "白屋", "寒窗", "破茧", "腾霄", "经纬|青云", "" }),
            ("gongfa_hunyuantongchen", "混元同尘典", 2, new[] { "", "", "", "同尘", "和光", "万乘" }),
            ("gongfa_baoyuanshouyi", "抱元守一经", 1, new[] { "", "守一篇", "抱元篇", "致虚篇", "守静篇", "归根篇" }),
            ("gongfa_wanwubuqian", "万物不迁法", 2, new[] { "", "", "", "各住", "各性", "" }),
            ("gongfa_qiushuiyouxin", "秋水游心经", 0, new[] { "", "观水篇", "知海篇", "齐物篇", "游心篇", "" }),
            ("gongfa_yunzhuandurenjing", "云篆度人经", 2, new[] { "", "", "灵宝篇", "度人篇", "云篆篇", "天书篇" }),
            ("gongfa_buzhenzixu", "不真自虚法", 2, new[] { "", "", "", "", "不真", "中道" }),
            ("gongfa_xinwuxingyou", "心无性有法", 2, new[] { "", "", "虚照", "无相", "", "" }),
            ("gongfa_jiuxiaoleijie", "九霄雷劫录", 2, new[] { "", "", "引雷", "蓄霆", "九霄", "摧折" }),
            ("gongfa_kuxingjiandian", "苦行剑典", 1, new[] { "", "磨剑", "断陇", "无倦", "血锋", "孤剑" }),
            ("gongfa_leicnicuiti", "雷池淬体功", 0, new[] { "感雷", "引雷", "淬体", "雷肌", "不坏", "" }),
            ("gongfa_nanhuaxuangan", "南华玄感录", 1, new[] { "", "无心", "即色", "游玄", "逍遥", "" }),
            ("gongfa_shengmozhengfa", "绳墨正法录", 1, new[] { "", "法度", "绳墨", "正法", "严纪|不移", "" }),
            ("gongfa_nanhuachanyandian", "南华阐衍典", 2, new[] { "", "", "", "独化", "性分", "玄冥" }),
            ("gongfa_taiyishancangjing", "太易山藏经", 2, new[] { "", "", "", "周易", "连山", "归藏" }),
            ("gongfa_xuanpindaocang", "玄牝道藏", 2, new[] { "", "", "", "", "", "道可道" }),
            ("gongfa_kongwuborejing", "空无般若经", 2, new[] { "", "", "", "空观", "无相", "真如" }),
            ("gongfa_wanfadulujing", "万法都箓经", 2, new[] { "", "", "", "都箓", "摄法", "正统" }),
            ("gongfa_zhengyimengweilu", "正一盟威箓", 2, new[] { "", "", "", "", "盟威", "血誓" }),
        };
        private static int GongFaIndex(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            int index = Array.FindIndex(GongFaFacts, fact => fact.Id == id || fact.Name == id);
            if (index < 0) throw new ArgumentException("character_gongfa_unknown:" + id);
            return index;
        }
        public static void ProjectActionEconomy(CombatRuleSetData rules, CharacterProgressionSnapshot progression,
            out int actionPoints, out int movement)
        {
            if (rules == null || !rules.TryValidateIdentity(out _)) throw new ArgumentException("combat_rule_set_invalid");
            if (progression == null || !progression.MovementGrowthInputsPresent || progression.MovementGrowthSources == null ||
                !(progression.RealmMultiplier > 0f) || float.IsInfinity(progression.RealmMultiplier))
                throw new ArgumentException("character_growth_inputs_invalid");
            var rows = new Dictionary<string, CombatRuleSetData.RealmRow>(StringComparer.Ordinal);
            foreach (var row in rules.realms)
            {
                int order = Array.IndexOf(RealmIds, row.realmId);
                int count = order == 0 ? 0 : order == 1 ? 9 : order == 2 || order == 3 ? 3 : 4;
                if (order < 0 || row.realmOrder != order || row.usageScope != (order < 4 ? "player" : "reserved") ||
                    !rows.TryAdd(row.realmId, row) || row.actionPointMaximumUnits <= 0 || row.baseMovementEfficiencyMilliUnits < 0 ||
                    row.legacyBaseMovementEfficiencyMilliUnits <= 0 || row.movementNodeWeightDenominator <= 0 ||
                    row.movementNodeWeightNumerators.Length != count || row.movementNodeWeightNumerators.Any(n => n <= 0) ||
                    (count > 0 && row.movementNodeWeightNumerators.Sum(n => (long)n) != row.movementNodeWeightDenominator))
                    throw new ArgumentException("character_growth_configuration_invalid");
            }
            var globals = rules.economy.Where(row => row.recordKind == "global" && row.definitionRef == "action_economy").ToArray();
            if (globals.Length != 1 || !globals[0].movementBudgetNumerator.present || !globals[0].movementBudgetDenominator.present ||
                globals[0].movementBudgetNumerator.value <= 0 || globals[0].movementBudgetDenominator.value <= 0)
                throw new ArgumentException("character_growth_configuration_invalid");
            var current = rows[ResolveRealmId(progression.RealmStage)];
            int active = GongFaIndex(progression.GongFaId);
            string instance = null;
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            var legacyBonus = new GrowthRatio(0, 1);
            foreach (var source in progression.MovementGrowthSources)
            {
                if (source == null || string.IsNullOrWhiteSpace(source.SourceInstanceId) || source.CompletedNodeIndices == null ||
                    source.CompletedNodeIndices.Count == 0 || !rows.TryGetValue(source.RealmId ?? "", out var row))
                    throw new ArgumentException("character_growth_source_invalid");
                int index = GongFaIndex(source.GongFaId);
                if (index < 0 || index != active || row.realmOrder < GongFaFacts[index].Minimum || row.realmOrder > current.realmOrder ||
                    !GongFaFacts[index].Chapters[row.realmOrder].Split('|').Contains(source.ChapterId, StringComparer.Ordinal) ||
                    string.IsNullOrEmpty(source.ChapterId) || (instance != null && instance != source.SourceInstanceId))
                    throw new ArgumentException("character_growth_source_qualification_invalid");
                instance = source.SourceInstanceId;
                int previous = 0;
                foreach (int node in source.CompletedNodeIndices)
                {
                    if (node <= previous || node > row.movementNodeWeightNumerators.Length ||
                        row.realmOrder == current.realmOrder && node > NodeLimit(progression.RealmStage, row.movementNodeWeightNumerators.Length))
                        throw new ArgumentException("character_growth_nodes_invalid");
                    previous = node;
                    string key = instance + ":" + source.RealmId + ":" + node;
                    string fact = GongFaFacts[index].Id + ":" + source.ChapterId;
                    if (seen.TryGetValue(key, out string old))
                    { if (old != fact) throw new ArgumentException("character_growth_node_conflict"); continue; }
                    seen.Add(key, fact);
                    if (GongFaFacts[index].Id == "gongfa_kuxingjiandian")
                        legacyBonus += new GrowthRatio(row.legacyBaseMovementEfficiencyMilliUnits, 1) *
                            new GrowthRatio(row.movementNodeWeightNumerators[node - 1], row.movementNodeWeightDenominator) *
                            new GrowthRatio(globals[0].movementBudgetNumerator.value, globals[0].movementBudgetDenominator.value);
                }
            }
            var effective = new GrowthRatio(current.baseMovementEfficiencyMilliUnits, 1) + legacyBonus *
                new GrowthRatio(current.baseMovementEfficiencyMilliUnits, current.legacyBaseMovementEfficiencyMilliUnits);
            actionPoints = current.actionPointMaximumUnits;
            movement = checked((int)(effective.Numerator / effective.Denominator));
        }
        private readonly struct GrowthRatio
        {
            public GrowthRatio(long numerator, long denominator)
            {
                if (numerator < 0 || denominator <= 0) throw new ArgumentException("character_growth_ratio_invalid");
                long gcd = Gcd(numerator, denominator);
                Numerator = numerator / gcd; Denominator = denominator / gcd;
            }
            public long Numerator { get; }
            public long Denominator { get; }
            private static long Gcd(long a, long b) { while (b != 0) { long next = a % b; a = b; b = next; } return a; }
            public static GrowthRatio operator +(GrowthRatio a, GrowthRatio b)
            {
                long gcd = Gcd(a.Denominator, b.Denominator);
                return new GrowthRatio(checked(a.Numerator * (b.Denominator / gcd) + b.Numerator * (a.Denominator / gcd)),
                    checked(a.Denominator * (b.Denominator / gcd)));
            }
            public static GrowthRatio operator *(GrowthRatio a, GrowthRatio b)
            {
                long left = Gcd(a.Numerator, b.Denominator), right = Gcd(b.Numerator, a.Denominator);
                return new GrowthRatio(checked((a.Numerator / left) * (b.Numerator / right)),
                    checked((a.Denominator / right) * (b.Denominator / left)));
            }
        }

        private static bool IsEquippedSubsetOfKnown(string[] equipped, string[] known, int slots)
        {
            equipped = equipped ?? new string[0];
            known = known ?? new string[0];
            if (equipped.Length > slots) return false;
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            var knownSet = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (string value in known) if (!string.IsNullOrWhiteSpace(value)) knownSet.Add(value);
            foreach (string value in equipped)
            {
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(value) || !knownSet.Contains(value)) return false;
            }
            return true;
        }
    }
}
