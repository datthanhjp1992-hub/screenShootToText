using System;
using System.Threading;
using System.Windows.Forms;

namespace ScreenOcr
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            using (var mutex = new Mutex(true, @"Local\ScreenOcr_SingleInstance_6F1B2C0E", out bool createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("ScreenOcr đang chạy rồi (xem icon ở khay hệ thống).", "ScreenOcr",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) =>
                    MessageBox.Show(e.Exception.Message, "ScreenOcr - Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);

                Application.Run(new MainForm());
            }
        }
    }
}
