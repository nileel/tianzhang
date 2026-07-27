using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BattleSim;

internal sealed record NpcActionWeight(
    string Id,
    string ActionId,
    string LegalityRuleSetRef,
    double BaseWeight,
    string RiskGateRef,
    bool Enabled,
    string ActionTotalCapRef);

internal sealed record NpcWeightModifier(
    string Id,
    string SourceKind,
    string ActionId,
    string SelectorRef,
    double PriorityDelta,
    int ApplicationOrder,
    string CapRef,
    string DiminishingRef,
    double RiskThresholdDelta);

internal sealed record NpcCapPolicy(string Id, string Scope, double Minimum, double Maximum, string AppliesAfterSourceKind);
internal sealed record NpcDiminishingPolicy(string Id, string Scope, string InputBasis, double ActivationThreshold, string Segments, double OutputBound);
internal sealed record NpcRiskGate(string Id, string[] KnownEvidenceRefs, string RiskAssessmentRef, double BaseRiskThreshold, string LifespanCapRef);
internal sealed record NpcDecisionCandidate(string ActionId, bool Accepted, string RejectionReason, double Score, string[] MatchedModifierIds);
internal sealed record NpcDecisionResult(IReadOnlyList<NpcDecisionCandidate> Candidates, string SelectedActionId);

/// <summary>
/// BattleSim's read-only projection of the NPC cultivation profile CSV. It validates the exact
/// source set and has no numeric constants for any production action, modifier, threshold or cap.
/// </summary>
internal sealed class NpcCultivationActionWeightProfile
{
    internal const string SchemaId = "npcCultivationActionWeightProfile";
    internal const int SchemaVersion = 1;
    internal const string IllegalAction = "NPC_WEIGHT_ILLEGAL_ACTION";
    internal const string RiskGateRejected = "NPC_WEIGHT_RISK_GATE_REJECTED";

    private static readonly string[] Columns =
    {
        "schemaId", "schemaVersion", "profileId", "sourceContentHash", "authorityKind", "recordKind", "recordId",
        "actionStableId", "legalityRuleSetRef", "baseWeight", "subjectiveRiskGateRef", "enabled", "sourceKind",
        "selectorRef", "priorityDelta", "applicationOrder", "capPolicyRef", "diminishingPolicyRef", "actionTotalCapPolicyRef",
        "scope", "minimum", "maximum", "appliesAfterSourceKind", "inputBasis", "activationThreshold", "segments",
        "outputBound", "tieBreakPolicy", "triggerStableId", "riskThresholdDelta", "knownEvidenceRefs", "riskAssessmentRef",
        "baseRiskThreshold", "lifespanCapPolicyRef",
    };

    private static readonly string[] RequiredActions =
    {
        "FOUNDATION_TRIAL",
        "FOUNDATION_NURTURE",
        "MANSION_EMBRYO_NURTURE",
        "MANSION_OPENING_TRIAL",
        "JINDAN_PROOF",
    };

    private static readonly string[] SourceOrder =
    {
        "PERSONALITY", "SECT", "REALM_GOAL", "LIFESPAN", "RESOURCE", "ENVIRONMENT",
    };

    private readonly Dictionary<string, NpcActionWeight> actions;
    private readonly NpcWeightModifier[] modifiers;
    private readonly Dictionary<string, NpcCapPolicy> caps;
    private readonly Dictionary<string, NpcDiminishingPolicy> diminishing;
    private readonly Dictionary<string, NpcRiskGate> riskGates;

    private NpcCultivationActionWeightProfile(
        string profileId,
        string sourceContentHash,
        IEnumerable<NpcActionWeight> actionRows,
        IEnumerable<NpcWeightModifier> modifierRows,
        IEnumerable<NpcCapPolicy> capPolicies,
        IEnumerable<NpcDiminishingPolicy> diminishingPolicies,
        IEnumerable<NpcRiskGate> gates)
    {
        ProfileId = profileId;
        SourceContentHash = sourceContentHash;
        actions = actionRows.ToDictionary(row => row.ActionId, StringComparer.Ordinal);
        modifiers = modifierRows.ToArray();
        caps = capPolicies.ToDictionary(row => row.Id, StringComparer.Ordinal);
        diminishing = diminishingPolicies.ToDictionary(row => row.Id, StringComparer.Ordinal);
        riskGates = gates.ToDictionary(row => row.Id, StringComparer.Ordinal);
    }

    internal string ProfileId { get; }
    internal string SourceContentHash { get; }

