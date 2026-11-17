using System.Collections.Generic;

namespace MafiaAI.Mystery
{
    /// <summary>
    /// 사건 1 「자정의 원고」 — 폭풍으로 고립된 안개곶 저택, 소설가 노아 피살.
    ///
    /// 진실(플레이어에게는 마지막에만 보인다):
    ///   제로(의사)는 원고 6장에 적힌 3년 전 수술 사고(노아의 여동생 노을)를 감추려고 노아를 죽였다.
    ///   22:40 응접실 자동 피아노에 녹턴 롤을 걸어 '연주하는 척' 알리바이를 만들고, 연결문으로 서재에 들어가
    ///   22:45 응접실 부지깽이로 내리쳤다. 회중시계를 23:10에 맞춰 용두를 당겨 멈추고, 청동 문진에 피를 묻혔다.
    ///   세탁실에서 의료용 거즈로 부지깽이를 씻고(22:55 세이가 봄), 6장은 피아노 의자에 숨겼다.
    ///   23:05부터 식당에서 하루·세이와 함께 있으며 23:10의 알리바이를 만들었다.
    ///   노아는 원고를 출판할 생각이 없었다 — 자정에 모두 앞에서 태우고 서로를 놓아주려 했다(마지막 줄 '이 원고를 ㅌ').
    ///
    /// 설계 원칙: 모든 결정적 사실에 길이 둘 이상 있다(시계 용두 / 미로의 23시 증언, 롤 / 사진 속 빈 의자, …).
    /// 늦어도 막히지 않는다: 6장이 타면 재 속 조각이, 세이가 죽으면 그녀의 수첩과 사진이 남는다.
    /// </summary>
    public static class Case01
    {
        static int T(int h, int m) => MysteryCase.At(h, m);

