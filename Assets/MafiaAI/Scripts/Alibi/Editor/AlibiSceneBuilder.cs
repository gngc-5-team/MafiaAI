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
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using MafiaAI.Alibi.UI;
using Object = UnityEngine.Object;

namespace MafiaAI.Alibi.EditorTools
{
    /// <summary>
    /// 알리바이 심문 씬을 '한 번' 저작한다(v2: 무대 + 대사창 + 수사 수첩 + 컷신). 만든 뒤엔 하이라키·인스펙터에서 직접 고친다
    /// (코드는 위치·크기·폰트를 건드리지 않는다). 씬이 이미 있으면 덮어쓰기 전에 묻는다 — 사람이 다듬은 값을 지우지 않게.
    ///
    /// 화풍: 잼 아트(검정·진홍 고딕 누아르, 아이보리 장식 액자, 촛불 복도) + 코덱스 소재(양피지·가죽·붉은 먹·회중시계).
    /// 서체: 본명조(대사·제목) · 나눔손글씨 펜(수첩 손글씨) · 나눔손글씨 붓(모순!·두 번째 비명).
    /// </summary>
    public static class AlibiSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/AlibiPrototype.unity";
        const string Art = "Assets/MafiaAI/Art/Alibi/";
        const string Ui = Art + "UI/";
        const string Codex = Art + "Codex/";
        const string Fonts = Art + "Fonts/";
        const string Old = "Assets/sprites/UI/";

        static readonly Color Ink = new Color(0.17f, 0.12f, 0.09f);
        static readonly Color Cream = new Color(0.94f, 0.9f, 0.82f);
        static readonly Color Muted = new Color(0.66f, 0.6f, 0.53f);
        static readonly Color Crimson = new Color(0.62f, 0.11f, 0.19f);
        static readonly Color Bright = new Color(0.86f, 0.2f, 0.27f);
        static readonly Color Amber = new Color(0.9f, 0.74f, 0.46f);
        static readonly Color Brown = new Color(0.42f, 0.33f, 0.24f);
        static readonly Color Clear = new Color(1, 1, 1, 0);
        static readonly Color Hover = new Color(0.62f, 0.38f, 0.2f, 0.16f);   // 진술표 칸에 마우스를 올렸을 때만 보이는 연한 얼룩

        static TMP_FontAsset _serif, _serifBold, _pen, _brush;

        [MenuItem("MafiaAI/알리바이/프로토타입 씬 만들기")]
        static void BuildMenu()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("알리바이 프로토타입", "씬이 이미 있습니다.\n새로 만들면 하이라키에서 직접 고친 내용이 사라집니다. 덮어쓸까요?", "덮어쓰기", "취소"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        public static void BuildBatch()
        {
            bool overwrite = Environment.GetCommandLineArgs().Contains("-alibiOverwrite");
            if (File.Exists(ScenePath) && !overwrite) { Debug.Log("[AlibiBuilder] 씬이 이미 있어 건너뜀(덮어쓰려면 -alibiOverwrite)"); return; }
            Build();
        }

        /// <summary>MCP·스크립트에서 묻지 않고 다시 만들 때(사람이 고친 게 없음을 확인한 경우에만).</summary>
        public static void BuildOverwrite() => Build();

        // ───────────────────────── 준비: 서체 · 그림 ─────────────────────────

        static void PrepareAssets()
        {
            _serif = FontAsset(Fonts + "NotoSerifKR-Regular.otf", "Alibi Serif SDF");
            _serifBold = FontAsset(Fonts + "NotoSerifKR-Bold.otf", "Alibi Serif Bold SDF");
            _pen = FontAsset(Fonts + "NanumPenScript.ttf", "Alibi Pen SDF");
            _brush = FontAsset(Fonts + "NanumBrushScript.ttf", "Alibi Brush SDF");
            // 손글씨·붓 서체에 없는 기호(→ × † … 등)는 본명조로 대신 그린다
            foreach (var fa in new[] { _pen, _brush, _serifBold })
            {
                if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!fa.fallbackFontAssetTable.Contains(_serif)) { fa.fallbackFontAssetTable.Add(_serif); EditorUtility.SetDirty(fa); }
            }
            AssetDatabase.SaveAssets();
        }

        static TMP_FontAsset FontAsset(string fontPath, string name)
        {
            string assetPath = Fonts + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null) throw new Exception("서체 파일 없음: " + fontPath);
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

