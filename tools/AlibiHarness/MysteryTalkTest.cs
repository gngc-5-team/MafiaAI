using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MafiaAI.Mystery;

namespace AlibiHarness
{
    /// <summary>
    /// 자유 질문 대화 점검(실제 Ollama):  dotnet run -- talk [model=...]
    /// 게임과 같은 MysteryTalk로, 이어지는 질문을 던져 '대화가 되는지'(앞말을 받는지 · 감춘 게 새지 않는지 · 너무 짧지 않은지)를 본다.
    /// </summary>
    public static class MysteryTalkTest
    {
        public static int Run(string[] args)
        {
            string model = args.FirstOrDefault(a => a.StartsWith("model="))?.Substring(6) ?? "gemma4:e4b-it-qat";
            var llm = new HttpOllama(model);
            var scripts = new (string who, string room, string[] topics, string[] questions)[]
            {
                ("하루", "hall", new[] { "h_rel" }, new[] { "대부분? 그게 무슨 뜻입니까?", "노아 씨가 당신한테 뭘 가져갔다는 거죠?", "『푸른 등대』는 어떤 책입니까?", "오늘 밤 노아 씨 기분은 어땠나요?" }),
                ("카이", "kitchen", new[] { "k_rel" }, new[] { "시는 왜 그만뒀어?", "노아랑 마지막으로 이야기한 게 언제야?", "서재에 간 적 정말 없어?", "미로는 어떤 사람이야?" }),
                ("미로", "dining", new string[0], new[] { "카드 마술 하나 보여 줄 수 있어?", "노아랑은 어떻게 친해졌어?", "그 바다 그림 어떻게 생각해?", "23시쯤엔 어디 있었어?" }),
                ("제로", "study", new[] { "z_rel" }, new[] { "노을 씨 수술은 어떻게 된 일입니까?", "그날 술을 드셨습니까?", "피아노는 언제부터 치셨죠?", "노아 씨를 마지막으로 본 게 언제입니까?" }),
                ("세이", "hall", new string[0], new[] { "무슨 사진을 찍었어?", "노아랑은 어떻게 아는 사이야?", "노아는 어떤 사람이었어?", "번개 칠 때 뭐 봤어?" }),
            };
            int leaks = 0, shorts = 0, deflects = 0, total = 0;
            var sw = Stopwatch.StartNew();
            foreach (var (who, room, topics, questions) in scripts)
            {
                var s = new MysterySession(Case01.Build()) { RequirePresence = false };
                s.Meet(who);
                foreach (var t in topics) s.Ask(who, t);
                Console.WriteLine($"\n== {who}");
                foreach (var l in MysteryTalk.History(s, who)) Console.WriteLine($"   ({l.Speaker}) {l.Text}");
                foreach (var q in questions)
                {
                    var t0 = sw.ElapsedMilliseconds;
                    string a = MysteryTalk.AnswerAsync(llm, s, who, q, CancellationToken.None).GetAwaiter().GetResult();
                    s.RecordFreeQuestion(who, q, a);
                    total++;
                    bool deflect = a == MysteryTalk.Deflect(who);
                    if (deflect) deflects++;
                    var guards = s.Case.SuspectByName(who).Statements.Where(st => st.Lie && !s.Broken(st)).SelectMany(st => st.Guard).ToArray();
                    var leak = guards.FirstOrDefault(g => a.Contains(g));
                    if (leak != null) leaks++;
                    if (a.Count(ch => ch >= '가' && ch <= '힣') < 8) shorts++;
                    Console.WriteLine($"  탐정: {q}\n  {who}: {a}   [{sw.ElapsedMilliseconds - t0}ms{(deflect ? " · 얼버무림" : "")}{(leak != null ? " · 샘! " + leak : "")}]");
                }
            }
            Console.WriteLine($"\n질문 {total} · 새어 나감 {leaks} · 너무 짧음 {shorts} · 얼버무림 대체 {deflects} · LLM 호출 {llm.Calls}회 평균 {(llm.Calls > 0 ? llm.TotalMs / llm.Calls : 0)}ms");
            return leaks == 0 ? 0 : 1;
        }
    }
}