        public static MysteryCase Build() => new MysteryCase
        {
            Title = "자정의 원고",
            Subtitle = "안개곶 저택 · 소설가 노아 피살",
            Victim = "노아",
            Killer = "제로",
            Start = T(23, 45),
            Dawn = T(6, 0),
            StartRoom = "hall",
            Prologue = new[]
            {
                new Panel("story_storm", "폭풍이 몰아치던 밤.\n탐정은 편지 한 통을 받고 바다 끝 안개곶 저택으로 향했다."),
                new Panel("story_letter", "『탐정님, 이번 주말 안개곶의 만찬에 와 주십시오.\n자정에 한 편의 이야기가 끝납니다. 증인이 필요합니다. — 노아』"),
                new Panel("story_dinner", "그날 저녁, 소설가 노아는 다섯 손님 앞에서 잔을 들었다.\n\"내 새 소설 『가면들의 만찬』은 — 이 식탁에 앉은 여러분의 이야기입니다.\""),
                new Panel("story_discovery", "23시 30분, 비명이 저택을 갈랐다.\n쓰러진 나무에 길이 막혀 걸어온 탐정이 현관문을 두드린 건, 그로부터 10분 뒤였다."),
                new Panel("story_storm", "노아는 서재 책상에 엎드린 채 숨져 있었다.\n바닷길은 물에 잠겼고, 전화는 먹통이었다. 날이 밝기 전까지 이 저택을 나갈 수 있는 사람은 없다.\n\n범인은 이 안에 있다."),
            },

            Rooms = new[]
            {
                new Room
                {
                    Id = "study", Name = "서재", Image = "room_study", X = 0, Y = 0,
                    Intro = "사건 현장. 노아는 책상에 엎드린 채 숨져 있다. 창밖에 번개가 친다.",
                    Hotspots = new[]
                    {
                        H("study_body", "노아", 0.455f, 0.33f, 0.215f, 0.135f, "body",
                            "노아는 타자기 옆에 엎드린 채 움직이지 않는다. 뒤통수에 길고 가느다란 상처 하나. 피는 많지 않다."),
                        H("study_watch", "회중시계", 0.505f, 0.47f, 0.085f, 0.09f, "watch",
                            "시신의 손 옆에 회중시계가 떨어져 있다. 유리가 깨졌고, 바늘은 23시 10분에 멈췄다."),
                        H("study_typewriter", "타자기", 0.305f, 0.36f, 0.145f, 0.14f, "typewriter",
                            "타자기에 종이가 끼워져 있다. 「제7장 자정의 불 — 22:40」. 본문은 두 줄에서 끊겼다."),
                        H("study_manuscript", "원고 뭉치", 0.645f, 0.44f, 0.155f, 0.09f, "manuscript,ch1,ch2,ch4,ch5",
                            "노끈으로 묶인 원고 『가면들의 만찬』. 목차엔 일곱 장이 있는데, 3장과 6장이 빠져 있다."),
                        H("study_teacup", "찻잔", 0.78f, 0.485f, 0.075f, 0.07f, "teacup",
                            "찻잔이 가득 찬 채 식었다. 한 모금도 마시지 않았다."),
                        H("study_paperweight", "청동 문진", 0.62f, 0.825f, 0.09f, 0.095f, "paperweight",
                            "책상 앞 바닥에 둥근 청동 문진이 굴러 있다. 위쪽에 피가 묻어 있다."),
                        H("study_card", "카드", 0.495f, 0.795f, 0.05f, 0.06f, "card",
                            "책상 밑 그늘에 트럼프 카드 한 장. 스페이드 에이스다."),
                        H("study_chair", "쓰러진 의자", 0.0f, 0.62f, 0.29f, 0.38f, "lighter",
                            "의자가 옆으로 쓰러져 있다. 그 밑에 은 라이터가 떨어져 있다."),
                        H("study_fireplace", "벽난로", 0.86f, 0.24f, 0.14f, 0.32f, null,
                            "불을 피운 흔적이 없다. 재도 차갑다. 오늘 밤 이 방에서 태운 것은 없다."),
                        H("study_window", "창문", 0.6f, 0.0f, 0.2f, 0.33f, null,
                            "안에서 잠겨 있다. 창틀도 말라 있다. 아무도 창으로 드나들지 않았다."),
                        H("study_door", "응접실 연결문", 0.02f, 0.02f, 0.16f, 0.52f, null,
                            "응접실로 통하는 연결문. 잠겨 있지 않다. 홀 계단을 거치지 않고도 응접실에서 곧장 이 방에 들어올 수 있다.", new[] { "knows_door" }),
                        H("study_shelf", "책장", 0.24f, 0.0f, 0.23f, 0.25f, null,
                            "노아의 책들. 『푸른 등대』 초판만 유난히 손때가 묻었다. 속표지에 연필로 작게 — 'H에게. 언젠가 돌려줄게.'"),
                        H("study_mantel", "탁상시계", 0.94f, 0.13f, 0.055f, 0.09f, null,
                            "벽난로 위 탁상시계는 멀쩡히 간다. 지금 시각과 정확히 맞는다."),
                    },
                },
                new Room
                {
                    Id = "drawing", Name = "응접실", Image = "room_drawing", X = 1, Y = 0,
                    Intro = "벽난로 불이 낮게 타고 있다. 서재와는 연결문 하나 사이다.",
                    Hotspots = new[]
                    {
                        H("drawing_piano", "피아노", 0.155f, 0.3f, 0.27f, 0.27f, "pianola",
                            "자동 연주 장치가 달린 업라이트 피아노. 건반 위 덮개 안에 롤을 끼우는 축이 있다. 지금은 비어 있다."),
                        HIf("drawing_bench", "피아노 의자", 0.285f, 0.575f, 0.18f, 0.17f, "ch6", "!ch6_burned",
                            "의자 뚜껑이 살짝 들려 있다. 악보 밑에 접힌 원고가 숨겨져 있다 — 「제6장 흰 가운의 침묵」."),
                        HIf("drawing_bench_empty", "피아노 의자", 0.285f, 0.575f, 0.18f, 0.17f, null, "ch6_burned,!has:ch6",
                            "의자 뚜껑이 살짝 들려 있다. 안에는 악보뿐이다. 종이 뭉치가 접혀 있던 자국만 남았다."),
                        H("drawing_rolls", "롤 보관함", 0.085f, 0.35f, 0.115f, 0.4f, "roll",
                            "자동 피아노 롤 열두 개. 「녹턴 Op.9-2」만 끝이 풀린 채 꽂혀 있다."),
                        H("drawing_tools", "벽난로 도구", 0.8f, 0.5f, 0.09f, 0.32f, "poker",
                            "부젓가락과 부삽은 그을음투성이다. 그런데 부지깽이만 반짝일 만큼 깨끗하고, 아직 축축하다."),
                        HIf("drawing_fire", "벽난로", 0.9f, 0.38f, 0.1f, 0.42f, null, "!ch6_burned",
                            "불이 낮게 타고 있다. 장작 몇 개와 재뿐이다."),
                        HIf("drawing_fire_burned", "벽난로", 0.9f, 0.38f, 0.1f, 0.42f, "fragment", "ch6_burned",
                            "재 속에 타다 만 종이 조각이 있다. 타자기 글씨 — 원고다. 누군가 방금 태웠다."),
                        H("drawing_door", "서재 연결문", 0.0f, 0.05f, 0.08f, 0.7f, null,
                            "서재로 통하는 문. 피아노에서 일곱 걸음이다.", new[] { "knows_door" }),
                        H("drawing_table", "디캔터", 0.555f, 0.5f, 0.115f, 0.18f, null,
                            "디캔터에 브랜디가 반쯤 남았다. 잔은 하나도 쓰이지 않았다."),
                    },
                },
                new Room
                {
                    Id = "dining", Name = "식당", Image = "room_dining", X = 2, Y = 0,
                    Intro = "만찬이 끝난 식탁. 촛불이 아직 타고 있다.",
                    Hotspots = new[]
                    {
                        H("dining_gap", "촛대 자리", 0.44f, 0.5f, 0.14f, 0.12f, "candlestick",
                            "식탁 가운데 은촛대 한 짝이 없다. 촛농 자국만 동그랗게 남았다."),
                        H("dining_glass", "손대지 않은 잔", 0.91f, 0.54f, 0.06f, 0.12f, null,
                            "식탁 끝 잔만 처음 따른 그대로다. 좌석표를 보니 제로의 자리다. 오늘 밤 그는 한 모금도 마시지 않았다."),
                        H("dining_seats", "좌석표", 0.17f, 0.6f, 0.1f, 0.08f, null,
                            "노아의 자리 맞은편이 비어 있다. 좌석표엔 '탐정'. 노아는 처음부터 당신을 이 식탁에 앉힐 생각이었다."),
                        H("dining_painting", "바다 그림", 0.37f, 0.1f, 0.25f, 0.22f, null,
                            "폭풍 치는 바다 그림. 서명이 또렷하다 — 그런데 붓질이 어딘가 너무 매끈하다."),
                        H("dining_sideboard", "찬장", 0.3f, 0.36f, 0.4f, 0.08f, null,
                            "은식기가 가지런하다. 디저트 와인 잔 세 개를 꺼낸 자리가 비어 있다."),
                    },
                },
                new Room
                {
                    Id = "greenhouse", Name = "온실", Image = "room_greenhouse", X = 0, Y = 1,
                    Intro = "유리 지붕 위로 비가 쏟아진다. 구석의 나선 계단이 2층 서재로 곧장 이어진다.",
                    Overlays = new[] { new Overlay { Image = "ev_covered_body", X = 0.34f, Y = 0.55f, W = 0.33f, H = 0.44f, ShowIf = "dead:세이" } },
                    Hotspots = new[]
                    {
                        H("gh_pot", "화분", 0.06f, 0.62f, 0.23f, 0.2f, "butts",
                            "벤치 옆 화분 가장자리에 담배꽁초 세 개와 재가 있다."),
                        H("gh_door", "정원 문", 0.555f, 0.3f, 0.105f, 0.34f, null,
                            "정원으로 난 유리문. 빗물이 들이친다. 문턱 진흙엔 발자국이 없다 — 오늘 밤 정원으로 나간 사람은 없다."),
                        H("gh_table", "전지가위", 0.82f, 0.56f, 0.18f, 0.14f, null,
                            "흙 묻은 전지가위. 제자리에 있다.", null, new[] { "o_shears" }),
                        HIf("gh_body", "세이", 0.36f, 0.66f, 0.3f, 0.24f, "notebook,photo", "dead:세이",
                            "흰 천 아래 세이가 누워 있다. 카메라는 사라졌다. 손에 수첩이 쥐어져 있고, 수첩 사이에 사진 한 장이 끼워져 있다."),
                    },
                },
                new Room
                {
                    Id = "hall", Name = "현관홀", Image = "room_hall", X = 1, Y = 1,
                    Intro = "괘종시계가 15분마다 종을 친다. 가운데 계단은 2층 응접실로, 구석의 돌계단은 지하 세탁실로 이어진다.",
                    Hotspots = new[]
                    {
                        H("hall_clock", "괘종시계", 0.285f, 0.11f, 0.11f, 0.59f, "grandclock",
                            "15분마다 맑은 종을 친다. 시각이 정확하다. 이 소리는 저택 어디서든 들린다."),
                        H("hall_phone", "전화기", 0.03f, 0.44f, 0.18f, 0.48f, "phone",
                            "전화 코드가 둘로 끊겨 있다. 절단면이 매끈하다."),
                        H("hall_door", "현관문", 0.8f, 0.1f, 0.2f, 0.62f, null,
                            "바닷길이 잠겼다. 날이 밝기 전엔 아무도 나가지도, 들어오지도 못한다."),
                        H("hall_coats", "외투걸이", 0.74f, 0.29f, 0.065f, 0.4f, null,
                            "젖은 외투는 당신 것뿐이다. 오늘 밤 밖에 나갔다 온 손님은 없다."),
                        H("hall_view", "응접실 쪽 문", 0.07f, 0.1f, 0.13f, 0.33f, null,
                            "응접실로 난 문이 활짝 열려 있다. 여기 서면 피아노와 의자가 정면으로 보인다."),
                        H("hall_stairs", "계단", 0.45f, 0.2f, 0.27f, 0.5f, null,
                            "윤기 나는 마호가니 난간. 계단 끝은 2층 응접실로 이어진다."),
                    },
                },
                new Room
                {
                    Id = "kitchen", Name = "부엌", Image = "room_kitchen", X = 2, Y = 1,
                    Intro = "주전자가 아직 미지근하다.",
                    Hotspots = new[]
                    {
                        H("kitchen_tray", "찻쟁반", 0.385f, 0.49f, 0.385f, 0.2f, null,
                            "찻주전자와 찻잔 하나. 씻어 엎어 둔 잔이 하나 더 있다. 23시 30분에 하루가 새 차를 우리던 흔적이다."),
                        H("kitchen_knives", "칼꽂이", 0.86f, 0.21f, 0.12f, 0.34f, null,
                            "칼이 전부 제자리다. 오늘 밤 쓴 흔적은 없다.", null, new[] { "o_knife" }),
                        H("kitchen_stove", "화덕", 0.0f, 0.31f, 0.25f, 0.47f, null,
                            "주전자가 아직 미지근하다. 화덕엔 불씨만 낮게 남았다."),
                        H("kitchen_window", "창문", 0.59f, 0.1f, 0.17f, 0.29f, null,
                            "빗줄기 너머로 온실의 불빛이 흐릿하게 보인다."),
                    },
                },
                new Room
                {
                    Id = "laundry", Name = "세탁실", Image = "room_laundry", X = 1, Y = 2,
                    Intro = "좁은 돌방. 알전구 하나가 흔들린다.",
                    Hotspots = new[]
                    {
                        H("laundry_basin", "대야", 0.27f, 0.34f, 0.44f, 0.28f, "gauze",
                            "대야 물이 옅은 분홍빛이다. 거즈 뭉치가 잠겨 있고, 수면에 그을음 가루가 떠 있다."),
                        H("laundry_iron", "다리미", 0.05f, 0.22f, 0.13f, 0.24f, null,
                            "무쇠 다리미. 차갑다. 오늘 밤 쓰지 않았다.", null, new[] { "o_iron" }),
                        H("laundry_towel", "젖은 수건", 0.87f, 0.11f, 0.12f, 0.45f, null,
                            "수건 한 장만 축축하다. 누군가 손을 닦았다."),
                        H("laundry_drain", "배수구", 0.69f, 0.85f, 0.1f, 0.1f, null,
                            "배수구 둘레에 옅은 분홍빛 물자국이 남아 있다."),
                    },
                },
                new Room
                {
                    Id = "storage", Name = "창고", Image = "room_storage", X = 2, Y = 2,
                    Intro = "와인 냄새와 먼지 냄새. 저택에서 가장 외진 방이다.",
                    Hotspots = new[]
                    {
                        H("storage_ledger", "장부", 0.43f, 0.49f, 0.19f, 0.12f, "ledger",
                            "와인 출납 장부. 석 달 동안 비싼 와인 열두 병이 '파손'으로 지워졌다."),
                        H("storage_racks", "와인 선반", 0.1f, 0.22f, 0.22f, 0.32f, null,
                            "선반 한 칸에 정확히 열두 자리가 비어 있다."),
                        H("storage_tools", "공구 선반", 0.8f, 0.2f, 0.2f, 0.42f, null,
                            "망치와 톱. 먼지가 고르게 앉아 있다. 오늘 밤 아무도 만지지 않았다.", null, new[] { "o_hammer" }),
                    },
                },
            },

            Corridors = new[]
            {
                new[] { "study", "drawing" }, new[] { "drawing", "dining" }, new[] { "drawing", "hall" },
                new[] { "study", "greenhouse" }, new[] { "greenhouse", "hall" }, new[] { "dining", "kitchen" },
                new[] { "hall", "kitchen" }, new[] { "hall", "laundry" }, new[] { "kitchen", "storage" },
                new[] { "laundry", "storage" },
            },

            Evidence = BuildEvidence(),
            Suspects = BuildSuspects(),
            Questions = BuildQuestions(),

            Events = new[]
            {
                new TimedEvent
                {
                    Id = "midnight", At = T(0, 0),
                    Toast = "괘종시계가 자정을 친다 — 노아가 '이야기를 끝내겠다'던 시각이다.",
                },
                // 응접실에 탐정이 있으면 제로는 피아노 의자에 손을 대지 못한다(그때 한 번 더 노린다)
                new TimedEvent
                {
                    Id = "burn_watch", At = T(0, 45), Unless = "!at:drawing|has:ch6",
                    SetFlags = new[] { "burn_stopped" },
                    Toast = "피아노 의자 쪽으로 다가가던 제로가, 당신을 보고 걸음을 멈춘다.",
                },
                new TimedEvent
                {
                    Id = "burn_held", At = T(0, 45), Unless = "!held:제로|has:ch6|at:drawing",
                    Toast = "제로가 힐끗 회중시계를 본다. 당신과 마주 앉아 있는 한, 그는 아무 데도 갈 수 없다.",
                },
                new TimedEvent
                {
                    Id = "burn", At = T(0, 45), Unless = "has:ch6|at:drawing|held:제로", Room = "drawing",
                    SetFlags = new[] { "ch6_burned" },
                    Toast = "응접실 쪽에서 종이 타는 냄새가 난다…",
                },
                new TimedEvent
                {
                    Id = "burn_watch2", At = T(1, 20), Unless = "!at:drawing|has:ch6|ch6_burned",
                    Toast = "제로가 다시 피아노 쪽을 흘끗 본다. 당신이 있는 한 손을 쓰지 못하는 눈치다.",
                },
                new TimedEvent
                {
                    Id = "burn2", At = T(1, 20), Unless = "has:ch6|at:drawing|ch6_burned|held:제로", Room = "drawing",
                    SetFlags = new[] { "ch6_burned" },
                    Toast = "응접실 쪽에서 종이 타는 냄새가 난다…",
                },
                new TimedEvent
                {
                    Id = "warning", At = T(1, 45), Unless = "theory_ok|dead:세이",
                    Toast = "세이가 카메라를 들고 온실 쪽으로 가는 게 보였다. \"번개 사진 좀 찍을게.\"",
                },
                new TimedEvent
                {
                    Id = "murder", At = T(2, 0), Unless = "theory_ok|at:greenhouse|held:제로", Kill = "세이", Room = "greenhouse",
                    SetFlags = new[] { "sei_dead" },
                    CutTitle = "두 번째 비명",
                    CutSub = "세이가 온실에서 숨진 채 발견됐다.",
                    CutNote = "흰 천 아래, 세이의 손에\n수첩 한 권이 쥐어져 있었다.",
                },
                new TimedEvent
                {
                    Id = "murder_held", At = T(2, 0), Unless = "!held:제로|at:greenhouse|theory_ok|dead:세이",
                    Toast = "제로가 자꾸 온실 쪽을 돌아본다. 당신이 붙잡아 둔 사이, 세이는 번개 사진 몇 장을 찍고 식당으로 돌아갔다.",
                },
                new TimedEvent
                {
                    Id = "murder_watch", At = T(2, 0), Unless = "!at:greenhouse|theory_ok|dead:세이",
                    Toast = "온실 입구에서 서성이던 제로가 당신을 보고 발길을 돌린다. 세이는 번개 사진 몇 장을 찍고 식당으로 돌아갔다.",
                },
            },

            Schedule = new[]
            {
                new Stop("카이", T(23, 45), "kitchen", 0.36f), new Stop("카이", T(0, 30), "storage", 0.44f), new Stop("카이", T(1, 5), "kitchen", 0.74f),
                new Stop("제로", T(23, 45), "study", 0.72f), new Stop("제로", T(0, 20), "hall", 0.22f), new Stop("제로", T(0, 40), "drawing", 0.74f),
                new Stop("제로", T(1, 15), "drawing", 0.28f), new Stop("제로", T(1, 30), "drawing", 0.58f), new Stop("제로", T(1, 50), "greenhouse", 0.68f),
                new Stop("제로", T(2, 5), "drawing", 0.58f),
                new Stop("미로", T(23, 45), "dining", 0.42f),
                new Stop("하루", T(23, 45), "hall", 0.48f), new Stop("하루", T(0, 50), "kitchen", 0.3f), new Stop("하루", T(1, 30), "dining", 0.22f),
                new Stop("세이", T(23, 45), "hall", 0.9f), new Stop("세이", T(1, 0), "dining", 0.82f), new Stop("세이", T(1, 45), "greenhouse", 0.58f),
                new Stop("세이", T(2, 10), "dining", 0.82f),
            },

            Words = BuildWords(),
            Blanks = new[]
            {
                new Blank { Id = "killer", Kind = WordKind.Person, Answer = "p_zero" },
                new Blank { Id = "time", Kind = WordKind.Time, Answer = "t2245" },
                new Blank { Id = "weapon", Kind = WordKind.Object, Answer = "o_poker" },
                new Blank { Id = "fake", Kind = WordKind.Deception, Answer = "d_watch" },
                new Blank { Id = "alibi", Kind = WordKind.Deception, Answer = "d_pianola" },
                new Blank { Id = "wash", Kind = WordKind.Place, Answer = "r_laundry" },
                new Blank { Id = "motive", Kind = WordKind.Secret, Answer = "s_surgery" },
            },
            Sentences = new[]
            {
                "노아를 죽인 범인은 {killer}.",
                "{time}, 범인은 서재에서 {weapon:로} 노아의 뒤통수를 내리쳤다.",
                "그리고 {fake:로} 사망 시각을 늦춰 보이게 했다.",
                "범행 시각의 알리바이는 {alibi:이} 만들어 주었다.",
                "흉기에 묻은 피는 {wash}에서 씻어 냈다.",
                "노아의 원고에는 범인이 감추려던 비밀, {motive:이} 적혀 있었다.",
            },
            Rehearsal = new[]
            {
                "제로|재구성이라… 그럴듯한 이야기군요.",
                "제로|하지만 이야기는 증거가 아닙니다, 탐정님. 하나씩 따져 보죠.",
            },
            Rebuttals = new[]
            {
                new Rebuttal
                {
                    Line = "시계를 누가 만졌든 저와는 상관없습니다. 22시 45분이라면 저는 응접실에서 녹턴을 치고 있었으니까요.",
                    Accept = new[] { "photo@detail:bench", "roll" },
                    Counter = "그 녹턴은 사람이 친 게 아니었습니다.",
                    Success = new[]
                    {
                        "탐정|22시 45분, 응접실의 피아노 의자는 비어 있었습니다. 녹턴을 친 건 롤을 건 자동 연주 장치였죠.",
                        "탐정|당신은 그 시각 연결문 너머, 서재에 있었습니다.",
                        "제로|…….",
                    },
                    Wrong = "그게 제가 피아노 앞에 없었다는 증거가 됩니까?",
                },
                new Rebuttal
                {
                    Line = "세탁실의 거즈 말입니까? 시신을 살필 때 제가 쓴 겁니다. 의사니까요.",
                    Accept = new[] { "t_laundry", "notebook" },
                    Counter = "22시 55분 — 시신이 발견되기 35분 전입니다.",
                    Success = new[]
                    {
                        "탐정|22시 55분, 당신은 소매가 젖은 채 세탁실에서 나왔습니다. 세이 씨가 봤습니다.",
                        "탐정|노아가 발견된 건 23시 30분. 그보다 35분 전에, 당신은 무엇을 씻고 있었습니까?",
                        "제로|……그건.",
                    },
                    Wrong = "그 시각에 세탁실에 있었다는 증거는 어디 있습니까?",
                },
                new Rebuttal
                {
                    Line = "동기가 없습니다. 노아는 제 친구였어요. 원고 따위로 사람을 죽이진 않습니다.",
                    Accept = new[] { "ch6", "fragment" },
                    Counter = "원고 제6장, 「흰 가운의 침묵」.",
                    Success = new[]
                    {
                        "탐정|3년 전 겨울밤, 노을 씨의 수술. 당신은 그날 당직이 아니었고, 술을 마시고 있었습니다.",
                        "탐정|노아는 그걸 알고 있었습니다. 그래서 당신은 이 장이 세상에 나오기 전에 그를 막아야 했죠.",
                        "제로|…그만하십시오.",
                    },
                    Wrong = "그게 제 동기라는 증거가 됩니까?",
                },
            },
            Confession = new[]
            {
                "제로|…노을이는, 제가 마지막으로 본 환자였습니다.",
                "제로|그날 밤 저는 당직이 아니었어요. 술을 마시고 있었죠. 전화가 왔고, 병원에 남은 의사는 저뿐이었습니다. 손이 떨렸습니다.",
                "제로|기록을 고쳤습니다. '불가항력.' 3년 동안 그 한 줄로 버텼습니다.",
                "제로|오늘 저녁 노아가 그러더군요. '6장은 네 얘기야. 자정에 다 같이 이야기하자.' 모두 앞에서 끝장을 내겠다는 뜻이라고 생각했습니다.",
                "제로|만찬 전에 전화선을 잘랐습니다. 수술 가위로. 녹턴 롤을 걸고, 연결문으로 들어갔습니다.",
                "탐정|노아는 원고를 출판할 생각이 없었습니다.",
                "탐정|타자기에 남은 마지막 줄. '자정, 나는 이 원고를 ㅌ—'. 그다음 글자는 '태운다'였을 겁니다.",
                "하루|…노아 씨가 그러셨어요. 자정에 다 돌려주겠다고. 불에 태워서, 우리 모두를 놓아주겠다고.",
                "[has:ch6]탐정|6장의 마지막 문장입니다. '나는 그를 용서하기로 했다.'",
                "[!has:ch6]탐정|6장의 마지막 장은 재가 되었습니다. 하지만 노아가 당신을 이 식탁에 부른 이유는 하나였습니다. 용서하려고.",
                "제로|…………",
            },
            Endings = new[]
            {
                new Ending
                {
                    Id = "witness", Title = "새벽의 증인", Solved = true,
                    Panels = new[]
                    {
                        new Panel("story_fire", "자정을 훌쩍 넘긴 시각. 남은 다섯 사람은 응접실 벽난로 앞에 모였다.\n그들은 각자의 장을 불 속에 넣었다. 6장만은 남겨 두었다 — 그건 이제 증거였으니까."),
                        new Panel("story_dawn", "새벽, 폭풍이 걷히고 바닷길이 열렸다.\n세이는 그 장면을 찍지 않았다. 대신 오래 바라보았다. 다락방의 아이가, 처음으로 가족 사이에 서 있었다.\n\n— 「자정의 원고」 사건 종결."),
                    },
                },
                new Ending
                {
                    Id = "two_graves", Title = "두 개의 무덤", Solved = true,
                    Panels = new[]
                    {
                        new Panel("story_fire", "남은 네 사람은 응접실 벽난로 앞에 모였다. 그들은 각자의 장을 불 속에 넣었다.\n주인을 잃은 5장 「다락방의 아이」는, 하루가 대신 넣었다."),
                        new Panel("story_dawn", "새벽, 폭풍이 걷혔다. 하룻밤에 두 사람이 죽었다.\n탐정은 생각했다. 조금만 더 빨랐더라면.\n\n— 「자정의 원고」 사건 종결."),
                    },
                },
                new Ending
                {
                    Id = "wrong_man", Title = "새벽의 오판", Solved = false,
                    Panels = new[]
                    {
                        new Panel("story_dawn", "새벽 여섯 시, 경찰이 도착했다. 탐정은 끝내 진실에 닿지 못했다."),
                        new Panel("story_storm", "경찰은 23시 10분에 혼자였고 노아와 다퉜던 카이를 데려갔다.\n제로는 조용히 외투를 걸치고 저택을 떠났다. 응접실 벽난로에는, 아직 재가 따뜻했다."),
                    },
                },
            },
            Truth = new[]
            {
                "22:30  카이가 서재에서 노아와 다툼(원고 2장 — 와인을 팔아 노름빚을 갚은 일). 22:35 온실로 나감.",
                "22:35  하루가 서재로 차를 가져감. 응접실에서 피아노 앞에 앉은 제로를 봄.",
                "22:40  제로가 자동 연주 피아노에 「녹턴」 롤을 걸고, 연결문으로 서재에 들어감. 노아는 7장을 쓰기 시작.",
                "22:45  제로가 응접실 부지깽이로 노아를 내리침. 현관홀의 세이가 쿵 소리를 듣고 셔터를 누름.",
                "22:46  회중시계 바늘을 23:10에 맞추고 용두를 당겨 멈춤. 청동 문진에 피를 묻힘. 원고 6장을 챙김.",
                "22:50  세탁실에서 의료용 거즈로 부지깽이를 씻음. 22:55 젖은 소매로 나오다 세이에게 보임.",
                "22:58  녹턴이 끝나자 롤을 되감지 않고 보관함에 꽂음. 6장은 피아노 의자 속에 숨김.",
                "23:00  미로가 자기 장(3장)을 훔치러 온실 나선 계단으로 서재에 올라갔다가, 시신을 보고 달아남.",
                "23:05  제로가 식당에 합류해 23:10의 알리바이를 만듦.",
                "23:30  하루가 시신을 발견. 제로가 '사망 23:10, 흉기 문진'이라고 진단함.",
                "[ch6_burned]00:45  제로가 응접실 벽난로에서 6장을 태움.",
                "[dead:세이]02:00  제로가 온실에서 세이를 살해하고 카메라를 가져감.",
                "만찬 전, 제로는 수술 가위로 전화선을 잘라 두었다.",
                "노아는 원고를 출판할 생각이 없었다. 자정에 모두 앞에서 태우고, 서로를 놓아주려 했다.",
            },
        };

