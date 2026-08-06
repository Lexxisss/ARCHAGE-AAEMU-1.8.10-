using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Housing;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Housing;

public sealed class House : Unit
{
    public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Housing;
    private readonly object _lock = new();
    private readonly Dictionary<uint, int> _statePublicationVersions = new();
    private HousingTemplate _template;
    private int _currentStep;
    private int _allAction;
    private uint _id;
    private ulong _accountId;
    private int _ht;
    private uint _coOwnerId;
    private uint _templateId;
    private int _baseAction;
    private bool _isDirty;
    private HousingPermission _permission;
    private int _numAction;
    private DateTime _placeDate;
    private DateTime _protectionEndDate;
    private bool _allowRecover;
    private uint _sellToPlayerId;
    private uint _sellPrice;
    private int _expandedDecoLimit;
    private bool _isPublic;

    /// <summary>
    /// IsDirty flag for Houses, not all properties are taken into account here as most of the data that needs to be updated will never change
    /// after it's initial addition to the table, like position/rotation. Therefore it's ok to only set the dirty marker on the other properties
    /// </summary>
    public bool IsDirty { get => _isDirty; set => _isDirty = value; }
    public new uint Id { get => _id; set { _id = value; _isDirty = true; } }
    public ulong AccountId { get => _accountId; set { _accountId = value; _isDirty = true; } }

    /// <summary>
    /// The <c>ht</c> value the client sent with its placement request. The client always sends
    /// zero here - the field is initialised to zero and no sender path assigns it - so this is
    /// not the building's housing type and must not be echoed back in the state block. What the
    /// client expects to receive is <see cref="HousingType"/>.
    /// </summary>
    public int Ht { get => _ht; set { _ht = value; _isDirty = true; } }

    /// <summary>
    /// The housing type the client classifies buildings by. Its accessor reads this from the
    /// building's own definition, and the classifiers compare it against category numbers, so it
    /// comes from the design and never from the request that placed the building.
    /// </summary>
    public int HousingType => (int)(_template?.CategoryId ?? 0);

    /// <summary>
    /// Who the client resolves as this building's owner. An ownerless building announces itself
    /// with <see cref="PublicOwnerIdentity"/>, which makes the client print its own "public owner"
    /// text; anything else is taken as the identity itself.
    /// </summary>
    /// <remarks>
    /// The client only treats values of 1000 and above as identities without further
    /// interpretation; below that is its sentinel space, of which only 600 and 601 have a
    /// recovered meaning - publicly owned, and look the owner up through the linked scene unit.
    /// What it does with the rest of that space was not recovered.
    ///
    /// This matters here: character ids start at one (see CharacterIdManager), so every owner on
    /// a fresh server lands in that unrecovered range. If a building still shows no owner after
    /// its state block reads correctly, this is the next thing to look at - and the answer is to
    /// start handing out character ids above the threshold, not to add an offset here, because
    /// the client also compares this value against ids it already knows.
    /// </remarks>
    public long OwnerIdentity => OwnerId == 0 ? PublicOwnerIdentity : OwnerId;
    public uint CoOwnerId { get => _coOwnerId; set { _coOwnerId = value; _isDirty = true; } }
    public int ExpandedDecoLimit { get => _expandedDecoLimit; set { _expandedDecoLimit = value; _isDirty = true; } }
    public bool IsPublic { get => _isPublic; set { _isPublic = value; _isDirty = true; } }

    /// <summary>Part of the state block; we have nothing driving it yet.</summary>
    public bool IsBoundButler { get; set; }
    public new uint TemplateId { get => _templateId; set { _templateId = value; _isDirty = true; } }
    public HousingTemplate Template
    {
        get => _template;
        set
        {
            _template = value;
            _allAction = _template.BuildSteps.Values.Sum(step => step.NumActions);
        }
    }
    public List<Doodad> AttachedDoodads { get; set; }
    public int AllAction { get => _allAction; set { _allAction = value; _isDirty = true; } }
    private int BaseAction { get => _baseAction; set { _baseAction = value; _isDirty = true; } }
    public int CurrentAction => BaseAction + NumAction;
    public int NumAction { get => _numAction; set { _numAction = value; _isDirty = true; } }
    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            _currentStep = value;
            _isDirty = true;

