using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace LiwaPlayer
{
    // Windows görev çubuğundaki uygulama simgesinin üzerine gelince açılan
    // küçük resim (thumbnail) araç çubuğuna önceki/oynat-duraklat/sonraki
    // butonları ekler. Bu, sistem tepsisi (tray) simgesinden TAMAMEN FARKLI
    // bir özelliktir; resmi ve belgelenmiş bir Windows API'si (ITaskbarList3)
    // kullanır — YouTube tahminlerinin aksine bu kesin çalışır.
    public sealed class TaskbarThumbButtons : IDisposable
    {
        private const int WM_COMMAND = 0x0111;
        private const int THBN_CLICKED = 0x1800;

        private const uint ButtonIdPrevious = 100;
        private const uint ButtonIdPlayPause = 101;
        private const uint ButtonIdNext = 102;

        private readonly uint _wmTaskbarButtonCreated;
        private readonly ITaskbarList3 _taskbarList;
        private readonly System.Windows.Window _window;

        private HwndSource? _source;
        private IntPtr _hwnd;
        private bool _buttonsAdded;

        private readonly IntPtr _iconPrevious;
        private readonly IntPtr _iconPlay;
        private readonly IntPtr _iconPause;
        private readonly IntPtr _iconNext;

        public event Action? PreviousClicked;
        public event Action? PlayPauseClicked;
        public event Action? NextClicked;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public TaskbarThumbButtons(System.Windows.Window window)
        {
            _window = window;
            _wmTaskbarButtonCreated = RegisterWindowMessage("TaskbarButtonCreated");

            _taskbarList = (ITaskbarList3)new TaskbarInstance();
            _taskbarList.HrInit();

            // Uygulamanın diğer yerlerindeki Segoe MDL2 Assets ikonlarıyla
            // görsel tutarlılık için aynı yazı tipinden simge üretilir
            _iconPrevious = CreateGlyphIcon('');
            _iconPlay = CreateGlyphIcon('');
            _iconPause = CreateGlyphIcon('');
            _iconNext = CreateGlyphIcon('');

            if (_window.IsLoaded)
                Attach();
            else
                _window.SourceInitialized += (_, _) => Attach();
        }

        private void Attach()
        {
            _hwnd = new WindowInteropHelper(_window).Handle;
            _source = HwndSource.FromHwnd(_hwnd);
            _source?.AddHook(WndProc);

            // Görev çubuğu düğmesi genelde bu noktada zaten oluşmuş olur;
            // WM_TASKBARBUTTONCREATED mesajı kaçırılmışsa diye hemen de dene
            AddButtons();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _wmTaskbarButtonCreated)
            {
                _buttonsAdded = false;
                AddButtons();
            }
            else if (msg == WM_COMMAND)
            {
                int loWord = (short)((long)wParam & 0xFFFF);
                int hiWord = (short)(((long)wParam >> 16) & 0xFFFF);

                if (hiWord == THBN_CLICKED)
                {
                    switch ((uint)loWord)
                    {
                        case ButtonIdPrevious:
                            PreviousClicked?.Invoke();
                            handled = true;
                            break;

                        case ButtonIdPlayPause:
                            PlayPauseClicked?.Invoke();
                            handled = true;
                            break;

                        case ButtonIdNext:
                            NextClicked?.Invoke();
                            handled = true;
                            break;
                    }
                }
            }

            return IntPtr.Zero;
        }

        private void AddButtons()
        {
            if (_buttonsAdded || _hwnd == IntPtr.Zero)
                return;

            var buttons = new[]
            {
                MakeButton(ButtonIdPrevious, _iconPrevious, "Önceki"),
                MakeButton(ButtonIdPlayPause, _iconPlay, "Oynat"),
                MakeButton(ButtonIdNext, _iconNext, "Sonraki")
            };

            int hr = _taskbarList.ThumbBarAddButtons(_hwnd, (uint)buttons.Length, buttons);

            _buttonsAdded = hr == 0;
        }

        // Oynat/duraklat durumuna göre orta butonun ikon ve ipucunu günceller
        public void SetPlaying(bool isPlaying)
        {
            if (!_buttonsAdded || _hwnd == IntPtr.Zero)
                return;

            var buttons = new[]
            {
                MakeButton(ButtonIdPlayPause,
                    isPlaying ? _iconPause : _iconPlay,
                    isPlaying ? "Duraklat" : "Oynat")
            };

            _taskbarList.ThumbBarUpdateButtons(_hwnd, (uint)buttons.Length, buttons);
        }

        private static THUMBBUTTON MakeButton(uint id, IntPtr icon, string tooltip) => new()
        {
            dwMask = THBMASK.THB_ICON | THBMASK.THB_TOOLTIP | THBMASK.THB_FLAGS,
            iId = id,
            hIcon = icon,
            szTip = tooltip,
            dwFlags = THBFLAGS.THBF_ENABLED
        };

        private static IntPtr CreateGlyphIcon(char glyph)
        {
            const int size = 32;

            using var bitmap = new System.Drawing.Bitmap(size, size);
            using var g = System.Drawing.Graphics.FromImage(bitmap);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(System.Drawing.Color.Transparent);

            using var font = new System.Drawing.Font("Segoe MDL2 Assets", 16f, System.Drawing.GraphicsUnit.Pixel);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);

            var text = glyph.ToString();
            var textSize = g.MeasureString(text, font);

            g.DrawString(text, font, brush,
                (size - textSize.Width) / 2f,
                (size - textSize.Height) / 2f);

            return bitmap.GetHicon();
        }

        public void Dispose()
        {
            try
            {
                _source?.RemoveHook(WndProc);
            }
            catch
            {
            }

            foreach (var icon in new[] { _iconPrevious, _iconPlay, _iconPause, _iconNext })
            {
                if (icon != IntPtr.Zero)
                    DestroyIcon(icon);
            }
        }
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    internal class TaskbarInstance
    {
    }

    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITaskbarList3
    {
        // ITaskbarList
        [PreserveSig] void HrInit();
        [PreserveSig] void AddTab(IntPtr hwnd);
        [PreserveSig] void DeleteTab(IntPtr hwnd);
        [PreserveSig] void ActivateTab(IntPtr hwnd);
        [PreserveSig] void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        [PreserveSig] void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, uint tbpFlags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint tbatFlags);

        [PreserveSig]
        int ThumbBarAddButtons(IntPtr hwnd, uint cButtons,
            [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] pButtons);

        [PreserveSig]
        int ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons,
            [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] pButtons);

        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        void SetThumbnailClip(IntPtr hwnd, ref RECT prcClip);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct THUMBBUTTON
    {
        public THBMASK dwMask;
        public uint iId;
        public uint iBitmap;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szTip;

        public THBFLAGS dwFlags;
    }

    [Flags]
    internal enum THBMASK : uint
    {
        THB_BITMAP = 0x1,
        THB_ICON = 0x2,
        THB_TOOLTIP = 0x4,
        THB_FLAGS = 0x8
    }

    [Flags]
    internal enum THBFLAGS : uint
    {
        THBF_ENABLED = 0,
        THBF_DISABLED = 0x1,
        THBF_DISMISSONCLICK = 0x2,
        THBF_NOBACKGROUND = 0x4,
        THBF_HIDDEN = 0x8,
        THBF_NONINTERACTIVE = 0x10
    }
}
