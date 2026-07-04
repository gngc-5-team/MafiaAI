using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MafiaAI.LLM
{
    /// <summary>
    /// 로컬 Ollama(gemma4:latest 등)를 UnityWebRequest로 호출하는 얇은 클라이언트.
    /// 발언(자유 생성)과 행동(JSON 강제)을 모두 지원한다.
    /// </summary>
    public class OllamaClient
    {
        readonly string _baseUrl;

        public OllamaClient(string baseUrl = "http://localhost:11434")
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }

        [Serializable]
        class GenerateRequest
        {
            public string model;
            public string prompt;
            public string system;
            public bool stream;
            public string format;   // "json" 이면 구조화 출력 강제, "" 이면 자유 생성
            public Options options;
        }

        [Serializable]
        class Options
        {
            public float temperature;
            public int num_predict;
        }

        [Serializable]
        class GenerateResponse
        {
            public string response;
            public bool done;
        }

        /// <summary>
        /// 한 번의 생성 호출. jsonFormat=true 이면 Ollama에게 JSON 출력만 하도록 강제한다.
        /// </summary>
        public async Task<string> GenerateAsync(
            string model,
            string prompt,
            string system = null,
            float temperature = 0.8f,
            bool jsonFormat = false,
            CancellationToken ct = default,
            int maxTokens = 100)
        {
            // 프롬프트가 "2문장 이내"/"한 문장만"으로 답을 제한해도 Ollama 자체엔 길이 제한이 없어서
            // 모델이 그보다 훨씬 길게 계속 생성하고, 우리는 OneSentence()로 첫 문장만 잘라 쓰고 나머지는 버렸다.
            // num_predict로 딱 그만큼만 생성하게 막아서, 버려질 뒷부분을 기다리는 시간을 없앤다.
            var payload = new GenerateRequest
            {
                model = model,
                prompt = prompt,
                system = system ?? string.Empty,
                stream = false,
                format = jsonFormat ? "json" : string.Empty,
                options = new Options { temperature = temperature, num_predict = maxTokens }
            };

            string body = JsonUtility.ToJson(payload);

            using var www = new UnityWebRequest($"{_baseUrl}/api/generate", "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            www.SetRequestHeader("Content-Type", "application/json");

            var op = www.SendWebRequest();
            while (!op.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            if (www.result != UnityWebRequest.Result.Success)
            {
                throw new Exception(
                    $"[Ollama] 요청 실패: {www.error}\n응답본문: {www.downloadHandler.text}");
            }

            var resp = JsonUtility.FromJson<GenerateResponse>(www.downloadHandler.text);
            return resp != null ? resp.response?.Trim() : string.Empty;
        }

        /// <summary>서버가 살아있고 모델 목록을 반환하는지 확인.</summary>
        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            using var www = UnityWebRequest.Get($"{_baseUrl}/api/tags");
            var op = www.SendWebRequest();
            while (!op.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            return www.result == UnityWebRequest.Result.Success;
        }
    }
}
