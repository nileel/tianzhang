using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class JindanSealView : MonoBehaviour
    {
        [SerializeField] private GameObject supportMarker, supportPrefab, outerPrefab, innerPrefab;
        private CombatBattlefieldSurface surface;
        private GameObject intact, failed, glow;
        private JindanSpaceBand outerBand, innerBand;
        private int lastOuter, lastInner;

        public void ConfigureSurface(CombatBattlefieldSurface value) => surface = value;

        public void Present(CombatUnitPresentationHex[] path, int outer, int inner, bool destroyed)
        {
            if (!intact)
            {
                if (!supportMarker.activeInHierarchy) return;
                supportMarker.transform.Find("Intact").gameObject.SetActive(false);
                supportMarker.transform.Find("Failed").gameObject.SetActive(false);
                var replacement = Instantiate(supportPrefab, supportMarker.transform, false);
                intact = replacement.transform.Find("Intact").gameObject;
                failed = replacement.transform.Find("Failed").gameObject;
                glow = replacement.transform.Find("Intact/Glow").gameObject;
            }
            intact.SetActive(!destroyed);
            failed.SetActive(destroyed);
            glow.SetActive(outer > 0);
            UpdateLayer(ref outerBand, outerPrefab, path, lastOuter, outer);
            UpdateLayer(ref innerBand, innerPrefab, path, lastInner, inner);
            lastOuter = outer;
            lastInner = inner;
        }

        private void UpdateLayer(ref JindanSpaceBand band, GameObject prefab, CombatUnitPresentationHex[] path,
            int oldHealth, int health)
        {
            if (path != null && path.Length == 2 && health > 0)
            {
                if (!band) band = JindanSpaceBand.Place(prefab, transform, surface, path[0], path[1]);
                else if (health < oldHealth) band.Pulse(1);
            }
            else if (band)
            {
                band.Dissolve();
                band = null;
            }
        }
    }
}
