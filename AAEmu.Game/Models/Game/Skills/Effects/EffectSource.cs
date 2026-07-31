using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class EffectSource
{
    public Skill Skill { get; set; }
    public BuffTemplate Buff { get; set; }
    public Buff SourceBuff { get; set; }
    public int Amount { get; set; }
    public bool IsTrigger { get; set; }

    public EffectSource()
    {
    }

    public EffectSource(Skill skill)
    {
        Skill = skill;
    }

    public EffectSource(BuffTemplate buff)
    {
        Buff = buff;
    }

    public EffectSource(Buff buff)
    {
        SourceBuff = buff;
        Buff = buff?.Template;
        Skill = buff?.Skill;
    }

    public EffectSource(Skill skill, BuffTemplate buff)
    {
        Skill = skill;
        Buff = buff;
    }
}
