using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using KotobaReader.Core;

namespace KotobaReader.Services;

public sealed class EngineClient(string root) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? process;
    private bool disposed;

    public async Task<JsonElement> RequestAsync(string operation, ReaderSettings settings,
        Dictionary<string, object?>? values = null, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await gate.WaitAsync(timeout.Token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            EnsureStarted(settings);
            var id = Guid.NewGuid().ToString("N");
            var request = values ?? [];
            request["id"] = id;
            request["operation"] = operation;
            request["config"] = new
            {
                tesseract = settings.TesseractPath,
                tessdata = !string.IsNullOrWhiteSpace(settings.TessdataPath) ? settings.TessdataPath
                    : Directory.Exists(Path.Combine(root, "runtime", "tessdata")) ? Path.Combine(root, "runtime", "tessdata") : "",
                models = string.IsNullOrWhiteSpace(settings.ModelsPath) ? Path.Combine(root, "models") : settings.ModelsPath
            };
            await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token)
                ?? throw new InvalidOperationException("Local engine stopped. Verify Python dependencies in Settings.");
            using var response = JsonDocument.Parse(line);
            var message = response.RootElement;
            if (message.GetProperty("id").GetString() != id) throw new InvalidDataException("Engine returned an unexpected response.");
            if (!message.GetProperty("ok").GetBoolean())
                throw new InvalidOperationException(message.GetProperty("error").GetString() ?? "Local engine failed.");
            return message.GetProperty("result").Clone();
        }
        catch (OperationCanceledException)
        {
            Stop();
            if (cancellation.IsCancellationRequested) throw;
            throw new TimeoutException("Local engine exceeded three minutes. Try a smaller selection.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            Stop();
            throw new InvalidOperationException("Could not communicate with the local engine. Check Settings and dependencies.", ex);
        }
        finally { gate.Release(); }
    }

    private void EnsureStarted(ReaderSettings settings)
    {
        if (process is { HasExited: false }) return;
        Stop();
        var bundledPython = Path.Combine(root, "runtime", "engine", "Scripts", "python.exe");
        var python = !string.IsNullOrWhiteSpace(settings.PythonPath) ? settings.PythonPath
            : File.Exists(bundledPython) ? bundledPython : "python";
        var script = Path.Combine(AppContext.BaseDirectory, "worker", "engine.py");
        if (!File.Exists(script)) script = Path.Combine(root, "worker", "engine.py");
        if (!File.Exists(script)) throw new FileNotFoundException("Engine script is missing. Reinstall KotobaReader.");
        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = root
        };
        start.ArgumentList.Add("-u"); start.ArgumentList.Add(script);
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["HF_HUB_OFFLINE"] = "1";
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Python could not start.");
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("Python was not found. Run scripts/setup-engine.ps1 or set its path in Settings.", ex);
        }
    }

    private void Stop()
    {
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); process = null; }
    }
    public void Dispose() { disposed = true; Stop(); }
}
