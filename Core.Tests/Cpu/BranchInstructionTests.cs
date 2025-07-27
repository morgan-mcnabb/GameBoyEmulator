using System.Reflection;
using Core.Cpu;
using Core.Memory.Regions;
using FluentAssertions;

namespace Core.Tests.Cpu;

public sealed class BranchInstructionTests
{
    private static Arm7TdmiCpu CreateCpu(params uint[] programWords)
    {
        var rom = new WritableMemoryRegion(0x0000_0000, (uint)programWords.Length * 4);
        for (var i = 0; i < programWords.Length; i++)
            rom.Write32((uint)i * 4, programWords[i]);

        return new Arm7TdmiCpu(new SystemBusBuilder().Add(rom).Build());
    }

    private static uint GetRegister(Arm7TdmiCpu cpu, int index)
        => ((uint[])typeof(Arm7TdmiCpu)
               .GetField("_registers", BindingFlags.NonPublic | BindingFlags.Instance)!
               .GetValue(cpu)!)[index];

    private static ProgramStatusRegister GetCpsr(Arm7TdmiCpu cpu)
        => (ProgramStatusRegister)typeof(Arm7TdmiCpu)
               .GetField("_currentProgramStatusRegister", BindingFlags.NonPublic | BindingFlags.Instance)!
               .GetValue(cpu)!;

    private static uint EncodeBranch(ConditionCode condition, bool link, int wordOffset)
    {
        var imm24 = (uint)wordOffset & 0x00FF_FFFFu; // 24-bit 2’s-comp
        var opcode = ((uint)condition << 28)          // cond
                     | 0x0A_00_00_00u                 // 101 tag
                     | (link ? 1u << 24 : 0u)         // link bit
                     | imm24;
        return opcode;
    }


    [Fact]
    public void B_forward_updates_pc_correctly()
    {
        // PC=0, visible PC (=PC+8)=8, branch +1 word  => target 8+4 = 12
        var branch = EncodeBranch(ConditionCode.Always, link: false, wordOffset: +1);
        var cpu = CreateCpu(branch);

        cpu.Step();

        cpu.Pc.Should().Be(0x0000_000Cu);
        GetRegister(cpu, 14).Should().Be(0u, "LR must remain untouched for plain B");
    }

    [Fact]
    public void BL_sets_link_register_and_branches()
    {
        var branchLink = EncodeBranch(ConditionCode.Always, link: true, wordOffset: +1);
        var cpu = CreateCpu(branchLink);

        cpu.Step();

        cpu.Pc.Should().Be(0x0000_000Cu);
        GetRegister(cpu, 14).Should().Be(0x0000_0004u, "LR receives address of next sequential instruction");
    }

    [Fact]
    public void B_backward_with_negative_offset_works()
    {
        var branchBack = EncodeBranch(ConditionCode.Always, link: false, wordOffset: -2);
        var cpu = CreateCpu(branchBack);

        cpu.Step();

        cpu.Pc.Should().Be(0x0000_0000u);
    }

    public static IEnumerable<object[]> ConditionVectors()
    {
        yield return [ConditionCode.Equal,          true,  true ];
        yield return [ConditionCode.Equal,          false, false];
        yield return [ConditionCode.NotEqual,       false, true ];
        yield return [ConditionCode.NotEqual,       true,  false];
        yield return [ConditionCode.CarrySet,       /*C*/ true,  true ];
        yield return [ConditionCode.CarrySet,       /*C*/ false, false];
    }

    [Theory]
    [MemberData(nameof(ConditionVectors))]
    public void Conditional_branch_respects_cpsr_flags(
        ConditionCode condition,
        bool flagValue,
        bool expectTaken)
    {
        var branch = EncodeBranch(condition, link: false, wordOffset: +1);
        var cpu = CreateCpu(branch, 0xDEAD_BEEFu /* padding */);

        var cpsr = new ProgramStatusRegister
        {
            Zero  = condition is ConditionCode.Equal or ConditionCode.NotEqual ? flagValue : false,
            Carry = condition is ConditionCode.CarrySet ? flagValue : false
        };
        typeof(Arm7TdmiCpu)
            .GetField("_currentProgramStatusRegister", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cpu, cpsr);

        cpu.Step();

        var expectedPc = expectTaken ? 0x0000_000Cu : 0x0000_0004u;
        cpu.Pc.Should().Be(expectedPc);
    }

    [Fact]
    public void B_with_max_positive_offset_branches_correctly()
    {
        var branch = EncodeBranch(ConditionCode.Always, link: false, wordOffset: 0x7FFFFF);
        var cpu = CreateCpu(branch);

        cpu.Step();

        cpu.Pc.Should().Be(0x0200_0004u);
    }

    [Fact]
    public void B_with_max_negative_offset_branches_correctly()
    {
        var branch = EncodeBranch(ConditionCode.Always, link: false, wordOffset: unchecked((int)0xFF800000u));
        var cpu = CreateCpu(branch);

        cpu.Step();

        cpu.Pc.Should().Be(unchecked(0xFE00_0008u)); 
    }

    [Fact]
    public void Branch_does_not_modify_cpsr_flags()
    {
        var branch = EncodeBranch(ConditionCode.Always, link: false, wordOffset: +1);
        var cpu = CreateCpu(branch);

        var originalPsr = new ProgramStatusRegister
        {
            Negative = true,
            Zero     = true,
            Carry    = true,
            Overflow = true
        };
        typeof(Arm7TdmiCpu)
            .GetField("_currentProgramStatusRegister", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cpu, originalPsr);

        cpu.Step();

        GetCpsr(cpu).Should().Be(originalPsr);
    }
}
