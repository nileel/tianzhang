using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CharacterCreation
{
    public sealed class StartMenuView : MonoBehaviour
    {
        private const float DeleteButtonWidth = 96f;

        [SerializeField] private Button newPlayerButton;
        [SerializeField] private Transform slotContainer;
        [SerializeField] private Text emptyText;
        [SerializeField] private Text failureText;
        [SerializeField] private GameObject deleteConfirmPanel;
        [SerializeField] private Text deleteConfirmText;
        [SerializeField] private Button deleteConfirmButton;
        [SerializeField] private Button deleteCancelButton;

        private Action<string> loadPlayer;
        private Action<string> deletePlayer;
        private string pendingDeleteSlotId;

        public void Configure(
            Action openNewPlayer,
            Action<string> onLoadPlayer,
            Action<string> onDeletePlayer)
        {
            loadPlayer = onLoadPlayer ?? throw new ArgumentNullException(nameof(onLoadPlayer));
            deletePlayer = onDeletePlayer ?? throw new ArgumentNullException(nameof(onDeletePlayer));
            if (newPlayerButton != null)
            {
                newPlayerButton.onClick.RemoveAllListeners();
                newPlayerButton.onClick.AddListener(() => openNewPlayer());
            }
            if (deleteConfirmButton != null)
            {
                deleteConfirmButton.onClick.RemoveAllListeners();
                deleteConfirmButton.onClick.AddListener(ConfirmDelete);
            }
            if (deleteCancelButton != null)
            {
                deleteCancelButton.onClick.RemoveAllListeners();
                deleteCancelButton.onClick.AddListener(CancelDelete);
            }
            HideDeleteConfirmation();
        }

        public void ShowSlots(IReadOnlyList<PlayerSlotSummary> slots)
        {
            HideDeleteConfirmation();
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
            go.GetComponent<Image>().color = new Color(0.2f, 0.34f, 0.3f, 1f);
            LayoutElement rowLayout = go.GetComponent<LayoutElement>();
            rowLayout.minHeight = rowLayout.preferredHeight = 48f;
            rowLayout.flexibleHeight = 0f;
            CreateLabel(
                go.transform,
                slot.CanLoad
                    ? slot.SlotId + " · " + slot.DisplayName
                    : slot.SlotId + " · 无法读取（" + slot.FailureReason + "）",
                18,
                slot.CanDelete ? DeleteButtonWidth : 0f);
            Button button = go.GetComponent<Button>();
            button.interactable = slot.CanLoad;
            string slotId = slot.SlotId;
            button.onClick.AddListener(() => loadPlayer(slotId));
            if (slot.CanDelete)
                CreateDeleteButton(go.transform, slot);
        }

        private void CreateDeleteButton(Transform row, PlayerSlotSummary slot)
        {
            var go = new GameObject(
                "DeleteSlot_" + slot.SlotId,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            go.transform.SetParent(row, false);
            go.GetComponent<Image>().color = new Color(0.42f, 0.2f, 0.2f, 1f);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-6f, 0f);
            rect.sizeDelta = new Vector2(DeleteButtonWidth - 12f, 34f);
            CreateLabel(go.transform, "删除", 16, 0f);
            go.GetComponent<Button>().onClick.AddListener(() => RequestDelete(slot));
        }

        private void RequestDelete(PlayerSlotSummary slot)
        {
            pendingDeleteSlotId = slot.SlotId;
            if (deleteConfirmPanel != null) deleteConfirmPanel.SetActive(true);
            if (deleteConfirmText == null) return;
            deleteConfirmText.text = slot.CanLoad
                ? "确认删除存档 " + slot.SlotId + "（" + slot.DisplayName + "）？\n删除后不可恢复。"
                : "确认删除存档 " + slot.SlotId + "？\n该存档无法读取。\n删除后不可恢复。";
        }

        private void ConfirmDelete()
        {
            string slotId = pendingDeleteSlotId;
            HideDeleteConfirmation();
            if (string.IsNullOrEmpty(slotId)) return;
            deletePlayer(slotId);
        }

        private void CancelDelete()
        {
            HideDeleteConfirmation();
        }

        private void HideDeleteConfirmation()
        {
            pendingDeleteSlotId = null;
            if (deleteConfirmPanel != null) deleteConfirmPanel.SetActive(false);
        }

        private static Text CreateLabel(Transform parent, string value, int fontSize, float rightInset)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.color = new Color(0.91f, 0.88f, 0.77f, 1f);
            label.raycastTarget = false;
            label.alignment = TextAnchor.MiddleCenter;
            label.text = value;
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.offsetMax = new Vector2(-rightInset, 0f);
            return label;
        }
    }
}
