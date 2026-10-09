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

        public bool IsApprovedEquipment => contentScope == "content_scope_production" &&
            itemCategory == "weapon" && maxStack == 1 && equipmentSlotId == "main_hand" &&
            equipmentRealmRequirementId == "realm_lianqi" &&
            ((itemId == "item_iron_sword" && equipmentBasicAttackProfileId == "basic_iron_sword") ||
             (itemId == "item_iron_blade" && equipmentBasicAttackProfileId == "basic_iron_blade"));
    }
}
