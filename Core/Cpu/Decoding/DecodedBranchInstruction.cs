namespace Core.Cpu.Decoding;

/// <summary>
/// Parsed form of an ARM <c>B</c>/<c>BL</c> instruction (cond 101 offset).
/// </summary>
/// <param name="Link">True when bit 24 is set – the variant is <c>BL</c>.</param>
/// <param name="SignedOffset">PC-relative, byte-addressed offset</param>
public readonly record struct DecodedBranchInstruction(
    bool Link,
    int  SignedOffset   
);