    internal static NpcCultivationActionWeightProfile LoadProduction(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, "src", "Assets", "DataConfig", "NpcCultivationActionWeightProfiles.csv");
        if (!File.Exists(path))
            throw new InvalidDataException($"NPC_WEIGHT_MISSING_EXPLICIT_VALUE: missing source profile '{path}'.");

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length < 2)
            throw new InvalidDataException("NPC_WEIGHT_MISSING_EXPLICIT_VALUE: source profile has no records.");

        string[] headers = Split(lines[0]);
        if (!headers.SequenceEqual(Columns, StringComparer.Ordinal))
            throw new InvalidDataException("NPC_WEIGHT_UNKNOWN_SCHEMA: source profile headers differ from the locked CSV schema.");

        var rows = lines.Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            .Select(Split)
            .ToArray();
        if (rows.Any(row => row.Length != Columns.Length))
            throw new InvalidDataException("NPC_WEIGHT_MISSING_EXPLICIT_VALUE: source profile has an incomplete row.");

        int hashIndex = Array.IndexOf(Columns, "sourceContentHash");
        string contentHash = ComputeHash(headers, rows, hashIndex);
        string profileId = Required(rows[0], "profileId");
        if (rows.Any(row => Required(row, "profileId") != profileId))
            throw new InvalidDataException("NPC_WEIGHT_DOUBLE_AUTHORITY: one source file may not mix profile IDs.");

        var manifest = rows.Where(row => Value(row, "recordKind") == "MANIFEST").ToArray();
        if (manifest.Length != 1 || Required(manifest[0], "schemaId") != SchemaId ||
            ParseInt(Required(manifest[0], "schemaVersion"), "schemaVersion") != SchemaVersion ||
            Required(manifest[0], "authorityKind") != "CSV_SOURCE_SET" ||
            Required(manifest[0], "sourceContentHash") != contentHash ||
            Required(manifest[0], "tieBreakPolicy") != "LEXICOGRAPHIC_ASC")
        {
            throw new InvalidDataException("NPC_WEIGHT_DOUBLE_AUTHORITY: manifest must be the sole CSV source authority with the matching content hash.");
        }

        var actionRows = rows.Where(row => Value(row, "recordKind") == "ACTION").Select(row => new NpcActionWeight(
            Required(row, "recordId"), Required(row, "actionStableId"), Required(row, "legalityRuleSetRef"),
            ParseDouble(Required(row, "baseWeight"), "baseWeight"), Value(row, "subjectiveRiskGateRef"),
            ParseBool(Required(row, "enabled")), Required(row, "actionTotalCapPolicyRef"))).ToArray();
        var modifierRows = rows.Where(row => Value(row, "recordKind") == "MODIFIER").Select(row => new NpcWeightModifier(
            Required(row, "recordId"), Required(row, "sourceKind"), Required(row, "actionStableId"), Required(row, "selectorRef"),
            ParseDouble(Required(row, "priorityDelta"), "priorityDelta"), ParseInt(Required(row, "applicationOrder"), "applicationOrder"),
            Required(row, "capPolicyRef"), Required(row, "diminishingPolicyRef"), ParseDouble(Value(row, "riskThresholdDelta", "0"), "riskThresholdDelta"))).ToArray();
        var capPolicies = rows.Where(row => Value(row, "recordKind") == "CAP_POLICY").Select(row => new NpcCapPolicy(
            Required(row, "recordId"), Required(row, "scope"), ParseDouble(Required(row, "minimum"), "minimum"),
            ParseDouble(Required(row, "maximum"), "maximum"), Value(row, "appliesAfterSourceKind"))).ToArray();
        var diminishingPolicies = rows.Where(row => Value(row, "recordKind") == "DIMINISHING_POLICY").Select(row => new NpcDiminishingPolicy(
            Required(row, "recordId"), Required(row, "scope"), Required(row, "inputBasis"),
            ParseDouble(Required(row, "activationThreshold"), "activationThreshold"), Required(row, "segments"),
            ParseDouble(Required(row, "outputBound"), "outputBound"))).ToArray();
        var gates = rows.Where(row => Value(row, "recordKind") == "RISK_GATE").Select(row => new NpcRiskGate(
            Required(row, "recordId"), Required(row, "knownEvidenceRefs").Split('|'), Required(row, "riskAssessmentRef"),
            ParseDouble(Required(row, "baseRiskThreshold"), "baseRiskThreshold"), Required(row, "lifespanCapPolicyRef"))).ToArray();
        var triggers = rows.Where(row => Value(row, "recordKind") == "TRIGGER").Select(row => Required(row, "triggerStableId")).ToArray();

        Validate(profileId, actionRows, modifierRows, capPolicies, diminishingPolicies, gates, triggers);
        return new NpcCultivationActionWeightProfile(profileId, contentHash, actionRows, modifierRows, capPolicies, diminishingPolicies, gates);
    }

    internal NpcDecisionResult Evaluate(
        IEnumerable<string> legalActionIds,
        IEnumerable<string> selectorRefs,
        IReadOnlyDictionary<string, double> riskAssessments)
    {
        var legal = new HashSet<string>(legalActionIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var selectors = new HashSet<string>(selectorRefs ?? Array.Empty<string>(), StringComparer.Ordinal);
        riskAssessments ??= new Dictionary<string, double>(StringComparer.Ordinal);
        var candidates = new List<NpcDecisionCandidate>();

        foreach (string actionId in RequiredActions)
        {
            var action = actions[actionId];
            if (!action.Enabled || !legal.Contains(actionId))
            {
                candidates.Add(new NpcDecisionCandidate(actionId, false, IllegalAction, 0, Array.Empty<string>()));
                continue;
            }

            var matched = modifiers.Where(row => row.ActionId == actionId && selectors.Contains(row.SelectorRef))
                .OrderBy(row => Array.IndexOf(SourceOrder, row.SourceKind))
                .ThenBy(row => row.ApplicationOrder)
                .ThenBy(row => row.Id, StringComparer.Ordinal)
                .ToArray();
            if (!PassesRiskGate(action, matched, selectors, riskAssessments))
            {
                candidates.Add(new NpcDecisionCandidate(actionId, false, RiskGateRejected, 0, matched.Select(row => row.Id).ToArray()));
                continue;
            }

            double score = action.BaseWeight;
            foreach (string sourceKind in SourceOrder)
            {
                var sourceRows = matched.Where(row => row.SourceKind == sourceKind).ToArray();
                if (sourceRows.Length == 0)
                    continue;
                score += ApplyCap(ApplyDiminishing(sourceRows.Sum(row => row.PriorityDelta), diminishing[sourceRows[0].DiminishingRef]), caps[sourceRows[0].CapRef]);
            }
            score = ApplyCap(score, caps[action.ActionTotalCapRef]);
            candidates.Add(new NpcDecisionCandidate(actionId, true, null, score, matched.Select(row => row.Id).ToArray()));
        }

        var ordered = OrderAccepted(candidates);
        return new NpcDecisionResult(candidates, ordered.FirstOrDefault()?.ActionId);
    }

    internal static IReadOnlyList<NpcDecisionCandidate> OrderAccepted(IEnumerable<NpcDecisionCandidate> candidates) =>
        candidates.Where(candidate => candidate.Accepted)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ActionId, StringComparer.Ordinal)
            .ToArray();

    private bool PassesRiskGate(
        NpcActionWeight action,
        NpcWeightModifier[] matched,
        ISet<string> selectors,
        IReadOnlyDictionary<string, double> assessments)
    {
        if (string.IsNullOrEmpty(action.RiskGateRef))
            return true;

        var gate = riskGates[action.RiskGateRef];
        if (gate.KnownEvidenceRefs.Any(reference => !selectors.Contains(reference)) ||
            !assessments.TryGetValue(gate.RiskAssessmentRef, out double assessment))
            return false;

        double threshold = gate.BaseRiskThreshold + matched.Where(row => row.SourceKind == "LIFESPAN").Sum(row => row.RiskThresholdDelta);
        return assessment >= ApplyCap(threshold, caps[gate.LifespanCapRef]);
    }

    private static double ApplyCap(double value, NpcCapPolicy policy) => Math.Clamp(value, policy.Minimum, policy.Maximum);

    private static double ApplyDiminishing(double value, NpcDiminishingPolicy policy)
    {
        double sign = value < 0 ? -1 : 1;
        double magnitude = Math.Abs(value);
        double output = 0;
        foreach (string segment in policy.Segments.Split('|'))
        {
            string[] parts = segment.Split('@');
            string[] bounds = parts[0].Split('-');
            double lower = ParseDouble(bounds[0], "segment lower");
            double upper = ParseDouble(bounds[1], "segment upper");
            double multiplier = ParseDouble(parts[1], "segment multiplier");
            if (magnitude > lower)
                output += (Math.Min(magnitude, upper) - lower) * multiplier;
        }
        return sign * Math.Min(output, policy.OutputBound);
    }

    private static void Validate(
        string profileId,
        NpcActionWeight[] actionRows,
        NpcWeightModifier[] modifierRows,
        NpcCapPolicy[] capPolicies,
        NpcDiminishingPolicy[] diminishingPolicies,
        NpcRiskGate[] gates,
        string[] triggers)
    {
        if (string.IsNullOrWhiteSpace(profileId) || actionRows.Length != RequiredActions.Length ||
            actionRows.Select(row => row.ActionId).Distinct(StringComparer.Ordinal).Count() != RequiredActions.Length ||
            !RequiredActions.All(actionId => actionRows.Any(row => row.ActionId == actionId)) ||
            modifierRows.Any(row => !SourceOrder.Contains(row.SourceKind) || !RequiredActions.Contains(row.ActionId)) ||
            triggers.Length != 8 || triggers.Distinct(StringComparer.Ordinal).Count() != triggers.Length)
            throw new InvalidDataException("NPC_WEIGHT_UNKNOWN_ACTION: action, modifier, or recalculation records do not match the locked contract.");

        var allIds = actionRows.Select(row => row.Id).Concat(modifierRows.Select(row => row.Id)).Concat(capPolicies.Select(row => row.Id))
            .Concat(diminishingPolicies.Select(row => row.Id)).Concat(gates.Select(row => row.Id)).Concat(triggers).ToArray();
        if (allIds.Any(string.IsNullOrWhiteSpace) || allIds.Distinct(StringComparer.Ordinal).Count() != allIds.Length ||
            capPolicies.Any(policy => policy.Minimum > policy.Maximum || policy.Scope is not ("SOURCE_GROUP" or "ACTION_TOTAL" or "RISK_THRESHOLD")) ||
            diminishingPolicies.Any(policy => policy.Scope != "SOURCE_GROUP" || policy.OutputBound < 0 || !HasContinuousSegments(policy.Segments)))
            throw new InvalidDataException("NPC_WEIGHT_INVALID_POLICY: profile records are duplicate, incomplete, or have an invalid policy.");

        var capIds = new HashSet<string>(capPolicies.Select(policy => policy.Id), StringComparer.Ordinal);
        var diminishingIds = new HashSet<string>(diminishingPolicies.Select(policy => policy.Id), StringComparer.Ordinal);
        var gateIds = new HashSet<string>(gates.Select(gate => gate.Id), StringComparer.Ordinal);
        if (actionRows.Any(row => !capIds.Contains(row.ActionTotalCapRef) || (!string.IsNullOrEmpty(row.RiskGateRef) && !gateIds.Contains(row.RiskGateRef))) ||
            modifierRows.Any(row => !capIds.Contains(row.CapRef) || !diminishingIds.Contains(row.DiminishingRef)) ||
            gates.Any(gate => !capIds.Contains(gate.LifespanCapRef)))
            throw new InvalidDataException("NPC_WEIGHT_UNKNOWN_ACTION: profile references an unknown rule, policy, or risk gate.");
    }

    private static bool HasContinuousSegments(string segments)
    {
        double expectedLower = 0;
        foreach (string segment in segments.Split('|'))
        {
            string[] parts = segment.Split('@');
            string[] bounds = parts[0].Split('-');
            if (parts.Length != 2 || bounds.Length != 2 ||
                !double.TryParse(bounds[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lower) ||
                !double.TryParse(bounds[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double upper) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double multiplier) ||
                lower != expectedLower || upper <= lower || multiplier < 0)
                return false;
            expectedLower = upper;
        }
        return true;
    }

    private static string[] Split(string line) => line.Split(',').Select(value => value.Trim()).ToArray();

    private static string Value(string[] row, string column, string defaultValue = "")
    {
        int index = Array.IndexOf(Columns, column);
        string value = row[index];
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static string Required(string[] row, string column)
    {
        string value = Value(row, column);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"NPC_WEIGHT_MISSING_EXPLICIT_VALUE: column '{column}' is empty.");
        return value;
    }

    private static int ParseInt(string value, string field) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : throw new InvalidDataException($"NPC_WEIGHT_MISSING_EXPLICIT_VALUE: {field} is invalid.");

    private static double ParseDouble(string value, string field) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) && !double.IsNaN(result) && !double.IsInfinity(result)
            ? result
            : throw new InvalidDataException($"NPC_WEIGHT_MISSING_EXPLICIT_VALUE: {field} is invalid.");

    private static bool ParseBool(string value) => value switch
    {
        "true" => true,
        "false" => false,
        _ => throw new InvalidDataException("NPC_WEIGHT_MISSING_EXPLICIT_VALUE: enabled must be true or false."),
    };

    private static string ComputeHash(string[] headers, IEnumerable<string[]> rows, int hashIndex)
    {
        string canonical = string.Join("\n", new[] { string.Join(",", headers) }.Concat(rows.Select(row =>
        {
            string[] copy = row.ToArray();
            copy[hashIndex] = string.Empty;
            return string.Join(",", copy);
        })));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
