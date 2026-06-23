using System.Windows;

namespace Agent.GUI;

public partial class MainWindow : Window
{
    private readonly LocalWebServer _webServer;

    public MainWindow()
    {
        InitializeComponent();

        _webServer = new LocalWebServer(5100, "http://localhost:7300");

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 启动本地 Web 服务器（只提供 UI 和配置 API，不管理 Host）
            await _webServer.StartAsync();

            // 初始化 WebView2 并导航
            await WebView.EnsureCoreWebView2Async();
            WebView.CoreWebView2.Navigate(_webServer.Url);
        }
        catch (Exception ex)
        {
            var errorMsg = ex.Message;

            if (errorMsg.Contains("WebView2") ||
                errorMsg.Contains("HRESULT: 0x80070002") ||
                errorMsg.Contains("找不到"))
            {
                MessageBox.Show(
                    "WebView2 Runtime 未安装。\n\n" +
                    "请使用启动器安装环境。",
                    "缺少 WebView2 Runtime",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show(
                    $"启动失败：{errorMsg}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            Close();
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _webServer.Stop();
    }
}
