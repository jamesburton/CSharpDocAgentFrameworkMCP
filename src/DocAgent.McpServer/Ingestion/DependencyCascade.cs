using DocAgent.Core;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Provides dependency cascade utilities for incremental solution ingestion:
/// topological sort, dirty set computation, structural change detection, and cycle detection.
/// </summary>
internal static class DependencyCascade
{
    /// <summary>
    /// Returns project names in dependency order (leaves first) using Kahn's algorithm.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if a cycle is detected.</exception>
    public static IReadOnlyList<string> TopologicalSort(
        IReadOnlyList<ProjectEntry> projects,
        IReadOnlyList<ProjectEdge> edges)
    {
        // Build adjacency list and in-degree map
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            adjacency.TryAdd(project.Name, []);
            inDegree.TryAdd(project.Name, 0);
        }

        foreach (var edge in edges)
        {
            // edge.From depends on edge.To, so edge.To must come first.
            // In topological sort terms: To -> From (To is a prerequisite of From).
            if (!adjacency.TryGetValue(edge.To, out var neighbors))
            {
                neighbors = [];
                adjacency[edge.To] = neighbors;
            }
            neighbors.Add(edge.From);

            inDegree.TryAdd(edge.From, 0);
            inDegree.TryAdd(edge.To, 0);
            inDegree[edge.From]++;
        }

        // Kahn's algorithm
        var queue = new Queue<string>();
        foreach (var (name, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(name);
        }

        var result = new List<string>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);

            if (adjacency.TryGetValue(current, out var dependents))
            {
                foreach (var dep in dependents)
                {
                    inDegree[dep]--;
                    if (inDegree[dep] == 0)
                        queue.Enqueue(dep);
                }
            }
        }

        if (result.Count != inDegree.Count)
            throw new InvalidOperationException(
                "Cycle detected in project dependency graph. Run DetectCycles for details.");

        return result;
    }

    /// <summary>
    /// BFS propagation from directly-changed projects through their dependents.
    /// Returns the full set of projects that need re-ingestion.
    /// </summary>
    public static HashSet<string> ComputeDirtySet(
        IReadOnlyList<string> directlyChanged,
        IReadOnlyList<ProjectEdge> edges)
    {
        // Build reverse adjacency: for each dependency target, who depends on it?
        // edge.From depends on edge.To → if edge.To changes, edge.From is dirty.
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            if (!dependents.TryGetValue(edge.To, out var list))
            {
                list = [];
                dependents[edge.To] = list;
            }
            list.Add(edge.From);
        }

        var dirty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        foreach (var changed in directlyChanged)
        {
            if (dirty.Add(changed))
                queue.Enqueue(changed);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (dependents.TryGetValue(current, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (dirty.Add(dep))
                        queue.Enqueue(dep);
                }
            }
        }

        return dirty;
    }

    /// <summary>
    /// Returns true if the project set has changed (added or removed projects).
    /// </summary>
    public static bool HasStructuralChange(
        IReadOnlyList<ProjectEntry>? previousProjects,
        IReadOnlyList<string> currentProjectPaths)
    {
        if (previousProjects is null)
            return true;

        var previousPaths = new HashSet<string>(
            previousProjects.Select(p => p.Path),
            StringComparer.OrdinalIgnoreCase);

        if (previousPaths.Count != currentProjectPaths.Count)
            return true;

        foreach (var path in currentProjectPaths)
        {
            if (!previousPaths.Contains(path))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Detects cycles in the project dependency graph using DFS.
    /// Returns a list of cycles, where each cycle is a list of project names forming the loop.
    /// </summary>
    internal static List<List<string>> DetectCycles(IReadOnlyList<ProjectEdge> edges)
    {
        // Build adjacency list
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            if (!graph.TryGetValue(edge.From, out var neighbors))
            {
                neighbors = [];
                graph[edge.From] = neighbors;
            }
            neighbors.Add(edge.To);
        }

        var cycles = new List<List<string>>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var node in graph.Keys)
        {
            if (!visited.Contains(node))
                DfsCycleDetect(node, graph, visited, inStack, path, cycles);
        }

        return cycles;
    }

    private static void DfsCycleDetect(
        string node,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> inStack,
        List<string> path,
        List<List<string>> cycles)
    {
        visited.Add(node);
        inStack.Add(node);
        path.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    DfsCycleDetect(neighbor, graph, visited, inStack, path, cycles);
                }
                else if (inStack.Contains(neighbor))
                {
                    var cycleStart = path.IndexOf(neighbor);
                    if (cycleStart >= 0)
                    {
                        var cycle = new List<string>(path.Skip(cycleStart));
                        cycle.Add(neighbor);
                        cycles.Add(cycle);
                    }
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        inStack.Remove(node);
    }
}
