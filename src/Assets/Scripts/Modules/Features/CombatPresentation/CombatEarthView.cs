using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Displays only committed earth objects and supplies their explicit edge support to the existing surface.</summary>
    public sealed class CombatEarthView : MonoBehaviour
    {
        [SerializeField] private GameObject wallPrefab;
        [SerializeField] private GameObject rampPrefab;
        [SerializeField] private GameObject wallDissolvePrefab;
        [SerializeField] private GameObject rampDissolvePrefab;
        private CombatBattlefieldSurface surface;
        private readonly HashSet<string> rampIds = new();
        private readonly List<GameObject> dissolving = new();
        private readonly Dictionary<string,GameObject> objects=new Dictionary<string,GameObject>();
        private string signature=string.Empty;
        public void ConfigureSurface(CombatBattlefieldSurface value) { surface=value; signature=string.Empty; }
        public void Present(CombatEarthObjectSnapshot[] values)
        {
            if (values == null) { Clear(); return; }
            string next=string.Join(";",values.Select(v=>v.Id+":"+v.Durability));
            if(signature==next) return;
            if(surface==null && values.Length>0) throw new InvalidOperationException("earth_surface_missing");
            var ids=values.Select(v=>v.Id).ToHashSet();
            foreach (string id in objects.Keys.Where(id => !ids.Contains(id)).ToArray())
            {
                var original = objects[id];
                var tail = Instantiate(rampIds.Remove(id) ? rampDissolvePrefab : wallDissolvePrefab,
                    original.transform.position, original.transform.rotation, transform);
                tail.transform.localScale = original.transform.localScale;
                original.SetActive(false);
                Destroy(original);
                objects.Remove(id);
                dissolving.RemoveAll(value => value == null);
                dissolving.Add(tail);
                Destroy(tail, 1f);
            }
            var ramps=new List<CombatBattlefieldSurface.EarthRamp>();
            foreach(var value in values)
            {
                Vector3 a=surface.Center(value.A),b=surface.Center(value.B);
                Vector3 low=a.y<=b.y?a:b, high=a.y<=b.y?b:a;
                Vector3 direction=high-low; direction.y=0; direction.Normalize();
                if(!objects.TryGetValue(value.Id,out var obj))
                {
                    obj=Instantiate(value.IsRamp?rampPrefab:wallPrefab,transform);obj.name=value.Id;objects.Add(value.Id,obj);
                    if (value.IsRamp) rampIds.Add(value.Id);
                }
                if(value.IsRamp)
                {
                    obj.transform.SetPositionAndRotation(low,Quaternion.LookRotation(direction));
                    obj.transform.localScale = new Vector3(1, (high.y-low.y)/.75f, 1);
                    ramps.Add(new CombatBattlefieldSurface.EarthRamp(value.A,value.B,low,high));
                }
                else
                {
                    Vector3 middle=(a+b)*.5f; middle.y=Mathf.Min(a.y,b.y);
                    Vector3 normal=b-a;normal.y=0;
                    obj.transform.SetPositionAndRotation(middle,Quaternion.LookRotation(normal.normalized));
                }
            }
            surface?.SetEarthRamps(ramps.ToArray()); signature=next;
        }
        private void OnDisable() => Clear();
        private void Clear()
        {
            if (objects.Count == 0 && dissolving.Count == 0) return;
            foreach (var obj in objects.Values.Concat(dissolving))
                if (obj != null) { obj.SetActive(false); Destroy(obj); }
            dissolving.Clear();
            rampIds.Clear();
            objects.Clear();surface?.SetEarthRamps(Array.Empty<CombatBattlefieldSurface.EarthRamp>());signature=string.Empty;
        }
    }
}
