using ShareX.Roll;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;

internal static class SettingsOpenLatencyRegression
{
    internal static double StartupMilliseconds;
    private static void Call(MainForm main, string method) => typeof(MainForm)
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);

    internal static async Task RunAsync(MainForm main, bool baseline, string? outputDirectory = null)
    {
        List<object> measurements = [];
        await Task.Delay(200);
        for (int i = 0; i < 3; i++)
        {
            Stopwatch clock = Stopwatch.StartNew();
            Call(main, "OpenSettings");
            double total = clock.Elapsed.TotalMilliseconds;
            var timing = main.SettingsOpenTiming;
            var sample = new { attempt = i + 1, dpi = main.DeviceDpi, startupMs = StartupMilliseconds, totalMs = total,
                preparationMs = timing.Preparation, constructionMs = timing.Construction,
                attachmentMs = timing.Attachment, snapshotMs = timing.Snapshot };
            measurements.Add(sample);
            Console.WriteLine(JsonSerializer.Serialize(sample));
            if (!main.Controls.Find("SettingsPage", true).Single().Visible)
                throw new InvalidOperationException("Settings did not become visible.");
            await Task.Delay(450);
            Call(main, "CloseSettings");
            await Task.Delay(300);
        }
        string output = Path.GetFullPath($"{outputDirectory ?? "artifacts/settings-open"}/{(baseline ? "before" : "after")}-{main.DeviceDpi}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: all three settings visits open and close successfully.");
    }
}
