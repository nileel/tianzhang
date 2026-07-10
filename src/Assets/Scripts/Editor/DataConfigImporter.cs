using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using TianZhang.Entity;
using TianZhang.Combat;
using TianZhang.Cultivation;
using TianZhang.Game.CharacterCreation;

namespace TianZhang.Editor
{
    /// <summary>
    /// CSV 配置导入工具（v3 — 按列名/表头解析）
    /// ⚠️ 已修改/未审核；修改方：Claude Code
    /// 从 Assets/DataConfig/*.csv 读取数据，通过 Language.csv 解析文本 ID
    /// 生成 ScriptableObject .asset 文件
    /// v3 变更：所有导入器不再依赖硬编码列序，改为按表头列名读取；Characters/Enemies 同步升级。
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
            ImportCharacterCreationPointBuy();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DataConfigImporter] 全部配置导入完成");
        }

        [MenuItem("天章/导入角色创建点购配置")]
        static void ImportCharacterCreationPointBuy()
        {
            string path = "Assets/DataConfig/CharacterCreationPointBuy.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }

            var lines = File.ReadAllLines(path);
            var headers = FindHeader(lines);
            RequireColumns(headers, path,
                "configId", "purchasePointLimit", "minValue", "baseValue", "maxValue",
                "fromValue", "toValue", "costPerLevel");

            var rows = lines
                .Skip(1)
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                .Select(ParseCSV)
                .Where(cols => cols.Length >= headers.Length
                    && GetRequiredColumnValue(headers, cols, "configId", path) == "default")
                .ToArray();

            if (rows.Length == 0)
            {
                Debug.LogError("[DataConfigImporter] CharacterCreationPointBuy.csv missing default config rows.");
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
        static void ImportGongFa()
        {
            _lang = null;
            string path = "Assets/DataConfig/GongFa.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headers = FindHeader(lines);
            RequireColumns(headers, path,
                "name", "affiliation", "grade", "elementMain", "elementSub",
                "starRootBone", "starPhysique", "starSpirit", "starMind",
                "starReaction", "starTalent", "starFortune", "growth");

            foreach (var line in lines.Skip(1))
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
                asset.contentScope = GetColumnValueOrDefault(headers, cols, "contentScope", "player");
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
        static void ImportSpells()
        {
            _lang = null;
            string path = "Assets/DataConfig/Spells.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headers = FindHeader(lines);
            RequireColumns(headers, path,
                "name", "type", "minRange", "maxRange", "mpCost",
                "cooldownTicks", "damageMultiplier", "healAmount",
                "cannotBlock", "cannotDodge", "penetratingShield", "stunChance",
                "element");

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = ParseCSV(line);
                if (cols.Length < headers.Length)
                {
                    Debug.LogWarning($"[Importer] {path} row has {cols.Length} columns, expected >= {headers.Length}; skipping");
                    continue;
                }

                var asset = ScriptableObject.CreateInstance<SpellData>();
                asset.spellName = T(GetRequiredColumnValue(headers, cols, "name", path));
                asset.type = (SpellType)int.Parse(GetRequiredColumnValue(headers, cols, "type", path));
                asset.minRange = int.Parse(GetRequiredColumnValue(headers, cols, "minRange", path));
                asset.maxRange = int.Parse(GetRequiredColumnValue(headers, cols, "maxRange", path));
                asset.contentScope = GetColumnValueOrDefault(headers, cols, "contentScope", "player");
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

                string assetPath = $"Assets/Data/Spells/Spell_{SanitizeName(GetRequiredColumnValue(headers, cols, "name", path))}.asset";
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
            var headers = FindHeader(lines);
            RequireColumns(headers, path,
                "name", "type", "minRange", "maxRange", "mpCost",
                "cooldownTicks", "damageMultiplier", "healAmount",
                "cannotBlock", "cannotDodge", "penetratingShield", "stunChance",
                "isDomain", "isBloodline", "specialEffectDesc",
                "element");

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.StartsWith("name,")) continue;
                var cols = ParseCSV(line);
                if (cols.Length < headers.Length)
                {
                    Debug.LogWarning($"[Importer] {path} row has {cols.Length} columns, expected >= {headers.Length}; skipping");
                    continue;
                }

                var asset = ScriptableObject.CreateInstance<DivineSkillData>();
                asset.skillName = T(GetRequiredColumnValue(headers, cols, "name", path));
                asset.type = (SpellType)int.Parse(GetRequiredColumnValue(headers, cols, "type", path));
                asset.minRange = int.Parse(GetRequiredColumnValue(headers, cols, "minRange", path));
                asset.maxRange = int.Parse(GetRequiredColumnValue(headers, cols, "maxRange", path));
                asset.contentScope = GetColumnValueOrDefault(headers, cols, "contentScope", "player");
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

                string assetPath = $"Assets/Data/Skills/Skill_{SanitizeName(GetRequiredColumnValue(headers, cols, "name", path))}.asset";
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
            var headers = FindHeader(lines);
            RequireColumns(headers, path,
                "name", "realmMultiplier", "rootBone", "physique", "spirit", "mind",
                "reaction", "talent", "blockRate", "blockReduction",
                "soulShieldRate", "soulShieldReduction", "dodgeRate",
                "critRate", "critDamage", "hitRateBonus", "gongFaName",
                "equippedSpells", "equippedSkills");

            foreach (var line in lines.Skip(1))
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
        static void ImportEnemies()
        {
            _lang = null;
            string path = "Assets/DataConfig/Enemies.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headers = FindHeader(lines);
            RequireColumns(headers, path,
                "name", "realmMultiplier", "rootBone", "physique", "spirit", "mind",
                "reaction", "talent", "blockRate", "blockReduction",
                "soulShieldRate", "soulShieldReduction", "dodgeRate",
                "critRate", "critDamage", "hitRateBonus", "equippedSpells");

            foreach (var line in lines.Skip(1))
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
                var enemySpellsRaw = GetColumnValueOrDefault(headers, cols, "equippedSpells", "");
                asset.equippedSpells = enemySpellsRaw.Length > 0 ? enemySpellsRaw.Split('|') : new string[0];
                asset.equippedSkills = new string[0];

                string assetPath = $"Assets/Data/Characters/Char_Enemy_{SanitizeName(GetRequiredColumnValue(headers, cols, "name", path))}.asset";
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

        static string[] FindHeader(string[] lines)
        {
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                var cols = ParseCSV(line);
                if (cols.Length > 0 && cols[0] == "name")
                    return cols;
            }
            return Array.Empty<string>();
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

        static void RequireColumns(string[] headers, string sourceName, params string[] columnNames)
        {
            foreach (var columnName in columnNames)
            {
                if (FindColumnIndex(headers, columnName) < 0)
                    throw new InvalidDataException($"{sourceName} missing required column '{columnName}'.");
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
