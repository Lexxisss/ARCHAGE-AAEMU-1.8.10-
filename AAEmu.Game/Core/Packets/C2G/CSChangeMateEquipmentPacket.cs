using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeMateEquipmentPacket : GamePacket
{
    public CSChangeMateEquipmentPacket() : base(CSOffsets.CSChangeMateEquipmentPacket, 5)
    {
    }

    /// <summary>
    /// Request to change a pet's or mount's equipment.
    /// </summary>
    /// <remarks>
    /// Header: <c>ownerPersistentId:i64, tl:u16, mateType:u32, bts:bool, num:u8</c>. The owner
    /// id is 64 bits, not 32 - reading it short pulled the handle and everything after it off
    /// the wrong bytes. The client clamps the record count to two here, one fewer than for a
    /// ship.
    /// </remarks>
    public override void Read(PacketStream stream)
    {
        var characterId = stream.ReadInt64();
        var tl = stream.ReadUInt16(); // mate tl
        var passengerId = stream.ReadUInt32(); // mateType, generic label in the client
        var bts = stream.ReadBoolean();
        var num = stream.ReadByte();

        Logger.Debug($"ChangeMateEquipment, TlId: {tl}, Id: {characterId}, Id2: {passengerId}, BTS: {bts}, num: {num}");

        var mate = MateManager.Instance.GetActiveMateByTlId(Connection.ActiveChar.ObjId, tl);
        if (mate == null)
        {
            Logger.Warn($"ChangeMateEquipment, Unable to find mate with tlId {tl}!");
            return;
        }
        if (num == 0)
            return;
        //                  SlotType, Slot, Item
        var invItems = new (SlotType, byte, Item)[num];
        var equipItems = new (SlotType, byte, Item)[num];
        var character = Connection.ActiveChar;

        for (var i = 0; i < num; i++)
        {
            invItems[i].Item3 = new EquipItem();
            invItems[i].Item3.Read(stream);

            equipItems[i].Item3 = new EquipItem();
            equipItems[i].Item3.Read(stream);

            invItems[i].Item1 = (SlotType)stream.ReadByte();
            invItems[i].Item2 = stream.ReadByte();

            equipItems[i].Item1 = (SlotType)stream.ReadByte();
            equipItems[i].Item2 = stream.ReadByte();

            var isEquip = invItems[i].Item3.TemplateId != 0;

            // The two item records as the client sent them, kept before the server replaces them
            // with what it actually holds. They are the only statement of which of the pair the
            // client treats as the earlier state, so they are worth reading rather than dropping.
            var requestedFirst = invItems[i].Item3.TemplateId;
            var requestedSecond = equipItems[i].Item3.TemplateId;

            invItems[i].Item3 = (EquipItem)character.Inventory.Bag.GetItemBySlot(invItems[i].Item2);
            equipItems[i].Item3 = (EquipItem)mate.Equipment.GetItemBySlot(equipItems[i].Item2);

            // What the mate slot holds before anything moves, for the case where gear replaces
            // gear and the old piece has to be announced wherever it ends up.
            var slotBefore = equipItems[i].Item3;

            Logger.Debug($"FROM: ({invItems[i].Item1}:{invItems[i].Item2}) TO ({equipItems[i].Item1}:{equipItems[i].Item2}) ITEMS: {invItems[i].Item3?.Id}, {equipItems[i].Item3?.Id}, EQUIP: {isEquip}");
            Logger.Debug($"ChangeMateEquipment request records: first=tpl {requestedFirst}, second=tpl {requestedSecond}");

            // Gear moving on or off a mate empties one slot and fills another, and both halves
            // have to be said. Emptying is action 8 - the only one that unlinks the item and
            // destroys the client's object - and filling is action 6, carrying the full record.
            // A single move task is the wrong shape here: it resolves both slot objects first and
            // gives up if either is missing, which an empty mate slot cannot promise.
            //
            // Only the first half used to be sent, so the saddle left the bag and arrived nowhere,
            // and taking it off said nothing at all so the bag never got it back.
            //
            // The container kind is taken from the request rather than from the item, because the
            // client keeps a separate virtual container per mate family - one for ride, another
            // for battle - while everything here lives in a single container.
            if (isEquip)
            {
                if (invItems[i].Item3 != null)
                {
                    var movedItem = invItems[i].Item3;

                    if (character.Inventory.SplitOrMoveItemEx(ItemTaskType.Invalid, character.Inventory.Bag, mate.Equipment, invItems[i].Item3.Id, invItems[i].Item1, invItems[i].Item2, 0, equipItems[i].Item1, equipItems[i].Item2))
                    {
                        SendEquipmentChanged(equipItems[i], slotBefore, mate.Equipment.GetItemBySlot(equipItems[i].Item2),
                            tl, characterId, passengerId, bts);

                        var tasks = new List<ItemTask>
                        {
                            new ItemRemove(movedItem, invItems[i].Item1, invItems[i].Item2),
                            new ItemGain(movedItem, equipItems[i].Item1, equipItems[i].Item2)
                        };

                        // Gear replacing gear: whatever was in the slot has been pushed somewhere
                        // else, and it carries its new home itself.
                        if (slotBefore != null)
                            tasks.Add(new ItemGain(slotBefore));

                        Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SwapItems, tasks, []));
                    }
                }
            }
            else
            {
                if (equipItems[i].Item3 != null)
                {
                    var movedItem = equipItems[i].Item3;

                    if (character.Inventory.SplitOrMoveItemEx(ItemTaskType.Invalid, mate.Equipment, character.Inventory.Bag, equipItems[i].Item3.Id, equipItems[i].Item1, equipItems[i].Item2, 0, invItems[i].Item1, invItems[i].Item2))
                    {
                        SendEquipmentChanged(equipItems[i], slotBefore, mate.Equipment.GetItemBySlot(equipItems[i].Item2),
                            tl, characterId, passengerId, bts);

                        Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SwapItems,
                            [
                                new ItemRemove(movedItem, equipItems[i].Item1, equipItems[i].Item2),
                                new ItemGain(movedItem, invItems[i].Item1, invItems[i].Item2)
                            ],
                            []));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Confirms one equipment change to the client that asked for it.
    /// </summary>
    /// <remarks>
    /// The request and the reply share a structure but not a subject. The request is a move:
    /// take this out of that bag slot and put it in that mate slot. The reply describes the mate
    /// slot alone - as it was and as it is now - which is why the reverse notes name the two item
    /// records before and after.
    ///
    /// So both container bytes name the mate's container and both indexes its slot. The bag was
    /// being named instead, copied straight out of the request, and the accepted container kinds
    /// for this packet are only the two mate ones: an unsupported kind never reaches the apply
    /// path, and what the client is left showing is exactly what it showed before.
    /// </remarks>
    private void SendEquipmentChanged(
        (SlotType type, byte slot, Item item) mateSlot,
        Item before,
        Item after,
        ushort tl,
        long characterId,
        uint passengerId,
        bool bts)
    {
        Logger.Debug($"ChangeMateEquipment reply: ({mateSlot.type}:{mateSlot.slot}) " +
                     $"before=tpl {before?.TemplateId ?? 0}, after=tpl {after?.TemplateId ?? 0}");

        Connection.SendPacket(new SCMateEquipmentChangedPacket(
            (mateSlot.type, mateSlot.slot, before),
            (mateSlot.type, mateSlot.slot, after),
            tl, characterId, passengerId, bts));
    }
}
