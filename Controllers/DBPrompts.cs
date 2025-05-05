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
            List<string> ingredients, int recipeCount, string sortBy = "relevance", int page = 1)
        {
            var query =
                """
                WITH search_products AS (SELECT unnest(@ingredients) AS product),
                recipe_products AS (
                    SELECT 
                        r.id, r.title, r.time, r.energy, r.directions, r.image,
                        jsonb_object_keys(r.ingredients) AS recipe_product
                    FROM recipes r
                )
                SELECT 
                    rp.id,
                    rp.title,
                    rp.time,
                    rp.energy,
                    rp.directions,
                    rp.image,
                    COUNT(DISTINCT sp.product) AS match_count,
                    string_agg(DISTINCT sp.product, ', ') AS matched_products
                FROM recipe_products rp
                JOIN search_products sp 
                    ON rp.recipe_product ILIKE '%' || sp.product || '%'
                GROUP BY rp.id, rp.title, rp.time, rp.energy, rp.directions, rp.image
                """;
            switch (sortBy)
            {
                case "relevance":
                    query += """
                             
                             ORDER BY match_count DESC
                             limit @limit offset @offset;
                             """;
                    break;
                case "time":
                    query += """
                             
                             ORDER BY time ASC
                             limit @limit offset @offset;
                             """;
                    break;
            }

            return await ReturnAndQuery(ingredients, recipeCount, page, query);
        }

        public async Task<List<Dictionary<string, object>>> QueryRecipesOnlyTheseProductsFromDatabaseAsync(
            List<string> ingredients, int recipeCount, string sortBy, int page)
        {
            var query =
                """
                WITH search_products AS (
                    SELECT unnest(@ingredients) AS product
                ),
                     recipe_products AS (
                         SELECT
                             r.id,
                             r.title,
                             r.time,
                             r.energy,
                             r.image,
                             jsonb_object_keys(r.ingredients) AS recipe_product
                         FROM recipes r
                     ),
                     recipe_matches AS (
                         SELECT
                             rp.id,
                             rp.title,
                             rp.time,
                             rp.energy,
                             rp.image,
                             rp.recipe_product,
                             EXISTS (
                                 SELECT 1 FROM search_products sp
                                 WHERE rp.recipe_product ILIKE '%' || sp.product || '%'
                             ) AS is_matched
                         FROM recipe_products rp
                     ),
                     recipe_stats AS (
                         SELECT
                             id,
                             title,
                             time,
                             energy,
                             image,
                             COUNT(*) AS total_ingredients,
                             SUM(CASE WHEN is_matched THEN 1 ELSE 0 END) AS matched_ingredients
                         FROM recipe_matches
                         GROUP BY id, title, time, energy, image
                     )
                SELECT
                    id,
                    title,
                    time,
                    energy,
                    image,
                    matched_ingredients AS match_count
                FROM recipe_stats
                WHERE total_ingredients = matched_ingredients
                """;
            
            switch (sortBy)
            {
                case "relevance":
                    query += """

                             ORDER BY matched_ingredients DESC
                             LIMIT @limit offset @offset;
                             """;
                    break;
                case "time":
                    query += """

                             ORDER BY time ASC
                             limit @limit offset @offset;
                             """;
                    break;
            }

            return await ReturnAndQuery(ingredients, recipeCount, page, query);
        }

        private async Task<List<Dictionary<string, object>>> ReturnAndQuery(List<string> ingredients,
            int recipeCount, int page, string query)
        {
            var parameters = new Dictionary<string, (object value, NpgsqlDbType dbType)>
            {
                { "ingredients", (ingredients, NpgsqlDbType.Array | NpgsqlDbType.Text) },
                { "limit", (recipeCount, NpgsqlDbType.Integer) },
                { "offset", (10 * (page - 1), NpgsqlDbType.Integer) }
            };

            return await _dbService.ExecuteQueryAsync(query, parameters);
        }
    }
}