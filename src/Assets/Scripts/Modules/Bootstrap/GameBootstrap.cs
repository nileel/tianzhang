using UnityEngine;

namespace TianZhang.Bootstrap
{
    /// <summary>Unique Unity composition root. It only owns creation and lifetime wiring.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        private static GameBootstrap instance;

        public static GameRuntime Runtime { get; private set; }

        public static GameRuntime RequireRuntime()
        {
            if (instance == null) Runtime = null;
            if (instance != null && Runtime != null) return Runtime;

            GameBootstrap existing = instance != null
                ? instance
                : Object.FindFirstObjectByType<GameBootstrap>();
            if (existing == null)
            {
                var root = new GameObject("GameBootstrap");
                existing = root.AddComponent<GameBootstrap>();
            }
            if (instance == null) instance = existing;
            existing.EnsureRuntime();
            return Runtime;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureRuntime();
        }

        private void EnsureRuntime()
        {
            if (Runtime == null) Runtime = new GameRuntime();
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            instance = null;
            Runtime = null;
        }
    }
}
