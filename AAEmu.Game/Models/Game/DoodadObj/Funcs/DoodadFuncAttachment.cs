using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncAttachment : DoodadFuncTemplate
{
    // doodad_funcs
    public AttachPointKind AttachPointId { get; set; }
    public int Space { get; set; }
    public BondKind BondKindId { get; set; }
    public uint AnimActionId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncAttachment");
        if (caster is Character character)
        {
            if (BondKindId > BondKind.BondInvalid)
            {
                var spot = owner.Seat.LoadPassenger(character, owner.ObjId, Space); // ask for a free meta number for landing
                if (spot == -1)
                {
                    return; // we leave if there is no place
                }

                // Chairs, beds etc.
                // spot = 0 sit left, = 1 sit right on the bench, spot = -1 нет свободного места
                // Space = 1-means that there is one place (a chair), Space = 2-means that there are two places to sit (a bench on transport)
                character.Bonding = new BondDoodad(owner, AttachPointId, BondKindId, Space, spot, AnimActionId);

                // v14 left Parent null. The new capture then showed the first attach with parent=0;
                // the client held a bond without completing the visible sit, the next click sent
                // detach, and only the following attach produced the seated state. Restore the
                // chair relation before SCAttachToDoodad so movement is handled as attached from
                // the first packet.
                //
                // Preserve the exact world transform while converting it to chair-local data. The
                // movement handler below performs the same conversion for later seated updates, so
                // detach can safely add the parent transform once instead of producing world+world.
                var worldBeforeAttach = character.Transform.World.Clone();
                character.Transform.StickyParent = owner.Transform.StickyParent;
                character.Transform.Parent = owner.Transform;
                character.Transform.Local.Rotation =
                    worldBeforeAttach.Rotation - owner.Transform.World.Rotation;
                character.Transform.ResetFinalizeTransform();

                character.BroadcastPacket(new SCAttachToDoodadPacket(caster.ObjId, character.Bonding), true);

                var world = character.Transform.World;
                Logger.Debug(
                    "Doodad attach: char={0}/{1}, doodad={2}, persistentToken={3}, parent={4}, sticky={5}, world=({6:F2},{7:F2},{8:F2})",
                    character.Id,
                    character.ObjId,
                    owner.ObjId,
                    character.Id,
                    character.Transform.Parent?.GameObject?.ObjId ?? 0,
                    character.Transform.StickyParent?.GameObject?.ObjId ?? 0,
                    world.Position.X,
                    world.Position.Y,
                    world.Position.Z);
            }
            // Ships // TODO Check how sit on the ship
            else
            {
                SlaveManager.Instance.BindSlave(character, owner.ParentObjId, AttachPointId, AttachUnitReason.BoardTransfer);
            }
        }
    }
}
