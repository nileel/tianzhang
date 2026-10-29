using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    // Production entry, movement quotes, commits, HUD and prefab states. Enemy turns
    // wait to isolate the prop check. The 15-durability state is an explicit fixture:
    // the currently available environmental attack deals 30 damage in one hit.
    public sealed class R4LowCoverEvidenceTests
    {
        private EncounterCoordinator encounter;
        private CombatSession session;
        private CombatBattlefieldSurface surface;
        private string saves, output;
        private readonly CombatCommandService commands = new();
        private readonly List<string> ledger = new();
        private readonly List<(double time, Texture2D image)> frames = new();

        [UnityTest, Timeout(600000)] public IEnumerator StatesAndPassage1366() => Verify(1366);
        [UnityTest, Timeout(600000)] public IEnumerator StatesAndPassage1920() => Verify(1920);

        private IEnumerator Verify(int width)
        {
            ledger.Clear(); frames.Clear();
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/r4", width.ToString()));
            Directory.CreateDirectory(output);
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(width, width == 1366 ? 768 : 1080);
            var stale = Object.FindFirstObjectByType<GameBootstrap>();
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves = Path.Combine(Application.temporaryCachePath, "R4-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
            var creation = Find<CharacterCreationController>();
            creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("r4", "残垣取证");
            yield return SceneReady(GameplaySceneNames.World);
            Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            encounter = Find<EncounterCoordinator>();
            var adventure = Find<AdventureController>();
            for (int i = 0; i < 180 && !adventure.CanSelectExplorationNode; i++) yield return null;
            Assert.IsTrue(adventure.CanSelectExplorationNode);
            Find<AdventureSceneInstaller>().BeginTerrainRehearsal();
            session = Get<CombatSession>(encounter, "session");
            surface = Find<CombatBattlefieldSurface>();
            Set(encounter, "enemyPolicies", new Dictionary<string, ICombatActionPolicy>
                { { "enemy", new WaitPolicy() }, { "enemy_2", new WaitPolicy() } });
            yield return Ready();
            Assert.AreEqual(144, surface.Cells.Count);
            Assert.AreEqual(width, Screen.width);
            Assert.AreEqual(width == 1366 ? 768 : 1080, Screen.height);
            var walls = session.SceneObjects.All.Where(o => o.Definition.ContentType == "low_wall").OrderBy(o => o.Cell.Q).ToArray();
            Assert.AreEqual(2, walls.Length);
            foreach (var wall in walls) CheckAsset(wall);
            foreach (string cell in "4:1,5:1,5:2,5:3,5:4,5:5,4:5".Split(',')) yield return Move(Parse(cell));
            foreach (var wall in walls)
            {
                if (wall != walls[0])
                    foreach (string cell in "5:5,5:6,5:7,6:7,7:7,8:7,9:7".Split(',')) yield return Move(Parse(cell));
                Assert.AreEqual(wall.Cell, Player.Position);
                int originalQuote = 0;
                for (int state = 0; state < 3; state++)
                {
                    if (state > 0)
                    {
                        // No player damage command is fabricated: this fixture exercises
                        // the real session projection and view at the half-durability boundary.
                        typeof(CombatSceneObjects).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(session.SceneObjects, new object[] { session.SceneObjects.PrepareDamage(new[] { wall.Id }, 15) });
                        typeof(EncounterCoordinator).GetMethod("Present", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(encounter, new object[] { "残垣状态取证", true, null });
                    }
                    Assert.AreEqual(new[] { 30, 15, 0 }[state], wall.Durability);
                    Assert.AreEqual(new[] { 2, 1, 0 }[state], wall.CoverTier);
                    var binding = Get<CombatSceneObjectView.Binding[]>(Find<CombatSceneObjectView>(), "bindings").Single(b => b.instanceId == wall.Id);
                    var states = new[] { binding.intact, binding.damaged, binding.residue };
                    for (int i = 0; i < 3; i++) Assert.AreEqual(i == state, states[i].activeInHierarchy);
                    Focus(wall);
                    yield return Capture(wall.Id + "-state-" + state);
                    if (wall == walls[0]) { yield return HoldFrames(.3f); }
                    yield return EnsureBudget(wall.EdgeOther.Value);
                    var quote = Quote(wall.EdgeOther.Value);
                    Assert.AreEqual(1, quote.MovementSteps.Count);
                    if (state == 0) originalQuote = quote.ActionPointCostUnits;
                    else Assert.AreEqual(originalQuote, quote.ActionPointCostUnits, "Low-cover state adds no traversal surcharge.");
                    yield return Move(wall.EdgeOther.Value, wall.Id + "-" + state, wall == walls[0]);
                    if (wall == walls[0]) { yield return HoldFrames(.3f); }
                    yield return EnsureBudget(wall.Cell);
                    Assert.AreEqual(1, Quote(wall.Cell).MovementSteps.Count);
                    Assert.AreEqual(originalQuote, Quote(wall.Cell).ActionPointCostUnits);
                    yield return Move(wall.Cell, wall.Id + "-" + state + "-reverse");
                }
            }
            File.WriteAllLines(Path.Combine(output, "ap-ledger.csv"), new[] { "stage,from,to,preview_ap_units,before_ap_units,after_ap_units" }.Concat(ledger));
            FlushFrames();
        }

        private void CheckAsset(CombatSceneObject wall)
        {
            var binding = Get<CombatSceneObjectView.Binding[]>(Find<CombatSceneObjectView>(), "bindings").Single(b => b.instanceId == wall.Id);
            Transform root = binding.intact.transform.parent;
            Vector3 anchor = (Center(wall.Cell) + Center(wall.EdgeOther.Value)) * .5f;
            Assert.That(Vector3.Distance(root.position, anchor), Is.LessThan(.0001f));
            Assert.AreEqual(new Vector3(.93f, 1, 1), root.localScale);
            Assert.IsEmpty(root.GetComponentsInChildren<Collider>(true));
            var intact = binding.intact.GetComponentsInChildren<Renderer>(true).Single();
            var damaged = binding.damaged.GetComponentsInChildren<Renderer>(true).Single();
            Assert.That(intact.bounds.size.y, Is.EqualTo(.375f).Within(.0001f));
            Assert.That(damaged.bounds.size.y, Is.InRange(.23f, .25f));
            Assert.That(Mathf.Abs(intact.bounds.min.y - anchor.y), Is.LessThan(.0001f));
            Assert.That(Mathf.Abs(damaged.bounds.min.y - anchor.y), Is.LessThan(.0001f));
            Assert.AreEqual(4096, intact.sharedMaterial.mainTexture.width);
            Assert.AreSame(intact.sharedMaterial, damaged.sharedMaterial);
        }
        private IEnumerator EnsureBudget(HexCoord cell)
        {
            if (!Quote(cell).IsLegal) { encounter.RequestEndTurn("player"); yield return Ready(); }
            Assert.IsTrue(Quote(cell).IsLegal, Quote(cell).RejectionReason);
        }
        private IEnumerator Move(HexCoord cell, string stage = "approach", bool record = false)
        {
            yield return EnsureBudget(cell);
            var quote = Quote(cell);
            var option = Find<CombatHudPresenter>().Snapshot.Movement.Options.Single(o => o.Destination.Q == cell.Q && o.Destination.R == cell.R);
            Assert.AreEqual(quote.ActionPointCostUnits, option.Cost);
            int before = Player.ActionPointsRemainingUnits;
            string from = Player.Position.Q + ":" + Player.Position.R;
            encounter.RequestMove("player", cell.Q, cell.R);
            do { yield return new WaitForEndOfFrame(); if (record) Frame(); } while (encounter.IsPresenting);
            yield return Ready();
            Assert.AreEqual(cell, Player.Position);
            Assert.Greater(quote.ActionPointCostUnits, 0);
            Assert.AreEqual(quote.ActionPointCostUnits, before - Player.ActionPointsRemainingUnits);
            ledger.Add(string.Join(",", stage, from, cell.Q + ":" + cell.R, quote.ActionPointCostUnits, before, Player.ActionPointsRemainingUnits));
        }
        private CombatCommandPreview Quote(HexCoord cell) => commands.Preview(session, new CombatCommand(CombatCommandKind.Move, "player", destination: cell));
        private CombatantSnapshot Player { get { session.Combatants.TryGet("player", out var value); return value; } }
        private Vector3 Center(HexCoord cell) => surface.Center(new CombatUnitPresentationHex(cell.Q, cell.R));
        private void Focus(CombatSceneObject wall)
        {
            Camera.main.orthographicSize = 5;
            Camera.main.transform.position = (Center(wall.Cell) + Center(wall.EdgeOther.Value)) * .5f - Camera.main.transform.forward * 20;
        }
        private IEnumerator Ready()
        {
            double end = Time.realtimeSinceStartupAsDouble + 60;
            do { yield return null; } while (!encounter.AcceptsPlayerCommand && Time.realtimeSinceStartupAsDouble < end);
            Assert.IsTrue(encounter.AcceptsPlayerCommand);
        }
        private IEnumerator Capture(string name)
        { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); yield return null; }
        private IEnumerator HoldFrames(float duration)
        {
            double end = Time.realtimeSinceStartupAsDouble + duration;
            do { yield return new WaitForEndOfFrame(); Frame(); } while (Time.realtimeSinceStartupAsDouble < end);
        }
        private void Frame()
        {
            if (frames.Count == 0 || Time.realtimeSinceStartupAsDouble - frames.Last().time >= 1d / 30)
                frames.Add((Time.realtimeSinceStartupAsDouble, ScreenCapture.CaptureScreenshotAsTexture()));
        }
        private void FlushFrames()
        {
            File.WriteAllLines(Path.Combine(output, "captures.csv"), new[] { "file,realtime" }.Concat(frames.Select((f, i) =>
                "frame-" + i.ToString("D4") + ".jpg," + f.time.ToString("F6", CultureInfo.InvariantCulture))));
            for (int i = 0; i < frames.Count; i++)
            { File.WriteAllBytes(Path.Combine(output, "frame-" + i.ToString("D4") + ".jpg"), frames[i].image.EncodeToJPG(95)); Object.Destroy(frames[i].image); }
            frames.Clear();
        }
        private static HexCoord Parse(string text) { int[] qr = text.Split(':').Select(int.Parse).ToArray(); return new HexCoord(qr[0], qr[1]); }
        private static IEnumerator SceneReady(string name)
        { for (int i = 0; i < 300 && SceneManager.GetActiveScene().name != name; i++) yield return null; yield return null; yield return null; Assert.AreEqual(name, SceneManager.GetActiveScene().name); }
        private sealed class WaitPolicy : ICombatActionPolicy
        { public CombatCommand ChooseAction(CombatSession s, IReadOnlyList<CombatCommand> legal) => legal.FirstOrDefault(c => c.Kind == CombatCommandKind.EndTurn) ?? legal.FirstOrDefault(c => c.Kind == CombatCommandKind.Wait); }
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>() ?? throw new Exception(typeof(T).Name + " missing");
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var frame in frames) Object.Destroy(frame.image);
            frames.Clear();
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            if (saves != null && Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }
}
