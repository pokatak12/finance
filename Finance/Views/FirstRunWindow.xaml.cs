using System;
using System.Windows;
using Npgsql;
using Finance.Helpers;

namespace Finance.Views
{
    public partial class FirstRunWindow : Window
    {
        public FirstRunWindow()
        {
            InitializeComponent();
        }

        private async void CreateAdmin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await using var conn = new NpgsqlConnection(DbConnectionFactory.ConnectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand("SELECT public.create_first_admin(@l, @p, @f)", conn);
                cmd.Parameters.AddWithValue("l", LoginBox.Text);
                cmd.Parameters.AddWithValue("p", PasswordBox.Password);
                cmd.Parameters.AddWithValue("f", FullNameBox.Text);

                await cmd.ExecuteScalarAsync();

                MessageBox.Show("Администратор успешно создан!", "Успех");

                // Открываем окно входа
                new LoginWindow().Show();
                this.Close();
            }
            catch (PostgresException ex)
            {
                // Выводим понятную ошибку из БД (например, пароль слишком короткий)
                MessageBox.Show(ex.MessageText, "Ошибка БД", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}