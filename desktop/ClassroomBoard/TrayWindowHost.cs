using System.ComponentModel;
using System.Windows;

namespace ClassroomBoard;

/// <summary>主窗口关闭或最小化时收进托盘；显式退出才真正关闭窗口。</summary>
public sealed class TrayWindowHost(Func<Window> createWindow) : IDisposable
{
    private Window? window;
    private bool exiting;

    public void Open()
    {
        if (exiting) return;
        if (window is null) {
            window = createWindow();
            window.Closing += OnClosing;
            window.StateChanged += OnStateChanged;
        }
        // Hide 后必须重新 Show；单独 Activate 无法唤回已隐藏的窗口。
        window.ShowInTaskbar = true;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void Hide()
    {
        window!.Hide();
        window.ShowInTaskbar = false;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        Hide();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!exiting && window?.WindowState == WindowState.Minimized) Hide();
    }

    public void Dispose()
    {
        if (exiting) return;
        exiting = true;
        if (window is null) return;
        window.Closing -= OnClosing;
        window.StateChanged -= OnStateChanged;
        window.Close();
        window = null;
    }
}
