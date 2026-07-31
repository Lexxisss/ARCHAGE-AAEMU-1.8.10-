using System;
using System.Collections.Generic;

using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Templates;

public enum DynamicBonusFunctionType : byte
{
    Linear = 0,
    Manual = 1,
    DynamicAttribute = 2,
    Formula = 3
}

/// <summary>
/// Runtime unit modifier attached to a buff. The four function modes and their
/// integer/truncation behaviour are recovered from x2game-dev_dedicate.dll.
/// </summary>
public class DynamicBonusTemplate
{
    public uint Id { get; set; }
    public UnitAttribute Attribute { get; set; }
    public UnitModifierType ModifierType { get; set; }
    public uint FunctionId { get; set; }
    public DynamicBonusFunctionType FunctionType { get; set; }

    public int LinearStartValue { get; set; }
    public int LinearEndValue { get; set; }
    public IReadOnlyList<int> ManualValues { get; set; } = Array.Empty<int>();
    public UnitAttribute SourceAttribute { get; set; }
    public int DynamicScale { get; set; }
    public Formula Formula { get; set; }
    public IReadOnlyList<byte> FormulaAttributeIds { get; set; } = Array.Empty<byte>();

    public int Evaluate(Unit owner, Buff buff)
    {
        if (owner == null || buff == null)
            return 0;

        long value = FunctionType switch
        {
            DynamicBonusFunctionType.Linear => EvaluateLinear(buff),
            DynamicBonusFunctionType.Manual => EvaluateManual(buff),
            DynamicBonusFunctionType.DynamicAttribute =>
                (long)Math.Truncate(owner.GetAttributeNumeric(SourceAttribute) * DynamicScale * 0.001d),
            DynamicBonusFunctionType.Formula => EvaluateFormula(owner),
            _ => 0
        };

        value *= Math.Max(1, buff.StackCount);
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    private int EvaluateLinear(Buff buff)
    {
        // x2game-dev_dedicate.dll uses buff.charge as the current step and
        // duration / tick as the last step. It does not use elapsed time.
        var lastStep = buff.DynamicModifierStepCount;
        var currentStep = buff.Charge;
        if (lastStep <= 0 || currentStep <= 0)
            return LinearStartValue;
        if (currentStep >= lastStep)
            return LinearEndValue;

        // Exact signed-integer interpolation from the dedicated server.
        var delta = (long)LinearEndValue - LinearStartValue;
        return (int)(LinearStartValue + delta * currentStep / lastStep);
    }

    private int EvaluateManual(Buff buff)
    {
        if (ManualValues == null || ManualValues.Count == 0)
            return 0;

        // ManualFunc indexes the value list with buff.charge and clamps to
        // the first/last element. The list itself stores no time axis.
        var currentStep = buff.Charge;
        if (currentStep <= 0)
            return ManualValues[0];
        if (currentStep >= ManualValues.Count)
            return ManualValues[^1];
        return ManualValues[currentStep];
    }

    private int EvaluateFormula(Unit owner)
    {
        if (Formula == null)
            return 0;

        var parameters = new Dictionary<string, double>();
        owner.PopulateFormulaParameters(parameters);
        parameters["pc_level"] = owner.Level;
        parameters["gear_score"] = owner.GetGearScore();

        // FormulaFunc expressions address attributes by their wire id (attr_0, attr_1, ...).
        foreach (var attributeId in FormulaAttributeIds)
            parameters[$"attr_{attributeId}"] = owner.GetAttributeNumeric((UnitAttribute)attributeId);

        return (int)Math.Truncate(Formula.Evaluate(parameters));
    }
}
