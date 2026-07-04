using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// Binds gameplay movement and room detection to the sprites already placed in the scene.
    /// Expected scene objects: room1(+corridor1) as templates — GameConfig.RoomCount decides how many
    /// room2..N / corridor2..N-1 are cloned from them at runtime, so the mansion size is configurable.
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public class SpriteMansionView : MonoBehaviour
    {
        const float HumanSpeed = 3.8f;
        const float NpcSpeed = 2.4f;
        const float RoomEdgePadding = 0.2f;
        const float MansionCellSize = 20f; // room1~5 원래 배치 간격과 동일(씬 좌표 실측값)
        const int MaxMansionSlots = 20;    // RoomCount를 줄였을 때 꺼야 할 여분 room/corridor를 찾는 탐색 상한(GameController의 클램프 상한보다 넉넉히)

        static readonly Color Player = Hex("8A4DFF");
        static readonly Color Npc = Hex("D9B56B");
        static readonly Color Text = Hex("EEE7D6");

        GameController _controller;
        Transform _tokenRoot;
        Sprite _pixel;
        Material _litSpriteMaterial;

        [Header("타일맵 오토타일 (매판 생성되는 방 구조에 맞춰 런타임 배치)")]
        [SerializeField] Tilemap _tilemap;
        [SerializeField] TileBase _floorTile;         // tile11_4 (바닥, 스프라이트 꽉 참)
        [SerializeField] TileBase _backWallTopTile;   // top_half_of_the_frontwall (뒷벽 면 위줄)
        [SerializeField] TileBase _backWallBotTile;   // down_half_of_the_frontwall (뒷벽 면 아래줄)
        [SerializeField] TileBase _lineWallTile;      // Straight_side_wall (선벽; 회전으로 상/하/좌/우)
        [SerializeField] TileBase _cornerTile;        // Point_Shape_wall2 (바깥 볼록 코너)
        [SerializeField] TileBase _cornerLTile;       // L_shape_wall (개구부 오목 코너)
        [SerializeField] TileBase _bgTile;            // Black_background (방 밖 검정 배경)
        [SerializeField] int _tilemapSortingOrder = 1;

        [Header("뒷벽 변형 (기본 frontwall 쌍, 확률로 framewall/window 쌍)")]
        [SerializeField] TileBase _frameWallTopTile;  // top_of_framewall
        [SerializeField] TileBase _frameWallBotTile;  // down_of_framewall
        [SerializeField] TileBase _windowTopTile;     // top_of_the_window
        [SerializeField] TileBase _windowBotTile;     // down_of_the_window
        [Range(0f, 1f)][SerializeField] float _backWallVariantChance = 0.2f; // 열 단위 변형 확률(변형 시 frame/window 반반)

        [Header("카펫 데코 (별도 TilemapDecor, 바닥 위)")]
        [SerializeField] Tilemap _decorTilemap;       // Grid/TilemapDecor (sortingOrder = 바닥+1)
        [SerializeField] TileBase _carpetTL;          // carpet_0 (좌상, 각 조각 2x2유닛)
        [SerializeField] TileBase _carpetTR;          // carpet_1 (우상)
        [SerializeField] TileBase _carpetBL;          // carpet_2 (좌하)
        [SerializeField] TileBase _carpetBR;          // carpet_3 (우하)
        [Range(0f, 1f)][SerializeField] float _carpetChance = 0.8f; // 방마다 카펫 깔릴 확률

        /// <summary>도트 캐릭터 스킨 하나(aseprite에서 임포트된 idle/walk 프레임).</summary>
        [System.Serializable]
        public class CharacterSkin
        {
            public string label;          // 원본 파일 표시용 (cha_1 등)
            public Sprite[] idleFrames;
            public Sprite[] walkFrames;
            public float idleFps = 8f;
            public float walkFps = 12f;
        }

        [Header("도트 캐릭터 스킨 (비어 있으면 기존 도형 토큰 폴백)")]
        [SerializeField] CharacterSkin[] _charSkins;
        [SerializeField] float _charScale = 1.5f;          // PPU100 기준 약 1유닛 → 확대 배율
        [SerializeField] float _walkSpeedThreshold = 0.15f; // 이 속도 이상이면 walk 애니메이션

        class TokenAnimState
        {
            public SpriteRenderer Sr;
            public CharacterSkin Skin;
            public float Clock;
            public Vector3 LastPos;
            public bool Walking;
        }
        readonly Dictionary<string, TokenAnimState> _tokenAnims = new();

        /// <summary>이번 판 뒷벽에 배치된 창문 하나(인접 열 병합됨). 달빛 라이트 스냅용 앵커.</summary>
        public struct WindowAnchor
        {
            public Vector2 Pos;   // 창문 세그먼트 중앙(월드, 창 아래줄 셀 기준)
            public int Width;     // 병합된 열 수(1=한 칸짜리 창)
        }

        /// <summary>PaintTiles가 매판 갱신. MansionLightingController가 달빛을 여기에 스냅한다.</summary>
        public readonly List<WindowAnchor> WindowAnchors = new();
        readonly List<Vector2Int> _windowCells = new(); // 창문 아래줄 셀 수집(앵커 병합 전 원본)

        readonly Dictionary<string, Rect> _roomRects = new();
        readonly Dictionary<string, Transform> _tokens = new();
        readonly Dictionary<string, Vector3> _tokenVel = new();
        readonly List<Rect> _walkableAreas = new();
        readonly Dictionary<string, Vector3> _npcTargets = new();
        readonly Dictionary<string, SpeechBubble> _bubbles = new();

        Vector3 _humanWorldPos;
        string _humanRoom;

        // ── 밤 시야(암전) + 공간 사냥 (머지 때 유실됐던 필드 복구) ──
        const float MaskWorldHalf = 30f;   // 암전 스프라이트 반경(카메라 뷰를 넉넉히 덮음)
        GameObject _nightMask;
        SpriteRenderer _nightMaskSr;
        float _nightMaskHole = -1f;
        bool _killedThisNight;

        /// <summary>인간 플레이어의 현재 월드 좌표(밤 이동 스냅샷/복구용).</summary>
        public Vector3 HumanWorldPosition => _humanWorldPos;

        /// <summary>다른 플레이어(주로 AI) 토큰의 현재 화면 좌표. 근접 판정(예: 밤 이동 지목)에 쓴다.</summary>
        public bool TryGetTokenPosition(string playerId, out Vector3 pos)
        {
            if (_tokens.TryGetValue(playerId, out var t) && t != null) { pos = t.position; return true; }
            pos = default;
            return false;
        }

        /// <summary>인간 플레이어 위치를 강제로 지정한다(밤 시작 전 위치로 되돌릴 때 사용).</summary>
        public void SetHumanPosition(Vector3 worldPos, string room)
        {
            _humanWorldPos = worldPos;
            _humanRoom = room;
        }

        void Awake()
        {
            _controller = GetComponent<GameController>();
            _pixel = MakePixel();
            _litSpriteMaterial = FindLitSpriteMaterial();
            CleanupGeneratedMap();
            BindSceneMap();
            ConfigureCamera();
            _controller.OnGameSetup += BuildTokens;
            _controller.OnLocationsChanged += RefreshTargets;
            _controller.OnPhaseChanged += delegate { RefreshTargets(); };
            _controller.OnPhaseChanged += HandleNightPhase;
            _controller.OnLog += HandleWorldSpeech;
            EnsureNightMask();
        }

        void OnDestroy()
        {
            if (_controller != null) _controller.OnLog -= HandleWorldSpeech;
        }

        void Update()
        {
            HandleHumanMovement();
            HandleNightKill();
            UpdateTokens();
            UpdateTokenAnimations();
            UpdateSpeechBubbles();
            FollowCamera();
            UpdateNightMaskPosition();
        }

        void CleanupGeneratedMap()
        {
            var old = transform.Find("SpriteMansion");
            if (old != null) Destroy(old.gameObject);
        }

        void BindSceneMap()
        {
            _roomRects.Clear();
            _walkableAreas.Clear();

            int roomCount = _controller.Rooms.Length;
            for (int i = 1; i <= roomCount; i++)
            {
                var go = GameObject.Find("room" + i);
                if (go == null) continue;
                if (!TryGetWorldRect(go, out var rect)) continue;
                var roomName = "방" + i;
                _roomRects[roomName] = Shrink(rect, RoomEdgePadding);
                _walkableAreas.Add(rect);
                EnsureCollider(go, rect);
            }

            for (int i = 1; i <= roomCount - 1; i++)
            {
                var go = GameObject.Find("corridor" + i);
                if (go == null) continue;
                if (!TryGetWorldRect(go, out var rect)) continue;
                _walkableAreas.Add(rect);
                EnsureCollider(go, rect);
            }

            if (_roomRects.Count == 0)
                Debug.LogWarning("room1~room" + roomCount + " SpriteRenderer를 찾지 못했습니다. 씬에 배치한 방 이름을 확인하세요.");
        }

        bool TryGetWorldRect(GameObject go, out Rect rect)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var b = sr.bounds;
                rect = new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
                return b.size.x > 0.01f && b.size.y > 0.01f;
            }

            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                float minX = corners.Min(c => c.x);
                float maxX = corners.Max(c => c.x);
                float minY = corners.Min(c => c.y);
                float maxY = corners.Max(c => c.y);
                rect = new Rect(minX, minY, maxX - minX, maxY - minY);
                return rect.width > 0.01f && rect.height > 0.01f;
            }

            rect = default;
            return false;
        }

        void EnsureCollider(GameObject go, Rect rect)
        {
            var col = go.GetComponent<BoxCollider2D>();
            if (col == null) col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(rect.width / Mathf.Max(0.001f, go.transform.lossyScale.x), rect.height / Mathf.Max(0.001f, go.transform.lossyScale.y));
            col.offset = Vector2.zero;
        }

        void BuildTokens()
        {
            if (_tokenRoot != null) Destroy(_tokenRoot.gameObject);
            _tokenRoot = new GameObject("RuntimeActors").transform;
            _tokenRoot.SetParent(transform, false);

            _tokens.Clear();
            _tokenVel.Clear();
            _npcTargets.Clear();
            _tokenAnims.Clear();

            int roomCount = _controller.Rooms.Length;
            EnsureSlots("room", roomCount);
            EnsureSlots("corridor", Mathf.Max(0, roomCount - 1));
            LayoutMansion();
            BindSceneMap();

            int seat = 0;
            foreach (var p in _controller.State.Players)
            {
                var token = new GameObject("Actor_" + p.Id).transform;
                token.SetParent(_tokenRoot, false);
                token.position = TargetPosition(p.Id);

                if (p.IsHuman)
                {
                    _humanWorldPos = token.position;
                    _humanRoom = _controller.GetPlayerRoom(p.Id);
                }
                else _npcTargets[p.Id] = token.position;

                AddActorSprite(token, p.Id, p.IsHuman, seat++);
                _tokens[p.Id] = token;
            }

            RefreshTargets();
        }

        /// <summary>
        /// 씬에 손으로 배치해둔 room1~5 / corridor1~4 스프라이트를 매판 새로 생성된 저택 구조에 맞춰 재배치한다.
        /// 방 5개짜리 신장 트리는 항상 간선이 4개라 기존 복도 스프라이트 4개와 정확히 맞아떨어진다.
        /// </summary>
        void LayoutMansion()
        {
            var mansion = _controller.Mansion;
            if (mansion.GridPos == null) return;

            int roomCount = _controller.Rooms.Length;
            for (int i = 1; i <= roomCount; i++)
            {
                var go = GameObject.Find("room" + i);
                if (go == null || !mansion.GridPos.TryGetValue("방" + i, out var cell)) continue;
                var p = go.transform.position;
                go.transform.position = new Vector3(cell.X * MansionCellSize, cell.Y * MansionCellSize, p.z);
            }

            for (int i = 0; i < mansion.Corridors.Count; i++)
            {
                var go = GameObject.Find("corridor" + (i + 1));
                if (go == null) continue;
                var (a, b) = mansion.Corridors[i];
                if (!mansion.GridPos.TryGetValue(a, out var ca) || !mansion.GridPos.TryGetValue(b, out var cb)) continue;

                go.transform.position = new Vector3((ca.X + cb.X) * 0.5f * MansionCellSize, (ca.Y + cb.Y) * 0.5f * MansionCellSize, go.transform.position.z);
                bool vertical = ca.X == cb.X;
                go.transform.rotation = Quaternion.Euler(0f, 0f, vertical ? 90f : 0f);
            }

            PaintTiles();
        }

        // ================= 타일 오토타일 (walkable 집합 기반) =================
        // 걷을 수 있는 칸(방 10x10 + 복도 10x4)을 바닥으로 꽉 채우고, 그 '바깥' 경계에만 벽을 세운다.
        // → 이동 공간과 시각이 일치하고, 복도가 방을 뚫지 않으며, 개구부는 자동으로 뚫린다.
        void PaintTiles()
        {
            if (_tilemap == null || _floorTile == null) return;
            var mansion = _controller.Mansion;
            if (mansion.GridPos == null) return;

            const int Cs = (int)MansionCellSize;   // 방 중심 간격(20)
            const int RH = 5;                      // 방 반지름(10x10)
            const int CW = 2;                      // 복도 폭 반(4칸)
            const int Margin = 4;

            _tilemap.ClearAllTiles();
            _windowCells.Clear();
            var tr = _tilemap.GetComponent<TilemapRenderer>();
            if (tr != null) tr.sortingOrder = _tilemapSortingOrder;

            // 1) walkable 칸 집합 W = 방(10x10) + 복도(10x4)
            var W = new HashSet<Vector2Int>();
            var centers = new Dictionary<string, Vector2Int>();
            foreach (var kv in mansion.GridPos)
            {
                var ctr = new Vector2Int(kv.Value.X * Cs, kv.Value.Y * Cs);
                centers[kv.Key] = ctr;
                for (int x = ctr.x - RH; x <= ctr.x + RH - 1; x++)
                    for (int y = ctr.y - RH; y <= ctr.y + RH - 1; y++)
                        W.Add(new Vector2Int(x, y));
            }
            foreach (var pair in mansion.Corridors)
            {
                if (!centers.TryGetValue(pair.A, out var a) || !centers.TryGetValue(pair.B, out var b)) continue;
                AddCorridorCells(W, a, b, RH, CW);
            }
            if (W.Count == 0) return;

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var c in W) { if (c.x < minX) minX = c.x; if (c.x > maxX) maxX = c.x; if (c.y < minY) minY = c.y; if (c.y > maxY) maxY = c.y; }

            // 2) 바닥(=walkable) / 검정 배경(=나머지)
            for (int x = minX - Margin; x <= maxX + Margin; x++)
                for (int y = minY - Margin; y <= maxY + Margin; y++)
                    SetT(x, y, W.Contains(new Vector2Int(x, y)) ? _floorTile : _bgTile, 0);

            // 3) 방/복도별 벽 링을 W 바깥에만 얹는다(개구부=W 칸은 건너뜀 → 자동으로 뚫림)
            foreach (var c in centers.Values) PaintRoomRing(c, W);
            foreach (var pair in mansion.Corridors)
            {
                if (!centers.TryGetValue(pair.A, out var a) || !centers.TryGetValue(pair.B, out var b)) continue;
                PaintCorridorRing(a, b, W);
            }

            PaintCarpets(centers);
            BuildWindowAnchors();
            HideMapSprites();
        }

        // 카펫: 방마다 _carpetChance 확률로 1장, 방 안 랜덤 위치. 데코 타일맵(바닥 위)에 얹는다.
        // 각 조각이 2x2유닛(64px@32PPU)이라 4분면을 2칸 간격으로 놓으면 4x4유닛 러그가 이음새 없이 완성된다.
        void PaintCarpets(Dictionary<string, Vector2Int> centers)
        {
            if (_decorTilemap == null) return;
            _decorTilemap.ClearAllTiles();
            var dr = _decorTilemap.GetComponent<TilemapRenderer>();
            if (dr != null) dr.sortingOrder = _tilemapSortingOrder + 1;
            if (_carpetTL == null || _carpetTR == null || _carpetBL == null || _carpetBR == null) return;

            foreach (var c in centers.Values)
            {
                if (Random.value > _carpetChance) continue;
                // 러그(4x4)가 방(10x10) 안쪽에 여유 1칸을 두고 들어오도록 원점(좌하 셀) 범위 제한
                int ox = Random.Range(c.x - 4, c.x + 2);   // [x0+1, x0+6]
                int oy = Random.Range(c.y - 4, c.y + 2);
                _decorTilemap.SetTile(new Vector3Int(ox,     oy + 2, 0), _carpetTL);
                _decorTilemap.SetTile(new Vector3Int(ox + 2, oy + 2, 0), _carpetTR);
                _decorTilemap.SetTile(new Vector3Int(ox,     oy,     0), _carpetBL);
                _decorTilemap.SetTile(new Vector3Int(ox + 2, oy,     0), _carpetBR);
            }
        }

        // 복도 walkable 칸(방 사이 gap)을 집합에 추가. 가로=10x4, 세로=4x10.
        void AddCorridorCells(HashSet<Vector2Int> W, Vector2Int a, Vector2Int b, int rh, int cw)
        {
            int cx = (a.x + b.x) / 2, cy = (a.y + b.y) / 2;
            if (a.y == b.y) // 가로 복도
            {
                int xlo = Mathf.Min(a.x, b.x) + rh, xhi = Mathf.Max(a.x, b.x) - rh - 1;
                for (int x = xlo; x <= xhi; x++)
                    for (int y = cy - cw; y <= cy + cw - 1; y++)
                        W.Add(new Vector2Int(x, y));
            }
            else            // 세로 복도
            {
                int ylo = Mathf.Min(a.y, b.y) + rh, yhi = Mathf.Max(a.y, b.y) - rh - 1;
                for (int y = ylo; y <= yhi; y++)
                    for (int x = cx - cw; x <= cx + cw - 1; x++)
                        W.Add(new Vector2Int(x, y));
            }
        }

        // 방 벽 링: 바닥(10x10) 바깥을 두른다. 위=뒷벽 면(2줄), 좌/우=선벽(뒷벽 높이까지 연장해 상단코너 검정 방지), 아래=선벽+Point.
        void PaintRoomRing(Vector2Int c, HashSet<Vector2Int> W)
        {
            int x0 = c.x - 5, x1 = c.x + 4, y0 = c.y - 5, y1 = c.y + 4;
            for (int x = x0; x <= x1; x++)
            {
                PaintBackWallColumn(W, x, y1 + 1, y1 + 2);   // 뒷벽 한 열(아래+위 쌍, 확률 변형)
                TryBottomLine(W, x, y0 - 1);                 // 아래 선벽(+개구부 L)
            }
            for (int y = y0 - 1; y <= y1 + 2; y++)            // 좌/우 선벽: 아래 코너~뒷벽 위까지
            {
                TryWall(W, x0 - 1, y, _lineWallTile, 180);   // 좌 선벽(검정=왼쪽)
                TryWall(W, x1 + 1, y, _lineWallTile, 0);     // 우 선벽(검정=오른쪽)
            }
            TryWall(W, x0 - 1, y0 - 1, _cornerTile, 0);      // 좌하 볼록 코너
            TryWall(W, x1 + 1, y0 - 1, _cornerTile, 90);     // 우하 볼록 코너
        }

        // 복도 벽 링. 가로=위 뒷벽 면(2줄)+아래 선벽, 세로=좌/우 선벽.
        void PaintCorridorRing(Vector2Int a, Vector2Int b, HashSet<Vector2Int> W)
        {
            int cx = (a.x + b.x) / 2, cy = (a.y + b.y) / 2;
            if (a.y == b.y) // 가로 복도: W = x[min+5,max-6], y[cy-2,cy+1]
            {
                int xlo = Mathf.Min(a.x, b.x) + 5, xhi = Mathf.Max(a.x, b.x) - 6;
                for (int x = xlo; x <= xhi; x++)
                {
                    PaintBackWallColumn(W, x, cy + 2, cy + 3);
                    TryWall(W, x, cy - 3, _lineWallTile, 270);
                }
            }
            else            // 세로 복도: W = x[cx-2,cx+1], y[min+5,max-6]
            {
                int ylo = Mathf.Min(a.y, b.y) + 5, yhi = Mathf.Max(a.y, b.y) - 6;
                // 측벽은 방 벽(아래방 뒷벽 2줄 / 위방 바닥선 1줄)에는 겹치지 않고 '순수 gap'에만 세운다.
                // → 방 벽이 입구를 직접 감싸므로 측벽 타일의 검정이 뒷벽과 복도 사이를 가르지 않는다.
                for (int y = ylo + 2; y <= yhi - 1; y++)
                {
                    TryWall(W, cx - 3, y, _lineWallTile, 180);
                    TryWall(W, cx + 2, y, _lineWallTile, 0);
                }
            }
        }

        // 뒷벽 한 열(아래줄+위줄 쌍). 기본은 frontwall 쌍, _backWallVariantChance 확률로 framewall 또는 window 쌍(반반).
        // 쌍은 반드시 같은 종류로 맞춰야 하므로 열 단위로 한 번만 굴린다.
        void PaintBackWallColumn(HashSet<Vector2Int> W, int x, int yBot, int yTop)
        {
            TileBase top = _backWallTopTile, bot = _backWallBotTile;
            bool window = false;
            if (_frameWallTopTile != null && _frameWallBotTile != null &&
                _windowTopTile != null && _windowBotTile != null &&
                Random.value < _backWallVariantChance)
            {
                bool frame = Random.value < 0.5f;
                window = !frame;
                top = frame ? _frameWallTopTile : _windowTopTile;
                bot = frame ? _frameWallBotTile : _windowBotTile;
            }
            TryWall(W, x, yBot, bot, 0);
            TryWall(W, x, yTop, top, 0);
            // 실제로 창문이 그려진 열만 달빛 앵커 후보(개구부는 TryWall이 스킵하므로 제외)
            if (window && !W.Contains(new Vector2Int(x, yBot)))
                _windowCells.Add(new Vector2Int(x, yBot));
        }

        // 인접한 창문 열을 하나의 창 세그먼트로 병합해 달빛 앵커를 만든다.
        void BuildWindowAnchors()
        {
            WindowAnchors.Clear();
            if (_windowCells.Count == 0) return;
            var sorted = _windowCells.OrderBy(c => c.y).ThenBy(c => c.x).ToList();
            int runStart = sorted[0].x, runLen = 1, runY = sorted[0].y;
            for (int i = 1; i <= sorted.Count; i++)
            {
                bool cont = i < sorted.Count && sorted[i].y == runY && sorted[i].x == runStart + runLen;
                if (cont) { runLen++; continue; }
                WindowAnchors.Add(new WindowAnchor
                {
                    Pos = new Vector2(runStart + runLen * 0.5f, runY + 0.5f),
                    Width = runLen
                });
                if (i < sorted.Count) { runStart = sorted[i].x; runLen = 1; runY = sorted[i].y; }
            }
        }

        // 바닥이 아니면(=개구부 아니면) 벽을 얹는다. 이미 있던 벽은 덮어씀(뒷벽 면이 접합부에서 우선).
        void TryWall(HashSet<Vector2Int> W, int x, int y, TileBase t, int ang)
        {
            if (W.Contains(new Vector2Int(x, y))) return;
            SetT(x, y, t, ang);
        }

        // 아래 선벽: 옆이 개구부(복도)면 L 오목 코너로 마감.
        void TryBottomLine(HashSet<Vector2Int> W, int x, int y)
        {
            if (W.Contains(new Vector2Int(x, y))) return;
            if (_cornerLTile != null && W.Contains(new Vector2Int(x + 1, y))) SetT(x, y, _cornerLTile, 270); // 오른쪽 개구부
            else if (_cornerLTile != null && W.Contains(new Vector2Int(x - 1, y))) SetT(x, y, _cornerLTile, 0); // 왼쪽 개구부
            else SetT(x, y, _lineWallTile, 270);
        }

        // 타일 배치 + z축 회전(도) 적용. 회전은 검정을 방 바깥으로, 선을 이웃 벽과 잇기 위함.
        void SetT(int x, int y, TileBase t, int ang)
        {
            var p = new Vector3Int(x, y, 0);
            _tilemap.SetTile(p, t != null ? t : _floorTile);
            _tilemap.SetTransformMatrix(p, ang == 0
                ? Matrix4x4.identity
                : Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, ang)));
        }

        // 방/복도 흰 사각형 스프라이트는 숨겨 타일이 보이게 한다(BindSceneMap의 bounds는 유지).
        void HideMapSprites()
        {
            for (int i = 1; i <= MaxMansionSlots; i++)
            {
                FadeSprite(FindAny("room" + i));
                FadeSprite(FindAny("corridor" + i));
            }
        }

        void FadeSprite(GameObject go)
        {
            if (go == null) return;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            var col = sr.color; col.a = 0f; sr.color = col;
        }

        /// <summary>
        /// room1 / corridor1을 원본 삼아 필요한 개수만큼 복제하고, 지금 판에서 안 쓰는 여분은 꺼둔다.
        /// 씬에 손으로 더 배치해둘 필요 없이 GameConfig.RoomCount만 바꾸면 개수가 그대로 반영된다.
        /// </summary>
        void EnsureSlots(string prefix, int needed)
        {
            // GameObject.Find는 비활성 오브젝트를 못 찾으므로(이전 판에서 꺼둔 여분과 중복 생성될 수 있음)
            // 존재 여부 확인·재활성화 모두 비활성 포함 검색(FindAny)으로 처리한다.
            var template = FindAny(prefix + "1");
            if (template != null)
            {
                for (int i = 2; i <= needed; i++)
                {
                    if (FindAny(prefix + i) != null) continue;
                    var clone = Instantiate(template, template.transform.parent);
                    clone.name = prefix + i;
                }
            }

            for (int i = 1; i <= MaxMansionSlots; i++)
            {
                var go = FindAny(prefix + i);
                if (go == null) continue;
                go.SetActive(i <= needed);
            }
        }

        /// <summary>비활성 오브젝트까지 포함해 이름으로 찾는다(GameObject.Find는 활성 오브젝트만 찾음).</summary>
        GameObject FindAny(string name)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t.name == name && t.gameObject.scene.IsValid())
                    return t.gameObject;
            }
            return null;
        }

        void AddActorSprite(Transform token, string id, bool isHuman, int seatIndex)
        {
            // 도트 캐릭터 스킨이 있으면 애니메이션 스프라이트 하나로 토큰 구성 (좌석 순서대로 스킨 순환 배정 — 겹침 허용)
            if (_charSkins != null && _charSkins.Length > 0)
            {
                var skin = _charSkins[seatIndex % _charSkins.Length];
                if (skin != null && skin.idleFrames != null && skin.idleFrames.Length > 0)
                {
                    var go = new GameObject("Body");
                    go.transform.SetParent(token, false);
                    go.transform.localScale = Vector3.one * _charScale;
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = skin.idleFrames[0];
                    if (_litSpriteMaterial != null) sr.sharedMaterial = _litSpriteMaterial;
                    sr.sortingOrder = 20;
                    _tokenAnims[id] = new TokenAnimState
                    {
                        Sr = sr,
                        Skin = skin,
                        LastPos = token.position,
                        Clock = Random.value * 10f // 전원 같은 프레임에서 시작하지 않게 위상차
                    };
                    AddLabel(token, id + (isHuman ? " (YOU)" : ""), new Vector3(0, 0.75f * _charScale + 0.35f, -0.05f));
                    return;
                }
            }

            // 폴백: 기존 도형 토큰
            AddSprite(token, "Body", Vector3.zero, new Vector2(1.05f, 1.18f), isHuman ? Player : Npc, 20);
            AddSprite(token, "Backpack", new Vector3(-0.55f, -0.04f, -0.01f), new Vector2(0.32f, 0.68f), Hex("181820"), 21);
            AddSprite(token, "Visor", new Vector3(0.18f, 0.26f, -0.02f), new Vector2(0.62f, 0.36f), Hex("BFD7E8"), 22);
            AddSprite(token, "LegL", new Vector3(-0.25f, -0.72f, -0.01f), new Vector2(0.28f, 0.36f), isHuman ? Hex("5F34C9") : Hex("9D7D43"), 21);
            AddSprite(token, "LegR", new Vector3(0.30f, -0.72f, -0.01f), new Vector2(0.28f, 0.36f), isHuman ? Hex("5F34C9") : Hex("9D7D43"), 21);
            AddLabel(token, id + (isHuman ? " (YOU)" : ""), new Vector3(0, 1.05f, -0.05f));
        }

        // 토큰 이동 여부에 따라 idle/walk 프레임을 돌리고, 이동 방향으로 좌우 반전한다.
        void UpdateTokenAnimations()
        {
            foreach (var kv in _tokenAnims)
            {
                var a = kv.Value;
                if (a == null || a.Sr == null) continue;
                var tokenTr = a.Sr.transform.parent;
                if (tokenTr == null || !tokenTr.gameObject.activeSelf) continue;

                var pos = tokenTr.position;
                var delta = pos - a.LastPos;
                a.LastPos = pos;

                bool walking = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f) > _walkSpeedThreshold;
                if (walking != a.Walking) { a.Walking = walking; a.Clock = 0f; }
                if (Mathf.Abs(delta.x) > 0.0005f) a.Sr.flipX = delta.x > 0f;

                var frames = walking && a.Skin.walkFrames != null && a.Skin.walkFrames.Length > 0
                    ? a.Skin.walkFrames : a.Skin.idleFrames;
                float fps = walking ? a.Skin.walkFps : a.Skin.idleFps;
                if (frames == null || frames.Length == 0) continue;

                a.Clock += Time.deltaTime;
                int idx = (int)(a.Clock * Mathf.Max(1f, fps)) % frames.Length;
                a.Sr.sprite = frames[idx];
            }
        }

        SpriteRenderer AddSprite(Transform parent, string name, Vector3 pos, Vector2 scale, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _pixel;
            if (_litSpriteMaterial != null) sr.sharedMaterial = _litSpriteMaterial;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        Material FindLitSpriteMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            return shader == null ? null : new Material(shader) { name = "Runtime Sprite Lit Material" };
        }

        void AddLabel(Transform parent, string label, Vector3 pos)
        {
            var go = new GameObject("Label_" + label);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = label;
            tm.fontSize = 42;
            tm.characterSize = 0.10f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Text;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sortingOrder = 30;
        }

        void HandleWorldSpeech(LogEntry e)
        {
            if (e.Kind != LogKind.Speech) return;
            if (!_tokens.TryGetValue(e.Speaker, out var token) || token == null) return;

            string line = ExtractSpeechBody(e.Text);
            if (string.IsNullOrWhiteSpace(line)) return;
            ShowSpeechBubble(token, e.Speaker, line);
        }

        string ExtractSpeechBody(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            int colon = raw.IndexOf(':');
            string body = colon >= 0 && colon + 1 < raw.Length ? raw.Substring(colon + 1).Trim() : raw.Trim();
            if (body.Length > 46) body = body.Substring(0, 46).Trim() + "...";
            return body;
        }

        void ShowSpeechBubble(Transform token, string speakerId, string text)
        {
            var bubble = GetSpeechBubble(token, speakerId);
            bubble.Text.text = text;
            bubble.Root.SetActive(true);
            bubble.HideAt = Time.realtimeSinceStartup + 4.5f;

            int len = Mathf.Clamp(text.Length, 8, 46);
            float width = Mathf.Clamp(1.9f + len * 0.085f, 2.4f, 5.8f);
            bubble.Backdrop.transform.localScale = new Vector3(width, 0.72f, 1f);
        }

        SpeechBubble GetSpeechBubble(Transform token, string speakerId)
        {
            if (_bubbles.TryGetValue(speakerId, out var bubble) && bubble.Root != null) return bubble;

            var root = new GameObject("SpeechBubble").transform;
            root.SetParent(token, false);
            root.localPosition = new Vector3(0f, 2.05f, -0.2f);

            var bg = AddSprite(root, "BubbleBg", Vector3.zero, new Vector2(3.4f, 0.72f), new Color(0f, 0f, 0f, 0.78f), 80);
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(root, false);
            textGo.transform.localPosition = new Vector3(0f, -0.02f, -0.04f);
            var tm = textGo.AddComponent<TextMesh>();
            tm.text = "";
            tm.fontSize = 40;
            tm.characterSize = 0.062f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            textGo.GetComponent<MeshRenderer>().sortingOrder = 81;

            bubble = new SpeechBubble { Root = root.gameObject, Backdrop = bg, Text = tm, HideAt = 0f };
            bubble.Root.SetActive(false);
            _bubbles[speakerId] = bubble;
            return bubble;
        }

        void UpdateSpeechBubbles()
        {
            float now = Time.realtimeSinceStartup;
            foreach (var kv in _bubbles)
            {
                var bubble = kv.Value;
                if (bubble.Root == null || !bubble.Root.activeSelf) continue;
                if (now >= bubble.HideAt) bubble.Root.SetActive(false);
            }
        }

        class SpeechBubble
        {
            public GameObject Root;
            public SpriteRenderer Backdrop;
            public TextMesh Text;
            public float HideAt;
        }

        void RefreshTargets()
        {
            if (_controller == null || _controller.State == null) return;
            BindSceneMap();
            foreach (var p in _controller.State.Players)
            {
                if (!_tokens.TryGetValue(p.Id, out var t)) continue;
                t.gameObject.SetActive(p.Alive);
                if (!p.IsHuman) _npcTargets[p.Id] = TargetPosition(p.Id);
            }
        }

        void UpdateTokens()
        {
            foreach (var kv in _tokens)
            {
                var id = kv.Key;
                var t = kv.Value;
                if (t == null) continue;

                if (_controller.HumanPlayer != null && id == _controller.HumanPlayer.Id)
                {
                    t.position = _humanWorldPos;
                    continue;
                }

                var target = _npcTargets.TryGetValue(id, out var nt) ? nt : TargetPosition(id);
                t.position = MoveInsideWalkable(t.position, target, NpcSpeed * Time.deltaTime);
            }
        }

        void HandleHumanMovement()
        {
            if (_controller == null || _controller.State == null || _controller.HumanPlayer == null) return;
            if (!_controller.HumanPlayer.Alive) return;
            if (IsTyping()) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            Vector2 input = Vector2.zero;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
            if (input.sqrMagnitude <= 0.01f) return;

            input.Normalize();
            var next = _humanWorldPos + new Vector3(input.x, input.y, 0f) * HumanSpeed * Time.deltaTime;
            if (!IsWalkable(next)) return;

            _humanWorldPos = next;
            string room = RoomAt(next);
            if (!string.IsNullOrEmpty(room) && room != _humanRoom)
            {
                _humanRoom = room;
                _controller.MoveHumanToRoom(room);
            }
        }

        Vector3 MoveInsideWalkable(Vector3 current, Vector3 target, float maxDelta)
        {
            var desired = Vector3.MoveTowards(current, target, maxDelta);
            if (IsWalkable(desired)) return desired;

            var xOnly = new Vector3(desired.x, current.y, current.z);
            if (IsWalkable(xOnly)) return xOnly;

            var yOnly = new Vector3(current.x, desired.y, current.z);
            if (IsWalkable(yOnly)) return yOnly;

            return current;
        }

        bool IsTyping()
        {
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if (selected == null) return false;
            return selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null;
        }

        bool IsWalkable(Vector3 pos)
        {
            if (_walkableAreas.Count == 0) return true;
            var p = new Vector2(pos.x, pos.y);
            foreach (var area in _walkableAreas)
                if (area.Contains(p)) return true;
            return false;
        }

        string RoomAt(Vector3 pos)
        {
            var p = new Vector2(pos.x, pos.y);
            foreach (var kv in _roomRects)
                if (kv.Value.Contains(p)) return kv.Key;
            return null;
        }

        Rect Shrink(Rect r, float amount)
        {
            return new Rect(r.xMin + amount, r.yMin + amount, Mathf.Max(0.01f, r.width - amount * 2f), Mathf.Max(0.01f, r.height - amount * 2f));
        }

        Vector3 TargetPosition(string id)
        {
            if (_controller == null || _controller.State == null || _roomRects.Count == 0) return Vector3.zero;

            string room = _controller.GetPlayerRoom(id);
            if (string.IsNullOrEmpty(room) || !_roomRects.ContainsKey(room)) room = _roomRects.Keys.First();

            var r = _roomRects[room];
            var people = _controller.GetAliveInRoom(room);
            int idx = Mathf.Max(0, people.IndexOf(id));
            int cols = 3;
            float ox = ((idx % cols) - 1) * Mathf.Min(1.0f, r.width * 0.18f);
            float oy = (idx / cols) * -Mathf.Min(0.8f, r.height * 0.18f);
            var pos = new Vector3(r.center.x + ox, r.center.y + oy, -1.0f);
            return IsWalkable(pos) ? pos : new Vector3(r.center.x, r.center.y, -1.0f);
        }

        void ConfigureCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                cam = go.AddComponent<Camera>();
                go.tag = "MainCamera";
            }

            cam.orthographic = true;
            cam.transform.rotation = Quaternion.identity;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("3F3F3F");

            if (_walkableAreas.Count > 0 && cam.transform.position == Vector3.zero)
            {
                var first = _walkableAreas[0];
                cam.transform.position = new Vector3(first.center.x, first.center.y, -10f);
            }
        }

        void FollowCamera()
        {
            if (_controller == null || _controller.HumanPlayer == null) return;
            if (!_tokens.TryGetValue(_controller.HumanPlayer.Id, out var human) || human == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            var target = new Vector3(human.position.x, human.position.y, cam.transform.position.z);
            cam.transform.position = Vector3.Lerp(cam.transform.position, target, 12f * Time.deltaTime);
        }

        // ========================= 밤 시야(암전) =========================

        void EnsureNightMask()
        {
            if (_nightMask != null) return;
            _nightMask = new GameObject("NightVision");
            _nightMask.transform.SetParent(transform, false);
            _nightMaskSr = _nightMask.AddComponent<SpriteRenderer>();
            _nightMaskSr.sortingOrder = 500;   // 토큰 위, UI(스크린 캔버스) 아래
            _nightMask.SetActive(false);
        }

        void HandleNightPhase(GameState s)
        {
            EnsureNightMask();
            bool night = s != null && s.Phase == Phase.Night
                         && _controller.HumanPlayer != null && _controller.HumanPlayer.Alive;
            if (!night) { _nightMask.SetActive(false); return; }

            _killedThisNight = false;
            bool mafia = _controller.HumanPlayer.Role == Role.Mafia;
            float vision = mafia ? _controller.config.MafiaNightVision : _controller.config.CitizenNightVision;
            SetNightMaskHole(vision);
            _nightMask.SetActive(true);
        }

        void UpdateNightMaskPosition()
        {
            if (_nightMask == null || !_nightMask.activeSelf) return;
            _nightMask.transform.position = new Vector3(_humanWorldPos.x, _humanWorldPos.y, -2f);
        }

        /// <summary>중심은 투명(시야), 반경 밖은 검정으로 채운 방사형 암전 스프라이트를 생성.</summary>
        void SetNightMaskHole(float holeWorld)
        {
            if (Mathf.Approximately(_nightMaskHole, holeWorld) && _nightMaskSr.sprite != null) return;
            _nightMaskHole = holeWorld;

            const int T = 256;
            float ppu = T / (2f * MaskWorldHalf);       // world → pixel
            float holePx = holeWorld * ppu;
            float softPx = Mathf.Max(6f, holePx * 0.5f); // 시야 가장자리 부드럽게
            float cx = (T - 1) * 0.5f, cy = (T - 1) * 0.5f;

            var tex = new Texture2D(T, T, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var cols = new Color[T * T];
            for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float a = Mathf.Clamp01((d - holePx) / softPx); // 안쪽 0(투명) → 바깥 1(검정)
                    cols[y * T + x] = new Color(0f, 0f, 0f, a);
                }
            tex.SetPixels(cols);
            tex.Apply();
            _nightMaskSr.sprite = Sprite.Create(tex, new Rect(0, 0, T, T), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
        }

        // ========================= 공간 사냥(인간 마피아) =========================

        void HandleNightKill()
        {
            if (_controller == null || _controller.State == null) return;
            if (_controller.State.Phase != Phase.Night || _killedThisNight) return;
            var hp = _controller.HumanPlayer;
            if (hp == null || !hp.Alive || hp.Role != Role.Mafia) return;
            if (IsTyping()) return;
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.spaceKey.wasPressedThisFrame) return;

            string best = null;
            float bestD = float.MaxValue;
            foreach (var id in _controller.NightKillCandidates())
            {
                if (!_tokens.TryGetValue(id, out var t) || t == null || !t.gameObject.activeSelf) continue;
                float d = Vector2.Distance(new Vector2(t.position.x, t.position.y),
                                           new Vector2(_humanWorldPos.x, _humanWorldPos.y));
                if (d < bestD) { bestD = d; best = id; }
            }

            if (best == null || bestD > _controller.config.KillRadius) return;   // 사거리 밖 → 헛손질
            if (_controller.TrySubmitNightKill(best))
            {
                _killedThisNight = true;
                StartCoroutine(KillFeedback(best));
            }
        }

        /// <summary>살해 순간의 시각 피드백. 실제 사망 표시·정산은 새벽에만 일어난다(은밀).</summary>
        IEnumerator KillFeedback(string id)
        {
            if (!_tokens.TryGetValue(id, out var t) || t == null) yield break;
            var body = t.Find("Body");
            var bsr = body != null ? body.GetComponent<SpriteRenderer>() : null;
            Color orig = bsr != null ? bsr.color : Color.white;
            if (bsr != null) bsr.color = Hex("8A3A3A");

            var mark = new GameObject("KillMark");
            mark.transform.SetParent(t, false);
            mark.transform.localPosition = new Vector3(0f, 1.4f, -0.1f);
            var tm = mark.AddComponent<TextMesh>();
            tm.text = "제거"; tm.fontSize = 42; tm.characterSize = 0.10f;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.color = Hex("E0503A");
            mark.GetComponent<MeshRenderer>().sortingOrder = 520;

            yield return new WaitForSeconds(1.2f);
            if (mark != null) Destroy(mark);
            if (bsr != null) bsr.color = orig;   // 표식 제거 — 사망은 새벽 정산 때 드러난다
        }

        Sprite MakePixel()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
