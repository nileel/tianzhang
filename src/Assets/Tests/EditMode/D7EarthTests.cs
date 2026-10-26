using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class D7EarthTests
    {
        private const string Manifest="EARTH_MANIFEST", Gravity="EARTH_GRAVITY";
        private readonly CombatCommandService commands=new CombatCommandService();
        private static HexCoord C(int q)=>new HexCoord(q,0);
        private static CombatantSnapshot Actor(CombatSession s,string id="player") { s.Combatants.TryGet(id,out var a); return a; }
        private static CombatJindanSeatState Seat(CombatSession s)=>s.Jindan.Find("earth");
        private CombatActionResult Commit(CombatSession s,CombatCommand c) { var r=commands.Execute(s,c); Assert.IsTrue(r.Succeeded,r.RejectionReason); return r; }
        private static CombatCommand Earth(CombatEarthMode mode,int a=1,int b=2,string target="player")=>
            new CombatCommand(CombatCommandKind.JindanAbility,"player",profileId:mode is CombatEarthMode.Wall or CombatEarthMode.Ramp?Manifest:Gravity,
                jindan:new CombatJindanRequest("earth",new[]{C(a),C(b)},earthMode:mode,earthTargetId:target,selectedTargetCell:C(target=="player"?0:6)));
        private static CombatCommand Close(CombatSession s)=>new CombatCommand(CombatCommandKind.CloseJindan,"player",profileId:Seat(s).EffectId,jindan:new CombatJindanRequest("earth"));
        private static CombatCommand Move(int q)=>new CombatCommand(CombatCommandKind.Move,"player",destination:C(q));
        private static void Cooldown(CombatSession s)=>Actor(s).SetCooldown(Seat(s).EffectId,0);
        private void Next(CombatSession s)
        {
            Commit(s,new CombatCommand(CombatCommandKind.EndTurn,s.CurrentActorId));
            for(int i=0;i<20;i++)
            {
                var turn=commands.AdvanceUntilAction(s); Assert.IsTrue(turn.HasActor);
                if(turn.ActorId=="player") return;
                Commit(s,new CombatCommand(CombatCommandKind.EndTurn,turn.ActorId));
            }
            Assert.Fail("player_turn_missing");
        }

        [Test]
        public void WallsQuoteThenPayOnceBlockGroundAirAndEffectsAndBreakWithoutResidue()
        {
            var s=Ready(); var a=Actor(s); var c=Earth(CombatEarthMode.Wall); var quote=commands.Preview(s,c);
            Assert.IsTrue(quote.IsLegal,quote.RejectionReason); Assert.AreEqual(250000,quote.ActionPointCostUnits); Assert.AreEqual(400,quote.SpiritCost);
            Assert.IsTrue(commands.Prepare(s,c,quote,out _).Succeeded); Assert.IsEmpty(s.SceneObjects.All);
            Commit(s,c); var wall=Seat(s).ActiveEarth.Objects.Single();
            Assert.AreEqual(750,wall.Durability); Assert.AreEqual(750000,a.ActionPointsRemainingUnits); Assert.AreEqual(5600,a.CurrentSpirit);
            Assert.AreEqual("combat_quote_stale",commands.Execute(s,c,quote).RejectionReason);
            Assert.IsFalse(s.SceneObjects.Board.InspectEdge(C(1),C(2),SpatialQueryKind.Movement).IsLegal);
            Assert.IsFalse(s.SceneObjects.Board.InspectEdge(C(1),C(2),SpatialQueryKind.Attack).IsLegal);
            Assert.IsFalse(s.SceneObjects.Board.InspectFlightEdge(new SpatialPose(C(1),movementMode:SpatialMovementMode.Fly,altitudeBand:2),C(2)).IsLegal);
            var attack=new CombatCommand(CombatCommandKind.Art,"player",profileId:"break",targetCell:C(1),rolls:new CombatResolutionRolls(0,100,100,100,100));
            Commit(s,attack); Assert.AreEqual(375,wall.Durability); Assert.IsFalse(s.SceneObjects.Board.InspectEdge(C(1),C(2),SpatialQueryKind.Attack).IsLegal);
            a.SetCooldown("break",0); Commit(s,attack); Assert.IsEmpty(s.SceneObjects.All); Assert.IsNull(Seat(s).ActiveEarth);
            Assert.IsTrue(s.SceneObjects.Board.InspectEdge(C(1),C(2),SpatialQueryKind.Attack).IsLegal);
        }

        [Test]
        public void ManifestCapacityAndFirstDeploymentClockDoNotRefreshOnAppend()
        {
            var s=Ready(); Commit(s,Earth(CombatEarthMode.Wall,3,4)); Next(s); Cooldown(s);
            Commit(s,Earth(CombatEarthMode.Wall)); Assert.AreEqual(2,Seat(s).ActiveEarth.Objects.Count);
            Assert.AreEqual(3,Seat(s).ActiveEarth.RemainingOwnTurnEnds); Cooldown(s);
            Assert.AreEqual("jindan_capacity_full",commands.Preview(s,Earth(CombatEarthMode.Wall,4,5)).RejectionReason);
            Next(s); Assert.AreEqual(2,Seat(s).ActiveEarth.RemainingOwnTurnEnds);
            Next(s); Assert.AreEqual(1,Seat(s).ActiveEarth.RemainingOwnTurnEnds);
            Next(s); Assert.IsNull(Seat(s).ActiveEarth); Assert.IsEmpty(s.SceneObjects.All);
        }

        [Test]
        public void RampOpensOnlyOneSingleCellCliffAndRemovalRestoresIt()
        {
            var s=Ready(cliff:true);
            Assert.IsFalse(s.SceneObjects.Board.InspectEdge(C(1),C(2),SpatialQueryKind.Movement).IsLegal);
            Commit(s,Earth(CombatEarthMode.Ramp));
            Assert.IsTrue(s.SceneObjects.Board.InspectEdge(C(1),C(2),SpatialQueryKind.Movement).IsLegal);
            Assert.IsTrue(s.SceneObjects.Board.InspectEdge(C(2),C(1),SpatialQueryKind.Movement).IsLegal);
            Assert.AreEqual(SpatialQueryReasons.ClearanceInsufficient,s.SceneObjects.Board.InspectEdge(C(1),C(2),SpatialQueryKind.Movement,bodyScaleRank:2).Reason);
            Commit(s,Move(2)); Assert.AreEqual(C(2),Actor(s).Position);
            Commit(s,Close(s)); Assert.AreEqual(C(2),Actor(s).Position);
            Assert.IsFalse(s.SceneObjects.Board.InspectEdge(C(1),C(2),SpatialQueryKind.Movement).IsLegal);
        }

        [TestCase("occupied")][TestCase("flat_ramp")][TestCase("mana")][TestCase("sealed")]
        public void InvalidManifestNeverPays(string failure)
        {
            var s=Ready(); var a=Actor(s);
            if(failure=="occupied") Actor(s,"enemy").SetPosition(C(1));
            if(failure=="mana") a.SetSpirit(6000,399);
            if(failure=="sealed") a.ApplyStatus(CombatStatusKind.SealSpirit,2);
            int ap=a.ActionPointsRemainingUnits,mp=a.CurrentSpirit;
            Assert.IsFalse(commands.Execute(s,Earth(failure=="flat_ramp"?CombatEarthMode.Ramp:CombatEarthMode.Wall)).Succeeded);
            Assert.AreEqual(ap,a.ActionPointsRemainingUnits); Assert.AreEqual(mp,a.CurrentSpirit); Assert.IsEmpty(s.SceneObjects.All);
        }

        [Test]
        public void GravityUsesAnchoredMixedStatusAndExactWholeEdgeRationalPrice()
        {
            var s=Ready(gravity:true,burden:1); var a=Actor(s);
            Commit(s,Earth(CombatEarthMode.Light)); var status=CombatForceMovement.Gravity(a);
            Assert.AreEqual(CombatStatusPolarity.MIXED,status.Polarity); Assert.AreEqual("ANCHORED",status.RemovalPolicy);
            Assert.AreEqual(3,status.GravityShiftRank); Assert.AreEqual(0,CombatForceMovement.LoadRank(a));
            var quote=commands.Preview(s,Move(1)); Assert.IsTrue(quote.IsLegal,quote.RejectionReason);
            Assert.AreEqual(11250,quote.ActionPointCostUnits); Commit(s,Move(1));
            Assert.AreEqual(SpatialMovementMode.Ground,a.MovementMode);
            Assert.AreEqual(2,s.SceneObjects.Board.InspectEdge(C(0),C(1),SpatialQueryKind.Attack).MetricDistanceUnits);
        }

        [Test]
        public void HeavyCapsActualEdgesAndRecastDoesNotErasePressure()
        {
            var s=Ready(gravity:true); var a=Actor(s); Commit(s,Earth(CombatEarthMode.Heavy));
            Assert.IsFalse(commands.Preview(s,Move(3)).IsLegal); Commit(s,Move(2));
            Assert.AreEqual(2,CombatForceMovement.Gravity(a).Pressure); Assert.IsFalse(commands.Preview(s,Move(1)).IsLegal);
            Cooldown(s); var recast=new CombatCommand(CombatCommandKind.JindanAbility,"player",profileId:Gravity,
                jindan:new CombatJindanRequest("earth",earthMode:CombatEarthMode.Heavy,earthTargetId:"player",selectedTargetCell:C(2)));
            Commit(s,recast); Assert.AreEqual(2,CombatForceMovement.Gravity(a).Pressure);
            Next(s); Assert.AreEqual(2,CombatForceMovement.Gravity(a).Pressure);
            Next(s); Assert.AreEqual(0,CombatForceMovement.Gravity(a).Pressure);
        }

        [Test]
        public void LightLeapDoesNotGrantFlightAndStillRequiresLegalGroundLanding()
        {
            var s=Ready(gravity:true,cliff:true); Commit(s,Earth(CombatEarthMode.Light)); Commit(s,Move(2));
            Assert.AreEqual(SpatialMovementMode.Ground,Actor(s).MovementMode);
            var blocked=Ready(gravity:true,cliff:true,blocked:2); Commit(blocked,Earth(CombatEarthMode.Light));
            Assert.IsFalse(commands.Preview(blocked,Move(2)).IsLegal);
        }

        [Test]
        public void AnchorNeedsStrictlyHigherForceAndPhysicalPushDoesNotSpendTargetAp()
        {
            var s=Ready(gravity:true); var a=Actor(s); Commit(s,Earth(CombatEarthMode.Anchor));
            int ap=a.ActionPointsRemainingUnits;
            Assert.AreEqual("earth_anchored",commands.Preview(s,Move(1)).RejectionReason);
            foreach(int force in new[]{2,3}) Assert.IsEmpty(CombatForceMovement.Query(s,a.Id,0,1,force).Path);
            var result=CombatForceMovement.Apply(s,a.Id,0,1,4); Assert.AreEqual(C(1),result.Destination.Anchor);
            Assert.AreEqual(ap,a.ActionPointsRemainingUnits); Assert.IsNull(CombatForceMovement.Gravity(a));
        }

        [TestCase(false,0,900)][TestCase(true,1,1000)]
        public void HeavyUsesActualLiftForPressureLandingAndAirOnlyLowering(bool bothAir,int expectedAltitude,int health)
        {
            var s=Ready(gravity:true); var a=Actor(s); var target=Actor(s,"enemy"); target.SetPosition(C(2));
            SetAir(target,2); ActivateFlight(s,"enemy");
            if(bothAir) { SetAir(a,1); ActivateFlight(s,"player"); }
            var c=new CombatCommand(CombatCommandKind.JindanAbility,a.Id,profileId:Gravity,
                jindan:new CombatJindanRequest("earth",earthMode:CombatEarthMode.Heavy,earthTargetId:target.Id,selectedTargetCell:C(2)));
            int ap=target.ActionPointsRemainingUnits; Commit(s,c);
            Assert.AreEqual(expectedAltitude,target.AltitudeBand); Assert.AreEqual(health,target.CurrentHealth); Assert.AreEqual(ap,target.ActionPointsRemainingUnits);
            Commit(s,Close(s)); Assert.AreEqual(expectedAltitude,target.AltitudeBand); Assert.AreEqual(health,target.CurrentHealth);
        }

        [Test]
        public void ForcedMovementChecksRearBodyAndSpaceSealBeforeAnchor()
        {
            var s=Ready(gravity:true); var a=Actor(s); Commit(s,Earth(CombatEarthMode.Anchor));
            Assert.AreEqual("forced_movement_space_sealed",CombatForceMovement.Query(Ready(gravity:true,seal:true),"player",0,1,2).Reason);
            Actor(s,"enemy").SetPosition(C(1)); Assert.IsEmpty(CombatForceMovement.Query(s,a.Id,0,1,4).Path);
            var pair=new SpatialPose(C(1),0,SpatialFootprintKind.AdjacentPair,2);
            Assert.IsNotEmpty(s.SceneObjects.Board.InspectPlacement(pair,new[]{C(0)}));
        }

        [Test]
        public void LethalGravityLandingClearsInstanceBeforeCommandReturns()
        {
            var s=Ready(gravity:true,keepEnemy:true); var target=Actor(s,"enemy"); target.SetPosition(C(2));
            target.ReceiveDamage(950); SetAir(target,2); ActivateFlight(s,target.Id);
            Commit(s,new CombatCommand(CombatCommandKind.JindanAbility,"player",profileId:Gravity,
                jindan:new CombatJindanRequest("earth",earthMode:CombatEarthMode.Heavy,earthTargetId:target.Id,selectedTargetCell:C(2))));
            Assert.IsFalse(target.IsAlive); Assert.IsNull(Seat(s).ActiveEarth);
            Assert.AreEqual("jindan_instance_missing",commands.Preview(s,Close(s)).RejectionReason);
        }

        [TestCase(false,false,true)][TestCase(true,false,false)][TestCase(false,true,false)]
        public void DropFlagRequiresActualDescendingGroundCrossing(bool upward,bool fly,bool expected)
        {
            var s=Ready(gravity:upward,cliff:true,drop:true); var a=Actor(s); a.SetPosition(C(upward?1:2));
            if(upward) Commit(s,new CombatCommand(CombatCommandKind.JindanAbility,a.Id,profileId:Gravity,
                jindan:new CombatJindanRequest("earth",earthMode:CombatEarthMode.Light,earthTargetId:a.Id,selectedTargetCell:a.Position)));
            if(fly){SetAir(a,1);ActivateFlight(s,a.Id);}
            var result=CombatForceMovement.Query(s,a.Id,upward?0:3,1,2);
            Assert.IsNotEmpty(result.Path);Assert.AreEqual(expected,result.CrossedDrop);
        }

        [TestCase("clear")][TestCase("rear_edge")][TestCase("rear_occupied")]
        public void ForcedTranslationChecksBothActualBodyEdgesAndLandings(string obstruction)
        {
            var cells=new Dictionary<HexCoord,SpatialCellRules>(); var edges=new Dictionary<SpatialDirectedEdge,SpatialEdgeRules>();
            for(int q=0;q<4;q++) for(int r=0;r<3;r++) cells[new HexCoord(q,r)]=new SpatialCellRules();
            foreach(var cell in cells.Keys) foreach(var next in cell.Neighbors()) if(cells.ContainsKey(next))
                edges[new SpatialDirectedEdge(cell,next)]=new SpatialEdgeRules(2,
                    !(obstruction=="rear_edge" && cell==new HexCoord(0,0) && next==new HexCoord(0,1)),true);
            var objects=new CombatSceneObjects(new SpatialQueryBoard(cells,edges,new SpatialQueryLimits(2,16)),Array.Empty<CombatSceneObject>(),10,20);
            var pair=new CombatantSnapshot("pair",CombatTeam.Player,new HexCoord(1,0),100,100,100,10,10,1,1,100000,1000,
                footprint:SpatialFootprintKind.AdjacentPair,bodyScaleRank:2,facing:0);
            var enemy=new CombatantSnapshot("enemy",CombatTeam.Enemy,new HexCoord(obstruction=="rear_occupied"?0:3,1),1,100,100,10,10,1,1,100000,1000);
            var s=new CombatSession(new[]{pair,enemy},Array.Empty<CombatAttackProfile>(),new Query(objects),
                new[]{new CombatOrdinaryActionDefinition(CombatCommandKind.Guard,50000,50000,0,0),
                    new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell,50000,50000,0,60)},sceneObjects:objects);
            int ap=pair.ActionPointsRemainingUnits;
            var result=CombatForceMovement.Apply(s,pair.Id,5,1,2);
            Assert.AreEqual(obstruction=="clear"?1:0,result.Path.Count);
            Assert.AreEqual(0,pair.Facing);Assert.AreEqual(ap,pair.ActionPointsRemainingUnits);
        }

        private CombatSession Ready(bool gravity=false,bool cliff=false,int burden=0,int blocked=-1,bool seal=false,bool drop=false,bool keepEnemy=false)
        {
            var cells=new Dictionary<HexCoord,SpatialCellRules>(); var edges=new Dictionary<SpatialDirectedEdge,SpatialEdgeRules>();
            for(int q=0;q<8;q++) cells[C(q)]=new SpatialCellRules(cliff && q>=2?1:0,q==blocked,false,false,burden,true,4);
            for(int q=0;q<7;q++) foreach(bool reverse in new[]{false,true})
                edges[new SpatialDirectedEdge(C(reverse?q+1:q),C(reverse?q:q+1))]=new SpatialEdgeRules(2,true,true,
                    effectBlockerMask:seal && q==0?1UL:0UL,edgeKind:cliff && q==1?(drop?SpatialEdgeKind.OneWayDrop:SpatialEdgeKind.OpenCliff):SpatialEdgeKind.Flat,allowsFlight:true);
            var objects=new CombatSceneObjects(new SpatialQueryBoard(cells,edges,new SpatialQueryLimits(2,16)),Array.Empty<CombatSceneObject>(),10,20);
            var bindings=new Dictionary<string,string>{{Manifest,"guardian"},{Gravity,"guardian"},{CombatFlight.ProfileId,"player_flight"}};
            var a=new CombatantSnapshot("player",CombatTeam.Player,C(0),100,1000,1000,100,100,10,10,1000000,10000,
                equippedArtProfileIds:new[]{"break"},availableArtProfileIds:new[]{"break"},abilityInstanceIds:bindings);
            a.SetSpirit(6000,6000);
            var b=new CombatantSnapshot("enemy",CombatTeam.Enemy,C(6),1,1000,1000,100,100,10,10,1000000,10000,
                abilityInstanceIds:new Dictionary<string,string>{{CombatFlight.ProfileId,"enemy_flight"}}); b.SetSpirit(6000,6000);
            var profiles=new[]{new CombatAttackProfile("break",CombatAttackKind.Art,CombatAttackEffect.Physical,0,8,250000,250000,0,1,
                physicalMultiplier:1,spatialPolicy:new SpatialAttackPolicy(SpatialMovementMode.Ground|SpatialMovementMode.Fly,SpatialMovementMode.Ground|SpatialMovementMode.Fly,3),
                environmentTargetTypes:new[]{"high_wall","stone_support"},environmentDamage:375)};
            var ordinary=new[]{new CombatOrdinaryActionDefinition(CombatCommandKind.Guard,50000,50000,0,0),new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell,50000,50000,0,60)};
            var actors=new List<CombatantSnapshot>{a,b};
            if(keepEnemy)actors.Add(new CombatantSnapshot("survivor",CombatTeam.Enemy,C(7),1,1000,1000,100,100,10,10,1000000,10000));
            var s=new CombatSession(actors,profiles,new Query(objects),ordinary,sceneObjects:objects);
            s.ConfigureJindan(new CombatJindanLoadout(new[]{new CombatJindanSeatDefinition(a.Id,"earth","earth-position","Source","guardian",new[]{Manifest,Gravity},gravity?Gravity:Manifest)},
                new[]{Definition(Manifest),Definition(Gravity)},1));
            s.ConfigureFlight(new CombatFlight(new[]{new KeyValuePair<string,string>(a.Id,"player_flight"),new KeyValuePair<string,string>(b.Id,"enemy_flight")},
                Enum.GetValues(typeof(CombatFlightAction)).Cast<CombatFlightAction>().Select(action=>new CombatFlightCost(action,250000,200,1)),100,3));
            Assert.AreEqual(a.Id,commands.AdvanceUntilAction(s).ActorId); return s;
        }
        private static CombatJindanAbilityDefinition Definition(string id)=>new CombatJindanAbilityDefinition(id,true,250000,400,id==Manifest?1:3,25000,0,8,3,id==Manifest?2:1,600,0,8,Array.Empty<int>(),"",id==Manifest?3:2);
        private static void SetAir(CombatantSnapshot a,int altitude)=>typeof(CombatantSnapshot).GetMethod("SetMovementMode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(a,new object[]{SpatialMovementMode.Fly,altitude});
        private static void ActivateFlight(CombatSession s,string id)
        {
            var state=s.Flight.Find(id); typeof(CombatFlightState).GetProperty("Active").SetValue(state,true);
            typeof(CombatFlightState).GetProperty("RemainingOwnTurnEnds").SetValue(state,3);
        }
        private sealed class Query:ICombatSpatialQuery
        {
            private readonly CombatSceneObjects objects;
            internal Query(CombatSceneObjects objects)=>this.objects=objects;
            public SpatialContactResult QueryRange(SpatialPose a,SpatialPose b,int min,int max,SpatialAttackPolicy policy=null)=>objects.Board.QueryContact(a,b,min,max,policy);
            public CombatMovementQueryResult QueryMovement(SpatialPose a,HexCoord b,int budget,int efficiency,IReadOnlyCollection<HexCoord> occupied,int? destinationFacing=null)
            {var p=objects.Board.QueryMovementPath(a,b,budget,efficiency,occupied,destinationFacing);return new CombatMovementQueryResult(p.Path.Count>0,p.Reason,p.Steps,p.UnitsPerRange,p.ActionPointCostUnits);}
            public IReadOnlyDictionary<SpatialPose,int> FindReachable(SpatialPose a,int budget,int efficiency,IReadOnlyCollection<HexCoord> occupied)=>objects.Board.FindReachableWithinActionPoints(a,budget,efficiency,occupied);
        }
    }
}
