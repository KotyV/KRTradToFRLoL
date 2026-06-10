using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KRTradToFRLoL.Overlay;

/// <summary>
/// Plein écran translucide : l'utilisateur dessine un rectangle autour de la zone de chat.
/// Renvoie la zone en PIXELS PHYSIQUES écran (l'app est PerMonitorV2-aware).
/// </summary>
public partial class RegionSelectorWindow : Window
{
    private Point _start;
    private bool _dragging;

    /// <summary>Zone sélectionnée en pixels physiques, null si annulé.</summary>
    public System.Drawing.Rectangle? SelectedRegion { get; private set; }

    public RegionSelectorWindow()
    {
        InitializeComponent();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(RootCanvas);
        _dragging = true;
        SelectionRect.Visibility = Visibility.Visible;
        UpdateRect(_start);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging) UpdateRect(e.GetPosition(RootCanvas));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;

        var end = e.GetPosition(RootCanvas);
        // Conversion DIP → pixels physiques via PointToScreen (fenêtre maximisée à l'origine de l'écran)
        var p1 = PointToScreen(_start);
        var p2 = PointToScreen(end);
        var x = (int)Math.Min(p1.X, p2.X);
        var y = (int)Math.Min(p1.Y, p2.Y);
        var w = (int)Math.Abs(p2.X - p1.X);
        var h = (int)Math.Abs(p2.Y - p1.Y);

        if (w >= 40 && h >= 20)
            SelectedRegion = new System.Drawing.Rectangle(x, y, w, h);
        DialogResult = SelectedRegion is not null;
        Close();
    }

    private void UpdateRect(Point current)
    {
        Canvas.SetLeft(SelectionRect, Math.Min(_start.X, current.X));
        Canvas.SetTop(SelectionRect, Math.Min(_start.Y, current.Y));
        SelectionRect.Width = Math.Abs(current.X - _start.X);
        SelectionRect.Height = Math.Abs(current.Y - _start.Y);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}
