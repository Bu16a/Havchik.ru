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
            List<string> ingredients, List<string> allergens, int recipeCount, string sortBy = "relevance", int page = 1)
        {
            var query =
                """
                WITH
                    search_product_terms AS (
                        SELECT DISTINCT LOWER(p_term) AS term
                        FROM unnest(@ingredients) AS t(p_term)
                        WHERE p_term IS NOT NULL AND p_term <> ''
                    ),
                    allergen_terms AS (
                        SELECT DISTINCT LOWER(a_term) AS term
                        FROM unnest(@allergens) AS t(a_term)
                        WHERE a_term IS NOT NULL AND a_term <> ''
                    )
                SELECT
                    r.id,
                    r.title,
                    r.time,
                    r.energy,
                    r.image,
                    r.views,
                    counts.matched_ingredients_count,
                    counts.new_ingredients_count
                FROM
                    recipes r
                        CROSS JOIN LATERAL (
                        SELECT
                            COUNT(recipe_ing) FILTER (
                                WHERE EXISTS (
                                    SELECT 1
                                    FROM search_product_terms spt
                                    WHERE recipe_ing LIKE '%' || spt.term || '%'
                                )
                                ) AS matched_ingredients_count,
                
                            COUNT(recipe_ing) FILTER (
                                WHERE NOT EXISTS (
                                    SELECT 1
                                    FROM search_product_terms spt
                                    WHERE recipe_ing LIKE '%' || spt.term || '%'
                                )
                                ) AS new_ingredients_count
                        FROM
                            unnest(r.ingredients_unique) AS recipe_ing
                        ) AS counts
                WHERE
                    NOT EXISTS (
                        SELECT 1
                        FROM unnest(r.ingredients_unique) AS recipe_ing
                        WHERE EXISTS (
                            SELECT 1 FROM allergen_terms at_
                            WHERE recipe_ing LIKE '%' || at_.term || '%'
                        )
                    )
                """;
            switch (sortBy)
            {
                case "relevance":
                    query += """
                             
                             ORDER BY counts.matched_ingredients_count DESC, counts.new_ingredients_count, views DESC
                             limit @limit offset @offset;
                             """;
                    break;
                case "time":
                    query += """
                             
                             ORDER BY time ASC, views DESC
                             limit @limit offset @offset;
                             """;
                    break;
            }

            return await ReturnAndQuery(ingredients, allergens, recipeCount, page, query);
        }

        public async Task<List<Dictionary<string, object>>> QueryRecipesOnlyTheseProductsFromDatabaseAsync(
            List<string> ingredients, List<string> allergens, int recipeCount, string sortBy, int page)
        {
            var query =
                """
                WITH
                    search_products AS (
                        SELECT unnest(@ingredients) AS product
                    ),
                    allergen_products AS (
                        SELECT unnest(@allergens) AS allergen
                    )
                SELECT
                    r.id,
                    r.title,
                    r.time,
                    r.energy,
                    r.image,
                    r.views,
                    (SELECT COUNT(*) FROM jsonb_object_keys(r.ingredients)) AS match_count
                FROM recipes r
                WHERE
                    NOT EXISTS (
                        SELECT 1
                        FROM allergen_products ap, jsonb_object_keys(r.ingredients) ingredient_key
                        WHERE ingredient_key ILIKE '%' || ap.allergen || '%'
                    )
                  AND
                    NOT EXISTS (
                        SELECT 1
                        FROM jsonb_object_keys(r.ingredients) ingredient_key
                        WHERE NOT EXISTS (
                            SELECT 1
                            FROM search_products sp
                            WHERE ingredient_key ILIKE '%' || sp.product || '%'
                        )
                    )
                """;
            
            switch (sortBy)
            {
                case "relevance":
                    query += """

                             ORDER BY match_count DESC, views DESC
                             LIMIT @limit offset @offset;
                             """;
                    break;
                case "time":
                    query += """

                             ORDER BY time ASC, views DESC
                             limit @limit offset @offset;
                             """;
                    break;
            }

            return await ReturnAndQuery(ingredients, allergens, recipeCount, page, query);
        }

        private async Task<List<Dictionary<string, object>>> ReturnAndQuery(List<string> ingredients, List<string> allergens,
            int recipeCount, int page, string query)
        {
            var parameters = new Dictionary<string, (object value, NpgsqlDbType dbType)>
            {
                { "ingredients", (ingredients, NpgsqlDbType.Array | NpgsqlDbType.Text) },
                { "allergens", (allergens, NpgsqlDbType.Array | NpgsqlDbType.Text) },
                { "limit", (recipeCount, NpgsqlDbType.Integer) },
                { "offset", (10 * (page - 1), NpgsqlDbType.Integer) }
            };

            return await _dbService.ExecuteQueryAsync(query, parameters);
        }
    }
}