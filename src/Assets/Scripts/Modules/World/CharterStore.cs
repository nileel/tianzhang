using System.Collections.Generic;

namespace TianZhang.World
{
    /// <summary>Owns persistent Charter data. CharterCommitService is its only public mutation route.</summary>
    public sealed class CharterStore
    {
        private readonly Dictionary<string, CharterDefinition> definitions = new Dictionary<string, CharterDefinition>();
        private readonly List<CharterStateEntry> committed = new List<CharterStateEntry>();
        public bool RegisterDefinition(CharterDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.DefinitionId) || definitions.ContainsKey(definition.DefinitionId)) return false;
            definitions.Add(definition.DefinitionId, definition); return true;
        }
        public bool TryGetDefinition(string definitionId, out CharterDefinition definition) { return definitions.TryGetValue(definitionId, out definition); }
        public CharterStateSnapshot Capture() { return new CharterStateSnapshot(committed.ToArray()); }
        public void Restore(CharterStateSnapshot snapshot)
        { if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot)); committed.Clear(); committed.AddRange(snapshot.Entries); }
        internal void Commit(CharterCandidate candidate) { committed.Add(new CharterStateEntry(candidate.Definition.DefinitionId, candidate.OperationId, candidate.Definition.ConflictKey)); }
    }
}
