using System;
using System.Windows;
using Npgsql;
using Finance.Helpers;

namespace Finance.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await using var conn = new NpgsqlConnection(DbConnectionFactory.ConnectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand("SELECT * FROM public.login(@l, @p)", conn);
                cmd.Parameters.AddWithValue("l", LoginBox.Text);
                cmd.Parameters.AddWithValue("p", PasswordBox.Password);

               await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    MessageBox.Show("Неверный логин или пароль");
                    return;
                }

                // Получаем данные из БД
                DbConnectionFactory.CurrentUserId = reader.GetInt32(0);
                int roleId = reader.GetInt32(1);
                DbConnectionFactory.CurrentUserRole = roleId == 1 ? "admin" : "user";
                DbConnectionFactory.CurrentUserLogin = reader.GetString(2); // Логин
                DbConnectionFactory.CurrentUserName = reader.GetString(3);  // ФИО

                // Открываем главное окно
                new MainWindow().Show();
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }
        private void GoToRegister_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var regWindow = new RegisterWindow { Owner = this };
            regWindow.ShowDialog();
        }
    }
}