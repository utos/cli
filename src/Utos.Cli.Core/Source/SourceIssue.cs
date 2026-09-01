namespace Utos.Cli.Core.Source;

/// <summary>
/// A problem found while reading or resolving authored source, as opposed to a rule violation in
/// a built bundle. Codes are the <c>UTOS-S###</c> range of
/// <c>api/docs/workflow-source-format.md</c>.
/// </summary>
/// <param name="Code">The stable rule identifier, e.g. <c>UTOS-S007</c>.</param>
/// <param name="Message">A human-readable explanation. Not contractual.</param>
/// <param name="File">The file the problem is in.</param>
/// <param name="Line">1-based line, or 0 when the problem is not tied to one.</param>
/// <param name="Column">1-based column, or 0.</param>
public sealed record SourceIssue(string Code, string Message, string File, int Line = 0, int Column = 0)
{
    /// <summary>Renders as <c>file:line:col: CODE message</c>, the form editors can jump to.</summary>
    public override string ToString()
    {
        var location = File;
        if (Line > 0) location += ":" + Line;
        if (Line > 0 && Column > 0) location += ":" + Column;

        return location + ": " + Code + " " + Message;
    }
}

/// <summary>The <c>UTOS-S###</c> codes this CLI reports.</summary>
public static class SourceCodes
{
    /// <summary>A dependency alias is empty, or declared twice.</summary>
    public const string DependencyAliasInvalid = "UTOS-S001";

    /// <summary>A dependency reference is malformed.</summary>
    public const string DependencyReferenceInvalid = "UTOS-S002";

    /// <summary>A local dependency file does not exist or cannot be read.</summary>
    public const string DependencyFileUnreadable = "UTOS-S003";

    /// <summary>An activity names an alias not declared in <c>spec.dependencies</c>.</summary>
    public const string DependencyAliasUnknown = "UTOS-S004";

    /// <summary>The dependency graph contains a cycle.</summary>
    public const string DependencyCycle = "UTOS-S005";

    /// <summary>Two documents resolve to the same canonical identity with differing content.</summary>
    public const string IdentityCollision = "UTOS-S006";

    /// <summary>An activity's <c>type</c> is missing, or is not a known activity kind.</summary>
    public const string ActivityTypeInvalid = "UTOS-S007";

    /// <summary>A mapping contains duplicate keys.</summary>
    public const string DuplicateKey = "UTOS-S008";

    /// <summary>The document is not well-formed, or does not match the workflow schema.</summary>
    public const string DocumentMalformed = "UTOS-S009";

    /// <summary>A registry dependency was requested; resolution is not implemented yet.</summary>
    public const string RegistryUnsupported = "UTOS-S010";

    /// <summary><c>self</c> is used anywhere other than a promise branch's <c>workflow</c>.</summary>
    public const string SelfNotAllowedHere = "UTOS-S011";
}

/// <summary>Thrown when authored source cannot be turned into a workflow.</summary>
public sealed class WorkflowSourceException : Exception
{
    /// <summary>Creates an exception carrying every issue found.</summary>
    public WorkflowSourceException(IReadOnlyList<SourceIssue> issues)
        : base(issues.Count == 1 ? issues[0].ToString() : $"{issues.Count} problems in workflow source")
    {
        Issues = issues;
    }

    /// <summary>Creates an exception carrying a single issue.</summary>
    public WorkflowSourceException(SourceIssue issue) : this(new[] { issue })
    {
    }

    /// <summary>Every issue found.</summary>
    public IReadOnlyList<SourceIssue> Issues { get; }
}
