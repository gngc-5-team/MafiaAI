using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// Binds gameplay movement and room detection to the sprites already placed in the scene.
    /// Expected scene objects: room1..room5, corridor1..corridor4, with roomline/corridorline as visual walls.
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public class SpriteMansionView : MonoBehaviour
    {
        const float HumanSpeed = 3.8f;
        const float NpcSpeed = 2.4f;
        const float RoomEdgePadding = 0.2f;

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
        }

        void Update()
        {
            HandleHumanMovement();
            UpdateTokens();
            FollowCamera();
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

            for (int i = 1; i <= 5; i++)
            {
                var go = GameObject.Find("room" + i);
                if (go == null) continue;
                if (!TryGetWorldRect(go, out var rect)) continue;
                var roomName = "방" + i;
                _roomRects[roomName] = Shrink(rect, RoomEdgePadding);
                _walkableAreas.Add(rect);
                EnsureCollider(go, rect);
            }

            for (int i = 1; i <= 4; i++)
            {
                var go = GameObject.Find("corridor" + i);
                if (go == null) continue;
                if (!TryGetWorldRect(go, out var rect)) continue;
                _walkableAreas.Add(rect);
                EnsureCollider(go, rect);
            }

            if (_roomRects.Count == 0)
                Debug.LogWarning("room1~room5 SpriteRenderer를 찾지 못했습니다. 씬에 배치한 방 이름을 확인하세요.");
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
            return selected != null && selected.GetComponent<InputField>() != null;
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
