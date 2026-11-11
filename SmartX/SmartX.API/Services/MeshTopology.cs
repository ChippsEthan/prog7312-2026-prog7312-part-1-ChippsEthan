using SmartX.API.Models;

namespace SmartX.API.Services;

public class MeshTopology
{
    public const string GatewayId = "GATEWAY";

    private sealed record MstState(
        Dictionary<string, string> Parent,
        List<MeshLink> Edges,
        double Total,
        HashSet<string> InTree);

    private sealed record MeshContext(
        Dictionary<string, Sensor> ByMac,
        Func<string, bool> IsUp,
        Func<string, string> NameOf);

    private readonly DeviceRegistry _registry;
    private readonly Dictionary<string, List<MeshLink>> _adj = new();
    private readonly List<MeshLink> _links = new();
    private readonly HashSet<string> _forcedDown = new();
    private readonly object _lock = new();

    public MeshTopology(DeviceRegistry registry)
    {
        _registry = registry;
        _adj[GatewayId] = new List<MeshLink>();
    }


    public void SeedDefaults()
    {
        lock (_lock)
        {
            const string P = "24:6F:28:";

            AddLink(GatewayId, P + "A1:00:01", 4);
            AddLink(GatewayId, P + "B2:00:01", 5);
            AddLink(GatewayId, P + "C3:00:02", 7);

            AddLink(P + "A1:00:01", P + "A1:00:02", 2);
            AddLink(P + "A1:00:01", P + "A1:00:03", 3);
            AddLink(P + "A1:00:02", P + "A1:00:03", 2);
            AddLink(P + "A1:00:02", P + "C3:00:01", 3);
            AddLink(P + "A1:00:03", P + "B2:00:01", 4);
            AddLink(P + "A1:00:03", P + "C3:00:01", 5);

            AddLink(P + "B2:00:01", P + "B2:00:02", 2);
            AddLink(P + "B2:00:02", P + "C3:00:02", 3);
            AddLink(P + "C3:00:01", P + "C3:00:02", 6);
        }
    }

    private void AddLink(string a, string b, double cost)
    {
        if (!_adj.TryGetValue(a, out var listA)) { listA = new List<MeshLink>(); _adj[a] = listA; }
        if (!_adj.TryGetValue(b, out var listB)) { listB = new List<MeshLink>(); _adj[b] = listB; }

        if (listA.Any(l => Other(l, a) == b)) return;

        var link = new MeshLink(a, b, cost);
        _links.Add(link);
        listA.Add(link);
        listB.Add(link);
    }

 
    private void SyncDevices(List<Sensor> devices)
    {
        foreach (var d in devices)
        {
            if (_adj.ContainsKey(d.MacAddress)) continue;

            _adj[d.MacAddress] = new List<MeshLink>();
            AddLink(GatewayId, d.MacAddress, 8);

            var zone = ZoneOf(d.Location);
            var neighbour = devices.FirstOrDefault(o =>
                o.MacAddress != d.MacAddress &&
                _adj.ContainsKey(o.MacAddress) &&
                ZoneOf(o.Location) == zone);

            if (neighbour is not null)
                AddLink(d.MacAddress, neighbour.MacAddress, 3);
        }
    }

    private static string ZoneOf(string location)
    {
        var parts = location.Split('-');
        return parts.Length >= 2 ? parts[0] + "-" + parts[1] : location;
    }

    private static string Other(MeshLink link, string vertex) => link.A == vertex ? link.B : link.A;

    private MeshContext BuildContext()
    {
        var devices = _registry.GetAll();
        SyncDevices(devices);

        var byMac = devices.ToDictionary(s => s.MacAddress);

        return new MeshContext(
            byMac,
            id => id == GatewayId || (byMac.TryGetValue(id, out var sensor) && sensor.IsOnline && !_forcedDown.Contains(id)),
            id => id == GatewayId ? "Gateway Hub" : (byMac.TryGetValue(id, out var found) ? found.Name : id));
    }

  

    public bool SetForcedDown(string mac, bool down)
    {
        lock (_lock)
        {
            var id = DeviceRegistry.NormalizeMac(mac);
            if (!_registry.Exists(id)) return false;

            if (down) _forcedDown.Add(id);
            else _forcedDown.Remove(id);
            return true;
        }
    }

   
    private MstState Prim(Func<string, bool> isUp)
    {
        var parent = new Dictionary<string, string>();
        var inTree = new HashSet<string> { GatewayId };
        var edges = new List<MeshLink>();
        double total = 0;

        var heap = new PriorityQueue<(MeshLink Link, string To, string From), double>();

        void Push(string vertex)
        {
            foreach (var link in _adj[vertex])
            {
                var other = Other(link, vertex);
                if (isUp(other) && !inTree.Contains(other))
                    heap.Enqueue((link, other, vertex), link.Cost);
            }
        }

        Push(GatewayId);

        while (heap.TryDequeue(out var item, out _))
        {
            if (inTree.Contains(item.To)) continue;

            inTree.Add(item.To);
            parent[item.To] = item.From;
            edges.Add(item.Link);
            total += item.Link.Cost;
            Push(item.To);
        }

        return new MstState(parent, edges, total, inTree);
    }

    private static List<string>? PathToGateway(string id, MstState mst)
    {
        if (id == GatewayId) return new List<string> { GatewayId };
        if (!mst.Parent.ContainsKey(id)) return null;

        var path = new List<string> { id };
        var current = id;
        while (current != GatewayId)
        {
            current = mst.Parent[current];
            path.Add(current);
        }
        return path;
    }

