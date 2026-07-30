using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class CharacterLobbyTail1810Tests
{
    [Fact]
    public void CompiledTargetCharacterTailHasNoFaceCompensationBytes()
    {
        var character = new Character(new UnitCustomModelParams());
        var stream = new PacketStream();
        var method = typeof(Character).GetMethod(
            "WriteCharacter1810Tail",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method.Invoke(character, new object[] { stream });

        Assert.Equal(190, stream.Count);
    }
}
