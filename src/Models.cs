namespace TimeAgent;

public record WorkItem
{
    public long Id { get; init; }
    public string Name { get; set; } = "";
    public string EntityType { get; init; } = "";   // "Tasks" | "Bugs"
    public string DisplayType { get; init; } = "";   // "Task" | "Bug"
    public long StateId { get; set; }
    public string StateName { get; set; } = "?";
    public bool IsFinal { get; set; }
    public string ProjectName { get; init; } = "";
    public long ProcessId { get; init; }
    public string Sprint { get; init; } = "";
    public long UsId { get; init; }
    public string UsName { get; init; } = "";
}

public record TimeEntry
{
    public long Id { get; init; }
    public long ItemId { get; init; }
    public double Hours { get; init; }
    public string Day { get; init; } = "";          // YYYY-MM-DD (offset-aware)
    public string Description { get; init; } = "";
}

public record WorkflowState
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public bool IsFinal { get; init; }
    public double Priority { get; init; }
}

public class DynamicMeeting
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public long TaskId { get; set; }
    public string Description { get; set; } = "";
}

public class RecurringEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Label { get; set; } = "";
    public long TaskId { get; set; }
    public double Hours { get; set; } = 1;
}
