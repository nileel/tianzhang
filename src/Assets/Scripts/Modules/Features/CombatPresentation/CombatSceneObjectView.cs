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
        public void Configure(Binding[] values) => bindings = values;
        public void Present(IReadOnlyList<CombatSceneObjectSnapshot> states)
        {
            foreach (Binding binding in bindings)
            {
                var state = states?.FirstOrDefault(value => value.Id == binding.instanceId);
                GameObject visible = state?.State == 2 ? binding.residue : state?.State == 1 ? binding.damaged : binding.intact;
                foreach (GameObject item in new[] { binding.intact, binding.damaged, binding.residue }.Distinct())
                    if (item != null) item.SetActive(item == visible);
            }
        }
    }
}
