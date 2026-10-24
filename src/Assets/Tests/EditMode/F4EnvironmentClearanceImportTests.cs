using System;
using System.Linq;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests.EditMode
{
    public sealed class F4EnvironmentClearanceImportTests
    {
        [Test]
        public void SparseCsvClearanceSurvivesAssetReloadAndRestrictsTheRuntimeBoardByBodyRank()
        {
            const string header = "profileId,queryLimits,battlefieldCells,surfacePrototypeRefs,phenomenonChannels,phenomenonPairs,elementRelationRefs,spiritRecoveryEnvironment,edgeClearanceLimits,mapSchemaVersion,mapVersion,directedEdgeOverrides,physicalSurfaceProfiles,mapMarkers";
            const string row = "f4_clearance_fixture,unitsPerRange=2;maxQueryRange=16,-1:0@0@0|0:0@0@0|1:0@0@0,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze,element_wood|element_fire|element_earth|element_metal|element_water,ordinary,0:0>1:0@1,,,,,";
            EnvironmentProfileDefinition parsed = WorldContentImporter.ParseEnvironmentProfiles(new[] { header, row }, "F4-clearance.csv").Single();
            string path = "Assets/F4ClearanceFixture_" + Guid.NewGuid().ToString("N") + ".asset";
            var asset = ScriptableObject.CreateInstance<EnvironmentProfileAsset>();
            try
            {
                asset.Apply(parsed);
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
                Resources.UnloadAsset(asset);
                asset = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(path);
                Assert.IsNotNull(asset);
                Assert.IsTrue(asset.TryCreateDefinition(out var definition, out string reason), reason);
                Assert.IsTrue(EnvironmentProfileRuntime.TryCreate(definition, out var runtime, out reason), reason);
                Assert.AreEqual(1, runtime.DirectedEdges.Single(edge => edge.fromQ == 0 && edge.toQ == 1).clearanceLimit);
                Assert.AreEqual(0, runtime.DirectedEdges.Single(edge => edge.fromQ == 1 && edge.toQ == 0).clearanceLimit,
                    "Sparse clearance must not leak into the reverse direction.");
                Assert.AreEqual(0, runtime.DirectedEdges.Single(edge => edge.fromQ == -1 && edge.toQ == 0).clearanceLimit,
                    "Omitted edges must retain their unlimited-clearance meaning.");
                var grid = new TacticalGridModel();
                foreach (EnvironmentBattlefieldCell cell in definition.battlefieldCells)
                    grid.SetTile(new TacticalTileData(new HexCoord(cell.q, cell.r))
                    { BlocksGroundMove = cell.blocksGroundMove, BlocksLineOfSight = cell.blocksLineOfSight });
                Assert.IsTrue(SpatialQueryBoardFactory.TryCreate(grid, runtime, out var snapshot, out reason), reason);
                var origin = new HexCoord(0, 0);
                var destination = new HexCoord(1, 0);
                foreach (SpatialFootprintKind footprint in new[] { SpatialFootprintKind.Single, SpatialFootprintKind.AdjacentPair })
                {
                    var small = new SpatialPose(origin, 0, footprint, 1);
                    var large = new SpatialPose(origin, 0, footprint, 2);
                    var admitted = snapshot.Board.QueryMovementPath(small, destination, 100000, 1000);
                    var rejected = snapshot.Board.QueryMovementPath(large, destination, 100000, 1000);
                    Assert.AreEqual(1, admitted.Steps.Count, admitted.Reason);
                    Assert.IsEmpty(rejected.Steps, footprint.ToString());
                    Assert.AreEqual(SpatialQueryReasons.ClearanceInsufficient, rejected.Reason);
                    Assert.IsTrue(snapshot.Board.QueryContact(large, new SpatialPose(destination), 1, 1).IsInRange,
                        "Body clearance restricts movement, not attack range or line of sight.");
                }
            }
            finally
            {
                if (AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(path) != null) AssetDatabase.DeleteAsset(path);
                else if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
