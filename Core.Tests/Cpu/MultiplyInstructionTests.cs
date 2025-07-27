using System.Reflection;
using Core.Cpu;
using Core.Cpu.Decoding;
using Core.Memory.Regions;
using FluentAssertions;

namespace Core.Tests.Cpu;

public sealed class MultiplyInstructionTests
{
    private static uint EncodeMultiply(
        bool accumulate,
        bool setConditionCodes,
        int  rd,
        int  rn,
        int  rs,
        int  rm)
    {
        var instruction = 0xE000_0090u;          // cond = 1110 (AL) + base MUL pattern
        if (accumulate)        instruction |= 1u << 21; // A bit
        if (setConditionCodes) instruction |= 1u << 20; // S bit
        instruction |= (uint)rd << 16;
        instruction |= (uint)rn << 12;
        instruction |= (uint)rs << 8;
        instruction |= (uint)rm;   
        return instruction;
    }

    private static Arm7TdmiCpu BuildCpu(
        uint instruction,
        uint[] registerValues,
        ProgramStatusRegister? psr = null)
    {
        // ROM holds a single 32‑bit instruction under test.
        var rom = new WritableMemoryRegion(0x0000_0000, 4);
        rom.Write32(0, instruction);

        var cpu = new Arm7TdmiCpu(new SystemBusBuilder().Add(rom).Build());

        var registersField = typeof(Arm7TdmiCpu)
            .GetField("_registers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var registers = (uint[])registersField.GetValue(cpu)!;
        registerValues.CopyTo(registers, 0);

        if (psr.HasValue)
            typeof(Arm7TdmiCpu)
                .GetField("_currentProgramStatusRegister", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(cpu, psr.Value);

        return cpu;
    }

    private static (uint[] Registers, ProgramStatusRegister Flags) RunAndCapture(Arm7TdmiCpu cpu)
    {
        cpu.Step();
        var registers = (uint[])typeof(Arm7TdmiCpu)
            .GetField("_registers", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cpu)!;
        var psr = (ProgramStatusRegister)typeof(Arm7TdmiCpu)
            .GetField("_currentProgramStatusRegister", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cpu)!;
        return (registers, psr);
    }

    public static IEnumerable<object[]> MulVectors()
    {
        // accumulate, S, Rm, Rs, Rn, expected, expectN, expectZ
        yield return [false, false, 3u, 4u,  0u,  12u, false, false];
        yield return [false, true,  0u, 123u,0u,   0u, false, true ];
        yield return [false, true,  0x8000_0000u, 1u, 0u, 0x8000_0000u, true, false];
        yield return [true,  false, 3u, 4u,  5u,  17u, false, false];
        yield return [true,  true,  0xFFFF_FFFFu, 2u, 3u, 1u, false, false];
    }

    [Theory]
    [MemberData(nameof(MulVectors))]
    public void Multiply_variants_produce_expected_results_and_flags(
        bool accumulate,
        bool setFlags,
        uint multiplicand,
        uint multiplier,
        uint addend,
        uint expectedResult,
        bool expectedNegative,
        bool expectedZero)
    {
        var rd = 0; // Destination R0
        var rn = 1; // Accumulate register (only used when accumulate == true)
        var rs = 2; // Multiplier
        var rm = 3; // Multiplicand

        var instruction = EncodeMultiply(accumulate, setFlags, rd, rn, rs, rm);

        var regs = new uint[16];
        regs[rm] = multiplicand;
        regs[rs] = multiplier;
        regs[rn] = addend;

        var psrSeed = new ProgramStatusRegister { Carry = true, Overflow = false };

        var cpu = BuildCpu(instruction, regs, psrSeed);
        var (afterRegs, afterFlags) = RunAndCapture(cpu);

        afterRegs[rd].Should().Be(expectedResult);

        if (setFlags)
        {
            afterFlags.Negative.Should().Be(expectedNegative);
            afterFlags.Zero    .Should().Be(expectedZero);
            afterFlags.Carry   .Should().Be(psrSeed.Carry);
            afterFlags.Overflow.Should().Be(psrSeed.Overflow);
        }
        else
        {
            // No flag update => entire CPSR unchanged.
            afterFlags.Should().Be(psrSeed);
        }
    }


    [Fact]
    public void TryDecodeMultiply_identifies_mul_pattern_correctly()
    {
        var sampleMul = EncodeMultiply(accumulate: false, setConditionCodes: false,
            rd: 0, rn: 0, rs: 1, rm: 2);

        var recognised = ArmInstructionDecoder.TryDecodeMultiply(sampleMul, out var decoded);
        recognised.Should().BeTrue();
        decoded.Accumulate.Should().BeFalse();
        decoded.DestinationRegister.Should().Be(0);
        decoded.OperandMRegister.Should().Be(2);
        decoded.OperandSRegister.Should().Be(1);
    }

    [Fact]
    public void TryDecodeMultiply_rejects_non_mul_opcodes()
    {
        // A random ADD (Rd=R0,Rn=R1,Rm=R2)
        const uint addInstruction = 0xE080_0002u;
        ArmInstructionDecoder.TryDecodeMultiply(addInstruction, out _)
            .Should().BeFalse();
    }
}