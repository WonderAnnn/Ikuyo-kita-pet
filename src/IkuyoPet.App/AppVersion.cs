namespace IkuyoPet.App;

public static class AppVersion
{
    public static string Text => typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.1.4";
}
