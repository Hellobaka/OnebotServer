using OnebotServer.Core;

namespace OnebotServer.UI;

/// <summary>
/// OneBot v11 服务设置窗口：TabControl 按配置节分页编辑 config.json 并立即应用。
/// 保存/取消按钮行位于 TabControl 之外的底部独立区域。
/// </summary>
internal sealed class ConfigWindow : Form
{
    private readonly CheckBox _httpEnable = new();
    private readonly TextBox _httpHost = new();
    private readonly TextBox _httpPort = new();

    private readonly CheckBox _postEnable = new();
    private readonly TextBox _postUrl = new();
    private readonly TextBox _postSecret = new();
    private readonly TextBox _postTimeout = new();

    private readonly CheckBox _wsEnable = new();
    private readonly TextBox _wsHost = new();
    private readonly TextBox _wsPort = new();

    private readonly CheckBox _wsReverseEnable = new();
    private readonly TextBox _reverseUrl = new();
    private readonly TextBox _reverseApiUrl = new();
    private readonly TextBox _reverseEventUrl = new();
    private readonly CheckBox _reverseUniversal = new();
    private readonly TextBox _reverseReconnect = new();

    private readonly TextBox _accessToken = new();
    private readonly ComboBox _messageFormat = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _heartbeatEnable = new();
    private readonly TextBox _heartbeatInterval = new();
    private readonly TextBox _rateLimitInterval = new();

    private const int FormWidth = 540;
    private const int TabWidth = FormWidth - 24;
    private const int TabHeight = 248;
    private int _y;

    public ConfigWindow()
    {
        Text = "OneBot v11 服务设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(FormWidth, TabHeight + 70);
        SuspendLayout();

        var tabs = new TabControl
        {
            Location = new Point(12, 12),
            Size = new Size(TabWidth, TabHeight),
        };

        var tabHttp = new TabPage("HTTP 服务");
        _y = 14;
        Check(tabHttp, _httpEnable, "启用 HTTP 服务（API 调用）");
        Row(tabHttp, "监听 IP", _httpHost, "端口", _httpPort);
        tabs.TabPages.Add(tabHttp);

        var tabPost = new TabPage("HTTP POST 上报");
        _y = 14;
        Check(tabPost, _postEnable, "启用 HTTP POST 事件上报");
        Row(tabPost, "上报 URL", _postUrl);
        Row(tabPost, "签名密钥", _postSecret, "超时(秒)", _postTimeout);
        tabs.TabPages.Add(tabPost);

        var tabWs = new TabPage("正向 WebSocket");
        _y = 14;
        Check(tabWs, _wsEnable, "启用正向 WebSocket 服务");
        Row(tabWs, "监听 IP", _wsHost, "端口", _wsPort);
        tabs.TabPages.Add(tabWs);

        var tabReverse = new TabPage("反向 WebSocket");
        _y = 14;
        Check(tabReverse, _wsReverseEnable, "启用反向 WebSocket 服务");
        Row(tabReverse, "共用 URL", _reverseUrl);
        Row(tabReverse, "API URL", _reverseApiUrl);
        Row(tabReverse, "Event URL", _reverseEventUrl);
        Check(tabReverse, _reverseUniversal, "使用 Universal 客户端（单连接）");
        Row(tabReverse, "重连间隔(ms)", _reverseReconnect);
        tabs.TabPages.Add(tabReverse);

        var tabGeneral = new TabPage("通用");
        _y = 14;
        Row(tabGeneral, "access_token", _accessToken);
        Row(tabGeneral, "消息格式", _messageFormat);
        Check(tabGeneral, _heartbeatEnable, "启用心跳元事件");
        Row(tabGeneral, "心跳间隔(ms)", _heartbeatInterval, "限速(ms)", _rateLimitInterval);
        tabs.TabPages.Add(tabGeneral);

        Controls.Add(tabs);

        // 保存按钮行独立于 TabControl
        int buttonY = 12 + TabHeight + 14;
        var save = new Button { Text = "保存并应用", Location = new Point(FormWidth - 290, buttonY), Width = 130 };
        save.Click += OnSave;
        var cancel = new Button { Text = "取消", Location = new Point(FormWidth - 150, buttonY), Width = 110 };
        cancel.Click += (_, _) => Hide();
        Controls.Add(save);
        Controls.Add(cancel);

        ResumeLayout(false);
        LoadFromConfig();
    }

    private void Check(Control parent, CheckBox box, string text)
    {
        box.Text = text;
        box.Location = new Point(18, _y);
        box.AutoSize = true;
        parent.Controls.Add(box);
        _y += 28;
    }

