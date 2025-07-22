using Core.Abstract;
using Core.Memory.Regions;

namespace Core.Memory;

/// <inheritdoc cref="IMemoryBus"/>
public sealed class SystemBus : IMemoryBus
{
    // this will need to change when i add per-cycle accurate bus behavior.
    private const byte OpenBus8 = 0xFF;
    private const ushort OpenBus16 = 0xFFFF;
    private const uint OpenBus32 = 0xFFFF_FFFF;

    private readonly IReadOnlyList<IMemoryRegion> _regions;

    public SystemBus(IEnumerable<IMemoryRegion> regions) 
    {
        ArgumentNullException.ThrowIfNull(regions);
        _regions = regions.ToArray();
        if(_regions.Count == 0)
            throw new ArgumentException("No regions specified.");
    }

    private IMemoryRegion? FindRegion(uint address) => _regions.FirstOrDefault(region => region.Handles(address));

    public byte Read8(uint address)
    {
        var region = FindRegion(address);
        return region?.Read8(address) ?? OpenBus8;
    }

    public ushort Read16(uint address)
    {
        var region = FindRegion(address);
        return region?.Read16(address) ?? OpenBus16;
    }

    public uint Read32(uint address)
    {
        var region = FindRegion(address);
        return region?.Read32(address) ?? OpenBus32;
    }

    public void Write8(uint address, byte value)
    {
        FindRegion(address)?.Write8(address, value);
        // silent no-op if unmapped
    }

    public void Write16(uint address, ushort value)
    {
        FindRegion(address)?.Write16(address, value);
    }

    public void Write32(uint address, uint value)
    {
        FindRegion(address)?.Write32(address, value);
    }
}