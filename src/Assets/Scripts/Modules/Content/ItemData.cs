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
    }
}
