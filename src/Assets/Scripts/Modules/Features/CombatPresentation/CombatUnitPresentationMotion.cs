using System.Collections;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Projects the supplied pose path; never chooses turns or movement legality.</summary>
    internal static class CombatUnitPresentationMotion
    {
        public static Vector3 Center(CombatUnitPresentationPose pose)
        {
            Vector3 front = Static3DCombatUnitPresentationProvider.HexToWorld(pose.Position);
            return pose.IsDoubleCell
                ? (front + Static3DCombatUnitPresentationProvider.HexToWorld(pose.Rear)) * .5f : front;
        }

        public static Vector3 BodyScale(Static3DCombatUnitPresentationProfile profile, bool isDoubleCell, float fill)
        {
            if (!isDoubleCell) return Vector3.one * 1.4f;
            Vector3 size = profile.approvedBoundsMax - profile.approvedBoundsMin;
            // Two unit-spaced hexes span two metres along their shared axis and one metre across.
            // The approved beast's long axis is local Z. Preserve its proportions in all axes.
            float scale = Mathf.Min(2f * fill / size.z, fill / size.x);
            return Vector3.one * scale;
        }

        public static IEnumerator AnimateStep(Transform root, CombatUnitPresentationMovementStep step,
            int[] yawDegrees, float cellsPerSecond)
        {
            if (step.From.IsDoubleCell)
            {
                Vector3 front = Static3DCombatUnitPresentationProvider.HexToWorld(step.From.Position);
                foreach (int facing in step.TurnFacings)
                {
                    Quaternion start = root.rotation;
                    Quaternion end = Quaternion.Euler(0f, yawDegrees[facing], 0f);
                    float progress = 0f;
                    while (progress < 1f)
                    {
                        yield return null;
                        progress = Mathf.Min(1f, progress + Time.deltaTime * cellsPerSecond);
                        root.rotation = Quaternion.Slerp(start, end, progress);
                        root.position = front - root.rotation * Vector3.forward * .5f;
                    }
                }
            }
            root.rotation = Quaternion.Euler(0f, yawDegrees[step.To.Facing], 0f);
            Vector3 origin = Center(new CombatUnitPresentationPose(step.From.Position,
                step.To.Facing, step.From.IsDoubleCell));
            Vector3 destination = Center(step.To);
            root.position = origin;
            float moveProgress = 0f;
            while (moveProgress < 1f)
            {
                yield return null;
                moveProgress = Mathf.Min(1f, moveProgress + Time.deltaTime * cellsPerSecond);
                root.position = Vector3.Lerp(origin, destination, moveProgress);
            }
        }
    }
}
