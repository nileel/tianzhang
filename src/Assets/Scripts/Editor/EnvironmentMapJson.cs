using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TianZhang.Content;
using UnityEngine;

namespace TianZhang.Editor
{
    [Serializable]
    public sealed class EnvironmentPhysicalSurfaceProfilesJson
    {
        public EnvironmentGridGeometryJson gridGeometry;
        public EnvironmentFlatSurfaceProfileJson[] flatProfiles;
        public EnvironmentRampSurfaceProfileJson[] rampProfiles;
        public EnvironmentStairConnectionJson[] stairConnections;
    }

    [Serializable]
    public sealed class EnvironmentGridGeometryJson
    {
        public string worldCenterFormula;
        public double neighborCenterDistanceMeters;
        public string hexOrientation;
    }

    [Serializable]
    public sealed class EnvironmentFlatSurfaceProfileJson
    {
        public string id;
        public string kind;
        public double supportY;
    }

    [Serializable]
    public sealed class EnvironmentRampSurfaceProfileJson
    {
        public string id;
        public string kind;
        public string[] coveredCells;
        public string[] shoulderCells;
        public double worldZStart;
        public double worldZEnd;
        public double lowerY;
        public double upperY;
        public string[] lowerPortalEdges;
        public string[] upperPortalEdges;
    }

    [Serializable]
    public sealed class EnvironmentStairConnectionJson
    {
        public string id;
        public string kind;
        public string[] coveredCells;
        public int lowerLevel;
        public int upperLevel;
        public int clearanceLimit;
        public EnvironmentStairPlatformCenterJson[] platformCenters;
    }

    [Serializable]
    public sealed class EnvironmentStairPlatformCenterJson
    {
        public string cell;
        public int heightLevel;
        public bool groundStopAllowed;
    }

    [Serializable]
    public sealed class EnvironmentMapMarkerJson
    {
        public string id;
        public string kind;
        public string cell;
    }

    [Serializable]
    public sealed class EnvironmentMapMarkerArrayJson
    {
        public EnvironmentMapMarkerJson[] items;
    }

    /// <summary>
    /// Version-2 map JSON is a normal quoted CSV cell in the existing environment profile entry.
    /// This validator resolves support ids and marker cells before an asset can be written; the
    /// detailed geometry stays opaque until D3 consumes it for scene assembly.
    /// </summary>
    internal static class EnvironmentMapJsonValidator
    {
        internal sealed class SupportIndex
        {
            public readonly Dictionary<string, EnvironmentEdgeKind> Kinds =
                new Dictionary<string, EnvironmentEdgeKind>(StringComparer.Ordinal);
            public readonly Dictionary<string, EnvironmentRampSurfaceProfileJson> Ramps =
                new Dictionary<string, EnvironmentRampSurfaceProfileJson>(StringComparer.Ordinal);
            public readonly Dictionary<string, EnvironmentStairConnectionJson> Stairs =
                new Dictionary<string, EnvironmentStairConnectionJson>(StringComparer.Ordinal);
        }

