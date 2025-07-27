namespace Core.Cpu.Decoding;

/// <summary>parsed form of an <c>LDM/STM</c> instruction.</summary>
public readonly record struct DecodedBlockDataTransferInstruction(
    bool   Load,          
    bool   PreIndexing,   
    bool   AddOffset,      
    bool   WriteBack,    
    bool   PsrsUserMode,  
    int    BaseRegister, 
    ushort RegisterList   
);