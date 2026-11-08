using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text; // ОБЯЗАТЕЛЬНО
using System.Windows;
using CsvHelper;
using Npgsql;
using Finance.Helpers;
using Finance.Services;
using Microsoft.Win32;

namespace Finance.Views
{
    public partial class ImportWindow : Window
    {
        public ImportWindow()
        {
            InitializeComponent();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
            if (dlg.ShowDialog() == true) FilePathBox.Text = dlg.FileName;
        }

        private async void StartImport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FilePathBox.Text)) { MessageBox.Show("Выберите файл."); return; }

            if (MessageBox.Show($"Вы уверены, что хотите импортировать файл:\n{Path.GetFileName(FilePathBox.Text)}?",
                "Подтверждение импорта", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            ImportButton.IsEnabled = false;
            ProgressBar.Visibility = Visibility.Visible;
            StatusText.Text = "Чтение файла...";

            var selectedItem = (System.Windows.Controls.ComboBoxItem)BankComboBox.SelectedItem;
            int bankId = Convert.ToInt32(selectedItem.Tag);
            string filePath = FilePathBox.Text;

            Encoding encoding = Encoding.UTF8;
            if (EncodingComboBox.SelectedIndex == 1)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                encoding = Encoding.GetEncoding(1251);
            }

            int imported = 0, skippedStatus = 0, skippedAnalytics = 0, skippedDup = 0, errors = 0;
            var accountCache = new Dictionary<string, int>();

            var errorLog = new List<string>();
            var skipLog = new List<string>();
            int rowIndex = 1;

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                var options = new CsvReaderOptions { Separator = ';', TrimWhitespace = true };

                using var reader = new StreamReader(filePath, encoding, detectEncodingFromByteOrderMarks: true);
                using var csv = CsvReader.Create(reader, options);

                while (csv.Read())
                {
                    rowIndex++;
                    try
                    {
                        string dateStr, amountStr, desc, originalCategory, mccStr, status;
                        string accountName, accountNumber, currency;
                        decimal amount;

                        if (bankId == 2) // ВТБ
                        {
                            accountName = csv.GetString(0)?.Trim();
                            accountNumber = csv.GetString(2)?.Trim();
                            currency = csv.GetString(8)?.Trim();
                            dateStr = csv.GetString(3);
                            amountStr = csv.GetString(5);
                            desc = csv.GetString(11) ?? "";
                            originalCategory = csv.GetString(13) ?? "";
                            mccStr = csv.GetString(15);
                            status = csv.GetString(17);

                            if (status != "Выполнено")
                            {
                                skippedStatus++;
                                skipLog.Add($"Строка {rowIndex}: Пропущено (Статус: {status}) - {desc}");
                                continue;
                            }
                        }
                        else if (bankId == 3) // Т-Банк
                        {
                            accountName = csv.GetString(0)?.Trim();
                            accountNumber = csv.GetString(1)?.Trim();
                            currency = csv.GetString(6)?.Trim();
                            dateStr = csv.GetString(2);
                            amountStr = csv.GetString(3);
                            status = csv.GetString(7);
                            originalCategory = csv.GetString(8) ?? "";
                            mccStr = csv.GetString(10);
                            desc = csv.GetString(11) ?? "";
                            string inAnalytics = csv.GetString(16);

                            if (status != "Ок")
                            {
                                skippedStatus++;
                                skipLog.Add($"Строка {rowIndex}: Пропущено (Статус: {status}) - {desc}");
                                continue;
                            }
                            if (inAnalytics == "Нет")
                            {
                                skippedAnalytics++;
                                skipLog.Add($"Строка {rowIndex}: Пропущено (Банк пометил 'Не учитывать') - {desc}");
                                continue;
                            }
                        }
                        else throw new Exception("Неизвестный банк");

                        // === БЛОК ОПРЕДЕЛЕНИЯ СЧЁТА (ТОТ, ЧТО БЫЛ ПОТЕРЯН) ===
                        if (!string.IsNullOrEmpty(accountNumber))
                        {
                            accountNumber = new string(accountNumber.Where(char.IsDigit).ToArray());
                        }

                        if (string.IsNullOrWhiteSpace(accountName)) accountName = "Основной счёт";
                        if (string.IsNullOrWhiteSpace(accountNumber))
                        {
                            using var md5Temp = MD5.Create();
                            byte[] nameHash = md5Temp.ComputeHash(Encoding.UTF8.GetBytes(accountName));
                            accountNumber = "AUTO-" + BitConverter.ToString(nameHash).Replace("-", "").Substring(0, 10);
                        }
                        if (string.IsNullOrWhiteSpace(currency)) currency = "RUB";

                        accountName = accountName.Length > 30 ? accountName.Substring(0, 30) : accountName;
                        accountNumber = accountNumber.Length > 20 ? accountNumber.Substring(0, 20) : accountNumber;
                        currency = currency.Length > 3 ? currency.Substring(0, 3) : currency;

                        string accKey = $"{accountName}_{accountNumber}";

                        if (!accountCache.ContainsKey(accKey))
                        {
                            await using var accCmd = new NpgsqlCommand(
                                "INSERT INTO public.accounts (id_user, id_bank, account_name, account_number, currency) " +
                                "VALUES (@uid, @bid, @name, @num, @curr) " +
                                "ON CONFLICT (id_user, id_bank, account_number) " +
                                "DO UPDATE SET account_name = EXCLUDED.account_name, currency = EXCLUDED.currency " +
                                "RETURNING id_account", conn);

                            accCmd.Parameters.AddWithValue("uid", DbConnectionFactory.CurrentUserId);
                            accCmd.Parameters.AddWithValue("bid", bankId);
                            accCmd.Parameters.AddWithValue("name", accountName);
                            accCmd.Parameters.AddWithValue("num", accountNumber);
                            accCmd.Parameters.AddWithValue("curr", currency);

                            int newId = (int)await accCmd.ExecuteScalarAsync();
                            accountCache[accKey] = newId;
                        }

                        int accountId = accountCache[accKey]; // <--- ТЕПЕРЬ ОН ОПРЕДЕЛЕН ЗДЕСЬ
                                                              // ========================================================

                        amountStr = amountStr.Replace(" ", "").Replace("RUB", "").Replace(',', '.').Trim();
                        if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out amount))
                            throw new Exception($"Неверный формат суммы: '{amountStr}'");

                        amount = Math.Abs(amount);

                        if (!DateTime.TryParseExact(dateStr, new[] { "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy H:mm", "dd.MM.yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime opDate))
                            throw new Exception($"Неверный формат даты: '{dateStr}'");

                        short? mcc = null;
                        if (short.TryParse(mccStr, out short m)) mcc = m;

                        var cat = await CategoryClassifier.ClassifyAsync(conn, bankId, desc, originalCategory, mcc, amount);

                        string hashString = $"{accountId}_{opDate.Ticks}_{amount}_{desc}_{mcc}";
                        using var md5 = MD5.Create();
                        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(hashString));
                        string externalHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

                        await using var cmd = new NpgsqlCommand(
                            "INSERT INTO public.transactions (id_account, id_category, operation_date, amount, description, mcc_code, original_category, source_file, created_manually, merchant_name, external_hash) " +
                            "VALUES (@aid, @cid, @date, @amt, @desc, @mcc, @orig, @src, false, @merchant, @hash) ON CONFLICT DO NOTHING", conn);

                        string merchantName = string.IsNullOrWhiteSpace(desc) ? null : (desc.Length > 100 ? desc.Substring(0, 100) : desc);

                        cmd.Parameters.AddWithValue("aid", accountId);
                        cmd.Parameters.AddWithValue("cid", cat.CategoryId);
                        cmd.Parameters.AddWithValue("date", opDate);
                        cmd.Parameters.AddWithValue("amt", amount);
                        cmd.Parameters.AddWithValue("desc", string.IsNullOrWhiteSpace(desc) ? "Без описания" : desc);
                        cmd.Parameters.AddWithValue("mcc", mcc.HasValue ? mcc.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("orig", originalCategory);
                        cmd.Parameters.AddWithValue("src", Path.GetFileName(filePath));
                        cmd.Parameters.AddWithValue("merchant", (object)merchantName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("hash", externalHash);

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows > 0) imported++;
                        else
                        {
                            skippedDup++;
                            skipLog.Add($"Строка {rowIndex}: Пропущено (Дубликат уже есть в базе) - {desc}");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        errorLog.Add($"Строка {rowIndex}: {ex.Message}");
                    }
                }

                // === ВЫВОД РЕЗУЛЬТАТОВ ===
                int totalSkipped = skippedStatus + skippedAnalytics + skippedDup;
                string resultMessage = $"Импорт завершен!\n\n" +
                                       $"✅ Успешно добавлено: {imported}\n" +
                                       $"⏭️ Пропущено: {totalSkipped}\n" +
                                       $"   • Отклонено банком (статус): {skippedStatus}\n" +
                                       $"   • Не учитывать в аналитике: {skippedAnalytics}\n" +
                                       $"   • Дубликаты (уже в базе): {skippedDup}\n" +
                                       $"❌ Ошибок: {errors}";

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (errors > 0)
                {
                    string errorFilePath = Path.Combine(baseDir, "import_errors.txt");
                    File.WriteAllLines(errorFilePath, errorLog);
                    resultMessage += $"\n\nОшибки сохранены в: import_errors.txt";
                }
                if (totalSkipped > 0)
                {
                    string skipFilePath = Path.Combine(baseDir, "import_skipped.txt");
                    File.WriteAllLines(skipFilePath, skipLog);
                    resultMessage += $"\nПропуски сохранены в: import_skipped.txt";
                }

                MessageBox.Show(resultMessage, "Результат импорта");
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Критическая ошибка импорта:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                ImportButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }
    }
}