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
    private BitmapSource? currentBubbleSource;
    private string lastBubbleText = string.Empty;
    private IReadOnlyList<string> lastActionLabels = Array.Empty<string>();

    public event EventHandler? InteractionRequested;

    public PetWindow()
    {
        InitializeComponent();
        var defaultTheme = BubbleThemeCatalog.BuiltInThemes[0];
        var bubbleAssetsRoot = Path.Combine(AppContext.BaseDirectory, "assets", "bubbles");
        SetBubbleTheme(defaultTheme, Path.Combine(bubbleAssetsRoot, defaultTheme.ResourcePath));
        Loaded += (_, _) => ClampToWorkArea();
    }

    public bool OpenedMainWindow => false;
    public void ShowAtStartup()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ShowAtStartup);
            return;
        }

        PositionInsideWorkArea(forceDefault: !hasPosition);
        if (!IsVisible) Show();
        Activate();
        Dispatcher.BeginInvoke(ClampToWorkArea, System.Windows.Threading.DispatcherPriority.Loaded);
    }



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
        currentBubbleSource = BubbleRenderer.LoadBitmap(resourcePath);
        BubbleRoot.Source = currentBubbleSource;
        if (BubbleRoot.Visibility == Visibility.Visible)
        {
            var petScreenAnchor = CapturePetScreenAnchor();
            ApplyBubbleLayout(lastBubbleText, lastActionLabels, petScreenAnchor);
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
            var petScreenAnchor = CapturePetScreenAnchor();
            ApplyBubbleLayout(text, petScreenAnchor: petScreenAnchor);
            BubbleRoot.BeginAnimation(UIElement.OpacityProperty, null);
            BubbleRoot.Opacity = 1;
            BubbleRoot.Visibility = Visibility.Visible;
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
        var petScreenAnchor = CapturePetScreenAnchor();
        ShowIdleSkin();
        BubbleRoot.Visibility = Visibility.Collapsed;
        RestoreCompactWindowLayout(petScreenAnchor);
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
        BubbleRoot.BeginAnimation(UIElement.OpacityProperty, null);
        BubbleRoot.Opacity = 1;
        BubbleRoot.Visibility = Visibility.Visible;
        RenderView(view);
        ApplyBubbleLayout(view.Text, view.Actions.Select(action => action.Label).ToArray(), CapturePetScreenAnchor());
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
                new PetReminderActionInvokedEventArgs(eventId, action, currentView?.Due.Kind));
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
        ClampToWorkArea();
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
            var petScreenAnchor = CapturePetScreenAnchor();
            ApplyBubbleLayout(text, petScreenAnchor: petScreenAnchor);
            BubbleRoot.BeginAnimation(UIElement.OpacityProperty, null);
            BubbleRoot.Opacity = 0;
            BubbleRoot.Visibility = Visibility.Visible;
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
        BubbleRoot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration));
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
        BubbleRoot.BeginAnimation(UIElement.OpacityProperty, animation);
    }
    private Point? CapturePetScreenAnchor()
    {
        if (!hasPosition && !IsVisible) return null;
        if (!double.IsFinite(Left) || !double.IsFinite(Top)) return null;
        var petLeft = Canvas.GetLeft(PetHitArea);
        var petTop = Canvas.GetTop(PetHitArea);
        if (!double.IsFinite(petLeft) || !double.IsFinite(petTop)) return null;
        return new Point(Left + petLeft, Top + petTop);
    }

    private void ApplyBubbleLayout(string text, IReadOnlyList<string>? actionLabels = null, Point? petScreenAnchor = null)
    {
        lastBubbleText = text;
        lastActionLabels = actionLabels ?? Array.Empty<string>();
        if (currentBubbleTheme is null)
        {
            BubbleRoot.Width = 356;
            BubbleRoot.Height = 116;
            BubbleRoot.TextBounds = new Rect(18, 14, 320, 88);
            UpdateWindowLayout(petScreenAnchor);
            return;
        }

        var maximumBubbleHeight = Math.Clamp(SystemParameters.WorkArea.Height * 0.42, 260, 460);
        var render = BubbleRenderer.Create(
            currentBubbleTheme, currentBubbleSource, text, lastActionLabels,
            VisualTreeHelper.GetDpi(this).DpiScaleX,
            maximumBubbleHeight);
        BubbleRoot.ApplyRender(render);
        UpdateWindowLayout(petScreenAnchor);
    }

    private void UpdateWindowLayout(Point? petScreenAnchor = null)
    {
        var layout = PetBubbleLayout.Calculate(
            new Size(BubbleRoot.Width, BubbleRoot.Height),
            new Size(PetHitArea.Width, PetHitArea.Height));
        Width = layout.WindowWidth;
        Height = layout.WindowHeight;
        RootCanvas.Width = Width;
        RootCanvas.Height = Height;
        Canvas.SetLeft(BubbleRoot, layout.BubbleLeft);
        Canvas.SetTop(BubbleRoot, layout.BubbleTop);
        Canvas.SetLeft(PetHitArea, layout.PetLeft);
        Canvas.SetTop(PetHitArea, layout.PetTop);
        if (petScreenAnchor is { } anchor)
        {
            var origin = PetBubbleLayout.CalculateWindowOriginForPetAnchor(anchor, layout);
            Left = origin.X;
            Top = origin.Y;
            hasPosition = true;
        }
    }

    private void RestoreCompactWindowLayout(Point? petScreenAnchor = null)
    {
        Width = 380;
        Height = 430;
        RootCanvas.Width = Width;
        RootCanvas.Height = Height;
        Canvas.SetLeft(PetHitArea, 110);
        Canvas.SetTop(PetHitArea, 104);
        if (petScreenAnchor is { } anchor)
        {
            Left = anchor.X - 110;
            Top = anchor.Y - 104;
            hasPosition = true;
        }
        ClampToWorkArea();
    }
    private void ClampToWorkArea()
    {
        PositionInsideWorkArea(forceDefault: false);
    }

    private void PositionInsideWorkArea(bool forceDefault)
    {
        var workArea = SystemParameters.WorkArea;
        var measuredWidth = double.IsFinite(Width) && Width > 0 ? Width : ActualWidth;
        var measuredHeight = double.IsFinite(Height) && Height > 0 ? Height : ActualHeight;
        var maximumLeft = Math.Max(workArea.Left, workArea.Right - measuredWidth);
        var maximumTop = Math.Max(workArea.Top, workArea.Bottom - measuredHeight);
        if (forceDefault || !double.IsFinite(Left) || !double.IsFinite(Top))
        {
            Left = Math.Max(workArea.Left, maximumLeft - 24);
            Top = Math.Max(workArea.Top, maximumTop - 24);
            hasPosition = true;
            return;
        }
        Left = Math.Clamp(Left, workArea.Left, maximumLeft);
        Top = Math.Clamp(Top, workArea.Top, maximumTop);
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

public sealed class PetReminderActionInvokedEventArgs(Guid eventId, ReminderAction action, string? reminderKind = null) : EventArgs
{
    public Guid EventId { get; } = eventId;
    public ReminderAction Action { get; } = action;
    public string? ReminderKind { get; } = reminderKind;
}

public static class PetReminderActionEventFactory
{
    public static PetReminderActionInvokedEventArgs Create(
        PetReminderView view,
        ReminderAction action)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new PetReminderActionInvokedEventArgs(view.Due.EventId, action, view.Due.Kind);
    }
}