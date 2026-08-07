using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActEtcItemObtain : QuestActTemplate
{
    public uint ItemId { get; set; }
    public int Count { get; set; }
    public uint HighlightDooadId { get; set; }
    public bool Cleanup { get; set; }

    //public static int ItemObtainStatus { get; private set; } = 0;
    /// <summary>Percent this act adds for each thing done on a scored quest.</summary>
    public override int ScorePerUnit => Count;


    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Warn("QuestActEtcItemObtain");

        if (quest.Template.Score > 0) // Check if the quest use Template.Score or Count
        {
            quest.ItemObtainStatus = objective * Count; // Count в данном случае % за единицу
            // The quest adds up every objective itself. Summing a few of these per-type fields
            // here lost whatever a second component of the same type had written over.
            quest.RecalculateOverCompletion();
            if (quest.Template.LetItDone)
            {
                if (quest.OverCompletionPercent >= quest.Template.Score * 3 / 5)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > quest.Template.Score)
                    quest.ExtraCompletion = true;
            }

            return quest.OverCompletionPercent >= quest.Template.Score;
        }

        if (quest.Template.LetItDone)
        {
            quest.OverCompletionPercent = objective * 100 / Count;

            if (quest.OverCompletionPercent >= 60)
                quest.EarlyCompletion = true;

            if (quest.OverCompletionPercent > 100)
                quest.ExtraCompletion = true;
        }
        return objective >= Count;
    }
}
