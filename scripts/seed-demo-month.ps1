param(
    [string]$DataFolder = "$env:APPDATA\ClubTimerXbox"
)

$ErrorActionPreference = "Stop"

function New-GuidString {
    return [guid]::NewGuid().ToString()
}

function New-DateString([datetime]$Date) {
    return $Date.ToString("yyyy-MM-ddTHH:mm:ss")
}

function New-CheckoutItem([string]$Name, [int]$Quantity, [int]$UnitPrice, [string]$Category) {
    [ordered]@{
        Name = $Name
        Quantity = $Quantity
        UnitPrice = $UnitPrice
        Category = $Category
    }
}

function Join-Chars([int[]]$Codes) {
    return -join ($Codes | ForEach-Object { [char]$_ })
}

function New-PaymentRecord(
    [datetime]$CreatedAt,
    [string]$EmployeeName,
    [string]$Title,
    [string]$PlaceName,
    [array]$Items,
    [int]$CashAmount,
    [int]$MBankAmount,
    [string]$Comment
) {
    [ordered]@{
        Id = New-GuidString
        CreatedAt = New-DateString $CreatedAt
        EmployeeName = $EmployeeName
        OperationTitle = $Title
        PlaceName = $PlaceName
        GameSessionId = $null
        Items = $Items
        TotalAmount = $CashAmount + $MBankAmount
        CashAmount = $CashAmount
        MBankAmount = $MBankAmount
        Comment = $Comment
    }
}

function New-CashRecord(
    [datetime]$CreatedAt,
    [string]$EmployeeName,
    [string]$IncomeEmployeeName,
    [string]$RelatedEmployeeName,
    [int]$Type,
    [string]$Title,
    [string]$Description,
    [int]$Amount,
    [string]$Category,
    [string]$ExpenseCategory,
    [string]$PaymentMethod,
    [string]$PlaceName
) {
    [ordered]@{
        Id = New-GuidString
        CreatedAt = New-DateString $CreatedAt
        EmployeeName = $EmployeeName
        IncomeEmployeeName = $IncomeEmployeeName
        RelatedEmployeeName = $RelatedEmployeeName
        Type = $Type
        Title = $Title
        Description = $Description
        Amount = $Amount
        Category = $Category
        ExpenseCategory = $ExpenseCategory
        PaymentMethod = $PaymentMethod
        PlaceName = $PlaceName
        GameSessionId = $null
        IsAttachedToGameSession = $false
    }
}

function New-Shift([string]$EmployeeName, [datetime]$Start, [datetime]$End) {
    [ordered]@{
        Id = New-GuidString
        EmployeeName = $EmployeeName
        StartedAt = New-DateString $Start
        ClosedAt = New-DateString $End
        IsClosed = $true
    }
}

function New-GameSession(
    [string]$PlaceName,
    [string]$EmployeeName,
    [datetime]$StartedAt,
    [datetime]$ClosedAt,
    [int]$GameAmount,
    [int]$ProductsAmount
) {
    [ordered]@{
        Id = New-GuidString
        PlaceName = $PlaceName
        StartedByEmployeeName = $EmployeeName
        StartedAt = New-DateString $StartedAt
        IsOpenMode = $false
        TariffText = "Тестовая месячная запись"
        PaidAmount = $GameAmount
        ExtraLines = @()
        SaleLines = @()
        ClosedByEmployeeName = $EmployeeName
        ClosedAt = New-DateString $ClosedAt
        ActualPlayedAmount = $GameAmount
        RefundAmount = 0
        NeedToPayAmount = 0
        CashIncomeAmount = $GameAmount
        ProductsAndServicesAmount = $ProductsAmount
        TotalToPayAmount = $GameAmount + $ProductsAmount
        IncomeEmployeeName = $EmployeeName
        IsClosed = $true
    }
}

function Save-Json($Path, $Data) {
    $json = $Data | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
}

New-Item -ItemType Directory -Force -Path $DataFolder | Out-Null

$backupFolder = Join-Path $DataFolder ("backup-before-demo-seed-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $backupFolder | Out-Null

$filesToBackup = @(
    "cash_records.json",
    "payments.json",
    "logs.json",
    "cashless_records.json"
)

foreach ($file in $filesToBackup) {
    $source = Join-Path $DataFolder $file
    if (Test-Path $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $backupFolder $file) -Force
    }
}

