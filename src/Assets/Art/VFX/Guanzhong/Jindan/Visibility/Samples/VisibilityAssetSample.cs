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

namespace TianZhang.Art.Jindan.Visibility.Samples
{
    // Independent asset sample: no production View/HUD or saved character is replaced.
    public sealed class VisibilityAssetSample : MonoBehaviour
    {
        public CombatBattlefieldSurface surface;
        public Static3DCombatUnitPresentationProvider provider;
        public Camera sampleCamera;
        public GameObject yangAltar, yinAltar;
        public JindanDemoCatalogData catalog;
        public CombatRuleSetData rules;
        public EnvironmentProfileAsset environment;
        public AttackProfileData[] attacks;
        public bool capture;
        public int captureWidth = 1920;
        public bool Complete { get; private set; }
        public string Failure { get; private set; }
        private VisibilitySampleRules demo;
        private readonly List<string> proof = new();
        private GameObject yang, yin;
        private RenderTexture target;
        private Texture2D pixels;
        private string output, evidence, phase;
        private int frame;
        private Vector3[] initialCenters;

        private IEnumerator Start()
        {
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d12-visibility/" + captureWidth));
            evidence = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/visibility/evidence/" + captureWidth));
            if (capture)
            {
                Directory.CreateDirectory(output); Directory.CreateDirectory(evidence);
                Time.captureDeltaTime = 1f / 60;
                target = new RenderTexture(captureWidth, captureWidth == 1920 ? 1080 : 768, 24);
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                sampleCamera.targetTexture = target;
            }
            initialCenters = surface.Cells.Select(c => Point(new HexCoord(c.q,c.r))).ToArray();
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
            {
                object next = null; bool moved = false;
                try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
                catch (Exception exception)
                {
                    Failure = exception.ToString();
                    if (capture) File.WriteAllLines(Path.Combine(evidence,"runtime-proof.txt"),proof.Concat(new[] { Failure }));
                    Debug.LogException(exception); break;
                }
                if (!moved) { stack.Pop(); continue; }
                if (next is IEnumerator nested) { stack.Push(nested); continue; }
                yield return next;
            }
            if (capture)
            {
                File.WriteAllLines(Path.Combine(evidence,"runtime-proof.txt"), proof.Concat(new[] { Failure ?? "PASS" }));
                File.WriteAllText(Path.Combine(evidence,"capture-frames.txt"), ((frame + 1) / 2).ToString());
            }
            Complete = true;
        }

        public void ProbeDestruction()
        {
            var demo = new VisibilitySampleRules(this);
            demo.Deploy(CombatVisibility.Yang);
            var seat = demo.Seat(CombatVisibility.Yang);
            var support = seat.ActiveVisibility.Support;
            for (int i = 0; i < 24 && seat.ActiveVisibility != null; i++)
            {
                var command = new CombatCommand(CombatCommandKind.Art,VisibilitySampleRules.Yang,profileId:"spell_guanshiji",targetCell:support.Cell);
                var quote = demo.Commands.Preview(demo.Session,command);
                Debug.Log("ALTAR_PROBE hit=" + i + " durability=" + support.Durability + " tick=" + demo.Session.TurnScheduler.CurrentTick
                    + " expiry=" + seat.ActiveVisibility.ExpiresTick + " AP=" + demo.Actor(VisibilitySampleRules.Yang).ActionPointsRemainingUnits
                    + " nextCost=" + quote.ActionPointCostUnits + " reject=" + quote.RejectionReason);
                if (!quote.IsLegal)
                {
                    demo.Execute(new CombatCommand(CombatCommandKind.EndTurn,VisibilitySampleRules.Yang));
                    demo.Commands.AdvanceUntilAction(demo.Session); demo.ToTurn(VisibilitySampleRules.Yang);
                    Debug.Log("ALTAR_PROBE after turn durability=" + support.Durability + " tick=" + demo.Session.TurnScheduler.CurrentTick + " end=" + seat.LastEndReason);
                    if (seat.ActiveVisibility == null) break;
                }
                demo.Execute(command);
            }
        }

