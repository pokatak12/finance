using System;
using System.Collections.Generic;
using System.Text;

namespace Finance.Models
{
    public class AccountModel
    {
        public bool IsSelected { get; set; }
        public int RowNumber { get; set; }
        public int id_account { get; set; }
        public string bank_name { get; set; } = "";
        public string account_name { get; set; } = "";
        public string account_number { get; set; } = "";
        public string currency { get; set; } = "";
    }
}
