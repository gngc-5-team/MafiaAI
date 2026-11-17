using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using MafiaAI.Alibi;
using MafiaAI.Alibi.UI;
using MafiaAI.Mystery.Manor;
using MafiaAI.Mystery.UI;
using M = MafiaAI.Mystery.EditorTools.MysterySceneBuilder;
using Object = UnityEngine.Object;

namespace MafiaAI.Mystery.EditorTools
{
    /// <summary>
    /// 「자정의 원고 — 저택 단면」 씬을 한 번 저작한다. 만든 뒤엔 하이라키·인스펙터에서 직접 고친다.
    ///
    /// 저택은 앞벽을 걷어 낸 3층 단면(2층: 서재·응접실·식당 / 1층: 온실·홀·부엌 / 지하: 세탁실·창고)이고,
    /// 방 그림(Codex, 측면 입면도)은 같은 비율로 잘라 같은 크기 상자에 넣는다(발 디딤선이 모든 방에서 같은 높이).
    /// 조사 지점·계단·조명·창문 좌표는 원본 그림 위에서 잰 값(0~1)을 아래 표에 적어 두고, 자른 영역에 맞춰 월드로 옮긴다.
    /// 연출: REPLACED식 — 레터박스, 작은 도트 인물 대 큰 공간, 촛불·벽난로 빛 웅덩이(2D 조명), 번개, 비, 전경 실루엣, 필름 그레인·블룸.
    /// </summary>
    public static class ManorSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/MidnightManor.unity";
        const string Art = "Assets/MafiaAI/Art/Manor/";
        const string MysteryArt = "Assets/MafiaAI/Art/Mystery/";
        const string AlibiUi = "Assets/MafiaAI/Art/Alibi/UI/";
        const string AlibiCodex = "Assets/MafiaAI/Art/Alibi/Codex/";
        const string Old = "Assets/sprites/UI/";
        const string Audio = "Assets/MafiaAI/Audio/";

        // 단면 치수(월드 단위)
        const float W = 10.4f, H = 6.5f, Gap = 0.4f, Slab = 0.7f, Outer = 0.7f;
        const float FeetQ = 0.92f;         // 방 상자 위에서부터 발 디딤선까지(모든 방 공통)
        // 도트 밀도 하나로: 기본 줌(직교 5.4 · 1080p)에서 모든 그림이 '화면 2픽셀 = 1텍셀'(50텍셀/유닛)이 되게 맞춘다 — REPLACED처럼 배경과 인물의 도트 크기를 통일.
        // 방 그림 520×325 = 10.4×6.5유닛, 잼 인물(64PPU)은 1.28배, 바깥 깊이 겹은 화면 배율만큼 PPU를 낮춰 그린다.
        const float Tpu = 50f;
        const float CharScale = 64f / Tpu;
        const float GroundY = H + Slab + 0.1f;                  // 바깥 땅(절벽 윗면) = 1층 바닥 아래 슬래브
        static readonly Vector2 Ref = new Vector2(16f, 10f);    // 깊이 겹의 기준점(저택 가운데쯤)

        static readonly Color Ink = M.Ink, Cream = M.Cream, Muted = M.Muted, Crimson = M.Crimson, Bright = M.Bright, Amber = new Color(0.91f, 0.69f, 0.44f);
        static readonly Color Clear = new Color(1, 1, 1, 0);

        static TMP_FontAsset _hud, _hudBold;
        static Material _lit, _unlit, _add;

        // ───────────────────────── 방 배치표(원본 그림 기준 0~1 좌표) ─────────────────────────

        sealed class RoomDef
        {
            public string Id;
            public int Col, Row;              // 단면 칸(Row 0 = 2층)
            public float CropX, Top, Height;  // 원본에서 잘라 쓴 영역
            public (string id, float x0, float y0, float x1, float y1)[] Hotspots;
            public (string to, float x, bool up)[] Stairs;
            public (string kind, float x, float y)[] Lights;
            public (float x0, float y0, float x1, float y1)[] Windows;
            public (float x, float y, float angle, float length, float width)[] Shafts;
        }

        static readonly RoomDef[] Rooms =
        {
            new RoomDef
            {
                Id = "study", Col = 0, Row = 0, CropX = 0.0247f, Top = 0f, Height = 0.8913f,
                Hotspots = new[]
                {
                    ("study_body", 0.47f, 0.51f, 0.585f, 0.645f), ("study_watch", 0.56f, 0.595f, 0.61f, 0.655f),
                    ("study_typewriter", 0.425f, 0.575f, 0.485f, 0.645f), ("study_manuscript", 0.59f, 0.575f, 0.645f, 0.645f),
                    ("study_teacup", 0.635f, 0.6f, 0.665f, 0.645f), ("study_paperweight", 0.55f, 0.8f, 0.605f, 0.84f),
                    ("study_card", 0.475f, 0.795f, 0.515f, 0.83f), ("study_chair", 0.2f, 0.69f, 0.34f, 0.835f),
                    ("study_fireplace", 0.8f, 0.5f, 0.96f, 0.77f), ("study_window", 0.655f, 0.1f, 0.78f, 0.65f),
                    ("study_door", 0.93f, 0.25f, 0.99f, 0.8f), ("study_shelf", 0.2f, 0.1f, 0.4f, 0.58f),
                    ("study_mantel", 0.84f, 0.4f, 0.9f, 0.5f),
                },
                Stairs = new[] { ("greenhouse", 0.105f, false) },
                Lights = new[] { ("candle", 0.065f, 0.31f), ("candle", 0.425f, 0.31f), ("candle", 0.605f, 0.31f), ("lamp", 0.43f, 0.57f), ("candle", 0.825f, 0.42f), ("candle", 0.94f, 0.42f) },
                Windows = new[] { (0.655f, 0.1f, 0.78f, 0.65f) },
                Shafts = new[] { (0.72f, 0.3f, 24f, 4.6f, 2.6f) },
            },
            new RoomDef
            {
                Id = "drawing", Col = 1, Row = 0, CropX = 0.0098f, Top = 0f, Height = 0.9185f,
                Hotspots = new[]
                {
                    ("drawing_rolls", 0.04f, 0.51f, 0.145f, 0.8f), ("drawing_piano", 0.15f, 0.55f, 0.31f, 0.69f),
                    ("drawing_bench", 0.165f, 0.7f, 0.275f, 0.815f), ("drawing_bench_empty", 0.165f, 0.7f, 0.275f, 0.815f),
                    ("drawing_tools", 0.92f, 0.6f, 0.975f, 0.81f), ("drawing_fire", 0.735f, 0.6f, 0.9f, 0.8f),
                    ("drawing_fire_burned", 0.735f, 0.6f, 0.9f, 0.8f), ("drawing_door", 0.0f, 0.25f, 0.04f, 0.8f),
                    ("drawing_table", 0.56f, 0.66f, 0.63f, 0.81f),
                },
                Stairs = new[] { ("hall", 0.4f, false) },
                Lights = new[] { ("candle", 0.105f, 0.46f), ("candle", 0.365f, 0.51f), ("candle", 0.385f, 0.66f), ("fire", 0.82f, 0.72f), ("candle", 0.76f, 0.49f) },
                Windows = new[] { (0.45f, 0.12f, 0.56f, 0.7f), (0.585f, 0.12f, 0.68f, 0.7f) },
                Shafts = new[] { (0.5f, 0.3f, 18f, 4.4f, 2.2f), (0.63f, 0.3f, 18f, 4.4f, 2.2f) },
            },
            new RoomDef
            {
                Id = "dining", Col = 2, Row = 0, CropX = 0.0013f, Top = 0f, Height = 0.9348f,
                Hotspots = new[]
                {
                    ("dining_gap", 0.45f, 0.6f, 0.53f, 0.7f), ("dining_glass", 0.72f, 0.6f, 0.76f, 0.68f),
                    ("dining_seats", 0.66f, 0.635f, 0.72f, 0.69f), ("dining_painting", 0.38f, 0.24f, 0.62f, 0.52f),
                    ("dining_sideboard", 0.05f, 0.53f, 0.2f, 0.77f),
                },
                Stairs = new[] { ("kitchen", 0.88f, false) },
                Lights = new[] { ("chandelier", 0.5f, 0.12f), ("candle", 0.355f, 0.35f), ("candle", 0.645f, 0.35f), ("candle", 0.31f, 0.6f), ("candle", 0.565f, 0.6f), ("candle", 0.69f, 0.6f) },
                Windows = new[] { (0.16f, 0.05f, 0.3f, 0.56f), (0.7f, 0.05f, 0.84f, 0.56f) },
                Shafts = new[] { (0.23f, 0.25f, 16f, 4.8f, 2.4f), (0.77f, 0.25f, -16f, 4.8f, 2.4f) },
            },
            new RoomDef
            {
                Id = "greenhouse", Col = 0, Row = 1, CropX = 0.013f, Top = 0f, Height = 0.913f,
                Hotspots = new[]
                {
                    ("gh_pot", 0.29f, 0.6f, 0.385f, 0.77f), ("gh_door", 0.4f, 0.34f, 0.52f, 0.74f),
                    ("gh_table", 0.73f, 0.57f, 0.96f, 0.8f), ("gh_body", 0.53f, 0.7f, 0.73f, 0.86f),
                },
                Stairs = new[] { ("study", 0.115f, true) },
                Lights = new[] { ("lamp", 0.5f, 0.13f), ("lamp", 0.94f, 0.37f), ("lamp", 0.05f, 0.33f), ("cold", 0.55f, 0.4f) },
                Windows = new[] { (0.2f, 0.05f, 0.95f, 0.75f) },
                Shafts = new[] { (0.35f, 0.08f, 12f, 5.2f, 3.2f), (0.62f, 0.08f, 12f, 5.2f, 3.2f), (0.86f, 0.08f, 12f, 5.2f, 3.0f) },
            },
            new RoomDef
            {
                Id = "hall", Col = 1, Row = 1, CropX = 0f, Top = 0.1025f, Height = 0.9375f,
                Hotspots = new[]
                {
                    ("hall_clock", 0.245f, 0.37f, 0.325f, 0.93f), ("hall_phone", 0.15f, 0.64f, 0.235f, 0.93f),
                    ("hall_door", 0.675f, 0.4f, 0.865f, 0.93f), ("hall_coats", 0.895f, 0.5f, 0.97f, 0.93f),
                    ("hall_view", 0.38f, 0.3f, 0.5f, 0.7f),
                },
                Stairs = new[] { ("drawing", 0.575f, true), ("laundry", 0.09f, false) },
                Lights = new[] { ("lamp", 0.185f, 0.39f), ("lamp", 0.625f, 0.39f), ("lamp", 0.925f, 0.39f), ("lamp", 0.07f, 0.6f) },
                Windows = new[] { (0.69f, 0.5f, 0.75f, 0.68f), (0.79f, 0.5f, 0.85f, 0.68f) },
                Shafts = new[] { (0.77f, 0.55f, 0f, 3.6f, 1.6f) },
            },
            new RoomDef
            {
                Id = "kitchen", Col = 2, Row = 1, CropX = 0.0358f, Top = 0f, Height = 0.8696f,
                Hotspots = new[]
                {
                    ("kitchen_tray", 0.28f, 0.46f, 0.465f, 0.58f), ("kitchen_knives", 0.645f, 0.44f, 0.73f, 0.56f),
                    ("kitchen_stove", 0.055f, 0.36f, 0.21f, 0.76f), ("kitchen_window", 0.5f, 0.2f, 0.59f, 0.46f),
                },
                Stairs = new[] { ("storage", 0.56f, false), ("dining", 0.885f, true) },
                Lights = new[] { ("stove", 0.13f, 0.65f), ("lamp", 0.255f, 0.47f), ("lamp", 0.275f, 0.33f), ("lamp", 0.8f, 0.29f) },
                Windows = new[] { (0.5f, 0.2f, 0.59f, 0.46f) },
                Shafts = new[] { (0.545f, 0.3f, 14f, 4.2f, 1.8f) },
            },
            new RoomDef
            {
                Id = "laundry", Col = 1, Row = 2, CropX = 0f, Top = 0.0725f, Height = 0.9375f,
                Hotspots = new[]
                {
                    ("laundry_basin", 0.4f, 0.66f, 0.61f, 0.9f), ("laundry_iron", 0.22f, 0.6f, 0.39f, 0.9f),
                    ("laundry_towel", 0.815f, 0.42f, 0.89f, 0.67f), ("laundry_drain", 0.6f, 0.88f, 0.71f, 0.94f),
                },
                Stairs = new[] { ("hall", 0.13f, true) },
                Lights = new[] { ("bulb", 0.535f, 0.37f) },
                Windows = new[] { (0.075f, 0.15f, 0.13f, 0.25f) },
                Shafts = new[] { (0.1f, 0.2f, -20f, 3.2f, 1.4f) },
            },
            new RoomDef
            {
                Id = "storage", Col = 2, Row = 2, CropX = 0f, Top = 0.0725f, Height = 0.9375f,
                Hotspots = new[]
                {
                    ("storage_ledger", 0.42f, 0.58f, 0.57f, 0.9f), ("storage_racks", 0.05f, 0.27f, 0.4f, 0.89f),
                    ("storage_tools", 0.75f, 0.23f, 0.95f, 0.89f),
                },
                Stairs = new[] { ("kitchen", 0.66f, true) },
                Lights = new[] { ("lamp", 0.545f, 0.58f), ("cold", 0.61f, 0.12f) },
                Windows = new[] { (0.55f, 0.02f, 0.67f, 0.1f) },
                Shafts = new[] { (0.61f, 0.08f, 6f, 5.4f, 1.8f) },
            },
        };

        static Rect Box(RoomDef d) => new Rect(d.Col * (W + Gap), (2 - d.Row) * (H + Slab), W, H);

