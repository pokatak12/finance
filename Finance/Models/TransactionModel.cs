using System;
using System.Collections.Generic;
using System.Text;

namespace Finance.Models
{
    public class TransactionModel
    {
        public bool IsSelected { get; set; }
        public int RowNumber { get; set; }
        public int id_transaction { get; set; }
        public DateTime operation_date { get; set; }
        public string category_type { get; set; } = "";
        public string category_name { get; set; } = "";
        public decimal amount { get; set; }
        public string account_name { get; set; } = "";
        public string bank_name { get; set; } = "";
        public string merchant_name { get; set; } = "";
    }
}