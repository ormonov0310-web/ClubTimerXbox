using System;
using System.Collections.Generic;

namespace ClubTimerXbox.Models
{
    public enum CashReportSection
    {
        Games,
        ProductsAndServices,
        Employees,
        Expenses
    }

    public enum CashReportPeriodMode
    {
        Day,
        Month,
        CustomPeriod
    }

    public enum CashReportViewMode
    {
        Records,
        Days,
        Places,
        Items,
        Employees,
        Categories
    }

    public class CashReportFilter
    {
        public CashReportSection Section { get; set; } = CashReportSection.Games;

        public CashReportPeriodMode PeriodMode { get; set; } = CashReportPeriodMode.Day;

        public CashReportViewMode ViewMode { get; set; } = CashReportViewMode.Records;

        public DateTime SelectedDay { get; set; } = DateTime.Today;

        public int SelectedYear { get; set; } = DateTime.Today.Year;

        public int SelectedMonth { get; set; } = DateTime.Today.Month;

        public DateTime PeriodStart { get; set; } = DateTime.Today;

        public DateTime PeriodEnd { get; set; } = DateTime.Today;
    }

    public class CashReportSummary
    {
        public string Title { get; set; } = "";

        public int TotalAmount { get; set; }

        public int CashAmount { get; set; }

        public int MBankAmount { get; set; }

        public int RecordsCount { get; set; }
    }

    public class CashReportRow
    {
        public string Title { get; set; } = "";

        public string Subtitle { get; set; } = "";

        public string TimeText { get; set; } = "";

        public int TotalAmount { get; set; }

        public int CashAmount { get; set; }

        public int MBankAmount { get; set; }

        public string EmployeeName { get; set; } = "";

        public string PlaceName { get; set; } = "";

        public string Category { get; set; } = "";

        public bool IsExpense { get; set; }
    }

    public class CashReportResult
    {
        public CashReportSummary Summary { get; set; } = new CashReportSummary();

        public List<CashReportRow> Rows { get; set; } = new List<CashReportRow>();
    }
}
