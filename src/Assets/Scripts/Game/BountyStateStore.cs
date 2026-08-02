using System;
using System.Collections.Generic;

namespace TianZhang.Game
{
    /// <summary>
    /// 悬赏实例状态；无实例即为 <see cref="BountyStatus.Available"/>。
    /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Flash；变更范围：新增文件
    /// </summary>
    public enum BountyStatus
    {
        Available,
        Accepted,
        ObjectiveCompleted,
        Claimed,
    }

    /// <summary>
    /// 单个悬赏实例快照；进度必须非负，其余语义约束由 <see cref="BountyRuntime"/> 校验。
    /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Flash；变更范围：新增文件
    /// </summary>
    public sealed class BountyStateSnapshot
    {
        public string BountyId { get; }
        public BountyStatus Status { get; }
        public int Progress { get; }

        public BountyStateSnapshot(string bountyId, BountyStatus status, int progress)
        {
            if (string.IsNullOrWhiteSpace(bountyId))
                throw new ArgumentException("Bounty ID must not be empty.", nameof(bountyId));
            if (status < BountyStatus.Available || status > BountyStatus.Claimed)
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown bounty status.");
            if (progress < 0)
                throw new ArgumentOutOfRangeException(nameof(progress), progress, "Progress must not be negative.");

            BountyId = bountyId;
            Status = status;
            Progress = progress;
        }
    }

    /// <summary>
    /// 会话级悬赏实例存储；与 QuestStateStore 语义无关，独立保存悬赏状态。
    /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Flash；变更范围：新增文件
    /// </summary>
    public sealed class BountyStateStore
    {
        private Dictionary<string, BountyStateSnapshot> snapshots =
            new Dictionary<string, BountyStateSnapshot>(StringComparer.Ordinal);

        public int Count => snapshots.Count;
        internal IEnumerable<BountyStateSnapshot> Snapshots => snapshots.Values;

        public void Set(BountyStateSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Status == BountyStatus.Available)
                throw new ArgumentException("An Available bounty has no instance and must not be stored.", nameof(snapshot));
            snapshots[snapshot.BountyId] = snapshot;
        }

        public bool TryGet(string bountyId, out BountyStateSnapshot snapshot)
        {
            return snapshots.TryGetValue(bountyId, out snapshot);
        }

        public void Clear()
        {
            snapshots.Clear();
        }

        internal void ReplaceAll(IEnumerable<BountyStateSnapshot> source)
        {
            var replacement = new Dictionary<string, BountyStateSnapshot>(StringComparer.Ordinal);
            foreach (BountyStateSnapshot snapshot in source)
            {
                if (snapshot == null ||
                    snapshot.Status == BountyStatus.Available ||
                    !replacement.TryAdd(snapshot.BountyId, snapshot))
                {
                    throw new ArgumentException("Duplicate, null or Available bounty state.", nameof(source));
                }
            }
            snapshots = replacement;
        }
    }
}
