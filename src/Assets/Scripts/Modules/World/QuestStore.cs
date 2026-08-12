using System;
using System.Collections.Generic;

namespace TianZhang.World
{
    public sealed class QuestStore
    {
        private Dictionary<string, QuestState> states =
            new Dictionary<string, QuestState>(StringComparer.Ordinal);

        public bool TryGet(string questId, out QuestState state) { return states.TryGetValue(questId, out state); }

        public bool TrySet(string questId, int step, bool completed)
        {
            if (string.IsNullOrWhiteSpace(questId) || step < 0) return false;
            states[questId] = new QuestState(questId, step, completed);
            return true;
        }

        public QuestStoreSnapshot Capture()
        {
            var entries = new List<QuestState>(states.Values);
            entries.Sort((left, right) => string.CompareOrdinal(left.QuestId, right.QuestId));
            return new QuestStoreSnapshot(entries);
        }

        public void Restore(QuestStoreSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var replacement = new Dictionary<string, QuestState>(StringComparer.Ordinal);
            foreach (QuestState state in snapshot.States)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.QuestId) || state.Step < 0 ||
                    replacement.ContainsKey(state.QuestId))
                {
                    throw new InvalidOperationException("Invalid quest snapshot.");
                }
                replacement.Add(state.QuestId, state);
            }
            states = replacement;
        }
    }

    public sealed class QuestState
    {
        public QuestState(string questId, int step, bool completed)
        {
            QuestId = questId;
            Step = step;
            Completed = completed;
        }

        public string QuestId { get; }
        public int Step { get; }
        public bool Completed { get; }
    }

    public sealed class QuestStoreSnapshot
    {
        public QuestStoreSnapshot(IEnumerable<QuestState> states)
        {
            States = states == null ? new QuestState[0] : new List<QuestState>(states).ToArray();
        }

        public QuestState[] States { get; }
    }
}
