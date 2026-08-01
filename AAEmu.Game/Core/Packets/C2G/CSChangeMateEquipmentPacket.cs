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

            // What the mate slot holds before anything moves. The reply has to describe this slot
            // as it was and as it became; sending the bag item and the mate item instead told the
            // client the change ran the other way, so the slot never redrew.
            var slotBefore = equipItems[i].Item3;

            Logger.Debug($"FROM: ({invItems[i].Item1}:{invItems[i].Item2}) TO ({equipItems[i].Item1}:{equipItems[i].Item2}) ITEMS: {invItems[i].Item3?.Id}, {equipItems[i].Item3?.Id}, EQUIP: {isEquip}");
            Logger.Debug($"ChangeMateEquipment request records: first=tpl {requestedFirst}, second=tpl {requestedSecond}");

            // Gear moving on or off a mate is a move between two slots, and the client is told so.
            // It used to be announced as a destruction: the item was handed to the mate and then,
            // in the very next message, the client was asked to drop that same object from its
            // slot and id registries. Nothing referring to it could survive that, which is why the
            // saddle only ever showed up on the next summon, when the whole mate was described
            // again. Taking gear off announced nothing at all, so the bag never got it back.
            if (isEquip)
            {
                if (invItems[i].Item3 != null)
                {
                    var movedItemId = invItems[i].Item3.Id;

                    if (character.Inventory.SplitOrMoveItemEx(ItemTaskType.Invalid, character.Inventory.Bag, mate.Equipment, invItems[i].Item3.Id, invItems[i].Item1, invItems[i].Item2, 0, equipItems[i].Item1, equipItems[i].Item2))
                    {
                        SendEquipmentChanged(invItems[i], equipItems[i], mate, slotBefore, tl, characterId, passengerId, bts);
                        Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SwapItems,
                            [new ItemMove(
                                invItems[i].Item1, invItems[i].Item2, movedItemId,
                                equipItems[i].Item1, equipItems[i].Item2, slotBefore?.Id ?? 0)],
                            []));
                    }
                }
            }
            else
            {
                if (equipItems[i].Item3 != null)
                {
                    var movedItemId = equipItems[i].Item3.Id;
                    var bagSlotBefore = invItems[i].Item3;

                    if (character.Inventory.SplitOrMoveItemEx(ItemTaskType.Invalid, mate.Equipment, character.Inventory.Bag, equipItems[i].Item3.Id, equipItems[i].Item1, equipItems[i].Item2, 0, invItems[i].Item1, invItems[i].Item2))
                    {
                        SendEquipmentChanged(invItems[i], equipItems[i], mate, slotBefore, tl, characterId, passengerId, bts);
                        Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SwapItems,
                            [new ItemMove(
                                equipItems[i].Item1, equipItems[i].Item2, movedItemId,
                                invItems[i].Item1, invItems[i].Item2, bagSlotBefore?.Id ?? 0)],
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
    /// The reply carries the mate slot twice - as it was and as it is now - followed by the
    /// source and destination the client named. The two are independent: the four type/index
    /// bytes are echoed back untouched, while the item records describe only the mate slot.
    ///
    /// Both records used to come straight off the request handling: the bag item first and the
    /// mate item second, whichever direction the change ran. Equipping therefore announced a
    /// slot that went from full to empty, and unequipping one that went from empty to full -
    /// both backwards, which is why the slot redrew in neither case.
    /// </remarks>
    private void SendEquipmentChanged(
        (SlotType type, byte slot, Item item) source,
        (SlotType type, byte slot, Item item) dest,
        Models.Game.Units.Mate mate,
        Item slotBefore,
        ushort tl,
        long characterId,
        uint passengerId,
        bool bts)
    {
        var slotAfter = mate.Equipment.GetItemBySlot(dest.slot);

        Logger.Debug($"ChangeMateEquipment reply: slot {dest.slot}, before=tpl {slotBefore?.TemplateId ?? 0}, after=tpl {slotAfter?.TemplateId ?? 0}");

        Connection.SendPacket(new SCMateEquipmentChangedPacket(
            (source.type, source.slot, slotBefore),
            (dest.type, dest.slot, slotAfter),
            tl, characterId, passengerId, bts));
    }
}
