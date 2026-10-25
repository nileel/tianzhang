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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class D4CombatEnvironmentPlayModeTests
    {
        private Mouse mouse;
        private string saves, output;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private CombatBattlefieldSurface surface;
        private CombatActionBarView bar;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldEditorInput;
        private readonly List<string> evidence = new List<string>();
        [UnityTest, Timeout(600000)] public IEnumerator ProductionDestructionAndReentry() => Verify(false);
        [UnityTest, Timeout(1800000)] public IEnumerator NativeDetourAndBreakthrough() => Verify(true);

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            if (saves != null && Directory.Exists(saves)) Directory.Delete(saves, true);
        }
        private IEnumerator Verify(bool native)
        {
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d4/" + (native ? "native" : "production")));
            Directory.CreateDirectory(output);
            oldBackground = InputSystem.settings.backgroundBehavior;
            oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>();
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1366, 768);
            var stale = Object.FindFirstObjectByType<GameBootstrap>();
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves = Path.Combine(Application.temporaryCachePath, "D4-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
            var creation = Find<CharacterCreationController>();
            creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            creation.Submit("d4", "破路验证");
            yield return SceneReady(GameplaySceneNames.World);
            Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            yield return EnterAdventure();
            yield return Click(GameObject.Find("TerrainRehearsalButton").GetComponent<Button>());
            BindRehearsal();
            yield return Ready();
            Assert.AreEqual(7, session.SceneObjects.All.Count);
            Assert.AreEqual(60, Wall.Durability);
            // Preparation uses normal UI commands and budgets. Enemy decisions alone are isolated from terrain acceptance.
            foreach (string key in "4:1,5:1,5:2,5:3,5:4,5:5,5:6,5:7,6:7,7:7,8:7,8:8,8:9,8:10,8:11,9:11,9:12,8:13".Split(','))
            { int[] qr = key.Split(':').Select(int.Parse).ToArray(); yield return Move(qr[0], qr[1]); }
            yield return EndTurn();
            Focus(new CombatUnitPresentationHex(8,14));
            yield return Click(bar.BasicAttackButton);
            yield return Click(Point(8,14));
            yield return Ready();
            Assert.AreEqual(60, Wall.Durability, "Ordinary empty attack may not damage the wall.");
            evidence.Add("ordinary empty attack: wall remains 60");
            yield return Move(8,14);
            yield return EndTurn();
            Focus(new CombatUnitPresentationHex(9,14));
            var detour = Option(9,14);
            Assert.AreEqual(2, detour.Path.Count, string.Join(";", detour.Path.Select(Key)));
            evidence.Add("intact path=" + string.Join(";", detour.Path.Select(Key)));
            yield return Capture("01-intact");
            if (native) yield return ObserveNative("detour", () => Player.Position == new HexCoord(9,14), new HexCoord(9,14));
            else yield return Move(9,14);
            yield return Ready();
            Assert.AreEqual(60, Wall.Durability);
            yield return Capture("02-detour");
            EnsureTestMouse();
            yield return Move(8,14);
            yield return EndTurn();
            Focus(new CombatUnitPresentationHex(9,14));
            if (native) yield return ObserveNative("break-wall", () => Wall.IsDestroyed, new HexCoord(9,14));
            else
            {
                yield return Strike();
                Assert.AreEqual(30, Wall.Durability);
                Assert.IsTrue(Wall.IsDamaged);
                Assert.AreEqual(2, Option(9,14).Path.Count);
                yield return Capture("03-damaged");
                yield return EndTurn();
                yield return Strike();
            }
            yield return Ready();
            Assert.AreEqual(0, Wall.Durability);
            Assert.AreEqual(60, session.SceneObjects.Find("HighWall_8_15").Durability, "Same connection id must not merge distinct walls.");
            var binding = Get<CombatSceneObjectView.Binding[]>(Find<CombatSceneObjectView>(), "bindings")
                .Single(value => value.instanceId == "HighWall_8_14");
            Assert.IsTrue(binding.residue.activeSelf);
            Assert.IsFalse(binding.intact.activeSelf);
            Assert.IsFalse(binding.damaged.activeSelf);
            var contact = session.SceneObjects.Board.QueryContact(Player.Pose, new SpatialPose(new HexCoord(9,14)), 1, 1);
            Assert.IsTrue(contact.IsInRange, contact.Reason);
            var direct = Option(9,14);
            Assert.AreEqual(1, direct.Path.Count);
            evidence.Add("destroyed path=" + string.Join(";", direct.Path.Select(Key)) + "; wall=0; other wall=60; LOS=true");
            yield return Capture("04-destroyed");
            if (native) yield return ObserveNative("breakthrough", () => Player.Position == new HexCoord(9,14), new HexCoord(9,14));
            else yield return Move(9,14);
            yield return Ready();
            yield return Capture("05-breakthrough");
            EnsureTestMouse();
            yield return Withdraw();
            yield return EnterAdventure();
            Assert.IsNotNull(GameObject.Find("GuanzhongBattlefield"));
            Assert.IsFalse(Find<AdventureController>().IsTerrainRehearsal);
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            yield return null;
            encounter = Find<EncounterCoordinator>();
            session = Get<CombatSession>(encounter, "session");
            Assert.IsNotNull(session);
            Assert.IsNull(session.SceneObjects, "Ordinary bounty must not receive rehearsal objects.");
            yield return Ready();
            bar=Find<CombatActionBarView>();
            int bountyAp=Player.ActionPointsRemainingUnits;
            yield return Click(bar.GuardButton);
            yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
            yield return Ready();
            Assert.Less(Player.ActionPointsRemainingUnits,bountyAp);
            evidence.Add("ordinary bounty entered; environment=null; guard committed and paid AP");
            yield return Withdraw();
            yield return EnterAdventure();
            yield return Click(GameObject.Find("TerrainRehearsalButton").GetComponent<Button>());
            BindRehearsal();
            yield return Ready();
            Assert.IsTrue(session.SceneObjects.All.All(value => value.Durability == value.Definition.MaxDurability));
            Assert.IsFalse(session.SceneObjects.Board.QueryContact(new SpatialPose(new HexCoord(8,14)), new SpatialPose(new HexCoord(9,14)), 1, 1).IsInRange);
            Assert.AreEqual(2,session.SceneObjects.Board.QueryMovementPath(new SpatialPose(new HexCoord(8,14)),
                new HexCoord(9,14),100000,Player.MovementEfficiencyMilliUnits).Steps.Count);
            binding = Get<CombatSceneObjectView.Binding[]>(Find<CombatSceneObjectView>(), "bindings").Single(value => value.instanceId == "HighWall_8_14");
            Assert.IsTrue(binding.intact.activeSelf); Assert.IsFalse(binding.residue.activeSelf);
            evidence.Add("reentry: all 7 durability reset; wall blocks movement/effects; intact visible");
            yield return Capture("06-reentry");
            File.WriteAllLines(Path.Combine(output,"proof.txt"), evidence);
        }
        private IEnumerator Strike()
        {
            var hud = Find<CombatHudPresenter>();
            int index = hud.Snapshot.ArtActions.ToList().FindIndex(value => value.ProfileId == "spell_guanshiji");
            Assert.GreaterOrEqual(index,0);
            yield return Click(new[] { bar.ArtButton,bar.SecondArtButton,bar.ThirdArtButton }[index]);
            yield return Hover(Point(9,14));
            var preview = Find<CombatCommandInput>().Preview;
            Assert.IsTrue(preview.CanConfirm,preview.Reason);
            Assert.AreEqual(1,preview.TargetDistance);
            Assert.AreEqual(30,preview.AttackArea.SceneObjects.Single(value=>value.Id==Wall.Id).PredictedDamage);
            int before=Player.ActionPointsRemainingUnits, mp=Player.CurrentSpirit;
            yield return Click(Point(9,14)); yield return Ready();
            Assert.AreEqual(50000,before-Player.ActionPointsRemainingUnits);
            Assert.AreEqual(12,mp-Player.CurrentSpirit);
        }
        private IEnumerator ObserveNative(string stage,Func<bool> condition,HexCoord target)
        {
            if(mouse!=null) { InputSystem.RemoveDevice(mouse); mouse=null; }
            File.WriteAllText(Path.Combine(output,"stage.txt"),stage);
            bool nativeTargetClick=false; int lastDurability=Wall.Durability;
            for(float end=Time.realtimeSinceStartup+900;!condition() && Time.realtimeSinceStartup<end;)
            {
                yield return null;
                var real=Mouse.current;
                if(real!=null && real.native && real.leftButton.wasPressedThisFrame)
                {
                    var movement=Find<CombatMovementView>();
                    Vector2 point=real.position.ReadValue();
                    CombatUnitPresentationHex cell=default;
                    bool picked=!movement.UiOwnsPointer(point) && movement.TryPickCell(point,out cell);
                    if(picked && cell.Q==target.Q && cell.R==target.R) nativeTargetClick=true;
                    evidence.Add(stage+" native click="+point+" picked="+(picked?Key(cell):"UI")+" AP="+Player.ActionPointsRemainingUnits+" wall="+Wall.Durability);
                }
                if(Wall.Durability!=lastDurability)
                {
                    lastDurability=Wall.Durability;
                    evidence.Add(stage+" wall="+lastDurability+" AP="+Player.ActionPointsRemainingUnits+" MP="+Player.CurrentSpirit);
                    yield return Capture("native-wall-"+lastDurability);
                }
            }
            Assert.IsTrue(condition(),stage+" timed out"); Assert.IsTrue(nativeTargetClick,stage+" requires actual OS mouse target input");
            evidence.Add(stage+" completed at "+Player.Position);
            File.WriteAllLines(Path.Combine(output,"proof.txt"),evidence);
        }
        private CombatSceneObject Wall => session.SceneObjects.Find("HighWall_8_14");
        private CombatantSnapshot Player { get { session.Combatants.TryGet("player",out var value); return value; } }
        private CombatMovementOption Option(int q,int r) => Find<CombatHudPresenter>().Snapshot.Movement.Options.FirstOrDefault(value=>value.Destination.Q==q && value.Destination.R==r);
        private void BindRehearsal()
        {
            encounter=Find<EncounterCoordinator>(); session=Get<CombatSession>(encounter,"session");
            surface=Find<CombatBattlefieldSurface>(); bar=Find<CombatActionBarView>();
            Set(encounter,"enemyPolicies",new Dictionary<string,ICombatActionPolicy>{{"enemy",new WaitPolicy()},{"enemy_2",new WaitPolicy()}});
        }
        private IEnumerator Move(int q,int r)
        {
            for(int tries=0;Option(q,r)==null && tries<3;tries++) yield return EndTurn();
            Assert.IsNotNull(Option(q,r),"No move to "+q+":"+r);
            Focus(new CombatUnitPresentationHex(q,r)); yield return Click(bar.MoveButton); yield return Click(Point(q,r));
            yield return Ready(); Assert.AreEqual(new HexCoord(q,r),Player.Position);
        }
        private IEnumerator EndTurn()
        {
            long before=session.TurnSequence;
            yield return Click(bar.WaitButton); yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
            for(float end=Time.realtimeSinceStartup+40;session.TurnSequence==before && Time.realtimeSinceStartup<end;) yield return null;
            Assert.Greater(session.TurnSequence,before); yield return Ready();
        }
        private IEnumerator Ready()
        {
            for(float end=Time.realtimeSinceStartup+50;Time.realtimeSinceStartup<end && (session.CurrentActorId!="player" || encounter.IsPresenting || !encounter.AcceptsPlayerCommand);) yield return null;
            Assert.IsTrue(encounter.AcceptsPlayerCommand,"Player input never became ready");
        }
        private IEnumerator EnterAdventure() { Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure); }
        private IEnumerator Withdraw()
        {
            // AdventureController publishes return-button availability in LateUpdate.
            yield return null;
            yield return Click(GameObject.Find("ReturnButton").GetComponent<Button>());
            Assert.IsTrue(Find<AdventureController>().IsWithdrawalConfirmationOpen);
            yield return Click(GameObject.Find("ConfirmWithdrawalButton").GetComponent<Button>());
            yield return SceneReady(GameplaySceneNames.Settlement);
        }
        private void EnsureTestMouse() { if(mouse==null) mouse=InputSystem.AddDevice<Mouse>(); }
        private void Focus(CombatUnitPresentationHex cell) => Camera.main.transform.position=surface.Center(cell)-Camera.main.transform.forward*20;
        private Vector2 Point(int q,int r) => Camera.main.WorldToScreenPoint(surface.Center(new CombatUnitPresentationHex(q,r)));
        private IEnumerator Hover(Vector2 point) { InputSystem.QueueStateEvent(mouse,new MouseState {position=point}); yield return null; yield return null; }
        private IEnumerator Click(Button button)
        {
            Canvas.ForceUpdateCanvases(); Assert.IsTrue(button.interactable && button.gameObject.activeInHierarchy,button.name);
            yield return Click(RectTransformUtility.WorldToScreenPoint(null,button.transform.position));
        }
        private IEnumerator Click(Vector2 point)
        {
            yield return Hover(point); InputSystem.QueueStateEvent(mouse,new MouseState {position=point}.WithButton(MouseButton.Left));
            yield return null; yield return null; yield return Hover(point);
        }
        private IEnumerator Capture(string name)
        { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png")); yield return null; }
        private static IEnumerator SceneReady(string name)
        { for(int i=0;i<300 && SceneManager.GetActiveScene().name!=name;i++) yield return null; yield return null; yield return null; Assert.AreEqual(name,SceneManager.GetActiveScene().name); }
        private sealed class WaitPolicy:ICombatActionPolicy
        { public CombatCommand ChooseAction(CombatSession s,IReadOnlyList<CombatCommand> legal) => legal.FirstOrDefault(c=>c.Kind==CombatCommandKind.EndTurn) ?? legal.FirstOrDefault(c=>c.Kind==CombatCommandKind.Wait); }
        private static string Key(CombatUnitPresentationHex cell)=>cell.Q+":"+cell.R;
        private static T Find<T>() where T:Object => Object.FindFirstObjectByType<T>() ?? throw new Exception(typeof(T).Name+" missing");
        private static T Get<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
        private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(owner,value);
    }
}
