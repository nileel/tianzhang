using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.CombatPresentation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongTerrainRehearsalPlayModeTests
    {
        private Mouse mouse;
        private string saves;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldEditorInput;
        private string output;
        [UnityTest,Timeout(600000)] public IEnumerator FormalEntry1920() => Verify(1920,1080);
        [UnityTest,Timeout(600000)] public IEnumerator FormalEntry1366() => Verify(1366,768);
        [UnityTest,Timeout(600000)] public IEnumerator PairWitness1366() => Verify(1366,768,true);
        [UnityTest,Timeout(1800000)] public IEnumerator NativeWindow1920() => Verify(1920,1080);
        [UnityTest,Timeout(1800000)] public IEnumerator NativeWindow1366() => Verify(1366,768);
        [UnityTest,Timeout(600000)] public IEnumerator MotionRegressions() => Verify(1366,768,regression:true);
        [Test] public void NativeEvidenceRejectsGroundClicksAndCameraReplacement()
        {
            Assert.IsFalse(GuanzhongTerrainNativeObservation.IsConfirmedBodyPick(false,"enemy","enemy"));
            Assert.IsFalse(GuanzhongTerrainNativeObservation.IsConfirmedBodyPick(true,"enemy",null));
            Assert.IsTrue(GuanzhongTerrainNativeObservation.IsConfirmedBodyPick(true,"enemy","enemy"));
            Assert.IsFalse(GuanzhongTerrainNativeObservation.SameCameraContext(1,2,3,4));
            Assert.IsFalse(GuanzhongTerrainNativeObservation.SameCameraContext(1,1,3,4));
            Assert.IsFalse(GuanzhongTerrainNativeObservation.SameCameraContext(1,1,0,0));
            Assert.IsTrue(GuanzhongTerrainNativeObservation.SameCameraContext(1,1,3,3));
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(mouse!=null) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior=oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode=oldEditorInput;
            var bootstrap=Object.FindFirstObjectByType<GameBootstrap>();
            if(bootstrap!=null) Object.Destroy(bootstrap.gameObject);
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            if(saves!=null && Directory.Exists(saves)) Directory.Delete(saves,true);
        }
        private IEnumerator Verify(int width,int height,bool pairWitness=false,bool regression=false)
        {
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"../Logs/d36/"+width));
            Directory.CreateDirectory(output);
            oldBackground=InputSystem.settings.backgroundBehavior;
            oldEditorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse=InputSystem.AddDevice<Mouse>();
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(width,height);
            var stale=Object.FindFirstObjectByType<GameBootstrap>();
            if(stale!=null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves=Path.Combine(Application.temporaryCachePath,"D36-"+Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(),"slotStore",new GameSaveSlotStore(saves));
            var creation=Find<CharacterCreationController>();
            creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            creation.Submit("d36","阶地验证");
            yield return SceneReady(GameplaySceneNames.World);
            Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            bool manual=Environment.GetCommandLineArgs().Contains("-d36Manual");
            if(manual)
            {
                File.WriteAllText(Path.Combine(output,"manual-stage.txt"),"entry");
                for(float end=Time.realtimeSinceStartup+240;!Find<AdventureController>().IsTerrainRehearsal && Time.realtimeSinceStartup<end;) yield return null;
            }
            else yield return Click(GameObject.Find("TerrainRehearsalButton").GetComponent<Button>());
            var controller=Find<AdventureController>();
            Assert.IsTrue(controller.IsTerrainRehearsal);
            Assert.AreEqual(AdventureSceneState.Combat,controller.CurrentState);
            var surface=Find<CombatBattlefieldSurface>();
            var movement=Find<CombatMovementView>();
            var encounter=Find<EncounterCoordinator>();
            var provider=Find<Static3DCombatUnitPresentationProvider>();
            var session=Get<CombatSession>(encounter,"session");
            // Isolate terrain/input acceptance from enemy combat tuning. Authority still owns every turn and move.
            Set(encounter,"enemyPolicies",new Dictionary<string,ICombatActionPolicy> {
                {"enemy",new WaitPolicy()},{"enemy_2",new WaitPolicy()} });
            Assert.AreEqual(144,surface.Cells.Count);
            Assert.IsFalse(GameObject.Find("GuanzhongBattlefield")!=null);
            yield return PlayerReady(encounter,session);
            if(regression)
            {
                yield return GuanzhongTerrainMotionRegression.Run(surface,provider,output);
                yield break;
            }
            if(Environment.GetCommandLineArgs().Contains("-d36NativeOnly"))
            {
                yield return GuanzhongTerrainNativeObservation.Run(output);
                yield break;
            }
            if(pairWitness)
            {
                if(provider.TryGetPresentedUnit("player",out var actualPlayer)) actualPlayer.SetActive(false);
                yield return GuanzhongTerrainPairReplay.Run(surface,provider,output);
                yield break;
            }
            Camera camera=Camera.main;
            var colliders=new List<Collider>();
            foreach(var filter in surface.GetComponentsInChildren<MeshFilter>())
            {
                if(filter.transform.parent!=null && filter.transform.IsChildOf(surface.transform) &&
                    (filter.name.Contains("ExactCap") || filter.transform.GetComponentsInParent<Transform>().Any(t=>t.name.Contains("ramp") || t.name.Contains("stairs"))))
                {
                    var collider=filter.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh=filter.sharedMesh; colliders.Add(collider);
                }
            }
            Physics.SyncTransforms();
            int picks=0,outlinePoints=0;
            float maxRingError=0,maxActualSupportError=0;
            foreach(var cell in surface.Cells)
            {
                var hex=new CombatUnitPresentationHex(cell.q,cell.r);
                Vector3 center=surface.Center(hex);
                float actual=GuanzhongStairBodyProbe.Height(center,colliders.ToArray());
                Assert.IsFalse(float.IsNegativeInfinity(actual),"Actual support mesh missing at "+cell.q+":"+cell.r);
                maxActualSupportError=Mathf.Max(maxActualSupportError,Mathf.Abs(actual-center.y));
                Assert.IsTrue(surface.TryPick(new Ray(center+Vector3.up*10,Vector3.down),out var picked));
                Assert.AreEqual(hex,picked);
                LineRenderer ring=movement.Highlights[new Vector2Int(cell.q,cell.r)];
                for(int i=0;i<ring.positionCount;i++)
                {
                    Vector3 p=ring.GetPosition(i);
                    Assert.IsTrue(surface.TryHeight(p,out float y));
                    maxRingError=Mathf.Max(maxRingError,Mathf.Abs(p.y-y-.01f)); outlinePoints++;
                }
                picks++;
            }
            Assert.Less(maxRingError,.00001f);
            Assert.Less(maxActualSupportError,.002f);
            var performance=new List<float>();
            for(int i=0;i<120;i++) { yield return null; performance.Add(Time.unscaledDeltaTime*1000); }
            performance.Sort();
            File.WriteAllText(Path.Combine(output,"load.json"),JsonUtility.ToJson(new Load {
                meanMilliseconds=performance.Average(),p95Milliseconds=performance[113],
                visibleTriangles=UnityEditor.UnityStats.triangles,drawCalls=UnityEditor.UnityStats.drawCalls,
                managedBytes=GC.GetTotalMemory(false),totalAllocatedBytes=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() },true));
            yield return Capture("entry");
            var bar=Find<CombatActionBarView>();
            yield return Click(bar.MoveButton);
            Assert.IsTrue(movement.IsSelecting);
            yield return Click(new Vector2(width*.5f,height*.5f),MouseButton.Right);
            Assert.IsFalse(movement.IsSelecting);
            var option=Find<CombatHudPresenter>().Snapshot.Movement.Options.First(o=>o.Destination.Q==3 && o.Destination.R==0);
            Vector3 target=surface.Center(option.Destination);
            Focus(camera,target);
            if(!manual) yield return Click(bar.MoveButton);
            Vector2 point=camera.WorldToScreenPoint(target);
            Assert.IsTrue(movement.TryPickCell(point,out var destination));
            Assert.AreEqual(option.Destination,destination);
            if(manual)
            {
                File.WriteAllText(Path.Combine(output,"manual-stage.txt"),"move-to-3:0");
                for(float end=Time.realtimeSinceStartup+240;Time.realtimeSinceStartup<end;)
                {
                    Assert.IsTrue(session.Combatants.TryGet("player",out var current));
                    if(current.Position.Q==3 && current.Position.R==0) break;
                    yield return null;
                }
            }
            else yield return Click(point,MouseButton.Left);
            yield return PlayerReady(encounter,session);
            Assert.IsTrue(session.Combatants.TryGet("player",out var player));
            Assert.AreEqual(3,player.Position.Q);
            yield return Capture("first-move");
            string[] route="2:1,2:2,2:3,2:4,2:5,2:6,2:7,3:7,4:7,5:7,6:7,7:7,8:7,8:8,8:9,8:10,8:11,7:11".Split(',');
            var numeric=new List<string> { "cell,rootX,rootY,rootZ,pitch,roll,maxSoleGap,maxPenetration" };
            for(int i=0;i<route.Length;i++)
            {
                int[] qr=route[i].Split(':').Select(int.Parse).ToArray();
                yield return MoveTo(qr[0],qr[1]);
                Assert.IsTrue(provider.TryGetPresentedUnit("player",out var piece));
                float penetration=0,left=float.PositiveInfinity,right=float.PositiveInfinity;
                var body=piece.transform.GetChild(0);
                var points=body.GetComponentsInChildren<MeshFilter>().Where(f=>f.name!="CombatTargetPreviewSurface")
                    .SelectMany(f=>f.sharedMesh.vertices.Select(v=>new { local=body.InverseTransformPoint(f.transform.TransformPoint(v)),world=f.transform.TransformPoint(v) })).ToArray();
                float low=points.Min(p=>p.local.y)+.01f/1.4f;
                foreach(var p in points)
                {
                    if(!surface.TryHeight(p.world,out float y)) continue;
                    penetration=Mathf.Max(penetration,y-p.world.y);
                    if(p.local.y<=low)
                    { if(p.local.x<0) left=Mathf.Min(left,p.world.y-y); else right=Mathf.Min(right,p.world.y-y); }
                }
                float gap=Mathf.Max(left,right);
                numeric.Add(string.Join(",",route[i],piece.transform.position.x,piece.transform.position.y,piece.transform.position.z,
                    piece.transform.eulerAngles.x,piece.transform.eulerAngles.z,gap,penetration));
                File.WriteAllLines(Path.Combine(output,"runtime-body.csv"),numeric);
                Assert.Less(penetration,.002f,"Body intrusion at "+route[i]);
                Assert.Less(gap,.002f,"Static foot support at "+route[i]);
                yield return Capture("route-"+i.ToString("D2"));
            }
            var targetInput=Find<CombatTargetInput>();
            var hud=Find<CombatHudPresenter>();
            foreach(var hex in new[] {new CombatUnitPresentationHex(8,12),new CombatUnitPresentationHex(7,12)})
            {
                Focus(camera,surface.Center(hex)); yield return null;
                Vector2 p=camera.WorldToScreenPoint(surface.Center(hex));
                Assert.IsTrue(targetInput.TryGetCell(p,out var pickedCell)); Assert.AreEqual(hex,pickedCell);
                Assert.AreEqual("enemy",targetInput.FindUnitAtCell(pickedCell));
                yield return Click(p,MouseButton.Left);
                Assert.AreEqual("enemy",hud.InspectedTargetId);
                yield return Capture("pair-cell-"+hex.Q);
            }
            hud.ClearInspectedTarget();
            // Actual board cast on an unoccupied high cell; count the unchanged static environment afterwards.
            int propCount=surface.GetComponentsInChildren<Renderer>().Length;
            session.Combatants.TryGet("player",out var castingPlayer);
            if(castingPlayer.ActionPointsRemainingUnits<40000)
            {
                yield return Click(bar.WaitButton); yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
                yield return PlayerReady(encounter,session);
            }
            var empty=new CombatUnitPresentationHex(7,10);
            Assert.IsNull(targetInput.FindUnitAtCell(empty));
            Focus(camera,surface.Center(empty)); yield return null;
            yield return Click(bar.BasicAttackButton);
            int apBeforeEmpty=castingPlayer.ActionPointsRemainingUnits;
            yield return Click((Vector2)camera.WorldToScreenPoint(surface.Center(empty)),MouseButton.Left);
            yield return PlayerReady(encounter,session);
            Assert.Less(castingPlayer.ActionPointsRemainingUnits,apBeforeEmpty,"The legal empty cast must actually commit.");
            Assert.AreEqual(propCount,surface.GetComponentsInChildren<Renderer>().Length);
            yield return Capture("high-empty-cast");
            // Return down the same upper stair; continue down the front-right stair and its dry landing.
            foreach(string key in "8:11,8:10,8:9,8:8,8:7,7:7,6:7,5:7,5:6,5:5,5:4,5:3,5:2,5:1,5:0,6:0".Split(','))
            { int[] qr=key.Split(':').Select(int.Parse).ToArray(); yield return MoveTo(qr[0],qr[1]); }
            yield return Capture("returned-low");
            float oldSize=camera.orthographicSize;
            Vector2 centerPoint=new Vector2(width*.5f,height*.5f);
            InputSystem.QueueStateEvent(mouse,new MouseState {position=centerPoint,scroll=new Vector2(0,1)});
            yield return null; yield return null;
            Assert.Less(camera.orthographicSize,oldSize);
            InputSystem.QueueStateEvent(mouse,new MouseState {position=centerPoint,scroll=new Vector2(0,-1)});
            yield return null; yield return null;
            Assert.AreEqual(oldSize,camera.orthographicSize,.001f);
            Vector3 cameraBefore=camera.transform.position;
            InputSystem.QueueStateEvent(mouse,new MouseState {position=centerPoint}.WithButton(MouseButton.Middle)); yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState {position=centerPoint+new Vector2(80,0)}.WithButton(MouseButton.Middle)); yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState {position=centerPoint+new Vector2(80,0)}); yield return null;
            Assert.Greater(Vector3.Distance(cameraBefore,camera.transform.position),.01f);
            yield return Capture("zoom-pan");
            yield return Click(GameObject.Find("ReturnButton").GetComponent<Button>());
            Assert.IsTrue(controller.IsWithdrawalConfirmationOpen);
            yield return Click(GameObject.Find("ContinueCombatButton").GetComponent<Button>());
            Assert.IsFalse(controller.IsWithdrawalConfirmationOpen);
            yield return Click(GameObject.Find("ReturnButton").GetComponent<Button>());
            yield return Click(GameObject.Find("ConfirmWithdrawalButton").GetComponent<Button>());
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            Assert.IsNotNull(GameObject.Find("GuanzhongBattlefield"));
            Assert.IsNull(Object.FindFirstObjectByType<CombatBattlefieldSurface>());
            yield return Click(GameObject.Find("TerrainRehearsalButton").GetComponent<Button>());
            Assert.IsTrue(Find<AdventureController>().IsTerrainRehearsal);
            Assert.AreEqual(144,Find<CombatMovementView>().Highlights.Count);
            surface=Find<CombatBattlefieldSurface>();
            yield return Capture("reentered");

            IEnumerator MoveTo(int q,int r)
            {
                CombatMovementOption move=null;
                for(int turns=0;turns<3 && move==null;turns++)
                {
                    move=Find<CombatHudPresenter>().Snapshot.Movement.Options.FirstOrDefault(o=>o.Destination.Q==q && o.Destination.R==r);
                    if(move==null)
                    {
                        long turn=session.TurnSequence;
                        yield return Click(bar.WaitButton);
                        yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
                        for(float end=Time.realtimeSinceStartup+30;session.TurnSequence==turn && Time.realtimeSinceStartup<end;) yield return null;
                        Assert.Greater(session.TurnSequence,turn);
                        yield return PlayerReady(encounter,session);
                    }
                }
                Assert.IsNotNull(move,"No quoted legal route to "+q+":"+r);
                Focus(camera,surface.Center(move.Destination));
                yield return null;
                yield return Click(bar.MoveButton);
                Vector2 p=camera.WorldToScreenPoint(surface.Center(move.Destination));
                Assert.IsTrue(movement.TryPickCell(p,out var pickedCell));
                Assert.AreEqual(move.Destination,pickedCell);
                yield return Click(p,MouseButton.Left);
                yield return PlayerReady(encounter,session);
                Assert.IsTrue(session.Combatants.TryGet("player",out var current));
                Assert.AreEqual(q,current.Position.Q); Assert.AreEqual(r,current.Position.R);
            }
            foreach(Collider c in colliders) Object.Destroy(c);
            File.WriteAllText(Path.Combine(output,"initial-proof.json"),JsonUtility.ToJson(new Proof {
                width=Screen.width,height=Screen.height,picks=picks,outlinePoints=outlinePoints,maxRingError=maxRingError,maxActualSupportError=maxActualSupportError,
                triangles=surface.GetComponentsInChildren<MeshFilter>().Sum(f=>(long)f.sharedMesh.triangles.Length/3),
                renderedObjects=surface.GetComponentsInChildren<Renderer>().Length },true));
        }
        [Serializable] private sealed class Proof { public int width,height,picks,outlinePoints,renderedObjects; public float maxRingError,maxActualSupportError; public long triangles; }
        [Serializable] private sealed class Load { public float meanMilliseconds,p95Milliseconds; public int visibleTriangles,drawCalls; public long managedBytes,totalAllocatedBytes; }
        private sealed class WaitPolicy:ICombatActionPolicy
        {
            public CombatCommand ChooseAction(CombatSession session,IReadOnlyList<CombatCommand> legal) =>
                legal.FirstOrDefault(c=>c.Kind==CombatCommandKind.EndTurn) ?? legal.FirstOrDefault(c=>c.Kind==CombatCommandKind.Wait);
        }
        private static void Focus(Camera camera,Vector3 target) => camera.transform.position=target-camera.transform.forward*20;
        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
            yield return null;
        }
        private IEnumerator Click(Button button)
        {
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(button.interactable && button.gameObject.activeInHierarchy,button.name);
            yield return Click(RectTransformUtility.WorldToScreenPoint(null,button.transform.position),MouseButton.Left);
        }
        private IEnumerator Click(Vector2 point,MouseButton button)
        {
            InputSystem.QueueStateEvent(mouse,new MouseState { position=point }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState { position=point }.WithButton(button)); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState { position=point }); yield return null; yield return null;
        }
        private static IEnumerator PlayerReady(EncounterCoordinator encounter,CombatSession session)
        {
            for(float end=Time.realtimeSinceStartup+50;Time.realtimeSinceStartup<end &&
                (session.CurrentActorId!="player" || encounter.IsPresenting || !encounter.AcceptsPlayerCommand);) yield return null;
            Assert.IsTrue(encounter.AcceptsPlayerCommand,"Player turn did not become ready.");
        }
        private static IEnumerator SceneReady(string name)
        { for(int i=0;i<300 && SceneManager.GetActiveScene().name!=name;i++) yield return null; yield return null; yield return null; Assert.AreEqual(name,SceneManager.GetActiveScene().name); }
        private static T Find<T>() where T:Object => Object.FindFirstObjectByType<T>() ?? throw new Exception(typeof(T).Name+" missing");
        private static T Get<T>(object owner,string name) => (T)owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
        private static void Set(object owner,string name,object value) => owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(owner,value);
    }
}
