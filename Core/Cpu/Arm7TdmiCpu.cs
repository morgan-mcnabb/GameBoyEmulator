using Core.Abstract;
using Core.Cpu.Decoding;

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

    private void AdvancePc() => _registers[PcIndex] += 4;
    
    /// <inheritdoc />
    public void Step()
    {
        // fetch
        var opcode = _bus.Read32(Pc & ~3u);

        var condition = (ConditionCode)(opcode >> 28);
        if (!ConditionMet(condition))
        {
            AdvancePc();
            return; // no-op
        }
        
        // decode 
        // TODO: insert decode table & execution pipeline
        if (ArmInstructionDecoder.TryDecodeDataProcessing(opcode, out var decodedInstruction))
            ExecuteDataProcessing(decodedInstruction);
        else
            throw new NotImplementedException(
                $"Opcode group not yet implemented (0x{opcode:X8})");

        AdvancePc();
    }

    private void ExecuteDataProcessing(DecodedDataProcessingInstruction instruction)
    {
        var operand1Value = _registers[instruction.RegisterN];
        var operand2Value = ArmInstructionDecoder.ExpandOperand2(instruction, _registers,
            _currentProgramStatusRegister.Carry, out var shifterCarryFlag);

        uint result;
        var carryOutFlag = false;
        var overflowOutFlag = false;

        switch (instruction.Opcode)
        {
            case DataProcessingOpcode.Mov:
                result        = operand2Value;
                carryOutFlag  = shifterCarryFlag;
                overflowOutFlag = _currentProgramStatusRegister.Overflow;
                break;

            case DataProcessingOpcode.Add:
                (result, carryOutFlag, overflowOutFlag) =
                    AddWithCarry(operand1Value, operand2Value, carryInFlag: false);
                break;

            case DataProcessingOpcode.Sub:
                (result, carryOutFlag, overflowOutFlag) =
                    AddWithCarry(operand1Value, ~operand2Value, carryInFlag: true);
                break;

            default:
                throw new NotImplementedException($"Opcode {instruction.Opcode} not yet supported.");
        }

        _registers[instruction.RegisterD] = result;
        
        if (instruction.SetConditionCodes)
            AluFlagHelper.UpdateNegativeZeroCarryOverflow(ref _currentProgramStatusRegister, result, carryOutFlag, overflowOutFlag);
    }

    private static (uint Result, bool Carry, bool Overflow) AddWithCarry(
        uint leftOperand, uint rightOperand, bool carryInFlag)
    {
        var unsignedSum = (ulong)leftOperand + rightOperand + (carryInFlag ? 1UL : 0UL);
        var signedSum = (long)(int)leftOperand + (int)rightOperand + (carryInFlag ? 1L : 0L);

        var result = (uint)unsignedSum;
        var carryOut = (unsignedSum >> 32) != 0;
        var overflowOut = signedSum is < int.MinValue or > int.MaxValue;

        return (result, carryOut, overflowOut);
    }
    
    private bool ConditionMet(ConditionCode condition)
    {
        var psr = _currentProgramStatusRegister;
        return condition switch
        {
            ConditionCode.Equal                 => psr.Zero,
            ConditionCode.NotEqual              => !psr.Zero,
            ConditionCode.CarrySet              => psr.Carry,
            ConditionCode.CarryClear            => !psr.Carry,
            ConditionCode.Minus                 => psr.Negative,
            ConditionCode.Plus                  => !psr.Negative,
            ConditionCode.OverflowSet           => psr.Overflow,
            ConditionCode.OverflowClear         => !psr.Overflow,
            ConditionCode.UnsignedHigher        => psr.Carry && !psr.Zero,
            ConditionCode.UnsignedLowerOrSame   => !psr.Carry || psr.Zero,
            ConditionCode.SignedGreaterOrEqual  => psr.Negative == psr.Overflow,
            ConditionCode.SignedLess            => psr.Negative != psr.Overflow,
            ConditionCode.SignedGreater         => !psr.Zero && psr.Negative == psr.Overflow,
            ConditionCode.SignedLessOrEqual     => psr.Zero || psr.Negative != psr.Overflow,
            _                                   => true // ConditionCode.Always
        };
    }
}
