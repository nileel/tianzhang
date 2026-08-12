using System.Collections.Generic;
using TianZhang.Entity;

namespace TianZhang.Cultivation
{
    /// <summary>Aggregate cultivation state with no dependency on a mutable Character implementation.</summary>
    public sealed class CultivationState
    {
        private readonly Dictionary<string, MansionState> mansions = new Dictionary<string, MansionState>();
        private readonly Dictionary<string, GuardianAbilityState> guardians = new Dictionary<string, GuardianAbilityState>();
        public CultivationState(FoundationState foundation, CultivationActionState action, ClosedRetreatState retreat, JindanLockState jindanLock)
        {
            Foundation = foundation ?? throw new System.ArgumentNullException(nameof(foundation));
            Action = action ?? throw new System.ArgumentNullException(nameof(action));
            Retreat = retreat ?? throw new System.ArgumentNullException(nameof(retreat));
            JindanLock = jindanLock ?? throw new System.ArgumentNullException(nameof(jindanLock));
        }
        public FoundationState Foundation { get; } public CultivationActionState Action { get; }
        public ClosedRetreatState Retreat { get; } public JindanLockState JindanLock { get; }
        public bool TryAddMansion(MansionState state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.MansionId) || mansions.ContainsKey(state.MansionId)) return false;
            mansions.Add(state.MansionId, state); return true;
        }
        public bool TryAddGuardian(GuardianAbilityState state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.MansionId) || guardians.ContainsKey(state.MansionId)) return false;
            guardians.Add(state.MansionId, state); return true;
        }
        public bool TryGetMansion(string id, out MansionState state) { return mansions.TryGetValue(id, out state); }
        public static CultivationState CreateEmpty()
        {
            return new CultivationState(
                new FoundationState(0, 0f, 0),
                new CultivationActionState(string.Empty, 0),
                new ClosedRetreatState(string.Empty, false, string.Empty),
                new JindanLockState(false, string.Empty));
        }

        public static CultivationState FromDefinition(FoundationPurpleMansionStateData definition)
        {
            if (definition == null) return CreateEmpty();
            FoundationStateRecord foundation = definition.foundationState;
            CultivationActionStateRecord action = definition.cultivationActionState;
            ClosedRetreatPlanRecord retreat = definition.closedRetreatPlan;
            JindanLockRecord jindan = definition.jindanLock;
            var state = new CultivationState(
                new FoundationState(
                    foundation == null ? 0 : (int)foundation.phase,
                    foundation == null ? 0f : foundation.continuousProgress,
                    foundation == null ? 0 : foundation.totalMansionCapacity),
                new CultivationActionState(
                    action == null ? string.Empty : action.actionStateId,
                    action == null ? 0 : (int)action.status),
                new ClosedRetreatState(
                    retreat == null ? string.Empty : retreat.actionStateId,
                    false,
                    string.Empty),
                new JindanLockState(
                    jindan != null && jindan.status == JindanLockStatus.Formed,
                    jindan != null && jindan.formationSnapshot != null
                        ? jindan.formationSnapshot.foundationInstanceId
                        : string.Empty));
            if (action != null)
            {
                foreach (string cycleId in action.committedCycleIds ?? new string[0])
                    state.Action.TryCommitCycle(cycleId);
            }
            foreach (PurpleMansionStateRecord mansion in definition.mansionStates ?? new PurpleMansionStateRecord[0])
            {
                if (mansion != null)
                    state.TryAddMansion(new MansionState(mansion.mansionKind.ToString(), (int)mansion.state, 0));
            }
            foreach (GuardianAbilityRecord guardian in definition.guardianAbilities ?? new GuardianAbilityRecord[0])
            {
                if (guardian != null)
                    state.TryAddGuardian(new GuardianAbilityState(
                        guardian.mansionInstanceId,
                        guardian.abilityInstanceId,
                        guardian.form.ToString()));
            }
            return state;
        }

        public static CultivationState FromSnapshot(CultivationStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            var state = CreateEmpty();
            state.Restore(snapshot);
            return state;
        }
        public CultivationStateSnapshot Capture()
        {
            var mansionSnapshots = new List<MansionStateSnapshot>(); foreach (MansionState state in mansions.Values) mansionSnapshots.Add(state.Capture());
            mansionSnapshots.Sort((left, right) => string.CompareOrdinal(left.MansionId, right.MansionId));
            var guardianSnapshots = new List<GuardianAbilityStateSnapshot>(); foreach (GuardianAbilityState state in guardians.Values) guardianSnapshots.Add(state.Capture());
            guardianSnapshots.Sort((left, right) => string.CompareOrdinal(left.MansionId, right.MansionId));
            return new CultivationStateSnapshot(Foundation.Capture(), mansionSnapshots, guardianSnapshots, Action.Capture(), Retreat.Capture(), JindanLock.Capture());
        }
        public void Restore(CultivationStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            Foundation.Restore(snapshot.Foundation); Action.Restore(snapshot.Action); Retreat.Restore(snapshot.Retreat); JindanLock.Restore(snapshot.JindanLock);
            mansions.Clear(); foreach (MansionStateSnapshot entry in snapshot.Mansions) TryAddMansion(new MansionState(entry.MansionId, entry.BuildState, entry.Capacity));
            guardians.Clear(); foreach (GuardianAbilityStateSnapshot entry in snapshot.Guardians) TryAddGuardian(new GuardianAbilityState(entry.MansionId, entry.AbilityInstanceId, entry.Form));
        }
    }
    public sealed class CultivationStateSnapshot
    {
        public CultivationStateSnapshot(FoundationStateSnapshot foundation, IEnumerable<MansionStateSnapshot> mansions, IEnumerable<GuardianAbilityStateSnapshot> guardians,
            CultivationActionStateSnapshot action, ClosedRetreatStateSnapshot retreat, JindanLockStateSnapshot jindanLock)
        {
            Foundation = foundation ?? throw new System.ArgumentNullException(nameof(foundation));
            Mansions = mansions == null ? new MansionStateSnapshot[0] : new List<MansionStateSnapshot>(mansions).ToArray();
            Guardians = guardians == null ? new GuardianAbilityStateSnapshot[0] : new List<GuardianAbilityStateSnapshot>(guardians).ToArray();
            Action = action ?? throw new System.ArgumentNullException(nameof(action)); Retreat = retreat ?? throw new System.ArgumentNullException(nameof(retreat));
            JindanLock = jindanLock ?? throw new System.ArgumentNullException(nameof(jindanLock));
        }
        public FoundationStateSnapshot Foundation { get; } public MansionStateSnapshot[] Mansions { get; }
        public GuardianAbilityStateSnapshot[] Guardians { get; } public CultivationActionStateSnapshot Action { get; }
        public ClosedRetreatStateSnapshot Retreat { get; } public JindanLockStateSnapshot JindanLock { get; }
    }
}
