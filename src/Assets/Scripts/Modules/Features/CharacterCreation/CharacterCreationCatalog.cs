using System.Linq;

namespace TianZhang.Features.CharacterCreation
{
    public static class CharacterCreationCatalog
    {
        public const int CreationBudget = 10;
        public const int CraftSkillStartingPoints = 3;
        // 新建角色唯一无装备基础攻击档案 ID（生产 AttackProfiles.csv 中 basic_unarmed 行）。
        public const string BasicUnarmedAttackProfileId = "basic_unarmed";

        public static readonly SpiritRootOption[] VisibleSpiritRoots =
        {
            new SpiritRootOption("root_metal_low", "下品金灵根", SpiritRootKind.Basic, "下品", "金", "金", -1, 0.5f, 0.85f, "筑基", "关陇玄域", new[] { "element_metal", "profession_sword", "profession_artifact" }),
            new SpiritRootOption("root_wood_low", "下品木灵根", SpiritRootKind.Basic, "下品", "木", "木", -1, 0.5f, 0.85f, "筑基", "江左天域", new[] { "element_wood", "profession_alchemy" }),
            new SpiritRootOption("root_water_low", "下品水灵根", SpiritRootKind.Basic, "下品", "水", "水", -1, 0.5f, 0.85f, "筑基", "江左天域", new[] { "element_water", "profession_talisman" }),
            new SpiritRootOption("root_fire_low", "下品火灵根", SpiritRootKind.Basic, "下品", "火", "火", -1, 0.5f, 0.85f, "筑基", "太行火域", new[] { "element_fire", "profession_alchemy", "profession_artifact" }),
            new SpiritRootOption("root_earth_low", "下品土灵根", SpiritRootKind.Basic, "下品", "土", "土", -1, 0.5f, 0.85f, "筑基", "关陇玄域", new[] { "element_earth", "profession_array", "profession_body" }),

            new SpiritRootOption("root_metal_middle", "中品金灵根", SpiritRootKind.Basic, "中品", "金", "金", 0, 1.0f, 1.0f, "元婴", "关陇玄域", new[] { "element_metal", "profession_sword", "profession_artifact" }),
            new SpiritRootOption("root_wood_middle", "中品木灵根", SpiritRootKind.Basic, "中品", "木", "木", 0, 1.0f, 1.0f, "元婴", "江左天域", new[] { "element_wood", "profession_alchemy" }),
            new SpiritRootOption("root_water_middle", "中品水灵根", SpiritRootKind.Basic, "中品", "水", "水", 0, 1.0f, 1.0f, "元婴", "江左天域", new[] { "element_water", "profession_talisman" }),
            new SpiritRootOption("root_fire_middle", "中品火灵根", SpiritRootKind.Basic, "中品", "火", "火", 0, 1.0f, 1.0f, "元婴", "太行火域", new[] { "element_fire", "profession_alchemy", "profession_artifact" }),
            new SpiritRootOption("root_earth_middle", "中品土灵根", SpiritRootKind.Basic, "中品", "土", "土", 0, 1.0f, 1.0f, "元婴", "关陇玄域", new[] { "element_earth", "profession_array", "profession_body" }),

            new SpiritRootOption("root_metal_high", "上品金灵根", SpiritRootKind.Basic, "上品", "金", "金", 3, 2.0f, 1.2f, "化神", "关陇玄域", new[] { "element_metal", "profession_sword", "profession_artifact" }),
            new SpiritRootOption("root_wood_high", "上品木灵根", SpiritRootKind.Basic, "上品", "木", "木", 3, 2.0f, 1.2f, "化神", "江左天域", new[] { "element_wood", "profession_alchemy" }),
            new SpiritRootOption("root_water_high", "上品水灵根", SpiritRootKind.Basic, "上品", "水", "水", 3, 2.0f, 1.2f, "化神", "江左天域", new[] { "element_water", "profession_talisman" }),
            new SpiritRootOption("root_fire_high", "上品火灵根", SpiritRootKind.Basic, "上品", "火", "火", 3, 2.0f, 1.2f, "化神", "太行火域", new[] { "element_fire", "profession_alchemy", "profession_artifact" }),
            new SpiritRootOption("root_earth_high", "上品土灵根", SpiritRootKind.Basic, "上品", "土", "土", 3, 2.0f, 1.2f, "化神", "关陇玄域", new[] { "element_earth", "profession_array", "profession_body" }),

            new SpiritRootOption("root_wind_middle", "中品风灵根", SpiritRootKind.Variant, "中品", "风", "木", 2, 1.0f, 1.0f, "元婴", "漠北荒域", new[] { "element_wind", "element_wood", "profession_sword" }),
            new SpiritRootOption("root_thunder_middle", "中品雷灵根", SpiritRootKind.Variant, "中品", "雷", "金", 2, 1.0f, 1.0f, "元婴", "陇西雷域", new[] { "element_thunder", "element_metal", "profession_sword", "profession_artifact" }),
            new SpiritRootOption("root_ice_middle", "中品冰灵根", SpiritRootKind.Variant, "中品", "冰", "水", 2, 1.0f, 1.0f, "元婴", "辽海寒域", new[] { "element_ice", "element_water", "profession_talisman" }),
            new SpiritRootOption("root_dark_middle", "中品暗灵根", SpiritRootKind.Variant, "中品", "暗", "土", 2, 1.0f, 1.0f, "元婴", "蜀川幽域", new[] { "element_dark", "element_earth", "profession_soul" }),
            new SpiritRootOption("root_star_middle", "中品星灵根", SpiritRootKind.Variant, "中品", "星", "火", 2, 1.0f, 1.0f, "元婴", "河西星域", new[] { "element_star", "element_fire", "profession_array" }),
            new SpiritRootOption("root_poison_middle", "中品毒灵根", SpiritRootKind.Variant, "中品", "毒", "木", 2, 1.0f, 1.0f, "元婴", "蜀川幽域", new[] { "element_poison", "element_wood", "profession_poison" }),

            new SpiritRootOption("root_wind_high", "上品风灵根", SpiritRootKind.Variant, "上品", "风", "木", 5, 2.0f, 1.2f, "化神", "漠北荒域", new[] { "element_wind", "element_wood", "profession_sword" }),
            new SpiritRootOption("root_thunder_high", "上品雷灵根", SpiritRootKind.Variant, "上品", "雷", "金", 5, 2.0f, 1.2f, "化神", "陇西雷域", new[] { "element_thunder", "element_metal", "profession_sword", "profession_artifact" }),
            new SpiritRootOption("root_ice_high", "上品冰灵根", SpiritRootKind.Variant, "上品", "冰", "水", 5, 2.0f, 1.2f, "化神", "辽海寒域", new[] { "element_ice", "element_water", "profession_talisman" }),
            new SpiritRootOption("root_dark_high", "上品暗灵根", SpiritRootKind.Variant, "上品", "暗", "土", 5, 2.0f, 1.2f, "化神", "蜀川幽域", new[] { "element_dark", "element_earth", "profession_soul" }),
            new SpiritRootOption("root_star_high", "上品星灵根", SpiritRootKind.Variant, "上品", "星", "火", 5, 2.0f, 1.2f, "化神", "河西星域", new[] { "element_star", "element_fire", "profession_array" }),
            new SpiritRootOption("root_poison_high", "上品毒灵根", SpiritRootKind.Variant, "上品", "毒", "木", 5, 2.0f, 1.2f, "化神", "蜀川幽域", new[] { "element_poison", "element_wood", "profession_poison" }),
        };

