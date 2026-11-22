using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using StarVortex.Core;

namespace StarVortex.EditorTools
{
    /// <summary>一键搭建器（粒子部分）：Juice 系统的 5 个 ParticleSystem。</summary>
    public static partial class DemoSceneBuilder
    {
        /// <summary>发光材质：自写加色 Shader（Assets/Settings/UnlitAdditive.asset 已入库）或 Sprites/Default 兜底。</summary>
        internal static Material NeonMaterial(Color color, bool additive)
        {
            EnsureDir("Assets/Materials/Generated");
            string path = $"Assets/Materials/Generated/Mat_Glow_{ColorUtility.ToHtmlStringRGBA(color)}_{(additive ? "Add" : "Norm")}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            var shader = additive
                ? (Shader.Find("StarVortex/UnlitAdditive") ?? Shader.Find("Sprites/Default"))
                : Shader.Find("Sprites/Default");
            mat = new Material(shader);
            mat.SetColor("_Color", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
        /// <summary>通用迸发粒子（死亡/命中/拾取/枪口共用参数模板，Emit 粒子由运行时逐次发射）。</summary>
        private static ParticleSystem BuildParticle(Transform parent, string name, float baseSize,
            ParticleSystemSimulationSpace simulationSpace = ParticleSystemSimulationSpace.World)
        {
            var go = new GameObject(name, typeof(ParticleSystem), typeof(ParticleSystemRenderer));
            go.transform.SetParent(parent, false);
            go.layer = 0;

            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = simulationSpace;
            main.startLifetime = 0.5f;
            main.startSpeed = 0f;               // 初速由 EmitParams.velocity 注入
            main.startSize = baseSize;
            main.startColor = Color.white;
            main.maxParticles = 512;
            main.gravityModifier = 0f;
            main.playOnAwake = false;
            main.scalingMode = ParticleSystemScalingMode.Local;

            var emission = ps.emission;
            emission.rateOverTime = 0f;         // 只用 Emit() 爆发，不做持续发射

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = grad;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.sizeMultiplier = 1f;
            var sizeCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = NeonMaterial(new Color(1.4f, 1.4f, 1.4f, 1f), additive: true);
            renderer.sortingOrder = 5;          // 粒子永远在实体之上

            return ps;
        }

        /// <summary>升级光环：环形扩散粒子（一次 Emit 24 个沿圆周爆开）。</summary>
        private static ParticleSystem BuildRingParticle(Transform parent, string name)
        {
            var ps = BuildParticle(parent, name, 0.22f, ParticleSystemSimulationSpace.World);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.5f;
            // 圆周速度：沿圆周向外
            var main = ps.main;
            main.startSpeed = 0f;
            return ps;
        }
    }
}
