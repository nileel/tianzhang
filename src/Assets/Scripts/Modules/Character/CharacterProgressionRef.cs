using System;
using System.Collections.Generic;

namespace TianZhang.Character
{
    /// <summary>Authoritative progression inputs, published only by the character aggregate.</summary>
    public sealed class CharacterProgressionRef
    {
        public CharacterProgressionRef(string gongFaId, string realmStage, float realmMultiplier,
            bool movementGrowthInputsPresent = true, MovementGrowthSource[] movementGrowthSources = null)
        { Restore(new CharacterProgressionSnapshot(gongFaId, realmStage, realmMultiplier,
            movementGrowthInputsPresent, movementGrowthSources ?? Array.Empty<MovementGrowthSource>())); }
        public string GongFaId { get; private set; }
        public string RealmStage { get; private set; }
        public float RealmMultiplier { get; private set; }
        public bool MovementGrowthInputsPresent { get; private set; }
        private MovementGrowthSource[] sources;
        public IReadOnlyList<MovementGrowthSource> MovementGrowthSources => Array.AsReadOnly(sources);
        public CharacterProgressionSnapshot Capture() => new CharacterProgressionSnapshot(
            GongFaId, RealmStage, RealmMultiplier, MovementGrowthInputsPresent, sources);
        internal void Restore(CharacterProgressionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            GongFaId = snapshot.GongFaId; RealmStage = snapshot.RealmStage; RealmMultiplier = snapshot.RealmMultiplier;
            MovementGrowthInputsPresent = snapshot.MovementGrowthInputsPresent;
            sources = snapshot.CopySources();
        }
    }
    public sealed class CharacterProgressionSnapshot
    {
        public CharacterProgressionSnapshot(string gongFaId, string realmStage, float realmMultiplier,
            bool movementGrowthInputsPresent = true, MovementGrowthSource[] movementGrowthSources = null)
        {
            GongFaId = gongFaId ?? string.Empty; RealmStage = realmStage ?? string.Empty; RealmMultiplier = realmMultiplier;
            MovementGrowthInputsPresent = movementGrowthInputsPresent;
            sources = movementGrowthSources == null ? null : (MovementGrowthSource[])movementGrowthSources.Clone();
            // The three-argument creation form explicitly declares no unlocked nodes.
            if (movementGrowthSources == null && movementGrowthInputsPresent) sources = Array.Empty<MovementGrowthSource>();
        }
        private readonly MovementGrowthSource[] sources;
        public string GongFaId { get; }
        public string RealmStage { get; }
        public float RealmMultiplier { get; }
        public bool MovementGrowthInputsPresent { get; }
        public IReadOnlyList<MovementGrowthSource> MovementGrowthSources => sources == null ? null : Array.AsReadOnly(sources);
        internal MovementGrowthSource[] CopySources() => sources == null ? null : (MovementGrowthSource[])sources.Clone();
    }
    /// <summary>A finite, already unlocked chapter/node fact; never a cached movement bonus.</summary>
    public sealed class MovementGrowthSource
    {
        private readonly int[] nodes;
        public MovementGrowthSource(string sourceInstanceId, string gongFaId, string chapterId, string realmId, int[] completedNodeIndices)
        {
            SourceInstanceId = sourceInstanceId; GongFaId = gongFaId; ChapterId = chapterId; RealmId = realmId;
            nodes = completedNodeIndices == null ? null : (int[])completedNodeIndices.Clone();
        }
        public string SourceInstanceId { get; }
        public string GongFaId { get; }
        public string ChapterId { get; }
        public string RealmId { get; }
        public IReadOnlyList<int> CompletedNodeIndices => nodes == null ? null : Array.AsReadOnly(nodes);
    }
}
