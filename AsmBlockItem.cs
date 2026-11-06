using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace x2_prototype;

public class AsmBlockItem(string asmBlockType, string asmNation, string? asmUnit, uint? asmValue, string? asmTech, string? asmBuilding)
{
    public string AsmBlockType { get; } = asmBlockType;
    public string AsmNation { get; } = asmNation;
    public string? AsmUnit { get; } = asmUnit;
    public uint? AsmValue { get; } = asmValue;
    public string? AsmTech { get; } = asmTech;
    public string? AsmBuilding { get; } = asmBuilding;

    // Catastrophically implemented because it's easier for prototyping
    public bool UsesX =>
        AsmBlockType is "X unit is Y% cheaper to build"
                     or "X unit is Y% faster to build"
                     or "Start game with X unit";

    public bool UsesY =>
        AsmBlockType is "X unit is Y% cheaper to build"
                     or "X unit is Y% faster to build";

    public bool UsesZ1 =>
        AsmBlockType is "Start game with Z1 tech";

    public bool UsesZ2 =>
        AsmBlockType is "Start game with Z2 building";
}

