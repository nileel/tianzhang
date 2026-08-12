using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CharacterCreation
{
    public sealed class StartMenuView : MonoBehaviour
    {
        [SerializeField] private Button newPlayerButton;
        [SerializeField] private Transform slotContainer;
        [SerializeField] private Text emptyText;
        [SerializeField] private Text failureText;

        private Action<string> loadPlayer;

        public void Configure(Action openNewPlayer, Action<string> onLoadPlayer)
        {
            loadPlayer = onLoadPlayer ?? throw new ArgumentNullException(nameof(onLoadPlayer));
            if (newPlayerButton == null) return;
            newPlayerButton.onClick.RemoveAllListeners();
            newPlayerButton.onClick.AddListener(() => openNewPlayer());
        }

        public void ShowSlots(IReadOnlyList<PlayerSlotSummary> slots)
        {
            if (slotContainer != null)
            {
                for (int i = slotContainer.childCount - 1; i >= 0; i--)
                    Destroy(slotContainer.GetChild(i).gameObject);
                foreach (PlayerSlotSummary slot in slots)
                    CreateSlotButton(slot);
            }
            if (emptyText != null) emptyText.gameObject.SetActive(slots == null || slots.Count == 0);
            if (failureText != null) failureText.text = string.Empty;
        }

        public void ShowFailure(string reason)
        {
            if (failureText != null) failureText.text = reason;
        }

        private void CreateSlotButton(PlayerSlotSummary slot)
        {
            if (slotContainer == null) return;
            var go = new GameObject(
                "SaveSlot_" + slot.SlotId,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(slotContainer, false);
            go.GetComponent<LayoutElement>().preferredHeight = 48f;
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            Text label = labelGo.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.alignment = TextAnchor.MiddleCenter;
            label.text = slot.CanLoad
                ? slot.SlotId + " · " + slot.DisplayName
                : slot.SlotId + " · 无法读取（" + slot.FailureReason + "）";
            RectTransform rect = labelGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            Button button = go.GetComponent<Button>();
            button.interactable = slot.CanLoad;
            string slotId = slot.SlotId;
            button.onClick.AddListener(() => loadPlayer(slotId));
        }
    }
}
