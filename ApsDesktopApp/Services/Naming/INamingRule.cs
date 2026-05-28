namespace ApsDesktopApp.Services.Naming;

// A single naming-convention violation found on a file.
public record NamingViolation(string FileName, string RuleName, string Message);

// A pluggable naming-convention rule. Implementations inspect a file name and
// report a violation (or null if it conforms). Kept WPF-free so rules can run
// in a future headless/web variant.
public interface INamingRule
{
    // Short display name for the rule (shown in the violations report).
    string Name { get; }

    // Returns null when fileName conforms; otherwise a NamingViolation
    // describing what is wrong. fileName is the file's display name including
    // extension (e.g. "ACME-AR-L01-FloorPlan-001.rvt").
    NamingViolation? Check(string fileName);
}