        // ───────────────────────── 증거 ─────────────────────────

        static Evidence[] BuildEvidence() => new[]
        {
            new Evidence
            {
                Id = "body", Name = "노아의 시신", Kind = EvidenceKind.Physical, Image = "ev_body", Source = "서재 · 책상",
                Summary = "뒤통수에 길고 가느다란 상처.",
                Text = "책상에 엎드린 채 숨져 있다. 뒤통수에 길고 가느다란 상처 하나 — 둥근 물건보다는 막대 같은 것에 맞은 모양이다. 피는 많지 않다. 몸은 아직 조금 따뜻하지만, 그것만으로 정확한 시각을 말하기는 어렵다.",
            },
            new Evidence
            {
                Id = "watch", Name = "노아의 회중시계", Kind = EvidenceKind.Physical, Image = "ev_watch", Source = "서재 · 시신의 손 옆",
                Summary = "23시 10분에 멈춰 있다.",
                Text = "은 회중시계. 유리가 깨졌고 바늘은 23시 10분에 멈춰 있다. 쓰러질 때 깨진 걸까.",
                Timeline = new[] { new TimeNote(T(23, 10), "노아의 회중시계가 멈춘 시각") },
                Words = new[] { "t2310" },
                Details = new[]
                {
                    new Detail
                    {
                        Id = "crown", Label = "당겨진 용두", X = 0.55f, Y = 0.11f, W = 0.16f, H = 0.16f,
                        Text = "용두가 바깥으로 당겨져 있다. 시곗바늘을 맞출 때의 위치다 — 이렇게 하면 시계가 멈춘다. 쓰러지면서 이렇게 될 수는 없다. 누군가 바늘을 23시 10분에 맞춰 놓은 것이다.",
                        Words = new[] { "d_watch" },
                        Timeline = new[] { new TimeNote(T(23, 10), "회중시계는 23:10에 '맞춰져' 멈춰 있었다 — 조작") },
                    },
                },
            },
            new Evidence
            {
                Id = "paperweight", Name = "피 묻은 청동 문진", Kind = EvidenceKind.Physical, Image = "ev_paperweight", Source = "서재 · 책상 앞 바닥",
                Summary = "범행 도구로 보인다.",
                Text = "사자 장식이 달린 둥근 청동 문진. 위쪽에 피가 묻어 있다.",
                Words = new[] { "o_paperweight" },
                Details = new[]
                {
                    new Detail
                    {
                        Id = "top", Label = "발라 놓은 피", X = 0.55f, Y = 0.21f, W = 0.2f, H = 0.18f,
                        Text = "피는 윗면에만 얇게 '발려' 있다. 머리를 내리친 물건이라면 둥근 아랫면에 피나 머리카락이 묻었어야 한다. 아랫면은 깨끗하다.",
                        Words = new[] { "d_blood" },
                    },
                },
            },
            new Evidence
            {
                Id = "typewriter", Name = "타자기의 마지막 장", Kind = EvidenceKind.Document, Image = "ev_typewriter", Source = "서재 · 책상",
                Summary = "「제7장 자정의 불 — 22:40」에서 끊겼다.",
                Text = "타자기에 끼워진 종이. 맨 위에 「제7장 자정의 불」, 그 옆에 노아가 늘 적던 시각 표시 「22:40」. 본문은 두 줄뿐이다.",
                Document = "제7장  자정의 불                    22:40\n\n가면은 오늘 밤 모두 벗겨진다.\n그리고 자정, 나는 이 원고를 ㅌ",
                Chapter = 7,
                Timeline = new[] { new TimeNote(T(22, 40), "노아가 7장을 쓰기 시작함 (타자기)") },
                Words = new[] { "t2240" },
            },
            new Evidence
            {
                Id = "manuscript", Name = "원고 뭉치 『가면들의 만찬』", Kind = EvidenceKind.Physical, Image = "ev_manuscript", Source = "서재 · 책상",
                Summary = "3장과 6장이 빠져 있다.",
                Text = "노끈으로 묶인 원고. 목차에는 일곱 장 — 1 만찬의 초대 · 2 빚진 집사 · 3 가짜 거장 · 4 그림자 작가 · 5 다락방의 아이 · 6 흰 가운의 침묵 · 7 자정의 불. 3장과 6장이 없다. (남은 장은 '원고' 탭에서 읽을 수 있다.)",
            },
            new Evidence
            {
                Id = "ch1", Name = "원고 제1장 「만찬의 초대」", Kind = EvidenceKind.Document, Image = "ev_pages", Chapter = 1, Source = "원고 뭉치",
                Summary = "노아가 쓴 서문.",
                Text = "원고의 첫 장. 노아가 이 소설을 쓴 이유가 적혀 있다.",
                Document = "여섯 개의 의자, 여섯 개의 가면. 나는 오랫동안 이 식탁을 상상했다.\n\n우리 여섯은 같은 학교 문예부였다. 서로의 첫 문장을 읽어 주던 시절이 있었다. 그리고 각자 하나씩, 말할 수 없는 것을 갖게 되었다.\n\n이 소설은 고발장이 아니다. 거울이다. 오늘 밤 우리는 거울 앞에 앉는다.\n그리고 자정이 되면 — 그다음 일은 마지막 장에 쓰겠다.",
            },
            new Evidence
            {
                Id = "ch2", Name = "원고 제2장 「빚진 집사」", Kind = EvidenceKind.Document, Image = "ev_pages", Chapter = 2, Source = "원고 뭉치",
                Summary = "집사의 비밀.",
                Text = "저택 집사에 관한 장.",
                Document = "집사는 매일 밤 잔을 닦는다. 그리고 매주 한 병씩, 지하 저장고의 와인을 몰래 팔았다. 판 돈은 노름판으로 사라졌다.\n\n그는 주인이 모르는 줄 알았다. 주인은 알고 있었다. 장부의 숫자는 거짓말을 하지 않으니까.\n\n주인은 그를 내쫓지 않았다. 그가 아직 친구라고 믿고 싶었기 때문이다.",
                Words = new[] { "s_debt" },
            },
            new Evidence
            {
                Id = "ch3", Name = "원고 제3장 「가짜 거장」", Kind = EvidenceKind.Document, Image = "ev_pages", Chapter = 3, Source = "미로에게서",
                Summary = "마술사의 비밀.",
                Text = "미로가 서재에서 몰래 가져갔던 장.",
                Document = "마술사는 관객의 눈을 속여 밥을 먹는다. 그런데 어느 날부터 그는 무대 밖에서도 속이기 시작했다.\n\n그가 판 그림 가운데 진짜는 하나도 없었다. 아버지의 식당에 걸린 바다 그림도 그의 솜씨다.\n\n나는 그 가짜 앞에서 오래 웃었다. 정말 잘 그렸으니까.",
                Words = new[] { "s_forgery" },
            },
            new Evidence
            {
                Id = "ch4", Name = "원고 제4장 「그림자 작가」", Kind = EvidenceKind.Document, Image = "ev_pages", Chapter = 4, Source = "원고 뭉치",
                Summary = "『푸른 등대』의 비밀.",
                Text = "노아의 출세작에 관한 장.",
                Document = "『푸른 등대』를 쓴 사람은 내가 아니다. 열아홉 살 소녀가 공책에 쓴 이야기를, 나는 내 이름으로 세상에 내놓았다.\n\n그녀는 아무 말도 하지 않았다. 대신 삼 년 동안 내 원고를 타자로 옮겼다. 한 글자도 틀리지 않고.\n\n이 장은 그녀에게 쓰는 사과문이다. 너무 늦은.",
                Words = new[] { "s_ghost" },
            },
            new Evidence
            {
                Id = "ch5", Name = "원고 제5장 「다락방의 아이」", Kind = EvidenceKind.Document, Image = "ev_pages", Chapter = 5, Source = "원고 뭉치",
                Summary = "다락방에 있던 소녀.",
                Text = "노아 집안의 숨은 이야기.",
                Document = "아버지의 장례식 날, 다락방 창가에 낯선 소녀가 서 있었다. 아무도 그녀를 소개하지 않았다.\n\n나중에야 알았다. 그 아이는 아버지의 또 다른 딸이었다. 이 집의 절반은 처음부터 그녀의 것이었다.\n\n그녀는 늘 카메라 뒤에 숨는다. 찍히는 쪽이 되면 들킬까 봐.",
                Words = new[] { "s_sibling" },
            },
            new Evidence
            {
                Id = "ch6", Name = "원고 제6장 「흰 가운의 침묵」", Kind = EvidenceKind.Document, Image = "ev_pages", Chapter = 6, Source = "응접실 · 피아노 의자 속",
                Summary = "누군가 피아노 의자에 숨겼다.",
                Text = "피아노 의자 속 악보 밑에 접혀 있던 장.",
                Document = "삼 년 전 겨울밤, 내 동생 노을이 수술대에 올랐다. 그날 밤 당직은 그가 아니었다. 그는 술을 마시고 있었고, 병원에 남은 의사는 그뿐이었다.\n\n노을은 깨어나지 못했다. 기록에는 '불가항력'이라고 적혔다.\n\n나는 간호사의 편지로 모든 것을 알았다. 그리고 오래 그를 미워했다.\n\n이제 그만하려 한다. 나는 그를 용서하기로 했다.",
                Words = new[] { "s_surgery" },
            },
            new Evidence
            {
                Id = "fragment", Name = "타다 남은 원고 조각", Kind = EvidenceKind.Document, Image = "ev_fragment", Chapter = 6, Source = "응접실 · 벽난로",
                Summary = "누군가 6장을 태웠다.",
                Text = "벽난로 재 속에서 건진 종이 조각. 「제6장」의 일부다. 남은 글자를 이어 붙이면…",
                Document = "…흰 가운의…\n\n…그날 밤 당직은 그가 아니…\n…술을 마시고 있…\n\n…노을은 깨어나지…\n…기록에는 '불가항…",
                Words = new[] { "s_surgery" },
            },
            new Evidence
            {
                Id = "lighter", Name = "'K'가 새겨진 은 라이터", Kind = EvidenceKind.Physical, Image = "ev_lighter", Source = "서재 · 쓰러진 의자 밑",
                Summary = "누군가 서재에서 떨어뜨렸다.",
                Text = "은으로 만든 라이터. 옆면에 'K'가 새겨져 있다. 아직 기름 냄새가 난다.",
            },
            new Evidence
            {
                Id = "card", Name = "스페이드 에이스", Kind = EvidenceKind.Physical, Image = "ev_card", Source = "서재 · 책상 밑",
                Summary = "마술사의 표식이 찍힌 카드.",
                Text = "트럼프 카드 한 장. 귀퉁이에 금박으로 작은 별 세 개 — 무대 마술사가 자기 카드에 남기는 표식이다.",
            },
            new Evidence
            {
                Id = "teacup", Name = "식은 찻잔", Kind = EvidenceKind.Physical, Image = "ev_teacup", Source = "서재 · 책상",
                Summary = "한 모금도 마시지 않았다.",
                Text = "찻잔이 가득 찬 채 식어 있다. 표면에 얇은 막이 앉았다. 차를 받고 얼마 지나지 않아 변을 당했다는 뜻일까.",
            },
            new Evidence
            {
                Id = "poker", Name = "응접실의 부지깽이", Kind = EvidenceKind.Physical, Image = "ev_poker", Source = "응접실 · 벽난로 도구걸이",
                Summary = "혼자만 깨끗하고, 축축하다.",
                Text = "부젓가락과 부삽은 그을음투성이인데, 부지깽이만 반짝일 만큼 깨끗하고 아직 물기가 남아 있다.",
                Words = new[] { "o_poker" },
                Details = new[]
                {
                    new Detail
                    {
                        Id = "seam", Label = "이음매의 핏자국", X = 0.59f, Y = 0.25f, W = 0.16f, H = 0.16f,
                        Text = "손잡이와 쇠막대가 이어지는 틈에 검붉은 얼룩이 말라붙어 있다. 닦아 냈지만 틈새까지는 못 닦았다 — 피다.",
                    },
                },
            },
            new Evidence
            {
                Id = "pianola", Name = "자동 연주 피아노", Kind = EvidenceKind.Physical, Image = "ev_pianola", Source = "응접실 · 피아노",
                Summary = "롤을 걸면 사람 없이도 연주된다.",
                Text = "자동 연주 장치(피아놀라)가 달린 업라이트 피아노. 건반 위 덮개 안에 종이 롤을 끼우는 축이 있다. 지금은 비어 있다.",
                Words = new[] { "d_pianola" },
            },
            new Evidence
            {
                Id = "roll", Name = "덜 감긴 녹턴 롤", Kind = EvidenceKind.Physical, Image = "ev_roll", Source = "응접실 · 롤 보관함",
                Summary = "누군가 최근에 이 곡을 틀었다.",
                Text = "롤 열두 개 가운데 「녹턴 Op.9-2」만 끝이 풀린 채 꽂혀 있다. 다른 롤은 모두 처음까지 되감겨 있다. 연주가 끝난 뒤 되감지 않고 서둘러 넣은 것이다.",
                Words = new[] { "d_pianola" },
            },
            new Evidence
            {
                Id = "butts", Name = "담배꽁초 세 개", Kind = EvidenceKind.Physical, Image = "ev_butts", Source = "온실 · 화분",
                Summary = "같은 상표. 20분은 머문 셈이다.",
                Text = "금색 띠를 두른 같은 상표의 꽁초 세 개와 재. 한 개비에 칠팔 분이라면, 누군가 여기서 20분 넘게 서 있었다.",
            },
            new Evidence
            {
                Id = "candlestick", Name = "짝 잃은 은촛대", Kind = EvidenceKind.Physical, Image = "ev_candlestick", Source = "식당 · 식탁",
                Summary = "은촛대 한 짝이 사라졌다.",
                Text = "식탁 가운데 은촛대 한 짝이 없다. 식탁보에 동그란 촛농 자국만 남았다. 흉기가 되기에 충분히 무거운 물건이다.",
                Words = new[] { "o_candle" },
            },
            new Evidence
            {
                Id = "gauze", Name = "분홍빛 대야의 거즈", Kind = EvidenceKind.Physical, Image = "ev_gauze", Source = "세탁실 · 대야",
                Summary = "의료용 거즈. 이 저택의 물건이 아니다.",
                Text = "대야 물이 옅은 분홍빛이다. 물에 잠긴 거즈 뭉치, 수면에는 검은 그을음 가루. 멸균 포장되는 의료용 거즈다 — 저택 구급상자엔 없는 물건이다.",
            },
            new Evidence
            {
                Id = "phone", Name = "잘린 전화선", Kind = EvidenceKind.Physical, Image = "ev_phone", Source = "현관홀 · 협탁",
                Summary = "폭풍 탓이 아니다.",
                Text = "전화 코드가 둘로 끊겨 있다. 절단면이 칼로 자른 듯 매끈하다 — 아주 잘 드는 가위가 아니면 이렇게 자를 수 없다.",
                Words = new[] { "d_phone" },
            },
            new Evidence
            {
                Id = "grandclock", Name = "현관홀 괘종시계", Kind = EvidenceKind.Physical, Image = "ev_grandclock", Source = "현관홀",
                Summary = "15분마다 종을 친다. 정확하다.",
                Text = "현관홀의 괘종시계. 15분마다 맑은 종을 친다. 지금 시각과 정확히 맞는다. 저택 어디서든 이 종소리가 들린다.",
                Words = new[] { "d_clock" },
            },
            new Evidence
            {
                Id = "ledger", Name = "와인 저장고 장부", Kind = EvidenceKind.Document, Image = "ev_ledger", Source = "창고 · 술통 위",
                Summary = "장부와 병 수가 맞지 않는다.",
                Text = "와인 출납 장부. 지난 석 달 동안 비싼 와인 열두 병이 '파손'으로 지워졌다. 저장고 선반에는 정확히 열두 칸이 비어 있다. 장부 글씨는 집사의 것이다.",
                Words = new[] { "s_debt" },
            },
            new Evidence
            {
                Id = "photo", Name = "세이의 사진 (22:45)", Kind = EvidenceKind.Physical, Image = "ev_photo", Source = "세이",
                Summary = "번개가 친 순간의 현관홀.",
                Text = "즉석카메라 사진. 번개가 번쩍인 순간의 현관홀과 괘종시계. 바늘은 22시 45분을 가리킨다.",
                Timeline = new[] { new TimeNote(T(22, 45), "세이가 현관홀에서 사진을 찍음") },
                Words = new[] { "t2245" },
                Details = new[]
                {
                    new Detail
                    {
                        Id = "bench", Label = "비어 있는 피아노 의자", X = 0.66f, Y = 0.4f, W = 0.22f, H = 0.25f,
                        Text = "사진 위쪽, 가운데 계단 끝 — 2층 응접실의 피아노가 찍혔다. 의자가 비어 있다. 그런데 그 시각 응접실에선 녹턴이 흘러나오고 있었다.",
                        Words = new[] { "d_pianola" },
                        Timeline = new[] { new TimeNote(T(22, 45), "응접실 피아노 의자는 비어 있었다 (사진)") },
                    },
                },
            },
            new Evidence
            {
                Id = "notebook", Name = "세이의 수첩", Kind = EvidenceKind.Document, Image = "ev_notebook", Source = "온실 · 세이의 손",
                Summary = "시각별 짧은 메모.",
                Text = "세이가 늘 들고 다니던 수첩. 짧은 메모가 시각별로 적혀 있다.",
                Document = "22:40  홀. 폭풍. 셔터.\n22:45  괘종. 위층에서 쿵. 찍음.\n22:55  제로 — 세탁실에서 나옴. 소매 젖음. 손 씻었나?\n23:30  하루 비명.\n01:45  온실. 번개 좋을 듯.",
                Timeline = new[]
                {
                    new TimeNote(T(22, 45), "위층에서 쿵 소리 (세이의 수첩)"),
                    new TimeNote(T(22, 55), "제로가 젖은 소매로 세탁실에서 나옴 (세이의 수첩)"),
                },
                Words = new[] { "t2245", "t2255" },
            },

            // ── 증언(진술을 들으면 수첩에 적힌다) ──
            Testimony("t_shout", "하루의 증언 — 22:30의 고함", "하루",
                "22시 30분쯤 서재에서 고함이 들렸다. 카이의 목소리였다.", T(22, 30), "서재에서 고함 — 카이의 목소리 (하루)", "t2230"),
            Testimony("t_tea", "하루의 증언 — 22:35의 차", "하루",
                "22시 35분, 하루가 서재로 차를 가져갔을 때 노아는 살아서 타자를 치고 있었다.", T(22, 35), "하루가 차를 가져감. 노아는 살아 있었다 (하루)", "t2235"),
            Testimony("t_piano", "녹턴 — 22:40부터", "하루 · 미로",
                "22시 40분부터 23시 무렵까지 응접실에서 녹턴이 흘러나왔다. 제로가 치겠다고 했다.", T(22, 40), "응접실에서 녹턴이 들리기 시작함 (하루 · 미로)", "t2240"),
            Testimony("t_found", "하루의 증언 — 발견", "하루",
                "23시 30분, 새 차를 가져간 하루가 시신을 발견했다.", T(23, 30), "하루가 시신을 발견 (하루)", "t2330"),
            Testimony("t_dining", "식당의 세 사람", "제로 · 세이",
                "23시 5분부터 23시 30분까지 제로 · 하루 · 세이는 식당에 함께 있었다.", T(23, 5), "제로 · 하루 · 세이가 식당에 모임", null),
            Testimony("t_kai", "카이의 증언 — 22:30의 서재", "카이",
                "카이는 22시 30분에 서재에서 노아와 다투고 22시 35분에 나선 계단으로 온실에 내려갔다. 그때 노아는 살아 있었다.", T(22, 35), "카이가 서재를 나와 온실로 (카이)", "t2235"),
            Testimony("t_kai_saw", "카이의 증언 — 23:00의 미로", "카이",
                "23시쯤, 미로가 온실 나선 계단으로 서재에 올라가는 걸 카이가 봤다.", T(23, 0), "미로가 온실 나선 계단으로 서재에 올라감 (카이)", "t2300"),
            Testimony("t_miro", "미로의 증언 — 23:00의 서재", "미로",
                "23시 정각, 미로가 서재에 들어갔을 때 노아는 이미 죽어 있었다.", T(23, 0), "노아는 이미 죽어 있었다 (미로)", "t2300"),
            Testimony("t_thud", "세이의 증언 — 22:45의 소리", "세이",
                "22시 45분, 괘종이 칠 때 위층에서 '쿵' 소리가 났다.", T(22, 45), "위층에서 쿵 소리 (세이)", "t2245"),
            Testimony("t_laundry", "세이의 증언 — 22:55의 제로", "세이",
                "22시 55분, 제로가 세탁실에서 나왔다. 소매가 젖어 있었다.", T(22, 55), "제로가 젖은 소매로 세탁실에서 나옴 (세이)", "t2255"),
        };

