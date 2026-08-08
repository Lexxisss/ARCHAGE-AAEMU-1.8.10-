using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCSkillAnimation1810Tests
{
    [Fact]
    public void SkillStartedPreservesPositionTargetVariant()
    {
        var target = new SkillCastPositionTarget
        {
            Type = SkillCastTargetType.Position,
            PosX = 1234.5f,
            PosY = 6789.25f,
            PosZ = 42.75f,
            PosRot = 1.25f,
            ObjId1 = 0x11,
            ObjId2 = 0x22,
            ObjId3 = 0x33
        };
        var skill = new Skill(new SkillTemplate { Id = 23593 });
        var packet = new SCSkillStartedPacket(
            23593,
            7,
            new SkillCasterUnit(0x44),
            target,
            skill,
            new SkillObject())
        {
            CastTime = 1000,
            BaseCastTime = 1000
        };

        var encoded = new PacketStream();
        packet.Write(encoded);
        var stream = new PacketStream(encoded.GetBytes());

        Assert.Equal(23593u, stream.ReadUInt32());
        Assert.Equal((ushort)7, stream.ReadUInt16());

        var casterType = (SkillCasterType)stream.ReadByte();
        var caster = SkillCaster.GetByType(casterType);
        caster.Read(stream);
        Assert.Equal(SkillCasterType.Unit, casterType);
        Assert.Equal(0x44u, caster.ObjId);

        var targetType = (SkillCastTargetType)stream.ReadByte();
        var decodedTarget = SkillCastTarget.GetByType(targetType);
        decodedTarget.Read(stream);

        var decodedPosition = Assert.IsType<SkillCastPositionTarget>(decodedTarget);
        Assert.Equal(SkillCastTargetType.Position, targetType);
        Assert.Equal(target.PosX, decodedPosition.PosX, 3);
        Assert.Equal(target.PosY, decodedPosition.PosY, 3);
        Assert.Equal(target.PosZ, decodedPosition.PosZ);
        Assert.Equal(target.PosRot, decodedPosition.PosRot);
        Assert.Equal(target.ObjId1, decodedPosition.ObjId1);
        Assert.Equal(target.ObjId2, decodedPosition.ObjId2);
        Assert.Equal(target.ObjId3, decodedPosition.ObjId3);
    }

    [Fact]
    public void FireAnimationSelectorMatchesTargetPriority()
    {
        var template = new SkillTemplate
        {
            FireAnimId = 100,
            StringInstrumentFireAnimId = 101,
            PercussionInstrumentFireAnimId = 102,
            TubeInstrumentFireAnimId = 103,
            ShotGunFireAnimId = 104,
            TwoHandFireAnimId = 105,
            DualWieldFireAnimId = 106
        };
        var rangedShotGun = new Holdable { Id = 31 };
        var twoHand = new Holdable { Id = 7, SlotTypeId = (uint)EquipmentItemSlotType.TwoHanded };

        Assert.Equal(101u, SCSkillFiredPacket.SelectSkillFireAnimation(
            template, new Holdable { Id = 21 }, rangedShotGun, twoHand, true));
        Assert.Equal(102u, SCSkillFiredPacket.SelectSkillFireAnimation(
            template, new Holdable { Id = 22 }, rangedShotGun, twoHand, true));
        Assert.Equal(103u, SCSkillFiredPacket.SelectSkillFireAnimation(
            template, new Holdable { Id = 23 }, rangedShotGun, twoHand, true));
        Assert.Equal(104u, SCSkillFiredPacket.SelectSkillFireAnimation(
            template, null, rangedShotGun, twoHand, true));
        Assert.Equal(105u, SCSkillFiredPacket.SelectSkillFireAnimation(
            template, null, null, twoHand, true));
        Assert.Equal(106u, SCSkillFiredPacket.SelectSkillFireAnimation(
            template, null, null, new Holdable { Id = 1, SlotTypeId = (uint)EquipmentItemSlotType.OneHanded }, true));
        Assert.Equal(100u, SCSkillFiredPacket.SelectSkillFireAnimation(
            template, null, null, null, false));
    }

    [Fact]
    public void MissingInstrumentOverrideFallsThroughToShotGun()
    {
        var template = new SkillTemplate
        {
            FireAnimId = 200,
            StringInstrumentFireAnimId = 0,
            ShotGunFireAnimId = 204
        };

        Assert.Equal(204u, SCSkillFiredPacket.SelectSkillFireAnimation(
            template,
            new Holdable { Id = 21 },
            new Holdable { Id = 31 },
            null,
            false));
    }
    [Fact]
    public void FireballAndGliderKeepTheirBaseFireAnimation()
    {
        // 1.8.1.0-Kakao-KR.sqlite: both skills have every specialized fire override set to 0.
        var fireball = new SkillTemplate { Id = 24894, FireAnimId = 599 };
        var gliderRoll = new SkillTemplate { Id = 23040, FireAnimId = 0 };
        var instrument = new Holdable { Id = 21 };
        var rangedShotGun = new Holdable { Id = 31 };
        var twoHand = new Holdable { Id = 7, SlotTypeId = (uint)EquipmentItemSlotType.TwoHanded };

        Assert.Equal(599u, SCSkillFiredPacket.SelectSkillFireAnimation(
            fireball, instrument, rangedShotGun, twoHand, true));
        Assert.Equal(0u, SCSkillFiredPacket.SelectSkillFireAnimation(
            gliderRoll, instrument, rangedShotGun, twoHand, true));
    }

}
