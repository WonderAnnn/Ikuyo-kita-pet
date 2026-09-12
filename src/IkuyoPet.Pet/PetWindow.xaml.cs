using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using IkuyoPet.Core.Bubbles;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Pet.Skins;
using IkuyoPet.Pet.Interaction;

namespace IkuyoPet.Pet;

public sealed partial class PetWindow : Window, IPetWindowHost
{
    private static readonly TimeSpan InteractionCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BubbleFadeDuration = TimeSpan.FromMilliseconds(350);
    private bool hasPosition;
    private PetReminderView? currentView;
    private SkinAssetSet? skinAssets;
    private readonly PetGestureTracker gestureTracker = new(SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance);
    private readonly PetBubbleStateMachine bubbleState = new();
    private readonly PetInteractionThrottle interactionThrottle = new(InteractionCooldown);
    private CancellationTokenSource? interactionFlushCancellation;
    private Task? interactionFlushTask;
    private TimeSpan interactionDuration;
    private CancellationTokenSource? interactionBubbleCancellation;
    private bool dragStarted;
    private BubbleThemeDefinition? currentBubbleTheme;
    private string lastBubbleText = string.Empty;
    private IReadOnlyList<string> lastActionLabels = Array.Empty<string>();

    public event EventHandler? InteractionRequested;

