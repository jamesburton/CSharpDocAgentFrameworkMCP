namespace DocAgent.Core;

/// <summary>
/// Pure stateless algorithm that compares two <see cref="SymbolGraphSnapshot"/> instances
/// and produces a deterministic <see cref="SymbolDiff"/> result.
/// </summary>
public static class SymbolGraphDiffer
{
    // ── Public entry point ────────────────────────────────────────────────

    /// <summary>
    /// Compares <paramref name="before"/> and <paramref name="after"/> snapshots and returns
    /// a fully-populated <see cref="SymbolDiff"/> with all detected changes.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the snapshots belong to different projects.
    /// </exception>
    public static SymbolDiff Diff(SymbolGraphSnapshot before, SymbolGraphSnapshot after)
    {
        if (before.ProjectName != after.ProjectName)
            throw new ArgumentException(
                $"Cannot diff snapshots from different projects: '{before.ProjectName}' vs '{after.ProjectName}'.");

        // 1. Index nodes by SymbolId.Value
        var beforeIndex = IndexNodes(before);
        var afterIndex  = IndexNodes(after);

        // 2. Index edges by From.Value
        var beforeEdges = IndexEdges(before);
        var afterEdges  = IndexEdges(after);

        var changes = new List<SymbolChange>();

        // 3. Added symbols (in after but not before)
        foreach (var key in afterIndex.Keys.Except(beforeIndex.Keys, StringComparer.Ordinal))
        {
            var node     = afterIndex[key];
            var parentId = FindParentId(node.Id.Value, afterEdges);
            var severity = DetermineAddedSeverity(node.Accessibility);
            changes.Add(new SymbolChange(
                SymbolId:               node.Id,
                BeforeSnapshotSymbolId: null,
                AfterSnapshotSymbolId:  node.Id,
                ParentSymbolId:         parentId,
                ChangeType:             ChangeType.Added,
                Category:               ChangeCategory.Signature,
                Severity:               severity,
                Description:            $"Symbol '{node.DisplayName}' added.",
                SignatureDetail:        null,
                NullabilityDetail:      null,
                ConstraintDetail:       null,
                AccessibilityDetail:    null,
                DependencyDetail:       null,
                DocCommentDetail:       null));
        }

        // 4. Removed symbols (in before but not after)
        foreach (var key in beforeIndex.Keys.Except(afterIndex.Keys, StringComparer.Ordinal))
        {
            var node     = beforeIndex[key];
            var parentId = FindParentId(node.Id.Value, beforeEdges);
            var severity = DetermineRemovedOrModifiedSeverity(node.Accessibility, ChangeCategory.Signature);
            changes.Add(new SymbolChange(
                SymbolId:               node.Id,
                BeforeSnapshotSymbolId: node.Id,
                AfterSnapshotSymbolId:  null,
                ParentSymbolId:         parentId,
                ChangeType:             ChangeType.Removed,
                Category:               ChangeCategory.Signature,
                Severity:               severity,
                Description:            $"Symbol '{node.DisplayName}' removed.",
                SignatureDetail:        null,
                NullabilityDetail:      null,
                ConstraintDetail:       null,
                AccessibilityDetail:    null,
                DependencyDetail:       null,
                DocCommentDetail:       null));
        }

        // 5. Modified symbols (present in both)
        foreach (var key in beforeIndex.Keys.Intersect(afterIndex.Keys, StringComparer.Ordinal))
        {
            var b        = beforeIndex[key];
            var a        = afterIndex[key];
            var parentId = FindParentId(key, afterEdges);

            changes.AddRange(CompareSignature(b, a, parentId));
            changes.AddRange(CompareNullability(b, a, parentId));
            changes.AddRange(CompareConstraints(b, a, parentId));
            changes.AddRange(CompareAccessibility(b, a, parentId));
            changes.AddRange(CompareDependencies(b, a, parentId, beforeEdges, afterEdges));
            changes.AddRange(CompareDocComments(b, a, parentId));
        }

        // 6. Deterministic sort: SymbolId.Value (Ordinal) then Category (enum int value)
        changes.Sort((x, y) =>
        {
            int c = StringComparer.Ordinal.Compare(x.SymbolId.Value, y.SymbolId.Value);
            return c != 0 ? c : x.Category.CompareTo(y.Category);
        });

        // 7. Build summary
        var summary = BuildSummary(changes);

        return new SymbolDiff(
            BeforeSnapshotVersion: before.SourceFingerprint,
            AfterSnapshotVersion:  after.SourceFingerprint,
            ProjectName:           before.ProjectName,
            Summary:               summary,
            Changes:               changes);
    }

