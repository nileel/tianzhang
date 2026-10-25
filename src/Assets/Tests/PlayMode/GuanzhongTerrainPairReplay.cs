using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Tests.PlayMode
{
    /// <summary>Replays D1 witness projections through the production carrier; never substitutes for player-command tests.</summary>
    internal static class GuanzhongTerrainPairReplay
    {
        internal static IEnumerator Run(CombatBattlefieldSurface surface,Static3DCombatUnitPresentationProvider provider,string output)
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/基础设定/演武地图/关中阶地-20261024/map/pair-main-route.json"));
            Route route=JsonUtility.FromJson<Route>(File.ReadAllText(path));
            const string id="acceptance_pair";
            provider.Spawn(new CombatUnitPresentationDescriptor(id,"combat_enemy_shijiahou_v1",CombatUnitDisplayFaction.Enemy,
                Hex(route.poses[0].front),route.poses[0].facing,true));
            Assert.IsTrue(provider.TryGetPresentedUnit(id,out GameObject root));
            Transform body=root.transform.GetChild(0);
            Vector3[] vertices=body.GetComponentsInChildren<MeshFilter>().SelectMany(f=>f.sharedMesh.vertices
                .Select(v=>body.InverseTransformPoint(f.transform.TransformPoint(v)))).ToArray();
            float low=vertices.Min(v=>v.y)+.01f/body.localScale.x;
            var proof=new Proof();
            var values=new List<string> {"sample,rootX,rootY,rootZ,pitch,roll,frontGap,rearGap,penetration"};
            string frames=Path.Combine(output,"pair-frames"); Directory.CreateDirectory(frames);
            foreach(Movement move in route.movements)
            {
                var from=new CombatUnitPresentationPose(Hex(move.fromPose.front),move.fromPose.facing,true);
                var to=new CombatUnitPresentationPose(Hex(move.toPose.front),move.toPose.facing,true);
                Camera camera=Camera.main;
                camera.transform.position=surface.Center(from.Position)-camera.transform.forward*20;
                provider.Present(new CombatUnitPresentationEventProjection(id,CombatUnitPresentationEvent.Move,
                    from.Position,to.Position,to.Facing,null,new[]{to.Position},
                    movementSteps:new[]{new CombatUnitPresentationMovementStep(from,to,move.intermediateTurns.Select(t=>t.pose.facing).ToArray())}));
                int frame=0;
                do
                {
                    yield return null;
                    if(frame++%8!=0) continue;
                    Verify();
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(frames,(proof.frames++).ToString("D4")+".png"));
                } while(provider.IsPresenting);
                Verify(); proof.steps++;
            }
            File.WriteAllText(Path.Combine(output,"pair-replay.json"),JsonUtility.ToJson(proof,true));
            File.WriteAllLines(Path.Combine(output,"pair-runtime.csv"),values);
            provider.Remove(id);

            void Verify()
            {
                float front=float.PositiveInfinity,rear=float.PositiveInfinity,penetration=0;
                foreach(Vector3 vertex in vertices)
                {
                    Vector3 p=body.TransformPoint(vertex);
                    if(!surface.TryHeight(p,out float y)) continue;
                    penetration=Mathf.Max(penetration,y-p.y);
                    if(vertex.y<=low)
                    { if(vertex.z>0) front=Mathf.Min(front,p.y-y); else rear=Mathf.Min(rear,p.y-y); }
                }
                float gap=Mathf.Max(front,rear);
                proof.maxFootGap=Mathf.Max(proof.maxFootGap,gap);
                proof.maxPenetration=Mathf.Max(proof.maxPenetration,penetration);
                values.Add(string.Join(",",proof.samples++,root.transform.position.x,root.transform.position.y,root.transform.position.z,
                    root.transform.eulerAngles.x,root.transform.eulerAngles.z,front,rear,penetration));
                File.WriteAllLines(Path.Combine(output,"pair-runtime.csv"),values);
                Assert.Less(penetration,.002f,"Pair production body intrusion; see pair-runtime.csv");
                Assert.Less(gap,.002f,"Pair production foot support; see pair-runtime.csv");
            }
        }
        private static CombatUnitPresentationHex Hex(string value) { int[] qr=value.Split(':').Select(int.Parse).ToArray(); return new CombatUnitPresentationHex(qr[0],qr[1]); }
        [Serializable] private sealed class Route { public Pose[] poses; public Movement[] movements; }
        [Serializable] private sealed class Pose { public string front; public int facing; }
        [Serializable] private sealed class Movement { public Pose fromPose,toPose; public Turn[] intermediateTurns; }
        [Serializable] private sealed class Turn { public Pose pose; }
        [Serializable] private sealed class Proof { public int samples,steps,frames; public float maxFootGap,maxPenetration; }
    }
}
