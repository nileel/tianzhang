namespace TianZhang.World
{
    /// <summary>The unique long-term Charter mutation entry point.</summary>
    public sealed class CharterCommitService
    {
        public CharterResult TryCommit(CharterStore store, CharterAuthorization authorization, CharterInvocationRequest request)
        {
            if (store == null || authorization == null || request == null) return CharterResult.Rejected("INVALID_INPUT");
            CharterDefinition definition;
            if (!store.TryGetDefinition(request.DefinitionId, out definition)) return CharterResult.Rejected("UNKNOWN_DEFINITION");
            CharterCandidate candidate;
            CharterResult result = CharterCandidateBuilder.TryBuild(definition, request, out candidate);
            if (!result.Succeeded) return result;
            result = CharterInvocationEvaluator.Evaluate(authorization, candidate);
            if (!result.Succeeded) return result;
            result = CharterConflictResolver.Resolve(definition, store.Capture());
            if (!result.Succeeded) return result;
            store.Commit(candidate);
            return CharterResult.Committed(definition.DefinitionId, candidate.OperationId);
        }
    }
}
