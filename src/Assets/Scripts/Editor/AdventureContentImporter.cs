using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TianZhang.Content;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Editor
{
    public static class AdventureContentImporter
    {
        public const string CsvPath = "Assets/DataConfig/Adventures.csv";
        public const string OutputDirectory = "Assets/Data/Adventures";
        public const string CatalogPath = "Assets/Data/ContentCatalog/ContentCatalog.asset";

        [MenuItem("天章/内容/导入冒险地图")]
        public static void Import()
        {
            IReadOnlyList<AdventureMapData> maps = Parse(CsvTableReader.ReadRequired(CsvPath), CsvPath);
            var adventureIds = new string[maps.Count];
            for (int i = 0; i < maps.Count; i++) adventureIds[i] = maps[i].adventureId;
            if (!AssetDatabase.IsValidFolder(OutputDirectory))
                AssetDatabase.CreateFolder("Assets/Data", "Adventures");

            foreach (AdventureMapData map in maps)
            {
                string path = OutputDirectory + "/AdventureMap_" + map.adventureId + ".asset";
                AdventureMapData existing = AssetDatabase.LoadAssetAtPath<AdventureMapData>(path);
                if (existing == null)
                {
                    AssetCommitter.Commit(map, path);
                    continue;
                }
                existing.adventureId = map.adventureId;
                existing.displayNameKey = map.displayNameKey;
                existing.contentScope = map.contentScope;
                existing.nodes = map.nodes;
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(map);
            }

            ContentCatalogData catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(CatalogPath);
            if (catalog == null) throw ImportDiagnostics.AtomicCommitRequired(CatalogPath);
            var committed = new AdventureMapData[maps.Count];
            for (int i = 0; i < committed.Length; i++)
                committed[i] = AssetDatabase.LoadAssetAtPath<AdventureMapData>(
                    OutputDirectory + "/AdventureMap_" + adventureIds[i] + ".asset");
            catalog.SetAdventureMaps(committed);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        public static IReadOnlyList<AdventureMapData> Parse(string[] lines, string sourceName)
        {
            if (lines == null || lines.Length < 2)
                throw new InvalidOperationException(sourceName + ": adventure rows are required");
            string[] header = CsvTableReader.ParseRow(lines[0]);
            string[] required = { "adventureId", "displayNameKey", "contentScope", "nodeId", "nodeTypeId", "q", "r", "contentId" };
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < header.Length; i++) columns[header[i]] = i;
            foreach (string name in required)
                if (!columns.ContainsKey(name)) throw new InvalidOperationException(sourceName + ": missing column " + name);

            var maps = new Dictionary<string, AdventureMapData>(StringComparer.Ordinal);
            var nodeIds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var coordinates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var nodes = new Dictionary<string, List<AdventureNodeData>>(StringComparer.Ordinal);
            for (int lineNumber = 1; lineNumber < lines.Length; lineNumber++)
            {
                if (string.IsNullOrWhiteSpace(lines[lineNumber]) || lines[lineNumber].TrimStart().StartsWith("#")) continue;
                string[] row = CsvTableReader.ParseRow(lines[lineNumber]);
                string Get(string name) => columns[name] < row.Length ? row[columns[name]].Trim() : string.Empty;
                string adventureId = Get("adventureId");
                string nodeId = Get("nodeId");
                string nodeTypeId = Get("nodeTypeId");
                if (string.IsNullOrWhiteSpace(adventureId) || string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(nodeTypeId))
                    throw new InvalidOperationException(sourceName + ": stable IDs are required at line " + (lineNumber + 1));
                if (!int.TryParse(Get("q"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int q) ||
                    !int.TryParse(Get("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r))
                    throw new InvalidOperationException(sourceName + ": invalid coordinate at line " + (lineNumber + 1));

                if (!maps.TryGetValue(adventureId, out AdventureMapData map))
                {
                    map = ScriptableObject.CreateInstance<AdventureMapData>();
                    map.adventureId = adventureId;
                    map.displayNameKey = Get("displayNameKey");
                    map.contentScope = Get("contentScope");
                    maps.Add(adventureId, map);
                    nodeIds.Add(adventureId, new HashSet<string>(StringComparer.Ordinal));
                    coordinates.Add(adventureId, new HashSet<string>(StringComparer.Ordinal));
                    nodes.Add(adventureId, new List<AdventureNodeData>());
                }
                else if (!string.Equals(map.displayNameKey, Get("displayNameKey"), StringComparison.Ordinal) ||
                         !string.Equals(map.contentScope, Get("contentScope"), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(sourceName + ": inconsistent adventure declaration " + adventureId);
                }

                if (!nodeIds[adventureId].Add(nodeId))
                    throw new InvalidOperationException(sourceName + ": duplicate node " + nodeId);
                if (!coordinates[adventureId].Add(q + ":" + r))
                    throw new InvalidOperationException(sourceName + ": duplicate coordinate " + q + ":" + r);
                nodes[adventureId].Add(new AdventureNodeData
                {
                    nodeId = nodeId,
                    nodeTypeId = nodeTypeId,
                    q = q,
                    r = r,
                    contentId = Get("contentId"),
                });
            }

            var result = new List<AdventureMapData>(maps.Count);
            foreach (KeyValuePair<string, AdventureMapData> pair in maps)
            {
                pair.Value.nodes = nodes[pair.Key].ToArray();
                result.Add(pair.Value);
            }
            result.Sort((left, right) => StringComparer.Ordinal.Compare(left.adventureId, right.adventureId));
            return result;
        }
    }
}
