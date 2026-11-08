using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;

namespace Finance.Services
{
    public class ClassifierResult
    {
        public int CategoryId { get; set; }
        public string CategoryType { get; set; } = ""; // Income, Expense, Transfer
        public string CategoryName { get; set; } = "";
    }

    public static class CategoryClassifier
    {
        /// <summary>
        /// Классифицирует операцию по правилам: Keyword -> MCC -> CategoryMapping -> Fallback.
        /// </summary>
        public static async Task<ClassifierResult> ClassifyAsync(
            NpgsqlConnection conn, int bankId, string description, string originalCategory,
            short? mccCode, decimal amount)
        {
            string descLower = (description ?? "").ToLower().Trim();
            string catLower = (originalCategory ?? "").ToLower().Trim();
            string fallbackName = amount > 0 ? "Прочие доходы" : "Другое";

            // 1. Ищем по ключевым словам (самый высокий приоритет)
            await using (var cmd = new NpgsqlCommand(
                "SELECT c.id_category, c.category_type, c.category_name " +
                "FROM public.keyword_mapping k JOIN public.categories c ON c.id_category = k.id_category " +
                "WHERE @desc LIKE '%' || LOWER(k.keyword) || '%' LIMIT 1", conn))
            {
                cmd.Parameters.AddWithValue("desc", descLower);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync()) return ReadResult(r);
            }

            // 2. Ищем по MCC коду
            if (mccCode.HasValue)
            {
                await using (var cmd = new NpgsqlCommand(
                    "SELECT c.id_category, c.category_type, c.category_name " +
                    "FROM public.mcc_mapping m JOIN public.categories c ON c.id_category = m.id_category " +
                    "WHERE m.mcc_code = @mcc AND (m.id_bank = @bid OR m.id_bank IS NULL) " +
                    "ORDER BY m.id_bank DESC NULLS LAST LIMIT 1", conn))
                {
                    cmd.Parameters.AddWithValue("mcc", mccCode.Value);
                    cmd.Parameters.AddWithValue("bid", bankId);
                    await using var r = await cmd.ExecuteReaderAsync();
                    if (await r.ReadAsync()) return ReadResult(r);
                }
            }

            // 3. Ищем по категории банка (originalCategory)
            if (!string.IsNullOrWhiteSpace(catLower))
            {
                await using (var cmd = new NpgsqlCommand(
                    "SELECT c.id_category, c.category_type, c.category_name " +
                    "FROM public.category_mapping m JOIN public.categories c ON c.id_category = m.id_category " +
                    "WHERE m.id_bank = @bid AND LOWER(m.bank_category) = @cat LIMIT 1", conn))
                {
                    cmd.Parameters.AddWithValue("bid", bankId);
                    cmd.Parameters.AddWithValue("cat", catLower);
                    await using var r = await cmd.ExecuteReaderAsync();
                    if (await r.ReadAsync()) return ReadResult(r);
                }
            }

            // 4. Fallback (если ничего не подошло)
            await using (var cmd = new NpgsqlCommand(
                "SELECT id_category, category_type, category_name FROM public.categories WHERE category_name = @name", conn))
            {
                cmd.Parameters.AddWithValue("name", fallbackName);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync()) return ReadResult(r);
            }

            throw new Exception("Критическая ошибка: в БД отсутствуют категории фоллбэка (Прочие доходы / Другое)");
        }

        private static ClassifierResult ReadResult(NpgsqlDataReader r)
        {
            return new ClassifierResult
            {
                CategoryId = r.GetInt32(0),
                CategoryType = r.GetString(1),
                CategoryName = r.GetString(2)
            };
        }
    }
}