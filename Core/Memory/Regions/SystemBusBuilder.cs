using Core.Abstract;

namespace Core.Memory.Regions;

public sealed class SystemBusBuilder
{
    private readonly List<IMemoryRegion> _regions = [];

    public SystemBusBuilder Add(IMemoryRegion region)
    {
        _regions.Add(region ?? throw new ArgumentNullException(nameof(region)));
        return this;
    }

    public SystemBus Build() => new(_regions);
}