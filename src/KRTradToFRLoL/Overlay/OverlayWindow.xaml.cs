using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KRTradToFRLoL.Core;

namespace KRTradToFRLoL.Overlay;

/// <summary>
/// Fenêtre overlay : transparente, click-through, jamais activée, toujours au-dessus.
/// 100 % externe au jeu. Masquée automatiquement quand il n'y a aucun message
/// (une fenêtre topmost permanente peut désactiver l'independent flip / VRR du jeu).
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x80;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);

    public sealed class MessageVm : INotifyPropertyChanged
    {
        private string _body = "";
        private Brush _bodyBrush = Brushes.White;

        public string Timestamp { get; init; } = "";
        public string Speaker { get; init; } = "";
        public Brush SpeakerBrush { get; init; } = AllyBlue;
        public string Key { get; init; } = "";
        public double FontSize { get; init; } = 13;
        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        public string Body
        {
            get => _body;
            set { _body = value; OnChanged(nameof(Body)); }
        }

        public Brush BodyBrush
        {
            get => _bodyBrush;
            set { _bodyBrush = value; OnChanged(nameof(BodyBrush)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Code couleur calqué sur le chat du jeu : allié bleu, /all ennemi rouge, lobby doré.
    public static readonly Brush AllyBlue = new SolidColorBrush(Color.FromRgb(0x6F, 0xB1, 0xFF));
    public static readonly Brush EnemyRed = new SolidColorBrush(Color.FromRgb(0xFF, 0x7B, 0x6B));
    public static readonly Brush LobbyGold = new SolidColorBrush(Color.FromRgb(0xE8, 0xC3, 0x6A));

    public static Brush BrushForChannel(string channel) => channel switch
    {
        "All" => EnemyRed,
        "Lobby" => LobbyGold,
        _ => AllyBlue,
    };

    private readonly ObservableCollection<MessageVm> _messages = [];
    private readonly AppConfig _config;
    private readonly DispatcherTimer _housekeeping;
    private bool _clickThrough = true;

    public OverlayWindow(AppConfig config)
    {
        _config = config;
        InitializeComponent();
        MessagesList.ItemsSource = _messages;
        Left = config.OverlayX;
        Top = config.OverlayY;
        Width = config.OverlayWidth;

        _housekeeping = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _housekeeping.Tick += (_, _) => Housekeeping();
        _housekeeping.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyClickThrough(true);
    }

    private void ApplyClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        style = enabled ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }

    /// <summary>Mode édition : l'overlay devient déplaçable à la souris.</summary>
    public void SetEditMode(bool edit)
    {
        ApplyClickThrough(!edit);
        RootBorder.BorderBrush = edit ? Brushes.Orange : (Brush)new BrushConverter().ConvertFromString("#552EA8FF")!;
        if (edit)
        {
            MouseLeftButtonDown += OnDragMove;
            if (_messages.Count == 0)
                Upsert("démo", "12:34", "[Yuumi]", "exemple de traduction — déplace-moi puis quitte le mode édition", false);
        }
        else
        {
            MouseLeftButtonDown -= OnDragMove;
            _config.OverlayX = Left;
            _config.OverlayY = Top;
            _config.Save();
        }
    }

    private void OnDragMove(object sender, System.Windows.Input.MouseButtonEventArgs e) => DragMove();

    /// <summary>Retire un message (sa ligne d'origine a disparu du chat à l'écran).</summary>
    public void Remove(string key)
    {
        for (var i = _messages.Count - 1; i >= 0; i--)
        {
            if (_messages[i].Key == key) _messages.RemoveAt(i);
        }
    }

    /// <summary>Ajoute ou met à jour un message (clé = ligne de chat d'origine).</summary>
    public void Upsert(string key, string timestamp, string speaker, string body, bool failed, Brush? speakerBrush = null)
    {
        var existing = _messages.FirstOrDefault(m => m.Key == key);
        if (existing is null)
        {
            existing = new MessageVm
            {
                Key = key,
                Timestamp = timestamp,
                Speaker = speaker,
                SpeakerBrush = speakerBrush ?? AllyBlue,
                FontSize = _config.OverlayFontSize,
            };
            _messages.Add(existing);
            while (_messages.Count > 6) _messages.RemoveAt(0);
        }
        existing.Body = body;
        existing.BodyBrush = failed ? Brushes.LightGray : Brushes.White;
        if (!IsVisible) Show();
    }

    private void Housekeeping()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-_config.OverlayMessageLifetimeSeconds);
        for (var i = _messages.Count - 1; i >= 0; i--)
            if (_messages[i].CreatedAt < cutoff && _messages[i].Key != "démo")
                _messages.RemoveAt(i);

        if (_messages.Count == 0 && IsVisible && _clickThrough)
        {
            Hide(); // pas de fenêtre topmost inutile au-dessus du jeu
        }
        else if (IsVisible)
        {
            // Ré-assertion du topmost (robustesse broadcast)
            Topmost = false;
            Topmost = true;
        }
    }
}
