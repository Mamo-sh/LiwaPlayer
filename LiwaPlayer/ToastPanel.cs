using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace LiwaPlayer
{
    public enum ToastKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    // Uygulama içi bildirim kutuları (sağ üstte, kendiliğinden kaybolur).
    // Oynatıcının durum satırı (şarkı adının altı) yalnızca çalma durumunu
    // göstersin; hata, bağlantı, güncelleme gibi haberler buradan verilir.
    // Aynı "key" ile gelen bildirim yenisini eklemek yerine var olanı günceller
    // (ör. indirme yüzdesi).
    public class ToastPanel : StackPanel
    {
        private const int MaxVisible = 4;

        private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

        private readonly List<Toast> _toasts = new();

        private sealed class Toast
        {
            public string? Key;
            public Border Root = null!;
            public Border AccentBar = null!;
            public TextBlock Icon = null!;
            public TextBlock Text = null!;
            public DispatcherTimer Timer = null!;
            public Action? OnClick;
            public int DurationMs;
            public bool Closing;
        }

        public void Show(string message, ToastKind kind, string? key = null,
            Action? onClick = null, int durationMs = 0)
        {
            if (durationMs <= 0)
            {
                durationMs = kind switch
                {
                    ToastKind.Error => 8000,
                    ToastKind.Warning => 6500,
                    ToastKind.Success => 3500,
                    _ => 4000
                };
            }

            var existing = key == null ? null : _toasts.Find(t => t.Key == key && !t.Closing);

            if (existing != null)
            {
                existing.Text.Text = message;
                AutomationProperties.SetName(existing.Root, message);
                existing.OnClick = onClick;
                existing.Root.Cursor = onClick != null ? Cursors.Hand : Cursors.Arrow;

                ApplyKind(existing, kind);
                Restart(existing, durationMs);
                return;
            }

            var toast = Build(message, kind, key, onClick, durationMs);

            _toasts.Add(toast);
            Children.Add(toast.Root);

            // Çok bildirim birikirse en eskileri hemen kaldır
            while (_toasts.Count > MaxVisible)
                Remove(_toasts[0], animate: false);

            AnimateIn(toast.Root);
            Restart(toast, durationMs);
        }

        // ═══════════ Görünüm ═══════════

        private Toast Build(string message, ToastKind kind, string? key, Action? onClick, int durationMs)
        {
            var toast = new Toast { Key = key, OnClick = onClick, DurationMs = durationMs };

            toast.AccentBar = new Border
            {
                Width = 4,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 10, 0)
            };

            toast.Icon = new TextBlock
            {
                FontFamily = IconFont,
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 10, 0)
            };

            toast.Text = new TextBlock
            {
                Text = message,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            toast.Text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var close = new TextBlock
            {
                Text = "",
                FontFamily = IconFont,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(12, 4, 0, 0),
                Cursor = Cursors.Hand
            };
            close.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            close.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Remove(toast, animate: true);
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(toast.Icon, 1);
            Grid.SetColumn(toast.Text, 2);
            Grid.SetColumn(close, 3);

            grid.Children.Add(toast.AccentBar);
            grid.Children.Add(toast.Icon);
            grid.Children.Add(toast.Text);
            grid.Children.Add(close);

            toast.Root = new Border
            {
                Child = grid,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                MinWidth = 260,
                MaxWidth = 380,
                Opacity = 0,
                Cursor = onClick != null ? Cursors.Hand : Cursors.Arrow,
                RenderTransform = new TranslateTransform(24, 0)
            };
            toast.Root.SetResourceReference(Border.BackgroundProperty, "Surface2Brush");
            toast.Root.SetResourceReference(Border.BorderBrushProperty, "LineBrush");

            AutomationProperties.SetName(toast.Root, message);
            AutomationProperties.SetLiveSetting(toast.Root,
                kind == ToastKind.Error ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);

            toast.Root.MouseLeftButtonUp += (_, _) =>
            {
                toast.OnClick?.Invoke();
                Remove(toast, animate: true);
            };

            // Üzerine gelinirken kaybolmasın; ayrılınca kısa bir süre sonra kapansın
            toast.Root.MouseEnter += (_, _) => toast.Timer.Stop();
            toast.Root.MouseLeave += (_, _) => Restart(toast, 2000);

            toast.Timer = new DispatcherTimer();
            toast.Timer.Tick += (_, _) => Remove(toast, animate: true);

            ApplyKind(toast, kind);

            return toast;
        }

        private static void ApplyKind(Toast toast, ToastKind kind)
        {
            toast.Icon.Text = kind switch
            {
                ToastKind.Success => "",
                ToastKind.Warning => "",
                ToastKind.Error => "",
                _ => ""
            };

            if (kind == ToastKind.Info)
            {
                // Bilgi bildirimleri temanın vurgu rengini izler
                toast.AccentBar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                toast.Icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                return;
            }

            Brush brush = new SolidColorBrush(kind switch
            {
                ToastKind.Success => Color.FromRgb(0x2E, 0xA0, 0x43),
                ToastKind.Warning => Color.FromRgb(0xD2, 0x99, 0x22),
                _ => Color.FromRgb(0xE5, 0x48, 0x4D)
            });

            toast.AccentBar.Background = brush;
            toast.Icon.Foreground = brush;
        }

        // ═══════════ Zamanlayıcı ve animasyon ═══════════

        private static void Restart(Toast toast, int durationMs)
        {
            toast.Timer.Stop();
            toast.Timer.Interval = TimeSpan.FromMilliseconds(durationMs);
            toast.Timer.Start();
        }

        private static void AnimateIn(Border root)
        {
            var duration = TimeSpan.FromMilliseconds(170);

            root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));

            ((TranslateTransform)root.RenderTransform).BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(24, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private void Remove(Toast toast, bool animate)
        {
            if (toast.Closing)
                return;

            toast.Closing = true;
            toast.Timer.Stop();

            void Finish()
            {
                Children.Remove(toast.Root);
                _toasts.Remove(toast);
            }

            if (!animate)
            {
                Finish();
                return;
            }

            var fade = new DoubleAnimation(toast.Root.Opacity, 0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (_, _) => Finish();

            toast.Root.BeginAnimation(OpacityProperty, fade);
        }
    }
}
