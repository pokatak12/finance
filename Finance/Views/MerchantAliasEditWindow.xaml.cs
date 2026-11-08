using System;
using System.Windows;
using Npgsql;
using Finance.Helpers;

namespace Finance.Views
{
    public partial class MerchantAliasEditWindow : Window
    {
        private int? _editId = null;

        public MerchantAliasEditWindow(int? id = null)
        {
            InitializeComponent();
            _editId = id;
            if (_editId.HasValue) Loaded += async (s, e) => await LoadData();
        }

        private async System.Threading.Tasks.Task LoadData()
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                await using var cmd = new NpgsqlCommand("SELECT merchant_canonical, raw_pattern FROM public.merchant_aliases WHERE id_alias = @id", conn);
                cmd.Parameters.AddWithValue("id", _editId.Value);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    CanonicalBox.Text = r.GetString(0);
                    RawPatternBox.Text = r.GetString(1);
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CanonicalBox.Text) || string.IsNullOrWhiteSpace(RawPatternBox.Text))
            {
                MessageBox.Show("Заполните оба поля."); return;
            }

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                string sql = _editId.HasValue
                    ? "UPDATE public.merchant_aliases SET merchant_canonical=@canon, raw_pattern=@raw WHERE id_alias=@id"
                    : "INSERT INTO public.merchant_aliases (id_user, merchant_canonical, raw_pattern) VALUES (@uid, @canon, @raw)";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("canon", CanonicalBox.Text.Trim());
                cmd.Parameters.AddWithValue("raw", RawPatternBox.Text.Trim().ToLower());
                if (_editId.HasValue) cmd.Parameters.AddWithValue("id", _editId.Value);
                else cmd.Parameters.AddWithValue("uid", DbConnectionFactory.CurrentUserId);

                await cmd.ExecuteNonQueryAsync();
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка (возможно, дубликат):\n" + ex.Message); }
        }
    }
}