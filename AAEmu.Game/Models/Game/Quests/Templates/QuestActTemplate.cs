using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public abstract class QuestActTemplate
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public uint Id { get; set; }

    public void Start()
    {
        Logger.Info("Акт начат.");
    }
    public void Complete()
    {
        Logger.Info("Акт завершен.");
    }
    public virtual bool IsCompleted()
    {
        return false;
    }
    public virtual int GetCount()
    {
        Logger.Info("Получим, информацию на сколько выполнено задание.");
        return 0;
    }

    /// <summary>
    /// Points this act contributes for each thing done, on a quest that is scored.
    /// </summary>
    /// <remarks>
    /// Zero for acts that take no part in a score. The objective acts return their Count, which
    /// on a scored quest is a percentage per unit rather than a number of things to do.
    /// </remarks>
    public virtual int ScorePerUnit => 0;
    public virtual void Update()
    {
        Logger.Info("Акт обновлен.");
    }
    public virtual void ClearStatus()
    {
        Logger.Info("Сбросили статус в ноль.");
    }

    public abstract bool Use(ICharacter character, Quest quest, int objective);

}
