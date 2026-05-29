using System;
using ApsDesktopApp.Models;

namespace ApsDesktopApp.ViewModels;

// Immutable row shown in the Issues DataGrid. Names (assignedTo/owner/createdBy)
// are resolved from user IDs at construction time via the nameOf delegate.
public class IssueRow
{
    public string  Id          { get; private init; } = string.Empty;
    public int     DisplayId   { get; private init; }
    public string  Title       { get; private init; } = string.Empty;
    public string  Status      { get; private init; } = string.Empty;
    public string  Type        { get; private init; } = string.Empty;
    public string  AssignedTo  { get; private init; } = string.Empty;
    public string  CreatedBy   { get; private init; } = string.Empty;
    public string  Owner       { get; private init; } = string.Empty;
    public string  CreatedAt   { get; private init; } = string.Empty;
    public string  DueDate     { get; private init; } = string.Empty;
    public string  ClosedAt    { get; private init; } = string.Empty;
    public string? Description { get; private init; }

    public static IssueRow FromApi(AccIssue issue, Func<string?, string> nameOf) => new()
    {
        Id         = issue.Id,
        DisplayId  = issue.DisplayId,
        Title      = issue.Title      ?? string.Empty,
        Status     = issue.Status     ?? string.Empty,
        Type       = issue.IssueType?.Title ?? string.Empty,
        AssignedTo = nameOf(issue.AssignedTo),
        CreatedBy  = nameOf(issue.CreatedBy),
        Owner      = nameOf(issue.OwnerId),
        CreatedAt  = issue.CreatedAt.HasValue
                       ? issue.CreatedAt.Value.ToLocalTime().ToString("yyyy-MM-dd")
                       : string.Empty,
        DueDate    = issue.DueDate    ?? string.Empty,
        ClosedAt   = issue.ClosedAt.HasValue
                       ? issue.ClosedAt.Value.ToLocalTime().ToString("yyyy-MM-dd")
                       : string.Empty,
        Description = issue.Description,
    };
}
