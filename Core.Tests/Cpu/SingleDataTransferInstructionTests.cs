using System.Reflection;
using Core.Cpu;
using Core.Cpu.Decoding;
using Core.Memory.Regions;
using FluentAssertions;

namespace Core.Tests.Cpu;

public sealed class SingleDataTransferInstructionTests
{
    private const uint RamBaseAddress = 0x0200_0000;
    private const int  RegisterCount  = 16;

    private static uint EncodeSingleDataTransfer(
        ConditionCode condition,
        bool          useRegisterOffset,
        bool          preIndexed,
        bool          addOffset,
        bool          byteTransfer,
        bool          writeBack,
        bool          loadOperation,
        int           baseRegisterIndex,
        int           sourceDestRegisterIndex,
        uint          offsetField)
    {
        var opcode = ((uint)condition << 28) | 0x0400_0000u; 

        if (useRegisterOffset) opcode |= 1u << 25; 
        if (preIndexed)        opcode |= 1u << 24;
        if (addOffset)         opcode |= 1u << 23; 
        if (byteTransfer)      opcode |= 1u << 22; 
        if (writeBack)         opcode |= 1u << 21; 
        if (loadOperation)     opcode |= 1u << 20; 

        opcode |= (uint)baseRegisterIndex       << 16;
        opcode |= (uint)sourceDestRegisterIndex << 12;
        opcode |= offsetField & 0xFFFu;

        return opcode;
    }

    private static (Arm7TdmiCpu Cpu, WritableMemoryRegion Ram) BuildSystem(
        uint instructionWord,
        uint ramStart = RamBaseAddress,
        uint ramSize  = 4096)
    {
        var rom = new WritableMemoryRegion(0x0000_0000u, 4);
        rom.Write32(0, instructionWord);

        var ram = new WritableMemoryRegion(ramStart, ramSize);

        var bus = new SystemBusBuilder()
                  .Add(rom)
                  .Add(ram)
                  .Build();

        return (new Arm7TdmiCpu(bus), ram);
    }

    private static uint[] GetRegisterArray(Arm7TdmiCpu cpu) =>
        (uint[])typeof(Arm7TdmiCpu)
            .GetField("_registers", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cpu)!;

    private static void InitialiseRegisters(
        Arm7TdmiCpu cpu,
        params (int Index, uint Value)[] initializers)
    {
        var registers = GetRegisterArray(cpu);
        foreach (var (index, value) in initializers)
            registers[index] = value;
    }

    private static uint ReadRegister(Arm7TdmiCpu cpu, int index) =>
        GetRegisterArray(cpu)[index];

    /* ────────────────────────── Immediate-offset (pre-indexed) ───────────────── */

    public static IEnumerable<object[]> ImmediatePreIndexedMatrix()
    {
      
        yield return [true,  false, true,  false]; // LDR
        yield return [false, false, true,  false]; // STR
        yield return [true,  true,  true,  false]; // LDRB
        yield return [false, true,  true,  false]; // STRB
        yield return [true,  false, false, false]; // LDR (subtract)
        yield return [false, false, false, false]; // STR (subtract)
        yield return [true,  false, true,  true]; // LDR with write‑back
        yield return [false, false, false, true]; // STR with write‑back (subtract)
    }

