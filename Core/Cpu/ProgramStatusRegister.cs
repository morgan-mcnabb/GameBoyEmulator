namespace Core.Cpu;

public struct ProgramStatusRegister
{
    // ─── Condition‑code flags ────────────────────────────────────────────
    public bool Negative;   // N bit (31)
    public bool Zero;       // Z bit (30)
    public bool Carry;      // C bit (29)
    public bool Overflow;   // V bit (28)

    // ─── Interrupt / processor state flags ───────────────────────────────
    public bool IrqDisable; // I bit (7)
    public bool FiqDisable; // F bit (6)
    public bool ThumbState; // T bit (5)

    /// <summary> Packs / unpacks the raw 32‑bit CPSR value. </summary>
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

    public override string ToString() => Value.ToString("X8");
}