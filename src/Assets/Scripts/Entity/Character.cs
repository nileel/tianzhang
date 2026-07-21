using UnityEngine;
using System.Collections.Generic;
using TianZhang.Core;

namespace TianZhang.Entity
{
    /// <summary>
    /// 运行时角色实体
    /// 属性公式与 BattleSim 一致（v3.4 次线性HP + 二级重映射）
    /// </summary>
    public class Character
    {
        // ---- 一级属性 ----
        public int RootBone;
        public int Physique;
        public int Spirit;
        public int Mind;
        public int Reaction;
        public int Talent;
        public int Fortune;

        // ---- 二级属性 ----
        public int MaxHP;
        public int CurrentHP;
        public int MaxMP;
        public int CurrentMP;
        public int PhysAtk;
        public int MagAtk;
        public int PhysDef;
        public int MagDef;

        // ---- 二级概率 ----
        public float BlockRate;
        public float BlockReduction;
        public float SoulShieldRate;
        public float SoulShieldReduction;
        public float DodgeRate;
        public float CritRate;
        public float CritDamage;
        public float HitRateBonus;

        // ---- 战斗状态 ----
        public int Facing;           // 0-5 朝向
        public HexCoord Position;
        public int MovePoints = 3;   // 每回合可移动格数（从反应推算）
        public bool IsGuarding;      // 防御姿态
        public bool IsAlive = true;

        // ---- CTB 引用 ----
        public CTBEngine.CTBUnit CTBUnit;

        // ---- 术法/神通冷却 ----
        public int[] SpellCooldowns;   // 剩余冷却刻数
        public string[] EquippedSpellIds;   // 当前装备的术法ID
        public string[] EquippedSkillIds;   // 当前装备的神通ID
        public int[] SkillCooldowns;


        // ---- 术法槽位 ----
        public int MaxSpellSlots;      // 术法槽位上限
        public int MaxSkillSlots;      // 神通槽位上限
        public string[] AvailableSpells; // 术法库（已学会的全部术法）
        public string[] AvailableSkills; // 神通库
        public int CombatSwapsUsed;     // 本场战斗已换法次数
        public const int MaxCombatSwaps = 2; // 每场最多临阵换法次数
        public string[] DevelopedMansions; // 已主修紫府府位
        public string RealmStage;
        public string TargetPosition;
        public string PositionOccupationState;
        public string DanXiangId;
        public string DanPivotRole;
        public string[] MansionBindings;
        public string DanArtifactForm;
        public string LegacyDanJiType;
        public string VisibleRootId;
        public string VisibleRootElement;
        public string HiddenRootState;

        // ---- 印记状态 ----
        public int ShouyiStacks;     // 守一印记层数（抱元守一经）
        public int FudanStacks;      // 符胆印记层数（云篆度人经）
        public int LeijieStacks;     // 雷劫印记层数（九霄雷劫录）

        // ---- 功法 ----
        public string GongFaName;
        public float RealmMultiplier = 1f;

        // ---- 姓名 ----
        public string Name;

