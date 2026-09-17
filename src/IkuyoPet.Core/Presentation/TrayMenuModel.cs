namespace IkuyoPet.Core.Presentation;

public enum TrayCommand
{
    TogglePet,
    PauseReminders,
    OpenToday,
    Exit,
}

public sealed record TrayMenuItem(TrayCommand Command, string Label);

public static class TrayMenuModel
{
    public static IReadOnlyList<TrayMenuItem> T1 { get; } =
    [
        new(TrayCommand.TogglePet, "显示 / 隐藏桌宠"),
        new(TrayCommand.PauseReminders, "暂停提醒 30 分钟"),
        new(TrayCommand.OpenToday, "打开今日"),
        new(TrayCommand.Exit, "退出 Ikuyo Pet"),
    ];
}
