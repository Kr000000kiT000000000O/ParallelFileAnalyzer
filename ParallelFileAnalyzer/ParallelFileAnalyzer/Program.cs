using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ParallelFileAnalyzer;

internal static class Program
{
    private static readonly ConcurrentDictionary<int, string> WorkerStatus = new();
    private static readonly ConcurrentBag<FileResult> Results = new();
    private static readonly ConcurrentDictionary<string, List<string>> HashGroups = new();
    private static readonly object HashLock = new();
    private static readonly ManualResetEventSlim PauseGate = new(initialState: true);
    private static CancellationTokenSource? _cancellation;
    private static int _completed;
    private static int _failed;
    private static int _total;

    private static async Task Main()
    {
        Console.Title = "Parallel File Analyzer";
        Console.WriteLine("====================================");
        Console.WriteLine("       PARALLEL FILE ANALYZER       ");
        Console.WriteLine("====================================\n");

        Console.Write("Folder to scan: ");
        var folder = Console.ReadLine()?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            Console.WriteLine("The folder does not exist. Press Enter to exit.");
            Console.ReadLine();
            return;
        }

        Console.Write("Number of workers (1-32, default 4): ");
        var workerInput = Console.ReadLine();
        var workerCount = int.TryParse(workerInput, out var parsed) ? Math.Clamp(parsed, 1, 32) : 4;

        string[] files;
        try
        {
            files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not scan folder: {ex.Message}");
            return;
        }

        _total = files.Length;
        Console.WriteLine($"\nFound {_total} files. Ready to start {workerCount} workers.\n");
        if (_total == 0)
        {
            Console.WriteLine("No files found.");
            return;
        }

        using var queue = new BlockingCollection<string>(new ConcurrentQueue<string>());
        foreach (var file in files) queue.Add(file);
        queue.CompleteAdding();

        _cancellation = new CancellationTokenSource();
        Task[]? workers = null;
        Task? allWorkers = null;

        PrintHelp();
        while (allWorkers is null || !allWorkers.IsCompleted)
        {
            Console.Write("\nCommand> ");
            var command = (await Console.In.ReadLineAsync())?.Trim().ToLowerInvariant();
            if (command is null)
            {
                _cancellation.Cancel();
                PauseGate.Set();
                if (allWorkers is not null) await allWorkers;
                return;
            }
            switch (command)
            {
                case "start":
                    if (allWorkers is null)
                    {
                        workers = Enumerable.Range(1, workerCount)
                            .Select(id => Task.Run(() => ProcessQueueAsync(id, queue, _cancellation.Token)))
                            .ToArray();
                        allWorkers = Task.WhenAll(workers);
                        Console.WriteLine($"Started {workerCount} workers.");
                    }
                    else Console.WriteLine("Workers have already been started.");
                    break;
                case "pause":
                    if (allWorkers is null) Console.WriteLine("Start the workers first.");
                    else
                    {
                        PauseGate.Reset();
                        Console.WriteLine("All workers paused.");
                    }
                    break;
                case "resume":
                    if (allWorkers is null) Console.WriteLine("Start the workers first.");
                    else
                    {
                        PauseGate.Set();
                        Console.WriteLine("All workers resumed.");
                    }
                    break;
                case "stop":
                    if (allWorkers is null) Console.WriteLine("Start the workers first.");
                    else
                    {
                        _cancellation.Cancel();
                        PauseGate.Set();
                        Console.WriteLine("Stop requested. Workers are finishing their current operation...");
                    }
                    break;
                case "status":
                    PrintStatus(workerCount);
                    break;
                case "results":
                    PrintResults();
                    break;
                case "help":
                    PrintHelp();
                    break;
                case "exit":
                    _cancellation.Cancel();
                    PauseGate.Set();
                    if (allWorkers is not null) await allWorkers;
                    return;
                case "":
                    break;
                default:
                    Console.WriteLine("Unknown command. Type 'help'.");
                    break;
            }
        }

        if (allWorkers is not null)
        {
            try { await allWorkers; }
            catch (OperationCanceledException) { }
        }

        Console.WriteLine("\n========== ANALYSIS FINISHED ==========");
        PrintResults();
        PrintDuplicates();
        Console.WriteLine("\nPress Enter to close.");
        Console.ReadLine();
    }

    private static async Task ProcessQueueAsync(int workerId, BlockingCollection<string> queue, CancellationToken token)
    {
        WorkerStatus[workerId] = "Waiting for a job";
        try
        {
            foreach (var file in queue.GetConsumingEnumerable(token))
            {
                token.ThrowIfCancellationRequested();
                PauseGate.Wait(token);
                WorkerStatus[workerId] = $"Analyzing: {Path.GetFileName(file)}";
                try
                {
                    await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                        bufferSize: 81920, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                    var info = new FileInfo(file);
                    Results.Add(new FileResult(file, info.Length, hash, "Success", null));

                    lock (HashLock)
                    {
                        if (!HashGroups.TryGetValue(hash, out var paths))
                        {
                            paths = new List<string>();
                            HashGroups[hash] = paths;
                        }
                        paths.Add(file);
                    }
                    Interlocked.Increment(ref _completed);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Results.Add(new FileResult(file, 0, "-", "Failed", ex.Message));
                    Interlocked.Increment(ref _failed);
                }
                WorkerStatus[workerId] = "Waiting for a job";
            }
        }
        catch (OperationCanceledException) { WorkerStatus[workerId] = "Stopped"; }
        WorkerStatus[workerId] = "Finished";
    }

    private static void PrintStatus(int workerCount)
    {
        Console.WriteLine("\nWORKERS");
        Console.WriteLine("ID  STATUS");
        for (var id = 1; id <= workerCount; id++)
            Console.WriteLine($"{id,-3} {WorkerStatus.GetValueOrDefault(id, "Starting...")}");
        Console.WriteLine($"Progress: {_completed + _failed}/{_total} | Successful: {_completed} | Failed: {_failed}");
    }

    private static void PrintResults()
    {
        Console.WriteLine("\nRESULTS (latest 15)");
        Console.WriteLine("STATUS   SIZE(bytes)  SHA-256 (first 16 chars)  FILE");
        foreach (var result in Results.Reverse().Take(15))
            Console.WriteLine($"{result.Status,-8} {result.SizeBytes,11}  {result.Hash[..Math.Min(16, result.Hash.Length)],-22} {Path.GetFileName(result.Path)}");
        Console.WriteLine($"Total processed: {_completed + _failed}/{_total}");
    }

    private static void PrintDuplicates()
    {
        var duplicates = HashGroups.Where(pair => pair.Value.Count > 1).ToList();
        Console.WriteLine($"\nDUPLICATE GROUPS: {duplicates.Count}");
        foreach (var group in duplicates)
        {
            Console.WriteLine($"Hash: {group.Key}");
            foreach (var path in group.Value) Console.WriteLine($"  - {path}");
        }
        if (duplicates.Count == 0) Console.WriteLine("No duplicate files were found.");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Commands: start | status | pause | resume | stop | results | help | exit");
        Console.WriteLine("Use 'start' to begin, then 'status' to see what every worker is processing.");
    }

    private sealed record FileResult(string Path, long SizeBytes, string Hash, string Status, string? Error);
}
