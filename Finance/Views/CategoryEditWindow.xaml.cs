using System;
using System.Windows;
using Npgsql;
using Finance.Helpers;

namespace Finance.Views
{
    public partial class CategoryEditWindow : Window
    {
        private int? _editId = null;

        public CategoryEditWindow(int? categoryId = null)
        {
            InitializeComponent();
            _editId = categoryId;
            if (_editId.HasValue) LoadData();
        }

        private async void LoadData()
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                await using var cmd = new NpgsqlCommand("SELECT category_name, category_type FROM public.categories WHERE id_category = @id", conn);
                cmd.Parameters.AddWithValue("id", _editId.Value);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    NameBox.Text = reader.GetString(0);
                    string type = reader.GetString(1);
                    foreach (System.Windows.Controls.ComboBoxItem item in TypeComboBox.Items)
                        if (item.Content.ToString() == type) TypeComboBox.SelectedItem = item;
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text)) return;
            string type = ((System.Windows.Controls.ComboBoxItem)TypeComboBox.SelectedItem).Content.ToString();

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                string sql = _editId.HasValue
                    ? "UPDATE public.categories SET category_name=@n, category_type=@t WHERE id_category=@id"
                    : "INSERT INTO public.categories (category_name, category_type) VALUES (@n, @t)";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("n", NameBox.Text.Trim());
                cmd.Parameters.AddWithValue("t", type);
                if (_editId.HasValue) cmd.Parameters.AddWithValue("id", _editId.Value);

                await cmd.ExecuteNonQueryAsync();
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }
    }
}