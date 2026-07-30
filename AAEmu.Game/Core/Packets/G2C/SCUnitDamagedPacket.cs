using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces a single damage/miss/absorb event, driving the floating damage
/// number over the target. Field layout matched against 1.8.1.0 client captures.
/// </summary>
public class SCUnitDamagedPacket : GamePacket
{
    private const byte HoldableId = 0x06;

    private readonly CastAction _castAction;
    private readonly SkillCaster _skillCaster;
    private readonly uint _casterId;
    private readonly uint _targetId;
    private readonly int _damage;

    public int ManaBurn { get; set; }
    public SkillHitType HitType { get; set; }

    public SCUnitDamagedPacket(CastAction castAction, SkillCaster skillCaster, uint casterId, uint targetId, int damage, int absorbed)
        : base(SCOffsets.SCUnitDamagedPacket, 5)
    {
        _castAction = castAction;
        _skillCaster = skillCaster;
        _casterId = casterId;
        _targetId = targetId;
        _damage = damage;
        // Absorbed damage is not represented separately in the 1.8.1.0 layout.
        _ = absorbed;
    }

    public override PacketStream Write(PacketStream stream)
    {
        if (_castAction is CastPlot castPlot && !ShouldNormalizeDamagePlot(castPlot))
            stream.Write(_castAction);
        else
            WriteDamagePlot(stream);

        stream.Write(_skillCaster);
        stream.WriteBc(_casterId);
        stream.WriteBc(_targetId);
        stream.Write((byte)0); // crimeState
        stream.WritePisc(_damage, GetDamageStateCode());
        stream.WritePisc(0, 0, ManaBurn);
        stream.Write(HoldableId);
        stream.Write(0L);
        stream.Write((byte)0);
        stream.Write((byte)GetDamageResult());
        stream.Write((ushort)0x0800);
        stream.Write((byte)0);
        return stream;
    }

    private void WriteDamagePlot(PacketStream stream)
    {
        stream.Write((byte)CastType.Plot);
        stream.Write(0x09EDu);
        stream.Write(GetTlId());
        stream.Write(0x50FAu);
        stream.Write(GetActionId());
    }

    private ushort GetTlId()
    {
        return _castAction switch
        {
            CastSkill castSkill => castSkill.TlId,
            CastPlot castPlot => castPlot.TlId,
            _ => 0
        };
    }

    private static bool ShouldNormalizeDamagePlot(CastPlot castPlot)
    {
        return castPlot.EventId == 0x50FAu &&
               (castPlot.PlotId == 0x46D3u || castPlot.PlotId == 0x46D6u);
    }

    private uint GetActionId()
    {
        return HitType switch
        {
            SkillHitType.MeleeCritical or SkillHitType.RangedCritical or SkillHitType.SpellCritical => 0x46D6u,
            _ => 0x46D3u
        };
    }

    private int GetDamageResult()
    {
        return HitType switch
        {
            SkillHitType.MeleeMiss or SkillHitType.RangedMiss or SkillHitType.SpellMiss => 0,
            SkillHitType.MeleeDodge or SkillHitType.RangedDodge => 0,
            SkillHitType.MeleeBlock or SkillHitType.RangedBlock => 0,
            SkillHitType.MeleeParry or SkillHitType.RangedParry => 0,
            SkillHitType.Immune or SkillHitType.SpellResist => 0,
            _ => 3
        };
    }

    private int GetDamageStateCode()
    {
        // Ordinary-hit captures keep this PISC state byte at 0 for normal
        // damage; the later result byte carries the hit outcome.
        return HitType switch
        {
            SkillHitType.MeleeMiss or SkillHitType.RangedMiss or SkillHitType.SpellMiss => 0,
            SkillHitType.Immune or SkillHitType.SpellResist => 0,
            SkillHitType.MeleeDodge or SkillHitType.RangedDodge => 5,
            SkillHitType.MeleeBlock or SkillHitType.RangedBlock => 6,
            SkillHitType.MeleeParry or SkillHitType.RangedParry => 7,
            _ => 0
        };
    }
}
