namespace NH5Toolkit
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            // Keep the hub usable even if something unexpected slips through.
            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception.Message, "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);

            ToolkitConfig.Load();
            Theme.Load();
            Application.Run(new MainForm());
        }
    }
}
