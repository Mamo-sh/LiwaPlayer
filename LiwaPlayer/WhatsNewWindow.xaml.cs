using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LiwaPlayer
{
    // Güncelleme uygulanıp uygulama yeniden başlayınca bir kez gösterilir.
    // Uygulamanın açık/koyu temasıyla uyumlu olması için (bir MessageBox'ın
    // aksine) tamamen DynamicResource fırçalarıyla, kendi penceremizde çizilir.
    public partial class WhatsNewWindow : Window
    {
        public WhatsNewWindow(string version, string notes)
        {
            InitializeComponent();

            UiScaleHelper.Apply(this);

            txtHeader.Text = $"🎉 LiwaPlayer v{version} — Yenilikler";
            Title = $"LiwaPlayer v{version} — Yenilikler";

            BuildNotes(notes);
        }

        // GitHub release notlarını (basit markdown) okunabilir bir listeye
        // çevirir: "## " satır başları başlık, "- " / "* " satır başları
        // madde işareti olur; geri kalan düz metin olarak gösterilir
        private void BuildNotes(string? notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
            {
                var fallback = new TextBlock
                {
                    Text = "Bu sürümde çeşitli iyileştirmeler ve hata düzeltmeleri yapıldı.",
                    TextWrapping = TextWrapping.Wrap
                };

                fallback.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                notesPanel.Children.Add(fallback);
                return;
            }

            foreach (var rawLine in notes.Replace("\r\n", "\n").Split('\n'))
            {
                var trimmed = rawLine.Trim();

                if (trimmed.Length == 0)
                {
                    notesPanel.Children.Add(new FrameworkElement { Height = 8 });
                    continue;
                }

                if (trimmed.StartsWith("## "))
                {
                    var header = new TextBlock
                    {
                        Text = trimmed[3..].Trim(),
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 14,
                        Margin = new Thickness(0, 6, 0, 4),
                        TextWrapping = TextWrapping.Wrap
                    };

                    header.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                    notesPanel.Children.Add(header);
                }
                else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var bullet = new TextBlock
                    {
                        Text = "•",
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    bullet.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

                    var text = new TextBlock
                    {
                        Text = trimmed[2..].Trim(),
                        TextWrapping = TextWrapping.Wrap
                    };
                    text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

                    Grid.SetColumn(text, 1);
                    row.Children.Add(bullet);
                    row.Children.Add(text);

                    notesPanel.Children.Add(row);
                }
                else
                {
                    var para = new TextBlock
                    {
                        Text = trimmed,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 2)
                    };

                    para.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                    notesPanel.Children.Add(para);
                }
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
