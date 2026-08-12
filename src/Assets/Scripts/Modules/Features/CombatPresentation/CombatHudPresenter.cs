using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatHudPresenter : MonoBehaviour, ICombatPresentationSink
    {
        [SerializeField] private CombatHudView view;
        [SerializeField] private CombatCommandInput commandInput;
        [SerializeField] private CombatLogView logView;

        public void Configure(
            CombatHudView hudView,
            CombatCommandInput input,
            CombatLogView combatLog)
        {
            view = hudView;
            commandInput = input;
            logView = combatLog;
        }

        public void Present(CombatHudSnapshot snapshot)
        {
            view?.Present(snapshot);
            string art = snapshot != null && snapshot.ArtProfileIds.Count > 0 ? snapshot.ArtProfileIds[0] : null;
            string divine = snapshot != null && snapshot.DivineProfileIds.Count > 0 ? snapshot.DivineProfileIds[0] : null;
            commandInput?.SetContext(
                snapshot?.Player?.Id,
                snapshot?.Enemy?.Id,
                art,
                divine,
                snapshot != null && snapshot.AcceptsCommands);
        }

        public void ClearLog() => logView?.Clear();
        public void AppendLog(string message) => logView?.Append(message);

        public void Hide()
        {
            view?.Hide();
            commandInput?.SetContext(null, null, null, null, false);
        }
    }
}
