using UnityEngine;

namespace TianZhang.ClothWideSleevePilot
{
    // One independent trial per facing. Reset is outside the measured action interval.
    public static class ClothWideSleeveMotion
    {
        public const float TrialSeconds = 12f;
        public const float MoveSpeed = 1.25f;

        public struct Sample
        {
            public float armUp, armForward, forearm, yawOffset, distance;
            public string action;
        }

        public static Sample Evaluate(float time)
        {
            var s = new Sample { armUp = -0.62f, forearm = 0.08f, action = "RESET / SETTLE" };
            if (time < 2f) return s;
            if (time < 4f)
            {
                float pulse = Mathf.Pow(Mathf.Sin((time - 2f) * Mathf.PI * 0.5f), 2f);
                s.armUp += 1.34f * pulse;
                s.forearm -= 0.26f * pulse;
                s.action = "RAISE / LOWER";
            }
            else if (time < 6f)
            {
                float t = time - 4f;
                float window = Mathf.Pow(Mathf.Sin(t * Mathf.PI * 0.5f), 2f);
                s.armUp += window * (0.44f + 0.33f * Mathf.Sin(t * Mathf.PI * 2f));
                s.armForward = window * Mathf.Sin(t * Mathf.PI * 3f) * 0.82f;
                s.forearm += window * (-0.26f + 0.28f * Mathf.Sin(t * Mathf.PI * 2f));
                s.action = "PRONOUNCED SWING";
            }
            else if (time < 8f)
            {
                float t = time - 6f;
                s.yawOffset = -55f * Mathf.Sin(t * Mathf.PI) * Mathf.Sin(t * Mathf.PI * 0.5f);
                s.action = "BODY TURN / RETURN";
            }
            else
            {
                // Continuous position with an intentional velocity discontinuity at t=9.
                // The arm stays at rest, so the stop isolates translation inertia.
                s.distance = MoveSpeed * Mathf.Clamp(time - 8f, 0f, 1f);
                s.action = time < 9f ? "MOVE 1.25 m/s" : "ABRUPT STOP / RECOVER";
            }
            return s;
        }
    }
}
