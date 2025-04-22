using System.Net;

namespace Server;

public class UrlParser : IUrlParser
{
    private readonly string _baseUrl;

    public UrlParser(string baseUrl)
    {
        _baseUrl = baseUrl;
    }

    public string GetCheckpoint(string url)
    {
        if (string.IsNullOrEmpty(url))
            throw new ArgumentException("URL cannot be null or empty");

        var parts = url.Split('?');
        var path = parts[0];
        
        if (!path.StartsWith("/"))
            path = "/" + path;

        return path;
    }

    public Dictionary<string, string> GetParams(HttpListenerRequest request)
    {
        var result = new Dictionary<string, string>();

        var query = request.Url.Query;

        if (!string.IsNullOrEmpty(query))
        {
            var queryParams = query.TrimStart('?').Split('&');

            foreach (var param in queryParams)
            {
                var parts = param.Split('=');
                if (parts.Length == 2)
                {
                    var key = Uri.UnescapeDataString(parts[0]);
                    var value = Uri.UnescapeDataString(parts[1]);
                    result[key] = value;
                }
            }
        }

        return result;
    }
}