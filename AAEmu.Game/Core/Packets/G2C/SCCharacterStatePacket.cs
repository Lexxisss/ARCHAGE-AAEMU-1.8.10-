using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterStatePacket : GamePacket
{
    private const int Protocol1810AbilityCount = 30;
    private const int Protocol1810ReservedTailLength = 74;

    private readonly Character _character;

    public SCCharacterStatePacket(Character character) : base(SCOffsets.SCCharacterStatePacket, 5)
    {
        _character = character;
    }

    public override PacketStream Write(PacketStream stream)
    {
        var bodyStart = stream.Count;

        stream.Write(_character.InstanceId); // iid
        stream.Write(_character.Guid);       // characterId
        stream.Write(0);                     // rwd
        stream.Write(0);                     // 10.8.1 pre-character reserved field

        // The state packet embeds the same target-version character descriptor
        // used by create/list responses. It must not use the legacy Character.Write layout.
        _character.WriteCharacterList1810(stream);

        stream.Write(0f); // angle x
        stream.Write(0f); // angle y
        stream.Write(0f); // angle z
        stream.Write(_character.Experience);
        stream.Write(0L); // heir experience; not persisted by the current character model yet
        stream.Write(_character.RecoverableExp);
        stream.Write(0u); // penalized experience
        stream.Write(0u); // return district id
        stream.Write(0);  // return district type id
        stream.Write(0u); // resurrection district type id

        for (var i = 0; i < Protocol1810AbilityCount; i++)
            stream.Write(0u);

        stream.Write(_character.Mails.UnreadMailCount.TotalSent);
        stream.Write(_character.Mails.UnreadMailCount.TotalReceived);
        stream.Write(_character.Mails.UnreadMailCount.TotalMiaReceived);
        stream.Write(_character.Mails.UnreadMailCount.TotalCommercialReceived);
        stream.Write(_character.Mails.UnreadMailCount.UnreadReceived);
        stream.Write(_character.Mails.UnreadMailCount.UnreadMiaReceived);
        stream.Write(_character.Mails.UnreadMailCount.UnreadCommercialReceived);
        stream.Write(_character.NumInventorySlots);
        stream.Write(_character.NumBankSlots);
        stream.Write(_character.Money);
        stream.Write(_character.Money2);
        stream.Write(0L);
        stream.Write(0L);

        stream.Write(_character.AutoUseAAPoint);
        stream.Write(0);  // jury points
        stream.Write(0);  // jail seconds
        stream.Write(0L); // bounty money
        stream.Write(0L); // bounty time
        stream.Write(0);  // reported count
        stream.Write(0);  // suspected count
        stream.Write(0);  // total play time

        // Target 10.8.1 has no legacy createdTime field at this position.
        stream.Write(_character.ExpandedExpert);
        stream.Write(DateTime.MinValue); // nation join time
        stream.Write((byte)0);           // remaining bot checks
        stream.Write((short)0);          // failed bot checks

        for (var i = 0; i < 8; i++)
            stream.Write(DateTime.MinValue);

        stream.Write(0u);                // daily leadership points
        stream.Write(DateTime.MinValue); // last daily leadership time
        stream.Write(0);                 // total bad-user reports
        stream.Write(new byte[Protocol1810ReservedTailLength], false);

        Logger.Info(
            "SCCharacterState 0x28B: charId={0}, typed protocol1810 bodyLen={1}",
            _character.Id,
            stream.Count - bodyStart);

        return stream;
    }
}
