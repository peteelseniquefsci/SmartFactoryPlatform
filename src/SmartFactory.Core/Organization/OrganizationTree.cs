namespace SmartFactory.Core.Organization;

/// <summary>In-memory helpers over the reporting line. Works on loaded positions so modules never query the database for the tree.</summary>
public static class OrganizationTree
{
    /// <summary>Every position that reports, directly or through others, to <paramref name="positionId"/>.</summary>
    public static HashSet<int> SubordinatePositions(IEnumerable<Position> all, int positionId)
    {
        var list = all.ToList();
        var result = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(positionId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var p in list.Where(p => p.ReportsToPositionId == current))
            {
                if (result.Add(p.Id)) queue.Enqueue(p.Id);
            }
        }
        return result;
    }

    /// <summary>The position a person's KRA is approved by: the position they report to.</summary>
    public static Position? Superior(IEnumerable<Position> all, Position position)
        => position.ReportsToPositionId is int id ? all.FirstOrDefault(p => p.Id == id) : null;

    /// <summary>True when <paramref name="superior"/> sits above <paramref name="position"/> in the reporting line.</summary>
    public static bool IsAbove(IEnumerable<Position> all, int superiorPositionId, int positionId)
        => SubordinatePositions(all, superiorPositionId).Contains(positionId);
}
