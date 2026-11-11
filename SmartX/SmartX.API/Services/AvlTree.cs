namespace SmartX.API.Services;


public class AvlTree<TKey, TValue> where TKey : IComparable<TKey>
{
    private sealed class Node
    {
        public TKey Key;
        public TValue Value;
        public Node? Left;
        public Node? Right;
        public int Height = 1;

        public Node(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }

    private Node? _root;

    public int Count { get; private set; }
    public int Rotations { get; private set; }
    public int LastQueryNodesVisited { get; private set; }
    public int Height => HeightOf(_root);

    public void Insert(TKey key, TValue value) => _root = InsertNode(_root, key, value);

    public bool ContainsKey(TKey key)
    {
        var node = _root;
        while (node is not null)
        {
            int c = key.CompareTo(node.Key);
            if (c == 0) return true;
            node = c < 0 ? node.Left : node.Right;
        }
        return false;
    }


    public List<TValue> Range(TKey low, TKey high)
    {
        var result = new List<TValue>();
        LastQueryNodesVisited = 0;
        Collect(_root, low, high, result);
        return result;
    }

    public bool RemoveMin()
    {
        if (_root is null) return false;
        _root = RemoveMinNode(_root);
        Count--;
        return true;
    }

    private void Collect(Node? node, TKey low, TKey high, List<TValue> result)
    {
        if (node is null) return;
        LastQueryNodesVisited++;

        if (low.CompareTo(node.Key) < 0)
            Collect(node.Left, low, high, result);

        if (low.CompareTo(node.Key) <= 0 && high.CompareTo(node.Key) >= 0)
            result.Add(node.Value);

        if (high.CompareTo(node.Key) > 0)
            Collect(node.Right, low, high, result);
    }

    private Node InsertNode(Node? node, TKey key, TValue value)
    {
        if (node is null)
        {
            Count++;
            return new Node(key, value);
        }

        int c = key.CompareTo(node.Key);
        if (c < 0) node.Left = InsertNode(node.Left, key, value);
        else if (c > 0) node.Right = InsertNode(node.Right, key, value);
        else
        {
            node.Value = value;   
            return node;
        }

        return Rebalance(node);
    }

    private Node? RemoveMinNode(Node node)
    {
        if (node.Left is null) return node.Right;
        node.Left = RemoveMinNode(node.Left);
        return Rebalance(node);
    }

    private Node Rebalance(Node node)
    {
        UpdateHeight(node);
        int balance = HeightOf(node.Left) - HeightOf(node.Right);

        if (balance > 1)
        {
            if (node.Left is not null && HeightOf(node.Left.Left) < HeightOf(node.Left.Right))
                node.Left = RotateLeft(node.Left);         
            return RotateRight(node);                      
        }

        if (balance < -1)
        {
            if (node.Right is not null && HeightOf(node.Right.Right) < HeightOf(node.Right.Left))
                node.Right = RotateRight(node.Right);       
            return RotateLeft(node);                        
        }

        return node;
    }

    private Node RotateRight(Node y)
    {
        var x = y.Left!;
        y.Left = x.Right;
        x.Right = y;
        UpdateHeight(y);
        UpdateHeight(x);
        Rotations++;
        return x;
    }

    private Node RotateLeft(Node x)
    {
        var y = x.Right!;
        x.Right = y.Left;
        y.Left = x;
        UpdateHeight(x);
        UpdateHeight(y);
        Rotations++;
        return y;
    }

    private static int HeightOf(Node? node) => node?.Height ?? 0;

    private static void UpdateHeight(Node node) =>
        node.Height = 1 + Math.Max(HeightOf(node.Left), HeightOf(node.Right));
}