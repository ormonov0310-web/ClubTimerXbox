using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClubTimerXbox
{
    public static class DialogKeyboard
    {
        public static void Configure(Window window, Control input, Button confirm)
        {
            confirm.IsDefault = true;
            FocusManager.SetFocusedElement(window, input);
            bool waitingForFocus = true;

            void FocusInput(object? sender, EventArgs args)
            {
                if (!waitingForFocus || !input.IsVisible || !input.IsEnabled) return;
                FocusManager.SetFocusedElement(window, input);
                input.Focus();
                Keyboard.Focus(input);
                if (input.IsKeyboardFocusWithin)
                {
                    waitingForFocus = false;
                    window.ContentRendered -= FocusInput;
                    window.Activated -= FocusInput;
                }
            }

            window.ContentRendered += FocusInput;
            window.Activated += FocusInput;
            window.Closed += (_, _) =>
            {
                waitingForFocus = false;
                window.ContentRendered -= FocusInput;
                window.Activated -= FocusInput;
            };
            window.PreviewKeyDown += (_, args) =>
            {
                if (args.Key != Key.Enter || !input.IsKeyboardFocusWithin ||
                    Keyboard.Modifiers != ModifierKeys.None) return;
                // Consume first, so the default button cannot post a second operation.
                args.Handled = true;
                if (!args.IsRepeat && confirm.IsEnabled)
                    confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, confirm));
            };
        }
    }
}
