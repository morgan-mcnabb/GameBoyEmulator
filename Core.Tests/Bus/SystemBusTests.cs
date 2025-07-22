using Core.Memory;
using Core.Memory.Regions;
using FluentAssertions;

namespace Core.Tests.Bus;

public class SystemBusTests
{
    private const uint RamStart = 0x0200_0000;

    private readonly SystemBus _bus;

    public SystemBusTests()
    {
        var biosData = Enumerable.Range(0, 16 * 1024)
                                 .Select(i => (byte)i)  // predictable pattern
                                 .ToArray();

        var bios = new ReadOnlyMemoryRegion(0x0000_0000, biosData);
        var ram = new WritableMemoryRegion(RamStart, 1024); // 1 KiB

        _bus = new SystemBusBuilder()
                  .Add(bios)
                  .Add(ram)
                  .Build();
    }

    [Fact]
    public void ReadWrite8_roundtrip_through_bus()
    {
        _bus.Write8(RamStart + 4, 0x7E);
        _bus.Read8 (RamStart + 4).Should().Be(0x7E);
    }

    [Fact]
    public void ReadWrite16_roundtrip_and_alignment()
    {
        _bus.Write16(RamStart + 5, 0xBEEF);             // unaligned write
        _bus.Read16 (RamStart + 4).Should().Be(0xBEEF); // even address
        _bus.Read16 (RamStart + 5).Should().Be(0xBEEF); // odd address maps same
    }

    [Fact]
    public void ReadWrite32_roundtrip_and_alignment()
    {
        _bus.Write32(RamStart + 3, 0xCAFEBABE);         // unaligned write
        _bus.Read32 (RamStart).Should().Be(0xCAFEBABE);
    }

    [Fact]
    public void Unmapped_reads_return_ff_fill()
    {
        _bus.Read8 (0x0500_0000).Should().Be(0xFF);
        _bus.Read16(0x0500_0000).Should().Be(0xFFFF);
        _bus.Read32(0x0500_0000).Should().Be(0xFFFF_FFFF);
    }

    [Fact]
    public void Unmapped_writes_do_not_throw()
    {
        _bus.Invoking(b => b.Write32(0x0600_0000, 0x12345678))
            .Should().NotThrow();
    }

    [Fact]
    public void Write_to_readonly_region_throws()
    {
        _bus.Invoking(b => b.Write8(0x0000_0000, 0xAA))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*read-only*");
    }

    [Fact]
    public void Reads_from_readonly_region_return_backing_data()
    {
        _bus.Read8(0x0000_0010).Should().Be(0x10); // biosData[0x10] == 0x10
    }
}