        public static SupportIndex ValidatePhysicalSurfaceProfiles(
            string raw,
            IReadOnlyDictionary<string, EnvironmentBattlefieldCell> cells,
            string sourceName)
        {
            var root = Parse<EnvironmentPhysicalSurfaceProfilesJson>(raw, sourceName, "physicalSurfaceProfiles");
            if (root == null) Fail(sourceName, "physicalSurfaceProfiles has no JSON object.");
            if (root.gridGeometry == null) Fail(sourceName, "physicalSurfaceProfiles.gridGeometry is required.");
            if (string.IsNullOrWhiteSpace(root.gridGeometry.worldCenterFormula))
                Fail(sourceName, "physicalSurfaceProfiles.gridGeometry.worldCenterFormula is required.");
            if (string.IsNullOrWhiteSpace(root.gridGeometry.hexOrientation))
                Fail(sourceName, "physicalSurfaceProfiles.gridGeometry.hexOrientation is required.");
            if (!IsFinitePositive(root.gridGeometry.neighborCenterDistanceMeters))
                Fail(sourceName, "physicalSurfaceProfiles.gridGeometry.neighborCenterDistanceMeters must be positive.");
            if (root.flatProfiles == null || root.rampProfiles == null || root.stairConnections == null)
                Fail(sourceName, "physicalSurfaceProfiles requires flatProfiles, rampProfiles and stairConnections arrays.");

            var index = new SupportIndex();
            foreach (EnvironmentFlatSurfaceProfileJson profile in root.flatProfiles)
            {
                RequireUniqueSupport(index, profile?.id, sourceName, "flatProfiles");
                if (double.IsNaN(profile.supportY) || double.IsInfinity(profile.supportY))
                    Fail(sourceName, $"flat profile '{profile.id}' has an invalid supportY.");
                index.Kinds[profile.id] = EnvironmentEdgeKind.Flat;
            }

            foreach (EnvironmentRampSurfaceProfileJson profile in root.rampProfiles)
            {
                RequireUniqueSupport(index, profile?.id, sourceName, "rampProfiles");
                index.Kinds[profile.id] = EnvironmentEdgeKind.Slope;
                string[] covered = RequireCells(profile.coveredCells, cells, sourceName, "rampProfiles.coveredCells", profile.id);
                if (profile.shoulderCells != null && profile.shoulderCells.Length > 0)
                {
                    string[] shoulders = RequireCells(profile.shoulderCells, cells, sourceName, "rampProfiles.shoulderCells", profile.id);
                    foreach (string shoulder in shoulders)
                        if (!cells[shoulder].blocksGroundMove)
                            Fail(sourceName, $"ramp shoulder '{shoulder}' must block ground movement.");
                }
                foreach (string cell in covered)
                    if (cells[cell].physicalSurfaceId != profile.id)
                        Fail(sourceName, $"ramp cell '{cell}' must reference support '{profile.id}'.");
                index.Ramps.Add(profile.id, profile);
            }

            foreach (EnvironmentStairConnectionJson profile in root.stairConnections)
            {
                RequireUniqueSupport(index, profile?.id, sourceName, "stairConnections");
                index.Kinds[profile.id] = EnvironmentEdgeKind.Stairs;
                string[] covered = RequireCells(profile.coveredCells, cells, sourceName, "stairConnections.coveredCells", profile.id);
                if (profile.clearanceLimit < 0) Fail(sourceName, $"stair connection '{profile.id}' has negative clearance.");
                if (profile.lowerLevel > profile.upperLevel)
                    Fail(sourceName, $"stair connection '{profile.id}' has lowerLevel above upperLevel.");
                if (profile.upperLevel - profile.lowerLevel > 1)
                    Fail(sourceName, $"stair connection '{profile.id}' may cross at most one rule height level.");
                if (profile.platformCenters == null ||
                    profile.platformCenters.Length != covered.Length ||
                    covered.Length < 2)
                    Fail(sourceName, $"stair connection '{profile.id}' requires one platform center per covered cell.");
                for (var i = 0; i < profile.platformCenters.Length; i++)
                {
                    EnvironmentStairPlatformCenterJson platform = profile.platformCenters[i];
                    if (platform == null)
                        Fail(sourceName, $"stair connection '{profile.id}' has an empty platform center.");
                    string platformCell = NormalizeCell(platform.cell, sourceName, $"stairConnections '{profile.id}' platform");
                    if (!cells.TryGetValue(platformCell, out var cell) || !covered.Contains(platformCell))
                        Fail(sourceName, $"stair connection '{profile.id}' platform cell is not mapped.");
                    if (platform.heightLevel != cell.heightLevel)
                        Fail(sourceName, $"stair platform '{platformCell}' heightLevel differs from the map cell.");
                    int expected = i == 0 ? profile.lowerLevel :
                        i == profile.platformCenters.Length - 1 ? profile.upperLevel : profile.lowerLevel;
                    if (platform.heightLevel != expected)
                        Fail(sourceName, $"stair platform '{platformCell}' has an invalid intermediate height.");
                }
                foreach (string cell in covered)
                    if (cells[cell].physicalSurfaceId != profile.id)
                        Fail(sourceName, $"stair cell '{cell}' must reference support '{profile.id}'.");
                index.Stairs.Add(profile.id, profile);
            }

            foreach (string cellName in cells.Keys)
                if (!index.Kinds.ContainsKey(cells[cellName].physicalSurfaceId ?? ""))
                    Fail(sourceName, $"cell '{cellName}' references unknown physical support '{cells[cellName].physicalSurfaceId}'.");
            return index;
        }

