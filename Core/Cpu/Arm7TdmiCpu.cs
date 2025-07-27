using System.Security.AccessControl;
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

    private uint ReadRegisterWithPcOffset(int index) 
        => index == PcIndex ? _registers[PcIndex] + 8u : _registers[index];

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

        var pcWasWritten = false;
        // decode 
        // TODO: insert decode table & execution pipeline
        if (ArmInstructionDecoder.TryDecodeMultiply(opcode, out var multiplyInstruction))
            pcWasWritten = ExecuteMultiple(multiplyInstruction);
        else if (ArmInstructionDecoder.TryDecodeDataProcessing(opcode, out var decodedInstruction))
            pcWasWritten = ExecuteDataProcessing(decodedInstruction);
        else
            throw new NotImplementedException(
                $"Opcode group not yet implemented (0x{opcode:X8})");

        if (pcWasWritten)
            return;
        
        AdvancePc();
    }

    private bool ExecuteDataProcessing(DecodedDataProcessingInstruction instruction)
    {
        var operand1Value = ReadRegisterWithPcOffset(instruction.RegisterN);

        Span<uint> registersForOp2 = stackalloc uint[RegisterCount];
        _registers.AsSpan().CopyTo(registersForOp2);
        registersForOp2[PcIndex] = _registers[PcIndex] + 8u;

        var operand2Value = ArmInstructionDecoder.ExpandOperand2(instruction, registersForOp2,
            _currentProgramStatusRegister.Carry, out var shifterCarryFlag);

        uint  result = 0;
        var  carryOut = shifterCarryFlag;            // logical ops default
        var  overflowOut = _currentProgramStatusRegister.Overflow;          // logical ops leave V
        var  writeBackToRd = true;
        var  updateCondition = instruction.SetConditionCodes;
        var pcWritten = false;


        switch (instruction.Opcode)
        {
            case DataProcessingOpcode.And:
                result = operand1Value & operand2Value;
                break;
            
            case DataProcessingOpcode.Eor:
                result = operand1Value ^ operand2Value;
                break;
            
            case DataProcessingOpcode.Orr:
                result = operand1Value | operand2Value;
                break;
            
            case DataProcessingOpcode.Bic:
                result = operand1Value & ~operand2Value;
                break;
            
            case DataProcessingOpcode.Mov:
                result = operand2Value;
                break;
           
            case DataProcessingOpcode.Mvn:
                result = ~operand2Value;
                break;
            
            case DataProcessingOpcode.Add:
                (result, carryOut, overflowOut) =
                    AluOperations.AddWithCarry(operand1Value, operand2Value, carryInFlag: false);
                break;
            
            case DataProcessingOpcode.Adc:
                (result, carryOut, overflowOut) =
                    AluOperations.AddWithCarry(operand1Value, operand2Value, _currentProgramStatusRegister.Carry);
                break;
            
            case DataProcessingOpcode.Sub:
                (result, carryOut, overflowOut) =
                    AluOperations.AddWithCarry(operand1Value, ~operand2Value, carryInFlag: true);
                break;
            
            case DataProcessingOpcode.Sbc:
                (result, carryOut, overflowOut) =
                    AluOperations.AddWithCarry(operand1Value, ~operand2Value, _currentProgramStatusRegister.Carry);
                break;
            
            case DataProcessingOpcode.Rsb:
                (result, carryOut, overflowOut) =
                    AluOperations.AddWithCarry(operand2Value, ~operand1Value, carryInFlag: true);
                break;
            
            case DataProcessingOpcode.Rsc:
                (result, carryOut, overflowOut) =
                    AluOperations.AddWithCarry(operand2Value, ~operand1Value, _currentProgramStatusRegister.Carry);
                break;
            
            case DataProcessingOpcode.Tst:     
                result          = operand1Value & operand2Value;
                writeBackToRd   = false;
                updateCondition = true;       
                break;

            case DataProcessingOpcode.Teq:    
                result          = operand1Value ^ operand2Value;
                writeBackToRd   = false;
                updateCondition = true;
                break;

            case DataProcessingOpcode.Cmp: 
                (result, carryOut, overflowOut) =
                    AluOperations.AddWithCarry(operand1Value, ~operand2Value,
                        carryInFlag: true);
                writeBackToRd   = false;
                updateCondition = true;
                break;

            case DataProcessingOpcode.Cmn:    
                (result, carryOut, overflowOut) =
                    AluOperations.AddWithCarry(operand1Value, operand2Value,
                        carryInFlag: false);
                writeBackToRd   = false;
                updateCondition = true;
                break;
            default:
                throw new NotImplementedException($"Opcode {instruction.Opcode} not yet supported.");
        }

        if (writeBackToRd)
        {
            _registers[instruction.RegisterD] = result;
            pcWritten = instruction.RegisterD == PcIndex;
        }

        if (updateCondition)
            AluOperations.UpdateNegativeZeroCarryOverflow(ref _currentProgramStatusRegister, result, carryOut, overflowOut );

        return pcWritten;
    }

    private bool ExecuteMultiple(DecodedMultiplyInstruction instruction)
    {
        var multiplicand = _registers[instruction.OperandMRegister];
        var multiplier = _registers[instruction.OperandSRegister];
        var product64 = (ulong)multiplicand * multiplier; // 64-bit to avoid overflow loss
        var result = (uint)product64;

        if (instruction.Accumulate)
        {
            var addend = _registers[instruction.AccumulateRegister];
            result = unchecked(result + addend); // wraparound, disable overflow checking
        }

        _registers[instruction.DestinationRegister] = result;
        var pcWritten = instruction.DestinationRegister == PcIndex;

        if (instruction.SetConditionCodes)
            AluOperations.UpdateNegativeZero(ref _currentProgramStatusRegister, result);

        return pcWritten;
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
