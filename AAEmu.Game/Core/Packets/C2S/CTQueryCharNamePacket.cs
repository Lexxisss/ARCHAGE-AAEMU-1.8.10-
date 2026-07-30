using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Core.Packets.S2C;

namespace AAEmu.Game.Core.Packets.C2S;

/// <summary>
/// Target 10.8 name/cache query (client name: CTQueryCharInfoPacket).
/// Wire layout is 15 bytes:
/// UInt16 cacheType, UInt64 typeId, UInt8 worldId, UInt32 type.
/// </summary>
public class CTQueryCharNamePacket : StreamPacket
{
    private const ushort CharacterNameCacheType = 1;

    public CTQueryCharNamePacket() : base(CTOffsets.CTQueryCharNamePacket)
    {
    }

    public override void Read(PacketStream stream)
    {
        // 10.8 no longer sends the old single UInt32 character id here.
        var cacheType = stream.ReadUInt16();
        var typeId = stream.ReadUInt64();
        var worldId = stream.ReadByte();
        var type = stream.ReadUInt32();

        // Character/object cache keys in this protocol carry the local id in
        // the high 32 bits. Keep a low-half fallback for malformed/legacy keys.
        var characterId = (uint)(typeId >> 32);
        if (characterId == 0)
            characterId = (uint)typeId;

        var name = cacheType == CharacterNameCacheType
            ? NameManager.Instance.GetCharacterName(characterId) ?? string.Empty
            : string.Empty;

        // A query must always receive a structurally valid response, even when
        // the requested entry is not present. The target client caches it.
        Connection.SendPacket(new TCCharNameQueriedPacket(
            cacheType,
            typeId,
            worldId,
            type,
            lastCachedTime: 0,
            value: name));

        Logger.Debug(
            "QueryCharInfo: cacheType={0}, typeId=0x{1:X16}, worldId={2}, type={3}, characterId={4}, value='{5}', trailing={6}",
            cacheType,
            typeId,
            worldId,
            type,
            characterId,
            name,
            stream.LeftBytes);
    }
}
