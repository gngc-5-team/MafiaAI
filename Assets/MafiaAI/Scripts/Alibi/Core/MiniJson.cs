using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MafiaAI.Alibi
{
    /// <summary>
    /// LLM이 낸 JSON을 읽는 최소 파서(UnityEngine 비의존 — 오프라인 하네스에서도 같은 코드로 검증한다).
    /// 객체·배열·문자열·숫자·불·null만 다룬다. 앞뒤 잡문이 붙어도 첫 '{'부터 짝이 맞는 '}'까지만 읽는다.
    /// </summary>
    public static class MiniJson
    {
        public static Dictionary<string, object> ParseObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int start = text.IndexOf('{');
            if (start < 0) return null;
            int i = start;
            try
            {
                return ReadValue(text, ref i) as Dictionary<string, object>;
            }
            catch
            {
                return null;
            }
        }

        static object ReadValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new System.FormatException();
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i);
            if (c == '[') return ReadArray(s, ref i);
            if (c == '"') return ReadString(s, ref i);
            if (c == 't' && Match(s, i, "true")) { i += 4; return true; }
            if (c == 'f' && Match(s, i, "false")) { i += 5; return false; }
            if (c == 'n' && Match(s, i, "null")) { i += 4; return null; }
            return ReadNumber(s, ref i);
        }

        static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var obj = new Dictionary<string, object>();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return obj; }
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                string key = ReadString(s, ref i);
                SkipWs(s, ref i);
                if (s[i] != ':') throw new System.FormatException();
                i++;
                obj[key] = ReadValue(s, ref i);
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return obj; }
                throw new System.FormatException();
            }
            throw new System.FormatException();
        }

        static List<object> ReadArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (i < s.Length)
            {
                list.Add(ReadValue(s, ref i));
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new System.FormatException();
            }
            throw new System.FormatException();
        }

        static string ReadString(string s, ref int i)
        {
            if (s[i] != '"') throw new System.FormatException();
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': break;
                        case 'u':
                            if (i + 4 <= s.Length && int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            throw new System.FormatException();
        }

        static object ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw new System.FormatException();
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static bool Match(string s, int i, string word) =>
            i + word.Length <= s.Length && string.CompareOrdinal(s, i, word, 0, word.Length) == 0;

        // ── 꺼내기 도우미 ──
        public static string Str(Dictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) && v is string s ? s.Trim() : null;

        public static bool Bool(Dictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) && v is bool b && b;

        public static List<string> StrList(Dictionary<string, object> o, string key)
        {
            var result = new List<string>();
            if (o == null || !o.TryGetValue(key, out var v)) return result;
            if (v is List<object> list)
                foreach (var x in list) if (x is string s && s.Trim().Length > 0) result.Add(s.Trim());
            if (v is string single && single.Trim().Length > 0) result.Add(single.Trim());
            return result;
        }
    }
}
