// Core.Tests/Cpu/BlockDataTransferTests.cs
/*
 * Comprehensive validation of the Arm7TdmiCpu block-transfer implementation.
 *
 *  ∘ All 4 addressing modes (IA, IB, DA, DB)
 *  ∘ Both directions   (Load / Store)
 *  ∘ Write-back off/on
 *  ∘ Edge cases:
 *       – Empty register list  ⇒ PC-only transfer
 *       – Storing PC ensures PC+12 value is written
 *
 * Test strategy:
 *  1. Build an isolated SystemBus with:
 *       –  4 KiB “BIOS” region at 0x0000_0000   (contains the test instruction)
 *       –  8 KiB “RAM”  region at 0x0200_0000   (target for loads/stores)
 *  2. Use reflection to initialise / read the CPU’s private register array.
 *  3. Execute cpu.Step() once.
 *  4. Assert on:
 *       – RAM contents   (for stores)
 *       – Register values and PC (for loads)
 *       – Base-register write-back behaviour
 *
 * Frameworks: xUnit + FluentAssertions
 */

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Core.Cpu;
using Core.Memory;
using Core.Memory.Regions;
using FluentAssertions;
using Xunit;

namespace Core.Tests.Cpu;

public sealed class BlockDataTransferTests
{
    private const uint CodeRegionStart = 0x0000_0000u;
    private const uint DataRegionStart = 0x0200_0000u;

    // ──────────────────────────────────────────────────────────────────────
    // Helpers (encoding, CPU bootstrap, register access)
    // ──────────────────────────────────────────────────────────────────────
    private static uint EncodeBlockTransfer(
        bool  load,         // true = LDM, false = STM
        bool  preIndex,     // P-bit
        bool  addOffset,    // U-bit
        bool  writeBack,    // W-bit
        int   baseRegister, // Rn (0-15)
        ushort registerList // 16-bit register bitmap
    )
    {
        const uint ConditionAlways   = 0xE000_0000u; // bits 31-28 = 1110 (AL)
        const uint BlockTransferTag  = 0x0800_0000u; // bits 27-25 = 100 (LDM/STM)

        uint opcode = ConditionAlways | BlockTransferTag;

        if (preIndex)  opcode |= 1u << 24; // P
        if (addOffset) opcode |= 1u << 23; // U
        /* bit-22 (S) left clear – user-mode PSR transfer not tested here     */
        if (writeBack) opcode |= 1u << 21; // W
        if (load)      opcode |= 1u << 20; // L

        opcode |= (uint)baseRegister << 16; // Rn field
        opcode |= registerList;             // register list (bits 15-0)

        return opcode;
    }

    private static Arm7TdmiCpu CreateCpuInstance(
        uint encodedInstruction,
        out WritableMemoryRegion dataRam)
    {
        // 1. “BIOS” region with the single instruction
        var bios = new WritableMemoryRegion(CodeRegionStart, 4 * 1024);
        bios.Write32(CodeRegionStart, encodedInstruction);

        // 2. RAM region used for loads/stores
        dataRam = new WritableMemoryRegion(DataRegionStart, 8 * 1024);

        var bus = new SystemBusBuilder()
                  .Add(bios)
                  .Add(dataRam)
                  .Build();

        return new Arm7TdmiCpu(bus);
    }

    private static uint[] GetRegisterArray(Arm7TdmiCpu cpu)
        => (uint[])typeof(Arm7TdmiCpu)
           .GetField("_registers", BindingFlags.NonPublic | BindingFlags.Instance)!
           .GetValue(cpu)!;

    // Calculates the sequence of addresses the CPU will use for the given mode.
    private static IReadOnlyList<uint> ExpectedTransferAddresses(
        uint baseAddress, bool preIndex, bool addOffset, int registerCount)
    {
        var addressDelta    = addOffset ? 4u : unchecked((uint)-4);
        var currentAddress  = preIndex ? baseAddress + addressDelta : baseAddress;

        var addresses = new List<uint>(registerCount);
        for (var i = 0; i < registerCount; i++)
        {
            addresses.Add(currentAddress);
            currentAddress += addressDelta;
        }

        return addresses;
    }

    private static uint ExpectedWriteBackValue(
        uint baseAddress, bool addOffset, bool writeBack, int registerCount)
        => writeBack
           ? (addOffset
                  ? baseAddress + (uint)registerCount * 4u
                  : baseAddress - (uint)registerCount * 4u)
           : baseAddress;

    // ──────────────────────────────────────────────────────────────────────
    // Parameter-generation for the “matrix” tests
    // ──────────────────────────────────────────────────────────────────────
    private record Mode(string Name, bool PreIndex, bool AddOffset);

    private static readonly Mode[] AddressingModes =
    {
        new("IA",  PreIndex:false, AddOffset:true ),
        new("IB",  PreIndex:true , AddOffset:true ),
        new("DA",  PreIndex:false, AddOffset:false),
        new("DB",  PreIndex:true , AddOffset:false),
    };

    // MemberData for store-matrix and load-matrix (4 modes × 2 W-bit) = 8 cases each.
    public static IEnumerable<object?[]> StoreMatrixCases =>
        from mode      in AddressingModes
        from writeBack in new[] { false, true }
        select new object?[]
        {
            mode.Name, mode.PreIndex, mode.AddOffset, writeBack
        };

    public static IEnumerable<object?[]> LoadMatrixCases => StoreMatrixCases;

