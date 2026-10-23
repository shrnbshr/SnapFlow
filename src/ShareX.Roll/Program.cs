// SnapFlow, GPL-3.0-or-later. Uses the original ShareX 21.0.0 capture UI and engine.
using ShareX.HelpersLib;

namespace ShareX.Roll;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using Mutex instance = new(true, @"Local\SnapFlow", out bool first);
        if (!first) return;
        UiLanguage.Initialize(Path.Combine(AppContext.BaseDirectory, "SnapFlow.preferences.json"));
        ApplicationConfiguration.Initialize();
        ShareXResources.Name = "SnapFlow";
        Application.Run(new MainForm());
    }
}
