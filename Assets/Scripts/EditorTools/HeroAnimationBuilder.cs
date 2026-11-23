using System.Collections.Generic;
using StarVortex.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 勇者动画构建器：像素帧 → AnimationClip → 一张基础状态机 + 每个职业一个 AnimatorOverrideController。
    /// 面试点：
    /// ① 三个职业共用同一张状态机（状态、过渡、参数只维护一份），换职业只换剪辑——
    ///    这是 AnimatorOverrideController 的标准用法，加第四个职业只需要再配一组帧；
    /// ② 攻击/受击走 Any State + 触发器，播完靠 Exit Time 回到移动态。
    ///    旧版"攻击卡在最后一帧"的根因：脚本 AddTransition/AddExitTransition 创建的过渡默认
    ///    hasExitTime = false，又没有任何条件，Unity 会直接忽略这条过渡，状态永远出不去；
    /// ③ Animator 是 sprite 的唯一写入者，HeroController 只写参数，不会出现两处同时改 sprite 的竞争。
    /// </summary>
    public static class HeroAnimationBuilder
    {
        public const string OutputDir = "Assets/Animations/Pixel";

        // 参数名：HeroController 里用 Animator.StringToHash 预哈希同名参数
        public const string ParamSpeed = "Speed";
        public const string ParamVelY = "VelY";
        public const string ParamGrounded = "Grounded";
        public const string ParamAttack = "Attack";
        public const string ParamHurt = "Hurt";
        public const string ParamDead = "Dead";

        private enum HeroState { Idle, Run, Jump, Fall, Attack, AirAttack, Hurt, Death }

        /// <summary>一个动作的配置：用哪组帧、帧率、是否循环。</summary>
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

        /// <summary>一个职业：帧目录名 + 对应武器 + 各动作配置。</summary>
        private sealed class HeroLook
        {
            public string Character;
            public WeaponKind Weapon;
            public Dictionary<HeroState, ClipSpec> Clips;
        }

        // 帧率按动作节奏定：攻击帧率要让整段动作略短于武器攻击间隔（长剑 6 帧 / 18fps ≈ 0.33s），连点时每一下都能完整播完
        private static readonly HeroLook Knight = new()
        {
            Character = "Knight",
            Weapon = WeaponKind.Sword,
            Clips = new Dictionary<HeroState, ClipSpec>
            {
                [HeroState.Idle] = new("Idle", 7f, true),
                [HeroState.Run] = new("Run", 16f, true),
                [HeroState.Jump] = new("Jump", 10f, false),
                [HeroState.Fall] = new("Fall", 8f, true),
                [HeroState.Attack] = new("Attack", 18f, false),
                [HeroState.AirAttack] = new("AirAttack", 16f, false),
                [HeroState.Hurt] = new("Hurt", 14f, false),
                [HeroState.Death] = new("Hurt", 5f, false), // 素材无死亡动作：受击帧慢放并停在最后一帧
            },
        };

        private static readonly HeroLook Hunter = new()
        {
            Character = "Hunter",
            Weapon = WeaponKind.Bow,
            Clips = new Dictionary<HeroState, ClipSpec>
            {
                [HeroState.Idle] = new("Idle", 8f, true),
                [HeroState.Run] = new("Run", 20f, true),
                [HeroState.Jump] = new("Jump", 8f, false),
                [HeroState.Fall] = new("Fall", 8f, true),
                [HeroState.Attack] = new("Attack", 14f, false),
                [HeroState.AirAttack] = new("Attack", 14f, false), // 空中射击沿用地面射击帧
                [HeroState.Hurt] = new("Hurt", 10f, false),
                [HeroState.Death] = new("Hurt", 4f, false),
            },
        };

        private static readonly HeroLook Monk = new()
        {
            Character = "Monk",
            Weapon = WeaponKind.Staff,
            Clips = new Dictionary<HeroState, ClipSpec>
            {
                [HeroState.Idle] = new("Idle", 6f, true),
                [HeroState.Run] = new("Run", 12f, true),
                [HeroState.Jump] = new("Jump", 8f, false),
                [HeroState.Fall] = new("Fall", 8f, true),
                [HeroState.Attack] = new("Attack", 16f, false),
                [HeroState.AirAttack] = new("AirAttack", 10f, false),
                [HeroState.Hurt] = new("Hurt", 10f, false),
                [HeroState.Death] = new("Hurt", 4f, false),
            },
        };

        private static readonly EditorCurveBinding SpriteBinding =
            EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        /// <summary>生成全部剪辑与控制器（长剑骑士 = 基础状态机，另外两个职业 = Override）。</summary>
        public static void BuildAll()
        {
            PixelArtPipeline.EnsureImported();
            PixelArtPipeline.EnsureFolder(OutputDir);

            var knightClips = BuildClips(Knight);
            var baseController = BuildBaseController(ControllerPath(Knight), knightClips);
            BuildOverride(ControllerPath(Hunter), baseController, knightClips, BuildClips(Hunter));
            BuildOverride(ControllerPath(Monk), baseController, knightClips, BuildClips(Monk));
            AssetDatabase.SaveAssets();
        }

        /// <summary>某把武器对应的职业控制器（BuildAll 之后可用）。</summary>
        public static RuntimeAnimatorController ControllerFor(WeaponKind weapon) =>
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath(LookFor(weapon)));

        /// <summary>预制体的初始 sprite：该职业待机动作第一帧（Animator 启动前也有正确外观）。</summary>
        public static Sprite PreviewSprite(WeaponKind weapon)
        {
            var frames = PixelArtPipeline.LoadFrames(LookFor(weapon).Character, "Idle");
            return frames.Length > 0 ? frames[0] : null;
        }

        private static HeroLook LookFor(WeaponKind weapon) => weapon switch
        {
            WeaponKind.Bow => Hunter,
            WeaponKind.Staff => Monk,
            _ => Knight,
        };

        private static string ControllerPath(HeroLook look) =>
            look == Knight
                ? $"{OutputDir}/Hero_{look.Character}.controller"
                : $"{OutputDir}/Hero_{look.Character}.overrideController";

        // ---------- 剪辑 ----------

        private static Dictionary<HeroState, AnimationClip> BuildClips(HeroLook look)
        {
            string dir = $"{OutputDir}/{look.Character}";
            PixelArtPipeline.EnsureFolder(dir);

            var clips = new Dictionary<HeroState, AnimationClip>();
            foreach (var pair in look.Clips)
            {
                Sprite[] frames = PixelArtPipeline.LoadFrames(look.Character, pair.Value.Frames);
                if (frames.Length == 0)
                {
                    Debug.LogError($"[HeroAnim] 缺少帧：{PixelArtPipeline.ArtRoot}/{look.Character}/{pair.Value.Frames}_*.png");
                    continue;
                }
                clips[pair.Key] = WriteClip($"{dir}/{look.Character}_{pair.Key}.anim", frames, pair.Value);
            }
            return clips;
        }

        /// <summary>
        /// 逐帧换 sprite 的剪辑。末尾多补一个"最后一帧"关键帧：PPtr 曲线是阶梯式的，
        /// 不补的话最后一帧的显示时长为 0，非循环动作会看起来提前收招。
        /// 已存在的剪辑原地覆盖曲线，保持资产 GUID 不变。
        /// </summary>
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

        /// <summary>
        /// 基础状态机：
        /// 移动层 Idle ⇄ Run，离地按 VelY 分 Jump / Fall，落地回 Idle 或 Run；
        /// Any State 按优先级 Death > Hurt > Attack / AirAttack，一次性动作播完经 Exit 回默认态。
        /// 像素帧动画不做混合，所有过渡时长都是 0。
        /// </summary>
        private static AnimatorController BuildBaseController(string path, Dictionary<HeroState, AnimationClip> clips)
        {
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter(ParamSpeed, AnimatorControllerParameterType.Float);
            controller.AddParameter(ParamVelY, AnimatorControllerParameterType.Float);
            controller.AddParameter(ParamGrounded, AnimatorControllerParameterType.Bool);
            controller.AddParameter(ParamAttack, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(ParamHurt, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(ParamDead, AnimatorControllerParameterType.Bool);

            var sm = controller.layers[0].stateMachine;
            var idle = AddState(sm, HeroState.Idle, clips, new Vector3(300f, 0f));
            var run = AddState(sm, HeroState.Run, clips, new Vector3(550f, 0f));
            var jump = AddState(sm, HeroState.Jump, clips, new Vector3(300f, 120f));
            var fall = AddState(sm, HeroState.Fall, clips, new Vector3(550f, 120f));
            var attack = AddState(sm, HeroState.Attack, clips, new Vector3(300f, -140f));
            var airAttack = AddState(sm, HeroState.AirAttack, clips, new Vector3(550f, -140f));
            var hurt = AddState(sm, HeroState.Hurt, clips, new Vector3(50f, -140f));
            var death = AddState(sm, HeroState.Death, clips, new Vector3(50f, 120f));
            sm.defaultState = idle;

            const float moveThreshold = 0.3f;

            // 地面移动
            Link(idle, run, When(AnimatorConditionMode.Greater, ParamSpeed, moveThreshold));
            Link(run, idle, When(AnimatorConditionMode.Less, ParamSpeed, moveThreshold));

            // 离地：上升进 Jump、下降进 Fall（走下平台边缘直接 Fall）
            foreach (var ground in new[] { idle, run })
            {
                Link(ground, jump, When(AnimatorConditionMode.IfNot, ParamGrounded), When(AnimatorConditionMode.Greater, ParamVelY, 0.1f));
                Link(ground, fall, When(AnimatorConditionMode.IfNot, ParamGrounded), When(AnimatorConditionMode.Less, ParamVelY, 0.1f));
            }
            Link(jump, fall, When(AnimatorConditionMode.Less, ParamVelY, 0f));
            Link(fall, jump, When(AnimatorConditionMode.Greater, ParamVelY, 1f)); // 二段跳

            // 落地：边跑边落地直接进 Run，避免中间闪一帧 Idle
            foreach (var air in new[] { jump, fall })
            {
                Link(air, run, When(AnimatorConditionMode.If, ParamGrounded), When(AnimatorConditionMode.Greater, ParamSpeed, moveThreshold));
                Link(air, idle, When(AnimatorConditionMode.If, ParamGrounded));
            }

            // Any State：添加顺序就是判定优先级
            AnyState(sm, death, false, When(AnimatorConditionMode.If, ParamDead));
            AnyState(sm, hurt, true, When(AnimatorConditionMode.If, ParamHurt), When(AnimatorConditionMode.IfNot, ParamDead));
            // 允许打断自身：连击每一下都从第一帧重新挥
            AnyState(sm, attack, true, When(AnimatorConditionMode.If, ParamAttack), When(AnimatorConditionMode.If, ParamGrounded),
                When(AnimatorConditionMode.IfNot, ParamDead));
            AnyState(sm, airAttack, true, When(AnimatorConditionMode.If, ParamAttack), When(AnimatorConditionMode.IfNot, ParamGrounded),
                When(AnimatorConditionMode.IfNot, ParamDead));

            ExitWhenFinished(attack);
            ExitWhenFinished(airAttack);
            ExitWhenFinished(hurt);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>职业 Override：把基础状态机里骑士的每个剪辑替换成本职业同名动作的剪辑。</summary>
        private static void BuildOverride(string path, AnimatorController baseController,
            Dictionary<HeroState, AnimationClip> baseClips, Dictionary<HeroState, AnimationClip> clips)
        {
            AssetDatabase.DeleteAsset(path);
            var overrideController = new AnimatorOverrideController(baseController);
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            foreach (var pair in baseClips)
            {
                if (clips.TryGetValue(pair.Key, out var replacement))
                    overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(pair.Value, replacement));
            }
            overrideController.ApplyOverrides(overrides);
            AssetDatabase.CreateAsset(overrideController, path);
        }

        // ---------- 小工具 ----------

        private readonly struct Condition
        {
            public readonly AnimatorConditionMode Mode;
            public readonly string Parameter;
            public readonly float Threshold;

            public Condition(AnimatorConditionMode mode, string parameter, float threshold)
            {
                Mode = mode;
                Parameter = parameter;
                Threshold = threshold;
            }
        }

        private static Condition When(AnimatorConditionMode mode, string parameter, float threshold = 0f) =>
            new(mode, parameter, threshold);

        private static AnimatorState AddState(AnimatorStateMachine sm, HeroState heroState,
            Dictionary<HeroState, AnimationClip> clips, Vector3 position)
        {
            var state = sm.AddState(heroState.ToString(), position);
            if (clips.TryGetValue(heroState, out var clip)) state.motion = clip;
            return state;
        }

        /// <summary>条件过渡：无 Exit Time、无过渡时长（参数一满足就切）。</summary>
        private static void Link(AnimatorState from, AnimatorState to, params Condition[] conditions)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            foreach (var c in conditions) transition.AddCondition(c.Mode, c.Threshold, c.Parameter);
        }

        private static void AnyState(AnimatorStateMachine sm, AnimatorState to, bool canInterruptSelf,
            params Condition[] conditions)
        {
            var transition = sm.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.canTransitionToSelf = canInterruptSelf;
            foreach (var c in conditions) transition.AddCondition(c.Mode, c.Threshold, c.Parameter);
        }

        /// <summary>一次性动作：显式打开 Exit Time（播完整段）后回默认态，再由移动层条件分流。</summary>
        private static void ExitWhenFinished(AnimatorState state)
        {
            var transition = state.AddExitTransition();
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
        }
    }
}