        static Evidence Testimony(string id, string name, string who, string text, int at, string note, string word) => new Evidence
        {
            Id = id, Name = name, Kind = EvidenceKind.Testimony, Image = null, Source = who,
            Summary = text, Text = text,
            Timeline = new[] { new TimeNote(at, note) },
            Words = word != null ? new[] { word } : new string[0],
        };

        // ───────────────────────── 인물 ─────────────────────────

        static Suspect[] BuildSuspects() => new[]
        {
            new Suspect
            {
                Name = "카이", Role = "집사 겸 바텐더",
                Bio = "노아의 동창. 지금은 이 저택의 집사로 일한다. 성미가 급하다.",
                Greeting = new[] { "탐정? 하, 늦게도 왔네.", "주인 나리가 죽었어. 보면 알잖아. 뭘 더 물어?" },
                Statements = new[]
                {
                    new Statement { Id = "k_alibi", Text = "만찬 끝나고 잔을 닦다가, 22시 35분부터 23시 5분까지 온실에서 담배 피웠어." },
                    new Statement
                    {
                        Id = "k_study", Lie = true, Text = "서재? 근처에도 안 갔어.",
                        BrokenBy = new[] { "lighter", "t_shout" },
                        Why = "카이의 라이터가 서재에 떨어져 있었고, 22시 30분 서재에서 카이의 고함이 들렸다.",
                        BreakLines = new[]
                        {
                            "…쳇.",
                            "그래, 갔다. 22시 반에. 그 자식이 내 얘기를 책에 썼다길래 따지러 갔지.",
                            "5분 만에 나왔어. 나선 계단으로 온실에 내려왔지. 그땐 멀쩡히 살아서 타자 치고 있었다고!",
                            "…그러고 보니 23시쯤, 미로 녀석이 온실 나선 계단으로 올라가더라. 난 담배나 피우고 있었고.",
                        },
                        Gives = new[] { "t_kai", "t_kai_saw" },
                        Truth = "22:30에 서재에서 노아와 다투고 22:35에 온실로 나갔다. 23:00쯤 미로가 온실 나선 계단으로 서재에 올라가는 걸 봤다.",
                        Hint = "서재 이야기가 나오면 대뜸 짜증을 내며 말을 돌린다. 말하다 말고 '내 라이터 어디 갔지' 하고 혼잣말을 한다. 탐정이 계속 파고들면 '서재에서 뭘 주웠으면 가져와 보든가'라고 받아친다.",
                        Guard = new[] { "서재에 갔", "서재에 들어갔", "서재에 올라갔", "따지러 갔", "다퉜", "싸웠", "라이터를 떨어" },
                    },
                    new Statement { Id = "k_2310", Text = "23시 10분? 복도를 걷고 있었지. 혼자서. 그게 뭐." },
                    new Statement
                    {
                        Id = "k_debt", Lie = true, Text = "빚? 그딴 거 없어.",
                        ShowIf = "has:ch2|has:ledger|broken:k_study",
                        BrokenBy = new[] { "ch2", "ledger" },
                        Why = "장부에서 와인 열두 병이 '파손'으로 지워졌다. 원고 2장은 집사가 와인을 팔아 노름빚을 갚았다고 말한다.",
                        BreakLines = new[]
                        {
                            "……",
                            "와인 좀 팔았어. 노름빚 갚으려고. 열두 병.",
                            "노아가 알아챘더군. 그런데 날 안 자르더라. 그게 더 비참했어.",
                            "책에 그걸 쓴다니까 눈이 돌았던 거야. 하지만 손은 안 댔어. 맹세해.",
                        },
                        Words = new[] { "s_debt" },
                        Truth = "와인 열두 병을 몰래 팔아 노름빚을 갚았다. 노아는 알았지만 그를 자르지 않았다.",
                        Hint = "돈·와인·창고 이야기에 유난히 예민하다. '와인 셀러는 내 담당'이라며 말을 끊고, 장부 얘기가 나오면 입을 다문다.",
                        Guard = new[] { "노름", "빚을 갚", "내가 팔았", "몰래 팔", "도박" },
                    },
                },
                Topics = new[]
                {
                    new Topic { Id = "k_rel", Label = "노아와의 관계", Lines = new[] { "학교 동창이야. 지금은… 그 자식 집사고.", "한때는 같은 책상에서 시 쓰던 사이였는데. 웃기지." } },
                    new Topic { Id = "k_novel", Label = "원고에 대해", Lines = new[] { "다들 벌벌 떨더군. 자기 얘기가 실렸다고.", "나? …몰라." } },
                    new Topic { Id = "k_house", Label = "저택 구조", Lines = new[] { "이 집 구석구석은 내가 제일 잘 알아.", "서재랑 응접실은 연결문으로 이어져 있어. 온실 나선 계단으로도 서재에 바로 올라갈 수 있고." }, SetFlags = new[] { "knows_door" } },
                    new Topic { Id = "k_sus", Label = "누가 수상해?", Lines = new[] { "의사 양반.", "시신 보자마자 시각이며 흉기며 줄줄 읊더라. 맥도 안 짚고. 너무 침착해서 소름 끼쳤어." } },
                },
                Reactions = new Dictionary<string, string>
                {
                    ["lighter"] = "…내 거야. 어디서 났어.",
                    ["card"] = "미로 놈 카드잖아. 그 녀석 마술할 때 쓰는 거. 별 세 개 찍힌 거.",
                    ["butts"] = "내 담배 맞아. 온실에 있었다고 했잖아.",
                    ["poker"] = "응접실 부지깽이네. 내가 매일 닦는 건데, 오늘은 안 닦았어. 근데 왜 이렇게 깨끗하지?",
                    ["pianola"] = "그 피아노? 롤만 끼우면 혼자 쳐. 노아가 손님 올 때마다 자랑하던 거야.",
                    ["roll"] = "롤이 덜 감겼잖아. 쓰고 나면 꼭 되감아 두라고 그렇게 일렀는데.",
                    ["gauze"] = "저택에 그런 거즈는 없어. 구급상자는 내가 관리하거든. 붕대뿐이야.",
                    ["ledger"] = "…그건.",
                    ["phone"] = "전화선? 저녁 먹기 전까진 멀쩡했어. 내가 식료품점에 전화했었거든.",
                    ["candlestick"] = "은촛대가 하나 없네? 만찬 땐 분명 둘 다 있었는데.",
                    ["photo"] = "…빈 의자네. 그 시각엔 피아노 소리가 났잖아.",
                    ["watch"] = "노아 회중시계. 늘 조끼에 차고 다녔지.",
                },
                DefaultReaction = "몰라. 처음 보는 거야.",
                WrongLines = new[] { "그게 뭐. 그걸로 날 어쩌자고?", "헛다리 짚지 마, 탐정." },
                Facts = "저택의 집사 겸 바텐더이고 노아의 학교 동창이다. 만찬 뒤 식당에서 잔을 닦았다. 22시 35분부터 23시 5분까지 온실에서 담배를 피웠다. 23시 10분엔 혼자 복도를 걷고 있었다. 저택 구조를 잘 안다: 서재와 응접실은 연결문으로 이어지고, 온실에도 서재로 통하는 문이 있다. 비가 퍼부어 온실에선 바깥 소리가 잘 안 들렸다.",
                Persona = "노아와 고등학교 문예반에서 같은 책상에 앉아 시를 쓰던 사이. 시인이 되지 못하고 바텐더로 떠돌다 3년 전부터 이 저택의 집사로 일한다. 친구에게 고용됐다는 게 늘 자존심 상한다. 성미가 급하고 말이 거칠지만 저택 일엔 자부심이 있다 — 방 구조와 와인 셀러를 구석구석 꿰고 있고, 은식기와 잔 개수까지 센다. 손님들 인상: 미로는 손버릇 나쁜 놈이라 오늘도 은식기를 세어 봤다, 제로는 너무 차가워서 소름 끼친다, 하루는 착한 애인데 노아한테 일만 잔뜩 받는다, 세이는 말 없는 사진쟁이인데 노아가 유난히 챙겼다. 오늘 만찬에서 노아가 '이 식탁의 이야기'를 발표할 때 쟁반을 떨어뜨릴 뻔했다. 지금 기분: 화나고, 겁나고, 담배가 당긴다.",
            },
            new Suspect
            {
                Name = "제로", Role = "의사",
                Bio = "노아의 동창이자 의사. 시신을 가장 먼저 살폈다. 늘 침착하다.",
                Greeting = new[] { "의사 제로입니다. 시신은 제가 살폈습니다.", "감정은 수사에 방해가 되죠. 사실만 말씀드리겠습니다." },
                Statements = new[]
                {
                    new Statement
                    {
                        Id = "z_time", Lie = true, Text = "사망 시각은 23시 10분 전후입니다. 회중시계가 그 시각에 멈췄고, 체온도 그 정도입니다.",
                        BrokenBy = new[] { "watch@detail:crown", "t_miro" },
                        NearMiss = "시계는 23시 10분에 멈춰 있습니다. 무엇이 문제입니까?",
                        Why = "회중시계는 스스로 멈춘 게 아니다. 누군가 바늘을 23시 10분에 맞춰 놓았다 — 노아는 그보다 먼저 죽었다.",
                        BreakLines = new[]
                        {
                            "……",
                            "시계가 조작됐다, 이 말씀이군요.",
                            "좋습니다. 그렇다면 득을 보는 사람은 23시 10분에 알리바이가 있는 사람입니다. 식당에 있던 저, 하루 씨, 세이 씨.",
                            "그리고 시신을 '발견'한 건 하루 씨였죠. 생각해 보십시오.",
                        },
                        Truth = "시계가 조작됐다는 것을 반박하지 못했다. 대신 하루를 의심하라고 했다.",
                        Hint = "사망 시각 이야기에는 의사로서 단호하다 — 회중시계와 체온을 근거로 든다. 시계를 만져 봤냐고 물으면 '시신 곁의 물건엔 손대지 않았다'고 짧게 답한다.",
                        Guard = new[] { "바늘을 돌", "용두를 당", "맞춰 놓", "내가 죽였", "제가 죽였", "제가 노아를", "죽였습니다" },
                    },
                    new Statement
                    {
                        Id = "z_weapon", Lie = true, Text = "흉기는 청동 문진입니다. 상처 모양이 일치합니다.",
                        BrokenBy = new[] { "poker@detail:seam", "paperweight@detail:top", "body" },
                        NearMiss = "그게 흉기가 아니라는 근거가 있습니까?",
                        Why = "상처는 길고 가늘다. 문진의 피는 윗면에만 발려 있었고, 진짜 흉기는 씻긴 채 응접실에 돌아가 있었다.",
                        BreakLines = new[] { "…….", "부지깽이라. 응접실은 누구나 드나드는 곳입니다. 저만 거기 있었던 것도 아니고요." },
                        Truth = "흉기가 문진이라는 소견을 지키지 못했다.",
                        Hint = "흉기 이야기에는 문진을 근거로 든다. 다른 흉기 가능성을 물으면 '상처 모양이 말해 준다'며 일축한다.",
                        Guard = new[] { "씻었", "씻어 냈", "닦아 놓" },
                    },
                    new Statement
                    {
                        Id = "z_piano", Lie = true, Text = "22시 40분부터 23시 무렵까지 응접실에서 녹턴을 쳤습니다. 다들 들으셨을 겁니다.",
                        BrokenBy = new[] { "photo@detail:bench", "roll" },
                        Related = new[] { "pianola", "t_piano", "photo" },
                        NearMiss = "피아노에 무슨 장치가 달려 있든, 제가 치지 않았다는 증거가 됩니까?",
                        Why = "22시 45분, 녹턴이 흐르던 그 순간 응접실의 피아노 의자는 비어 있었다. 녹턴은 롤을 건 자동 연주 장치가 쳤다.",
                        BreakLines = new[] { "……", "피아노가 혼자 쳤다는 겁니까.", "…더 드릴 말씀이 없습니다. 변호사 없이는." },
                        SetFlags = new[] { "zero_closed" },
                        Truth = "피아노를 직접 쳤다는 주장이 무너졌다.",
                        Hint = "피아노와 녹턴 이야기가 나오면 연주 실력과 곡 해석을 유난히 길게 늘어놓는다. 피아노에 달린 장치나 롤 이야기는 하지 않는다.",
                        Guard = new[] { "롤을 걸", "롤을 끼", "혼자 연주", "치지 않았", "안 쳤" },
                    },
                    new Statement
                    {
                        Id = "z_secret", Lie = true, Text = "원고에 제 이야기가 있었다 해도, 저는 숨길 것이 없습니다.",
                        ShowIf = "has:manuscript",
                        BrokenBy = new[] { "ch6", "fragment" },
                        Why = "원고 6장 「흰 가운의 침묵」 — 3년 전, 술에 취한 채 집도한 수술로 노아의 여동생 노을이 죽었다.",
                        BreakLines = new[] { "…….", "노을이 일은… 사고였습니다.", "사고였다고요." },
                        Words = new[] { "s_surgery" },
                        Truth = "3년 전 노아의 여동생 노을을 수술하다 잃은 일이 원고 6장에 적혀 있었다.",
                        Hint = "노을 이야기가 나오면 말이 짧아지고 '사고였다'는 말만 되풀이한다. 그날 술을 마셨냐는 질문엔 대답 대신 질문으로 되받는다.",
                        Guard = new[] { "술에 취", "취한 채", "마시고 수술", "음주" },
                    },
                    new Statement { Id = "z_dining", Text = "23시 5분부터는 식당에서 하루 씨, 세이 씨와 함께 있었습니다.", Hears = "t_dining" },
                },
                Topics = new[]
                {
                    new Topic { Id = "z_rel", Label = "노아와의 관계", Lines = new[] { "대학 동기입니다.", "그리고… 3년 전, 그의 여동생 노을을 제가 수술했습니다. 살리지 못했죠. 그 뒤로 조금 서먹했습니다." } },
                    new Topic { Id = "z_body", Label = "시신 소견", Lines = new[] { "뒤통수에 둔기로 맞은 상처 하나. 즉사에 가까웠을 겁니다.", "청동 문진의 피와 상처가 일치합니다. 시계가 멈춘 23시 10분과 체온도 맞고요." } },
                    new Topic { Id = "z_novel", Label = "원고에 대해", Lines = new[] { "노아다운 장난입니다. 우리를 겁주려는.", "저는 원고 내용에 관심 없습니다." } },
                    new Topic { Id = "z_sus", Label = "누가 수상해?", Lines = new[] { "카이 씨가 22시 반에 노아와 크게 다퉜습니다.", "그리고 23시 10분, 카이 씨는 혼자였죠. 저는 사실을 말씀드릴 뿐입니다." } },
                },
                Reactions = new Dictionary<string, string>
                {
                    ["gauze"] = "제가 시신을 살필 때 쓴 겁니다. 의사니까요.",
                    ["poker"] = "부지깽이가 깨끗하다… 그래서요?",
                    ["card"] = "미로 씨의 카드군요. 손버릇이 나쁜 사람입니다.",
                    ["photo"] = "…번개 사진이군요.",
                    ["watch"] = "23시 10분에 멈췄습니다. 쓰러질 때 깨졌겠죠.",
                    ["pianola"] = "노아의 장난감이죠.",
                    ["roll"] = "누가 틀었겠죠. 손님이 많았으니까요.",
                    ["phone"] = "폭풍 때문이겠죠.",
                    ["ch6"] = "…….",
                    ["fragment"] = "…….",
                },
                DefaultReaction = "그게 이 사건과 무슨 관계가 있습니까?",
                WrongLines = new[] { "그게 무슨 증거가 됩니까, 탐정님?", "추측은 추측일 뿐입니다." },
                Facts = "의사이고 노아의 대학 동기다. 시신을 살펴 '사망 23시 10분 전후, 흉기는 청동 문진'이라고 했다. 22시 40분부터 응접실에서 녹턴을 쳤다고 말한다. 23시 5분부터 식당에서 하루, 세이와 함께 있었다. 3년 전 노아의 여동생 노을을 수술했으나 살리지 못했다. 오늘 밤 술은 한 모금도 마시지 않았다.",
                Persona = "노아의 대학 동기이자 외과 의사. 감정을 드러내지 않고 사실과 논리로 말한다. 3년 전 노아의 여동생 노을의 수술을 맡았다가 살리지 못했고, 그 뒤 노아와는 서먹했다. 오늘 초대에 응한 건 '오랜 친구에 대한 예의'라고 말한다. 피아노를 잘 치고 쇼팽 녹턴을 좋아한다. 손님들 인상: 카이는 감정 조절을 못 하는 사람, 미로는 손버릇이 나쁜 사람, 하루는 노아에게 지나치게 헌신적인 사람, 세이는 말이 없어 속을 알 수 없는 사람. 오늘 밤 술은 한 모금도 마시지 않았다. 지금 기분: 흔들리지 않는 척한다.",
                ClosedIf = "zero_closed",
                ClosedLine = "변호사 없이는 더 말하지 않겠습니다.",
            },
            new Suspect
            {
                Name = "미로", Role = "마술사",
                Bio = "무대 마술사. 노아의 동창. 장난이 지나치다.",
                Greeting = new[] { "오, 탐정님 등장~ 타이밍 한번 극적이네.", "난 미로. 마술사야. 사람 눈 속이는 게 직업이지. 아, 오늘은 아니고." },
                Statements = new[]
                {
                    new Statement { Id = "m_alibi", Text = "22시 반부터 23시 가까이 식당에서 혼자 와인 마셨지~ 피아노 소리 들으면서.", Hears = "t_piano" },
                    new Statement
                    {
                        Id = "m_study", Lie = true, Text = "서재? 난 근처에도 안 갔는데?",
                        BrokenBy = new[] { "card", "t_kai_saw" },
                        Why = "서재 책상 밑에 미로의 표식이 찍힌 카드가 떨어져 있었다.",
                        BreakLines = new[]
                        {
                            "…에이, 들켰네.",
                            "23시 정각에 온실 나선 계단으로 올라갔어. 내 장만 슬쩍하려고.",
                            "근데 노아가… 이미 죽어 있더라. 책상에 엎드린 채로. 꼼짝도 안 했어.",
                            "무서워서 내 장만 챙겨서 도망쳤지. 마술사가 시체 앞에서 카드나 떨어뜨리고. 꼴좋다.",
                        },
                        Gives = new[] { "t_miro", "ch3" },
                        Truth = "23시 정각에 온실 나선 계단으로 서재에 올라갔다가, 이미 죽어 있는 노아를 보고 3장만 챙겨 나왔다.",
                        Hint = "서재 이야기엔 농담으로 넘기며 카드 마술을 보여 주겠다고 하다가, 카드 한 장이 모자란다고 투덜댄다. 23시 무렵 이야기가 나오면 말이 빨라진다.",
                        Guard = new[] { "서재에 갔", "서재에 올라갔", "나선 계단으로 올라", "이미 죽어", "죽어 있었", "내 장을", "챙겨 나왔" },
                    },
                    new Statement { Id = "m_2310", Text = "23시 넘어선 부엌에서 혼자 물 마셨어. 와인을 너무 마셔서." },
                    new Statement
                    {
                        Id = "m_candle", Lie = true, Text = "은촛대? 그런 게 있었나?",
                        ShowIf = "has:candlestick",
                        BrokenBy = new[] { "candlestick" },
                        Why = "만찬 때 둘이던 은촛대가 하나 사라졌다. 손버릇 나쁜 마술사가 모를 리 없다.",
                        BreakLines = new[] { "아하하… 감정 좀 해 보려고 빌렸지. 진짜 은인지 궁금하잖아.", "내 가방에 있어. 흉기? 에이, 피 한 방울 안 묻었어. 확인해 봐." },
                        SetFlags = new[] { "candle_found" },
                        Truth = "은촛대는 미로가 가방에 넣어 두었다. 흉기가 아니다.",
                        Hint = "은촛대 이야기엔 '진짜 은은 무게로 안다'며 지나치게 잘 아는 티를 낸다.",
                        Guard = new[] { "가방에", "빌렸", "내가 가져" },
                    },
                    new Statement
                    {
                        Id = "m_secret", Lie = true, Text = "3장? 별거 없어. 그냥 내 무대 얘기야.",
                        ShowIf = "has:ch3",
                        BrokenBy = new[] { "ch3" },
                        Why = "원고 3장 「가짜 거장」 — 마술사는 무대 밖에서도 속였다. 그가 판 그림은 전부 가짜였다.",
                        BreakLines = new[] { "…다 읽었구나.", "그래. 가짜 그림 팔았어. 식당에 걸린 바다 그림도 내 솜씨고.", "노아는 그걸 알면서 웃었대. 잘 그렸다고. …그런 녀석을 내가 왜 죽여." },
                        Words = new[] { "s_forgery" },
                        Truth = "가짜 그림을 팔아 왔다. 노아는 알고도 웃어 넘겼다.",
                        Hint = "그림과 미술 이야기엔 갑자기 진지해지고, 식당의 바다 그림을 유난히 칭찬한다. 3장 내용을 물으면 '무대 이야기'라며 웃어넘긴다.",
                        Guard = new[] { "가짜 그림", "위조", "내가 그렸", "내 솜씨", "가짜를 팔" },
                    },
                },
                Topics = new[]
                {
                    new Topic { Id = "m_rel", Label = "노아와의 관계", Lines = new[] { "학교 때부터 친구지. 내 첫 마술을 보고 웃어 준 녀석이야.", "그 녀석은 늘 이야기를 썼고, 난 늘 속임수를 썼지. 둘 다 거짓말쟁이였던 셈이야." } },
                    new Topic { Id = "m_novel", Label = "원고에 대해", Lines = new[] { "다들 자기 장 찾느라 눈이 벌겋더라고.", "난? 아~무 관심 없어." } },
                    new Topic { Id = "m_sus", Label = "누가 수상해?", Lines = new[] { "하루? 『푸른 등대』 얘기만 나오면 표정이 굳더라고.", "그리고 의사 양반. 저 사람 웃는 걸 3년째 못 봤어." } },
                    new Topic
                    {
                        Id = "m_scene", Label = "서재에서 본 것", ShowIf = "broken:m_study",
                        Lines = new[] { "타자기에 종이가 꽂혀 있었고, 찻잔은 가득 차 있었어.", "시계? 몰라. 시체 앞에서 시계 볼 정신이 어딨어.", "아, 응접실로 통하는 연결문이 살짝 열려 있었어. 피아노 소리는 그때 이미 그쳤고." },
                        SetFlags = new[] { "knows_door" },
                    },
                },
                Reactions = new Dictionary<string, string>
                {
                    ["pianola"] = "자동 피아노? 오~ 마술사들이 좋아하는 장치야. 사람 없이도 연주되거든. 관객은 귀로 믿지.",
                    ["roll"] = "롤이 덜 감겼네. 누가 틀고 나서 대충 꽂아 둔 거야.",
                    ["photo"] = "어? 피아노 의자가 비었는데? 이 시각엔 피아노 소리가 났잖아. 이거 완전 내 전문 분야인데.",
                    ["card"] = "…어디서 났어? 내 카드 같긴 한데~",
                    ["candlestick"] = "은촛대? 몰라~",
                    ["gauze"] = "의료용이네. 이 집에 의사가 한 명 있지, 아마?",
                    ["watch"] = "유리가 깨졌네. 근데 용두가 왜 빠져 있지? 시계 맞출 때나 그렇게 하는데.",
                    ["paperweight"] = "피가 위에만 묻었네. 내리쳤으면 아랫면이 더러워야지. 소품 담당이 대충 했구먼.",
                },
                DefaultReaction = "흠~ 그건 내 전문 분야가 아니라서.",
                WrongLines = new[] { "에이, 탐정님. 그건 마술보다 억지인데?", "꽝~ 다시 해 봐." },
                Facts = "무대 마술사이고 노아의 동창이다. 22시 30분부터 23시 가까이 식당에서 혼자 와인을 마시며 응접실의 피아노 소리를 들었다. 23시 이후엔 부엌에서 혼자 물을 마셨다.",
                Persona = "무대 마술사. 노아와는 학교 때부터 친구 — 노아가 그의 첫 마술을 보고 웃어 준 사람이다. 장난이 심하고 말을 비틀며, 늘 카드나 동전을 손가락 사이로 굴린다. 골동품과 그림 보는 눈이 있다고 자랑한다. 손님들 인상: 카이는 화만 많은 집사, 제로는 3년째 웃는 걸 못 본 의사, 하루는 『푸른 등대』 얘기만 나오면 표정이 굳는 비서, 세이는 사진기 뒤에 숨는 아이. 오늘 만찬 때 카드 마술을 보여 줬다. 지금 기분: 무서운데 티 안 내려고 농담을 한다.",
            },
            new Suspect
            {
                Name = "하루", Role = "비서 겸 타자수",
                Bio = "노아의 원고를 3년째 타자로 옮겨 온 비서. 시신을 처음 발견했다.",
                Greeting = new[] { "탐, 탐정님… 제가… 제가 노아 씨를 발견했어요.", "차를 새로 내 가려고 했는데… 문을 열었더니…" },
                Statements = new[]
                {
                    new Statement { Id = "h_tea", Text = "22시 35분에 노아 씨께 차를 갖다드렸어요. 그땐 타자를 치고 계셨어요.", Hears = "t_tea" },
                    new Statement { Id = "h_shout", Text = "그 직전, 22시 반쯤 서재에서 고함이 들렸어요. 카이 씨 목소리였어요.", Hears = "t_shout" },
                    new Statement { Id = "h_piano", Text = "차를 들고 응접실을 지날 때 제로 씨가 피아노 앞에 계셨어요. 곧 녹턴을 치신다고… 정말 22시 40분부터 들렸어요.", Hears = "t_piano" },
                    new Statement { Id = "h_found", Text = "23시 30분에 새 차를 가져갔다가… 노아 씨를…", Hears = "t_found" },
                    new Statement
                    {
                        Id = "h_secret", Lie = true, Text = "노아 씨 소설이요? 저는… 타자만 쳐 드렸어요.",
                        BrokenBy = new[] { "ch4" },
                        Why = "원고 4장 「그림자 작가」 — 『푸른 등대』는 열아홉 살 소녀가 쓴 이야기였다.",
                        BreakLines = new[]
                        {
                            "…….",
                            "『푸른 등대』는… 제가 쓴 거예요. 열아홉 살 때, 공책에.",
                            "노아 씨가 자기 이름으로 냈어요. 언젠가 바로잡겠다고… 3년을 기다렸어요.",
                            "그런데 오늘 저녁에 그러셨어요. '하루야, 자정에 다 돌려줄게.' …무슨 뜻인지 그땐 몰랐어요.",
                        },
                        Words = new[] { "s_ghost" },
                        SetFlags = new[] { "midnight_hint" },
                        Truth = "『푸른 등대』의 진짜 작가다. 노아가 오늘 저녁 '자정에 다 돌려주겠다'고 했다.",
                        Hint = "노아의 글이나 『푸른 등대』 이야기가 나오면 눈을 피하고 목소리가 작아진다. '노아 씨는… 남의 문장도 자기 것처럼 아끼셨어요' 같은 서운함이 새어 나오지만, 무슨 뜻이냐고 물으면 '원고를… 끝까지 읽어 보시면 아실지도요'라며 말을 흐린다.",
                        Guard = new[] { "제가 쓴", "제가 썼", "내가 쓴", "내가 썼", "대필", "훔쳐", "자정에 다 돌려", "제 소설" },
                    },
                },
                Topics = new[]
                {
                    new Topic { Id = "h_rel", Label = "노아와의 관계", Lines = new[] { "비서예요. 3년째 노아 씨 원고를 타자로 옮겨 드렸어요.", "…좋은 분이었어요. 대부분은요." } },
                    new Topic { Id = "h_novel", Label = "원고에 대해", Lines = new[] { "오늘 아침에 6장까지 다 치셨대요. 7장은 오늘 밤에 쓰신다고, 서재에 아무도 들이지 말라고 하셨어요.", "그런데 카이 씨가 막무가내로…" } },
                    new Topic { Id = "h_sus", Label = "누가 수상해?", Lines = new[] { "몰, 몰라요… 그냥 무서워요.", "카이 씨가 그렇게 소리 지르는 건 처음 들었어요." } },
                    new Topic { Id = "h_zero", Label = "식당의 제로", ShowIf = "broken:z_time", Lines = new[] { "제로 씨요? 23시 5분쯤 식당에 오셨어요.", "손이 아주 차가웠어요. 손을 씻고 오셨다고… 그때는 그냥 의사라서 그런가 했어요." } },
                },
                Reactions = new Dictionary<string, string>
                {
                    ["photo"] = "어머… 피아노 의자가 비어 있네요. 그때 분명 녹턴이 들렸는데…",
                    ["teacup"] = "제가 드린 차예요… 한 모금도 안 드셨네요. 그럼 제가 나오고 얼마 안 돼서…",
                    ["typewriter"] = "7장… 처음 보는 거예요. '이 원고를 ㅌ'… 뭘 하려고 하셨던 걸까요.",
                    ["gauze"] = "저택 물건이 아니에요. 구급상자엔 붕대랑 소독약뿐이에요.",
                    ["pianola"] = "자동 피아노요? 노아 씨가 손님들 오시면 자랑하던 거예요. 롤만 끼우면 혼자 친다고.",
                    ["lighter"] = "카이 씨 라이터예요. 늘 조끼 주머니에 넣고 다녀요.",
                    ["card"] = "미로 씨 카드 같아요. 저녁 식사 때 그걸로 마술을 보여 주셨어요.",
                    ["poker"] = "응접실 부지깽이예요. 왜 이렇게 깨끗하죠? 카이 씨가 오늘은 벽난로 청소를 안 했다던데…",
                    ["phone"] = "전화가 안 돼요… 그래서 경찰을 못 불렀어요.",
                    ["roll"] = "녹턴 롤이네요. 노아 씨가 제일 좋아하던 곡이에요.",
                    ["ch4"] = "그, 그건…",
                },
                DefaultReaction = "그건… 잘 모르겠어요.",
                WrongLines = new[] { "그, 그게 무슨 상관이에요…?", "저, 저는 잘 모르겠어요…" },
                Facts = "노아의 비서 겸 타자수. 22시 30분쯤 서재에서 카이의 고함을 들었다. 22시 35분에 서재로 차를 가져갔고 노아는 타자를 치고 있었다. 응접실을 지날 때 피아노 앞의 제로를 봤고, 22시 40분부터 녹턴을 들었다. 23시부터 식당에서 디저트 와인을 준비했고 23시 5분부터 제로, 세이와 함께 있었다. 23시 30분에 새 차를 가져갔다가 시신을 발견했다.",
                Persona = "노아의 비서 겸 타자수. 3년째 노아의 원고를 타자로 옮기며 누구보다 먼저 읽는다. 차를 내는 것도 하루 몫이다. 노아를 존경하지만 서운한 것도 많다. 소심하고 말을 더듬지만 원고와 문장 이야기엔 눈이 반짝인다. 시신을 처음 발견한 충격이 아직 가시지 않았다. 손님들 인상: 카이는 무섭지만 나쁜 사람은 아닌 것 같다, 제로는 너무 침착해서 오히려 무섭다, 미로는 장난이 지나치다, 세이는 조용하지만 노아 씨가 특별히 아꼈다. 지금 기분: 무섭고, 슬프고, 혼란스럽다.",
            },
            new Suspect
            {
                Name = "세이", Role = "사진가",
                Bio = "사진가. 말수가 적고 늘 카메라를 든다.",
                Greeting = new[] { "세이. 사진가.", "…물어봐." },
                Statements = new[]
                {
                    new Statement
                    {
                        Id = "s_hall", Lie = true, Text = "22시 40분부터 현관홀. 폭풍 찍었어. 아무것도 못 봤어.",
                        BrokenBy = new[] { "t_miro", "watch@detail:crown" },
                        NearMiss = "시계가 왜.",
                        Why = "노아는 23시 이전에 이미 죽어 있었다. 그 시각 세이는 응접실 바로 옆, 현관홀에 있었다.",
                        BreakLines = new[] { "……", "그럼 그 소리.", "22시 45분, 괘종 칠 때. 위층에서 쿵.", "마침 셔터를 눌렀어. 번개 칠 때. …이거." },
                        Gives = new[] { "t_thud", "photo" },
                        Truth = "22시 45분, 괘종이 칠 때 위층에서 쿵 소리를 들었고 그때 사진을 찍었다.",
                        Hint = "홀에서 찍은 사진 이야기가 나오면 '현상해 봐야 안다'며 말을 아낀다. 괘종과 번개 이야기엔 잠깐 반응한다.",
                        Guard = new[] { "쿵", "위층에서 소리", "떨어지는 소리", "쓰러지는 소리" },
                    },
                    new Statement
                    {
                        Id = "s_hall2", Lie = true, Text = "그 뒤론 아무도 못 봤어.",
                        ShowIf = "broken:s_hall",
                        BrokenBy = new[] { "gauze" },
                        Why = "세탁실은 현관홀 바로 옆이다. 누군가 그 대야에서 무언가를 씻었다면, 홀에 있던 세이가 못 봤을 리 없다.",
                        BreakLines = new[] { "…세탁실.", "22시 55분. 제로가 나왔어. 소매가 젖어 있었고.", "손 씻었나 했어. 관심 없었어." },
                        Gives = new[] { "t_laundry" },
                        Truth = "22시 55분에 제로가 소매가 젖은 채 세탁실에서 나오는 걸 봤다.",
                        Hint = "세탁실 이야기엔 고개를 젓는다. 누구를 봤냐는 질문엔 '관심 없었어'라고만 한다.",
                        Guard = new[] { "제로가 나왔", "소매", "젖어 있" },
                    },
                    new Statement { Id = "s_dining", Text = "22시 57분부터 식당. 하루랑 제로가 왔어.", Hears = "t_dining" },
                    new Statement
                    {
                        Id = "s_secret", Lie = true, Text = "노아? 그냥 아는 사이.",
                        BrokenBy = new[] { "ch5" },
                        Why = "원고 5장 「다락방의 아이」 — 노아의 아버지에게는 또 다른 딸이 있었다. 늘 카메라 뒤에 숨는 아이.",
                        BreakLines = new[] { "……", "배다른 남매. 아버지가 같아.", "자정에 다들 앞에서 말해 준다고 했어. 나도 가족이라고.", "…그게 다야." },
                        Words = new[] { "s_sibling" },
                        SetFlags = new[] { "midnight_hint" },
                        Truth = "노아의 배다른 여동생이다. 노아가 자정에 모두 앞에서 가족이라고 밝히겠다고 했다.",
                        Hint = "노아 이야기엔 '아는 사이'라고만 하지만, 노아의 어린 시절이나 가족 이야기가 나오면 잠깐 말이 길어졌다가 뚝 끊는다. 원고 5장을 물으면 '읽어 봐. 거기 있어.'라고만 한다.",
                        Guard = new[] { "배다른", "남매", "오빠", "같은 아버지", "가족이라" },
                    },
                },
                Topics = new[]
                {
                    new Topic { Id = "s_rel", Label = "노아와의 관계", Lines = new[] { "아는 사이." } },
                    new Topic { Id = "s_novel", Label = "원고에 대해", Lines = new[] { "다들 무서워해.", "난 아니야." } },
                    new Topic { Id = "s_sus", Label = "누가 수상해?", Lines = new[] { "의사.", "시신 앞에서 맥도 안 짚었어. 시계부터 봤지." } },
                    new Topic { Id = "s_photo", Label = "찍은 사진", ShowIf = "!broken:s_hall", Lines = new[] { "폭풍. 번개. 그런 거.", "…보여 줄 건 없어." } },
                },
                Reactions = new Dictionary<string, string>
                {
                    ["pianola"] = "…자동 피아노.",
                    ["photo"] = "응접실 문이 열려 있었어. 의자는… 비어 있네.",
                    ["watch"] = "용두가 빠져 있어. 시계 맞출 때처럼.",
                    ["card"] = "미로 거.",
                    ["lighter"] = "카이 거.",
                    ["gauze"] = "…….",
                    ["grandclock"] = "괘종 소리. 저택 어디서든 들려.",
                },
                DefaultReaction = "몰라.",
                WrongLines = new[] { "…아니야.", "그거 말고." },
                Facts = "사진가. 22시 40분부터 현관홀에서 폭풍 사진을 찍었다고 한다. 22시 57분부터 식당에서 하루, 제로와 함께 있었다. 말수가 아주 적다.",
                Persona = "사진가. 말수가 아주 적고 늘 카메라를 목에 건다. 번개나 폭풍 같은 순간을 찍는 걸 좋아하고, 사람보다 빛과 그림자를 본다. 노아와는 몇 년 전부터 아는 사이라고만 말한다. 손님들 인상: 카이는 시끄럽다, 제로는 시신 앞에서 시계부터 봤다, 미로는 손이 빠르다, 하루는 잘 운다. 단답이 버릇이지만 사진·빛·폭풍 이야기엔 한두 마디 더 한다. 지금 기분: 드러내지 않는다.",
            },
        };

