using UnityEngine;

namespace TianZhang.Content
{
    [CreateAssetMenu(fileName = "Item_", menuName = "天章/内容/物品数据")]
    public sealed class ItemData : ScriptableObject
    {
        public string itemId;
        public string displayNameKey;
        public string descriptionKey;
        public string contentScope;
        public string itemCategory;
        public int maxStack;
        public string equipmentSlotId;
        public string equipmentRealmRequirementId;
        public string equipmentBasicAttackProfileId;
        public string combatUseEffectId;
        public string combatUseRealmId;
        public string combatUseTargetMode;
        public int combatUseHealthRecovery;
        public int initialGrantQuantity;

        public bool HasCombatUseFields => !string.IsNullOrEmpty(combatUseEffectId) ||
            !string.IsNullOrEmpty(combatUseRealmId) || !string.IsNullOrEmpty(combatUseTargetMode) ||
            combatUseHealthRecovery != 0 || initialGrantQuantity != 0;

        public bool IsApprovedCombatItem => itemId == "item_huoxue_dan" &&
            displayNameKey == itemId && descriptionKey == itemId + "_description" &&
            contentScope == "content_scope_production" && itemCategory == "pill" && maxStack == 99 &&
            combatUseEffectId == "restore_current_health" && combatUseRealmId == "realm_lianqi" &&
            combatUseTargetMode == "self" && combatUseHealthRecovery > 0 &&
            initialGrantQuantity > 0 && initialGrantQuantity <= maxStack &&
            string.IsNullOrEmpty(equipmentSlotId) && string.IsNullOrEmpty(equipmentRealmRequirementId) &&
            string.IsNullOrEmpty(equipmentBasicAttackProfileId);

        public bool IsApprovedEquipment => !HasCombatUseFields && contentScope == "content_scope_production" &&
            itemCategory == "weapon" && maxStack == 1 && equipmentSlotId == "main_hand" &&
            equipmentRealmRequirementId == "realm_lianqi" &&
            ((itemId == "item_iron_sword" && equipmentBasicAttackProfileId == "basic_iron_sword") ||
             (itemId == "item_iron_blade" && equipmentBasicAttackProfileId == "basic_iron_blade"));
    }
}
