namespace Core.Cpu.Decoding;

/// <summary>
/// ARM data-processing opcodes.
/// </summary>
public enum DataProcessingOpcode : byte
{
   And  = 0x0,
   Eor  = 0x1,
   Sub  = 0x2,  // Implemented
   Rsb  = 0x3,
   Add  = 0x4,  // Implemented
   Adc  = 0x5,
   Sbc  = 0x6,
   Rsc  = 0x7,
   Tst  = 0x8,
   Teq  = 0x9,
   Cmp  = 0xA,
   Cmn  = 0xB,
   Orr  = 0xC,
   Mov  = 0xD,  // Implemented
   Bic  = 0xE,
   Mvn  = 0xF
}