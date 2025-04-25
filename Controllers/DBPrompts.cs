using DotNetEnv;
using Google.Cloud.AIPlatform.V1;
using NpgsqlTypes;
using Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace HavalNeGovno.Controllers
{
    public class DBPrompts
    {
        private readonly IDbService _dbService;

        public DBPrompts(IDbService dbService)
        {
            _dbService = dbService;
        }

        public async Task<List<Dictionary<string, object>>> QueryRecipesByIdFromDatabaseAsync(int id)
        {
            var query = @"
            SELECT *
            FROM recipes
            WHERE id = @id
            LIMIT 1";

            var parameters = new Dictionary<string, (object value, NpgsqlDbType dbType)>
            {
                { "id", (id, NpgsqlDbType.Integer) }
            };

            return await _dbService.ExecuteQueryAsync(query, parameters);
        }


        public async Task<List<Dictionary<string, object>>> QueryRecipesFromDatabaseAsync(
            List<string> translatedIngredients, int recipeCount)
        {
            var query = @"
            SELECT *
            FROM recipes
            WHERE ner_ingredients && @ingredients::TEXT[]
            LIMIT @limit";

            return await ReturnAndQuery(translatedIngredients, recipeCount, query);
        }

        public async Task<List<Dictionary<string, object>>> QueryRecipesOnlyTheseProductsFromDatabaseAsync(
            List<string> translatedIngredients, int recipeCount)
        {
            var query = @"
            SELECT *
            FROM recipes
            WHERE ner_ingredients <@ @ingredients::TEXT[]
            ORDER BY id
            LIMIT @limit";

            return await ReturnAndQuery(translatedIngredients, recipeCount, query);
        }

        private async Task<List<Dictionary<string, object>>> ReturnAndQuery(List<string> translatedIngredients, int recipeCount, string query)
        {
            var parameters = new Dictionary<string, (object value, NpgsqlDbType dbType)>
            {
                { "ingredients", (translatedIngredients, NpgsqlDbType.Array | NpgsqlDbType.Text) },
                { "limit", (recipeCount, NpgsqlDbType.Integer) }
            };

            return await _dbService.ExecuteQueryAsync(query, parameters);
        }
    }
}
