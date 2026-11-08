using System;
using System.Linq;
using System.Windows;
using Npgsql;
using Finance.Helpers;

namespace Finance.Views
{
    public partial class AddAccountWindow : Window
    {
        private int? _editId = null;

        public AddAccountWindow(int? accountId = null)
        {
            InitializeComponent();
            _editId = accountId;
            if (_editId.HasValue) Title = "Редактирование счёта";
            Loaded += AddAccountWindow_Loaded;
        }

        private async void AddAccountWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                await using var cmd = new NpgsqlCommand("SELECT id_bank, bank_name FROM public.banks ORDER BY bank_name", conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                var banks = new System.Collections.Generic.List<dynamic>();
                while (await reader.ReadAsync())
                    banks.Add(new { id_bank = reader.GetInt32(0), bank_name = reader.GetString(1) });

                BankComboBox.ItemsSource = banks;
                BankComboBox.SelectedIndex = 0;

                // Если это редактирование — загружаем старые данные
                if (_editId.HasValue)
                {
                    await reader.CloseAsync(); // Закрываем ридер перед новым запросом
                    await using var loadCmd = new NpgsqlCommand("SELECT id_bank, account_name, account_number, currency FROM public.accounts WHERE id_account = @id", conn);
                    loadCmd.Parameters.AddWithValue("id", _editId.Value);
                    await using var loadReader = await loadCmd.ExecuteReaderAsync();
                    if (await loadReader.ReadAsync())
                    {
                        BankComboBox.SelectedValue = loadReader.GetInt32(0);
                        AccountNameBox.Text = loadReader.GetString(1);
                        AccountNumberBox.Text = loadReader.GetString(2);

                        string curr = loadReader.GetString(3).Trim();
                        foreach (System.Windows.Controls.ComboBoxItem item in CurrencyComboBox.Items)
                            if (item.Content.ToString() == curr) CurrencyComboBox.SelectedItem = item;
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private async void SaveAccount_Click(object sender, RoutedEventArgs e)
        {
            if (BankComboBox.SelectedValue == null) { MessageBox.Show("Выберите банк"); return; }
            if (string.IsNullOrWhiteSpace(AccountNameBox.Text)) { MessageBox.Show("Введите название"); return; }

            string accountNum = AccountNumberBox.Text.Trim();
            if (string.IsNullOrEmpty(accountNum) || !accountNum.All(char.IsDigit))
            {
                MessageBox.Show("Номер счёта должен состоять только из цифр.");
                return;
            }

            SaveButton.IsEnabled = false;
            SaveButton.Content = "Сохранение...";

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();

                string sql;
                if (_editId.HasValue)
                    sql = "UPDATE public.accounts SET id_bank=@bid, account_name=@name, account_number=@num, currency=@curr WHERE id_account=@id";
                else
                    sql = "INSERT INTO public.accounts (id_bank, account_name, account_number, currency, id_user) VALUES (@bid, @name, @num, @curr, @uid)";

                await using var cmd = new NpgsqlCommand(sql, conn);
                if (_editId.HasValue) cmd.Parameters.AddWithValue("id", _editId.Value);
                else cmd.Parameters.AddWithValue("uid", DbConnectionFactory.CurrentUserId);

                cmd.Parameters.AddWithValue("bid", Convert.ToInt32(BankComboBox.SelectedValue));
                cmd.Parameters.AddWithValue("name", AccountNameBox.Text.Trim());
                cmd.Parameters.AddWithValue("num", accountNum);
                cmd.Parameters.AddWithValue("curr", ((System.Windows.Controls.ComboBoxItem)CurrencyComboBox.SelectedItem).Content.ToString());

                await cmd.ExecuteNonQueryAsync();
                this.DialogResult = true;
                this.Close();
            }
            catch (PostgresException ex) { MessageBox.Show(ex.MessageText, "Ошибка БД", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            finally { SaveButton.IsEnabled = true; SaveButton.Content = "Сохранить счёт"; }
        }
    }
}