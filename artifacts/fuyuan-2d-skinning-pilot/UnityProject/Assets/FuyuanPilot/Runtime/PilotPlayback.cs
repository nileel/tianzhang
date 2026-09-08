using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace FuyuanPilot
{
    public sealed class PilotPlayback : MonoBehaviour
    {
        public GameObject actor;
        public Camera closeCamera;
        public Camera tacticalCamera;
        public SpriteSkin[] skins;
        public Transform[] bones;
        public bool showMesh;
        public float captureSeconds=10.6f;
        string captureDirectory;
        int frame;
        Animator animator;
        Vector3 footNear,footFar;
        readonly List<FrameEvidence> records=new List<FrameEvidence>();
        readonly Dictionary<string,Vector3[]> initialVertices=new Dictionary<string,Vector3[]>();
        readonly Dictionary<string,int[]> visibleTriangles=new Dictionary<string,int[]>();
        readonly Dictionary<string,float[]> initialAreas=new Dictionary<string,float[]>();
        Material lines;
        bool recording;
        GUIStyle title,small;

        void Start()
        {
            animator=actor.GetComponent<Animator>();
            footNear=bones[15].position;footFar=bones[16].position;
            string[] args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length-1;i++)if(args[i]=="--capture")captureDirectory=args[i+1];
            recording=!string.IsNullOrEmpty(captureDirectory);
            if(recording){Directory.CreateDirectory(captureDirectory);Time.captureFramerate=24;}
            QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
            lines=new Material(Shader.Find("Hidden/Internal-Colored"));
            lines.hideFlags=HideFlags.HideAndDontSave;
            lines.SetInt("_ZWrite",0);lines.SetInt("_Cull",0);lines.SetInt("_ZTest",8);
            StartCoroutine(Capture());
        }

        IEnumerator Capture()
        {
            while(true)
            {
                yield return new WaitForEndOfFrame();
                if(!recording)continue;
                var evidence=Measure();records.Add(evidence);
                var texture=ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(captureDirectory,"frame_"+frame.ToString("D4")+".png"),texture.EncodeToPNG());
                Destroy(texture);
                frame++;
                if(frame>=Mathf.CeilToInt(captureSeconds*24))
                {
                    File.WriteAllText(Path.Combine(captureDirectory,"runtime-evidence.json"),JsonUtility.ToJson(new EvidenceFile {
                        unityVersion=Application.unityVersion,description="Unity Player end-of-frame screenshots. Animator drives bone curves; official SpriteSkin deforming vertices.",frames=records.ToArray()},true));
                    Debug.Log("PILOT_CAPTURE_DONE frames="+frame+" skins="+skins.Length);
                    Application.Quit();yield break;
                }
            }
        }

        FrameEvidence Measure()
        {
            var state=animator.GetCurrentAnimatorStateInfo(0);
            var result=new FrameEvidence {frame=frame,time=frame/24f,state=state.IsName("Cast")?"Cast":"Idle",normalizedTime=state.normalizedTime,
                footDrift=Mathf.Max(Vector3.Distance(footNear,bones[15].position),Vector3.Distance(footFar,bones[16].position)),
                nearShoulder=actor.transform.InverseTransformPoint(bones[5].position),nearElbow=actor.transform.InverseTransformPoint(bones[6].position),nearWrist=actor.transform.InverseTransformPoint(bones[7].position),
                cameraPosition=tacticalCamera.transform.position,cameraEuler=tacticalCamera.transform.eulerAngles,orthoSize=tacticalCamera.orthographicSize,
                screen=new Vector2(Screen.width,Screen.height),actorPosition=actor.transform.position,actorRotation=actor.transform.eulerAngles};
            var stats=new List<SkinEvidence>();
            Vector2 min=Vector2.one*float.MaxValue,max=Vector2.one*float.MinValue;
            foreach(var skin in skins)
            {
                bool current=skin.HasCurrentDeformedVertices();
                if(!current){stats.Add(new SkinEvidence{name=skin.name,ready=false});continue;}
                var vertices=skin.GetDeformedVertexPositionData().ToArray();
                var sprite=skin.GetComponent<SpriteRenderer>().sprite;
                if(!initialVertices.ContainsKey(skin.name))
                {
                    initialVertices[skin.name]=vertices;
                    var triangles=sprite.triangles.Select(v=>(int)v).ToArray();
                    var uv=sprite.uv;var texture=sprite.texture;
                    var selected=new List<int>();var areas=new List<float>();
                    for(int t=0;t<triangles.Length;t+=3)
                    {
                        var center=(uv[triangles[t]]+uv[triangles[t+1]]+uv[triangles[t+2]])/3f;
                        if(texture.GetPixelBilinear(center.x,center.y).a<.5f)continue;
                        selected.Add(triangles[t]);selected.Add(triangles[t+1]);selected.Add(triangles[t+2]);
                        areas.Add(Area(vertices[triangles[t]],vertices[triangles[t+1]],vertices[triangles[t+2]]));
                    }
                    visibleTriangles[skin.name]=selected.ToArray();initialAreas[skin.name]=areas.ToArray();
                }
                float displacement=0,minRatio=float.MaxValue,maxRatio=0;int inverted=0;
                for(int i=0;i<vertices.Length;i++)displacement=Mathf.Max(displacement,Vector3.Distance(vertices[i],initialVertices[skin.name][i]));
                int[] indices=visibleTriangles[skin.name];
                for(int t=0;t<indices.Length;t+=3)
                {
                    float ratio=Area(vertices[indices[t]],vertices[indices[t+1]],vertices[indices[t+2]])/initialAreas[skin.name][t/3];
                    if(ratio<0)inverted++;
                    minRatio=Mathf.Min(minRatio,ratio);maxRatio=Mathf.Max(maxRatio,ratio);
                    for(int j=0;j<3;j++){Vector3 s=tacticalCamera.WorldToScreenPoint(skin.transform.TransformPoint(vertices[indices[t+j]]));min=Vector2.Min(min,s);max=Vector2.Max(max,s);}
                }
                stats.Add(new SkinEvidence{name=skin.name,ready=true,vertices=vertices.Length,maxVertexDisplacement=displacement,invertedVisibleTriangles=inverted,minAreaRatio=minRatio,maxAreaRatio=maxRatio});
            }
            result.skins=stats.ToArray();result.projectedMin=min;result.projectedMax=max;result.projectedSize=max-min;
            return result;
        }
        static float Area(Vector3 a,Vector3 b,Vector3 c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);

        void OnGUI()
        {
            if(title==null){title=new GUIStyle(GUI.skin.label){fontSize=24,fontStyle=FontStyle.Bold};small=new GUIStyle(GUI.skin.label){fontSize=17};title.normal.textColor=new Color(.86f,.88f,.87f);small.normal.textColor=new Color(.74f,.79f,.79f);}
            float split=Screen.width*.55f;
            GUI.Label(new Rect(24,18,split-40,38),"FU YUAN  /  2D SKINNING PILOT",title);
            var state=animator==null?default:animator.GetCurrentAnimatorStateInfo(0);
            string phase="IDLE / breathing";
            if(state.IsName("Cast")){float t=(state.normalizedTime%1)*3.8f;phase=t<.55f?"CAST / charge":t<1.10f?"CAST / raise arm":t<1.50f?"CAST / forward sweep":t<2.2f?"CAST / hold":"CAST / return to idle";}
            GUI.Label(new Rect(24,58,split-40,32),"DIRECTION 1   |   "+phase,small);
            GUI.Label(new Rect(split+24,18,Screen.width-split-36,38),"TACTICAL CAMERA  /  NATIVE SCALE",title);
            GUI.Label(new Rect(split+24,59,Screen.width-split-30,62),"38 deg  /  orthographic 6.2  /  1920 x 1080\nSame actor, same frame, same viewing direction",small);
            GUI.Label(new Rect(24,Screen.height-94,split-40,28),"Official Unity 2D Animation 13.0.5  |  11 layers / 17 bones",small);
            if(GUI.Button(new Rect(24,Screen.height-52,160,32),animator!=null&&animator.speed==0?"Resume":"Pause"))animator.speed=animator.speed==0?1:0;
            if(GUI.Button(new Rect(198,Screen.height-52,160,32),"Restart cast"))animator.Play("Cast",0,0);
            showMesh=GUI.Toggle(new Rect(374,Screen.height-47,200,32),showMesh,"Show sleeve mesh");
            GUI.Label(new Rect(split+24,Screen.height-96,Screen.width-split-40,80),"No effects. Feet fixed.\nSingle direction only. Visual quality remains for review.",small);
        }

        void OnRenderObject()
        {
            if(!showMesh||Camera.current!=closeCamera||lines==null)return;
            lines.SetPass(0);GL.PushMatrix();GL.MultMatrix(actor.transform.localToWorldMatrix);GL.Begin(GL.LINES);GL.Color(new Color(.32f,.94f,.71f,.8f));
            foreach(var skin in skins.Where(s=>s.name=="sleeve_near"||s.name=="sleeve_far"))
            {
                if(!skin.HasCurrentDeformedVertices())continue;
                var v=skin.GetDeformedVertexPositionData().ToArray();var ix=skin.GetComponent<SpriteRenderer>().sprite.triangles;
                for(int t=0;t<ix.Length;t+=3)for(int j=0;j<3;j++){GL.Vertex(v[ix[t+j]]);GL.Vertex(v[ix[t+(j+1)%3]]);}
            }
            GL.End();GL.PopMatrix();
        }

        [Serializable] public class EvidenceFile {public string unityVersion,description;public FrameEvidence[] frames;}
        [Serializable] public class FrameEvidence {public int frame;public float time,normalizedTime,footDrift,orthoSize;public string state;public Vector2 screen,projectedMin,projectedMax,projectedSize;public Vector3 actorPosition,actorRotation,nearShoulder,nearElbow,nearWrist,cameraPosition,cameraEuler;public SkinEvidence[] skins;}
        [Serializable] public class SkinEvidence {public string name;public bool ready;public int vertices,invertedVisibleTriangles;public float maxVertexDisplacement,minAreaRatio,maxAreaRatio;}
    }
}
