using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Npgsql;
using Finance.Helpers;
using Finance.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Windows.Media;

namespace Finance.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UserInfoText.Text = $"Пользователь: {DbConnectionFactory.CurrentUserName} ({DbConnectionFactory.CurrentUserLogin}) | Роль: {DbConnectionFactory.CurrentUserRole.ToUpper()}";

            // Настройка дат для графиков по умолчанию
            StartDatePicker.SelectedDate = DateTime.Today.AddMonths(-1);
            EndDatePicker.SelectedDate = DateTime.Today;

            await LoadAccounts();
            await LoadTransactions();
            await LoadAnalyticsFilters();
            await LoadAnalytics();
            await LoadDetailsFilters();
            await LoadGoals();
            StartDatePickerDetails.SelectedDate = DateTime.Today.AddMonths(-1);
            EndDatePickerDetails.SelectedDate = DateTime.Today;
            if (DbConnectionFactory.CurrentUserRole != "admin")
            {
                AdminTab.Visibility = Visibility.Collapsed; // Скрываем вкладку
            }
            else
            {
                await LoadAdminData(); // Загружаем данные для админа
            }

        }

        // ================= ЗАГРУЗКА ДАННЫХ (СЧЕТА И ТРАНЗАКЦИИ) =================

        private async System.Threading.Tasks.Task LoadAccounts()
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "SELECT a.id_account, a.account_name, a.account_number, a.currency, b.bank_name " +
                    "FROM public.accounts a JOIN public.banks b ON b.id_bank = a.id_bank " +
                    "ORDER BY b.bank_name, a.account_name", conn);

                await using var reader = await cmd.ExecuteReaderAsync();
                var accounts = new List<AccountModel>();
                int rowNum = 1;
                while (await reader.ReadAsync())
                {
                    accounts.Add(new AccountModel
                    {
                        RowNumber = rowNum++,
                        id_account = reader.GetInt32(0),
                        account_name = reader.GetString(1),
                        account_number = reader.GetString(2),
                        currency = reader.GetString(3).Trim(),
                        bank_name = reader.GetString(4),
                        IsSelected = false
                    });
                }

                var cvs = new CollectionViewSource { Source = accounts };
                cvs.GroupDescriptions.Add(new PropertyGroupDescription("bank_name"));
                AccountsGrid.ItemsSource = cvs.View;
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка загрузки счетов:\n{ex.Message}"); }
        }
        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Вы уверены, что хотите выйти из аккаунта?", "Выход", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                DbConnectionFactory.CurrentUserId = 0;
                DbConnectionFactory.CurrentUserRole = "user";
                DbConnectionFactory.CurrentUserLogin = ""; 
                DbConnectionFactory.CurrentUserName = ""; 

                var loginWindow = new LoginWindow();
                loginWindow.Show();
                Application.Current.MainWindow = loginWindow;
                this.Close();
            }
        }
        private async System.Threading.Tasks.Task LoadTransactions()
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "SELECT t.id_transaction, t.operation_date, c.category_name, c.category_type, t.amount, a.account_name, b.bank_name, COALESCE(t.merchant_name, t.description) " +
                    "FROM public.transactions t " +
                    "JOIN public.accounts a ON a.id_account = t.id_account " +
                    "JOIN public.banks b ON b.id_bank = a.id_bank " +
                    "JOIN public.categories c ON c.id_category = t.id_category " +
                    "ORDER BY b.bank_name, a.account_name, t.operation_date DESC", conn);

                await using var reader = await cmd.ExecuteReaderAsync();
                var transactions = new List<TransactionModel>();
                int rowNum = 1;
                while (await reader.ReadAsync())
                {
                    transactions.Add(new TransactionModel
                    {
                        RowNumber = rowNum++,
                        id_transaction = reader.GetInt32(0),
                        operation_date = reader.GetDateTime(1),
                        category_name = reader.GetString(2),
                        category_type = reader.GetString(3),
                        amount = reader.GetDecimal(4),
                        account_name = reader.GetString(5),
                        bank_name = reader.GetString(6),
                        merchant_name = reader.IsDBNull(7) ? "" : reader.GetString(7),
                        IsSelected = false
                    });
                }

                var cvs = new CollectionViewSource { Source = transactions };
                cvs.GroupDescriptions.Add(new PropertyGroupDescription("bank_name"));
                cvs.GroupDescriptions.Add(new PropertyGroupDescription("account_name"));
                TransactionsGrid.ItemsSource = cvs.View;
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка загрузки транзакций:\n{ex.Message}"); }
        }

        // ================= СЧЕТА: КНОПКИ =================

        private async void AddAccount_Click(object sender, RoutedEventArgs e)
        {
            var addWindow = new AddAccountWindow { Owner = this };
            if (addWindow.ShowDialog() == true) await LoadAccounts();
        }

        private async void EditAccount_Click(object sender, RoutedEventArgs e)
        {
            var selected = AccountsGrid.SelectedItem as AccountModel;
            if (selected == null) { MessageBox.Show("Кликните на строку со счётом, который хотите изменить."); return; }

            var editWindow = new AddAccountWindow(selected.id_account) { Owner = this };
            if (editWindow.ShowDialog() == true) await LoadAccounts();
        }

        private async void DeleteSelectedAccounts_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = AccountsGrid.Items.Cast<AccountModel>().Where(x => x.IsSelected).ToList();
            if (selectedItems == null || selectedItems.Count == 0)
            {
                MessageBox.Show("Отметьте галочками счета, которые хотите удалить.");
                return;
            }

            if (MessageBox.Show($"Вы уверены, что хотите удалить выбранные счета ({selectedItems.Count} шт.)?\nВНИМАНИЕ: Все операции по этим счетам также будут удалены!",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                var ids = string.Join(",", selectedItems.Select(x => x.id_account));
                await using var cmd = new NpgsqlCommand($"DELETE FROM public.accounts WHERE id_account IN ({ids})", conn);
                await cmd.ExecuteNonQueryAsync();

                await LoadAccounts();
                await LoadTransactions();
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка удаления:\n{ex.Message}"); }
        }

        private async void ClearAllAccounts_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("ВНИМАНИЕ! Вы собираетесь удалить ВСЕ свои счета и ВСЮ историю операций.\nЭто действие нельзя отменить. Продолжить?",
                "Полная очистка", MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes) return;

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                await using var cmd = new NpgsqlCommand("DELETE FROM public.accounts", conn);
                await cmd.ExecuteNonQueryAsync();

                await LoadAccounts();
                await LoadTransactions();
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка очистки:\n{ex.Message}"); }
        }

        // ================= ТРАНЗАКЦИИ: КНОПКИ =================

        private async void AddTransaction_Click(object sender, RoutedEventArgs e)
        {
            var addWindow = new AddTransactionWindow { Owner = this };
            if (addWindow.ShowDialog() == true) await LoadTransactions();
        }

        private async void EditTransaction_Click(object sender, RoutedEventArgs e)
        {
            var selected = TransactionsGrid.SelectedItem as TransactionModel;
            if (selected == null) { MessageBox.Show("Кликните на строку с операцией, которую хотите изменить."); return; }

            var editWindow = new AddTransactionWindow(selected.id_transaction) { Owner = this };
            if (editWindow.ShowDialog() == true) await LoadTransactions();
        }

        private async void DeleteSelectedTransactions_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = TransactionsGrid.Items.Cast<TransactionModel>().Where(x => x.IsSelected).ToList();
            if (selectedItems == null || selectedItems.Count == 0)
            {
                MessageBox.Show("Отметьте галочками операции, которые хотите удалить.");
                return;
            }

            if (MessageBox.Show($"Удалить выбранные операции ({selectedItems.Count} шт.)?",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                var ids = string.Join(",", selectedItems.Select(x => x.id_transaction));
                await using var cmd = new NpgsqlCommand($"DELETE FROM public.transactions WHERE id_transaction IN ({ids})", conn);
                await cmd.ExecuteNonQueryAsync();

                await LoadTransactions();
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка удаления:\n{ex.Message}"); }
        }

        private async void ClearAllTransactions_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Вы уверены, что хотите удалить ВСЮ историю операций? Счета останутся на месте.",
                "Очистка операций", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                await using var cmd = new NpgsqlCommand("DELETE FROM public.transactions", conn);
                await cmd.ExecuteNonQueryAsync();
                await LoadTransactions();
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка очистки:\n{ex.Message}"); }
        }

        private async void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var importWindow = new ImportWindow { Owner = this };
            if (importWindow.ShowDialog() == true)
            {
                await LoadAccounts();
                await LoadTransactions();
                await LoadAnalytics();
            }
        }

        // ================= АНАЛИТИКА =================

        private async System.Threading.Tasks.Task LoadAnalyticsFilters()
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();

                var banks = new List<dynamic>();
                banks.Add(new { id_bank = (int?)null, bank_name = "Все банки" });

                await using (var cmd = new NpgsqlCommand("SELECT id_bank, bank_name FROM public.banks", conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        banks.Add(new { id_bank = (int?)reader.GetInt32(0), bank_name = reader.GetString(1) });
                }

                BankFilterComboBox.ItemsSource = banks;
                BankFilterComboBox.SelectedIndex = 0;
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка загрузки фильтров:\n{ex.Message}"); }
        }

        private async void BankFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (BankFilterComboBox.SelectedValue == null) return;

            int? bankId = BankFilterComboBox.SelectedValue as int?;

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                var accounts = new List<dynamic>();
                accounts.Add(new { id_account = (int?)null, Display = "Все счета" });

                string sql = "SELECT id_account, account_name FROM public.accounts";
                if (bankId.HasValue) sql += " WHERE id_bank = @bid";

                await using var cmd = new NpgsqlCommand(sql, conn);
                if (bankId.HasValue) cmd.Parameters.AddWithValue("bid", bankId.Value);

                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        accounts.Add(new { id_account = (int?)reader.GetInt32(0), Display = reader.GetString(1) });
                }

                AccountFilterComboBox.ItemsSource = accounts;
                AccountFilterComboBox.SelectedIndex = 0;
            }
            catch { }
        }

        private async void UpdateCharts_Click(object sender, RoutedEventArgs e)
        {
            await LoadAnalytics();
        }

        private async System.Threading.Tasks.Task LoadAnalytics()
        {
            if (StartDatePicker.SelectedDate == null || EndDatePicker.SelectedDate == null) return;

            DateTime startDate = StartDatePicker.SelectedDate.Value;
            DateTime endDate = EndDatePicker.SelectedDate.Value.AddDays(1).AddSeconds(-1);

            int? bankId = BankFilterComboBox.SelectedValue as int?;
            int? accountId = AccountFilterComboBox.SelectedValue as int?;

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();

                // 1. РАСХОДЫ
                var expenseData = await GetCategoryData(conn, "Expense", startDate, endDate, bankId, accountId);
                var expenseSeries = new List<PieSeries<double>>();

                foreach (var item in expenseData)
                {
                    expenseSeries.Add(new PieSeries<double>
                    {
                        Values = new[] { (double)item.Value },
                        Name = $"{item.Key} ({item.Value:N0} ₽)"
                    });
                }
                ExpenseChart.Series = expenseSeries;

                // Динамическая высота (300 на круг + 25 на каждую строку легенды)
                ExpenseChart.Height = 300 + (expenseData.Count * 25);

                // 2. ДОХОДЫ
                var incomeData = await GetCategoryData(conn, "Income", startDate, endDate, bankId, accountId);
                var incomeSeries = new List<PieSeries<double>>();

                foreach (var item in incomeData)
                {
                    incomeSeries.Add(new PieSeries<double>
                    {
                        Values = new[] { (double)item.Value },
                        Name = $"{item.Key} ({item.Value:N0} ₽)"
                    });
                }
                IncomeChart.Series = incomeSeries;

                IncomeChart.Height = 300 + (incomeData.Count * 25);

                // 3. СУММЫ И СОВЕТ
                decimal totalExpense = expenseData.Sum(x => x.Value);
                decimal totalIncome = incomeData.Sum(x => x.Value);

                TotalIncomeText.Text = $"Общий доход: {totalIncome:N2} ₽";
                TotalExpenseText.Text = $"Общий расход: {totalExpense:N2} ₽";

                if (expenseData.Count > 0)
                {
                    var topExpense = expenseData.OrderByDescending(x => x.Value).First();
                    double percent = totalExpense > 0 ? (double)(topExpense.Value / totalExpense * 100) : 0;

                    AdviceText.Text = $"💡 Самая большая статья расходов — «{topExpense.Key}» ({topExpense.Value:N2} ₽). " +
                                      $"Это {percent:F1}% от всех трат. " +
                                      $"Совет: попробуйте сократить траты здесь на 15%, чтобы сэкономить {(topExpense.Value * 0.15m):N2} ₽.";
                }
                else
                {
                    AdviceText.Text = "💡 За выбранный период расходов не найдено. Отличная работа!";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки аналитики:\n{ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task<Dictionary<string, decimal>> GetCategoryData(
            NpgsqlConnection conn, string categoryType, DateTime start, DateTime end, int? bankId, int? accountId)
        {
            var result = new Dictionary<string, decimal>();

            string sql = @"
                SELECT c.category_name, SUM(t.amount) 
                FROM public.transactions t
                JOIN public.categories c ON c.id_category = t.id_category
                JOIN public.accounts a ON a.id_account = t.id_account
                JOIN public.banks b ON b.id_bank = a.id_bank
                WHERE a.id_user = @uid 
                  AND c.category_type = @type
                  AND t.operation_date BETWEEN @start AND @end";

            if (bankId.HasValue) sql += " AND b.id_bank = @bid";
            if (accountId.HasValue) sql += " AND a.id_account = @aid";

            sql += " GROUP BY c.category_name ORDER BY SUM(t.amount) DESC";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("uid", DbConnectionFactory.CurrentUserId);
            cmd.Parameters.AddWithValue("type", categoryType);
            cmd.Parameters.AddWithValue("start", start);
            cmd.Parameters.AddWithValue("end", end);

            if (bankId.HasValue) cmd.Parameters.AddWithValue("bid", bankId.Value);
            if (accountId.HasValue) cmd.Parameters.AddWithValue("aid", accountId.Value);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(reader.GetString(0), reader.GetDecimal(1));
            }
            return result;
        }

        private async System.Threading.Tasks.Task LoadDetailsFilters()
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                var banks = new List<dynamic> { new { id_bank = (int?)null, bank_name = "Все банки" } };

                await using (var cmd = new NpgsqlCommand("SELECT id_bank, bank_name FROM public.banks", conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        banks.Add(new { id_bank = (int?)reader.GetInt32(0), bank_name = reader.GetString(1) });
                }
                BankFilterDetailsComboBox.ItemsSource = banks;
                BankFilterDetailsComboBox.SelectedIndex = 0;
            }
            catch { }
        }

        // Обработчик выбора банка для 2-й вкладки
        private async void BankFilterDetails_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (BankFilterDetailsComboBox.SelectedValue == null) return;
            int? bankId = BankFilterDetailsComboBox.SelectedValue as int?;

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                var accounts = new List<dynamic> { new { id_account = (int?)null, Display = "Все счета" } };

                string sql = "SELECT id_account, account_name FROM public.accounts";
                if (bankId.HasValue) sql += " WHERE id_bank = @bid";

                await using var cmd = new NpgsqlCommand(sql, conn);
                if (bankId.HasValue) cmd.Parameters.AddWithValue("bid", bankId.Value);

                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        accounts.Add(new { id_account = (int?)reader.GetInt32(0), Display = reader.GetString(1) });
                }
                AccountFilterDetailsComboBox.ItemsSource = accounts;
                AccountFilterDetailsComboBox.SelectedIndex = 0;
            }
            catch { }
        }

        // Кнопка "Показать"
        private async void UpdateDetails_Click(object sender, RoutedEventArgs e)
        {
            await LoadMerchantDetails();
        }

        // Основной метод загрузки детализации (Пункт 6 ТЗ)
        private async System.Threading.Tasks.Task LoadMerchantDetails()
        {
            if (StartDatePickerDetails.SelectedDate == null || EndDatePickerDetails.SelectedDate == null) return;

            DateTime startDate = StartDatePickerDetails.SelectedDate.Value;
            DateTime endDate = EndDatePickerDetails.SelectedDate.Value.AddDays(1).AddSeconds(-1);

            int? bankId = BankFilterDetailsComboBox.SelectedValue as int?;
            int? accountId = AccountFilterDetailsComboBox.SelectedValue as int?;

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();

                // Хитрый SQL: мы используем COALESCE и LEFT JOIN с merchant_aliases.
                // Если у пользователя настроен алиас (например, Яндекс.Такси), программа возьмет красивое имя.
                // Если алиаса нет, возьмет merchant_name или description.
                string sql = @"
            SELECT 
                c.category_name, 
                COALESCE(ma.merchant_canonical, t.merchant_name, t.description, 'Без названия') AS merchant,
                COUNT(*) AS trans_count,
                SUM(t.amount) AS total_amount
            FROM public.transactions t
            JOIN public.categories c ON c.id_category = t.id_category
            JOIN public.accounts a ON a.id_account = t.id_account
            JOIN public.banks b ON b.id_bank = a.id_bank
            LEFT JOIN public.merchant_aliases ma 
                   ON ma.id_user = a.id_user 
                  AND LOWER(TRIM(ma.raw_pattern)) = LOWER(TRIM(t.merchant_name))
            WHERE a.id_user = @uid 
              AND c.category_type = 'Expense' 
              AND t.operation_date BETWEEN @start AND @end";

                if (bankId.HasValue) sql += " AND b.id_bank = @bid";
                if (accountId.HasValue) sql += " AND a.id_account = @aid";

                sql += @" GROUP BY c.category_name, COALESCE(ma.merchant_canonical, t.merchant_name, t.description, 'Без названия')
                  ORDER BY c.category_name, SUM(t.amount) DESC";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("uid", DbConnectionFactory.CurrentUserId);
                cmd.Parameters.AddWithValue("start", startDate);
                cmd.Parameters.AddWithValue("end", endDate);

                if (bankId.HasValue) cmd.Parameters.AddWithValue("bid", bankId.Value);
                if (accountId.HasValue) cmd.Parameters.AddWithValue("aid", accountId.Value);

                await using var reader = await cmd.ExecuteReaderAsync();
                var stats = new List<MerchantStatsModel>();

                while (await reader.ReadAsync())
                {
                    stats.Add(new MerchantStatsModel
                    {
                        CategoryName = reader.GetString(0),
                        MerchantName = reader.GetString(1),
                        TransactionCount = reader.GetInt32(2),
                        TotalAmount = reader.GetDecimal(3)
                    });
                }

                // Группируем по категориям для красивого отображения в DataGrid
                var cvs = new CollectionViewSource { Source = stats };
                cvs.GroupDescriptions.Add(new PropertyGroupDescription("CategoryName"));
                MerchantDetailsGrid.ItemsSource = cvs.View;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки детализации:\n{ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task LoadAdminData()
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();

                // 1. Пользователи
                var users = new List<dynamic>();
                await using (var cmd = new NpgsqlCommand("SELECT u.id_users, u.login_user, u.full_name, r.role_user FROM public.users u JOIN public.roles r ON r.id_role = u.id_role", conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                    while (await reader.ReadAsync()) users.Add(new { id_users = reader.GetInt32(0), login_user = reader.GetString(1), full_name = reader.GetString(2), role_user = reader.GetString(3) });
                AdminUsersGrid.ItemsSource = users;

                // 2. Категории
                var categories = new List<dynamic>();
                await using (var cmd = new NpgsqlCommand("SELECT id_category, category_name, category_type FROM public.categories ORDER BY category_type, category_name", conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                    while (await reader.ReadAsync()) categories.Add(new { id_category = reader.GetInt32(0), category_name = reader.GetString(1), category_type = reader.GetString(2) });
                AdminCategoriesGrid.ItemsSource = categories;

                // 3. Банки
                var banks = new List<dynamic>();
                await using (var cmd = new NpgsqlCommand("SELECT id_bank, bank_name FROM public.banks ORDER BY bank_name", conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                    while (await reader.ReadAsync()) banks.Add(new { id_bank = reader.GetInt32(0), bank_name = reader.GetString(1) });
                AdminBanksGrid.ItemsSource = banks;

                // 4. MCC Mapping (с названиями категорий и банков)
                var mccList = new List<MccMappingModel>();
                await using (var cmd = new NpgsqlCommand(@"
            SELECT m.id_mapping, m.mcc_code, m.id_category, m.id_bank, c.category_name, COALESCE(b.bank_name, 'Все банки') 
            FROM public.mcc_mapping m 
            JOIN public.categories c ON c.id_category = m.id_category 
            LEFT JOIN public.banks b ON b.id_bank = m.id_bank", conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                    while (await reader.ReadAsync()) mccList.Add(new MccMappingModel
                    {
                        id_mapping = reader.GetInt32(0),
                        mcc_code = reader.GetInt32(1),
                        id_category = reader.GetInt32(2),
                        id_bank = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                        CategoryName = reader.GetString(4),
                        BankName = reader.GetString(5)
                    });
                AdminMccGrid.ItemsSource = mccList;

                // 5. Keyword Mapping
                var kwList = new List<KeywordMappingModel>();
                await using (var cmd = new NpgsqlCommand(@"
            SELECT k.id_keyword, k.keyword, k.id_category, c.category_name 
            FROM public.keyword_mapping k 
            JOIN public.categories c ON c.id_category = k.id_category", conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                    while (await reader.ReadAsync()) kwList.Add(new KeywordMappingModel
                    {
                        id_keyword = reader.GetInt32(0),
                        keyword = reader.GetString(1),
                        id_category = reader.GetInt32(2),
                        CategoryName = reader.GetString(3)
                    });
                AdminKeywordGrid.ItemsSource = kwList;

                // 6. Category Mapping (Категории банков)
                var catMapList = new List<CategoryMappingModel>();
                await using (var cmd = new NpgsqlCommand(@"
            SELECT cm.id_category_mapping, cm.id_bank, cm.bank_category, cm.id_category, b.bank_name, c.category_name 
            FROM public.category_mapping cm 
            JOIN public.banks b ON b.id_bank = cm.id_bank 
            JOIN public.categories c ON c.id_category = cm.id_category", conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                    while (await reader.ReadAsync()) catMapList.Add(new CategoryMappingModel
                    {
                        id_category_mapping = reader.GetInt32(0),
                        id_bank = reader.GetInt32(1),
                        bank_category = reader.GetString(2),
                        id_category = reader.GetInt32(3),
                        BankName = reader.GetString(4),
                        CategoryName = reader.GetString(5)
                    });
                AdminCatMapGrid.ItemsSource = catMapList;

                // 7. Алиасы магазинов
                var aliases = new List<dynamic>();
                await using (var cmd = new NpgsqlCommand("SELECT id_alias, merchant_canonical, raw_pattern FROM public.merchant_aliases WHERE id_user = @uid", conn))
                {
                    cmd.Parameters.AddWithValue("uid", DbConnectionFactory.CurrentUserId);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) aliases.Add(new { id_alias = reader.GetInt32(0), merchant_canonical = reader.GetString(1), raw_pattern = reader.GetString(2) });
                }
                AdminAliasesGrid.ItemsSource = aliases;
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка загрузки данных админа:\n{ex.Message}"); }
        }

        private void AdminAddUser_Click(object sender, RoutedEventArgs e)
        {
            if (DbConnectionFactory.CurrentUserRole != "admin")
            {
                MessageBox.Show("У вас нет прав на это действие.");
                return;
            }
            // Админ использует то же окно регистрации
            var regWindow = new RegisterWindow { Owner = this };
            if (regWindow.ShowDialog() == true)
                _ = LoadAdminData(); // Обновляем таблицу (fire and forget)
        }

        private async void AdminAddCategory_Click(object sender, RoutedEventArgs e)
        {
            if (DbConnectionFactory.CurrentUserRole != "admin")
            {
                MessageBox.Show("У вас нет прав на это действие.");
                return;
            }

            // Открываем наше новое красивое окно
            var w = new CategoryEditWindow { Owner = this };
            if (w.ShowDialog() == true)
                await LoadAdminData();
        }

        private async void AdminDeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            if (DbConnectionFactory.CurrentUserRole != "admin")
            {
                MessageBox.Show("У вас нет прав на это действие.");
                return;
            }
            if (AdminCategoriesGrid.SelectedItem == null)
            {
                MessageBox.Show("Кликните на категорию в таблице, чтобы удалить.");
                return;
            }

            // Получаем ID через dynamic
            int id = ((dynamic)AdminCategoriesGrid.SelectedItem).id_category;

            if (MessageBox.Show("Удалить выбранную категорию? ВНИМАНИЕ: Она может использоваться в транзакциях!", "Осторожно", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                await using var cmd = new NpgsqlCommand("DELETE FROM public.categories WHERE id_category = @id", conn);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
                await LoadAdminData();
            }
            catch (PostgresException ex)
            {
                // Если категория используется в транзакциях, БД выдаст ошибку Foreign Key
                if (ex.SqlState == "23503")
                    MessageBox.Show("Нельзя удалить категорию, так как по ней уже есть транзакции. Сначала удалите транзакции.", "Ошибка");
                else MessageBox.Show(ex.MessageText);
            }
        }

        private async void AdminEditCategory_Click(object sender, RoutedEventArgs e)
        {
            if (DbConnectionFactory.CurrentUserRole != "admin")
            {
                MessageBox.Show("У вас нет прав на это действие.");
                return;
            }
            if (AdminCategoriesGrid.SelectedItem == null) { MessageBox.Show("Выберите категорию"); return; }
            int id = ((dynamic)AdminCategoriesGrid.SelectedItem).id_category;
            var w = new CategoryEditWindow(id) { Owner = this };
            if (w.ShowDialog() == true) await LoadAdminData();
        }

        private async void AdminAddBank_Click(object sender, RoutedEventArgs e)
        {
            if (DbConnectionFactory.CurrentUserRole != "admin")
            {
                MessageBox.Show("У вас нет прав на это действие.");
                return;
            }
            string name = Microsoft.VisualBasic.Interaction.InputBox("Введите название банка:", "Новый банк");
            if (string.IsNullOrWhiteSpace(name)) return;

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                await using var cmd = new NpgsqlCommand("INSERT INTO public.banks (bank_name) VALUES (@n)", conn);
                cmd.Parameters.AddWithValue("n", name);
                await cmd.ExecuteNonQueryAsync();
                await LoadAdminData();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private async System.Threading.Tasks.Task LoadGoals()
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "SELECT id_goal, goal_name, target_amount, current_amount, deadline, status FROM public.financial_goals ORDER BY created_date DESC", conn);

                await using var reader = await cmd.ExecuteReaderAsync();
                var goals = new List<GoalModel>();
                while (await reader.ReadAsync())
                {
                    goals.Add(new GoalModel
                    {
                        id_goal = reader.GetInt32(0),
                        goal_name = reader.GetString(1),
                        target_amount = reader.GetDecimal(2),
                        current_amount = reader.GetDecimal(3),
                        deadline = reader.GetDateTime(4).ToString("dd.MM.yyyy"),
                        status = reader.GetString(5)
                    });
                }
                GoalsGrid.ItemsSource = goals;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private async void AddGoal_Click(object sender, RoutedEventArgs e)
        {
            var w = new GoalEditWindow { Owner = this };
            if (w.ShowDialog() == true) await LoadGoals();
        }

        private async void EditGoal_Click(object sender, RoutedEventArgs e)
        {
            if (GoalsGrid.SelectedItem is not GoalModel selected) { MessageBox.Show("Выберите цель"); return; }
            var w = new GoalEditWindow(selected.id_goal) { Owner = this };
            if (w.ShowDialog() == true) await LoadGoals();
        }

        private async void DeleteGoal_Click(object sender, RoutedEventArgs e)
        {
            if (GoalsGrid.SelectedItem is not GoalModel selected) { MessageBox.Show("Выберите цель"); return; }
            if (MessageBox.Show("Удалить цель?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                await using var cmd = new NpgsqlCommand("DELETE FROM public.financial_goals WHERE id_goal = @id", conn);
                cmd.Parameters.AddWithValue("id", selected.id_goal);
                await cmd.ExecuteNonQueryAsync();
                await LoadGoals();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // --- MCC ---
        private async void AdminAddMcc_Click(object sender, RoutedEventArgs e)
        {
            var w = new MccEditWindow { Owner = this };
            if (w.ShowDialog() == true) await LoadAdminData();
        }
        private async void AdminEditMcc_Click(object sender, RoutedEventArgs e)
        {
            if (AdminMccGrid.SelectedItem is not MccMappingModel sel) { MessageBox.Show("Выберите строку"); return; }
            var w = new MccEditWindow(sel.id_mapping) { Owner = this };
            if (w.ShowDialog() == true) await LoadAdminData();
        }
        private async void AdminDeleteMcc_Click(object sender, RoutedEventArgs e)
        {
            if (AdminMccGrid.SelectedItem is not MccMappingModel sel) return;
            if (MessageBox.Show("Удалить MCC правило?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            await using var conn = await DbConnectionFactory.OpenAsync();
            await using var cmd = new NpgsqlCommand("DELETE FROM public.mcc_mapping WHERE id_mapping = @id", conn);
            cmd.Parameters.AddWithValue("id", sel.id_mapping);
            await cmd.ExecuteNonQueryAsync();
            await LoadAdminData();
        }

        // --- Keyword ---
        private async void AdminAddKeyword_Click(object sender, RoutedEventArgs e)
        {
            var w = new KeywordEditWindow { Owner = this };
            if (w.ShowDialog() == true) await LoadAdminData();
        }
        private async void AdminEditKeyword_Click(object sender, RoutedEventArgs e)
        {
            if (AdminKeywordGrid.SelectedItem is not KeywordMappingModel sel) { MessageBox.Show("Выберите строку"); return; }
            var w = new KeywordEditWindow(sel.id_keyword) { Owner = this };
            if (w.ShowDialog() == true) await LoadAdminData();
        }
        private async void AdminDeleteKeyword_Click(object sender, RoutedEventArgs e)
        {
            if (AdminKeywordGrid.SelectedItem is not KeywordMappingModel sel) return;
            if (MessageBox.Show("Удалить ключевое слово?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            await using var conn = await DbConnectionFactory.OpenAsync();
            await using var cmd = new NpgsqlCommand("DELETE FROM public.keyword_mapping WHERE id_keyword = @id", conn);
            cmd.Parameters.AddWithValue("id", sel.id_keyword);
            await cmd.ExecuteNonQueryAsync();
            await LoadAdminData();
        }

        // --- Category Mapping ---
        private async void AdminAddCatMap_Click(object sender, RoutedEventArgs e)
        {
            var w = new CategoryMappingEditWindow { Owner = this };
            if (w.ShowDialog() == true) await LoadAdminData();
        }
        private async void AdminEditCatMap_Click(object sender, RoutedEventArgs e)
        {
            if (AdminCatMapGrid.SelectedItem is not CategoryMappingModel sel) { MessageBox.Show("Выберите строку"); return; }
            var w = new CategoryMappingEditWindow(sel.id_category_mapping) { Owner = this };
            if (w.ShowDialog() == true) await LoadAdminData();
        }
        private async void AdminDeleteCatMap_Click(object sender, RoutedEventArgs e)
        {
            if (AdminCatMapGrid.SelectedItem is not CategoryMappingModel sel) return;
            if (MessageBox.Show("Удалить маппинг?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            await using var conn = await DbConnectionFactory.OpenAsync();
            await using var cmd = new NpgsqlCommand("DELETE FROM public.category_mapping WHERE id_category_mapping = @id", conn);
            cmd.Parameters.AddWithValue("id", sel.id_category_mapping);
            await cmd.ExecuteNonQueryAsync();
            await LoadAdminData();
        }
        // --- Aliases ---
        private async void AdminAddAlias_Click(object sender, RoutedEventArgs e)
        {
            var w = new MerchantAliasEditWindow { Owner = this };
            if (w.ShowDialog() == true) await LoadAdminData();
        }
        private async void AdminEditAlias_Click(object sender, RoutedEventArgs e)
        {
            if (AdminAliasesGrid.SelectedItem == null) { MessageBox.Show("Выберите строку"); return; }
            int id = ((dynamic)AdminAliasesGrid.SelectedItem).id_alias;
            var w = new MerchantAliasEditWindow(id) { Owner = this };
            if (w.ShowDialog() == true) await LoadAdminData();
        }
        private async void AdminDeleteAlias_Click(object sender, RoutedEventArgs e)
        {
            if (AdminAliasesGrid.SelectedItem == null) return;
            int id = ((dynamic)AdminAliasesGrid.SelectedItem).id_alias;

            if (MessageBox.Show("Удалить алиас?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            await using var conn = await DbConnectionFactory.OpenAsync();
            await using var cmd = new NpgsqlCommand("DELETE FROM public.merchant_aliases WHERE id_alias = @id", conn);
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
            await LoadAdminData();
        }

        private void ChartsScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Проверяем, зажат ли Ctrl
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                e.Handled = true; // Отключаем стандартную прокрутку

                // Получаем текущий масштаб
                double currentZoom = ChartsScaleTransform.ScaleX;

                // Вычисляем новый (шаг 0.1)
                double newZoom = e.Delta > 0 ? currentZoom + 0.1 : currentZoom - 0.1;

                // Ограничиваем масштаб от 0.5 (50%) до 3.0 (300%), чтобы не сломать верстку
                newZoom = Math.Max(0.5, Math.Min(3.0, newZoom));

                ChartsScaleTransform.ScaleX = newZoom;
                ChartsScaleTransform.ScaleY = newZoom;
            }
        }

    }
}