using System.Collections.Generic;
using System.Numerics;

using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// One placed sphere that hands a buff to whatever stands inside it. The volume comes from the
/// client's level design, the buff and the conditions from the client database.
/// </summary>
public class SphereBuffArea
{
    public uint SphereId { get; set; }
    public string Name { get; set; }

    public uint WorldId { get; set; }
    public uint ZoneKey { get; set; }

    /// <summary>World coordinates, already converted from the zone-local ones in the level design.</summary>
    public Vector3 Position { get; set; }
    public float Radius { get; set; }

    public uint BuffId { get; set; }
    public uint RemoveOnLeaveBuffId { get; set; }
    public bool AndPet { get; set; }

    /// <summary>
    /// True when the sphere is satisfied by any one of its requirements rather than all of them.
    /// </summary>
    public bool OrUnitReqs { get; set; }

    public List<PlotUnitRequirement> Requirements { get; set; } = new();

    public bool Contains(Vector3 position)
    {
        return Vector3.DistanceSquared(Position, position) <= Radius * Radius;
    }

    /// <summary>
    /// Whether this unit is the kind of thing the sphere acts on.
    /// </summary>
    /// <remarks>
    /// A requirement kind the evaluator does not know fails closed, as it does for plots. That is
    /// safe here because the harbour spheres pair their faction check with an unrecovered kind
    /// under "or", so the faction alone decides them.
    /// </remarks>
    public bool Accepts(Unit unit)
    {
        if (Requirements.Count == 0)
            return true;

        if (OrUnitReqs)
        {
            foreach (var requirement in Requirements)
            {
                if (requirement.Check(unit, unit))
                    return true;
            }

            return false;
        }

        foreach (var requirement in Requirements)
        {
            if (!requirement.Check(unit, unit))
                return false;
        }

        return true;
    }
}
