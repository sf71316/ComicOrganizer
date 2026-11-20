using System.Collections.Generic;
using System.Threading.Tasks;

namespace ComicOrganizer.App.Core;

public interface ITranslationService
{
    /// <summary>
    /// 批次將多個句子從指定來源語言翻譯為目標語言。
    /// </summary>
    /// <param name="texts">待翻譯的句子清單</param>
    /// <param name="fromLanguage">來源語言代碼 (預設 "ja")</param>
    /// <param name="toLanguage">目標語言代碼 (預設 "zh-Hant")</param>
    /// <returns>翻譯後的句子清單，順序與輸入相同；若發生錯誤則回傳原始文字。</returns>
    Task<List<string>> TranslateBatchAsync(List<string> texts, string fromLanguage = "ja", string toLanguage = "zh-Hant");
}
