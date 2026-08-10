namespace TianZhang.World
{
    /// <summary>Pure conflict check; it cannot mutate CharterStore.</summary>
    public static class CharterConflictResolver
    {
        public static CharterResult Resolve(CharterDefinition definition, CharterStateSnapshot current)
        {
            if (definition == null) return CharterResult.Rejected("UNKNOWN_DEFINITION");
            foreach (CharterStateEntry entry in (current == null ? new CharterStateEntry[0] : current.Entries))
                if (!string.IsNullOrWhiteSpace(definition.ConflictKey) && definition.ConflictKey == entry.ConflictKey)
                    return CharterResult.Rejected("CONFLICT");
            return CharterResult.Accepted();
        }
    }
}
