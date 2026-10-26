using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class D8WaterTideTests
    {
        private readonly CombatCommandService commands=new CombatCommandService();
        private const string Effect="WATER_TIDE", Source="water_guardian";
        private static HexCoord C(int q,int r=0)=>new HexCoord(q,r);
        private static CombatantSnapshot Actor(CombatSession session,string id="player")
        { session.Combatants.TryGet(id,out var actor);return actor; }
        private static CombatCommand Tide(CombatTideAction action=CombatTideAction.Rise,int q=0,bool charged=false,bool eye=false)=>
            new CombatCommand(CombatCommandKind.JindanAbility,"player",profileId:Effect,
                jindan:new CombatJindanRequest("water",tideAction:action,tideSource:C(q),charged:charged,createTideEye:eye));
        private CombatActionResult Commit(CombatSession session,CombatCommand command)
        { var result=commands.Execute(session,command);Assert.IsTrue(result.Succeeded,result.RejectionReason);return result; }
        private static CombatCommand Close()=>new CombatCommand(CombatCommandKind.CloseJindan,"player",profileId:Effect,jindan:new CombatJindanRequest("water"));
        private static void Cooldown(CombatSession session)=>Actor(session).SetCooldown(Effect,0);
        private void Next(CombatSession session)
        {
            Commit(session,new CombatCommand(CombatCommandKind.EndTurn,session.CurrentActorId));
            for(int i=0;i<10;i++)
            {
                var next=commands.AdvanceUntilAction(session);
                if(next.ActorId=="player") return;
                Commit(session,new CombatCommand(CombatCommandKind.EndTurn,next.ActorId));
            }
            Assert.Fail("player_not_ready");
        }

        [TestCase(CombatSurfaceKind.None,CombatTideAction.Rise,CombatSurfaceKind.Wet)]
        [TestCase(CombatSurfaceKind.Scorched,CombatTideAction.Rise,CombatSurfaceKind.Wet)]
        [TestCase(CombatSurfaceKind.Burning,CombatTideAction.Rise,CombatSurfaceKind.Wet)]
        [TestCase(CombatSurfaceKind.Wet,CombatTideAction.Rise,CombatSurfaceKind.ShallowWater)]
        [TestCase(CombatSurfaceKind.ShallowWater,CombatTideAction.Rise,CombatSurfaceKind.DeepWater)]
        [TestCase(CombatSurfaceKind.DeepWater,CombatTideAction.Rise,CombatSurfaceKind.DeepWater)]
        [TestCase(CombatSurfaceKind.DeepWater,CombatTideAction.Ebb,CombatSurfaceKind.ShallowWater)]
        [TestCase(CombatSurfaceKind.ShallowWater,CombatTideAction.Ebb,CombatSurfaceKind.Wet)]
        [TestCase(CombatSurfaceKind.Wet,CombatTideAction.Ebb,CombatSurfaceKind.None)]
        public void UniqueWaterTransitions(CombatSurfaceKind before,CombatTideAction action,CombatSurfaceKind after)
            =>Assert.AreEqual(after,CombatSurfaces.Water(before,action));

        [TestCase(CombatSurfaceKind.DeepWater,CombatSurfaceKind.ShallowWater)]
        [TestCase(CombatSurfaceKind.ShallowWater,CombatSurfaceKind.Wet)]
        [TestCase(CombatSurfaceKind.Wet,CombatSurfaceKind.None)]
        [TestCase(CombatSurfaceKind.None,CombatSurfaceKind.Burning)]
        public void HeatCannotSkipWaterLevels(CombatSurfaceKind before,CombatSurfaceKind after)
            =>Assert.AreEqual(after,CombatSurfaces.Heat(before,true));

        [Test]
        public void RiseFreezesSevenCellsAndUsesRealForceAndDamageWithoutTargetAP()
        {
            var session=Ready();var target=Actor(session,"enemy");int ap=target.ActionPointsRemainingUnits;
            var preview=commands.Preview(session,Tide()); Assert.IsTrue(preview.IsLegal,preview.RejectionReason);
            Assert.AreEqual(7,preview.Tide.Changes.Count);Assert.AreEqual(1,preview.Tide.WaterlineHeight);
            Assert.AreEqual(C(2),preview.Tide.Pushes.Single().Movement.Destination.Anchor);
            int mana=Actor(session).CurrentSpirit,actorAp=Actor(session).ActionPointsRemainingUnits;
            Commit(session,Tide());Assert.AreEqual(C(2),target.Position);Assert.AreEqual(ap,target.ActionPointsRemainingUnits);
            Assert.Less(target.CurrentHealth,target.MaximumHealth);Assert.AreEqual(mana-400,Actor(session).CurrentSpirit);
            Assert.AreEqual(actorAp-250000,Actor(session).ActionPointsRemainingUnits);
            Assert.AreEqual(7,session.Jindan.Find("water").OwnedSurfaceCount);
            Assert.AreEqual(1,session.Surfaces.Settlements.Last().Damage.Count);
        }

        [Test]
        public void AnchoredTargetStillTakesExactlyOneWaterHit()
        {
            var session=Ready();Anchor(Actor(session,"enemy"),3);
            var preview=commands.Preview(session,Tide());Assert.IsTrue(preview.IsLegal,preview.RejectionReason);
            Assert.AreEqual("earth_anchored",preview.Tide.Pushes.Single().Movement.Reason);
            Commit(session,Tide());Assert.AreEqual(C(1),Actor(session,"enemy").Position);
            Assert.AreEqual(1,session.Surfaces.Settlements.Last().Damage.Count);
        }

        [Test]
        public void StrongerForceBreaksWeakAnchorAndPreviewMatches()
        {
            var session=Ready();Anchor(Actor(session,"enemy"),1);
            var before=commands.Preview(session,Tide()).Tide.Pushes.Single().Movement.Destination;
            Commit(session,Tide());Assert.AreEqual(before,Actor(session,"enemy").Pose);
            Assert.IsNull(CombatForceMovement.Gravity(Actor(session,"enemy")));
        }

        [Test]
        public void FlyingTargetIsExcludedUntilGrounded()
        {
            var session=Ready(flying:true);var target=Actor(session,"enemy");
            Assert.IsEmpty(commands.Preview(session,Tide()).Tide.WaterTargetIds);Commit(session,Tide());
            Assert.AreEqual(target.MaximumHealth,target.CurrentHealth);Assert.AreEqual(C(1),target.Position);
            typeof(CombatantSnapshot).GetMethod("SetMovementMode",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(target,new object[]{SpatialMovementMode.Ground,0});
            Cooldown(session);Commit(session,Tide(CombatTideAction.Ebb));
            Assert.Less(target.CurrentHealth,target.MaximumHealth);
        }

        [Test]
        public void ClosedWaterBarrierStopsBfsAndMidActionErosionDoesNotExpandIt()
        {
            var wall=new CombatSceneObject("wall",new CombatSceneObjectDefinition("high_wall","stone",300,0,0,true,true,"rubble"),C(0),C(1));
            typeof(CombatSceneObject).GetMethod("ApplyDurability",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(wall,new object[]{150});
            var session=Ready(objects:new[]{wall},barrier:true,wetSource:true);
            var preview=commands.Preview(session,Tide());Assert.IsTrue(preview.IsLegal,preview.RejectionReason);
            Assert.IsFalse(preview.Tide.Changes.Any(value=>value.Cell==C(1)));
            CollectionAssert.Contains(preview.Tide.ErosionObjectIds,"wall");Commit(session,Tide());
            Assert.IsTrue(wall.IsDestroyed);Assert.AreEqual(CombatSurfaceKind.None,session.Surfaces.Get(C(1)).Kind);
        }

        [Test]
        public void LowCoverAndUnstoppableSupportCellsDoNotBlockWater()
        {
            var low=new CombatSceneObject("low",new CombatSceneObjectDefinition("low_wall","stone",300,2,2,false,false,"rubble"),C(0),C(1));
            var session=Ready(objects:new[]{low},nonStandable:true);
            var preview=commands.Preview(session,Tide());Assert.IsTrue(preview.IsLegal,preview.RejectionReason);
            Assert.IsTrue(preview.Tide.Changes.Any(value=>value.Cell==C(0,1)));
            Assert.IsTrue(preview.Tide.Changes.Any(value=>value.Cell==C(1)));
        }

        [Test]
        public void HeightAboveFixedWaterlineIsExcluded()
        {
            var session=Ready(highCell:true);var preview=commands.Preview(session,Tide());
            Assert.IsTrue(preview.IsLegal,preview.RejectionReason);Assert.IsFalse(preview.Tide.Changes.Any(value=>value.Cell==C(0,1)));
        }

        [Test]
        public void CapacityCountsOldOwnedCellsAndRejectsNewDistantPatchWithoutPayment()
        {
            var session=Ready(secondSource:true);Commit(session,Tide());Cooldown(session);
            Actor(session).SetPosition(C(1,-1));int mana=Actor(session).CurrentSpirit,ap=Actor(session).ActionPointsRemainingUnits;
            var result=commands.Execute(session,Tide(q:3));Assert.AreEqual("jindan_capacity_full",result.RejectionReason);
            Assert.AreEqual(mana,Actor(session).CurrentSpirit);Assert.AreEqual(ap,Actor(session).ActionPointsRemainingUnits);
        }

        [Test]
        public void TerminationClearsOnlyOwnedCurrentSurfacesAndNeverRestoresOldWater()
        {
            var session=Ready(secondSource:true);Commit(session,Tide());Commit(session,Close());
            Assert.AreEqual(CombatSurfaceKind.None,session.Surfaces.Get(C(0)).Kind);
            Assert.AreEqual(CombatSurfaceKind.ShallowWater,session.Surfaces.Get(C(3)).Kind);
            Assert.AreEqual(0,session.Jindan.Find("water").OwnedSurfaceCount);
            Assert.AreEqual("jindan_instance_missing",commands.Preview(session,Close()).RejectionReason);
        }

        [Test]
        public void SurfaceChangeInvalidatesPreviouslyFrozenMoveQuote()
        {
            var session=Ready();var move=new CombatCommand(CombatCommandKind.Move,"player",destination:C(-1));
            var before=commands.Preview(session,move);Assert.IsTrue(before.IsLegal,before.RejectionReason);
            Commit(session,Tide());var stale=commands.Execute(session,move,before);Assert.IsFalse(stale.Succeeded);
            var after=commands.Preview(session,move);Assert.IsTrue(after.IsLegal,after.RejectionReason);
            Assert.Greater(after.ActionPointCostUnits,before.ActionPointCostUnits);
            Assert.AreEqual(2,session.SceneObjects.Board.InspectEdge(C(-2),C(-1),SpatialQueryKind.Attack).MetricDistanceUnits);
        }

        [Test]
        public void NonSurfaceTerrainBurdenSurvivesConfigurationAndClear()
        {
            var session=Ready(dryBurden:3);session.SceneObjects.Board.TryGetCell(C(-2),out var cell);
            Assert.AreEqual(3,cell.MovementBurdenUnits);Commit(session,Tide());Commit(session,Close());
            session.SceneObjects.Board.TryGetCell(C(-1),out cell);Assert.AreEqual(3,cell.MovementBurdenUnits);
        }

        [Test]
        public void ChargedTidePaysOnceWaitsOneRealTickAndCompletesWithoutSecondCommand()
        {
            var session=Ready();int mana=Actor(session).CurrentSpirit;Commit(session,Tide(charged:true));
            Assert.NotNull(session.Jindan.Find("water").PendingTide);Assert.AreEqual(0,session.Surfaces.SettlementSequence);
            Assert.AreEqual(mana-600,Actor(session).CurrentSpirit);int tick=session.TurnScheduler.CurrentTick;
            Next(session);Assert.Greater(session.TurnScheduler.CurrentTick,tick);Assert.IsNull(session.Jindan.Find("water").PendingTide);
            Assert.AreEqual(1,session.Jindan.Find("water").SuccessfulDeployments);Assert.AreEqual(mana-600,Actor(session).CurrentSpirit);
            Assert.AreEqual(CombatSurfaceKind.DeepWater,session.Surfaces.Get(C(0)).Kind);
        }

        [Test]
        public void ChargedCompletionRevalidatesRangeAndDoesNotRefund()
        {
            var session=Ready();Commit(session,Tide(charged:true));int mana=Actor(session).CurrentSpirit;
            Actor(session).SetPosition(C(-4,-4));Next(session);
            Assert.IsNull(session.Jindan.Find("water").PendingTide);Assert.AreEqual(CombatSurfaceKind.ShallowWater,session.Surfaces.Get(C(0)).Kind);
            Assert.AreEqual(mana,Actor(session).CurrentSpirit);StringAssert.StartsWith("tide_process_invalid:",session.Jindan.Find("water").LastEndReason);
        }

        [Test]
        public void ChargedProcessBlocksReentryEvenAfterSharedCooldownClears()
        {
            var session=Ready();Commit(session,Tide(charged:true));Cooldown(session);
            Assert.AreEqual("tide_process_pending",commands.Preview(session,Tide()).RejectionReason);
            Commit(session,Close());Assert.IsNull(session.Jindan.Find("water").PendingTide);Assert.AreEqual(0,session.Surfaces.SettlementSequence);
        }

        [Test]
        public void TideEyeIsPaidAtomicAndRefusedWhenRealSourceExists()
        {
            var natural=Ready();Assert.AreEqual("tide_eye_cell_invalid",commands.Preview(natural,Tide(eye:true)).RejectionReason);
            var session=Ready(noWater:true);var quote=commands.Preview(session,Tide(charged:true,eye:true));
            Assert.IsTrue(quote.IsLegal,quote.RejectionReason);Assert.AreEqual(500000,quote.ActionPointCostUnits);Assert.AreEqual(800,quote.SpiritCost);
            Commit(session,Tide(charged:true,eye:true));Assert.AreEqual(CombatSurfaceKind.None,session.Surfaces.Get(C(0)).Kind);
            Next(session);Assert.AreEqual(CombatSurfaceKind.DeepWater,session.Surfaces.Get(C(0)).Kind);
        }

        [Test]
        public void EyeCannotBypassAnotherAccessibleNaturalSource()
        {
            var session=Ready(noWater:true,secondSource:true);Actor(session).SetPosition(C(1,-1));
            Assert.AreEqual("tide_real_source_available",commands.Preview(session,Tide(eye:true)).RejectionReason);
        }

        [Test]
        public void MultiTargetPushOrderUsesVacatedCellsAndNeverPushesAChain()
        {
            var session=Ready(centerTarget:true);var preview=commands.Preview(session,Tide()).Tide;
            Assert.AreEqual("enemy",preview.Pushes[0].ActorId);Assert.AreEqual(C(2),preview.Pushes[0].Movement.Destination.Anchor);
            Assert.AreEqual(C(1),preview.Pushes[1].Movement.Destination.Anchor);Commit(session,Tide());
            foreach(var push in preview.Pushes) Assert.AreEqual(push.Movement.Destination,Actor(session,push.ActorId).Pose);
        }

        [Test]
        public void CompleteStoneAndMetalRemainWhileTemporaryEarthAndPlantsErode()
        {
            var objects=new[]{
                new CombatSceneObject("stone",new CombatSceneObjectDefinition("low_wall","stone",300,1,2,false,false,"rubble"),C(0),C(1)),
                new CombatSceneObject("metal",new CombatSceneObjectDefinition("high_wall","metal",300,0,0,false,false,"rubble"),C(0),C(-1)),
                new CombatSceneObject("earth",new CombatSceneObjectDefinition("stone_support","stone",750,0,0,false,false,"none",isTemporaryRamp:true),C(0),C(0,1),"earth_guardian"),
                new CombatSceneObject("plant",new CombatSceneObjectDefinition("low_plant","plant",60,0,0,false,false,"charred"),C(0))};
            typeof(CombatSceneObject).GetMethod("ApplyDurability",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(objects[1],new object[]{150});
            var session=Ready(objects:objects,wetSource:true);var preview=commands.Preview(session,Tide());Assert.IsTrue(preview.IsLegal,preview.RejectionReason);
            CollectionAssert.AreEquivalent(new[]{"earth","plant"},preview.Tide.ErosionObjectIds);Commit(session,Tide());
            Assert.AreEqual(300,objects[0].Durability);Assert.AreEqual(150,objects[1].Durability);Assert.AreEqual(600,objects[2].Durability);Assert.IsTrue(objects[3].IsDestroyed);
        }

        [Test]
        public void OrdinaryFireUsesGlobalClockDeduplicatesAndSpreadsOnlyNextCycle()
        {
            var session=Ready(fire:true);var target=Actor(session,"enemy");target.SetPosition(C(0,1));int hp=target.CurrentHealth;
            AdvanceFire(session,8);Assert.AreEqual(hp,target.CurrentHealth);
            AdvanceFire(session,1);Assert.Less(target.CurrentHealth,hp);var fire=session.Surfaces.FireSources.Single();
            Assert.AreEqual(10,fire.RemainingFuel);Assert.AreEqual(2,session.Surfaces.Cells.Count(value=>value.Kind==CombatSurfaceKind.Burning));
            Assert.AreEqual(1,session.Surfaces.Settlements.Last().Damage.Count);
            AdvanceFire(session,30);Assert.IsFalse(session.Surfaces.Cells.Any(value=>value.Kind==CombatSurfaceKind.Burning));
        }

        [Test]
        public void WaterExtinguishesFireAndSeversItsFurtherSpread()
        {
            var session=Ready(fire:true);Commit(session,Tide());Assert.AreEqual(CombatSurfaceKind.Wet,session.Surfaces.Get(C(0,1)).Kind);
            AdvanceFire(session,40);Assert.IsFalse(session.Surfaces.Cells.Any(value=>value.Kind==CombatSurfaceKind.Burning));
        }

        [Test]
        public void DestroyedFuelObjectImmediatelyEndsDependentFire()
        {
            var plant=new CombatSceneObject("fuel",new CombatSceneObjectDefinition("low_plant","plant",60,0,0,false,false,"charred"),C(0,1));
            var session=Ready(fire:true,objects:new[]{plant},fuelObject:true);
            typeof(CombatSceneObject).GetMethod("ApplyDurability",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(plant,new object[]{0});
            typeof(CombatSurfaces).GetMethod("Reconcile",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(session.Surfaces,new object[]{session});
            Assert.AreEqual(CombatSurfaceKind.Scorched,session.Surfaces.Get(C(0,1)).Kind);Assert.AreEqual(0,session.Surfaces.Get(C(0,1)).Fuel);
        }

        [Test]
        public void FireAndTideRespectNumericDefenseAndNoRandomDodgeOrCritical()
        {
            var a=Ready();var b=Ready(defense:10000);Actor(a,"enemy").DodgeRate=100;Actor(a).CriticalRate=100;
            Commit(a,Tide());Commit(b,Tide());int ordinary=a.Surfaces.Settlements.Last().Damage.Single().FinalDamage;
            int defended=b.Surfaces.Settlements.Last().Damage.Single().FinalDamage;
            Assert.Greater(ordinary,defended);Assert.IsFalse(a.Surfaces.Settlements.Last().Damage.Single().IsCritical);
            Assert.IsTrue(a.Surfaces.Settlements.Last().Damage.Single().IsHit);
        }

        [Test]
        public void EmptyOptionalContentReferencesAreEquivalentToAbsentReferences()
        {
            var value=new CombatSurfaceDefinition(C(0),CombatSurfaceKind.None,4,"","");
            Assert.IsNull(value.SourceRef);Assert.IsNull(value.FuelObjectId);
        }

        [Test]
        public void GuaranteedSoulShieldIsRetainedWithoutRollingProbabilisticShields()
        {
            var shield=Ready(fire:true);var ordinary=Ready(fire:true);var target=Actor(shield,"enemy");
            target.SetPosition(C(0,1));Actor(ordinary,"enemy").SetPosition(C(0,1));
            target.SoulShieldRate=100;target.SoulShieldReduction=50;
            Actor(ordinary,"enemy").SoulShieldRate=99;Actor(ordinary,"enemy").SoulShieldReduction=50;
            AdvanceFire(shield,9);AdvanceFire(ordinary,9);
            var defended=shield.Surfaces.Settlements.Last().Damage.Single();var normal=ordinary.Surfaces.Settlements.Last().Damage.Single();
            Assert.IsTrue(defended.IsSoulShielded);Assert.IsFalse(normal.IsSoulShielded);
            Assert.AreEqual(normal.FinalDamage/2,defended.FinalDamage);
        }

        [Test]
        public void TideEyeCanCoexistWithLowCoverAndPlants()
        {
            var objects=new[]{new CombatSceneObject("cover",new CombatSceneObjectDefinition("low_wall","stone",300,2,2,false,false,"rubble"),C(0),C(1)),
                new CombatSceneObject("plant",new CombatSceneObjectDefinition("low_plant","plant",300,0,0,false,false,"charred"),C(0))};
            var session=Ready(noWater:true,objects:objects);var quote=commands.Preview(session,Tide(eye:true));
            Assert.IsTrue(quote.IsLegal,quote.RejectionReason);Commit(session,Tide(eye:true));
            Assert.AreEqual(CombatSurfaceKind.DeepWater,session.Surfaces.Get(C(0)).Kind);Assert.AreEqual(150,objects[1].Durability);
        }

        [Test]
        public void SelfHitDoesNotRestoreManaOrCooldownDuringAtomicCommit()
        {
            var session=Ready();Actor(session).SetPosition(C(0));int mana=Actor(session).CurrentSpirit;
            Commit(session,Tide());Assert.AreEqual(mana-400,Actor(session).CurrentSpirit);Assert.AreEqual(1,Actor(session).GetCooldown(Effect));
            Assert.Less(Actor(session).CurrentHealth,10000);
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void NonCenterTidePushUsesTheActualRadialDirection(int direction)
        {
            var session=Ready();Actor(session,"enemy").SetPosition(C(0).Step(direction));
            Assert.AreEqual(direction,commands.Preview(session,Tide()).Tide.Pushes.Single().Direction);
        }

        [Test]
        public void RearBodyDropIsDetectedEvenWhenAnchorUsesFlatEdge()
        {
            var session=Ready(pair:true,rearDrop:true);
            var result=CombatForceMovement.Query(session,"enemy",5,1,2);
            Assert.AreEqual(1,result.Path.Count,result.Reason);Assert.IsTrue(result.CrossedDrop);
            Assert.AreEqual(C(1,1),result.Destination.Anchor);
        }

        [Test]
        public void TideDropPreviewAndReceiptSeparateOneFallFromOneWaterHit()
        {
            var session=Ready(drop:true);var quote=commands.Preview(session,Tide());
            Assert.IsTrue(quote.IsLegal,quote.RejectionReason);Assert.AreEqual(1000,quote.Tide.Pushes.Single().FallDamage);
            Commit(session,Tide());var receipt=session.Surfaces.Settlements.Last();
            Assert.AreEqual(1000,receipt.FallDamage.Single().FinalDamage);Assert.AreEqual(1,receipt.Damage.Count);
            Assert.AreEqual(10000-1000-receipt.Damage.Single().FinalDamage,Actor(session,"enemy").CurrentHealth);
        }

        [Test]
        public void ReceiptTargetsKeepTheirCommittedPoseHealthAndLiveness()
        {
            var session=Ready();Commit(session,Tide());var receipt=session.Surfaces.Settlements.Last();
            var snapshot=receipt.Targets.Single();int hp=snapshot.Health;
            Actor(session,"enemy").SetPosition(C(3));Actor(session,"enemy").ReceiveDamage(10000);
            Assert.AreEqual(C(2),snapshot.Pose.Anchor);Assert.AreEqual(hp,snapshot.Health);Assert.IsTrue(snapshot.IsAlive);
        }

        [Test]
        public void PhysicalDefenseDebtIsConsumedByWaterExactlyOnce()
        {
            var session=Ready(defense:2400);var target=Actor(session,"enemy");
            typeof(CombatantSnapshot).GetMethod("ApplyKuxingDefenseDebt",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(target,new object[]{"debt"});
            Commit(session,Tide());Assert.IsFalse(target.HasStatus(CombatStatusKind.KuxingDefenseDebt));
            int first=session.Surfaces.Settlements.Last().Damage.Single().FinalDamage;
            target.SetPosition(C(1));Cooldown(session);Commit(session,Tide(CombatTideAction.Ebb));
            Assert.Greater(first,session.Surfaces.Settlements.Last().Damage.Single().FinalDamage);
        }

        [Test]
        public void DoubleCellTargetIsHitAndPushedOnceWithItsCompleteBody()
        {
            var session=Ready(pair:true);var target=Actor(session,"enemy");
            var preview=commands.Preview(session,Tide());Assert.IsTrue(preview.IsLegal,preview.RejectionReason);
            Assert.AreEqual(1,preview.Tide.WaterTargetIds.Count);Assert.AreEqual(1,preview.Tide.Pushes.Count);
            Commit(session,Tide());Assert.AreEqual(2,target.OccupiedCells.Count);Assert.AreEqual(1,session.Surfaces.Settlements.Last().Damage.Count);
        }

        [Test]
        public void MetricCompressionUsesDistanceBudgetRatherThanOneTopologyStep()
        {
            var session=Ready(metric:1);var preview=commands.Preview(session,Tide());
            Assert.AreEqual(2,preview.Tide.Pushes.Single().Movement.Path.Count);
            Assert.AreEqual(C(3),preview.Tide.Pushes.Single().Movement.Destination.Anchor);
            Commit(session,Tide());Assert.AreEqual(C(3),Actor(session,"enemy").Position);
        }

        [Test]
        public void MetricExpansionPreventsPushButDoesNotPreventWaterHit()
        {
            var session=Ready(metric:4);Actor(session).SetPosition(C(-1,-1));
            var preview=commands.Preview(session,Tide());Assert.IsTrue(preview.IsLegal,preview.RejectionReason);
            Assert.IsEmpty(preview.Tide.Pushes.Single().Movement.Path);Commit(session,Tide());
            Assert.Less(Actor(session,"enemy").CurrentHealth,10000);
        }

        [Test]
        public void FatalTideRetainsCommittedReceiptAndClearsAllPersistentState()
        {
            var session=Ready();Actor(session,"enemy").ReceiveDamage(9999);Commit(session,Tide());
            Assert.IsFalse(Actor(session,"enemy").IsAlive);Assert.AreEqual(1,session.Surfaces.Settlements.Count);
            Assert.IsFalse(session.Surfaces.Cells.Any(value=>value.Kind!=CombatSurfaceKind.None));
            Assert.AreEqual(0,session.Jindan.Find("water").OwnedSurfaceCount);
            Assert.AreEqual(C(2),session.Surfaces.Settlements.Single().Pushes.Single().Movement.Destination.Anchor);
        }

        [Test]
        public void FatalEnvironmentCycleStopsBeforeLaterCyclesAndReturnsNoGhostTurn()
        {
            var session=Ready(fire:true);Actor(session,"enemy").SetPosition(C(0,1));Actor(session,"enemy").ReceiveDamage(9999);
            // Delay the next actor long enough that one scheduler advance spans all four possible fire cycles.
            typeof(CombatSession).GetMethod("RecordAction",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(session,new object[]{9900});
            Commit(session,new CombatCommand(CombatCommandKind.EndTurn,"player"));
            for(int i=0;i<12;i++)
            {
                var advance=commands.AdvanceUntilAction(session);
                if(!advance.HasActor)
                {
                    Assert.IsFalse(Actor(session,"enemy").IsAlive);Assert.AreEqual(1,session.Surfaces.Settlements.Count(value=>value.Damage.Count>0));
                    Assert.IsTrue(session.Surfaces.Cells.All(value=>value.Kind==CombatSurfaceKind.None));return;
                }
                Commit(session,new CombatCommand(CombatCommandKind.EndTurn,advance.ActorId));
            }
            Assert.Fail("fatal_fire_did_not_end_session");
        }

        private static void AdvanceFire(CombatSession session,int count)
        {
            var method=typeof(CombatSurfaces).GetMethod("AdvanceTick",BindingFlags.Instance|BindingFlags.NonPublic);
            for(int i=0;i<count;i++) method.Invoke(session.Surfaces,new object[]{session,i+1});
        }

        private static void Anchor(CombatantSnapshot actor,int rank)
        {
            var status=(CombatOwnTurnStatus)Activator.CreateInstance(typeof(CombatOwnTurnStatus),BindingFlags.Instance|BindingFlags.NonPublic,
                null,new object[]{CombatStatusKind.EarthGravity,2,0,false},null);
            typeof(CombatOwnTurnStatus).GetProperty("EarthMode").SetValue(status,CombatEarthMode.Anchor);
            typeof(CombatOwnTurnStatus).GetProperty("GravityShiftRank").SetValue(status,rank);
            typeof(CombatOwnTurnStatus).GetProperty("SourceRef").SetValue(status,"gravity");
            typeof(CombatantSnapshot).GetMethod("ApplyEarthGravity",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(actor,new object[]{status});
        }

        private CombatSession Ready(bool flying=false,CombatSceneObject[] objects=null,bool barrier=false,bool wetSource=false,
            bool nonStandable=false,bool highCell=false,bool secondSource=false,int dryBurden=0,bool noWater=false,
            bool centerTarget=false,bool fire=false,bool fuelObject=false,int defense=10,bool pair=false,int metric=2,bool rearDrop=false,bool drop=false)
        {
            var cells=new Dictionary<HexCoord,SpatialCellRules>();var edges=new Dictionary<SpatialDirectedEdge,SpatialEdgeRules>();
            for(int q=-4;q<=4;q++)for(int r=-4;r<=4;r++)cells[C(q,r)]=new SpatialCellRules(highCell && q==0 && r==1?2:
                rearDrop && q==0 && r==0 || drop && q<=1?1:0,
                nonStandable && q==0 && r==1,false,false,(q==0&&r==0&&!noWater)?(wetSource?0:1):dryBurden,true,4);
            foreach(var cell in cells.Keys)foreach(var next in cell.Neighbors())if(cells.ContainsKey(next))
            {
                bool waterBarrier=barrier && Math.Min(cell.Q,next.Q)==0 && Math.Max(cell.Q,next.Q)==1 && !(cell==C(0)&&next==C(1) || cell==C(1)&&next==C(0));
                bool pairSupport=rearDrop && (cell==C(0)&&next==C(1) || cell==C(1)&&next==C(0));
                var kind=pairSupport?SpatialEdgeKind.Slope:rearDrop && cell==C(0)&&next==C(0,1) ||
                    drop && cells[cell].HeightLevel>cells[next].HeightLevel?SpatialEdgeKind.OneWayDrop:
                    cells[cell].HeightLevel!=cells[next].HeightLevel?SpatialEdgeKind.OpenCliff:SpatialEdgeKind.Flat;
                edges[new SpatialDirectedEdge(cell,next)]=new SpatialEdgeRules(metric,true,!waterBarrier,
                    clearanceLimit:pairSupport?2:0,edgeKind:kind,connectionId:pairSupport?"pair_support":null,allowsFlight:true);
            }
            var scene=new CombatSceneObjects(new SpatialQueryBoard(cells,edges,new SpatialQueryLimits(2,16)),objects??Array.Empty<CombatSceneObject>(),10,20);
            var player=new CombatantSnapshot("player",CombatTeam.Player,C(-2),100,10000,10000,2400,2400,10,10,1000000,10000,
                abilityInstanceIds:new Dictionary<string,string>{{Effect,Source}});player.SetSpirit(10000,10000);
            var enemy=new CombatantSnapshot("enemy",CombatTeam.Enemy,C(1),1,10000,10000,2400,2400,defense,defense,1000000,10000,
                footprint:pair?SpatialFootprintKind.AdjacentPair:SpatialFootprintKind.Single,bodyScaleRank:pair?2:1,
                movementMode:flying?SpatialMovementMode.Fly:SpatialMovementMode.Ground,altitudeBand:flying?1:0);
            var actors=new List<CombatantSnapshot>{player,enemy};
            if(centerTarget)actors.Add(new CombatantSnapshot("center",CombatTeam.Enemy,C(0),1,10000,10000,2400,2400,10,10,1000000,10000));
            var session=new CombatSession(actors,Array.Empty<CombatAttackProfile>(),new Query(scene),
                new[]{new CombatOrdinaryActionDefinition(CombatCommandKind.Guard,50000,50000,0,0),new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell,50000,50000,0,60)},sceneObjects:scene);
            session.ConfigureJindan(new CombatJindanLoadout(new[]{new CombatJindanSeatDefinition("player","water","position","Source",Source,new[]{Effect},Effect)},
                new[]{new CombatJindanAbilityDefinition(Effect,true,250000,400,1,25000,0,4,3,7,0,0,8,Array.Empty<int>(),"",tide:
                    new CombatTideDefinition(1,1,2,1,125000,200,1,125000,200,2400,13f/280,1.25f,150))},1));
            var surfaces=cells.Keys.Select(cell=>new CombatSurfaceDefinition(cell,
                fire&&cell==C(0,1)?CombatSurfaceKind.Burning:cell==C(0)&&!noWater || cell==C(3)&&secondSource?CombatSurfaceKind.ShallowWater:CombatSurfaceKind.None,
                fire&&cell.TopologicalDistanceTo(C(0,1))<=1?4:0,fire&&cell==C(0,1)?"fire":null,fuelObject&&cell==C(0,1)?"fuel":null));
            session.ConfigureSurfaces(new CombatSurfaces(surfaces,fire?new[]{new CombatFireDefinition("fire","enemy",10,40,3,12,2400,.01f)}:null));
            Assert.AreEqual("player",commands.AdvanceUntilAction(session).ActorId);return session;
        }

        private sealed class Query:ICombatSpatialQuery
        {
            private readonly CombatSceneObjects scene;internal Query(CombatSceneObjects scene)=>this.scene=scene;
            public SpatialContactResult QueryRange(SpatialPose a,SpatialPose b,int min,int max,SpatialAttackPolicy policy=null)=>scene.Board.QueryContact(a,b,min,max,policy);
            public CombatMovementQueryResult QueryMovement(SpatialPose a,HexCoord b,int budget,int efficiency,IReadOnlyCollection<HexCoord> occupied,int? destinationFacing=null)
            { var path=scene.Board.QueryMovementPath(a,b,budget,efficiency,occupied,destinationFacing);return new CombatMovementQueryResult(path.Path.Count>0,path.Reason,path.Steps,path.UnitsPerRange,path.ActionPointCostUnits); }
            public IReadOnlyDictionary<SpatialPose,int> FindReachable(SpatialPose a,int budget,int efficiency,IReadOnlyCollection<HexCoord> occupied)=>scene.Board.FindReachableWithinActionPoints(a,budget,efficiency,occupied);
        }
    }
}
