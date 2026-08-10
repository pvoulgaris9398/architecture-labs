using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LoadGenerator;

internal static class EnvironmentCollector
{
    public static async Task<EnvironmentEvidence> CaptureAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var commit = await RunAsync("git", ["rev-parse", "HEAD"], cancellationToken);
        var status = await RunAsync("git", ["status", "--porcelain"], cancellationToken);
        var sdk = await RunAsync("dotnet", ["--version"], cancellationToken);
        var docker = await RunAsync("docker", ["--version"], cancellationToken);
        var compose = await RunAsync("docker", ["compose", "version"], cancellationToken);
        var dockerMemoryText = await RunAsync(
            "docker",
            ["info", "--format", "{{.MemTotal}}"],
            cancellationToken
        );

        return new EnvironmentEvidence(
            startedAt,
            commit,
            status.Length > 0,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            GetCpuDescription(),
            Environment.ProcessorCount,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            sdk,
            RuntimeInformation.FrameworkDescription,
            docker,
            compose,
            long.TryParse(dockerMemoryText, out var dockerMemory) ? dockerMemory : null
        );
    }

    private static string GetCpuDescription()
    {
        var windowsDescription = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(windowsDescription))
            return windowsDescription;

        const string cpuInfo = "/proc/cpuinfo";
        if (File.Exists(cpuInfo))
        {
            var model = File.ReadLines(cpuInfo).FirstOrDefault(line =>
                line.StartsWith("model name", StringComparison.OrdinalIgnoreCase)
            );
            if (model is not null)
                return model[(model.IndexOf(':') + 1)..].Trim();
        }
        return "unavailable";
    }

    private static async Task<string> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{fileName} {string.Join(' ', arguments)} failed: {error}"
            );
        return output;
    }
}
