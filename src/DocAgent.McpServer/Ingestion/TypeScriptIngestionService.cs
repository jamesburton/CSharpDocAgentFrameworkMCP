using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Ingests TypeScript projects via a Node.js sidecar process using the TypeScript Compiler API.
/// Supports incremental ingestion via SHA-256 file hashing.
/// </summary>
public sealed class TypeScriptIngestionService
{
    private readonly SnapshotStore _store;
    private readonly ISearchIndex _index;
    private readonly DocAgentServerOptions _options;
    private readonly PathAllowlist _allowlist;
    private readonly ILogger<TypeScriptIngestionService> _logger;

    // Optional injectable pipeline for testing.
    internal Func<string, Task<SymbolGraphSnapshot>>? PipelineOverride { get; set; }

    // Per-path semaphores — serialize same-project ingestion.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TypeScriptIngestionService(
        SnapshotStore store,
        ISearchIndex index,
        PathAllowlist allowlist,
        IOptions<DocAgentServerOptions> options,
        ILogger<TypeScriptIngestionService> logger)
    {
        _store = store;
        _index = index;
        _allowlist = allowlist;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestTypeScriptAsync(
        string tsconfigPath,
        CancellationToken cancellationToken,
        bool forceReindex = false,
        Func<int, int, string, Task>? progressCallback = null)
    {
        if (string.IsNullOrWhiteSpace(tsconfigPath))
            throw new ArgumentException("tsconfig.json path is required", nameof(tsconfigPath));

        var normalizedPath = Path.GetFullPath(tsconfigPath);

        if (!_allowlist.IsAllowed(normalizedPath))
        {
            _logger.LogWarning("Ingestion denied: path {Path} outside allowlist", normalizedPath);
            throw new UnauthorizedAccessException("Path is not in the configured allow list.");
        }

        if (!File.Exists(normalizedPath))
            throw new TypeScriptIngestionException("tsconfig_invalid", "tsconfig.json not found at the specified path.");

        var semaphore = _locks.GetOrAdd(normalizedPath, _ => new SemaphoreSlim(1, 1));

        var sidecarTimeout = _options.TypeScriptSidecarTimeoutSeconds > 0
            ? _options.TypeScriptSidecarTimeoutSeconds
            : 120;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(sidecarTimeout));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var ct = linkedCts.Token;

        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();

        try
        {
            if (progressCallback is not null)
                await progressCallback(0, 3, "starting").ConfigureAwait(false);

            // --- Incremental Check ---
            var projectDir = Path.GetDirectoryName(normalizedPath)!;
            var extensions = _options.TypeScriptFileExtensions ?? [".ts", ".tsx"];
            var tsFiles = Directory.EnumerateFiles(projectDir, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
                .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            // Include tsconfig.json and package-lock.json in manifest scope
            var manifestFiles = new List<string>(tsFiles);
            if (File.Exists(normalizedPath))
                manifestFiles.Add(normalizedPath);
            var packageLockPath = Path.Combine(projectDir, "package-lock.json");
            if (File.Exists(packageLockPath))
                manifestFiles.Add(packageLockPath);
            manifestFiles.Sort(StringComparer.Ordinal);

            var currentManifest = await FileHasher.ComputeManifestAsync(manifestFiles, ct).ConfigureAwait(false);

            // Use a stable manifest path based on the tsconfig path hash to avoid collisions
            var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath))).ToLowerInvariant()[..8];
            var manifestName = $"ts-manifest-{pathHash}.json";
            var manifestPath = Path.Combine(_store.ArtifactsDir, manifestName);

            var previousManifest = await FileHasher.LoadAsync(manifestPath, ct).ConfigureAwait(false);
            var diff = FileHasher.Diff(previousManifest, currentManifest);

            SymbolGraphSnapshot? snapshot = null;

            if (!forceReindex && !diff.HasChanges && previousManifest is not null)
            {
                // Try to load the latest snapshot for this project
                var entries = await _store.ListAsync(ct).ConfigureAwait(false);
                var projectEntries = entries.Where(e => e.ProjectName == Path.GetFileName(projectDir)).Reverse();

                foreach (var entry in projectEntries)
                {
                    var candidate = await _store.LoadAsync(entry.ContentHash, ct).ConfigureAwait(false);
                    if (candidate is not null && candidate.SourceFingerprint == "ts-compiler")
                    {
                        snapshot = candidate;
                        _logger.LogInformation("Incremental hit for TypeScript project {Project}. Reusing snapshot {Hash}.", snapshot.ProjectName, entry.ContentHash);
                        sw.Stop();
                        return new IngestionResult(
                            SnapshotId: entry.ContentHash,
                            SymbolCount: snapshot.Nodes.Count,
                            ProjectCount: 1,
                            Duration: sw.Elapsed,
                            Warnings: warnings.AsReadOnly(),
                            IndexError: null,
                            Skipped: true,
                            Reason: "no changes detected");
                    }
                }
            }

            if (progressCallback is not null)
                await progressCallback(1, 3, "extracting").ConfigureAwait(false);

            if (PipelineOverride is not null)
            {
                snapshot = await PipelineOverride(normalizedPath).ConfigureAwait(false);
            }
            else
            {
                snapshot = await RunSidecarExtractionAsync(normalizedPath, warnings, ct).ConfigureAwait(false);
            }

            // Ensure CreatedAt is fresh.
            snapshot = snapshot with { CreatedAt = DateTimeOffset.UtcNow };

            // Save snapshot
            var saved = await _store.SaveAsync(snapshot, ct: ct).ConfigureAwait(false);
            snapshot = saved;

            if (progressCallback is not null)
                await progressCallback(2, 3, "indexing").ConfigureAwait(false);

            // Index (soft failure)
            string? indexError = null;
            try
            {
                await _index.IndexAsync(saved, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Index update failed for TypeScript snapshot {SnapshotId}", saved.ContentHash);
                indexError = ex.Message;
            }

            // Save new manifest
            await FileHasher.SaveAsync(currentManifest, manifestPath, ct).ConfigureAwait(false);

            if (progressCallback is not null)
                await progressCallback(3, 3, "complete").ConfigureAwait(false);

            sw.Stop();

            return new IngestionResult(
                SnapshotId: snapshot.ContentHash ?? snapshot.SourceFingerprint,
                SymbolCount: snapshot.Nodes.Count,
                ProjectCount: 1,
                Duration: sw.Elapsed,
                Warnings: warnings.AsReadOnly(),
                IndexError: indexError);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TypeScriptIngestionException("sidecar_timeout", $"TypeScript sidecar timed out after {sidecarTimeout} seconds.");
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Discover all tsconfig.json files under a root directory and ingest each as a separate project.
    /// </summary>
    public async Task<WorkspaceIngestionResult> IngestTypeScriptWorkspaceAsync(
        string rootDir,
        CancellationToken cancellationToken,
        bool forceReindex = false,
        Func<int, int, string, Task>? progressCallback = null)
    {
        if (string.IsNullOrWhiteSpace(rootDir))
            throw new ArgumentException("Root directory path is required", nameof(rootDir));

        var normalizedRoot = Path.GetFullPath(rootDir);

        if (!_allowlist.IsAllowed(normalizedRoot))
        {
            _logger.LogWarning("Workspace ingestion denied: path {Path} outside allowlist", normalizedRoot);
            throw new UnauthorizedAccessException("Path is not in the configured allow list.");
        }

        if (!Directory.Exists(normalizedRoot))
            throw new TypeScriptIngestionException("tsconfig_invalid", "Root directory does not exist.");

        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();

        // Discover all tsconfig.json files, excluding node_modules
        var tsconfigFiles = Directory.EnumerateFiles(normalizedRoot, "tsconfig.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (tsconfigFiles.Count == 0)
        {
            warnings.Add("No tsconfig.json files found under the root directory.");
            sw.Stop();
            return new WorkspaceIngestionResult(
                TotalProjectCount: 0,
                IngestedCount: 0,
                Projects: [],
                Duration: sw.Elapsed,
                Warnings: warnings.AsReadOnly());
        }

        var results = new List<WorkspaceProjectResult>();
        var ingestedCount = 0;

        for (var i = 0; i < tsconfigFiles.Count; i++)
        {
            var tsconfig = tsconfigFiles[i];
            if (progressCallback is not null)
                await progressCallback(i, tsconfigFiles.Count, $"ingesting project {i + 1}/{tsconfigFiles.Count}: {Path.GetRelativePath(normalizedRoot, tsconfig)}").ConfigureAwait(false);

            try
            {
                var result = await IngestTypeScriptAsync(tsconfig, cancellationToken, forceReindex).ConfigureAwait(false);
                ingestedCount++;
                results.Add(new WorkspaceProjectResult(
                    TsconfigPath: Path.GetRelativePath(normalizedRoot, tsconfig),
                    SnapshotId: result.SnapshotId,
                    SymbolCount: result.SymbolCount,
                    Status: result.Skipped ? "skipped" : "success",
                    Reason: result.Reason));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to ingest TypeScript project at {Path}", tsconfig);
                warnings.Add($"Failed: {tsconfig}: {ex.Message}");
                results.Add(new WorkspaceProjectResult(
                    TsconfigPath: Path.GetRelativePath(normalizedRoot, tsconfig),
                    SnapshotId: null,
                    SymbolCount: 0,
                    Status: "error",
                    Reason: ex.Message));
            }
        }

        if (progressCallback is not null)
            await progressCallback(tsconfigFiles.Count, tsconfigFiles.Count, "complete").ConfigureAwait(false);

        sw.Stop();

        return new WorkspaceIngestionResult(
            TotalProjectCount: tsconfigFiles.Count,
            IngestedCount: ingestedCount,
            Projects: results.AsReadOnly(),
            Duration: sw.Elapsed,
            Warnings: warnings.AsReadOnly());
    }

    private async Task<SymbolGraphSnapshot> RunSidecarExtractionAsync(string tsconfigPath, ICollection<string> warnings, CancellationToken ct)
    {
        var sidecarDir = _options.SidecarDir;
        if (sidecarDir == null)
        {
            // Fallback chain for different deployment models
            var pathsToTry = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "ts-symbol-extractor"),
                Path.Combine(AppContext.BaseDirectory, "src", "ts-symbol-extractor"),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src", "ts-symbol-extractor"))
            };

            foreach (var path in pathsToTry)
            {
                if (Directory.Exists(path))
                {
                    sidecarDir = path;
                    break;
                }
            }
        }

        if (sidecarDir == null || !Directory.Exists(sidecarDir))
            throw new TypeScriptIngestionException($"Sidecar directory not found. Please configure DocAgent:SidecarDir or ensure the 'ts-symbol-extractor' folder is present in the application directory.");

        var entryPoint = Path.Combine(sidecarDir, "dist", "index.js");
        if (!File.Exists(entryPoint))
            throw new TypeScriptIngestionException($"Sidecar entry point not found at {entryPoint}. Please run 'npm install && npm run build' in the sidecar directory.");

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.NodeExecutable,
            Arguments = $"\"{entryPoint}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = sidecarDir
        };

        using var process = new Process { StartInfo = startInfo };
        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                stderrBuilder.AppendLine(e.Data);
                _logger.LogInformation("[ts-sidecar] {Line}", e.Data);
            }
        };

        if (!process.Start())
            throw new TypeScriptIngestionException("Failed to start TypeScript sidecar process.");

        process.BeginErrorReadLine();

        var tempOutputFile = Path.Combine(Path.GetTempPath(), $"ts-extract-{Guid.NewGuid()}.json");
        var request = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "extract",
            @params = new { tsconfigPath, outputPath = tempOutputFile }
        };

        var requestJson = JsonSerializer.Serialize(request);

        // Start reading stdout task - we expect a small signal response now
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);

        await process.StandardInput.WriteLineAsync(requestJson).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();

        // Wait for exit
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var signalJson = await stdoutTask.ConfigureAwait(false);

        var stderr = stderrBuilder.ToString();

        if (process.ExitCode != 0)
        {
            if (File.Exists(tempOutputFile)) try { File.Delete(tempOutputFile); } catch { }
            throw new TypeScriptIngestionException(process.ExitCode, stderr);
        }

        if (!File.Exists(tempOutputFile))
            throw new TypeScriptIngestionException("Sidecar process exited but output file was not created.");

        string responseJson;
        try
        {
            responseJson = await File.ReadAllTextAsync(tempOutputFile, ct).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(tempOutputFile); } catch { }
        }

        if (string.IsNullOrWhiteSpace(responseJson))
            throw new TypeScriptIngestionException("Sidecar process produced an empty output file.");

        try
        {
            var trimmed = responseJson.Trim();
            _logger.LogInformation("Parsing JSON starting with: {Start}", trimmed.Length > 100 ? trimmed.Substring(0, 100) : trimmed);
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorProp))
            {
                var message = errorProp.TryGetProperty("message", out var m) ? m.GetString() : "Unknown sidecar error";
                var code = errorProp.TryGetProperty("code", out var c) ? c.GetInt32() : -1;
                throw new TypeScriptIngestionException("parse_error", $"Sidecar returned error {code}: {message}");
            }

            if (!root.TryGetProperty("result", out var resultProp))
                throw new TypeScriptIngestionException("parse_error", "Invalid JSON-RPC response from sidecar: missing 'result' or 'error'.");

            var snapshot = JsonSerializer.Deserialize<SymbolGraphSnapshot>(resultProp.GetRawText(), JsonOptions);
            return snapshot ?? throw new TypeScriptIngestionException("parse_error", "Failed to deserialize SymbolGraphSnapshot from sidecar response.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse sidecar response from file. Length: {Length}", responseJson.Length);
            throw new TypeScriptIngestionException("parse_error", $"Failed to parse JSON-RPC response from sidecar. Length: {responseJson.Length}");
        }
    }
}

/// <summary>Result of a workspace-level TypeScript ingestion.</summary>
public sealed record WorkspaceIngestionResult(
    int TotalProjectCount,
    int IngestedCount,
    IReadOnlyList<WorkspaceProjectResult> Projects,
    TimeSpan Duration,
    IReadOnlyList<string> Warnings);

/// <summary>Per-project result within a workspace ingestion.</summary>
public sealed record WorkspaceProjectResult(
    string TsconfigPath,
    string? SnapshotId,
    int SymbolCount,
    string Status,
    string? Reason);
