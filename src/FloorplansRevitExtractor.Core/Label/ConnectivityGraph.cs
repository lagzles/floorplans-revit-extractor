using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Model;

namespace FloorplansRevitExtractor.Core.Label
{
    public sealed record GraphNode(int Id, string? Type, string Name);

    public sealed record GraphEdge(int Source, int Target, int DoorId, string Kind);

    /// <summary>
    /// The label's connectivity_graph, as the generator builds it: one node
    /// per room, one undirected edge per door with a room on both sides, and
    /// external_doors listing the doors whose room_b is null. There is no
    /// "outside" node.
    /// </summary>
    public sealed record ConnectivityGraph(
        IReadOnlyList<GraphNode> Nodes,
        IReadOnlyList<GraphEdge> Edges,
        IReadOnlyList<int> ExternalDoors)
    {
        public static ConnectivityGraph Build(IReadOnlyList<RoomPreviewRecord> rooms, IReadOnlyList<DoorRecord> doors) => new ConnectivityGraph(
            rooms.Select(r => new GraphNode(r.Id, r.Type, r.Name)).ToList(),
            doors.Where(d => d.RoomB.HasValue).Select(d => new GraphEdge(d.RoomA, d.RoomB!.Value, d.Id, d.Kind)).ToList(),
            doors.Where(d => !d.RoomB.HasValue).Select(d => d.Id).ToList());
    }
}
