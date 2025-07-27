using System.Reflection;
using Core.Cpu;
using Core.Cpu.Decoding;
using Core.Memory.Regions;
using FluentAssertions;

namespace Core.Tests.Cpu;

public sealed class DataProcessingExtendedTests
{
    /*──────────────────────────── Helpers ────────────────────────────*/

    private static uint EncodeRegisterForm(
        DataProcessingOpcode opcode,
        bool                 setConditionCodes,
        int                  rn,
        int                  rd,
        int                  rm)
    {
        var instruction = 0xE000_0000u;
        instruction |= ((uint)opcode & 0xF) << 21;
        if (setConditionCodes) instruction |= 1u << 20;
        instruction |= (uint)rn << 16;
        instruction |= (uint)rd << 12;
        instruction |= (uint)rm; // No shift bits
        return instruction;
    }

    private static Arm7TdmiCpu BuildCpuWithRegisters(
        uint instructionWord,
        uint rnValue,
        uint rmValue,
        bool carryFlagIn,
        int  rnIndex = 1,
        int  rdIndex = 0,
        int  rmIndex = 2)
    {
        // ROM holds just the instruction under test
        var rom = new WritableMemoryRegion(0x0000_0000, 4);
        rom.Write32(0, instructionWord);

        var cpu = new Arm7TdmiCpu(new SystemBusBuilder().Add(rom).Build());

        var registersField = typeof(Arm7TdmiCpu)
            .GetField("_registers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var registers = (uint[])registersField.GetValue(cpu)!;
        registers[rnIndex] = rnValue;
        registers[rmIndex] = rmValue;

        var cpsrField = typeof(Arm7TdmiCpu)
            .GetField("_currentProgramStatusRegister", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var psr = (ProgramStatusRegister)cpsrField.GetValue(cpu)!;
        psr.Carry = carryFlagIn;
        cpsrField.SetValue(cpu, psr);

        return cpu;
    }

    private static (uint destination, ProgramStatusRegister flags) RunAndCapture(
        Arm7TdmiCpu cpu,
        int         destinationIndex = 0)
    {
        cpu.Step(); // single instruction

        var registers = (uint[])typeof(Arm7TdmiCpu)
            .GetField("_registers", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cpu)!;

        var psr = (ProgramStatusRegister)typeof(Arm7TdmiCpu)
            .GetField("_currentProgramStatusRegister", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cpu)!;

        return (registers[destinationIndex], psr);
    }

    /*──────────────────── Logical-operation vectors ───────────────────*/

    public static IEnumerable<object[]> LogicalVectors()
    {
        // opcode, setS, Rn, Rm, carryIn,
        // expectedResult, expectN, expectZ, expectCarry
        yield return [DataProcessingOpcode.And, true,  0xF0F0u,        0x0FF0u, false, 0x00F0u,          false, false, false];
        yield return [DataProcessingOpcode.Eor, true,  0xAA55u,        0xFFFFu, false, 0x55AAu,          false, false, false];
        yield return [DataProcessingOpcode.Orr, true,  0x8000_0000u,   1u,      false, 0x8000_0001u,     true,  false, false];
        yield return [DataProcessingOpcode.Bic, true,  0xFF00u,        0x0F0Fu, true,  0xF000u,          false, false, true ];
        yield return [DataProcessingOpcode.Mov, true,  0u,             0xABu,   false, 0xABu,            false, false, false];
        yield return [DataProcessingOpcode.Mvn, true,  0u,             0u,      false, 0xFFFF_FFFFu,     true,  false, false];
    }

    [Theory]
    [MemberData(nameof(LogicalVectors))]
    public void Logical_operations_set_result_and_flags_correctly(
        DataProcessingOpcode opcode,
        bool                 setConditionCodes,
        uint                 rnValue,
        uint                 rmValue,
        bool                 carryIn,
        uint                 expectedResult,
        bool                 expectedNegative,
        bool                 expectedZero,
        bool                 expectedCarry)
    {
        var instruction = EncodeRegisterForm(opcode, setConditionCodes, 1, 0, 2);
        var cpu         = BuildCpuWithRegisters(instruction, rnValue, rmValue, carryIn);

        var (destination, flags) = RunAndCapture(cpu);

        if (opcode is not (DataProcessingOpcode.Tst or DataProcessingOpcode.Teq))
            destination.Should().Be(expectedResult);

        flags.Negative.Should().Be(expectedNegative);
        flags.Zero    .Should().Be(expectedZero);
        flags.Carry   .Should().Be(expectedCarry);
    }

    /*─────────────────── Add / ADC / CMN vectors ─────────────────────*/

    public static IEnumerable<object[]> AddVectors()
    {
        // opcode, carryIn, left, right,
        // expectedResult, expectCarry, expectOverflow
        yield return [DataProcessingOpcode.Add, false, 1u,            2u,            3u,            false, false];
        yield return [DataProcessingOpcode.Add, false, 0xFFFF_FFFFu,  1u,            0u,            true,  false];
        yield return [DataProcessingOpcode.Add, false, 0x7FFF_FFFFu,  1u,            0x8000_0000u, false, true ];
        yield return [DataProcessingOpcode.Adc, true,  2u,            2u,            5u,            false, false];
        // CMN (flags only)
        yield return [DataProcessingOpcode.Cmn, false, 2u,            0xFFFF_FFFFu,  1u,            true,  false];
    }

    [Theory]
    [MemberData(nameof(AddVectors))]
    public void Add_family_behaves_correctly(
        DataProcessingOpcode opcode,
        bool                 carryIn,
        uint                 leftOperand,
        uint                 rightOperand,
        uint                 expectedResult,
        bool                 expectedCarry,
        bool                 expectedOverflow)
    {
        var instruction = EncodeRegisterForm(opcode, true, 1, 0, 2);
        var cpu         = BuildCpuWithRegisters(instruction, leftOperand, rightOperand, carryIn);

        var (destination, flags) = RunAndCapture(cpu);

        if (opcode != DataProcessingOpcode.Cmn)
            destination.Should().Be(expectedResult);

        flags.Carry   .Should().Be(expectedCarry);
        flags.Overflow.Should().Be(expectedOverflow);
    }

    public static IEnumerable<object[]> SubVectors()
    {
        // opcode, carryIn, left, right,
        // expectedResult, expectCarry, expectOverflow
        yield return [DataProcessingOpcode.Sub, false, 3u,            1u,            2u,             true,  false];
        yield return [DataProcessingOpcode.Sub, false, 0u,            1u,            0xFFFF_FFFFu,   false, false];
        yield return [DataProcessingOpcode.Rsb, false, 1u,            3u,            2u,             true, false ];
        yield return [DataProcessingOpcode.Sbc, false, 5u,            3u,            1u,             true,  false];
        // CMP (flags only)
        yield return [DataProcessingOpcode.Cmp, false, 0u,            1u,            0u,             false, false];
    }

    [Theory]
    [MemberData(nameof(SubVectors))]
    public void Subtract_family_behaves_correctly(
        DataProcessingOpcode opcode,
        bool                 carryIn,
        uint                 leftOperand,
        uint                 rightOperand,
        uint                 expectedResult,
        bool                 expectedCarry,
        bool                 expectedOverflow)
    {
        var instruction = EncodeRegisterForm(opcode, true, 1, 0, 2);
        var cpu         = BuildCpuWithRegisters(instruction, leftOperand, rightOperand, carryIn);

        var (destination, flags) = RunAndCapture(cpu);

        if (opcode != DataProcessingOpcode.Cmp)
            destination.Should().Be(expectedResult);

        flags.Carry   .Should().Be(expectedCarry);
        flags.Overflow.Should().Be(expectedOverflow);
    }

    [Fact]
    public void Tst_sets_flags_without_modifying_destination()
    {
        var instruction = EncodeRegisterForm(DataProcessingOpcode.Tst, true, 1, 0, 2);
        var cpu         = BuildCpuWithRegisters(instruction, 0xFFFFu, 0xFFFFu, false);

        var (destination, flags) = RunAndCapture(cpu);

        destination.Should().Be(0u);
        flags.Zero   .Should().BeFalse();
        flags.Negative.Should().BeFalse();
    }

    [Fact]
    public void Teq_sets_zero_flag_when_operands_equal()
    {
        var instruction = EncodeRegisterForm(DataProcessingOpcode.Teq, true, 1, 0, 2);
        var cpu         = BuildCpuWithRegisters(instruction, 0xBEEFu, 0xBEEFu, false);

        var (_, flags) = RunAndCapture(cpu);

        flags.Zero.Should().BeTrue();
    }
}
