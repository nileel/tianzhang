using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Features.CombatPresentation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    // Reference is the former eager path construction; sole evaluation remains the production implementation.
    internal static class R2SurfacePlaybackComparison
    {
        internal static IEnumerator Run(Static3DCombatUnitPresentationProvider provider, string output)
        {
            var type = typeof(Static3DCombatUnitPresentationProvider).Assembly.GetType("TianZhang.Features.CombatPresentation.CombatUnitSurfacePose");
            var evaluate = type.GetMethod("Evaluate"); var move = type.GetMethod("Move");
            var root = new GameObject("R2 trajectory witness");
            var surface = root.AddComponent<CombatBattlefieldSurface>();
            var witness = new GameObject("R2 streamed body");
            var reference = new GameObject("R2 eager body");
            var rows = new List<string> { "actor,profile,descending,samples,playback_frames,max_position_error,max_rotation_error" };
            float previousCapture = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f/60;
            try
            {
                foreach (string id in new[] { "player", "enemy_2" })
                for (int kind=0;kind<3;kind++)
                foreach (bool descending in new[] { false, true })
                {
                    var cells = Enumerable.Range(-4,9).SelectMany(q=>Enumerable.Range(-4,9).Select(r=>
                        new CombatBattlefieldSurface.Cell { q=q,r=r,kind=kind,lowerY=1,upperY=2,startZ=.5f,endZ=2.5f,
                            riserZ=new[] {.8f,1.1f,1.4f,1.7f,2f},riserY=new[] {1.2f,1.4f,1.6f,1.8f,2f} })).ToArray();
                    Set(surface,"cells",cells); Set(surface,"index",null);
                    Assert.IsTrue(provider.TryGetPresentedUnit(id,out var unit));
                    object fit = Activator.CreateInstance(type, surface, unit.transform.GetChild(0), id=="enemy_2");
                    Vector3 start=new(0,0,descending?2.4f:.6f),end=new(0,0,descending?.6f:2.4f);
                    float yaw=descending?180:0;
                    int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(start,end)/.015f));
                    var first=Evaluate(start);
                    var eager=new List<(Vector3 p,Quaternion q,float distance)> { (first.p,first.q,0) };
                    for(int i=1;i<=count;i++)
                    {
                        Vector3 center=Vector3.Lerp(start,end,i/(float)count);
                        var next=Evaluate(center); Vector3 previous=eager[eager.Count-1].p;
                        if(surface.IsStair(center) && Mathf.Abs(next.p.y-previous.y)>.002f)
                            Add(next.p.y>previous.y?new Vector3(previous.x,next.p.y,previous.z):new Vector3(next.p.x,previous.y,next.p.z),next.q);
                        Add(next.p,next.q);
                    }
                    var playback=(IEnumerator)move.Invoke(fit,new object[] {witness.transform,start,end,yaw,4f});
                    Assert.IsTrue(playback.MoveNext());
                    Assert.AreEqual(first.p,witness.transform.position);
                    float travelled=0,length=eager[eager.Count-1].distance,maxPosition=0,maxRotation=0;
                    int segment=1,frames=0;
                    bool playing=true;
                    while(playing)
                    {
                        yield return null;
                        travelled=Mathf.Min(length,travelled+Time.deltaTime*4f);
                        playing=playback.MoveNext();
                        while(segment<eager.Count-1 && eager[segment].distance<travelled)segment++;
                        var a=eager[segment-1];var b=eager[segment];float t=(travelled-a.distance)/(b.distance-a.distance);
                        Vector3 p=Vector3.Lerp(a.p,b.p,t);Quaternion q=Quaternion.Slerp(a.q,b.q,t);
                        // The old coroutine also wrote through Transform, which normalizes the quaternion.
                        reference.transform.SetPositionAndRotation(p,q);
                        q=reference.transform.rotation;
                        maxPosition=Mathf.Max(maxPosition,Vector3.Distance(p,witness.transform.position));
                        Quaternion actual=witness.transform.rotation;
                        maxRotation=Mathf.Max(maxRotation,Mathf.Abs(q.x-actual.x),Mathf.Abs(q.y-actual.y),Mathf.Abs(q.z-actual.z),Mathf.Abs(q.w-actual.w));
                        Assert.AreEqual(p,witness.transform.position,id+" position profile="+kind);
                        Assert.AreEqual(q,witness.transform.rotation,id+" rotation profile="+kind);
                        Assert.AreEqual(travelled<length,playing,"Streaming must not add waiting frames.");
                        Assert.Less(++frames,300);
                    }
                    rows.Add(string.Join(",",id,kind,descending,count,frames,maxPosition,maxRotation));
                    // A zero-length step also retains the original no-yield behavior.
                    playback=(IEnumerator)move.Invoke(fit,new object[] {witness.transform,start,start,yaw,4f});
                    Assert.IsFalse(playback.MoveNext());

                    (Vector3 p,Quaternion q) Evaluate(Vector3 center)
                    { object[] args={center,yaw,null,null};evaluate.Invoke(fit,args);return ((Vector3)args[2],(Quaternion)args[3]); }
                    void Add(Vector3 p,Quaternion q)
                    { var previous=eager[eager.Count-1];float step=Vector3.Distance(previous.p,p);if(step>.000001f)eager.Add((p,q,previous.distance+step)); }
                }
                File.WriteAllLines(Path.Combine(output,"streaming-trajectory.csv"),rows);
            }
            finally
            {
                Time.captureDeltaTime=previousCapture;
                Object.Destroy(witness);Object.Destroy(reference);Object.Destroy(root);
            }
        }
        private static void Set(object owner,string field,object value) => owner.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(owner,value);
    }
}
