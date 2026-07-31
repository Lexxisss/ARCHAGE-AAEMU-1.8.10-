using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSInstanceLoadedPacket : GamePacket
{
    public CSInstanceLoadedPacket() : base(CSOffsets.CSInstanceLoadedPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Empty struct
        // TODO Debug

        Connection.SendPacket(new SCUnitStatePacket(Connection.ActiveChar));
        // The client keeps its own cooldown maps and only draws a hotbar sweep for what is
        // in them. Without this snapshot it never learns about server-side cooldowns, so it
        // kept re-sending casts the server was rejecting as CooldownTime. Sent even when the
        // lists are empty, which is the authoritative "nothing is on cooldown" answer.
        Connection.SendPacket(SCCooldownsPacket.ForCharacter(Connection.ActiveChar));
        Connection.SendPacket(new SCDetailedTimeOfDayPacket(12f));

        Connection.ActiveChar.DisabledSetPosition = false;

        Logger.Debug("InstanceLoaded.");
    }
}