        /// <summary>새로 들어온 PNG를 UI 스프라이트로(9분할 테두리 포함). 기존 잼 아트는 건드리지 않는다.</summary>
        static Sprite LoadSprite(string path, Vector4 border = default, int maxSize = 2048)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[AlibiBuilder] 그림 없음: " + path); return null; }
            bool dirty = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
            if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; dirty = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
            if (ti.maxTextureSize != maxSize) { ti.maxTextureSize = maxSize; dirty = true; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (ti.spriteBorder != border) { ti.spriteBorder = border; dirty = true; }
            if (dirty) ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>잼 아트(여러 조각으로 잘린 것 포함)에서 가장 큰 스프라이트를 그대로 쓴다.</summary>
        static Sprite OldSprite(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderByDescending(s => s.rect.width * s.rect.height).FirstOrDefault();

        // ───────────────────────── 조립 ─────────────────────────

        static void Build()
        {
            PrepareAssets();
            var s = new Sprites();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.02f, 0.03f);
            camGo.transform.position = new Vector3(0, 0, -10);

            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var canvasGo = new GameObject("AlibiCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            // 배경: 촛불 켜진 저택(잼 배경) + 무대 비네트
            var bg = Img(Stretch("Background", root), new Color(0.62f, 0.57f, 0.66f), s.Bg0);
            bg.raycastTarget = false;
            var vig = Img(UI("Vignette", root, 0, 0, 1190, 1080), new Color(1, 1, 1, 0.92f), s.Vignette);
            vig.raycastTarget = false;

            var shake = Stretch("ShakeRoot", root);
            var stage = BuildStage(shake.transform, s, bg);
            var dialogue = BuildDialogue(shake.transform, s);
            var notebook = BuildNotebook(shake.transform, s);
            var hud = canvasGo.AddComponent<AlibiHud>();
            BuildTopBar(shake.transform, s, hud);
            BuildToast(root, s, hud);
            BuildObjection(root, s, hud);
            BuildMurder(root, s, hud);
            BuildDeadline(root, s, hud);
            BuildBriefing(root, s, hud);
            BuildAccuse(root, s, hud);
            BuildResult(root, s, hud);
            // 컷신·알림은 꺼진 채로 저작한다(에디터에서 씬을 볼 때 화면을 가리지 않게) — AlibiHud가 필요할 때 켠다
            foreach (var g in new[] { hud.objectionGroup, hud.murderGroup, hud.deadlineGroup, hud.toastGroup })
            {
                g.alpha = 0f;
                g.gameObject.SetActive(false);
            }

            var audioGo = new GameObject("AlibiAudio");
            var audio = audioGo.AddComponent<AlibiAudio>();
            audio.music = audioGo.AddComponent<AudioSource>();
            audio.sfx = audioGo.AddComponent<AudioSource>();
            audio.music.playOnAwake = false;
            audio.sfx.playOnAwake = false;
            audio.musicClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MafiaAI/Audio/bgm.mp3");
            audio.click = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MafiaAI/Audio/vote_select.mp3");
            audio.stamp = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MafiaAI/Audio/vote_confirm.mp3");
            audio.murder = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MafiaAI/Audio/night_start.mp3");
            audio.reveal = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MafiaAI/Audio/role_assigned.mp3");
            audio.ending = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MafiaAI/Audio/game_end.mp3");

            var game = new GameObject("AlibiGame").AddComponent<AlibiGame>();
            game.stage = stage;
            game.dialogue = dialogue;
            game.notebook = notebook;
            game.hud = hud;
            game.audioFx = audio;
            game.shakeRoot = (RectTransform)shake.transform;
            var cardFiles = new Dictionary<string, string> { ["카이"] = "1", ["미로"] = "2", ["노아"] = "3", ["세이"] = "4", ["제로"] = "5", ["하루"] = "6" };
            game.characters = CharacterSheets.All.Select(c => new AlibiGame.CharacterArt
            {
                name = c.Name,
                card = OldSprite(Old + "Char/" + cardFiles[c.Name] + ".png"),
                portrait = OldSprite(Old + "Char/" + c.PortraitKey + ".png"),
                face = LoadSprite(Ui + "face_" + c.PortraitKey + ".png", default, 256),
            }).ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[AlibiBuilder] 씬 저장: " + ScenePath);
        }

        /// <summary>쓰는 그림 묶음(없는 건 null → 색만 칠해진다).</summary>
        class Sprites
        {
            public readonly Sprite Bg0 = OldSprite(Old + "background_art/1e1_bg.png");
            public readonly Sprite Bg1 = OldSprite(Old + "background_art/1e1_bg2.png");
            public readonly Sprite Bg2 = OldSprite(Old + "background_art/1e1_bg3.png");
            public readonly Sprite XMark = OldSprite(Old + "X2.png");
            public readonly Sprite Vignette = LoadSprite(Ui + "vignette.png", default, 1024);
            public readonly Sprite DialogueBox = LoadSprite(Ui + "dialogue_box.png", new Vector4(140, 70, 140, 70));
            public readonly Sprite InkBox = LoadSprite(Ui + "ink_box.png", new Vector4(16, 16, 16, 16), 256);
            public readonly Sprite InkBoxRed = LoadSprite(Ui + "ink_box_red.png", new Vector4(16, 16, 16, 16), 256);
            public readonly Sprite TagPaper = LoadSprite(Ui + "tag_paper.png", new Vector4(22, 22, 22, 22), 256);
            public readonly Sprite TagCrimson = LoadSprite(Ui + "tag_crimson.png", new Vector4(22, 22, 22, 22), 256);
            public readonly Sprite TagDark = LoadSprite(Ui + "tag_dark.png", new Vector4(22, 22, 22, 22), 256);
            public readonly Sprite White = LoadSprite(Ui + "white.png", default, 32);
            public readonly Sprite CorridorH = LoadSprite(Ui + "corridor_h.png", default, 128);
            public readonly Sprite CorridorV = LoadSprite(Ui + "corridor_v.png", default, 128);
            public readonly Sprite HourHand = LoadSprite(Ui + "watch_hand_hour.png", default, 512);
            public readonly Sprite MinuteHand = LoadSprite(Ui + "watch_hand_minute.png", default, 512);
            public readonly Sprite Parchment = LoadSprite(Codex + "parchment_page.png");
            public readonly Sprite Leather = LoadSprite(Codex + "leather_cover.png");
            public readonly Sprite InkRing = LoadSprite(Codex + "ink_stamp_ring.png");
            public readonly Sprite Blood = LoadSprite(Codex + "blood_splatter.png");
            public readonly Sprite Seal = LoadSprite(Codex + "wax_seal.png", default, 512);
            public readonly Sprite Watch = LoadSprite(Codex + "pocket_watch_face.png", default, 512);
            public readonly Sprite TornNote = LoadSprite(Codex + "torn_note.png");
            public readonly Sprite PencilCircle = LoadSprite(Codex + "red_pencil_circle.png", default, 1024);

            public Sprite Weapon(string room)
            {
                var key = room switch
                {
                    "서재" => "paperweight", "응접실" => "poker", "식당" => "candlestick", "온실" => "shears",
                    "현관홀" => "cane", "부엌" => "knife", "세탁실" => "iron", _ => "hammer",
                };
                return LoadSprite(Ui + "weapon_" + key + ".png", default, 256);
            }
        }

        // ───────────────────────── 무대 ─────────────────────────

        static AlibiStageView BuildStage(Transform parent, Sprites s, Image background)
        {
            var stageGo = UI("Stage", parent, 0, 72, 1190, 1008);
            var view = stageGo.AddComponent<AlibiStageView>();
            view.background = background;
            view.roomBackgrounds = new[] { s.Bg0, s.Bg1, s.Bg2 };
            view.dim = Img(Stretch("Dim", stageGo.transform), new Color(0, 0, 0, 0));
            view.dim.raycastTarget = false;
            view.redFlash = Img(Stretch("RedFlash", stageGo.transform), new Color(0.62f, 0.05f, 0.1f, 0f));
            view.redFlash.raycastTarget = false;

            // 용의자 목록 — 잼 액자 카드를 무대 위에 부채꼴로
            var lineup = Stretch("Lineup", stageGo.transform);
            view.lineupRoot = lineup;
            var victim = Img(UI("VictimCard", lineup.transform, 36, 22, 112, 158), new Color(0.5f, 0.5f, 0.52f));
            victim.preserveAspect = true;
            victim.raycastTarget = false;
            view.victimArt = victim;
            view.victimLabel = Txt(UI("VictimLabel", lineup.transform, 20, 182, 150, 26), "피해자", _serif, 16, Muted, TextAlignmentOptions.Center);
            Txt(UI("Title", lineup.transform, 0, 40, 1190, 52), "이 저택의 손님들", _serifBold, 34, Cream, TextAlignmentOptions.Center);
            Txt(UI("Subtitle", lineup.transform, 0, 96, 1190, 30), "그중 하나가 살인자다. 카드를 눌러 심문하라.", _serif, 19, Muted, TextAlignmentOptions.Center);
            float[] arc = { 30, 10, 0, 10, 30 };
            for (int i = 0; i < 5; i++)
            {
                var card = UI("Card" + i, lineup.transform, 48 + i * 224, 190 + arc[i], 200, 283);
                var art = Img(card, Color.white);
                art.preserveAspect = true;
                var ui = new AlibiStageView.CardUI
                {
                    button = AddButton(card, art),
                    rect = (RectTransform)card.transform,
                    art = art,
                    name = Txt(UI("Name", card.transform, 30, 174, 140, 38), "이름", _serifBold, 21, Ink, TextAlignmentOptions.Center),
                    note = Txt(UI("Note", card.transform, -10, 290, 220, 26), "아직 진술 전", _serif, 16, Cream, TextAlignmentOptions.Center),
                    stamp = Txt(UI("Stamp", card.transform, 104, 4, 110, 60), "", _brush, 48, Bright, TextAlignmentOptions.Center),
                };
                ui.stamp.rectTransform.localEulerAngles = new Vector3(0, 0, -14);
                var x = Img(UI("DeadMark", card.transform, 22, 30, 156, 150), Color.white, s.XMark);
                x.preserveAspect = true;
                x.raycastTarget = false;
                x.enabled = false;
                ui.deadMark = x;
                view.cards[i] = ui;
            }
            view.lineupHint = Txt(UI("Hint", lineup.transform, 0, 552, 1190, 30), "첫 대면에 각자 밤의 동선을 털어놓는다 · 누구를 봤는지는 직접 물어야 나온다", _serif, 18, Muted, TextAlignmentOptions.Center);

            // 심문 장면 — 크게 마주 앉은 사람
            var talk = Stretch("Talk", stageGo.transform);
            view.talkRoot = talk;
            var portraitGo = UI("Portrait", talk.transform, 40, 26, 620, 877);
            portraitGo.AddComponent<CanvasGroup>();
            var pr = (RectTransform)portraitGo.transform;
            pr.pivot = new Vector2(0.5f, 0f);   // 숨쉬기·다가가기를 발치 기준으로
            pr.anchoredPosition = new Vector2(40 + 310, -(26 + 877));
            view.portraitRect = pr;
            view.portrait = Img(portraitGo, Color.white);
            view.portrait.preserveAspect = true;
            view.portrait.raycastTarget = false;
            view.dossierName = Txt(UI("Name", talk.transform, 700, 14, 460, 70), "이름", _serifBold, 42, Cream, TextAlignmentOptions.MidlineLeft);
            view.dossierTagline = Txt(UI("Tagline", talk.transform, 700, 84, 460, 30), "", _serif, 19, Muted, TextAlignmentOptions.MidlineLeft);
            view.backButton = Btn("BackButton", talk.transform, 700, 124, 236, 44, "◀ 손님 목록 (Esc)", _serif, 17, s.TagDark, Cream, out _);
            talk.SetActive(false);
            return view;
        }

        // ───────────────────────── 대사창 ─────────────────────────

        static AlibiDialogueView BuildDialogue(Transform parent, Sprites s)
        {
            var host = UI("Dialogue", parent, 0, 72, 1190, 1008);
            var view = host.AddComponent<AlibiDialogueView>();

            // 추궁 꼬리표(무대 오른쪽, 초상 옆)
            var press = UI("Press", host.transform, 700, 196, 456, 420);
            view.pressRoot = press;
            Txt(UI("Title", press.transform, 0, 0, 456, 52), "추궁", _brush, 50, Bright, TextAlignmentOptions.MidlineLeft);
            Txt(UI("Sub", press.transform, 0, 52, 456, 26), "찾은 모순을 이 사람에게 들이밀어라", _serif, 16, Muted, TextAlignmentOptions.MidlineLeft);
            var pressList = UI("List", press.transform, 0, 88, 456, 330);
            var plg = pressList.AddComponent<VerticalLayoutGroup>();
            plg.spacing = 10;
            plg.childControlWidth = true; plg.childControlHeight = true;
            plg.childForceExpandWidth = true; plg.childForceExpandHeight = false;
            var pressTemplate = Btn("PressTemplate", pressList.transform, 0, 0, 456, 58, "카이 23:15 × 하루 23:15", _serif, 19, s.TagCrimson, Cream, out _);
            pressTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = 58;
            pressTemplate.gameObject.SetActive(false);
            view.pressContent = (RectTransform)pressList.transform;
            view.pressTemplate = pressTemplate;
            press.SetActive(false);

            // 질문 쪽지(대사창 바로 위)
            var chips = UI("Chips", host.transform, 26, 646, 1140, 46);
            var hlg = chips.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            var chip = UI("ChipTemplate", chips.transform, 0, 0, 180, 46);
            var chipImg = Img(chip, Color.white, s.TagPaper);
            chipImg.type = Image.Type.Sliced;
            var chipBtn = AddButton(chip, chipImg);
            var chipLayout = chip.AddComponent<HorizontalLayoutGroup>();
            chipLayout.padding = new RectOffset(18, 18, 6, 6);
            chipLayout.childControlWidth = true; chipLayout.childControlHeight = true;
            chipLayout.childForceExpandWidth = false; chipLayout.childForceExpandHeight = true;
            var chipLabel = Txt(UI("Label", chip.transform, 0, 0, 160, 34), "23:00 누굴 봤나", _serif, 16, Ink, TextAlignmentOptions.Center);
            chipLabel.textWrappingMode = TextWrappingModes.NoWrap;
            chip.SetActive(false);
            view.chipContent = (RectTransform)chips.transform;
            view.chipTemplate = chipBtn;

            // 대사창(잼 텍스트박스 장식, 9분할)
            var box = UI("Box", host.transform, 18, 698, 1154, 300);
            var boxImg = Img(box, Color.white, s.DialogueBox);
            boxImg.type = Image.Type.Sliced;
            view.boxButton = AddButton(box, boxImg);
            view.speaker = Txt(UI("Speaker", box.transform, 132, 38, 600, 44), "수사 기록", _serifBold, 27, Crimson, TextAlignmentOptions.MidlineLeft);
            view.historyButton = Btn("HistoryButton", box.transform, 1004, 44, 104, 36, "기록", _serif, 16, s.TagPaper, Ink, out _);
            view.question = Txt(UI("Question", box.transform, 78, 84, 1030, 28), "", _serif, 17, Brown, TextAlignmentOptions.MidlineLeft);
            view.question.fontStyle = FontStyles.Italic;
            view.line = Txt(UI("Line", box.transform, 78, 114, 1034, 124), "", _serif, 24, Ink, TextAlignmentOptions.TopLeft);
            view.line.overflowMode = TextOverflowModes.Overflow;
            view.line.lineSpacing = -12;
            var tw = box.AddComponent<AlibiTypewriter>();
            tw.target = view.line;
            view.typewriter = tw;

            var inputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            inputGo.name = "Input";
            inputGo.transform.SetParent(box.transform, false);
            Place(inputGo, 78, 240, 886, 42);
            var inputBg = inputGo.GetComponent<Image>();
            inputBg.sprite = s.TagPaper;
            inputBg.type = Image.Type.Sliced;
            inputBg.color = new Color(1, 1, 1, 0.75f);
            var input = inputGo.GetComponent<TMP_InputField>();
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 120;
            input.onFocusSelectAll = false;
            foreach (var t in inputGo.GetComponentsInChildren<TMP_Text>(true))
            {
                t.font = _serif;
                t.fontSize = 19;
                t.alignment = TextAlignmentOptions.MidlineLeft;
            }
            input.textComponent.color = Ink;
            var ph = (TMP_Text)input.placeholder;
            ph.text = "직접 물어보기 — 떠보거나 몰아붙여라 (Enter)";
            ph.color = new Color(0.45f, 0.38f, 0.3f, 0.75f);
            ph.fontStyle = FontStyles.Italic;
            input.pointSize = 19;
            view.input = input;
            view.askButton = Btn("AskButton", box.transform, 976, 236, 136, 48, "묻기", _serifBold, 20, s.TagDark, Cream, out _);

            // 대화 기록(무대 위에 펼치는 종이)
            var hist = UI("History", host.transform, 40, 30, 1110, 640);
            view.historyRoot = hist;
            Img(hist, Color.white, s.Parchment);
            Txt(UI("Title", hist.transform, 40, 18, 600, 44), "대화 기록", _serifBold, 26, Ink, TextAlignmentOptions.MidlineLeft);
            view.historyClose = Btn("Close", hist.transform, 950, 20, 120, 40, "닫기", _serif, 17, s.TagDark, Cream, out _);
            var scrollGo = UI("Scroll", hist.transform, 40, 70, 1030, 550);
            Img(scrollGo, Clear);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            var viewport = Stretch("Viewport", scrollGo.transform);
            viewport.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1);
            crt.sizeDelta = Vector2.zero;
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 6);
            vlg.spacing = 10;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var line = Txt(UI("LineTemplate", content.transform, 0, 0, 1000, 30), "탐정  질문", _serif, 20, Ink, TextAlignmentOptions.TopLeft);
            line.gameObject.SetActive(false);
            scroll.content = crt;
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            scroll.inertia = false;
            view.historyScroll = scroll;
            view.historyContent = crt;
            view.historyLineTemplate = line;
            hist.SetActive(false);
            return view;
        }

