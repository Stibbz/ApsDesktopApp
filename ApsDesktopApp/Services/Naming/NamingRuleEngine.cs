using System.Collections.Generic;
using System.Linq;

namespace ApsDesktopApp.Services.Naming;

// Runs the registered naming rules over a set of file names and collects every
// violation. Rules are independent and order-insensitive; a single file can
// trip several rules. New conventions are added by registering more INamingRule
// implementations -- no change here.
public class NamingRuleEngine
{
    private readonly IReadOnlyList<INamingRule> _rules;

    public NamingRuleEngine(IEnumerable<INamingRule> rules)
    {
        _rules = rules.ToList();
    }

    public IReadOnlyList<NamingViolation> Check(IEnumerable<string> fileNames)
    {
        var violations = new List<NamingViolation>();
        foreach (var fileName in fileNames)
        {
            foreach (var rule in _rules)
            {
                var violation = rule.Check(fileName);
                if (violation is not null)
                    violations.Add(violation);
            }
        }
        return violations;
    }
}
