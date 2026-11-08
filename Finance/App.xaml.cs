using System.Windows;
using Npgsql;
using Finance.Helpers;
using Finance.Views;

namespace Finance
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Проверяем, есть ли в БД хотя бы один админ
            bool hasAdmin = false;
            try
            {
                await using var conn = new NpgsqlConnection(DbConnectionFactory.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("SELECT public.has_admin()", conn);
                hasAdmin = (bool)await cmd.ExecuteScalarAsync()!;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Ошибка подключения к БД:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // Открываем нужное окно
            if (hasAdmin)
            {
                new LoginWindow().Show();
            }
            else
            {
                new FirstRunWindow().Show();
            }
        }
    }
}