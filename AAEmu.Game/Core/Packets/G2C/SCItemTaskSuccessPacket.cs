using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 1.8.1.0 item-task delta packet (SC 0x010B).
///
/// Layout was recovered from x2game.dll serializer 0x399D06F0 and the nested
/// item-task serializers at 0x39A90F10 / 0x39A90C40. The leading byte is the
/// unit-owner type; the item task type belongs to the nested result object.
/// </summary>
public class SCItemTaskSuccessPacket : GamePacket
{
    private const int MaxTasks = 30;
    private const int MaxForceRemoves = 30;

    private readonly byte _unitOwnerType;
    private readonly ItemTaskType _taskType;
    private readonly List<ItemTask> _tasks;
    private readonly List<ulong> _forceRemove;
    private readonly ulong _type;
    private readonly int _lockItemSlotKey;
    private readonly bool _queryResult;
    private readonly ulong _flags;

    public SCItemTaskSuccessPacket(ItemTaskType taskType, List<ItemTask> tasks, List<ulong> forceRemove)
        : this(0, taskType, tasks, forceRemove, 0, 0, true, 0)
    {
    }

    public SCItemTaskSuccessPacket(ItemTaskType taskType, ItemTask task, List<ulong> forceRemove)
        : this(0, taskType, task == null ? [] : [task], forceRemove, 0, 0, true, 0)
    {
    }

    public SCItemTaskSuccessPacket(
        byte unitOwnerType,
        ItemTaskType taskType,
        List<ItemTask> tasks,
        List<ulong> forceRemove,
        ulong type,
        int lockItemSlotKey,
        bool queryResult,
        ulong flags)
        : base(SCOffsets.SCItemTaskSuccessPacket, 5)
    {
        _unitOwnerType = unitOwnerType;
        _taskType = taskType;
        _tasks = tasks ?? [];
        _forceRemove = forceRemove ?? [];
        _type = type;
        _lockItemSlotKey = lockItemSlotKey;
        _queryResult = queryResult;
        _flags = flags;
    }

    public override PacketStream Write(PacketStream stream)
    {
        if (_tasks.Count > MaxTasks)
            throw new InvalidOperationException($"SCItemTaskSuccessPacket supports at most {MaxTasks} tasks");
        if (_forceRemove.Count > MaxForceRemoves)
            throw new InvalidOperationException($"SCItemTaskSuccessPacket supports at most {MaxForceRemoves} force removes");

        stream.Write(_unitOwnerType);             // unitOwnerType : u8
        stream.Write((byte)_taskType);            // result.type   : u8

        stream.Write((byte)_tasks.Count);         // result.count  : u8, clamped to 30 by client
        foreach (var task in _tasks)
            stream.Write(task);

        // Trailing block: 22 bytes when no force removes are present.
        //
        // We previously padded this to 42, because four independent packet lengths all
        // resolved to that one constant. They did - but the extra 20 bytes belong to a
        // transport envelope around the captured record, not to this serializer, and the
        // same 20 bytes sit on all four:
        //   no tasks              45 = 25 + 20
        //   a 10-byte task        55 = 35 + 20
        //   an action 5           65 = 45 + 20
        //   action 5 + action 6  133 = 113 + 20
        // The client parses to its own structure rather than to a declared body length, so
        // the padding was harmless filler - not the reason anything began working.
        stream.Write((byte)_forceRemove.Count);   // forceRemoveCount : u8, max 30
        foreach (var remove in _forceRemove)
            stream.Write(remove);                 // forceRemoves[]   : u64

        stream.Write(_type);                      // type              : u64
        stream.Write(_lockItemSlotKey);           // lockItemSlotKey   : i32
        stream.Write(_queryResult);               // queryResult       : bool
        stream.Write(_flags);                     // 35 flag bits packed into u64

        return stream;
    }
}
