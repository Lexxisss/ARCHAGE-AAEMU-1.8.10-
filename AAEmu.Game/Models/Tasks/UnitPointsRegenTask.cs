using System;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Tasks;

public class UnitPointsRegenTask : Task
{
    private Unit _unit;

    public UnitPointsRegenTask(Unit unit)
    {
        _unit = unit;
    }

    public override void Execute()
    {
        var oldHp = _unit.Hp;
        if (_unit.Hp < _unit.MaxHp && _unit.Hp > 0)
            _unit.Hp += _unit.HpRegen; // TODO at battle _unit.PersistentHpRegen
        if (_unit.Mp < _unit.MaxMp && _unit.Hp > 0)
            _unit.Mp += _unit.MpRegen; // TODO at battle _unit.PersistentMpRegen
        _unit.Hp = Math.Min(_unit.Hp, _unit.MaxHp);
        _unit.Mp = Math.Min(_unit.Mp, _unit.MaxMp);
        // Do not publish point updates before the unit has completed its world
        // visibility transition. Target captures do not send periodic 0x187
        // between self SCUnitState and FinishState(7); doing so made the 10.8
        // client open/update a target frame for a not-yet-visible entity.
        if (_unit.IsVisible && (_unit is not Character character || character.WorldEntryComplete))
            _unit.BroadcastPacket(new SCUnitPointsPacket(_unit.ObjId, _unit.Hp, _unit.Mp, _unit.HighAbilityRsc), true);
        _unit.PostUpdateCurrentHp(_unit,oldHp, _unit.Hp, KillReason.Unknown);
        //if (_unit.Hp >= _unit.MaxHp && _unit.Mp >= _unit.MaxMp)
        //    _unit.StopRegen();
    }
}
