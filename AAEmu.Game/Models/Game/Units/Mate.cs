using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Tasks.Mate;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units;

public class MatePassengerInfo
{
    public uint ObjId;
    public AttachUnitReason Reason;
}

public sealed class Mate : Unit
{
    public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Mate;

    public override void PopulateFormulaParameters(System.Collections.Generic.Dictionary<string, double> parameters, bool skipCoreStats = false)
    {
        base.PopulateFormulaParameters(parameters, skipCoreStats);
        if (!skipCoreStats)
        {
            parameters["str"] = Str;
            parameters["dex"] = Dex;
            parameters["sta"] = Sta;
            parameters["int"] = Int;
            parameters["spi"] = Spi;
            parameters["fai"] = Fai;
        }
    }
    //public ushort TlId { get; set; }
    //public uint TemplateId { get; set; } // moved to BaseUnit
    public NpcTemplate Template { get; set; }

    public uint OwnerObjId { get; set; }
    public Dictionary<AttachPointKind, MatePassengerInfo> Passengers { get; }

    public override float Scale => Template.Scale;

    // SpawnMate
    //public uint Id { get; set; } // moved to BaseUnit
    public ulong ItemId { get; set; }
    public byte UserState { get; set; }
    public int Experience { get; set; }
    public int Mileage { get; set; }
    public uint SpawnDelayTime { get; set; }
    public List<uint> Skills { get; set; }
    public MateDb DbInfo { get; set; }
    public Task MateXpUpdateTask { get; set; }

    /// <summary>The repeating step that walks an unridden mate back to its owner.</summary>
    public Task MateFollowTask { get; set; }

    public MateType MateType { get; set; }  // added in 3+

    /// <summary>True while somebody is in the driver's seat, so the rider is steering.</summary>
    public bool IsRidden =>
        Passengers.TryGetValue(AttachPointKind.Driver, out var driver) && driver.ObjId != 0;

    #region Attributes

