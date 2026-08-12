using TianZhang.Content;

namespace TianZhang.World
{
    public static class CharterUseCaseReasons
    {
        public const string Ok = "";
        public const string InvalidResult = "charter_commit_invalid_result";
        public const string AlreadyCommitted = "charter_commit_already_committed";
        public const string CatalogUnavailable = "charter_commit_catalog_unavailable";
        public const string VersionMismatch = "charter_commit_version_mismatch";
        public const string StateInvalid = "charter_commit_state_invalid";
    }

    public sealed class CharterUseCaseResult
    {
        private CharterUseCaseResult(bool succeeded, string reason)
        {
            Succeeded = succeeded;
            Reason = reason ?? string.Empty;
        }

        public bool Succeeded { get; }
        public string Reason { get; }
        public static CharterUseCaseResult Success() { return new CharterUseCaseResult(true, CharterUseCaseReasons.Ok); }
        public static CharterUseCaseResult Rejected(string reason) { return new CharterUseCaseResult(false, reason); }
    }

    /// <summary>Validates and atomically commits an already evaluated formal Charter result.</summary>
    public sealed class CharterUseCase
    {
        private readonly CharterStore store;

        public CharterUseCase(CharterStore store)
        {
            this.store = store ?? throw new System.ArgumentNullException(nameof(store));
        }

        public CharterRuntimeStateData CurrentState
        {
            get
            {
                CharterRuntimeStateData state = store.Capture().RuntimeState;
                return state == null ? null : state.CreateCopy();
            }
        }

        public int DefinitionCatalogVersion { get { return store.Capture().DefinitionCatalogVersion; } }

        public CharterUseCaseResult CommitEvaluatedState(
            ContentCatalogData catalog,
            CharterRuntimeStateData evaluatedState,
            int expectedCatalogVersion)
        {
            CharterStateSnapshot current = store.Capture();
            if (evaluatedState == null) return CharterUseCaseResult.Rejected(CharterUseCaseReasons.InvalidResult);
            if (current.RuntimeState != null) return CharterUseCaseResult.Rejected(CharterUseCaseReasons.AlreadyCommitted);
            CharterRuleStaticCatalogData staticCatalog;
            string catalogReason = string.Empty;
            if (catalog == null || !catalog.TryGetCharterRuleStaticCatalog(out staticCatalog, out catalogReason))
                return CharterUseCaseResult.Rejected(CharterUseCaseReasons.CatalogUnavailable);
            if (expectedCatalogVersion != staticCatalog.DefinitionCatalogVersion)
                return CharterUseCaseResult.Rejected(CharterUseCaseReasons.VersionMismatch);
            string stateReason;
            if (!evaluatedState.TryValidate(staticCatalog.Definitions, staticCatalog.ReferenceCatalog, out stateReason))
                return CharterUseCaseResult.Rejected(CharterUseCaseReasons.StateInvalid);
            return store.TryCommitRuntimeState(evaluatedState, staticCatalog.DefinitionCatalogVersion)
                ? CharterUseCaseResult.Success()
                : CharterUseCaseResult.Rejected(CharterUseCaseReasons.AlreadyCommitted);
        }

        public static void ValidateRestoredState(ContentCatalogData catalog, CharterStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            if (snapshot.RuntimeState == null)
            {
                if (snapshot.DefinitionCatalogVersion != 0)
                    throw new System.ArgumentException("Charter runtime state presence does not match its payload.", nameof(snapshot));
                return;
            }
            CharterRuleStaticCatalogData staticCatalog;
            string catalogReason = string.Empty;
            if (catalog == null || !catalog.TryGetCharterRuleStaticCatalog(out staticCatalog, out catalogReason))
                throw new System.ArgumentException("Charter static catalog is unavailable: " + catalogReason, nameof(snapshot));
            if (snapshot.DefinitionCatalogVersion != staticCatalog.DefinitionCatalogVersion)
                throw new System.ArgumentException("Charter definition catalog version mismatch.", nameof(snapshot));
            string stateReason;
            if (!snapshot.RuntimeState.TryValidate(staticCatalog.Definitions, staticCatalog.ReferenceCatalog, out stateReason))
                throw new System.ArgumentException("Charter runtime state is invalid: " + stateReason, nameof(snapshot));
        }
    }
}
