using System;
using System.Collections.Generic;
using UnityEngine;

namespace MafiaAI.LLM
{
    /// <summary>AI가 고른 대상 + 이유.</summary>
    public struct ActionChoice
    {
        public string TargetId;
        public string Reason;
        public bool Valid => !string.IsNullOrEmpty(TargetId);
    }

    /// <summary>구조화 출력(JSON) 파싱 + 이름 매칭 폴백.</summary>
    public static class ActionParser
    {
        [Serializable]
        class TargetChoiceDto
        {
            public string target;
            public string reason;
        }

        /// <summary>
        /// raw 응답에서 target/reason 추출. 순서:
        /// (1) JSON 파싱 → (2) 유효 이름 매칭 → (3) raw 내 이름 문자열 스캔.
        /// 모두 실패 시 Valid=false 반환(호출측이 랜덤 폴백).
        /// </summary>
        public static ActionChoice Parse(string raw, IReadOnlyList<string> validIds)
        {
            var choice = new ActionChoice();
            if (string.IsNullOrWhiteSpace(raw)) return choice;

            // (1) JSON 오브젝트만 잘라 파싱
            int open = raw.IndexOf('{');
            int close = raw.LastIndexOf('}');
            if (open >= 0 && close > open)
            {
                string json = raw.Substring(open, close - open + 1);
                try
                {
                    var dto = JsonUtility.FromJson<TargetChoiceDto>(json);
                    if (dto != null)
                    {
                        choice.Reason = dto.reason;
                        string matched = MatchName(dto.target, validIds);
                        if (matched != null) { choice.TargetId = matched; return choice; }
                    }
                }
                catch { /* 폴백으로 진행 */ }
            }

            // (2)(3) raw 전체에서 유효 이름 스캔
            string scanned = ScanForName(raw, validIds);
            if (scanned != null) choice.TargetId = scanned;
            return choice;
        }

        static string MatchName(string candidate, IReadOnlyList<string> validIds)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return null;
            candidate = candidate.Trim();
            foreach (var id in validIds)
                if (string.Equals(id, candidate, StringComparison.OrdinalIgnoreCase)) return id;
            foreach (var id in validIds)
                if (candidate.Contains(id)) return id;
            return null;
        }

        static string ScanForName(string raw, IReadOnlyList<string> validIds)
        {
            foreach (var id in validIds)
                if (raw.Contains(id)) return id;
            return null;
        }
    }
}
