using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Reads the sphere volumes out of a client level-design file.
/// </summary>
/// <remarks>
/// The file is a flat list of five-line blocks, and the sphere is named by its database id in
/// <c>stype</c>:
/// <code>
/// area
///     kind 1
///     stype 2307
///     pos ( x 1313.48, y 1111.46, z 100 )
///     radius 500
/// </code>
/// The positions are zone-local; turning them into world coordinates is the caller's business,
/// since only the caller knows which zone the file was found in.
/// </remarks>
public static class SphereVolumeReader
{
    public readonly record struct Volume(uint SphereId, Vector3 Position, float Radius);

    public static List<Volume> Read(string contents)
    {
        var volumes = new List<Volume>();
        if (string.IsNullOrWhiteSpace(contents))
            return volumes;

        var lines = contents.ToLower().Split('\n');

        for (var i = 0; i < lines.Length - 4; i++)
        {
            var head = Trim(lines[i]);
            var stype = Trim(lines[i + 2]);
            var position = Trim(lines[i + 3]);
            var radius = Trim(lines[i + 4]);

            if (!head.StartsWith("area") || !stype.StartsWith("stype") ||
                !position.StartsWith("pos") || !radius.StartsWith("radius"))
                continue;

            var volume = TryRead(stype, position, radius);
            if (volume != null)
                volumes.Add(volume.Value);

            i += 4;
        }

        return volumes;
    }

    private static Volume? TryRead(string stypeLine, string positionLine, string radiusLine)
    {
        try
        {
            var sphereId = uint.Parse(stypeLine.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture);

            var coordinates = positionLine
                .Substring(3)
                .Replace("(", "").Replace(")", "")
                .Replace("x", "").Replace("y", "").Replace("z", "")
                .Replace(" ", "")
                .Split(',');
            if (coordinates.Length != 3)
                return null;

            var position = new Vector3(
                float.Parse(coordinates[0], NumberStyles.Float, CultureInfo.InvariantCulture),
                float.Parse(coordinates[1], NumberStyles.Float, CultureInfo.InvariantCulture),
                float.Parse(coordinates[2], NumberStyles.Float, CultureInfo.InvariantCulture));

            var radius = float.Parse(radiusLine.AsSpan(7), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (radius <= 0f)
                return null;

            return new Volume(sphereId, position, radius);
        }
        catch (Exception)
        {
            // One malformed block must not cost the caller the rest of the file.
            return null;
        }
    }

    private static string Trim(string line)
    {
        return line.Trim(' ', '\t', '\r');
    }
}