        // ───────────────────────── 의문점 ─────────────────────────

        static Question[] BuildQuestions() => new[]
        {
            new Question
            {
                Id = "q_time", Text = "노아는 정말 23시 10분에 죽었나?", OpenIf = "",
                Hint = "회중시계를 확대해서 들여다보자. 23시 10분보다 먼저 서재에 들어간 사람은 없었을까?",
                ResolveIf = "broken:z_time", Answer = "회중시계는 조작됐다. 노아는 23시 이전에 이미 죽어 있었다.",
            },
            new Question
            {
                Id = "q_weapon", Text = "흉기는 정말 청동 문진인가?", OpenIf = "has:paperweight|has:body",
                Hint = "상처 모양과 문진에 묻은 피를 비교해 보자. 머리를 내리칠 만한 물건이 또 있다.",
                ResolveIf = "broken:z_weapon", Answer = "진짜 흉기는 씻긴 채 응접실에 돌아가 있던 부지깽이다.",
            },
            new Question
            {
                Id = "q_shout", Text = "22시 30분, 서재에서 노아와 다툰 사람은?", OpenIf = "has:t_shout",
                Hint = "서재 바닥에 떨어진 물건의 주인을 찾아보자.",
                ResolveIf = "broken:k_study", Answer = "카이였다. 하지만 22시 35분에 나갈 때 노아는 살아 있었다.",
            },
            new Question
            {
                Id = "q_missing", Text = "사라진 원고 3장과 6장은 어디에?", OpenIf = "has:manuscript",
                Hint = "자기 장을 몰래 가져가고 싶었던 사람이 있다. 범인이라면 자기 장을 어떻게 처리했을까?",
                ResolveIf = "has:ch3,has:ch6|has:fragment", Answer = "3장은 미로가, 6장은 범인이 가져갔다.",
            },
            new Question
            {
                Id = "q_piano", Text = "22시 40분의 녹턴 — 정말 제로가 쳤나?", OpenIf = "has:t_piano",
                Hint = "응접실의 피아노를 살펴보자. 그 시각 응접실을 본 사람이나 사진은 없을까?",
                ResolveIf = "broken:z_piano", Answer = "녹턴은 자동 연주 장치가 쳤다. 피아노 의자는 비어 있었다.",
            },
            new Question
            {
                Id = "q_gauze", Text = "세탁실의 의료용 거즈는 누가 썼나?", OpenIf = "has:gauze",
                Hint = "세탁실은 현관홀 바로 옆이다. 그 시각 현관홀에 있던 사람이라면…",
                ResolveIf = "has:t_laundry|has:notebook", Answer = "22시 55분, 제로가 젖은 소매로 세탁실에서 나왔다.",
            },
            new Question
            {
                Id = "q_midnight", Text = "노아는 자정에 무엇을 하려 했나? (「이 원고를 ㅌ—」)", OpenIf = "has:typewriter",
                Hint = "노아와 가까웠던 사람이 오늘 저녁 무언가 들었을지도 모른다.",
                ResolveIf = "midnight_hint", Answer = "노아는 자정에 '다 돌려주겠다'고 했다. 원고를 어떻게 하려 했던 걸까.",
            },
            new Question
            {
                Id = "q_phone", Text = "전화선은 누가 잘랐나?", OpenIf = "has:phone",
                Hint = "절단면이 매끈하다. 아주 잘 드는 가위를 가진 사람.",
                ResolveIf = "solved", Answer = "제로가 만찬 전에 수술 가위로 잘랐다.",
            },
        };

