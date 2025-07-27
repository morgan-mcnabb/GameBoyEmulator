namespace Core.Cpu;

internal static class AluOperations
{
    internal static void UpdateNegativeZero(ref ProgramStatusRegister programStatusRegister, uint result)
    {
        programStatusRegister.Negative = (result & (1u << 31)) != 0;
        programStatusRegister.Zero = result == 0;
    }
    
    internal static void UpdateNegativeZeroCarryOverflow(
        ref ProgramStatusRegister programStatusRegister,
        uint result,
        bool carry,
        bool overflow)
    {
        UpdateNegativeZero(ref programStatusRegister, result);
        programStatusRegister.Carry    = carry;
        programStatusRegister.Overflow = overflow;
    }

    internal static (uint Result, bool Carry, bool Overflow) AddWithCarry(
        uint leftOperand,
        uint rightOperand,
        bool carryInFlag
    )
    {
        // Unsigned 33-bit addition to capture carry-out
        var unsignedSum = (ulong)leftOperand
                          +  rightOperand
                          + (carryInFlag ? 1UL : 0UL);

        // Signed addition to detect two’s-complement overflow.
        var signedSum = (long)(int)leftOperand
                        +  (int)rightOperand
                        + (carryInFlag ? 1L : 0L);

        var result        = (uint)unsignedSum;
        var carryOut      = (unsignedSum >> 32) != 0;
        var overflowOccur = signedSum is < int.MinValue or > int.MaxValue;

        return (result, carryOut, overflowOccur);
    }
}