    // ── Indexers ──────────────────────────────────────────────────────────

    private static Dictionary<string, SymbolNode> IndexNodes(SymbolGraphSnapshot snap)
    {
        var dict = new Dictionary<string, SymbolNode>(StringComparer.Ordinal);
        foreach (var node in snap.Nodes)
            dict[node.Id.Value] = node;
        return dict;
    }

    private static Dictionary<string, List<SymbolEdge>> IndexEdges(SymbolGraphSnapshot snap)
    {
        var dict = new Dictionary<string, List<SymbolEdge>>(StringComparer.Ordinal);
        foreach (var edge in snap.Edges)
        {
            if (!dict.TryGetValue(edge.From.Value, out var list))
                dict[edge.From.Value] = list = new List<SymbolEdge>();
            list.Add(edge);
        }
        return dict;
    }

    // ── Parent resolution ─────────────────────────────────────────────────

    private static SymbolId? FindParentId(
        string symbolKey,
        Dictionary<string, List<SymbolEdge>> edgesByFrom)
    {
        // Scan all edge lists for a Contains edge where To == symbolKey
        foreach (var (_, edges) in edgesByFrom)
        {
            foreach (var edge in edges)
            {
                if (edge.Kind == SymbolEdgeKind.Contains &&
                    StringComparer.Ordinal.Equals(edge.To.Value, symbolKey))
                    return edge.From;
            }
        }
        return null;
    }

    // ── Severity helpers ──────────────────────────────────────────────────

    private static ChangeSeverity DetermineAddedSeverity(Accessibility _)
        // Additive changes are always NonBreaking regardless of visibility
        => ChangeSeverity.NonBreaking;

    private static ChangeSeverity DetermineRemovedOrModifiedSeverity(
        Accessibility accessibility,
        ChangeCategory category)
    {
        if (category == ChangeCategory.DocComment)
            return ChangeSeverity.Informational;

        return accessibility switch
        {
            Accessibility.Public            => ChangeSeverity.Breaking,
            Accessibility.Protected         => ChangeSeverity.Breaking,
            Accessibility.ProtectedInternal => ChangeSeverity.Breaking,
            _                               => ChangeSeverity.NonBreaking,
        };
    }

    // ── Comparison helpers ────────────────────────────────────────────────

