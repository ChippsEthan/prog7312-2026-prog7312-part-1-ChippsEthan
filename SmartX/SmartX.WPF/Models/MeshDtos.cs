namespace SmartX.WPF.Models;

public class MeshNodeDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsGateway { get; set; }
    public bool IsUp { get; set; }
    public bool ForcedDown { get; set; }
}

public class MeshLinkDto
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public double Cost { get; set; }
    public bool Active { get; set; }
    public bool InMst { get; set; }
}

public class TraversalStepDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Hops { get; set; }
}

public class RerouteDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> OldPath { get; set; } = new();
    public List<string> NewPath { get; set; } = new();
    public double OldCost { get; set; }
    public double NewCost { get; set; }
    public bool Isolated { get; set; }
}

public class MeshSnapshotDto
{
    public List<MeshNodeDto> Nodes { get; set; } = new();
    public List<MeshLinkDto> Links { get; set; } = new();
    public double MstTotalCost { get; set; }
    public int ReachableCount { get; set; }
    public List<TraversalStepDto> Bfs { get; set; } = new();
    public List<string> DownNodes { get; set; } = new();
    public List<string> IsolatedNodes { get; set; } = new();
    public List<RerouteDto> Reroutes { get; set; } = new();
}

public class MeshRouteDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Reachable { get; set; }
    public List<string> Path { get; set; } = new();
    public List<string> PathNames { get; set; } = new();
    public int Hops { get; set; }
    public double TotalCost { get; set; }
    public string Note { get; set; } = "";
}

public class TraverseResultDto
{
    public string Mode { get; set; } = "";
    public int Visited { get; set; }
    public List<TraversalStepDto> Steps { get; set; } = new();
}

public class TimeRangeDto
{
    public string Mac { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public int Count { get; set; }
    public int NodesVisited { get; set; }
    public int TreeSize { get; set; }
    public int TreeHeight { get; set; }
    public List<ReadingDto> Readings { get; set; } = new();
}