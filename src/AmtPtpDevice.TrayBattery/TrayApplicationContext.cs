namespace AmtPtpDevice.TrayBattery
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private const int PollIntervalMs = 30_000;

        private readonly NotifyIcon _notifyIcon;
        private readonly System.Windows.Forms.Timer _timer;

        public TrayApplicationContext()
        {
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("종료", null, (_, _) => ExitThread());

            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "Magic Trackpad 배터리",
                ContextMenuStrip = contextMenu,
                Icon = BatteryIconFactory.CreateUnknownIcon()
            };

            _timer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
            _timer.Tick += (_, _) => Refresh();

            Refresh();
            _timer.Start();
        }

        private void Refresh()
        {
            var oldIcon = _notifyIcon.Icon;

            if (HidBattery.TryReadChargePercent(out var percent))
            {
                _notifyIcon.Icon = BatteryIconFactory.CreatePercentIcon(percent);
                _notifyIcon.Text = $"Magic Trackpad 배터리 {percent}%";
            }
            else
            {
                _notifyIcon.Icon = BatteryIconFactory.CreateUnknownIcon();
                _notifyIcon.Text = "Magic Trackpad 연결 안 됨";
            }

            oldIcon?.Dispose();
        }

        protected override void ExitThreadCore()
        {
            _timer.Stop();
            _timer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            base.ExitThreadCore();
        }
    }
}