    [UnitAttribute(UnitAttribute.Str)]
    public int Str
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Str);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            PopulateFormulaParameters(parameters, true);
            var result = formula.Evaluate(parameters);
            var res = (int)result;
            //foreach (var item in Inventory.Equip)
            //    if (item is EquipItem equip)
            //        res += equip.Str;
            foreach (var bonus in GetBonuses(UnitAttribute.Str))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Dex)]
    public int Dex
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Dex);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            PopulateFormulaParameters(parameters, true);
            var res = (int)formula.Evaluate(parameters);
            //foreach (var item in Inventory.Equip)
            //    if (item is EquipItem equip)
            //        res += equip.Dex;
            foreach (var bonus in GetBonuses(UnitAttribute.Dex))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Sta)]
    public int Sta
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Sta);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            PopulateFormulaParameters(parameters, true);
            var res = (int)formula.Evaluate(parameters);
            //foreach (var item in Inventory.Equip)
            //    if (item is EquipItem equip)
            //        res += equip.Sta;
            foreach (var bonus in GetBonuses(UnitAttribute.Sta))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Int)]
    public int Int
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Int);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            PopulateFormulaParameters(parameters, true);
            var res = (int)formula.Evaluate(parameters);
            //foreach (var item in Inventory.Equip)
            //    if (item is EquipItem equip)
            //        res += equip.Int;
            foreach (var bonus in GetBonuses(UnitAttribute.Int))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Spi)]
    public int Spi
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Spi);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            PopulateFormulaParameters(parameters, true);
            var res = (int)formula.Evaluate(parameters);
            //foreach (var item in Inventory.Equip)
            //    if (item is EquipItem equip)
            //        res += equip.Spi;
            foreach (var bonus in GetBonuses(UnitAttribute.Spi))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Fai)]
    public int Fai
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Fai);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            PopulateFormulaParameters(parameters, true);
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Fai))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.MaxHealth)]
    public override int MaxHp
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MaxHealth);
            var mateKindVariable = FormulaManager.Instance.GetUnitVariable(formula.Id,
                UnitFormulaVariableType.MateKind, (uint)Template.MateKindId);

            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["mate_kind"] = mateKindVariable
            };
            PopulateFormulaParameters(parameters);
            var res = (int)formula.Evaluate(parameters);

            res = (int)CalculateWithBonuses(res, UnitAttribute.MaxHealth);

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.HealthRegen)]
    public override int HpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.HealthRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["mate_kind"] = Template.MateKindId
            };
            PopulateFormulaParameters(parameters);
            var res = (int)formula.Evaluate(parameters);
            res += Spi / 10;
            foreach (var bonus in GetBonuses(UnitAttribute.HealthRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.PersistentHealthRegen)]
    public override int PersistentHpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.PersistentHealthRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["mate_kind"] = Template.MateKindId
            };
            PopulateFormulaParameters(parameters);
            var res = (int)formula.Evaluate(parameters);
            res /= 5; // TODO ...
            foreach (var bonus in GetBonuses(UnitAttribute.PersistentHealthRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.MaxMana)]
    public override int MaxMp
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MaxMana);
            var mateKindVariable = FormulaManager.Instance.GetUnitVariable(formula.Id,
                UnitFormulaVariableType.MateKind, (uint)Template.MateKindId);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["mate_kind"] = mateKindVariable
            };
            PopulateFormulaParameters(parameters);
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.MaxMana))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.ManaRegen)]
    public override int MpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.ManaRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["mate_kind"] = Template.MateKindId
            };
            PopulateFormulaParameters(parameters);
            var res = (int)formula.Evaluate(parameters);
            res += Spi / 10;
            foreach (var bonus in GetBonuses(UnitAttribute.ManaRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.PersistentManaRegen)]
    public override int PersistentMpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.PersistentManaRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["mate_kind"] = Template.MateKindId
            };
            PopulateFormulaParameters(parameters);
            var res = (int)formula.Evaluate(parameters);
            res /= 5; // TODO ...
            foreach (var bonus in GetBonuses(UnitAttribute.PersistentManaRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }

            return res;
        }
    }

    // [UnitAttribute(UnitAttribute.Dps)]
    public override float LevelDps
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.LevelDps);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["ab_level"] = Level
            };
            PopulateFormulaParameters(parameters);

            var res = formula.Evaluate(parameters);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeDpsInc)]
    public override int DpsInc
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MeleeDpsInc);
            var parameters = new Dictionary<string, double>();
            PopulateFormulaParameters(parameters);
            parameters["level"] = Level;
            parameters["str"] = Str;
            parameters["dex"] = Dex;
            parameters["sta"] = Sta;
            parameters["int"] = Int;
            parameters["spi"] = Spi;
            parameters["fai"] = Fai;
            var res = formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.MeleeDpsInc))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }
            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellDpsInc)]
    public override int MDpsInc
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.SpellDpsInc);
            var parameters = new Dictionary<string, double>();
            PopulateFormulaParameters(parameters);
            parameters["level"] = Level;
            parameters["str"] = Str;
            parameters["dex"] = Dex;
            parameters["sta"] = Sta;
            parameters["int"] = Int;
            parameters["spi"] = Spi;
            parameters["fai"] = Fai;
            var res = formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.SpellDpsInc))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }
            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Armor)]
    public override int Armor
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Armor);
            var parameters = new Dictionary<string, double>();
            PopulateFormulaParameters(parameters);
            parameters["level"] = Level;
            parameters["str"] = Str;
            parameters["dex"] = Dex;
            parameters["sta"] = Sta;
            parameters["int"] = Int;
            parameters["spi"] = Spi;
            parameters["fai"] = Fai;
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Armor))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.MagicResist)]
    public override int MagicResistance
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MagicResist);
            var parameters = new Dictionary<string, double>();
            PopulateFormulaParameters(parameters);
            parameters["level"] = Level;
            parameters["str"] = Str;
            parameters["dex"] = Dex;
            parameters["sta"] = Sta;
            parameters["int"] = Int;
            parameters["spi"] = Spi;
            parameters["fai"] = Fai;
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.MagicResist))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += bonus.Value;
            }
            return res;
        }
    }
    #endregion

    public Mate()
    {
        ModelParams = new UnitCustomModelParams();
        Skills = new List<uint>();
        Passengers = new Dictionary<AttachPointKind, MatePassengerInfo>();
        Equipment = new MateEquipmentContainer(0, SlotType.EquipmentMate, false);

        // TODO: Spawn this with the correct amount of seats depending on the template
        // 2 seats by default
        Passengers.Add(AttachPointKind.Driver, new MatePassengerInfo { ObjId = 0, Reason = 0 });
        Passengers.Add(AttachPointKind.Passenger0, new MatePassengerInfo { ObjId = 0, Reason = 0 });
    }

    /// <summary>
    /// Milliseconds between walking steps. Also what turns a step into a speed, so the figure the
    /// client is told agrees with the ground actually covered.
    /// </summary>
    public const int FollowTickIntervalMs = 100;

    /// <summary>True when the last state sent said the mate was moving.</summary>
    private bool _isMoving;

    /// <summary>
    /// Walks one tick's worth of the way towards a point and tells everyone about it.
    /// </summary>
    /// <remarks>
    /// The velocity sent has to agree with the ground actually covered: the client extrapolates
    /// between states from it, so a figure that contradicts the displacement is what makes an
    /// animal skate instead of walk.
    /// </remarks>
    /// <param name="target">Where it is heading.</param>
    /// <param name="distance">How far it may travel this tick.</param>
    public void MoveTowards(Vector3 target, float distance)
    {
        if (distance < 0.01f || IsDead)
            return;

        var currentPosition = Transform.Local.Position;
        var targetDistance = MathUtil.CalculateDistance(currentPosition, target, true);
        if (targetDistance <= 0.1f)
            return;

        var travelDistance = Math.Min(targetDistance, distance);
        var (newX, newY, newZ) = PositionAndRotation.AddDistanceToFront(
            travelDistance, targetDistance, currentPosition, target);
        Transform.Local.SetPosition(newX, newY, newZ);

        // Follow the ground rather than the straight line to the owner, but only where the two
        // are close enough that this is a slope and not a different floor altogether.
        var groundHeight = WorldManager.Instance.GetHeight(Transform.ZoneId, newX, newY);
        if (Math.Abs(newZ - groundHeight) < 1f)
            Transform.Local.SetHeight(groundHeight);

        var angle = MathUtil.CalculateAngleFrom(Transform.Local.Position, target);
        Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90);

        var speedPerSecond = travelDistance * (1000f / FollowTickIntervalMs);
        SendMovement(speedPerSecond, (float)angle);
        _isMoving = true;
    }

    /// <summary>
    /// Says the mate has come to a stop, once.
    /// </summary>
    /// <remarks>
    /// Without a state carrying no speed the client keeps extrapolating from the last one it had
    /// and the animal drifts on past where the server has it standing.
    /// </remarks>
    public void StopMovement()
    {
        if (!_isMoving)
            return;

        _isMoving = false;
        SendMovement(0f, 0f);
    }

    private void SendMovement(float speedPerSecond, float angleDegrees)
    {
        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

        short velX = 0;
        short velY = 0;
        if (speedPerSecond > 0f)
        {
            var normalized = speedPerSecond / ActorVelocityScale;
            var (rawX, rawY) = MathUtil.AddDistanceToFront(
                normalized * PacketStream.NormalizedShortScale, 0, 0, angleDegrees.DegToRad());
            velX = (short)Math.Clamp(rawX, short.MinValue, short.MaxValue);
            velY = (short)Math.Clamp(rawY, short.MinValue, short.MaxValue);
        }

        var (rotationX, rotationY, rotationZ) = Transform.Local.ToRollPitchYawSBytesMovement();

        moveType.X = Transform.Local.Position.X;
        moveType.Y = Transform.Local.Position.Y;
        moveType.Z = Transform.Local.Position.Z;
        moveType.VelX = velX;
        moveType.VelY = velY;
        moveType.RotationX = rotationX;
        moveType.RotationY = rotationY;
        moveType.RotationZ = rotationZ;
        moveType.DeltaMovement = [0, speedPerSecond > 0f ? (sbyte)127 : (sbyte)0, 0];
        moveType.Stance = 0;
        moveType.Alertness = 0;
        moveType.ActorFlags = 0;
        moveType.Flags = 4;
        moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

        BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveType), false);
    }

    /// <summary>What a full-scale velocity component means for the actor variant.</summary>
    private const float ActorVelocityScale = 60f;

    /// <summary>Stops the walking step. Called when the mate leaves the world.</summary>
    public void StopFollowing()
    {
        _ = MateFollowTask?.CancelAsync();
        MateFollowTask = null;
        _isMoving = false;
    }

    public void AddExp(int exp)
    {

        if (exp == 0)
            return;
        if (exp > 0)
        {
            var totalExp = (int)Math.Round(AppConfiguration.Instance.World.ExpRate * exp);
            Experience += totalExp;
        }
        SendPacket(new SCExpChangedPacket(ObjId, exp, false));
        CheckLevelUp();
    }

    public void CheckLevelUp()
    {
        var needExp = ExperienceManager.Instance.GetExpForLevel((byte)(Level + 1));
        var change = false;
        while (Experience >= needExp)
        {
            change = true;
            Level++;
            needExp = ExperienceManager.Instance.GetExpForLevel((byte)(Level + 1));
        }

        if (change)
        {
            BroadcastPacket(new SCLevelChangedPacket(ObjId, Level), true);
            WorldManager.Instance.GetCharacterByObjId(OwnerObjId)?.Quests?.OnMateLevelChanged(this);
            //StartRegen();
        }

        DbInfo.Xp = Experience;
        DbInfo.Level = Level;
    }

    public override void AddVisibleObject(Character character)
    {
        base.AddVisibleObject(character);

        character.SendPacket(new SCUnitStatePacket(this));
        character.SendPacket(new SCMateStatusPacket(ObjId));
        character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp, HighAbilityRsc));
        // TODO: Maybe let base handle this ?
        foreach (var ati in Passengers)
        {
            if (ati.Value.ObjId > 0)
            {
                var player = WorldManager.Instance.GetCharacterByObjId(ati.Value.ObjId);
                if (player != null)
                    character.SendPacket(new SCUnitAttachedPacket(player.ObjId, ati.Key, ati.Value.Reason, ObjId));
            }
        }
    }

    public override void RemoveVisibleObject(Character character)
    {
        base.RemoveVisibleObject(character);

        character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));
    }

    public override int DoFallDamage(ushort fallVel)
    {
        var fallDmg = base.DoFallDamage(fallVel);
        if (Hp <= 0)
        {
            var riders = Passengers.ToList();
            // When fall damage kills a mount, also kill all of it's riders
            for (var i = riders.Count - 1; i >= 0; i--)
            {
                var pos = riders[i].Key;
                var rider = WorldManager.Instance.GetCharacterByObjId(riders[i].Value.ObjId);
                if (rider != null)
                {
                    rider.DoFallDamage(fallVel);
                    if (rider.Hp <= 0)
                        MateManager.Instance.UnMountMate(rider.Connection, TlId, pos, AttachUnitReason.SlaveBinding);
                }
            }
        }

        return fallDmg;
    }

    public override void Regenerate()
    {
        if (!NeedsRegen)
        {
            return;
        }
        if (IsDead)
        {
            var riders = Passengers.ToList();
            for (var i = riders.Count - 1; i >= 0; i--)
            {
                var pos = riders[i].Key;
                var rider = WorldManager.Instance.GetCharacterByObjId(riders[i].Value.ObjId);
                if (rider != null)
                {
                    MateManager.Instance.UnMountMate(rider.Connection, TlId, pos, AttachUnitReason.None);
                }
            }
            return;
        }

        var oldHp = Hp;

        if (IsInBattle)
        {
            Hp += PersistentHpRegen;
            Mp += PersistentMpRegen;
        }
        else
        {
            Hp += HpRegen;
            Mp += MpRegen;
        }

        Hp = Math.Min(Hp, MaxHp);
        Mp = Math.Min(Mp, MaxMp);
        BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Mp, HighAbilityRsc), false);
        PostUpdateCurrentHp(this, oldHp, Hp, KillReason.Unknown);
    }

    public void StartUpdateXp(Character Owner)
    {
        if (MateXpUpdateTask != null)
        {
            return;
        }
        MateXpUpdateTask = new MateXpUpdateTask(Owner, this);
        TaskManager.Instance.Schedule(MateXpUpdateTask, TimeSpan.FromSeconds(60));
        //Logger.Trace("[StartUpdateXp] The current timer has been started...");
    }

    public void StopUpdateXp()
    {
        _ = MateXpUpdateTask?.CancelAsync();
        MateXpUpdateTask = null;
        //Logger.Trace("[StopUpdateXp] The current timer has been canceled...");
    }
}
