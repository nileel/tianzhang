using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Fits the approved static body's real soles to finite support, without changing its scale.</summary>
    internal sealed class CombatUnitSurfacePose
    {
#if UNITY_EDITOR
        internal static Action<int, int, double> PathMeasured;
        internal static Action<int, double> SamplesMeasured;
#endif
        private readonly CombatBattlefieldSurface surface;
        private readonly Vector3[] soles;
        private readonly SoleNode firstSoles, secondSoles;
        private readonly float soleCoordinateScale;
        private readonly Vector3 soleMiddle, soleExtent;
        private readonly List<CombatBattlefieldSurface.Cell> heightCells = new();
        private Vector3 axisX, axisY, axisZ, extentX, extentY, extentZ;
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
            soles = points.Where(p => p.y <= minimum+.01f).Distinct().ToArray();
            bool First(Vector3 p) => pair ? p.z>0 : p.x<0;
            int firstCount=soles.Count(First);
            soles=soles.Where(First).Concat(soles.Where(p=>!First(p))).ToArray();
            soleCoordinateScale=soles.Max(p=>Mathf.Abs(p.x)+Mathf.Abs(p.y)+Mathf.Abs(p.z));
            Vector3 minimumSole=soles.Aggregate(Vector3.Min),maximumSole=soles.Aggregate(Vector3.Max);
            soleMiddle=(minimumSole+maximumSole)*.5f;soleExtent=(maximumSole-minimumSole)*.5f;
            firstSoles=BuildSoles(0,firstCount); secondSoles=BuildSoles(firstCount,soles.Length-firstCount);
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
                center.y = Required(center,rotation).height;
                position = center;
                return;
            }
            float gradient = 0, gradientX=0;
            if (surface.TryHeight(center+Vector3.forward*.01f,out float a) &&
                surface.TryHeight(center-Vector3.forward*.01f,out float b)) gradient = (a-b)/.02f;
            if(surface.TryHeight(center+Vector3.right*.01f,out float rightHeight) &&
                surface.TryHeight(center-Vector3.right*.01f,out float leftHeight)) gradientX=(rightHeight-leftHeight)/.02f;
            float forwardGradient=gradient*Mathf.Cos(radians)+gradientX*Mathf.Sin(radians);
            float rightGradient=gradientX*Mathf.Cos(radians)-gradient*Mathf.Sin(radians);
            float pitch = Mathf.Atan(forwardGradient);
            // A single-cell manifested ramp is a short climb: keep the person upright along the climb,
            // while the existing sole fit still resolves left/right support and required lift.
            if (!pair && surface.IsEarthRamp(center)) pitch = 0;
            float roll = Mathf.Atan2(rightGradient,Mathf.Sqrt(1+forwardGradient*forwardGradient));
            float initial = pair ? pitch : roll;
            float lo = initial-20*Mathf.Deg2Rad, hi = initial+20*Mathf.Deg2Rad;
            float Difference(float angle)
            {
                Quaternion q = Rotation(pair ? angle : pitch,pair ? roll : angle);
                var support = Required(center,q);
                return support.first-support.second;
            }
            var initialRotation = Rotation(pitch,roll);
            var initialSupport = Required(center,initialRotation);
            if(Mathf.Abs(initialSupport.first-initialSupport.second)<.000001f)
            {
                rotation=initialRotation;
                center.y=initialSupport.height;
                position=center;
                return;
            }
            float low = Difference(lo), high = Difference(hi);
            if (float.IsInfinity(low) || float.IsInfinity(high) || low*high>0)
                throw new InvalidOperationException("unit_sole_support_not_bracketed");
            for (int i=0;i<20;i++)
            {
                float mid = (lo+hi)*.5f;
                float difference = Difference(mid);
                if (low*difference<=0) hi=mid; else { lo=mid; low=difference; }
            }
            if (pair) pitch=(lo+hi)*.5f; else roll=(lo+hi)*.5f;
            rotation=Rotation(pitch,roll);
            center.y=Required(center,rotation).height;
            if (float.IsInfinity(center.y)) throw new InvalidOperationException("unit_sole_outside_support");
            position=center;

            Quaternion Rotation(float p,float r)
            {
                Vector3 f = forward*Mathf.Cos(p)+Vector3.up*Mathf.Sin(p);
                return Quaternion.LookRotation(f,-right*Mathf.Sin(r)+Vector3.Cross(f,right)*Mathf.Cos(r));
            }
        }

        private (float height, float first, float second) Required(Vector3 center, Quaternion rotation)
        {
            // Padding only makes bounds less selective; every accepted height still comes from
            // an original vertex and the original finite-support query. Include coordinate scale
            // in XZ as well as Y so rounding cannot omit a stair or a higher neighboring tile.
            float padding=2e-6f*(1+soleCoordinateScale+Mathf.Abs(center.x)+Mathf.Abs(center.y)+Mathf.Abs(center.z));
            axisX=rotation*Vector3.right;axisY=rotation*Vector3.up;axisZ=rotation*Vector3.forward;
            extentX=Abs(axisX);extentY=Abs(axisY);extentZ=Abs(axisZ);
            RotateBounds(soleMiddle,soleExtent,rotation,padding,out _,out var minimum,out var maximum);
            surface.CollectHeightCells(center+minimum,center+maximum,heightCells);
            float first=float.NegativeInfinity,second=float.NegativeInfinity;
            Search(firstSoles,Bound(firstSoles,center,rotation,padding),ref first);
            Search(secondSoles,Bound(secondSoles,center,rotation,padding),ref second);
            return (Mathf.Max(first,second),first,second);

            void Search(SoleNode node,float upper,ref float best)
            {
                if(node==null || upper<=best)return;
                if(node.Left==null)
                {
                    for(int i=node.Start;i<node.Start+node.Count;i++)
                    {
                        Vector3 p=rotation*soles[i];
                        if(surface.TryHeight(center+p,heightCells,out float y))best=Mathf.Max(best,y-p.y);
                    }
                    return;
                }
                float left=Bound(node.Left,center,rotation,padding),right=Bound(node.Right,center,rotation,padding);
                if(left>=right){Search(node.Left,left,ref best);Search(node.Right,right,ref best);}
                else {Search(node.Right,right,ref best);Search(node.Left,left,ref best);}
            }
        }

        private float Bound(SoleNode node,Vector3 center,Quaternion rotation,float padding)
        {
            if(node==null)return float.NegativeInfinity;
            RotateBounds(node.Middle,node.Extent,rotation,padding,out var middle,out var minimum,out var maximum);
            return surface.SoleHeightUpperBound(heightCells,middle,node.Extent,axisX,axisY,axisZ,
                center,center+minimum,center+maximum,padding);
        }

        private void RotateBounds(Vector3 localMiddle,Vector3 extent,Quaternion rotation,float padding,
            out Vector3 middle,out Vector3 minimum,out Vector3 maximum)
        {
            middle=rotation*localMiddle;
            Vector3 half=extentX*extent.x+extentY*extent.y+extentZ*extent.z+Vector3.one*padding;
            minimum=middle-half;maximum=middle+half;
        }

        private static Vector3 Abs(Vector3 value)=>new(Mathf.Abs(value.x),Mathf.Abs(value.y),Mathf.Abs(value.z));

        private SoleNode BuildSoles(int start,int count)
        {
            if(count==0)return null;
            Vector3 minimum=soles[start],maximum=soles[start];
            for(int i=start+1;i<start+count;i++)
            {minimum=Vector3.Min(minimum,soles[i]);maximum=Vector3.Max(maximum,soles[i]);}
            var node=new SoleNode {Start=start,Count=count,Middle=(minimum+maximum)*.5f,Extent=(maximum-minimum)*.5f};
            if(count<=8)return node;
            Vector3 size=maximum-minimum;
            int axis=size.x>=size.y && size.x>=size.z?0:size.y>=size.z?1:2;
            Array.Sort(soles,start,count,Comparer<Vector3>.Create((a,b)=>a[axis].CompareTo(b[axis])));
            int half=count/2;node.Left=BuildSoles(start,half);node.Right=BuildSoles(start+half,count-half);
            return node;
        }

        private sealed class SoleNode
        {
            public int Start,Count;
            public Vector3 Middle,Extent;
            public SoleNode Left,Right;
        }

        public void Place(Transform root,Vector3 center,float yaw)
        {
            Evaluate(center,yaw,out Vector3 p,out Quaternion q);
            root.SetPositionAndRotation(p,q);
        }

        /// <summary>Only an explicitly quoted single-edge light leap clears the high ledge, then lands on real support.</summary>
        public System.Collections.IEnumerator Leap(Transform root, Vector3 start, Vector3 end, float yaw, float speed)
        {
            Evaluate(start, yaw, out Vector3 origin, out Quaternion fromRotation);
            Evaluate(end, yaw, out Vector3 landing, out Quaternion toRotation);
            float apex = Mathf.Max(origin.y, landing.y) + .35f;
            var points = new[] { origin, new Vector3(origin.x, apex, origin.z), new Vector3(landing.x, apex, landing.z), landing };
            for (int i = 1; i < points.Length; i++)
            {
                float length = Vector3.Distance(points[i-1], points[i]);
                for (float distance = 0; distance < length;)
                {
                    yield return null;
                    distance = Mathf.Min(length, distance + Time.deltaTime * speed);
                    root.SetPositionAndRotation(Vector3.Lerp(points[i-1], points[i], distance / length),
                        Quaternion.Slerp(fromRotation, toRotation, (i-1 + distance / length) / 3));
                }
            }
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
#if UNITY_EDITOR
            double samplingMs = 0;
#endif
            float distance=Vector3.Distance(new Vector3(start.x,0,start.z),new Vector3(end.x,0,end.z));
            int count=Mathf.Max(1,Mathf.CeilToInt(distance/.015f));
            Place(root,start,yaw);
            var path = new List<(Vector3 position, Quaternion rotation, float distance)>
                { (root.position, root.rotation, 0f) };
            int sampled=0, segment=1;
            float travelled=0;
            ExtendPath(0);
            // Evaluate only the original samples needed for this frame's travelled distance.
            // Retain cumulative distances and stair corners, so interpolation and speed are unchanged.
            while(sampled<count || travelled<path[path.Count-1].distance)
            {
                yield return null;
                float requested=travelled+Time.deltaTime*speed;
                ExtendPath(requested);
                travelled=Mathf.Min(path[path.Count-1].distance,requested);
                while(segment<path.Count-1 && path[segment].distance<travelled) segment++;
                var a=path[segment-1]; var b=path[segment];
                float t=(travelled-a.distance)/(b.distance-a.distance);
                root.SetPositionAndRotation(Vector3.Lerp(a.position,b.position,t),Quaternion.Slerp(a.rotation,b.rotation,t));
            }
#if UNITY_EDITOR
            PathMeasured?.Invoke(soles.Length, count, samplingMs);
#endif
            void ExtendPath(float requiredDistance)
            {
#if UNITY_EDITOR
                long began = PathMeasured == null && SamplesMeasured == null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
                int before = sampled;
#endif
                while(sampled<count && (path.Count==1 || path[path.Count-1].distance<requiredDistance))
                {
                    Vector3 center=Vector3.Lerp(start,end,++sampled/(float)count);
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
#if UNITY_EDITOR
                if (began != 0 && sampled>before)
                {
                    double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-began)*1000d/System.Diagnostics.Stopwatch.Frequency;
                    samplingMs+=ms;
                    SamplesMeasured?.Invoke(sampled-before,ms);
                }
#endif
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
