using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Fits the approved static body's real soles to finite support, without changing its scale.</summary>
    internal sealed class CombatUnitSurfacePose
    {
        private readonly CombatBattlefieldSurface surface;
        private readonly Vector3[] soles;
        private readonly bool pair;
        public CombatUnitSurfacePose(CombatBattlefieldSurface surface, Transform body, bool pair)
        {
            this.surface = surface;
            this.pair = pair;
            Transform root = body.parent;
            var points = new List<Vector3>();
            foreach (MeshFilter filter in body.GetComponentsInChildren<MeshFilter>())
                points.AddRange(filter.sharedMesh.vertices.Select(v => root.InverseTransformPoint(filter.transform.TransformPoint(v))));
            float minimum = points.Min(p => p.y);
            soles = points.Where(p => p.y <= minimum+.01f).ToArray();
        }

        public void Evaluate(Vector3 center, float yaw, out Vector3 position, out Quaternion rotation)
        {
            float radians = yaw*Mathf.Deg2Rad;
            Vector3 right = new Vector3(Mathf.Cos(radians),0,-Mathf.Sin(radians));
            Vector3 forward = new Vector3(Mathf.Sin(radians),0,Mathf.Cos(radians));
            bool onStair = surface.IsStair(center);
            if (onStair)
            {
                rotation = Quaternion.Euler(0,yaw,0);
                center.y = Required(center,rotation,0);
                position = center;
                return;
            }
            float gradient = 0;
            if (surface.TryHeight(center+Vector3.forward*.01f,out float a) &&
                surface.TryHeight(center-Vector3.forward*.01f,out float b)) gradient = (a-b)/.02f;
            float pitch = Mathf.Atan(gradient*Mathf.Cos(radians));
            float roll = -Mathf.Atan2(gradient*Mathf.Sin(radians),Mathf.Sqrt(1+Mathf.Pow(gradient*Mathf.Cos(radians),2)));
            float initial = pair ? pitch : roll;
            float lo = initial-20*Mathf.Deg2Rad, hi = initial+20*Mathf.Deg2Rad;
            float Difference(float angle)
            {
                Quaternion q = Rotation(pair ? angle : pitch,pair ? roll : angle);
                return Required(center,q,1)-Required(center,q,2);
            }
            if(Mathf.Abs(Difference(initial))<.000001f)
            {
                rotation=Rotation(pitch,roll);
                center.y=Required(center,rotation,0);
                position=center;
                return;
            }
            float low = Difference(lo), high = Difference(hi);
            if (float.IsInfinity(low) || float.IsInfinity(high) || low*high>0)
                throw new InvalidOperationException("unit_sole_support_not_bracketed");
            for (int i=0;i<20;i++)
            {
                float mid = (lo+hi)*.5f;
                if (low*Difference(mid)<=0) hi=mid; else { lo=mid; low=Difference(mid); }
            }
            if (pair) pitch=(lo+hi)*.5f; else roll=(lo+hi)*.5f;
            rotation=Rotation(pitch,roll);
            center.y=Required(center,rotation,0);
            if (float.IsInfinity(center.y)) throw new InvalidOperationException("unit_sole_outside_support");
            position=center;

            Quaternion Rotation(float p,float r)
            {
                Vector3 f = forward*Mathf.Cos(p)+Vector3.up*Mathf.Sin(p);
                return Quaternion.LookRotation(f,-right*Mathf.Sin(r)+Vector3.Cross(f,right)*Mathf.Cos(r));
            }
        }

        private float Required(Vector3 center, Quaternion rotation, int group)
        {
            float height=float.NegativeInfinity;
            foreach (Vector3 sole in soles)
            {
                bool first = pair ? sole.z>0 : sole.x<0;
                if (group==1 && !first || group==2 && first) continue;
                Vector3 p=rotation*sole;
                if (surface.TryHeight(center+p,out float y)) height=Mathf.Max(height,y-p.y);
            }
            return height;
        }

        public void Place(Transform root,Vector3 center,float yaw)
        {
            Evaluate(center,yaw,out Vector3 p,out Quaternion q);
            root.SetPositionAndRotation(p,q);
        }

        /// <summary>For an explicit one-way drop, clear the ledge horizontally before descending to the supported landing.</summary>
        public System.Collections.IEnumerator Drop(Transform root, Vector3 start, Vector3 end, float yaw, float speed)
        {
            Evaluate(start, yaw, out Vector3 origin, out Quaternion fromRotation);
            Evaluate(end, yaw, out Vector3 landing, out Quaternion toRotation);
            if (origin.y <= landing.y) throw new InvalidOperationException("presentation_drop_not_descending");
            Vector3 corner = new Vector3(landing.x, origin.y, landing.z);
            float horizontal = Vector3.Distance(origin, corner);
            float vertical = origin.y - landing.y;
            float length = horizontal + vertical;
            float travelled = 0;
            root.SetPositionAndRotation(origin, fromRotation);
            while (travelled < length)
            {
                yield return null;
                travelled = Mathf.Min(length, travelled + Time.deltaTime * speed);
                Vector3 position = travelled < horizontal
                    ? Vector3.Lerp(origin, corner, travelled / horizontal)
                    : Vector3.Lerp(corner, landing, (travelled - horizontal) / vertical);
                root.SetPositionAndRotation(position, Quaternion.Slerp(fromRotation, toRotation, travelled / length));
            }
        }

        /// <summary>Translate horizontally only after lifting to the next true tread envelope; reverse on descent.</summary>
        public System.Collections.IEnumerator Move(Transform root, Vector3 start,Vector3 end,float yaw,float speed)
        {
            float distance=Vector3.Distance(new Vector3(start.x,0,start.z),new Vector3(end.x,0,end.z));
            int count=Mathf.Max(1,Mathf.CeilToInt(distance/.015f));
            Place(root,start,yaw);
            var path = new List<(Vector3 position, Quaternion rotation, float distance)>
                { (root.position, root.rotation, 0f) };
            for(int i=1;i<=count;i++)
            {
                Vector3 center=Vector3.Lerp(start,end,i/(float)count);
                Evaluate(center,yaw,out Vector3 next,out Quaternion q);
                Vector3 previous=path[path.Count-1].position;
                bool stair=surface.IsStair(center);
                if (stair && Mathf.Abs(next.y-previous.y)>.002f)
                {
                    Vector3 high=next.y>previous.y ? new Vector3(previous.x,next.y,previous.z) : new Vector3(next.x,previous.y,next.z);
                    Add(high,q);
                }
                Add(next,q);
            }
            // Spatial precision must not impose a frame wait on every sample or stair corner.
            float travelled=0, length=path[path.Count-1].distance;
            int segment=1;
            while(travelled<length)
            {
                yield return null;
                travelled=Mathf.Min(length,travelled+Time.deltaTime*speed);
                while(segment<path.Count-1 && path[segment].distance<travelled) segment++;
                var a=path[segment-1]; var b=path[segment];
                float t=(travelled-a.distance)/(b.distance-a.distance);
                root.SetPositionAndRotation(Vector3.Lerp(a.position,b.position,t),Quaternion.Slerp(a.rotation,b.rotation,t));
            }
            void Add(Vector3 p,Quaternion q)
            {
                var previous=path[path.Count-1];
                float step=Vector3.Distance(previous.position,p);
                if(step>0.000001f) path.Add((p,q,previous.distance+step));
            }
        }
    }
}
