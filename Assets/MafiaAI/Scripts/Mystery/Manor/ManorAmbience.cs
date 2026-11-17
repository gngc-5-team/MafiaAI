using System;
using System.Collections;
using UnityEngine;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>
    /// 소리 풍경. 배경 다섯 겹(바깥 폭우 · 벽 너머 비 · 유리 지붕 비 · 바람 · 바다)을 방마다 다르게 섞고,
    /// 화면에 바깥이 많이 보일수록(양 끝 방·저택도) 바깥 소리를 키운다. 대화 땐 한 발 물러난다.
    /// 발소리는 방 바닥 재질(나무·대리석·돌·젖은 타일)에 따라, 계단은 계단 재질(나무·나선 철계단·돌계단)에 따라 고른다.
    /// 손님 발소리는 그 사람 자리에서 난다(3D — 귀는 카메라가 아니라 저택 평면 위에 있다).
    /// 소리는 모두 합성해 넣은 파일이다(tools 없이 scratchpad의 make_sfx.py).
    /// </summary>
    public class ManorAmbience : MonoBehaviour
    {
        [Serializable]
        public class StepSet
        {
            public string id;
            public AudioClip[] clips;
            [Range(0f, 1.5f)] public float volume = 1f;
        }

        [Serializable]
        public class RoomMix
        {
            public string room;
            public string floor = "wood";
            [Range(0f, 1f)] public float rainOut, rainIn, rainGlass, wind, sea;
        }

        public enum Mode { Explore, Talk, Map, Cutscene }

        [Header("배경(반복)")]
        public AudioSource rainOut;
        public AudioSource rainIn;
        public AudioSource rainGlass;
        public AudioSource wind;
        public AudioSource sea;
        public RoomMix[] rooms;
        public RoomMix overview = new RoomMix { room = "map", rainOut = 0.62f, rainIn = 0.12f, rainGlass = 0.08f, wind = 0.42f, sea = 0.4f };
        [Tooltip("화면 중 바깥이 차지하는 비율만큼 더하는 양")]
        public float outsideBoost = 0.42f;
        public float fadeSpeed = 0.5f;

        [Header("발소리")]
        public AudioSource steps;               // 탐정(2D)
        public StepSet[] floors;
        [Range(0f, 1f)] public float stepVolume = 0.62f;
        [Range(0f, 1f)] public float npcStepVolume = 0.42f;

        [Header("한 번")]
        public AudioSource sfx;
        public AudioClip door;
        public AudioClip chime;
        public AudioClip lensOn;
        public AudioClip lensOff;
        public AudioClip photo;

        [Header("참조")]
        public Camera viewCamera;
        public ManorWorld world;

        RoomMix _target;
        Mode _mode = Mode.Explore;
        string _room;

        void Start()
        {
            foreach (var s in new[] { rainOut, rainIn, rainGlass, wind, sea })
            {
                if (s == null || s.clip == null) continue;
                s.loop = true;
                s.volume = 0f;
                s.time = UnityEngine.Random.Range(0f, s.clip.length * 0.9f);   // 겹들이 같은 박자로 맞물리지 않게
                s.Play();
            }
            if (_target == null && rooms != null && rooms.Length > 0) _target = rooms[0];
        }

        void Update()
        {
            var mix = _mode == Mode.Map ? overview : _target;
            if (mix == null) return;
            float duck = _mode == Mode.Talk ? 0.55f : _mode == Mode.Cutscene ? 0.75f : 1f;
            float outside = _mode == Mode.Map ? 0f : OutsideFraction() * outsideBoost;
            float k = 1f - Mathf.Exp(-fadeSpeed * 4f * Time.unscaledDeltaTime);
            Blend(rainOut, (mix.rainOut + outside) * duck, k);
            Blend(rainIn, mix.rainIn * duck * (1f - outside * 0.6f), k);
            Blend(rainGlass, mix.rainGlass * duck, k);
            Blend(wind, (mix.wind + outside * 0.6f) * duck, k);
            Blend(sea, (mix.sea + outside * 0.7f) * duck, k);
        }

        static void Blend(AudioSource s, float target, float k)
        {
            if (s == null) return;
            s.volume = Mathf.Lerp(s.volume, Mathf.Clamp01(target), k);
        }

        /// <summary>지금 화면에서 집 밖(하늘·바다·절벽)이 차지하는 가로 비율.</summary>
        float OutsideFraction()
        {
            if (viewCamera == null || world == null || world.houseBounds.width <= 0f) return 0f;
            float half = viewCamera.orthographicSize * viewCamera.aspect;
            float x0 = viewCamera.transform.position.x - half, x1 = viewCamera.transform.position.x + half;
            float inside = Mathf.Max(0f, Mathf.Min(x1, world.houseBounds.xMax) - Mathf.Max(x0, world.houseBounds.xMin));
            return Mathf.Clamp01(1f - inside / Mathf.Max(0.01f, x1 - x0));
        }

        /// <summary>방에 따라 배경 소리 배합을 바꾼다.</summary>
        public void SetRoom(string roomId)
        {
            _room = roomId;
            if (rooms == null) return;
            foreach (var r in rooms) if (r.room == roomId) { _target = r; return; }
        }

        public void SetMode(Mode mode) => _mode = mode;

        /// <summary>예전 호환: 대화 등에서 배경을 낮춘다.</summary>
        public void Muffle(bool on) => _mode = on ? Mode.Talk : Mode.Explore;

        // ───────────── 발소리 ─────────────

        StepSet Floor(string id)
        {
            if (floors == null) return null;
            foreach (var f in floors) if (f.id == id) return f;
            return null;
        }

        string FloorOf(string roomId)
        {
            if (rooms != null) foreach (var r in rooms) if (r.room == roomId) return r.floor;
            return "wood";
        }

        static AudioClip Pick(StepSet set) => set == null || set.clips == null || set.clips.Length == 0 ? null : set.clips[UnityEngine.Random.Range(0, set.clips.Length)];

        /// <summary>탐정 한 걸음(지금 방 바닥 — floor를 주면 그 재질).</summary>
        public void Step(string floor = null)
        {
            if (steps == null) return;
            var set = Floor(floor ?? FloorOf(_room));
            var clip = Pick(set);
            if (clip == null) return;
            steps.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            steps.PlayOneShot(clip, stepVolume * set.volume * UnityEngine.Random.Range(0.85f, 1f));
        }

        /// <summary>계단 재질: 온실↔서재 나선 철계단, 홀↔응접실·부엌↔식당 나무 계단, 지하로는 돌계단.</summary>
        public static string StairFloor(string a, string b)
        {
            bool Pair(string x, string y) => (a == x && b == y) || (a == y && b == x);
            if (Pair("greenhouse", "study")) return "stair_iron";
            if (Pair("hall", "laundry") || Pair("kitchen", "storage")) return "stone";
            return "stair_wood";
        }

        /// <summary>손님 한 걸음 — 그 사람 자리의 소리(3D).</summary>
        public void NpcStep(PixelActor actor, AudioSource source)
        {
            if (actor == null || source == null || world == null) return;
            var room = world.RoomAt(actor.transform.position);
            var set = Floor(room != null ? FloorOf(room.id) : "wood");
            var clip = Pick(set);
            if (clip == null) return;
            source.pitch = UnityEngine.Random.Range(1.0f, 1.12f);    // 손님들은 조금 가볍게
            source.PlayOneShot(clip, npcStepVolume * set.volume * UnityEngine.Random.Range(0.8f, 1f));
        }

        // ───────────── 한 번 ─────────────

        void One(AudioClip clip, float volume, float pitch = 1f)
        {
            if (sfx == null || clip == null) return;
            sfx.pitch = pitch;
            sfx.PlayOneShot(clip, volume);
        }

        public void Door() => One(door, 0.55f, UnityEngine.Random.Range(0.94f, 1.05f));
        public void LensOn() => One(lensOn, 0.5f);
        public void LensOff() => One(lensOff, 0.45f);
        public void Photo() => One(photo, 0.7f);

        public IEnumerator Chime(int strikes)
        {
            if (sfx == null || chime == null) yield break;
            for (int i = 0; i < strikes; i++)
            {
                sfx.pitch = 1f;
                sfx.PlayOneShot(chime, 0.5f);
                yield return new WaitForSecondsRealtime(1.1f);
            }
        }
    }
}