        private IEnumerator Run()
        {
            foreach (string branch in new[] { "yang", "yin", "neutral" })
            {
                Fresh(); yield return Hold(branch + "-00-dormant");
                demo.Deploy(CombatVisibility.Yin); Present();
                Check(!demo.Visible, "yin alone conceals the remote target");
                yield return Hold(branch + "-01-concealed");
                VerifyHidden(branch + " before conflict");
                demo.Deploy(CombatVisibility.Yang); Present();
                Check(demo.Session.Visibility.IsPaused, "both domains trigger observation conflict");
                yield return Hold(branch + "-02-both-public-altars");
                demo.Resolve(branch); Present();
                var conflict = demo.Session.Visibility.Current;
                Check(!conflict.IsPending && demo.Visible == (branch != "yin"), "authoritative observation result " + branch);
                int spent = branch == "yang" ? 100 : branch == "yin" ? 50 : 200;
                Check(conflict.LeftSpent == spent && conflict.RightSpent == spent, "committed conflict payment " + branch);
                Check(demo.Seat(CombatVisibility.Yang).ActiveVisibility != null && demo.Seat(CombatVisibility.Yin).ActiveVisibility != null,
                    "both domains survive every conflict outcome");
                Check(yang.transform.Find("Intact/Glow").gameObject.activeInHierarchy && yin.transform.Find("Intact/Glow").gameObject.activeInHierarchy,
                    "both public altar glows remain active");
                proof.Add(branch + " outcome=" + conflict.Outcome + " visible=" + demo.Visible + " spent=" + conflict.LeftSpent + "/" + conflict.RightSpent
                    + " public altars=2 active; tick=" + demo.Session.TurnScheduler.CurrentTick);
                yield return Hold(branch + "-03-result");
                if (branch == "yin")
                {
                    VerifyHidden("yin result");
                    yield return Move(new HexCoord(6,14), "yin-04-natural-reveal");
                    Check(demo.Visible && demo.Session.Visibility.NaturallyRevealed(demo.Actor(VisibilitySampleRules.Yin)), "adjacent natural reveal");
                    yield return Hold("yin-04-natural-reveal");
                    yield return AttackThenConceal();
                }
            }
            yield return ResourceStates();
            Check(initialCenters.SequenceEqual(surface.Cells.Select(c => Point(new HexCoord(c.q,c.r)))), "144 ground centers unchanged");
            proof.Add("144 ground centers unchanged; Unity=" + Application.unityVersion + "; camera tilt38 size5, detail size3.");
            proof.Add("Independent resource sample. Production Views, V3 HUD and self-created player integration remain pending.");
        }

        private void Fresh()
        {
            if (yang) Destroy(yang); if (yin) Destroy(yin);
            demo = new VisibilitySampleRules(this);
            provider.ConfigureSurface(surface);
            provider.Prepare(JindanRehearsalFactory.Descriptors(demo.Spawned, "combat_player_default_v1"));
            var config = catalog.visibilityRules;
            yang = Instantiate(yangAltar, Point(new HexCoord(config.yangQ,config.yangR)), Quaternion.identity);
            yin = Instantiate(yinAltar, Point(new HexCoord(config.yinQ,config.yinR)), Quaternion.identity);
            sampleCamera.transform.rotation = Quaternion.Euler(38,0,0);
            sampleCamera.orthographic = true; sampleCamera.orthographicSize = 5;
            Vector3 focus = (Point(new HexCoord(4,14)) + Point(new HexCoord(7,15))) * .5f;
            sampleCamera.transform.position = focus - sampleCamera.transform.forward * 20;
            Present();
        }

        private void Present()
        {
            Support(yang, demo.Seat(CombatVisibility.Yang)); Support(yin, demo.Seat(CombatVisibility.Yin));
            foreach (var actor in demo.Session.Combatants.All)
                if (provider.TryGetPresentedUnit(actor.Id, out var root))
                    root.SetActive(actor.IsAlive && (actor.Team == CombatTeam.Player || demo.Visible));
        }
        private static void Support(GameObject altar, CombatJindanSeatState seat)
        {
            bool destroyed = seat.LastEndReason == "jindan_support_destroyed";
            altar.transform.Find("Intact").gameObject.SetActive(!destroyed);
            altar.transform.Find("Failed").gameObject.SetActive(destroyed);
            altar.transform.Find("Intact/Glow").gameObject.SetActive(seat.ActiveVisibility != null && !destroyed);
        }

