using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Projects the already placed fire_domain_support; fuel and rules stay in Combat.</summary>
    public sealed class JindanFireDomainView : MonoBehaviour
    {
        [SerializeField] private GameObject supportMarker;
        private GameObject intact, glow;
        private void Awake()
        {
            intact = supportMarker.transform.Find("Intact").gameObject;
            glow = supportMarker.transform.Find("Intact/Glow").gameObject;
        }
        public void Present(bool deployed, bool destroyed)
        {
            intact.SetActive(!destroyed);
            supportMarker.transform.Find("Failed").gameObject.SetActive(false);
            glow.SetActive(deployed && !destroyed);
        }
    }
}
