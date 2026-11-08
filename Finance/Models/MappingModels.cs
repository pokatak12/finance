namespace Finance.Models
{
    public class MccMappingModel
    {
        public int id_mapping { get; set; }
        public int mcc_code { get; set; }
        public int id_category { get; set; }
        public int? id_bank { get; set; }
        public string BankName { get; set; } = "Все банки";
        public string CategoryName { get; set; } = "";
    }

    public class KeywordMappingModel
    {
        public int id_keyword { get; set; }
        public string keyword { get; set; } = "";
        public int id_category { get; set; }
        public string CategoryName { get; set; } = "";
    }

    public class CategoryMappingModel
    {
        public int id_category_mapping { get; set; }
        public int id_bank { get; set; }
        public string bank_category { get; set; } = "";
        public int id_category { get; set; }
        public string BankName { get; set; } = "";
        public string CategoryName { get; set; } = "";
    }
}