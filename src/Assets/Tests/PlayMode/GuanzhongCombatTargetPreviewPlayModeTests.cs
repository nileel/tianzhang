using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using TianZhang.Spatial;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed partial class GuanzhongCombatTargetSelectionPlayModeTests
    {
        private const string PreviewSurface = "CombatTargetPreviewSurface";

        [UnityTest]
        public IEnumerator G3SurfaceSweepSelectionAndRefusalAtBothResolutions()
        {
            var input = Find<CombatCommandInput>();
            var bar = Find<CombatActionBarView>();
            var popup = Find<CombatActionPreviewView>();
            var originals = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .ToDictionary(renderer => renderer, renderer => renderer.sharedMaterials);
            int ap = spawned.Player.ActionPointsRemainingUnits, mp = spawned.Player.CurrentSpirit;
            int tick = session.TurnScheduler.CurrentTick;
            foreach (int width in new[] { 1920, 1366 })
            {
                GuanzhongCombatActionPreviewPlayModeTests.SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.ThirdArtButton.transform.position));
                AssertG3Targets();
                yield return CaptureG3("open-" + width);
                foreach (var enemy in spawned.Enemies)
                foreach (HexCoord cell in enemy.OccupiedCells)
                foreach (bool body in new[] { false, true })
                {
                    Assert.IsTrue(TryFindCastingPoint(cell, out var point, body));
                    yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(point);
                    Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
                    Assert.AreEqual(new CombatUnitPresentationHex(cell.Q, cell.R), input.Candidate.TargetCell);
                    AssertAffectedTarget(input, enemy.Id);
                    AssertG3Targets(enemy.Id);
                    AssertSpellHighlights(session);
                    CollectionAssert.AreEqual(new[] { input.Candidate.TargetCell.Value },
                        input.Preview.AttackArea.Cells.Where(value => value.IsLegal).Select(value => value.Position));
                    yield return CaptureG3(enemy.Id + "-" + cell.Q + "-" + cell.R + (body ? "-body-" : "-ground-") + width);
                }
                yield return RecordG3Sweep("sweep-" + width);
                Assert.IsFalse(provider.IsPresenting);
                Assert.IsFalse(encounter.IsPresenting);

                HexCoord empty = EmptyCell("spell_feixingzhi");
                Assert.IsTrue(TryFindCastingPoint(empty, out var emptyPoint));
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(emptyPoint);
                Assert.IsTrue(input.Preview.CanConfirm);
                AssertG3Targets();
                yield return CaptureG3("empty-" + width);
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(new Vector2(-50, -50));
                AssertG3Targets();
                Assert.IsFalse(input.Preview.CanConfirm);
                yield return PointArt(session, spawned.Enemy.Id);
                AssertG3Targets(spawned.Enemy.Id);
                yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.SecondArtButton.transform.position));
                AssertG3Targets();
                Assert.AreEqual("spell_julingshu", input.Candidate.ProfileId);
                yield return PointArt(session, "player");
                AssertG3Targets("player");
                yield return CaptureG3("self-recovery-" + width);
                yield return Click(RectTransformUtility.WorldToScreenPoint(null, popup.CancelButton.transform.position));
                AssertG3Targets();

                yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.ThirdArtButton.transform.position));
                yield return PointArt(session, spawned.Enemy.Id);
                AssertG3Targets(spawned.Enemy.Id);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = mouse.position.ReadValue() }.WithButton(MouseButton.Right));
                yield return null; yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = mouse.position.ReadValue() });
                yield return null;
                Assert.IsNull(input.Candidate);
                AssertG3Targets();

                spawned.Player.SetCooldown("spell_feixingzhi", 60);
                RefreshAuthority();
                yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.ThirdArtButton.transform.position));
                yield return PointArt(session, spawned.Enemy.Id);
                Assert.IsFalse(input.Preview.CanConfirm);
                Assert.Greater(input.Preview.RemainingCooldownTicks, 0);
                AssertG3Targets();
                yield return CaptureG3("cooldown-" + width);
                spawned.Player.SetCooldown("spell_feixingzhi", 0);
                RefreshAuthority();
                AssertG3Targets();
                foreach (var pair in originals)
                    if (pair.Key != null) CollectionAssert.AreEqual(pair.Value, pair.Key.sharedMaterials, pair.Key.name);
                Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
                Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
                Assert.AreEqual(tick, session.TurnScheduler.CurrentTick);
            }
            spawned.Player.ApplyStatus(CombatStatusKind.SealSpirit, 2);
            RefreshAuthority();
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.ThirdArtButton.transform.position));
            yield return PointArt(session, spawned.Enemy.Id);
            Assert.IsFalse(input.Preview.CanConfirm);
            AssertG3Targets();
            yield return CaptureG3("sealed");
            input.CancelCandidate();
            AssertG3Targets();
        }

        [UnityTest] public IEnumerator G3MultiTargetCommitDeathAndClear1920() => G3MultiTargetLifecycle(1920);
        [UnityTest] public IEnumerator G3MultiTargetCommitDeathAndClear1366() => G3MultiTargetLifecycle(1366);

        private IEnumerator G3MultiTargetLifecycle(int width)
        {
            GuanzhongCombatActionPreviewPlayModeTests.SetGameSize(width, width == 1920 ? 1080 : 768);
            for (int frame = 0; frame < 8; frame++) yield return null;
            // Isolated placement: keep the production board, units, profiles and input/quote chain.
            // Current AI deliberately separates its melee/ranged units, so it cannot supply this AOE fixture.
            var board = Get<SpatialQueryBoard>(session.SpatialQuery, "board");
            bool placed = false;
            HexCoord aim = default;
            foreach (HexCoord origin in board.Cells.OrderBy(cell => Mathf.Abs(cell.Q - 3) + Mathf.Abs(cell.R - 3)))
            {
                if (placed) break;
                for (int direction = 0; direction < 6 && !placed; direction++)
                for (int facing = 0; facing < 6 && !placed; facing++)
                {
                    spawned.Player.SetPosition(origin);
                    spawned.Enemies[0].SetPosition(origin.Step(direction));
                    spawned.Enemies[1].SetPosition(origin.Step(direction).Step(direction));
                    foreach (var enemy in spawned.Enemies) enemy.Facing = facing;
                    var occupied = new HashSet<HexCoord>();
                    if (session.Combatants.All.Any(unit => board.InspectPlacement(unit.Pose) != SpatialQueryReasons.Ok ||
                        unit.OccupiedCells.Any(cell => !occupied.Add(cell)))) continue;
                    aim = spawned.Enemies[0].Position;
                    var quote = new CombatCommandService().Preview(session, new CombatCommand(CombatCommandKind.BasicAttack,
                        "player", profileId: spawned.Player.BasicAttackProfileId, attackDirection: direction, targetCell: aim));
                    placed = quote.IsLegal && quote.AttackArea.TargetIds.Count == 2;
                }
            }
            Assert.IsTrue(placed, "Need a legal non-overlapping two-unit production AOE.");
            var args = new object[] { spawned, null, null };
            Assert.IsTrue((bool)typeof(EncounterCoordinator).GetMethod("TryCreatePresentationDescriptors",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(encounter, args));
            provider.Prepare((IReadOnlyList<CombatUnitPresentationDescriptor>)args[1]);
            RefreshAuthority();
            yield return null;
            var input = Find<CombatCommandInput>();
            var bar = Find<CombatActionBarView>();
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.BasicAttackButton.transform.position));
            Assert.IsTrue(TryFindCastingPoint(aim, out var point));
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(point);
            Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
            AssertG3Targets(spawned.Enemies.Select(enemy => enemy.Id).ToArray());
            AssertBasicHighlights(input, Find<CombatMovementView>());
            yield return CaptureG3("multi-target-" + width);
            yield return RecordG3Sweep("multi-sweep-" + width);
            int ap = spawned.Player.ActionPointsRemainingUnits;
            int cost = input.Preview.ActionPointCostUnits;
            yield return GuanzhongBasicAttackPlayModeTests.ConfirmPointedAttack();
            AssertG3Targets();
            Assert.IsNull(input.Candidate);
            Assert.AreEqual(ap - cost, spawned.Player.ActionPointsRemainingUnits);
            yield return CaptureG3("submitted-" + width);
            yield return CommandsReady();

            yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.ThirdArtButton.transform.position));
            yield return PointArt(session, spawned.Enemy.Id);
            AssertG3Targets(spawned.Enemy.Id);
            var dead = spawned.Enemy;
            var position = new CombatUnitPresentationHex(dead.Position.Q, dead.Position.R);
            var staleTargets = input.Preview.AttackArea.Targets;
            dead.ReceiveDamage(dead.CurrentHealth);
            provider.Present(new CombatUnitPresentationEventProjection(dead.Id, CombatUnitPresentationEvent.Death,
                position, position, dead.Facing, null));
            AssertG3Targets();
            provider.PresentTargetPreview(staleTargets);
            AssertG3Targets();
            RefreshAuthority();
            Assert.IsNull(input.Candidate);
            yield return CaptureG3("death-refresh-" + width);
            yield return CommandsReady();
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, bar.ThirdArtButton.transform.position));
            yield return PointArt(session, spawned.Enemies[1].Id);
            AssertG3Targets(spawned.Enemies[1].Id);
            // Exercise exit's Hide/Clear hooks; actual scene navigation is verified separately.
            presenter.Hide();
            AssertG3Targets();
            Assert.IsNull(input.Candidate);
            provider.Clear();
            Assert.AreEqual(0, provider.ActiveCombatantCount);
            yield return null;
            Assert.IsFalse(UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Any(renderer => renderer.name == PreviewSurface));
        }

        private void AssertG3Targets(params string[] expected)
        {
            var visible = new List<string>();
            foreach (var unit in session.Combatants.All)
            {
                if (!provider.TryGetPresentedUnit(unit.Id, out var root)) continue;
                var surfaces = root.GetComponentsInChildren<MeshRenderer>(true).Where(renderer => renderer.name == PreviewSurface).ToArray();
                if (surfaces.Any(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)) visible.Add(unit.Id);
                foreach (var surface in surfaces)
                {
                    Assert.AreEqual("TianZhang/CombatTargetPreview", surface.sharedMaterial.shader.name);
                    Assert.IsTrue(surface.sharedMaterial.shader.isSupported);
                    Assert.IsNull(surface.GetComponent<Collider>());
                    Assert.That(surface.sharedMaterial.GetColor("_Color").a, Is.LessThanOrEqualTo(.3f));
                    MeshFilter original = surface.transform.parent.GetComponent<MeshFilter>();
                    Assert.AreSame(original.sharedMesh, surface.GetComponent<MeshFilter>().sharedMesh);
                    Assert.AreEqual(original.transform.localToWorldMatrix, surface.transform.localToWorldMatrix);
                }
            }
            CollectionAssert.AreEquivalent(expected, visible, "Only the current confirmable quote's affected units may sweep.");
        }

        private IEnumerator RecordG3Sweep(string label)
        {
#if UNITY_EDITOR
            while (UnityEditor.ShaderUtil.anythingCompiling) yield return null;
#endif
            string directory = Path.Combine(Application.dataPath, "../Logs/g3-target-preview", label);
            Directory.CreateDirectory(directory);
            var evidence = new List<string>();
            for (int frame = 0; frame < 40; frame++)
            {
                Assert.IsFalse(provider.IsPresenting);
                Assert.IsFalse(encounter.IsPresenting);
                evidence.Add(Time.unscaledTime.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "\t" + G3State());
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, frame.ToString("D3") + ".png"));
                yield return new WaitForSecondsRealtime(.1f);
            }
            File.WriteAllLines(Path.Combine(directory, "frames.tsv"), evidence);
        }

        private IEnumerator CaptureG3(string label)
        {
            string directory = Path.Combine(Application.dataPath, "../Logs/g3-target-preview");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, label + ".txt"), G3State());
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, label + ".png"));
            yield return null; yield return null;
        }

        private string G3State()
        {
            var input = Find<CombatCommandInput>();
            var movement = Find<CombatMovementView>();
            var sources = new List<string>();
            foreach (var unit in session.Combatants.All)
            {
                if (!provider.TryGetPresentedUnit(unit.Id, out var root)) continue;
                provider.TryGetVisibleBounds(unit.Id, out Bounds bounds);
                foreach (var surface in root.GetComponentsInChildren<MeshRenderer>(true).Where(renderer => renderer.name == PreviewSurface))
                {
                    var block = new MaterialPropertyBlock(); surface.GetPropertyBlock(block);
                    sources.Add(unit.Id + ":visible=" + (surface.enabled && surface.gameObject.activeInHierarchy) +
                        ":bounds=" + bounds.min.ToString("F4") + "/" + bounds.max.ToString("F4") +
                        ":sweep=" + block.GetVector("_Sweep").ToString("F4") + ":source=" + surface.transform.parent.name +
                        ":originalMaterials=" + string.Join(",", surface.transform.parent.GetComponent<Renderer>().sharedMaterials.Select(m => m.name)));
                }
            }
            return "resolution=" + Screen.width + "x" + Screen.height + "; mouse=" + Mouse.current.position.ReadValue() +
                "; cell=" + input.Candidate?.TargetCell?.Q + "," + input.Candidate?.TargetCell?.R +
                "; canConfirm=" + input.Preview?.CanConfirm + "; affected=" +
                string.Join("/", input.Preview?.AttackArea?.Targets.Select(unit => unit.Id) ?? Array.Empty<string>()) +
                "; effects=" + string.Join("/", input.Preview?.AttackArea?.Cells.Where(cell => cell.IsLegal).Select(cell => cell.Position.Q + "," + cell.Position.R) ?? Array.Empty<string>()) +
                "; providerBusy=" + provider.IsPresenting + "; encounterBusy=" + encounter.IsPresenting +
                "; AP=" + spawned.Player.ActionPointsRemainingUnits + "; MP=" + spawned.Player.CurrentSpirit +
                "; surfaces=" + string.Join("/", sources) + "; borders=" +
                string.Join("/", movement.Highlights.Where(pair => pair.Value.gameObject.activeSelf).Select(pair =>
                    pair.Key + ":width=" + pair.Value.widthMultiplier + ":vertices=" + string.Join("|",
                        Enumerable.Range(0, 6).Select(index => pair.Value.GetPosition(index).ToString("F4")))));
        }
    }
}
