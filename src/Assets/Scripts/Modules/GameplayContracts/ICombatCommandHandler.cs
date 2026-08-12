namespace TianZhang.Gameplay.Contracts
{
    /// <summary>
    /// Scene-agnostic command boundary between combat presentation and the gameplay composition root.
    /// All identities are stable combatant or profile IDs; coordinates are represented as scalar values.
    /// </summary>
    public interface ICombatCommandHandler
    {
        void RequestBasicAttack(string actorId, string targetId);
        void RequestArt(string actorId, string targetId, string profileId);
        void RequestDivine(string actorId, string targetId, string profileId);
        void RequestGuard(string actorId);
        void RequestWait(string actorId);
        void RequestMove(string actorId, int destinationQ, int destinationR);
        void RequestSwapSpell(string actorId, int slotIndex, string profileId);
    }
}
