namespace DocAgent.McpServer.Cli;

internal static class SkillContent
{
    public static readonly string SetupProject = """
        ---
        name: docagent-setup-project
        description: Set up DocAgent for the current project
        type: project-setup
        ---
        # DocAgent Project Setup
        Guide the user through setting up DocAgent for the current project.
        ## Steps
        1. Check for docagent.project.json in cwd. If present: [Reconfigure] [Re-ingest only] [Cancel]. If absent: proceed.
        2. Find .sln/.csproj files nearby. Present as choices. Map to --primary <path>.
        3. Find secondary sources (other .sln, TypeScript dirs with package.json). Multi-select. Map to --secondary flags.
        4. Ask: Ingest now? [Yes / No] → --ingest if Yes.
        5. Ask: Enable git hooks? [Yes / No / Tell me more] → --no-hooks if No.
        6. Run: docagent init --non-interactive --primary <x> [--secondary ...] [--ingest] [--no-hooks] --yes
        7. Stream ingest output if selected.
        8. Report: files written, symbol count, snapshot hash, reminder about /docagent:update.
        """;

    public static readonly string Update = """
        ---
        name: docagent-update
        description: Re-ingest the current project's code into DocAgent
        type: update
        ---
        # DocAgent Update
        1. Check docagent.project.json exists. If missing: offer to run setup.
        2. Run: docagent update
        3. Parse JSON output: {status, projectsIngested, symbolCount, durationMs, snapshotHash}
        4. Report: "DocAgent updated: N projects, M symbols in Xs (snapshot hash)."
        5. On error: surface stderr and suggest running docagent update in terminal.
        """;
}
