using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class OwnerApiServer
    {
        private static HttpListener? _listener;
        private static CancellationTokenSource? _cancellationTokenSource;
        private static Func<List<ClubPlace>>? _getPlaces;

        private const string LocalPrefix = "http://localhost:5050/";

        public static bool IsRunning { get; private set; }

        public static void Start(Func<List<ClubPlace>> getPlaces)
        {
            if (IsRunning)
                return;

            _getPlaces = getPlaces;

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();

                _listener = new HttpListener();
                _listener.Prefixes.Add(LocalPrefix);
                _listener.Start();

                IsRunning = true;

                Task.Run(() => ListenLoop(_cancellationTokenSource.Token));
            }
            catch
            {
                IsRunning = false;
                Stop();
            }
        }

        public static void Stop()
        {
            try
            {
                _cancellationTokenSource?.Cancel();

                if (_listener != null)
                {
                    if (_listener.IsListening)
                        _listener.Stop();

                    _listener.Close();
                }
            }
            catch
            {
                // Пока ничего не делаем.
            }
            finally
            {
                _listener = null;
                _cancellationTokenSource = null;
                IsRunning = false;
            }
        }

        private static async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();

                    _ = Task.Run(() => HandleRequest(context));
                }
                catch
                {
                    if (!token.IsCancellationRequested)
                    {
                        // Пока ничего не делаем.
                    }
                }
            }
        }

        private static void HandleRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "/";

                if (path == "/" || path == "/index.html")
                {
                    WriteHtml(context, BuildHomePageHtml());
                    return;
                }

                if (path == "/api/summary")
                {
                    WriteJson(context, BuildSummary());
                    return;
                }

                if (path == "/api/places")
                {
                    WriteJson(context, BuildPlaces());
                    return;
                }

                if (path == "/api/stock")
                {
                    WriteJson(context, BuildStock());
                    return;
                }

                WriteNotFound(context);
            }
            catch
            {
                WriteServerError(context);
            }
        }

        private static object BuildSummary()
        {
            DateTime todayStart = DateTime.Today;
            DateTime tomorrowStart = todayStart.AddDays(1);

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

            int cashToday = CashService.GetCashIncomeTotalByPeriod(
                todayStart,
                tomorrowStart
            );

            var places = _getPlaces?.Invoke() ?? new List<ClubPlace>();

            int totalPlaces = places.Count;
            int busyPlaces = places.Count(place => place.IsBusy);
            int freePlaces = places.Count(place => !place.IsBusy);
            int openModePlaces = places.Count(place => place.IsBusy && place.IsOpenMode);

            return new
            {
                generatedAt = DateTime.Now,
                cashToday,
                gamesToday,
                productsToday,
                shortagesToday,
                expensesToday,
                totalPlaces,
                busyPlaces,
                freePlaces,
                openModePlaces
            };
        }

        private static object BuildPlaces()
        {
            var places = _getPlaces?.Invoke() ?? new List<ClubPlace>();

            return places.Select(place => new
            {
                name = place.Name,
                type = place.Type.ToString(),
                isBusy = place.IsBusy,
                isOpenMode = place.IsOpenMode,
                isCalculating = place.IsCalculating,
                isTimeExpiredAwaitingAcknowledgement = place.IsTimeExpiredAwaitingAcknowledgement,
                paidAmount = place.PaidAmount,
                startedByEmployeeName = place.StartedByEmployeeName,
                incomeEmployeeName = place.IncomeEmployeeName,
                remainingSeconds = place.RemainingSeconds
            }).ToList();
        }

        private static object BuildStock()
        {
            return ProductStockService.StockItems.Select(item => new
            {
                productName = item.ProductName,
                quantity = item.Quantity,
                purchasePrice = item.PurchasePrice,
                salePrice = item.SalePrice,
                minimumQuantity = item.MinimumQuantity,
                isLowStock = ProductStockService.IsLowStock(item.ProductName),
                updatedAt = item.UpdatedAt
            }).ToList();
        }

        private static string BuildHomePageHtml()
        {
            var summary = BuildSummary();

            string json = JsonSerializer.Serialize(
                summary,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }
            );

            return $@"
<!doctype html>
<html lang=""ru"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>Club Timer Xbox</title>
    <style>
        body {{
            margin: 0;
            padding: 20px;
            background: #10141C;
            color: white;
            font-family: Arial, sans-serif;
        }}

        .card {{
            background: #18202B;
            border-radius: 16px;
            padding: 18px;
            margin-bottom: 14px;
        }}

        h1 {{
            margin: 0 0 6px 0;
            font-size: 28px;
        }}

        .muted {{
            color: #AAB4C3;
            margin-bottom: 18px;
        }}

        .big {{
            font-size: 26px;
            font-weight: bold;
            color: #4ADE80;
        }}

        .line {{
            font-size: 17px;
            margin: 8px 0;
            color: #CBD5E1;
        }}

        a {{
            color: #60A5FA;
            display: block;
            margin-top: 8px;
            text-decoration: none;
        }}

        pre {{
            white-space: pre-wrap;
            color: #CBD5E1;
        }}
    </style>
</head>
<body>
    <h1>Club Timer Xbox</h1>
    <div class=""muted"">Первая страница владельца</div>

    <div class=""card"">
        <div class=""big"">Касса сегодня: {CashService.GetCashIncomeTotalByPeriod(DateTime.Today, DateTime.Today.AddDays(1))} сом</div>
        <div class=""line"">Игры: {CashService.GetTotalByPeriodAndCategory(DateTime.Today, DateTime.Today.AddDays(1), "Игры")} сом</div>
        <div class=""line"">Товары/услуги: {CashService.GetTotalByPeriodAndCategory(DateTime.Today, DateTime.Today.AddDays(1), "Товары и услуги")} сом</div>
        <div class=""line"">Недостачи: {CashService.GetShortageTotalByPeriod(DateTime.Today, DateTime.Today.AddDays(1))} сом</div>
        <div class=""line"">Расходы: {CashService.GetExpenseTotalByPeriod(DateTime.Today, DateTime.Today.AddDays(1))} сом</div>
    </div>

    <div class=""card"">
        <div class=""line"">API:</div>
        <a href=""/api/summary"">/api/summary</a>
        <a href=""/api/places"">/api/places</a>
        <a href=""/api/stock"">/api/stock</a>
    </div>

    <div class=""card"">
        <div class=""line"">Данные summary:</div>
        <pre>{WebUtility.HtmlEncode(json)}</pre>
    </div>
</body>
</html>";
        }

        private static void WriteHtml(HttpListenerContext context, string html)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(html);

            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;

            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }

        private static void WriteJson(HttpListenerContext context, object data)
        {
            string json = JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }
            );

            byte[] buffer = Encoding.UTF8.GetBytes(json);

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;

            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }

        private static void WriteNotFound(HttpListenerContext context)
        {
            byte[] buffer = Encoding.UTF8.GetBytes("Not found");

            context.Response.StatusCode = 404;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;

            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }

        private static void WriteServerError(HttpListenerContext context)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes("Server error");

                context.Response.StatusCode = 500;
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = buffer.Length;

                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch
            {
                // Уже нечего делать.
            }
        }
    }
}
