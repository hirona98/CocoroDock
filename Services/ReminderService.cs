using CocoroDock.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CocoroDock.Services
{
    public class ReminderService : IReminderService
    {
        private readonly string _dbPath;

        public ReminderService(IAppSettings appSettings)
        {
            _dbPath = Path.Combine(appSettings.UserDataDirectory, "Reminders.db");
            Debug.WriteLine($"ReminderService DB Path: {_dbPath}");
        }

        public ReminderService(string dbPath)
        {
            _dbPath = dbPath;
        }

        private string GetConnectionString() => $"Data Source={_dbPath};";

        public async Task<bool> InitializeDatabaseAsync()
        {
            try
            {
                using var connection = new SqliteConnection(GetConnectionString());
                await connection.OpenAsync();

                var createTableSql = @"
                    CREATE TABLE IF NOT EXISTS reminders (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        remind_datetime TEXT NOT NULL,
                        requirement TEXT NOT NULL
                    )";

                var command = new SqliteCommand(createTableSql, connection);
                await command.ExecuteNonQueryAsync();

                Debug.WriteLine($"リマインダーデータベースを初期化しました: {_dbPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"データベース初期化エラー: {ex.Message}");
                return false;
            }
        }

        public async Task<Reminder> CreateReminderAsync(Reminder reminder)
        {
            await InitializeDatabaseAsync();

            using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync();

            var insertSql = @"
                INSERT INTO reminders (remind_datetime, requirement)
                VALUES (@remind_datetime, @requirement)
                RETURNING *";

            using var command = new SqliteCommand(insertSql, connection);
            command.Parameters.AddWithValue("@remind_datetime", reminder.RemindDatetime);
            command.Parameters.AddWithValue("@requirement", reminder.Requirement);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return ReadReminderFromReader(reader);
            }

            throw new InvalidOperationException("リマインダーの作成に失敗しました");
        }

        public async Task<List<Reminder>> GetAllRemindersAsync()
        {
            await InitializeDatabaseAsync();

            var reminders = new List<Reminder>();
            using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync();

            var selectSql = "SELECT * FROM reminders ORDER BY remind_datetime DESC";
            using var command = new SqliteCommand(selectSql, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                reminders.Add(ReadReminderFromReader(reader));
            }

            return reminders;
        }

        public async Task<Reminder> UpdateReminderAsync(Reminder reminder)
        {
            await InitializeDatabaseAsync();

            using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync();

            var updateSql = @"
                UPDATE reminders SET
                    remind_datetime = @remind_datetime,
                    requirement = @requirement
                WHERE id = @id";

            using var command = new SqliteCommand(updateSql, connection);
            command.Parameters.AddWithValue("@id", reminder.Id);
            command.Parameters.AddWithValue("@remind_datetime", reminder.RemindDatetime);
            command.Parameters.AddWithValue("@requirement", reminder.Requirement);

            await command.ExecuteNonQueryAsync();
            return reminder;
        }

        public async Task<bool> DeleteReminderAsync(int id)
        {
            await InitializeDatabaseAsync();

            using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync();

            var deleteSql = "DELETE FROM reminders WHERE id = @id";
            using var command = new SqliteCommand(deleteSql, connection);
            command.Parameters.AddWithValue("@id", id);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }

        private static Reminder ReadReminderFromReader(SqliteDataReader reader)
        {
            return new Reminder
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                RemindDatetime = reader.GetString(reader.GetOrdinal("remind_datetime")),
                Requirement = reader.GetString(reader.GetOrdinal("requirement"))
            };
        }
    }
}