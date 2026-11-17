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
using UnityEngine.UI;
using MafiaAI.Alibi;
using MafiaAI.Alibi.UI;
using MafiaAI.Mystery.UI;
using Object = UnityEngine.Object;

namespace MafiaAI.Mystery.EditorTools
{
    /// <summary>
    /// 「자정의 원고」 씬을 '한 번' 저작한다. 만든 뒤엔 하이라키·인스펙터에서 직접 고친다(코드는 위치·크기·폰트를 건드리지 않는다).
    /// 씬이 이미 있으면 덮어쓰기 전에 묻는다.
    ///
    /// 화면: 왼쪽 무대(방 조사 그림 + 서술창·저택 도면 / 심문) · 오른쪽 가죽 수첩(증거품·타임라인·원고·의문점) · 위 회중시계.
    /// 아트: Codex로 그린 방 8곳·증거 클로즈업·이야기 그림(Art/Mystery) + v2 소재(양피지·붉은 먹·회중시계) + 잼 캐릭터.
    /// </summary>
    public static class MysterySceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/MysteryPrototype.unity";
        const string Art = "Assets/MafiaAI/Art/Mystery/";
        const string AlibiUi = "Assets/MafiaAI/Art/Alibi/UI/";
        const string AlibiCodex = "Assets/MafiaAI/Art/Alibi/Codex/";
        const string Fonts = "Assets/MafiaAI/Art/Alibi/Fonts/";
        const string Old = "Assets/sprites/UI/";

        internal static readonly Color Ink = new Color(0.17f, 0.12f, 0.09f);
        internal static readonly Color Cream = new Color(0.94f, 0.9f, 0.82f);
        internal static readonly Color Muted = new Color(0.66f, 0.6f, 0.53f);
        internal static readonly Color Crimson = new Color(0.62f, 0.11f, 0.19f);
        internal static readonly Color Bright = new Color(0.86f, 0.2f, 0.27f);
        internal static readonly Color Amber = new Color(0.9f, 0.74f, 0.46f);
        internal static readonly Color Brown = new Color(0.42f, 0.33f, 0.24f);
        internal static readonly Color Clear = new Color(1, 1, 1, 0);
        internal static readonly Color Panel = new Color(0.045f, 0.035f, 0.045f, 1f);

        internal static TMP_FontAsset _serif, _serifBold, _pen, _brush;

