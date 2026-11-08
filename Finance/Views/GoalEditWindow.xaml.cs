using System;
using System.Windows;
using System.Windows.Controls;
using Npgsql;
using Finance.Helpers;

namespace Finance.Views
{
    public partial class GoalEditWindow : Window
    {
        private int? _editId = null;

        public GoalEditWindow(int? goalId = null)
        {
            InitializeComponent();
            _editId = goalId;
            DeadlinePicker.SelectedDate = DateTime.Today.AddMonths(6);
            if (_editId.HasValue) LoadData();
        }

        private async void LoadData()
        {
            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                await using var cmd = new NpgsqlCommand("SELECT goal_name, target_amount, current_amount, deadline, status FROM public.financial_goals WHERE id_goal = @id", conn);
                cmd.Parameters.AddWithValue("id", _editId.Value);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    NameBox.Text = reader.GetString(0);
                    TargetAmountBox.Text = reader.GetDecimal(1).ToString("N0");
                    CurrentAmountBox.Text = reader.GetDecimal(2).ToString("N0");
                    DeadlinePicker.SelectedDate = reader.GetDateTime(3);

                    string status = reader.GetString(4);
                    foreach (ComboBoxItem item in StatusComboBox.Items)
                        if (item.Content.ToString() == status) StatusComboBox.SelectedItem = item;
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // ФОРМАТИРОВАНИЕ ЧИСЕЛ (1000 -> 1 000)
        private void AmountBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;

            // Удаляем все, кроме цифр
            string rawText = tb.Text.Replace(" ", "").Replace(",", "").Replace(".", "");
            if (string.IsNullOrEmpty(rawText)) return;

            // Пытаемся распарсить и отформатировать обратно с пробелами
            if (decimal.TryParse(rawText, out decimal val))
            {
                // Запоминаем позицию курсора (чтобы он не прыгал в конец)
                int caretIndex = tb.CaretIndex;

                tb.Text = val.ToString("N0");

                // Восстанавливаем курсор
                if (caretIndex < tb.Text.Length) tb.CaretIndex = caretIndex;
                else tb.CaretIndex = tb.Text.Length;
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            // Очищаем пробелы перед сохранением в БД
            string targetStr = TargetAmountBox.Text.Replace(" ", "");
            string currentStr = CurrentAmountBox.Text.Replace(" ", "");

            if (!decimal.TryParse(targetStr, out decimal target) || target <= 0) { MessageBox.Show("Неверная целевая сумма"); return; }
            if (!decimal.TryParse(currentStr, out decimal current) || current < 0) { MessageBox.Show("Неверная накопленная сумма"); return; }

            string status = ((ComboBoxItem)StatusComboBox.SelectedItem).Content.ToString();

            try
            {
                await using var conn = await DbConnectionFactory.OpenAsync();
                string sql = _editId.HasValue
                    ? "UPDATE public.financial_goals SET goal_name=@n, target_amount=@t, current_amount=@c, deadline=@d, status=@s WHERE id_goal=@id"
                    : "INSERT INTO public.financial_goals (id_user, goal_name, target_amount, current_amount, created_date, deadline, status) VALUES (@uid, @n, @t, @c, NOW(), @d, @s)";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("n", NameBox.Text.Trim());
                cmd.Parameters.AddWithValue("t", target);
                cmd.Parameters.AddWithValue("c", current);
                cmd.Parameters.AddWithValue("d", DeadlinePicker.SelectedDate ?? DateTime.Today.AddMonths(1));
                cmd.Parameters.AddWithValue("s", status);

                if (_editId.HasValue) cmd.Parameters.AddWithValue("id", _editId.Value);
                else cmd.Parameters.AddWithValue("uid", DbConnectionFactory.CurrentUserId);

                await cmd.ExecuteNonQueryAsync();
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }
    }
}