using Newtonsoft.Json;
using Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HavalNeGovno.Services
{
    public class GoogleImageSearchHelper : Logger, IGoogleImageSearchHelper
    {
        private readonly string _apiKey;
        private readonly string _cx;
        private readonly HttpClient _httpClient;

        public GoogleImageSearchHelper(string apiKey, string cx)
        {
            _apiKey = apiKey;
            _cx = cx;
            _httpClient = new HttpClient();
        }

        public async Task<string?> GetFirstImageUrlAsync(string query)
        {
            string searchUrl = "https://www.googleapis.com/customsearch/v1";
            var parameters = new Dictionary<string, string>
            {
                { "q", query },
                { "cx", _cx },
                { "key", _apiKey },
                { "searchType", "image" },
                { "num", "1" }
            };

            var queryString = await new FormUrlEncodedContent(parameters).ReadAsStringAsync();
            var requestUrl = $"{searchUrl}?{queryString}";

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);

                if (!response.IsSuccessStatusCode)
                {
                    Log($"Ошибка при запросе к API: {response.StatusCode}");
                    return null;
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                dynamic? data = JsonConvert.DeserializeObject(jsonResponse);

                if (data != null && data.items != null && data.items.Count > 0)
                {
                    return data.items[0].link;
                }
                else
                {
                    Log("Картинка не найдена");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log($"Исключение при запросе к Google Custom Search API: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