        // ───────────────────────── 수사 수첩 ─────────────────────────

        static AlibiNotebookView BuildNotebook(Transform parent, Sprites s)
        {
            var book = UI("Notebook", parent, 1190, 78, 724, 998);
            var leather = Img(book, s.Leather != null ? Color.white : new Color(0.3f, 0.07f, 0.1f), s.Leather);
            leather.raycastTarget = false;
            var page = UI("Page", book.transform, 18, 16, 688, 966);
            Img(page, Color.white, s.Parchment).raycastTarget = false;
            var view = book.AddComponent<AlibiNotebookView>();
            var p = page.transform;

            Txt(UI("Title", p, 26, 8, 400, 60), "수사 수첩", _brush, 52, Crimson, TextAlignmentOptions.MidlineLeft);
            Txt(UI("Hint", p, 330, 24, 334, 26), "의심 가는 진술을 눌러 고른다", _serif, 14, Brown, TextAlignmentOptions.MidlineRight);

            // 진술표 머리
            Txt(UI("WhoHeader", p, 26, 76, 160, 40), "손님", _serif, 15, Brown, TextAlignmentOptions.MidlineLeft);
            for (int t = 0; t < 4; t++)
            {
                var head = UI("Slot" + t, p, 188 + t * 120, 70, 116, 54);
                var hit = Img(head, Hover);
                view.slotButtons[t] = HoverButton(head, hit);
                view.slotLabels[t] = Txt(UI("Label", head.transform, 0, 0, 116, 50), AlibiCase.TimeLabel(t), _serifBold, 20, Ink, TextAlignmentOptions.Center);
                var mark = Img(UI("Focus", head.transform, 14, 48, 88, 3), Crimson, s.White);
                mark.raycastTarget = false;
                view.slotMarks[t] = mark;
            }
            Line(p, 26, 126, 636, 2, new Color(Ink.r, Ink.g, Ink.b, 0.55f), s);
            for (int t = 0; t < 4; t++) Line(p, 186 + t * 120, 74, 1, 430, new Color(Ink.r, Ink.g, Ink.b, 0.16f), s);

            // 진술표 줄
            for (int i = 0; i < 5; i++)
            {
                float y = 132 + i * 74;
                var head = UI("Row" + i, p, 26, y, 160, 72);
                var hit = Img(head, Hover);
                var row = new AlibiNotebookView.RowUI { header = HoverButton(head, hit), cells = new AlibiNotebookView.CellUI[4] };
                var face = Img(UI("Face", head.transform, 0, 8, 56, 56), Color.white);
                face.preserveAspect = true;
                face.raycastTarget = false;
                row.face = face;
                row.name = Txt(UI("Name", head.transform, 62, 6, 100, 34), "이름", _serifBold, 21, Ink, TextAlignmentOptions.MidlineLeft);
                row.status = Txt(UI("Status", head.transform, 62, 40, 110, 28), "", _pen, 21, Crimson, TextAlignmentOptions.MidlineLeft);
                for (int t = 0; t < 4; t++)
                {
                    var cell = UI($"Cell{i}_{t}", p, 188 + t * 120, y, 116, 72);
                    var cellHit = Img(cell, Hover);
                    var circle = Img(UI("Circle", cell.transform, -8, 4, 132, 64), Color.white, s.PencilCircle);
                    circle.raycastTarget = false;
                    circle.enabled = false;
                    var ui = new AlibiNotebookView.CellUI
                    {
                        button = HoverButton(cell, cellHit),
                        rect = (RectTransform)cell.transform,
                        room = Txt(UI("Room", cell.transform, 0, 2, 92, 38), "—", _pen, 30, Ink, TextAlignmentOptions.Center),
                        seen = Txt(UI("Seen", cell.transform, 0, 38, 92, 32), "", _pen, 20, Ink, TextAlignmentOptions.Center),
                        circle = circle,
                        mark = Txt(UI("Mark", cell.transform, 86, -2, 34, 76), "", _brush, 33, Crimson, TextAlignmentOptions.Center),
                    };
                    ui.seen.textWrappingMode = TextWrappingModes.Normal;
                    ui.mark.fontStyle = FontStyles.Bold;
                    ui.mark.lineSpacing = -40;   // 세로 낙관(거/짓)이 칸 오른쪽 기둥 안에 들어오게
                    ui.mark.rectTransform.localEulerAngles = new Vector3(0, 0, -6);
                    row.cells[t] = ui;
                }
                Line(p, 26, y + 72, 636, 1, new Color(Ink.r, Ink.g, Ink.b, 0.22f), s);
                var strike = Img(UI("Strike" + i, p, 20, y + 34, 648, 3), new Color(Crimson.r, Crimson.g, Crimson.b, 0.85f), s.White);
                strike.raycastTarget = false;
                strike.enabled = false;
                row.strike = strike;
                view.rows[i] = row;
            }

            // 저택 도면
            Txt(UI("PlanTitle", p, 26, 512, 320, 30), "저택 도면 · 복도 한 칸 = 15분", _serifBold, 16, Ink, TextAlignmentOptions.MidlineLeft);
            view.focusLabel = Txt(UI("FocusLabel", p, 340, 508, 324, 34), "", _pen, 23, Ink, TextAlignmentOptions.MidlineRight);
            const float ox = 42, oy = 550, w = 172, h = 74, px = 214, py = 100;
            var corridorRoot = UI("Corridors", p, 0, 0, 688, 966);
            view.corridors = AlibiMap.Corridors.Select(c =>
            {
                var a = AlibiMap.Get(c.A);
                var b = AlibiMap.Get(c.B);
                GameObject go;
                if (a.Y == b.Y)
                {
                    int gx = Math.Min(a.X, b.X);
                    go = UI(c.A + "-" + c.B, corridorRoot.transform, ox + gx * px + w - 3, oy + a.Y * py + h / 2 - 10, px - w + 6, 20);
                    return new AlibiNotebookView.CorridorUI { a = c.A, b = c.B, image = Img(go, Ink, s.CorridorH) };
                }
                int gy = Math.Min(a.Y, b.Y);
                go = UI(c.A + "-" + c.B, corridorRoot.transform, ox + a.X * px + w / 2 - 10, oy + gy * py + h - 3, 20, py - h + 6);
                return new AlibiNotebookView.CorridorUI { a = c.A, b = c.B, image = Img(go, Ink, s.CorridorV) };
            }).ToArray();
            foreach (var cor in view.corridors) cor.image.raycastTarget = false;

            var roomRoot = UI("Rooms", p, 0, 0, 688, 966);
            view.rooms = AlibiMap.Rooms.Select(r =>
            {
                var node = UI(r.Name, roomRoot.transform, ox + r.X * px, oy + r.Y * py, w, h);
                var fill = Img(UI("Fill", node.transform, 5, 5, w - 10, h - 10), Clear, s.White);
                fill.raycastTarget = false;
                var boxImg = Img(UI("Box", node.transform, 0, 0, w, h), Color.white, s.InkBox);
                boxImg.type = Image.Type.Sliced;
                boxImg.raycastTarget = false;
                var weapon = Img(UI("Weapon", node.transform, 134, 6, 32, 32), Color.white, s.Weapon(r.Name));
                weapon.preserveAspect = true;
                weapon.raycastTarget = false;
                var murder = Img(UI("MurderMark", node.transform, 128, 30, 40, 40), Color.white, s.XMark);
                murder.preserveAspect = true;
                murder.raycastTarget = false;
                murder.enabled = false;
                return new AlibiNotebookView.RoomUI
                {
                    room = r.Name,
                    box = boxImg,
                    fill = fill,
                    label = Txt(UI("Label", node.transform, 18, 13, 112, 28), r.Name, _pen, 25, Ink, TextAlignmentOptions.MidlineLeft),
                    weapon = weapon,
                    occupants = Txt(UI("Occupants", node.transform, 18, 41, 148, 24), "", _pen, 19, Crimson, TextAlignmentOptions.MidlineLeft),
                    murderMark = murder,
                };
            }).ToArray();
            view.distance = Txt(UI("Distance", p, 26, 828, 640, 30), "", _pen, 21, Ink, TextAlignmentOptions.MidlineLeft);

            // 하단: 고른 진술 · 모순 제시(밀랍 인장) · 지우기 · 범인 지목
            view.selection = Txt(UI("Selection", p, 26, 860, 640, 28), "", _pen, 22, Ink, TextAlignmentOptions.MidlineLeft);
            var present = UI("PresentButton", p, 22, 892, 236, 66);
            var presentHit = Img(present, Clear);
            view.presentButton = AddButton(present, presentHit);
            view.presentGroup = present.AddComponent<CanvasGroup>();
            var seal = Img(UI("Seal", present.transform, 0, 0, 66, 66), s.Seal != null ? Color.white : Clear, s.Seal);
            seal.preserveAspect = true;
            seal.raycastTarget = false;
            Txt(UI("Label", present.transform, 72, 10, 164, 46), "모순 제시", _serifBold, 24, Crimson, TextAlignmentOptions.MidlineLeft);
            view.clearButton = Btn("ClearButton", p, 270, 904, 110, 44, "지우기", _serif, 17, s.TagPaper, Ink, out _);
            view.accuseButton = Btn("AccuseButton", p, 448, 898, 216, 56, "범인 지목", _serifBold, 23, s.TagCrimson, Cream, out _);
            return view;
        }

