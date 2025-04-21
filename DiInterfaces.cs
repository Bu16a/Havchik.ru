using System.Net;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
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
}

public interface ILogger
{
    void Log(string message);
}