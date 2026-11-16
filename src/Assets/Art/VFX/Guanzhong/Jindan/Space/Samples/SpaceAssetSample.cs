using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using UnityEngine;

namespace TianZhang.Art.Jindan.Space.Samples
{
    public sealed class SpaceAssetSample : MonoBehaviour
    {
        public CombatBattlefieldSurface surface;
        public Static3DCombatUnitPresentationProvider provider;
        public Camera sampleCamera;
        public GameObject compressed, expanded, scaleSupport, sealSupport, sealOuter, sealInner;
        public JindanDemoCatalogData catalog;
        public CombatRuleSetData rules;
        public EnvironmentProfileAsset environment;
        public AttackProfileData[] attacks;
        public bool capture;
        public int captureWidth=1920;
        public bool Complete { get; private set; }
        public string Failure { get; private set; }
        private SpaceSampleRules demo;
        private readonly List<string> proof=new();
        private readonly List<GameObject> effects=new();
        private readonly List<Mesh> meshes=new();
        private readonly List<Material> materials=new();
        private GameObject support, band, outer, inner;
        private Vector3[] initialCenters;
        private RenderTexture target;
        private Texture2D pixels;
        private string output,evidence,phase;
        private int frame;
        private int compressedCost,expandedCost;
        private static readonly HexCoord Home=new(4,14);