        // ───────────────────────── 상단 바 · 알림 ─────────────────────────

        static void BuildTopBar(Transform parent, Sprites s, AlibiHud hud)
        {
            var bar = UI("TopBar", parent, 0, 0, 1920, 72);
            Img(bar, new Color(0.03f, 0.022f, 0.03f, 0.9f)).raycastTarget = false;
            Line(bar.transform, 0, 71, 1920, 1, new Color(0.75f, 0.6f, 0.36f, 0.45f), s);
            hud.caseTitle = Txt(UI("CaseTitle", bar.transform, 28, 4, 760, 40), "사건", _serifBold, 26, Cream, TextAlignmentOptions.MidlineLeft);
            hud.caseFacts = Txt(UI("CaseFacts", bar.transform, 28, 42, 760, 24), "", _serif, 15, Muted, TextAlignmentOptions.MidlineLeft);

            var watch = UI("Watch", bar.transform, 806, -8, 86, 86);
            var face = Img(watch, Color.white, s.Watch);
            face.preserveAspect = true;
            face.raycastTarget = false;
            hud.hourHand = Hand("HourHand", watch.transform, s.HourHand, 6, 26);
            hud.minuteHand = Hand("MinuteHand", watch.transform, s.MinuteHand, 6, 34);
            hud.clockText = Txt(UI("Clock", bar.transform, 900, -2, 170, 52), "23:45", _serifBold, 32, Cream, TextAlignmentOptions.MidlineLeft);
            hud.threatText = Txt(UI("Threat", bar.transform, 900, 42, 420, 26), "", _serif, 15, Amber, TextAlignmentOptions.MidlineLeft);
            hud.aiStatus = Txt(UI("AiStatus", bar.transform, 1440, 8, 456, 24), "AI 확인 중…", _serif, 14, Muted, TextAlignmentOptions.MidlineRight);
            hud.debugText = Txt(UI("Debug", bar.transform, 1100, 40, 796, 24), "", _serif, 12, new Color(Muted.r, Muted.g, Muted.b, 0.8f), TextAlignmentOptions.MidlineRight);
        }