        public static Character FromData(CharacterData data, HexCoord startPos)
        {
            var c = new Character
            {
                Name = data.charName,
                GongFaName = data.gongFaName,
                RootBone = data.rootBone,
                Physique = data.physique,
                Spirit = data.spirit,
                Mind = data.mind,
                Reaction = data.reaction,
                Talent = data.talent,
                Fortune = data.fortune,
                Position = startPos,
                Facing = 0,
            };

            // 境界倍率（从 data 读取，或默认值）
            float realm = data.realmMultiplier > 0 ? data.realmMultiplier : 1f;
            c.RealmMultiplier = realm;
            c.RealmStage = data.realmStage;
            c.m_Realm = !string.IsNullOrWhiteSpace(data.realmStage)
                ? NormalizeRealmFromStage(data.realmStage)
                : RealmNameFromMultiplier(realm);
            c.DevelopedMansions = data.developedMansions != null ? (string[])data.developedMansions.Clone() : new string[0];
            c.TargetPosition = data.targetPosition;
            c.PositionOccupationState = data.positionOccupationState;
            c.DanXiangId = data.danXiangId;
            c.DanPivotRole = data.danPivotRole;
            c.MansionBindings = data.mansionBindings != null ? (string[])data.mansionBindings.Clone() : new string[0];
            c.DanArtifactForm = data.danArtifactForm;
            c.LegacyDanJiType = data.legacyDanJiType;
            c.VisibleRootId = data.visibleRootId;
            c.VisibleRootElement = data.visibleRootElement;
            c.HiddenRootState = data.hiddenRootState;

            // ---- 次线性HP公式（v3.4：根骨^0.75 × 境界倍率 × 基础值）----
            float hpBase = Mathf.Pow(c.RootBone, 0.75f) * realm * 80f;
            c.MaxHP = Mathf.RoundToInt(hpBase + data.hpBonus);
            c.CurrentHP = c.MaxHP;

            // MP
            float mpBase = c.Spirit * realm * 15f;
            c.MaxMP = Mathf.RoundToInt(mpBase + data.mpBonus);
            c.CurrentMP = c.MaxMP;

            // 攻击力
            c.PhysAtk = Mathf.RoundToInt(c.RootBone * realm * 5f + data.physAtkBonus);
            c.MagAtk = Mathf.RoundToInt(c.Spirit * realm * 5f + data.magAtkBonus);

            // 防御力
            c.PhysDef = Mathf.RoundToInt(c.Physique * realm * 3.5f + data.physDefBonus);
            c.MagDef = Mathf.RoundToInt(c.Mind * realm * 3.5f + data.magDefBonus);

            // 反应 → 移动力
            c.MovePoints = Mathf.Clamp(Mathf.RoundToInt(c.Reaction / 20f), 2, 8);

            // 二级概率
            c.BlockRate = data.blockRate;
            c.BlockReduction = data.blockReduction;
            c.SoulShieldRate = data.soulShieldRate;
            c.SoulShieldReduction = data.soulShieldReduction;
            c.DodgeRate = data.dodgeRate;
            c.CritRate = data.critRate;
            c.CritDamage = data.critDamage;
            c.HitRateBonus = data.hitRateBonus;

            // 术法/神通槽位上限（须在冷却数组之前赋值）
            var (spellSlots, skillSlots) = CalculateSlotLimits(realm, c.DevelopedMansions);
            c.MaxSpellSlots = data.maxSpellSlots > 0 ? data.maxSpellSlots : spellSlots;
            c.MaxSkillSlots = data.maxSkillSlots > 0 ? data.maxSkillSlots : skillSlots;
            c.EquippedSpellIds = CloneAndTrim(data.equippedSpells, c.MaxSpellSlots, "术法", c.Name);
            c.EquippedSkillIds = CloneAndTrim(data.equippedSkills, c.MaxSkillSlots, "神通", c.Name);
            // 术法/神通冷却初始化
            c.SpellCooldowns = new int[Mathf.Max(c.EquippedSpellIds.Length, c.MaxSpellSlots)];
            c.SkillCooldowns = new int[Mathf.Max(c.EquippedSkillIds.Length, c.MaxSkillSlots)];
            c.AvailableSpells = data.availableSpells ?? new string[0];
            c.AvailableSkills = data.availableSkills ?? new string[0];
            c.CombatSwapsUsed = 0;
            // 回归检查：冷却数组尺寸不应小于槽位上限
            if (c.SpellCooldowns.Length < c.MaxSpellSlots || c.SkillCooldowns.Length < c.MaxSkillSlots)
                Debug.LogError($"Character.FromData [{c.Name}]: 冷却数组尺寸不足 SpellCooldowns={c.SpellCooldowns.Length}/{c.MaxSpellSlots} SkillCooldowns={c.SkillCooldowns.Length}/{c.MaxSkillSlots}");

            return c;
        }

        /// <summary>受击朝向修正（正面0/侧面1-2/背面3 → 命中率/伤害修正）</summary>
        public float GetFacingHitModifier(Character attacker)
        {
            int dirToAttacker = Position.DirectionTo(attacker.Position);
            if (dirToAttacker < 0) return 1f; // 不相邻，无朝向
            int diff = HexCoord.DirectionDiff(Facing, dirToAttacker);
            return diff switch
            {
                0 => 0.85f,  // 正面：命中率降低
                1 or 2 => 1.0f, // 侧面：正常
                3 => 1.15f,  // 背面：命中率提高
                _ => 1f,
            };
        }

