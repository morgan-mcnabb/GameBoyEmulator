namespace Core.Cpu;

internal static class AluFlagHelper
{
    public static void UpdateNegativeZeroCarryOverflow(
        ref ProgramStatusRegister programStatusRegister,
        uint result,
        bool carry,
        bool overflow)
    {
        programStatusRegister.Negative = (result & (1u << 31)) != 0;
        programStatusRegister.Zero     = result == 0;
        programStatusRegister.Carry    = carry;
        programStatusRegister.Overflow = overflow;
    }
}