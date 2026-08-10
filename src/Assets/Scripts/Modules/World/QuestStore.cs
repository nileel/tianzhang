using System.Collections.Generic;

namespace TianZhang.World
{
    public sealed class QuestStore
    {
        private readonly Dictionary<string, QuestState> states = new Dictionary<string, QuestState>();
        public bool TryGet(string questId, out QuestState state) { return states.TryGetValue(questId, out state); }
        public bool TrySet(string questId, int step, bool completed)
        {
            if (string.IsNullOrWhiteSpace(questId) || step < 0) return false;
            states[questId] = new QuestState(questId, step, completed); return true;
        }
        public QuestStoreSnapshot Capture() { return new QuestStoreSnapshot(states.Values); }
        public void Restore(QuestStoreSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            states.Clear(); foreach (QuestState state in snapshot.States) if (!TrySet(state.QuestId, state.Step, state.Completed)) throw new System.InvalidOperationException("Invalid quest snapshot.");
        }
    }
    public sealed class QuestState { public QuestState(string questId, int step, bool completed) { QuestId = questId; Step = step; Completed = completed; } public string QuestId { get; } public int Step { get; } public bool Completed { get; } }
    public sealed class QuestStoreSnapshot { public QuestStoreSnapshot(IEnumerable<QuestState> states) { States = states == null ? new QuestState[0] : new List<QuestState>(states).ToArray(); } public QuestState[] States { get; } }
}
