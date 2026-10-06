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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongUnitHealthBarPlayModeTests
    {
        private readonly List<GameObject> ownedObjects = new List<GameObject>();
        private string saveDirectory;
        private bool enteredFormalScenes;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject value in ownedObjects)
                if (value != null) Object.Destroy(value);
            ownedObjects.Clear();
            if (enteredFormalScenes)
            {
                SceneManager.LoadScene(GameplaySceneNames.StartMenu);
                yield return WaitForScene(GameplaySceneNames.StartMenu);
                GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
                if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
                enteredFormalScenes = false;
            }
            yield return null;
            if (saveDirectory != null && Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
            saveDirectory = null;
        }

        [UnityTest]
        public IEnumerator StableIdsKeepDamageHealingAndHealthFillWhenSnapshotOrderChanges()
        {
            Fixture fixture = CreateFixture();
            CombatHudPresenter presenter = Own("HealthPresenter").AddComponent<CombatHudPresenter>();
            presenter.ConfigureHealthBars(fixture.View);
            presenter.Present(Snapshot(Unit("player", "修士", 100, 100, 40, 60), Unit("enemy", "石甲猴", 80, 80, 20, 30)));
            yield return null;
            AssertResources(FindBar(fixture.Bars, "player"), 100, 100);
            AssertResources(FindBar(fixture.Bars, "enemy"), 80, 80);
            Color healthyColor = FindBar(fixture.Bars, "player").healthFill.color;

            presenter.Present(Snapshot(Unit("player", "修士", 15, 100, 12, 60), Unit("enemy", "石甲猴", 80, 80, 20, 30)));
            yield return null;
            AssertResources(FindBar(fixture.Bars, "player"), 15, 100);
            AssertResources(FindBar(fixture.Bars, "enemy"), 80, 80);
            Assert.AreNotEqual(healthyColor, FindBar(fixture.Bars, "player").healthFill.color,
                "Low health needs a visible color distinction as well as its numeric value and fill.");

            // Swap DTO slots: position must still follow the stable ID, rather than the player/enemy slot.
            presenter.Present(Snapshot(Unit("enemy", "石甲猴", 35, 80, 7, 30), Unit("player", "修士", 65, 100, 12, 60)));
            yield return null;
            AssertResources(FindBar(fixture.Bars, "player"), 65, 100);
            AssertResources(FindBar(fixture.Bars, "enemy"), 35, 80);
            Assert.AreEqual(healthyColor, FindBar(fixture.Bars, "player").healthFill.color);
            presenter.Present(Snapshot(Unit("enemy", "石甲猴", 55, 80, 7, 30), Unit("player", "修士", 65, 100, 12, 60)));
            yield return null;
            AssertResources(FindBar(fixture.Bars, "enemy"), 55, 80);
            AssertResources(FindBar(fixture.Bars, "player"), 65, 100);
            AssertProjection(fixture, "player", FindBar(fixture.Bars, "player"));
            AssertProjection(fixture, "enemy", FindBar(fixture.Bars, "enemy"));
            foreach (CombatUnitHealthBarView.Bar bar in fixture.Bars)
            {
                Assert.IsEmpty(bar.root.GetComponentsInChildren<Text>(true),
                    bar.root.name + " must stay a text-free slim bar.");
                Assert.IsEmpty(bar.root.GetComponentsInChildren<LayoutGroup>(true),
                    bar.root.name + " must not keep the text-driven layout group.");
                Assert.IsEmpty(bar.root.GetComponentsInChildren<ContentSizeFitter>(true),
                    bar.root.name + " must not stretch with its content.");
                Assert.IsNull(bar.root.GetComponent<Image>(), bar.root.name + " must not keep the old text panel background.");
                Assert.AreEqual(new Vector2(CombatUnitHealthBarView.BarWidth, CombatUnitHealthBarView.BarHeight),
                    bar.root.sizeDelta, bar.root.name + " must keep the fixed slim bar size.");
            }
            foreach (Graphic graphic in fixture.Canvas.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(graphic.raycastTarget, graphic.name + " must not take world input.");
            CombatUnitHealthBarView.Bar invalidBar = FindBar(fixture.Bars, "player");
            LogAssert.Expect(LogType.Error, "combat_health_bar_input_invalid:player");
            presenter.Present(Snapshot(Unit("player", "修士", 1, 0, 12, 60), Unit("enemy", "石甲猴", 55, 80, 7, 30)));
            Assert.IsFalse(invalidBar.root.gameObject.activeSelf);
            Assert.Zero(invalidBar.healthFill.fillAmount, "Invalid maxima cannot masquerade as full health.");
            AssertResources(FindBar(fixture.Bars, "enemy"), 55, 80);
            presenter.Hide();
            AssertAllHidden(fixture.Bars);
        }

        [UnityTest]
        public IEnumerator HeadAnchorUsesActualBodyBoundsThroughSixFacingsMovementActionScaleAndCameraChanges()
        {
            Fixture fixture = CreateFixture();
            fixture.View.Present(DefaultSnapshot());
            yield return null;
            foreach (string id in new[] { "player", "enemy" })
            {
                Assert.IsTrue(fixture.Provider.TryGetPresentedUnit(id, out GameObject root));
                Assert.AreEqual(Vector3.one * 1.4f, root.transform.GetChild(0).localScale);
                // Stay one hex from the prepared origin so every facing keeps the unit inside the validation camera.
                CombatUnitPresentationHex origin = id == "player"
                    ? new CombatUnitPresentationHex(0, 0) : new CombatUnitPresentationHex(1, 0);
                for (int facing = 0; facing < 6; facing++)
                {
                    CombatUnitPresentationHex away = Neighbour(origin, facing);
                    Project(fixture.Provider, id, CombatUnitPresentationEvent.Move, facing, origin, away, away);
                    yield return WaitForPresentation(fixture.Provider);
                    Refresh(fixture.View);
                    AssertProjection(fixture, id, FindBar(fixture.Bars, id));
                    Project(fixture.Provider, id, CombatUnitPresentationEvent.Move, (facing + 3) % 6, away, origin, origin);
                    yield return WaitForPresentation(fixture.Provider);
                    Refresh(fixture.View);
                    AssertProjection(fixture, id, FindBar(fixture.Bars, id));
                }
                foreach (CombatUnitPresentationEvent action in new[] { CombatUnitPresentationEvent.Attack, CombatUnitPresentationEvent.Hit })
                {
                    Project(fixture.Provider, id, action, 2, new CombatUnitPresentationHex(1, -1), new CombatUnitPresentationHex(1, -1));
                    Vector3 expectedScale = action == CombatUnitPresentationEvent.Attack
                        ? new Vector3(1.08f, 0.94f, 1.08f) : new Vector3(0.94f, 1.06f, 0.94f);
                    Assert.AreEqual(expectedScale, root.transform.localScale);
                    Refresh(fixture.View);
                    AssertProjection(fixture, id, FindBar(fixture.Bars, id));
                }
                yield return new WaitForSeconds(0.6f);
                Assert.AreEqual(Vector3.one, root.transform.localScale);
                Refresh(fixture.View);
                AssertProjection(fixture, id, FindBar(fixture.Bars, id));
            }

            foreach (float size in new[] { 4f, 8f })
            {
                fixture.Camera.orthographicSize = size;
                fixture.Camera.transform.position += Vector3.right * 0.25f;
                Refresh(fixture.View);
                AssertProjection(fixture, "player", FindBar(fixture.Bars, "player"));
            }
            AssertResources(FindBar(fixture.Bars, "player"), 100, 100);
        }

        [UnityTest]
        public IEnumerator VisibilityAndLifecycleHideInvalidAnchorsWithoutTreatingGeometryAsOcclusion()
        {
            Fixture fixture = CreateFixture();
            fixture.View.Present(DefaultSnapshot());
            yield return null;
            CombatUnitHealthBarView.Bar playerBar = FindBar(fixture.Bars, "player");
            Assert.IsTrue(fixture.Provider.TryGetPresentedUnit("player", out GameObject playerRoot));
            Vector3 original = playerRoot.transform.position;
            playerRoot.transform.position += Vector3.right * 100f;
            Refresh(fixture.View);
            Assert.IsFalse(playerBar.root.gameObject.activeSelf);
            playerRoot.transform.position = fixture.Camera.transform.position - fixture.Camera.transform.forward * 5f;
            Refresh(fixture.View);
            Assert.IsFalse(playerBar.root.gameObject.activeSelf);
            playerRoot.transform.position = original;
            Assert.IsTrue(fixture.Provider.TryGetHeadAnchor("player", out Vector3 anchor));
            GameObject occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ownedObjects.Add(occluder);
            occluder.transform.position = Vector3.Lerp(fixture.Camera.transform.position, anchor, 0.5f);
            occluder.transform.localScale = Vector3.one * 2f;
            Physics.SyncTransforms();
            Assert.IsTrue(Physics.Linecast(fixture.Camera.transform.position, anchor, out RaycastHit hit));
            Assert.AreSame(occluder.GetComponent<Collider>(), hit.collider);
            Refresh(fixture.View);
            Assert.IsTrue(playerBar.root.gameObject.activeSelf, "Overlay health bars remain visible behind scene geometry.");
            AssertProjection(fixture, "player", playerBar);

            playerRoot.SetActive(false);
            Refresh(fixture.View);
            Assert.IsFalse(playerBar.root.gameObject.activeSelf);
            playerRoot.SetActive(true);
            fixture.View.Present(Snapshot(Unit("player", "修士", 0, 100, 40, 60), Unit("enemy", "石甲猴", 80, 80, 20, 30)));
            Refresh(fixture.View);
            Assert.IsFalse(FindBar(fixture.Bars, "player").root.gameObject.activeSelf);
            fixture.Provider.Remove("enemy");
            Refresh(fixture.View);
            AssertAllHidden(fixture.Bars);

            fixture.View.Hide();
            fixture.Provider.Prepare(Descriptors());
            fixture.View.Present(DefaultSnapshot());
            yield return null;
            AssertProjection(fixture, "player", FindBar(fixture.Bars, "player"));
            AssertProjection(fixture, "enemy", FindBar(fixture.Bars, "enemy"));
            fixture.View.enabled = false;
            AssertAllHidden(fixture.Bars);
            fixture.View.enabled = true;
            yield return null;
            AssertAllHidden(fixture.Bars);
            fixture.View.Present(DefaultSnapshot());
            yield return null;
            Assert.IsTrue(FindBar(fixture.Bars, "player").root.gameObject.activeSelf);
            fixture.Provider.gameObject.SetActive(false);
            yield return null;
            AssertAllHidden(fixture.Bars);
        }

        [UnityTest]
        public IEnumerator FormalStartMenuRouteInjectsHealthBarsAndFeedsRealCombatResources()
        {
            enteredFormalScenes = true;
            GameBootstrap stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return WaitForScene(GameplaySceneNames.StartMenu);
            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-HP-" + Guid.NewGuid().ToString("N"));
            SetField(bootstrap, "slotStore", new GameSaveSlotStore(saveDirectory));
            CharacterCreationController creation = FindInScene<CharacterCreationController>();
            Assert.IsNotNull(creation);
            creation.Open();
            creation.Draft.OriginId = "origin_minor_clan";
            creation.Submit("hp-wiring", "血条接线验证");
            yield return WaitForScene(GameplaySceneNames.World);
            WorldMapController world = FindInScene<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return WaitForScene(GameplaySceneNames.Settlement);
            SettlementController settlement = FindInScene<SettlementController>();
            BountyBoardView board = FindInScene<BountyBoardView>();
            board.Show(GetField<TianZhang.Content.ContentCatalogData>(settlement, "contentCatalog"),
                "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
            board.SubmitAccept("bounty_guanzhong_shijiahou");
            Assert.IsTrue(settlement.EnterAdventure("guanzhong_wild"));
            yield return WaitForScene(GameplaySceneNames.Adventure);
            AdventureController adventure = FindInScene<AdventureController>();
            for (int frame = 0; frame < 120 && adventure.Session == null; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            adventure.SetEncounterRandomSource(new FixedEncounterRandom());
            EncounterCoordinator encounter = FindInScene<EncounterCoordinator>();
            encounter.SetCombatResolutionRandomSource(new FixedCombatRandom());
            Assert.IsTrue(FindInScene<AdventureInputController>().SelectNode("shijiahou_encounter"));
            yield return null;
            CombatUnitHealthBarView view = FindInScene<CombatUnitHealthBarView>();
            Assert.IsNotNull(view, "Formal AdventureScene must carry the injected health view.");
            CombatUnitHealthBarView.Bar[] bars = GetField<CombatUnitHealthBarView.Bar[]>(view, "bars");
            Assert.AreEqual(2, bars.Length);
            Canvas.ForceUpdateCanvases();
            foreach (CombatUnitHealthBarView.Bar bar in bars) AssertSlimTextFreeBar(bar);
            CombatSession session = GetField<CombatSession>(encounter, "session");
            AdventureSpawnSet spawned = GetField<AdventureSpawnSet>(encounter, "spawned");
            Assert.IsTrue(session.Combatants.TryGet(spawned.Player.Id, out CombatantSnapshot player));
            Assert.IsTrue(session.Combatants.TryGet(spawned.Enemy.Id, out CombatantSnapshot enemy));
            CombatUnitHealthBarView.Bar playerBar = FindBar(bars, player.Id);
            CombatUnitHealthBarView.Bar enemyBar = FindBar(bars, enemy.Id);
            string enemyName = UiText.Resolve(spawned.EnemyData.displayNameKey);
            Assert.AreNotEqual("血条接线验证", enemyName,
                "Name-independent bar width needs two different committed names.");
            AssertResources(playerBar, player.CurrentHealth, player.MaximumHealth);
            AssertResources(enemyBar, enemy.CurrentHealth, enemy.MaximumHealth);
            CombatHudPresenter hud = FindInScene<CombatHudPresenter>();
            CombatTargetView targetView = FindInScene<CombatTargetView>();
            Canvas hudCanvas = FindInScene<Canvas>();
            foreach (int width in new[] { 1920, 1366 })
            {
                int height = width == 1920 ? 1080 : 768;
                SetGameSize(width, height);
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.AreEqual(width, Screen.width, "The formal combat frame must keep the requested width.");
                Rect playerRect = AssertSlimBarOnScreen(playerBar, player.Id);
                Rect enemyRect = AssertSlimBarOnScreen(enemyBar, enemy.Id);
                Assert.AreEqual(playerRect.width, enemyRect.width, 0.01f, "Bar width must not depend on the unit name.");
                Assert.AreEqual(playerRect.height, enemyRect.height, 0.01f, "Bar height must not depend on the unit name.");
                for (int inspect = 0; inspect < 60 && targetView.TargetId != enemy.Id; inspect++)
                {
                    hud.InspectTarget(enemy.Id);
                    yield return null;
                }
                Assert.AreEqual(enemy.Id, targetView.TargetId, "The existing target panel must still present details.");
                StringAssert.Contains(enemyName, targetView.DetailsText);
                StringAssert.Contains("生命 " + enemy.CurrentHealth + "/" + enemy.MaximumHealth, targetView.DetailsText);
                StringAssert.Contains("灵力 " + enemy.CurrentSpirit + "/" + enemy.MaximumSpirit, targetView.DetailsText);
                yield return CaptureOverlayFrame(width, height);
                RectTransform canvasRect = (RectTransform)hudCanvas.transform;
                TestContext.WriteLine($"Formal slim bar {width}x{height}: screen={Screen.width}x{Screen.height}, " +
                    $"canvasScale={hudCanvas.scaleFactor}, canvasRect={RectText(canvasRect.rect)}, " +
                    $"playerRect={RectText(playerRect)}, enemyRect={RectText(enemyRect)}, slots={bars.Length}");
            }
            int playerBefore = player.CurrentHealth;
            int enemyBefore = enemy.CurrentHealth;
            for (int frame = 0; frame < 180 && player.CurrentHealth == playerBefore && enemy.CurrentHealth == enemyBefore; frame++)
            {
                ((ICombatCommandHandler)encounter).RequestBasicAttack(player.Id, enemy.Id);
                yield return null;
            }
            Assert.IsTrue(player.CurrentHealth < playerBefore || enemy.CurrentHealth < enemyBefore,
                "Formal combat must produce real damage before the resource refresh assertion.");
            yield return null;
            AssertResources(playerBar, player.CurrentHealth, player.MaximumHealth);
            AssertResources(enemyBar, enemy.CurrentHealth, enemy.MaximumHealth);
            TestContext.WriteLine($"Formal HP refresh: player {playerBefore}->{player.CurrentHealth}, enemy {enemyBefore}->{enemy.CurrentHealth}");
        }

        private Fixture CreateFixture()
        {
            var fixture = new Fixture();
            GameObject providerHost = Own("HealthProvider");
            fixture.Provider = providerHost.AddComponent<Static3DCombatUnitPresentationProvider>();
            Type database = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
            Assert.IsNotNull(database, "Approved-profile PlayMode validation needs the editor asset database.");
            MethodInfo loadAsset = database.GetMethod("LoadAssetAtPath", new[] { typeof(string), typeof(Type) });
            Assert.IsNotNull(loadAsset);
            SetField(fixture.Provider, "profileSet", loadAsset.Invoke(null, new object[]
            {
                "Assets/Data/CombatPresentationProfiles/Static3DCombatUnitPresentationProfileSet.asset",
                typeof(Static3DCombatUnitPresentationProfileSet),
            }));
            Assert.IsTrue(fixture.Provider.TryValidate(out string reason), reason);
            fixture.Provider.Prepare(Descriptors());
            fixture.Camera = Own("HealthCamera").AddComponent<Camera>();
            fixture.Camera.orthographic = true;
            fixture.Camera.orthographicSize = 6f;
            fixture.Camera.transform.position = new Vector3(0f, 8f, -10f);
            fixture.Camera.transform.LookAt(new Vector3(0f, 0.5f, 1f));
            fixture.Camera.enabled = false;
            GameObject canvasHost = Own("HealthCanvas", typeof(RectTransform), typeof(Canvas));
            fixture.Canvas = canvasHost.GetComponent<Canvas>();
            fixture.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            GameObject viewHost = new GameObject("HealthBars", typeof(RectTransform));
            viewHost.transform.SetParent(canvasHost.transform, false);
            RectTransform container = viewHost.GetComponent<RectTransform>();
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.sizeDelta = Vector2.zero;
            fixture.View = viewHost.AddComponent<CombatUnitHealthBarView>();
            fixture.Bars = new[] { CreateBar(container, "Bar0"), CreateBar(container, "Bar1") };
            SetField(fixture.View, "bars", fixture.Bars);
            fixture.View.Configure(fixture.Camera, fixture.Canvas, fixture.Provider);
            Canvas.ForceUpdateCanvases();
            return fixture;
        }

        private static CombatUnitHealthBarView.Bar CreateBar(Transform parent, string name)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            RectTransform root = host.GetComponent<RectTransform>();
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(CombatUnitHealthBarView.BarWidth, CombatUnitHealthBarView.BarHeight);
            var track = new GameObject("HealthTrack", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(root, false);
            var fill = new GameObject("HealthFill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            fill.GetComponent<Image>().type = Image.Type.Filled;
            return new CombatUnitHealthBarView.Bar
            {
                root = root,
                healthFill = fill.GetComponent<Image>(),
            };
        }

        private static void AssertProjection(Fixture fixture, string id, CombatUnitHealthBarView.Bar bar)
        {
            Assert.IsTrue(fixture.Provider.TryGetPresentedUnit(id, out GameObject root));
            Renderer[] renderers = root.transform.GetChild(0).GetComponentsInChildren<Renderer>();
            Assert.IsNotEmpty(renderers);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            Vector3 expected = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            Assert.IsTrue(fixture.Provider.TryGetHeadAnchor(id, out Vector3 anchor));
            Assert.Less(Vector3.Distance(expected, anchor), 0.0001f, "Anchor must come from actual body bounds.");
            Vector3 screen = fixture.Camera.WorldToScreenPoint(anchor);
            Assert.IsTrue(bar.root.gameObject.activeSelf);
            Assert.IsTrue(RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)bar.root.parent, screen, null, out Vector2 canvasPoint));
            Vector2 bottom = RectTransformUtility.WorldToScreenPoint(null,
                bar.root.TransformPoint(new Vector3(bar.root.rect.center.x, bar.root.rect.yMin, 0f)));
            Assert.Less(Vector2.Distance(screen, bottom), 0.5f, "Bar bottom center must meet the projected head.");
            TestContext.WriteLine($"{id}: body={root.transform.GetChild(0).localScale}, root={root.transform.localScale}, bounds={bounds}, head={anchor}, screen={screen}, canvas={canvasPoint}, rectBottom={bottom}");
        }

        private static CombatUnitHealthBarView.Bar FindBar(CombatUnitHealthBarView.Bar[] bars, string id) =>
            bars.Single(bar => string.Equals(GetField<string>(bar, "Id"), id, StringComparison.Ordinal));

        private static void AssertSlimTextFreeBar(CombatUnitHealthBarView.Bar bar)
        {
            Assert.IsNotNull(bar.root, "Health bar slot needs its root.");
            Assert.IsEmpty(bar.root.GetComponentsInChildren<Text>(true), bar.root.name + " must carry no head text.");
            Assert.IsEmpty(bar.root.GetComponentsInChildren<LayoutGroup>(true),
                bar.root.name + " must not keep the text-driven layout group.");
            Assert.IsEmpty(bar.root.GetComponentsInChildren<ContentSizeFitter>(true),
                bar.root.name + " must not stretch with its content.");
            Assert.IsNull(bar.root.GetComponent<Image>(), bar.root.name + " must not keep the old text panel background.");
            Assert.AreEqual(new Vector2(CombatUnitHealthBarView.BarWidth, CombatUnitHealthBarView.BarHeight),
                bar.root.sizeDelta, bar.root.name + " must keep the fixed slim bar size.");
            Assert.IsNotNull(bar.healthFill, bar.root.name + " must keep its health fill.");
            Assert.IsFalse(bar.healthFill.raycastTarget, bar.root.name + " must not take world input.");
        }

        private static Rect AssertSlimBarOnScreen(CombatUnitHealthBarView.Bar bar, string id)
        {
            Assert.IsTrue(bar.root.gameObject.activeSelf, id + " bar must stay visible in the formal combat frame.");
            var corners = new Vector3[4];
            bar.root.GetWorldCorners(corners);
            var rect = new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
            Assert.Greater(rect.width, 0f, id + " bar needs a visible width.");
            Assert.Less(rect.height, Screen.height * 0.02f, id + " bar must stay a slim strip, not a text panel.");
            Assert.AreEqual(CombatUnitHealthBarView.BarWidth / CombatUnitHealthBarView.BarHeight, rect.width / rect.height,
                0.01f, id + " bar must keep the fixed thin aspect.");
            return rect;
        }

        private static string RectText(Rect rect) =>
            $"{rect.x:F1},{rect.y:F1},{rect.width:F1}x{rect.height:F1}";

        private static IEnumerator CaptureOverlayFrame(int width, int height)
        {
            Assert.AreNotEqual(GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType,
                "Overlay evidence requires a graphical PlayMode runner, not -nographics.");
            string path = Path.Combine(Application.temporaryCachePath,
                "tzg-slim-healthbar-overlay-" + width + "x" + height + "-" + Guid.NewGuid().ToString("N") + ".png");
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!File.Exists(path))
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "Expected a Game View screenshot containing the Overlay.");
                yield return null;
            }
            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.Greater(new FileInfo(path).Length, 0, "Overlay screenshot must not be empty.");
                Assert.IsTrue(image.LoadImage(File.ReadAllBytes(path)), "Overlay screenshot must be a readable PNG.");
                Assert.AreEqual(width, image.width, "Overlay screenshot width must match the requested Game View.");
                Assert.AreEqual(height, image.height, "Overlay screenshot height must match the requested Game View.");
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
            TestContext.WriteLine("Formal Overlay frame " + width + "x" + height + ": " + path);
        }

        private static void SetGameSize(int width, int height)
        {
#if UNITY_EDITOR
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var sizeType = assembly.GetType("UnityEditor.GameViewSize");
            var kind = assembly.GetType("UnityEditor.GameViewSizeType");
            var size = Activator.CreateInstance(sizeType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { Enum.ToObject(kind, 1), width, height, "Slim health bar validation" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            var game = UnityEditor.EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
            game.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(game, count - 1);
#else
            Screen.SetResolution(width, height, false);
#endif
        }

        private static void AssertResources(CombatUnitHealthBarView.Bar bar, int hp, int maxHp)
        {
            Assert.IsTrue(bar.healthFill.enabled, "The slim bar must keep an enabled health fill.");
            Assert.AreEqual(hp / (float)maxHp, bar.healthFill.fillAmount, 0.0001f);
        }

        private static void AssertAllHidden(CombatUnitHealthBarView.Bar[] bars)
        {
            foreach (CombatUnitHealthBarView.Bar bar in bars) Assert.IsFalse(bar.root.gameObject.activeSelf);
        }

        private static void Refresh(CombatUnitHealthBarView view) => view.SendMessage("LateUpdate");
        private static CombatantHudSnapshot Unit(string id, string name, int hp, int maxHp, int spirit, int maxSpirit) =>
            new CombatantHudSnapshot(id, name, hp, maxHp, spirit, maxSpirit);
        private static CombatHudSnapshot Snapshot(CombatantHudSnapshot player, CombatantHudSnapshot enemy) =>
            new CombatHudSnapshot(player, enemy, "", true, Array.Empty<string>(), Array.Empty<string>());
        private static CombatHudSnapshot DefaultSnapshot() =>
            Snapshot(Unit("player", "修士", 100, 100, 40, 60), Unit("enemy", "石甲猴", 80, 80, 20, 30));
        private static CombatUnitPresentationDescriptor[] Descriptors() => new[]
        {
            new CombatUnitPresentationDescriptor("player", Static3DCombatUnitPresentationProfileSet.PlayerProfileId,
                CombatUnitDisplayFaction.Player, new CombatUnitPresentationHex(0, 0), 0),
            new CombatUnitPresentationDescriptor("enemy", Static3DCombatUnitPresentationProfileSet.ShijiahouProfileId,
                CombatUnitDisplayFaction.Enemy, new CombatUnitPresentationHex(1, 0), 3),
        };
        private static void Project(Static3DCombatUnitPresentationProvider provider, string id,
            CombatUnitPresentationEvent action, int facing, CombatUnitPresentationHex start, CombatUnitPresentationHex end,
            params CombatUnitPresentationHex[] movementPath) =>
            provider.Present(new CombatUnitPresentationEventProjection(id, action, start, end, facing, null, movementPath));

        /// <summary>Neighbour hex the provider maps to the requested six-direction facing.</summary>
        private static CombatUnitPresentationHex Neighbour(CombatUnitPresentationHex from, int facing) => facing switch
        {
            0 => new CombatUnitPresentationHex(from.Q + 1, from.R),
            1 => new CombatUnitPresentationHex(from.Q + 1, from.R - 1),
            2 => new CombatUnitPresentationHex(from.Q, from.R - 1),
            3 => new CombatUnitPresentationHex(from.Q - 1, from.R),
            4 => new CombatUnitPresentationHex(from.Q - 1, from.R + 1),
            5 => new CombatUnitPresentationHex(from.Q, from.R + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(facing)),
        };

        private static IEnumerator WaitForPresentation(Static3DCombatUnitPresentationProvider provider)
        {
            float deadline = Time.time + 10f;
            while (provider.IsPresenting && Time.time < deadline) yield return null;
            Assert.IsFalse(provider.IsPresenting, "The presentation provider must settle before the next projection.");
        }
        private GameObject Own(string name, params Type[] components)
        {
            var value = new GameObject(name, components);
            ownedObjects.Add(value);
            return value;
        }
        private static FieldInfo Field(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            return field;
        }
        private static T GetField<T>(object target, string name) => (T)Field(target, name).GetValue(target);
        private static void SetField(object target, string name, object value) => Field(target, name).SetValue(target, value);
        private static IEnumerator WaitForScene(string name)
        {
            for (int frame = 0; frame < 120; frame++)
            {
                if (SceneManager.GetActiveScene().name == name) { yield return null; yield break; }
                yield return null;
            }
            Assert.Fail("Scene did not load: " + name);
        }
        private static T FindInScene<T>() where T : Component =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(value => value.gameObject.scene == SceneManager.GetActiveScene());
        private sealed class Fixture
        {
            public Camera Camera;
            public Canvas Canvas;
            public Static3DCombatUnitPresentationProvider Provider;
            public CombatUnitHealthBarView View;
            public CombatUnitHealthBarView.Bar[] Bars;
        }
        private sealed class FixedEncounterRandom : IFormalEncounterRandomSource
        {
            public int NextPercent() => 0;
        }
        private sealed class FixedCombatRandom : ICombatResolutionRandomSource
        {
            public float NextPercent() => 0f;
        }
    }
}
