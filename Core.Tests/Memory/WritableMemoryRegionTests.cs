using Core.Memory.Regions;
using FluentAssertions;

namespace Core.Tests.Memory;

public class WritableMemoryRegionTests
{
    private const uint Start = 0x0200_0000;
    private readonly WritableMemoryRegion _ram = new(Start, 1024); // 1 KiB

    [Fact]
    public void Handles_returns_true_inside_bounds_and_false_outside()
    {
        _ram.Handles(Start).Should().BeTrue();
        _ram.Handles(Start + 512).Should().BeTrue();
        _ram.Handles(Start + 1023).Should().BeTrue();
        _ram.Handles(Start - 1).Should().BeFalse();
        _ram.Handles(Start + 1024).Should().BeFalse();
    }

    [Fact]
    public void ReadWrite8_roundtrip()
    {
        _ram.Write8(Start + 10, 0xAB);
        _ram.Read8(Start + 10).Should().Be(0xAB);
    }

    [Fact]
    public void ReadWrite16_roundtrip_and_alignment()
    {
        // write at odd address (should align to even internally)
        _ram.Write16(Start + 3, 0xBEEF);
        _ram.Read16(Start + 2).Should().Be(0xBEEF);   // even addr
        _ram.Read16(Start + 3).Should().Be(0xBEEF);   // odd addr maps same
    }

    [Fact]
    public void ReadWrite32_roundtrip_and_alignment()
    {
        _ram.Write32(Start + 5, 0xDEADBEEF);          // unaligned
        _ram.Read32(Start + 4).Should().Be(0xDEADBEEF);
        _ram.Read32(Start + 5).Should().Be(0xDEADBEEF);
    }  
}