using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatHudView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text playerText;
        [SerializeField] private Text enemyText;
        [SerializeField] private Text turnText;
        [SerializeField] private Image healthFill;
        [SerializeField] private Text healthLabel;
        [SerializeField] private Image spiritFill;
        [SerializeField] private Text spiritLabel;
        [SerializeField] private Image actionPointFill;
        [SerializeField] private Text actionPointLabel;

        public void Present(CombatHudSnapshot snapshot)
        {
            root?.SetActive(snapshot != null);
            if (snapshot == null) return;
            if (playerText != null) playerText.text = snapshot.Player?.DisplayName ?? string.Empty;
            PresentResource(healthFill, healthLabel, "生命", snapshot.Player?.CurrentHealth ?? 0,
                snapshot.Player?.MaximumHealth ?? 0);
            PresentResource(spiritFill, spiritLabel, "灵力", snapshot.Player?.CurrentSpirit ?? 0,
                snapshot.Player?.MaximumSpirit ?? 0);
            PresentResource(actionPointFill, actionPointLabel, "AP", snapshot.Movement?.RemainingPoints ?? 0,
                snapshot.ActionPointLimitUnits, true);
            if (enemyText != null) enemyText.text = Format(snapshot.Enemy);
            if (turnText != null) turnText.text = snapshot.TurnText;
        }

        public void Hide()
        {
            root?.SetActive(false);
        }

        private static void PresentResource(Image fill, Text label, string name, int current, int maximum,
            bool actionPoints = false)
        {
            if (fill != null) fill.fillAmount = maximum > 0 ? Mathf.Clamp01((float)current / maximum) : 0f;
            if (label != null) label.text = name + " " +
                (actionPoints ? CombatActionPreviewView.FormatActionPoints(current) : current.ToString()) + "/" +
                (actionPoints ? CombatActionPreviewView.FormatActionPoints(maximum) : maximum.ToString());
        }

        private static string Format(CombatantHudSnapshot combatant)
        {
            if (combatant == null) return string.Empty;
            return combatant.DisplayName + "\n生命 " + combatant.CurrentHealth + "/" + combatant.MaximumHealth +
                   "  灵力 " + combatant.CurrentSpirit + "/" + combatant.MaximumSpirit;
        }
    }
}
