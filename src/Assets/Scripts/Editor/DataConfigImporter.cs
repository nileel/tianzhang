using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using TianZhang.Entity;
using TianZhang.Combat;
using TianZhang.Cultivation;

namespace TianZhang.Editor
{
    /// <summary>
    /// CSV 配置导入工具（v2 — 支持文本 ID 解析）
    /// 从 Assets/DataConfig/*.csv 读取数据，通过 Language.csv 解析文本 ID
    /// 生成 ScriptableObject .asset 文件
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
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DataConfigImporter] 全部配置导入完成");
        }

        [MenuItem("天章/导入功法配置")]
        static void ImportGongFa()
        {
            _lang = null;
            string path = "Assets/DataConfig/GongFa.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = ParseCSV(line);
                if (cols.Length < 14) continue;

                var asset = ScriptableObject.CreateInstance<GongFaGrowthData>();
                asset.gongFaName = T(cols[0]);
                asset.affiliation = T(cols[1]);
                asset.grade = T(cols[2]);
                asset.elementMain = T(cols[3]);
                asset.elementSub = T(cols[4]);
                asset.starRootBone = int.Parse(cols[5]);
                asset.starPhysique = int.Parse(cols[6]);
                asset.starSpirit = int.Parse(cols[7]);
                asset.starMind = int.Parse(cols[8]);
                asset.starReaction = int.Parse(cols[9]);
                asset.starTalent = int.Parse(cols[10]);
                asset.starFortune = int.Parse(cols[11]);

                // 境界成长表
                var growthList = new List<GongFaGrowthData.SubGrowthPerRealm>();
                foreach (var realmEntry in cols[12].Split('|'))
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

                // 篇章加成
                if (cols.Length > 13)
                {
                    var chapterList = new List<GongFaGrowthData.ChapterBonus>();
                    foreach (var chEntry in cols[13].Split('|'))
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
                string assetPath = $"Assets/Data/GongFa/GongFa_{SanitizeName(cols[0])}.asset";
                EnsureDirectory(assetPath);
                AssetDatabase.CreateAsset(asset, assetPath);
                Debug.Log($"  功法: {asset.gongFaName} ← {assetPath}");
            }
        }

        [MenuItem("天章/导入术法配置")]
        static void ImportSpells()
        {
            _lang = null;
            string path = "Assets/DataConfig/Spells.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = ParseCSV(line);
                if (cols.Length < 14) continue;

                var asset = ScriptableObject.CreateInstance<SpellData>();
                asset.spellName = T(cols[0]);
                asset.type = (SpellType)int.Parse(cols[1]);
                asset.minRange = int.Parse(cols[2]);
                asset.maxRange = int.Parse(cols[3]);
                asset.mpCost = int.Parse(cols[4]);
                asset.cooldownTicks = int.Parse(cols[5]);
                asset.damageMultiplier = float.Parse(cols[6]);
                asset.healAmount = int.Parse(cols[7]);
                asset.cannotBlock = cols[8] == "1";
                asset.cannotDodge = cols[9] == "1";
                asset.penetratingShield = cols[10] == "1";
                asset.stunChance = float.Parse(cols[11]);
                // 五行属性（从 elementReq 解析为标准五行）
                if (cols.Length > 13)
                    asset.element = TianZhang.Combat.DamageCalculator.ResolveElement(cols[13]);
                // realmReq, elementReq, affiliation stored for reference (already in name)

                string assetPath = $"Assets/Data/Spells/Spell_{SanitizeName(cols[0])}.asset";
                EnsureDirectory(assetPath);
                AssetDatabase.CreateAsset(asset, assetPath);
                Debug.Log($"  术法: {asset.spellName} ← {assetPath}");
            }
        }

        [MenuItem("天章/导入神通配置")]
        static void ImportSkills()
        {
            _lang = null;
            string path = "Assets/DataConfig/Skills.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = ParseCSV(line);
                if (cols.Length < 16) continue;

                var asset = ScriptableObject.CreateInstance<DivineSkillData>();
                asset.skillName = T(cols[0]);
                asset.type = (SpellType)int.Parse(cols[1]);
                asset.minRange = int.Parse(cols[2]);
                asset.maxRange = int.Parse(cols[3]);
                asset.mpCost = int.Parse(cols[4]);
                asset.cooldownTicks = int.Parse(cols[5]);
                asset.damageMultiplier = float.Parse(cols[6]);
                asset.healAmount = int.Parse(cols[7]);
                asset.cannotBlock = cols[8] == "1";
                asset.cannotDodge = cols[9] == "1";
                asset.penetratingShield = cols[10] == "1";
                asset.stunChance = float.Parse(cols[11]);
                // 五行属性（神通继承功法属性，优先查CSV显式字段）
                if (cols.Length > 16)
                    asset.element = TianZhang.Combat.DamageCalculator.ResolveElement(cols[16]);
                asset.isDomain = cols[12] == "1";
                asset.isBloodline = cols[13] == "1";
                asset.specialEffectDesc = T(cols[14]);

                string assetPath = $"Assets/Data/Skills/Skill_{SanitizeName(cols[0])}.asset";
                EnsureDirectory(assetPath);
                AssetDatabase.CreateAsset(asset, assetPath);
                Debug.Log($"  神通: {asset.skillName} ← {assetPath}");
            }
        }

        [MenuItem("天章/导入角色配置")]
        static void ImportCharacters()
        {
            _lang = null;
            string path = "Assets/DataConfig/Characters.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = ParseCSV(line);
                if (cols.Length < 19) continue;

                var asset = ScriptableObject.CreateInstance<CharacterData>();
                asset.charName = T(cols[0]);
                asset.realmMultiplier = float.Parse(cols[1]);
                asset.rootBone = int.Parse(cols[2]);
                asset.physique = int.Parse(cols[3]);
                asset.spirit = int.Parse(cols[4]);
                asset.mind = int.Parse(cols[5]);
                asset.reaction = int.Parse(cols[6]);
                asset.talent = int.Parse(cols[7]);
                asset.blockRate = float.Parse(cols[8]);
                asset.blockReduction = float.Parse(cols[9]);
                asset.soulShieldRate = float.Parse(cols[10]);
                asset.soulShieldReduction = float.Parse(cols[11]);
                asset.dodgeRate = float.Parse(cols[12]);
                asset.critRate = float.Parse(cols[13]);
                asset.critDamage = float.Parse(cols[14]);
                asset.hitRateBonus = float.Parse(cols[15]);
                asset.gongFaName = T(cols[16]);
                asset.equippedSpells = cols[17].Length > 0 ? cols[17].Split('|') : new string[0];
                asset.equippedSkills = cols[18].Length > 0 ? cols[18].Split('|') : new string[0];

                string assetPath = $"Assets/Data/Characters/Char_{SanitizeName(cols[0])}.asset";
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

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = ParseCSV(line);
                if (cols.Length < 22) continue;

                var asset = ScriptableObject.CreateInstance<CharacterData>();
                asset.charName = T(cols[0]);
                asset.realmMultiplier = float.Parse(cols[4]);
                asset.rootBone = int.Parse(cols[5]);
                asset.physique = int.Parse(cols[6]);
                asset.spirit = int.Parse(cols[7]);
                asset.mind = int.Parse(cols[8]);
                asset.reaction = int.Parse(cols[9]);
                asset.talent = int.Parse(cols[10]);
                asset.blockRate = float.Parse(cols[11]);
                asset.blockReduction = float.Parse(cols[12]);
                asset.soulShieldRate = float.Parse(cols[13]);
                asset.soulShieldReduction = float.Parse(cols[14]);
                asset.dodgeRate = float.Parse(cols[15]);
                asset.critRate = float.Parse(cols[16]);
                asset.critDamage = float.Parse(cols[17]);
                asset.hitRateBonus = float.Parse(cols[18]);
                asset.equippedSpells = cols[19].Length > 0 ? cols[19].Split('|') : new string[0];
                asset.equippedSkills = new string[0];

                string assetPath = $"Assets/Data/Characters/Char_Enemy_{SanitizeName(cols[0])}.asset";
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
