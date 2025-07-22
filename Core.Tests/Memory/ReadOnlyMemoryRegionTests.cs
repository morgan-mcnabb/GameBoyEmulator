using Core.Memory.Regions;
using FluentAssertions;
namespace Core.Tests.Memory;

public class ReadOnlyMemoryRegionTests
{
    private const uint BiosAddr = 0x0000_0000;
    private readonly byte[] _bios = Enumerable.Range(0, 16 * 1024)
        .Select(i => (byte)(i & 0xFF))
        .ToArray();

    private readonly ReadOnlyMemoryRegion _rom;

    public ReadOnlyMemoryRegionTests() => _rom = new ReadOnlyMemoryRegion(BiosAddr, _bios);

    [Fact]
    public void Handles_matches_span()
    {
        _rom.Handles(BiosAddr).Should().BeTrue();
        _rom.Handles(BiosAddr + 0x3FFF).Should().BeTrue();
        _rom.Handles(BiosAddr + 0x4000).Should().BeFalse();
    }

    [Fact]
    public void Read8_returns_backing_data()
    {
        _rom.Read8(BiosAddr + 123).Should().Be(_bios[123]);
    }

    [Fact]
    public void Write8_throws_InvalidOperationException()
    {
        var act = () => _rom.Write8(BiosAddr, 0xFF);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*read-only*");
    }
}