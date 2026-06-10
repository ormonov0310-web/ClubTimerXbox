using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class FirebaseSyncService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        private static string BaseUrl =>
            FirebaseSettings.DatabaseUrl.TrimEnd('/');

        public static async Task PushCurrentStateAsync(List<ClubPlace> places)
        {
            try
            {
                DateTime todayStart = DateTime.Today;
                DateTime tomorrowStart = todayStart.AddDays(1);

                DateTime monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                DateTime nextMonthStart = monthStart.AddMonths(1);

                int cashToday = CashService.GetCashIncomeTotalByPeriod(todayStart, tomorrowStart);

                int gamesToday = CashService.GetTotalByPeriodAndCategory(
                    todayStart,
                    tomorrowStart,
                    "Игры"
                );

                int productsToday = CashService.GetTotalByPeriodAndCategory(
                    todayStart,
                    tomorrowStart,
                    "Товары и услуги"
                );

                int shortagesToday = CashService.GetShortageTotalByPeriod(
                    todayStart,
                    tomorrowStart
                );

                int expensesToday = CashService.GetExpenseTotalByPeriod(
                    todayStart,
                    tomorrowStart
                );

                int cashExpenseToday = CashService.GetCashExpenseTotalByPeriod(
                    todayStart,
                    tomorrowStart
                );

                int cashlessExpenseToday = CashService.GetCashlessExpenseTotalByPeriod(
                    todayStart,
                    tomorrowStart
                );

                int cashlessToday = CashlessService.GetAmountForToday();
                int expectedCashToday = CashlessService.GetExpectedCashForToday();

                int cashMonth = CashService.GetCashIncomeTotalByPeriod(
                    monthStart,
                    nextMonthStart
                );

                int cashlessMonth = CashlessService.GetAmountByPeriod(
                    monthStart,
                    nextMonthStart
                );

                int expensesMonth = CashService.GetExpenseTotalByPeriod(
                    monthStart,
                    nextMonthStart
                );

                int cashExpenseMonth = CashService.GetCashExpenseTotalByPeriod(
                    monthStart,
                    nextMonthStart
                );

                int cashlessExpenseMonth = CashService.GetCashlessExpenseTotalByPeriod(
                    monthStart,
                    nextMonthStart
                );

                int salaryToday = CashService.GetSalaryTotalByPeriod(
                    todayStart,
                    tomorrowStart
                );

                int salaryMonth = CashService.GetSalaryTotalByPeriod(
                    monthStart,
                    nextMonthStart
                );

                int stockPurchaseToday = StockPurchaseService.GetTotalByPeriod(
                    todayStart,
                    tomorrowStart
                );

                int stockPurchaseMonth = StockPurchaseService.GetTotalByPeriod(
                    monthStart,
                    nextMonthStart
                );

                var expenseCategories = CashService.GetDefaultExpenseCategories();

                var expensesByCategory = expenseCategories
                    .Select(category => new
                    {
                        category,
                        total = CashService.GetExpenseTotalByPeriodAndExpenseCategory(
                            monthStart,
                            nextMonthStart,
                            category
                        )
                    })
                    .ToList();

                var employees = EmployeeService.GetAllEmployees();

                var salaryRecords = CashService.GetSalaryRecordsByPeriod(
                        monthStart,
                        nextMonthStart
                    )
                    .Take(100)
                    .Select(record => new
                    {
                        createdAt = record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        employeeName = record.RelatedEmployeeName,
                        amount = record.Amount,
                        paymentMethod = record.PaymentMethod,
                        description = record.Description,
                        addedBy = record.EmployeeName
                    })
                    .ToList();

                var stockPurchases = StockPurchaseService.GetPurchasesByPeriod(
                        monthStart,
                        nextMonthStart
                    )
                    .Take(100)
                    .Select(purchase => new
                    {
                        createdAt = purchase.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        addedBy = purchase.AddedBy,
                        note = purchase.Note,
                        totalAmount = purchase.TotalAmount,
                        items = purchase.Items.Select(item => new
                        {
                            productName = item.ProductName,
                            quantity = item.Quantity,
                            purchasePrice = item.PurchasePrice,
                            salePrice = item.SalePrice,
                            totalAmount = item.TotalAmount
                        }).ToList()
                    })
                    .ToList();

                var stockItems = ProductStockService.StockItems
                    .Select(item => new
                    {
                        itemType = "Product",
                        productName = item.ProductName,
                        name = item.ProductName,
                        quantity = item.Quantity,
                        purchasePrice = item.PurchasePrice,
                        salePrice = item.SalePrice,
                        minimumQuantity = item.MinimumQuantity,
                        isLowStock = ProductStockService.IsLowStock(item.ProductName),
                        updatedAt = item.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                    .ToList();

                var serviceItems = CustomServiceService.GetAllServices()
                    .Select(item => new
                    {
                        itemType = "Service",
                        productName = item.Name,
                        name = item.Name,
                        quantity = 0,
                        purchasePrice = 0,
                        salePrice = item.SalePrice,
                        minimumQuantity = 0,
                        isLowStock = false,
                        updatedAt = ""
                    })
                    .ToList();

                var saleItems = stockItems
                    .Cast<object>()
                    .Concat(serviceItems.Cast<object>())
                    .ToList();

                var data = new
                {
                    updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),

                    cash = new
                    {
                        cashToday,
                        gamesToday,
                        productsToday,
                        shortagesToday,
                        expensesToday,
                        cashExpenseToday,
                        cashlessExpenseToday,
                        cashlessToday,
                        expectedCashToday,

                        cashMonth,
                        cashlessMonth,
                        expensesMonth,
                        cashExpenseMonth,
                        cashlessExpenseMonth,

                        salaryToday,
                        salaryMonth,

                        stockPurchaseToday,
                        stockPurchaseMonth
                    },

                    places = places.Select(place => new
                    {
                        name = place.Name,
                        type = place.Type.ToString(),
                        isBusy = place.IsBusy,
                        isOpenMode = place.IsOpenMode,
                        isCalculating = place.IsCalculating,
                        paidAmount = place.PaidAmount,
                        remainingSeconds = place.RemainingSeconds,
                        startedByEmployeeName = place.StartedByEmployeeName,
                        incomeEmployeeName = place.IncomeEmployeeName
                    }).ToList(),

                    stock = stockItems,

                    services = serviceItems,

                    saleItems = saleItems,

                    stockPurchases = stockPurchases,

                    employees = employees.Select(employee =>
                    {
                        var summary = EmployeeStatsService.GetSummary(employee.Name);

                        int salaryForMonth = CashService.GetSalaryTotalByPeriodForEmployee(
                            monthStart,
                            nextMonthStart,
                            employee.Name
                        );

                        var journal = EmployeeStatsService
                            .GetJournalForCurrentMonth(employee.Name)
                            .Take(150)
                            .ToList();

                        var allJournal = journal
                            .Select(item => new
                            {
                                createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                                type = item.Type,
                                title = item.Title,
                                description = item.Description,
                                amount = item.Amount
                            })
                            .ToList();

                        var incomeJournal = journal
                            .Where(item =>
                                item.Type == "Игры" ||
                                item.Type == "Выручка" ||
                                item.Type == "Товар/услуга" ||
                                item.Type == "Товары и услуги")
                            .Select(item => new
                            {
                                createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                                type = item.Type,
                                title = item.Title,
                                description = item.Description,
                                amount = item.Amount
                            })
                            .ToList();

                        var shortageJournal = journal
                            .Where(item =>
                                item.Type == "Недостача" ||
                                item.Type.Contains("Штраф"))
                            .Select(item => new
                            {
                                createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                                type = item.Type,
                                title = item.Title,
                                description = item.Description,
                                amount = item.Amount
                            })
                            .ToList();

                        return new
                        {
                            name = employee.Name,
                            pinCode = employee.PinCode,
                            isActive = employee.IsActive,

                            todayWorkTime = EmployeeStatsService.FormatTime(summary.TodayWorkTime),
                            monthWorkTime = EmployeeStatsService.FormatTime(summary.MonthWorkTime),

                            todayIncome = summary.TodayTotalIncome,
                            monthIncome = summary.MonthTotalIncome,

                            todayGameIncome = summary.TodayGameIncome,
                            todayProductsIncome = summary.TodayProductsIncome,

                            monthGameIncome = summary.MonthGameIncome,
                            monthProductsIncome = summary.MonthProductsIncome,

                            todayShortages = summary.TodayShortages,
                            monthShortages = summary.MonthShortages,

                            monthSalaryPaid = salaryForMonth,

                            closedGameSessionsCount = summary.ClosedGameSessionsCount,
                            productServiceOperationsCount = summary.ProductServiceOperationsCount,
                            shortageCount = summary.ShortageCount,

                            journal = allJournal,
                            incomeJournal = incomeJournal,
                            shortageJournal = shortageJournal
                        };
                    }).ToList(),

                    expenseCategories = expenseCategories,

                    expensesByCategory = expensesByCategory,

                    salaryRecords = salaryRecords,

                    expenses = CashService.GetRecordsByPeriodAndCategory(monthStart, nextMonthStart, "Расходы")
                        .Take(150)
                        .Select(record => new
                        {
                            createdAt = record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                            title = record.Title,
                            description = record.Description,
                            amount = record.Amount,
                            paymentMethod = record.PaymentMethod,
                            expenseCategory = string.IsNullOrWhiteSpace(record.ExpenseCategory)
                                ? "Другое"
                                : record.ExpenseCategory,
                            relatedEmployeeName = record.RelatedEmployeeName,
                            employeeName = record.EmployeeName
                        })
                        .ToList()
                };

                await PutAsync("club/current", data);
            }
            catch
            {
                // Если интернет пропал, программа должна продолжать работать.
            }
        }

        public static async Task CheckCommandsAsync()
        {
            try
            {
                var commands = await GetAsync<Dictionary<string, FirebaseCommand>>("club/commands");

                if (commands == null)
                    return;

                foreach (var pair in commands)
                {
                    string commandId = pair.Key;
                    FirebaseCommand command = pair.Value;

                    if (command == null)
                        continue;

                    if (command.Status != "pending")
                        continue;

                    await ApplyCommandAsync(commandId, command);
                }
            }
            catch
            {
                // Пока молча игнорируем ошибки связи.
            }
        }

        private static async Task ApplyCommandAsync(string commandId, FirebaseCommand command)
        {
            try
            {
                if (command.Type == "ShowMessage")
                {
                    MessageBox.Show(
                        command.Message,
                        "Команда из Firebase"
                    );

                    await MarkCommandApplied(commandId, command, "Сообщение показано.");
                    return;
                }

                if (command.Type == "UpdateProductSalePrice")
                {
                    ApplyUpdateProductSalePrice(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Цена товара обновлена: {command.ProductName} → {command.SalePrice} сом."
                    );

                    return;
                }

                if (command.Type == "UpdateStockProduct")
                {
                    ApplyUpdateStockProduct(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Товар обновлён: {command.ProductName} → {command.NewProductName}."
                    );

                    return;
                }

                if (command.Type == "UpdateServiceItem")
                {
                    ApplyUpdateServiceItem(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Услуга обновлена: {command.ProductName} → {command.NewProductName}."
                    );

                    return;
                }

                if (command.Type == "AddStockProduct")
                {
                    ApplyAddStockProduct(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Товар добавлен: {command.ProductName}."
                    );

                    return;
                }

                if (command.Type == "DeleteStockProduct")
                {
                    ApplyDeleteStockProduct(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Товар удалён: {command.ProductName}."
                    );

                    return;
                }

                if (command.Type == "AddSaleItem")
                {
                    ApplyAddSaleItem(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Позиция добавлена: {command.ProductName}, тип: {NormalizeSaleItemType(command.ItemType)}."
                    );

                    return;
                }

                if (command.Type == "DeleteSaleItem")
                {
                    ApplyDeleteSaleItem(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Позиция удалена: {command.ProductName}, тип: {NormalizeSaleItemType(command.ItemType)}."
                    );

                    return;
                }

                if (command.Type == "ConfirmStockPurchase")
                {
                    var purchase = ApplyConfirmStockPurchase(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Закуп подтверждён: {purchase.TotalAmount} сом, позиций: {purchase.Items.Count}."
                    );

                    return;
                }

                if (command.Type == "AddExpense")
                {
                    ApplyAddExpense(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Расход добавлен: {command.Title}, {command.Amount} сом, тип: {NormalizePaymentMethod(command.PaymentMethod)}, категория: {CashService.NormalizeExpenseCategory(command.ExpenseCategory)}."
                    );

                    return;
                }

                if (command.Type == "AddSalaryPayment")
                {
                    ApplyAddSalaryPayment(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Зарплата выдана: {command.EmployeeName}, {command.Amount} сом, тип: {NormalizePaymentMethod(command.PaymentMethod)}."
                    );

                    return;
                }

                if (command.Type == "SetCashlessForToday")
                {
                    ApplySetCashlessForToday(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Безнал за сегодня сохранён: {command.Amount} сом."
                    );

                    return;
                }

                if (command.Type == "AddEmployee")
                {
                    ApplyAddEmployee(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Работник добавлен: {command.EmployeeName}."
                    );

                    return;
                }

                if (command.Type == "UpdateEmployeePin")
                {
                    ApplyUpdateEmployeePin(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Код работника изменён: {command.EmployeeName}."
                    );

                    return;
                }

                if (command.Type == "DisableEmployee")
                {
                    ApplyDisableEmployee(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Работник отключён: {command.EmployeeName}."
                    );

                    return;
                }

                if (command.Type == "EnableEmployee")
                {
                    ApplyEnableEmployee(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Работник включён: {command.EmployeeName}."
                    );

                    return;
                }

                await MarkCommandError(
                    commandId,
                    command,
                    $"Неизвестный тип команды: {command.Type}"
                );
            }
            catch (Exception ex)
            {
                await MarkCommandError(
                    commandId,
                    command,
                    ex.Message
                );
            }
        }

        private static void ApplyUpdateProductSalePrice(FirebaseCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.ProductName))
                throw new Exception("Не указан productName.");

            if (command.SalePrice < 0)
                throw new Exception("salePrice не может быть меньше 0.");

            var product = ProductStockService.FindByProductName(command.ProductName);

            if (product == null)
                throw new Exception($"Товар не найден: {command.ProductName}");

            ProductStockService.UpdateProductSettings(
                productName: product.ProductName,
                purchasePrice: product.PurchasePrice,
                salePrice: command.SalePrice,
                minimumQuantity: product.MinimumQuantity
            );
        }

        private static void ApplyUpdateStockProduct(FirebaseCommand command)
        {
            string oldProductName = command.ProductName.Trim();
            string newProductName = command.NewProductName.Trim();

            if (string.IsNullOrWhiteSpace(oldProductName))
                throw new Exception("Не указано старое название товара.");

            if (string.IsNullOrWhiteSpace(newProductName))
                newProductName = oldProductName;

            var product = ProductStockService.FindByProductName(oldProductName);

            if (product == null)
                throw new Exception($"Товар не найден: {oldProductName}");

            bool nameChanged = !oldProductName.Equals(newProductName, StringComparison.OrdinalIgnoreCase);

            if (nameChanged)
            {
                if (ProductStockService.ExistsByProductName(newProductName))
                    throw new Exception($"Товар с таким названием уже есть: {newProductName}");

                if (CustomServiceService.ExistsByName(newProductName))
                    throw new Exception($"Услуга с таким названием уже есть: {newProductName}");
            }

            if (command.Quantity < 0)
                throw new Exception("quantity не может быть меньше 0.");

            if (command.PurchasePrice < 0)
                throw new Exception("purchasePrice не может быть меньше 0.");

            if (command.SalePrice < 0)
                throw new Exception("salePrice не может быть меньше 0.");

            if (command.MinimumQuantity < 0)
                throw new Exception("minimumQuantity не может быть меньше 0.");

            bool updated = ProductStockService.UpdateProductFull(
                oldProductName: oldProductName,
                newProductName: newProductName,
                quantity: command.Quantity,
                purchasePrice: command.PurchasePrice,
                salePrice: command.SalePrice,
                minimumQuantity: command.MinimumQuantity
            );

            if (!updated)
                throw new Exception("Не удалось обновить товар.");
        }

        private static void ApplyUpdateServiceItem(FirebaseCommand command)
        {
            string oldServiceName = command.ProductName.Trim();
            string newServiceName = command.NewProductName.Trim();

            if (string.IsNullOrWhiteSpace(oldServiceName))
                throw new Exception("Не указано старое название услуги.");

            if (string.IsNullOrWhiteSpace(newServiceName))
                newServiceName = oldServiceName;

            var service = CustomServiceService.FindByName(oldServiceName);

            if (service == null)
                throw new Exception($"Услуга не найдена: {oldServiceName}");

            bool nameChanged = !oldServiceName.Equals(newServiceName, StringComparison.OrdinalIgnoreCase);

            if (nameChanged)
            {
                if (CustomServiceService.ExistsByName(newServiceName))
                    throw new Exception($"Услуга с таким названием уже есть: {newServiceName}");

                if (ProductStockService.ExistsByProductName(newServiceName))
                    throw new Exception($"Товар с таким названием уже есть: {newServiceName}");
            }

            if (command.SalePrice < 0)
                throw new Exception("salePrice не может быть меньше 0.");

            service.Name = newServiceName;
            service.SalePrice = command.SalePrice;
            service.IsActive = true;

            CustomServiceService.Save();
        }

        private static void ApplyAddStockProduct(FirebaseCommand command)
        {
            command.ItemType = "Product";
            ApplyAddSaleItem(command);
        }

        private static void ApplyDeleteStockProduct(FirebaseCommand command)
        {
            command.ItemType = "Product";
            ApplyDeleteSaleItem(command);
        }

        private static void ApplyAddSaleItem(FirebaseCommand command)
        {
            string itemType = NormalizeSaleItemType(command.ItemType);
            string name = command.ProductName.Trim();

            if (string.IsNullOrWhiteSpace(name))
                throw new Exception("Не указано название товара/услуги.");

            if (itemType == "Product")
            {
                if (ProductStockService.ExistsByProductName(name))
                    throw new Exception($"Такой товар уже есть: {name}");

                if (CustomServiceService.ExistsByName(name))
                    throw new Exception($"Такая услуга уже есть: {name}");

                if (command.InitialQuantity < 0)
                    throw new Exception("initialQuantity не может быть меньше 0.");

                if (command.PurchasePrice < 0)
                    throw new Exception("purchasePrice не может быть меньше 0.");

                if (command.SalePrice < 0)
                    throw new Exception("salePrice не может быть меньше 0.");

                if (command.MinimumQuantity < 0)
                    throw new Exception("minimumQuantity не может быть меньше 0.");

                ProductStockService.AddNewProduct(
                    productName: name,
                    initialQuantity: command.InitialQuantity,
                    purchasePrice: command.PurchasePrice,
                    salePrice: command.SalePrice,
                    minimumQuantity: command.MinimumQuantity
                );

                return;
            }

            if (itemType == "Service")
            {
                if (CustomServiceService.ExistsByName(name))
                    throw new Exception($"Такая услуга уже есть: {name}");

                if (ProductStockService.ExistsByProductName(name))
                    throw new Exception($"Такой товар уже есть: {name}");

                if (command.SalePrice < 0)
                    throw new Exception("salePrice не может быть меньше 0.");

                CustomServiceService.AddService(
                    name: name,
                    salePrice: command.SalePrice
                );

                return;
            }

            throw new Exception($"Неизвестный тип позиции: {command.ItemType}");
        }

        private static void ApplyDeleteSaleItem(FirebaseCommand command)
        {
            string itemType = NormalizeSaleItemType(command.ItemType);
            string name = command.ProductName.Trim();

            if (string.IsNullOrWhiteSpace(name))
                throw new Exception("Не указано название товара/услуги.");

            if (itemType == "Product")
            {
                var product = ProductStockService.FindByProductName(name);

                if (product == null)
                    throw new Exception($"Товар не найден: {name}");

                bool deleted = ProductStockService.DeleteProduct(product.ProductName);

                if (!deleted)
                    throw new Exception($"Не удалось удалить товар: {name}");

                return;
            }

            if (itemType == "Service")
            {
                var service = CustomServiceService.FindByName(name);

                if (service == null)
                    throw new Exception($"Услуга не найдена: {name}");

                bool deleted = CustomServiceService.DeleteService(service.Name);

                if (!deleted)
                    throw new Exception($"Не удалось удалить услугу: {name}");

                return;
            }

            throw new Exception($"Неизвестный тип позиции: {command.ItemType}");
        }

        private static StockPurchase ApplyConfirmStockPurchase(FirebaseCommand command)
        {
            if (command.PurchaseItems == null || command.PurchaseItems.Count == 0)
                throw new Exception("Корзина закупа пустая.");

            var items = new List<StockPurchaseItem>();

            foreach (var item in command.PurchaseItems)
            {
                if (item == null)
                    continue;

                string productName = item.ProductName.Trim();

                if (string.IsNullOrWhiteSpace(productName))
                    continue;

                if (item.Quantity <= 0)
                    continue;

                int purchasePrice = item.PurchasePrice;
                int salePrice = item.SalePrice;

                if (purchasePrice < 0)
                    purchasePrice = 0;

                if (salePrice < 0)
                    salePrice = 0;

                items.Add(new StockPurchaseItem
                {
                    ProductName = productName,
                    Quantity = item.Quantity,
                    PurchasePrice = purchasePrice,
                    SalePrice = salePrice
                });
            }

            if (items.Count == 0)
                throw new Exception("В корзине закупа нет правильных товаров.");

            var purchase = StockPurchaseService.AddPurchase(
                items: items,
                addedBy: "Владелец",
                note: command.Description
            );

            CashService.AddExpense(
                employeeName: "Владелец",
                title: "Закуп товаров",
                description: BuildPurchaseDescription(purchase),
                amount: purchase.TotalAmount,
                paymentMethod: NormalizePaymentMethod(command.PaymentMethod),
                expenseCategory: "Закупка"
            );

            return purchase;
        }

        private static string BuildPurchaseDescription(StockPurchase purchase)
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(purchase.Note))
            {
                lines.Add(purchase.Note);
            }

            foreach (var item in purchase.Items)
            {
                lines.Add(
                    $"{item.ProductName}: {item.Quantity} шт × {item.PurchasePrice} сом = {item.TotalAmount} сом"
                );
            }

            return string.Join("\n", lines);
        }

        private static void ApplyAddExpense(FirebaseCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.Title))
                throw new Exception("Не указан title.");

            if (command.Amount <= 0)
                throw new Exception("amount должен быть больше 0.");

            CashService.AddExpense(
                employeeName: "Владелец",
                title: command.Title,
                description: command.Description,
                amount: command.Amount,
                paymentMethod: NormalizePaymentMethod(command.PaymentMethod),
                expenseCategory: CashService.NormalizeExpenseCategory(command.ExpenseCategory)
            );
        }

        private static void ApplyAddSalaryPayment(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указан работник для зарплаты.");

            if (command.Amount <= 0)
                throw new Exception("amount должен быть больше 0.");

            var employee = EmployeeService.FindByName(employeeName);

            if (employee == null)
                throw new Exception($"Работник не найден: {employeeName}");

            CashService.AddSalaryPayment(
                ownerName: "Владелец",
                employeeName: employee.Name,
                amount: command.Amount,
                paymentMethod: NormalizePaymentMethod(command.PaymentMethod),
                description: command.Description
            );
        }

        private static void ApplySetCashlessForToday(FirebaseCommand command)
        {
            if (command.Amount < 0)
                throw new Exception("amount не может быть меньше 0.");

            CashlessService.SetAmountForToday(
                amount: command.Amount,
                note: command.Description
            );
        }

        private static void ApplyAddEmployee(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();
            string pinCode = command.PinCode.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указано имя работника.");

            if (string.IsNullOrWhiteSpace(pinCode))
                throw new Exception("Не указан код работника.");

            if (EmployeeService.ExistsByName(employeeName))
                throw new Exception($"Работник уже существует: {employeeName}");

            EmployeeService.AddEmployee(employeeName, pinCode);
        }

        private static void ApplyUpdateEmployeePin(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();
            string pinCode = command.PinCode.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указано имя работника.");

            if (string.IsNullOrWhiteSpace(pinCode))
                throw new Exception("Не указан новый код работника.");

            var employee = EmployeeService.FindByName(employeeName);

            if (employee == null)
                throw new Exception($"Работник не найден: {employeeName}");

            EmployeeService.ChangePinCode(employeeName, pinCode);
        }

        private static void ApplyDisableEmployee(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указано имя работника.");

            var employee = EmployeeService.FindByName(employeeName);

            if (employee == null)
                throw new Exception($"Работник не найден: {employeeName}");

            EmployeeService.SetEmployeeActive(employeeName, false);
        }

        private static void ApplyEnableEmployee(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указано имя работника.");

            var employee = EmployeeService.FindByName(employeeName);

            if (employee == null)
                throw new Exception($"Работник не найден: {employeeName}");

            EmployeeService.SetEmployeeActive(employeeName, true);
        }

        private static string NormalizePaymentMethod(string paymentMethod)
        {
            if (paymentMethod == "Наличные")
                return "Наличные";

            if (paymentMethod == "Безнал")
                return "Безнал";

            return "Наличные";
        }

        private static string NormalizeSaleItemType(string itemType)
        {
            if (itemType == "Service")
                return "Service";

            return "Product";
        }

        private static async Task MarkCommandApplied(
            string commandId,
            FirebaseCommand command,
            string resultMessage)
        {
            command.Status = "applied";
            command.AppliedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            command.ResultMessage = resultMessage;

            await PutAsync($"club/commands/{commandId}", command);
        }

        private static async Task MarkCommandError(
            string commandId,
            FirebaseCommand command,
            string errorMessage)
        {
            command.Status = "error";
            command.AppliedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            command.ResultMessage = errorMessage;

            await PutAsync($"club/commands/{commandId}", command);
        }

        private static async Task<T?> GetAsync<T>(string path)
        {
            string url = $"{BaseUrl}/{path}.json";

            string json = await _httpClient.GetStringAsync(url);

            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return default;

            return JsonSerializer.Deserialize<T>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }
            );
        }

        private static async Task PutAsync(string path, object data)
        {
            string url = $"{BaseUrl}/{path}.json";

            string json = JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }
            );

            using var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

            await _httpClient.PutAsync(url, content);
        }

        private class FirebaseCommand
        {
            public string Type { get; set; } = "";
            public string Status { get; set; } = "pending";

            public string Message { get; set; } = "";

            public string ItemType { get; set; } = "Product";

            public string ProductName { get; set; } = "";

            public string NewProductName { get; set; } = "";

            public int Quantity { get; set; }

            public int InitialQuantity { get; set; }
            public int PurchasePrice { get; set; }
            public int SalePrice { get; set; }
            public int MinimumQuantity { get; set; }

            public List<StockPurchaseItem> PurchaseItems { get; set; } = new List<StockPurchaseItem>();

            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public int Amount { get; set; }

            public string PaymentMethod { get; set; } = "Наличные";

            public string ExpenseCategory { get; set; } = "Другое";

            public string EmployeeName { get; set; } = "";
            public string PinCode { get; set; } = "";

            public string CreatedAt { get; set; } = "";
            public string AppliedAt { get; set; } = "";
            public string ResultMessage { get; set; } = "";
        }
    }
}