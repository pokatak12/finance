using System;
using System.Windows;
using Npgsql;
using Finance.Helpers;

namespace Finance.Views
{
    public partial class KeywordEditWindow : Window
    {
        private int? _editId = null;

        public KeywordEditWindow(int? id = null)
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

                // Загружаем категории
                var cats = new System.Collections.Generic.List<dynamic>();
                await using (var cmd = new NpgsqlCommand("SELECT id_category, category_name FROM public.categories ORDER BY category_name", conn))
                await using (var r = await cmd.ExecuteReaderAsync())
                    while (await r.ReadAsync()) cats.Add(new { id_category = r.GetInt32(0), category_name = r.GetString(1) });
                CategoryComboBox.ItemsSource = cats;
                CategoryComboBox.SelectedIndex = 0;

                // Если это редактирование
                if (_editId.HasValue)
                {
                    await using var cmd = new NpgsqlCommand("SELECT keyword, id_category FROM public.keyword_mapping WHERE id_keyword = @id", conn);
                    cmd.Parameters.AddWithValue("id", _editId.Value);
                    await using var r = await cmd.ExecuteReaderAsync();
                    if (await r.ReadAsync())
                    {
                        KeywordBox.Text = r.GetString(0);
                        CategoryComboBox.SelectedValue = r.GetInt32(1);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(KeywordBox.Text)) { MessageBox.Show("Введите ключевое слово"); return; }

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                string sql = _editId.HasValue
                    ? "UPDATE public.keyword_mapping SET keyword=@kw, id_category=@cat WHERE id_keyword=@id"
                    : "INSERT INTO public.keyword_mapping (keyword, id_category) VALUES (@kw, @cat)";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("kw", KeywordBox.Text.Trim().ToLower());
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