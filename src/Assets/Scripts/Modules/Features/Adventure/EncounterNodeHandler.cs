using System;
using TianZhang.Content;

namespace TianZhang.Features.Adventure
{
    public sealed class EncounterNodeHandler : IAdventureNodeHandler
    {
        public const string StableNodeTypeId = "adventure_node_encounter";
        private readonly AdventureNodeAction beginEncounter;

        public EncounterNodeHandler(AdventureNodeAction action)
        {
            beginEncounter = action ?? throw new ArgumentNullException(nameof(action));
        }

        public string NodeTypeId => StableNodeTypeId;

        public bool TryValidate(AdventureNodeData node, ContentCatalogData catalog, out string reason)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(node?.contentId) ||
                !catalog.TryGetEnemy(node.contentId, out EnemyData enemy) || enemy.combatTemplate == null)
            {
                reason = "adventure_encounter_enemy_unresolved";
                return false;
            }
            reason = null;
            return true;
        }

        public bool TryHandle(AdventureNodeData node, out string reason) => beginEncounter(node, out reason);
    }
}
