using System;

using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public sealed class SpecialEffectRepeatTask : Task
{
    private readonly SpecialEffectAction _action;
    private readonly BaseUnit _caster;
    private readonly SkillCaster _casterObj;
    private readonly BaseUnit _target;
    private readonly SkillCastTarget _targetObj;
    private readonly CastAction _castObj;
    private readonly Skill _skill;
    private readonly SkillObject _skillObject;
    private readonly DateTime _time;
    private readonly int _value1;
    private readonly int _value2;
    private readonly int _value3;
    private readonly int _value4;

    public SpecialEffectRepeatTask(SpecialEffectAction action, BaseUnit caster, SkillCaster casterObj,
        BaseUnit target, SkillCastTarget targetObj, CastAction castObj, Skill skill,
        SkillObject skillObject, DateTime time, int value1, int value2, int value3, int value4)
    {
        _action = action;
        _caster = caster;
        _casterObj = casterObj;
        _target = target;
        _targetObj = targetObj;
        _castObj = castObj;
        _skill = skill;
        _skillObject = skillObject;
        _time = time;
        _value1 = value1;
        _value2 = value2;
        _value3 = value3;
        _value4 = value4;
    }

    public override void Execute()
    {
        _action?.Execute(_caster, _casterObj, _target, _targetObj, _castObj, _skill, _skillObject,
            _time, _value1, _value2, _value3, _value4);
    }
}
