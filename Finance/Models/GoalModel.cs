namespace Finance.Models
{
    public class GoalModel
    {
        public int id_goal { get; set; }
        public string goal_name { get; set; } = "";
        public decimal target_amount { get; set; }
        public decimal current_amount { get; set; }
        public string deadline { get; set; } = "";
        public string status { get; set; } = "";
        public double ProgressPercent => target_amount > 0 ? (double)(current_amount / target_amount * 100) : 0;
    }
}