    private void Row(Control parent, string label, Control input, string? label2 = null, Control? input2 = null)
    {
        parent.Controls.Add(new Label { Text = label, Location = new Point(22, _y + 4), AutoSize = true });
        input.Location = new Point(118, _y);
        input.Width = 160;
        parent.Controls.Add(input);
        if (label2 != null && input2 != null)
        {
            parent.Controls.Add(new Label { Text = label2, Location = new Point(298, _y + 4), AutoSize = true });
            input2.Location = new Point(382, _y);
            input2.Width = 96;
            parent.Controls.Add(input2);
        }

        _y += 32;
    }

    private void LoadFromConfig()
    {
        var cfg = PluginRuntime.Config;

        _httpEnable.Checked = cfg.Http.Enable;
        _httpHost.Text = cfg.Http.Host;
        _httpPort.Text = cfg.Http.Port.ToString();

        _postEnable.Checked = cfg.HttpPost.Enable;
        _postUrl.Text = cfg.HttpPost.Url;
        _postSecret.Text = cfg.HttpPost.Secret;
        _postTimeout.Text = cfg.HttpPost.Timeout.ToString();

        _wsEnable.Checked = cfg.Ws.Enable;
        _wsHost.Text = cfg.Ws.Host;
        _wsPort.Text = cfg.Ws.Port.ToString();

        _wsReverseEnable.Checked = cfg.WsReverse.Enable;
        _reverseUrl.Text = cfg.WsReverse.Url;
        _reverseApiUrl.Text = cfg.WsReverse.ApiUrl;
        _reverseEventUrl.Text = cfg.WsReverse.EventUrl;
        _reverseUniversal.Checked = cfg.WsReverse.UseUniversalClient;
        _reverseReconnect.Text = cfg.WsReverse.ReconnectInterval.ToString();

        _accessToken.Text = cfg.Auth.AccessToken;
        _messageFormat.Items.AddRange(new object[] { "string", "array" });
        _messageFormat.SelectedItem = cfg.Event.MessageFormat.Equals("array", StringComparison.OrdinalIgnoreCase) ? "array" : "string";
        _heartbeatEnable.Checked = cfg.Heartbeat.Enable;
        _heartbeatInterval.Text = cfg.Heartbeat.Interval.ToString();
        _rateLimitInterval.Text = cfg.Api.RateLimitInterval.ToString();
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var cfg = PluginRuntime.Config;

        cfg.Http.Enable = _httpEnable.Checked;
        cfg.Http.Host = _httpHost.Text.Trim();
        cfg.Http.Port = ParseInt(_httpPort.Text, 5700);

        cfg.HttpPost.Enable = _postEnable.Checked;
        cfg.HttpPost.Url = _postUrl.Text.Trim();
        cfg.HttpPost.Secret = _postSecret.Text;
        cfg.HttpPost.Timeout = ParseInt(_postTimeout.Text, 0);

        cfg.Ws.Enable = _wsEnable.Checked;
        cfg.Ws.Host = _wsHost.Text.Trim();
        cfg.Ws.Port = ParseInt(_wsPort.Text, 6700);

        cfg.WsReverse.Enable = _wsReverseEnable.Checked;
        cfg.WsReverse.Url = _reverseUrl.Text.Trim();
        cfg.WsReverse.ApiUrl = _reverseApiUrl.Text.Trim();
        cfg.WsReverse.EventUrl = _reverseEventUrl.Text.Trim();
        cfg.WsReverse.UseUniversalClient = _reverseUniversal.Checked;
        cfg.WsReverse.ReconnectInterval = ParseInt(_reverseReconnect.Text, 3000);

        cfg.Auth.AccessToken = _accessToken.Text;
        cfg.Event.MessageFormat = _messageFormat.SelectedItem as string ?? "string";
        cfg.Heartbeat.Enable = _heartbeatEnable.Checked;
        cfg.Heartbeat.Interval = ParseInt(_heartbeatInterval.Text, 15000);
        cfg.Api.RateLimitInterval = ParseInt(_rateLimitInterval.Text, 500);

        try
        {
            cfg.Save(PluginRuntime.ConfigPath);
            _ = Task.Run(PluginRuntime.ReloadAsync);
            MessageBox.Show(this, "配置已保存，服务正在重启。", "OneBot v11 服务", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Hide();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败: {ex.Message}", "OneBot v11 服务", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static int ParseInt(string text, int fallback)
        => int.TryParse(text.Trim(), out var value) && value >= 0 ? value : fallback;
}
