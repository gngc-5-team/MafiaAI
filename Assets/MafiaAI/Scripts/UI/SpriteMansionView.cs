using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
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

        readonly Dictionary<string, Rect> _roomRects = new();
        readonly Dictionary<string, Transform> _tokens = new();
        readonly Dictionary<string, Vector3> _tokenVel = new();
        readonly List<Rect> _walkableAreas = new();
        readonly Dictionary<string, Vector3> _npcTargets = new();

        Vector3 _humanWorldPos;
        string _humanRoom;

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
            CleanupGeneratedMap();
            BindSceneMap();
            ConfigureCamera();
            _controller.OnGameSetup += BuildTokens;
            _controller.OnLocationsChanged += RefreshTargets;
            _controller.OnPhaseChanged += delegate { RefreshTargets(); };
            _controller.OnPhaseChanged += HandleNightPhase;
            EnsureNightMask();
        }

        void Update()
        {
            HandleHumanMovement();
            HandleNightKill();
            UpdateTokens();
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

            int roomCount = _controller.Rooms.Length;
            EnsureSlots("room", roomCount);
            EnsureSlots("corridor", Mathf.Max(0, roomCount - 1));
            LayoutMansion();
            BindSceneMap();

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

                AddActorSprite(token, p.Id, p.IsHuman);
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

        void AddActorSprite(Transform token, string id, bool isHuman)
        {
            AddSprite(token, "Body", Vector3.zero, new Vector2(1.05f, 1.18f), isHuman ? Player : Npc, 20);
            AddSprite(token, "Backpack", new Vector3(-0.55f, -0.04f, -0.01f), new Vector2(0.32f, 0.68f), Hex("181820"), 21);
            AddSprite(token, "Visor", new Vector3(0.18f, 0.26f, -0.02f), new Vector2(0.62f, 0.36f), Hex("BFD7E8"), 22);
            AddSprite(token, "LegL", new Vector3(-0.25f, -0.72f, -0.01f), new Vector2(0.28f, 0.36f), isHuman ? Hex("5F34C9") : Hex("9D7D43"), 21);
            AddSprite(token, "LegR", new Vector3(0.30f, -0.72f, -0.01f), new Vector2(0.28f, 0.36f), isHuman ? Hex("5F34C9") : Hex("9D7D43"), 21);
            AddLabel(token, id + (isHuman ? " (YOU)" : ""), new Vector3(0, 1.05f, -0.05f));
        }

        SpriteRenderer AddSprite(Transform parent, string name, Vector3 pos, Vector2 scale, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _pixel;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
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