        public static void ValidateMapMarkers(
            string raw,
            IReadOnlyDictionary<string, EnvironmentBattlefieldCell> cells,
            string sourceName)
        {
            EnvironmentMapMarkerArrayJson root;
            try
            {
                root = JsonUtility.FromJson<EnvironmentMapMarkerArrayJson>("{\"items\":" + raw + "}");
            }
            catch (Exception exception) when (exception is ArgumentException || exception is FormatException)
            {
                throw new InvalidDataException($"{sourceName} has invalid mapMarkers JSON: {exception.Message}");
            }

            if (root?.items == null) Fail(sourceName, "mapMarkers must be a JSON array.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (EnvironmentMapMarkerJson marker in root.items)
            {
                if (marker == null || string.IsNullOrWhiteSpace(marker.id))
                    Fail(sourceName, "mapMarkers entries require a non-empty id.");
                if (!ids.Add(marker.id)) Fail(sourceName, $"mapMarkers repeats id '{marker.id}'.");
                if (string.IsNullOrWhiteSpace(marker.cell) || !cells.ContainsKey(marker.cell))
                    Fail(sourceName, $"mapMarkers '{marker.id}' references an unknown cell '{marker.cell}'.");
            }
        }

        public static string NormalizeCell(string raw, string sourceName, string field)
        {
            string[] parts = (raw ?? "").Split(':');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out int q) ||
                !int.TryParse(parts[1], out int r))
                throw new InvalidDataException($"{sourceName} has invalid {field} cell '{raw}'.");
            return q + ":" + r;
        }

        private static void RequireUniqueSupport(
            SupportIndex index,
            string id,
            string sourceName,
            string group)
        {
            if (string.IsNullOrWhiteSpace(id) || !IsReference(id))
                Fail(sourceName, $"{group} requires a valid support id.");
            if (index.Kinds.ContainsKey(id) || index.Ramps.ContainsKey(id) || index.Stairs.ContainsKey(id))
                Fail(sourceName, $"physical support id '{id}' is duplicated.");
        }

        private static string[] RequireCells(
            string[] values,
            IReadOnlyDictionary<string, EnvironmentBattlefieldCell> cells,
            string sourceName,
            string field,
            string supportId)
        {
            if (values == null || values.Length == 0)
                Fail(sourceName, $"{field} for support '{supportId}' must not be empty.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var normalizedValues = new string[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                string normalized = NormalizeCell(values[index], sourceName, field);
                if (!cells.ContainsKey(normalized) || !seen.Add(normalized))
                    Fail(sourceName, $"{field} for support '{supportId}' references an unknown or duplicate cell '{values[index]}'.");
                normalizedValues[index] = normalized;
            }
            return normalizedValues;
        }

        private static T Parse<T>(string raw, string sourceName, string field) where T : class
        {
            try
            {
                return JsonUtility.FromJson<T>(raw);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is FormatException)
            {
                throw new InvalidDataException($"{sourceName} has invalid {field} JSON: {exception.Message}");
            }
        }

        private static bool IsReference(string value) => !string.IsNullOrWhiteSpace(value) &&
            value.All(character => char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.');

        private static bool IsFinitePositive(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;

        private static void Fail(string sourceName, string message) =>
            throw new InvalidDataException($"{sourceName} {message}");
    }
}
