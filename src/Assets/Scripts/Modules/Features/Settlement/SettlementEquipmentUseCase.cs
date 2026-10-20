using System.Collections.Generic;
using TianZhang.Content;

namespace TianZhang.Features.Settlement
{
    /// <summary>
    /// Out-of-combat main-hand changes, the growth step between two formal fights. Only the
    /// already-owned equipment instances from the formal catalog can be selected; combat keeps its
    /// own in-battle swap with AP/CT costs.
    /// </summary>
    public interface ISettlementEquipmentUseCase
    {
        string CurrentMainHandItemId { get; }
        IReadOnlyList<string> OwnedMainHandItemIds(ContentCatalogData catalog);

        /// <returns><c>null</c> on success, otherwise a stable reason.</returns>
        string EquipMainHand(ContentCatalogData catalog, string itemId);
    }

    public static class SettlementEquipmentReasons
    {
        public const string Unavailable = "settlement_equipment_unavailable";
        public const string CatalogMissing = "settlement_equipment_catalog_missing";
        public const string PlayerMissing = "settlement_equipment_player_missing";
        public const string ItemInvalid = "settlement_equipment_item_invalid";
        public const string NotOwned = "settlement_equipment_not_owned";
        public const string Equipped = "settlement_equipment_equipped";
    }
}
