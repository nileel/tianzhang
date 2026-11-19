using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Only selects the existing D3.5 meshes. Durability and blocking belong to Combat.</summary>
    public sealed class CombatSceneObjectView : MonoBehaviour
    {
        [Serializable]
        public sealed class Binding
        {
            public string instanceId;
            public GameObject intact;
            public GameObject damaged;
            public GameObject residue;
        }
        [SerializeField] private Binding[] bindings = Array.Empty<Binding>();
        private readonly HashSet<string> erodedPlants = new();
        public Transform ErodePlant(string id)
        {
            var binding = bindings.FirstOrDefault(value => value.instanceId == id);
            if (binding == null || !erodedPlants.Add(id)) return null;
            binding.intact.SetActive(false);
            binding.residue.SetActive(false);
            return binding.intact.transform.parent;
        }
        public void ClearErosion() => erodedPlants.Clear();

        public void Configure(Binding[] values) => bindings = values;
        public void Present(IReadOnlyList<CombatSceneObjectSnapshot> states)
        {
            foreach (Binding binding in bindings)
            {
                var state = states?.FirstOrDefault(value => value.Id == binding.instanceId);
                GameObject visible = state?.State == 2 ? (erodedPlants.Contains(binding.instanceId) ? null : binding.residue) : state?.State == 1 ? binding.damaged : binding.intact;
                foreach (GameObject item in new[] { binding.intact, binding.damaged, binding.residue }.Distinct())
                    if (item != null) item.SetActive(item == visible);
            }
        }
    }
}