    // ──────────────────────────────────────────────────────────────────────
    // 1 ▪ Store-matrix  (all addressing modes, W-bit off/on)
    // ──────────────────────────────────────────────────────────────────────
    [Theory, MemberData(nameof(StoreMatrixCases))]
    public void StoreMultiple_WritesExpectedMemory(
        string friendlyName,
        bool preIndex,
        bool addOffset,
        bool writeBack)
    {
        // Arrange: R0-R3 will be stored.   Base register = R4.
        const ushort registerList = 0b0000_0000_0000_1111; // R0-R3
        const int    baseRegisterIndex = 4;
        const uint   baseAddress = DataRegionStart + 0x100;

        var encoded = EncodeBlockTransfer(
            load:false, preIndex, addOffset, writeBack,
            baseRegisterIndex, registerList);

        var cpu = CreateCpuInstance(encoded, out var ram);
        var registers = GetRegisterArray(cpu);

        // Give every register a distinct value.
        for (var i = 0; i < registers.Length; i++)
        {
            if (i == 15) continue;
            registers[i] = 0xA000_0000u + (uint)i;
        }

        registers[baseRegisterIndex] = baseAddress;

        // Act
        cpu.Step();

        // Assert – memory contents
        var addresses = ExpectedTransferAddresses(
            baseAddress, preIndex, addOffset, registerCount:4);

        for (var i = 0; i < 4; i++)
        {
            var expectedValue = 0xA000_0000u + (uint)i; // R0..R3
            ram.Read32(addresses[i]).Should().Be(expectedValue,
                $"Mode={friendlyName}, Store index {i}");
        }

        // Assert – base register write-back
        var expectedBase = ExpectedWriteBackValue(
            baseAddress, addOffset, writeBack, 4);

        registers[baseRegisterIndex].Should().Be(expectedBase,
            $"Mode={friendlyName}, write-back {(writeBack ? "on" : "off")}");

        // Assert – PC advanced by 4 (no PC write in store-matrix cases)
        cpu.Pc.Should().Be(4u);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 2 ▪ Load-matrix  (all addressing modes, W-bit off/on)
    // ──────────────────────────────────────────────────────────────────────
    [Theory, MemberData(nameof(LoadMatrixCases))]
    public void LoadMultiple_LoadsExpectedRegisters(
        string friendlyName,
        bool preIndex,
        bool addOffset,
        bool writeBack)
    {
        // Arrange: load into R0-R3.  Base = R4.
        const ushort registerList = 0b0000_0000_0000_1111; // R0-R3
        const int    baseRegisterIndex = 4;
        const uint   baseAddress = DataRegionStart + 0x200;

        var encoded = EncodeBlockTransfer(
            load:true, preIndex, addOffset, writeBack,
            baseRegisterIndex, registerList);

        var cpu = CreateCpuInstance(encoded, out var ram);
        var registers = GetRegisterArray(cpu);

        registers[baseRegisterIndex] = baseAddress;

        // Pre-populate memory with known pattern.
        var addresses = ExpectedTransferAddresses(
            baseAddress, preIndex, addOffset, 4);

        for (var i = 0; i < 4; i++)
            ram.Write32(addresses[i], 0xB000_0000u + (uint)i);

        // Act
        cpu.Step();

        // Assert – registers loaded
        for (var i = 0; i < 4; i++)
        {
            var expected = 0xB000_0000u + (uint)i;
            registers[i].Should().Be(expected,
                $"Mode={friendlyName}, Load reg R{i}");
        }

        // Assert – base register write-back
        var expectedBase = ExpectedWriteBackValue(
            baseAddress, addOffset, writeBack, 4);

        registers[baseRegisterIndex].Should().Be(expectedBase);

        // Assert – PC advanced by 4 (PC not loaded in these cases)
        cpu.Pc.Should().Be(4u);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 3 ▪ Empty register list  ⇒ PC-only transfer (LDMIA, no W)
    // ──────────────────────────────────────────────────────────────────────
    [Fact]
    public void LoadMultiple_EmptyList_LoadsProgramCounterOnly()
    {
        const ushort registerList = 0x0000;            // empty ⇒ PC only
        const int    baseRegisterIndex = 0;            // R0 (arbitrary)
        const uint   baseAddress = DataRegionStart + 0x300;
        const uint   valueAtMemory = 0x1234_5679u;     // mis-aligned LSB=1

        var encoded = EncodeBlockTransfer(
            load:true, preIndex:false, addOffset:true, writeBack:false,
            baseRegisterIndex, registerList);

        var cpu = CreateCpuInstance(encoded, out var ram);
        var registers = GetRegisterArray(cpu);

        registers[baseRegisterIndex] = baseAddress;
        ram.Write32(baseAddress, valueAtMemory);

        // Act
        cpu.Step();

        // Assert – PC loaded & aligned
        cpu.Pc.Should().Be(valueAtMemory & ~1u);

        // Assert – base register unchanged (no W)
        registers[baseRegisterIndex].Should().Be(baseAddress);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 4 ▪ Store list that *includes* PC ⇒ verify PC+12 written
    // ──────────────────────────────────────────────────────────────────────
    [Fact]
    public void StoreMultiple_IncludesPc_WritesPcPlusTwelve()
    {
        // Store R15 only using STMIA with write-back off.
        const ushort registerList = 1 << 15;           // PC only
        const int    baseRegisterIndex = 1;            // R1
        const uint   baseAddress = DataRegionStart + 0x400;

        var encoded = EncodeBlockTransfer(
            load:false, preIndex:false, addOffset:true, writeBack:false,
            baseRegisterIndex, registerList);

        var cpu = CreateCpuInstance(encoded, out var ram);
        var registers = GetRegisterArray(cpu);

        registers[baseRegisterIndex] = baseAddress;
        registers[15] /*PC*/ = 0u; // CPU reset already sets PC=0

        // Act
        cpu.Step();

        // Assert – stored value == (original PC + 12)
        ram.Read32(baseAddress).Should().Be(12u);

        // Assert – PC after step advanced by 4 (normal flow)
        cpu.Pc.Should().Be(4u);
    }
}