        static Vector2 ToWorld(RoomDef d, float u, float v)
        {
            var b = Box(d);
            float x = b.xMin + (u - d.CropX) / (1f - 2f * d.CropX) * W;
            float y = b.yMax - (v - d.Top) / d.Height * H;
            return new Vector2(x, y);
        }

        static Rect ToWorld(RoomDef d, float x0, float y0, float x1, float y1)
        {
            var a = ToWorld(d, x0, y0);
            var b = ToWorld(d, x1, y1);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static float FloorY(RoomDef d) => Box(d).yMin + (1f - FeetQ) * H;

        // ───────────────────────── 메뉴 ─────────────────────────

        [MenuItem("MafiaAI/미스터리/「자정의 원고 — 저택 단면」 씬 만들기")]
        static void BuildMenu()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("저택 단면", "씬이 이미 있습니다.\n새로 만들면 하이라키에서 직접 고친 내용이 사라집니다. 덮어쓸까요?", "덮어쓰기", "취소"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        public static void BuildOverwrite() => Build();

        // ───────────────────────── 준비 ─────────────────────────

        static void Prepare()
        {
            M.PrepareFonts();
            _hud = FontAsset("Assets/Fonts/DNFForgedBlade-Medium.ttf", "Manor HUD SDF");
            _hudBold = FontAsset("Assets/Fonts/DNFForgedBlade-Bold.ttf", "Manor HUD Bold SDF");
            foreach (var fa in new[] { _hud, _hudBold })
            {
                if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!fa.fallbackFontAssetTable.Contains(M._serif)) { fa.fallbackFontAssetTable.Add(M._serif); EditorUtility.SetDirty(fa); }
            }
            _lit = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat");
            _unlit = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");
            string addPath = Art + "ManorAdditive.mat";
            _add = AssetDatabase.LoadAssetAtPath<Material>(addPath);
            if (_add == null)
            {
                _add = new Material(Shader.Find("MafiaAI/Manor/Additive"));
                AssetDatabase.CreateAsset(_add, addPath);
            }
            AssetDatabase.SaveAssets();
        }

        static TMP_FontAsset FontAsset(string fontPath, string name)
        {
            string assetPath = Art + "Fonts/" + name + ".asset";
            Directory.CreateDirectory(Art + "Fonts");
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            fa.name = name;
            AssetDatabase.CreateAsset(fa, assetPath);
            fa.atlasTextures[0].name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        /// <summary>그림을 스프라이트로(ppu를 주면 그 크기로, 아니면 100). point=도트처럼 또렷하게, repeat=흐르는 겹용 반복,
        /// mips=저택도처럼 멀리서 볼 때 지글거리지 않게, pivot=기준점(0~1, 없으면 가운데).</summary>
        static Sprite Sprite(string path, float ppu = 100f, bool point = false, bool compress = true, int maxSize = 2048, bool repeat = false, bool mips = false, Vector2? pivot = null)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[ManorBuilder] 그림 없음: " + path); return null; }
            bool dirty = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
            if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; dirty = true; }
            if (!Mathf.Approximately(ti.spritePixelsPerUnit, ppu)) { ti.spritePixelsPerUnit = ppu; dirty = true; }
            if (ti.mipmapEnabled != mips) { ti.mipmapEnabled = mips; dirty = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
            var fm = point ? FilterMode.Point : FilterMode.Bilinear;
            if (ti.filterMode != fm) { ti.filterMode = fm; dirty = true; }
            if (ti.maxTextureSize != maxSize) { ti.maxTextureSize = maxSize; dirty = true; }
            var comp = compress ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
            if (ti.textureCompression != comp) { ti.textureCompression = comp; dirty = true; }
            var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (ti.wrapMode != wrap) { ti.wrapMode = wrap; dirty = true; }
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            bool st = false;
            if (settings.spriteMeshType != SpriteMeshType.FullRect) { settings.spriteMeshType = SpriteMeshType.FullRect; st = true; }
            int align = pivot.HasValue ? (int)SpriteAlignment.Custom : (int)SpriteAlignment.Center;
            if (settings.spriteAlignment != align) { settings.spriteAlignment = align; st = true; }
            if (pivot.HasValue && settings.spritePivot != pivot.Value) { settings.spritePivot = pivot.Value; st = true; }
            if (st) { ti.SetTextureSettings(settings); dirty = true; }
            if (dirty) ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>도트 그림: 점 필터 · 무압축 · 밉맵.</summary>
        static Sprite Pixel(string path, float ppu = Tpu, bool repeat = false, Vector2? pivot = null) => Sprite(path, ppu, true, false, 2048, repeat, true, pivot);

        [Serializable] class FrameInfo { public string file; public float pivotX, pivotY; }
        [Serializable] class SheetInfo { public FrameInfo[] frames; public int[] idle; public int[] walk; }

        /// <summary>탐정 도트(잼 캐릭터를 다시 칠하고 중절모를 씌운 것) — 프레임마다 낱장 PNG, 발끝 기준 피벗.</summary>
        static Sprite[] DetectiveFrames(out int[] idle, out int[] walk)
        {
            string dir = Art + "Characters/Detective/";
            var info = JsonUtility.FromJson<SheetInfo>(File.ReadAllText(dir + "frames.json"));
            idle = info.idle;
            walk = info.walk;
            var list = new List<Sprite>();
            foreach (var f in info.frames)
            {
                string path = dir + f.file;
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = 64;
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.alphaIsTransparency = true;
                var st = new TextureImporterSettings();
                ti.ReadTextureSettings(st);
                st.spriteAlignment = (int)SpriteAlignment.Custom;
                st.spritePivot = new Vector2(f.pivotX, f.pivotY);
                st.spriteMeshType = SpriteMeshType.FullRect;
                ti.SetTextureSettings(st);
                ti.SaveAndReimport();
                list.Add(AssetDatabase.LoadAssetAtPath<Sprite>(path));
            }
            return list.ToArray();
        }

        static Sprite[] ClipFrames(string asepritePath, string clipName)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(asepritePath).OfType<AnimationClip>().FirstOrDefault(c => c.name == clipName);
            if (clip == null) return new Sprite[0];
            var b = AnimationUtility.GetObjectReferenceCurveBindings(clip).FirstOrDefault();
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, b);
            var list = new List<Sprite>();
            foreach (var k in keys) if (k.value is Sprite sp && (list.Count == 0 || list[list.Count - 1] != sp)) list.Add(sp);
            return list.ToArray();
        }

        // ───────────────────────── 조립 ─────────────────────────

