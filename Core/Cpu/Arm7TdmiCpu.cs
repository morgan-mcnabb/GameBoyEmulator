using System.Security.AccessControl;
using Core.Abstract;
using Core.Cpu.Decoding;
using static System.Numerics.BitOperations;

namespace Core.Cpu;

public sealed partial class Arm7TdmiCpu : ICpu
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
        if (ArmInstructionDecoder.TryDecodeBranch(opcode, out var branch))
            pcWasWritten = ExecuteBranch(branch);
        else if (ArmInstructionDecoder.TryDecodeMultiply(opcode, out var multiplyInstruction))
            pcWasWritten = ExecuteMultiple(multiplyInstruction);
        else if (ArmInstructionDecoder.TryDecodeBlockDataTransfer(opcode, out var blockDataTransferInstruction))
            pcWasWritten = ExecuteBlockDataTransfer(blockDataTransferInstruction);
        else if (ArmInstructionDecoder.TryDecodeSingleDataTransfer(opcode, out var dataTransferInstruction))
            pcWasWritten = ExecuteSingleDataTransfer(dataTransferInstruction);
        else if (ArmInstructionDecoder.TryDecodeDataProcessing(opcode, out var decodedInstruction))
            pcWasWritten = ExecuteDataProcessing(decodedInstruction);
        else
            throw new NotImplementedException(
                $"Opcode group not yet implemented (0x{opcode:X8})");

        if (!pcWasWritten)
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

    private bool ExecuteBranch(DecodedBranchInstruction instruction)
    {
        // pipeline is ahead by 8 bytes
        var programVisiblePc = _registers[PcIndex] + 8u;
        var targetAddress = unchecked(programVisiblePc + (uint)instruction.SignedOffset);

        if (instruction.Link)
            _registers[14] = _registers[PcIndex] + 4u;

        _registers[PcIndex] = targetAddress;
        return true;

    }

    private bool ExecuteSingleDataTransfer(DecodedSingleDataTransferInstruction instruction)
    {
        var baseValue = ReadRegisterWithPcOffset(instruction.BaseRegister);

        uint offsetValue;
        if (instruction.UsesRegisterOffset)
        {
            var registersSnapshot = _registers.AsSpan();
            Span<uint> registersWithPc = stackalloc uint[RegisterCount];
            registersSnapshot.CopyTo(registersWithPc);
            registersWithPc[PcIndex] = _registers[PcIndex] + 8u;

            var fakeOp = new DecodedDataProcessingInstruction(Opcode: 0, UsesImmediate: false, SetConditionCodes: false,
                RegisterN: 0, RegisterD: 0, Operand2Raw: instruction.OffsetField);
            offsetValue =
                ArmInstructionDecoder.ExpandOperand2(fakeOp, registersWithPc, _currentProgramStatusRegister.Carry,
                    out _);
        }
        else
            offsetValue = instruction.OffsetField;

        uint effectiveAddress;
        if (instruction.PreIndexing)
        {
            effectiveAddress = instruction.AddOffset
                ? baseValue + offsetValue
                : baseValue - offsetValue;
        }
        else
            effectiveAddress = baseValue;

        var pcWasWritten = false;

        if (instruction.Load)
        {
            uint loadedValue;
            if (instruction.ByteTransfer)
                loadedValue = _bus.Read8(effectiveAddress);
            else
            {
                // for the unaligned word LDR, ARM rotates the aligned word right by 8 * (address & 3)
                var alignedAddress = effectiveAddress & ~3u;
                var rawWord = _bus.Read32(alignedAddress);
                var rotateAmount = (int)((effectiveAddress & 3u) * 8);
                loadedValue = RotateRight(rawWord, rotateAmount);
            }

            _registers[instruction.SourceDestRegister] = loadedValue;
            pcWasWritten = instruction.SourceDestRegister == PcIndex;
        }
        else //store
        {
            // when the destination registers == program counter, the stored value is PC+12 
            // according to ARM documentation?
            var rawStoreValue = instruction.SourceDestRegister == PcIndex
                ? _registers[PcIndex] + 12u
                : _registers[instruction.SourceDestRegister];
            
            if (instruction.ByteTransfer)
                _bus.Write8(effectiveAddress, (byte)rawStoreValue);
            else
                _bus.Write32(effectiveAddress, rawStoreValue);
        }

        if (instruction.PreIndexing)
        {
            if (!instruction.WriteBack) return pcWasWritten;
            _registers[instruction.BaseRegister] = effectiveAddress;
        }
        else // post-indexed => write back is compulsory
        {
            var updatedBase = instruction.AddOffset
                ? baseValue + offsetValue
                : baseValue - offsetValue;

            _registers[instruction.BaseRegister] = updatedBase;
        }

        pcWasWritten |= instruction.BaseRegister == PcIndex;

        return pcWasWritten;
    }
    
    private bool ExecuteBlockDataTransfer(DecodedBlockDataTransferInstruction instruction)
    {
        // Snapshot of Rn before any side-effects (needed for self-stores / write-back).
        var baseRegisterInitialValue = ReadRegisterWithPcOffset(instruction.BaseRegister);

        // Determine whether addresses grow (+4) or shrink (-4).
        var addressDelta = instruction.AddOffset ? 4u : unchecked((uint)-4);

        // For pre-indexed mode we adjust the address before the first transfer.
        var currentAddress = instruction.PreIndexing
            ? baseRegisterInitialValue + addressDelta
            : baseRegisterInitialValue;

        // Empty register list = transfer PC only (defined by ARM ARM).
        var registerList = instruction.RegisterList == 0
            ? (ushort)(1u << PcIndex)
            : instruction.RegisterList;

        var totalRegisterCount = PopCount(registerList);
        var programCounterWasWritten = false;

        // Iterate in ascending register order (R0 → R15).  Memory address
        // naturally moves according to addressDelta, matching ARM behaviour in
        // all four addressing modes (IA/IB/DA/DB).
        for (var registerIndex = 0; registerIndex < RegisterCount; registerIndex++)
        {
            if ((registerList & (1 << registerIndex)) == 0)
                continue;

            if (instruction.Load) // ──────── LDM ────────
            {
                var loadedValue = _bus.Read32(currentAddress);

                if (registerIndex == PcIndex)
                {
                    // Align to word and flush pipeline by setting PC directly.
                    _registers[PcIndex] = loadedValue & ~1u;
                    programCounterWasWritten = true;
                }
                else
                {
                    _registers[registerIndex] = loadedValue;
                }
            }
            else // ──────── STM ────────
            {
                var valueToStore = registerIndex == PcIndex
                    ? _registers[PcIndex] + 12u // PC is 8 ahead + 4 pipeline bubble
                    : _registers[registerIndex];

                _bus.Write32(currentAddress, valueToStore);
            }

            currentAddress += addressDelta;
        }

        // ─── Write-back (if W-bit set) ─────────────────────────────────────
        if (instruction.WriteBack)
        {
            var finalBaseValue = instruction.AddOffset
                ? baseRegisterInitialValue + 4u * (uint)totalRegisterCount
                : baseRegisterInitialValue - 4u * (uint)totalRegisterCount;

            _registers[instruction.BaseRegister] = finalBaseValue;
            if (instruction.BaseRegister == PcIndex)
                programCounterWasWritten = true;
        }

        // S-bit (PSR / user-mode) is ignored for now – no mode switching yet.

        return programCounterWasWritten;
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
