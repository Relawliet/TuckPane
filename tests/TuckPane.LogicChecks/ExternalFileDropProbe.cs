using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using TuckPane.Services;

internal static class ExternalFileDropProbe
{
    private const uint InputMouse = 0;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseAbsolute = 0x8000;
    private const uint MouseVirtualDesktop = 0x4000;
    private const int VirtualScreenLeft = 76;
    private const int VirtualScreenTop = 77;
    private const int VirtualScreenWidth = 78;
    private const int VirtualScreenHeight = 79;
    private const int VirtualKeyLeftButton = 0x01;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    internal static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), $"TuckPane-file-drop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string file = Path.Combine(root, "ordinary-file.txt");
            string folder = Path.Combine(root, "ordinary-folder");
            await File.WriteAllTextAsync(file, "TuckPane external file drop probe");
            Directory.CreateDirectory(folder);

            await RunCaseAsync(file, DragDropEffects.Copy, ShellDragOutcome.ExternalCopied);
            await RunCaseAsync(folder, DragDropEffects.Copy, ShellDragOutcome.ExternalCopied);
            await RunCaseAsync(file, DragDropEffects.Move, ShellDragOutcome.ExternalMoved);
            await RunCaseAsync(file, DragDropEffects.Link, ShellDragOutcome.ExternalLinked);

            string portableNote = Path.Combine(root, "portable-note.tucknote");
            string portableEvidence = Path.Combine(root, "portable-note-evidence.tucknote");
            await File.WriteAllTextAsync(portableNote, """
                {"format":"TuckPane.Note","version":1,"theme":6,"fontSize":17,"showRuledLines":true,"placement":null,"html":"<div>cross-process evidence</div><img src=\"data:image/png;base64,AA==\">"}
                """);
            await RunCaseAsync(
                portableNote,
                DragDropEffects.Copy,
                ShellDragOutcome.ExternalCopied,
                portableEvidence);
            File.Delete(portableNote);
            if (!File.Exists(portableEvidence))
                throw new InvalidOperationException("Portable-note evidence did not survive source cleanup.");
            PortableNoteEvidence captured = ReadPortableNote(File.ReadAllBytes(portableEvidence));
            if (captured.Theme != 6 || captured.FontSize != 17 || !captured.ShowRuledLines ||
                !captured.Html.Contains("data:image/png;base64,AA==", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Portable-note evidence fields were not readable after source cleanup.");
            }

            Console.WriteLine("TuckPane cross-process file/folder Copy|Move|Link and portable-note evidence drop: PASS");
        }
        finally
        {
            SendMouseButton(MouseLeftUp);
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    internal static void RunTarget(string effectName)
    {
        int separator = effectName.IndexOf('|');
        string requestedEffect = separator < 0 ? effectName : effectName[..separator];
        string? evidencePath = separator < 0 ? null : effectName[(separator + 1)..];
        if (evidencePath is not null && string.IsNullOrWhiteSpace(evidencePath))
            throw new ArgumentException("The portable-note evidence path cannot be empty.", nameof(effectName));
        DragDropEffects effect = Enum.Parse<DragDropEffects>(requestedEffect, ignoreCase: true);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunTargetWindow(effect, evidencePath); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("External drop target failed.", failure);
    }

    private static async Task RunCaseAsync(
        string sourcePath,
        DragDropEffects requestedEffect,
        ShellDragOutcome expectedOutcome,
        string? evidencePath = null)
    {
        using Process target = StartTarget(requestedEffect, evidencePath);
        var sourceReady = new TaskCompletionSource<DragSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceFinished = new TaskCompletionSource<ShellDragResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceThread = new Thread(() => RunSource(sourcePath, sourceReady, sourceFinished)) { IsBackground = true };
        sourceThread.SetApartmentState(ApartmentState.STA);
        sourceThread.Start();

        try
        {
            string readyLine = await ReadTargetLineAsync(target, "READY");
            string[] ready = readyLine.Split('\t');
            if (ready.Length != 3 ||
                !int.TryParse(ready[1], out int targetX) ||
                !int.TryParse(ready[2], out int targetY))
            {
                throw new InvalidOperationException($"External drop target returned an invalid READY message: {readyLine}");
            }

            DragSource source = await sourceReady.Task.WaitAsync(Timeout);
            _ = NativeMethods.SetForegroundWindow(source.Window);
            MoveCursor(source.Point);
            await Task.Delay(100);
            SendMouseButton(MouseLeftDown);
            if ((GetAsyncKeyState(VirtualKeyLeftButton) & 0x8000) == 0)
                throw new InvalidOperationException("Synthetic left-button press did not reach Windows.");
            await MoveCursorAsync(source.Point, new System.Drawing.Point(source.Point.X + 140, source.Point.Y));
            source.Start();
            await Task.Delay(100);
            MoveCursor(new System.Drawing.Point(source.Point.X + 141, source.Point.Y));
            await source.DragLoopReady.WaitAsync(Timeout);
            await MoveCursorAsync(source.Point, new System.Drawing.Point(targetX, targetY));
            await Task.Delay(250);
            SendMouseButton(MouseLeftUp);
            if ((GetAsyncKeyState(VirtualKeyLeftButton) & 0x8000) != 0)
                throw new InvalidOperationException("Synthetic left-button release did not reach Windows.");

            ShellDragResult result = await sourceFinished.Task.WaitAsync(Timeout);
            if (result.Outcome != expectedOutcome)
                throw new InvalidOperationException($"{requestedEffect} target returned {result.Outcome}.");
            string dropLine = await ReadTargetLineAsync(target, "DROP");
            string[] paths = JsonSerializer.Deserialize<string[]>(dropLine[5..]) ?? [];
            await target.WaitForExitAsync().WaitAsync(Timeout);

            if (paths.Length != 1 || !Path.GetFullPath(paths[0]).Equals(Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{requestedEffect} target did not receive the original path.");
            if (requestedEffect == DragDropEffects.Copy && !File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                throw new InvalidOperationException("Copy-style external drop removed the organizer source item.");
            if (evidencePath is not null && !File.Exists(evidencePath))
                throw new InvalidOperationException("The target returned before portable-note evidence was persisted.");
        }
        finally
        {
            SendMouseButton(MouseLeftUp);
            if (!target.HasExited) target.Kill(entireProcessTree: true);
        }
    }

    private static void RunSource(
        string sourcePath,
        TaskCompletionSource<DragSource> ready,
        TaskCompletionSource<ShellDragResult> finished)
    {
        using var form = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(120, 120),
            Size = new System.Drawing.Size(240, 140),
            ShowInTaskbar = false,
            TopMost = true,
            Text = "TuckPane external file drag source"
        };
        form.Shown += (_, _) =>
        {
            System.Drawing.Point point = form.PointToScreen(
                new System.Drawing.Point(form.ClientSize.Width / 2, form.ClientSize.Height / 2));
            var dragLoopReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ready.TrySetResult(new DragSource(form.Handle, point, dragLoopReady.Task, () => form.BeginInvoke(() =>
            {
                try
                {
                    finished.TrySetResult(ShellDragService.Move(form.Handle, sourcePath, () =>
                    {
                        dragLoopReady.TrySetResult();
                        return false;
                    }));
                }
                catch (Exception ex) { finished.TrySetException(ex); }
                finally { form.Close(); }
            })));
        };
        Application.Run(form);
    }

    private static void RunTargetWindow(DragDropEffects requestedEffect, string? evidencePath)
    {
        Exception? dropFailure = null;
        bool pathsReported = false;
        using var form = new Form
        {
            AllowDrop = true,
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(520, 120),
            Size = new System.Drawing.Size(320, 180),
            ShowInTaskbar = false,
            TopMost = true,
            Text = "TuckPane external file drop probe"
        };
        void SelectEffect(DragEventArgs e)
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true &&
                (e.AllowedEffect & requestedEffect) != 0
                    ? requestedEffect
                    : DragDropEffects.None;
        }
        form.DragEnter += (_, e) =>
        {
            SelectEffect(e);
            Console.Error.WriteLine($"ENTER\tFileDrop={e.Data?.GetDataPresent(DataFormats.FileDrop) == true}\tAllowed={e.AllowedEffect}\tSelected={e.Effect}");
            Console.Error.Flush();
            if (!pathsReported && (evidencePath is not null || requestedEffect == DragDropEffects.None))
            {
                pathsReported = true;
                string[] enteredPaths = e.Data?.GetData(DataFormats.FileDrop) as string[] ?? [];
                Console.WriteLine("PATHS\t" + JsonSerializer.Serialize(enteredPaths));
                Console.Out.Flush();
            }
        };
        form.DragOver += (_, e) => SelectEffect(e);
        form.DragDrop += (_, e) =>
        {
            try
            {
                SelectEffect(e);
                string[] paths = e.Data?.GetData(DataFormats.FileDrop) as string[] ?? [];
                if (evidencePath is not null)
                {
                    if (paths.Length != 1)
                        throw new InvalidDataException("Portable-note evidence capture requires exactly one FileDrop path.");
                    PortableNoteEvidence evidence = CapturePortableNoteEvidence(paths[0], evidencePath);
                    Console.WriteLine("EVIDENCE\t" + JsonSerializer.Serialize(evidence));
                    Console.Out.Flush();
                }
                Console.WriteLine("DROP\t" + JsonSerializer.Serialize(paths));
                Console.Out.Flush();
            }
            catch (Exception ex)
            {
                dropFailure = ex;
                Console.Error.WriteLine("ERROR\t" + ex);
                Console.Error.Flush();
            }
            finally { form.Close(); }
        };
        form.Shown += (_, _) =>
        {
            System.Drawing.Point point = form.PointToScreen(
                new System.Drawing.Point(form.ClientSize.Width / 2, form.ClientSize.Height / 2));
            Console.WriteLine($"READY\t{point.X}\t{point.Y}");
            Console.Out.Flush();
        };
        Application.Run(form);
        if (dropFailure is not null)
            throw new InvalidOperationException("The target could not capture portable-note evidence during Drop.", dropFailure);
    }

    private static Process StartTarget(DragDropEffects requestedEffect, string? evidencePath = null)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the logic-check executable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(ExternalFileDropProbe).Assembly.Location);
        start.ArgumentList.Add("--external-file-drop-target");
        start.ArgumentList.Add(evidencePath is null
            ? requestedEffect.ToString()
            : $"{requestedEffect}|{Path.GetFullPath(evidencePath)}");
        return Process.Start(start) ?? throw new InvalidOperationException("Cannot start the external drop target.");
    }

    private static async Task<string> ReadTargetLineAsync(Process target, string expectedPrefix)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        string? line = null;
        while (DateTime.UtcNow < deadline)
        {
            line = await target.StandardOutput.ReadLineAsync().WaitAsync(deadline - DateTime.UtcNow);
            if (line is null || line.StartsWith(expectedPrefix + "\t", StringComparison.Ordinal)) break;
        }
        if (line is not null && line.StartsWith(expectedPrefix + "\t", StringComparison.Ordinal)) return line;
        string error = target.HasExited ? await target.StandardError.ReadToEndAsync() : "target is still running";
        throw new InvalidOperationException($"External drop target did not report {expectedPrefix}: {line ?? "<EOF>"} {error}".Trim());
    }

    private static PortableNoteEvidence CapturePortableNoteEvidence(string sourcePath, string evidencePath)
    {
        if (!Path.GetExtension(sourcePath).Equals(".tucknote", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The captured FileDrop item is not a .tucknote file.");
        byte[] bytes = File.ReadAllBytes(sourcePath);
        PortableNoteEvidence evidence = ReadPortableNote(bytes);
        string fullEvidencePath = Path.GetFullPath(evidencePath);
        string? directory = Path.GetDirectoryName(fullEvidencePath);
        if (directory is not null) Directory.CreateDirectory(directory);
        if (File.Exists(fullEvidencePath))
            throw new IOException($"Portable-note evidence already exists: {fullEvidencePath}");
        File.WriteAllBytes(fullEvidencePath, bytes);
        return evidence;
    }

    private static PortableNoteEvidence ReadPortableNote(byte[] bytes)
    {
        const int maximumLength = 64 * 1024 * 1024;
        if (bytes.LongLength > maximumLength)
            throw new InvalidDataException("The portable note exceeds 64 MiB.");
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("format", out JsonElement format) ||
            format.ValueKind != JsonValueKind.String ||
            format.GetString() != "TuckPane.Note" ||
            !root.TryGetProperty("version", out JsonElement version) ||
            !version.TryGetInt32(out int versionValue) || versionValue != 1 ||
            !root.TryGetProperty("theme", out JsonElement theme) ||
            !theme.TryGetInt32(out int themeValue) || themeValue is < 0 or > 6 ||
            !root.TryGetProperty("fontSize", out JsonElement fontSize) ||
            !fontSize.TryGetDouble(out double fontSizeValue) || !double.IsFinite(fontSizeValue) ||
            fontSizeValue is < 8 or > 48 ||
            !root.TryGetProperty("showRuledLines", out JsonElement ruledLines) ||
            ruledLines.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !root.TryGetProperty("placement", out JsonElement placement) ||
            placement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object) ||
            !root.TryGetProperty("html", out JsonElement html) || html.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("The portable note does not contain readable v1 fields.");
        }
        ValidatePlacement(placement);
        return new PortableNoteEvidence(
            format.GetString()!,
            versionValue,
            themeValue,
            fontSizeValue,
            ruledLines.GetBoolean(),
            placement.ValueKind == JsonValueKind.Object,
            html.GetString()!,
            bytes.LongLength);
    }

    private static void ValidatePlacement(JsonElement placement)
    {
        if (placement.ValueKind == JsonValueKind.Null) return;
        if (!placement.TryGetProperty("monitorDevice", out JsonElement monitor) || monitor.ValueKind != JsonValueKind.String ||
            !TryGetFiniteNumber(placement, "xDip", out _) ||
            !TryGetFiniteNumber(placement, "yDip", out _) ||
            !TryGetFiniteNumber(placement, "widthDip", out double width) || width is < 280 or > 1600 ||
            !TryGetFiniteNumber(placement, "heightDip", out double height) || height is < 220 or > 1200)
        {
            throw new InvalidDataException("The portable note placement is invalid.");
        }
    }

    private static bool TryGetFiniteNumber(JsonElement owner, string name, out double value)
    {
        value = 0;
        return owner.TryGetProperty(name, out JsonElement property) &&
            property.TryGetDouble(out value) &&
            double.IsFinite(value);
    }

    private static async Task MoveCursorAsync(System.Drawing.Point start, System.Drawing.Point end)
    {
        const int steps = 16;
        for (int step = 1; step <= steps; step++)
        {
            int x = start.X + (end.X - start.X) * step / steps;
            int y = start.Y + (end.Y - start.Y) * step / steps;
            MoveCursor(new System.Drawing.Point(x, y));
            await Task.Delay(15);
        }
    }

    private static void MoveCursor(System.Drawing.Point point)
    {
        int left = GetSystemMetrics(VirtualScreenLeft);
        int top = GetSystemMetrics(VirtualScreenTop);
        int width = Math.Max(2, GetSystemMetrics(VirtualScreenWidth));
        int height = Math.Max(2, GetSystemMetrics(VirtualScreenHeight));
        SendMouse(
            (int)Math.Round((point.X - left) * 65535d / (width - 1)),
            (int)Math.Round((point.Y - top) * 65535d / (height - 1)),
            MouseMove | MouseAbsolute | MouseVirtualDesktop);
    }

    private static void SendMouseButton(uint flags) => SendMouse(0, 0, flags);

    private static void SendMouse(int x, int y, uint flags)
    {
        var input = new Input
        {
            Type = InputMouse,
            Mouse = new MouseInput { X = x, Y = y, Flags = flags }
        };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            throw new InvalidOperationException($"SendInput failed with Win32 error {Marshal.GetLastWin32Error()}.");
    }

    private sealed record DragSource(IntPtr Window, System.Drawing.Point Point, Task DragLoopReady, Action Start);

    private sealed record PortableNoteEvidence(
        string Format,
        int Version,
        int Theme,
        double FontSize,
        bool ShowRuledLines,
        bool HasPlacement,
        string Html,
        long Length);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