        static RectTransform Hand(string name, Transform watch, Sprite sprite, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(watch, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.44f);   // 시계판 중심(용두 때문에 그림 중심보다 조금 아래)
            rt.pivot = new Vector2(0.5f, 40f / 512f);                // 바늘 그림의 회전축
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return rt;
        }

        static void BuildToast(Transform root, Sprites s, AlibiHud hud)
        {
            var toast = UI("Toast", root, 1214, 90, 676, 80);   // 수첩 머리에 꽂히는 쪽지 — 심문 무대(초상·이름표)를 가리지 않는다
            var img = Img(toast, Color.white, s.TagPaper);
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            hud.toastGroup = toast.AddComponent<CanvasGroup>();
            hud.toastText = Txt(UI("Text", toast.transform, 24, 10, 628, 60), "알림", _serif, 18, Ink, TextAlignmentOptions.Center);
        }

        // ───────────────────────── 컷신 ─────────────────────────

        static void BuildObjection(Transform root, Sprites s, AlibiHud hud)
        {
            var go = Stretch("CutObjection", root);
            hud.objectionGroup = go.AddComponent<CanvasGroup>();
            var dim = Img(go, new Color(0.02f, 0.01f, 0.02f, 0.82f));
            hud.objectionButton = AddButton(go, dim);
            var ring = Img(UI("Ring", go.transform, 660, 110, 600, 600), Color.white, s.InkRing);
            ring.preserveAspect = true;
            ring.raycastTarget = false;
            hud.objectionRing = ring.rectTransform;
            var word = Txt(UI("Word", go.transform, 560, 230, 800, 360), "모순!", _brush, 240, Bright, TextAlignmentOptions.Center);
            hud.objectionWord = word.rectTransform;
            var panel = UI("Panel", go.transform, 330, 760, 1260, 200);
            var pImg = Img(panel, Color.white, s.DialogueBox);
            pImg.type = Image.Type.Sliced;
            pImg.raycastTarget = false;
            hud.objectionPanel = (RectTransform)panel.transform;
            hud.objectionText = Txt(UI("Text", panel.transform, 70, 34, 1120, 110), "설명", _serif, 25, Ink, TextAlignmentOptions.Center);
            Txt(UI("Hint", panel.transform, 0, 150, 1260, 26), "클릭해서 계속", _serif, 15, Brown, TextAlignmentOptions.Center);
        }

