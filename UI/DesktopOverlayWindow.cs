using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SysTelemetry.Interop;
using SysTelemetry.Models;
using SysTelemetry.Services;

namespace SysTelemetry.UI
{
    public sealed class DesktopOverlayWindow : Window
    {
        private readonly AppConfig _config;
        private readonly ContentPresenter _host = new ContentPresenter();
        private TelemetrySnapshot _lastSnap;
        private IntPtr _hwnd;
        private bool _positionedOnce;

        public IntPtr Handle { get { return _hwnd; } }

        public DesktopOverlayWindow(AppConfig config)
        {
            _config = config;
            AllowsTransparency = true;
            WindowStyle = WindowStyle.None;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Content = _host;

            SourceInitialized += OnSourceInitialized;
            SizeChanged += (s, e) => { if (double.IsNaN(_config.DesktopLeft)) EnsureInitialPosition(); };
            MouseLeftButtonDown += OnMouseLeftButtonDown;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyLockAndOpacity();
            EnsureInitialPosition();
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_config.DesktopLocked) return;
            try
            {
                DragMove();
                _config.DesktopLeft = Left;
                _config.DesktopTop = Top;
                _config.Save();
            }
            catch { }
        }

        public void ApplyLockAndOpacity()
        {
            Opacity = _config.DesktopOpacity;
            Cursor = _config.DesktopLocked ? Cursors.Arrow : Cursors.SizeAll;
            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.ApplyToolWindowStyle(_hwnd, clickThrough: _config.DesktopLocked);
            }
            Redraw();
        }

        public void ResetToTopRight()
        {
            _config.DesktopLeft = double.NaN;
            _config.DesktopTop = 16.0;
            _config.Save();
            _positionedOnce = false;
            EnsureInitialPosition();
        }

        public void UpdateMetrics(TelemetrySnapshot snap)
        {
            _lastSnap = snap;
            Redraw();
            if (!_positionedOnce || double.IsNaN(_config.DesktopLeft))
                EnsureInitialPosition();
            if (IsVisible) NativeMethods.EnsureTopmostNoActivate(_hwnd);
        }

        public void Redraw()
        {
            if (_lastSnap == null) return;
            var pal = ThemeHelper.GetPalette(_config.Theme);
            _host.Content = WidgetVisualBuilder.BuildDesktopSingleLineVisual(_lastSnap, _config, pal, !_config.DesktopLocked);
        }

        private void EnsureInitialPosition()
        {
            if (ActualWidth <= 1) return;

            var source = PresentationSource.FromVisual(this);
            if (source == null || source.CompositionTarget == null) return;

            if (!double.IsNaN(_config.DesktopLeft))
            {
                Left = _config.DesktopLeft;
                Top = _config.DesktopTop;
                _positionedOnce = true;
                return;
            }

            var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            Point topRightDip = source.CompositionTarget.TransformFromDevice.Transform(new Point(bounds.Right, bounds.Top));
            Left = topRightDip.X - ActualWidth - 18.0;
            Top = topRightDip.Y + (double.IsNaN(_config.DesktopTop) ? 16.0 : _config.DesktopTop);
            _positionedOnce = true;
        }
    }
}
