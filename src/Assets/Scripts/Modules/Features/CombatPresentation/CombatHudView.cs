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

        public void Present(CombatHudSnapshot snapshot)
        {
            root?.SetActive(snapshot != null);
            if (snapshot == null) return;
            if (playerText != null) playerText.text = Format(snapshot.Player);
            if (enemyText != null) enemyText.text = Format(snapshot.Enemy);
            if (turnText != null) turnText.text = snapshot.TurnText;
        }

        public void Hide()
        {
            root?.SetActive(false);
        }

        private static string Format(CombatantHudSnapshot combatant)
        {
            if (combatant == null) return string.Empty;
            return combatant.DisplayName + "\nHP " + combatant.CurrentHealth + "/" + combatant.MaximumHealth +
                   "  灵力 " + combatant.CurrentSpirit + "/" + combatant.MaximumSpirit;
        }
    }
}
