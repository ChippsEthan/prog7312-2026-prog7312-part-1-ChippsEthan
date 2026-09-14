namespace SmartX.API.Models;

public class DeploymentNode
{
  public string Name { get; set; } = string.Empty;
  public bool IsConfigured { get; set; }
  public List<DeploymentNode> Children { get; set; } = new();

    public static bool ValidateTree(DeploymentNode node)
    {
    if (node.Children.Count == 0)
    return node.IsConfigured;

         foreach (var child in node.Children)
        {
         if (!ValidateTree(child))
         return false;
        }
         return node.IsConfigured;
    }}