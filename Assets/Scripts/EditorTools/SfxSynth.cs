// ============================================================================
// 《星涡幸存者 StarVortex》纯代码音效合成器（Editor 工具）
// ----------------------------------------------------------------------------
// - 不下载、不引用任何外部音频素材，全部音效由数学合成（合成器风格）
// - 直接手写 RIFF/WAVE 字节流，输出 44100Hz / 16bit PCM / 单声道 WAV
// - 输出目录：Assets/Audio/Generated/（不存在则自动创建）
// - 菜单：StarVortex/生成音效资源（也可由 DemoSceneBuilder 一键搭建调用）
// - 幂等可重跑：噪声用固定种子的 xorshift32，重复执行生成完全一致的
//   字节并覆盖旧文件，重新导入后资源不变
// - 顺序契约：生成顺序与运行时 SfxId 枚举索引一一对应
//   （见 Core/AudioService.cs 与 DemoSceneBuilder.BindAudio），严禁调整
// ============================================================================

using System;                      // System.Math（三角 / 指数 / 幂函数），无外部依赖
using System.Collections.Generic;  // List<T>：记录已写出的资源路径
using System.IO;                   // File / Directory / Path：WAV 落盘
using UnityEditor;                 // MenuItem / AssetDatabase（仅编辑器）
using UnityEngine;                 // Application / Debug

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 纯代码合成全部音效（激光 / 爆炸 / 电子音 / 环境音乐）并输出 WAV 的编辑器工具。
    /// 文件名与顺序和运行时 SfxId 枚举索引一一对应，运行时按索引绑定加载。
    /// </summary>
    public static class SfxSynth
    {
        // ====================================================================
        // 常量
        // ====================================================================

        /// <summary>采样率：44100Hz。</summary>
        const int SampleRate = 44100;

        /// <summary>2π。</summary>
        const double TwoPi = Math.PI * 2.0;

        /// <summary>输出目录（相对 Assets 的资源路径）。</summary>
        const string OutputDir = "Assets/Audio/Generated";

        /// <summary>统一 5ms 淡入，避免起始爆音（无缝循环素材除外，见 SynthAmbientMusic）。</summary>
        const double FadeInSec = 0.005;

        // ====================================================================
        // 菜单入口
        // ====================================================================

        [MenuItem("StarVortex/生成音效资源")]
        public static void GenerateAll()
        {
            // 1. 确保输出目录存在（Assets/Audio/Generated），不存在则创建
            string absDir = Path.Combine(Application.dataPath, "Audio", "Generated");
            if (!Directory.Exists(absDir))
            {
                Directory.CreateDirectory(absDir); // 缺失的上级目录会一并创建
                AssetDatabase.Refresh();           // 让 Unity 立即感知新目录
            }

            // 2. 依次合成并写出 WAV。
            //    顺序契约：与运行时 SfxId { Shoot, Hit, CritHit, Kill, EliteKill,
            //    Pickup, LevelUp, PlayerHurt, GameOver, UiClick } 索引严格一一对应
            //    （索引 0~9 为一次性音效，索引 10 为循环环境音乐），严禁调整顺序。
            //    WriteClip 参数：文件名 / 采样数组 / 目标峰值 / 结尾淡出秒数 / 是否无缝循环
            List<string> written = new List<string>
            {
                WriteClip("sfx_shoot.wav",     SynthShoot(),        0.65, 0.010, false), // 0  Shoot     激光 zap
                WriteClip("sfx_hit.wav",       SynthHit(),          0.60, 0.008, false), // 1  Hit       命中 click
                WriteClip("sfx_crit.wav",      SynthCrit(),         0.80, 0.010, false), // 2  CritHit   暴击
                WriteClip("sfx_kill.wav",      SynthKill(),         0.85, 0.020, false), // 3  Kill      小爆炸
                WriteClip("sfx_elitekill.wav", SynthEliteKill(),    0.90, 0.030, false), // 4  EliteKill 大爆炸
                WriteClip("sfx_pickup.wav",    SynthPickup(),       0.60, 0.010, false), // 5  Pickup    拾取 blip
                WriteClip("sfx_levelup.wav",   SynthLevelUp(),      0.75, 0.120, false), // 6  LevelUp   升级琶音
                WriteClip("sfx_hurt.wav",      SynthHurt(),         0.80, 0.020, false), // 7  PlayerHurt 玩家受伤
                WriteClip("sfx_gameover.wav",  SynthGameOver(),     0.70, 0.150, false), // 8  GameOver  结算
                WriteClip("sfx_click.wav",     SynthClick(),        0.50, 0.006, false), // 9  UiClick   UI 点击
                WriteClip("music_ambient.wav", SynthAmbientMusic(), 0.55, 0.000, true),  // 10 环境音乐（无缝循环，不做首尾淡入淡出）
            };

            // 3. 逐个强制导入，确保资源立即可被 LoadAssetAtPath 引用
            foreach (string assetPath in written)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }

            // 4. 保存并刷新资源数据库
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int count = written.Count;
            Debug.Log($"[SfxSynth] 合成 {count} 个音频 → {OutputDir}");
        }

        // ====================================================================
        // 落盘管线：后处理 → 编码 → 写文件
        // ====================================================================

        /// <summary>
        /// 音频后处理（5ms 淡入 / 结尾淡出 / 峰值归一防 clip），
        /// 编码为 16bit PCM WAV 后写盘。返回资源相对路径（供 ImportAsset 使用）。
        /// </summary>
        static string WriteClip(string fileName, float[] samples, double targetPeak, double fadeOutSec, bool seamlessLoop)
        {
            if (!seamlessLoop)
            {
                ApplyFadeIn(samples, FadeInSec);                         // 5ms 淡入避免起始爆音
                if (fadeOutSec > 0.0) ApplyFadeOut(samples, fadeOutSec); // 自然 release，结尾精确归零防咔哒
            }
            Normalize(samples, targetPeak); // 目标峰值均 <= 0.90 < 0.95，硬性防 clip

            byte[] wav = BuildWav16BitMono(samples, SampleRate);
            string absPath = Path.Combine(Application.dataPath, "Audio", "Generated", fileName);
            File.WriteAllBytes(absPath, wav); // 幂等：直接覆盖旧文件
            return OutputDir + "/" + fileName;
        }

        // ====================================================================
        // WAV 编码（手写 RIFF/WAVE 字节流：44 字节标准头 + 16bit PCM 数据）
        // ====================================================================

        /// <summary>float 采样数组 → int16 PCM → byte[]（RIFF/WAVE 44 字节标准头，单声道，小端）。</summary>
        static byte[] BuildWav16BitMono(float[] samples, int sampleRate)
        {
            int dataLen = samples.Length * 2;          // 16bit = 每样本 2 字节
            byte[] wav = new byte[44 + dataLen];

            // --- RIFF 文件头 ---
            wav[0] = (byte)'R'; wav[1] = (byte)'I'; wav[2] = (byte)'F'; wav[3] = (byte)'F';
            PutU32(wav, 4, (uint)(36 + dataLen));      // RIFF 块大小 = 文件总长 - 8
            wav[8] = (byte)'W'; wav[9] = (byte)'A'; wav[10] = (byte)'V'; wav[11] = (byte)'E';

            // --- fmt 子块 ---
            wav[12] = (byte)'f'; wav[13] = (byte)'m'; wav[14] = (byte)'t'; wav[15] = (byte)' ';
            PutU32(wav, 16, 16);                       // fmt 块大小（PCM 固定 16 字节）
            PutU16(wav, 20, 1);                        // 音频格式：1 = 线性 PCM
            PutU16(wav, 22, 1);                        // 声道数：1 = 单声道
            PutU32(wav, 24, (uint)sampleRate);         // 采样率
            PutU32(wav, 28, (uint)(sampleRate * 2));   // 字节率 = 采样率 × 块对齐
            PutU16(wav, 32, 2);                        // 块对齐 = 声道数 × 每样本字节数 = 2
            PutU16(wav, 34, 16);                       // 位深：16bit

            // --- data 子块 ---
            wav[36] = (byte)'d'; wav[37] = (byte)'a'; wav[38] = (byte)'t'; wav[39] = (byte)'a';
            PutU32(wav, 40, (uint)dataLen);            // 数据字节数

            // --- PCM16 采样数据（先钳制到 [-1,1] 再量化，双保险防 clip）---
            for (int i = 0; i < samples.Length; i++)
            {
                float v = samples[i];
                if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                short s16 = (short)Math.Round(v * 32767.0);
                int o = 44 + i * 2;
                wav[o] = (byte)(s16 & 0xFF);           // 低字节在前（小端）
                wav[o + 1] = (byte)((s16 >> 8) & 0xFF);
            }
            return wav;
        }

        /// <summary>写入小端 uint32。</summary>
        static void PutU32(byte[] buf, int offset, uint value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>写入小端 uint16。</summary>
        static void PutU16(byte[] buf, int offset, ushort value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        // ====================================================================
        // 通用 DSP 小工具
        // ====================================================================

        /// <summary>按秒数分配采样缓冲（四舍五入到整数样本，避免 double 截断差一）。</summary>
        static float[] Alloc(double seconds)
        {
            return new float[(int)(SampleRate * seconds + 0.5)];
        }

        /// <summary>噪声状态（xorshift32）。</summary>
        static uint _noiseState;

        /// <summary>重置噪声种子。固定种子 → 输出确定性 → 幂等可重跑。</summary>
        static void SeedNoise(uint seed)
        {
            _noiseState = seed == 0u ? 0x9E3779B9u : seed;
        }

        /// <summary>白噪声，返回 [-1, 1)。</summary>
        static float NextNoise()
        {
            _noiseState ^= _noiseState << 13;
            _noiseState ^= _noiseState >> 17;
            _noiseState ^= _noiseState << 5;
            return (float)(_noiseState / 2147483648.0 - 1.0);
        }

        /// <summary>起始 attack 斜坡：t 未达 attack 时线性上升，之后保持 1。</summary>
        static double AttackRamp(double t, double attack)
        {
            if (t <= 0.0) return 0.0;
            return t >= attack ? 1.0 : t / attack;
        }

        /// <summary>频率指数滑音：f0 → f1，在 glideTime 秒内指数插值，之后保持 f1。</summary>
        static double GlideFreq(double f0, double f1, double t, double glideTime)
        {
            double p = t / glideTime;
            if (p > 1.0) p = 1.0;
            return f0 * Math.Pow(f1 / f0, p);
        }

        /// <summary>一阶 IIR 低通滤波；state 为滤波器状态，需跨样本保持（传引用）。</summary>
        static double LowPass(double input, ref double state, double cutoffHz)
        {
            double dt = 1.0 / SampleRate;
            double alpha = dt / (dt + 1.0 / (TwoPi * cutoffHz));
            state += alpha * (input - state);
            return state;
        }

        /// <summary>开头 n 秒线性淡入（第一样本精确为 0，避免起始爆音）。</summary>
        static void ApplyFadeIn(float[] buf, double seconds)
        {
            int n = (int)(SampleRate * seconds + 0.5);
            if (n > buf.Length) n = buf.Length;
            if (n < 2) return;
            for (int i = 0; i < n; i++)
            {
                buf[i] = (float)(buf[i] * (i / (double)(n - 1)));
            }
        }

        /// <summary>结尾 n 秒线性淡出（最后样本精确为 0，避免结尾咔哒声）。</summary>
        static void ApplyFadeOut(float[] buf, double seconds)
        {
            int n = (int)(SampleRate * seconds + 0.5);
            if (n > buf.Length) n = buf.Length;
            if (n < 2) return;
            int start = buf.Length - n;
            for (int i = 0; i < n; i++)
            {
                buf[start + i] = (float)(buf[start + i] * (1.0 - i / (double)(n - 1)));
            }
        }

        /// <summary>峰值归一到 targetPeak（调用处均取 &lt; 0.95，硬性防 clip）。</summary>
        static void Normalize(float[] buf, double targetPeak)
        {
            double peak = 0.0;
            for (int i = 0; i < buf.Length; i++)
            {
                double a = Math.Abs(buf[i]);
                if (a > peak) peak = a;
            }
            if (peak < 1e-9) return;
            double gain = targetPeak / peak;
            for (int i = 0; i < buf.Length; i++)
            {
                buf[i] = (float)(buf[i] * gain);
            }
        }

        // ====================================================================
        // 音效合成（编号即运行时 SfxId 枚举索引，与 GenerateAll 调用顺序一致）
        // ====================================================================

        /// <summary>
        /// 0. 激光 zap（0.15s）：880→220Hz 指数下滑的正弦+方波混合，
        ///    5ms 快 attack，60ms 处衰减至约 10%，尾部叠加比音调衰减更慢的轻微噪声。
        ///    峰值 0.65，音量适中不刺耳。
        /// </summary>
        static float[] SynthShoot()
        {
            float[] buf = Alloc(0.15);
            SeedNoise(0x5EED0Eu);
            double phase = 0.0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)SampleRate;
                double f = GlideFreq(880.0, 220.0, t, 0.06); // 频率指数下滑（60ms 内完成）
                phase += TwoPi * f / SampleRate;             // 累积相位：频率变化时波形保持连续
                double env = AttackRamp(t, 0.005);
                double sine = Math.Sin(phase);
                double tone = (0.55 * sine + 0.35 * Math.Sign(sine)) * Math.Exp(-t * 38.0); // 正弦+方波
                double tail = 0.10 * NextNoise() * Math.Exp(-t * 22.0); // 末尾轻微噪声（衰减更慢）
                buf[i] = (float)((tone + tail) * env);
            }
            return buf;
        }

        /// <summary>
        /// 1. 命中 click（0.08s）：短噪声脉冲 + 300Hz 低频顿点，极快衰减。
        /// </summary>
        static float[] SynthHit()
        {
            float[] buf = Alloc(0.08);
            SeedNoise(0xA11CE0u);
            double phase = 0.0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)SampleRate;
                phase += TwoPi * 300.0 / SampleRate;
                double env = AttackRamp(t, 0.005);
                double thump = 0.95 * Math.Sin(phase) * Math.Exp(-t * 75.0); // 低频顿点
                double noise = 0.70 * NextNoise() * Math.Exp(-t * 95.0);     // 短噪声脉冲
                buf[i] = (float)((thump + noise) * env);
            }
            return buf;
        }

        /// <summary>
        /// 2. 暴击（0.12s）：比 hit 更亮——1200→400Hz 下滑正弦（叠加二次泛音提亮）+ 噪声，
        ///    峰值 0.80，音量比 hit 稍大。
        /// </summary>
        static float[] SynthCrit()
        {
            float[] buf = Alloc(0.12);
            SeedNoise(0xC121Eu);
            double phase = 0.0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)SampleRate;
                double f = GlideFreq(1200.0, 400.0, t, 0.10);
                phase += TwoPi * f / SampleRate;
                double env = AttackRamp(t, 0.005);
                double tone = (0.80 * Math.Sin(phase) + 0.25 * Math.Sin(2.0 * phase)) * Math.Exp(-t * 30.0);
                double noise = 0.35 * NextNoise() * Math.Exp(-t * 55.0);
                buf[i] = (float)((tone + noise) * env);
            }
            return buf;
        }

        /// <summary>
        /// 3. 小爆炸（0.35s）：120→40Hz 低频正弦下滑 + 白噪声指数衰减 + tanh 轻微失真。
        /// </summary>
        static float[] SynthKill()
        {
            float[] buf = Alloc(0.35);
            SeedNoise(0xB0B4Eu);
            double phase = 0.0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)SampleRate;
                double f = GlideFreq(120.0, 40.0, t, 0.30);
                phase += TwoPi * f / SampleRate;
                double env = AttackRamp(t, 0.005);
                double sub = Math.Sin(phase) * Math.Exp(-t * 10.0);        // 低频下坠
                double noise = NextNoise() * Math.Exp(-t * 14.0);          // 爆裂白噪声
                double crunch = Math.Tanh(1.8 * (sub + 0.70 * noise));     // 轻微失真增加冲击感
                buf[i] = (float)(crunch * env);
            }
            return buf;
        }

        /// <summary>
        /// 4. 大爆炸（0.6s）：同 kill 但更长、起始 90Hz，
        ///    附加 150Hz 低通白噪声 rumble 尾巴（衰减更慢，营造余震）。
        /// </summary>
        static float[] SynthEliteKill()
        {
            float[] buf = Alloc(0.6);
            SeedNoise(0xE12107u);
            double phase = 0.0;
            double rumbleState = 0.0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)SampleRate;
                double f = GlideFreq(90.0, 35.0, t, 0.50);
                phase += TwoPi * f / SampleRate;
                double env = AttackRamp(t, 0.005);
                double sub = Math.Sin(phase) * Math.Exp(-t * 7.5);
                double noise = NextNoise() * Math.Exp(-t * 10.0);
                double crunch = Math.Tanh(1.7 * (sub + 0.75 * noise));
                double rumble = 0.55 * LowPass(NextNoise(), ref rumbleState, 150.0) * Math.Exp(-t * 4.5);
                buf[i] = (float)((crunch + rumble) * env);
            }
            return buf;
        }

        /// <summary>
        /// 5. 拾取 blip（0.12s）：660→1320Hz 上滑正弦 + 二次泛音，清脆短促。
        /// </summary>
        static float[] SynthPickup()
        {
            float[] buf = Alloc(0.12);
            double phase = 0.0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)SampleRate;
                double f = GlideFreq(660.0, 1320.0, t, 0.09);
                phase += TwoPi * f / SampleRate;
                double env = AttackRamp(t, 0.005) * Math.Exp(-t * 30.0);
                double tone = 0.85 * Math.Sin(phase) + 0.22 * Math.Sin(2.0 * phase);
                buf[i] = (float)(tone * env);
            }
            return buf;
        }

        /// <summary>
        /// 6. 升级（0.7s）：C5-E5-G5-C6 上行琶音，每音约 120ms，正弦 + 二/三次泛音，
        ///    末音慢衰减拖尾（尾部由 WriteClip 的 120ms 淡出自然收束）。
        /// </summary>
        static float[] SynthLevelUp()
        {
            float[] buf = Alloc(0.7);
            double[] noteFreq  = { 523.25, 659.25, 783.99, 1046.50 }; // C5 E5 G5 C6
            double[] noteStart = { 0.00, 0.12, 0.24, 0.36 };
            double[] noteDecay = { 11.0, 11.0, 11.0, 4.8 };           // 末音衰减慢 → 拖尾
            double[] noteAmp   = { 0.55, 0.55, 0.55, 0.62 };
            double[] phases    = { 0.0, 0.0, 0.0, 0.0 };
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)SampleRate;
                double mix = 0.0;
                for (int k = 0; k < 4; k++)
                {
                    double age = t - noteStart[k];
                    if (age < 0.0) continue;
                    phases[k] += TwoPi * noteFreq[k] / SampleRate; // 累积相位（每音从相位 0 起振）
                    double env = AttackRamp(age, 0.010) * Math.Exp(-age * noteDecay[k]);
                    double p = phases[k];
                    mix += noteAmp[k] * (Math.Sin(p) + 0.35 * Math.Sin(2.0 * p) + 0.15 * Math.Sin(3.0 * p)) * env;
                }
                buf[i] = (float)mix;
            }
            return buf;
        }

        /// <summary>
        /// 7. 玩家受伤（0.25s）：200→60Hz 下滑方波（叠加同频正弦增厚）+ 800Hz 低通噪声，厚重。
        /// </summary>
        static float[] SynthHurt()
        {
            float[] buf = Alloc(0.25);
            SeedNoise(0x4A2710u);
            double phase = 0.0;
            double noiseState = 0.0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)SampleRate;
                double f = GlideFreq(200.0, 60.0, t, 0.22);
                phase += TwoPi * f / SampleRate;
                double env = AttackRamp(t, 0.008);
                double sine = Math.Sin(phase);
                double body = (0.50 * Math.Sign(sine) + 0.40 * sine) * Math.Exp(-t * 12.0);
                double noise = 0.30 * LowPass(NextNoise(), ref noiseState, 800.0) * Math.Exp(-t * 16.0);
                buf[i] = (float)((body + noise) * env);
            }
            return buf;
        }

        /// <summary>
        /// 8. 结算（1.2s）：A4→E4→A3 下行三音，15ms 慢 attack、慢指数衰减，
        ///    每音叠加二次泛音与低八度分量，氛围低沉（尾部由 150ms 淡出自然收束）。
        /// </summary>
        static float[] SynthGameOver()
        {
            float[] buf = Alloc(1.2);
            double[] noteFreq  = { 440.00, 329.63, 220.00 }; // A4 E4 A3
            double[] noteStart = { 0.00, 0.40, 0.80 };
            double[] noteAmp   = { 0.42, 0.42, 0.46 };
            double[] phases    = { 0.0, 0.0, 0.0 };
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)SampleRate;
                double mix = 0.0;
                for (int k = 0; k < 3; k++)
                {
                    double age = t - noteStart[k];
                    if (age < 0.0) continue;
                    phases[k] += TwoPi * noteFreq[k] / SampleRate;
                    double env = AttackRamp(age, 0.015) * Math.Exp(-age * 3.2); // 缓慢衰减
                    double p = phases[k];
                    mix += noteAmp[k] * (Math.Sin(p) + 0.22 * Math.Sin(2.0 * p) + 0.15 * Math.Sin(0.5 * p)) * env;
                }
                buf[i] = (float)mix;
            }
            return buf;
        }

        /// <summary>
        /// 9. UI 点击（0.05s）：2kHz 短 tick + 一点噪声，极快衰减。
        /// </summary>
        static float[] SynthClick()
        {
            float[] buf = Alloc(0.05);
            SeedNoise(0x71C31Du);
            double phase = 0.0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)SampleRate;
                phase += TwoPi * 2000.0 / SampleRate;
                double env = AttackRamp(t, 0.005);
                double tone = 0.85 * Math.Sin(phase) * Math.Exp(-t * 110.0);
                double noise = 0.12 * NextNoise() * Math.Exp(-t * 150.0);
                buf[i] = (float)((tone + noise) * env);
            }
            return buf;
        }

        /// <summary>
        /// 10. 环境音乐（9.6s 无缝循环）：A 小调暗黑氛围 pad——
        ///     两层正弦和弦（A2+C3+E3；第二层整体轻微正失谐，产生约 4.8s 周期的慢拍频，
        ///     音量缓慢起伏）+ 每 2 秒一次的柔和 A1(55Hz) 低音脉冲 + 轻微低通噪声风声。
        ///     无缝循环实现要点：
        ///     a) 缓冲取整数样本 44100 × 9.6 = 423360；
        ///     b) 所有持续振荡器的相位增量 = 2π × 周期数 / 总样本数（整段缓冲恰为整数个周期），
        ///        首尾相位严格连续；
        ///     c) 音量起伏 LFO 以总时长（或其整数分之一）为周期 → 首尾音量相等；
        ///     d) 低音脉冲位于 t = 0,2,4,6,8（t=10 处的脉冲即下一循环的 t=0，节奏跨循环连续），
        ///        且脉冲包络在 2s 末尾精确归零 → 循环接缝零跳变；
        ///     e) 风声包络在首尾为 0；
        ///     f) 循环素材不做首尾淡入淡出（淡入淡出反而会破坏无缝循环），爆音由上述周期性天然避免。
        /// </summary>
        static float[] SynthAmbientMusic()
        {
            const double durationSec = 9.6;
            int n = (int)(SampleRate * durationSec + 0.5); // 423360，整数样本
            float[] buf = new float[n];
            SeedNoise(0xA0E47u);

            // —— 两层正弦和弦（A2+C3+E3，A 小调）——
            // 周期数 = 频率 × 9.6 四舍五入：A2=1056、C3≈1256、E3≈1582；
            // 第二层各 +2 周期（约 +0.21Hz 失谐），与第一层形成缓慢拍频起伏
            int[] noteCycles  = { 1056, 1256, 1582, 1058, 1258, 1584 };
            double[] noteAmp  = { 0.26, 0.20, 0.17, 0.15, 0.12, 0.10 };
            int[] lfoCycles   = { 1, 2, 3, 1, 2, 3 };            // 每循环起伏次数 → 周期 9.6 / 4.8 / 3.2 秒
            double[] lfoPhase0 = { 0.0, 2.1, 4.2, 1.0, 3.1, 5.2 };
            double[] oscPhase = new double[6];
            double[] oscInc   = new double[6];                   // 相位增量：2π×周期数/总样本数
            for (int k = 0; k < 6; k++) oscInc[k] = TwoPi * noteCycles[k] / n;

            // —— 低音脉冲：A1 = 55Hz，55 × 9.6 = 528 个整周期 → 相位跨循环严格连续 ——
            double pulseInc = TwoPi * 55.0 / n;
            double pulsePhase = 0.0;
            double windState = 0.0; // 风声低通滤波器状态

            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SampleRate;

                // pad：两层正弦和弦，音量缓慢起伏
                double s = 0.0;
                for (int k = 0; k < 6; k++)
                {
                    oscPhase[k] += oscInc[k];
                    double lfo = 0.65 + 0.35 * Math.Sin(TwoPi * lfoCycles[k] * i / n + lfoPhase0[k]);
                    s += noteAmp[k] * lfo * Math.Sin(oscPhase[k]);
                }

                // 每 2 秒一次柔和低音脉冲
                double pulseAge = t % 2.0;
                double pulseEnv = AttackRamp(pulseAge, 0.008) * Math.Exp(-pulseAge * 5.0);
                if (pulseAge > 1.6) pulseEnv *= (2.0 - pulseAge) / 0.4; // 脉冲尾部精确归零 → 接缝零跳变
                pulsePhase += pulseInc;
                s += 0.42 * pulseEnv * Math.Sin(pulsePhase);

                // 轻微滤波噪声风声：每循环两次缓慢起伏，首尾幅度为 0
                double windEnv = 0.5 - 0.5 * Math.Cos(TwoPi * 2.0 * i / n);
                s += 0.30 * windEnv * LowPass(NextNoise(), ref windState, 350.0);

                buf[i] = (float)s;
            }
            return buf;
        }
    }
}
