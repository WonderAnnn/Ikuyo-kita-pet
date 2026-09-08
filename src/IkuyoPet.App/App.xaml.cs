using IkuyoPet.Core.Dashboard;
using IkuyoPet.Infrastructure.Storage;
using System.IO;
using System.Windows;

namespace IkuyoPet.App;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IkuyoPet");
        Directory.CreateDirectory(dataRoot);
        var databasePath = Path.Combine(dataRoot, "ikuyo-pet.db");
        var connectionString = $"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared";
        new DatabaseMigrator(connectionString).MigrateAsync().GetAwaiter().GetResult();
        var repository = new SqliteEventRepository(connectionString);
        var dashboard = new DashboardQueryService(repository);
        var viewModel = new MainWindowViewModel(dashboard);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }
}
