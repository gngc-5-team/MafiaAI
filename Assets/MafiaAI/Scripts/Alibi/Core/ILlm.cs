using System.Threading;
using System.Threading.Tasks;

namespace MafiaAI.Alibi
{
    /// <summary>
    /// LLM 호출 창구. 유니티에선 OllamaClient(UnityWebRequest) 어댑터가, 오프라인 하네스에선 HttpClient 어댑터가 구현한다.
    /// 엔진은 이 인터페이스만 알아서, 같은 코드를 에디터 없이 검증할 수 있다.
    /// </summary>
    public interface ILlm
    {
        Task<string> GenerateAsync(string system, string prompt, float temperature, bool json, int maxTokens, CancellationToken ct);
    }
}
