using FMOD;
using FMODUnity;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Runtime.InteropServices; // 用于 FMOD 内存读取

namespace MCKillFeedback
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        public static bool Loaded = false;
        public static Dictionary<string, object> DefaultConfig = new Dictionary<string, object>();
        // 建议缩小一点默认尺寸，4.0对于大多数UI来说太大了
        public static Vector3 IconSizeDrop = new Vector3(2.5f, 2.5f, 1f);
        public static Vector3 IconSizeStay = new Vector3(1.5f, 1.5f, 1f);

        // 修改了默认位置，使其更靠近屏幕中心下方，便于测试观察
        public static Vector2 IconPosPrctDrop = new Vector2(0.5f, 0.4f); // 屏幕中心稍下
        public static Vector2 IconPosPrctStay = new Vector2(0.5f, 0.3f); // 再往下掉一点

        public static float icon_size_multi = 1.0f;
        public static float icon_alpha = 0.75f;
        public static float combo_seconds = 8.0f;
        public static float icon_drop_seconds = 0.15f; // 稍微增加一点掉落时间让动画更明显
        public static float icon_stay_seconds = 1.0f;
        public static float icon_fadeout_seconds = 1.0f;
        public static bool simple_sfx = false;
        public static bool disable_icon = false;
        public static bool is_dont_use_headshot_icon_if_combo = false;
        public static bool disable_headshot = false;

        public static ModBehaviour? Instance;

        public static readonly string[] IconNames = new string[]
        {
            "kill", "kill2", "kill3", "kill4", "kill5", "kill6", "kill7", "kill8",
            "headshot", "grenade_kill", "melee_kill"
        };

        public static readonly string[] AudioNames = new string[]
        {
            "kill", "kill2", "kill3", "kill4", "kill5", "kill6", "kill7", "kill8",
            "headshot", "grenade_kill", "melee_kill", "death"
        };

        public static Dictionary<string, Texture2D> KillFeedbackIcons = new Dictionary<string, Texture2D>();
        public static Dictionary<string, Sound> KillFeedbackAudios_FMOD = new Dictionary<string, Sound>();

        internal static Image? ui_image;
        internal static RectTransform? ui_transform;
        internal static CanvasGroup? ui_canvasgroup;

        public static float volume = 1.0f;
        public static float last_kill_time = -999.0f; // 初始化为一个负数，确保刚进游戏时不显示
        public static int combo_count = 0;

        private void Update()
        {
            // 如果UI不存在或未激活，不执行更新
            if (ui_canvasgroup == null || ui_transform == null || last_kill_time < 0) return;

            float delta = Time.time - last_kill_time;

            // 获取父级容器的大小以实现正确的相对定位
            RectTransform parentRect = ui_transform.parent as RectTransform;
            Vector2 parentSize = parentRect != null ? parentRect.rect.size : new Vector2(Screen.width, Screen.height);

            if (delta < icon_drop_seconds)
            {
                // 阶段1：掉落/出现
                float t = Math.Clamp(delta / icon_drop_seconds, 0.0f, 1.0f);
                // 使用平滑插值会让动画看起来更顺滑
                t = Mathf.SmoothStep(0f, 1f, t);

                ui_canvasgroup.alpha = t * icon_alpha;
                ui_transform.localScale = Vector3.Lerp(IconSizeDrop, IconSizeStay, t) * icon_size_multi;

                Vector2 posIsNormal = Vector2.Lerp(IconPosPrctDrop, IconPosPrctStay, t);
                ui_transform.anchoredPosition = new Vector2(posIsNormal.x * parentSize.x, posIsNormal.y * parentSize.y);
            }
            else if (delta < icon_drop_seconds + icon_stay_seconds)
            {
                // 阶段2：停留
                ui_transform.localScale = IconSizeStay * icon_size_multi;
                ui_canvasgroup.alpha = icon_alpha;
                ui_transform.anchoredPosition = new Vector2(IconPosPrctStay.x * parentSize.x, IconPosPrctStay.y * parentSize.y);
            }
            else if (delta < icon_drop_seconds + icon_stay_seconds + icon_fadeout_seconds)
            {
                // 阶段3：淡出
                float fadeDelta = delta - icon_drop_seconds - icon_stay_seconds;
                float t = Math.Clamp(fadeDelta / icon_fadeout_seconds, 0.0f, 1.0f);

                ui_transform.localScale = IconSizeStay * icon_size_multi;
                ui_canvasgroup.alpha = (1.0f - t) * icon_alpha;
                ui_transform.anchoredPosition = new Vector2(IconPosPrctStay.x * parentSize.x, IconPosPrctStay.y * parentSize.y);
            }
            else
            {
                // 阶段4：完全隐藏
                ui_canvasgroup.alpha = 0.0f;
            }

            if (disable_icon)
            {
                ui_canvasgroup.alpha = 0.0f;
            }
        }

        public void OnDead(Health health, DamageInfo damageInfo)
        {
            if (health == null) return;

            if (health.IsMainCharacterHealth)
            {
                PlaySound("death");
                return;
            }

            // === 修复核心 BUG：增加空值检查 ===
            if (damageInfo.fromCharacter != null && damageInfo.fromCharacter.Team == Teams.player)
            {
                bool headshot = !disable_headshot && damageInfo.crit > 0;
                bool melee = damageInfo.fromCharacter.GetMeleeWeapon() != null;
                bool explosion = damageInfo.isExplosion;
                // 简化 goldheadshot 判断，防止浮点数误差
                bool goldheadshot = !disable_headshot && (damageInfo.finalDamage >= health.MaxHealth * 0.85f);

                PlayKill(headshot, goldheadshot, melee, explosion);
            }
        }

        public void PlayKill(bool headshot, bool goldheadshot, bool melee, bool explosion)
        {
            if (ui_transform == null) CreateUI();

            UpdateCombo();

            string iconKey = "kill";
            string audioKey = "kill";

            // --- 简化的逻辑判定 ---
            if (combo_count > 8)
            {
                iconKey = "kill8";
            }
            else if (combo_count > 1)
            {
                iconKey = "kill" + combo_count;
                audioKey = "kill" + combo_count;
            }

            // 特殊击杀覆盖 (优先级处理)
            if (explosion) { iconKey = "grenade_kill"; audioKey = "grenade_kill"; }
            if (melee) { iconKey = "melee_kill"; audioKey = "melee_kill"; }
            // 如果不是连杀或者允许覆盖连杀图标，则显示爆头
            if (headshot && (combo_count <= 1 || !is_dont_use_headshot_icon_if_combo))
            {
                iconKey = "headshot";
                // 只有普通击杀才播放爆头音效，避免覆盖掉更重要的连杀音效（可根据需求调整）
                if (audioKey == "kill") audioKey = "headshot";
            }

            // 简易音效模式
            if (simple_sfx)
            {
                audioKey = headshot ? "headshot" : "kill";
            }

            // --- 执行播放 ---
            PlaySound(audioKey);

            if (ui_image != null && KillFeedbackIcons.TryGetValue(iconKey, out Texture2D tex))
            {
                // 重新激活图标动画
                // last_kill_time = Time.time; // UpdateCombo 已经做了这个
                ui_image.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                // 强制设置 alpha 为 0 从头开始动画
                if (ui_canvasgroup != null) ui_canvasgroup.alpha = 0f;
            }
        }

        // 封装音频播放方法，增加鲁棒性
        private void PlaySound(string key)
        {
            if (KillFeedbackAudios_FMOD.TryGetValue(key, out Sound sound))
            {
                // 尝试获取 SFX 总线，如果失败则尝试用 Master，再失败则不使用总线
                ChannelGroup cg;
                if (RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out cg) != RESULT.OK)
                {
                    RuntimeManager.GetBus("bus:/Master").getChannelGroup(out cg);
                }

                var res = RuntimeManager.CoreSystem.playSound(sound, cg, false, out Channel channel);
                if (res == RESULT.OK)
                {
                    channel.setVolume(volume);
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"MCKillFeedback: FMOD playSound failed: {res}");
                }
            }
        }

        public static void UpdateCombo()
        {
            float time = Time.time;
            if (time - last_kill_time > combo_seconds)
            {
                combo_count = 1;
            }
            else
            {
                combo_count++;
            }
            last_kill_time = time;
        }

        // ... (Awake 和 OnEnable 部分保持你原来的大部分逻辑，已省略以节约篇幅，主要改动在下面 LoadRes) ...

        private void Awake()
        {
            // (此处保留你原本的 Config 初始化代码)
            // ...
            DefaultConfig.TryAdd("volume", 1.0f);
            DefaultConfig.TryAdd("simple_sfx", false);
            DefaultConfig.TryAdd("disable_icon", false);
            DefaultConfig.TryAdd("icon_size_multi", 1.0f);
            DefaultConfig.TryAdd("icon_alpha", 0.75f);
            DefaultConfig.TryAdd("combo_seconds", 8.0f);
            DefaultConfig.TryAdd("icon_drop_seconds", 0.15f);
            DefaultConfig.TryAdd("icon_stay_seconds", 1.0f);
            DefaultConfig.TryAdd("icon_fadeout_seconds", 1.0f);
            DefaultConfig.TryAdd("is_dont_use_headshot_icon_if_combo", false);
            DefaultConfig.TryAdd("disable_headshot", false);

            Instance = this;
            if (!Loaded)
            {
                if (LoadRes()) Loaded = true;
            }
        }

        private void OnEnable() { Health.OnDead += OnDead; LoadConfig(); } // 建议把配置读取单独抽一个方法
        private void OnDisable() { Health.OnDead -= OnDead; }

        private void LoadConfig()
        {
            // (把你原来 OnEnable 里的读取配置代码放这里，保持整洁)
            // 略...
        }

        public void CreateUI()
        {
            HUDManager hud_manager = UnityEngine.Object.FindObjectOfType<HUDManager>();
            if (hud_manager == null) return;

            GameObject game_object = new GameObject("MCKillFeedback");
            ui_transform = game_object.AddComponent<RectTransform>();
            ui_image = game_object.AddComponent<Image>();
            ui_canvasgroup = game_object.AddComponent<CanvasGroup>();

            ui_transform.SetParent(hud_manager.transform, false); // false 表示不保持世界坐标，直接贴合父级

            // === 关键 UI 修复 ===
            // 设置锚点为左下角(0,0)，这样上面的 Update 计算逻辑 (pos.x * parentSize.x) 才能正常工作。
            ui_transform.anchorMin = new Vector2(0f, 0f);
            ui_transform.anchorMax = new Vector2(0f, 0f);
            ui_transform.pivot = new Vector2(0.5f, 0.5f); // 中心点作为旋转和定位点

            ui_image.preserveAspect = true;
            ui_image.raycastTarget = false; // 重要：防止阻挡鼠标射线
            ui_canvasgroup.alpha = 0f;      // 初始隐藏
            ui_canvasgroup.blocksRaycasts = false; // 防止阻挡交互

            UnityEngine.Debug.Log("MCKillFeedback: UI created");
        }

        // 替换原来的 LoadRes 方法
        public bool LoadRes()
        {
            UnityEngine.Debug.Log("MCKillFeedback: 开始从DLL加载嵌入资源...");
            bool success = true;
            Assembly assembly = Assembly.GetExecutingAssembly();
            string namespacePrefix = "MCKillFeedback.CustomPacks.mc."; // 格式通常是: 你的项目命名空间.文件夹名.

            // === 1. 加载图片 ===
            foreach (string icon_name in IconNames)
            {
                // 拼接资源全名，例如 MCKillFeedback.Assets.kill.png
                string resourceName = namespacePrefix + icon_name + ".png";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        UnityEngine.Debug.LogError($"MCKillFeedback: 找不到嵌入资源 '{resourceName}'。请检查文件名大小写或是否已设置为'嵌入的资源'。");
                        // 尝试打印所有可用资源名称，方便排查问题
                        // foreach(var name in assembly.GetManifestResourceNames()) Debug.Log("可用资源: " + name);
                        success = false;
                        continue;
                    }

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);

                    Texture2D texture = new Texture2D(2, 2);
                    // LoadImage 可以自动解析 png/jpg 的 byte[]
                    if (texture.LoadImage(buffer))
                    {
                        texture.name = icon_name;
                        KillFeedbackIcons.TryAdd(icon_name, texture);
                        UnityEngine.Debug.Log($"MCKillFeedback: 已加载嵌入图标: {icon_name}");
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"MCKillFeedback: 解析图标失败: {icon_name}");
                    }
                }
            }

            // === 2. 加载音频 (FMOD 内存加载) ===
            foreach (string audio_name in AudioNames)
            {
                // !!! 确认你的嵌入文件到底是 .wav 还是 .mp3，这里假设是 .wav !!!
                string resourceName = namespacePrefix + audio_name + ".mp3";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        UnityEngine.Debug.LogError($"MCKillFeedback: 找不到嵌入音频 '{resourceName}'");
                        success = false;
                        continue;
                    }

                    byte[] audioBytes = new byte[stream.Length];
                    stream.Read(audioBytes, 0, audioBytes.Length);

                    // --- FMOD 内存加载核心魔法 ---

                    // 1. 配置 FMOD 加载信息，告诉它我们要加载多大的数据
                    FMOD.CREATESOUNDEXINFO exinfo = new FMOD.CREATESOUNDEXINFO();
                    exinfo.cbsize = Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO));
                    exinfo.length = (uint)audioBytes.Length;

                    // 2. 临时“钉住”托管内存，获取一个非托管指针给 FMOD 用
                    GCHandle pinnedArray = GCHandle.Alloc(audioBytes, GCHandleType.Pinned);
                    try
                    {
                        // 3. 使用 OPENMEMORY 模式，传入内存指针
                        // MODE.CREATESAMPLE 非常重要，它让 FMOD 把数据复制到自己的内存里，这样我们就能立刻释放掉 C# 这边的内存了
                        MODE mode = MODE.OPENMEMORY | MODE._2D | MODE.CREATESAMPLE | MODE.IGNORETAGS;

                        RESULT result = RuntimeManager.CoreSystem.createSound(
                            pinnedArray.AddrOfPinnedObject(), // 指针
                            mode,
                            ref exinfo, // 传入配置好的长度信息
                            out Sound sound
                        );

                        if (result == RESULT.OK)
                        {
                            KillFeedbackAudios_FMOD.TryAdd(audio_name, sound);
                            UnityEngine.Debug.Log($"MCKillFeedback: 已加载嵌入音频: {audio_name}");
                        }
                        else
                        {
                            UnityEngine.Debug.LogError($"MCKillFeedback: FMOD 内存加载失败 {audio_name}: {result}");
                            success = false;
                        }
                    }
                    finally
                    {
                        // 4. 无论成功失败，一定要释放钉住的内存句柄，否则会导致内存泄漏
                        pinnedArray.Free();
                    }
                }
            }

            return success;
        }
    }
}