//using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TNovCommon
{
    public static class DatabaseInitializer
    {
        private const string CreateTablesSql = @"
            CREATE EXTENSION IF NOT EXISTS ""uuid-ossp"";
            CREATE TABLE IF NOT EXISTS users (
                user_id    UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                ad_upn     TEXT NOT NULL UNIQUE,
                display_name TEXT
            );
            CREATE TABLE IF NOT EXISTS function_data (
                id           UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                user_id      UUID NOT NULL REFERENCES users(user_id),
                function_name TEXT NOT NULL,
                data         JSONB NOT NULL DEFAULT '{}'::jsonb,
                updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
                UNIQUE(user_id, function_name)
            );
        ";
        /*
        public static async Task InitializeAsync()
        {
            using (var conn = new NpgsqlConnection(ConnectionStringProvider.GetConnectionString()))
            {
                await conn.OpenAsync();
                using (var cmd = new NpgsqlCommand(CreateTablesSql, conn))
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }*/
    }
}
