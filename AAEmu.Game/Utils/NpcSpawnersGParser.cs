using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;

using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Utils;

public static partial class NpcSpawnersGParser
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static List<NpcSpawnerPlacement> Parse(string fileName)
    {
        var result = new List<NpcSpawnerPlacement>();
        NpcSpawnerPlacement current = null;
        NpcSpawnerPoint point = null;
        NpcSpawnerTriangle triangle = null;
        NpcSpawnerAnchor anchor = null;
        var section = string.Empty;

        foreach (var rawLine in File.ReadLines(fileName))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line == "spawner")
            {
                if (current != null)
                    result.Add(current);
                current = new NpcSpawnerPlacement();
                point = null;
                triangle = null;
                anchor = null;
                section = string.Empty;
                continue;
            }

            if (current == null)
                continue;

            if (TryUInt(line, "spawnerId", out var placementId))
                current.PlacementId = placementId;
            else if (TryUInt(line, "spawnerType", out var templateId))
                current.TemplateId = templateId;
            else if (TryString(line, "spawnAreaType", out var areaType))
                current.SpawnAreaType = areaType;
            else if (TryString(line, "roamingArea", out var roamingArea))
                current.RoamingArea = roamingArea;
            else if (TryString(line, "combatArea", out var combatArea))
                current.CombatArea = combatArea;
            else if (line is "points" or "triInfos" or "paths" or "anchors")
            {
                section = line;
                point = null;
                triangle = null;
                anchor = null;
            }
            else if (line == "point" && section == "points")
            {
                point = new NpcSpawnerPoint();
                current.Points.Add(point);
            }
            else if (line == "triInfo" && section == "triInfos")
            {
                triangle = new NpcSpawnerTriangle();
                current.Triangles.Add(triangle);
            }
            else if (line == "anchor" && section == "anchors")
            {
                anchor = new NpcSpawnerAnchor();
                current.Anchors.Add(anchor);
            }
            else if (section == "points" && point != null && TryVector(line, "pos", out var pos))
                point.Position = pos;
            else if (section == "points" && point != null && TryFloat(line, "zRot", out var zRot))
                point.ZRotation = zRot;
            else if (section == "triInfos" && triangle != null && TryVector(line, "v1", out var v1))
                triangle.V1 = v1;
            else if (section == "triInfos" && triangle != null && TryVector(line, "v2", out var v2))
                triangle.V2 = v2;
            else if (section == "triInfos" && triangle != null && TryVector(line, "v3", out var v3))
                triangle.V3 = v3;
            else if (section == "triInfos" && triangle != null && TryFloat(line, "areaRate", out var areaRate))
                triangle.AreaRate = areaRate;
            else if (section == "paths" && TryPath(line, out var path))
                current.Paths.Add(path);
            else if (section == "anchors" && anchor != null && TryString(line, "anchorName", out var anchorName))
                anchor.Name = anchorName;
            else if (section == "anchors" && anchor != null && TryString(line, "anchorType", out var anchorType))
                anchor.Type = anchorType;
            else if (section == "anchors" && anchor != null && TryVector(line, "pos", out var anchorPos))
                anchor.Position = anchorPos;
        }

        if (current != null)
            result.Add(current);
        return result;
    }

    private static bool TryUInt(string line, string name, out uint value)
    {
        value = 0;
        return line.StartsWith(name + " ") && uint.TryParse(line[(name.Length + 1)..], NumberStyles.Integer, Invariant, out value);
    }

    private static bool TryFloat(string line, string name, out float value)
    {
        value = 0;
        return line.StartsWith(name + " ") && float.TryParse(line[(name.Length + 1)..], NumberStyles.Float, Invariant, out value);
    }

    private static bool TryString(string line, string name, out string value)
    {
        value = null;
        if (!line.StartsWith(name + " "))
            return false;
        value = line[(name.Length + 1)..].Trim();
        return true;
    }

    private static bool TryVector(string line, string name, out Vector3 vector)
    {
        vector = default;
        var match = VectorRegex().Match(line);
        if (!match.Success || match.Groups[1].Value != name)
            return false;
        vector = new Vector3(
            float.Parse(match.Groups[2].Value, Invariant),
            float.Parse(match.Groups[3].Value, Invariant),
            float.Parse(match.Groups[4].Value, Invariant));
        return true;
    }

    private static bool TryPath(string line, out NpcSpawnerPath path)
    {
        path = null;
        var match = PathRegex().Match(line);
        if (!match.Success)
            return false;
        path = new NpcSpawnerPath { Name = match.Groups[1].Value, PointNo = int.Parse(match.Groups[2].Value, Invariant) };
        return true;
    }

    [GeneratedRegex(@"^(\w+)\s+\(\s*x\s+([-+\d.eE]+),\s*y\s+([-+\d.eE]+),\s*z\s+([-+\d.eE]+)\s*\)$")]
    private static partial Regex VectorRegex();

    [GeneratedRegex(@"^path\s+\(\s*pathName\s+([^,\s]+),\s*pointNo\s+(-?\d+)\s*\)$")]
    private static partial Regex PathRegex();
}
