using System.Reflection;
using Core.Cpu;
using Core.Memory.Regions;
using FluentAssertions;

namespace Core.Tests.Cpu;

public sealed class DataProcessingInstructionTests
{
    private static Arm7TdmiCpu CreateCpuWithProgram(params uint[] programWords)
    {
        var rom = new WritableMemoryRegion(0x0000_0000, (uint)(programWords.Length * 4));
        for (var i = 0; i < programWords.Length; i++)
        {
            rom.Write32((uint)(i * 4), programWords[i]);
        }

        var bus = new SystemBusBuilder().Add(rom).Build();
        return new Arm7TdmiCpu(bus);
    }

    private static uint GetRegister(Arm7TdmiCpu cpu, int index)
    {
        var registers = (uint[])typeof(Arm7TdmiCpu)
            .GetField("_registers", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cpu)!;
        return registers[index];
    }

    private static ProgramStatusRegister GetCpsr(Arm7TdmiCpu cpu)
    {
        return (ProgramStatusRegister)typeof(Arm7TdmiCpu)
            .GetField("_currentProgramStatusRegister", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cpu)!;
    }


    [Fact]
    public void MovImmediate_SetsRegisterAndFlags()
    {
        const uint movInstruction = 0xE3B0_0001;
        var cpu = CreateCpuWithProgram(movInstruction);

        cpu.Step();

        GetRegister(cpu, 0).Should().Be(1);
        cpu.Pc.Should().Be(4);

        var cpsr = GetCpsr(cpu);
        cpsr.Zero.Should().BeFalse();
        cpsr.Negative.Should().BeFalse();
        cpsr.Carry.Should().BeFalse();
    }

    [Fact]
    public void AddImmediate_ProducesCorrectResultAndFlags()
    {
        var movR1_2 = 0xE3A0_1002;
        var addR2_R1_3 = 0xE291_2003;
        var cpu = CreateCpuWithProgram(movR1_2, addR2_R1_3);

        cpu.Step(); 
        cpu.Step(); 

        GetRegister(cpu, 1).Should().Be(2);
        GetRegister(cpu, 2).Should().Be(5);
        cpu.Pc.Should().Be(8);

        var cpsr = GetCpsr(cpu);
        cpsr.Zero.Should().BeFalse();
        cpsr.Negative.Should().BeFalse();
        cpsr.Carry.Should().BeFalse();
        cpsr.Overflow.Should().BeFalse();
    }

    [Fact]
    public void SubImmediate_SetsBorrowAndNegativeFlag()
    {
        var movR3_0 = 0xE3A0_3000;
        var subR4_R3_1 = 0xE253_4001;
        var cpu = CreateCpuWithProgram(movR3_0, subR4_R3_1);

        cpu.Step(); // MOV
        cpu.Step(); // SUB

        GetRegister(cpu, 4).Should().Be(0xFFFF_FFFF);

        var cpsr = GetCpsr(cpu);
        cpsr.Negative.Should().BeTrue();
        cpsr.Zero.Should().BeFalse();
        cpsr.Carry.Should().BeFalse(); // borrow occurred
    }
    
    [Fact]
    public void AddRegisterShiftedOperand2()
    {
        var movR1_0x1 = 0xE3A0_1001; // R1 = 1
        var movR2_0x2 = 0xE3A0_2002; // R2 = 2
        var addR0_R1_R2_LSL1 = 0xE081_0082; // ADD R0, R1, R2, LSL #1 => R0 = R1 + (R2 << 1) = 1 + 4 = 5

        var cpu = CreateCpuWithProgram(movR1_0x1, movR2_0x2, addR0_R1_R2_LSL1);
        cpu.Step(); cpu.Step(); cpu.Step();

        GetRegister(cpu, 0).Should().Be(5);
        GetRegister(cpu, 1).Should().Be(1);
        GetRegister(cpu, 2).Should().Be(2);
    } 
}
