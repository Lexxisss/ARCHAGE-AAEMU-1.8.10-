using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C;

/// <summary>
/// Target 10.8 cache response (client name: TCCharInfoQueriedPacket).
///
/// Wire layout:
///   query key:
///     UInt16 cacheType
///     UInt64 typeId
///     UInt8  worldId
///     UInt32 type
///   cached value:
///     UInt16 cacheType
///     UInt64 lastCachedTime
///     variant payload selected by cacheType
///
/// Known variants:
///   1 = character name (string)
///   2 = three ability bytes
///   3 = expedition name (string)
/// </summary>
public class TCCharNameQueriedPacket : StreamPacket
{
    private const ushort CharacterNameCacheType = 1;
    private const ushort AbilitiesCacheType = 2;
    private const ushort ExpeditionNameCacheType = 3;

    private readonly ushort _cacheType;
    private readonly ulong _typeId;
    private readonly byte _worldId;
    private readonly uint _type;
    private readonly ulong _lastCachedTime;
    private readonly string _value;
    private readonly byte[] _abilities;

    public TCCharNameQueriedPacket(
        ushort cacheType,
        ulong typeId,
        byte worldId,
        uint type,
        ulong lastCachedTime,
        string value,
        byte[] abilities = null) : base(TCOffsets.TCCharNameQueriedPacket)
    {
        _cacheType = cacheType;
        _typeId = typeId;
        _worldId = worldId;
        _type = type;
        _lastCachedTime = lastCachedTime;
        _value = value ?? string.Empty;
        _abilities = abilities ?? Array.Empty<byte>();
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Echo the complete 10.8 query key.
        stream.Write(_cacheType);
        stream.Write(_typeId);
        stream.Write(_worldId);
        stream.Write(_type);

        // Cached result header. The cache type is intentionally serialized a
        // second time; this is what the target client's serializer expects.
        stream.Write(_cacheType);
        stream.Write(_lastCachedTime);

        switch (_cacheType)
        {
            case CharacterNameCacheType:
            case ExpeditionNameCacheType:
                stream.Write(_value);
                break;

            case AbilitiesCacheType:
                // The target serializer writes exactly three ability bytes.
                stream.Write(_abilities.Length > 0 ? _abilities[0] : (byte)0);
                stream.Write(_abilities.Length > 1 ? _abilities[1] : (byte)0);
                stream.Write(_abilities.Length > 2 ? _abilities[2] : (byte)0);
                break;

            default:
                // Unknown cache types have no proven variant payload. The
                // common key and cache-result header are still required.
                break;
        }

        return stream;
    }
}
