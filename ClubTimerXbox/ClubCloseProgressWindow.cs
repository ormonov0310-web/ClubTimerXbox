using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClubTimerXbox;

public sealed class ClubCloseProgressWindow : AdaptiveDialogWindow
{
    private readonly TextBlock _status;
    private bool _finished;

    public ClubCloseProgressWindow() : base(440, 320)
    {
        Title = "Завершение работы";
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = "Приложение закрывается", FontSize = 21, FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        _status = new TextBlock
        {
            Text = "Отправка данных: до 5 секунд", FontSize = 16,
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 16, 0, 0)
        };
        panel.Children.Add(_status);
        SetAdaptiveContent(panel);
    }

    public void SetRemaining(int seconds) => _status.Text = $"Отправка данных: {seconds} с";
    public void Finish(bool delivered)
    {
        _finished = true;
        _status.Text = delivered ? "Данные переданы в Firebase." :
            "Доставка не подтверждена. Данные сохранены для следующего запуска.";
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_finished) e.Cancel = true;
        base.OnClosing(e);
    }
}