        public float GetFacingDamageModifier(Character attacker)
        {
            int dirToAttacker = Position.DirectionTo(attacker.Position);
            if (dirToAttacker < 0) return 1f;
            int diff = HexCoord.DirectionDiff(Facing, dirToAttacker);
            return diff switch
            {
                0 => 1f,     // 正面正常伤害
                1 or 2 => 1.15f,  // 侧面+15%
                3 => 1.3f,   // 背面+30%
                _ => 1f,
            };
        }

        /// <summary>转动朝向（面向目标）</summary>
        public void FaceTarget(HexCoord target)
        {
            int dir = Position.DirectionTo(target);
            if (dir >= 0) Facing = dir;
        }

        /// <summary>消耗MP</summary>
        public bool ConsumeMP(int amount)
        {
            if (CurrentMP < amount) return false;
            CurrentMP -= amount;
            return true;
        }

        public void TakeDamage(int amount)
        {
            if (amount > 0 && GongFaName == "九霄雷劫录")
                LeijieStacks = Mathf.Min(LeijieStacks + 1, MaxLeijie());

            CurrentHP -= amount;
            if (CurrentHP <= 0)
            {
                CurrentHP = 0;
                IsAlive = false;
            }
        }

        public void Heal(int amount)
        {
            CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        }

        public void RestoreMP(int amount)
        {
            CurrentMP = Mathf.Min(MaxMP, CurrentMP + amount);
        }

        /// <summary>
        /// 将角色领域状态投影为可展示快照；不引用 UI 组件或动作入口。
        /// </summary>
        public CombatantPanelState BuildCombatantPanelState(float ctRatio, string element, string status)
        {
            return new CombatantPanelState(
                Name,
                CurrentHP,
                MaxHP,
                CurrentMP,
                MaxMP,
                ctRatio,
                element,
                status);
        }

        /// <summary>应用功法篇章加成（永久二级属性）</summary>
        public void ApplyGongFaBonuses(Cultivation.GongFaGrowthData gongFa, string realm)
        {
            if (gongFa == null) return;
            var bonus = gongFa.GetCumulativeBonus(realm);
            SoulShieldRate += bonus.soulShieldRate;
            HitRateBonus += bonus.hitRate;
            BlockRate += bonus.blockRate;
            CritRate += bonus.critRate;
            CritDamage += bonus.critDamage;
            DodgeRate += bonus.dodgeRate;
            MagAtk += Mathf.RoundToInt(bonus.magAtkBonus);
            MagDef += Mathf.RoundToInt(bonus.magDefBonus);
        }


        /// <summary>按当前玩家主线推算基础术法槽位；高阶倍率只作为兼容强度，不继续线性加槽。</summary>
        public static int GetDefaultSpellSlots(float realmMultiplier)
        {
            if (realmMultiplier >= 3f) return 5;    // 筑基基准；金丹/高阶无通用加槽
            if (realmMultiplier >= 1.5f) return 4;  // 练气
            return 0;                                // 凡人
        }

        /// <summary>按当前玩家主线推算基础神通槽位；高阶倍率只作为兼容强度，不继续线性加槽。</summary>
        public static int GetDefaultSkillSlots(float realmMultiplier)
        {
            if (realmMultiplier >= 3f) return 2;    // 筑基基准；金丹/高阶无通用加槽
            if (realmMultiplier >= 1.5f) return 1;  // 练气
            return 0;                                // 凡人
        }

        public void RecalculateSlots()
        {
            var (spellSlots, skillSlots) = CalculateSlotLimits(GetEffectiveRealmMultiplier(), DevelopedMansions);
            MaxSpellSlots = spellSlots;
            MaxSkillSlots = skillSlots;
            EnsureCooldownArraySize();
        }

        public void EnsureCooldownArraySize()
        {
            if (SpellCooldowns == null || SpellCooldowns.Length < MaxSpellSlots)
                System.Array.Resize(ref SpellCooldowns, MaxSpellSlots);
            if (SkillCooldowns == null || SkillCooldowns.Length < MaxSkillSlots)
                System.Array.Resize(ref SkillCooldowns, MaxSkillSlots);
        }