        [MenuItem("MafiaAI/미스터리/「자정의 원고」 씬 만들기")]
        static void BuildMenu()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("자정의 원고", "씬이 이미 있습니다.\n새로 만들면 하이라키에서 직접 고친 내용이 사라집니다. 덮어쓸까요?", "덮어쓰기", "취소"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>MCP·스크립트에서 묻지 않고 다시 만들 때(사람이 고친 게 없음을 확인한 경우에만).</summary>
        public static void BuildOverwrite() => Build();

        // ───────────────────────── 준비 ─────────────────────────

        internal static void PrepareFonts()
        {
            _serif = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "Alibi Serif SDF.asset");
            _serifBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "Alibi Serif Bold SDF.asset");
            _pen = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "Alibi Pen SDF.asset");
            _brush = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "Alibi Brush SDF.asset");
            if (_serif == null || _serifBold == null || _pen == null || _brush == null)
                throw new Exception("서체 에셋이 없습니다 — 먼저 'MafiaAI/알리바이/프로토타입 씬 만들기'를 한 번 실행해 서체를 만드세요.");
        }

        /// <summary>새로 들어온 PNG를 UI 스프라이트로. 큰 그림은 고품질 압축, 작은 UI 조각은 무압축.</summary>
        internal static Sprite LoadSprite(string path, Vector4 border = default, int maxSize = 2048, bool compress = false)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[MysteryBuilder] 그림 없음: " + path); return null; }
            bool dirty = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
            if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; dirty = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
            if (ti.maxTextureSize != maxSize) { ti.maxTextureSize = maxSize; dirty = true; }
            var comp = compress ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
            if (ti.textureCompression != comp) { ti.textureCompression = comp; dirty = true; }
            if (ti.spriteBorder != border) { ti.spriteBorder = border; dirty = true; }
            if (dirty) ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        internal static Sprite OldSprite(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderByDescending(s => s.rect.width * s.rect.height).FirstOrDefault();

        internal sealed class Sprites
        {
            public readonly Sprite Bg = OldSprite(Old + "background_art/1e1_bg2.png");
            public readonly Sprite XMark = OldSprite(Old + "X2.png");
            public readonly Sprite Vignette = LoadSprite(AlibiUi + "vignette.png", default, 1024);
            public readonly Sprite DialogueBox = LoadSprite(AlibiUi + "dialogue_box.png", new Vector4(140, 70, 140, 70));
            public readonly Sprite InkBox = LoadSprite(AlibiUi + "ink_box.png", new Vector4(16, 16, 16, 16), 256);
            public readonly Sprite TagPaper = LoadSprite(AlibiUi + "tag_paper.png", new Vector4(22, 22, 22, 22), 256);
            public readonly Sprite TagCrimson = LoadSprite(AlibiUi + "tag_crimson.png", new Vector4(22, 22, 22, 22), 256);
            public readonly Sprite TagDark = LoadSprite(AlibiUi + "tag_dark.png", new Vector4(22, 22, 22, 22), 256);
            public readonly Sprite White = LoadSprite(AlibiUi + "white.png", default, 32);
            public readonly Sprite HourHand = LoadSprite(AlibiUi + "watch_hand_hour.png", default, 512);
            public readonly Sprite MinuteHand = LoadSprite(AlibiUi + "watch_hand_minute.png", default, 512);
            public readonly Sprite Parchment = LoadSprite(AlibiCodex + "parchment_page.png");
            public readonly Sprite Leather = LoadSprite(AlibiCodex + "leather_cover.png");
            public readonly Sprite InkRing = LoadSprite(AlibiCodex + "ink_stamp_ring.png");
            public readonly Sprite Blood = LoadSprite(AlibiCodex + "blood_splatter.png");
            public readonly Sprite Watch = LoadSprite(AlibiCodex + "pocket_watch_face.png", default, 512);
            public readonly Sprite TornNote = LoadSprite(AlibiCodex + "torn_note.png");
            public readonly Sprite PencilCircle = LoadSprite(AlibiCodex + "red_pencil_circle.png", default, 1024);
            public readonly Sprite Seal = LoadSprite(AlibiCodex + "wax_seal.png", default, 512);
            public readonly Sprite Pin = LoadSprite(Art + "UI/ui_pin.png", default, 256);
            public readonly Sprite Magnifier = LoadSprite(Art + "UI/ui_magnifier.png", default, 512);
            public readonly Sprite Clip = LoadSprite(Art + "UI/ui_paperclip.png", default, 256);
            public readonly Sprite Testimony = LoadSprite(Art + "UI/ui_testimony.png", default, 512);
            public readonly Sprite CaseTag = LoadSprite(Art + "UI/ui_case_tag.png", default, 512);
            public readonly Sprite Shade = LoadSprite(Art + "UI/ui_shade.png", default, 256);
        }

        // ───────────────────────── 조립 ─────────────────────────

        static void Build()
        {
            PrepareFonts();
            var s = new Sprites();
            var c = Case01.Build();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.02f, 0.03f);
            camGo.transform.position = new Vector3(0, 0, -10);

            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var canvasGo = new GameObject("MysteryCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            Img(Stretch("Backdrop", root), Panel).raycastTarget = false;
            var shake = Stretch("ShakeRoot", root);
            var hud = canvasGo.AddComponent<MysteryHud>();
            var roomView = BuildRoom(shake.transform, s, c);
            var talkView = BuildTalk(shake.transform, s);
            var book = BuildBook(shake.transform, s);
            BuildTopBar(shake.transform, s, hud);
            BuildToast(root, s, hud);
            var viewer = BuildViewer(root, s);
            var reader = BuildReader(root, s);
            var theory = BuildTheory(root, s);
            BuildObjection(root, s, hud);
            BuildMurder(root, s, hud);
            BuildStory(root, s, hud);
            BuildReport(root, s, hud);
            foreach (var g in new[] { hud.objectionGroup, hud.murderGroup, hud.toastGroup })
            {
                g.alpha = 0f;
                g.gameObject.SetActive(false);
            }

            var audioGo = new GameObject("MysteryAudio");
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

            var game = new GameObject("MysteryGame").AddComponent<MysteryGame>();
            game.hud = hud;
            game.room = roomView;
            game.talk = talkView;
            game.book = book;
            game.viewer = viewer;
            game.reader = reader;
            game.theory = theory;
            game.audioFx = audio;
            game.shakeRoot = (RectTransform)shake.transform;

            var entries = new List<MysteryGame.SpriteEntry>();
            foreach (var r in c.Rooms) entries.Add(Entry(r.Image, LoadSprite(Art + "Rooms/" + r.Image + ".png", default, 2048, true)));
            foreach (var key in c.Evidence.Select(e => e.Image).Where(k => !string.IsNullOrEmpty(k)).Distinct())
                entries.Add(Entry(key, LoadSprite(Art + "Evidence/" + key + ".png", default, 1024, true)));
            entries.Add(Entry("ev_covered_body", LoadSprite(Art + "Evidence/ev_covered_body.png", default, 1024, true)));
            foreach (var key in c.Prologue.Select(p => p.Image).Concat(c.Endings.SelectMany(e => e.Panels).Select(p => p.Image)).Distinct())
                entries.Add(Entry(key, LoadSprite(Art + "Story/" + key + ".png", default, 2048, true)));
            entries.Add(Entry("ui_testimony", s.Testimony));
            game.art = entries.Where(e => e.sprite != null).ToArray();

            var cardFiles = new Dictionary<string, string> { ["카이"] = "1", ["미로"] = "2", ["노아"] = "3", ["세이"] = "4", ["제로"] = "5", ["하루"] = "6" };
            game.characters = CharacterSheets.All.Select(ch => new MysteryGame.CharacterArt
            {
                name = ch.Name,
                card = OldSprite(Old + "Char/" + cardFiles[ch.Name] + ".png"),
                portrait = OldSprite(Old + "Char/" + ch.PortraitKey + ".png"),
                face = LoadSprite(AlibiUi + "face_" + ch.PortraitKey + ".png", default, 256),
            }).ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[MysteryBuilder] 씬 저장: " + ScenePath);
        }

        static MysteryGame.SpriteEntry Entry(string key, Sprite sp) => new MysteryGame.SpriteEntry { key = key, sprite = sp };

        // ───────────────────────── 방 조사 ─────────────────────────

        static RoomView BuildRoom(Transform parent, Sprites s, MysteryCase c)
        {
            var host = UI("Room", parent, 0, 72, 1190, 1008);
            var view = host.AddComponent<RoomView>();
            view.root = host;
            Img(Stretch("Bg", host.transform), Panel).raycastTarget = false;

            var image = Img(UI("RoomImage", host.transform, 0, 0, 1190, 793), Color.white);
            image.raycastTarget = false;
            view.roomImage = image;
            var vig = Img(UI("Vignette", host.transform, 0, 0, 1190, 793), new Color(1, 1, 1, 0.55f), s.Vignette);
            vig.raycastTarget = false;
            var overlays = UI("Overlays", host.transform, 0, 0, 1190, 793);
            view.overlayLayer = (RectTransform)overlays.transform;
            var ovImg = Img(UI("OverlayTemplate", overlays.transform, 0, 0, 200, 200), new Color(0.8f, 0.82f, 0.9f, 1f));   // 밤 조명에 맞춰 살짝 어둡고 차갑게
            ovImg.raycastTarget = false;
            view.overlayTemplate = ovImg;

            var layer = UI("Hotspots", host.transform, 0, 0, 1190, 793);
            view.hotspotLayer = (RectTransform)layer.transform;
            var spot = UI("HotspotTemplate", layer.transform, 0, 0, 120, 80);
            var spotImg = Img(spot, Clear);
            var spotBtn = spot.AddComponent<Button>();
            spotBtn.targetGraphic = spotImg;
            spotBtn.transition = Selectable.Transition.None;
            spot.AddComponent<HoverRelay>();
            var pin = Img(UI("Pin", spot.transform, 0, 0, 34, 34), Color.white, s.Pin);
            pin.preserveAspect = true;
            pin.raycastTarget = false;
            var prt = (RectTransform)pin.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(1, 1);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(-6, -6);
            pin.gameObject.SetActive(false);
            view.hotspotTemplate = spotBtn;

            var ring = Img(UI("HoverRing", layer.transform, 0, 0, 160, 100), new Color(1, 1, 1, 0.95f), s.PencilCircle);
            ring.raycastTarget = false;
            var rrt = ring.rectTransform;
            rrt.pivot = new Vector2(0.5f, 0.5f);
            view.hoverRing = rrt;
            var tag = UI("HoverTag", layer.transform, 0, 0, 220, 42);
            var tagImg = Img(tag, Color.white, s.TagPaper);
            tagImg.type = Image.Type.Sliced;
            tagImg.raycastTarget = false;
            ((RectTransform)tag.transform).pivot = new Vector2(0.5f, 0.5f);
            view.hoverTag = (RectTransform)tag.transform;
            view.hoverLabel = Txt(UI("Label", tag.transform, 0, 0, 220, 42), "이름", _serifBold, 18, Ink, TextAlignmentOptions.Center);

            var plate = UI("RoomPlate", host.transform, 14, 12, 250, 52);
            var plateImg = Img(plate, Color.white, s.TagDark);
            plateImg.type = Image.Type.Sliced;
            plateImg.raycastTarget = false;
            view.roomName = Txt(UI("Name", plate.transform, 0, 0, 250, 52), "서재", _serifBold, 25, Cream, TextAlignmentOptions.Center);
            view.interrogateButton = Btn("InterrogateButton", host.transform, 932, 12, 244, 54, "손님 심문  ▶", _serifBold, 22, s.TagCrimson, Cream, out _);

            // 아래: 서술창 · 저택 도면
            Img(UI("BottomBg", host.transform, 0, 793, 1190, 215), new Color(0.06f, 0.045f, 0.05f, 1f)).raycastTarget = false;
            Line(host.transform, 0, 793, 1190, 2, new Color(0.75f, 0.6f, 0.36f, 0.5f), s);
            var desc = UI("Description", host.transform, 12, 805, 800, 192);
            var descImg = Img(desc, Color.white, s.TagPaper);
            descImg.type = Image.Type.Sliced;
            view.descButton = AddButton(desc, descImg);
            view.descTitle = Txt(UI("Title", desc.transform, 26, 12, 600, 34), "서재", _serifBold, 22, Crimson, TextAlignmentOptions.MidlineLeft);
            view.descText = Txt(UI("Text", desc.transform, 26, 50, 750, 132), "", _serif, 20, Ink, TextAlignmentOptions.TopLeft);
            view.descText.lineSpacing = -6;
            var dtw = desc.AddComponent<AlibiTypewriter>();
            dtw.target = view.descText;
            dtw.charsPerSecond = 60f;
            view.descTypewriter = dtw;

            var map = UI("Map", host.transform, 822, 805, 356, 192);
            var mapImg = Img(map, Color.white, s.TagPaper);
            mapImg.type = Image.Type.Sliced;
            mapImg.raycastTarget = false;
            const float ox = 14, oy = 10, w = 104, h = 42, px = 116, py = 54;
            var nodes = new List<RoomView.MapNode>();
            var links = new List<RoomView.MapLink>();
            var linkRoot = UI("Corridors", map.transform, 0, 0, 356, 192);
            foreach (var cor in c.Corridors)
            {
                var a = c.RoomById(cor[0]); var b = c.RoomById(cor[1]);
                float ax = ox + a.X * px + w / 2, ay = oy + a.Y * py + h / 2, bx = ox + b.X * px + w / 2, by = oy + b.Y * py + h / 2;
                bool horizontal = Mathf.Approximately(ay, by);
                var li = horizontal
                    ? Img(UI($"Link_{a.Id}_{b.Id}", linkRoot.transform, Mathf.Min(ax, bx), ay - 2, Mathf.Abs(bx - ax), 4), new Color(Ink.r, Ink.g, Ink.b, 0.6f), s.White)
                    : Img(UI($"Link_{a.Id}_{b.Id}", linkRoot.transform, ax - 2, Mathf.Min(ay, by), 4, Mathf.Abs(by - ay)), new Color(Ink.r, Ink.g, Ink.b, 0.6f), s.White);
                li.raycastTarget = false;
                links.Add(new RoomView.MapLink { a = a.Id, b = b.Id, image = li });
            }
            foreach (var r in c.Rooms)
            {
                var node = UI("Node_" + r.Id, map.transform, ox + r.X * px, oy + r.Y * py, w, h);
                var box = Img(node, Color.white, s.TagPaper);
                box.type = Image.Type.Sliced;
                var btn = AddButton(node, box);
                var outline = Img(Stretch("Outline", node.transform), new Color(Ink.r, Ink.g, Ink.b, 0.8f), s.InkBox);
                outline.type = Image.Type.Sliced;
                outline.raycastTarget = false;
                var relay = node.AddComponent<HoverRelay>();
                var label = Txt(UI("Label", node.transform, 0, 0, w, h), r.Name, _pen, 24, Ink, TextAlignmentOptions.Center);
                var badge = Txt(UI("Badge", node.transform, w - 26, -6, 30, 24), "", _serifBold, 14, Bright, TextAlignmentOptions.Center);
                nodes.Add(new RoomView.MapNode { room = r.Id, button = btn, box = box, label = label, badge = badge, hover = relay });
            }
            view.nodes = nodes.ToArray();
            view.links = links.ToArray();
            view.moveHint = Txt(UI("MoveHint", map.transform, 14, 168, 330, 20), "", _serif, 13, Brown, TextAlignmentOptions.MidlineLeft);
            return view;
        }

        // ───────────────────────── 심문 ─────────────────────────

        static TalkView BuildTalk(Transform parent, Sprites s)
        {
            var host = UI("Talk", parent, 0, 72, 1190, 1008);
            var view = host.AddComponent<TalkView>();
            view.root = host;
            var bg = Img(Stretch("Background", host.transform), new Color(0.55f, 0.5f, 0.58f), s.Bg);
            bg.raycastTarget = false;
            view.background = bg;
            var vig = Img(Stretch("Vignette", host.transform), new Color(1, 1, 1, 0.9f), s.Vignette);
            vig.raycastTarget = false;
            view.redFlash = Img(Stretch("RedFlash", host.transform), new Color(0.62f, 0.05f, 0.1f, 0f));
            view.redFlash.raycastTarget = false;

            // 손님 목록
            var lineup = Stretch("Lineup", host.transform);
            view.lineupRoot = lineup;
            view.lineupBack = Btn("BackToRoom", lineup.transform, 20, 18, 210, 44, "◀ 조사로 (Esc)", _serif, 17, s.TagDark, Cream, out _);
            Txt(UI("Title", lineup.transform, 0, 34, 1190, 52), "식당에 모인 손님들", _serifBold, 34, Cream, TextAlignmentOptions.Center);
            Txt(UI("Subtitle", lineup.transform, 0, 90, 1190, 30), "카드를 눌러 마주 앉는다 · 진술 한 줄을 골라 증거로 맞서라", _serif, 19, Muted, TextAlignmentOptions.Center);
            var victim = Img(UI("VictimCard", lineup.transform, 1044, 20, 112, 158), new Color(0.5f, 0.5f, 0.52f));
            victim.preserveAspect = true;
            victim.raycastTarget = false;
            view.victimArt = victim;
            view.victimLabel = Txt(UI("VictimLabel", lineup.transform, 1020, 180, 160, 26), "피해자", _serif, 16, Muted, TextAlignmentOptions.Center);
            float[] arc = { 30, 10, 0, 10, 30 };
            for (int i = 0; i < 5; i++)
            {
                var card = UI("Card" + i, lineup.transform, 48 + i * 224, 220 + arc[i], 200, 283);
                var art = Img(card, Color.white);
                art.preserveAspect = true;
                var ui = new TalkView.CardUI
                {
                    button = AddButton(card, art),
                    art = art,
                    name = Txt(UI("Name", card.transform, 30, 174, 140, 38), "이름", _serifBold, 21, Ink, TextAlignmentOptions.Center),
                    note = Txt(UI("Note", card.transform, -10, 290, 220, 26), "", _serif, 16, Cream, TextAlignmentOptions.Center),
                    stamp = Txt(UI("Stamp", card.transform, 104, 4, 110, 60), "", _brush, 40, Bright, TextAlignmentOptions.Center),
                };
                ui.stamp.rectTransform.localEulerAngles = new Vector3(0, 0, -14);
                var x = Img(UI("DeadMark", card.transform, 22, 30, 156, 150), Color.white, s.XMark);
                x.preserveAspect = true;
                x.raycastTarget = false;
                x.enabled = false;
                ui.deadMark = x;
                view.cards[i] = ui;
            }

            // 마주 앉은 사람
            var talk = Stretch("Face", host.transform);
            view.talkRoot = talk;
            var portraitGo = UI("Portrait", talk.transform, 40, 26, 620, 877);
            portraitGo.AddComponent<CanvasGroup>();
            var pr = (RectTransform)portraitGo.transform;
            pr.pivot = new Vector2(0.5f, 0f);
            pr.anchoredPosition = new Vector2(40 + 310, -(26 + 877));
            view.portraitRect = pr;
            view.portrait = Img(portraitGo, Color.white);
            view.portrait.preserveAspect = true;
            view.portrait.raycastTarget = false;
            view.nameText = Txt(UI("Name", talk.transform, 700, 10, 460, 58), "이름", _serifBold, 42, Cream, TextAlignmentOptions.MidlineLeft);
            view.roleText = Txt(UI("Role", talk.transform, 700, 68, 460, 28), "", _serifBold, 19, Amber, TextAlignmentOptions.MidlineLeft);
            view.bioText = Txt(UI("Bio", talk.transform, 700, 98, 470, 52), "", _serif, 16, Muted, TextAlignmentOptions.TopLeft);
            view.backButton = Btn("BackButton", talk.transform, 700, 154, 220, 40, "◀ 손님 목록 (Esc)", _serif, 16, s.TagDark, Cream, out _);

            // 진술 목록
            var stPanel = UI("Statements", talk.transform, 686, 204, 488, 432);
            var stBg = Img(stPanel, new Color(0.02f, 0.015f, 0.02f, 0.72f), s.TagDark);
            stBg.type = Image.Type.Sliced;
            stBg.raycastTarget = false;
            Txt(UI("Title", stPanel.transform, 18, 6, 200, 50), "진술", _brush, 44, Bright, TextAlignmentOptions.MidlineLeft);
            view.statementHint = Txt(UI("Hint", stPanel.transform, 120, 16, 352, 36), "", _serif, 14, Muted, TextAlignmentOptions.MidlineRight);
            var stList = UI("List", stPanel.transform, 12, 62, 464, 362);
            var vlg = stList.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var row = UI("StatementTemplate", stList.transform, 0, 0, 464, 64);
            var rowHit = Img(row, Clear);
            var rowBtn = AddButton(row, rowHit);
            var rcb = rowBtn.colors;
            rcb.normalColor = Clear; rcb.highlightedColor = new Color(1, 1, 1, 0.08f); rcb.pressedColor = new Color(1, 1, 1, 0.14f); rcb.selectedColor = Clear; rcb.disabledColor = Clear;
            rowBtn.colors = rcb;
            row.AddComponent<LayoutElement>().preferredHeight = 64;
            var frame = Img(Stretch("Frame", row.transform), Clear, s.TagCrimson);
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = false;
            Txt(UI("Text", row.transform, 12, 4, 380, 56), "「진술」", _serif, 17, Cream, TextAlignmentOptions.MidlineLeft);
            var mark = Txt(UI("Mark", row.transform, 384, 6, 80, 52), "", _brush, 28, Bright, TextAlignmentOptions.Center);
            mark.rectTransform.localEulerAngles = new Vector3(0, 0, -10);
            view.statementContent = (RectTransform)stList.transform;
            view.statementTemplate = rowBtn;

            // 최후의 추궁 머리
            var confront = UI("Confront", talk.transform, 30, 16, 640, 130);
            view.confrontRoot = confront;
            view.confrontTitle = Txt(UI("Title", confront.transform, 0, 0, 640, 80), "최후의 추궁", _brush, 70, Bright, TextAlignmentOptions.MidlineLeft);
            var marks = new List<Image>();
            Txt(UI("Label", confront.transform, 0, 84, 120, 34), "남은 기회", _serif, 16, Muted, TextAlignmentOptions.MidlineLeft);
            for (int i = 0; i < 3; i++)
            {
                var m = Img(UI("Resolve" + i, confront.transform, 100 + i * 40, 86, 30, 30), Bright, s.Seal);
                m.preserveAspect = true;
                m.raycastTarget = false;
                marks.Add(m);
            }
            view.resolveMarks = marks.ToArray();
            confront.SetActive(false);

            // 질문 쪽지
            var chips = UI("Chips", talk.transform, 26, 646, 1140, 46);
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
            var chipLabel = Txt(UI("Label", chip.transform, 0, 0, 160, 34), "노아와의 관계", _serif, 16, Ink, TextAlignmentOptions.Center);
            chipLabel.textWrappingMode = TextWrappingModes.NoWrap;
            view.chipContent = (RectTransform)chips.transform;
            view.chipTemplate = chipBtn;

            // 대사창
            var box = UI("Box", talk.transform, 18, 698, 1154, 300);
            var boxImg = Img(box, Color.white, s.DialogueBox);
            boxImg.type = Image.Type.Sliced;
            view.boxButton = AddButton(box, boxImg);
            view.speaker = Txt(UI("Speaker", box.transform, 132, 38, 600, 44), "", _serifBold, 27, Crimson, TextAlignmentOptions.MidlineLeft);
            view.question = Txt(UI("Question", box.transform, 78, 84, 1030, 28), "", _serif, 17, Brown, TextAlignmentOptions.MidlineLeft);
            view.question.fontStyle = FontStyles.Italic;
            view.line = Txt(UI("Line", box.transform, 78, 114, 1034, 124), "", _serif, 24, Ink, TextAlignmentOptions.TopLeft);
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
            ph.text = "직접 물어보기 — 무엇이든 (Enter)";
            ph.color = new Color(0.45f, 0.38f, 0.3f, 0.75f);
            ph.fontStyle = FontStyles.Italic;
            input.pointSize = 19;
            view.input = input;
            view.askButton = Btn("AskButton", box.transform, 976, 236, 136, 48, "묻기", _serifBold, 20, s.TagDark, Cream, out _);
            talk.SetActive(false);
            host.SetActive(false);
            return view;
        }

        // ───────────────────────── 수사 수첩 ─────────────────────────

        internal static CaseBookView BuildBook(Transform parent, Sprites s)
        {
            var host = UI("CaseBook", parent, 1190, 72, 730, 1008);
            var view = host.AddComponent<CaseBookView>();
            Img(UI("Cover", host.transform, 0, 6, 724, 998), Color.white, s.Leather).raycastTarget = false;
            var page = UI("Page", host.transform, 18, 22, 688, 966);
            Img(page, Color.white, s.Parchment).raycastTarget = false;
            var p = page.transform;

            string[] tabs = { "증거품", "타임라인", "원고", "의문점" };
            for (int i = 0; i < 4; i++)
            {
                var tab = UI("Tab" + i, p, 14 + i * 166, 12, 160, 52);
                var hit = Img(tab, Clear);
                var b = tab.AddComponent<Button>();
                b.targetGraphic = hit;
                b.transition = Selectable.Transition.None;
                view.tabButtons[i] = b;
                view.tabLabels[i] = Txt(UI("Label", tab.transform, 0, 0, 160, 46), tabs[i], _serifBold, 21, Ink, TextAlignmentOptions.Center);
                var m = Img(UI("Mark", tab.transform, 24, 46, 112, 4), Crimson, s.White);
                m.raycastTarget = false;
                view.tabMarks[i] = m;
            }
            Line(p, 16, 66, 656, 1, new Color(Ink.r, Ink.g, Ink.b, 0.35f), s);

            // 증거품
            var evPanel = UI("EvidencePanel", p, 0, 0, 688, 966);
            view.tabPanels[0] = evPanel;
            var grid = ScrollList("Grid", evPanel.transform, 16, 74, 656, 404, out var gridContent);
            Object.DestroyImmediate(gridContent.GetComponent<VerticalLayoutGroup>());
            var gl = gridContent.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(152, 132);
            gl.spacing = new Vector2(10, 10);
            gl.padding = new RectOffset(4, 4, 6, 6);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 4;
            var card = UI("EvidenceTemplate", gridContent.transform, 0, 0, 152, 132);
            var cardBg = Img(card, new Color(1, 1, 1, 0.55f), s.TagPaper);
            cardBg.type = Image.Type.Sliced;
            var cardBtn = AddButton(card, cardBg);
            var cframe = Img(Stretch("Frame", card.transform), Clear, s.InkBox);
            cframe.type = Image.Type.Sliced;
            cframe.raycastTarget = false;
            var cart = Img(UI("Art", card.transform, 16, 6, 120, 88), Color.white);
            cart.preserveAspect = true;
            cart.raycastTarget = false;
            Txt(UI("Name", card.transform, 4, 94, 144, 36), "증거", _serif, 14, Ink, TextAlignmentOptions.Center).lineSpacing = -18;
            var dot = Txt(UI("New", card.transform, 126, 2, 24, 24), "●", _serif, 16, Bright, TextAlignmentOptions.Center);
            view.evidenceContent = gridContent;
            view.evidenceTemplate = cardBtn;
            view.evidenceEmpty = Txt(UI("Empty", evPanel.transform, 16, 200, 656, 60), "아직 증거가 없다. 방을 살펴보자.", _pen, 28, Brown, TextAlignmentOptions.Center);

            var detail = UI("Detail", evPanel.transform, 16, 490, 656, 370);
            view.detailRoot = detail;
            Line(detail.transform, 0, 0, 656, 1, new Color(Ink.r, Ink.g, Ink.b, 0.3f), s);
            var dart = Img(UI("Art", detail.transform, 0, 14, 150, 150), Color.white);
            dart.preserveAspect = true;
            dart.raycastTarget = false;
            view.detailArt = dart;
            view.detailName = Txt(UI("Name", detail.transform, 164, 10, 492, 34), "증거", _serifBold, 21, Ink, TextAlignmentOptions.MidlineLeft);
            view.detailSource = Txt(UI("Source", detail.transform, 164, 44, 492, 22), "", _serif, 14, Brown, TextAlignmentOptions.MidlineLeft);
            view.detailText = Txt(UI("Text", detail.transform, 164, 70, 492, 170), "", _serif, 16, Ink, TextAlignmentOptions.TopLeft);
            view.detailText.lineSpacing = -8;
            view.detailFound = Txt(UI("Found", detail.transform, 0, 244, 656, 44), "", _pen, 21, Crimson, TextAlignmentOptions.MidlineLeft);
            view.inspectButton = Btn("InspectButton", detail.transform, 0, 300, 176, 52, "  돋보기로 보기", _serif, 17, s.TagPaper, Ink, out var inspectLabel);
            inspectLabel.alignment = TextAlignmentOptions.MidlineRight;
            inspectLabel.margin = new Vector4(0, 0, 14, 0);
            var mag = Img(UI("Icon", view.inspectButton.transform, 6, 6, 40, 40), Color.white, s.Magnifier);
            mag.preserveAspect = true;
            mag.raycastTarget = false;
            view.readButton = Btn("ReadButton", detail.transform, 188, 300, 110, 52, "읽기", _serif, 17, s.TagPaper, Ink, out _);
            view.actButton = Btn("ActButton", detail.transform, 382, 296, 274, 60, "보여 주기", _serifBold, 21, s.TagCrimson, Cream, out var actLabel);
            view.actLabel = actLabel;

            // 타임라인
            var tlPanel = UI("TimelinePanel", p, 0, 0, 688, 966);
            view.tabPanels[1] = tlPanel;
            Txt(UI("Head", tlPanel.transform, 16, 74, 656, 34), "알아낸 사실을 시각순으로 — 어긋나는 곳을 찾아라", _pen, 23, Brown, TextAlignmentOptions.MidlineLeft);
            ScrollList("List", tlPanel.transform, 16, 112, 656, 740, out var tlContent);
            view.timelineContent = tlContent;
            view.timelineTemplate = Txt(UI("RowTemplate", tlContent.transform, 0, 0, 640, 30), "22:30  사실", _serif, 17, Ink, TextAlignmentOptions.TopLeft);

            // 원고
            var chPanel = UI("ChapterPanel", p, 0, 0, 688, 966);
            view.tabPanels[2] = chPanel;
            Txt(UI("Head", chPanel.transform, 16, 74, 656, 60), "노아의 원고 『가면들의 만찬』\n<size=75%>장을 눌러 읽는다. 사라진 장이 있다.</size>", _serifBold, 22, Ink, TextAlignmentOptions.TopLeft);
            var chList = UI("List", chPanel.transform, 16, 144, 656, 620);
            var chv = chList.AddComponent<VerticalLayoutGroup>();
            chv.spacing = 8;
            chv.childControlWidth = true; chv.childControlHeight = true;
            chv.childForceExpandWidth = true; chv.childForceExpandHeight = false;
            var chRow = UI("ChapterTemplate", chList.transform, 0, 0, 656, 70);
            var chImg = Img(chRow, new Color(1, 1, 1, 0.6f), s.TagPaper);
            chImg.type = Image.Type.Sliced;
            var chBtn = AddButton(chRow, chImg);
            chRow.AddComponent<LayoutElement>().preferredHeight = 70;
            Txt(UI("Title", chRow.transform, 22, 0, 440, 70), "제1장", _serifBold, 20, Ink, TextAlignmentOptions.MidlineLeft);
            Txt(UI("State", chRow.transform, 450, 0, 186, 70), "읽기", _pen, 24, Crimson, TextAlignmentOptions.MidlineRight);
            view.chapterContent = (RectTransform)chList.transform;
            view.chapterTemplate = chBtn;
            var clip = Img(UI("Clip", chPanel.transform, 600, 60, 60, 120), Color.white, s.Clip);
            clip.preserveAspect = true;
            clip.raycastTarget = false;

            // 의문점
            var qPanel = UI("QuestionPanel", p, 0, 0, 688, 966);
            view.tabPanels[3] = qPanel;
            Txt(UI("Head", qPanel.transform, 16, 74, 656, 34), "풀리지 않은 것들 — 이걸 풀면 범인이 보인다", _pen, 23, Brown, TextAlignmentOptions.MidlineLeft);
            ScrollList("List", qPanel.transform, 16, 112, 656, 740, out var qContent);
            view.questionContent = qContent;
            view.questionTemplate = Txt(UI("RowTemplate", qContent.transform, 0, 0, 640, 30), "？ 의문", _serif, 18, Ink, TextAlignmentOptions.TopLeft);

            // 하단
            Line(p, 16, 866, 656, 1, new Color(Ink.r, Ink.g, Ink.b, 0.35f), s);
            view.counts = Txt(UI("Counts", p, 18, 880, 300, 60), "증거 0", _pen, 24, Ink, TextAlignmentOptions.MidlineLeft);
            view.theoryButton = Btn("TheoryButton", p, 352, 876, 320, 72, "사건 재구성", _serifBold, 27, s.TagCrimson, Cream, out var theoryLabel);
            view.theoryLabel = theoryLabel;
            return view;
        }

        /// <summary>세로 스크롤 목록(내용은 VerticalLayout + ContentSizeFitter).</summary>
        internal static GameObject ScrollList(string name, Transform parent, float x, float y, float w, float h, out RectTransform content)
        {
            var go = UI(name, parent, x, y, w, h);
            Img(go, Clear);
            var scroll = go.AddComponent<ScrollRect>();
            var viewport = Stretch("Viewport", go.transform);
            viewport.AddComponent<RectMask2D>();
            var c = new GameObject("Content", typeof(RectTransform));
            c.transform.SetParent(viewport.transform, false);
            var crt = (RectTransform)c.transform;
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1);
            crt.sizeDelta = Vector2.zero;
            var vlg = c.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 12;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            c.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = crt;
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            scroll.inertia = false;
            content = crt;
            return go;
        }

        // ───────────────────────── 상단 바 · 알림 ─────────────────────────

        static void BuildTopBar(Transform parent, Sprites s, MysteryHud hud)
        {
            var bar = UI("TopBar", parent, 0, 0, 1920, 72);
            Img(bar, new Color(0.03f, 0.022f, 0.03f, 0.95f)).raycastTarget = false;
            Line(bar.transform, 0, 71, 1920, 1, new Color(0.75f, 0.6f, 0.36f, 0.45f), s);
            hud.caseTitle = Txt(UI("CaseTitle", bar.transform, 28, 2, 600, 42), "자정의 원고", _brush, 40, Cream, TextAlignmentOptions.MidlineLeft);
            hud.caseSub = Txt(UI("CaseSub", bar.transform, 30, 44, 600, 24), "", _serif, 15, Muted, TextAlignmentOptions.MidlineLeft);
            var watch = UI("Watch", bar.transform, 806, -8, 86, 86);
            var face = Img(watch, Color.white, s.Watch);
            face.preserveAspect = true;
            face.raycastTarget = false;
            hud.hourHand = Hand("HourHand", watch.transform, s.HourHand, 6, 26);
            hud.minuteHand = Hand("MinuteHand", watch.transform, s.MinuteHand, 6, 34);
            hud.clockText = Txt(UI("Clock", bar.transform, 900, -2, 170, 52), "23:45", _serifBold, 32, Cream, TextAlignmentOptions.MidlineLeft);
            hud.dawnText = Txt(UI("Dawn", bar.transform, 900, 42, 300, 26), "", _serif, 15, Amber, TextAlignmentOptions.MidlineLeft);
            hud.aiStatus = Txt(UI("AiStatus", bar.transform, 300, 46, 480, 22), "AI 확인 중…", _serif, 13, new Color(Muted.r, Muted.g, Muted.b, 0.8f), TextAlignmentOptions.MidlineRight);
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

        static void BuildToast(Transform root, Sprites s, MysteryHud hud)
        {
            var toast = UI("Toast", root, 1110, 6, 790, 60);   // 상단 바 오른쪽 — 게임 화면을 가리지 않는 자리
            var img = Img(toast, Color.white, s.TagPaper);
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            hud.toastGroup = toast.AddComponent<CanvasGroup>();
            hud.toastText = Txt(UI("Text", toast.transform, 20, 4, 750, 52), "알림", _serif, 18, Ink, TextAlignmentOptions.Center);
        }

        // ───────────────────────── 증거 확대 · 원고 · 재구성 ─────────────────────────

        internal static EvidenceViewer BuildViewer(Transform root, Sprites s)
        {
            var dim = Stretch("EvidenceViewer", root);
            var view = dim.AddComponent<EvidenceViewer>();
            view.root = dim;
            Img(dim, new Color(0.02f, 0.01f, 0.02f, 0.93f));
            var panel = UI("Panel", dim.transform, 140, 50, 1640, 980);
            Img(panel, Color.white, s.Parchment).raycastTarget = false;
            var artGo = UI("Art", panel.transform, 50, 60, 860, 860);
            var art = Img(artGo, Color.white);
            art.preserveAspect = true;
            art.raycastTarget = false;
            view.art = art;
            var layer = UI("Details", panel.transform, 50, 60, 860, 860);
            view.detailLayer = (RectTransform)layer.transform;
            var spot = UI("DetailTemplate", layer.transform, 0, 0, 100, 100);
            var spotImg = Img(spot, Clear);
            var spotBtn = spot.AddComponent<Button>();
            spotBtn.targetGraphic = spotImg;
            spotBtn.transition = Selectable.Transition.None;
            spot.AddComponent<HoverRelay>();
            var found = Img(Stretch("Found", spot.transform), new Color(1, 1, 1, 0.85f), s.PencilCircle);
            found.raycastTarget = false;
            found.gameObject.SetActive(false);
            view.detailTemplate = spotBtn;
            var ring = Img(UI("HoverRing", layer.transform, 0, 0, 120, 120), Color.white, s.PencilCircle);
            ring.raycastTarget = false;
            ring.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            view.hoverRing = ring.rectTransform;
            view.nameText = Txt(UI("Name", panel.transform, 950, 60, 640, 50), "증거", _serifBold, 32, Ink, TextAlignmentOptions.MidlineLeft);
            view.sourceText = Txt(UI("Source", panel.transform, 952, 110, 640, 26), "", _serif, 16, Brown, TextAlignmentOptions.MidlineLeft);
            Line(panel.transform, 950, 144, 620, 1, new Color(Ink.r, Ink.g, Ink.b, 0.35f), s);
            view.bodyText = Txt(UI("Body", panel.transform, 950, 160, 630, 230), "", _serif, 20, Ink, TextAlignmentOptions.TopLeft);
            view.bodyText.lineSpacing = -6;
            view.hintText = Txt(UI("Hint", panel.transform, 950, 400, 630, 60), "", _pen, 24, Brown, TextAlignmentOptions.TopLeft);
            view.foundText = Txt(UI("Found", panel.transform, 950, 470, 630, 400), "", _serif, 19, Crimson, TextAlignmentOptions.TopLeft);
            view.closeButton = Btn("Close", panel.transform, 1440, 900, 160, 54, "닫기 (Esc)", _serif, 18, s.TagDark, Cream, out _);
            dim.SetActive(false);
            return view;
        }

        internal static ReaderView BuildReader(Transform root, Sprites s)
        {
            var dim = Stretch("Reader", root);
            var view = dim.AddComponent<ReaderView>();
            view.root = dim;
            Img(dim, new Color(0.02f, 0.01f, 0.02f, 0.9f));
            var page = UI("Page", dim.transform, 500, 30, 920, 1020);
            Img(page, Color.white, s.Parchment).raycastTarget = false;
            view.title = Txt(UI("Title", page.transform, 60, 50, 800, 60), "원고", _serifBold, 30, Ink, TextAlignmentOptions.Center);
            Line(page.transform, 160, 120, 600, 2, new Color(Ink.r, Ink.g, Ink.b, 0.4f), s);
            view.body = Txt(UI("Body", page.transform, 90, 150, 740, 740), "", _serif, 22, Ink, TextAlignmentOptions.TopLeft);
            view.body.lineSpacing = 8;
            view.footer = Txt(UI("Footer", page.transform, 90, 930, 500, 30), "", _serif, 15, Brown, TextAlignmentOptions.MidlineLeft);
            view.closeButton = Btn("Close", page.transform, 700, 920, 160, 52, "닫기 (Esc)", _serif, 18, s.TagDark, Cream, out _);
            dim.SetActive(false);
            return view;
        }

        internal static TheoryView BuildTheory(Transform root, Sprites s)
        {
            var dim = Stretch("Theory", root);
            var view = dim.AddComponent<TheoryView>();
            view.root = dim;
            Img(dim, new Color(0.02f, 0.01f, 0.02f, 0.95f));
            var page = UI("Report", dim.transform, 190, 30, 1540, 1020);
            Img(page, Color.white, s.Parchment).raycastTarget = false;
            Txt(UI("Title", page.transform, 0, 26, 1540, 100), "사건의 재구성", _brush, 84, Crimson, TextAlignmentOptions.Center);
            view.costNote = Txt(UI("Cost", page.transform, 0, 124, 1540, 30), "", _serif, 16, Brown, TextAlignmentOptions.Center);
            var list = UI("Sentences", page.transform, 130, 180, 1280, 560);
            var vlg = list.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 26;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            view.sentenceContent = (RectTransform)list.transform;
            var sentence = Txt(UI("SentenceTemplate", list.transform, 0, 0, 1280, 50), "문장", _serif, 30, Ink, TextAlignmentOptions.TopLeft);
            sentence.raycastTarget = true;
            sentence.gameObject.AddComponent<TmpLinkClick>();
            view.sentenceTemplate = sentence;
            view.feedback = Txt(UI("Feedback", page.transform, 130, 760, 1280, 44), "", _serifBold, 21, Crimson, TextAlignmentOptions.Center);
            view.submitButton = Btn("Submit", page.transform, 500, 820, 540, 76, "이 추리로 범인을 지목한다", _serifBold, 26, s.TagCrimson, Cream, out _);
            view.closeButton = Btn("Close", page.transform, 1340, 40, 160, 50, "닫기 (Esc)", _serif, 17, s.TagDark, Cream, out _);

            var pickerRoot = Stretch("Picker", dim.transform);
            view.pickerRoot = pickerRoot;
            var pickerDim = Img(pickerRoot, new Color(0.02f, 0.01f, 0.02f, 0.6f));
            var picker = UI("Panel", pickerRoot.transform, 560, 290, 800, 470);
            Img(picker, Color.white, s.Parchment);
            var pFrame = Img(Stretch("Frame", picker.transform), Color.white, s.TagPaper);
            pFrame.type = Image.Type.Sliced;
            pFrame.raycastTarget = false;
            view.pickerTitle = Txt(UI("Title", picker.transform, 30, 14, 600, 54), "누가", _pen, 40, Crimson, TextAlignmentOptions.MidlineLeft);
            var words = UI("Words", picker.transform, 30, 76, 740, 320);
            var gl = words.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(236, 56);
            gl.spacing = new Vector2(10, 10);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 3;
            view.pickerContent = (RectTransform)words.transform;
            view.wordTemplate = Btn("WordTemplate", words.transform, 0, 0, 236, 56, "단어", _serif, 20, s.TagPaper, Ink, out _);
            view.pickerClose = Btn("Close", picker.transform, 640, 404, 130, 46, "닫기", _serif, 17, s.TagDark, Cream, out _);
            pickerRoot.SetActive(false);
            dim.SetActive(false);
            return view;
        }

        // ───────────────────────── 컷신 · 이야기 · 보고서 ─────────────────────────

        internal static void BuildObjection(Transform root, Sprites s, MysteryHud hud)
        {
            var go = Stretch("CutObjection", root);
            hud.objectionGroup = go.AddComponent<CanvasGroup>();
            var dim = Img(go, new Color(0.02f, 0.01f, 0.02f, 0.88f));
            hud.objectionButton = AddButton(go, dim);
            var ring = Img(UI("Ring", go.transform, 660, 110, 600, 600), Color.white, s.InkRing);
            ring.preserveAspect = true;
            ring.raycastTarget = false;
            hud.objectionRing = ring.rectTransform;
            var word = Txt(UI("Word", go.transform, 460, 230, 1000, 360), "모순!", _brush, 240, Bright, TextAlignmentOptions.Center);
            hud.objectionWord = word.rectTransform;
            hud.objectionWordText = word;
            var panel = UI("Panel", go.transform, 330, 760, 1260, 200);
            var pImg = Img(panel, Color.white, s.DialogueBox);
            pImg.type = Image.Type.Sliced;
            pImg.raycastTarget = false;
            hud.objectionPanel = (RectTransform)panel.transform;
            hud.objectionText = Txt(UI("Text", panel.transform, 90, 40, 1080, 110), "설명", _serif, 24, Ink, TextAlignmentOptions.Center);
            Txt(UI("Hint", panel.transform, 0, 150, 1260, 26), "클릭해서 계속", _serif, 15, Brown, TextAlignmentOptions.Center);
        }

        internal static void BuildMurder(Transform root, Sprites s, MysteryHud hud)
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
            hud.murderNoteText = Txt(UI("Text", note.transform, 80, 90, 460, 280), "", _pen, 34, Ink, TextAlignmentOptions.Center);
            Txt(UI("Hint", go.transform, 0, 1020, 1920, 30), "클릭해서 계속", _serif, 16, Muted, TextAlignmentOptions.Center);
        }

        internal static void BuildStory(Transform root, Sprites s, MysteryHud hud)
        {
            var go = Stretch("Story", root);
            hud.storyGroup = go.AddComponent<CanvasGroup>();
            var black = Img(go, Color.black);
            hud.storyButton = AddButton(go, black);
            var imgGo = UI("Image", go.transform, 0, -100, 1920, 1280);   // 3:2 그림을 16:9 화면에 꽉 차게(위아래를 조금 자른다)
            var img = Img(imgGo, Color.white);
            img.raycastTarget = false;
            ((RectTransform)imgGo.transform).pivot = new Vector2(0.5f, 0.5f);
            ((RectTransform)imgGo.transform).anchoredPosition = new Vector2(960, -540);
            hud.storyImage = img;
            var shade = Img(UI("Shade", go.transform, 0, 560, 1920, 520), new Color(1, 1, 1, 0.92f), s.Shade);
            shade.raycastTarget = false;
            var title = Txt(UI("Title", go.transform, 70, 40, 1200, 130), "자정의 원고", _brush, 110, Cream, TextAlignmentOptions.MidlineLeft);
            hud.storyTitle = title;
            var text = Txt(UI("Text", go.transform, 260, 760, 1400, 230), "", _serif, 30, Cream, TextAlignmentOptions.Top);
            text.lineSpacing = 6;
            hud.storyText = text;
            var tw = go.AddComponent<AlibiTypewriter>();
            tw.target = text;
            tw.charsPerSecond = 34f;
            hud.storyTypewriter = tw;
            Txt(UI("Hint", go.transform, 0, 1030, 1920, 30), "클릭해서 계속", _serif, 16, Muted, TextAlignmentOptions.Center);
            hud.storyGroup.alpha = 0f;
            go.SetActive(false);
        }

        internal static void BuildReport(Transform root, Sprites s, MysteryHud hud)
        {
            var dim = Stretch("Report", root);
            Img(dim, new Color(0.02f, 0.01f, 0.02f, 0.96f));
            var report = UI("Paper", dim.transform, 160, 26, 1600, 1028);
            Img(report, Color.white, s.Parchment);
            hud.reportTitle = Txt(UI("Title", report.transform, 70, 40, 1150, 64), "사건 보고서", _serifBold, 38, Ink, TextAlignmentOptions.MidlineLeft);
            var stamp = UI("Stamp", report.transform, 1270, 22, 280, 280);
            var ring = Img(stamp, Color.white, s.InkRing);
            ring.preserveAspect = true;
            ring.raycastTarget = false;
            hud.reportStamp = (RectTransform)stamp.transform;
            hud.reportStamp.localEulerAngles = new Vector3(0, 0, -12);
            hud.reportStampWord = Txt(UI("Word", stamp.transform, 0, 40, 280, 200), "해결", _brush, 120, Crimson, TextAlignmentOptions.Center);
            hud.reportText = Txt(UI("Body", report.transform, 70, 120, 1170, 800), "", _serif, 19, Ink, TextAlignmentOptions.TopLeft);
            hud.reportText.lineSpacing = -4;
            hud.restartButton = Btn("Restart", report.transform, 650, 940, 300, 64, "처음부터", _serifBold, 24, s.TagCrimson, Cream, out _);
            hud.reportRoot = dim;
            dim.SetActive(false);
        }

        // ───────────────────────── 저작 도우미 ─────────────────────────

        internal static GameObject UI(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, w, h);
            return go;
        }

        internal static void Place(GameObject go, float x, float y, float w, float h)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        internal static GameObject Stretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go;
        }

        internal static Image Img(GameObject go, Color color, Sprite sprite = null)
        {
            var img = go.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            return img;
        }

        internal static void Line(Transform parent, float x, float y, float w, float h, Color color, Sprites s)
        {
            var img = Img(UI("Rule", parent, x, y, w, h), color, s.White);
            img.raycastTarget = false;
        }

        internal static TMP_Text Txt(GameObject go, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
        {
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        internal static Button AddButton(GameObject go, Graphic target)
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

        internal static Button Btn(string name, Transform parent, float x, float y, float w, float h, string label, TMP_FontAsset font, float size, Sprite sprite, Color fg, out TMP_Text labelText)
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
