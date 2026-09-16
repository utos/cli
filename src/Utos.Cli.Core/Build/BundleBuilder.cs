using Utos.Cli.Core.Source;
using Utos.Workflows.V1;

namespace Utos.Cli.Core.Build;

/// <summary>One node of the resolved dependency graph, for <c>utos inspect</c>.</summary>
/// <param name="Alias">The alias the parent bound this workflow to, or null for the entry point.</param>
/// <param name="Identity">The canonical identity this resolved to.</param>
/// <param name="File">The file it was read from.</param>
/// <param name="Dependencies">Its own resolved dependencies.</param>
public sealed record DependencyNode(
    string? Alias,
    string Identity,
    string File,
    IReadOnlyList<DependencyNode> Dependencies);

/// <summary>The result of resolving an authored workflow into an executable bundle.</summary>
/// <param name="Bundle">The fully-resolved bundle.</param>
/// <param name="Tree">The dependency graph that produced it.</param>
public sealed record BuildResult(WorkflowBundle Bundle, DependencyNode Tree);

/// <summary>
/// Turns an authored workflow and its dependencies into a <see cref="WorkflowBundle"/>.
/// <para>
/// This is a pipeline stage, not a command: a bundle is a wire payload rather than a distributable
/// artifact, so nothing writes one to disk. Both <c>utos validate</c> and <c>utos load</c> drive it.
/// </para>
/// </summary>
public static class BundleBuilder
{
    /// <summary>Resolves <paramref name="entryPath"/> and everything it depends on.</summary>
    /// <exception cref="WorkflowSourceException">The source cannot be resolved into a bundle.</exception>
    public static BuildResult Build(string entryPath)
    {
        var builder = new Builder();
        var tree = builder.Resolve(Path.GetFullPath(entryPath), alias: null);

        if (builder.Issues.Count > 0) throw new WorkflowSourceException(builder.Issues);

        var bundle = new WorkflowBundle { EntryPoint = tree.Identity };
        foreach (var (identity, workflow) in builder.Workflows) bundle.Workflows[identity] = workflow;

        return new BuildResult(bundle, tree);
    }

    private sealed class Builder
    {
        // Paths are compared case-insensitively so a workflow reached by two spellings of the same
        // file on Windows resolves once rather than colliding with itself.
        private readonly Dictionary<string, DependencyNode> _byPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _resolving = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Workflow> Workflows { get; } = new(StringComparer.Ordinal);

        public List<SourceIssue> Issues { get; } = [];

        public DependencyNode Resolve(string path, string? alias)
        {
            // Memoised before the cycle check, so a diamond resolves once while a true cycle is
            // still caught: the repeat visit that closes a loop has not been memoised yet.
            if (_byPath.TryGetValue(path, out var known))
                return known with { Alias = alias };

            if (!_resolving.Add(path))
            {
                throw new WorkflowSourceException(new SourceIssue(
                    SourceCodes.DependencyCycle,
                    $"Dependency cycle: '{path}' depends on itself, directly or indirectly.", path));
            }

            var workflow = WorkflowLoader.LoadFile(path);
            var directory = Path.GetDirectoryName(path) ?? ".";

            var children = new List<DependencyNode>();
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

            if (workflow.Spec is not null)
            {
                // Sorted so the resolution order — and any resulting error order — is reproducible.
                foreach (var dependencyAlias in workflow.Spec.Dependencies.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    var reference = workflow.Spec.Dependencies[dependencyAlias];
                    var child = ResolveDependency(dependencyAlias, reference, directory, path);
                    if (child is null) continue;

                    children.Add(child);
                    aliases[dependencyAlias] = child.Identity;
                }
            }

            // The identity is formatted from metadata verbatim rather than strictly derived, so a
            // malformed name surfaces as its own validation issue instead of failing the build here
            // with something less specific.
            var identity = WorkflowIdentity.FormatMetadata(workflow.Metadata);
            var built = Flatten(workflow, aliases, path);

            _resolving.Remove(path);

            if (Workflows.TryGetValue(identity, out var existing) && !existing.Equals(built))
            {
                Issues.Add(new SourceIssue(SourceCodes.IdentityCollision,
                    $"Two different workflows both claim the identity '{identity}'.", path));
            }

            Workflows[identity] = built;

            var node = new DependencyNode(alias, identity, path, children);
            _byPath[path] = node;
            return node;
        }