        public static readonly SpiritRootOption[] HiddenSpiritRoots =
        {
            new SpiritRootOption("hidden_root_metal_middle", "隐·中品金灵根", SpiritRootKind.Basic, "中品", "金", "金", 0, 1.0f, 1.0f, "元婴", "关陇玄域", new[] { "element_metal", "profession_sword", "profession_artifact" }),
            new SpiritRootOption("hidden_root_wood_middle", "隐·中品木灵根", SpiritRootKind.Basic, "中品", "木", "木", 0, 1.0f, 1.0f, "元婴", "江左天域", new[] { "element_wood", "profession_alchemy" }),
            new SpiritRootOption("hidden_root_water_middle", "隐·中品水灵根", SpiritRootKind.Basic, "中品", "水", "水", 0, 1.0f, 1.0f, "元婴", "江左天域", new[] { "element_water", "profession_talisman" }),
            new SpiritRootOption("hidden_root_fire_middle", "隐·中品火灵根", SpiritRootKind.Basic, "中品", "火", "火", 0, 1.0f, 1.0f, "元婴", "太行火域", new[] { "element_fire", "profession_alchemy", "profession_artifact" }),
            new SpiritRootOption("hidden_root_earth_middle", "隐·中品土灵根", SpiritRootKind.Basic, "中品", "土", "土", 0, 1.0f, 1.0f, "元婴", "关陇玄域", new[] { "element_earth", "profession_array", "profession_body" }),

            new SpiritRootOption("hidden_root_wind_high", "隐·上品风灵根", SpiritRootKind.Variant, "上品", "风", "木", 0, 2.0f, 1.2f, "化神", "漠北荒域", new[] { "element_wind", "element_wood", "profession_sword" }),
            new SpiritRootOption("hidden_root_thunder_high", "隐·上品雷灵根", SpiritRootKind.Variant, "上品", "雷", "金", 0, 2.0f, 1.2f, "化神", "陇西雷域", new[] { "element_thunder", "element_metal", "profession_sword", "profession_artifact" }),
            new SpiritRootOption("hidden_root_ice_high", "隐·上品冰灵根", SpiritRootKind.Variant, "上品", "冰", "水", 0, 2.0f, 1.2f, "化神", "辽海寒域", new[] { "element_ice", "element_water", "profession_talisman" }),
            new SpiritRootOption("hidden_root_dark_high", "隐·上品暗灵根", SpiritRootKind.Variant, "上品", "暗", "土", 0, 2.0f, 1.2f, "化神", "蜀川幽域", new[] { "element_dark", "element_earth", "profession_soul" }),
            new SpiritRootOption("hidden_root_star_high", "隐·上品星灵根", SpiritRootKind.Variant, "上品", "星", "火", 0, 2.0f, 1.2f, "化神", "河西星域", new[] { "element_star", "element_fire", "profession_array" }),
            new SpiritRootOption("hidden_root_poison_high", "隐·上品毒灵根", SpiritRootKind.Variant, "上品", "毒", "木", 0, 2.0f, 1.2f, "化神", "蜀川幽域", new[] { "element_poison", "element_wood", "profession_poison" }),

            new SpiritRootOption("hidden_root_chaos_story", "隐·混沌灵根线索", SpiritRootKind.Special, "上古", "混沌", "", 0, 0.2f, 1.0f, "化神", "中州天域", new[] { "element_chaos", "profession_any", "story_ancient" }),
        };

