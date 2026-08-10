using System.Collections.Generic;

namespace TianZhang.World
{
    public sealed class CharterInvocationRequest
    {
        public CharterInvocationRequest(string definitionId, string authorizationId, IEnumerable<string> atomicOperationIds)
        { DefinitionId = definitionId ?? string.Empty; AuthorizationId = authorizationId ?? string.Empty; AtomicOperationIds = atomicOperationIds == null ? new string[0] : new List<string>(atomicOperationIds).ToArray(); }
        public string DefinitionId { get; } public string AuthorizationId { get; } public string[] AtomicOperationIds { get; }
    }
    public sealed class CharterCandidate { public CharterCandidate(CharterDefinition definition, string operationId) { Definition = definition; OperationId = operationId; } public CharterDefinition Definition { get; } public string OperationId { get; } }
    public static class CharterCandidateBuilder
    {
        public static CharterResult TryBuild(CharterDefinition definition, CharterInvocationRequest request, out CharterCandidate candidate)
        {
            candidate = null;
            if (definition == null || request == null || definition.DefinitionId != request.DefinitionId) return CharterResult.Rejected("UNKNOWN_DEFINITION");
            if (request.AtomicOperationIds.Length != 1 || !definition.ContainsAtomicOperation(request.AtomicOperationIds[0])) return CharterResult.Rejected("ILLEGAL_ATOMIC_COMMIT");
            candidate = new CharterCandidate(definition, request.AtomicOperationIds[0]); return CharterResult.Accepted();
        }
    }
}
