using System.Collections.Generic;
using System.Linq;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Projects the public center, main domain and the transition into paid embers.</summary>
    public sealed class JindanFireDomainView : MonoBehaviour
    {
        [SerializeField] private GameObject supportMarker;
        [SerializeField] private GameObject fireCenterPrefab;
        [SerializeField] private GameObject warmEdgePrefab;
        [SerializeField] private GameObject embersPrefab;

        private CombatBattlefieldSurface surface;
        private GameObject center, glow, warmth;
        private readonly List<Mesh> fittedMeshes = new();
        private readonly Dictionary<CombatUnitPresentationHex, GameObject> embers = new();
        private readonly List<CombatUnitPresentationHex> expired = new();

        public void ConfigureSurface(CombatBattlefieldSurface value) => surface = value;

        public void Present(bool deployed, bool destroyed, IReadOnlyCollection<CombatUnitPresentationHex> tailCells,
            CombatUnitPresentationHex? centerCell)
        {
            if (!centerCell.HasValue)
            {
                RestoreMarker();
                return;
            }
            if (!center)
            {
                center = Instantiate(fireCenterPrefab, surface.Center(centerCell.Value), Quaternion.identity, transform);
                center.transform.Find("Failed").gameObject.SetActive(false);
                glow = center.transform.Find("Intact/Glow").gameObject;
            }
            supportMarker.SetActive(false);
            center.SetActive(!destroyed);
            glow.SetActive(deployed && !destroyed);
            if (deployed && !warmth) CreateWarmth();
            if (warmth) warmth.SetActive(deployed);
            UpdateEmbers(tailCells);
        }

        private void CreateWarmth()
        {
            warmth = new GameObject("FireDomainWarmth");
            warmth.transform.SetParent(transform, false);
            foreach (var support in surface.Cells)
            {
                var cell = new CombatUnitPresentationHex(support.q, support.r);
                var edge = Instantiate(warmEdgePrefab, surface.Center(cell), Quaternion.identity, warmth.transform);
                if (support.kind == 0)
                {
                    edge.transform.position += Vector3.up * .025f;
                    continue;
                }
                var filter = edge.GetComponent<MeshFilter>();
                var mesh = CombatSurfaceMesh.Fit(filter.sharedMesh, edge.transform, surface, support, .025f);
                filter.sharedMesh = mesh;
                fittedMeshes.Add(mesh);
            }
        }

        private void UpdateEmbers(IReadOnlyCollection<CombatUnitPresentationHex> tailCells)
        {
            // LowFire remains in the surface slot for the whole tail. Each transition gets one short burst.
            foreach (var cell in tailCells)
            {
                if (embers.ContainsKey(cell)) continue;
                var burst = Instantiate(embersPrefab, surface.Center(cell) + Vector3.up * .025f,
                    Quaternion.identity, transform);
                embers.Add(cell, burst);
            }
            foreach (var cell in embers.Keys)
                if (!tailCells.Contains(cell)) expired.Add(cell);
            foreach (var cell in expired)
            {
                embers[cell].SetActive(false);
                Destroy(embers[cell]);
                embers.Remove(cell);
            }
            expired.Clear();
        }

        private void RestoreMarker()
        {
            if (!center) return;
            if (supportMarker) supportMarker.SetActive(true);
            center.SetActive(false);
            if (warmth) warmth.SetActive(false);
            UpdateEmbers(System.Array.Empty<CombatUnitPresentationHex>());
        }

        private void OnDisable() => RestoreMarker();
        private void OnDestroy()
        {
            foreach (var mesh in fittedMeshes) Destroy(mesh);
        }
    }
}
