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
        public IEnumerator StableIdsKeepDamageHealingAndSpiritIndependentWhenSnapshotOrderChanges()
        {
            Fixture fixture = CreateFixture();
            CombatHudPresenter presenter = Own("HealthPresenter").AddComponent<CombatHudPresenter>();
            presenter.ConfigureHealthBars(fixture.View);
            presenter.Present(Snapshot(Unit("player", "修士", 100, 100, 40, 60), Unit("enemy", "石甲猴", 80, 80, 20, 30)));
            yield return null;
            AssertResources(FindBar(fixture.Bars, "修士"), 100, 100, 40, 60);
            AssertResources(FindBar(fixture.Bars, "石甲猴"), 80, 80, 20, 30);
            Color healthyColor = FindBar(fixture.Bars, "修士").healthFill.color;

            presenter.Present(Snapshot(Unit("player", "修士", 15, 100, 12, 60), Unit("enemy", "石甲猴", 80, 80, 20, 30)));
            yield return null;
            AssertResources(FindBar(fixture.Bars, "修士"), 15, 100, 12, 60);
            AssertResources(FindBar(fixture.Bars, "石甲猴"), 80, 80, 20, 30);
            Assert.AreNotEqual(healthyColor, FindBar(fixture.Bars, "修士").healthFill.color,
                "Low health needs a visible color distinction as well as its numeric value and fill.");

            // Swap DTO slots: position must still follow the stable ID, rather than the player/enemy slot.
            presenter.Present(Snapshot(Unit("enemy", "石甲猴", 35, 80, 7, 30), Unit("player", "修士", 65, 100, 12, 60)));
            yield return null;
            AssertResources(FindBar(fixture.Bars, "修士"), 65, 100, 12, 60);
            AssertResources(FindBar(fixture.Bars, "石甲猴"), 35, 80, 7, 30);
            Assert.AreEqual(healthyColor, FindBar(fixture.Bars, "修士").healthFill.color);
            presenter.Present(Snapshot(Unit("enemy", "石甲猴", 55, 80, 7, 30), Unit("player", "修士", 65, 100, 12, 60)));
            yield return null;
            AssertResources(FindBar(fixture.Bars, "石甲猴"), 55, 80, 7, 30);
            AssertResources(FindBar(fixture.Bars, "修士"), 65, 100, 12, 60);
            AssertProjection(fixture, "player", FindBar(fixture.Bars, "修士"));
            AssertProjection(fixture, "enemy", FindBar(fixture.Bars, "石甲猴"));
            foreach (Graphic graphic in fixture.Canvas.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(graphic.raycastTarget, graphic.name + " must not take world input.");
            CombatUnitHealthBarView.Bar invalidBar = FindBar(fixture.Bars, "修士");
            LogAssert.Expect(LogType.Error, "combat_health_bar_input_invalid:player");
            presenter.Present(Snapshot(Unit("player", "修士", 1, 0, 12, 60), Unit("enemy", "石甲猴", 55, 80, 7, 30)));
            Assert.IsFalse(invalidBar.root.gameObject.activeSelf);
            Assert.Zero(invalidBar.healthFill.fillAmount, "Invalid maxima cannot masquerade as full health.");
            AssertResources(FindBar(fixture.Bars, "石甲猴"), 55, 80, 7, 30);
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
                for (int facing = 0; facing < 6; facing++)
                {
                    Project(fixture.Provider, id, CombatUnitPresentationEvent.Move, facing, new CombatUnitPresentationHex(0, 0));
                    Refresh(fixture.View);
                    AssertProjection(fixture, id, FindBar(fixture.Bars, id == "player" ? "修士" : "石甲猴"));
                }
                foreach (CombatUnitPresentationEvent action in new[] { CombatUnitPresentationEvent.Attack, CombatUnitPresentationEvent.Hit })
                {
                    Project(fixture.Provider, id, action, 2, new CombatUnitPresentationHex(1, -1));
                    Vector3 expectedScale = action == CombatUnitPresentationEvent.Attack
                        ? new Vector3(1.08f, 0.94f, 1.08f) : new Vector3(0.94f, 1.06f, 0.94f);
                    Assert.AreEqual(expectedScale, root.transform.localScale);
                    Refresh(fixture.View);
                    AssertProjection(fixture, id, FindBar(fixture.Bars, id == "player" ? "修士" : "石甲猴"));
                }
                yield return new WaitForSeconds(0.6f);
                Assert.AreEqual(Vector3.one, root.transform.localScale);
                Refresh(fixture.View);
                AssertProjection(fixture, id, FindBar(fixture.Bars, id == "player" ? "修士" : "石甲猴"));
            }

            foreach (float size in new[] { 4f, 8f })
            {
                fixture.Camera.orthographicSize = size;
                fixture.Camera.transform.position += Vector3.right * 0.25f;
                Refresh(fixture.View);
                AssertProjection(fixture, "player", FindBar(fixture.Bars, "修士"));
            }
            AssertResources(FindBar(fixture.Bars, "修士"), 100, 100, 40, 60);
        }

        [UnityTest]
        public IEnumerator VisibilityAndLifecycleHideInvalidAnchorsWithoutTreatingGeometryAsOcclusion()
        {
            Fixture fixture = CreateFixture();
            fixture.View.Present(DefaultSnapshot());
            yield return null;
            CombatUnitHealthBarView.Bar playerBar = FindBar(fixture.Bars, "修士");
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
            Assert.IsFalse(FindBar(fixture.Bars, "修士").root.gameObject.activeSelf);
            fixture.Provider.Remove("enemy");
            Refresh(fixture.View);
            AssertAllHidden(fixture.Bars);

            fixture.View.Hide();
            fixture.Provider.Prepare(Descriptors());
            fixture.View.Present(DefaultSnapshot());
            yield return null;
            AssertProjection(fixture, "player", FindBar(fixture.Bars, "修士"));
            AssertProjection(fixture, "enemy", FindBar(fixture.Bars, "石甲猴"));
            fixture.View.enabled = false;
            AssertAllHidden(fixture.Bars);
            fixture.View.enabled = true;
            yield return null;
            AssertAllHidden(fixture.Bars);
            fixture.View.Present(DefaultSnapshot());
            yield return null;
            Assert.IsTrue(FindBar(fixture.Bars, "修士").root.gameObject.activeSelf);
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
            Text[] formalTexts = bars.SelectMany(bar => new[] { bar.identityText, bar.healthText, bar.spiritText }).ToArray();
            foreach (Text text in formalTexts) LogTextGeometry(text, "before_canvas_update");
            Canvas.ForceUpdateCanvases();
            foreach (Text text in formalTexts) LogTextGeometry(text, "after_canvas_update");
            foreach (Text text in formalTexts)
                if (!string.IsNullOrEmpty(text.text))
                    Assert.Greater(text.cachedTextGenerator.vertexCount, 0,
                        text.name + " has committed text but no generated visible geometry.");
            CombatSession session = GetField<CombatSession>(encounter, "session");
            AdventureSpawnSet spawned = GetField<AdventureSpawnSet>(encounter, "spawned");
            Assert.IsTrue(session.Combatants.TryGet(spawned.Player.Id, out CombatantSnapshot player));
            Assert.IsTrue(session.Combatants.TryGet(spawned.Enemy.Id, out CombatantSnapshot enemy));
            CombatUnitHealthBarView.Bar playerBar = FindBar(bars, "血条接线验证");
            CombatUnitHealthBarView.Bar enemyBar = FindBar(bars, UiText.Resolve(spawned.EnemyData.displayNameKey));
            AssertResources(playerBar, player.CurrentHealth, player.MaximumHealth, player.CurrentSpirit, player.MaximumSpirit);
            AssertResources(enemyBar, enemy.CurrentHealth, enemy.MaximumHealth, enemy.CurrentSpirit, enemy.MaximumSpirit);
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
            AssertResources(playerBar, player.CurrentHealth, player.MaximumHealth, player.CurrentSpirit, player.MaximumSpirit);
            AssertResources(enemyBar, enemy.CurrentHealth, enemy.MaximumHealth, enemy.CurrentSpirit, enemy.MaximumSpirit);
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
            var host = new GameObject(name, typeof(RectTransform), typeof(Image));
            host.transform.SetParent(parent, false);
            RectTransform root = host.GetComponent<RectTransform>();
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(170f, 72f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(root, false);
            fill.GetComponent<Image>().type = Image.Type.Filled;
            return new CombatUnitHealthBarView.Bar
            {
                root = root,
                identityText = CreateText(root, "Identity"),
                healthText = CreateText(root, "Health"),
                spiritText = CreateText(root, "Spirit"),
                healthFill = fill.GetComponent<Image>(),
            };
        }

        private static Text CreateText(Transform parent, string name)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Text));
            host.transform.SetParent(parent, false);
            return host.GetComponent<Text>();
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

        private static CombatUnitHealthBarView.Bar FindBar(CombatUnitHealthBarView.Bar[] bars, string name) =>
            bars.Single(bar => bar.identityText.text.Contains(name));

        private static void AssertResources(CombatUnitHealthBarView.Bar bar, int hp, int maxHp, int spirit, int maxSpirit)
        {
            StringAssert.Contains(hp + "/" + maxHp, bar.healthText.text);
            StringAssert.Contains(spirit + "/" + maxSpirit, bar.spiritText.text);
            Assert.AreEqual(hp / (float)maxHp, bar.healthFill.fillAmount, 0.0001f);
        }

        private static void LogTextGeometry(Text text, string phase)
        {
            Assert.IsNotNull(text);
            Assert.IsNotNull(text.font, text.name + " needs its formal font.");
            Canvas canvas = text.GetComponentInParent<Canvas>();
            TestContext.WriteLine($"HP text geometry {phase}: name={text.transform.parent.name}/{text.name}, text={text.text}, " +
                $"font={text.font.name}, fontSize={text.font.fontSize}, lineHeight={text.font.lineHeight}, ascent={text.font.ascent}, " +
                $"textFontSize={text.fontSize}, preferredWidth={text.preferredWidth}, preferredHeight={text.preferredHeight}, " +
                $"rectWidth={text.rectTransform.rect.width}, rectHeight={text.rectTransform.rect.height}, " +
                $"canvasScaleFactor={canvas.scaleFactor}, vertices={text.cachedTextGenerator.vertexCount}, " +
                $"active={text.gameObject.activeInHierarchy}, verticalOverflow={text.verticalOverflow}");
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
            CombatUnitPresentationEvent action, int facing, CombatUnitPresentationHex end) =>
            provider.Present(new CombatUnitPresentationEventProjection(id, action, end, end, facing, null));
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
