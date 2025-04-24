using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Server;
public class MyMemoryTranslator : ITranslator
{
    private static readonly HttpClient _httpClient = new HttpClient();

    public async Task<string> TranslateWithMyMemoryAsync(
        string text,
        string sourceLang = "ru",
        string targetLang = "en")
    {
        string url = "https://api.mymemory.translated.net/get";
        string query = $"?q={Uri.EscapeDataString(text)}&langpair={sourceLang}|{targetLang}";

        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync(url + query);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();
            JObject json = JObject.Parse(responseBody);

            return json["responseData"]?["translatedText"]?.ToString() ?? text;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"������ ��������: {ex.Message}");
            return text;
        }
    }

    public async Task<List<string>> TranslateIngredientsAsync(List<string> russianIngredients,
        Action onError = null)
    {
        var translatedIngredients = new List<string>(russianIngredients.Count);
        try
        {
            Console.WriteLine($"Translating ingredients: {string.Join(", ", russianIngredients)}");
            foreach (var ingredient in russianIngredients)
            {
                var translated = await TranslateWithMyMemoryAsync(ingredient);
                if (!string.IsNullOrWhiteSpace(translated))
                {
                    translatedIngredients.Add(translated.ToLowerInvariant());
                }
                else
                {
                    Console.WriteLine($"Warning: Translation failed or returned empty for '{ingredient}'");
                }
            }

            Console.WriteLine($"Translated ingredients: {string.Join(", ", translatedIngredients)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during translation: {ex.Message}");
            onError?.Invoke();
        }

        return translatedIngredients;
    }


    public async Task<object> TranslateValueAsync(object value, string sourceLang, string targetLang)
    {
        if (value is string originalString)
        {
            return await TranslateWithMyMemoryAsync(originalString, sourceLang, targetLang);
        }
        else if (value is string[] originalArray)
        {
            var translatedList = new List<string>(originalArray.Length);
            foreach (var item in originalArray)
            {
                translatedList.Add(await TranslateWithMyMemoryAsync(item, sourceLang, targetLang));
            }

            return translatedList.ToArray();
        }
        else if (value is List<string> originalList)
        {
            var translatedList = new List<string>(originalList.Count);
            foreach (var item in originalList)
            {
                translatedList.Add(await TranslateWithMyMemoryAsync(item, sourceLang, targetLang));
            }

            return translatedList;
        }

        return value;
    }
}