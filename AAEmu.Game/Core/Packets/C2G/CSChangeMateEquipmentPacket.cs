using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
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

        // The reply is one set carrying every record of the request, the same way the slave one
        // does. It used to be one message per record with the count written as a constant one,
        // which is only ever right for a request that carried a single change.
        var reply = new MateEquipment
        {
            OwnerPersistentId = characterId,
            Tl = tl,
            MateType = passengerId,
            Bts = bts
        };

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
                        AddChange(reply, invItems[i], equipItems[i]);

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
                        AddChange(reply, invItems[i], equipItems[i]);

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

        if (reply.Changes.Count > 0)
            Connection.SendPacket(new SCMateEquipmentChangedPacket(reply, true));
    }

    /// <summary>
    /// Records one applied change in the reply set.
    /// </summary>
    /// <remarks>
    /// A record is a swap seen from before it happened: the first item is what the source slot
    /// held, the second is what the destination slot held, and the two slot keys say where. On
    /// success the client puts the first item into the destination and the second one back into
    /// the source, which is what makes both an equip and an unequip fall out of the same shape.
    ///
    /// So the pair is not one slot before and after. Naming the mate's own slot on both sides
    /// made the client swap a slot with itself and crash on unequip, because the mate branch -
    /// unlike the slave one - does not check the source lookup for null before copying out of it.
    /// The slot keys are echoed as the client sent them; the source lookup goes through the
    /// generic inventory, so a bag on that side is expected.
    /// </remarks>
    private static void AddChange(MateEquipment reply,
        (SlotType type, byte slot, Item item) source,
        (SlotType type, byte slot, Item item) dest)
    {
        Logger.Debug($"ChangeMateEquipment reply record: ({source.type}:{source.slot})=tpl " +
                     $"{source.item?.TemplateId ?? 0} <-> ({dest.type}:{dest.slot})=tpl " +
                     $"{dest.item?.TemplateId ?? 0}");

        reply.Changes.Add(new MateEquipmentDelta
        {
            Before = source.item,
            After = dest.item,
            SourceType = source.type,
            SourceIndex = source.slot,
            DestType = dest.type,
            DestIndex = dest.slot,
            ExpireTime = 0
        });
    }
}
