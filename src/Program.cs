using WinFsTools;

try
{
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
}
catch (Exception exception)
{
    MessageBox.Show($"win fs tools could not start:\n\n{exception.Message}", "win fs tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