        private DependencyNode? ResolveDependency(string alias, string reference, string directory,
            string declaringFile)
        {
            if (alias.Length == 0 || string.IsNullOrWhiteSpace(reference))
            {
                Issues.Add(new SourceIssue(SourceCodes.DependencyAliasInvalid,
                    $"Dependency '{alias}' must bind a non-empty alias to a non-empty reference.",
                    declaringFile));
                return null;
            }

            if (!IsLocalFile(reference))
            {
                // Registry resolution is the CLI's job, but it needs the OCI work in DESIGN.md §10.
                //
                // Deliberately carries no UTOS-S code. The document is correct — a registry
                // reference is exactly what the source format says one looks like — and the
                // problem is that this tool has not built the feature yet. A conforming
                // implementation that had would never report it, so a code would burn a slot in a
                // shared range for something the specification never said.
                Issues.Add(new SourceIssue(string.Empty,
                    $"Dependency '{alias}' points at the registry reference '{reference}'. "
                    + "Registry resolution is not implemented yet; use a './' or '../' file reference.",
                    declaringFile));
                return null;
            }

            var resolved = Path.GetFullPath(Path.Combine(directory, reference));
            if (!File.Exists(resolved))
            {
                Issues.Add(new SourceIssue(SourceCodes.DependencyFileUnreadable,
                    $"Dependency '{alias}' points at '{reference}', which does not exist "
                    + $"(looked in {resolved}).", declaringFile));
                return null;
            }

            return Resolve(resolved, alias);
        }

        /// <summary>
        /// Produces the built form of a workflow: sub-workflow activities name canonical identities
        /// instead of aliases, and the dependency map is emptied. Leaving it populated would make
        /// two builds of the same logical workflow hash differently (rule <c>UTOS-B007</c>).
        /// </summary>
        private Workflow Flatten(Workflow workflow,
            IReadOnlyDictionary<string, string> aliases, string file)
        {
            var built = workflow.Clone();
            if (built.Spec is null) return built;

            built.Spec.Dependencies.Clear();

            var ownIdentity = WorkflowIdentity.FormatMetadata(built.Metadata);

            foreach (var (name, activity) in built.Spec.Activities)
            {
                switch (activity.ConfigCase)
                {
                    case WorkflowActivity.ConfigOneofCase.Workflow:
                        activity.Workflow.Workflow = Rewrite(
                            activity.Workflow.Workflow,
                            $"Activity '{name}'", selfAllowed: false);

                        if (activity.Workflow.ModeCase == WorkflowActivityConfig.ModeOneofCase.Call)
                            for (var i = 0; i < activity.Workflow.Call.OnEmitted.Count; i++)
                            {
                                var rule = activity.Workflow.Call.OnEmitted[i];

                                // Only a handle names a document. A transition names an activity
                                // in this workflow and a result names nothing, so neither is an
                                // alias site — and neither may be rewritten, since the value they
                                // carry is not a dependency reference at all.
                                if (rule.ActionCase != EmissionRule.ActionOneofCase.Handle) continue;

                                rule.Handle.Workflow = Rewrite(
                                    rule.Handle.Workflow,
                                    $"The onEmitted handler at index {i} of activity '{name}'",
                                    selfAllowed: false);
                            }

                        break;

                    case WorkflowActivity.ConfigOneofCase.Promise:
                        foreach (var branch in activity.Promise.Branches)
                            branch.Workflow = Rewrite(
                                branch.Workflow,
                                $"Branch '{branch.Name}' of activity '{name}'",
                                selfAllowed: true);

                        break;
                }
            }

            return built;

            // `self` is resolved exactly like an alias — to this document's own canonical identity
            // — which is what keeps it out of the dependency graph and so out of the cycle check
            // (UTOS-S005). The daemon never sees the word.
            string Rewrite(string reference, string subject, bool selfAllowed)
            {
                if (reference == SelfReference)
                {
                    if (selfAllowed) return ownIdentity;

                    // UTOS-S011. Only a promise branch may say `self`, because only a promise
                    // branch has no other way to say it: recursive fan-out through an alias is a
                    // cycle. Everywhere else it merely saves a document — and on an onEmitted rule
                    // it puts the handler back in the consumer's own graph, where a transition can
                    // reach the call activity that dispatched it and start a second producer per
                    // value, without bound.
                    Issues.Add(new SourceIssue(SourceCodes.SelfNotAllowedHere,
                        $"{subject} names 'self', which is reserved for promise branches. "
                        + "Declare the document in spec.dependencies and name the alias.", file));

                    return reference;
                }

                if (aliases.TryGetValue(reference, out var identity)) return identity;

                if (reference.Length > 0)
                    Issues.Add(new SourceIssue(SourceCodes.DependencyAliasUnknown,
                        $"{subject} references dependency '{reference}', which is not declared "
                        + "in spec.dependencies.", file));

                return reference;
            }
        }

        /// <summary>What an author writes to name the document a reference is written in.</summary>
        private const string SelfReference = "self";

        private static bool IsLocalFile(string reference) =>
            reference.StartsWith("./", StringComparison.Ordinal)
            || reference.StartsWith("../", StringComparison.Ordinal)
            || reference.StartsWith(".\\", StringComparison.Ordinal)
            || reference.StartsWith("..\\", StringComparison.Ordinal);
    }
}