        static void BuildMurder(Transform root, Sprites s, AlibiHud hud)
        {
            var go = Stretch("CutMurder", root);
            hud.murderGroup = go.AddComponent<CanvasGroup>();
            var black = Img(go, new Color(0.03f, 0f, 0.01f, 0.96f));
            hud.murderButton = AddButton(go, black);
            var splat = Img(UI("Splatter", go.transform, 192, 30, 1536, 1024), new Color(1, 1, 1, 0.9f), s.Blood);
            splat.preserveAspect = true;
            splat.raycastTarget = false;
            hud.murderSplatter = splat.rectTransform;
            hud.murderTitle = Txt(UI("Title", go.transform, 0, 200, 1920, 200), "두 번째 비명", _brush, 176, Bright, TextAlignmentOptions.Center);
            hud.murderSub = Txt(UI("Sub", go.transform, 0, 400, 1920, 60), "", _serif, 32, Cream, TextAlignmentOptions.Center);
            var note = UI("Note", go.transform, 650, 500, 620, 465);
            var nImg = Img(note, Color.white, s.TornNote != null ? s.TornNote : s.Parchment);
            nImg.preserveAspect = s.TornNote != null;
            nImg.raycastTarget = false;
            hud.murderNote = (RectTransform)note.transform;
            hud.murderNoteText = Txt(UI("Text", note.transform, 80, 80, 460, 300), "", _pen, 32, Ink, TextAlignmentOptions.Center);
            Txt(UI("Hint", go.transform, 0, 1020, 1920, 30), "클릭해서 계속", _serif, 16, Muted, TextAlignmentOptions.Center);
        }