    private static IEnumerable<SymbolChange> CompareSignature(
        SymbolNode b, SymbolNode a, SymbolId? parentId)
    {
        var paramChanges = new List<ParameterChange>();
        string? sigDescription = null;
        bool returnTypeChanged = false;

        // Return type
        if (b.ReturnType != a.ReturnType)
        {
            // Only count as signature if more than just a nullability '?' difference
            if (!IsOnlyNullabilityDiff(b.ReturnType, a.ReturnType))
            {
                returnTypeChanged = true;
                sigDescription = $"Return type changed from '{b.ReturnType}' to '{a.ReturnType}'.";
            }
        }

        // Parameters by position
        int maxLen = Math.Max(b.Parameters.Count, a.Parameters.Count);
        for (int i = 0; i < maxLen; i++)
        {
            if (i >= b.Parameters.Count)
            {
                // Added parameter
                var ap = a.Parameters[i];
                paramChanges.Add(new ParameterChange(
                    ChangeType.Added, ap.Name, null, ap.TypeName, null, ap.DefaultValue));
            }
            else if (i >= a.Parameters.Count)
            {
                // Removed parameter
                var bp = b.Parameters[i];
                paramChanges.Add(new ParameterChange(
                    ChangeType.Removed, bp.Name, bp.TypeName, null, bp.DefaultValue, null));
            }
            else
            {
                var bp = b.Parameters[i];
                var ap = a.Parameters[i];
                bool typeChanged    = bp.TypeName != ap.TypeName && !IsOnlyNullabilityDiff(bp.TypeName, ap.TypeName);
                bool nameChanged    = bp.Name != ap.Name;
                bool defaultChanged = bp.DefaultValue != ap.DefaultValue;

                if (typeChanged || nameChanged || defaultChanged)
                {
                    paramChanges.Add(new ParameterChange(
                        ChangeType.Modified,
                        ap.Name,
                        bp.TypeName,
                        ap.TypeName,
                        bp.DefaultValue,
                        ap.DefaultValue));
                }
            }
        }

        if (!returnTypeChanged && paramChanges.Count == 0)
            yield break;

        sigDescription ??= BuildParameterChangeSummary(paramChanges);

        var detail = new SignatureChangeDetail(
            sigDescription,
            paramChanges,
            b.ReturnType,
            a.ReturnType);

        yield return new SymbolChange(
            SymbolId:               b.Id,
            BeforeSnapshotSymbolId: b.Id,
            AfterSnapshotSymbolId:  a.Id,
            ParentSymbolId:         parentId,
            ChangeType:             ChangeType.Modified,
            Category:               ChangeCategory.Signature,
            Severity:               DetermineRemovedOrModifiedSeverity(b.Accessibility, ChangeCategory.Signature),
            Description:            sigDescription,
            SignatureDetail:        detail,
            NullabilityDetail:      null,
            ConstraintDetail:       null,
            AccessibilityDetail:    null,
            DependencyDetail:       null,
            DocCommentDetail:       null);
    }

    private static IEnumerable<SymbolChange> CompareNullability(
        SymbolNode b, SymbolNode a, SymbolId? parentId)
    {
        bool returnNullChanged = IsOnlyNullabilityDiff(b.ReturnType, a.ReturnType);
        bool paramNullChanged  = false;

        for (int i = 0; i < Math.Min(b.Parameters.Count, a.Parameters.Count); i++)
        {
            if (IsOnlyNullabilityDiff(b.Parameters[i].TypeName, a.Parameters[i].TypeName))
            {
                paramNullChanged = true;
                break;
            }
        }

        if (!returnNullChanged && !paramNullChanged)
            yield break;

        string desc = returnNullChanged
            ? $"Nullability changed on return type: '{b.ReturnType}' → '{a.ReturnType}'."
            : "Nullability changed on parameter type.";

        var detail = new NullabilityChangeDetail(
            desc,
            b.ReturnType,
            a.ReturnType);

        yield return new SymbolChange(
            SymbolId:               b.Id,
            BeforeSnapshotSymbolId: b.Id,
            AfterSnapshotSymbolId:  a.Id,
            ParentSymbolId:         parentId,
            ChangeType:             ChangeType.Modified,
            Category:               ChangeCategory.Nullability,
            Severity:               DetermineRemovedOrModifiedSeverity(b.Accessibility, ChangeCategory.Nullability),
            Description:            desc,
            SignatureDetail:        null,
            NullabilityDetail:      detail,
            ConstraintDetail:       null,
            AccessibilityDetail:    null,
            DependencyDetail:       null,
            DocCommentDetail:       null);
    }

    private static IEnumerable<SymbolChange> CompareConstraints(
        SymbolNode b, SymbolNode a, SymbolId? parentId)
    {
        // Index by TypeParameterName
        var beforeMap = b.GenericConstraints.ToDictionary(c => c.TypeParameterName, StringComparer.Ordinal);
        var afterMap  = a.GenericConstraints.ToDictionary(c => c.TypeParameterName, StringComparer.Ordinal);

        var allKeys = beforeMap.Keys.Union(afterMap.Keys, StringComparer.Ordinal);

        foreach (var tpName in allKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            beforeMap.TryGetValue(tpName, out var bc);
            afterMap.TryGetValue(tpName, out var ac);

            var beforeConstraints = bc?.Constraints ?? Array.Empty<string>();
            var afterConstraints  = ac?.Constraints ?? Array.Empty<string>();

            var removed = beforeConstraints.Except(afterConstraints, StringComparer.Ordinal).ToList();
            var added   = afterConstraints.Except(beforeConstraints, StringComparer.Ordinal).ToList();

            if (removed.Count == 0 && added.Count == 0)
                continue;

            string desc = $"Generic constraints changed for type parameter '{tpName}'.";
            var detail = new ConstraintChangeDetail(desc, tpName, removed, added);

            yield return new SymbolChange(
                SymbolId:               b.Id,
                BeforeSnapshotSymbolId: b.Id,
                AfterSnapshotSymbolId:  a.Id,
                ParentSymbolId:         parentId,
                ChangeType:             ChangeType.Modified,
                Category:               ChangeCategory.Constraint,
                Severity:               DetermineRemovedOrModifiedSeverity(b.Accessibility, ChangeCategory.Constraint),
                Description:            desc,
                SignatureDetail:        null,
                NullabilityDetail:      null,
                ConstraintDetail:       detail,
                AccessibilityDetail:    null,
                DependencyDetail:       null,
                DocCommentDetail:       null);
        }
    }

