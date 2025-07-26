namespace Core.Cpu;

/// <summary>
/// Execution condition field encoded in bits [31:28] of every ARM instruction.
/// </summary>
public enum ConditionCode : byte
{
    Equal          = 0x0, // Z == 1
    NotEqual       = 0x1, // Z == 0
    CarrySet       = 0x2, // C == 1
    CarryClear     = 0x3, // C == 0
    Minus          = 0x4, // N == 1
    Plus           = 0x5, // N == 0
    OverflowSet    = 0x6, // V == 1
    OverflowClear  = 0x7, // V == 0
    UnsignedHigher = 0x8, // C == 1 && Z == 0
    UnsignedLowerOrSame = 0x9, // C == 0 || Z == 1
    SignedGreaterOrEqual = 0xA, // N == V
    SignedLess    = 0xB, // N != V
    SignedGreater = 0xC, // Z == 0 && N == V
    SignedLessOrEqual = 0xD, // Z == 1 || N != V
    Always        = 0xE
}