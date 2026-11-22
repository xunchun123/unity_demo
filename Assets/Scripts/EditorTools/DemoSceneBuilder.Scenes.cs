using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using StarVortex.Core;
using StarVortex.Gameplay;
using StarVortex.UI;
using TMPro;

namespace StarVortex.EditorTools
{
    /// <summary>构建器共享件：字体字符表、Bloom Volume、音效绑定、视差背景（勇者远征版）。</summary>
    public static partial class DemoSceneBuilder
    {
        // 游戏内全部中文字符集：用于烘焙 TMP SDF 字体图集（静态化后打包可用，不依赖系统字体）
        private const string ChineseChars =
            "·×—…★、。《》「」『』一三上下不与专丝两个中串丸为主举久义也了事于云互五亡交产亮人仅从代以们件任会传伤但位低住体何余作你佳例供依保修俯倍值假做停像允充先免兜入全共关其兼内册再写冰冲决冷冻减出击刀刃分切列则创初判利到制刷刺刻前剑剩副割劈力功加动勇勤包化区升单占卡卫即却卸压去参双反发取受变叠口句只召可史右号各合同名后向否含启吸周呼命咚品响唤喂嗜器四回围圈土在地场坍坑块型域塌填士处复外多大天太失头奏契套奥如始字存它安完定实害家容宽对导射将小少尖就尾局层居屏展属巡左巨己已布带帧常幂幕平并幸广序应底度座延建开式弓引张弱弹强归当录形影征很律得御必快态性怪总恢息意感慢戏成或战所手才打扣执扩扫扳找技把投抢抽拥择拼拾持挂按挥捕换据捷掉排接推描提搭摆撞撤播操攒支收改攻放效敌教散数整文斗斩斯新方施旋无旧早时明易星是显晚普景暂暗暴更替最有望期未末本术机杀权杖条来板构林枚果枝枪架染查标栏树根格框案森模横次止正此步武死殊残段每比气永池没沿法波注洗洞活流浮消涌涡淡深混清渊游溅滑滚漂激火灰灵灼炸点烈烧烬焦焰然照燃爆爽片版牌物特状狙独率王玩环现球理生用由甲画界留疼疾痛白百的盖盘目直看真眼着瞬矢知石矿码硬确碎碰示离私种秒秘称移程空穿立符第等策筝签简算管箭类精累约级纪纯纳线组细终经绑结给统继绪续缀缓编缩缺罗置群老者而耗职背胜能脉脚自致舞航色节范草荡莱获菜落蓄蓝薄藏蝠血行补表袋被裂装要覆规视觉角解触计订认训记许设证试话该请读调象负责败费资赋走赶起超越跑距跟跨跳踏踩蹦身转轮载辑输边达过运近返还这进远连迟述追退送选透通速造逻遍遗避部都配醒里重量金钟钮链锁锋键长门闪闭闯闲间阅阱防阵阶附降限除险陷随隐隔难集雨雷需震霜静非面音顶项顺须预频颗题颜风馈首马驱驻验骨高魔鸣默鼠齐！（），：；？";

        private static TMP_FontAsset _cjkFont;

        /// <summary>
        /// Bloom 后处理 Volume（HDR 精灵 + 强度超 1 的光，全靠它发光）。
        /// "深色场地上只有真正过曝（HDR&gt;1）的东西才发光"——threshold ≥0.9，非 HDR 底色不糊。
        /// </summary>
        private static void CreateBloomVolume()
        {
            var volumeGo = new GameObject("PostVolume");
            volumeGo.transform.position = Vector3.zero;
            var volume = volumeGo.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;

            var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            AssetDatabase.CreateAsset(profile, "Assets/Settings/PostVolume.asset");

            var bloom = profile.Add<Bloom>();
            bloom.active = true;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 1.45f;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.9f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.8f;

            var vignette = profile.Add<Vignette>();
            vignette.active = true;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.35f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.55f;

            var colorAdj = profile.Add<ColorAdjustments>();
            colorAdj.active = true;
            colorAdj.postExposure.overrideState = true;
            colorAdj.postExposure.value = 0.05f;
            colorAdj.saturation.overrideState = true;
            colorAdj.saturation.value = 12f;
            colorAdj.contrast.overrideState = true;
            colorAdj.contrast.value = 6f;

            volume.sharedProfile = profile;
            EditorUtility.SetDirty(profile); // CreateAsset 之后的改动词同样要标脏才落盘
            AssetDatabase.SaveAssets();
        }

        /// <summary>音效绑定：程序化 WAV 按枚举顺序接线 + 环境音乐。</summary>
        private static void BindAudio(AudioService audio)
        {
            var clips = new List<AudioClip>
            {
                Sfx("sfx_shoot"), Sfx("sfx_hit"), Sfx("sfx_crit"), Sfx("sfx_kill"),
                Sfx("sfx_elitekill"), Sfx("sfx_pickup"), Sfx("sfx_levelup"), Sfx("sfx_hurt"),
                Sfx("sfx_gameover"), Sfx("sfx_click"),
            };
            var music = Sfx("music_ambient");
            audio.BindClips(clips, music);
        }
    }
}
