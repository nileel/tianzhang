namespace TianZhang.World
{
    public sealed class CharterStateEntry
    {
        public CharterStateEntry(string definitionId, string operationId, string conflictKey) { DefinitionId = definitionId; OperationId = operationId; ConflictKey = conflictKey; }
        public string DefinitionId { get; } public string OperationId { get; } public string ConflictKey { get; }
    }
    public sealed class CharterStateSnapshot
    {
        public CharterStateSnapshot(CharterStateEntry[] entries) { Entries = entries ?? new CharterStateEntry[0]; }
        public CharterStateEntry[] Entries { get; }
    }
}
