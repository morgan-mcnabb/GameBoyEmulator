using static System.Numerics.BitOperations;

namespace Core.Cpu.Decoding;

public static class ArmInstructionDecoder
{
    private const uint DataProcessingMask = 0x0C00_0000u; // Bits 27-26
    private const uint DataProcessingTag  = 0x0000_0000u; // Data-processing tag

    /// <summary>
    /// attempts to parse <paramref name="rawInstruction"/> as a data-processing opcode.
    /// </summary>
    public static bool TryDecodeDataProcessing(
        uint rawInstruction,
        out DecodedDataProcessingInstruction decoded)
    {
        // bits[27:26]==00 identifies the entire data-processing / misc group
        if ((rawInstruction & DataProcessingMask) != DataProcessingTag)
        {
            decoded = default;
            return false;
        }
        
        if (((rawInstruction >> 4) & 0xF) == 0x9)
        {
            decoded = default;
            return false;
        }

        var opcode = (DataProcessingOpcode)((rawInstruction >> 21) & 0xF);
        var usesImmediate = (rawInstruction & (1u << 25)) != 0;
        var setCc = (rawInstruction & (1u << 20)) != 0;
        var registerN = (int)((rawInstruction >> 16) & 0xF);
        var registerD = (int)((rawInstruction >> 12) & 0xF);
        var operand2 = rawInstruction & 0xFFF;

        decoded = new DecodedDataProcessingInstruction(
            opcode, usesImmediate, setCc, registerN, registerD, operand2);
        return true;
    }

    public static uint ExpandOperand2(
        DecodedDataProcessingInstruction instruction,
        ReadOnlySpan<uint> registers,
        bool currentCarryFlag,
        out bool carryOut)
    {
        if (instruction.UsesImmediate)
        {
            const int immediateBits = 8;
            
            var immediateValue = instruction.Operand2Raw & 0xFF;
            var rotateAmount = (int)((instruction.Operand2Raw >> immediateBits) & 0xF) * 2;

            var rotatedImmediate = RotateRight((uint)immediateValue, rotateAmount);
            carryOut = rotateAmount == 0
                ? currentCarryFlag
                : (rotatedImmediate & 0x8000_0000u) != 0;
            return rotatedImmediate;
        }

        var rmIndex   = (int)(instruction.Operand2Raw & 0xF);
        var rmValue   = registers[rmIndex];

        var isRegisterShift = (instruction.Operand2Raw & (1u << 4)) != 0;
        var shiftType = (BarrelShifter.ShiftType)((instruction.Operand2Raw >> 5) & 0x3);

        // immediate shift amount encoded directly in the instruction
        if (!isRegisterShift)
        {
            var shiftAmount = (int)((instruction.Operand2Raw >> 7) & 0x1F);
            return BarrelShifter.Apply(rmValue, shiftType, shiftAmount,
                currentCarryFlag, out carryOut);
        }

        // shift amount specified by lower byte of Rs
        var rsIndex      = (int)((instruction.Operand2Raw >> 8) & 0xF);
        var rsValue      = registers[rsIndex];
        var shiftAmount8 = (int)(rsValue & 0xFF);

        if (shiftAmount8 != 0)
            return BarrelShifter.Apply(rmValue, shiftType, shiftAmount8,
                currentCarryFlag, out carryOut);
        
        
        // carry unchanged, value unmodified
        carryOut = currentCarryFlag;
        return rmValue;

    }

}