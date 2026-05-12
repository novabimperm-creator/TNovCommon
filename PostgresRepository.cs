using Npgsql;
using System;
using System.Data;
using System.Threading.Tasks;

namespace TNovCommon
{

    public class PostgresRepository : IDataRepository
    {
        private readonly string _connectionString;

        public PostgresRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task<UserInfo> GetOrCreateUserAsync(string upn, string displayName)
        {
            const string sql = @"
        INSERT INTO users (ad_upn, display_name)
        VALUES (@upn, @display)
        ON CONFLICT (ad_upn) DO UPDATE SET display_name = EXCLUDED.display_name
        RETURNING user_id, ad_upn, display_name, department, rulerole;
    ";

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@upn", upn);
                    cmd.Parameters.AddWithValue("@display", displayName);

                    using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await reader.ReadAsync())
                        {
                            return new UserInfo
                            {
                                UserId = reader.GetGuid(0),
                                Upn = reader.GetString(1),
                                DisplayName = reader.GetString(2),
                                Department = reader.IsDBNull(3) ? null : reader.GetString(3),
                                RuleRole = reader.GetBoolean(4)
                            };
                        }
                        throw new InvalidOperationException("Не удалось создать или получить пользователя.");
                    }
                }
            }
        }
        public async Task<FunctionDataEntry> LoadAsync(Guid userId, string functionName)
        {
            const string sql = @"
            SELECT data, updated_at
            FROM function_data
            WHERE user_id = @uid AND function_name = @fn;
        ";

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@uid", userId);
                    cmd.Parameters.AddWithValue("@fn", functionName);

                    using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await reader.ReadAsync())
                        {
                            return new FunctionDataEntry
                            {
                                UserId = userId,
                                FunctionName = functionName,
                                DataJson = reader.GetString(0),
                                UpdatedAt = reader.GetDateTime(1)
                            };
                        }
                        else
                        {
                            // Записи нет — возвращаем пустой JSON
                            return new FunctionDataEntry
                            {
                                UserId = userId,
                                FunctionName = functionName,
                                DataJson = "{}",
                                UpdatedAt = DateTime.MinValue
                            };
                        }
                    }
                }
            }
        }

        public async Task SaveAsync(Guid userId, string functionName, string jsonData)
        {
            const string sql = @"
            INSERT INTO function_data (user_id, function_name, data)
            VALUES (@uid, @fn, @data::jsonb)
            ON CONFLICT (user_id, function_name) DO UPDATE
                SET data = EXCLUDED.data,
                    updated_at = now();
        ";

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@uid", userId);
                    cmd.Parameters.AddWithValue("@fn", functionName);
                    cmd.Parameters.AddWithValue("@data", jsonData);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task<ModelDataEntry> LoadForModelAsync(string modelName, string functionName)
        {
            const string sql = @"
        SELECT data, updated_at
        FROM model_data
        WHERE model_name = @model AND function_name = @fn;
    ";

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@model", modelName);
                    cmd.Parameters.AddWithValue("@fn", functionName);

                    using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await reader.ReadAsync())
                        {
                            return new ModelDataEntry
                            {
                                ModelName = modelName,
                                FunctionName = functionName,
                                DataJson = reader.GetString(0),
                                UpdatedAt = reader.GetDateTime(1)
                            };
                        }
                        else
                        {
                            return new ModelDataEntry
                            {
                                ModelName = modelName,
                                FunctionName = functionName,
                                DataJson = "{}",
                                UpdatedAt = DateTime.MinValue
                            };
                        }
                    }
                }
            }
        }

        public async Task SaveForModelAsync(string modelName, string functionName, string jsonData)
        {
            const string sql = @"
        INSERT INTO model_data (model_name, function_name, data)
        VALUES (@model, @fn, @data::jsonb)
        ON CONFLICT (model_name, function_name) DO UPDATE
            SET data = EXCLUDED.data,
                updated_at = now();
    ";

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@model", modelName);
                    cmd.Parameters.AddWithValue("@fn", functionName);
                    cmd.Parameters.AddWithValue("@data", jsonData);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task LogFunctionUsageAsync(string functionName, string userName, string version)
        {
            const string sql = @"
        INSERT INTO function_usage (function_name, user_name, plugin_version)
        VALUES (@fn, @user, @ver);
    ";

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@fn", functionName);
                    cmd.Parameters.AddWithValue("@user", userName);
                    cmd.Parameters.AddWithValue("@ver", version); // version теперь будет в plugin_version
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
    }
}