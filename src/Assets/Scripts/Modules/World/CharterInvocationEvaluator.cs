namespace TianZhang.World
{
    public static class CharterInvocationEvaluator
    {
        public static CharterResult Evaluate(CharterAuthorization authorization, CharterCandidate candidate)
        {
            if (authorization == null || candidate == null) return CharterResult.Rejected("INVALID_INPUT");
            return authorization.IsGranted(candidate.Definition.AuthorizationId) ? CharterResult.Accepted() : CharterResult.Rejected("UNAUTHORIZED");
        }
    }
}
