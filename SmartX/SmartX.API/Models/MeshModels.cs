namespace SmartX.API.Models;

public record MeshLink(string A, string B, double Cost);

public record MeshNodeInfo(
    string Id,
    string Name,
    string Location,
    string Category,
    bool IsGateway,
    bool IsUp,
    bool ForcedDown);

public record MeshLinkInfo(string A, string B, double Cost, bool Active, bool InMst);

public record TraversalStep(string Id, string Name, int Hops);

public record MeshRoute(
    string Id,
    string Name,
    bool Reachable,
    List<string> Path,
    List<string> PathNames,
    int Hops,
    double TotalCost,
    string Note);

public record Reroute(
    string Id,
    string Name,
    List<string> OldPath,
    List<string> NewPath,
    double OldCost,
    double NewCost,
    bool Isolated);

public record MeshSnapshot(
    List<MeshNodeInfo> Nodes,
    List<MeshLinkInfo> Links,
    double MstTotalCost,
    int ReachableCount,
    List<TraversalStep> Bfs,
    List<string> DownNodes,
    List<string> IsolatedNodes,
    List<Reroute> Reroutes);