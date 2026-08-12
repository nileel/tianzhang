using System;
using TianZhang.Features.WorldMap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TianZhang.Bootstrap
{
    public sealed class WorldSceneInstaller : MonoBehaviour
    {
        [SerializeField] private TextAsset languageTable;
        [SerializeField] private WorldMapController controller;
        [SerializeField] private WorldMapView view;

        private void Awake()
        {
            try
            {
                if (languageTable == null) throw new InvalidOperationException("world_language_table_missing");
                if (controller == null) throw new InvalidOperationException("world_controller_missing");
                if (view == null) throw new InvalidOperationException("world_view_missing");
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                controller.Configure(runtime, view, runtime.Navigation.WorldNodeId, SceneManager.LoadScene);
            }
            catch (InvalidOperationException exception)
            {
                if (controller != null) controller.enabled = false;
                Debug.LogError("[WorldInstaller] " + exception.Message);
            }
        }
    }
}
