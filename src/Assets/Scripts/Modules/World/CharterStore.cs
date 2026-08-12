using System;
using System.Collections.Generic;

namespace TianZhang.World
{
    /// <summary>Owns persistent Charter data. Use cases are its only mutation routes.</summary>
    public sealed class CharterStore
    {
        private readonly Dictionary<string, CharterDefinition> definitions =
            new Dictionary<string, CharterDefinition>(StringComparer.Ordinal);
        private readonly List<CharterStateEntry> committed = new List<CharterStateEntry>();
        private CharterRuntimeStateData runtimeState;
        private int definitionCatalogVersion;

        public bool RegisterDefinition(CharterDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.DefinitionId) ||
                definitions.ContainsKey(definition.DefinitionId))
            {
                return false;
            }
            definitions.Add(definition.DefinitionId, definition);
            return true;
        }

        public bool TryGetDefinition(string definitionId, out CharterDefinition definition)
        {
            return definitions.TryGetValue(definitionId, out definition);
        }

        public CharterStateSnapshot Capture()
        {
            var entries = new List<CharterStateEntry>(committed);
            entries.Sort((left, right) =>
            {
                int byDefinition = string.CompareOrdinal(left.DefinitionId, right.DefinitionId);
                return byDefinition != 0
                    ? byDefinition
                    : string.CompareOrdinal(left.OperationId, right.OperationId);
            });
            return new CharterStateSnapshot(entries.ToArray(), definitionCatalogVersion, runtimeState);
        }

        public void Restore(CharterStateSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var nextEntries = new List<CharterStateEntry>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharterStateEntry entry in snapshot.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.DefinitionId) ||
                    string.IsNullOrWhiteSpace(entry.OperationId) || string.IsNullOrWhiteSpace(entry.ConflictKey) ||
                    !identities.Add(entry.DefinitionId + "\n" + entry.OperationId))
                {
                    throw new InvalidOperationException("Invalid charter snapshot.");
                }
                nextEntries.Add(entry);
            }
            if ((snapshot.RuntimeState == null) != (snapshot.DefinitionCatalogVersion == 0) ||
                snapshot.DefinitionCatalogVersion < 0)
            {
                throw new InvalidOperationException("Charter runtime state presence does not match its catalog version.");
            }
            committed.Clear();
            committed.AddRange(nextEntries);
            runtimeState = snapshot.RuntimeState == null ? null : snapshot.RuntimeState.CreateCopy();
            definitionCatalogVersion = snapshot.DefinitionCatalogVersion;
        }

        internal void Commit(CharterCandidate candidate)
        {
            committed.Add(new CharterStateEntry(
                candidate.Definition.DefinitionId,
                candidate.OperationId,
                candidate.Definition.ConflictKey));
        }

        internal bool TryCommitRuntimeState(CharterRuntimeStateData state, int catalogVersion)
        {
            if (state == null || catalogVersion <= 0 || runtimeState != null)
                return false;
            runtimeState = state.CreateCopy();
            definitionCatalogVersion = catalogVersion;
            return true;
        }
    }
}
