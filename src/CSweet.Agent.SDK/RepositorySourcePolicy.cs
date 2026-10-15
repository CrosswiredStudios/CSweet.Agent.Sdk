namespace CSweet.Agent.SDK;

/// <summary>Source delivery excludes installed dependencies and generated build artifacts.</summary>
public static class RepositorySourcePolicy
{
    public const string Instructions = """
Maintain an appropriate root .gitignore and nested rules before installing dependencies or building.
Keep dependency manifests and lockfiles, authored source, tests, configuration and build recipes in source control.
Exclude installed libraries, package caches, build output and coverage (for example node_modules/, .venv/,
dist/, bin/, obj/, .next/ and coverage/). Do not copy third-party library source into the deliverable.
The workspace is a snapshot without Git metadata: .gitignore does not remove files already tracked.
Remove previously committed dependency/build files from the source snapshot and validate the resulting change.
Build deployment artifacts from pinned dependencies in the build recipe (for example a multi-stage Dockerfile);
do not commit a compiled bundle merely so a runtime-only Dockerfile can copy it. Keep authored assets and lockfiles.
Hand reviewers the published PR/branch reference and exact commit, with actual validation evidence, rather than
copying repository contents or generated library code into messages.
""";

    public static bool IsGeneratedOrDependencyPath(string path) => path.Replace('\\', '/').Split('/').Any(part =>
        part.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
        part.Equals(".venv", StringComparison.OrdinalIgnoreCase) || part.Equals("venv", StringComparison.OrdinalIgnoreCase) ||
        part.Equals("__pycache__", StringComparison.OrdinalIgnoreCase) || part.Equals("dist", StringComparison.OrdinalIgnoreCase) ||
        part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
        part.Equals(".next", StringComparison.OrdinalIgnoreCase) || part.Equals(".nuxt", StringComparison.OrdinalIgnoreCase) ||
        part.Equals("coverage", StringComparison.OrdinalIgnoreCase));
}