        // ───────────────────────── 재구성 단어 ─────────────────────────

        static Word[] BuildWords()
        {
            var list = new List<Word>
            {
                W("p_kai", "카이", WordKind.Person, ""), W("p_zero", "제로", WordKind.Person, ""),
                W("p_miro", "미로", WordKind.Person, ""), W("p_haru", "하루", WordKind.Person, ""),
                W("p_sei", "세이", WordKind.Person, ""),

                W("t2230", "22시 30분", WordKind.Time), W("t2235", "22시 35분", WordKind.Time),
                W("t2240", "22시 40분", WordKind.Time), W("t2245", "22시 45분", WordKind.Time),
                W("t2255", "22시 55분", WordKind.Time), W("t2300", "23시", WordKind.Time),
                W("t2310", "23시 10분", WordKind.Time, ""), W("t2330", "23시 30분", WordKind.Time, ""),

                W("o_paperweight", "청동 문진", WordKind.Object, ""), W("o_poker", "부지깽이", WordKind.Object),
                W("o_candle", "은촛대", WordKind.Object), W("o_knife", "식칼", WordKind.Object),
                W("o_iron", "다리미", WordKind.Object), W("o_hammer", "망치", WordKind.Object),
                W("o_shears", "전지가위", WordKind.Object),

                W("d_watch", "회중시계 바늘 돌리기", WordKind.Deception), W("d_blood", "문진에 피 묻히기", WordKind.Deception),
                W("d_pianola", "자동 연주 피아노", WordKind.Deception), W("d_phone", "전화선 끊기", WordKind.Deception),
                W("d_clock", "괘종시계 조작", WordKind.Deception),

                W("r_study", "서재", WordKind.Place, ""), W("r_drawing", "응접실", WordKind.Place, ""),
                W("r_dining", "식당", WordKind.Place, ""), W("r_greenhouse", "온실", WordKind.Place, ""),
                W("r_hall", "현관홀", WordKind.Place, ""), W("r_kitchen", "부엌", WordKind.Place, ""),
                W("r_laundry", "세탁실", WordKind.Place, ""), W("r_storage", "창고", WordKind.Place, ""),

                W("s_debt", "도박 빚", WordKind.Secret), W("s_forgery", "가짜 그림", WordKind.Secret),
                W("s_ghost", "대필", WordKind.Secret), W("s_sibling", "숨겨진 혈육", WordKind.Secret),
                W("s_surgery", "수술 사고", WordKind.Secret),
            };
            return list.ToArray();
        }

        static Word W(string id, string label, WordKind kind, string always = null) =>
            new Word { Id = id, Label = label, Kind = kind, AlwaysIf = always };

        // ───────────────────────── 조사 지점 도우미 ─────────────────────────

        static Hotspot H(string id, string label, float x, float y, float w, float h, string gives, string text,
            string[] flags = null, string[] words = null) => new Hotspot
        {
            Id = id, Label = label, X = x, Y = y, W = w, H = h, Gives = gives, Text = text,
            SetFlags = flags ?? new string[0], Words = words ?? new string[0],
        };

        static Hotspot HIf(string id, string label, float x, float y, float w, float h, string gives, string showIf, string text) => new Hotspot
        {
            Id = id, Label = label, X = x, Y = y, W = w, H = h, Gives = gives, ShowIf = showIf, Text = text,
        };
    }
}
