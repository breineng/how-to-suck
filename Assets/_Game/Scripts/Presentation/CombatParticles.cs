using UnityEngine;

namespace HowToSuck
{
    // Short lived world-space effects; never participate in physics or damage.
    public static class CombatParticles
    {
        private static Material material;
        public static Material Material
        {
            get
            {
                if(material!=null)return material;
                material=Resources.Load<Material>("CombatParticle");if(material!=null)return material;
                material=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                material.hideFlags=HideFlags.HideAndDontSave;
                var texture=new Texture2D(32,32,TextureFormat.RGBA32,false);
                for(int y=0;y<32;y++)for(int x=0;x<32;x++)
                {float a=Mathf.Clamp01(1-Vector2.Distance(new Vector2(x,y),new Vector2(15.5f,15.5f))/15.5f);texture.SetPixel(x,y,new Color(1,1,1,a*a));}
                texture.Apply();texture.hideFlags=HideFlags.HideAndDontSave;
                material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);
                material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);material.SetFloat("_ZWrite",0);
                material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;
                return material;
            }
        }
        public static void Burst(Vector3 position,Color color,int count=32,float speed=3,float size=.15f,float duration=.7f)
        {
            var ps=Create("Combat burst",position,duration);
            var main=ps.main;main.startColor=color;main.startSize=new ParticleSystem.MinMaxCurve(size*.35f,size);
            main.startSpeed=new ParticleSystem.MinMaxCurve(speed*.4f,speed);main.gravityModifier=.3f;
            var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.12f;
            var colorLife=ps.colorOverLifetime;colorLife.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(color,.18f)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});colorLife.color=gradient;
            ps.Emit(count);Object.Destroy(ps.gameObject,duration+1);
        }
        public static ParticleSystem Create(string name,Vector3 position,float lifetime)
        {
            var go=new GameObject(name);go.transform.position=position;var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=true;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;main.startLifetime=lifetime;main.maxParticles=240;
            var emission=ps.emission;emission.enabled=false;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=Material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();return ps;
        }
    }
}