        private static (int spellSlots, int skillSlots) CalculateSlotLimits(float realmMultiplier, string[] developedMansions)
        {
            int spellSlots = GetDefaultSpellSlots(realmMultiplier);
            int skillSlots = GetDefaultSkillSlots(realmMultiplier);

            if (developedMansions == null || developedMansions.Length == 0)
                return (spellSlots, skillSlots);

            var seen = new HashSet<string>();
            int spellBonus = 0;
            int skillBonus = 0;
            foreach (var mansion in developedMansions)
            {
                if (string.IsNullOrWhiteSpace(mansion) || !seen.Add(mansion))
                    continue;

                if ((mansion == "命府" || mansion == "魂府" || mansion == "气府") && spellBonus < 3)
                    spellBonus++;
                else if ((mansion == "识府" || mansion == "运府") && skillBonus < 2)
                    skillBonus++;
            }

            return (spellSlots + spellBonus, skillSlots + skillBonus);
        }

        private static string[] CloneAndTrim(string[] source, int maxSlots, string slotType, string characterName)
        {
            if (source == null || source.Length == 0)
                return new string[0];
            if (maxSlots <= 0)
                return new string[0];
            if (source.Length <= maxSlots)
                return (string[])source.Clone();

            Debug.LogWarning($"Character.FromData [{characterName}]: 装备{slotType}数 {source.Length} 超过槽位 {maxSlots}，已截断。");
            var trimmed = new string[maxSlots];
            System.Array.Copy(source, trimmed, maxSlots);
            return trimmed;
        }

        /// <summary>检查是否可以装备更多术法</summary>
        public bool CanEquipMoreSpells() =>
            (EquippedSpellIds?.Length ?? 0) < MaxSpellSlots;

        /// <summary>装备术法（非战斗中），返回成功与否</summary>
        public bool EquipSpell(string spellId)
        {
            var list = EquippedSpellIds != null ? new List<string>(EquippedSpellIds) : new List<string>();
            if (list.Count >= MaxSpellSlots) return false;
            if (list.Contains(spellId)) return false;
            list.Add(spellId);
            EquippedSpellIds = list.ToArray();
            return true;
        }

        /// <summary>卸下术法，返回成功与否</summary>
        public bool UnequipSpell(string spellId)
        {
            var list = EquippedSpellIds != null ? new List<string>(EquippedSpellIds) : new List<string>();
            if (!list.Remove(spellId)) return false;
            EquippedSpellIds = list.ToArray();
            return true;
        }

        /// <summary>战斗中临阵换法：卸下slotIndex位置的术法，换上newSpellId</summary>
        /// <returns>被卸下的旧术法ID，失败返回null</returns>
        public string SwapSpellInCombat(int slotIndex, string newSpellId)
        {
            if (CombatSwapsUsed >= MaxCombatSwaps) return null;
            if (slotIndex < 0 || slotIndex >= (EquippedSpellIds?.Length ?? 0)) return null;
            if (AvailableSpells == null || System.Array.IndexOf(AvailableSpells, newSpellId) < 0) return null;

            string oldId = EquippedSpellIds[slotIndex];
            if (oldId == newSpellId) return null; // 相同术法，无需换

            // 执行换法
            EquippedSpellIds[slotIndex] = newSpellId;
            CombatSwapsUsed++;

            // 新术法获得双倍冷却惩罚
            if (SpellCooldowns != null && slotIndex < SpellCooldowns.Length)
                SpellCooldowns[slotIndex] = 60; // 双倍CD(基础30×2)

            return oldId;
        }

        /// <summary>获取当前可换入的术法列表（库存中未装备的）</summary>
        public string[] GetSwappableSpells()
        {
            if (AvailableSpells == null || AvailableSpells.Length == 0) return new string[0];
            var equipped = new System.Collections.Generic.HashSet<string>(EquippedSpellIds ?? new string[0]);
            var swappable = new System.Collections.Generic.List<string>();
            foreach (var spell in AvailableSpells)
                if (!equipped.Contains(spell))
                    swappable.Add(spell);
            return swappable.ToArray();
        }

        /// <summary>守一印记最大层数（按境界）</summary>
        public int MaxShouyi()
        {
            return Mathf.RoundToInt(GetEffectiveRealmMultiplier()) switch
            {
                >= 6 => 5,   // 金丹+
                >= 3 => 4,   // 筑基
                >= 2 => 3,   // 练气
                _ => 3
            };
        }

        /// <summary>符胆印记最大层数（按境界）</summary>
        public int MaxFudan()
        {
            return Mathf.RoundToInt(GetEffectiveRealmMultiplier()) switch
            {
                >= 6 => 5,   // 金丹+
                >= 3 => 3,   // 筑基
                _ => 5
            };
        }

