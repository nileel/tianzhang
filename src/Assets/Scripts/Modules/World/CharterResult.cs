namespace TianZhang.World
{
    public sealed class CharterResult
    {
        private CharterResult(bool succeeded, string reason, string definitionId, string operationId)
        { Succeeded = succeeded; Reason = reason ?? string.Empty; DefinitionId = definitionId ?? string.Empty; OperationId = operationId ?? string.Empty; }
        public bool Succeeded { get; } public string Reason { get; } public string DefinitionId { get; } public string OperationId { get; }
        public static CharterResult Accepted() { return new CharterResult(true, string.Empty, string.Empty, string.Empty); }
        public static CharterResult Committed(string definitionId, string operationId) { return new CharterResult(true, "COMMITTED", definitionId, operationId); }
        public static CharterResult Rejected(string reason) { return new CharterResult(false, reason, string.Empty, string.Empty); }
    }
}
