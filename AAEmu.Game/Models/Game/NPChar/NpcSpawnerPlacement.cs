using System.Collections.Generic;
using System.Numerics;

namespace AAEmu.Game.Models.Game.NPChar;

public sealed class NpcSpawnerPlacement
{
    public uint PlacementId { get; set; }
    public uint TemplateId { get; set; }
    public string SpawnAreaType { get; set; } = "point";
    public string RoamingArea { get; set; }
    public string CombatArea { get; set; }
    public List<NpcSpawnerPoint> Points { get; } = new();
    public List<NpcSpawnerTriangle> Triangles { get; } = new();
    public List<NpcSpawnerPath> Paths { get; } = new();
    public List<NpcSpawnerAnchor> Anchors { get; } = new();
}

public sealed class NpcSpawnerPoint
{
    public Vector3 Position { get; set; }
    public float ZRotation { get; set; }
}

public sealed class NpcSpawnerTriangle
{
    public Vector3 V1 { get; set; }
    public Vector3 V2 { get; set; }
    public Vector3 V3 { get; set; }
    public float AreaRate { get; set; }
}

public sealed class NpcSpawnerPath
{
    public string Name { get; set; }
    public int PointNo { get; set; }
}

public sealed class NpcSpawnerAnchor
{
    public string Name { get; set; }
    public string Type { get; set; }
    public Vector3 Position { get; set; }
}

public sealed class NpcGroupMember
{
    public uint Id { get; set; }
    public uint NpcGroupId { get; set; }
    public uint NpcId { get; set; }
    public bool IsLeader { get; set; }
    public bool IsMoveLeader { get; set; }
    public Vector3 FormationOffset { get; set; }
    public float FormationTension { get; set; }
}
