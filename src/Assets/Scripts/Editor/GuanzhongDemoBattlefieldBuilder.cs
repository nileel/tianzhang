using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Bootstrap;
using TianZhang.Features.CombatPresentation;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    /// <summary>Production assembly of the frozen V3 map using the accepted D3 modules.</summary>
    public static class GuanzhongDemoBattlefieldBuilder
    {
        public const string RootName = "GuanzhongTerrainRehearsal";
        [MenuItem("天章/场景/D4 导入攻击与场景物并重建演武")]
        public static void ImportAndRebuild()
        {
            CombatContentImporter.ImportAttackProfiles();
            WorldContentImporter.ImportEnvironmentProfiles();
            Rebuild();
        }
        [MenuItem("天章/场景/D3.6 装配阶地演武与入口")]
        public static void Rebuild()
        {
            var scene=EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath,OpenSceneMode.Single);
            Install();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath,OpenSceneMode.Single);
            Debug.Log("D36_PRODUCTION_REBUILT");
        }

        public static void Install()
        {
            GameObject previous=GameObject.Find(RootName);
            // Find also the deliberately inactive production map on repeated rebuild.
            if (previous==null) previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene()
                .GetRootGameObjects().SingleOrDefault(o=>o.name==RootName);
            if (previous!=null) UnityEngine.Object.DestroyImmediate(previous);
            var environment=SceneBuildSupport.RequireAsset<EnvironmentProfileAsset>(GuanzhongRampSampleBuilder.EnvironmentPath);
            var profiles=JsonUtility.FromJson<Profiles>(environment.physicalSurfaceProfiles);
            var root=new GameObject(RootName);
            var surface=root.AddComponent<CombatBattlefieldSurface>();
            var data=new SerializedObject(surface);
            var definitions=data.FindProperty("cells");
            definitions.arraySize=environment.battlefieldCells.Length;
            for(int i=0;i<environment.battlefieldCells.Length;i++)
            {
                var cell=environment.battlefieldCells[i];
                var definition=definitions.GetArrayElementAtIndex(i);
                definition.FindPropertyRelative("q").intValue=cell.q;
                definition.FindPropertyRelative("r").intValue=cell.r;
                var flat=profiles.flatProfiles.SingleOrDefault(p=>p.id==cell.physicalSurfaceId);
                var ramp=profiles.rampProfiles.SingleOrDefault(p=>p.id==cell.physicalSurfaceId);
                var stair=profiles.stairConnections.SingleOrDefault(p=>p.id==cell.physicalSurfaceId);
                int kind=flat!=null ? 0 : ramp!=null ? 1 : 2;
                definition.FindPropertyRelative("kind").intValue=kind;
                float lower=flat!=null ? flat.supportY : ramp!=null ? ramp.lowerY : stair.lowerY;
                definition.FindPropertyRelative("lowerY").floatValue=lower;
                definition.FindPropertyRelative("upperY").floatValue=ramp!=null ? ramp.upperY : lower+.75f;
                definition.FindPropertyRelative("startZ").floatValue=ramp?.worldZStart ?? 0;
                definition.FindPropertyRelative("endZ").floatValue=ramp?.worldZEnd ?? 0;
                SetFloats(definition.FindPropertyRelative("riserZ"),stair?.risers.Select(r=>r.worldZ).ToArray() ?? Array.Empty<float>());
                SetFloats(definition.FindPropertyRelative("riserY"),stair?.risers.Select(r=>r.supportYAfter).ToArray() ?? Array.Empty<float>());
                Vector3 p=CombatBattlefieldSurface.Horizontal(cell.q,cell.r);
                p.y=lower;
                GameObject tile;
                if(flat!=null)
                {
                    string path=cell.surfaceKind=="shallow_water" ? GuanzhongWaterbankSampleBuilder.AssetRoot+"/ShallowWaterTile.prefab" :
                        cell.surfaceKind=="mud_bank" ? GuanzhongWaterbankSampleBuilder.AssetRoot+"/MudBankTile.prefab" : GuanzhongRampSampleBuilder.LandingPath;
                    tile=Place(root.transform,path,"GuanzhongHex_"+cell.q+"_"+cell.r,
                        p-Vector3.up*(cell.surfaceKind=="shallow_water" || cell.surfaceKind=="mud_bank" ? 0 : .000433121f));
                }
                else
                {
                    tile=new GameObject("GuanzhongHex_"+cell.q+"_"+cell.r);
                    tile.transform.SetParent(root.transform,false);
                    tile.transform.position=p;
                }
                if(cell.surfaceKind=="shallow_water")
                {
                    int[,] directions={{1,0},{1,-1},{0,-1},{-1,0},{-1,1},{0,1}};
                    for(int direction=0;direction<6;direction++)
                    {
                        int q=cell.q+directions[direction,0],r=cell.r+directions[direction,1];
                        if(environment.battlefieldCells.Any(c=>c.q==q && c.r==r && c.heightLevel==cell.heightLevel && c.surfaceKind=="shallow_water")) continue;
                        var edge=Place(tile.transform,GuanzhongWaterbankSampleBuilder.AssetRoot+"/ShoreEdge.prefab","Shore_"+direction,new Vector3(0,.005f,0));
                        edge.transform.localRotation=Quaternion.Euler(0,direction*60,0);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(edge.transform);
                    }
                }
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            foreach(var ramp in profiles.rampProfiles)
            {
                int q=int.Parse(ramp.coveredCells[0].Split(':')[0]);
                int r=int.Parse(ramp.coveredCells[0].Split(':')[1]);
                Place(root.transform,GuanzhongRampSampleBuilder.PrefabPath,ramp.id,
                    new Vector3(q+.5f*r,ramp.lowerY,ramp.worldZStart));
            }
            foreach(var stair in profiles.stairConnections)
                Place(root.transform,GuanzhongStairSampleBuilder.PrefabPath,stair.id,
                    new Vector3(stair.platformCenters[0].worldX,stair.lowerY,stair.worldZStart));
            foreach(var marker in JsonUtility.FromJson<Markers>("{\"items\":"+environment.mapMarkers+"}").items)
            {
                int[] qr=marker.cell.Split(':').Select(int.Parse).ToArray();
                var hex=new TianZhang.Gameplay.Contracts.CombatUnitPresentationHex(qr[0],qr[1]);
                Vector3 p=surface.Center(hex);
                string asset=marker.kind=="rock_obstacle" ? GuanzhongTerrainAssetBuilder.RockPrefabPath :
                    marker.kind=="public_planned_support" ? GuanzhongPropSampleBuilder.AssetRoot+"/SupportStele.prefab" : null;
                if(asset==null) continue;
                if(marker.kind!="rock_obstacle") p+=new Vector3(.25f,0,.4330127f);
                var prop=Place(root.transform,asset,marker.id,p);
                PrefabUtility.RecordPrefabInstancePropertyModifications(prop.transform);
            }
            InstallSceneObjects(root, surface, environment);
            var installer=UnityEngine.Object.FindFirstObjectByType<AdventureSceneInstaller>();
            Transform panel=GameObject.Find("AdventurePanel").transform;
            Transform oldButton=panel.Find("TerrainRehearsalButton");
            if(oldButton!=null) UnityEngine.Object.DestroyImmediate(oldButton.gameObject);
            Button button=SceneBuildSupport.CreateButton("TerrainRehearsalButton",panel,"阶地演武（地形试玩）",out _);
            SettlementSceneBuilder.ApplyButtonSkin(button);
            SceneBuildSupport.SetObject(installer,"terrainRehearsalButton",button);
            SceneBuildSupport.SetObject(installer,"ordinaryBattlefield",GameObject.Find("GuanzhongBattlefield"));
            SceneBuildSupport.SetObject(installer,"terrainRehearsalSurface",surface);
            SceneBuildSupport.SetObject(installer,"terrainRehearsalEnvironment",environment);
            var wiring=new SerializedObject(installer);
            SetNode(wiring.FindProperty("terrainRehearsalStart"),"terrain_start",4,0,null);
            SetNode(wiring.FindProperty("terrainRehearsalEncounter"),"terrain_encounter",8,12,"enemy_lieshijia");
            var extra=wiring.FindProperty("terrainRehearsalEncounter").FindPropertyRelative("additionalEnemies");
            extra.arraySize=1;
            var enemy=extra.GetArrayElementAtIndex(0);
            enemy.FindPropertyRelative("instanceId").stringValue="enemy_2";
            enemy.FindPropertyRelative("enemyId").stringValue="enemy_shijiahou";
            enemy.FindPropertyRelative("q").intValue=6;
            enemy.FindPropertyRelative("r").intValue=14;
            enemy.FindPropertyRelative("facing").intValue=0;
            wiring.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(false);
        }

        private static void InstallSceneObjects(GameObject root, CombatBattlefieldSurface surface, EnvironmentProfileAsset environment)
        {
            var bindings = new List<CombatSceneObjectView.Binding>();
            foreach (var instance in environment.sceneObjects.instances)
            {
                string prefab = instance.contentType switch
                {
                    "low_wall" => "LowWall", "high_wall" => "HighWall",
                    "stone_support" => "SupportStele", "low_plant" => "LowPlant",
                    _ => throw new InvalidOperationException("scene_object_visual_type_missing:" + instance.contentType),
                };
                Vector3 position = Center(instance.cell);
                float yaw = 0;
                if (!string.IsNullOrEmpty(instance.edgeOther))
                {
                    Vector3 other = Center(instance.edgeOther);
                    Vector3 direction = other - position;
                    position = (position + other) * .5f;
                    yaw = 90 - Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg;
                }
                else position += new Vector3(.25f, 0, .4330127f);
                var prop = Place(root.transform, GuanzhongPropSampleBuilder.AssetRoot + "/" + prefab + ".prefab", instance.instanceId, position);
                prop.transform.rotation = Quaternion.Euler(0, yaw, 0);
                if (instance.contentType == "low_wall") prop.transform.localScale = new Vector3(.93f, 1, 1);
                PrefabUtility.RecordPrefabInstancePropertyModifications(prop.transform);
                GameObject intact = prop.transform.Find(prefab == "LowPlant" ? "Living" : "Intact").gameObject;
                GameObject damaged = prop.transform.Find("Damaged")?.gameObject ?? intact;
                GameObject residue = prop.transform.Find(prefab == "LowPlant" ? "Charred" : prefab == "SupportStele" ? "Failed" : "Remnant").gameObject;
                if (prefab == "SupportStele")
                {
                    var glow = intact.transform.Find("Glow").gameObject;
                    glow.SetActive(false);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(glow);
                }
                bindings.Add(new CombatSceneObjectView.Binding { instanceId = instance.instanceId, intact = intact, damaged = damaged, residue = residue });
            }
            var view = root.AddComponent<CombatSceneObjectView>();
            view.Configure(bindings.ToArray());
            EditorUtility.SetDirty(view);
            SceneBuildSupport.SetObject(UnityEngine.Object.FindFirstObjectByType<CombatHudPresenter>(), "sceneObjects", view);

            Vector3 Center(string cell)
            {
                int[] qr = cell.Split(':').Select(int.Parse).ToArray();
                return surface.Center(new TianZhang.Gameplay.Contracts.CombatUnitPresentationHex(qr[0], qr[1]));
            }
        }
        private static void SetNode(SerializedProperty node,string id,int q,int r,string enemy)
        {
            node.FindPropertyRelative("nodeId").stringValue=id;
            node.FindPropertyRelative("q").intValue=q;
            node.FindPropertyRelative("r").intValue=r;
            node.FindPropertyRelative("contentId").stringValue=enemy;
            node.FindPropertyRelative("enemyFacing").intValue=0;
        }
        private static void SetFloats(SerializedProperty p,float[] values)
        { p.arraySize=values.Length; for(int i=0;i<values.Length;i++) p.GetArrayElementAtIndex(i).floatValue=values[i]; }
        private static GameObject Place(Transform parent,string path,string name,Vector3 position)
        {
            var obj=(GameObject)PrefabUtility.InstantiatePrefab(SceneBuildSupport.RequireAsset<GameObject>(path));
            obj.name=name; obj.transform.SetParent(parent,false); obj.transform.localPosition=position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj.transform);
            return obj;
        }
        [Serializable] private sealed class Profiles { public Flat[] flatProfiles; public Ramp[] rampProfiles; public Stair[] stairConnections; }
        [Serializable] private sealed class Flat { public string id; public float supportY; }
        [Serializable] private sealed class Ramp { public string id; public string[] coveredCells; public float lowerY,upperY,worldZStart,worldZEnd; }
        [Serializable] private sealed class Stair { public string id; public float lowerY,worldZStart; public Platform[] platformCenters; public Riser[] risers; }
        [Serializable] private sealed class Platform { public float worldX; }
        [Serializable] private sealed class Riser { public float worldZ,supportYAfter; }
        [Serializable] private sealed class Markers { public EnvironmentMapMarkerJson[] items; }
    }
}
