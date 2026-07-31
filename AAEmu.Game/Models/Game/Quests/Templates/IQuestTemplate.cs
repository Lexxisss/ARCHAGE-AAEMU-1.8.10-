using System.Collections.Generic;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public interface IQuestTemplate
{
    uint Id { get; set; }
    bool LetItDone { get; set; }
    byte Level { get; set; }
    byte MinLevel { get; set; }
    byte MaxLevel { get; set; }
    byte RaceMask { get; set; }
    string Name { get; set; }
    bool Repeatable { get; set; }
    bool RestartOnFail { get; set; }
    bool Selective { get; set; }
    int Score { get; set; }
    bool Successive { get; set; }
    IDictionary<uint, QuestComponent> Components { get; set; }
    QuestComponent GetFirstComponent(QuestComponentKind step);
    QuestComponent[] GetComponents(QuestComponentKind step);
}
