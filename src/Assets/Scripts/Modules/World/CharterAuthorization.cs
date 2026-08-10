using System.Collections.Generic;

namespace TianZhang.World
{
    public sealed class CharterAuthorization
    {
        private readonly HashSet<string> grantedIds = new HashSet<string>();
        public bool Grant(string authorizationId) { return !string.IsNullOrWhiteSpace(authorizationId) && grantedIds.Add(authorizationId); }
        public bool IsGranted(string authorizationId) { return !string.IsNullOrWhiteSpace(authorizationId) && grantedIds.Contains(authorizationId); }
        public CharterAuthorizationSnapshot Capture() { return new CharterAuthorizationSnapshot(new List<string>(grantedIds).ToArray()); }
        public void Restore(CharterAuthorizationSnapshot snapshot)
        { if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot)); grantedIds.Clear(); foreach (string id in snapshot.GrantedIds) Grant(id); }
    }
    public sealed class CharterAuthorizationSnapshot { public CharterAuthorizationSnapshot(string[] grantedIds) { GrantedIds = grantedIds ?? new string[0]; } public string[] GrantedIds { get; } }
}
