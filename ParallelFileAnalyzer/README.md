# Parallel File Analyzer

## Description
Parallel File Analyzer is a .NET console application that scans a selected folder and calculates the SHA-256 hash of every file. It uses multiple configurable workers to process files concurrently and groups files with identical hashes to identify possible duplicates.

## One job
One job is the analysis of a single file: open it, calculate its SHA-256 hash, collect its size and save the result.

## Planned features
- Configurable number of parallel workers (1–32).
- Explicit `start` command to launch the worker pool.
- `pause`, `resume`, and `stop` commands that control all workers together.
- `status` command showing each worker and its current file.
- `results` command showing processed files, sizes, hashes and errors.
- Duplicate detection by comparing SHA-256 hashes.
- Asynchronous file reading so the command interface remains responsive.

## Shared data and synchronization
Workers share the job queue, the results collection, the hash groups and progress counters. `BlockingCollection` provides a thread-safe job queue, `ConcurrentBag` and `ConcurrentDictionary` store concurrent results and worker states, `Interlocked` updates counters safely, and `lock` protects updates to lists inside hash groups. A shared `ManualResetEventSlim` coordinates Pause and Resume, while `CancellationTokenSource` is used for Stop.

## Technologies
- C# / .NET 8
- Tasks and async/await
- SHA-256 (`System.Security.Cryptography`)
- Concurrent collections and synchronization primitives

## Run the project
1. Install the .NET 8 SDK.
2. Clone this repository.
3. Open a terminal in the repository folder.
4. Run:

```bash
dotnet run --project ParallelFileAnalyzer/ParallelFileAnalyzer.csproj
```

5. Enter the folder path and the number of workers.
6. Type `start` to launch the workers. Use `status`, `pause`, `resume`, `stop`, and `results` while processing.

## Project structure

```text
ParallelFileAnalyzer/
├── ParallelFileAnalyzer.sln
├── README.md
├── .gitignore
└── ParallelFileAnalyzer/
    ├── ParallelFileAnalyzer.csproj
    └── Program.cs
```

## Author
Student project – Concurrent Programming, SoftUni BUDITEL.
