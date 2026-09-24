using Another_Mirai_Native.Abstractions.Attributes;
using Another_Mirai_Native.Abstractions.Context;
using Another_Mirai_Native.Abstractions.Handlers;
using OnebotServer.Core;

namespace OnebotServer.UI;

/// <summary>菜单：打开服务设置窗口。</summary>
[Menu("OneBot v11 服务设置...")]
public class ConfigMenu : IMenuHandler
{
    private ConfigWindow? _window;

    public void OnMenu(MenuContext e)
    {
        if (_window == null || _window.IsDisposed)
        {
            _window = new ConfigWindow();
            _window.FormClosing += (_, ev) =>
            {
                _window.Hide();
                ev.Cancel = true;
            };
        }

        _window.Show();
        _window.Activate();
    }
}

/// <summary>菜单：重载 config.json 并重启所有服务。</summary>
[Menu("重载 OneBot 配置")]
public class ReloadMenu : IMenuHandler
{
    public void OnMenu(MenuContext e)
    {
        var result = MessageBox.Show(
            "确定重载 OneBot 配置并重启服务？",
            "OneBot v11 服务",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);
        if (result == DialogResult.OK)
        {
            _ = Task.Run(PluginRuntime.ReloadAsync);
        }
    }
}

/// <summary>菜单：打开插件数据目录（config.json 所在目录）。</summary>
[Menu("打开 OneBot 配置目录")]
public class OpenConfigDirMenu : IMenuHandler
{
    public void OnMenu(MenuContext e)
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", PluginRuntime.AppDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开目录失败: {ex.Message}", "OneBot v11 服务", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