        private IEnumerator Move(HexCoord cell, string label)
        {
            var actor = demo.Actor(VisibilitySampleRules.Yang); HexCoord origin = actor.Position;
            var command = demo.Move(cell); var quote = demo.Commands.Preview(demo.Session, command);
            Check(quote.IsLegal, label + ":" + quote.RejectionReason);
            var steps = quote.MovementSteps.Select(s => new CombatUnitPresentationMovementStep(Pose(s.From),Pose(s.To),s.TurnFacings,s.IsLightLeap)).ToArray();
            demo.Execute(command); Present();
            provider.Present(new CombatUnitPresentationEventProjection(actor.Id, CombatUnitPresentationEvent.Move, Hex(origin),Hex(cell),actor.Facing,null,
                steps.Select(s => s.To.Position).ToArray(),movementSteps:steps));
            phase = label;
            for (int i = 0; i < 360 && provider.IsPresenting; i++) yield return Frames(1);
            Check(!provider.IsPresenting, "movement settled");
            proof.Add(label + " " + origin + "->" + cell + " AP=" + quote.ActionPointCostUnits + " visible=" + demo.Visible);
            yield return Frames(12); Key(label);
        }

        private IEnumerator AttackThenConceal()
        {
            var actor = demo.Actor(VisibilitySampleRules.Yang); var enemy = demo.Actor(VisibilitySampleRules.Yin);
            int before = enemy.CurrentHealth;
            demo.Execute(new CombatCommand(CombatCommandKind.Art,actor.Id,profileId:"spell_guanshiji",targetCell:enemy.Position));
            Check(enemy.CurrentHealth < before && enemy.IsAlive, "committed nonlethal attack hits naturally visible target");
            provider.Present(new CombatUnitPresentationEventProjection(actor.Id,CombatUnitPresentationEvent.Cast,Hex(actor.Position),Hex(actor.Position),actor.Facing,null,
                profileId:"spell_guanshiji",sourceCell:Hex(actor.Position),targetCell:Hex(enemy.Position)));
            provider.Present(new CombatUnitPresentationEventProjection(enemy.Id,CombatUnitPresentationEvent.Hit,Hex(enemy.Position),Hex(enemy.Position),enemy.Facing,null));
            phase = "yin-05-attack-hit-window"; yield return Frames(6); Key(phase);
            Check(provider.IsPresenting, "attack and hit feedback keeps existing presentation lock busy");
            proof.Add("Attack/hit at 0.1s: IsPresenting=true; current formal EncounterCoordinator blocks new player command and enemy advance during this window.");
            // Respect the same production busy guard; do not manufacture a leak by bypassing it.
            for (int i = 0; i < 180 && provider.IsPresenting; i++) yield return Frames(1);
            Check(!provider.IsPresenting, "feedback fully settled before changing observation");
            yield return Move(new HexCoord(4,14), "yin-06-withdraw-from-near-range");
            if (demo.Session.Visibility.IsPaused) demo.Resolve("yin");
            Present(); Check(!demo.Visible, "target concealed again after moving away");
            yield return Hold("yin-07-concealed-after-feedback");
            VerifyHidden("after real attack/hit and legal withdraw");
        }

        private IEnumerator ResourceStates()
        {
            // Explicit asset-state fixture, separate from the real observation branches above.
            // The existing slow actor cannot demolish a 600-durability altar before its six-tick expiry.
            Fresh(); yield return Hold("states-00-dormant");
            foreach (var altar in new[] { yang, yin }) altar.transform.Find("Intact/Glow").gameObject.SetActive(true);
            yield return Hold("states-01-active");
            foreach (var altar in new[] { yang, yin })
            {
                altar.transform.Find("Intact/Glow").gameObject.SetActive(false);
                altar.transform.Find("Intact").gameObject.SetActive(false);
                altar.transform.Find("Failed").gameObject.SetActive(true);
                Check(!altar.transform.Find("Intact/Glow").gameObject.activeInHierarchy, "broken asset has no glow");
            }
            yield return Hold("states-02-destroyed");
            proof.Add("states-* are explicit resource-state fixtures, not committed destruction. Original complete/failed meshes and root transforms preserved.");
        }

