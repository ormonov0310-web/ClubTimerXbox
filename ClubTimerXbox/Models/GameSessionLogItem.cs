using System;
using System.Collections.Generic;

namespace ClubTimerXbox.Models
{
    public class GameSessionLogItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string PlaceName { get; set; } = "";

        public string StartedByEmployeeName { get; set; } = "";
        public DateTime StartedAt { get; set; } = DateTime.Now;

        public bool IsOpenMode { get; set; }

        public string TariffText { get; set; } = "";
        public int PaidAmount { get; set; }

        // Дополнительные действия внутри сеанса:
        // штраф, добавление времени, пересадка и т.д.
        public List<GameSessionExtraLine> ExtraLines { get; set; } = new List<GameSessionExtraLine>();

        // Товары/услуги, которые клиент взял во время игры,
        // но оплатит потом вместе с закрытием сеанса.
        public List<GameSessionSaleLine> SaleLines { get; set; } = new List<GameSessionSaleLine>();

        public string ClosedByEmployeeName { get; set; } = "";
        public DateTime? ClosedAt { get; set; }

        public int ActualPlayedAmount { get; set; }
        public int RefundAmount { get; set; }
        public int NeedToPayAmount { get; set; }
        public int CashIncomeAmount { get; set; }

        // Отдельно сумма товаров/услуг внутри этой сессии.
        public int ProductsAndServicesAmount { get; set; }

        // Общая сумма к оплате клиентом:
        // время + товары/услуги.
        public int TotalToPayAmount { get; set; }

        public string IncomeEmployeeName { get; set; } = "";

        public bool IsClosed { get; set; }
    }

    public class GameSessionExtraLine
    {
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string Type { get; set; } = "";

        public string EmployeeName { get; set; } = "";

        public string Description { get; set; } = "";

        public int Amount { get; set; }
    }

    public class GameSessionSaleLine
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Кто оформил товар/услугу.
        public string EmployeeName { get; set; } = "";

        public string ItemName { get; set; } = "";

        public SaleItemType ItemType { get; set; }

        public int UnitPrice { get; set; }

        public int Quantity { get; set; }

        public int TotalAmount { get; set; }

        // Пока товар/услуга оформлены на сеанс, но ещё не оплачены.
        // При закрытии сеанса станет true.
        public bool IsPaid { get; set; }
    }
}