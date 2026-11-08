using System;
using System.Windows;
using Npgsql;
using Finance.Helpers;

namespace Finance.Views
{
    public partial class RegisterWindow : Window
    {
        public RegisterWindow()
        {
            InitializeComponent();
        }

        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            RegisterButton.IsEnabled = false;
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();

                // Вызываем нашу SECURITY DEFINER функцию из БД
                await using var cmd = new NpgsqlCommand("SELECT public.register_user(@l, @p, @f)", conn);
                cmd.Parameters.AddWithValue("l", LoginBox.Text.Trim());
                cmd.Parameters.AddWithValue("p", PasswordBox.Password);
                cmd.Parameters.AddWithValue("f", FullNameBox.Text.Trim());

                await cmd.ExecuteScalarAsync();

                MessageBox.Show("Аккаунт успешно создан! Теперь вы можете войти.", "Успех");
                this.DialogResult = true;
                this.Close();
            }
            catch (PostgresException ex)
            {
                // Сюда прилетят ошибки из БД (например, "Логин уже существует")
                MessageBox.Show(ex.MessageText, "Ошибка регистрации", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка");
            }
            finally
            {
                RegisterButton.IsEnabled = true;
            }
        }
    }
}