        private void VerifyHidden(string label)
        {
            Check(provider.TryGetPresentedUnit(VisibilitySampleRules.Yin,out var root) && !root.activeInHierarchy, "hidden root inactive");
            Check(root.GetComponentsInChildren<Renderer>(true).All(r => !r.gameObject.activeInHierarchy), "no model shadow outline or child marker renderer");
            Check(!provider.TryGetHeadAnchor(VisibilitySampleRules.Yin,out _) && !provider.TryGetVisibleBounds(VisibilitySampleRules.Yin,out _), "no visible HUD anchor");
            Check(FindObjectsByType<CombatAttackVfx>(FindObjectsSortMode.None).Length == 0, "no independent attack effect remains");
            Check(!FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Any(p => p.name.StartsWith("GuanzhongCombatFeedback") && p.IsAlive(true)), "no feedback particles remain");
            if (capture)
            {
                Render(); Color32[] before = pixels.GetPixels32();
                root.SetActive(true); foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                Render(); int residual = Difference(before,pixels.GetPixels32());
                root.SetActive(false); foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                Check(residual == 0, "hidden target has zero rendered contribution");
                proof.Add(label + ": inactive root + disabled-renderer baseline pixel difference=" + residual + "; model/shadow/visible HUD anchor=0.");
                proof.Add("Only generic Cast/Hit particles exercised; Guanshiji has no catalog Head/Tail/Ribbon. Actual HUD/sweep/flight markers are not assembled in this resource sample.");
            }
        }

        private IEnumerator Hold(string name)
        {
            phase = name; yield return Frames(60); Key(name);
            RecordAltar(yang,name); RecordAltar(yin,name);
            sampleCamera.orthographicSize = 3; Key(name + "-detail"); sampleCamera.orthographicSize = 5;
        }
        private void RecordAltar(GameObject altar, string label)
        {
            foreach (var renderer in altar.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name != "Glyph" && renderer.name != "Identity") continue;
                Bounds b = renderer.bounds;
                Vector2 min = new(float.MaxValue,float.MaxValue), max = new(float.MinValue,float.MinValue);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    Vector2 screen = sampleCamera.WorldToScreenPoint(corner); min = Vector2.Min(min,screen); max = Vector2.Max(max,screen);
                }
                proof.Add(label + " owner=VisibilityAssetSample/Support object=" + altar.name + "/Intact/" + renderer.name
                    + " world=" + b.center.ToString("F4") + " size=" + b.size.ToString("F4") + " screen=" + min.ToString("F2") + ".." + max.ToString("F2")
                    + " material=" + renderer.sharedMaterial.name + " shader=" + renderer.sharedMaterial.shader.name + " source=Space/Prefabs/ScaleSupport");
            }
        }
        private Vector3 Point(HexCoord cell) => surface.Center(Hex(cell));
        private static CombatUnitPresentationHex Hex(HexCoord cell) => new(cell.Q,cell.R);
        private static CombatUnitPresentationPose Pose(SpatialPose pose) => new(Hex(pose.Anchor),pose.Facing,pose.Footprint == SpatialFootprintKind.AdjacentPair,pose.AltitudeBand);
        private IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
                if (capture && frame % 2 == 0) { Render(); File.WriteAllBytes(Path.Combine(output,(frame/2).ToString("D4") + ".jpg"),pixels.EncodeToJPG(90)); }
                frame++;
            }
        }
        private void Render()
        {
            sampleCamera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0); pixels.Apply(); RenderTexture.active = previous;
        }
        private void Key(string name)
        {
            if (!capture) return; Render(); File.WriteAllBytes(Path.Combine(evidence,name + ".png"),pixels.EncodeToPNG());
            Check(FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled).All(r => r.sharedMaterials.All(m => m && m.shader.isSupported)), "supported materials");
        }
        private static int Difference(Color32[] a, Color32[] b)
        { int n=0; for(int i=0;i<a.Length;i++) if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>3)n++; return n; }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private void OnGUI() => GUI.Label(new Rect(24,18,900,40), "D12.5 资源样例 · " + phase + " · 正式 Demo 尚未接线");
        private void OnDestroy()
        {
            if (capture) Time.captureDeltaTime = 0;
            if (sampleCamera) sampleCamera.targetTexture = null;
            if (target) { target.Release(); Destroy(target); }
            if (pixels) Destroy(pixels);
        }
    }
}
