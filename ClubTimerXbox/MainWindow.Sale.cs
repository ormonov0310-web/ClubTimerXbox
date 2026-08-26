using System.Linq;
using System.Windows;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class MainWindow
    {
        private void SaleButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSaleWindowFromMainScreen();
        }

        private void OpenSaleWindowFromMainScreen()
        {
            var saleWindow = new SaleWindow
            {
                Owner = this
            };

            bool? result = saleWindow.ShowDialog();

            if (result != true ||
                saleWindow.SelectedSaleItem == null ||
                saleWindow.Quantity <= 0)
            {
                return;
            }

            if (saleWindow.ResultType == SaleWindowResultType.SoldNow)
            {
                SellNowFromMainScreen(
                    saleWindow.SelectedSaleItem,
                    saleWindow.Quantity,
                    saleWindow.TotalAmount
                );

                UpdateCashShortText();
                return;
            }

            if (saleWindow.ResultType == SaleWindowResultType.AttachToPlace)
            {
                AttachSaleToPlaceFromMainScreen(
                    saleWindow.SelectedSaleItem,
                    saleWindow.Quantity,
                    saleWindow.TotalAmount
                );

                UpdateCashShortText();
            }
        }

        private void SellNowFromMainScreen(SaleItem item, int quantity, int totalAmount)
        {
            string employeeName = EmployeeService.CurrentEmployee?.Name ?? "Неизвестно";

            if (item.Type == SaleItemType.Product &&
                ProductStockService.GetQuantity(item.Name) < quantity)
            {
                DecreaseProductStockIfNeeded(item, quantity);
                return;
            }

            var checkoutWindow = new CashCheckoutWindow(
                employeeName,
                "Продажа товара / услуги",
                new List<CheckoutItem>
                {
                    new CheckoutItem
                    {
                        Name = item.Name,
                        Quantity = quantity,
                        UnitPrice = item.SalePrice,
                        PurchasePrice = item.PurchasePrice,
                        Category = item.Type == SaleItemType.Product ? "Товар" : "Услуга",
                        ItemType = item.Type.ToString(),
                        CreatedByEmployeeName = employeeName,
                        SourceCreatedAt = ClubClock.Current.LocalNow
                    }
                })
            {
                Owner = this
            };

            if (checkoutWindow.ShowDialog() != true || checkoutWindow.PaymentRecord == null)
                return;

            if (!DecreaseProductStockIfNeeded(item, quantity))
                return;

            PaymentService.AddPayment(checkoutWindow.PaymentRecord);

            CashService.AddProductOrServiceIncome(
                employeeName: employeeName,
                title: item.Name,
                description:
                    $"{FormatSaleItemType(item)} продан сразу. " +
                    $"Количество: {quantity}. " +
                    $"Цена за 1 шт: {item.SalePrice} сом. " +
                    $"Итого: {totalAmount} сом.",
                amount: totalAmount,
                paymentRecordId: checkoutWindow.PaymentRecord.Id
            );

            MessageBox.Show(
                $"{item.Name}\n\n" +
                $"Количество: {quantity}\n" +
                $"Итого: {totalAmount} сом\n\n" +
                $"Продажа добавлена в кассу товаров/услуг.",
                "Продано сразу"
            );
        }

        private void AttachSaleToPlaceFromMainScreen(SaleItem item, int quantity, int totalAmount)
        {
            var activePlaces = _places
                .Where(place => place.IsBusy && !place.IsCalculating)
                .ToList();

            if (activePlaces.Count == 0)
            {
                MessageBox.Show(
                    "Нет активных ТВ или рулей.\n\n" +
                    "Сначала откройте сеанс, потом оформите товар/услугу на ТВ.",
                    "Оформить на ТВ"
                );

                return;
            }

            var selectWindow = new ActivePlaceSelectWindow(activePlaces)
            {
                Owner = this
            };

            bool? result = selectWindow.ShowDialog();

            if (result != true || selectWindow.SelectedPlace == null)
                return;

            if (!DecreaseProductStockIfNeeded(item, quantity))
                return;

            string employeeName = EmployeeService.CurrentEmployee?.Name ?? "Неизвестно";
            string placeName = selectWindow.SelectedPlace.Name;

            ActionLogService.AddSaleToActiveSession(
                placeName: placeName,
                employeeName: employeeName,
                item: item,
                quantity: quantity
            );

            DrawPlaces();
            SaveActivePlacesToStorage();

            MessageBox.Show(
                $"{item.Name}\n\n" +
                $"Количество: {quantity}\n" +
                $"Итого: {totalAmount} сом\n\n" +
                $"Оформлено на: {placeName}\n" +
                $"Оплата будет добавлена при закрытии сеанса.",
                "Оформлено на ТВ"
            );
        }

        private bool DecreaseProductStockIfNeeded(SaleItem item, int quantity)
        {
            if (item.Type != SaleItemType.Product)
                return true;

            int stock = ProductStockService.GetQuantity(item.Name);

            if (stock < quantity)
            {
                MessageBox.Show(
                    $"{item.Name}\n\n" +
                    $"На складе осталось: {stock} шт\n" +
                    $"Нужно: {quantity} шт\n\n" +
                    "Операция отменена.",
                    "Недостаточно товара"
                );

                return false;
            }

            bool decreased = ProductStockService.Decrease(item.Name, quantity);

            if (!decreased)
            {
                MessageBox.Show(
                    "Не удалось уменьшить остаток товара.",
                    "Склад"
                );

                return false;
            }

            return true;
        }

        private string FormatSaleItemType(SaleItem item)
        {
            return item.Type == SaleItemType.Product ? "Товар" : "Услуга";
        }
    }
}
