using System.Net;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NpgsqlTypes;
using Server;

namespace Server;

public interface IGeminiApi
{
    void ChangeModel(string modelName);
    Task<(HttpListenerResponse response, string responseBody, HttpStatusCode statusCode)> ProcessGeminiRequest(string prompt, HttpListenerResponse response);
}

public interface IUrlParser
{
    string GetCheckpoint(string path);
    Dictionary<string, string> GetParams(HttpListenerRequest request);
}

public interface IDbService
{
    Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string query, Dictionary<string, (object value, NpgsqlDbType dbType)> parameters);
}

public interface ITranslator
{
    Task<string> TranslateWithMyMemoryAsync(string text, string sourceLang, string targetLang);
    Task<List<string>> TranslateIngredientsAsync(List<string> ingredients, Action onError);
    Task<object> TranslateValueAsync(object value, string sourceLang, string targetLang);
}

public interface IJsonServing
{
    bool IsValidJsonContentType(HttpListenerRequest request);
    bool TryParseJson(string json, out JObject jsonData, out string errorMessage);
    bool TryGetParam<T>(JObject jsonData, string paramName, out T value, out string errorMessage);
}

public interface ILogger
{
    void Log(string message);
}