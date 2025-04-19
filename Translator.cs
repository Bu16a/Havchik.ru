using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class MyMemoryTranslator
{
    public class MyMemoryTranslator
{
    public static async Task<string> TranslateWithMyMemoryAsync(
        string text,
        string sourceLang = "ru",
        string targetLang = "en")
    {
        var httpClient = new HttpClient();

        try
        {
            var response = await httpClient.GetAsync(
            $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={sourceLang}|{targetLang}"
            );

            var json = await response.Content.ReadAsStringAsync();

            var jsonDoc = JsonDocument.Parse(json);
            return jsonDoc
                .RootElement
                .GetProperty("responseData")
                .GetProperty("translatedText")
                .GetString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error translate.");
            return text;
        }
    }
}
}