        private IEnumerator Start()
        {
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"../Logs/d12-space/"+captureWidth));
            evidence=Path.GetFullPath(Path.Combine(Application.dataPath,"../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/space/evidence/"+captureWidth));
            if(capture)
            {
                Directory.CreateDirectory(output); Directory.CreateDirectory(evidence); Time.captureDeltaTime=1f/60;
                target=new RenderTexture(captureWidth,captureWidth==1920?1080:768,24);
                pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false); sampleCamera.targetTexture=target;
            }
            initialCenters=surface.Cells.Select(c=>Point(new HexCoord(c.q,c.r))).ToArray();
            var stack=new Stack<IEnumerator>(); stack.Push(Run());
            while(stack.Count>0)
            {
                object next=null; bool moved=false;
                try { moved=stack.Peek().MoveNext(); if(moved)next=stack.Peek().Current; }
                catch(Exception exception) { Failure=exception.ToString(); Debug.LogException(exception); break; }
                if(!moved){stack.Pop();continue;}
                if(next is IEnumerator nested){stack.Push(nested);continue;}
                yield return next;
            }
            if(capture)File.WriteAllLines(Path.Combine(evidence,"runtime-proof.txt"),proof.Concat(new[]{Failure??"PASS"}));
            Complete=true;
        }
        private IEnumerator Run()
        {
            yield return ScaleSequence(1,"compress");
            yield return ScaleSequence(4,"expand");
            Check(compressedCost<expandedCost,"same edge compression costs less than expansion");
            proof.Add("same committed movement edge AP: compression="+compressedCost+" expansion="+expandedCost);
            yield return GateSequence();
            var after=surface.Cells.Select(c=>Point(new HexCoord(c.q,c.r))).ToArray();
            Check(initialCenters.SequenceEqual(after),"all 144 ground centers unchanged");
            proof.Add("ground centers="+after.Length+" maximum displacement=0; camera tilt38 size3 + size5 keyframes; Unity="+Application.unityVersion);
            proof.Add("Only independent resource sample; production Views and V3 HUD binding pending.");
        }
        private void Fresh(HexCoord focus)
        {
            foreach(var go in effects)if(go)Destroy(go);effects.Clear();
            demo=new SpaceSampleRules(this);provider.ConfigureSurface(surface);
            provider.Prepare(JindanRehearsalFactory.Descriptors(demo.Spawned,"combat_player_default_v1"));
            Focus(focus,3);
        }
        private IEnumerator ScaleSequence(int metric,string label)
        {
            Fresh(SpaceSampleRules.ScaleFrom); yield return Hold(label+"-01-before");
            Check(demo.Metric==2,"scale starts at normal distance2");
            demo.Scale(metric);
            band=PlaceBand(metric==1?compressed:expanded,SpaceSampleRules.ScaleFrom,SpaceSampleRules.ScaleTo);
            var midpoint=(Point(SpaceSampleRules.ScaleFrom)+Point(SpaceSampleRules.ScaleTo))*.5f;
            Check(surface.TryHeight(midpoint,out float height),"scale support grounded");midpoint.y=height;
            support=Instantiate(scaleSupport,midpoint,Quaternion.identity);effects.Add(support);
            Check(demo.Metric==metric,"committed scale distance");
            proof.Add(label+" metric="+demo.Metric+" support="+demo.ScaleSeat.ActiveScale.Support.Durability);
            yield return Hold(label+"-02-active"); Visibility(band,label);
            SetShimmer(0);Key(label+"-02-no-shimmer");SetShimmer(.25f);
            yield return Strike(SpaceSampleRules.ScaleFrom);
            Check(demo.ScaleSeat.ActiveScale.Support.Durability==170,"first scale strike actual durability170");
            support.transform.Find("Intact/Glow").gameObject.SetActive(false);
            Check(band.activeInHierarchy&&demo.Metric==metric,"damaged support keeps scale edge");
            yield return Hold(label+"-03-damaged");
            yield return Move(SpaceSampleRules.Player,SpaceSampleRules.ScaleFrom,label+"-approach");
            var quote=demo.Commands.Preview(demo.Session,demo.Move(SpaceSampleRules.Player,SpaceSampleRules.ScaleTo));
            if(metric==1)compressedCost=quote.ActionPointCostUnits;else expandedCost=quote.ActionPointCostUnits;
            yield return Move(SpaceSampleRules.Player,SpaceSampleRules.ScaleTo,label+"-04-cross");
            if(metric==1)
            {
                yield return Move(SpaceSampleRules.Player,Home,label+"-return");
                for(int i=0;i<6;i++)yield return Strike(SpaceSampleRules.ScaleFrom);
                Check(demo.ScaleSeat.ActiveScale==null,"zero durability ends compression");
            }
            else { demo.CloseScale();Check(demo.ScaleSeat.ActiveScale==null,"close ends expansion"); }
            band.SetActive(false);support.SetActive(false);
            Check(demo.Metric==2,"ending restores normal metric2");
            yield return Hold(label+"-05-restored");
            proof.Add(label+" ended metric="+demo.Metric+" reason="+demo.ScaleSeat.LastEndReason);
        }
        private IEnumerator GateSequence()
        {
            Fresh(SpaceSampleRules.GateTo);
            support=Instantiate(sealSupport,Point(SpaceSampleRules.GateTo),Quaternion.identity);effects.Add(support);
            support.transform.Find("Intact/Glow").gameObject.SetActive(false);
            yield return Hold("seal-00-before");
            demo.ToTurn(SpaceSampleRules.Enemy);demo.Seal();
            demo.ToTurn(SpaceSampleRules.Player);
            yield return Move(SpaceSampleRules.Player,SpaceSampleRules.GateFrom,"seal-approach");
            var seal=demo.SealSeat.ActiveSeal;
            Check(seal!=null,"legal production gate deployment persists");
            support.transform.Find("Intact/Glow").gameObject.SetActive(true);
            outer=PlaceBand(sealOuter,SpaceSampleRules.GateFrom,SpaceSampleRules.GateTo);
            inner=PlaceBand(sealInner,SpaceSampleRules.GateFrom,SpaceSampleRules.GateTo);
            yield return Hold("seal-01-two-layers");CheckGate(true);Visibility(outer,"seal outer");Visibility(inner,"seal inner");
            SetShimmer(0);Key("seal-01-no-shimmer");SetShimmer(.25f);
            // Show a real detour and legal reverse crossing before breaking the anchor.
            yield return Move(SpaceSampleRules.Player,SpaceSampleRules.GateTo,"seal-02-detour");
            yield return Move(SpaceSampleRules.Player,SpaceSampleRules.GateFrom,"seal-03-reverse");
            for(int i=0;i<4;i++)yield return Strike(SpaceSampleRules.GateTo);
            Check(seal.OuterDurability==0&&seal.InnerDurability==80,"four committed strikes break outer only");
            outer.SetActive(false);support.transform.Find("Intact/Glow").gameObject.SetActive(false);
            CheckGate(true);yield return Hold("seal-04-outer-broken");
            proof.Add("outer broken outer="+seal.OuterDurability+" inner="+seal.InnerDurability+" expiry="+seal.ExpiresTick);
            for(int i=0;i<3;i++)yield return Strike(SpaceSampleRules.GateTo);
            Check(demo.SealSeat.ActiveSeal==null&&seal.Support.Durability==0,"seventh strike opens gate");
            inner.SetActive(false);support.transform.Find("Intact").gameObject.SetActive(false);support.transform.Find("Failed").gameObject.SetActive(true);
            CheckGate(false);yield return Hold("seal-05-broken-open");
            yield return Move(SpaceSampleRules.Player,SpaceSampleRules.GateTo,"seal-06-through");
            Check(support.activeInHierarchy&&support.transform.Find("Failed").gameObject.activeInHierarchy,"fixed failed stele remains");
            Check(!outer.activeInHierarchy&&!inner.activeInHierarchy&&!support.transform.Find("Intact/Glow").gameObject.activeInHierarchy,"no gate glow or edge residual");
        }
        private void CheckGate(bool blocked)
        {
            var actor=demo.Actor(SpaceSampleRules.Player);
            Check(actor.Position==SpaceSampleRules.GateFrom,"gate probe from actual entry cell");
            var quote=demo.Commands.Preview(demo.Session,demo.Move(actor.Id,SpaceSampleRules.GateTo));
            Check(quote.IsLegal,"gate destination reachable");
            Check(blocked?quote.MovementSteps.Count>1:quote.MovementSteps.Count==1,"real path respects two layers");
            var push=CombatForceMovement.Query(demo.Session,actor.Id,0,1,2);
            Check(blocked?push.Path.Count==0:push.Path.Count==1,"forced entry respects layers");
            Check(demo.Session.SceneObjects.Board.QueryLineOfSight(SpaceSampleRules.GateFrom,SpaceSampleRules.GateTo).HasLineOfSight,"seal does not block sight");
            proof.Add("gate blocked="+blocked+" real path steps="+quote.MovementSteps.Count+" forced steps="+push.Path.Count);
        }
        private IEnumerator Strike(HexCoord cell)
        {
            demo.Strike(cell);var actor=demo.Actor(SpaceSampleRules.Player);
            provider.Present(new CombatUnitPresentationEventProjection(actor.Id,CombatUnitPresentationEvent.Cast,Hex(actor.Position),Hex(actor.Position),actor.Facing,null,
                profileId:"spell_guanshiji",sourceCell:Hex(actor.Position),targetCell:Hex(cell)));
            yield return Frames(24);
        }
        private IEnumerator Move(string id,HexCoord cell,string label)
        {
            var actor=demo.Actor(id);var origin=actor.Position;var command=demo.Move(id,cell);
            var quote=demo.Commands.Preview(demo.Session,command);Check(quote.IsLegal,label+": "+quote.RejectionReason);
            var steps=quote.MovementSteps.Select(s=>new CombatUnitPresentationMovementStep(Pose(s.From),Pose(s.To),s.TurnFacings,s.IsLightLeap)).ToArray();
            int before=actor.ActionPointsRemainingUnits;demo.Execute(command);
            Check(before-actor.ActionPointsRemainingUnits==quote.ActionPointCostUnits,"move pays committed AP quote");
            provider.Present(new CombatUnitPresentationEventProjection(id,CombatUnitPresentationEvent.Move,Hex(origin),Hex(cell),actor.Facing,null,
                steps.Select(s=>s.To.Position).ToArray(),movementSteps:steps));
            phase=label;
            for(int i=0;i<360&&provider.IsPresenting;i++)yield return Frames(1);
            yield return Frames(24);
            Check(!provider.IsPresenting,"movement presentation settled");
            Check(provider.TryGetPresentedUnit(id,out var unit)&&Vector3.Distance(unit.transform.position,Point(cell))<.02f,"actor displayed at committed landing");
            proof.Add(label+" "+origin+"->"+cell+" steps="+steps.Length+" AP="+quote.ActionPointCostUnits);Key(label);
        }
        private IEnumerator Hold(string name)
        {
            phase=name;yield return Frames(48);Key(name);
            sampleCamera.orthographicSize=5;Key(name+"-default-zoom");sampleCamera.orthographicSize=3;
        }
        private void Focus(HexCoord cell,float size)
        {sampleCamera.transform.rotation=Quaternion.Euler(38,0,0);sampleCamera.orthographic=true;sampleCamera.orthographicSize=size;sampleCamera.transform.position=Point(cell)-sampleCamera.transform.forward*20;}
        private GameObject PlaceBand(GameObject prefab,HexCoord a,HexCoord b)
        {
            Vector3 from=Point(a),to=Point(b),direction=(to-from).normalized;
            var go=Instantiate(prefab,from,Quaternion.FromToRotation(Vector3.right,direction));effects.Add(go);
            var filter=go.GetComponent<MeshFilter>();var mesh=Instantiate(filter.sharedMesh);meshes.Add(mesh);
            var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)
            {var world=go.transform.TransformPoint(vertices[i]);Check(surface.TryHeight(world,out float h),"space band support exists");world.y=h+.035f;vertices[i]=go.transform.InverseTransformPoint(world);}
            mesh.vertices=vertices;mesh.RecalculateBounds();filter.sharedMesh=mesh;
            var renderer=go.GetComponent<Renderer>();var material=new Material(renderer.sharedMaterial);materials.Add(material);renderer.sharedMaterial=material;
            return go;
        }
        private void SetShimmer(float value){foreach(var material in materials)if(material)material.SetFloat("_Shimmer",value);}
        private Vector3 Point(HexCoord cell)=>surface.Center(Hex(cell));
        private static CombatUnitPresentationHex Hex(HexCoord c)=>new(c.Q,c.R);
        private static CombatUnitPresentationPose Pose(SpatialPose p)=>new(Hex(p.Anchor),p.Facing,p.Footprint==SpatialFootprintKind.AdjacentPair,p.AltitudeBand);
        private IEnumerator Frames(int count)
        {
            for(int i=0;i<count;i++)
            {yield return null;if(capture&&frame%2==0){Render();File.WriteAllBytes(Path.Combine(output,(frame/2).ToString("D4")+".jpg"),pixels.EncodeToJPG(90));}frame++;}
        }
        private void Render()
        {sampleCamera.Render();var previous=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();RenderTexture.active=previous;}
        private void Key(string name)
        {
            if(!capture)return;Render();File.WriteAllBytes(Path.Combine(evidence,name+".png"),pixels.EncodeToPNG());
            Check(FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>r.enabled).All(r=>r.sharedMaterials.All(m=>m&&m.shader.isSupported)),"no unsupported materials");
        }
        private void Visibility(GameObject effect,string label)
        {
            if(!capture)return;Render();var before=pixels.GetPixels32();effect.SetActive(false);Render();var after=pixels.GetPixels32();effect.SetActive(true);
            int difference=0;for(int i=0;i<before.Length;i++)if(Math.Abs(before[i].r-after[i].r)+Math.Abs(before[i].g-after[i].g)+Math.Abs(before[i].b-after[i].b)>3)difference++;
            proof.Add(label+" band pixels="+difference);Check(difference>30,"space pattern visible "+label);
        }
        private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        private void OnGUI()=>GUI.Label(new Rect(24,18,900,40),"D12.4 资源样例 · "+phase+" · 正式 Demo 尚未接线");
        private void OnDestroy()
        {if(capture)Time.captureDeltaTime=0;if(sampleCamera)sampleCamera.targetTexture=null;if(target){target.Release();Destroy(target);}if(pixels)Destroy(pixels);foreach(var m in meshes)Destroy(m);foreach(var m in materials)Destroy(m);}
    }
}