    private double PathCost(List<string> path)
    {
        double total = 0;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            var a = path[i];
            var b = path[i + 1];
            total += _adj[a].First(l => Other(l, a) == b).Cost;
        }
        return total;
    }

    

    public List<TraversalStep> Traverse(string? start, bool depthFirst)
    {
        lock (_lock)
        {
            var ctx = BuildContext();
            var startId = string.IsNullOrWhiteSpace(start) || start.Equals(GatewayId, StringComparison.OrdinalIgnoreCase)
                ? GatewayId
                : DeviceRegistry.NormalizeMac(start);

            return TraverseInternal(startId, depthFirst, ctx);
        }
    }

    private List<TraversalStep> TraverseInternal(string startId, bool depthFirst, MeshContext ctx)
    {
        var order = new List<TraversalStep>();
        if (!_adj.ContainsKey(startId) || !ctx.IsUp(startId)) return order;

        if (!depthFirst)
        {
           
            var seen = new HashSet<string> { startId };
            var queue = new Queue<(string Id, int Hops)>();
            queue.Enqueue((startId, 0));

            while (queue.Count > 0)
            {
                var (id, hops) = queue.Dequeue();
                order.Add(new TraversalStep(id, ctx.NameOf(id), hops));

                foreach (var link in _adj[id].OrderBy(l => l.Cost))
                {
                    var next = Other(link, id);
                    if (ctx.IsUp(next) && seen.Add(next))
                        queue.Enqueue((next, hops + 1));
                }
            }
        }
        else
        {
            
            var visited = new HashSet<string>();
            var stack = new Stack<(string Id, int Hops)>();
            stack.Push((startId, 0));

            while (stack.Count > 0)
            {
                var (id, hops) = stack.Pop();
                if (!visited.Add(id)) continue;

                order.Add(new TraversalStep(id, ctx.NameOf(id), hops));

                foreach (var link in _adj[id].OrderByDescending(l => l.Cost))
                {
                    var next = Other(link, id);
                    if (ctx.IsUp(next) && !visited.Contains(next))
                        stack.Push((next, hops + 1));
                }
            }
        }

        return order;
    }

    

    public MeshRoute? GetRoute(string mac)
    {
        lock (_lock)
        {
            var ctx = BuildContext();
            var id = mac.Equals(GatewayId, StringComparison.OrdinalIgnoreCase)
                ? GatewayId
                : DeviceRegistry.NormalizeMac(mac);

            if (id != GatewayId && !ctx.ByMac.ContainsKey(id)) return null;

            if (!ctx.IsUp(id))
                return new MeshRoute(id, ctx.NameOf(id), false, new List<string>(), new List<string>(), 0, 0,
                    "Node is down, so it cannot send telemetry.");

            var mst = Prim(ctx.IsUp);
            var path = PathToGateway(id, mst);

            if (path is null)
                return new MeshRoute(id, ctx.NameOf(id), false, new List<string>(), new List<string>(), 0, 0,
                    "Node is up but no live path reaches the gateway.");

            return new MeshRoute(id, ctx.NameOf(id), true, path, path.Select(ctx.NameOf).ToList(),
                path.Count - 1, PathCost(path), "");
        }
    }

    public MeshSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var ctx = BuildContext();
            var mst = Prim(ctx.IsUp);
            var baseline = Prim(_ => true);            
            var mstEdges = new HashSet<MeshLink>(mst.Edges);

            var nodes = new List<MeshNodeInfo>
            {
                new(GatewayId, "Gateway Hub", "Central gateway", "Gateway", true, true, false)
            };

            foreach (var d in ctx.ByMac.Values.OrderBy(s => s.Name))
            {
                nodes.Add(new MeshNodeInfo(d.MacAddress, d.Name, d.Location, d.Category.ToString(),
                    false, ctx.IsUp(d.MacAddress), _forcedDown.Contains(d.MacAddress)));
            }

            var links = _links
                .Select(l => new MeshLinkInfo(l.A, l.B, l.Cost, ctx.IsUp(l.A) && ctx.IsUp(l.B), mstEdges.Contains(l)))
                .ToList();

            var down = ctx.ByMac.Keys.Where(id => !ctx.IsUp(id)).ToList();
            var isolated = ctx.ByMac.Keys.Where(id => ctx.IsUp(id) && !mst.InTree.Contains(id)).ToList();

            var reroutes = new List<Reroute>();
            foreach (var id in ctx.ByMac.Keys.Where(ctx.IsUp))
            {
                var oldPath = PathToGateway(id, baseline);
                if (oldPath is null) continue;

                bool crossedDownNode = oldPath.Any(p => p != id && !ctx.IsUp(p));
                if (!crossedDownNode) continue;

                var newPath = PathToGateway(id, mst);
                reroutes.Add(new Reroute(
                    id,
                    ctx.NameOf(id),
                    oldPath,
                    newPath ?? new List<string>(),
                    PathCost(oldPath),
                    newPath is null ? 0 : PathCost(newPath),
                    newPath is null));
            }

            var bfs = TraverseInternal(GatewayId, false, ctx);

            return new MeshSnapshot(nodes, links, mst.Total, mst.InTree.Count - 1, bfs, down, isolated, reroutes);
        }
    }
}