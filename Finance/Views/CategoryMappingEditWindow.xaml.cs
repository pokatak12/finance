using System;
using System.Windows;
using Npgsql;
using Finance.Helpers;

namespace Finance.Views
{
    public partial class CategoryMappingEditWindow : Window
    {
        private int? _editId = null;

        public CategoryMappingEditWindow(int? id = null)
        {
            InitializeComponent();
            _editId = id;
            Loaded += async (s, e) => await LoadData();
        }

        private async System.Threading.Tasks.Task LoadData()
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();

                // Загружаем банки
                var banks = new System.Collections.Generic.List<dynamic>();
                await using (var cmd = new NpgsqlCommand("SELECT id_bank, bank_name FROM public.banks ORDER BY bank_name", conn))
                await using (var r = await cmd.ExecuteReaderAsync())
                    while (await r.ReadAsync()) banks.Add(new { id_bank = r.GetInt32(0), bank_name = r.GetString(1) });
                BankComboBox.ItemsSource = banks;
                BankComboBox.SelectedIndex = 0;

                // Загружаем категории системы
                var cats = new System.Collections.Generic.List<dynamic>();
                await using (var cmd = new NpgsqlCommand("SELECT id_category, category_name FROM public.categories ORDER BY category_name", conn))
                await using (var r = await cmd.ExecuteReaderAsync())
                    while (await r.ReadAsync()) cats.Add(new { id_category = r.GetInt32(0), category_name = r.GetString(1) });
                CategoryComboBox.ItemsSource = cats;
                CategoryComboBox.SelectedIndex = 0;

                // Если это редактирование — заполняем поля
                if (_editId.HasValue)
                {
                    await using var cmd = new NpgsqlCommand("SELECT id_bank, bank_category, id_category FROM public.category_mapping WHERE id_category_mapping = @id", conn);
                    cmd.Parameters.AddWithValue("id", _editId.Value);
                    await using var r = await cmd.ExecuteReaderAsync();
                    if (await r.ReadAsync())
                    {
                        BankComboBox.SelectedValue = r.GetInt32(0);
                        BankCategoryBox.Text = r.GetString(1);
                        CategoryComboBox.SelectedValue = r.GetInt32(2);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(BankCategoryBox.Text)) { MessageBox.Show("Введите название категории банка"); return; }

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                string sql = _editId.HasValue
                    ? "UPDATE public.category_mapping SET id_bank=@bank, bank_category=@name, id_category=@cat WHERE id_category_mapping=@id"
                    : "INSERT INTO public.category_mapping (id_bank, bank_category, id_category) VALUES (@bank, @name, @cat)";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("bank", (int)BankComboBox.SelectedValue);
                cmd.Parameters.AddWithValue("name", BankCategoryBox.Text.Trim());
                cmd.Parameters.AddWithValue("cat", (int)CategoryComboBox.SelectedValue);
                if (_editId.HasValue) cmd.Parameters.AddWithValue("id", _editId.Value);

                await cmd.ExecuteNonQueryAsync();
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка (возможно, дубликат):\n" + ex.Message); }
        }
    }
}