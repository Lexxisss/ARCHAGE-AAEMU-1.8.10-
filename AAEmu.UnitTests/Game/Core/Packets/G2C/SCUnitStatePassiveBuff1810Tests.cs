using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCUnitStatePassiveBuff1810Tests
{
    [Fact]
    public void SlaveUsesPassiveBuffTypeIdsAndPiscEncoding()
    {
        var slave = new Slave
        {
            Template = new SlaveTemplate { Id = 423 }
        };
        slave.Template.PassiveBuffs.Add(new SlavePassiveBuffs { PassiveBuffId = 270 });
        slave.Template.PassiveBuffs.Add(new SlavePassiveBuffs { PassiveBuffId = 278 });

        var ids = SCUnitStatePacket.GetPassiveBuffTypeIds(slave);

        Assert.Equal(new uint[] { 270, 278 }, ids);
        Assert.DoesNotContain(11077u, ids);
        Assert.DoesNotContain(14278u, ids);

        var encoded = new PacketStream();
        SCUnitStatePacket.WritePiscValues(encoded, ids);
        var decoded = new PacketStream(encoded.GetBytes()).ReadPiscW(ids.Count);

        Assert.Equal(new long[] { 270, 278 }, decoded);
    }

    [Fact]
    public void CharacterAlsoUsesPassiveBuffTypeIdInsteadOfResolvedBuffId()
    {
        var character = new CharacterMock();
        character.Skills = new CharacterSkills(character);
        character.Skills.PassiveBuffs.Add(270, new PassiveBuff
        {
            Id = 270,
            Template = new PassiveBuffTemplate
            {
                Id = 270,
                BuffId = 11077
            }
        });

        var ids = SCUnitStatePacket.GetPassiveBuffTypeIds(character);

        Assert.Equal(new uint[] { 270 }, ids);
        Assert.DoesNotContain(11077u, ids);
    }
}