    private static IEnumerable<SymbolChange> CompareAccessibility(
        SymbolNode b, SymbolNode a, SymbolId? parentId)
    {
        if (b.Accessibility == a.Accessibility)
            yield break;

        string desc = $"Accessibility changed from '{b.Accessibility}' to '{a.Accessibility}'.";
        var detail  = new AccessibilityChangeDetail(desc, b.Accessibility, a.Accessibility);

        yield return new SymbolChange(
            SymbolId:               b.Id,
            BeforeSnapshotSymbolId: b.Id,
            AfterSnapshotSymbolId:  a.Id,
            ParentSymbolId:         parentId,
            ChangeType:             ChangeType.Modified,
            Category:               ChangeCategory.Accessibility,
            Severity:               DetermineRemovedOrModifiedSeverity(b.Accessibility, ChangeCategory.Accessibility),
            Description:            desc,
            SignatureDetail:        null,
            NullabilityDetail:      null,
            ConstraintDetail:       null,
            AccessibilityDetail:    detail,
            DependencyDetail:       null,
            DocCommentDetail:       null);
    }

    private static IEnumerable<SymbolChange> CompareDependencies(
        SymbolNode b, SymbolNode a, SymbolId? parentId,
        Dictionary<string, List<SymbolEdge>> beforeEdges,
        Dictionary<string, List<SymbolEdge>> afterEdges)
    {
        var bEdges = beforeEdges.TryGetValue(b.Id.Value, out var bl) ? bl : new List<SymbolEdge>();
        var aEdges = afterEdges.TryGetValue(a.Id.Value,  out var al) ? al : new List<SymbolEdge>();

        // Group by (From, To) pair
        var beforeByPair = bEdges.ToDictionary(e => (e.From.Value, e.To.Value));
        var afterByPair  = aEdges.ToDictionary(e => (e.From.Value, e.To.Value));

        var allPairs = beforeByPair.Keys.Union(afterByPair.Keys).ToHashSet();

        var removedEdges = new List<SymbolEdge>();
        var addedEdges   = new List<SymbolEdge>();

        foreach (var pair in allPairs)
        {
            bool inBefore = beforeByPair.TryGetValue(pair, out var be);
            bool inAfter  = afterByPair.TryGetValue(pair, out var ae);

            if (inBefore && inAfter)
            {
                // Same pair — if kind changed, report as modified (remove old, add new)
                if (be!.Kind != ae!.Kind)
                {
                    removedEdges.Add(be);
                    addedEdges.Add(ae);
                }
                // Else no change
            }
            else if (inBefore)
            {
                removedEdges.Add(be!);
            }
            else
            {
                addedEdges.Add(ae!);
            }
        }

        if (removedEdges.Count == 0 && addedEdges.Count == 0)
            yield break;

        string desc = $"Dependency edges changed: {removedEdges.Count} removed, {addedEdges.Count} added.";
        var detail  = new DependencyChangeDetail(desc, removedEdges, addedEdges);

        yield return new SymbolChange(
            SymbolId:               b.Id,
            BeforeSnapshotSymbolId: b.Id,
            AfterSnapshotSymbolId:  a.Id,
            ParentSymbolId:         parentId,
            ChangeType:             ChangeType.Modified,
            Category:               ChangeCategory.Dependency,
            Severity:               DetermineRemovedOrModifiedSeverity(b.Accessibility, ChangeCategory.Dependency),
            Description:            desc,
            SignatureDetail:        null,
            NullabilityDetail:      null,
            ConstraintDetail:       null,
            AccessibilityDetail:    null,
            DependencyDetail:       detail,
            DocCommentDetail:       null);
    }