        static void BuildDeadline(Transform root, Sprites s, AlibiHud hud)
        {
            var go = Stretch("CutDeadline", root);
            hud.deadlineGroup = go.AddComponent<CanvasGroup>();
            var black = Img(go, new Color(0.02f, 0f, 0.01f, 0.93f));
            hud.deadlineButton = AddButton(go, black);
            hud.deadlineTitle = Txt(UI("Title", go.transform, 0, 340, 1920, 190), "범인이 달아나려 한다", _brush, 128, Bright, TextAlignmentOptions.Center);
            Txt(UI("Sub", go.transform, 0, 540, 1920, 56), "지금 지목하라 — 기회는 한 번이다", _serif, 30, Cream, TextAlignmentOptions.Center);
            Txt(UI("Hint", go.transform, 0, 1020, 1920, 30), "클릭해서 계속", _serif, 16, Muted, TextAlignmentOptions.Center);
        }

        // ───────────────────────── 모달 ─────────────────────────

        static void BuildBriefing(Transform root, Sprites s, AlibiHud hud)
        {
            var dim = Stretch("Briefing", root);
            Img(dim, new Color(0.02f, 0.01f, 0.02f, 0.93f));
            var letter = UI("Letter", dim.transform, 560, 28, 800, 1024);
            Img(letter, Color.white, s.Parchment);
            Txt(UI("Title", letter.transform, 60, 34, 680, 96), "사건 파일", _brush, 82, Crimson, TextAlignmentOptions.Center);
            Line(letter.transform, 120, 132, 560, 2, new Color(Ink.r, Ink.g, Ink.b, 0.4f), s);
            hud.briefingText = Txt(UI("Body", letter.transform, 76, 156, 648, 650), "브리핑", _serif, 21, Ink, TextAlignmentOptions.TopLeft);
            hud.briefingText.lineSpacing = -8;
            var start = UI("StartButton", letter.transform, 290, 818, 220, 176);
            var hit = Img(start, Clear);
            hud.startButton = AddButton(start, hit);
            var seal = Img(UI("Seal", start.transform, 52, 0, 116, 116), s.Seal != null ? Color.white : Clear, s.Seal);
            seal.preserveAspect = true;
            seal.raycastTarget = false;
            hud.startLabel = Txt(UI("Label", start.transform, 0, 120, 220, 44), "AI 준비 중…", _serifBold, 25, Crimson, TextAlignmentOptions.Center);
            hud.briefingRoot = dim;
        }

