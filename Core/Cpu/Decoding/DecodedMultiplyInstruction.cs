namespace Core.Cpu.Decoding;

public readonly record struct DecodedMultiplyInstruction(
    bool Accumulate,   // bit 21; true => MLA, false => MUL
    bool SetConditionCodes, // bit 20 ("s" suffix)
    int DestinationRegister,
    int AccumulateRegister,
    int OperandMRegister,
    int OperandSRegister
    );