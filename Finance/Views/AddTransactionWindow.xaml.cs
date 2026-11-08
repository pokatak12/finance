using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions; // ДОБАВЛЕНО для Regex
using Npgsql;
using Finance.Helpers;

namespace Finance.Views
{
    public partial class AddTransactionWindow : Window
    {
        private int? _editId = null;
        private bool _isFormattingAmount = false;

        public AddTransactionWindow(int? transactionId = null)
        {
            InitializeComponent();
            _editId = transactionId;
            if (_editId.HasValue) Title = "Редактирование операции";

            if (DatePicker.SelectedDate == null) DatePicker.SelectedDate = DateTime.Now;
            Loaded += AddTransactionWindow_Loaded;
        }

        private async void AddTransactionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();

                await using (var cmd = new NpgsqlCommand("SELECT id_account, bank_name || ' - ' || account_name FROM public.accounts a JOIN public.banks b ON b.id_bank = a.id_bank", conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    var accounts = new System.Collections.Generic.List<dynamic>();
                    while (await reader.ReadAsync())
                        accounts.Add(new { id_account = reader.GetInt32(0), Display = reader.GetString(1) });
                    AccountComboBox.ItemsSource = accounts;
                    if (accounts.Count > 0) AccountComboBox.SelectedIndex = 0;
                }

                await using (var cmd = new NpgsqlCommand("SELECT id_category, category_name, category_type FROM public.categories WHERE category_type IN ('Income', 'Expense') ORDER BY category_type, category_name", conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    var categories = new System.Collections.Generic.List<dynamic>();
                    while (await reader.ReadAsync())
                    {
                        string type = reader.GetString(2) == "Income" ? "🟢 Доход" : "🔴 Расход";
                        categories.Add(new { id_category = reader.GetInt32(0), Display = $"[{type}] {reader.GetString(1)}" });
                    }
                    CategoryComboBox.ItemsSource = categories;
                    if (categories.Count > 0) CategoryComboBox.SelectedIndex = 0;
                }

                // Загрузка данных для редактирования
                if (_editId.HasValue)
                {
                    await using var loadCmd = new NpgsqlCommand("SELECT id_account, id_category, operation_date, amount, description, merchant_name FROM public.transactions WHERE id_transaction = @id", conn);
                    loadCmd.Parameters.AddWithValue("id", _editId.Value);
                    await using var loadReader = await loadCmd.ExecuteReaderAsync();
                    if (await loadReader.ReadAsync())
                    {
                        AccountComboBox.SelectedValue = loadReader.GetInt32(0);
                        CategoryComboBox.SelectedValue = loadReader.GetInt32(1);
                        DatePicker.SelectedDate = loadReader.GetDateTime(2);

                        // ИСПРАВЛЕНО: заменяем неразрывный пробел на обычный при загрузке
                        AmountBox.Text = loadReader.GetDecimal(3).ToString("N2").Replace("\u00A0", " ");

                        DescriptionBox.Text = loadReader.GetString(4);
                        MerchantBox.Text = loadReader.IsDBNull(5) ? "" : loadReader.GetString(5);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // При получении фокуса выделяем весь текст (удобно заменить сумму)
        private void AmountBox_GotFocus(object sender, RoutedEventArgs e)
        {
            AmountBox.SelectAll();
        }

        // При вводе разделяем разряды пробелами
        private void AmountBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isFormattingAmount) return;
            var tb = sender as TextBox;
            if (tb == null || string.IsNullOrEmpty(tb.Text)) return;

            // Заменяем запятую на точку
            string currentText = tb.Text.Replace(',', '.');

            // УДАЛЯЕМ ВСЕ ПРОБЕЛЫ (включая неразрывные) с помощью Regex
            string raw = Regex.Replace(currentText, @"\s+", "");

            bool endsWithDot = currentText.EndsWith(".");
            string[] parts = raw.Split('.');
            string intPart = parts[0];
            string decPart = parts.Length > 1 ? parts[1] : "";

            if (long.TryParse(intPart, out long val))
            {
                // Форматируем и принудительно меняем неразрывный пробел на обычный
                string formattedInt = val.ToString("N0").Replace("\u00A0", " ");
                string result = formattedInt;

                if (parts.Length > 1 || endsWithDot)
                {
                    result += "." + decPart;
                }

                if (tb.Text != result)
                {
                    _isFormattingAmount = true;

                    // Запоминаем позицию курсора
                    int caretIndex = tb.CaretIndex;
                    tb.Text = result;

                    // Восстанавливаем курсор (с учетом добавленных/удаленных пробелов)
                    if (caretIndex < result.Length)
                        tb.CaretIndex = caretIndex + (result.Length - currentText.Length);
                    else
                        tb.CaretIndex = result.Length;

                    _isFormattingAmount = false;
                }
            }
        }

        // При потере фокуса приводим к формату с копейками (1 000.00)
        private void AmountBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AmountBox.Text))
            {
                AmountBox.Text = "0.00";
                return;
            }

