using System;
using System.Linq;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class JindanScaleView : MonoBehaviour
    {
        [SerializeField] private GameObject supportPrefab, compressedPrefab, expandedPrefab;
        private CombatBattlefieldSurface surface;
        private GameObject support;
        private GameObject glow;
        private CombatUnitPresentationHex[] currentPath;
        private JindanSpaceBand[] bands = Array.Empty<JindanSpaceBand>();
        private int lastMetric, lastDurability;

        public void ConfigureSurface(CombatBattlefieldSurface value) => surface = value;

        public void Present(CombatUnitPresentationHex[] path, int metric, int durability, int maximum)
        {
            if (path == null || path.Length < 2)
            {
                if (currentPath != null) End();
                return;
            }
            if (surface == null) throw new InvalidOperationException("jindan_scale_surface_missing");
            if (metric != lastMetric || currentPath == null || !path.SequenceEqual(currentPath))
            {
                End();
                currentPath = path;
                lastMetric = metric;
                bands = new JindanSpaceBand[path.Length - 1];
                for (int i = 0; i < bands.Length; ++i)
                    bands[i] = JindanSpaceBand.Place(metric <= 1 ? compressedPrefab : expandedPrefab,
                        transform, surface, path[i], path[i + 1]);
                if (!support)
                {
                    support = Instantiate(supportPrefab, transform);
                    support.name = "JindanScaleSupport";
                    glow = support.transform.Find("Intact/Glow").gameObject;
                }
                var midpoint = (surface.Center(path[0]) + surface.Center(path[1])) * .5f;
                if (!surface.TryHeight(midpoint, out midpoint.y))
                    throw new InvalidOperationException("jindan_scale_support_outside_surface");
                support.transform.position = midpoint;
                support.SetActive(true);
            }
            else if (durability < lastDurability)
                foreach (var band in bands) band.Pulse(1);
            glow.SetActive(durability > 0 && durability >= maximum);
            lastDurability = durability;
        }

        private void End()
        {
            foreach (var band in bands) band.Dissolve();
            bands = Array.Empty<JindanSpaceBand>();
            currentPath = null;
            if (support) support.SetActive(false);
        }
    }
}
