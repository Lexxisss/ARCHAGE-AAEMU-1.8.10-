using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncConsumeChangerModel : DoodadPhaseFuncTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Rate { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncConsumeChangerModel");
        return false;
    }
}