        public static readonly HiddenRootSeedOption[] HiddenRootSeeds =
        {
            new HiddenRootSeedOption("hidden_ordinary_seed", "隐灵根种子", 2, new[] { "hidden_root_metal_middle", "hidden_root_wood_middle", "hidden_root_water_middle", "hidden_root_fire_middle", "hidden_root_earth_middle" }),
            new HiddenRootSeedOption("hidden_variant_seed", "变异隐根种子", 4, new[] { "hidden_root_wind_high", "hidden_root_thunder_high", "hidden_root_ice_high", "hidden_root_dark_high", "hidden_root_star_high", "hidden_root_poison_high" }),
            new HiddenRootSeedOption("hidden_ancient_seed", "上古隐根种子", 6, new[] { "hidden_root_chaos_story" }),
        };

        public static readonly OriginOption[] Origins =
        {
            new OriginOption("origin_loose", "寒门散修", 0, "jiangzuo_hub"),
            new OriginOption("origin_sect_servant", "宗门杂役", 1, "jiangzuo_hub"),
            new OriginOption("origin_minor_clan", "小族子弟", 2, "guanzhong_hub"),
            new OriginOption("origin_debt_exile", "负债流亡", -2, "jiangzuo_hub"),
        };

        public static readonly FateTagOption[] FateTags =
        {
            new FateTagOption("fate_early_teacher", "早年师承", 3),
            new FateTagOption("fate_market_contact", "坊市人脉", 2),
            new FateTagOption("flaw_meridian_crack", "经脉暗伤", -3),
            new FateTagOption("flaw_old_enemy", "旧怨缠身", -2),
        };

        public static readonly CraftSkillOption[] CraftSkills =
        {
            new CraftSkillOption("craft_alchemy", "炼丹", "魂魄"),
            new CraftSkillOption("craft_talisman", "制符", "神识"),
            new CraftSkillOption("craft_artifact", "炼器", "根骨"),
            new CraftSkillOption("craft_array", "阵法", "神识"),
            new CraftSkillOption("craft_herb", "灵植采药", "气运"),
            new CraftSkillOption("craft_trade", "交涉商贸", "气运"),
        };

        public static CharacterCreationDraft CreateDefaultDraft()
        {
            return new CharacterCreationDraft
            {
                CharacterName = "无名修士",
                VisibleSpiritRootId = "root_water_middle",
                HiddenRootSeedId = "",
                OriginId = "origin_loose",
                Innate = InnateAttributeSet.Balanced(),
                CraftSkills = new System.Collections.Generic.List<CraftSkillAllocation>
                {
                    new CraftSkillAllocation("craft_alchemy", 1),
                    new CraftSkillAllocation("craft_talisman", 1),
                    new CraftSkillAllocation("craft_herb", 1),
                },
            };
        }

        public static SpiritRootOption FindVisibleRoot(string id) => VisibleSpiritRoots.FirstOrDefault(item => item.Id == id);
        public static SpiritRootOption FindHiddenRoot(string id) => HiddenSpiritRoots.FirstOrDefault(item => item.Id == id);
        public static HiddenRootSeedOption FindHiddenRootSeed(string id) => HiddenRootSeeds.FirstOrDefault(item => item.Id == id);
        public static OriginOption FindOrigin(string id) => Origins.FirstOrDefault(item => item.Id == id);
        public static FateTagOption FindFateTag(string id) => FateTags.FirstOrDefault(item => item.Id == id);
        public static CraftSkillOption FindCraftSkill(string id) => CraftSkills.FirstOrDefault(item => item.Id == id);
    }
}
