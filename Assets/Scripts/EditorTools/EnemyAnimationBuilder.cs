using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 怪物动画构建器：五种怪物共用一张极简状态机，各自用 AnimatorOverrideController 换剪辑。
    /// 状态：Idle ⇄ Move（Speed 阈值）、Windup（前摇，Windup 布尔为真时保持在最后一帧）、Attack（触发器，播完回 Idle）。
    /// 面试点：
    /// ① 怪物 AI 本来就是显式状态机（蓄力/瞄准/施法/冲锋），动画只需要在 AI 换状态的那一行顺手写参数，
    ///    AI 与表现之间只有三个参数这一个接口，换素材不用碰 AI；
    /// ② 前摇用"不循环 + 停在最后一帧"表达：前摇时长由 AI 的计时器决定，动画只负责把姿势摆到位，
    ///    前摇长短调参时动画不会提前或滞后收招；
    /// ③ 素材缺的动作（幽灵没有移动帧、梦魇马没有攻击帧）在 Override 里指向已有剪辑，状态机不用为个别怪物开分支。
    /// </summary>
    public static class EnemyAnimationBuilder
    {
        public const string OutputDir = "Assets/Animations/Pixel/Enemies";

        public const string ParamSpeed = "Speed";
        public const string ParamWindup = "Windup";
        public const string ParamAttack = "Attack";

        public enum EnemyState { Idle, Move, Windup, Attack }

        private readonly struct ClipSpec
        {
            public readonly string Frames;
            public readonly float Fps;
            public readonly bool Loop;

            public ClipSpec(string frames, float fps, bool loop)
            {
                Frames = frames;
                Fps = fps;
                Loop = loop;
            }
        }

        private sealed class EnemyLook
        {
            public string Character;
            public Dictionary<EnemyState, ClipSpec> Clips;
        }

        // 帧率与 AI 计时对齐：地狱犬蓄力 0.4s = 2 帧 × 5fps；骷髅巫师施法前摇 0.6s = 6 帧 × 10fps
        private static readonly EnemyLook Hound = new()
        {
            Character = "Hound",
            Clips = new Dictionary<EnemyState, ClipSpec>
            {
                [EnemyState.Idle] = new("Idle", 8f, true),
                [EnemyState.Move] = new("Move", 14f, true),
                [EnemyState.Windup] = new("Windup", 5f, false),
                [EnemyState.Attack] = new("Attack", 12f, false),
            },
        };

        private static readonly EnemyLook Ghost = new()
        {
            Character = "Ghost",
            Clips = new Dictionary<EnemyState, ClipSpec>
            {
                [EnemyState.Idle] = new("Idle", 10f, true),
                [EnemyState.Move] = new("Idle", 10f, true),     // 飘浮怪没有单独的移动帧
                [EnemyState.Windup] = new("Windup", 10f, false), // 尖啸张臂 = 俯冲预告
                [EnemyState.Attack] = new("Windup", 10f, false),
            },
        };

        private static readonly EnemyLook Wizard = new()
        {
            Character = "Wizard",
            Clips = new Dictionary<EnemyState, ClipSpec>
            {
                [EnemyState.Idle] = new("Idle", 8f, true),
                [EnemyState.Move] = new("Idle", 8f, true),
                [EnemyState.Windup] = new("Windup", 10f, false), // 掌心聚火
                [EnemyState.Attack] = new("Attack", 12f, false), // 甩出火球
            },
        };

        private static readonly EnemyLook Nightmare = new()
        {
            Character = "Nightmare",
            Clips = new Dictionary<EnemyState, ClipSpec>
            {
                [EnemyState.Idle] = new("Idle", 6f, true),
                [EnemyState.Move] = new("Move", 10f, true),
                [EnemyState.Windup] = new("Idle", 14f, true),    // 前摇：原地躁动（加快的待机）
                [EnemyState.Attack] = new("Move", 16f, false),
            },
        };

        private static readonly EnemyLook Demon = new()
        {
            Character = "Demon",
            Clips = new Dictionary<EnemyState, ClipSpec>
            {
                [EnemyState.Idle] = new("Idle", 8f, true),
                [EnemyState.Move] = new("Idle", 8f, true),
                [EnemyState.Windup] = new("Windup", 10f, false), // 收翼蓄力
                [EnemyState.Attack] = new("Attack", 6f, false),  // 吐息，覆盖三连挥砍约 0.7s
            },
        };

        private static readonly EnemyLook[] All = { Hound, Ghost, Wizard, Nightmare, Demon };

        private static readonly EditorCurveBinding SpriteBinding =
            EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        /// <summary>生成全部怪物剪辑与控制器（地狱犬 = 基础状态机，其余 = Override）。</summary>
        public static void BuildAll()
        {
            PixelArtPipeline.EnsureImported();
            PixelArtPipeline.EnsureFolder(OutputDir);

            var baseClips = BuildClips(Hound);
            var baseController = BuildBaseController(ControllerPath(Hound), baseClips);
            foreach (var look in All)
            {
                if (look == Hound) continue;
                BuildOverride(ControllerPath(look), baseController, baseClips, BuildClips(look));
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>某种怪物的控制器（BuildAll 之后可用）。</summary>
        public static RuntimeAnimatorController ControllerFor(string character) =>
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath(Find(character)));

        /// <summary>预制体初始 sprite：待机第一帧。</summary>
        public static Sprite PreviewSprite(string character)
        {
            var frames = PixelArtPipeline.LoadFrames(character, "Idle");
            return frames.Length > 0 ? frames[0] : null;
        }

        private static EnemyLook Find(string character)
        {
            foreach (var look in All)
                if (look.Character == character) return look;
            throw new System.ArgumentException($"未配置的怪物：{character}");
        }

        private static string ControllerPath(EnemyLook look) =>
            look == Hound
                ? $"{OutputDir}/Enemy_{look.Character}.controller"
                : $"{OutputDir}/Enemy_{look.Character}.overrideController";

        // ---------- 剪辑 ----------

        private static Dictionary<EnemyState, AnimationClip> BuildClips(EnemyLook look)
        {
            string dir = $"{OutputDir}/{look.Character}";
            PixelArtPipeline.EnsureFolder(dir);

            var clips = new Dictionary<EnemyState, AnimationClip>();
            foreach (var pair in look.Clips)
            {
                Sprite[] frames = PixelArtPipeline.LoadFrames(look.Character, pair.Value.Frames);
                if (frames.Length == 0)
                {
                    Debug.LogError($"[EnemyAnim] 缺少帧：{PixelArtPipeline.ArtRoot}/{look.Character}/{pair.Value.Frames}_*.png");
                    continue;
                }
                clips[pair.Key] = WriteClip($"{dir}/{look.Character}_{pair.Key}.anim", frames, pair.Value);
            }
            return clips;
        }

        /// <summary>逐帧换 sprite 的剪辑（末尾补一个最后一帧关键帧，原因同 HeroAnimationBuilder.WriteClip）。</summary>
        private static AnimationClip WriteClip(string path, Sprite[] frames, ClipSpec spec)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.frameRate = spec.Fps;

            var keys = new ObjectReferenceKeyframe[frames.Length + 1];
            for (int i = 0; i < frames.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / spec.Fps, value = frames[i] };
            keys[frames.Length] = new ObjectReferenceKeyframe { time = frames.Length / spec.Fps, value = frames[frames.Length - 1] };
            AnimationUtility.SetObjectReferenceCurve(clip, SpriteBinding, keys);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = spec.Loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        // ---------- 状态机 ----------

        private static AnimatorController BuildBaseController(string path, Dictionary<EnemyState, AnimationClip> clips)
        {
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter(ParamSpeed, AnimatorControllerParameterType.Float);
            controller.AddParameter(ParamWindup, AnimatorControllerParameterType.Bool);
            controller.AddParameter(ParamAttack, AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;
            var idle = AddState(sm, EnemyState.Idle, clips, new Vector3(300f, 0f));
            var move = AddState(sm, EnemyState.Move, clips, new Vector3(550f, 0f));
            var windup = AddState(sm, EnemyState.Windup, clips, new Vector3(300f, -140f));
            var attack = AddState(sm, EnemyState.Attack, clips, new Vector3(550f, -140f));
            sm.defaultState = idle;

            const float moveThreshold = 0.3f;
            Link(idle, move, AnimatorConditionMode.Greater, ParamSpeed, moveThreshold);
            Link(move, idle, AnimatorConditionMode.Less, ParamSpeed, moveThreshold);
            Link(windup, idle, AnimatorConditionMode.IfNot, ParamWindup, 0f);

            // Any State 顺序即优先级：出手（Attack）压过前摇（Windup）
            var toAttack = sm.AddAnyStateTransition(attack);
            Configure(toAttack, canTransitionToSelf: true);
            toAttack.AddCondition(AnimatorConditionMode.If, 0f, ParamAttack);

            var toWindup = sm.AddAnyStateTransition(windup);
            Configure(toWindup, canTransitionToSelf: false); // 前摇期间每帧都满足条件，不能反复重进第一帧
            toWindup.AddCondition(AnimatorConditionMode.If, 0f, ParamWindup);

            var exit = attack.AddExitTransition();
            exit.hasExitTime = true; // 必须显式打开：无条件 + 无 Exit Time 的过渡会被 Unity 忽略
            exit.exitTime = 1f;
            exit.hasFixedDuration = true;
            exit.duration = 0f;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void BuildOverride(string path, AnimatorController baseController,
            Dictionary<EnemyState, AnimationClip> baseClips, Dictionary<EnemyState, AnimationClip> clips)
        {
            AssetDatabase.DeleteAsset(path);
            var overrideController = new AnimatorOverrideController(baseController);
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            foreach (var pair in baseClips)
                if (clips.TryGetValue(pair.Key, out var replacement))
                    overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(pair.Value, replacement));
            overrideController.ApplyOverrides(overrides);
            AssetDatabase.CreateAsset(overrideController, path);
        }

        private static AnimatorState AddState(AnimatorStateMachine sm, EnemyState enemyState,
            Dictionary<EnemyState, AnimationClip> clips, Vector3 position)
        {
            var state = sm.AddState(enemyState.ToString(), position);
            if (clips.TryGetValue(enemyState, out var clip)) state.motion = clip;
            return state;
        }

        private static void Link(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, string parameter, float threshold)
        {
            var transition = from.AddTransition(to);
            Configure(transition, canTransitionToSelf: false);
            transition.AddCondition(mode, threshold, parameter);
        }

        private static void Configure(AnimatorStateTransition transition, bool canTransitionToSelf)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.canTransitionToSelf = canTransitionToSelf;
        }
    }
}
