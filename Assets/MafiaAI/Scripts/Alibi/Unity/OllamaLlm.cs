using System.Threading;
using System.Threading.Tasks;
using MafiaAI.LLM;

namespace MafiaAI.Alibi.UI
{
    /// <summary>알리바이 엔진의 ILlm을 기존 OllamaClient(UnityWebRequest, think:false, keep_alive)로 연결한다.</summary>
    public sealed class OllamaLlm : ILlm
    {
        readonly OllamaClient _client;
        readonly string _model;

        public OllamaLlm(GameConfig config)
        {
            _client = new OllamaClient(config.BaseUrl);
            _model = config.Model;
        }

        public Task<string> GenerateAsync(string system, string prompt, float temperature, bool json, int maxTokens, CancellationToken ct) =>
            _client.GenerateAsync(_model, prompt, system, temperature, json, ct, maxTokens);
    }
}