$stalbek = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0KHRgtCw0LvQsdC10Lo="))
$argen = "$([char]0x0410)$([char]0x0440)$([char]0x0433)$([char]0x0435)$([char]0x043D)"
$owner = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0JLQu9Cw0LTQtdC70LXRhg=="))
$gamesCategory = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0JjQs9GA0Ys="))
$productsCashCategory = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0KLQvtCy0LDRgNGLINC4INGD0YHQu9GD0LPQuA=="))
$productItemCategory = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0KLQvtCy0LDRgA=="))
$expensesCategory = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0KDQsNGB0YXQvtC00Ys="))
$cash = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0J3QsNC70LjRh9C90YvQtQ=="))
$cashless = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0JHQtdC30L3QsNC7"))
$rent = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0JDRgNC10L3QtNCw"))
$electricity = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0KLQvtC6"))
$internet = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0JjQvdGC0LXRgNC90LXRgg=="))
$purchase = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0JfQsNC60YPQv9C60LA="))
$repair = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0KDQtdC80L7QvdGC"))
$cleaning = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0KPQsdC+0YDQutCw"))
$gameTimeTitle = Join-Chars @(0x0418,0x0433,0x0440,0x043E,0x0432,0x043E,0x0435,0x0020,0x0432,0x0440,0x0435,0x043C,0x044F)
$demoTitle = Join-Chars @(0x0422,0x0435,0x0441,0x0442,0x043E,0x0432,0x0430,0x044F,0x0020,0x0441,0x043C,0x0435,0x043D,0x043D,0x0430,0x044F,0x0020,0x0432,0x044B,0x0440,0x0443,0x0447,0x043A,0x0430)
$demoComment = Join-Chars @(0x0421,0x0433,0x0435,0x043D,0x0435,0x0440,0x0438,0x0440,0x043E,0x0432,0x0430,0x043D,0x043E,0x0020,0x0043,0x006F,0x0064,0x0065,0x0078,0x003A,0x0020,0x0434,0x0435,0x043C,0x043E,0x002D,0x043C,0x0435,0x0441,0x044F,0x0446,0x0020,0x0438,0x044E,0x043D,0x044C,0x0020,0x0032,0x0030,0x0032,0x0036)
$gameIncomeTitle = Join-Chars @(0x0418,0x0433,0x0440,0x043E,0x0432,0x0430,0x044F,0x0020,0x0432,0x044B,0x0440,0x0443,0x0447,0x043A,0x0430)
$productIncomeTitle = Join-Chars @(0x041F,0x0440,0x043E,0x0434,0x0430,0x0436,0x0430,0x0020,0x0442,0x043E,0x0432,0x0430,0x0440,0x043E,0x0432,0x002F,0x0443,0x0441,0x043B,0x0443,0x0433)

$payments = @()
$cashRecords = @()
$gameSessions = @()
$shifts = @()

$startMonth = [datetime]"2026-06-01T00:00:00"
$days = 1..10
$places = @("TV 1", "TV 2", "TV 3", "TV 4", "TV 5", "TV 6", "VIP 1", "VIP 2")
$productNames = @(
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0KLQntCg0J3QkNCU0J4=")),
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0JvQtdGC0YEg0JPQviAxINC70LjRgtGA")),
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0J/QuNC60L4gMSDQu9C40YLRgA==")),
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0JrQvtC70LAgMSDQu9C40YLRgA==")),
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("0JTRgNC20L7QudGB0YLQuNC6"))
)

