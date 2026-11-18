using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using System.Linq;
using TianZhang.Gameplay.Contracts;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Displays the rehearsal state supplied by Adventure; owns no combat rules.</summary>
    public sealed class JindanDemoView : MonoBehaviour
    {
        [Serializable]
        public sealed class ActionButton
        {
            public string key;
            public Button button;
            [NonSerialized] public UnityAction listener;
        }

        [SerializeField] private GameObject preparationRoot;
        [SerializeField] private Text preparationDetails;
        [SerializeField] private GameObject combatRoot;
        [SerializeField] private GameObject combatBody;
        [SerializeField] private Text combatDetails;
        [SerializeField] private Text combatQuote;
        [SerializeField] private Text toggleLabel;
        [SerializeField] private GameObject flightActions;
        [SerializeField] private GameObject scaleActions;
        [SerializeField] private GameObject earthActions;
        [SerializeField] private GameObject tideActions;
        [SerializeField] private GameObject visibilityActions;
        [SerializeField] private GameObject fireActions;
        [SerializeField] private GameObject statusRoot;
        [SerializeField] private Text statusText;
        [SerializeField] private GameObject detailsRoot;
        [SerializeField] private CombatActionBarView actionBar;
        private JindanHudSnapshot hud;
        private string selectedKey;
        public string SelectedKey => selectedKey;
        public string PersistentStatus => statusText == null ? string.Empty : statusText.text;
        [SerializeField] private ActionButton[] buttons = Array.Empty<ActionButton>();
        private bool bound;
        private bool collapsed;

        public event Action<string> ActionRequested;

        private void Awake() => BindButtons();

        private void BindButtons()
        {
            if (bound) return;
            foreach (ActionButton binding in buttons)
            {
                string key = binding.key;
                binding.listener = () => Request(key);
                binding.button.onClick.AddListener(binding.listener);
            }
            bound = true;
        }

        private void OnDestroy()
        {
            foreach (ActionButton binding in buttons)
                if (binding.button != null && binding.listener != null)
                    binding.button.onClick.RemoveListener(binding.listener);
        }

        private void Request(string key)
        {
            if (key is "flight_tab" or "scale_tab" or "earth_tab" or "tide_tab" or "visibility_tab" or "fire_tab")
            {
                selectedKey = key;
                actionBar?.SelectJindan(GetButton(key));
                ApplySelection();
                ActionRequested?.Invoke(key); return;
            }
            if (key == "toggle_panel")
            {
                detailsRoot.SetActive(!detailsRoot.activeSelf);
                return;
            }
            if (key == "cancel_targeting") CloseCombatSelection();
            ActionRequested?.Invoke(key);
        }

        public void CloseCombatSelection()
        {
            selectedKey = null;
            actionBar?.SelectJindan(null);
            combatRoot.SetActive(false);
        }

        public void PresentHud(JindanHudSnapshot snapshot)
        {
            if (hud?.ActorId != snapshot?.ActorId) CloseCombatSelection();
            hud = snapshot;
            foreach (string key in new[] { "flight_tab", "scale_tab", "earth_tab", "tide_tab", "visibility_tab", "fire_tab" })
            {
                Button button = GetButton(key);
                if (button == null) continue;
                var ability = hud?.Abilities.FirstOrDefault(value => value.Key == key);
                button.gameObject.SetActive(ability != null);
                if (ability == null) continue;
                string shortName = ability.Name.Length > 5 ? ability.Name.Substring(0, 4) + "…" : ability.Name;
                button.GetComponentInChildren<Text>().text = shortName + "\n" + ability.Status;
                Text cooldown = button.transform.Find("Cooldown")?.GetComponent<Text>();
                if (cooldown != null) cooldown.text = ability.Cooldown > 0 ? "CD " + ability.Cooldown : string.Empty;
            }
            if (selectedKey != null && !hud.Abilities.Any(value => value.Key == selectedKey)) CloseCombatSelection();
            statusText.text = hud?.PersistentStatus ?? string.Empty;
            var statusRect = (RectTransform)statusRoot.transform;
            float top = statusRect.anchoredPosition.y + statusRect.rect.height * (1 - statusRect.pivot.y);
            float height = Mathf.Min(284, statusText.preferredHeight + 24);
            statusRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            statusRect.anchoredPosition = new Vector2(statusRect.anchoredPosition.x, top - height * (1 - statusRect.pivot.y));
            statusRoot.SetActive(!string.IsNullOrEmpty(statusText.text));
            GetButton("toggle_panel").gameObject.SetActive(hud != null);
            if (hud == null) detailsRoot.SetActive(false);
            ApplySelection();
        }

        private void ApplySelection()
        {
            flightActions.SetActive(selectedKey == "flight_tab");
            scaleActions.SetActive(selectedKey == "scale_tab");
            earthActions.SetActive(selectedKey == "earth_tab");
            tideActions.SetActive(selectedKey == "tide_tab");
            visibilityActions.SetActive(selectedKey == "visibility_tab");
            fireActions.SetActive(selectedKey == "fire_tab");
            var ability = hud?.Abilities.FirstOrDefault(value => value.Key == selectedKey);
            bool manifest = ability?.EffectId == "EARTH_MANIFEST";
            foreach (string key in new[] { "earth_wall", "earth_ramp" }) GetButton(key).gameObject.SetActive(manifest);
            foreach (string key in new[] { "earth_heavy", "earth_light", "earth_anchor", "earth_next_target" })
                GetButton(key).gameObject.SetActive(!manifest);
            GetButton("earth_swap").gameObject.SetActive(ability?.CanSwap == true);
            GetButton("swap_effect").gameObject.SetActive(false);
            GetButton("break_object").gameObject.SetActive(false); // Equipped demolition is already in the ordinary art slots.
        }

        public Button GetButton(string key)
        {
            BindButtons();
            foreach (ActionButton binding in buttons)
                if (binding.key == key) return binding.button;
            return null;
        }

        public void PresentPreparation(string details)
        {
            BindButtons();
            preparationDetails.text = details ?? string.Empty;
            preparationRoot.SetActive(true);
            preparationRoot.transform.SetAsLastSibling();
            combatRoot.SetActive(false);
        }

        public void HidePreparation() => preparationRoot.SetActive(false);

        public void PresentCombat(string details, string quote, bool canInput, bool canConfirm)
        {
            BindButtons();
            HidePreparation();
            combatDetails.text = details ?? string.Empty;
            combatQuote.text = quote ?? string.Empty;
            combatRoot.SetActive(selectedKey != null);
            foreach (ActionButton binding in buttons)
            {
                if (!binding.button.transform.IsChildOf(combatBody.transform)) continue;
                binding.button.interactable = binding.key == "confirm" ? canInput && canConfirm : canInput;
            }
            foreach (var ability in hud?.Abilities ?? Array.Empty<JindanHudAbility>())
                GetButton(ability.Key).interactable = canInput && ability.Enabled;
            combatBody.SetActive(true);
        }

        private void ApplyCollapse()
        {
            combatBody.SetActive(!collapsed);
            toggleLabel.text = collapsed ? "展开" : "收起";
        }

        public void HideAll()
        {
            preparationRoot.SetActive(false);
            combatRoot.SetActive(false);
            collapsed = false;
            PresentHud(null);
            CloseCombatSelection();
        }
    }
}
