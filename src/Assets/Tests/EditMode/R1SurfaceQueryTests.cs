using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.EditMode
{
    public sealed class R1SurfaceQueryTests
    {
        [TestCase(0,false)] [TestCase(1,false)] [TestCase(1,true)] [TestCase(2,false)]
        public void HeightBoundsCoverProfilesHolesStepBoundariesAndManifestedRamps(int kind,bool descending)
        {
            var root = new GameObject("surface");
            try
            {
                var surface = root.AddComponent<CombatBattlefieldSurface>();
                var cells = Enumerable.Range(-3,7).SelectMany(q => Enumerable.Range(-3,7).Select(r =>
                    new CombatBattlefieldSurface.Cell { q=q,r=r,kind=kind,lowerY=descending?2:1,upperY=descending?1:2,startZ=-1,endZ=3,
                        riserZ=new[] { 0f,1f,2f },riserY=new[] {1.2f,2.1f,1.6f} })).ToArray();
                Set(surface,"cells",cells);
                Vector3 minimum=new(-.7f,0,.3f),maximum=new(.7f,0,1.7f);
                float bound=Upper(surface,minimum,maximum);
                for (float x=minimum.x;x<=maximum.x;x+=.07f) for(float z=minimum.z;z<=maximum.z;z+=.07f)
                {
                    Assert.IsTrue(surface.TryHeight(new Vector3(x,0,z),out float y));
                    Assert.GreaterOrEqual(bound,y);
                }
                Set(surface,"cells",cells.Where(c => c.q!=0 || c.r!=0).ToArray()); Set(surface,"index",null);
                Assert.GreaterOrEqual(Upper(surface,minimum,maximum),bound);
                Set(surface,"cells",cells); Set(surface,"index",null);
                cells.Single(c => c.q==0 && c.r==0).lowerY=5;
                cells.Single(c => c.q==0 && c.r==0).kind=0;
                Assert.GreaterOrEqual(Upper(surface,minimum,maximum),5);
                Assert.AreEqual(float.NegativeInfinity,Upper(surface,new Vector3(20,0,20),new Vector3(21,0,21)));
                cells.Single(c => c.q==0 && c.r==0).lowerY=descending?2:1;
                cells.Single(c => c.q==0 && c.r==0).kind=kind;
                foreach(float z in new[] {-.0000001f,0f,.0000001f,.9999999f,1f,1.0000001f,1.9999999f,2f,2.0000002f})
                {
                    var point=new Vector3(0,0,z);
                    if(surface.TryHeight(point,out float y))Assert.GreaterOrEqual(Upper(surface,point,point),y);
                }
                Set(surface,"cells",Array.Empty<CombatBattlefieldSurface.Cell>());Set(surface,"index",null);
                surface.SetEarthRamps(new[] {new CombatBattlefieldSurface.EarthRamp(new CombatUnitPresentationHex(0,0),
                    new CombatUnitPresentationHex(1,0),new Vector3(0,5,0),new Vector3(1,3,0))});
                Assert.GreaterOrEqual(Upper(surface,minimum,maximum),5);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(false)] [TestCase(true)]
        public void BatchedSoleSupportMatchesEveryOriginalPointAcrossProfilesAndHoles(bool pair)
        {
            var root=new GameObject("body root"); var body=new GameObject("body");body.transform.SetParent(root.transform,false);
            var mesh=new Mesh(); var ground=new GameObject("ground");
            try
            {
                var vertices=Enumerable.Range(0,81).Select(i=>new Vector3((i%9-4)*.08f,(i%5)*.002f,(i/9-4)*.08f)).ToArray();
                mesh.vertices=vertices.Concat(vertices).ToArray(); body.AddComponent<MeshFilter>().sharedMesh=mesh;
                var surface=ground.AddComponent<CombatBattlefieldSurface>();
                var cells=Enumerable.Range(-2,5).SelectMany(q=>Enumerable.Range(-2,5).Select(r=>
                    new CombatBattlefieldSurface.Cell {q=q,r=r,kind=q<0?1:0,lowerY=0,upperY=1,startZ=-1,endZ=3})).ToArray();
                Set(surface,"cells",cells.Where(c=>c.q!=1 || c.r!=0).ToArray());
                var type=typeof(CombatBattlefieldSurface).Assembly.GetType("TianZhang.Features.CombatPresentation.CombatUnitSurfacePose");
                var support=Activator.CreateInstance(type,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,
                    new object[] {surface,body.transform,pair},null);
                var method=type.GetMethod("Required",BindingFlags.NonPublic|BindingFlags.Instance);
                foreach(int profile in new[] {0,1,2})
                foreach(float x in new[] {-.8f,0f,.4f,1f,2.5f}) foreach(float yaw in new[] {0f,30f,60f,120f,180f,240f,300f})
                foreach(float tilt in new[] {-20f,0f,20f})
                {
                    foreach(var cell in cells.Where(c=>c.q<0))
                    {
                        cell.kind=profile==2?2:1;cell.lowerY=profile==1?1:0;cell.upperY=profile==1?0:1;
                        cell.riserZ=new[] {.7f,1f,1.3f};cell.riserY=new[] {1f,.5f,2f};
                    }
                    var center=new Vector3(x,0,1); var rotation=Quaternion.Euler(tilt,yaw,-tilt);
                    float first=float.NegativeInfinity,second=float.NegativeInfinity;
                    foreach(var sole in mesh.vertices)
                    {
                        var p=rotation*sole;
                        if(!surface.TryHeight(center+p,out float y))continue;
                        if(pair?sole.z>0:sole.x<0)first=Mathf.Max(first,y-p.y);else second=Mathf.Max(second,y-p.y);
                    }
                    var actual=((float height,float first,float second))method.Invoke(support,new object[] {center,rotation});
                    Assert.AreEqual(first,actual.first); Assert.AreEqual(second,actual.second);
                    Assert.AreEqual(Mathf.Max(first,second),actual.height);
                    var ordered=(Vector3[])Get(support,"soles");
                    float scale=(float)Get(support,"soleCoordinateScale");
                    float padding=2e-6f*(1+scale+Mathf.Abs(center.x)+Mathf.Abs(center.y)+Mathf.Abs(center.z));
                    CheckNode(Get(support,"firstSoles")); CheckNode(Get(support,"secondSoles"));
                    void CheckNode(object node)
                    {
                        if(node==null)return;
                        float upper=(float)type.GetMethod("Bound",BindingFlags.NonPublic|BindingFlags.Instance)
                            .Invoke(support,new object[] {node,center,rotation,padding});
                        int start=(int)Get(node,"Start"),count=(int)Get(node,"Count");
                        for(int i=start;i<start+count;i++)
                        {
                            var p=rotation*ordered[i];
                            if(surface.TryHeight(center+p,out float y))
                                Assert.GreaterOrEqual(upper,y-p.y,"Every node must bound each original point, even if another node wins.");
                        }
                        CheckNode(Get(node,"Left"));CheckNode(Get(node,"Right"));
                    }
                }
            }
            finally {Object.DestroyImmediate(root);Object.DestroyImmediate(ground);Object.DestroyImmediate(mesh);}
        }
        private static float Upper(CombatBattlefieldSurface surface,Vector3 min,Vector3 max)
        {
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var cells=new List<CombatBattlefieldSurface.Cell>();
            typeof(CombatBattlefieldSurface).GetMethod("CollectHeightCells",flags).Invoke(surface,new object[] {min,max,cells});
            return (float)typeof(CombatBattlefieldSurface).GetMethod("SoleHeightUpperBound",flags)
                .Invoke(surface,new object[] {cells,(min+max)*.5f,(max-min)*.5f,Vector3.right,Vector3.up,Vector3.forward,
                    Vector3.zero,min,max,0f});
        }
        private static object Get(object owner,string name)=>owner.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
        private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(owner,value);
    }
}
