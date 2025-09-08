using CocoroDock.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CocoroDock.Services
{
    public interface IReminderService
    {
        /// <summary>
        /// リマインダーを作成
        /// </summary>
        /// <param name="reminder">リマインダー情報</param>
        /// <returns>作成されたリマインダー</returns>
        Task<Reminder> CreateReminderAsync(Reminder reminder);

        /// <summary>
        /// 全てのリマインダーを取得
        /// </summary>
        /// <returns>リマインダーリスト</returns>
        Task<List<Reminder>> GetAllRemindersAsync();

        /// <summary>
        /// リマインダーを更新
        /// </summary>
        /// <param name="reminder">更新するリマインダー</param>
        /// <returns>更新されたリマインダー</returns>
        Task<Reminder> UpdateReminderAsync(Reminder reminder);

        /// <summary>
        /// リマインダーを削除
        /// </summary>
        /// <param name="id">削除するリマインダーのID</param>
        /// <returns>削除に成功したかどうか</returns>
        Task<bool> DeleteReminderAsync(int id);

        /// <summary>
        /// データベースを初期化
        /// </summary>
        /// <returns>初期化に成功したかどうか</returns>
        Task<bool> InitializeDatabaseAsync();
    }
}