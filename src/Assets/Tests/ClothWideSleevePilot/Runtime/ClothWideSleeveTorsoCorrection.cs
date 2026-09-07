using System;
using UnityEngine;

namespace TianZhang.ClothWideSleevePilot
{
    // One authored candidate for the frozen v001 mesh; no per-frame projection or mesh writes.
    // Derived from 11 Torso Contact 04 snapshots: proxy exit distance + 15 mm, rounded up to 1 mm.
    public static class ClothWideSleeveTorsoCorrection
    {
        private static readonly int[] Indices =
        {
            255,268,269,271,273,303,305,307,308,309,310,311,313,314,315,316,317,319,321,323,
            347,349,350,351,352,353,354,355,356,357,358,359,366,368,371,373,375,390,392,394,
            395,396,397,398,399,414,415,416,417,418,419,420,421,422,424,426,428,432,433,434,
            435,436,437,439,441,443,446,448,450,452,454
        };
        private static readonly float[] Distances =
        {
            .179f,.157f,.172f,.156f,.169f,.130f,.155f,.157f,.119f,.175f,.155f,.177f,.116f,.156f,
            .193f,.216f,.190f,.159f,.130f,.101f,.031f,.063f,.031f,.095f,.069f,.129f,.109f,.162f,
            .147f,.195f,.184f,.211f,.064f,.027f,.060f,.037f,.012f,.012f,.020f,.024f,.021f,.022f,
            .048f,.055f,.073f,.071f,.095f,.043f,.074f,.049f,.049f,.049f,.022f,.044f,.034f,.022f,
            .008f,.046f,.055f,.034f,.022f,.062f,.010f,.065f,.064f,.071f,.003f,.023f,.044f,.064f,.081f
        };

        // Call once, with Cloth disabled and the unchanged lowered-arm t=0 pose already applied.
        public static void Apply(Cloth cloth, Transform characterRoot)
        {
            var pairs = cloth.sphereColliders;
            if (cloth.enabled || pairs.Length != 3 || pairs[0].first == null || pairs[0].second == null ||
                pairs[0].first.name != "ClothCollider_TorsoLow" || pairs[0].second.name != "ClothCollider_TorsoHigh" ||
                Mathf.Abs(pairs[0].first.radius - .22f) > .0001f ||
                Mathf.Abs(pairs[0].second.radius - .14f) > .0001f)
                throw new InvalidOperationException("Torso candidate requires the disabled, unmodified v001 baseline.");

            var coefficients = cloth.coefficients;
            ClothWideSleevePilotController.ReleaseDiagnosedPins(coefficients);
            for (int i = 0; i < Indices.Length; i++)
                coefficients[Indices[i]].maxDistance = Mathf.Max(coefficients[Indices[i]].maxDistance, Distances[i]);

            SetSphere(pairs[0].first, new Vector3(.01f, .995f, .025f), .18f);
            SetSphere(pairs[0].second, new Vector3(.01f, 1.43f, .025f), .225f);
            cloth.coefficients = coefficients;
            // Re-register the same references after updating their authored geometry; arm pairs are untouched.
            cloth.sphereColliders = pairs;

            void SetSphere(SphereCollider sphere, Vector3 reference, float radius)
            {
                sphere.center = sphere.transform.InverseTransformPoint(characterRoot.TransformPoint(reference));
                sphere.radius = radius;
            }
        }
    }
}
