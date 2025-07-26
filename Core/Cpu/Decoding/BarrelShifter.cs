using System;

namespace Core.Cpu;

/// <summary>
/// Implements the ARM “barrel shifter” that produces the <c>Operand2</c> value
/// for data-processing instructions when the instruction uses register syntax
/// (bit 25 == 0).
/// </summary>
public static class BarrelShifter
{
    /// <summary>Shift type encoded in bits [6:5] of <c>Operand2</c>.</summary>
    public enum ShiftType : byte
    {
        LogicalLeft        = 0b00,   // LSL
        LogicalRight       = 0b01,   // LSR
        ArithmeticRight    = 0b10,   // ASR
        RotateRight        = 0b11    // ROR / RRX
    }

    /// <summary>
    /// Applies <paramref name="shiftType"/> by <paramref name="amount"/> to
    /// <paramref name="value"/>, returning the shifted result and the carry-out
    /// that the ARM architecture defines for each case.
    /// </summary>
    internal static uint Apply(
        uint value,
        ShiftType shiftType,
        int amount,
        bool carryIn,
        out bool carryOut)
    {
        // The architecture masks the amount to 0–255 when it originates from Rs,
        // so we accept any non-negative value.
        amount &= 0xFF;

        switch (shiftType)
        {
            case ShiftType.LogicalLeft: // LSL
            {
                switch (amount)
                {
                    case 0:
                        carryOut = carryIn;
                        return value;
                    case < 32:
                        carryOut = ((value >> (32 - amount)) & 1) != 0;
                        return value << amount;
                    case 32:
                        carryOut = (value & 1) != 0;
                        return 0u;
                    default:
                        // amount > 32 ⇒ result = 0, carry = 0
                        carryOut = false;
                        return 0u;
                }
            }

            case ShiftType.LogicalRight: // LSR
            {
                switch (amount)
                {
                    case 0:
                    case 32:
                        carryOut = (value >> 31) != 0;
                        return 0u;
                    case < 32:
                        carryOut = ((value >> (amount - 1)) & 1) != 0;
                        return value >> amount;
                    default:
                        // amount > 32 ⇒ result = 0, carry = 0
                        carryOut = false;
                        return 0u;
                }
            }

            case ShiftType.ArithmeticRight: // ASR
            {
                if (amount is 0 or >= 32)
                {
                    // replicates sign bit across entire word.
                    carryOut = (value >> 31) != 0;
                    return (value & 0x8000_0000u) != 0 ? 0xFFFF_FFFFu : 0u;
                }

                carryOut = ((value >> (amount - 1)) & 1) != 0;
                var shifted = (int)value >> amount; // arithmetic shift in C#
                return (uint)shifted;
            }

            case ShiftType.RotateRight: // ROR / RRX
            {
                amount &= 0x1F; // only 0–31 are meaningful for ROR

                if (amount == 0)
                {
                    // RRX (Rotate Right with Extend, amount == 0)
                    carryOut = (value & 1) != 0;
                    var msbFromCarry = carryIn ? 0x8000_0000u : 0u;
                    return msbFromCarry | (value >> 1);
                }

                carryOut = ((value >> (amount - 1)) & 1) != 0;
                return (value >> amount) | (value << (32 - amount));
            }
        }

        // we should never reach this point.
        throw new ArgumentOutOfRangeException(nameof(shiftType));
    }
}