    private static IEnumerable<SymbolChange> CompareDocComments(
        SymbolNode b, SymbolNode a, SymbolId? parentId)
    {
        if (b.Docs is null && a.Docs is null)
            yield break;

        if (DocsEqual(b.Docs, a.Docs))
            yield break;

        string desc = "Documentation comment changed.";
        var detail  = new DocCommentChangeDetail(desc, b.Docs, a.Docs);

        yield return new SymbolChange(
            SymbolId:               b.Id,
            BeforeSnapshotSymbolId: b.Id,
            AfterSnapshotSymbolId:  a.Id,
            ParentSymbolId:         parentId,
            ChangeType:             ChangeType.Modified,
            Category:               ChangeCategory.DocComment,
            Severity:               ChangeSeverity.Informational,
            Description:            desc,
            SignatureDetail:        null,
            NullabilityDetail:      null,
            ConstraintDetail:       null,
            AccessibilityDetail:    null,
            DependencyDetail:       null,
            DocCommentDetail:       detail);
    }

    // ── Utility helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the two type strings differ only by the presence/absence
    /// of a trailing '?' nullable annotation.
    /// </summary>
    private static bool IsOnlyNullabilityDiff(string? before, string? after)
    {
        if (before is null || after is null)
            return false;

        // Strip trailing '?' from both
        var stripped = before.TrimEnd('?');
        var strippedAfter = after.TrimEnd('?');

        // They must be the same base type, but differ in annotation
        return stripped == strippedAfter && before != after;
    }

    private static bool DocsEqual(DocComment? a, DocComment? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        if (a.Summary != b.Summary) return false;
        if (a.Remarks != b.Remarks) return false;
        if (a.Returns != b.Returns) return false;

        if (!DictEqual(a.Params, b.Params))     return false;
        if (!DictEqual(a.TypeParams, b.TypeParams)) return false;

        if (a.Examples.Count != b.Examples.Count) return false;
        for (int i = 0; i < a.Examples.Count; i++)
            if (a.Examples[i] != b.Examples[i]) return false;

        if (a.Exceptions.Count != b.Exceptions.Count) return false;
        for (int i = 0; i < a.Exceptions.Count; i++)
            if (a.Exceptions[i] != b.Exceptions[i]) return false;

        if (a.SeeAlso.Count != b.SeeAlso.Count) return false;
        for (int i = 0; i < a.SeeAlso.Count; i++)
            if (a.SeeAlso[i] != b.SeeAlso[i]) return false;

        return true;
    }

    private static bool DictEqual(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var bv) || v != bv)
                return false;
        }
        return true;
    }

    private static string BuildParameterChangeSummary(List<ParameterChange> changes)
    {
        if (changes.Count == 0) return "Signature changed.";
        return $"Parameter signature changed: {changes.Count} parameter(s) modified.";
    }

    private static DiffSummary BuildSummary(List<SymbolChange> changes)
    {
        int added        = 0, removed = 0, modified = 0;
        int breaking     = 0, nonBreaking = 0, informational = 0;

        foreach (var c in changes)
        {
            switch (c.ChangeType)
            {
                case ChangeType.Added:    added++;    break;
                case ChangeType.Removed:  removed++;  break;
                case ChangeType.Modified: modified++; break;
            }
            switch (c.Severity)
            {
                case ChangeSeverity.Breaking:      breaking++;      break;
                case ChangeSeverity.NonBreaking:   nonBreaking++;   break;
                case ChangeSeverity.Informational: informational++; break;
            }
        }

        return new DiffSummary(
            TotalChanges:  changes.Count,
            Added:         added,
            Removed:       removed,
            Modified:      modified,
            Breaking:      breaking,
            NonBreaking:   nonBreaking,
            Informational: informational);
    }
}