            // The model shown is the one the design gives for this stage, and the finished model
            // once there is no stage left. A stage the design has no row for used to throw here
            // and take the whole placement with it; say so and stand the finished building up
            // instead, because that is at least visible and tells us the design data is short.
            if (_currentStep == -1)
                ModelId = Template.MainModelId;
            else if (Template.BuildSteps.TryGetValue(_currentStep, out var stepRow))
                ModelId = stepRow.ModelId;
            else
            {
                Logger.Warn($"House {Id} design {TemplateId} has no build step {_currentStep}, using the finished model");
                ModelId = Template.MainModelId;
            }

            // A finished building has doors and windows, but they are not made here. They are made
            // once its record exists at the other end - see EnsureAttachedDoodads.
            if (_currentStep != -1 && AttachedDoodads.Count > 0)
            {
                foreach (var doodad in AttachedDoodads)
                    if (doodad.ObjId > 0)
                        ObjectIdManager.Instance.ReleaseId(doodad.ObjId);

                AttachedDoodads.Clear();
            }

            if (_currentStep > 0)
            {
                BaseAction = 0;
                foreach (var step in Template.BuildSteps.Values)
                    if (step.Step < _currentStep)
                        BaseAction += step.NumActions;
            }
        }
    }
    public override int MaxHp => Template.Hp;
    public override UnitCustomModelParams ModelParams { get; set; }

    public HousingPermission Permission
    {
        get => _permission;
        set { _permission = ((_template != null) && (_template.AlwaysPublic)) ? HousingPermission.Public : value; _isDirty = true; }
    }

    public DateTime PlaceDate { get => _placeDate; set { _placeDate = value; _isDirty = true; } }
    public DateTime ProtectionEndDate { get => _protectionEndDate; set { _protectionEndDate = value; _isDirty = true; } }
    public DateTime TaxDueDate { get => _protectionEndDate.AddDays(-7); }
    public uint SellToPlayerId { get => _sellToPlayerId; set { _sellToPlayerId = value; _isDirty = true; } }
    public uint SellPrice { get => _sellPrice; set { _sellPrice = value; _isDirty = true; } }
    public bool AllowRecover { get => _allowRecover; set { _allowRecover = value; _isDirty = true; } }

    // House always gets its guild from its owner
    public override Expedition Expedition
    {
        get
        {
            var guildId = ExpeditionManager.Instance.GetExpeditionOfCharacter(OwnerId);
            if (guildId == 0)
                return null;
            return ExpeditionManager.Instance.GetExpedition(guildId);
        }
        set
        {
            // Ignored, we always get the guild from its owner
        }
    }

    public House()
    {
        Level = 1;
        ModelParams = new UnitCustomModelParams();
        AttachedDoodads = new List<Doodad>();
        IsDirty = true;
        Events.OnDeath += OnDeath;
    }

    /// <summary>
    /// The contribution skill the design asks for right now, or zero for a building that is
    /// finished or whose design has no stages.
    /// </summary>
    /// <remarks>
    /// Stages partition the total number of actions between them: each says how many actions it
    /// takes, and the stage being worked on is the first whose running total passes the actions
    /// already done. That stage's skill is the only one the contribution path accepts, so it is
    /// the only one worth offering the player.
    /// </remarks>
    public uint ActiveBuildSkillId
    {
        get
        {
            if (CurrentStep == -1 || Template == null)
                return 0;

            var cumulative = 0;
            foreach (var step in Template.BuildSteps.Values.OrderBy(s => s.Step))
            {
                cumulative += step.NumActions;
                if (CurrentAction < cumulative)
                    return step.SkillId;
            }

            return 0;
        }
    }

    public void AddBuildAction()
    {
        if (CurrentStep == -1)
            return;

        lock (_lock)
        {
            // Stages are looked up by the ordinal the design numbers them with, not by position
            // in the table: a design whose stages do not start at zero used to lose its last one
            // and finish the building a stage early.
            var actionsInStep = Template.BuildSteps.TryGetValue(CurrentStep, out var stepRow) ? stepRow.NumActions : 0;
            var nextAction = NumAction + 1;
            if (actionsInStep > nextAction)
                NumAction = nextAction;
            else
            {
                NumAction = 0;
                var nextStep = CurrentStep + 1;
                CurrentStep = Template.BuildSteps.ContainsKey(nextStep) ? nextStep : -1;
            }
        }
    }

    #region Visible
    public override void Spawn()
    {
        base.Spawn();
        foreach (var doodad in AttachedDoodads)
            doodad.SpawnForBatch();
    }

    public override void Delete()
    {
        // Detach children that aren't part of the house itself
        foreach (var doodad in AttachedDoodads)
            if (doodad.AttachPoint == AttachPointKind.None)
                doodad.Transform.Parent = null;
        base.Delete();
    }

    public override void Show()
    {
        base.Show();
        foreach (var doodad in AttachedDoodads)
            doodad.Show();
    }

    public override void Hide()
    {
        foreach (var doodad in AttachedDoodads)
            doodad.Hide();
        base.Hide();
    }

    /// <summary>
    /// How long the client is given to register the building before it is told about it.
    /// </summary>
    /// <remarks>
    /// A pause is not the barrier this wants. What has to be true when the state and the fixtures
    /// arrive is that the building is registered, its record built, and the fixtures' parent
    /// resolvable - and no message from the client says when that happened; there is nothing to
    /// wait on. So this is a guess at how long it takes, and half a second is a generous one where
    /// two hundred milliseconds was barely a round trip and lost the race whenever the client was
    /// busy loading scenery.
    ///
    /// The second way in is <see cref="HousingManager.HouseTaxInfo"/>: a player asking about a
    /// building's tax has one, which is the closest thing to proof the protocol offers.
    /// </remarks>
    private static readonly TimeSpan StateFollowUpDelay = TimeSpan.FromMilliseconds(500);

    public override void AddVisibleObject(Character character)
    {
        // The building first, on its own. Its state and its doors are both looked up against what
        // this message registers, and the client neither waits for that nor says anything when it
        // has not happened yet - it drops the state whole, owner and all, and hangs the doors on
        // nothing. See HouseStateFollowUpTask.
        character.SendPacket(new SCUnitStatePacket(this));
        ScheduleStateFollowUp(character);

        base.AddVisibleObject(character);
    }

    private void ScheduleStateFollowUp(Character character)
    {
        int version;
        lock (_lock)
        {
            _statePublicationVersions.TryGetValue(character.ObjId, out version);
            version++;
            _statePublicationVersions[character.ObjId] = version;
        }

        TaskManager.Instance.Schedule(
            new HouseStateFollowUpTask(this, character, version),
            StateFollowUpDelay);
    }

    /// <summary>
    /// Re-sends the state to every viewer through the same ordered two-stage path used on spawn.
    /// Repeated build actions are coalesced per viewer, so only the newest state may publish fixtures.
    /// </summary>
    public void ScheduleStateFollowUpForVisibleCharacters()
    {
        foreach (var character in WorldManager.GetAround<Character>(this))
            ScheduleStateFollowUp(character);
    }

    public bool IsStatePublicationCurrent(uint characterObjId, int version)
    {
        lock (_lock)
            return _statePublicationVersions.TryGetValue(characterObjId, out var current) && current == version;
    }

    public void CompleteStatePublication(uint characterObjId, int version)
    {
        lock (_lock)
        {
            if (_statePublicationVersions.TryGetValue(characterObjId, out var current) && current == version)
                _statePublicationVersions.Remove(characterObjId);
        }
    }

    // Built-in fixtures are registered silently and then published by
    // HouseFixturesFollowUpTask. This avoids both duplicate 0x017A records and the race where a
    // child arrives before the client has created the house-model it must attach to.

    /// <summary>
    /// Makes this building's doors, windows and chests, if it is finished and has none yet.
    /// </summary>
    /// <remarks>
    /// The other half, and the reason a building raised in one go still came out dead after the
    /// double delivery was fixed. A fixture is fitted to the building's own record at the far end,
    /// and a fixture that arrives before that record exists is never fitted afterwards - it keeps
    /// what it was born with, which is nothing.
    ///
    /// Made where the stage was set, they were born during placement: after the building itself,
    /// but half a second ahead of the message that builds its record. A building raised stage by
    /// stage escaped that only by accident - it has no fixtures to make until the last stage is
    /// done, by which time the record has long existed - which is why the two behaved differently
    /// on identical code.
    ///
    /// So they are made here instead, once, straight after the record has been sent.
    /// </remarks>
    public void EnsureAttachedDoodads()
    {
        lock (_lock)
        {
            if (CurrentStep != -1 || AttachedDoodads.Count > 0 || Template?.HousingBindingDoodad == null)
                return;

            foreach (var bindingDoodad in Template.HousingBindingDoodad)
            {
                var doodad = DoodadManager.Instance.Create(0, bindingDoodad.DoodadId, this, true);
                if (doodad == null)
                {
                    Logger.Error("House {0}: cannot create binding doodad template={1}, attachPoint={2}",
                        Id, bindingDoodad.DoodadId, bindingDoodad.AttachPointId);
                    continue;
                }

                doodad.AttachPoint = bindingDoodad.AttachPointId;
                doodad.ParentObj = this;
                doodad.ParentObjId = ObjId;
                doodad.OwnerId = OwnerId;
                doodad.OwnerDbId = Id;
                doodad.OwnerType = DoodadOwnerType.Housing;
                doodad.Transform = Transform.CloneDetached(doodad);
                doodad.Transform.Parent = Transform;
                doodad.Transform.Local.ApplyWorldSpawnPositionWithDeg(bindingDoodad.Position);
                doodad.InitDoodad();

                AttachedDoodads.Add(doodad);

                // Register in WorldManager now, but do not emit 0x017A. The fixture task sends all
                // records with 0x0198 after SCHouseState has had its own client processing turn.
                doodad.SpawnForBatch();

                Logger.Info(
                    "House fixture ready: house={0}, objId={1}, template={2}, funcGroup={3}, itemTemplate={4}, parent={5}, attach={6}, local=({7:F4},{8:F4},{9:F4}), rotDeg=({10:F2},{11:F2},{12:F2})",
                    Id, doodad.ObjId, doodad.TemplateId, doodad.FuncGroupId, doodad.ItemTemplateId,
                    doodad.ParentObjId, doodad.AttachPoint,
                    doodad.Transform.Local.Position.X, doodad.Transform.Local.Position.Y,
                    doodad.Transform.Local.Position.Z,
                    bindingDoodad.Position.Roll, bindingDoodad.Position.Pitch, bindingDoodad.Position.Yaw);
            }

            Logger.Info("House {0}: created {1} built-in fixtures for delayed batch publication",
                Id, AttachedDoodads.Count);
        }
    }

    /// <summary>Sends this house's built-in fixtures to one viewer in target-sized batches.</summary>
    public void SendAttachedDoodads(Character character)
    {
        if (character == null || AttachedDoodads.Count == 0)
            return;

        var doodads = AttachedDoodads.Where(d => d != null && d.ObjId > 0).ToArray();
        for (var offset = 0; offset < doodads.Length; offset += SCDoodadsCreatedPacket.MaxCountPerPacket)
        {
            var count = Math.Min(SCDoodadsCreatedPacket.MaxCountPerPacket, doodads.Length - offset);
            var batch = new Doodad[count];
            Array.Copy(doodads, offset, batch, 0, count);
            character.SendPacket(new SCDoodadsCreatedPacket(batch));
            Logger.Info("House {0}: sent fixture batch to {1}, offset={2}, count={3}",
                Id, character.Name, offset, count);
        }
    }

    /// <summary>
    /// Takes the building and everything fixed to it back off the player's screen.
    /// </summary>
    /// <remarks>
    /// The fixtures go first and the building after. Taking the building away first leaves each
    /// door and window a moment with no parent, and nothing here waits for anything - and the
    /// client will not accept a handle it still holds, so a door that was not properly let go of
    /// is a door that cannot come back. That is why they were missing after a relog: they were
    /// still in the client's hands from last time, and the second delivery was thrown away as a
    /// repeat before it could be hung on anything.
    /// </remarks>
    public override void RemoveVisibleObject(Character character)
    {
        var doodadIds = AttachedDoodads
            .Where(d => d != null && d.ObjId > 0)
            .Select(d => d.ObjId)
            .ToArray();

        if (character.CurrentTarget == this ||
            (character.CurrentTarget != null && doodadIds.Contains(character.CurrentTarget.ObjId)))
        {
            character.CurrentTarget = null;
            character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
        }

        // Children first. Calling base here would recursively emit one 0x013B per child before this
        // batch and then the same handles would be removed a second time.
        for (var offset = 0; offset < doodadIds.Length; offset += SCDoodadsRemovedPacket.MaxCountPerPacket)
        {
            var remaining = doodadIds.Length - offset;
            var last = remaining <= SCDoodadsRemovedPacket.MaxCountPerPacket;
            var temp = new uint[last ? remaining : SCDoodadsRemovedPacket.MaxCountPerPacket];
            Array.Copy(doodadIds, offset, temp, 0, temp.Length);
            character.SendPacket(new SCDoodadsRemovedPacket(last, temp));
        }

        character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));
    }

    #endregion

    public bool Save(MySqlConnection connection, MySqlTransaction transaction = null)
    {
        if (!IsDirty)
            return false;
        if ((AccountId <= 0) || (OwnerId <= 0))
            return false; // recently destroyed/expired house
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText =
                "REPLACE INTO `housings` " +
                "(`id`,`account_id`,`owner`,`co_owner`,`template_id`,`name`,`x`,`y`,`z`,`yaw`,`pitch`,`roll`,`current_step`,`current_action`,`permission`,`place_date`," +
                "`protected_until`,`faction_id`,`sell_to`,`sell_price`, `allow_recover`) " +
                "VALUES(@id,@account_id,@owner,@co_owner,@template_id,@name,@x,@y,@z,@yaw,@pitch,@roll,@current_step,@current_action,@permission,@placedate," +
                "@protecteduntil,@factionid,@sellto,@sellprice,@allowrecover)";

            command.Parameters.AddWithValue("@id", Id);
            command.Parameters.AddWithValue("@account_id", AccountId);
            command.Parameters.AddWithValue("@owner", OwnerId);
            command.Parameters.AddWithValue("@co_owner", CoOwnerId);
            command.Parameters.AddWithValue("@template_id", TemplateId);
            command.Parameters.AddWithValue("@name", Name);
            command.Parameters.AddWithValue("@x", Transform.World.Position.X);
            command.Parameters.AddWithValue("@y", Transform.World.Position.Y);
            command.Parameters.AddWithValue("@z", Transform.World.Position.Z);
            command.Parameters.AddWithValue("@roll", Transform.World.Rotation.X);
            command.Parameters.AddWithValue("@pitch", Transform.World.Rotation.Y);
            command.Parameters.AddWithValue("@yaw", Transform.World.Rotation.Z);
            command.Parameters.AddWithValue("@current_step", CurrentStep);
            command.Parameters.AddWithValue("@current_action", NumAction);
            command.Parameters.AddWithValue("@permission", (byte)Permission);
            command.Parameters.AddWithValue("@placedate", PlaceDate);
            command.Parameters.AddWithValue("@protecteduntil", ProtectionEndDate);
            command.Parameters.AddWithValue("@factionid", Faction.Id);
            command.Parameters.AddWithValue("@sellto", SellToPlayerId);
            command.Parameters.AddWithValue("@sellprice", SellPrice);
            command.Parameters.AddWithValue("@allowrecover", AllowRecover);
            command.Prepare();
            command.ExecuteNonQuery();
        }

        IsDirty = false;
        return true;
    }

    /// <summary>Number of UCC decoration slots the state block always carries.</summary>
    private const int UccSlotCount = HousingTemplate.UccSlotCount;

    /// <summary>
    /// Number of world positions the state block ends with. Nothing on the server has a value for
    /// them, so they go out empty - see <see cref="Write"/>.
    /// </summary>
    private const int TrailingPositionCount = 2;

    /// <summary>Owner identity meaning "nobody in particular"; the client names such a building publicly owned.</summary>
    private const long PublicOwnerIdentity = 600;

    /// <summary>
    /// The work done so far, as the client is told it: level with what the design asks for once the
    /// building is finished, so that the two compare equal and it stops calling it a building site.
    /// </summary>
    public int BuildProgressAction => CurrentStep == -1 ? AllAction : CurrentAction;

    /// <summary>
    /// Writes the house state block.
    /// </summary>
    /// <remarks>
    /// The handle leads at two bytes, the same width it has everywhere else in this subsystem.
    /// Sent as four it put the whole block two bytes out from its first field, which is the
    /// third place in this protocol where that same mistake has hidden a building.
    ///
    /// The variable-width block after the scene id is three values, not one. Only the first is
    /// something we can name; the other two go out empty rather than filled with a guess, which
    /// at least gets the shape right.
    ///
    /// The three id fields are 64 bits on the wire while the model holds them as 32. Writing
    /// them at their model width lost four bytes each.
    ///
    /// There is one 64-bit value straight after that block and one before the restricted buyer's
    /// name, not none and two. It was moved from the first place to the second while chasing the
    /// ownerless building, and that was the wrong direction: it left everything from the housing
    /// type to both owner identities eight bytes early, which is where the owner was being read
    /// from - out of the middle of two other fields.
    ///
    /// The housing type is the design's own category, not the <c>ht</c> the placement request
    /// came with; that one is always zero. Of the two 64-bit ids only the second is read.
    ///
    /// The block ends with two world positions. It was six plain 64-bit values here for a while,
    /// on a reading that took the serializer's x/y/z/x/y/z labelling for bookkeeping rather than
    /// for what it says. Nothing on the server has a value for either position, so both go out
    /// empty - which is the same bytes the reserved reading produced, only eight fewer of them.
    /// </remarks>
    /// <remarks>
    /// The audit this follows gives the trailing block as two positions of twenty bytes each and
    /// then calls the block forty-four bytes. Forty is what two of them come to. Forty is what
    /// goes out; if the building's state stops arriving, this is the first place to look.
    /// </remarks>
    public PacketStream Write(PacketStream stream)
    {
        var ownerName = NameManager.Instance.GetCharacterName(OwnerId);
        var sellToPlayerName = NameManager.Instance.GetCharacterName(SellToPlayerId);

        stream.Write(TlId);                    // tl                 : u16, the handler's own handle
        stream.Write(Id);                      // dbId               : u32
        stream.WriteBc(ObjId);                 // sceneObjectId      : 3 bytes, the handler's lookup key

        // The design, and how far along the building is: the work it asks for and the work done.
        // The client keeps the pair and calls the building unfinished while the two differ - that
        // one comparison is the whole of it. Both were going out empty, which made them equal, so
        // every foundation announced itself as a finished house no matter what the unit state drew,
        // and a finished house is offered nothing to do. It is the same pair the progress message
        // carries, and it has to agree with it.
        //
        // They were briefly the guild and the family, on a reading that named them from the server
        // side. Two audits since put both in the same two slots of the client's own house record
        // as the progress message writes its counts into, which settles it.
        stream.WritePisc(TemplateId, AllAction, BuildProgressAction); // housingDescId, allstep, curstep

        // The asking price belongs here, at the front, and who the sale is reserved for belongs
        // further down before the buyer's name. The two were the other way round: a price sat
        // where the reservation is read and a zero where the price is.
        stream.Write((long)SellPrice);         // salePrice          : i64
        stream.Write(HousingType);             // ht                 : i32, the design's own category
        stream.Write(0L);                      // ownerIdentityAux   : u64, kept but never consulted
        stream.Write(OwnerIdentity);           // ownerIdentity      : u64
        stream.Write(ownerName ?? "");         // ownerName          : string, max 128
        stream.Write(AccountId);               // accountId          : u64
        stream.Write((byte)Permission);        // permission         : u8, 0 owner / 1 expedition / 2 all / 3 family
        WriteWorldPosition(stream, Transform.World.Position);
        stream.Write(Name ?? "");              // houseName          : string, max 128
        stream.Write(AllowRecover);            // allowRecover       : bool
        stream.Write((long)SellToPlayerId);    // saleTargetIdentity : u64, who the sale is held for
        stream.Write(sellToPlayerName ?? "");  // sellToName         : string, max 128
        stream.Write(ExpandedDecoLimit);       // expandedDecoLimit  : i32
        stream.Write(0);                       // unnamed            : u32, serialized but never applied
        stream.Write(IsPublic);                // isPublic           : bool
        stream.Write(IsBoundButler);           // isBoundButler      : bool
        stream.Write(0u);                      // butlerId           : u32, only read when bound

        // The five decal slots. Each carries the place it goes on the building, and that number is
        // what the client sorts them by - the order they arrive in does not matter, but the number
        // does, and it counts from one: wall, floor, top, outer wall, roof. Zero means no place at
        // all, which is what every slot was claiming while these were numbered from zero and read
        // in whatever order the columns happened to be declared.
        //
        // What is applied to a slot is a separate identity, and it stays empty: there is nowhere on
        // the server to keep one yet.
        var uccKinds = Template?.UccKinds;
        for (var i = 0; i < UccSlotCount; i++)
        {
            stream.Write(Id);                      // houseId      : i32
            stream.Write(0L);                      // type         : i64, the applied decal
            stream.Write(uccKinds?[i] ?? 0);       // ucc_kind     : i32
            stream.Write(i + 1);                   // ucc_position : i32, 1..5
        }

        // The dedicate writer initializes both unknown anchor positions to zero. Their semantic
        // purpose is still unresolved, so do not feed the house world position into them.
        for (var i = 0; i < TrailingPositionCount; i++)
            WriteWorldPosition(stream, Vector3.Zero); // tailPosition0..1 : 20 bytes each

        return stream;
    }

    /// <summary>
    /// The 20-byte world position this subsystem uses: <c>i64 x, i64 y, f32 z</c>. It is not
    /// the ordinary three-float vector.
    /// </summary>
    private static void WriteWorldPosition(PacketStream stream, Vector3 position)
    {
        stream.Write(Helpers.ConvertLongX(position.X));
        stream.Write(Helpers.ConvertLongY(position.Y));
        stream.Write(position.Z);
    }

    public void OnDeath(object sender, EventArgs args)
    {
        Logger.Debug("House died ObjId:{0} - TemplateId:{1} - {2}", ObjId, TemplateId, Name);
        HousingManager.Instance.RemoveDeadHouse(this);
    }

    public override bool AllowedToInteract(Character player)
    {
        if (Template.AlwaysPublic)
            return base.AllowedToInteract(player);
        if (CurrentStep != -1) // unfinished houses can't be used to private store, so always true
            return base.AllowedToInteract(player);
        switch (Permission)
        {
            case HousingPermission.Private:
                if (player.Id == OwnerId)
                    return base.AllowedToInteract(player);
                var ownerAccount = NameManager.Instance.GetCharaterAccount(OwnerId);
                return (player.AccountId == ownerAccount) && base.AllowedToInteract(player);
            case HousingPermission.Family when (player.Family > 0):
                return FamilyManager.Instance.GetFamily(player.Family).Members.Any(x => x.Id == OwnerId);
            case HousingPermission.Guild when (player.Expedition?.Id > 0):
                return player.Expedition.Members.Any(x => x.CharacterId == OwnerId);
            case HousingPermission.Public:
            default:
                return base.AllowedToInteract(player);
        }
    }
}
