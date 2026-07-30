using System.Collections.Generic;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Game.DoodadObj.Templates;

public class DoodadTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool ShowName { get; set; }
    public bool ShowMinimap { get; set; }
    public string MarkModel { get; set; } = string.Empty;
    public bool LoadModelFromWorld { get; set; }
    public bool Translate { get; set; } = true;
    public uint SpawnFxGroupId { get; set; }
    public bool SystemDoodad { get; set; }
    public bool DeleteWhenCreatorMissing { get; set; }
    public uint PlaceAreaKindId { get; set; }
    public bool ResetData { get; set; }
    public bool PassUpdateDistance { get; set; } = true;
    public bool PassThroughOuterSide { get; set; } = true;
    public bool PassThroughInnerSide { get; set; } = true;
    public int ViewDistanceRatio { get; set; } = 100;
    public bool ClientDoodad { get; set; }
    public bool OrUnitRequirements { get; set; }
    public uint CustomDualMaterialId { get; set; }
    public int SimHeight { get; set; }
    public uint Id { get; set; }
    public bool OnceOneMan { get; set; }
    public bool OnceOneInteraction { get; set; }
    public bool MgmtSpawn { get; set; }
    public int Percent { get; set; }
    public int MinTime { get; set; }
    public int MaxTime { get; set; }
    public uint ModelKindId { get; set; }
    public bool UseCreatorFaction { get; set; }
    public bool ForceTodTopPriority { get; set; }
    public uint MilestoneId { get; set; }
    public uint GroupId { get; set; }
    public bool UseTargetDecal { get; set; }
    public bool UseTargetSilhouette { get; set; }
    public bool UseTargetHighlight { get; set; }
    public float TargetDecalSize { get; set; }
    public int SimRadius { get; set; }
    public bool CollideShip { get; set; }
    public bool CollideVehicle { get; set; }
    public Climate ClimateId { get; set; }
    public bool SaveIndun { get; set; }
    public bool ForceUpAction { get; set; }
    public bool Parentable { get; set; }
    public bool Childable { get; set; }
    public uint FactionId { get; set; }
    public int GrowthTime { get; set; }
    public bool DespawnOnCollision { get; set; }
    public bool NoCollision { get; set; }
    public uint RestrictZoneId { get; set; }

    public List<DoodadFuncGroups> FuncGroups { get; set; }

    // Helper Properties
    public int TotalDoodadGrowthTime { get; set; }

    public DoodadTemplate()
    {
        FuncGroups = new List<DoodadFuncGroups>();
    }

    /// <summary>
    /// There's probably a better why to check this
    /// </summary>
    /// <returns>Returns true if the GroupId is one of ones that give vocation badges when used</returns>
    public bool GrantsVocationWhenUsed()
    {
        // TODO: Need to remove magic numbers
        switch (GroupId)
        {
            case 2: // Deforestation - Trees
            case 3: // Picking - Herbs
            case 4: // Mining - Minerals
            case 5: // Livestock - Livestock
            case 12: // Agriculture - Crops
            case 39: //Interaction - Excavation
            case 40: // Agriculture - Marine Crops
            case 65: // Fish (sports fishing ?)
                return true;
            default:
                return false;
        }
    }
}
