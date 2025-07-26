namespace Core.Cpu.Decoding;

public readonly record struct DecodedDataProcessingInstruction(
    DataProcessingOpcode Opcode,
    bool UsesImmediate,
    bool SetConditionCodes,
    int RegisterN, // First operand
    int RegisterD, // Desitnation register
    uint Operand2Raw // 12-bit immediate or shift field
    );