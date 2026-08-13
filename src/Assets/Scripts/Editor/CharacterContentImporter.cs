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
using TianZhang.World;

namespace TianZhang.Editor
{
    /// <summary>Owns the complete character content import pipeline.</summary>
    public static class CharacterContentImporter
    {
        [MenuItem("天章/内容/导入角色定义")]
        public static void Import() => ImportCharacterDefinitions();

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
                var cols = CsvTableReader.ParseRow(line);
                if (cols.Length >= 2 && !string.IsNullOrEmpty(cols[0]))
                    _lang[cols[0]] = cols[1];
            }
            Debug.Log($"[Importer] Loaded {_lang.Count} language entries");
            return _lang;
        }

        /// <summary>解析文本 ID → 显示文本</summary>
        static string T(string id) => LoadLanguage().TryGetValue(id, out var text) ? text : id;
        [MenuItem("天章/导入角色配置")]
        public static void ImportCharacterDefinitions()
        {
            ImportCharacters();
        }

        public static CharacterData[] ParseCharacterDefinitions(string[] lines, string sourceName)
        {
            var projected = new List<CharacterData>();
            try
            {
                var headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
                var headers = CsvTableReader.FindHeader(lines);
                CsvTableReader.RequireColumns(headers, sourceName,
                    "name", "realmMultiplier", "rootBone", "physique", "spirit", "mind",
                    "reaction", "talent", "blockRate", "blockReduction",
                    "soulShieldRate", "soulShieldReduction", "dodgeRate",
                    "critRate", "critDamage", "hitRateBonus", "gongFaName",
                    "equippedSpells", "equippedSkills");

                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in lines.Skip(headerLineIndex + 1))
                {
                    if (string.IsNullOrWhiteSpace(line) ||
                        line.TrimStart().StartsWith("#") ||
                        line.StartsWith("name,"))
                    {
                        continue;
                    }

                    var columns = CsvTableReader.ParseRow(line);
                    if (columns.Length < headers.Length)
                    {
                        throw new InvalidDataException(
                            $"{sourceName} row has {columns.Length} columns; expected at least {headers.Length}.");
                    }

                    var id = CsvTableReader.GetRequiredValue(headers, columns, "name", sourceName);
                    if (!ids.Add(id))
                        throw new InvalidDataException($"{sourceName} contains duplicate character ID '{id}'.");

                    var asset = ScriptableObject.CreateInstance<CharacterData>();
                    projected.Add(asset);
                    asset.charName = T(id);
                    asset.realmMultiplier = float.Parse(CsvTableReader.GetRequiredValue(headers, columns, "realmMultiplier", sourceName));
                    asset.rootBone = int.Parse(CsvTableReader.GetRequiredValue(headers, columns, "rootBone", sourceName));
                    asset.physique = int.Parse(CsvTableReader.GetRequiredValue(headers, columns, "physique", sourceName));
                    asset.spirit = int.Parse(CsvTableReader.GetRequiredValue(headers, columns, "spirit", sourceName));
                    asset.mind = int.Parse(CsvTableReader.GetRequiredValue(headers, columns, "mind", sourceName));
                    asset.reaction = int.Parse(CsvTableReader.GetRequiredValue(headers, columns, "reaction", sourceName));
                    asset.talent = int.Parse(CsvTableReader.GetRequiredValue(headers, columns, "talent", sourceName));
                    asset.blockRate = float.Parse(CsvTableReader.GetRequiredValue(headers, columns, "blockRate", sourceName));
                    asset.blockReduction = float.Parse(CsvTableReader.GetRequiredValue(headers, columns, "blockReduction", sourceName));
                    asset.soulShieldRate = float.Parse(CsvTableReader.GetRequiredValue(headers, columns, "soulShieldRate", sourceName));
                    asset.soulShieldReduction = float.Parse(CsvTableReader.GetRequiredValue(headers, columns, "soulShieldReduction", sourceName));
                    asset.dodgeRate = float.Parse(CsvTableReader.GetRequiredValue(headers, columns, "dodgeRate", sourceName));
                    asset.critRate = float.Parse(CsvTableReader.GetRequiredValue(headers, columns, "critRate", sourceName));
                    asset.critDamage = float.Parse(CsvTableReader.GetRequiredValue(headers, columns, "critDamage", sourceName));
                    asset.hitRateBonus = float.Parse(CsvTableReader.GetRequiredValue(headers, columns, "hitRateBonus", sourceName));
                    var gongFaId = CsvTableReader.GetValueOrDefault(headers, columns, "gongFaName", "");
                    asset.gongFaName = gongFaId.Length > 0 ? T(gongFaId) : string.Empty;
                    var equippedSpells = CsvTableReader.GetValueOrDefault(headers, columns, "equippedSpells", "");
                    asset.equippedSpells = equippedSpells.Length > 0 ? equippedSpells.Split('|') : Array.Empty<string>();
                    var equippedSkills = CsvTableReader.GetValueOrDefault(headers, columns, "equippedSkills", "");
                    asset.equippedSkills = equippedSkills.Length > 0 ? equippedSkills.Split('|') : Array.Empty<string>();
                }

                return projected.ToArray();
            }
            catch
            {
                foreach (var asset in projected)
                    UnityEngine.Object.DestroyImmediate(asset);
                throw;
            }
        }

        private static void ImportCharacters()
        {
            _lang = null;
            string path = "Assets/DataConfig/Characters.csv";
            if (!File.Exists(path)) { Debug.LogError($"找不到 {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headerLineIndex = CsvTableReader.FindHeaderIndex(lines);
            var headers = CsvTableReader.FindHeader(lines);
            var assets = ParseCharacterDefinitions(lines, path);
            var ids = lines
                .Skip(headerLineIndex + 1)
                .Where(line => !string.IsNullOrWhiteSpace(line) &&
                               !line.TrimStart().StartsWith("#") &&
                               !line.StartsWith("name,"))
                .Select(CsvTableReader.ParseRow)
                .Select(columns => CsvTableReader.GetRequiredValue(headers, columns, "name", path))
                .ToArray();

            try
            {
                for (var index = 0; index < assets.Length; index++)
                {
                    var asset = assets[index];
                    string assetPath =
                        $"Assets/Data/Characters/Char_{AssetCommitter.SanitizeName(ids[index])}.asset";
                    AssetCommitter.EnsureDirectory(assetPath);
                    var existing = AssetDatabase.LoadAssetAtPath<CharacterData>(assetPath);
                    if (existing == null)
                    {
                        AssetDatabase.CreateAsset(asset, assetPath);
                    }
                    else
                    {
                        EditorUtility.CopySerialized(asset, existing);
                        EditorUtility.SetDirty(existing);
                    }
                    Debug.Log($"  角色: {asset.charName} ← {assetPath}");
                }
            }
            finally
            {
                foreach (var asset in assets)
                {
                    if (asset != null && !AssetDatabase.Contains(asset))
                        UnityEngine.Object.DestroyImmediate(asset);
                }
            }
        }

    }
}
