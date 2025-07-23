using Core.Abstract;

namespace Core.Cpu;

public sealed class Arm7TdmiCpu : ICpu
{
    private const int RegisterCount = 16; // R0-R15
    private const int PcIndex = 15; // R15 alias
    private const uint ResetVectorAddress = 0x0000_0000; // GBA reset entry

    private readonly IMemoryBus _bus;
    private readonly uint[] _registers = new uint[RegisterCount];
    private ProgramStatusRegister _currentProgramStatusRegister;

    public Arm7TdmiCpu(IMemoryBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        Reset();
    }

    /// <summary>
    /// Places the CPU in its power-on state.
    /// </summary>
    private void Reset()
    {
        Array.Clear(_registers);
        _currentProgramStatusRegister = default;
        _registers[PcIndex] = ResetVectorAddress;
    }
    
    /// <inheritdoc />
    public uint Pc => _registers[PcIndex];
    
    /// <inheritdoc />
    public void Step()
    {
        // fetch
        var opcode = _bus.Read32(Pc & ~3u);
        
        // decode 
        // TODO: insert decode table & execution pipeline
        
        // increment PC
        _registers[PcIndex] += 4; // advance to next ARM instruction
    }


    /// <summary>
    /// *Program Status Register* (CPSR on real hardware).
    ///
    /// The real CPSR is a 32-bit word whose bits are packed like this:
    /// 31   30   29   28        7   6   5     0
    ///  N | Z | C | V | … | I | F | T |  MODE  |
    ///
    /// * **N, Z, C, V** – ALU condition flags (Negative, Zero, Carry, Overflow)  
    /// * **I** – IRQ disable  
    /// * **F** – FIQ disable  
    /// * **T** – Execution state (0 = ARM, 1 = Thumb) 
    /// </summary>
    private struct ProgramStatusRegister
    {
        // Condition-code flags
        public bool Negative;   // Bit 31
        public bool Zero;       // Bit 30
        public bool Carry;      // Bit 29
        public bool Overflow;   // Bit 28

        // Interrupt / state flags
        public bool IrqDisable; // Bit 7
        public bool FiqDisable; // Bit 6
        public bool ThumbState; // Bit 5

        /// <summary>
        /// Packs the human-readable flag fields into a raw 32-bit value.
        /// Getting the property *assembles* the word; setting it *decomposes*
        /// the word back into individual Booleans. 
        /// </summary>
        public uint Value
        {
            get
            {
                uint word = 0;
                if (Negative)   word |= 1u << 31;
                if (Zero)       word |= 1u << 30;
                if (Carry)      word |= 1u << 29;
                if (Overflow)   word |= 1u << 28;
                if (IrqDisable) word |= 1u << 7;
                if (FiqDisable) word |= 1u << 6;
                if (ThumbState) word |= 1u << 5;
                return word;
            }
            set
            {
                Negative   = (value & (1u << 31)) != 0;
                Zero       = (value & (1u << 30)) != 0;
                Carry      = (value & (1u << 29)) != 0;
                Overflow   = (value & (1u << 28)) != 0;
                IrqDisable = (value & (1u << 7))  != 0;
                FiqDisable = (value & (1u << 6))  != 0;
                ThumbState = (value & (1u << 5))  != 0;
            }
        }
    }
}
