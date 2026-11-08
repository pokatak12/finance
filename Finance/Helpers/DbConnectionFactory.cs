using Npgsql;

namespace Finance.Helpers;

public static class DbConnectionFactory
{
    // Строка подключения под пользователем app_user (для работы RLS)
    public const string ConnectionString =
        "Host=localhost;Port=5432;Database=finance;Username=app_user;Password=sA23iek_ewIjem;";

    // Текущий пользователь (заполняется после успешного логина)
    public static int CurrentUserId { get; set; } = 0;
    public static string CurrentUserRole { get; set; } = "user";
    public static string CurrentUserLogin { get; set; } = "";
    public static string CurrentUserName { get; set; } = "";
    /// <summary>
    /// Открывает соединение и устанавливает настройки для RLS.
    /// </summary>
    public static async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        if (CurrentUserId > 0)
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT set_config('app.user_id', @uid, false), " +
                "       set_config('app.user_role', @role, false)", conn);
            cmd.Parameters.AddWithValue("uid", CurrentUserId.ToString());
            cmd.Parameters.AddWithValue("role", CurrentUserRole);
            await cmd.ExecuteNonQueryAsync();
        }
        return conn;
    }
}