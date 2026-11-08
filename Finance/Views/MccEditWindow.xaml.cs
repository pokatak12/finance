using System;
using System.Windows;
using Npgsql;
using Finance.Helpers;

namespace Finance.Views
{
    public partial class MccEditWindow : Window
    {
        private int? _editId = null;

        public MccEditWindow(int? id = null)
        {
            InitializeComponent();
            _editId = id;
            Loaded += async (s, e) => await LoadComboBoxes();
        }

        private async System.Threading.Tasks.Task LoadComboBoxes()
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();

                // Категории
                var cats = new System.Collections.Generic.List<dynamic>();
                await using (var cmd = new NpgsqlCommand("SELECT id_category, category_name FROM public.categories ORDER BY category_name", conn))
                await using (var r = await cmd.ExecuteReaderAsync())
                    while (await r.ReadAsync()) cats.Add(new { id_category = r.GetInt32(0), category_name = r.GetString(1) });
                CategoryComboBox.ItemsSource = cats;
                CategoryComboBox.SelectedIndex = 0;

                // Банки (с опцией "Все")
                var banks = new System.Collections.Generic.List<dynamic> { new { id_bank = (int?)null, bank_name = "Все банки" } };
                await using (var cmd = new NpgsqlCommand("SELECT id_bank, bank_name FROM public.banks ORDER BY bank_name", conn))
                await using (var r = await cmd.ExecuteReaderAsync())
                    while (await r.ReadAsync()) banks.Add(new { id_bank = (int?)r.GetInt32(0), bank_name = r.GetString(1) });
                BankComboBox.ItemsSource = banks;
                BankComboBox.SelectedIndex = 0;

                if (_editId.HasValue)
                {
                    await using var cmd = new NpgsqlCommand("SELECT mcc_code, id_category, id_bank FROM public.mcc_mapping WHERE id_mapping = @id", conn);
                    cmd.Parameters.AddWithValue("id", _editId.Value);
                    await using var r = await cmd.ExecuteReaderAsync();
                    if (await r.ReadAsync())
                    {
                        MccCodeBox.Text = r.GetInt32(0).ToString();
                        CategoryComboBox.SelectedValue = r.GetInt32(1);
                        BankComboBox.SelectedValue = r.IsDBNull(2) ? (int?)null : r.GetInt32(2);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(MccCodeBox.Text, out int mcc) || mcc <= 0) { MessageBox.Show("Введите корректный MCC код"); return; }

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                string sql = _editId.HasValue
                    ? "UPDATE public.mcc_mapping SET mcc_code=@mcc, id_category=@cat, id_bank=@bank WHERE id_mapping=@id"
                    : "INSERT INTO public.mcc_mapping (mcc_code, id_category, id_bank) VALUES (@mcc, @cat, @bank)";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("mcc", mcc);
                cmd.Parameters.AddWithValue("cat", (int)CategoryComboBox.SelectedValue);
                cmd.Parameters.AddWithValue("bank", (object)BankComboBox.SelectedValue ?? DBNull.Value);
                if (_editId.HasValue) cmd.Parameters.AddWithValue("id", _editId.Value);

                await cmd.ExecuteNonQueryAsync();
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка (возможно, дубликат):\n" + ex.Message); }
        }
    }
}