        static void BuildAccuse(Transform root, Sprites s, AlibiHud hud)
        {
            var dim = Stretch("Accuse", root);
            Img(dim, new Color(0.02f, 0.01f, 0.02f, 0.97f));
            Txt(UI("Title", dim.transform, 0, 60, 1920, 120), "범인을 지목하라", _brush, 92, Bright, TextAlignmentOptions.Center);
            Txt(UI("Sub", dim.transform, 0, 182, 1920, 36), "기회는 한 번뿐이다 — 거짓말한 사람이 둘이라는 걸 잊지 마라", _serif, 21, Muted, TextAlignmentOptions.Center);
            for (int i = 0; i < 5; i++)
            {
                var card = UI("Pick" + i, dim.transform, 368 + i * 246, 270, 200, 283);
                var art = Img(card, Color.white);
                art.preserveAspect = true;
                var x = Img(UI("DeadMark", card.transform, 22, 30, 156, 150), Color.white, s.XMark);
                x.preserveAspect = true;
                x.raycastTarget = false;
                x.enabled = false;
                hud.pickCards[i] = new AlibiHud.PickCard
                {
                    button = AddButton(card, art),
                    art = art,
                    name = Txt(UI("Name", card.transform, 30, 174, 140, 38), "이름", _serifBold, 21, Ink, TextAlignmentOptions.Center),
                    deadMark = x,
                };
            }
            hud.accuseCancel = Btn("CancelButton", dim.transform, 810, 600, 300, 54, "닫기 (Esc)", _serif, 19, s.TagDark, Cream, out _);
            var confirm = UI("Confirm", dim.transform, 560, 690, 800, 230);
            var cImg = Img(confirm, Color.white, s.DialogueBox);
            cImg.type = Image.Type.Sliced;
            hud.confirmText = Txt(UI("Text", confirm.transform, 40, 30, 720, 70), "정말?", _serifBold, 32, Ink, TextAlignmentOptions.Center);
            hud.confirmYes = Btn("Yes", confirm.transform, 150, 128, 230, 62, "지목한다", _serifBold, 22, s.TagCrimson, Cream, out _);
            hud.confirmNo = Btn("No", confirm.transform, 420, 128, 230, 62, "다시 생각한다", _serif, 20, s.TagPaper, Ink, out _);
            hud.confirmRoot = confirm;
            confirm.SetActive(false);
            hud.accuseRoot = dim;
            dim.SetActive(false);
        }

        static void BuildResult(Transform root, Sprites s, AlibiHud hud)
        {
            var dim = Stretch("Result", root);
            Img(dim, new Color(0.02f, 0.01f, 0.02f, 0.96f));
            var report = UI("Report", dim.transform, 190, 26, 1540, 1028);
            Img(report, Color.white, s.Parchment);
            hud.resultTitle = Txt(UI("Title", report.transform, 70, 40, 1100, 64), "사건 보고서", _serifBold, 40, Ink, TextAlignmentOptions.MidlineLeft);
            var stamp = UI("Stamp", report.transform, 1210, 22, 280, 280);
            var ring = Img(stamp, Color.white, s.InkRing);
            ring.preserveAspect = true;
            ring.raycastTarget = false;
            hud.resultStamp = (RectTransform)stamp.transform;
            hud.resultStamp.localEulerAngles = new Vector3(0, 0, -12);
            hud.resultStampWord = Txt(UI("Word", stamp.transform, 0, 40, 280, 200), "해결", _brush, 120, Crimson, TextAlignmentOptions.Center);
            hud.resultText = Txt(UI("Body", report.transform, 70, 130, 1110, 780), "결과", _serif, 22, Ink, TextAlignmentOptions.TopLeft);   // 오른쪽 위 도장 밑으로 글이 들어가지 않게
            hud.newCaseButton = Btn("NewCaseButton", report.transform, 620, 932, 300, 64, "새 사건", _serifBold, 24, s.TagCrimson, Cream, out _);
            hud.resultRoot = dim;
            dim.SetActive(false);
        }

        // ───────────────────────── 저작 도우미 ─────────────────────────

        /// <summary>부모 왼쪽 위 기준 픽셀 좌표(아래로 +y)로 놓는다 — 1920×1080 레퍼런스.</summary>
        static GameObject UI(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, w, h);
            return go;
        }

        static void Place(GameObject go, float x, float y, float w, float h)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static GameObject Stretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go;
        }

        static Image Img(GameObject go, Color color, Sprite sprite = null)
        {
            var img = go.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            return img;
        }

        static void Line(Transform parent, float x, float y, float w, float h, Color color, Sprites s)
        {
            var img = Img(UI("Rule", parent, x, y, w, h), color, s.White);
            img.raycastTarget = false;
        }

        static TMP_Text Txt(GameObject go, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
        {
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;   // 본명조는 줄 높이가 커서 말줄임이면 글자가 통째로 숨는다
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        static Button AddButton(GameObject go, Graphic target)
        {
            var b = go.AddComponent<Button>();
            b.targetGraphic = target;
            var cb = b.colors;
            cb.highlightedColor = new Color(1f, 0.95f, 0.85f);
            cb.pressedColor = new Color(0.78f, 0.72f, 0.66f);
            cb.disabledColor = new Color(0.62f, 0.6f, 0.6f, 0.6f);
            b.colors = cb;
            return b;
        }

        /// <summary>평소엔 안 보이고 마우스를 올리면 바탕(Hover 색)이 드러나는 버튼 — 진술표 칸처럼 종이 위 글씨를 누를 때.</summary>
        static Button HoverButton(GameObject go, Graphic target)
        {
            var b = go.AddComponent<Button>();
            b.targetGraphic = target;
            var cb = b.colors;
            cb.normalColor = new Color(1, 1, 1, 0);
            cb.highlightedColor = Color.white;
            cb.pressedColor = new Color(1, 0.85f, 0.75f, 1f);
            cb.selectedColor = new Color(1, 1, 1, 0);
            cb.disabledColor = new Color(1, 1, 1, 0);
            cb.colorMultiplier = 1f;
            b.colors = cb;
            return b;
        }

        static Button Btn(string name, Transform parent, float x, float y, float w, float h, string label, TMP_FontAsset font, float size, Sprite sprite, Color fg, out TMP_Text labelText)
        {
            var go = UI(name, parent, x, y, w, h);
            var img = Img(go, Color.white, sprite);
            img.type = Image.Type.Sliced;
            var b = AddButton(go, img);
            labelText = Txt(UI("Label", go.transform, 0, 0, w, h), label, font, size, fg, TextAlignmentOptions.Center);
            return b;
        }
    }
}