    [Theory]
    [MemberData(nameof(ImmediatePreIndexedMatrix))]
    public void Immediate_preindexed_behaviour_is_correct(
        bool loadOperation,
        bool byteTransfer,
        bool addOffset,
        bool writeBack)
    {
        const int  baseRegisterIndex = 1;
        const int  dataRegisterIndex = 0;
        const uint offset            = 0x10u;

        var instruction = EncodeSingleDataTransfer(
            ConditionCode.Always,
            useRegisterOffset: false,
            preIndexed:        true,
            addOffset,
            byteTransfer,
            writeBack,
            loadOperation,
            baseRegisterIndex,
            dataRegisterIndex,
            offset);

        var (cpu, ram) = BuildSystem(instruction);

        // choose a base so the effective address always lies within the RAM region
        var baseAddress = addOffset ? RamBaseAddress : RamBaseAddress + offset;
        InitialiseRegisters(cpu, (baseRegisterIndex, baseAddress));

        const uint testWord = 0xDEADBEEFu;
        const byte testByte = 0xAB;

        var effectiveAddress = addOffset
            ? baseAddress + offset
            : baseAddress - offset;

        if (loadOperation)
        {
            if (byteTransfer)
                ram.Write8(effectiveAddress, testByte);
            else
                ram.Write32(effectiveAddress, testWord);
        }
        else // store
        {
            var valueToStore = byteTransfer ? testByte : testWord;
            InitialiseRegisters(cpu, (dataRegisterIndex, valueToStore));
        }

        cpu.Step();

        if (loadOperation)
        {
            var expectedLoadedValue = byteTransfer ? testByte : testWord;
            ReadRegister(cpu, dataRegisterIndex).Should().Be(expectedLoadedValue);
        }
        else
        {
            if (byteTransfer)
                ram.Read8(effectiveAddress).Should().Be(testByte);
            else
                ram.Read32(effectiveAddress).Should().Be(testWord);
        }

        var expectedBase = writeBack ? effectiveAddress : baseAddress;
        ReadRegister(cpu, baseRegisterIndex).Should().Be(expectedBase);
    }

    /* ────────────────────────── Post-indexed write‑back ─────────────────────── */

    [Fact]
    public void Postindexed_addition_updates_base_after_transfer()
    {
        const uint baseAddress  = RamBaseAddress + 0x10u;
        const uint offset       = 8u;
        const int  baseRegister = 1;
        const int  destination  = 0;

        var instruction = EncodeSingleDataTransfer(
            ConditionCode.Always,
            useRegisterOffset: false,
            preIndexed:        false,            // post‑indexed
            addOffset:         true,
            byteTransfer:      false,
            writeBack:         false,            // ignored for post‑indexed
            loadOperation:     true,
            baseRegisterIndex: baseRegister,
            sourceDestRegisterIndex: destination,
            offsetField: offset);

        var (cpu, ram) = BuildSystem(instruction);

        ram.Write32(baseAddress, 0xCAFEBABEu);
        InitialiseRegisters(cpu, (baseRegister, baseAddress));

        cpu.Step();

        ReadRegister(cpu, destination).Should().Be(0xCAFEBABEu);
        ReadRegister(cpu, baseRegister).Should().Be(baseAddress + offset);
    }

    [Fact]
    public void Register_offset_with_lsl_is_honoured()
    {
        const uint offsetField = (2u << 7) | (0u << 5) | 2u;

        var instruction = EncodeSingleDataTransfer(
            ConditionCode.Always,
            useRegisterOffset: true,
            preIndexed:        true,
            addOffset:         true,
            byteTransfer:      false,
            writeBack:         false,
            loadOperation:     true,
            baseRegisterIndex: 1,
            sourceDestRegisterIndex: 0,
            offsetField: offsetField);

        var (cpu, ram) = BuildSystem(instruction);
        InitialiseRegisters(cpu,
            (1, RamBaseAddress),
            (2, 3u)); 

        const uint effectiveAddress = RamBaseAddress + 12u;
        ram.Write32(effectiveAddress, 0xA1B2C3D4u);

        cpu.Step();

        ReadRegister(cpu, 0).Should().Be(0xA1B2C3D4u);
    }

    [Theory]
    [InlineData(1, 0x11223344u, 0x44112233u)]
    [InlineData(2, 0xAABBCCDDu, 0xCCDDAABBu)]
    [InlineData(3, 0x01234567u, 0x23456701u)]
    public void Unaligned_word_load_rotates_result_correctly(
        int  addressOffset,
        uint storedAlignedWord,
        uint expectedRotatedResult)
    {
        var instruction = EncodeSingleDataTransfer(
            ConditionCode.Always,
            useRegisterOffset: false,
            preIndexed:        true,
            addOffset:         true,
            byteTransfer:      false,
            writeBack:         false,
            loadOperation:     true,
            baseRegisterIndex: 1,
            sourceDestRegisterIndex: 0,
            offsetField: (uint)addressOffset);

        var (cpu, ram) = BuildSystem(instruction);

        const uint alignedAddress = RamBaseAddress;
        ram.Write32(alignedAddress, storedAlignedWord);

        // base register points to aligned address so that effectiveAddress = base + offset.
        InitialiseRegisters(cpu, (1, alignedAddress));

        cpu.Step();

        ReadRegister(cpu, 0).Should().Be(expectedRotatedResult);
    }

