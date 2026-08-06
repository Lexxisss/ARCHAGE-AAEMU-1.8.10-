using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Logging;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class MoveLog : ICommand
{
    public void OnLoad()
    {
        CommandManager.Instance.Register("movelog", this);
    }

    public string GetCommandLineHelp()
    {
        return "<on||off>";
    }

    public string GetCommandHelpText()
    {
        return "Record every movement body the server sends to Logs/MoveDebug. Off by default: one walking NPC is ten lines a second";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            character.SendMessage(
                "[MoveLog] recording is " + (MovementDebugLogger.Enabled ? "on" : "off") +
                ", " + CommandManager.CommandPrefix + "movelog <on||off>");
            return;
        }

        var value = args[0].ToLowerInvariant() switch
        {
            "on" or "true" or "1" => true,
            "off" or "false" or "0" => false,
            _ => (bool?)null
        };

        if (value == null)
        {
            character.SendMessage("[MoveLog] " + CommandManager.CommandPrefix + "movelog <on||off>");
            return;
        }

        MovementDebugLogger.Enabled = value.Value;
        character.SendMessage(value.Value
            ? "[MoveLog] recording movement bodies to Logs/MoveDebug"
            : "[MoveLog] recording stopped");
    }
}
