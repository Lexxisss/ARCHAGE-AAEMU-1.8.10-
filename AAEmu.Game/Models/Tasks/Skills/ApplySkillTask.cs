using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public class ApplySkillTask : Task
{
    private readonly Skill _skill;
    private readonly BaseUnit _caster;
    private readonly SkillCaster _casterCaster;
    private readonly BaseUnit _target;
    private readonly SkillCastTarget _targetCaster;
    private readonly SkillObject _skillObject;
    private readonly bool _executeOnFireStage;
    private readonly bool _processOneShotSideEffects;

    public ApplySkillTask(Skill skill, BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject,
        bool executeOnFireStage = false, bool processOneShotSideEffects = true)
    {
        _skill = skill;
        _caster = caster;
        _casterCaster = casterCaster;
        _target = target;
        _targetCaster = targetCaster;
        _skillObject = skillObject;
        _executeOnFireStage = executeOnFireStage;
        _processOneShotSideEffects = processOneShotSideEffects;
    }

    public override void Execute()
    {
        _skill.ApplyEffects(_caster, _casterCaster, _target, _targetCaster, _skillObject,
            _executeOnFireStage, _processOneShotSideEffects);
        _skill.EndSkill(_caster);
    }
}
