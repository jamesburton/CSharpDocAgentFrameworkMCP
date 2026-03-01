using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DocAgent.Core;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Review;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Serialization;
using DocAgent.McpServer.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace DocAgent.McpServer.Tools;

/// <summary>
/// Three MCP change intelligence tools: review_changes, find_breaking_changes, explain_change.
/// Uses Phase 9 SymbolGraphDiffer and Plan 01 ChangeReviewer to expose semantic diff results.
/// </summary>
[McpServerToolType]
public sealed class ChangeTools
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SnapshotStore _snapshotStore;
    private readonly PathAllowlist _allowlist;
    private readonly ILogger<ChangeTools> _logger;
    private readonly DocAgentServerOptions _options;

    public ChangeTools(
        SnapshotStore snapshotStore,
        PathAllowlist allowlist,
        ILogger<ChangeTools> logger,
        IOptions<DocAgentServerOptions> options)
    {
        _snapshotStore = snapshotStore;
        _allowlist = allowlist;
        _logger = logger;
        _options = options.Value;
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool 1: review_changes
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "review_changes"), Description("Review all changes between two snapshot versions, grouped by severity with unusual pattern detection.")]
    public async Task<string> ReviewChanges(
        [Description("Before snapshot version (content hash)")] string versionA,
        [Description("After snapshot version (content hash)")] string versionB,
        [Description("Include doc-only/trivial changes (default: false)")] bool verbose = false,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity("tool.review_changes", ActivityKind.Internal);
        activity?.SetTag("tool.name", "review_changes");
        if (DocAgentTelemetry.VerboseMode)
        {
            activity?.SetTag("tool.input.versionA", versionA);
            activity?.SetTag("tool.input.versionB", versionB);
        }

        try
        {
            // PathAllowlist gate — deny access to snapshot store if directory is not allowed
            if (!_allowlist.IsAllowed(_snapshotStore.ArtifactsDir))
            {
                _logger.LogWarning("ChangeTools: snapshot store directory denied by allowlist");
                activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
                return ErrorResponse(QueryErrorKind.NotFound, "Access denied.");
            }

            // 1. Load snapshots
            var snapshotA = await _snapshotStore.LoadAsync(versionA, cancellationToken);
            if (snapshotA is null)
                return ErrorResponse(QueryErrorKind.SnapshotMissing, $"Snapshot '{versionA}' not found.");

            var snapshotB = await _snapshotStore.LoadAsync(versionB, cancellationToken);
            if (snapshotB is null)
                return ErrorResponse(QueryErrorKind.SnapshotMissing, $"Snapshot '{versionB}' not found.");

            // 2. Diff snapshots
            SymbolDiff diff;
            try
            {
                diff = SymbolGraphDiffer.Diff(snapshotA, snapshotB);
            }
            catch (ArgumentException ex)
            {
                return ErrorResponse(QueryErrorKind.InvalidInput, ex.Message);
            }

            // 3. Analyze with ChangeReviewer
            var report = ChangeReviewer.Analyze(diff, verbose);

            // 4. Populate ImpactScope for each finding using snapshotB edges
            var edgesByTo = snapshotB.Edges
                .GroupBy(e => e.To.Value, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.From.Value).ToList(), StringComparer.Ordinal);

            var findingsWithScope = report.Findings.Select(f =>
            {
                var scope = edgesByTo.TryGetValue(f.SymbolId, out var callers) ? callers : [];
                return f with { ImpactScope = scope };
            }).ToList();

            var reportWithScope = report with { Findings = findingsWithScope };

            // 5. Prompt injection scan on user-derived text
            bool hasInjectionWarning = false;
            var sanitizedFindings = reportWithScope.Findings.Select(f =>
            {
                var (desc, dWarn) = PromptInjectionScanner.Scan(f.Description);
                var (rem, rWarn) = PromptInjectionScanner.Scan(f.Remediation);
                if (dWarn || rWarn) hasInjectionWarning = true;
                return f with { Description = desc, Remediation = rem };
            }).ToList();

            var finalReport = reportWithScope with { Findings = sanitizedFindings };

            activity?.SetTag("tool.result_count", finalReport.Findings.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return FormatResponse(format,
                () => SerializeReviewChangesJson(finalReport, hasInjectionWarning),
                () => TronSerializer.SerializeChangeReview(finalReport),
                () => RenderReviewChangesMarkdown(finalReport));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool 2: find_breaking_changes
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "find_breaking_changes"), Description("Find public API breaking changes between two snapshots. CI-optimized minimal output.")]
    public async Task<string> FindBreakingChanges(
        [Description("Before snapshot version (content hash)")] string versionA,
        [Description("After snapshot version (content hash)")] string versionB,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity("tool.find_breaking_changes", ActivityKind.Internal);
        activity?.SetTag("tool.name", "find_breaking_changes");
        if (DocAgentTelemetry.VerboseMode)
        {
            activity?.SetTag("tool.input.versionA", versionA);
            activity?.SetTag("tool.input.versionB", versionB);
        }

        try
        {
            // PathAllowlist gate — deny access to snapshot store if directory is not allowed
            if (!_allowlist.IsAllowed(_snapshotStore.ArtifactsDir))
            {
                _logger.LogWarning("ChangeTools: snapshot store directory denied by allowlist");
                activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
                return ErrorResponse(QueryErrorKind.NotFound, "Access denied.");
            }

            // 1. Load snapshots
            var snapshotA = await _snapshotStore.LoadAsync(versionA, cancellationToken);
            if (snapshotA is null)
                return ErrorResponse(QueryErrorKind.SnapshotMissing, $"Snapshot '{versionA}' not found.");

            var snapshotB = await _snapshotStore.LoadAsync(versionB, cancellationToken);
            if (snapshotB is null)
                return ErrorResponse(QueryErrorKind.SnapshotMissing, $"Snapshot '{versionB}' not found.");

            // 2. Diff
            SymbolDiff diff;
            try
            {
                diff = SymbolGraphDiffer.Diff(snapshotA, snapshotB);
            }
            catch (ArgumentException ex)
            {
                return ErrorResponse(QueryErrorKind.InvalidInput, ex.Message);
            }

            // 3. Filter to Breaking only (no DocComment / Informational)
            var breakingChanges = diff.Changes
                .Where(c => c.Severity == ChangeSeverity.Breaking)
                .ToList();

            activity?.SetTag("tool.result_count", breakingChanges.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return FormatResponse(format,
                () => SerializeBreakingChangesJson(diff.BeforeSnapshotVersion, diff.AfterSnapshotVersion, breakingChanges),
                () => TronSerializer.SerializeBreakingChanges(diff.BeforeSnapshotVersion, diff.AfterSnapshotVersion, breakingChanges),
                () => RenderBreakingChangesMarkdown(diff.BeforeSnapshotVersion, diff.AfterSnapshotVersion, breakingChanges));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool 3: explain_change
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "explain_change"), Description("Get a detailed human-readable explanation of changes to a specific symbol between two snapshots.")]
    public async Task<string> ExplainChange(
        [Description("Before snapshot version (content hash)")] string versionA,
        [Description("After snapshot version (content hash)")] string versionB,
        [Description("SymbolId to explain")] string symbolId,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity("tool.explain_change", ActivityKind.Internal);
        activity?.SetTag("tool.name", "explain_change");
        if (DocAgentTelemetry.VerboseMode)
            activity?.SetTag("tool.input.symbolId", symbolId);

        try
        {
            // PathAllowlist gate — deny access to snapshot store if directory is not allowed
            if (!_allowlist.IsAllowed(_snapshotStore.ArtifactsDir))
            {
                _logger.LogWarning("ChangeTools: snapshot store directory denied by allowlist");
                activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
                return ErrorResponse(QueryErrorKind.NotFound, "Access denied.");
            }

            // 1. Load snapshots
            var snapshotA = await _snapshotStore.LoadAsync(versionA, cancellationToken);
            if (snapshotA is null)
                return ErrorResponse(QueryErrorKind.SnapshotMissing, $"Snapshot '{versionA}' not found.");

            var snapshotB = await _snapshotStore.LoadAsync(versionB, cancellationToken);
            if (snapshotB is null)
                return ErrorResponse(QueryErrorKind.SnapshotMissing, $"Snapshot '{versionB}' not found.");

            // 2. Diff
            SymbolDiff diff;
            try
            {
                diff = SymbolGraphDiffer.Diff(snapshotA, snapshotB);
            }
            catch (ArgumentException ex)
            {
                return ErrorResponse(QueryErrorKind.InvalidInput, ex.Message);
            }

            // 3. Filter to requested symbol
            var symbolChanges = diff.Changes
                .Where(c => c.SymbolId.Value == symbolId)
                .ToList();

            if (symbolChanges.Count == 0)
                return ErrorResponse(QueryErrorKind.NotFound, $"No changes found for symbol '{symbolId}'");

            // 4. Build impact scope (callers via Calls edges in snapshotB)
            var callers = snapshotB.Edges
                .Where(e => e.To.Value == symbolId && e.Kind == SymbolEdgeKind.Calls)
                .Select(e => e.From.Value)
                .ToList();

            // 5. Build change details
            var changeType = symbolChanges[0].ChangeType.ToString();
            var changeDetails = symbolChanges.Select(c => BuildChangeDetail(c, callers)).ToList();

            // 6. Prompt injection scan
            bool hasInjectionWarning = false;
            var sanitizedDetails = changeDetails.Select(d =>
            {
                var (desc, dWarn) = PromptInjectionScanner.Scan(d.Description);
                var (why, wWarn) = PromptInjectionScanner.Scan(d.WhyItMatters);
                var (rem, rWarn) = PromptInjectionScanner.Scan(d.Remediation);
                if (dWarn || wWarn || rWarn) hasInjectionWarning = true;
                return d with { Description = desc, WhyItMatters = why, Remediation = rem };
            }).ToList();

            var displayName = symbolId.Split('.').Last();

            activity?.SetTag("tool.result_count", sanitizedDetails.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return FormatResponse(format,
                () => SerializeExplainChangeJson(symbolId, displayName, changeType, sanitizedDetails, hasInjectionWarning),
                () => SerializeExplainChangeTron(symbolId, displayName, changeType, sanitizedDetails),
                () => RenderExplainChangeMarkdown(symbolId, displayName, changeType, sanitizedDetails));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Change detail building
    // ─────────────────────────────────────────────────────────────────

    private static ExplainChangeDetail BuildChangeDetail(SymbolChange change, IReadOnlyList<string> callers)
    {
        var (before, after) = change.Category switch
        {
            ChangeCategory.Signature     => (change.SignatureDetail?.OldReturnType, change.SignatureDetail?.NewReturnType),
            ChangeCategory.Nullability   => (change.NullabilityDetail?.OldAnnotation, change.NullabilityDetail?.NewAnnotation),
            ChangeCategory.Constraint    => (change.ConstraintDetail?.TypeParameterName, null),
            ChangeCategory.Accessibility => (change.AccessibilityDetail?.OldAccessibility.ToString(), change.AccessibilityDetail?.NewAccessibility.ToString()),
            ChangeCategory.Dependency    => ($"{change.DependencyDetail?.RemovedEdges.Count ?? 0} edges removed", $"{change.DependencyDetail?.AddedEdges.Count ?? 0} edges added"),
            ChangeCategory.DocComment    => (change.DocCommentDetail?.OldDocs?.Summary, change.DocCommentDetail?.NewDocs?.Summary),
            _                            => (null, null),
        };

        var severityTier = change.Severity switch
        {
            ChangeSeverity.Breaking      => "breaking",
            ChangeSeverity.NonBreaking   => "non-breaking",
            ChangeSeverity.Informational => "informational",
            _                            => "informational",
        };

        var whyItMatters = BuildWhyItMatters(change);
        var remediation = BuildExplainRemediation(change);

        var impactScope = new ExplainImpactScope(callers.Count, callers);

        return new ExplainChangeDetail(
            Category: change.Category.ToString(),
            Severity: severityTier,
            Description: change.Description,
            Before: before,
            After: after,
            WhyItMatters: whyItMatters,
            ImpactScope: impactScope,
            Remediation: remediation);
    }

    private static string BuildWhyItMatters(SymbolChange change)
    {
        return change.Category switch
        {
            ChangeCategory.Signature when change.Severity == ChangeSeverity.Breaking
                => $"This {change.Description.TrimEnd('.')}. Any caller will fail to compile.",
            ChangeCategory.Nullability
                => "Return type/parameter nullability changed. Callers without null checks will get warnings/errors.",
            ChangeCategory.Accessibility when change.AccessibilityDetail is { } accDetail
                && IsAccessibilityWidening(accDetail.OldAccessibility, accDetail.NewAccessibility)
                => "Public API surface increased. This symbol is now accessible to more consumers.",
            ChangeCategory.Constraint when change.ConstraintDetail is { } cd && cd.RemovedConstraints.Count > 0
                => "Generic constraints removed. Code that depended on constraint guarantees may break.",
            ChangeCategory.Signature
                => $"This {change.Description.TrimEnd('.')}. Review call sites for compatibility.",
            ChangeCategory.Dependency
                => "Dependency edges changed. This may affect callers that rely on specific coupling.",
            ChangeCategory.DocComment
                => "Documentation was updated. No runtime impact expected.",
            _   => change.Description,
        };
    }

    private static string? BuildExplainRemediation(SymbolChange change)
    {
        return change.Category switch
        {
            ChangeCategory.Signature when change.Severity == ChangeSeverity.Breaking
                => "Update all callers to match the new signature.",
            ChangeCategory.Signature
                => "Review call sites — signature changed but impact may be limited.",
            ChangeCategory.Nullability
                => "Ensure all callers handle the updated nullability annotation.",
            ChangeCategory.Constraint
                => "Verify that removing constraints does not break callers that relied on them.",
            ChangeCategory.Accessibility
                => "Verify intent: widening accessibility increases public API surface.",
            ChangeCategory.Dependency
                => "Review dependency edge changes for unintended coupling.",
            ChangeCategory.DocComment
                => null,
            _ => null,
        };
    }

    private static bool IsAccessibilityWidening(Accessibility oldAcc, Accessibility newAcc)
    {
        var rank = new Dictionary<Accessibility, int>
        {
            [Accessibility.Private]           = 0,
            [Accessibility.PrivateProtected]  = 1,
            [Accessibility.Internal]          = 2,
            [Accessibility.Protected]         = 3,
            [Accessibility.ProtectedInternal] = 4,
            [Accessibility.Public]            = 5,
        };
        return rank.TryGetValue(oldAcc, out int oldRank)
            && rank.TryGetValue(newAcc, out int newRank)
            && newRank > oldRank;
    }

    // ─────────────────────────────────────────────────────────────────
    // JSON serializers
    // ─────────────────────────────────────────────────────────────────

    private static string SerializeReviewChangesJson(ChangeReviewReport report, bool hasInjectionWarning)
    {
        var payload = new
        {
            beforeVersion = report.BeforeVersion,
            afterVersion = report.AfterVersion,
            promptInjectionWarning = hasInjectionWarning,
            summary = new
            {
                breaking = report.Summary.Breaking,
                warning = report.Summary.Warning,
                info = report.Summary.Info,
                trivialFiltered = report.Summary.TrivialFiltered,
                overallRisk = report.Summary.OverallRisk,
            },
            findings = report.Findings.Select(f => new
            {
                symbolId = f.SymbolId,
                displayName = f.DisplayName,
                severity = f.Severity.ToString().ToLowerInvariant(),
                category = f.Category,
                description = f.Description,
                before = f.Before,
                after = f.After,
                impactScope = f.ImpactScope,
                remediation = f.Remediation,
            }).ToList(),
            unusualFindings = report.UnusualFindings.Select(u => new
            {
                kind = u.Kind.ToString(),
                symbolId = u.SymbolId,
                description = u.Description,
                remediation = u.Remediation,
            }).ToList(),
        };
        return JsonSerializer.Serialize(payload, s_jsonOptions);
    }

    private static string SerializeBreakingChangesJson(string beforeVersion, string afterVersion, IReadOnlyList<SymbolChange> breakingChanges)
    {
        var payload = new
        {
            beforeVersion,
            afterVersion,
            breakingCount = breakingChanges.Count,
            breakingChanges = breakingChanges.Select(c => new
            {
                symbolId = c.SymbolId.Value,
                displayName = c.SymbolId.Value.Split('.').Last(),
                description = c.Description,
            }).ToList(),
        };
        return JsonSerializer.Serialize(payload, s_jsonOptions);
    }

    private static string SerializeExplainChangeJson(
        string symbolId, string displayName, string changeType,
        IReadOnlyList<ExplainChangeDetail> changes, bool hasInjectionWarning)
    {
        var payload = new
        {
            symbolId,
            displayName,
            changeType,
            promptInjectionWarning = hasInjectionWarning,
            changes = changes.Select(c => new
            {
                category = c.Category,
                severity = c.Severity,
                description = c.Description,
                before = c.Before,
                after = c.After,
                whyItMatters = c.WhyItMatters,
                impactScope = new
                {
                    callerCount = c.ImpactScope.CallerCount,
                    callers = c.ImpactScope.Callers,
                },
                remediation = c.Remediation,
            }).ToList(),
        };
        return JsonSerializer.Serialize(payload, s_jsonOptions);
    }

    private static string SerializeExplainChangeTron(
        string symbolId, string displayName, string changeType,
        IReadOnlyList<ExplainChangeDetail> changes)
    {
        using var ms = new MemoryStream();
        using var writer = new System.Text.Json.Utf8JsonWriter(ms);

        writer.WriteStartObject();
        writer.WriteStartArray("$schema");
        writer.WriteStringValue("symbolId");
        writer.WriteStringValue("displayName");
        writer.WriteStringValue("changeType");
        writer.WriteStringValue("category");
        writer.WriteStringValue("severity");
        writer.WriteStringValue("before");
        writer.WriteStringValue("after");
        writer.WriteStringValue("whyItMatters");
        writer.WriteEndArray();

        writer.WriteStartArray("data");
        foreach (var c in changes)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(symbolId);
            writer.WriteStringValue(displayName);
            writer.WriteStringValue(changeType);
            writer.WriteStringValue(c.Category);
            writer.WriteStringValue(c.Severity);
            writer.WriteStringValue(c.Before ?? string.Empty);
            writer.WriteStringValue(c.After ?? string.Empty);
            writer.WriteStringValue(c.WhyItMatters);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────
    // Markdown renderers
    // ─────────────────────────────────────────────────────────────────

    private static string RenderReviewChangesMarkdown(ChangeReviewReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Change Review: {report.BeforeVersion} → {report.AfterVersion}");
        sb.AppendLine();
        sb.AppendLine($"**Risk:** {report.Summary.OverallRisk.ToUpperInvariant()} | Breaking: {report.Summary.Breaking} | Warning: {report.Summary.Warning} | Info: {report.Summary.Info}");
        sb.AppendLine();

        if (report.Findings.Count > 0)
        {
            var bySeverity = report.Findings.GroupBy(f => f.Severity.ToString());
            foreach (var group in bySeverity)
            {
                sb.AppendLine($"## {group.Key}");
                foreach (var f in group)
                {
                    sb.AppendLine($"### `{f.DisplayName}` ({f.Category})");
                    sb.AppendLine(f.Description);
                    if (f.Before is not null) sb.AppendLine($"- **Before:** `{f.Before}`");
                    if (f.After is not null) sb.AppendLine($"- **After:** `{f.After}`");
                    if (f.ImpactScope.Count > 0) sb.AppendLine($"- **Callers:** {f.ImpactScope.Count}");
                    if (f.Remediation is not null) sb.AppendLine($"- **Remediation:** {f.Remediation}");
                    sb.AppendLine();
                }
            }
        }

        if (report.UnusualFindings.Count > 0)
        {
            sb.AppendLine("## Unusual Patterns");
            foreach (var u in report.UnusualFindings)
            {
                sb.AppendLine($"- **{u.Kind}** (`{u.SymbolId}`): {u.Description}");
                sb.AppendLine($"  - {u.Remediation}");
            }
        }

        return sb.ToString();
    }

    private static string RenderBreakingChangesMarkdown(string beforeVersion, string afterVersion, IReadOnlyList<SymbolChange> breakingChanges)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Breaking Changes: {beforeVersion} → {afterVersion}");
        sb.AppendLine($"**Total:** {breakingChanges.Count}");
        sb.AppendLine();
        foreach (var c in breakingChanges)
            sb.AppendLine($"- `{c.SymbolId.Value}`: {c.Description}");
        return sb.ToString();
    }

    private static string RenderExplainChangeMarkdown(
        string symbolId, string displayName, string changeType,
        IReadOnlyList<ExplainChangeDetail> changes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Change Explanation: `{displayName}`");
        sb.AppendLine($"**Symbol ID:** `{symbolId}`");
        sb.AppendLine($"**Change Type:** {changeType}");
        sb.AppendLine();
        foreach (var c in changes)
        {
            sb.AppendLine($"## {c.Category} ({c.Severity})");
            sb.AppendLine(c.Description);
            if (c.Before is not null) sb.AppendLine($"- **Before:** `{c.Before}`");
            if (c.After is not null) sb.AppendLine($"- **After:** `{c.After}`");
            sb.AppendLine($"- **Why it matters:** {c.WhyItMatters}");
            if (c.ImpactScope.CallerCount > 0) sb.AppendLine($"- **Callers:** {c.ImpactScope.CallerCount}");
            if (c.Remediation is not null) sb.AppendLine($"- **Remediation:** {c.Remediation}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private string FormatResponse(string format, Func<string> jsonFactory, Func<string> tronFactory, Func<string> markdownFactory)
    {
        return format.ToLowerInvariant() switch
        {
            "tron" => tronFactory(),
            "markdown" => markdownFactory(),
            _ => jsonFactory(),
        };
    }

    private string ErrorResponse(QueryErrorKind kind, string? message)
    {
        var code = kind switch
        {
            QueryErrorKind.NotFound => "not_found",
            QueryErrorKind.SnapshotMissing => "snapshot_not_found",
            QueryErrorKind.StaleIndex => "stale_index",
            QueryErrorKind.InvalidInput => "invalid_input",
            _ => "error"
        };

        var detail = _options.VerboseErrors ? message : null;
        var opaque = kind == QueryErrorKind.NotFound ? "Access denied" : (message ?? "Request failed");

        return JsonSerializer.Serialize(new
        {
            error = code,
            message = _options.VerboseErrors ? message : opaque,
            detail,
        }, s_jsonOptions);
    }

    // ─────────────────────────────────────────────────────────────────
    // Private DTOs
    // ─────────────────────────────────────────────────────────────────

    private sealed record ExplainImpactScope(int CallerCount, IReadOnlyList<string> Callers);

    private sealed record ExplainChangeDetail(
        string Category,
        string Severity,
        string Description,
        string? Before,
        string? After,
        string WhyItMatters,
        ExplainImpactScope ImpactScope,
        string? Remediation);
}
