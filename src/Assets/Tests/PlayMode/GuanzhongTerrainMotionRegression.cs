using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Tests.PlayMode
{
    internal static class GuanzhongTerrainMotionRegression
    {
        internal static IEnumerator Run(CombatBattlefieldSurface surface, Static3DCombatUnitPresentationProvider provider, string output)
        {
            var report = new Report();
            var samples = new List<string> { "case,time,x,y,z,scaleX,scaleY,scaleZ,penetration" };
            var movements = (IDictionary)typeof(Static3DCombatUnitPresentationProvider)
                .GetField("movements",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(provider);
            int captureRate = Time.captureFramerate;
            const string id = "motion_probe";
            try
            {
                foreach(int fps in new[] {30,60,120})
                {
                    Time.captureFramerate = fps;
                    yield return null;
                    var from = new CombatUnitPresentationPose(new CombatUnitPresentationHex(4,7),0);
                    var to = new CombatUnitPresentationPose(new CombatUnitPresentationHex(3,7),3);
                    Spawn(from);
                    provider.TryGetPresentedUnit(id,out GameObject root);
                    Vector3 origin = surface.Center(from.Position), end = surface.Center(to.Position);
                    float began = Time.time;
                    provider.Present(new CombatUnitPresentationEventProjection(id,CombatUnitPresentationEvent.Move,
                        from.Position,to.Position,to.Facing,null,new[] {to.Position},
                        movementSteps:new[] {new CombatUnitPresentationMovementStep(from,to,new[] {1,2,3})}));
                    do
                    {
                        Vector3 p = root.transform.position; p.y = origin.y;
                        float t = Mathf.Clamp01(Vector3.Dot(p-origin,end-origin)/(end-origin).sqrMagnitude);
                        report.singlePathDeviation = Mathf.Max(report.singlePathDeviation,Vector3.Distance(p,Vector3.Lerp(origin,end,t)));
                        samples.Add(Row("turn-"+fps,root,0));
                        yield return null;
                    } while(movements.Contains(id));
                    report.seconds.Add(Time.time-began);
                    provider.Remove(id);
                    yield return null;
                }
                Time.captureFramerate = 60;
                foreach(bool pair in new[] {false,true})
                {
                    var pose = new CombatUnitPresentationPose(new CombatUnitPresentationHex(2,3),5,pair);
                    Spawn(pose);
                    provider.TryGetPresentedUnit(id,out GameObject root);
                    var body = root.transform.GetChild(0);
                    Vector3[] vertices = Vertices(body);
                    foreach(var action in new[] {CombatUnitPresentationEvent.Attack,CombatUnitPresentationEvent.Hit,CombatUnitPresentationEvent.Cast})
                    {
                        provider.Present(new CombatUnitPresentationEventProjection(id,action,pose.Position,pose.Position,pose.Facing,null));
                        for(int i=0;i<18;i++)
                        {
                            report.actionScaleDeviation = Mathf.Max(report.actionScaleDeviation,Vector3.Distance(root.transform.localScale,Vector3.one));
                            float penetration = Penetration(body,vertices,surface);
                            report.actionPenetration = Mathf.Max(report.actionPenetration,penetration);
                            report.actionSoleGap = Mathf.Max(report.actionSoleGap,SoleGap(body,vertices,surface,pair));
                            samples.Add(Row((pair?"pair-":"single-")+action,root,penetration));
                            yield return null;
                        }
                    }
                    provider.Remove(id);
                    yield return null;
                }
                // A legal single-piece route crosses each real tread in both directions.
                foreach(bool up in new[] {true,false})
                {
                    var from = new CombatUnitPresentationPose(new CombatUnitPresentationHex(8,up?8:10),up?5:2);
                    Spawn(from);
                    provider.TryGetPresentedUnit(id,out GameObject root);
                    var body = root.transform.GetChild(0);
                    Vector3[] vertices = Vertices(body);
                    for(int i=0;i<2;i++)
                    {
                        var to = new CombatUnitPresentationPose(new CombatUnitPresentationHex(8,from.Position.R+(up?1:-1)),from.Facing);
                        provider.Present(new CombatUnitPresentationEventProjection(id,CombatUnitPresentationEvent.Move,
                            from.Position,to.Position,to.Facing,null,new[] {to.Position},
                            movementSteps:new[] {new CombatUnitPresentationMovementStep(from,to)}));
                        do
                        {
                            float penetration = Penetration(body,vertices,surface);
                            report.stairPenetration = Mathf.Max(report.stairPenetration,penetration);
                            samples.Add(Row(up?"stair-up":"stair-down",root,penetration));
                            yield return null;
                        } while(movements.Contains(id));
                        report.stairRestGap = Mathf.Max(report.stairRestGap,SoleGap(body,vertices,surface,false));
                        from = to;
                    }
                    provider.Remove(id);
                    yield return null;
                }
                provider.ConfigureSurface(null);
                Spawn(new CombatUnitPresentationPose(new CombatUnitPresentationHex(4,7),0));
                provider.TryGetPresentedUnit(id,out GameObject flat);
                provider.Present(new CombatUnitPresentationEventProjection(id,CombatUnitPresentationEvent.Attack,
                    new CombatUnitPresentationHex(4,7),new CombatUnitPresentationHex(4,7),0,null));
                report.flatAttackScale = flat.transform.localScale;
                provider.Remove(id);
                provider.ConfigureSurface(surface);
            }
            finally { Time.captureFramerate = captureRate; }
            File.WriteAllText(Path.Combine(output,"motion-regression.json"),JsonUtility.ToJson(report,true));
            File.WriteAllLines(Path.Combine(output,"motion-regression.csv"),samples);
            {
                Assert.Less(report.singlePathDeviation,.002f,"Single-cell turns must stay on their movement segment.");
                foreach(float seconds in report.seconds) Assert.Less(seconds,.35f,"A one-metre flat move at 4m/s must not wait for each spatial sample.");
                foreach(float seconds in report.seconds) Assert.GreaterOrEqual(seconds,.25f-1f/30f,"Movement must not skip elapsed travel time.");
                Assert.Less(report.seconds.Max()-report.seconds.Min(),.08f,"Movement timing must remain stable across 30/60/120Hz.");
                Assert.Less(report.actionScaleDeviation,.0001f,"Terrain action feedback must preserve approved body scale.");
                Assert.Less(report.actionPenetration,.002f,"Actions must remain above the ramp support.");
                Assert.Less(report.actionSoleGap,.002f,"Both sole groups must remain supported during actions.");
                Assert.Less(report.stairPenetration,.002f,"Movement must remain above actual tread heights.");
                Assert.Less(report.stairRestGap,.002f,"Both feet must settle on each stair landing.");
                Assert.AreEqual(new Vector3(1.08f,.94f,1.08f),report.flatAttackScale,"Existing flat-board feedback must remain.");
            }

            void Spawn(CombatUnitPresentationPose pose) => provider.Spawn(new CombatUnitPresentationDescriptor(id,
                pose.IsDoubleCell?"combat_enemy_shijiahou_v1":"combat_player_default_v1",CombatUnitDisplayFaction.Player,
                pose.Position,pose.Facing,pose.IsDoubleCell));
            string Row(string name,GameObject root,float penetration) => string.Join(",",name,Time.time,
                root.transform.position.x,root.transform.position.y,root.transform.position.z,
                root.transform.localScale.x,root.transform.localScale.y,root.transform.localScale.z,penetration);
        }
        private static Vector3[] Vertices(Transform body) => body.GetComponentsInChildren<MeshFilter>()
            .SelectMany(f=>f.sharedMesh.vertices.Select(v=>body.InverseTransformPoint(f.transform.TransformPoint(v)))).ToArray();
        private static float Penetration(Transform body,Vector3[] vertices,CombatBattlefieldSurface surface)
        {
            float result = 0;
            foreach(Vector3 v in vertices)
            {
                Vector3 p = body.TransformPoint(v);
                if(surface.TryHeight(p,out float y)) result=Mathf.Max(result,y-p.y);
            }
            return result;
        }
        private static float SoleGap(Transform body,Vector3[] vertices,CombatBattlefieldSurface surface,bool pair)
        {
            float low=vertices.Min(v=>v.y)+.01f/body.lossyScale.y;
            float first=float.PositiveInfinity,second=float.PositiveInfinity;
            foreach(Vector3 v in vertices.Where(v=>v.y<=low))
            {
                Vector3 p=body.TransformPoint(v);
                if(!surface.TryHeight(p,out float y)) continue;
                if(pair?v.z>0:v.x<0) first=Mathf.Min(first,p.y-y);
                else second=Mathf.Min(second,p.y-y);
            }
            return Mathf.Max(first,second);
        }
        [Serializable] private sealed class Report
        {
            public float singlePathDeviation,actionScaleDeviation,actionPenetration,actionSoleGap,stairPenetration,stairRestGap;
            public List<float> seconds = new List<float>();
            public Vector3 flatAttackScale;
        }
    }
}
