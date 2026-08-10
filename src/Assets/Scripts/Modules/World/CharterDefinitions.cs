using System.Collections.Generic;

namespace TianZhang.World
{
    /// <summary>Immutable charter definition and its legal atomic operations.</summary>
    public sealed class CharterDefinition
    {
        public CharterDefinition(string definitionId, string authorizationId, string conflictKey, IEnumerable<string> atomicOperationIds)
        {
            DefinitionId = definitionId ?? string.Empty; AuthorizationId = authorizationId ?? string.Empty; ConflictKey = conflictKey ?? string.Empty;
            AtomicOperationIds = atomicOperationIds == null ? new string[0] : new List<string>(atomicOperationIds).ToArray();
        }
        public string DefinitionId { get; } public string AuthorizationId { get; } public string ConflictKey { get; } public string[] AtomicOperationIds { get; }
        public bool ContainsAtomicOperation(string operationId) { foreach (string known in AtomicOperationIds) if (known == operationId) return true; return false; }
    }
}
