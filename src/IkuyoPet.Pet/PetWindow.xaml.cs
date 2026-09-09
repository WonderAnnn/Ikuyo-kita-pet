using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Pet.Skins;
using IkuyoPet.Pet.Interaction;

namespace IkuyoPet.Pet;

public sealed partial class PetWindow : Window, IPetWindowHost
{
    private bool hasPosition;
    private PetReminderView? currentView;
    private SkinAssetSet? skinAssets;
    private readonly PetGestureTracker gestureTracker = new(SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance);
    private readonly PetBubbleStateMachine bubbleState = new();
    private CancellationTokenSource? interactionBubbleCancellation;
    private bool dragStarted;

    public event EventHandler? InteractionRequested;

    public PetWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ClampToWorkArea();
    }

    public bool OpenedMainWindow => false;

    public event EventHandler<PetReminderActionInvokedEventArgs>? ActionInvoked;

    public Task ShowAsync(PetReminderView view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.InvokeAsync(() => ShowCore(view), System.Windows.Threading.DispatcherPriority.Normal, cancellationToken)
                .Task;
        }

        ShowCore(view);
        return Task.CompletedTask;
    }

    public new void Hide()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Hide);
            return;
        }

        if (bubbleState.State == PetBubbleState.Interaction)
        {
            interactionBubbleCancellation?.Cancel();
            bubbleState.RestoreIdle();
        }
        base.Hide();
    }
    public async Task ShowFeedbackAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        await Dispatcher.InvokeAsync(() =>
        {
            interactionBubbleCancellation?.Cancel();
            if (!bubbleState.BeginFeedback()) return;
            currentView = null;
            ReminderText.Inlines.Clear();
            ReminderText.Inlines.Add(new Run(text));
            Bubble.Visibility = Visibility.Visible;
            BubbleArrow.Visibility = Visibility.Visible;
            if (!IsVisible) Show();
            ClampToWorkArea();
        }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(2.4), cancellationToken);
        RestoreIdle();
    }

    public void RestoreIdle()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(RestoreIdle);
            return;
        }

        if (bubbleState.State == PetBubbleState.Reminder) return;
        interactionBubbleCancellation?.Cancel();
        bubbleState.RestoreIdle();
        currentView = null;
        ShowIdleSkin();
        Bubble.Visibility = Visibility.Collapsed;
        BubbleArrow.Visibility = Visibility.Collapsed;
    }

    public void SetSkinAssets(SkinAssetSet assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        skinAssets = assets;
        try
        {
            TrySetSkinImage(skinAssets.IdlePath);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            skinAssets = null;
            PetImage.Source = null;
            PetImage.Visibility = Visibility.Collapsed;
            PetPlaceholder.Visibility = Visibility.Visible;
        }
    }

    public void ShowIdleSkin()
    {
        if (skinAssets is not null) TrySetSkinImage(skinAssets.IdlePath);
    }

    public void ShowReminderSkin()
    {
        if (skinAssets is not null) TrySetSkinImage(skinAssets.RemindPath);
    }

    private void TrySetSkinImage(string imagePath)
    {
        try
        {
            SetSkinImage(imagePath);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            skinAssets = null;
            PetImage.Source = null;
            PetImage.Visibility = Visibility.Collapsed;
            PetPlaceholder.Visibility = Visibility.Visible;
        }
    }

    public void SetSkinImage(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Skin image was not found.", imagePath);
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(Path.GetFullPath(imagePath), UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        PetImage.Source = image;
        PetImage.Visibility = Visibility.Visible;
        PetPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void ShowCore(PetReminderView view)
    {
        interactionBubbleCancellation?.Cancel();
        bubbleState.BeginReminder();
        currentView = view;
        ShowReminderSkin();
        Bubble.Visibility = Visibility.Visible;
        BubbleArrow.Visibility = Visibility.Visible;
        RenderView(view);
        if (!hasPosition)
        {
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left, workArea.Right - Width - 24);
            Top = Math.Max(workArea.Top, workArea.Bottom - Height - 24);
            hasPosition = true;
        }

        if (!IsVisible)
        {
            Show();
        }

        ClampToWorkArea();
    }

    private void RenderView(PetReminderView view)
    {
        ReminderText.Inlines.Clear();
        ReminderText.Inlines.Add(new Run(view.Text));

        foreach (var action in view.Actions)
        {
            ReminderText.Inlines.Add(new Run("  ·  "));
            var link = new Hyperlink(new Run(action.Label))
            {
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(194, 86, 146)),
                TextDecorations = null,
                Tag = action.Action,
            };
            link.Click += ActionLink_OnClick;
            ReminderText.Inlines.Add(link);
        }
    }

    private void ActionLink_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink { Tag: ReminderAction action })
        {
            var eventId = currentView?.Due.EventId ?? Guid.Empty;
            ActionInvoked?.Invoke(
                this,
                new PetReminderActionInvokedEventArgs(eventId, action));
        }

        e.Handled = true;
    }

    private void PetHitArea_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || e.ClickCount > 1) return;
        gestureTracker.Press(e.GetPosition(this));
        dragStarted = false;
        PetHitArea.CaptureMouse();
        e.Handled = true;
    }

    private void PetHitArea_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (gestureTracker.Move(e.GetPosition(this)) != PetGestureResult.DragStarted) return;
        dragStarted = true;
        PetHitArea.ReleaseMouseCapture();
        DragMove();
    }

    private void PetHitArea_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var result = gestureTracker.Release(e.GetPosition(this));
        PetHitArea.ReleaseMouseCapture();
        if (!dragStarted && result == PetGestureResult.Click && e.ClickCount == 1)
            InteractionRequested?.Invoke(this, EventArgs.Empty);
        dragStarted = false;
        ClampToWorkArea();
        e.Handled = true;
    }

    private void PetHitArea_OnLostMouseCapture(object sender, MouseEventArgs e) => gestureTracker.Cancel();

    public async Task ShowInteractionAsync(string text, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        cancellationToken.ThrowIfCancellationRequested();
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var accepted = false;
        await Dispatcher.InvokeAsync(() =>
        {
            if (!bubbleState.TryBeginInteraction()) return;
            accepted = true;
            interactionBubbleCancellation?.Cancel();
            interactionBubbleCancellation = tokenSource;
            currentView = null;
            ReminderText.Inlines.Clear();
            ReminderText.Inlines.Add(new Run(text));
            Bubble.Visibility = Visibility.Visible;
            BubbleArrow.Visibility = Visibility.Visible;
            if (!IsVisible) Show();
            ClampToWorkArea();
        }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken);
        if (!accepted) { tokenSource.Dispose(); return; }
        try
        {
            await Task.Delay(duration, tokenSource.Token);
            await Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(interactionBubbleCancellation, tokenSource) && bubbleState.State == PetBubbleState.Interaction)
                    RestoreIdle();
            });
        }
        finally { tokenSource.Dispose(); }
    }
    private void ClampToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - ActualWidth));
        Top = Math.Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - ActualHeight));
    }

    protected override void OnClosed(EventArgs e)
    {
        interactionBubbleCancellation?.Cancel();
        gestureTracker.Cancel();
        PetImage.Source = null;
        base.OnClosed(e);
    }
}

public sealed class PetReminderActionInvokedEventArgs(Guid eventId, ReminderAction action) : EventArgs
{
    public Guid EventId { get; } = eventId;
    public ReminderAction Action { get; } = action;
}

public static class PetReminderActionEventFactory
{
    public static PetReminderActionInvokedEventArgs Create(
        PetReminderView view,
        ReminderAction action)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new PetReminderActionInvokedEventArgs(view.Due.EventId, action);
    }
}
