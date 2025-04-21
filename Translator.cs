using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class MyMemoryTranslator
{
    private static readonly HttpClient _httpClient = new HttpClient();

    public static async Task<string> TranslateWithMyMemoryAsync(
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

            string translatedText = json["responseData"]?["translatedText"]?.ToString() ?? text;
            return translatedText;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка перевода: {ex.Message}");
            return text; 
        }
    }
}