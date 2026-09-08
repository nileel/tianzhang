using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FuyuanPilot.Editor
{
    public static class PilotAnimationAuthor
    {
        public static string BonePath(int i) => PilotRigBuilder.Parents[i]<0 ? PilotRigBuilder.Names[i] : BonePath(PilotRigBuilder.Parents[i])+"/"+PilotRigBuilder.Names[i];
        public static RuntimeAnimatorController CreateController()
        {
            var idle = NewClip("Idle_Breath",3f,true);
            Curve(idle,1,"m_LocalPosition.y",new[]{0f,.75f,1.5f,2.25f,3f},new[]{.78f,.784f,.786f,.783f,.78f});
            Curve(idle,3,"localEulerAnglesRaw.z",new[]{0f,1.5f,3f},new[]{0f,.35f,0f});
            Curve(idle,4,"localEulerAnglesRaw.z",new[]{0f,1f,2f,3f},new[]{0f,-1.2f,.8f,0f});
            Curve(idle,8,"m_LocalPosition.x",new[]{0f,1f,2f,3f},new[]{.16f,.163f,.158f,.16f});
            Curve(idle,9,"m_LocalPosition.y",new[]{0f,1f,2f,3f},new[]{.61f,.614f,.608f,.61f});
            Curve(idle,13,"m_LocalPosition.y",new[]{0f,1f,2f,3f},new[]{.61f,.612f,.608f,.61f});
            var cast = NewClip("Cast_Charge_Raise_Sweep_Hold_Return",3.8f,false);
            float[] t={0,.45f,1f,1.35f,1.75f,2.15f,3.1f,3.8f};
            Curve(cast,5,"localEulerAnglesRaw.z",t,new[]{0f,18f,-160f,-112f,-112f,-106f,-35f,0f});
            Curve(cast,6,"localEulerAnglesRaw.z",t,new[]{0f,-22f,32f,-8f,-8f,-3f,8f,0f});
            Curve(cast,7,"localEulerAnglesRaw.z",t,new[]{0f,0f,-5f,8f,8f,6f,0f,0f});
            Curve(cast,10,"localEulerAnglesRaw.z",t,new[]{0f,-6f,23f,18f,18f,12f,3f,0f});
            Curve(cast,11,"localEulerAnglesRaw.z",t,new[]{0f,12f,12f,5f,5f,4f,0f,0f});
            Curve(cast,1,"m_LocalPosition.y",t,new[]{.78f,.77f,.786f,.78f,.78f,.783f,.777f,.78f});
            Curve(cast,2,"localEulerAnglesRaw.z",t,new[]{0f,-2f,1.5f,3f,3f,2f,.3f,0f});
            Curve(cast,3,"localEulerAnglesRaw.z",t,new[]{0f,2f,-1.5f,-3f,-3f,-2f,-.3f,0f});
            float[] lag={0,.55f,1.15f,1.58f,1.95f,2.35f,3.2f,3.8f};
            Curve(cast,8,"m_LocalPosition.x",lag,new[]{.16f,.20f,.035f,-.10f,-.11f,-.06f,.12f,.16f});
            Curve(cast,8,"m_LocalPosition.y",lag,new[]{.60f,.61f,.92f,.73f,.72f,.70f,.59f,.60f});
            Curve(cast,8,"localEulerAnglesRaw.z",lag,new[]{0f,5f,-15f,-32f,-28f,-25f,-4f,0f});
            Curve(cast,9,"m_LocalPosition.x",lag,new[]{.30f,.34f,-.08f,-.22f,-.20f,-.16f,.25f,.30f});
            Curve(cast,9,"m_LocalPosition.y",lag,new[]{.61f,.65f,1.02f,.86f,.83f,.82f,.61f,.61f});
            Curve(cast,9,"localEulerAnglesRaw.z",lag,new[]{0f,5f,-35f,-65f,-60f,-50f,-10f,0f});
            Curve(cast,13,"m_LocalPosition.x",lag,new[]{-.12f,-.13f,-.06f,-.04f,-.05f,-.06f,-.11f,-.12f});
            Curve(cast,13,"m_LocalPosition.y",lag,new[]{.61f,.59f,.65f,.63f,.62f,.62f,.60f,.61f});
            Curve(cast,4,"localEulerAnglesRaw.z",lag,new[]{0f,2f,-4f,-6f,-3f,-2f,1f,0f});
            Curve(cast,14,"localEulerAnglesRaw.z",lag,new[]{0f,.2f,-.7f,.6f,.3f,.4f,-.2f,0f});
            BakeSleeveFollow(cast);
            AssetDatabase.CreateAsset(idle,PilotRigBuilder.Folder+"/Animation/Idle.anim");
            AssetDatabase.CreateAsset(cast,PilotRigBuilder.Folder+"/Animation/Cast.anim");
            string path=PilotRigBuilder.Folder+"/Animation/Fuyuan.controller";
            var controller=new AnimatorController {name="Fuyuan"};
            AssetDatabase.CreateAsset(controller,path);
            controller.AddLayer("Base Layer");
            var sm=controller.layers[0].stateMachine;
            var idleState=sm.AddState("Idle"); idleState.motion=idle;
            var castState=sm.AddState("Cast"); castState.motion=cast;
            sm.defaultState=idleState;
            Connect(idleState,castState);Connect(castState,idleState);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        // Diagnostic retune: remove the competing world-space belly trajectory.
        // Standard keyframes only; shoulder/elbow/wrist rotations and source art stay unchanged.
        static void BakeSleeveFollow(AnimationClip clip)
        {
            var root=new GameObject("TemporaryCurveSamplingRig");
            var bones=new Transform[PilotRigBuilder.Names.Length];
            for(int i=0;i<bones.Length;i++)
            {
                bones[i]=new GameObject(PilotRigBuilder.Names[i]).transform;
                bones[i].SetParent(PilotRigBuilder.Parents[i]<0?root.transform:bones[PilotRigBuilder.Parents[i]],false);
                bones[i].localPosition=PilotRigBuilder.Parents[i]<0?PilotRigBuilder.Rest[i]:PilotRigBuilder.Rest[i]-PilotRigBuilder.Rest[PilotRigBuilder.Parents[i]];
            }
            int[] targets={8,9,13};int[] shoulders={5,5,10};
            var times=new float[93];var xs=new float[3,93];var ys=new float[3,93];var zs=new float[3,93];
            for(int f=0;f<93;f++)
            {
                times[f]=3.8f*f/92f;
                clip.SampleAnimation(root,Mathf.Max(0,times[f]-.025f));
                for(int j=0;j<targets.Length;j++)
                {
                    Vector2 delta=PilotRigBuilder.Rest[targets[j]]-PilotRigBuilder.Rest[shoulders[j]];
                    Vector3 p=bones[shoulders[j]].TransformPoint(delta);
                    xs[j,f]=p.x;ys[j,f]=p.y;zs[j,f]=Mathf.DeltaAngle(0,bones[shoulders[j]].eulerAngles.z);
                }
            }
            for(int j=0;j<targets.Length;j++)
            {
                var x=new float[93];var y=new float[93];var z=new float[93];
                for(int f=0;f<93;f++){x[f]=xs[j,f];y[f]=ys[j,f];z[f]=zs[j,f];}
                x[92]=PilotRigBuilder.Rest[targets[j]].x;y[92]=PilotRigBuilder.Rest[targets[j]].y;z[92]=0;
                Curve(clip,targets[j],"m_LocalPosition.x",times,x);
                Curve(clip,targets[j],"m_LocalPosition.y",times,y);
                Curve(clip,targets[j],"localEulerAnglesRaw.z",times,z);
            }
            UnityEngine.Object.DestroyImmediate(root);
        }

        static AnimationClip NewClip(string name,float duration,bool loop)
        {
            var clip=new AnimationClip {name=name,frameRate=24};
            for(int i=0;i<PilotRigBuilder.Names.Length;i++)
            {
                Vector2 p=PilotRigBuilder.Parents[i]<0?PilotRigBuilder.Rest[i]:PilotRigBuilder.Rest[i]-PilotRigBuilder.Rest[PilotRigBuilder.Parents[i]];
                Curve(clip,i,"m_LocalPosition.x",new[]{0f,duration},new[]{p.x,p.x});
                Curve(clip,i,"m_LocalPosition.y",new[]{0f,duration},new[]{p.y,p.y});
                Curve(clip,i,"localEulerAnglesRaw.z",new[]{0f,duration},new[]{0f,0f});
            }
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=false;
            AnimationUtility.SetAnimationClipSettings(clip,settings);
            return clip;
        }
        static void Curve(AnimationClip clip,int bone,string property,float[] times,float[] values)
        {
            var keys=new Keyframe[times.Length];
            for(int i=0;i<keys.Length;i++)keys[i]=new Keyframe(times[i],values[i],0,0);
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(BonePath(bone),typeof(Transform),property),new AnimationCurve(keys));
        }
        static void Connect(AnimatorState from,AnimatorState to)
        {
            var transition=from.AddTransition(to);transition.hasExitTime=true;transition.exitTime=1;
            transition.hasFixedDuration=true;transition.duration=.10f;transition.offset=0;
        }
    }
}
