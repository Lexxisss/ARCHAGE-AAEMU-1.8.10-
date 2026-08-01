namespace AAEmu.Game.Models.Game.DoodadObj.Static;

/// <summary>
/// The relationship reason byte carried by the mount, unmount and attachment messages.
/// </summary>
/// <remarks>
/// The client picks 1 to 4 when mounting, from the player's direction relative to the mount
/// and which seats are already taken. The four names below are our own reading of that
/// geometry; the client's model axes are not labelled, so treat them as convenient labels
/// rather than proven directions.
///
/// Values 9 to 11 are proven from the client's own call sites and are dismount reasons, not
/// the unrelated meanings the names at those numbers suggest. Those names are kept because
/// existing code uses one of them, and the dismount meanings are added alongside as aliases.
///
/// Neither the attach nor the detach handler validates this byte: any value reaches the
/// relationship callbacks once the message has parsed.
/// </remarks>
public enum AttachUnitReason : byte
{
    None = 0,
    MountMateLeft = 1,
    MountMateRight = 2,
    MountMateBack = 3,
    MountMateForward = 4,
    SlaveBinding = 5,
    NewMaster = 6,
    BoardTransfer = 7,
    FoundMissedParent = 8,
    InitFromNub = 9,
    PrefabChanged = 10,
    TransferBinding = 11,
    HousingSlaveBinding = 12,

    /// <summary>A player getting off a mount themselves.</summary>
    UnmountNormal = 9,

    /// <summary>The automatic dismount the client performs before dismissing a ridden mount.</summary>
    UnmountBeforeDismiss = 10,

    /// <summary>Throwing a passenger off; the client walks passenger seats to find them.</summary>
    KickPassenger = 11
}
