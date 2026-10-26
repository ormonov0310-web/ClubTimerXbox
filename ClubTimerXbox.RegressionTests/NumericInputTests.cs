using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using ClubTimerXbox;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class NumericInputTests
{
    private static int _passed;
    private static string? _output;
    private static string _repo = "";

    public static void Run(string? output = null)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _passed = 0;
                _output = output;
                _repo = FindRepo();
                if (output != null) Directory.CreateDirectory(output);
                Check("empty hint never becomes editable text", EmptyHintAndTyping);
                Check("typing and paste1/123 replace only the empty hint", HintTypingAndPaste);
                Check("explicit zero and saved values survive focus", ExplicitAndSavedValues);
                Check("clearing and refocusing restores only the hint", ClearAndRefocus);
                Check("hint preserves sign, decimal and identifier semantics", NoNumericCoercion);
                Check("hint reload and disable do not accumulate adorners", HintLifecycle);
                Check("KKM full cash and bank keep opposite zero", CheckoutModes);
                Check("KKM 150 minus typed100 leaves50 in either direction", CheckoutTyping);
                Check("KKM typing cannot exceed the total", CheckoutTypingLimit);
                Check("KKM paste clamps valid amounts and rejects invalid/overflow", CheckoutPaste);
                Check("flexible time starts empty and converts only the opposite field", FlexibleTime);
                Check("both salary entry routes start empty without making payouts", SalaryRoutes);
                Check("manual debt repayment starts empty and keeps validation", DebtRepayment);
                Check("additional new-entry fields use hints, not seeded zeros", AdditionalFieldInventory);
                Check("code dialogs accept typing immediately without a click", CodeDialogFocus);
                Check("flexible time initially focuses amount, not minutes", FlexibleAmountFocus);
                Check("Enter clicks the confirmation once and respects validation", EnterConfirmation);
                Check("KKM Enter confirms the same150 split without posting cash", CheckoutEnter);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine($"PASS: {_passed} numeric input UI scenarios; no live money, app startup or cloud used.");
    }

    private static void Check(string name, Action action)
    {
        action();
        _passed++;
        Console.WriteLine("PASS numeric UI: " + name);
    }

    private static void EmptyHintAndTyping()
    {
        var input = Field();
        using var host = Show(new Window { Content = input, Width = 320, Height = 130 });
        Equal("", input.Text);
        True(HintVisible(input), "empty zero hint missing");
        Equal("Amount", AutomationProperties.GetName(input));
        Type(input, "123");
        Equal("123", input.Text);
        Equal(3, input.CaretIndex);
        True(!HintVisible(input), "hint must disappear behind actual text");
        Capture(host.Window, "hint-typed-123");
    }

    private static void HintTypingAndPaste()
    {
        var input = Field();
        using var host = Show(new Window { Content = input, Width = 320, Height = 130 });
        using var clipboard = new ClipboardFixture();
        foreach (string text in new[] { "1", "123" })
        {
            Clear(input);
            Type(input, text);
            Equal(text, input.Text);
            Clear(input);
            Paste(input, text, clipboard);
            Equal(text, input.Text);
            True(!HintVisible(input), "pasted value must replace the hint");
        }
    }

    private static void ExplicitAndSavedValues()
    {
        var input = Field();
        using var host = Show(new Window { Content = input, Width = 320, Height = 130 });
        Type(input, "0");
        Equal("0", input.Text);
        True(!HintVisible(input), "explicit zero is real text");
        input.Text = "507";
        input.Focus();
        Drain();
        Equal("507", input.Text);
        NumericInputHint.Apply(input, "Other label");
        Equal("507", input.Text);
        Equal("Amount", AutomationProperties.GetName(input));
    }

    private static void ClearAndRefocus()
    {
        var input = Field();
        var other = new Button { Content = "Other", Height = 30 };
        var panel = new StackPanel();
        panel.Children.Add(input);
        panel.Children.Add(other);
        using var host = Show(new Window { Content = panel, Width = 320, Height = 160 });
        Type(input, "123");
        Clear(input);
        Equal("", input.Text);
        True(HintVisible(input), "clear must restore hint");
        other.Focus();
        input.Focus();
        Drain();
        Equal("", input.Text);
        True(HintVisible(input), "focus must not insert zero");
        Capture(host.Window, "hint-empty-focused");
    }

    private static void NoNumericCoercion()
    {
        var input = Field();
        using var host = Show(new Window { Content = input, Width = 320, Height = 130 });
        Type(input, "-12,5");
        Equal("-12,5", input.Text);
        Clear(input);
        Type(input, "001");
        Equal("001", input.Text);
        Clear(input);
        True(!int.TryParse(input.Text, out _), "empty required input is not a persisted zero");
    }

    private static void HintLifecycle()
    {
        var input = Field();
        using var host = Show(new Window { Content = input, Width = 320, Height = 130 });
        var layer = AdornerLayer.GetAdornerLayer(input);
        Equal(1, layer.GetAdorners(input)!.Length);
        NumericInputHint.Apply(input, "Amount");
        Equal(1, layer.GetAdorners(input)!.Length);
        NumericInputHint.SetEnabled(input, false);
        Equal(0, layer.GetAdorners(input)?.Length ?? 0);
        NumericInputHint.SetEnabled(input, true);
        Equal(1, layer.GetAdorners(input)!.Length);
        host.Window.Content = null;
        Drain();
        Equal(0, layer.GetAdorners(input)?.Length ?? 0);
        host.Window.Content = input;
        Drain();
        Equal(1, AdornerLayer.GetAdornerLayer(input).GetAdorners(input)!.Length);
    }

    private static CashCheckoutWindow Checkout() => new("Test", "Input test", new()
    {
        new CheckoutItem { Name = "Fixture", Quantity = 1, UnitPrice = 150 }
    });

    private static void CheckoutModes()
    {
        using var host = Show(Checkout());
        var window = (CashCheckoutWindow)host.Window;
        var cash = (TextBox)window.FindName("CashTextBox");
        var bank = (TextBox)window.FindName("MBankTextBox");
        Equal("", cash.Text);
        Equal("150", bank.Text);
        Payment(window, 0, 150);
        True(HintVisible(cash), "cash must show a zero hint in bank mode");
        Capture(window, "checkout-full-bank");
        Click(window, "FullCashButton");
        Equal("150", cash.Text);
        Equal("", bank.Text);
        True(HintVisible(bank), "bank must show a zero hint in cash mode");
        Payment(window, 150, 0);
        Capture(window, "checkout-full-cash");
        Click(window, "FullMBankButton");
        Payment(window, 0, 150);
    }

    private static void CheckoutTyping()
    {
        using var host = Show(Checkout());
        var window = (CashCheckoutWindow)host.Window;
        var cash = (TextBox)window.FindName("CashTextBox");
        var bank = (TextBox)window.FindName("MBankTextBox");
        Type(cash, "1");
        Equal("1", cash.Text);
        Payment(window, 1, 149);
        Type(cash, "0");
        Equal("10", cash.Text);
        Payment(window, 10, 140);
        Type(cash, "0");
        Equal("100", cash.Text);
        Equal("50", bank.Text);
        Payment(window, 100, 50);
        Capture(window, "checkout-partial-100-50");
        Clear(bank);
        Type(bank, "100");
        Equal("100", bank.Text);
        Equal("50", cash.Text);
        Payment(window, 50, 100);
        Clear(cash);
        Equal("", cash.Text);
        Payment(window, 0, 150);
        Type(cash, "0");
        Equal("0", cash.Text);
        Payment(window, 0, 150);
    }

    private static void CheckoutTypingLimit()
    {
        using var host = Show(Checkout());
        var window = (CashCheckoutWindow)host.Window;
        foreach (var name in new[] { "CashTextBox", "MBankTextBox" })
        {
            var input = (TextBox)window.FindName(name);
            Clear(input);
            Type(input, "9999");
            Equal("150", input.Text);
            Equal(input.Text.Length, input.CaretIndex);
            Equal(150, window.CashAmount + window.MBankAmount);
            True(window.CashAmount <= 150 && window.MBankAmount <= 150, "overpayment accepted");
            EditingCommands.Backspace.Execute(null, input);
            Drain();
            Equal("15", input.Text);
        }
    }

    private static void CheckoutPaste()
    {
        using var host = Show(Checkout());
        using var clipboard = new ClipboardFixture();
        var window = (CashCheckoutWindow)host.Window;
        var cash = (TextBox)window.FindName("CashTextBox");
        foreach (int amount in new[] { 1, 123 })
        {
            Clear(cash);
            Paste(cash, amount.ToString(), clipboard);
            Equal(amount.ToString(), cash.Text);
            Payment(window, amount, 150 - amount);
        }
        Paste(cash, "100", clipboard);
        Equal("100", cash.Text);
        Payment(window, 100, 50);
        Paste(cash, "999", clipboard);
        Equal("150", cash.Text);
        Payment(window, 150, 0);
        foreach (var invalid in new[] { "999999999999999999999", "-1", "12x", "12.5" })
        {
            Paste(cash, invalid, clipboard);
            Equal("150", cash.Text);
            Payment(window, 150, 0);
        }
        var bank = (TextBox)window.FindName("MBankTextBox");
        Paste(bank, "100", clipboard);
        Payment(window, 50, 100);
        Paste(bank, "999", clipboard);
        Equal("150", bank.Text);
        Payment(window, 0, 150);
    }

    private static void FlexibleTime()
    {
        foreach (bool adding in new[] { false, true })
        {
            using var host = Show(new FlexibleTimeWindow(new ClubPlace { Name = "TV 1" },
                new TariffSettings { OneHourPrice = 120 }, adding));
            var window = (FlexibleTimeWindow)host.Window;
            var fields = Descendants(window).OfType<TextBox>().ToArray();
            Equal(2, fields.Length);
            Equal("", fields[0].Text);
            Equal("", fields[1].Text);
            Equal(0, window.Amount);
            Equal(0, window.Seconds);
            True(fields.All(HintVisible), "time and amount must begin with zero hints");
            Capture(window, adding ? "add-flexible-empty" : "flexible-empty");
            Type(fields[0], "30");
            Equal("30", fields[0].Text);
            Equal("60", fields[1].Text);
            Equal(1800, window.Seconds);
            Clear(fields[0]);
            Equal("", fields[1].Text);
            Equal(0, window.Amount);
            Type(fields[1], "123");
            Equal("123", fields[1].Text);
            Equal(123, window.Amount);
            Equal(3690, window.Seconds);
            Clear(fields[0]);
            Type(fields[0], "1,5");
            Equal("1,5", fields[0].Text);
            Equal(90, window.Seconds);
            Equal(3, window.Amount);
        }
    }

    private static void SalaryRoutes()
    {
        foreach (bool personalAccess in new[] { false, true })
        {
            var access = personalAccess ? new EmployeeSalaryAccessSession
                { EmployeeName = "Test", DutyEmployeeName = "Admin" } : null;
            using var host = Show(new EmployeeSalaryTakenWindow("Test", new DateTime(2026, 10, 1),
                null, access, () => (500, 0), () => 5000));
            var input = Descendants(host.Window).OfType<TextBox>().Single();
            Equal("", input.Text);
            True(input.IsKeyboardFocused, "salary amount must already have keyboard focus");
            True(HintVisible(input), "salary input must show only the hint");
            Capture(host.Window, personalAccess ? "salary-personal-code-empty" : "salary-statistics-empty");
            Type(input, "123", focus: false);
            Equal("123", input.Text);
            Clear(input);
            Equal("", input.Text);
        }
    }

    private static void DebtRepayment()
    {
        int submissions = 0;
        using var host = Show(new EmployeeDebtRepaymentWindow(() => new()
        {
            DebtRemainingAmount = 1000, AvailableSalaryAmount = 500,
            MaximumManualPaymentAmount = 500, AutomaticReserveAmount = 10
        }, (_, _) => submissions++));
        var input = Descendants(host.Window).OfType<TextBox>().Single();
        var ok = Descendants(host.Window).OfType<Button>().Single(x => Equals(x.Content, "ОК"));
        Equal("", input.Text);
        True(!ok.IsEnabled, "empty repayment must not be accepted");
        Type(input, "123");
        Equal("123", input.Text);
        True(ok.IsEnabled, "valid repayment must remain available");
        Clear(input);
        True(!ok.IsEnabled, "cleared repayment must not be accepted");
        Equal(0, submissions);
    }

    private static void AdditionalFieldInventory()
    {
        foreach (var file in new[] { "EmployeeSalaryTakenWindow.cs", "EmployeeDebtRepaymentWindow.cs",
            "ServiceSettingsWindow.cs", "StockWindow.cs" })
        {
            string source = File.ReadAllText(Path.Combine(_repo, "ClubTimerXbox", file));
            True(!source.Contains(".Text = \"0\""), file + " still seeds editable zero");
            True(source.Contains("NumericInputHint.Apply("), file + " does not use the common hint");
        }
    }

    private static void CodeDialogFocus()
    {
        foreach (var window in new Window[]
        {
            new LoginWindow(), new EmployeePinConfirmWindow("Test"), new EmployeeSalaryAccessWindow()
        })
        {
            using var host = Show(window);
            var pin = Descendants(window).OfType<PasswordBox>().Single();
            True(pin.IsKeyboardFocused, window.GetType().Name + " code input is not focused");
            Type(pin, "123", focus: false);
            Equal("123", pin.Password);
            True(Descendants(window).OfType<Button>().Any(button => button.IsDefault), "confirmation is not default");
        }
    }

    private static void FlexibleAmountFocus()
    {
        using var host = Show(new FlexibleTimeWindow(new ClubPlace { Name = "TV 1" },
            new TariffSettings { OneHourPrice = 120 }, false));
        var window = (FlexibleTimeWindow)host.Window;
        var inputs = Descendants(window).OfType<TextBox>().ToArray();
        True(inputs[1].IsKeyboardFocused && !inputs[0].IsKeyboardFocused, "initial focus must be on amount");
        Type(inputs[1], "123", focus: false);
        Equal(123, window.Amount);
    }

    private static void EnterConfirmation()
    {
        int clicks = 0;
        var input = Field();
        var other = Field();
        var confirm = new Button { Content = "Confirm" };
        confirm.Click += (_, _) => clicks++;
        var root = new StackPanel();
        root.Children.Add(input);
        root.Children.Add(other);
        root.Children.Add(confirm);
        var window = new Window { Content = root, Width = 320, Height = 220 };
        DialogKeyboard.Configure(window, input, confirm);
        using var host = Show(window);
        True(input.IsKeyboardFocused, "first numeric editor must be focused");
        Type(input, "123", focus: false);
        Enter(input);
        Equal(1, clicks);
        confirm.IsEnabled = false;
        Enter(input);
        Equal(1, clicks);
        other.Focus();
        typeof(Window).GetMethod("OnActivated",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, new object[] { EventArgs.Empty });
        True(other.IsKeyboardFocused, "later activation must not steal another field's focus");
    }

    private static void CheckoutEnter()
    {
        var window = Checkout();
        window.Resources.MergedDictionaries.Add(LoadTheme());
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                var cash = (TextBox)window.FindName("CashTextBox");
                True(cash.IsKeyboardFocused, "cash leg must be ready for input");
                Type(cash, "100", focus: false);
                Enter(cash);
                Equal(CashCheckoutResultType.Paid, window.ResultType);
            }
            catch (Exception error) { failure = error; window.Close(); }
        }));
        bool? result = window.ShowDialog();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        Equal(true, result);
        Equal(CashCheckoutResultType.Paid, window.ResultType);
        Equal(100, window.PaymentRecord!.CashAmount);
        Equal(50, window.PaymentRecord.MBankAmount);
    }

    private static void Enter(UIElement input)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), 0, Key.Enter)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        input.RaiseEvent(args);
        Drain();
        True(args.Handled, "Enter must be consumed to avoid duplicate confirmation");
    }

    private static TextBox Field()
    {
        var input = new TextBox { Height = 42, FontSize = 20, Padding = new Thickness(10, 5, 10, 5) };
        NumericInputHint.Apply(input, "Amount");
        return input;
    }

    private static WindowScope Show(Window window)
    {
        window.Resources.MergedDictionaries.Add(LoadTheme());
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.Show();
        Drain();
        window.UpdateLayout();
        return new WindowScope(window);
    }

    private static ResourceDictionary LoadTheme()
    {
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var root = XDocument.Load(Path.Combine(_repo, "ClubTimerXbox", "App.xaml")).Root!;
        var dictionary = new XElement(ns + "ResourceDictionary",
            root.Attributes().Where(x => x.IsNamespaceDeclaration),
            root.Element(ns + "Application.Resources")!.Elements().Select(x => new XElement(x)));
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    private static bool HintVisible(TextBox input) =>
        AdornerLayer.GetAdornerLayer(input)?.GetAdorners(input)?.Any(x => x.Visibility == Visibility.Visible) == true;

    private static void Type(UIElement input, string text, bool focus = true)
    {
        if (focus) input.Focus();
        foreach (char character in text)
        {
            var composition = new TextComposition(InputManager.Current, input, character.ToString());
            var preview = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
                { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            input.RaiseEvent(preview);
            if (!preview.Handled)
                input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
                    { RoutedEvent = TextCompositionManager.TextInputEvent });
            Drain();
        }
    }

    private static void Clear(TextBox input)
    {
        input.Focus();
        input.SelectAll();
        EditingCommands.Backspace.Execute(null, input);
        Drain();
    }

    private static void Paste(TextBox input, string text, ClipboardFixture clipboard)
    {
        input.Focus();
        input.SelectAll();
        clipboard.Set(text);
        ApplicationCommands.Paste.Execute(null, input);
        Drain();
    }

    private static void Click(Window window, string name)
    {
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Drain();
    }

    private static void Payment(CashCheckoutWindow window, int cash, int bank)
    {
        Equal(cash, window.CashAmount);
        Equal(bank, window.MBankAmount);
        Equal(150, window.CashAmount + window.MBankAmount);
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }

    private static void Capture(Window window, string name)
    {
        if (_output == null) return;
        window.UpdateLayout();
        var visual = (FrameworkElement)window.Content;
        SaveImage(visual, name);
        if (window is CashCheckoutWindow && visual is ScrollViewer scroll)
        {
            double previous = scroll.VerticalOffset;
            scroll.ScrollToEnd();
            Drain();
            window.UpdateLayout();
            SaveImage(visual, name + "-payment");
            scroll.ScrollToVerticalOffset(previous);
            Drain();
        }
    }

    private static void SaveImage(FrameworkElement visual, string name)
    {
        var image = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),
            (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(_output!, name + ".png"));
        encoder.Save(stream);
    }

    private static string FindRepo()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "ClubTimerXbox.slnx"))) return folder.FullName;
        throw new InvalidOperationException("Canonical source root not found");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected [{expected}], actual [{actual}]");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public Window Window => window;
        public void Dispose() => window.Close();
    }

    private sealed class ClipboardFixture : IDisposable
    {
        private readonly IDataObject? _original = Clipboard.GetDataObject();
        private string? _last;

        public void Set(string text)
        {
            Clipboard.SetText(text);
            _last = text;
        }

        public void Dispose()
        {
            if (_last == null || !Clipboard.ContainsText() || Clipboard.GetText() != _last) return;
            if (_original == null) Clipboard.Clear();
            else Clipboard.SetDataObject(_original, true);
        }
    }
}