            string raw = Regex.Replace(AmountBox.Text, @"\s+", "").Replace(',', '.');
            if (decimal.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal val))
            {
                // Принудительно меняем неразрывный пробел на обычный для отображения
                AmountBox.Text = val.ToString("N2").Replace("\u00A0", " ");
            }
        }

        private async void SaveTransaction_Click(object sender, RoutedEventArgs e)
        {
            if (AccountComboBox.SelectedValue == null || CategoryComboBox.SelectedValue == null)
            {
                MessageBox.Show("Выберите счёт и категорию.");
                return;
            }

            // ИСПРАВЛЕНО: Используем Regex для удаления ВСЕХ видов пробелов перед сохранением
            string cleanAmount = Regex.Replace(AmountBox.Text, @"\s+", "").Replace(',', '.');

            if (!decimal.TryParse(cleanAmount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
            {
                MessageBox.Show("Введите корректную положительную сумму.");
                return;
            }

            SaveButton.IsEnabled = false;
            SaveButton.Content = "Сохранение...";

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();

                string sql;
                if (_editId.HasValue)
                    sql = "UPDATE public.transactions SET id_account=@aid, id_category=@cid, operation_date=@date, amount=@amount, description=@desc, merchant_name=@merchant WHERE id_transaction=@id";
                else
                    sql = "INSERT INTO public.transactions (id_account, id_category, operation_date, amount, description, original_category, created_manually, merchant_name) VALUES (@aid, @cid, @date, @amount, @desc, 'Ручной ввод', true, @merchant)";

                await using var cmd = new NpgsqlCommand(sql, conn);
                if (_editId.HasValue) cmd.Parameters.AddWithValue("id", _editId.Value);

                cmd.Parameters.AddWithValue("aid", Convert.ToInt32(AccountComboBox.SelectedValue));
                cmd.Parameters.AddWithValue("cid", Convert.ToInt32(CategoryComboBox.SelectedValue));
                cmd.Parameters.AddWithValue("date", DatePicker.SelectedDate ?? DateTime.Now);
                cmd.Parameters.AddWithValue("amount", amount);
                cmd.Parameters.AddWithValue("desc", string.IsNullOrWhiteSpace(DescriptionBox.Text) ? "Без описания" : DescriptionBox.Text.Trim());

                if (string.IsNullOrWhiteSpace(MerchantBox.Text)) cmd.Parameters.AddWithValue("merchant", DBNull.Value);
                else cmd.Parameters.AddWithValue("merchant", MerchantBox.Text.Trim());

                await cmd.ExecuteNonQueryAsync();

                this.DialogResult = true;
                this.Close();
            }
            catch (PostgresException ex) { MessageBox.Show(ex.MessageText, "Ошибка БД", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            finally { SaveButton.IsEnabled = true; SaveButton.Content = "Сохранить"; }
        }
    }
}