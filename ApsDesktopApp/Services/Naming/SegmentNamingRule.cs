using System.IO;
using System.Text.RegularExpressions;

namespace ApsDesktopApp.Services.Naming;

// Validates a delimited, fixed-field file name (ISO 19650 style), e.g.
// "ACME-XYZ-XX-00-DR-A-0001.rvt". The split/count/per-field checks are generic;
// the convention itself is defined by the Fields table below.
public class SegmentNamingRule : INamingRule
{
    public string Name => "Segment naming convention";

    // Delimiter between fields. Change if your standard uses '_' etc.
    private const char Delimiter = '-';

    // TODO(you): define YOUR convention's fields, in order. Each entry is a
    // (Label, Pattern) pair: Label is shown in violation messages; Pattern is a
    // regex the whole segment must match. The values below are PLACEHOLDERS from
    // a generic ISO 19650 example -- replace them with your real fields, counts,
    // allowed codes, and lengths. The engine checks the segment count against
    // Fields.Length and each segment against its Pattern (see Check).
    private static readonly (string Label, string Pattern)[] Fields =
    {
        ("Project",    "^[A-Z]{3,4}$"),
        ("Originator", "^[A-Z]{2,6}$"),
        ("Volume",     "^[A-Z0-9]{2}$"),
        ("Level",      "^[0-9]{2}$"),
        ("Type",       "^[A-Z]{2}$"),
        ("Role",       "^[A-Z]$"),
        ("Number",     "^[0-9]{4}$"),
    };

    public NamingViolation? Check(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var segments = stem.Split(Delimiter);

        if (segments.Length != Fields.Length)
            return new NamingViolation(fileName, Name,
                $"Expected {Fields.Length} '{Delimiter}'-separated fields, found {segments.Length}.");

        for (var i = 0; i < Fields.Length; i++)
        {
            if (!Regex.IsMatch(segments[i], Fields[i].Pattern))
                return new NamingViolation(fileName, Name,
                    $"Field {i + 1} ({Fields[i].Label}) '{segments[i]}' "
                    + $"does not match {Fields[i].Pattern}.");
        }
        return null;
    }
}
