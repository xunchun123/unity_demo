using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using StarVortex.Gameplay;

namespace StarVortex.EditorTools
{
    /// <summary>一键搭建器（动画与场景部分）：AnimatorController、粒子系统、两个场景、全套 UGUI。</summary>
    public static partial class DemoSceneBuilder
    {
        // ---------- 动画控制器 ----------

        private static RuntimeAnimatorController _playerController;
        private static readonly Dictionary<string, RuntimeAnimatorController> _enemyControllers = new();

        /// <summary>
        /// 生成动画控制器：玩家 Idle/Run（布尔参数驱动），三种敌人各一个 Walk 循环。
        /// 面试点：动画帧全部来自程序化生成的序列帧——没有外部素材也能走完整的 Animator 工作流。
        /// </summary>
        private static void CreateAnimators()
        {
            EnsureDir("Assets/Animations/Generated");

            // 玩家：Idle(4帧 6fps) / Run(4帧 12fps)，参数 Moving 切换
            _playerController = MakeController("Player_AC", clip =>
            {
                var idle = MakeClip("Player_Idle", new[] { "player_idle_0", "player_idle_1", "player_idle_2", "player_idle_3" }, 6f);
                var run = MakeClip("Player_Run", new[] { "player_idle_0", "player_idle_1", "player_idle_2", "player_idle_3" }, 12f);
                var param = new AnimatorControllerParameter { name = "Moving", type = AnimatorControllerParameterType.Bool };
                clip.parameters = new[] { param };

                var root = clip.layers[0].stateMachine;
                var idleState = root.AddState("Idle");
                idleState.motion = idle;
                var runState = root.AddState("Run");
                runState.motion = run;
                root.defaultState = idleState;

                var toRun = idleState.AddTransition(runState);
                toRun.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
                toRun.duration = 0.1f;
                var toIdle = runState.AddTransition(idleState);
                toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
                toIdle.duration = 0.1f;
            });

            // 敌人：每种一个双帧循环
            MakeEnemyController("Enemy_Drone", new[] { "enemy_drone_0", "enemy_drone_1" }, 4f);
            MakeEnemyController("Enemy_Darter", new[] { "enemy_darter_0", "enemy_darter_1" }, 7f);
            MakeEnemyController("Enemy_Brute", new[] { "enemy_brute_0", "enemy_brute_1" }, 3f);
        }

        private static void MakeEnemyController(string name, string[] frames, float fps)
        {
            _enemyControllers[name] = MakeController(name, sm =>
            {
                var walk = MakeClip($"{name}_Walk", frames, fps);
                var state = sm.layers[0].stateMachine.AddState("Walk");
                state.motion = walk;
            });
        }

        private static AnimatorController MakeController(string name, System.Action<AnimatorController> build)
        {
            string path = $"Assets/Animations/Generated/{name}.controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null) return existing;

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            build(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>
        /// 把精灵序列帧烘焙成 AnimationClip（SpriteRenderer 的 m_Sprite 引用曲线）。
        /// 循环关键点：末尾补一帧回到首帧，保证每帧等时长显示。
        /// </summary>
        private static AnimationClip MakeClip(string clipName, string[] spriteNames, float fps)
        {
            string path = $"Assets/Animations/Generated/{clipName}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) return existing;

            var clip = new AnimationClip { name = clipName };
            clip.frameRate = fps;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var binding = new EditorCurveBinding
            {
                path = "Sprite",
                type = typeof(SpriteRenderer),
                propertyName = "m_Sprite",
            };

            float frameTime = 1f / fps;
            var frames = new List<ObjectReferenceKeyframe>(spriteNames.Length + 1);
            for (int i = 0; i < spriteNames.Length; i++)
            {
                var sprite = Art(spriteNames[i]);
                if (sprite == null) Debug.LogWarning($"[Anim] 缺少精灵 {spriteNames[i]}，帧缺失");
                frames.Add(new ObjectReferenceKeyframe { time = i * frameTime, value = sprite });
            }
            // 末帧回到首帧：让最后一帧也占满一个完整帧时长
            frames.Add(new ObjectReferenceKeyframe { time = spriteNames.Length * frameTime, value = Art(spriteNames[0]) });

            AnimationUtility.SetObjectReferenceCurve(clip, binding, frames.ToArray());
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // ---------- 场景 ----------

        private static void BuildScenes()
        {
            CreateAnimators();
            BuildGameScene();
            BuildMainMenuScene();
            AssetDatabase.SaveAssets();
        }
    }
}