    /* ────────────────────────── PC‑relative literal load ───────────────────── */

    [Fact]
    public void Pc_relative_literal_load_reads_correct_word()
    {
        var instruction = EncodeSingleDataTransfer(
            ConditionCode.Always,
            useRegisterOffset: false,
            preIndexed:        true,
            addOffset:         true,
            byteTransfer:      false,
            writeBack:         false,
            loadOperation:     true,
            baseRegisterIndex: 15,
            sourceDestRegisterIndex: 0,
            offsetField: 4);

        var rom = new WritableMemoryRegion(0x0000_0000u, 16u);
        rom.Write32(0, instruction);  
        rom.Write32(12u, 0xFEEDFACEu); 

        var bus = new SystemBusBuilder().Add(rom).Build();
        var cpu = new Arm7TdmiCpu(bus);

        cpu.Step();

        ReadRegister(cpu, 0).Should().Be(0xFEEDFACEu);
        cpu.Pc.Should().Be(4u);
    }

    [Fact]
    public void Store_using_program_counter_register_writes_pc_plus_12()
    {
        var instruction = EncodeSingleDataTransfer(
            ConditionCode.Always,
            useRegisterOffset: false,
            preIndexed:        true,
            addOffset:         true,
            byteTransfer:      false,
            writeBack:         false,
            loadOperation:     false,
            baseRegisterIndex: 1,
            sourceDestRegisterIndex: 15,
            offsetField: 0);

        const uint destinationAddress = RamBaseAddress + 0x20u;

        var (cpu, ram) = BuildSystem(instruction);
        InitialiseRegisters(cpu, (1, destinationAddress));

        cpu.Step();

        ram.Read32(destinationAddress).Should().Be(12u); // PC (0) + 12
    }

    [Fact]
    public void Decoder_recognises_single_data_transfer_pattern()
    {
        var sample = EncodeSingleDataTransfer(
            ConditionCode.Always,
            useRegisterOffset: false,
            preIndexed:        true,
            addOffset:         true,
            byteTransfer:      false,
            writeBack:         false,
            loadOperation:     true,
            baseRegisterIndex: 2,
            sourceDestRegisterIndex: 0,
            offsetField: 0x20);

        ArmInstructionDecoder.TryDecodeSingleDataTransfer(sample, out var decoded)
            .Should().BeTrue();

        decoded.Load.Should().BeTrue();
        decoded.ByteTransfer.Should().BeFalse();
        decoded.PreIndexing.Should().BeTrue();
        decoded.AddOffset.Should().BeTrue();
        decoded.WriteBack.Should().BeFalse();
        decoded.BaseRegister.Should().Be(2);
        decoded.SourceDestRegister.Should().Be(0);
        decoded.OffsetField.Should().Be(0x20u);
    }

    [Fact]
    public void Decoder_rejects_non_single_data_transfer_opcodes()
    {
        const uint randomAdd = 0xE080_0002u; 
        ArmInstructionDecoder.TryDecodeSingleDataTransfer(randomAdd, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Condition_mismatch_skips_memory_operation()
    {
        var instruction = EncodeSingleDataTransfer(
            ConditionCode.NotEqual,        
            useRegisterOffset: false,
            preIndexed:        false,
            addOffset:         true,
            byteTransfer:      false,
            writeBack:         false,
            loadOperation:     true,
            baseRegisterIndex: 1,
            sourceDestRegisterIndex: 0,
            offsetField: 4);

        var (cpu, ram) = BuildSystem(instruction);
        InitialiseRegisters(cpu, (1, RamBaseAddress));

        // Force Z flag set so condition fails
        typeof(Arm7TdmiCpu)
            .GetField("_currentProgramStatusRegister", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cpu, new ProgramStatusRegister { Zero = true });

        ram.Write32(RamBaseAddress, 0x12345678u);

        cpu.Step();

        ReadRegister(cpu, 0).Should().Be(0u, "instruction should be treated as NOP");
        ReadRegister(cpu, 1).Should().Be(RamBaseAddress, "base register must stay unchanged");
        ram.Read32(RamBaseAddress).Should().Be(0x12345678u);
    }
}