        static void Build()
        {
            Prepare();
            var c = Case01.Build();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 카메라 · 후처리
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.012f, 0.02f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            camGo.transform.position = new Vector3(16f, 10f, -20f);
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            // 귀는 카메라가 아니라 저택 평면(z=0) 위에 — 3D 소리의 거리·좌우가 화면에서 보이는 대로
            var ears = new GameObject("Ears");
            ears.transform.SetParent(camGo.transform, false);
            ears.transform.localPosition = new Vector3(0f, 0f, 20f);
            ears.AddComponent<AudioListener>();
            var rig = camGo.AddComponent<ManorCamera>();
            rig.cam = cam;
            rig.bounds = new Rect(-5.2f, -2f, 3 * W + 2 * Gap + 10.4f, 36f);   // 양 끝 방에선 집 밖(절벽·바다·비)이 화면의 1/5쯤 보인다

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var volGo = new GameObject("PostFX");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = VolumeProfileAsset();

            // 월드
            var worldGo = new GameObject("Manor");
            var world = worldGo.AddComponent<ManorWorld>();
            world.roomWidth = W;
            var globalLight = new GameObject("GlobalLight").AddComponent<Light2D>();
            globalLight.transform.SetParent(worldGo.transform, false);
            globalLight.lightType = Light2D.LightType.Global;
            globalLight.color = new Color(0.7f, 0.78f, 1f);
            globalLight.intensity = 0.68f;
            TargetAllLayers(globalLight);

            var lightning = worldGo.AddComponent<LightningController>();
            lightning.globalLight = globalLight;
            lightning.cameraRig = rig;
            var windowGlows = new List<SpriteRenderer>();
            var windowLights = new List<Light2D>();
            var flash = new List<SpriteRenderer>();

            BuildExterior(worldGo.transform, cam, lightning, flash);
            var slots = new List<ManorWorld.RoomSlot>();
            foreach (var d in Rooms) slots.Add(BuildRoom(worldGo.transform, d, c, windowGlows, windowLights));
            world.rooms = slots.ToArray();
            BuildStructure(worldGo.transform, c);
            BuildForeground(worldGo.transform, cam);
            lightning.windowGlows = windowGlows.ToArray();
            lightning.windowLights = windowLights.ToArray();
            lightning.flashRenderers = flash.ToArray();
            world.houseBounds = new Rect(-Outer, 0f, 3 * W + 2 * Gap + 2 * Outer, 3 * (H + Slab));

            var ringRoot = new GameObject("HoverRing");
            ringRoot.transform.SetParent(worldGo.transform, false);
            var ring = Spr("Ring", ringRoot.transform, Sprite(AlibiCodex + "red_pencil_circle.png", 100f, false, false, 1024), _unlit, 16);
            ring.color = new Color(1f, 0.32f, 0.3f, 0.95f);
            NormalizeSpriteSize(ring, 1f, 1f);
            ring.enabled = false;

            // 인물
            var detGo = new GameObject("Detective");
            detGo.transform.SetParent(worldGo.transform, false);
            var det = Actor(detGo, "탐정", DetectiveFrames(out var idleIdx, out var walkIdx), idleIdx, walkIdx, 9);
            var aura = new GameObject("Aura").AddComponent<Light2D>();
            aura.transform.SetParent(detGo.transform, false);
            aura.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            aura.lightType = Light2D.LightType.Point;
            aura.color = new Color(1f, 0.82f, 0.62f);
            aura.intensity = 0.32f;
            aura.pointLightOuterRadius = 2.6f;
            aura.pointLightInnerRadius = 0.2f;
            aura.falloffIntensity = 0.8f;
            TargetAllLayers(aura);
            var controller = detGo.AddComponent<DetectiveController>();
            controller.actor = det;
            controller.world = world;
            rig.target = detGo.transform;

            var npcRoot = new GameObject("Guests");
            npcRoot.transform.SetParent(worldGo.transform, false);
            var files = new Dictionary<string, string> { ["카이"] = "cha_1", ["미로"] = "cha_2-2", ["세이"] = "cha_4", ["제로"] = "cha_5", ["하루"] = "cha_6" };
            var actors = new List<PixelActor>();
            var npcSteps = new List<AudioSource>();
            foreach (var s in c.Suspects)
            {
                var go = new GameObject(s.Name);
                go.transform.SetParent(npcRoot.transform, false);
                string ase = "Assets/sprites/char/" + files[s.Name] + ".aseprite";
                var idle = ClipFrames(ase, "idle");
                var walk = ClipFrames(ase, "walk");
                actors.Add(Actor(go, s.Name, idle.Concat(walk).ToArray(), Enumerable.Range(0, idle.Length).ToArray(), Enumerable.Range(idle.Length, walk.Length).ToArray(), 8));
                npcSteps.Add(Source3D(go, 2.5f, 14f));
            }
            var director = npcRoot.AddComponent<NpcDirector>();
            director.world = world;
            director.actors = actors.ToArray();

            // 소리 — 배경 다섯 겹(방마다 배합) · 발소리(바닥 재질) · 그 자리에서 나는 소리 · 천둥
            var audioGo = new GameObject("Audio");
            var alibiAudio = audioGo.AddComponent<AlibiAudio>();
            alibiAudio.music = audioGo.AddComponent<AudioSource>();
            alibiAudio.sfx = audioGo.AddComponent<AudioSource>();
            alibiAudio.music.playOnAwake = false;
            alibiAudio.sfx.playOnAwake = false;
            alibiAudio.musicClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "bgm.mp3");
            alibiAudio.musicVolume = 0.07f;   // 잼 배경음은 아주 낮게 — 분위기는 빗소리·바람·발소리가 만든다
            alibiAudio.click = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "vote_select.mp3");
            alibiAudio.stamp = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "vote_confirm.mp3");
            alibiAudio.murder = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "night_start.mp3");
            alibiAudio.reveal = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "role_assigned.mp3");
            alibiAudio.ending = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "game_end.mp3");
            AudioClip Clip(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "Manor/" + n + ".wav");
            AudioClip[] Clips(string n, int count) => Enumerable.Range(1, count).Select(i => Clip($"{n}_{i}")).Where(x => x != null).ToArray();
            AudioSource Bed(string n)
            {
                var src = new GameObject("Bed_" + n).AddComponent<AudioSource>();
                src.transform.SetParent(audioGo.transform, false);
                src.clip = Clip(n);
                src.playOnAwake = false;
                src.loop = true;
                src.volume = 0f;
                return src;
            }
            var amb = audioGo.AddComponent<ManorAmbience>();
            amb.rainOut = Bed("rain_out");
            amb.rainIn = Bed("rain_in");
            amb.rainGlass = Bed("rain_glass");
            amb.wind = Bed("wind");
            amb.sea = Bed("sea");
            amb.rooms = new[]
            {
                new ManorAmbience.RoomMix { room = "study",      floor = "wood",   rainOut = 0.16f, rainIn = 0.55f, wind = 0.2f,  sea = 0.12f },
                new ManorAmbience.RoomMix { room = "drawing",    floor = "wood",   rainOut = 0.08f, rainIn = 0.6f,  wind = 0.14f, sea = 0.06f },
                new ManorAmbience.RoomMix { room = "dining",     floor = "wood",   rainOut = 0.16f, rainIn = 0.55f, wind = 0.2f,  sea = 0.14f },
                new ManorAmbience.RoomMix { room = "greenhouse", floor = "tile",   rainOut = 0.22f, rainIn = 0.15f, rainGlass = 0.75f, wind = 0.26f, sea = 0.16f },
                new ManorAmbience.RoomMix { room = "hall",       floor = "marble", rainOut = 0.12f, rainIn = 0.55f, wind = 0.2f,  sea = 0.08f },
                new ManorAmbience.RoomMix { room = "kitchen",    floor = "stone",  rainOut = 0.16f, rainIn = 0.5f,  wind = 0.2f,  sea = 0.12f },
                new ManorAmbience.RoomMix { room = "laundry",    floor = "stone",  rainOut = 0.03f, rainIn = 0.28f, wind = 0.07f, sea = 0.06f },
                new ManorAmbience.RoomMix { room = "storage",    floor = "stone",  rainOut = 0.03f, rainIn = 0.25f, wind = 0.08f, sea = 0.06f },
            };
            amb.steps = audioGo.AddComponent<AudioSource>();
            amb.steps.playOnAwake = false;
            amb.floors = new[]
            {
                new ManorAmbience.StepSet { id = "wood", clips = Clips("step_wood", 6), volume = 1f },
                new ManorAmbience.StepSet { id = "marble", clips = Clips("step_marble", 6), volume = 0.95f },
                new ManorAmbience.StepSet { id = "stone", clips = Clips("step_stone", 6), volume = 1f },
                new ManorAmbience.StepSet { id = "tile", clips = Clips("step_tile", 5), volume = 0.95f },
                new ManorAmbience.StepSet { id = "stair_wood", clips = Clips("step_stair_wood", 5), volume = 1f },
                new ManorAmbience.StepSet { id = "stair_iron", clips = Clips("step_stair_iron", 5), volume = 0.8f },
            };
            amb.sfx = audioGo.AddComponent<AudioSource>();
            amb.sfx.playOnAwake = false;
            amb.door = Clip("door");
            amb.chime = Clip("chime");
            amb.lensOn = Clip("ui_lens_on");
            amb.lensOff = Clip("ui_lens_off");
            amb.photo = Clip("ui_photo");
            amb.viewCamera = cam;
            amb.world = world;
            lightning.thunderSource = audioGo.AddComponent<AudioSource>();
            lightning.thunderSource.playOnAwake = false;
            lightning.thunder = Clips("thunder", 3);
            // 그 자리에서 나는 소리(3D)
            var drawingDef = Rooms.First(r => r.Id == "drawing");
            var kitchenDef = Rooms.First(r => r.Id == "kitchen");
            var hallDef = Rooms.First(r => r.Id == "hall");
            Emitter(worldGo.transform, "Fire_drawing", ToWorld(drawingDef, 0.82f, 0.72f), Clip("fire"), 0.7f, 2f, 12f);
            Emitter(worldGo.transform, "Fire_kitchen", ToWorld(kitchenDef, 0.13f, 0.65f), Clip("fire"), 0.32f, 1.5f, 9f);
            Emitter(worldGo.transform, "Kettle", Px(kitchenDef, 80f, 150f), Clip("kettle"), 0.35f, 1.2f, 7f);
            Emitter(worldGo.transform, "GrandfatherClock", ToWorld(hallDef, 0.285f, 0.6f), Clip("clock_tick"), 0.42f, 2f, 11f);
            var drips = Clips("drip", 4);
            foreach (var (room, tx, ty) in new[] { ("laundry", 338f, 68f), ("laundry", 150f, 66f), ("storage", 250f, 30f), ("storage", 470f, 40f) })
            {
                var d = Rooms.First(r => r.Id == room);
                var at = Px(d, tx, ty);
                var e = Emitter(worldGo.transform, "Drips_" + room, new Vector2(at.x, FloorY(d) + 0.2f), null, 0.55f, 1.5f, 9f);
                e.loop = false;
                e.clips = drips;
                e.interval = new Vector2(1.4f, 4.2f);
            }

            // UI
            var ui = BuildUi(cam, out var hud, out var talk, out var bookGroup, out var book, out var viewer, out var reader, out var theory, out var cut, out var shake);

            // 진행자
            var game = new GameObject("ManorGame").AddComponent<ManorGame>();
            game.world = world;
            game.detective = controller;
            game.npcs = director;
            game.cameraRig = rig;
            game.lightning = lightning;
            game.ambience = amb;
            game.hoverRing = ring;
            game.hoverRingRoot = ringRoot.transform;
            game.overviewCenter = new Vector2(16f, 15.9f);   // 지붕 꼭대기(굴뚝·풍향계)까지 들어오게
            game.overviewSize = 18.7f;
            game.talkSize = 2.7f;   // 대화 투샷: 화면 4픽셀 = 1텍셀(정수 배율 — 도트가 고르게)
            game.foreground = _foreground;
            game.hud = hud;
            game.talk = talk;
            game.bookGroup = bookGroup;
            game.book = book;
            game.viewer = viewer;
            game.reader = reader;
            game.theory = theory;
            game.cut = cut;
            game.audioFx = alibiAudio;
            game.shakeRoot = shake;

            var entries = new List<ManorGame.SpriteEntry>();
            foreach (var key in c.Evidence.Select(e => e.Image).Where(k => !string.IsNullOrEmpty(k)).Distinct())
                entries.Add(new ManorGame.SpriteEntry { key = key, sprite = M.LoadSprite(MysteryArt + "Evidence/" + key + ".png", default, 1024, true) });
            foreach (var key in c.Prologue.Select(p => p.Image).Concat(c.Endings.SelectMany(e => e.Panels).Select(p => p.Image)).Distinct())
            {
                // 이야기 그림도 도트로(640×427, 96색) — 프롤로그·엔딩이 게임 화면과 같은 결로 이어지게
                string px = MysteryArt + "Story/px_" + key + ".png";
                var sp = File.Exists(px) ? Sprite(px, 100f, true, false, 1024) : M.LoadSprite(MysteryArt + "Story/" + key + ".png", default, 2048, true);
                entries.Add(new ManorGame.SpriteEntry { key = key, sprite = sp });
            }
            entries.Add(new ManorGame.SpriteEntry { key = "ui_testimony", sprite = M.LoadSprite(MysteryArt + "UI/ui_testimony.png", default, 512) });
            game.art = entries.Where(e => e.sprite != null).ToArray();
            // 살펴보기 사진: 조사 지점마다 탐정 시선의 사진(320×240 도트 — 화면엔 2배로)
            game.photos = Rooms.SelectMany(r => r.Hotspots).Select(h => h.id).Distinct()
                .Select(id => new ManorGame.SpriteEntry { key = id, sprite = File.Exists(Art + "Photos/ph_" + id + ".png") ? Sprite(Art + "Photos/ph_" + id + ".png", 100f, true, false, 512) : null })
                .Where(e => e.sprite != null).ToArray();
            game.npcStepSources = npcSteps.ToArray();
            game.characters = CharacterSheets.All.Select(ch => new ManorGame.CharacterArt
            {
                name = ch.Name,
                portrait = M.OldSprite(Old + "Char/" + ch.PortraitKey + ".png"),
                face = M.LoadSprite(AlibiUi + "face_" + ch.PortraitKey + ".png", default, 256),
            }).ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[ManorBuilder] 씬 저장: " + ScenePath);
        }

        // ───────────────────────── 후처리(블룸·그레인·비네트·색) ─────────────────────────

        static VolumeProfile VolumeProfileAsset()
        {
            string path = Art + "ManorVolume.asset";
            var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            var bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.85f);
            bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(1f, 0.86f, 0.72f));
            var vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.3f);
            vig.smoothness.Override(0.5f);
            var grain = p.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.24f);
            grain.response.Override(0.8f);
            var col = p.Add<ColorAdjustments>(true);
            col.contrast.Override(14f);
            col.saturation.Override(-4f);
            col.postExposure.Override(0.15f);
            var smh = p.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.9f, 1f, 1.12f, 0f));
            smh.highlights.Override(new Vector4(1.08f, 1f, 0.9f, 0f));
            foreach (var comp in p.components) AssetDatabase.AddObjectToAsset(comp, p);
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            return p;
        }

        // ───────────────────────── 바깥(하늘·구름·번개·먼 곶·등대·바다·해안·안개·비·절벽·지붕) ─────────────────────────
        // REPLACED식 깊이: 직교 카메라 한 대로 찍되, 바깥은 깊이를 가진 겹(DepthLayer)으로 쌓아 원근처럼 움직이게 한다.
        // 겹 안의 좌표는 '그 깊이 평면'의 좌표(월드와 같은 눈금, 기준점 Ref 둘레)다. 화면 배율 p = D/(D+z), D = 5.4×3.3 ≈ 17.8
        //   → 먼 곶 160: 0.10 · 등대 150: 0.106 · 바다 140: 0.113 · 해안 50: 0.263 · 안개 20: 0.47 · 전경 −4: 1.29
        // 각 겹의 PPU는 기본 줌에서 '화면 2픽셀 = 1텍셀'이 되게 골랐다(PPU = 50 × p) — 모든 겹의 도트 크기가 같다.

        static void BuildExterior(Transform root, Camera cam, LightningController lightning, List<SpriteRenderer> flash)
        {
            var ext = new GameObject("Exterior").transform;
            ext.SetParent(root, false);
            float total = 3 * W + 2 * Gap;
            const float horizon = 12.7f;    // 수평선(바다 평면의 y): 1층에선 화면 가운데 조금 위, 2층에선 조금 아래에 걸린다

            // ── 하늘: 무한히 먼 겹(카메라에 붙어 아주 조금만 흐른다)
            var sky = Spr("Sky", Depth("Sky", ext, cam, 0f, true, 0.015f), Pixel(Art + "Exterior/px_sky.png"), _unlit, -100);
            sky.color = new Color(0.8f, 0.84f, 0.94f);
            lightning.sky = sky;

            // 먼 구름 · 번개 줄기 · 가까운 구름 — 서로 다른 빠르기로 흘러 하늘이 산다(구름·안개·빛은 REPLACED처럼 부드럽게)
            flash.Add(TileSpr("Clouds", Depth("CloudsFar", ext, cam, 0f, true, 0.03f), Sprite(Art + "FX/fx_clouds_b.png", 32f, false, false, 1024, true),
                ScrollMat("CloudsFar", new Vector2(0.0035f, 0f), false), -99, new Vector2(96f, 8f), new Color(0.36f, 0.42f, 0.54f, 0.5f), new Vector3(0f, 2.4f, 0f)));
            var boltLayer = Depth("Bolts", ext, cam, 0f, true, 0.04f);   // 구름 두 겹보다 앞, 곶·바다보다 뒤
            var boltMat = GlowMat("Bolt", new Color(3f, 3.3f, 4.2f, 1f));
            var bolts = new List<SpriteRenderer>();
            for (int i = 0; i < 4; i++)
            {
                // 그림 위쪽 절반에 줄기가 있다 — 화면 위 끝 바로 너머에서 내려와 수평선 높이(화면 가운데)쯤에서 끝나게
                var b = Spr("Bolt" + i, boltLayer, Sprite(Art + $"FX/fx_bolt_{i}.png", 88f, false, false, 1024), boltMat, -97);
                b.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                b.enabled = false;
                bolts.Add(b);
            }
            lightning.bolts = bolts.ToArray();
            lightning.viewCamera = cam;
            lightning.houseRect = new Rect(-2f, -Slab, total + 4f, 40f);
            flash.Add(TileSpr("Clouds", Depth("CloudsNear", ext, cam, 0f, true, 0.06f), Sprite(Art + "FX/fx_clouds_a.png", 26f, false, false, 1024, true),
                ScrollMat("CloudsNear", new Vector2(0.009f, 0f), false), -98, new Vector2(118.15f, 9.85f), new Color(0.2f, 0.24f, 0.31f, 0.72f), new Vector3(0f, 4.3f, 0f)));

            // ── 먼 곶(왼쪽, 깊이 160): 아랫부분은 바다 겹이 가려 수평선 위에 선 실루엣이 된다
            var head = Spr("Headland", Depth("FarHeadland", ext, cam, 160f), Pixel(Art + "Exterior/px_headland.png", 5f), _unlit, -96);
            head.color = new Color(0.6f, 0.66f, 0.78f);
            head.transform.localPosition = OnPlane(-78f, 2.6f + head.sprite.bounds.size.y * 0.5f);
            flash.Add(head);

            // ── 등대 곶(오른쪽, 깊이 150): 도는 불빛이 바다를 쓸고, 이쪽을 비추는 순간 등이 번쩍인다
            var lhLayer = Depth("Lighthouse", ext, cam, 150f);
            var lhSp = Pixel(Art + "Exterior/px_lighthouse.png", 5.3f);
            var lh = Spr("Headland", lhLayer, lhSp, _unlit, -96);
            lh.color = new Color(0.66f, 0.72f, 0.84f);
            const float lhX = 82.8f, lhBottom = 2.6f;
            var lsz = lhSp.bounds.size;
            lh.transform.localPosition = OnPlane(lhX, lhBottom + lsz.y * 0.5f);
            flash.Add(lh);
            var lamp = new Vector2(lhX - lsz.x * 0.5f + 345.6f / 5.3f, lhBottom + (lhSp.rect.height - 65.1f) / 5.3f);   // 그림 속 등불 방(텍셀 345.6, 65.1)
            var beamRoot = new GameObject("Beam").transform;
            beamRoot.SetParent(lhLayer, false);
            beamRoot.localPosition = OnPlane(lamp.x, lamp.y);
            var beam = Spr("Ray", beamRoot, Sprite(Art + "FX/fx_beam.png", 100f, false, false, 1024), GlowMat("Beam", new Color(1.5f, 1.35f, 1.05f, 1f)), -93);
            NormalizeSpriteSize(beam, 95f, 10f);
            beam.transform.localPosition = new Vector3(47.5f, 0f, 0f);
            var lampGlow = Spr("LampGlow", lhLayer, Sprite(Art + "FX/fx_glow.png", 256f, false, false, 256), GlowMat("LampGlow", new Color(2.4f, 1.8f, 1f, 1f)), -92);
            lampGlow.transform.localPosition = OnPlane(lamp.x, lamp.y);
            lampGlow.transform.localScale = Vector3.one * 9f;
            var lb = lhLayer.gameObject.AddComponent<LighthouseBeam>();
            lb.beam = beamRoot;
            lb.beamSprite = beam;
            lb.lampGlow = lampGlow;
            lb.period = 8f;
            lb.beamAlpha = 0.45f;

            // ── 바다(깊이 140): 수평선 쪽은 잔물결(그림 전체를 반 크기로), 아래로는 큰 물결(그림 아래 절반을 원래 크기로) — 물결이 원근대로 커진다.
            //    두 겹이 엇갈려 흘러 물마루가 일렁이고, 맨 아래는 거울로 한 장 더 이어 화면 아래까지 덮는다
            var seaLayer = Depth("Sea", ext, cam, 140f);
            var farSp = Pixel(Art + "Exterior/px_sea_far.png", 5.65f, true);     // 400×100 → 70.8×17.7 평면 유닛(화면 8×2)
            var nearSp = Pixel(Art + "Exterior/px_sea_near.png", 5.65f, true);   // 800×100 → 141.6×17.7
            float farH = farSp.bounds.size.y, nearH = nearSp.bounds.size.y, nearW = nearSp.bounds.size.x;
            var nearMat = ScrollMat("SeaNear", new Vector2(0.004f, 0f), false);
            flash.Add(TileSpr("SeaFar", seaLayer, farSp, ScrollMat("SeaFar", new Vector2(0.003f, 0f), false), -95, new Vector2(farSp.bounds.size.x * 8f, farH), new Color(0.52f, 0.58f, 0.7f), OnPlane(Ref.x, horizon - farH * 0.5f)));
            flash.Add(TileSpr("SeaNear", seaLayer, nearSp, nearMat, -95, new Vector2(nearW * 4f, nearH), new Color(0.46f, 0.52f, 0.64f), OnPlane(Ref.x, horizon - farH - nearH * 0.5f)));
            var deep = TileSpr("SeaDeep", seaLayer, nearSp, nearMat, -95, new Vector2(nearW * 4f, nearH), new Color(0.4f, 0.46f, 0.58f), OnPlane(Ref.x, horizon - farH - nearH * 1.5f));
            deep.flipY = true;
            flash.Add(deep);
            TileSpr("Swell", seaLayer, nearSp, ScrollMat("SeaSwell", new Vector2(-0.0045f, 0f), true), -94, new Vector2(nearW * 4f, nearH), new Color(0.5f, 0.6f, 0.78f, 0.12f), OnPlane(Ref.x + nearW * 0.37f, horizon - farH - nearH * 0.5f - 0.6f));
            var haze = Spr("HorizonHaze", seaLayer, Sprite(Art + "FX/fx_fogband.png", 100f, false, false, 1024), _add, -93);
            haze.color = new Color(0.42f, 0.55f, 0.72f, 0.32f);
            NormalizeSpriteSize(haze, 600f, 16f);
            haze.transform.localPosition = OnPlane(Ref.x, horizon + 1.5f);

            // ── 해안 절벽과 바위섬(깊이 50) + 밑동에 부서지는 물보라
            var coastLayer = Depth("Coast", ext, cam, 50f);
            var coastSp = Pixel(Art + "Exterior/px_coast.png", 13.125f, true);
            var csz = coastSp.bounds.size;
            const float coastBase = -4f;
            flash.Add(TileSpr("Coast", coastLayer, coastSp, _unlit, -90, new Vector2(csz.x * 5f, csz.y), new Color(0.6f, 0.66f, 0.78f), OnPlane(Ref.x, coastBase + csz.y * 0.5f)));
            var spray = Particles("Spray", coastLayer, ParticleMat("Spray", Art + "FX/fx_dust.png", true), -89, 400);
            spray.transform.localPosition = OnPlane(Ref.x, coastBase + 1.5f);
            {
                var m = spray.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.6f);
                m.startColor = new Color(0.75f, 0.85f, 1f, 0.5f);
                m.simulationSpace = ParticleSystemSimulationSpace.Local;
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var em = spray.emission;
                em.rateOverTime = 60f;
                var sh = spray.shape;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = new Vector3(260f, 0.4f, 0.1f);
                sh.rotation = new Vector3(-90f, 0f, 0f);
                sh.randomDirectionAmount = 0.25f;
                var f = spray.forceOverLifetime;
                f.enabled = true;
                f.space = ParticleSystemSimulationSpace.Local;
                f.x = new ParticleSystem.MinMaxCurve(0f);
                f.y = new ParticleSystem.MinMaxCurve(-16f);
                f.z = new ParticleSystem.MinMaxCurve(0f);
                FadeOut(spray, 0.1f);
            }

            // ── 안개(깊이 20): 절벽 끝 높이와 수평선 높이로 흐르는 두 띠 — 먼 겹과 집 사이를 공기로 잇는다
            var mistLayer = Depth("Mist", ext, cam, 20f);
            var mistSp = Sprite(Art + "FX/fx_mist.png", 25.6f, false, false, 1024, true);
            flash.Add(TileSpr("MistLow", mistLayer, mistSp, ScrollMat("MistLow", new Vector2(0.006f, 0f), false), -85, new Vector2(160f, 10f), new Color(0.55f, 0.64f, 0.78f, 0.34f), OnPlane(Ref.x, 5.9f)));
            flash.Add(TileSpr("MistHigh", mistLayer, mistSp, ScrollMat("MistHigh", new Vector2(0.0035f, 0f), false), -86, new Vector2(160f, 10f), new Color(0.5f, 0.6f, 0.75f, 0.2f), OnPlane(Ref.x + 13f, 11f)));

            // ── 비 세 겹: 먼 비(무한 겹 — 화면에 붙어 내린다) · 집 뒤 비 · 집 앞 비(조명을 받는다 — 랜턴 둘레와 번개 때만 빛난다)
            //    집 앞 비는 집(방)과 땅속(절벽 단면)을 가린다 — 단면 안쪽엔 비가 오지 않으니까
            string rainTex = Art + "FX/fx_rain.png";
            var farRain = Rain("Rain", Depth("FarRain", ext, cam, 0f, true, 0f), -92, ParticleMat("RainFar", rainTex, true), 26f, 900f, new Vector2(11f, 15f), new Vector2(0.02f, 0.04f), new Color(0.7f, 0.8f, 1f, 0.16f), true);
            farRain.transform.localPosition = new Vector3(0f, 7f, 0f);
            var midRain = Rain("Rain", ext, -80, ParticleMat("Rain", rainTex, true), 80f, 1100f, new Vector2(26f, 34f), new Vector2(0.05f, 0.1f), new Color(0.72f, 0.82f, 1f, 0.24f), false);
            midRain.transform.position = new Vector3(Ref.x, 36f, 0f);
            var nearRain = Rain("NearRain", ext, 36, LitParticleMat("RainNear", rainTex), 80f, 320f, new Vector2(36f, 44f), new Vector2(0.07f, 0.12f), new Color(0.85f, 0.9f, 1f, 0.6f), false);
            nearRain.transform.position = new Vector3(Ref.x, 34f, 0f);
            nearRain.GetComponent<ParticleSystemRenderer>().maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            Mask("HouseMask", ext, new Rect(-Outer, -Slab, total + 2 * Outer, 3 * (H + Slab)));
            Mask("GroundMask", ext, new Rect(-90f, -70f, total + 180f, 70f + GroundY));

            // 절벽 위 풀밭에 튀는 빗방울
            foreach (var (x0, x1) in new[] { (-40f, -Outer - 0.2f), (total + Outer + 0.2f, total + 40f) })
            {
                var sp = Particles("Splash", ext, ParticleMat("Splash", Art + "FX/fx_dust.png", true), -47, 200);
                sp.transform.position = new Vector3((x0 + x1) * 0.5f, GroundY + 0.12f, 0f);
                var m = sp.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
                m.startColor = new Color(0.8f, 0.88f, 1f, 0.6f);
                m.gravityModifier = 0.9f;
                m.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = sp.emission;
                em.rateOverTime = 90f;
                var sh = sp.shape;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = new Vector3(x1 - x0, 0.1f, 0.1f);
                sh.rotation = new Vector3(-90f, 0f, 0f);
                sh.randomDirectionAmount = 0.5f;
            }

            // ── 절벽(집 평면): 바위 면 + 풀 덮인 절벽 턱 + 철책 — 방과 같은 도트 밀도, 같은 2D 빛을 받는다
            var cliff = new GameObject("Cliff").transform;
            cliff.SetParent(ext, false);
            var rock = Spr("Rock", cliff, Pixel(Art + "Exterior/px_rock.png", Tpu, true), _lit, -50);
            rock.drawMode = SpriteDrawMode.Tiled;
            rock.size = new Vector2(180f, 60f);
            rock.color = new Color(0.5f, 0.53f, 0.6f);
            rock.transform.position = new Vector3(total * 0.5f, GroundY - 0.4f - 30f, 0f);
            var edgeSp = Pixel(Art + "Exterior/px_cliff_edge.png", Tpu, true);
            var fenceSp = Pixel(Art + "Exterior/px_fence.png", Tpu, true);
            float eh = edgeSp.bounds.size.y, fh = fenceSp.bounds.size.y;
            foreach (var (x0, x1, f0, f1) in new[] { (-70f, -Outer + 0.05f, -34f, -3.2f), (total + Outer - 0.05f, total + 70f, total + 3.6f, total + 30f) })
            {
                var e = Spr("CliffEdge", cliff, edgeSp, _lit, -49);
                e.drawMode = SpriteDrawMode.Tiled;
                e.size = new Vector2(x1 - x0, eh);
                e.color = new Color(0.78f, 0.82f, 0.9f);
                e.transform.position = new Vector3((x0 + x1) * 0.5f, GroundY - eh * 0.05f, 0f);   // 그림 아래 55%가 바위, 그 위가 풀
                var fence = Spr("Fence", cliff, fenceSp, _lit, -48);
                fence.drawMode = SpriteDrawMode.Tiled;
                fence.size = new Vector2(f1 - f0, fh);
                fence.color = new Color(0.7f, 0.74f, 0.82f);
                fence.transform.position = new Vector3((f0 + f1) * 0.5f, GroundY + 0.1f + fh * 0.5f, 0f);
            }

            // ── 지붕 + 굴뚝 연기(바람에 왼쪽으로 찢긴다)
            var roofSp = Pixel(Art + "Exterior/px_roof.png");
            var roof = Spr("Roof", ext, roofSp, _lit, 26);
            float roofTop = 3 * (H + Slab) - 0.25f + 12.5f;
            roof.transform.position = new Vector3(total * 0.5f, roofTop - roofSp.bounds.size.y * 0.5f, 0f);
            float roofLeft = total * 0.5f - roofSp.bounds.size.x * 0.5f;
            foreach (float tx in new[] { 390f, 1175f })   // 그림 속 굴뚝 꼭대기(텍셀)
                Smoke(ext, new Vector3(roofLeft + tx / Tpu, roofTop - 95f / Tpu, 0f));
        }

        static ParticleSystem Rain(string name, Transform parent, int order, Material mat, float width, float rate, Vector2 speed, Vector2 size, Color color, bool local)
        {
            var ps = Particles(name, parent, mat, order, Mathf.CeilToInt(rate * 2.2f));
            ps.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);   // 바람이 왼쪽으로 분다(나무·연기와 같은 쪽)
            var m = ps.main;
            m.startLifetime = local ? 1.4f : 2f;
            m.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            m.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            m.startColor = color;
            m.simulationSpace = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission;
            em.rateOverTime = rate;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(width, 1f, 1f);
            sh.rotation = new Vector3(90f, 0f, 0f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.03f;
            r.lengthScale = 3.2f;
            return ps;
        }

        static void Smoke(Transform parent, Vector3 at)
        {
            var ps = Particles("ChimneySmoke", parent, ParticleMat("Smoke", Art + "FX/fx_glow.png", false), 27, 60);
            ps.transform.position = at;
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            m.startColor = new Color(0.32f, 0.34f, 0.4f, 0.55f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 4f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 12f;
            sh.radius = 0.15f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var f = ps.forceOverLifetime;
            f.enabled = true;
            f.x = new ParticleSystem.MinMaxCurve(-1.4f, -0.6f);
            f.y = new ParticleSystem.MinMaxCurve(0f, 0.2f);
            f.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 3.2f));
            FadeOut(ps, 0.2f);
        }

        // ───────────────────────── 방 ─────────────────────────

        static ManorWorld.RoomSlot BuildRoom(Transform root, RoomDef d, MysteryCase c, List<SpriteRenderer> windowGlows, List<Light2D> windowLights)
        {
            var box = Box(d);
            var go = new GameObject("Room_" + d.Id);
            go.transform.SetParent(root, false);
            // 방 그림: 520×325 도트(50텍셀/유닛) — 인물과 같은 도트 크기
            var back = Spr("Backdrop", go.transform, Pixel(Art + "Rooms/px_" + d.Id + ".png"), _lit, 0);
            back.transform.position = new Vector3(box.center.x, box.center.y, 0f);

            var dim = Spr("Dim", go.transform, M.LoadSprite(AlibiUi + "white.png", default, 32), _unlit, 20);
            dim.color = new Color(0.01f, 0.012f, 0.02f, 0f);
            NormalizeSpriteSize(dim, W + 0.02f, H + 0.02f);
            dim.transform.position = new Vector3(box.center.x, box.center.y, 0f);

            // 조명 · 빛 번짐 · 창문(번쩍임 + 유리창을 타고 흐르는 빗물) · 빛줄기 · 먼지 · 불티 · 김 · 물방울
            var fx = new GameObject("Light");
            fx.transform.SetParent(go.transform, false);
            foreach (var (kind, x, y) in d.Lights)
            {
                var p = ToWorld(d, x, y);
                var l = new GameObject("Light_" + kind).AddComponent<Light2D>();
                l.transform.SetParent(fx.transform, false);
                l.transform.position = new Vector3(p.x, p.y, 0f);
                l.lightType = Light2D.LightType.Point;
                (Color col, float inten, float rad, float glow, float flick) = kind switch
                {
                    "fire" or "stove" => (new Color(1f, 0.52f, 0.24f), 1.25f, 4.8f, 1.6f, 0.28f),
                    "chandelier" => (new Color(1f, 0.74f, 0.45f), 0.95f, 5.5f, 1.2f, 0.06f),
                    "lamp" => (new Color(1f, 0.7f, 0.42f), 0.75f, 3.4f, 0.8f, 0.08f),
                    "bulb" => (new Color(1f, 0.78f, 0.5f), 1.0f, 4.6f, 0.9f, 0.05f),
                    "cold" => (new Color(0.5f, 0.66f, 1f), 0.55f, 5.5f, 0f, 0f),
                    _ => (new Color(1f, 0.68f, 0.4f), 0.55f, 2.4f, 0.55f, 0.16f),
                };
                l.color = col;
                l.intensity = inten;
                l.pointLightOuterRadius = rad;
                l.pointLightInnerRadius = rad * 0.08f;
                l.falloffIntensity = 0.65f;
                TargetAllLayers(l);
                if (kind == "cold") { windowLights.Add(l); continue; }
                var g = Spr("Glow", l.transform, Sprite(Art + "FX/fx_glow.png", 256f, false, false, 256), _add, 4);
                g.color = new Color(col.r, col.g, col.b, 0.42f) * new Color(1.6f, 1.6f, 1.6f, 1f);
                g.transform.localScale = Vector3.one * glow;
                var flicker = l.gameObject.AddComponent<LightFlicker>();
                flicker.light2D = l;
                flicker.glow = g;
                flicker.amount = flick;
                flicker.speed = kind == "fire" || kind == "stove" ? 7f : 4f;
                if (kind == "fire") Embers(fx.transform, p);
            }
            var glassSp = Sprite(Art + "FX/fx_glassrain.png", Tpu, true, false, 512, true);
            var glassMat = ScrollMat("GlassRain", new Vector2(0f, 0.045f), false);
            foreach (var (x0, y0, x1, y1) in d.Windows)
            {
                var r = ToWorld(d, x0, y0, x1, y1);
                var wg = Spr("WindowFlash", fx.transform, Sprite(Art + "FX/fx_glow.png", 256f, false, false, 256), _add, 3);
                wg.color = new Color(0.78f, 0.88f, 1f, 0f);
                NormalizeSpriteSize(wg, r.width * 1.4f, r.height * 1.3f);
                wg.transform.position = new Vector3(r.center.x, r.center.y, 0f);
                windowGlows.Add(wg);
                var wl = new GameObject("WindowLight").AddComponent<Light2D>();
                wl.transform.SetParent(fx.transform, false);
                wl.transform.position = new Vector3(r.center.x, r.center.y - r.height * 0.2f, 0f);
                wl.lightType = Light2D.LightType.Point;
                wl.color = new Color(0.55f, 0.7f, 1f);
                wl.intensity = 0.25f;
                wl.pointLightOuterRadius = Mathf.Max(3.5f, r.height * 1.1f);
                wl.pointLightInnerRadius = 0.3f;
                wl.falloffIntensity = 0.7f;
                TargetAllLayers(wl);
                windowLights.Add(wl);
                TileSpr("GlassRain", fx.transform, glassSp, glassMat, 2, new Vector2(r.width, r.height), new Color(0.78f, 0.86f, 1f, 0.5f), Vector3.zero)
                    .transform.position = new Vector3(r.center.x, r.center.y, 0f);
            }
            foreach (var (x, y, angle, length, width) in d.Shafts)
            {
                var p = ToWorld(d, x, y);
                var sh = Spr("Shaft", fx.transform, Sprite(Art + "FX/fx_shaft.png", 100f, false, false, 512), _add, 6);
                sh.color = new Color(0.62f, 0.76f, 1f, 0.085f);
                NormalizeSpriteSize(sh, width, length);
                sh.transform.position = new Vector3(p.x, p.y - length * 0.5f, 0f);
                sh.transform.RotateAround(new Vector3(p.x, p.y, 0f), Vector3.forward, angle);
            }
            Dust(fx.transform, box);
            if (d.Id == "kitchen") Steam(fx.transform, Px(d, 80f, 150f));                      // 화덕 위 주전자 주둥이
            if (d.Id == "laundry") { Drips(fx.transform, Px(d, 338f, 68f), FloorY(d)); Drips(fx.transform, Px(d, 150f, 66f), FloorY(d)); }   // 배수구 위 · 계단 쪽 배관
            if (d.Id == "storage") { Drips(fx.transform, Px(d, 250f, 30f), FloorY(d)); Drips(fx.transform, Px(d, 470f, 40f), FloorY(d)); }

            // 시신: 노아(서재 책상에 엎드림) — 다른 인물과 같은 도트(1텍셀 = 1/50유닛)로 그린 스프라이트
            if (d.Id == "study")
            {
                var noah = Spr("Noah", go.transform, Pixel(Art + "Characters/Bodies/noah_slumped.png", Tpu, false, new Vector2(0.5f, 0f)), _lit, 2);
                var at = Px(d, 275f, 238f);   // 책상 윗면(텍셀 242)보다 살짝 위 · 책상 가운데
                noah.transform.position = new Vector3(at.x, at.y, 0f);
            }

            // 조건부 그림: 두 번째 살인 뒤 온실 바닥의 흰 천
            var overlays = new List<ManorWorld.OverlaySlot>();
            if (d.Id == "greenhouse")
            {
                var body = Spr("CoveredBody", go.transform, Pixel(Art + "Characters/Bodies/covered_body.png", Tpu, false, new Vector2(0.5f, 0f)), _lit, 2);
                var r = ToWorld(d, 0.52f, 0.72f, 0.74f, 0.875f);
                body.transform.position = new Vector3(r.center.x, FloorY(d) - 0.04f, 0f);
                body.gameObject.SetActive(false);
                overlays.Add(new ManorWorld.OverlaySlot { showIf = "dead:세이", go = body.gameObject });
            }

            // 저택도(M)에서만 보이는 방 이름
            var label = WorldText("Label", go.transform, $"{c.FloorLabel(d.Id)}  {c.RoomById(d.Id).Name}", 12f, Cream, new Vector3(box.center.x, box.yMax - 0.9f, 0f));
            label.SetActive(false);

            return new ManorWorld.RoomSlot
            {
                id = d.Id,
                box = box,
                floorY = FloorY(d),
                backdrop = back,
                dim = dim,
                hotspots = d.Hotspots.Select(h => new ManorWorld.HotspotSlot { id = h.id, rect = ToWorld(d, h.x0, h.y0, h.x1, h.y1) }).ToArray(),
                stairs = d.Stairs.Select(s => new ManorWorld.StairSlot { to = s.to, x = ToWorld(d, s.x, 0.5f).x, up = s.up }).ToArray(),
                overlays = overlays.ToArray(),
                label = label,
            };
        }

        static void Dust(Transform parent, Rect box)
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(box.center.x, box.center.y, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new Color(1f, 0.85f, 0.65f, 0.55f);
            main.maxParticles = 60;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 5f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(box.width * 0.95f, box.height * 0.85f, 0.1f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.25f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = AddMat("DustMat", Art + "FX/fx_dust.png");
            r.sortingOrder = 12;
        }

        /// <summary>벽난로 불티: 위로 날아오르며 흔들리다 꺼진다(블룸이 걸리게 HDR).</summary>
        static void Embers(Transform parent, Vector2 at)
        {
            var ps = Particles("Embers", parent, ParticleMat("Ember", Art + "FX/fx_dust.png", true, new Color(2.2f, 2.2f, 2.2f, 1f)), 7, 40);
            ps.transform.position = new Vector3(at.x, at.y - 0.2f, 0f);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.18f, 0.9f), new Color(1f, 0.78f, 0.4f, 0.9f));
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 7f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(0.9f, 0.1f, 0.1f);
            sh.rotation = new Vector3(-90f, 0f, 0f);
            sh.randomDirectionAmount = 0.2f;
            var n = ps.noise;
            n.enabled = true;
            n.strength = 0.5f;
            n.frequency = 0.8f;
            FadeOut(ps, 0.05f);
        }

        /// <summary>주전자 김: 천천히 오르며 퍼지고 사라진다.</summary>
        static void Steam(Transform parent, Vector2 at)
        {
            var ps = Particles("Steam", parent, ParticleMat("Steam", Art + "FX/fx_glow.png", false), 7, 30);
            ps.transform.position = new Vector3(at.x, at.y, 0f);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            m.startColor = new Color(0.85f, 0.88f, 0.92f, 0.24f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 5f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 15f;
            sh.radius = 0.04f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 3.5f));
            var n = ps.noise;
            n.enabled = true;
            n.strength = 0.15f;
            n.frequency = 0.6f;
            FadeOut(ps, 0.2f);
        }

        /// <summary>지하 천장에서 떨어지는 물방울 — 바닥에 닿으면 작게 튄다.</summary>
        static void Drips(Transform parent, Vector2 from, float floorY)
        {
            var mat = ParticleMat("Drip", Art + "FX/fx_rain.png", true);
            var ps = Particles("Drips", parent, mat, 7, 8);
            ps.transform.position = new Vector3(from.x, from.y, 0f);
            var m = ps.main;
            m.gravityModifier = 0.8f;
            m.startLifetime = Mathf.Sqrt(2f * Mathf.Max(0.5f, from.y - floorY) / (9.81f * 0.8f));
            m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
            m.startColor = new Color(0.7f, 0.82f, 1f, 0.65f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0.6f;
            var sh = ps.shape;
            sh.enabled = false;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1.5f;

            var splash = Particles("Splash", ps.transform, mat, 7, 24);
            var sm = splash.main;
            sm.playOnAwake = false;
            sm.prewarm = false;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.035f);
            sm.startColor = new Color(0.75f, 0.85f, 1f, 0.6f);
            sm.gravityModifier = 1f;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            var se = splash.emission;
            se.rateOverTime = 0f;
            se.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)3, (short)5) });
            var ssh = splash.shape;
            ssh.shapeType = ParticleSystemShapeType.Cone;
            ssh.angle = 50f;
            ssh.radius = 0.01f;
            ssh.rotation = new Vector3(-90f, 0f, 0f);
            var sub = ps.subEmitters;
            sub.enabled = true;
            sub.AddSubEmitter(splash, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
        }

        // ───────────────────────── 단면 구조(층 슬래브 · 칸막이 · 바깥벽 · 겉면) ─────────────────────────

        static void BuildStructure(Transform root, MysteryCase c)
        {
            var st = new GameObject("CutAway");
            st.transform.SetParent(root, false);
            var stone = Pixel(Art + "Exterior/px_stone.png");   // 잘린 벽 단면 = 돌 쌓기(4×4유닛 타일, 방과 같은 도트)
            var white = M.LoadSprite(AlibiUi + "white.png", default, 32);
            Color wall = new Color(0.62f, 0.58f, 0.56f);   // 2D 빛을 받아 어둡게 — 방 촛불 가까이선 따뜻하게 살아난다
            Color rim = new Color(0.55f, 0.42f, 0.3f, 0.7f);
            float total = 3 * W + 2 * Gap;

            void Block(string name, float x0, float y0, float x1, float y1, Color col, int order)
            {
                var s = Spr(name, st.transform, stone, _lit, order);
                s.drawMode = SpriteDrawMode.Tiled;
                s.size = new Vector2(x1 - x0, y1 - y0);
                s.color = col;
                s.transform.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, 0f);
            }
            void RimLine(float x0, float y, float x1)
            {
                var s = Spr("Rim", st.transform, white, _unlit, 25);
                NormalizeSpriteSize(s, x1 - x0, 0.04f);
                s.color = rim;
                s.transform.position = new Vector3((x0 + x1) * 0.5f, y, 0f);
            }

            // 층 사이 슬래브(지하 아래 바닥 · 각 층 천장)
            for (int row = 0; row <= 3; row++)
            {
                float yTop = (3 - row) * (H + Slab);         // row 0 = 지붕 아래 천장, row 3 = 지하 바닥 아래
                float y0 = yTop - Slab, y1 = yTop;
                float left = -Outer;
                if (row == 3) { y0 = -Slab; y1 = 0f; left = W; }      // 지하 바닥은 지하 방 아래에만(왼쪽은 바위)
                Block("Slab" + row, left, y0, total + Outer, y1, wall, 25);
                RimLine(left, y1, total + Outer);
                RimLine(left, y0, total + Outer);
            }
            // 바깥벽(왼쪽 2·1층, 오른쪽 전 층) · 지하 왼쪽은 바위
            Block("OuterLeft", -Outer, H + Slab, 0f, 3 * (H + Slab) - Slab, wall, 25);
            Block("OuterRight", total, 0f, total + Outer, 3 * (H + Slab) - Slab, wall, 25);
            Block("RockEdge", W, 0f, W + Gap, H, wall, 25);

            // 칸막이(같은 층 옆방): 문이 있으면 아래를 비운다
            foreach (var row in new[] { 0, 1, 2 })
            {
                for (int col = 0; col < 2; col++)
                {
                    var a = c.Rooms.FirstOrDefault(r => r.X == col && r.Y == row);
                    var b = c.Rooms.FirstOrDefault(r => r.X == col + 1 && r.Y == row);
                    if (a == null || b == null) continue;
                    float x0 = col * (W + Gap) + W, x1 = x0 + Gap;
                    float yb = (2 - row) * (H + Slab);
                    bool door = c.Adjacent(a.Id, b.Id);
                    float floor = yb + (1f - FeetQ) * H;
                    if (door)
                    {
                        float lintel = floor + 4.1f;
                        Block($"Lintel_{a.Id}_{b.Id}", x0, lintel, x1, yb + H, wall, 25);
                        Block($"Threshold_{a.Id}_{b.Id}", x0, yb, x1, floor - 0.02f, wall * 0.55f, 25);
                        // 문틀
                        var f1 = Spr("Frame", st.transform, white, _lit, 24);
                        NormalizeSpriteSize(f1, Gap + 0.24f, 0.12f);
                        f1.color = new Color(0.32f, 0.2f, 0.12f);
                        f1.transform.position = new Vector3((x0 + x1) * 0.5f, lintel, 0f);
                        // 문 너머 어둠(문 틈으로 보이는 깊이)
                        var voidS = Spr("DoorVoid", st.transform, white, _unlit, -1);
                        NormalizeSpriteSize(voidS, Gap, lintel - floor);
                        voidS.color = new Color(0.008f, 0.006f, 0.006f);
                        voidS.transform.position = new Vector3((x0 + x1) * 0.5f, (lintel + floor) * 0.5f, 0f);
                        // 문틈으로 새어 나오는 빛
                        var spill = Spr("DoorSpill", st.transform, Sprite(Art + "FX/fx_glow.png", 256f, false, false, 256), _add, 24);
                        spill.color = new Color(1f, 0.62f, 0.32f, 0.16f);
                        NormalizeSpriteSize(spill, 1.6f, (lintel - floor) * 1.1f);
                        spill.transform.position = new Vector3((x0 + x1) * 0.5f, (lintel + floor) * 0.5f, 0f);
                    }
                    else Block($"Wall_{a.Id}_{b.Id}", x0, yb, x1, yb + H, wall, 25);
                }
            }

            // ── 바깥벽 겉면: 랜턴(작은 불빛이 빗줄기를 비춘다) · 담쟁이 · 홈통과 쏟아지는 물 · 처마 물방울
            //    집과 바깥의 경계를 곧은 선이 아니라 '물건과 빛'으로 잇는다
            var face = new GameObject("Facade").transform;
            face.SetParent(st.transform, false);
            float top = 3 * (H + Slab);
            float lampUp = (1f - FeetQ) * H + 2.5f;
            foreach (int level in new[] { 1, 2 })   // 1층 · 2층(아래에서 센 칸)
            {
                float floorBase = level * (H + Slab);
                Lantern(face, new Vector2(-Outer - 0.62f, floorBase + lampUp), true);
                Lantern(face, new Vector2(total + Outer + 0.62f, floorBase + lampUp), false);
            }
            Ivy(face, new Vector2(-Outer + 0.05f, top - 0.15f), false);
            Ivy(face, new Vector2(total + Outer - 0.05f, top - 0.15f), true);
            Ivy(face, new Vector2(-Outer + 0.1f, 2 * (H + Slab) - 0.1f), true);
            Drainpipe(face, total + Outer + 0.16f, top - 0.3f, GroundY + 0.05f);
            EaveDrips(face, -2f, -Outer - 0.1f, top - 0.2f);
            EaveDrips(face, total + Outer + 0.35f, total + 2f, top - 0.2f);
        }

        /// <summary>바깥벽 랜턴: 빛은 바깥쪽 반원으로만(집 안으로 새지 않게) — 빗줄기(조명 받는 비)를 비춘다.</summary>
        static void Lantern(Transform parent, Vector2 at, bool leftWall)
        {
            var root = new GameObject("Lantern").transform;
            root.SetParent(parent, false);
            root.position = new Vector3(at.x, at.y, 0f);
            var s = Spr("Lamp", root, Pixel(Art + "Exterior/px_lantern.png"), _lit, 27);
            s.flipX = leftWall;   // 그림은 왼쪽에 벽걸이 — 왼쪽 벽이면 거울
            var l = new GameObject("Light").AddComponent<Light2D>();
            l.transform.SetParent(root, false);
            l.transform.localPosition = new Vector3(leftWall ? -0.36f : 0.36f, -0.08f, 0f);   // 유리 등
            l.transform.localRotation = Quaternion.Euler(0f, 0f, leftWall ? 90f : -90f);
            l.lightType = Light2D.LightType.Point;
            l.color = new Color(1f, 0.7f, 0.42f);
            l.intensity = 1.1f;
            l.pointLightOuterRadius = 4.2f;
            l.pointLightInnerRadius = 0.15f;
            l.pointLightOuterAngle = 200f;
            l.pointLightInnerAngle = 140f;
            l.falloffIntensity = 0.75f;
            TargetAllLayers(l);
            var g = Spr("Glow", l.transform, Sprite(Art + "FX/fx_glow.png", 256f, false, false, 256), GlowMat("LanternGlow", new Color(1.8f, 1.25f, 0.75f, 1f)), 28);
            g.color = new Color(1f, 1f, 1f, 0.55f);
            g.transform.localScale = Vector3.one * 1.8f;
            var flicker = l.gameObject.AddComponent<LightFlicker>();
            flicker.light2D = l;
            flicker.glow = g;
            flicker.amount = 0.12f;
            flicker.speed = 5f;
        }

        static void Ivy(Transform parent, Vector2 topAt, bool flip)
        {
            var s = Spr("Ivy", parent, Pixel(Art + "Exterior/px_ivy.png", Tpu, false, new Vector2(0.5f, 1f)), _lit, 27);
            s.flipX = flip;
            s.color = new Color(0.8f, 0.85f, 0.85f);
            s.transform.position = new Vector3(topAt.x, topAt.y, 0f);
        }

        /// <summary>홈통: 처마에서 땅까지 내려오는 쇠 관과 죔쇠, 아래 토출구에서 빗물이 쏟아진다.</summary>
        static void Drainpipe(Transform parent, float x, float yTop, float yBottom)
        {
            var white = M.LoadSprite(AlibiUi + "white.png", default, 32);
            void Bar(string n, float cx, float cy, float w, float h, Color col, int order)
            {
                var s = Spr(n, parent, white, _lit, order);
                NormalizeSpriteSize(s, w, h);
                s.color = col;
                s.transform.position = new Vector3(cx, cy, 0f);
            }
            float len = yTop - yBottom;
            Bar("Pipe", x, (yTop + yBottom) * 0.5f, 0.16f, len, new Color(0.13f, 0.14f, 0.17f), 27);
            Bar("PipeLight", x - 0.05f, (yTop + yBottom) * 0.5f, 0.04f, len, new Color(0.34f, 0.4f, 0.5f), 28);
            for (float y = yTop - 1.2f; y > yBottom + 0.8f; y -= 2.4f) Bar("Clamp", x - 0.04f, y, 0.3f, 0.08f, new Color(0.09f, 0.09f, 0.11f), 28);
            Bar("Gutter", x - 1.1f, yTop, 2.4f, 0.14f, new Color(0.12f, 0.13f, 0.16f), 28);
            Bar("Shoe", x + 0.14f, yBottom + 0.36f, 0.36f, 0.14f, new Color(0.13f, 0.14f, 0.17f), 28);
            var spout = Particles("Spout", parent, LitParticleMat("RainNear", Art + "FX/fx_rain.png"), 28, 80);
            spout.transform.position = new Vector3(x + 0.32f, yBottom + 0.36f, 0f);
            var m = spout.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.42f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.1f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
            m.startColor = new Color(0.8f, 0.88f, 1f, 0.7f);
            m.gravityModifier = 1.2f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = spout.emission;
            em.rateOverTime = 45f;
            var sh = spout.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 6f;
            sh.radius = 0.02f;
            sh.rotation = new Vector3(0f, 90f, 0f);   // +Z → +X: 오른쪽으로 쏟아진다
            var r = spout.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 1.2f;
        }

        /// <summary>처마 끝에서 떨어지는 굵은 물방울 — 바깥벽을 따라 땅까지.</summary>
        static void EaveDrips(Transform parent, float x0, float x1, float y)
        {
            var ps = Particles("EaveDrips", parent, LitParticleMat("RainNear", Art + "FX/fx_rain.png"), 27, 40);
            ps.transform.position = new Vector3((x0 + x1) * 0.5f, y, 0f);
            var m = ps.main;
            m.gravityModifier = 1.4f;
            m.startSpeed = 0f;
            m.startLifetime = Mathf.Sqrt(2f * (y - GroundY) / (9.81f * 1.4f));
            m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
            m.startColor = new Color(0.8f, 0.88f, 1f, 0.65f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 9f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(Mathf.Abs(x1 - x0), 0.05f, 0.05f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 1f;
        }

        // ───────────────────────── 전경(카메라 쪽 겹) ─────────────────────────

        static GameObject _foreground;

        static void BuildForeground(Transform root, Camera cam)
        {
            var fg = new GameObject("Foreground").transform;
            fg.SetParent(root, false);
            float total = 3 * W + 2 * Gap;

            // 칸막이(벽) 앞 소품 — 깊이 −1.5(배율 1.09): 방 안 조사 지점을 가리지 않게 칸막이 앞에만 세운다
            var props = Depth("Props", fg, cam, -1.5f);
            _foreground = props.gameObject;
            void Prop(string file, float x, float floorRow, float height)
            {
                var s = Spr(file, props, Pixel(Art + "Props/px_" + file + ".png", 54.6f), _lit, 30);
                s.color = new Color(0.75f, 0.75f, 0.8f);
                float floorY = (2 - floorRow) * (H + Slab);
                s.transform.localPosition = OnPlane(x, floorY + height * 0.5f - 1.1f);
            }
            float p1 = W + Gap * 0.5f, p2 = 2 * W + Gap * 1.5f;
            Prop("candelabra", p1, 0, 5.4f);
            Prop("column", p2, 0, 7.8f);
            Prop("column", p1, 1, 7.8f);
            Prop("column", p2, 1, 7.8f);
            Prop("crates", p2, 2, 3.4f);

            // 집 밖 가까운 전경 — 깊이 −4(배율 1.29): 바람에 꺾인 소나무와 풀 둔덕의 검은 실루엣.
            // 카메라가 움직이면 집보다 빨리 바깥쪽으로 밀려나 절대 방을 가리지 않는다(평면 x가 집 밖이면 화면에서도 늘 집 밖)
            var near = Depth("Near", fg, cam, -4f);
            var grassSp = Pixel(Art + "Exterior/px_cliff_edge.png", Tpu, true);
            const float k = Tpu / 64.5f;   // 같은 그림을 이 깊이에서도 화면 2픽셀 = 1텍셀로(텍셀 50 → 평면 64.5/유닛)
            float gh = grassSp.bounds.size.y * k;
            const float grassLine = 7.65f;
            foreach (var (x0, x1) in new[] { (-60f, -1.2f), (total + 2f, total + 60f) })
            {
                var g = Spr("Grass", near, grassSp, _lit, 32);
                g.drawMode = SpriteDrawMode.Tiled;
                g.size = new Vector2((x1 - x0) / k, grassSp.bounds.size.y);
                g.transform.localScale = new Vector3(k, k, 1f);
                g.flipX = true;
                g.color = new Color(0.16f, 0.18f, 0.22f);
                g.transform.localPosition = OnPlane((x0 + x1) * 0.5f, grassLine - gh * 0.05f);
            }
            var treeSp = Pixel(Art + "Exterior/px_tree.png", 64.5f, false, new Vector2(0.5f, 0f));
            foreach (float x in new[] { -1.6f, total + 3.2f })
            {
                var t = Spr("Pine", near, treeSp, _lit, 31);
                t.color = new Color(0.3f, 0.33f, 0.4f);
                t.transform.localPosition = OnPlane(x, grassLine - 0.5f);
                var sway = t.gameObject.AddComponent<Sway>();
                sway.degrees = 0.8f;
                sway.speed = 0.45f;
            }
        }

        // ───────────────────────── 인물 ─────────────────────────

        static PixelActor Actor(GameObject go, string name, Sprite[] frames, int[] idle, int[] walk, int order)
        {
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(go.transform, false);
            bodyGo.transform.localScale = Vector3.one * CharScale;
            var sr = bodyGo.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = _lit;
            sr.sortingOrder = order;
            sr.sprite = frames.FirstOrDefault(f => f != null);
            // 발밑 접지 그림자 — 평평한 도트 인물을 바닥에 붙인다(REPLACED가 작은 조명과 그림자로 스프라이트를 세우듯)
            var shadow = Spr("Shadow", go.transform, Sprite(Art + "FX/fx_contact_shadow.png", 100f, false, false, 128), _unlit, order - 1);
            shadow.color = new Color(0f, 0f, 0f, 0.55f);
            NormalizeSpriteSize(shadow, 1.1f, 0.2f);
            shadow.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            var a = go.AddComponent<PixelActor>();
            a.actorName = name;
            a.body = sr;
            a.shadow = shadow;
            a.idle = idle.Select(i => frames[i]).ToArray();
            a.walk = walk.Select(i => frames[i]).ToArray();
            // 걷는 속도는 걷기 그림의 보폭에 맞춘다(발이 미끄러지지 않게): 탐정 = 다시 그린 긴 보폭(한 주기 1.2유닛), 손님 = 잼 원본(0.74유닛)
            a.walkStride = name == "탐정" ? 1.2f : 0.74f;
            a.speed = name == "탐정" ? 2.4f : 1.45f;
            var tag = WorldText("NameTag", go.transform, name == "탐정" ? "▼ 탐정" : name, 7f, name == "탐정" ? Amber : Cream, new Vector3(0f, name == "탐정" ? 3f : 2.62f, 0f));
            tag.SetActive(false);
            a.nameTag = tag;
            return a;
        }

        // ───────────────────────── UI ─────────────────────────

        static GameObject BuildUi(Camera cam, out ManorHud hud, out InterrogationView talk, out CanvasGroup bookGroup, out CaseBookView book,
            out EvidenceViewer viewer, out ReaderView reader, out TheoryView theory, out MysteryHud cut, out RectTransform shake)
        {
            var s = new M.Sprites();
            var canvasGo = new GameObject("ManorCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 5f;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            shake = (RectTransform)M.Stretch("ShakeRoot", root).transform;
            talk = BuildTalk(shake, s);
            hud = BuildHud(shake, s);
            // 암전은 월드·HUD만 덮는다(그 위에 수첩·컷신·이야기·장 제목이 뜬다 — 클릭도 그쪽이 받는다)
            var fade = M.Stretch("Fade", root);
            M.Img(fade, Color.black).raycastTarget = true;
            hud.fadeGroup = fade.AddComponent<CanvasGroup>();
            hud.fadeGroup.alpha = 0f;
            fade.SetActive(false);

            var bookRoot = M.Stretch("BookOverlay", root);
            bookGroup = bookRoot.AddComponent<CanvasGroup>();
            var dim = M.Img(bookRoot, new Color(0.01f, 0.01f, 0.02f, 0.62f));
            dim.raycastTarget = true;
            book = M.BuildBook(bookRoot.transform, s);
            M.Place(book.gameObject, 1160, 36, 730, 1008);
            Txt(UI("Close", bookRoot.transform, 1170, 1046, 700, 28), "TAB · ESC  닫기", _hud, 15, Muted, TextAlignmentOptions.MidlineRight);
            bookRoot.SetActive(false);

            viewer = M.BuildViewer(root, s);
            reader = M.BuildReader(root, s);
            theory = M.BuildTheory(root, s);
            var cutGo = M.Stretch("Cutscenes", root);
            cut = cutGo.AddComponent<MysteryHud>();
            M.BuildObjection(cutGo.transform, s, cut);
            M.BuildMurder(cutGo.transform, s, cut);
            M.BuildStory(cutGo.transform, s, cut);
            M.BuildReport(cutGo.transform, s, cut);
            foreach (var g in new[] { cut.objectionGroup, cut.murderGroup })
            {
                g.alpha = 0f;
                g.gameObject.SetActive(false);
            }
            // 장 제목은 맨 위
            BuildChapter(root, hud);
            return canvasGo;
        }

        static GameObject UI(string name, Transform parent, float x, float y, float w, float h) => M.UI(name, parent, x, y, w, h);
        static TMP_Text Txt(GameObject go, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align) => M.Txt(go, text, font, size, color, align);

        /// <summary>앵커를 직접 정해 놓는다(레터박스 띠처럼 크기가 변하는 부모 안에서).</summary>
        static RectTransform Anchored(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static ManorHud BuildHud(Transform parent, M.Sprites s)
        {
            var go = M.Stretch("Hud", parent);
            var hud = go.AddComponent<ManorHud>();

            // 위 띠
            var top = new GameObject("TopBar", typeof(RectTransform));
            top.transform.SetParent(go.transform, false);
            var trt = (RectTransform)top.transform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
            trt.sizeDelta = new Vector2(0, hud.topNormal);
            trt.anchoredPosition = Vector2.zero;
            M.Img(top, Color.black).raycastTarget = false;
            hud.topBar = trt;
            var topContent = Anchored("Content", top.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1920, 112));
            hud.topGroup = topContent.gameObject.AddComponent<CanvasGroup>();
            var watch = M.Img(Anchored("Watch", topContent, new Vector2(0, 0), new Vector2(0, 0), new Vector2(34, 18), new Vector2(74, 74)).gameObject, Color.white, s.Watch);
            watch.preserveAspect = true;
            watch.raycastTarget = false;
            hud.hourHand = Hand("HourHand", watch.transform, s.HourHand, 5, 22);
            hud.minuteHand = Hand("MinuteHand", watch.transform, s.MinuteHand, 5, 29);
            hud.clockText = M.Txt(Anchored("Clock", topContent, new Vector2(0, 0), new Vector2(0, 0), new Vector2(118, 42), new Vector2(220, 52)).gameObject, "23:45", _hudBold, 40, Amber, TextAlignmentOptions.MidlineLeft);
            hud.dawnText = M.Txt(Anchored("Dawn", topContent, new Vector2(0, 0), new Vector2(0, 0), new Vector2(120, 16), new Vector2(360, 28)).gameObject, "", M._serif, 16, Muted, TextAlignmentOptions.MidlineLeft);
            hud.objectiveText = M.Txt(Anchored("Objective", topContent, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-44, 26), new Vector2(900, 52)).gameObject, "", M._serif, 19, Cream, TextAlignmentOptions.MidlineRight);

            // 아래 띠
            var bottom = new GameObject("BottomBar", typeof(RectTransform));
            bottom.transform.SetParent(go.transform, false);
            var brt = (RectTransform)bottom.transform;
            brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(1, 0); brt.pivot = new Vector2(0.5f, 0);
            brt.sizeDelta = new Vector2(0, hud.bottomNormal);
            brt.anchoredPosition = Vector2.zero;
            M.Img(bottom, Color.black).raycastTarget = false;
            hud.bottomBar = brt;
            var sub = Anchored("Subtitle", bottom.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -14), new Vector2(1320, 120));
            hud.speakerText = M.Txt(Anchored("Speaker", sub, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1320, 34)).gameObject, "", _hud, 21, Amber, TextAlignmentOptions.Center);
            hud.subtitleText = M.Txt(Anchored("Line", sub, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(1320, 86)).gameObject, "", M._serif, 25, new Color(0.96f, 0.93f, 0.88f), TextAlignmentOptions.Top);
            hud.subtitleText.lineSpacing = -6;
            var tw = sub.gameObject.AddComponent<AlibiTypewriter>();
            tw.target = hud.subtitleText;
            tw.charsPerSecond = 46f;
            hud.typewriter = tw;
            var bottomContent = Anchored("Content", bottom.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1920, 60));
            hud.bottomGroup = bottomContent.gameObject.AddComponent<CanvasGroup>();
            hud.roomText = M.Txt(Anchored("Room", bottomContent, new Vector2(0, 0), new Vector2(0, 0), new Vector2(120, 16), new Vector2(360, 30)).gameObject, "", _hud, 17, new Color(0.78f, 0.72f, 0.64f), TextAlignmentOptions.MidlineLeft);
            hud.hintText = M.Txt(Anchored("Hints", bottomContent, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-44, 16), new Vector2(1100, 30)).gameObject, "", _hud, 16, new Color(0.7f, 0.66f, 0.6f), TextAlignmentOptions.MidlineRight);
            // 단면 미니맵(3×3, 2층이 위)
            var cells = new Image[9];
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    var cell = M.Img(Anchored($"Cell{row}{col}", bottomContent, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40 + col * 24, 12 + (2 - row) * 13), new Vector2(21, 10)).gameObject, new Color(0.25f, 0.23f, 0.24f, 0.9f), s.White);
                    cell.raycastTarget = false;
                    if (row == 2 && col == 0) cell.color = new Color(0.12f, 0.11f, 0.11f, 0.9f);
                    cells[row * 3 + col] = cell;
                }
            hud.miniCells = cells;

            // 물건 앞 표식
            var prompt = Anchored("Prompt", go.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(420, 44));
            var pbg = M.Img(prompt.gameObject, new Color(0.02f, 0.02f, 0.03f, 0.72f), Sprite(Art + "FX/ui_panel.png", 100f, false, false, 64));
            pbg.type = Image.Type.Sliced;
            pbg.raycastTarget = false;
            var fit = prompt.gameObject.AddComponent<HorizontalLayoutGroup>();
            fit.padding = new RectOffset(14, 16, 6, 6);
            fit.spacing = 10;
            fit.childAlignment = TextAnchor.MiddleCenter;
            fit.childControlWidth = true; fit.childControlHeight = true;
            fit.childForceExpandWidth = false; fit.childForceExpandHeight = false;
            prompt.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var dia = M.Img(UI("Diamond", prompt, 0, 0, 18, 18), Color.white, Sprite(Art + "FX/ui_diamond_fill.png", 100f, false, false, 64));
            dia.raycastTarget = false;
            dia.gameObject.AddComponent<LayoutElement>().preferredWidth = 16;
            hud.promptDiamond = dia;
            hud.promptLabel = M.Txt(UI("Label", prompt, 0, 0, 200, 30), "물건", M._serifBold, 19, Color.white, TextAlignmentOptions.MidlineLeft);
            hud.promptLabel.textWrappingMode = TextWrappingModes.NoWrap;
            hud.promptKey = M.Txt(UI("Key", prompt, 0, 0, 60, 30), "E", _hud, 16, Amber, TextAlignmentOptions.MidlineLeft);
            hud.promptKey.textWrappingMode = TextWrappingModes.NoWrap;
            hud.prompt = prompt;

            // 돋보기 표식(E) — 꺼 둔 원본 하나를 필요한 만큼 복제해 쓴다
            var markers = M.Stretch("Markers", go.transform);
            hud.markerRoot = (RectTransform)markers.transform;
            var mt = Anchored("MarkerTemplate", markers.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 56));
            var mback = M.Img(UI("Back", mt, -10, -10, 76, 76), new Color(0f, 0f, 0f, 0.55f), Sprite(Art + "FX/fx_glow.png", 256f, false, false, 256));
            mback.raycastTarget = false;
            var micon = M.Img(UI("Icon", mt, 8, 8, 40, 40), Color.white, Sprite(Art + "FX/ui_lens.png", 100f, true, false, 64));
            micon.raycastTarget = false;
            hud.markerTemplate = mt;
            hud.markerLook = micon.sprite;
            hud.markerStairUp = Sprite(Art + "FX/ui_stair_up.png", 100f, true, false, 64);
            hud.markerStairDown = Sprite(Art + "FX/ui_stair_down.png", 100f, true, false, 64);
            mt.gameObject.SetActive(false);

            // 살펴보기 사진(왼쪽 — 대화 때 초상이 서는 자리): 폴라로이드 틀 · 사진(320×240 도트를 2배로) · 셔터 플래시 · 테이프 · 손글씨 이름 · 시각 도장
            var photo = Anchored("Photo", go.transform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(440f, 34f), new Vector2(680f, 600f));
            hud.photoRect = photo;
            hud.photoGroup = photo.gameObject.AddComponent<CanvasGroup>();
            hud.photoGroup.blocksRaycasts = false;
            hud.photoGroup.interactable = false;
            var pshadow = M.Img(UI("Shadow", photo, 14, 18, 680, 600), new Color(0f, 0f, 0f, 0.5f), s.White);
            pshadow.raycastTarget = false;
            var frame = M.Img(UI("Frame", photo, 0, 0, 680, 600), Color.white, Sprite(Art + "FX/ui_polaroid.png", 100f, true, false, 512));
            frame.raycastTarget = false;
            hud.photoImage = M.Img(UI("Picture", photo, 20, 20, 640, 480), Color.white);
            hud.photoImage.raycastTarget = false;
            hud.photoFlash = M.Img(UI("Flash", photo, 20, 20, 640, 480), new Color(1f, 1f, 1f, 0f), s.White);
            hud.photoFlash.raycastTarget = false;
            var tape = M.Img(UI("Tape", photo, 284, -16, 112, 36), Color.white, Sprite(Art + "FX/ui_tape.png", 100f, true, false, 128));
            tape.raycastTarget = false;
            tape.rectTransform.localEulerAngles = new Vector3(0, 0, 4f);
            hud.photoCaption = Txt(UI("Caption", photo, 30, 506, 620, 84), "", M._brush, 44, new Color(0.17f, 0.14f, 0.12f), TextAlignmentOptions.Center);
            hud.photoStamp = Txt(UI("Stamp", photo, 470, 452, 180, 40), "", _hud, 22, new Color(1f, 0.6f, 0.25f, 0.92f), TextAlignmentOptions.MidlineRight);
            photo.gameObject.SetActive(false);

            // 알림
            var toast = Anchored("Toast", go.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -128), new Vector2(620, 48));
            var tbg = M.Img(toast.gameObject, new Color(0.02f, 0.02f, 0.03f, 0.7f), Sprite(Art + "FX/ui_panel.png", 100f, false, false, 64));
            tbg.type = Image.Type.Sliced;
            tbg.raycastTarget = false;
            hud.toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            hud.toastText = M.Txt(UI("Text", toast, 16, 4, 588, 40), "알림", M._serif, 17, Cream, TextAlignmentOptions.MidlineRight);
            return hud;
        }

        static RectTransform Hand(string name, Transform watch, Sprite sprite, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(watch, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.44f);
            rt.pivot = new Vector2(0.5f, 40f / 512f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return rt;
        }

        static GameObject BuildChapter(Transform root, ManorHud hud)
        {
            var go = M.Stretch("Chapter", root);
            hud.chapterGroup = go.AddComponent<CanvasGroup>();
            M.Img(go, new Color(0f, 0f, 0f, 0.94f)).raycastTarget = true;
            hud.chapterTitle = M.Txt(UI("Title", go.transform, 0, 420, 1920, 130), "제1장", _hudBold, 84, Cream, TextAlignmentOptions.Center);
            hud.chapterTitle.characterSpacing = 6;
            M.Line(go.transform, 860, 560, 200, 2, new Color(Amber.r, Amber.g, Amber.b, 0.7f), new M.Sprites());
            hud.chapterSub = M.Txt(UI("Sub", go.transform, 0, 580, 1920, 44), "", M._serif, 22, Muted, TextAlignmentOptions.Center);
            hud.chapterSub.characterSpacing = 4;
            go.SetActive(false);
            return go;
        }

        static InterrogationView BuildTalk(Transform parent, M.Sprites s)
        {
            var go = M.Stretch("Interrogation", parent);
            var view = go.AddComponent<InterrogationView>();
            view.group = go.AddComponent<CanvasGroup>();
            var fadeH = Sprite(Art + "FX/ui_fade_h.png", 100f, false, false, 256);
            var fadeV = Sprite(Art + "FX/ui_fade_v.png", 100f, false, false, 256);

            // 왼쪽: 큰 초상(어둠 속에서 떠오른다)
            var pGo = UI("Portrait", go.transform, 0, 80, 760, 1000);
            var prt = (RectTransform)pGo.transform;
            prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(380, -1080);
            view.portraitRect = prt;
            view.portraitGroup = pGo.AddComponent<CanvasGroup>();
            view.portrait = M.Img(pGo, Color.white);
            view.portrait.preserveAspect = true;
            view.portrait.raycastTarget = false;
            var leftShade = M.Img(UI("LeftShade", go.transform, 0, 0, 980, 1080), new Color(0, 0, 0, 0.82f), fadeH);
            leftShade.raycastTarget = false;
            leftShade.transform.SetSiblingIndex(0);

            // 오른쪽: 진술 · 질문
            var panelBg = M.Img(UI("PanelShade", go.transform, 1920, 0, 1000, 1080), new Color(0, 0, 0, 0.9f), fadeH);
            panelBg.rectTransform.localScale = new Vector3(-1, 1, 1);   // 왼쪽 위 피벗이라 뒤집으면 920..1920을 덮는다
            panelBg.raycastTarget = false;
            view.redFlash = M.Img(M.Stretch("RedFlash", go.transform), new Color(0.62f, 0.05f, 0.1f, 0f));
            view.redFlash.raycastTarget = false;
            view.nameText = Txt(UI("Name", go.transform, 1210, 160, 680, 70), "이름", _hudBold, 56, Cream, TextAlignmentOptions.MidlineLeft);
            view.roleText = Txt(UI("Role", go.transform, 1214, 228, 680, 30), "", _hud, 21, Amber, TextAlignmentOptions.MidlineLeft);
            view.bioText = Txt(UI("Bio", go.transform, 1214, 262, 660, 46), "", M._serif, 17, Muted, TextAlignmentOptions.TopLeft);
            Txt(UI("StatementsTitle", go.transform, 1210, 316, 200, 50), "진술", M._brush, 44, Bright, TextAlignmentOptions.MidlineLeft);
            view.statementHint = Txt(UI("Hint", go.transform, 1360, 330, 520, 30), "", M._serif, 15, Muted, TextAlignmentOptions.MidlineRight);
            var list = UI("Statements", go.transform, 1200, 372, 690, 340);
            var vlg = list.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var row = UI("StatementTemplate", list.transform, 0, 0, 690, 60);
            var rowHit = M.Img(row, Clear);
            var rowBtn = M.AddButton(row, rowHit);
            var rcb = rowBtn.colors;
            rcb.normalColor = Clear; rcb.highlightedColor = new Color(1, 1, 1, 0.08f); rcb.pressedColor = new Color(1, 1, 1, 0.14f); rcb.selectedColor = Clear; rcb.disabledColor = Clear;
            rowBtn.colors = rcb;
            row.AddComponent<LayoutElement>().preferredHeight = 60;
            var frame = M.Img(M.Stretch("Frame", row.transform), Clear, s.TagCrimson);
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = false;
            Txt(UI("Text", row.transform, 14, 4, 590, 52), "「진술」", M._serif, 18, Cream, TextAlignmentOptions.MidlineLeft);
            var mark = Txt(UI("Mark", row.transform, 600, 6, 88, 48), "", M._brush, 26, Bright, TextAlignmentOptions.Center);
            mark.rectTransform.localEulerAngles = new Vector3(0, 0, -10);
            view.statementContent = (RectTransform)list.transform;
            view.statementTemplate = rowBtn;

            var chips = UI("Chips", go.transform, 1200, 722, 690, 44);
            var hlg = chips.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            var chip = UI("ChipTemplate", chips.transform, 0, 0, 160, 44);
            var chipImg = M.Img(chip, Color.white, s.TagDark);
            chipImg.type = Image.Type.Sliced;
            var chipBtn = M.AddButton(chip, chipImg);
            var chipLayout = chip.AddComponent<HorizontalLayoutGroup>();
            chipLayout.padding = new RectOffset(16, 16, 6, 6);
            chipLayout.childControlWidth = true; chipLayout.childControlHeight = true;
            chipLayout.childForceExpandWidth = false; chipLayout.childForceExpandHeight = true;
            var chipLabel = Txt(UI("Label", chip.transform, 0, 0, 140, 32), "노아와의 관계", M._serif, 15, Cream, TextAlignmentOptions.Center);
            chipLabel.textWrappingMode = TextWrappingModes.NoWrap;
            view.chipContent = (RectTransform)chips.transform;
            view.chipTemplate = chipBtn;

            var inputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            inputGo.name = "Input";
            inputGo.transform.SetParent(go.transform, false);
            M.Place(inputGo, 1200, 776, 530, 44);
            var inputBg = inputGo.GetComponent<Image>();
            inputBg.sprite = s.TagDark;
            inputBg.type = Image.Type.Sliced;
            inputBg.color = new Color(1, 1, 1, 0.9f);
            var input = inputGo.GetComponent<TMP_InputField>();
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 120;
            input.onFocusSelectAll = false;
            foreach (var t in inputGo.GetComponentsInChildren<TMP_Text>(true))
            {
                t.font = M._serif;
                t.fontSize = 18;
                t.alignment = TextAlignmentOptions.MidlineLeft;
            }
            input.textComponent.color = Cream;
            var ph = (TMP_Text)input.placeholder;
            ph.text = "직접 물어보기 — 무엇이든 (Enter)";
            ph.color = new Color(0.66f, 0.6f, 0.53f, 0.8f);
            ph.fontStyle = FontStyles.Italic;
            input.pointSize = 18;
            view.input = input;
            view.askButton = M.Btn("AskButton", go.transform, 1740, 776, 150, 44, "묻기", _hud, 18, s.TagCrimson, Cream, out _);
            view.evidenceButton = M.Btn("EvidenceButton", go.transform, 1200, 830, 230, 40, "증거 수첩  TAB", _hud, 15, s.TagDark, Cream, out _);
            view.backButton = M.Btn("BackButton", go.transform, 1660, 830, 230, 40, "물러나기  ESC", _hud, 15, s.TagDark, Cream, out _);

            var confront = UI("Confront", go.transform, 1210, 96, 680, 60);
            view.confrontRoot = confront;
            Txt(UI("Title", confront.transform, 0, 0, 440, 60), "최후의 추궁", M._brush, 54, Bright, TextAlignmentOptions.MidlineLeft);
            var marks = new List<Image>();
            for (int i = 0; i < 3; i++)
            {
                var m = M.Img(UI("Resolve" + i, confront.transform, 470 + i * 38, 16, 30, 30), Bright, s.Seal);
                m.preserveAspect = true;
                m.raycastTarget = false;
                marks.Add(m);
            }
            view.resolveMarks = marks.ToArray();
            confront.SetActive(false);
            go.SetActive(false);
            view.group.alpha = 0f;
            return view;
        }

        // ───────────────────────── 소리 도우미 ─────────────────────────

        /// <summary>그 자리에서 나는 3D 소리(귀는 저택 평면 위라 화면에서 보이는 거리대로 작아지고 좌우로 갈린다).</summary>
        static AudioSource Source3D(GameObject go, float min, float max)
        {
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = min;
            src.maxDistance = max;
            src.dopplerLevel = 0f;
            src.spread = 70f;
            return src;
        }

        static SoundEmitter Emitter(Transform parent, string name, Vector2 at, AudioClip loopClip, float volume, float min, float max)
        {
            var go = new GameObject("Sound_" + name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(at.x, at.y, 0f);
            var src = Source3D(go, min, max);
            src.clip = loopClip;
            var e = go.AddComponent<SoundEmitter>();
            e.loop = loopClip != null;
            e.volume = volume;
            return e;
        }

        // ───────────────────────── 깊이 겹 · 흐르는 겹 · 입자 도우미 ─────────────────────────

        /// <summary>깊이 겹 하나(depth: 집 평면 0 기준 뒤로 +, 카메라 쪽 −). infinite면 하늘처럼 화면에 붙어 drift만큼만 흐른다.</summary>
        static Transform Depth(string name, Transform parent, Camera cam, float depth, bool infinite = false, float drift = 0.02f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var dl = go.AddComponent<DepthLayer>();
            dl.cam = cam;
            dl.depth = depth;
            dl.infinite = infinite;
            dl.infiniteDrift = drift;
            dl.reference = Ref;
            return go.transform;
        }

        /// <summary>깊이 겹 안에서 '그 평면'의 한 점(월드와 같은 눈금) → 겹 안 자리.</summary>
        static Vector3 OnPlane(float x, float y) => new Vector3(x - Ref.x, y - Ref.y, 0f);

        /// <summary>도트 방 그림(520×325)의 텍셀 좌표 → 월드.</summary>
        static Vector2 Px(RoomDef d, float tx, float ty)
        {
            var b = Box(d);
            return new Vector2(b.xMin + tx / Tpu, b.yMax - ty / Tpu);
        }

        /// <summary>반복해 까는 스프라이트(흐르는 재질이면 이어진 채로 흐른다).</summary>
        static SpriteRenderer TileSpr(string name, Transform parent, Sprite sprite, Material mat, int order, Vector2 size, Color color, Vector3 localPos)
        {
            var s = Spr(name, parent, sprite, mat, order);
            s.drawMode = SpriteDrawMode.Tiled;
            s.tileMode = SpriteTileMode.Continuous;
            s.size = size;
            s.color = color;
            s.transform.localPosition = localPos;
            return s;
        }

        static Material MatAsset(string name, string shader)
        {
            Directory.CreateDirectory(Art + "Materials");
            string path = Art + "Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null || mat.shader.name != shader)
            {
                if (mat != null) AssetDatabase.DeleteAsset(path);
                mat = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        /// <summary>흐르는 겹 재질(구름·바다·안개·유리창 빗물): 텍스처가 반복되며 scroll(uv/초)만큼 흐른다. additive면 가산, 아니면 알파.</summary>
        static Material ScrollMat(string name, Vector2 scroll, bool additive, Color? tint = null)
        {
            var mat = MatAsset(name, "MafiaAI/Manor/Scroll");
            mat.SetVector("_Scroll", new Vector4(scroll.x, scroll.y, 0f, 0f));
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetColor("_Color", tint ?? Color.white);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>빛 번짐(가산) 재질 — 색을 1보다 크게(HDR) 주면 블룸이 걸린다.</summary>
        static Material GlowMat(string name, Color hdr)
        {
            var mat = MatAsset(name, "MafiaAI/Manor/Additive");
            mat.SetColor("_Color", hdr);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>입자용 재질(텍스처를 재질에 직접). 텍스처 임포트 설정은 건드리지 않는다(같은 그림을 스프라이트로도 쓰니까).</summary>
        static Material ParticleMat(string name, string texPath, bool additive, Color? tint = null)
        {
            var mat = ScrollMat(name, Vector2.zero, additive, tint);
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>2D 빛을 받는 입자 재질 — 어둠 속에선 거의 안 보이다가 랜턴 둘레와 번개 때 빛난다.</summary>
        static Material LitParticleMat(string name, string texPath)
        {
            var mat = MatAsset(name, _lit.shader.name);
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static ParticleSystem Particles(string name, Transform parent, Material mat, int order, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.maxParticles = max;
            main.prewarm = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.sortingOrder = order;
            return ps;
        }

        static void FadeOut(ParticleSystem ps, float fadeIn)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        /// <summary>스프라이트 마스크(사각형) — 이 안에서는 '바깥에서만 보임' 렌더러가 가려진다.</summary>
        static void Mask(string name, Transform parent, Rect r)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var m = go.AddComponent<SpriteMask>();
            m.sprite = M.LoadSprite(AlibiUi + "white.png", default, 32);
            var size = m.sprite.bounds.size;
            go.transform.position = new Vector3(r.center.x, r.center.y, 0f);
            go.transform.localScale = new Vector3(r.width / size.x, r.height / size.y, 1f);
        }

        // ───────────────────────── 저작 도우미 ─────────────────────────

        static SpriteRenderer Spr(string name, Transform parent, Sprite sprite, Material mat, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = mat;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>스프라이트가 월드에서 w×h가 되도록 크기를 맞춘다.</summary>
        static void NormalizeSpriteSize(SpriteRenderer sr, float w, float h)
        {
            if (sr.sprite == null) return;
            var size = sr.sprite.bounds.size;
            sr.transform.localScale = new Vector3(w / size.x, h / size.y, 1f);
        }

        static Material AddMat(string name, string texPath)
        {
            string path = Art + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("MafiaAI/Manor/Additive"));
                AssetDatabase.CreateAsset(mat, path);
            }
            Sprite(texPath, 100f, false, false, 256);
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static GameObject WorldText(string name, Transform parent, string text, float size, Color color, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var t = go.AddComponent<TextMeshPro>();
            t.font = _hudBold;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.outlineWidth = 0.2f;
            t.outlineColor = new Color32(0, 0, 0, 220);
            t.rectTransform.sizeDelta = new Vector2(12f, 2f);
            t.GetComponent<MeshRenderer>().sortingOrder = 40;
            return go;
        }

        static void TargetAllLayers(Light2D light)
        {
            var so = new SerializedObject(light);
            var prop = so.FindProperty("m_ApplyToSortingLayers");
            if (prop == null) return;
            var layers = SortingLayer.layers;
            prop.arraySize = layers.Length;
            for (int i = 0; i < layers.Length; i++) prop.GetArrayElementAtIndex(i).intValue = layers[i].id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