        /// <summary>雷劫印记最大层数（按 BattleSim 当前境界口径）。</summary>
        public int MaxLeijie()
        {
            return Mathf.RoundToInt(GetEffectiveRealmMultiplier()) switch
            {
                >= 6 => 5,   // 金丹+
                >= 3 => 3,   // 筑基
                _ => 3
            };
        }

        public float LeijieDamageBonusPerStack()
        {
            return Mathf.RoundToInt(GetEffectiveRealmMultiplier()) switch
            {
                >= 24 => 0.30f, // 化神
                >= 12 => 0.22f, // 元婴
                >= 6 => 0.18f,  // 金丹
                >= 3 => 0.15f,  // 筑基
                _ => 0.15f
            };
        }

        public int XuanganMindStrengthBonus()
        {
            if (GongFaName != "南华玄感录")
                return 0;

            return Mathf.RoundToInt(GetEffectiveRealmMultiplier()) switch
            {
                >= 12 => 12, // 元婴
                >= 6 => 8,   // 金丹
                >= 3 => 5,   // 筑基
                >= 2 => 3,   // 练气
                _ => 3
            };
        }

        public float HanhongDefenseMultiplier(bool physical)
        {
            if (GongFaName != "含弘光大典")
                return 1f;

            float multiplier = 1f + ZaiwuAllDefenseBonus();
            if (physical)
                multiplier *= 1f + HanhongPhysicalDefenseBonus();
            return multiplier;
        }

        private float HanhongPhysicalDefenseBonus()
        {
            float realm = GetEffectiveRealmMultiplier();
            if (realm >= 24f) return 0.30f; // 光大
            if (realm >= 12f) return 0.25f; // 含弘
            if (realm >= 6f) return 0.20f;  // 载物
            if (realm >= 3f) return 0.15f;  // 厚德
            if (realm >= 1.5f) return 0.10f; // 含章
            return 0f;
        }

        private float ZaiwuAllDefenseBonus()
        {
            if (MaxHP <= 0 || CurrentHP >= MaxHP)
                return 0f;

            float realm = GetEffectiveRealmMultiplier();
            float cap = realm >= 24f ? 0.40f :
                        realm >= 6f ? 0.30f :
                        realm >= 3f ? 0.20f :
                        0f;
            if (cap <= 0f)
                return 0f;

            float missingHpRate = Mathf.Clamp01((MaxHP - CurrentHP) / (float)MaxHP);
            float bonus = Mathf.Floor(missingHpRate * 10f) * 0.02f;
            return Mathf.Min(bonus, cap);
        }

        private string m_Realm;

        public void SetRealm(string realm)
        {
            m_Realm = realm;
            RealmMultiplier = Cultivation.CultivationEngine.GetRealmMultiplier(realm);
            RecalculateSlots();
        }

        public string GetRealm() => m_Realm;

        private float GetEffectiveRealmMultiplier()
        {
            if (!string.IsNullOrEmpty(m_Realm))
                return Cultivation.CultivationEngine.GetRealmMultiplier(m_Realm);
            return RealmMultiplier > 0f ? RealmMultiplier : 1f;
        }

        private static string RealmNameFromMultiplier(float realmMultiplier)
        {
            if (realmMultiplier >= 24f) return "化神";
            if (realmMultiplier >= 12f) return "元婴";
            if (realmMultiplier >= 6f) return "金丹";
            if (realmMultiplier >= 3f) return "筑基";
            if (realmMultiplier >= 1.5f) return "练气";
            return "凡人";
        }

        private static string NormalizeRealmFromStage(string realmStage)
        {
            if (realmStage.Contains("化神")) return "化神";
            if (realmStage.Contains("元婴")) return "元婴";
            if (realmStage.Contains("金丹")) return "金丹";
            if (realmStage.Contains("筑基") || realmStage.Contains("紫府")) return "筑基";
            if (realmStage.Contains("练气")) return "练气";
            return "凡人";
        }

        public override string ToString() =>
            $"{Name} HP={CurrentHP}/{MaxHP} MP={CurrentMP}/{MaxMP} " +
            $"PAtk={PhysAtk} MAtk={MagAtk} PDef={PhysDef} MDef={MagDef} " +
            $"Pos={Position} Face={Facing}";
    }
}
