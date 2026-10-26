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
using TianZhang.Spatial;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class D7EarthPlayModeTests
    {
        private const string Earth="demo_20261024_earth", Water="demo_20261024_water";
        private Mouse mouse;
        private string saves,output;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private CombatBattlefieldSurface surface;
        private JindanDemoView view;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldInput;
        private bool configured;
        private float oldCaptureDelta;
        private HexCoord selectedA,selectedB;
        private readonly List<string> evidence=new List<string>();
        private CombatantSnapshot Actor {get{session.Combatants.TryGet(Earth,out var a);return a;}}
        private CombatJindanSeatState Seat=>session.Jindan.Seats.Single(s=>s.Definition.ActorId==Earth && s.EffectId.StartsWith("EARTH_"));

        [UnityTest,Timeout(600000)] public IEnumerator ProductionWallAndTwoHitDemolition()=>WallLoop(false);
        [UnityTest,Timeout(1800000)] public IEnumerator NativeWallAndTwoHitDemolition()=>WallLoop(true);
        [UnityTest,Timeout(600000)] public IEnumerator AirborneSourceLowersFlyingAlly()=>GravityLoop(true);
        [UnityTest,Timeout(600000)] public IEnumerator GroundSourceLandsFlyingAlly()=>GravityLoop(false);

        private IEnumerator Enter(bool manifest,int stage=1)
        {
            yield return Click(GameObject.Find("JindanRehearsalButton").GetComponent<Button>());
            view=Find<JindanDemoView>();yield return Press("stage"+stage);
            if(manifest)yield return Press("earth_toggle");
            yield return Press("enter");encounter=Find<EncounterCoordinator>();session=encounter.CurrentSession;
            surface=Find<CombatBattlefieldSurface>();yield return Ready();Assert.AreEqual(Earth,session.CurrentActorId);
            yield return Press("earth_tab");
        }
        private CombatCommand Deploy(CombatEarthMode mode,HexCoord a,HexCoord b)=>
            new CombatCommand(CombatCommandKind.JindanAbility,Earth,profileId:"EARTH_MANIFEST",
                jindan:new CombatJindanRequest(Seat.Definition.SeatId,new[]{a,b},earthMode:mode));
        private IEnumerator SelectEdge(string key,HexCoord a,HexCoord b)
        {
            yield return Press(key);yield return Press("toggle_panel");yield return Click(Point(a));yield return Click(Point(b));
            yield return Press("toggle_panel");yield return Press("earth_preview");
            Assert.IsTrue(view.GetButton("confirm").interactable,"edge quote failed: "+a+" -> "+b);
            yield return Press("confirm");yield return Ready();
        }
        private IEnumerator WallLoop(bool native)
        {
            yield return Setup(native);yield return Enter(true);
            string saved=JsonUtility.ToJson(CharacterRecord.Capture(GameBootstrap.RequireRuntime().Player.Capture()));
            var board=session.SceneObjects.Board;
            var edge=(from a in Actor.Position.Neighbors() from b in a.Neighbors()
                where encounter.PreviewJindan(Deploy(CombatEarthMode.Wall,a,b)).IsLegal
                orderby a.R descending,a.Q,b.R descending select (a,b)).First();
            selectedA=edge.a;selectedB=edge.b;
            Record("selected wall "+edge.a+" -> "+edge.b);
            if(native){RemoveMouse();yield return NativeStep("build","点击岩墙，收起面板，依次选A/B，展开面板后预演并确认。",()=>Seat.ActiveEarth!=null);}
            else yield return SelectEdge("earth_wall",edge.a,edge.b);
            yield return Ready();var wall=Seat.ActiveEarth.Objects.Single();
            Assert.AreEqual(750,wall.Durability);Assert.AreEqual(750000,Actor.ActionPointsRemainingUnits);Assert.AreEqual(5600,Actor.CurrentSpirit);
            Assert.IsFalse(session.SceneObjects.Board.InspectEdge(edge.a,edge.b,SpatialQueryKind.Attack).IsLegal);
            Assert.IsNotNull(GameObject.Find(wall.Id));Record("wall750 blocks movement and line; paid250AP400MP");yield return Capture("01-wall750");
            for(int hit=1;hit<=2;hit++)
            {
                int expected=750-hit*375;
                if(native)yield return NativeStep("hit"+hit,"点击破垒、收起面板、点击A，再展开并确认施放。",()=>wall.Durability==expected);
                else
                {
                    yield return Press("break_object");yield return Press("toggle_panel");yield return Click(Point(edge.a));
                    yield return Press("toggle_panel");
                    string details=GameObject.Find("CombatQuote").GetComponent<Text>().text;
                    Record("demolition preview: "+details);
                    Assert.IsTrue(view.GetButton("confirm").interactable,details);
                    yield return Press("confirm");
                }
                yield return Ready();Assert.AreEqual(expected,wall.Durability);Record("break hit"+hit+" durability="+wall.Durability);
                yield return Capture("0"+(hit+1)+"-wall"+expected);
            }
            Assert.IsNull(Seat.ActiveEarth);Assert.IsNull(GameObject.Find(wall.Id));
            Assert.IsTrue(session.SceneObjects.Board.InspectEdge(edge.a,edge.b,SpatialQueryKind.Attack).IsLegal);
            Assert.AreEqual(saved,JsonUtility.ToJson(CharacterRecord.Capture(GameBootstrap.RequireRuntime().Player.Capture())));
            if(native)EnsureMouse();
            var old=session;yield return Click(GameObject.Find("ReturnButton").GetComponent<Button>());
            yield return Click(GameObject.Find("ConfirmWithdrawalButton").GetComponent<Button>());
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(old.Jindan.Seats.All(s=>s.ActiveEarth==null));
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));yield return SceneReady(GameplaySceneNames.Adventure);
            yield return Enter(true);Assert.AreEqual(6000,Actor.CurrentSpirit);Assert.IsNull(Seat.ActiveEarth);
            Record("withdrawal, fresh entry and world identity preserved");
            File.WriteAllText(Path.Combine(output,"pending.json"),"{\"stage\":\"complete\"}");
        }

        [UnityTest,Timeout(600000)]
        public IEnumerator ProductionRampChangesRouteAndSupportsActualTraversal()
        {
            yield return Setup(false);yield return Enter(true);
            encounter.RequestMove(Earth,7,7);yield return Ready();Assert.AreEqual(new HexCoord(7,7),Actor.Position);
            // Position the inspection camera around this actual high-platform route, preserving production scale.
            Camera.main.transform.position += surface.Center(new CombatUnitPresentationHex(7,7))-surface.Center(new CombatUnitPresentationHex(4,0));
            var board=session.SceneObjects.Board;
            var candidates=(from a in surface.Cells where a.kind==0 from b in new HexCoord(a.q,a.r).Neighbors()
                where surface.Cells.Any(c=>c.q==b.Q && c.r==b.R && c.kind==0)
                let lowCell=new HexCoord(a.q,a.r)
                where board.TryGetCell(lowCell,out var low) && board.TryGetCell(b,out var high) && high.HeightLevel-low.HeightLevel==1
                where encounter.PreviewJindan(Deploy(CombatEarthMode.Ramp,lowCell,b)).IsLegal
                let route=board.QueryMovementPath(Actor.Pose,lowCell,500000,Actor.MovementEfficiencyMilliUnits,Occupied())
                where route.Steps.Count>0
                orderby route.ActionPointCostUnits select (a:lowCell,b)).ToArray();
            Assert.IsNotEmpty(candidates,"No legal ramp near spawn");var edge=candidates[0];
            Assert.IsFalse(board.InspectEdge(edge.a,edge.b,SpatialQueryKind.Movement).IsLegal);
            yield return SelectEdge("earth_ramp",edge.a,edge.b);var ramp=Seat.ActiveEarth.Objects.Single();
            Assert.IsTrue(session.SceneObjects.Board.InspectEdge(edge.a,edge.b,SpatialQueryKind.Movement).IsLegal);
            VerifyRampMesh(ramp);yield return Press("toggle_panel");yield return Capture("01-created-ramp");
            encounter.RequestMove(Earth,edge.a.Q,edge.a.R);yield return Ready();Assert.AreEqual(edge.a,Actor.Position);
            var provider=Find<Static3DCombatUnitPresentationProvider>();Assert.IsTrue(provider.TryGetPresentedUnit(Earth,out var piece));
            var mesh=piece.GetComponentsInChildren<MeshFilter>().First(f=>f.sharedMesh.vertexCount>1000);
            var localPoints=mesh.sharedMesh.vertices;
            float soleTop=localPoints.Min(p=>p.y)+.01f/1.4f;
            float maxPenetration=0,maxFootGap=0;int frames=0;
            Time.captureDeltaTime=1f/30f;
            encounter.RequestMove(Earth,edge.b.Q,edge.b.R);
            for(float end=Time.realtimeSinceStartup+10;Time.realtimeSinceStartup<end && !encounter.AcceptsPlayerCommand;)
            {
                float left=float.PositiveInfinity,right=float.PositiveInfinity;
                foreach(var point in localPoints)
                {
                    Vector3 world=mesh.transform.TransformPoint(point);
                    if(!surface.TryHeight(world,out float y))continue;
                    maxPenetration=Mathf.Max(maxPenetration,y-world.y);
                    if(point.y<=soleTop){if(point.x<0)left=Mathf.Min(left,world.y-y);else right=Mathf.Min(right,world.y-y);}
                }
                maxFootGap=Mathf.Max(maxFootGap,Mathf.Max(left,right));frames++;
                if(frames==5)yield return Capture("02-ramp-moving");
                yield return null;
            }
            yield return Ready();Assert.AreEqual(edge.b,Actor.Position);
            Time.captureDeltaTime=oldCaptureDelta;
            Record("ramp motion frames="+frames+" maxBodyPenetration="+maxPenetration+" maxFootGap="+maxFootGap);
            Assert.Greater(frames,5);Assert.Less(maxPenetration,.002f);Assert.Less(maxFootGap,.002f);
            Record("real ramp traversal "+edge.a+" -> "+edge.b);yield return Capture("02-ramp-climbed");
            yield return Press("toggle_panel");yield return Press("earth_close");yield return Press("confirm");yield return Ready();
            Assert.IsFalse(session.SceneObjects.Board.InspectEdge(edge.a,edge.b,SpatialQueryKind.Movement).IsLegal);
            Assert.IsFalse(surface.HasEarthRamp(new CombatUnitPresentationHex(edge.a.Q,edge.a.R),new CombatUnitPresentationHex(edge.b.Q,edge.b.R)));
            Assert.IsNull(GameObject.Find(ramp.Id));Record("ramp close restores original edge while occupant remains on legal high cell");
        }
        private void VerifyRampMesh(CombatSceneObject ramp)
        {
            var root=GameObject.Find(ramp.Id);Assert.IsNotNull(root);
            var filter=root.GetComponentInChildren<MeshFilter>();
            float maximumError=0;int samples=0;
            foreach(var vertex in filter.sharedMesh.vertices)
            {
                var p=filter.transform.TransformPoint(vertex);
                // Top edge vertices are sampled against the same physical surface used by foot fitting and picking.
                float localZ=root.transform.InverseTransformPoint(p).z;
                float expected=.75f*(1-Mathf.Cos(Mathf.PI*Mathf.Clamp01((localZ-.1f)/.4f)))*.5f;
                if(Mathf.Abs(root.transform.InverseTransformPoint(p).y-expected)>.0001f)continue;
                Assert.IsTrue(surface.TryHeight(p,out float y));maximumError=Mathf.Max(maximumError,Mathf.Abs(p.y-y));samples++;
            }
            Assert.Greater(samples,100);Assert.Less(maximumError,.001f);Record("ramp mesh/support vertices="+samples+" maxError="+maximumError);
            var sampleObject=new GameObject("D7RampSixDirections");
            var sampleSurface=sampleObject.AddComponent<CombatBattlefieldSurface>();
            var originalPosition=root.transform.position;var originalRotation=root.transform.rotation;var originalScale=root.transform.localScale;
            int checkedVertices=0;float rotationError=0;
            foreach(float rise in new[]{.375f,.75f})for(int direction=0;direction<6;direction++)
            {
                var a=new CombatUnitPresentationHex(0,0);var b=CombatUnitPresentationPose.Step(a,direction);
                Set(sampleSurface,"cells",new[]{new CombatBattlefieldSurface.Cell{q=0,r=0,lowerY=0},new CombatBattlefieldSurface.Cell{q=b.Q,r=b.R,lowerY=rise}});
                Set(sampleSurface,"index",null);
                Vector3 low=sampleSurface.Center(a),high=sampleSurface.Center(b),forward=high-low;forward.y=0;
                sampleSurface.SetEarthRamps(new[]{new CombatBattlefieldSurface.EarthRamp(a,b,low,high)});
                root.transform.SetPositionAndRotation(low,Quaternion.LookRotation(forward));root.transform.localScale=new Vector3(1,rise/.75f,1);
                foreach(var vertex in filter.sharedMesh.vertices)
                {
                    Vector3 p=filter.transform.TransformPoint(vertex);Vector3 local=root.transform.InverseTransformPoint(p);
                    float expected=.75f*(1-Mathf.Cos(Mathf.PI*Mathf.Clamp01((local.z-.1f)/.4f)))*.5f;
                    if(Mathf.Abs(local.y-expected)>.0001f)continue;
                    Assert.IsTrue(sampleSurface.TryHeight(p,out float y));rotationError=Mathf.Max(rotationError,Mathf.Abs(p.y-y));
                    Assert.IsTrue(sampleSurface.TryPick(new Ray(p+Vector3.up*3,Vector3.down),out _));checkedVertices++;
                }
            }
            root.transform.SetPositionAndRotation(originalPosition,originalRotation);root.transform.localScale=originalScale;Object.Destroy(sampleObject);
            Assert.Less(rotationError,.001f);Record("six directions, two physical rises: vertices/picks="+checkedVertices+" maxError="+rotationError);
        }
        [UnityTest,Timeout(600000)]
        public IEnumerator ProductionLightLeapKeepsGroundModeAndClearsTheLedge()
        {
            yield return Setup(false);yield return Enter(false);
            var low=new HexCoord(7,8);var high=new HexCoord(7,9);
            encounter.RequestMove(Earth,low.Q,low.R);yield return Ready();Assert.AreEqual(low,Actor.Position);
            Camera.main.transform.position+=surface.Center(new CombatUnitPresentationHex(7,8))-surface.Center(new CombatUnitPresentationHex(4,0));
            yield return Press("earth_light");yield return Press("toggle_panel");yield return Click(Point(low));yield return Press("toggle_panel");
            yield return Press("earth_preview");Assert.IsTrue(view.GetButton("confirm").interactable);yield return Press("confirm");yield return Ready();
            var command=new CombatCommand(CombatCommandKind.Move,Earth,destination:high);
            var quote=encounter.PreviewJindan(command);Assert.IsTrue(quote.IsLegal,quote.RejectionReason);
            Assert.AreEqual(1,quote.MovementSteps.Count);Assert.IsTrue(quote.MovementSteps[0].IsLightLeap);
            Assert.AreEqual(15625,quote.ActionPointCostUnits);
            yield return Press("toggle_panel");
            var provider=Find<Static3DCombatUnitPresentationProvider>();Assert.IsTrue(provider.TryGetPresentedUnit(Earth,out var root));
            encounter.RequestMove(Earth,high.Q,high.R);float highest=root.transform.position.y;bool captured=false;
            for(float end=Time.realtimeSinceStartup+10;Time.realtimeSinceStartup<end && !encounter.AcceptsPlayerCommand;)
            {
                highest=Mathf.Max(highest,root.transform.position.y);
                if(!captured && root.transform.position.y>surface.Center(new CombatUnitPresentationHex(high.Q,high.R)).y+.3f)
                {captured=true;yield return Capture("01-light-leap-apex");}
                yield return null;
            }
            yield return Ready();Assert.AreEqual(high,Actor.Position);Assert.AreEqual(SpatialMovementMode.Ground,Actor.MovementMode);
            Assert.AreEqual(0,Actor.AltitudeBand);Assert.IsFalse(session.Flight.Find(Earth).Active);Assert.IsTrue(captured);
            Record("single-edge light leap cost="+quote.ActionPointCostUnits+" maxRootY="+highest+" remainsGROUND; no flight instance");
            yield return Capture("02-light-leap-landed");
        }
        private IEnumerator GravityLoop(bool sourceAir)
        {
            yield return Setup(false);yield return Enter(false,2);
            if(sourceAir){yield return Press("flight_tab");yield return Press("flight_takeoff");yield return Press("confirm");yield return Ready();}
            yield return EndCurrentTurn();Assert.AreEqual(Water,session.CurrentActorId);
            yield return Press("flight_tab");yield return Press("flight_takeoff");yield return Press("confirm");yield return Ready();
            yield return Press("flight_ascend");yield return Press("confirm");yield return Ready();
            session.Combatants.TryGet(Water,out var water);Assert.AreEqual(2,water.AltitudeBand);
            yield return EndCurrentTurn();Assert.AreEqual(Earth,session.CurrentActorId);
            int hp=water.CurrentHealth,ap=water.ActionPointsRemainingUnits;
            yield return Press("earth_tab");yield return Press("earth_heavy");yield return Press("toggle_panel");yield return Click(Point(water.Position));
            yield return Press("toggle_panel");yield return Press("earth_preview");Assert.IsTrue(view.GetButton("confirm").interactable);
            yield return Press("confirm");yield return Ready();
            int expected=sourceAir?1:0;Assert.AreEqual(expected,water.AltitudeBand);Assert.AreEqual(ap,water.ActionPointsRemainingUnits);
            Assert.AreEqual(sourceAir?hp:hp-(water.MaximumHealth+9)/10,water.CurrentHealth);
            var provider=Find<Static3DCombatUnitPresentationProvider>();Assert.IsTrue(provider.TryGetPresentedUnit(Water,out var root));
            float offset=root.transform.position.y-surface.Center(new CombatUnitPresentationHex(water.Position.Q,water.Position.R)).y;
            Assert.That(offset,Is.EqualTo(expected*.75f).Within(.02f));
            var ring=root.GetComponentsInChildren<LineRenderer>(true).Single(l=>l.name=="FlightGroundProjection");Assert.AreEqual(sourceAir,ring.enabled);
            Record("real ally takeoff/high -> gravity altitude"+expected+" rootOffset="+offset+" targetHP="+water.CurrentHealth+" unchangedAP="+ap);
            yield return Capture(sourceAir?"01-pressure-lowered":"01-pressure-landed");
            yield return Press("earth_close");yield return Press("confirm");yield return Ready();
            Assert.AreEqual(expected,water.AltitudeBand);Assert.IsNull(CombatForceMovement.Gravity(water));Record("removal does not undo damage or freely ascend");
        }
        private IEnumerator EndCurrentTurn()
        {
            long sequence=session.TurnSequence;
            yield return Click(Find<CombatActionBarView>().WaitButton);yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
            for(float end=Time.realtimeSinceStartup+60;session.TurnSequence==sequence && Time.realtimeSinceStartup<end;)yield return null;
            Assert.Greater(session.TurnSequence,sequence);yield return Ready();
        }

        private IEnumerator Setup(bool native)
        {
            oldCaptureDelta=Time.captureDeltaTime;
            evidence.Clear(); output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d7/" + (native ? "native" : TestContext.CurrentContext.Test.Name)));
            Directory.CreateDirectory(output); oldBackground = InputSystem.settings.backgroundBehavior;
            oldInput = InputSystem.settings.editorInputBehaviorInPlayMode; configured = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            EnsureMouse(); GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1366, 768);
            var stale = Object.FindFirstObjectByType<GameBootstrap>(); if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves = Path.Combine(Application.temporaryCachePath, "D7-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
            var creation = Find<CharacterCreationController>(); creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("d7", "土系验证");
            yield return SceneReady(GameplaySceneNames.World); Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation(); yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure);
        }
        private IEnumerator NativeStep(string stage, string instruction, Func<bool> condition)
        {
            Assert.IsNull(mouse); int clicks = 0;
            for (float end = Time.realtimeSinceStartup + 600, update = 0; Time.realtimeSinceStartup < end;)
            {
                if (Time.realtimeSinceStartup >= update)
                {
                    File.WriteAllText(Path.Combine(output, "pending.json"), JsonUtility.ToJson(new Pending { stage = stage, instruction = instruction,
                        buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                            .Where(b => b.interactable).Select(b => new ButtonPoint { name = b.name, position = Center(b) }).ToArray(),
                        cell = Point(selectedA), cellB = Point(selectedB), width = Screen.width, height = Screen.height }, true));
                    update = Time.realtimeSinceStartup + 1;
                }
                yield return null;
                if (Mouse.current?.native == true && Mouse.current.leftButton.wasPressedThisFrame) { clicks++; Record(stage + " native=" + Mouse.current.position.ReadValue()); }
                if (condition()) break;
            }
            Assert.IsTrue(condition(), stage + " timed out"); Assert.Greater(clicks, 0);
        }
        private IEnumerator Ready()
        {
            for (float end = Time.realtimeSinceStartup + 60; Time.realtimeSinceStartup < end &&
                !encounter.AcceptsPlayerCommand;) yield return null;
            Assert.IsTrue(encounter.AcceptsPlayerCommand); yield return null;
        }
        private IEnumerator Press(string key) => Click(view.GetButton(key));
        private HexCoord[] Occupied() => session.Combatants.All.Where(a => a.IsAlive && a.Id != Earth).SelectMany(a => a.Pose.OccupiedCells).ToArray();
        private IEnumerator Click(Button button)
        {
            Canvas.ForceUpdateCanvases(); Assert.IsNotNull(button); Assert.IsTrue(button.interactable && button.gameObject.activeInHierarchy, button.name);
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Center(button) }, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, button.name + " covered");
            yield return Click(Center(button));
        }
        private IEnumerator Click(Vector2 point)
        {
            Assert.IsNotNull(mouse); InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private static Vector2 Center(Button b) { var r = (RectTransform)b.transform; return RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center)); }
        private Vector2 Point(HexCoord cell) => Camera.main.WorldToScreenPoint(surface.Center(new CombatUnitPresentationHex(cell.Q, cell.R)));
        private void EnsureMouse() { if (mouse == null) mouse = InputSystem.AddDevice<Mouse>(); }
        private void RemoveMouse() { if (mouse != null) InputSystem.RemoveDevice(mouse); mouse = null; }
        private void Record(string text)
        { evidence.Add(text + " | " + (session == null ? "" : "tick=" + session.TurnScheduler.CurrentTick + " AP=" + Actor.ActionPointsRemainingUnits + " MP=" + Actor.CurrentSpirit + " HP=" + Actor.CurrentHealth)); File.WriteAllLines(Path.Combine(output, "proof.txt"), evidence); }
        private IEnumerator Capture(string name) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); yield return null; }
        private static IEnumerator SceneReady(string name) { for (int i = 0; i < 300 && SceneManager.GetActiveScene().name != name; i++) yield return null; yield return null; yield return null; Assert.AreEqual(name, SceneManager.GetActiveScene().name); }
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>() ?? throw new Exception(typeof(T).Name + " missing");
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
        [Serializable] private sealed class Pending { public string stage, instruction; public int width, height; public Vector2 cell, cellB; public ButtonPoint[] buttons; }
        [Serializable] private sealed class ButtonPoint { public string name; public Vector2 position; }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.captureDeltaTime=oldCaptureDelta;
            RemoveMouse(); if (configured) { InputSystem.settings.backgroundBehavior = oldBackground; InputSystem.settings.editorInputBehaviorInPlayMode = oldInput; }
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>(); if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            if (saves != null && Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }
}
