using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    // Range opacity and particle lifetime follow the supplied public domain state.
    public sealed class JindanVisibilityDomain
    {
        public readonly GameObject Root;
        public readonly Renderer[] Renderers;
        public readonly ParticleSystem[] Particles;
        public bool Active { get; private set; }
        public float Opacity { get; private set; }
        private readonly Color[] colours;
        private readonly MaterialPropertyBlock block=new();
        private float eventTime=-100;

        public JindanVisibilityDomain(GameObject root)
        {
            Root=root; Renderers=root.GetComponentsInChildren<Renderer>(true);
            Particles=root.GetComponentsInChildren<ParticleSystem>(true); colours=new Color[Renderers.Length];
            for (int i=0;i<Renderers.Length;i++) colours[i]=Renderers[i].sharedMaterial.GetColor("_BaseColor");
            foreach (var ps in Particles) ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            Apply(); Root.SetActive(false);
        }

        public void Set(bool active)
        {
            if (Active==active) return;
            Active=active;
            if (active)
            {
                eventTime=Time.time; Root.SetActive(true);
                foreach (var ps in Particles) ps.Play(false);
            }
            else foreach (var ps in Particles) ps.Stop(false,ParticleSystemStopBehavior.StopEmitting);
        }

        public void Tick(float seconds)
        {
            Opacity=Mathf.MoveTowards(Opacity,Active ? 1 : 0,seconds/(Active ? .8f : .55f));
            Apply();
            if (!Active && Opacity==0 && Root.activeSelf)
            {
                foreach (var ps in Particles) ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                Root.SetActive(false);
            }
        }

        private void Apply()
        {
            for (int i=0;i<Renderers.Length;i++)
            {
                block.Clear();
                if (Renderers[i] is ParticleSystemRenderer)
                { Color colour=colours[i]; colour.a*=Opacity; block.SetColor("_BaseColor",colour); }
                else { block.SetFloat("_Opacity",Opacity); block.SetFloat("_EventTime",eventTime); }
                Renderers[i].SetPropertyBlock(block);
            }
        }
    }
}