    public PetWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ClampToWorkArea();
    }

    public bool OpenedMainWindow => false;

    public event EventHandler<PetReminderActionInvokedEventArgs>? ActionInvoked;

    public void SetBubbleTheme(BubbleThemeDefinition theme, string resourcePath)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetBubbleTheme(theme, resourcePath));
            return;
        }

        currentBubbleTheme = theme;
        BubbleDecoration.SliceInsets = new Thickness(
            theme.SliceInsets.Left, theme.SliceInsets.Top,
            theme.SliceInsets.Right, theme.SliceInsets.Bottom);
        BubbleDecoration.SliceScale = CalculateSliceScale(theme);
        BubbleDecoration.Source = TryLoadBubbleImage(resourcePath);
        if (Bubble.Visibility == Visibility.Visible)
        {
            ApplyBubbleLayout(lastBubbleText, lastActionLabels);
            ClampToWorkArea();
        }
    }
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
        interactionThrottle.Reset();
        interactionFlushCancellation?.Cancel();
        base.Hide();
    }
    public async Task ShowFeedbackAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        await Dispatcher.InvokeAsync(() =>
        {
            interactionBubbleCancellation?.Cancel();
            interactionThrottle.Reset();
            interactionFlushCancellation?.Cancel();
            if (!bubbleState.BeginFeedback()) return;
            currentView = null;
            ReminderText.Inlines.Clear();
            ReminderText.Inlines.Add(new Run(text));
            ApplyBubbleLayout(text);
            Bubble.BeginAnimation(UIElement.OpacityProperty, null);
            Bubble.Opacity = 1;
            Bubble.Visibility = Visibility.Visible;
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
        RestoreCompactWindowLayout();
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
        interactionThrottle.Reset();
        interactionFlushCancellation?.Cancel();
        bubbleState.BeginReminder();
        currentView = view;
        ShowReminderSkin();
        Bubble.BeginAnimation(UIElement.OpacityProperty, null);
        Bubble.Opacity = 1;
        Bubble.Visibility = Visibility.Visible;
        RenderView(view);
        ApplyBubbleLayout(view.Text, view.Actions.Select(action => action.Label).ToArray());
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
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        var decision = await Dispatcher.InvokeAsync(
            () => interactionThrottle.Offer(text, DateTimeOffset.UtcNow),
            System.Windows.Threading.DispatcherPriority.Normal,
            cancellationToken);
        if (!decision.ShowImmediately)
        {
            interactionDuration = duration;
            ScheduleInteractionFlush(decision.NextDueAt);
            return;
        }

        await ShowInteractionNowAsync(decision.Text!, duration, cancellationToken);
    }

    private async Task ShowInteractionNowAsync(string text, TimeSpan duration, CancellationToken cancellationToken)
    {
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
            ApplyBubbleLayout(text);
            Bubble.BeginAnimation(UIElement.OpacityProperty, null);
            Bubble.Opacity = 0;
            Bubble.Visibility = Visibility.Visible;
            BeginBubbleFadeIn();
            if (!IsVisible) Show();
            ClampToWorkArea();
        }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken);
        if (!accepted)
        {
            interactionThrottle.Reset();
            tokenSource.Dispose();
            return;
        }
        try
        {
            await Task.Delay(duration, tokenSource.Token);
            await Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(interactionBubbleCancellation, tokenSource) && bubbleState.State == PetBubbleState.Interaction)
                    BeginBubbleFadeOut(tokenSource);
            });
        }
        finally { tokenSource.Dispose(); }
    }

    private void ScheduleInteractionFlush(DateTimeOffset dueAt)
    {
        if (interactionFlushTask is { IsCompleted: false }) return;
        interactionFlushCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        interactionFlushCancellation = cancellation;
        interactionFlushTask = FlushInteractionAsync(dueAt, cancellation);
    }

    private async Task FlushInteractionAsync(DateTimeOffset dueAt, CancellationTokenSource cancellation)
    {
        try
        {
            var delay = dueAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellation.Token);

            var pendingText = await Dispatcher.InvokeAsync(
                () => interactionThrottle.Flush(DateTimeOffset.UtcNow),
                System.Windows.Threading.DispatcherPriority.Normal,
                cancellation.Token);
            if (!string.IsNullOrWhiteSpace(pendingText))
                await ShowInteractionNowAsync(pendingText, interactionDuration, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(interactionFlushCancellation, cancellation))
            {
                interactionFlushCancellation = null;
                interactionFlushTask = null;
            }
            cancellation.Dispose();
        }
    }

    private void BeginBubbleFadeIn()
    {
        var duration = new Duration(BubbleFadeDuration);
        Bubble.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration));
    }

    private void BeginBubbleFadeOut(CancellationTokenSource tokenSource)
    {
        var duration = new Duration(BubbleFadeDuration);
        var animation = new DoubleAnimation(1, 0, duration);
        animation.Completed += (_, _) =>
        {
            if (ReferenceEquals(interactionBubbleCancellation, tokenSource) && bubbleState.State == PetBubbleState.Interaction)
            {
                interactionBubbleCancellation = null;
                RestoreIdle();
            }
        };
        Bubble.BeginAnimation(UIElement.OpacityProperty, animation);
    }
    private void ApplyBubbleLayout(string text, IReadOnlyList<string>? actionLabels = null)
    {
        lastBubbleText = text;
        lastActionLabels = actionLabels ?? Array.Empty<string>();
        if (currentBubbleTheme is null)
        {
            Bubble.Width = 356;
            Bubble.Height = 116;
            Canvas.SetLeft(ReminderText, 18);
            Canvas.SetTop(ReminderText, 14);
            ReminderText.Width = 320;
            ReminderText.Height = 88;
            UpdateWindowLayout();
            return;
        }

        var layout = BubbleLayoutCalculator.Calculate(currentBubbleTheme, text, lastActionLabels);
        Bubble.Width = layout.WindowWidth;
        Bubble.Height = layout.WindowHeight;
        Canvas.SetLeft(ReminderText, layout.TextArea.Left + 28);
        Canvas.SetTop(ReminderText, layout.TextArea.Top + 22);
        ReminderText.Width = layout.TextArea.Width;
        ReminderText.Height = layout.TextArea.Height;
        UpdateWindowLayout();
    }

    private void UpdateWindowLayout()
    {
        const double bubbleMargin = 12;
        const double petHeight = 300;
        const double bottomMargin = 16;
        var bubbleBottom = Canvas.GetTop(Bubble) + Bubble.Height;
        var petTop = bubbleBottom + 4;
        Width = Math.Max(380, Bubble.Width + bubbleMargin * 2);
        Height = petTop + petHeight + bottomMargin;
        RootCanvas.Width = Width;
        RootCanvas.Height = Height;
        Canvas.SetLeft(Bubble, (Width - Bubble.Width) / 2);
        Canvas.SetTop(PetHitArea, petTop);
        Canvas.SetLeft(PetHitArea, (Width - PetHitArea.Width) / 2);
    }

    private void RestoreCompactWindowLayout()
    {
        Width = 380;
        Height = 430;
        RootCanvas.Width = Width;
        RootCanvas.Height = Height;
        Canvas.SetLeft(PetHitArea, 110);
        Canvas.SetTop(PetHitArea, 104);
        ClampToWorkArea();
    }

    private static double CalculateSliceScale(BubbleThemeDefinition theme)
    {
        const double horizontalPadding = 56;
        const double verticalPadding = 44;
        return Math.Min(
            (theme.MinContentWidth + horizontalPadding) / theme.PixelWidth,
            (theme.MinContentHeight + verticalPadding) / theme.PixelHeight);
    }

    private static BitmapSource? TryLoadBubbleImage(string resourcePath)
    {
        try
        {
            if (!File.Exists(resourcePath)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(Path.GetFullPath(resourcePath), UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
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
        interactionFlushCancellation?.Cancel();
        interactionThrottle.Reset();
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
