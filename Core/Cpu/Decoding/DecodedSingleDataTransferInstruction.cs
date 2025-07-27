namespace Core.Cpu.Decoding;

/// <summary>
/// Parsed form of an ARM single data-transfer instruction (LDR / STR / LDRB / STRB).
/// The constructor contains the raw <c>offsetField</c> so the CPU can expand
/// register-based offsets with the barrel-shifter at run-time.
/// </summary>
public readonly record struct DecodedSingleDataTransferInstruction(
    bool  Load,                 
    bool  ByteTransfer,         
    bool  PreIndexing,         
    bool  AddOffset,           
    bool  WriteBack,           
    bool  UsesRegisterOffset,   
    int   BaseRegister,         
    int   SourceDestRegister,   
    uint  OffsetField          
);