for ($index = 0; $index -lt $days.Count; $index++) {
    $day = $startMonth.AddDays($index)
    $stalbekStart = $day.AddHours(11)
    $stalbekEnd = $day.AddHours(20).AddMinutes(20)
    $argenStart = $stalbekEnd
    $argenEnd = $day.AddDays(1).AddHours(1)

    $shifts += New-Shift $stalbek $stalbekStart $stalbekEnd
    $shifts += New-Shift $argen $argenStart $argenEnd

    $dayRows = @(
        @{ Employee = $stalbek; Time = $day.AddHours(13).AddMinutes(10); Game = 7000; Products = 900; Cash = 3950; MBank = 3950; Place = $places[($index * 2) % $places.Count] },
        @{ Employee = $stalbek; Time = $day.AddHours(18).AddMinutes(30); Game = 5000; Products = 800; Cash = 3150; MBank = 2650; Place = $places[(($index * 2) + 1) % $places.Count] },
        @{ Employee = $argen; Time = $day.AddHours(22).AddMinutes(35); Game = 6000; Products = 800; Cash = 3150; MBank = 3650; Place = $places[(($index * 2) + 2) % $places.Count] }
    )

    foreach ($row in $dayRows) {
        $productName = $productNames[($index + $payments.Count) % $productNames.Count]
        $items = @(
            (New-CheckoutItem $gameTimeTitle 1 $row.Game $gamesCategory),
            (New-CheckoutItem $productName 1 $row.Products $productItemCategory)
        )
        $title = $demoTitle
        $comment = $demoComment

        $payments += New-PaymentRecord $row.Time $row.Employee $title $row.Place $items $row.Cash $row.MBank $comment

        $cashRecords += New-CashRecord $row.Time $row.Employee $row.Employee "" 0 $gameIncomeTitle $comment $row.Game $gamesCategory "" $cash $row.Place
        $cashRecords += New-CashRecord $row.Time.AddMinutes(2) $row.Employee $row.Employee "" 1 $productIncomeTitle $productName $row.Products $productsCashCategory "" $cash $row.Place

        $gameSessions += New-GameSession $row.Place $row.Employee $row.Time.AddHours(-1) $row.Time $row.Game $row.Products
    }
}

$expenseRows = @(
    @{ Day = 1; Hour = 12; Title = "Аренда помещения"; Amount = 25000; Method = $cashless; Category = $rent; Description = "Помесячная аренда клуба" },
    @{ Day = 3; Hour = 16; Title = "Электроэнергия"; Amount = 9000; Method = $cashless; Category = $electricity; Description = "Свет за месяц" },
    @{ Day = 5; Hour = 14; Title = "Интернет"; Amount = 2000; Method = $cashless; Category = $internet; Description = "Оплата интернета" },
    @{ Day = 6; Hour = 15; Title = "Закуп товаров"; Amount = 8000; Method = $cash; Category = $purchase; Description = "Пополнение напитков и мелких товаров" },
    @{ Day = 8; Hour = 17; Title = "Мелкий ремонт"; Amount = 3000; Method = $cash; Category = $repair; Description = "Кабели, переходники, расходники" },
    @{ Day = 10; Hour = 13; Title = "Уборка"; Amount = 2000; Method = $cash; Category = $cleaning; Description = "Уборка клуба" }
)

foreach ($expense in $expenseRows) {
    $date = $startMonth.AddDays($expense.Day - 1).AddHours($expense.Hour)
    $cashRecords += New-CashRecord $date $owner $owner "" 4 $expense.Title $expense.Description $expense.Amount $expensesCategory $expense.Category $expense.Method ""
}

$logs = [ordered]@{
    Shifts = $shifts
    GameSessions = $gameSessions
}

Save-Json (Join-Path $DataFolder "cash_records.json") $cashRecords
Save-Json (Join-Path $DataFolder "payments.json") $payments
Save-Json (Join-Path $DataFolder "logs.json") $logs

$summary = [ordered]@{
    BackupFolder = $backupFolder
    Days = $days.Count
    Employees = @($stalbek, $argen)
    GameRevenue = ($cashRecords | Where-Object { $_["Category"] -eq $gamesCategory } | ForEach-Object { $_["Amount"] } | Measure-Object -Sum).Sum
    ProductsRevenue = ($cashRecords | Where-Object { $_["Category"] -eq $productsCashCategory } | ForEach-Object { $_["Amount"] } | Measure-Object -Sum).Sum
    Expenses = ($cashRecords | Where-Object { $_["Category"] -eq $expensesCategory } | ForEach-Object { $_["Amount"] } | Measure-Object -Sum).Sum
    PaymentCash = ($payments | ForEach-Object { $_["CashAmount"] } | Measure-Object -Sum).Sum
    PaymentMBank = ($payments | ForEach-Object { $_["MBankAmount"] } | Measure-Object -Sum).Sum
    ShiftHoursStalbek = 93.33
    ShiftHoursArgen = 46.67
}

$summary | ConvertTo-Json -Depth 5
