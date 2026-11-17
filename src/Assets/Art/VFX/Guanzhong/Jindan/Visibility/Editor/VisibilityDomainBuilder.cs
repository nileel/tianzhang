using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Art.Jindan.Visibility.Editor
{
    // Package-local native ParticleSystem assets; no runtime component or production dependency.
    internal static class VisibilityDomainBuilder
    {
        private const string Root = VisibilityAssetBuilder.Root;
        private const string Earth = "Assets/Art/VFX/Guanzhong/Jindan/Earth";

        public static void Build(Mesh coverage)
        {
            foreach (bool moon in new[] { false,true })
            {
                string name=moon ? "YinDomainRange" : "YangDomainRange";
                var ground=new Material(Shader.Find("TianZhang/Jindan/Visibility Domain Range"));
                ground.SetColor("_BaseColor",moon ? new Color(.32f,.56f,.82f) : new Color(1.15f,.87f,.43f));
                ground.SetFloat("_Moon",moon ? 1 : 0); ground.SetFloat("_Opacity",0);
                ground.renderQueue=moon ? 3011 : 3010;
                ground=Save(ground,"Materials/"+name+".mat");
                var go=new GameObject(name); go.AddComponent<MeshFilter>().sharedMesh=coverage;
                var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=ground;
                renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
                if (moon)
                {
                    Particle(go.transform,"LowMoonMist",coverage,"SoilDust",new Color(.42f,.66f,.95f,.32f),
                        new Vector2(2.6f,4.2f),new Vector2(.65f,1.05f),new Vector3(.18f,.012f,-.10f),24,.12f,false);
                    Particle(go.transform,"MoonVeil",coverage,"PressureTrace",new Color(.45f,.7f,1.3f,.24f),
                        new Vector2(2.2f,3.4f),new Vector2(1.1f,1.8f),Vector3.zero,3,.55f,true);
                }
                else
                {
                    Particle(go.transform,"SunShafts",coverage,"PressureTrace",new Color(2.2f,1.6f,.65f,.55f),
                        new Vector2(1.6f,2.4f),new Vector2(2.8f,3.8f),Vector3.zero,21,.9f,true);
                    Particle(go.transform,"SunDust",coverage,"SoilDust",new Color(2.8f,2.15f,1.1f,.95f),
                        new Vector2(.06f,.11f),new Vector2(.06f,.11f),new Vector3(.08f,.22f,-.025f),35,.15f,false);
                }
                if (!PrefabUtility.SaveAsPrefabAsset(go,Root+"/Prefabs/"+name+".prefab"))
                    throw new InvalidOperationException("visibility_domain_save:"+name);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void Particle(Transform parent,string name,Mesh coverage,string sourceMaterial,Color tint,
            Vector2 width,Vector2 height,Vector3 velocity,float rate,float lift,bool vertical)
        {
            var source=AssetDatabase.LoadAssetAtPath<Material>(Earth+"/Materials/"+sourceMaterial+".mat");
            if (!source) throw new InvalidOperationException("visibility_particle_material:"+sourceMaterial);
            var material=new Material(source); material.SetColor("_BaseColor",tint); material.renderQueue=3020;
            material=Save(material,"Materials/"+name+".mat");
            var go=new GameObject(name); go.transform.SetParent(parent,false); go.transform.localPosition=Vector3.up*lift;
            var ps=go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main; main.loop=true; main.duration=6; main.prewarm=true; main.playOnAwake=false;
            main.startLifetime=new ParticleSystem.MinMaxCurve(3.5f,5.5f); main.startSpeed=0;
            main.startSize3D=true; main.startSizeX=new ParticleSystem.MinMaxCurve(width.x,width.y);
            main.startSizeY=new ParticleSystem.MinMaxCurve(height.x,height.y); main.startSizeZ=1;
            main.startColor=Color.white; main.maxParticles=180; main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            var emission=ps.emission; emission.rateOverTime=rate;
            var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Mesh; shape.mesh=coverage;
            shape.meshShapeType=ParticleSystemMeshShapeType.Triangle; shape.alignToDirection=false;
            var motion=ps.velocityOverLifetime; motion.enabled=true; motion.space=ParticleSystemSimulationSpace.World;
            motion.x=velocity.x; motion.y=velocity.y; motion.z=velocity.z;
            var colour=ps.colorOverLifetime; colour.enabled=true;
            var gradient=new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1) },
                new[] { new GradientAlphaKey(0,0),new GradientAlphaKey(1,.22f),new GradientAlphaKey(.7f,.65f),new GradientAlphaKey(0,1) });
            colour.color=gradient;
            var renderer=go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=material;
            renderer.renderMode=vertical ? ParticleSystemRenderMode.VerticalBillboard : ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            renderer.maxParticleSize=1;
            ps.useAutoRandomSeed=false; ps.randomSeed=(uint)(1729+parent.childCount);
        }

        private static T Save<T>(T value,string relative) where T : UnityEngine.Object
        {
            string path=Root+"/"+relative; value.name=System.IO.Path.GetFileNameWithoutExtension(relative);
            var existing=AssetDatabase.LoadAssetAtPath<T>(path);
            if (!existing) { AssetDatabase.CreateAsset(value,path); return value; }
            EditorUtility.CopySerialized(value,existing); UnityEngine.Object.DestroyImmediate(value);
            EditorUtility.SetDirty(existing); return existing;
        }
    }
}
