using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    /// <summary>Observation/capture only. Native input comes from the visible Unity window.</summary>
    internal static class GuanzhongTerrainNativeObservation
    {
        internal static IEnumerator Run(string output)
        {
            string frames=Path.Combine(output,"native-frames"); Directory.CreateDirectory(frames);
            string finish=Path.Combine(output,"native-finish.txt");
            if(File.Exists(finish)) File.Delete(finish);
            var proof=new Proof { width=Screen.width,height=Screen.height };
            var seen=new HashSet<string>();
            var log=new List<string>{"time,scene,cell,ap,pickedCell,inspected,mouseInterface"};
            int lastInstance=0;
            float captureAt=0,lastCapture=0;
            Vector3 previousCameraPosition=default;
            float previousSize=0, panDistance=0, zoomDistance=0;
            int previousCameraId=0, previousEncounterId=0;
            bool previousPanInput=false, previousZoomInput=false, previousWithdrawalPrompt=false;
            string pendingBodyId=null, pendingBodyCell=null;
            int pendingCancelAp=-1;
            int pendingEmptyAp=-1;
            string pendingEmptyOrigin=null;
            float pendingEmptyUntil=0;
            for(float end=Time.realtimeSinceStartup+1500;Time.realtimeSinceStartup<end;)
            {
                yield return null;
                proof.scene=SceneManager.GetActiveScene().name;
                if(proof.scene==GameplaySceneNames.Settlement && proof.withdrawalPromptSeen) proof.returnedSettlement=true;
                var controller=Object.FindFirstObjectByType<AdventureController>();
                var movement=Object.FindFirstObjectByType<CombatMovementView>();
                var input=Object.FindFirstObjectByType<CombatCommandInput>();
                var target=Object.FindFirstObjectByType<CombatTargetInput>();
                var provider=Object.FindFirstObjectByType<Static3DCombatUnitPresentationProvider>();
                var encounter=Object.FindFirstObjectByType<EncounterCoordinator>();
                var hud=Object.FindFirstObjectByType<CombatHudPresenter>();
                var surface=Object.FindFirstObjectByType<CombatBattlefieldSurface>();
                Camera camera=Camera.main;
                Mouse mouse=Mouse.current;
                bool nativeMouse=mouse!=null && mouse.native;
                bool clicked=nativeMouse && mouse.leftButton.wasPressedThisFrame;
                bool cancel=nativeMouse && mouse.rightButton.wasPressedThisFrame;
                bool inRehearsal=controller!=null && controller.IsTerrainRehearsal && encounter!=null && surface!=null;
                if(inRehearsal)
                {
                    if(lastInstance!=encounter.GetInstanceID()) { lastInstance=encounter.GetInstanceID(); proof.entries++; }
                    proof.withdrawalPromptSeen |= controller.IsWithdrawalConfirmationOpen;
                    proof.withdrawalCancelled |= previousWithdrawalPrompt && !controller.IsWithdrawalConfirmationOpen &&
                        controller.CurrentState==AdventureSceneState.Combat && previousEncounterId==encounter.GetInstanceID();
                    previousWithdrawalPrompt=controller.IsWithdrawalConfirmationOpen;
                    proof.ready=encounter.AcceptsPlayerCommand && !encounter.IsPresenting;
                    if(provider.TryGetPresentedPosition("player",out var position))
                    {
                        proof.cell=Key(position); seen.Add(proof.cell);
                        float y=surface.Center(position).y;
                        proof.low |= y<.4f;
                        proof.mid |= y>1.08f && y<1.11f;
                        proof.high |= y>1.83f;
                        proof.backToLow |= proof.entries==1 && proof.high && y<.4f;
                        proof.rearStair |= position.Q==8 && position.R==9;
                        proof.frontStair |= position.Q==5 && position.R==3;
                    }
                    var session=(CombatSession)typeof(EncounterCoordinator).GetField("session",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(encounter);
                    if(session!=null && session.Combatants.TryGet("player",out var player))
                    {
                        proof.ap=player.ActionPointsRemainingUnits;
                        if(Time.realtimeSinceStartup>pendingEmptyUntil) pendingEmptyAp=-1;
                        if(pendingEmptyAp>=0 && proof.ap==pendingEmptyAp-40000 && proof.cell==pendingEmptyOrigin)
                        { proof.emptyCast=true; pendingEmptyAp=-1; }
                    }
                    proof.inspected=hud.InspectedTargetId;
                    if(pendingCancelAp>=0)
                    {
                        proof.cancelled |= !movement.IsSelecting && !input.IsSelectingAttack && proof.ap==pendingCancelAp;
                        pendingCancelAp=-1;
                    }
                    if(cancel && (movement.IsSelecting || input.IsSelectingAttack)) pendingCancelAp=proof.ap;
                    if(IsConfirmedBodyPick(pendingBodyId!=null,pendingBodyId,proof.inspected))
                    {
                        proof.singlePicked |= pendingBodyId=="player";
                        proof.pairFrontPicked |= pendingBodyId=="enemy" && pendingBodyCell=="8:12";
                        proof.pairRearPicked |= pendingBodyId=="enemy" && pendingBodyCell=="7:12";
                    }
                    pendingBodyId=null;
                    if(clicked)
                    {
                        pendingEmptyAp=-1;
                        Vector2 pointer=mouse.position.ReadValue();
                        CombatUnitPresentationHex picked;
                        bool bodyHit=target.TryPickBodyPoint(pointer,out Vector3 body);
                        bool hit=bodyHit
                            ? movement.TryGetCellAtWorld(body,out picked) : movement.TryPickCell(pointer,out picked);
                        if(!movement.UiOwnsPointer(pointer) && hit)
                        {
                            proof.pickedCell=Key(picked);
                            string id=target.FindUnitAtCell(picked);
                            if(bodyHit && !movement.IsSelecting && !input.IsSelectingAttack)
                            { pendingBodyId=id; pendingBodyCell=proof.pickedCell; }
                            if(input.IsSelectingAttack && id==null)
                            { pendingEmptyAp=proof.ap; pendingEmptyOrigin=proof.cell; pendingEmptyUntil=Time.realtimeSinceStartup+2; }
                        }
                    }
                    proof.moves=hud.Snapshot?.Movement?.Options.Select(o=>
                    {
                        Vector3 screen=camera.WorldToScreenPoint(surface.Center(o.Destination));
                        return new Move { cell=Key(o.Destination),cost=o.Cost,x=screen.x,y=screen.y };
                    }).ToArray() ?? Array.Empty<Move>();
                    proof.cells=surface.Cells.Select(c=>
                    {
                        var hex=new CombatUnitPresentationHex(c.q,c.r);
                        Vector3 screen=camera.WorldToScreenPoint(surface.Center(hex));
                        return new Move { cell=Key(hex),x=screen.x,y=screen.y };
                    }).ToArray();
                }
                if(camera!=null)
                {
                    int cameraId=camera.GetInstanceID(), encounterId=inRehearsal?encounter.GetInstanceID():0;
                    bool sameContext=SameCameraContext(previousCameraId,cameraId,previousEncounterId,encounterId);
                    if(previousPanInput && sameContext) panDistance+=Vector3.Distance(camera.transform.position,previousCameraPosition);
                    if(previousZoomInput && sameContext) zoomDistance+=Mathf.Abs(camera.orthographicSize-previousSize);
                    proof.panned |= panDistance>.1f;
                    proof.zoomed |= zoomDistance>.1f;
                    proof.cameraSize=camera.orthographicSize;
                    previousCameraId=cameraId; previousEncounterId=encounterId;
                    previousCameraPosition=camera.transform.position; previousSize=camera.orthographicSize;
                    bool acceptsCamera=inRehearsal && nativeMouse && !movement.UiOwnsPointer(mouse.position.ReadValue());
                    previousPanInput=acceptsCamera && mouse.middleButton.isPressed;
                    previousZoomInput=acceptsCamera && mouse.scroll.y.ReadValue()!=0;
                }
                if(Time.realtimeSinceStartup>=captureAt || clicked || cancel)
                {
                    captureAt=Time.realtimeSinceStartup+.1f;
                    proof.visited=seen.ToArray();
                    File.WriteAllText(Path.Combine(output,"native-observation.json"),JsonUtility.ToJson(proof,true));
                    if(clicked || cancel)
                    {
                        log.Add(string.Join(",",Time.realtimeSinceStartup,proof.scene,proof.cell,proof.ap,proof.pickedCell,
                            proof.inspected,mouse.description.interfaceName));
                        File.WriteAllLines(Path.Combine(output,"native-clicks.csv"),log);
                    }
                    if(clicked || cancel || encounter!=null && encounter.IsPresenting || Time.realtimeSinceStartup-lastCapture>3)
                    {
                        lastCapture=Time.realtimeSinceStartup;
                        yield return new WaitForEndOfFrame();
                        ScreenCapture.CaptureScreenshot(Path.Combine(frames,(proof.frames++).ToString("D5")+".png"));
                    }
                }
                if(File.Exists(finish)) break;
            }
            Assert.IsTrue(File.Exists(finish),"Native acceptance timed out.");
            Assert.IsTrue(proof.low && proof.mid && proof.high && proof.backToLow,"Native lower/middle/upper route incomplete.");
            Assert.IsTrue(proof.rearStair && proof.frontStair,"Native stair branches incomplete.");
            Assert.IsTrue(proof.zoomed && proof.panned && proof.cancelled && proof.emptyCast,"Native camera/cancel/empty-cast incomplete.");
            Assert.IsTrue(proof.singlePicked && proof.pairFrontPicked && proof.pairRearPicked,"Native single/pair body picks incomplete.");
            Assert.IsTrue(proof.withdrawalPromptSeen && proof.withdrawalCancelled && proof.returnedSettlement && proof.entries>=2,"Native withdrawal/reentry incomplete.");
        }
        internal static bool IsConfirmedBodyPick(bool bodyHit,string pickedId,string inspectedId) =>
            bodyHit && pickedId!=null && pickedId==inspectedId;
        internal static bool SameCameraContext(int previousCamera,int camera,int previousEncounter,int encounter) =>
            camera!=0 && encounter!=0 && previousCamera==camera && previousEncounter==encounter;
        private static string Key(CombatUnitPresentationHex hex)=>hex.Q+":"+hex.R;
        [Serializable] private sealed class Move { public string cell; public int cost; public float x,y; }
        [Serializable] private sealed class Proof
        {
            public int width,height,ap,entries,frames;
            public string scene,cell,pickedCell,inspected;
            public bool ready,low,mid,high,backToLow,rearStair,frontStair,zoomed,panned,cancelled,emptyCast,
                singlePicked,pairFrontPicked,pairRearPicked,withdrawalPromptSeen,withdrawalCancelled,returnedSettlement;
            public float cameraSize;
            public string[] visited;
            public Move[] moves,cells;
        }
    }
}
