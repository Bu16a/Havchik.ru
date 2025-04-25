using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using Server;

namespace HavalNeGovno.Utils
{
    public class JsonServing : IJsonServing
    {
        public bool IsValidJsonContentType(HttpListenerRequest request) =>
            request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? false;

        public bool TryParseJson(string json, out JObject jsonData, out string errorMessage)
        {
            jsonData = null;
            errorMessage = null;
            try
            {
                jsonData = JObject.Parse(json);
                return true;
            }
            catch (JsonReaderException ex)
            {
                errorMessage = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"An unexpected error occurred during JSON parsing: {ex.Message}";
                return false;
            }
        }

        public bool TryGetParam<T>(JObject jsonData, string paramName, out T value, out string errorMessage)
        {
            value = default;
            errorMessage = null;

            if (!jsonData.TryGetValue(paramName, StringComparison.OrdinalIgnoreCase, out JToken token))
            {
                errorMessage = $"Parameter '{paramName}' is required.";
                return false;
            }

            if (token.Type == JTokenType.Null && default(T) != null)
            {
                errorMessage = $"Parameter '{paramName}' cannot be null.";
                return false;
            }

            try
            {
                value = token.ToObject<T>();
                if (value is null && default(T) != null)
                {
                    errorMessage = $"Parameter '{paramName}' could not be converted to the required type or is null.";
                    return false;
                }

                return true;
            }
            catch (System.Text.Json.JsonException ex)
            {
                errorMessage =
                    $"Invalid format for parameter '{paramName}'. Expected type: {typeof(T).Name}. Error: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"An unexpected error occurred while retrieving parameter '{paramName}': {ex.Message}";
                return false;
            }
        }
    }
}
