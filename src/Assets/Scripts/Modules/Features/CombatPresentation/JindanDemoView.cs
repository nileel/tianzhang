using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

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
            if (key is "flight_tab" or "scale_tab" or "earth_tab")
            {
                flightActions.SetActive(key == "flight_tab"); scaleActions.SetActive(key == "scale_tab");
                earthActions.SetActive(key=="earth_tab");
                ActionRequested?.Invoke(key=="flight_tab"?"cancel_targeting":key); return;
            }
            if (key == "toggle_panel")
            {
                collapsed = !collapsed;
                ApplyCollapse();
                return;
            }
            ActionRequested?.Invoke(key);
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
            combatRoot.SetActive(true);
            foreach (ActionButton binding in buttons)
            {
                if (!binding.button.transform.IsChildOf(combatBody.transform)) continue;
                binding.button.interactable = binding.key == "confirm" ? canInput && canConfirm : canInput;
            }
            ApplyCollapse();
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
        }
    }
}
