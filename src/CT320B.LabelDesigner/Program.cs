using CT320B.LabelDesigner.Services;

namespace CT320B.LabelDesigner;

internal static class Program
{
    /// <summary>WinForms entry point for the CT320B Label Designer.</summary>
    [STAThread]
    private static void Main()
    {
        Loc.Apply(AppSettings.Load().LanguageCode);   // localise the UI before any control is built
        ApplicationConfiguration.Initialize();        // HighDPI + default font (source-generated)
        Application.Run(new MainForm());
    }
}
