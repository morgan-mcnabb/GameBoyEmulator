using System.Reflection;
using Core.Abstract;

namespace Core.Memory.Regions;

/// <summary>
/// Same semantics as <see cref="WritableMemoryRegion"/> but throws on writes.
/// </summary>
public sealed class ReadOnlyMemoryRegion : IMemoryRegion
{
    private readonly WritableMemoryRegion _inner;

    public ReadOnlyMemoryRegion(uint startAddress, byte[] backingData) 
    {
        _inner = new WritableMemoryRegion(startAddress, (uint)backingData.Length);
        backingData.AsSpan().CopyTo(_innerBytes());
    }

    private Span<byte> _innerBytes() => ((byte[]?)typeof(WritableMemoryRegion)
        .GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance)!
        .GetValue(_inner))!;
    
    public bool Handles(uint address) => _inner.Handles(address);
    
    public byte Read8(uint address) => _inner.Read8(address);
    public ushort Read16(uint address) => _inner.Read16(address);
    public uint Read32(uint address) => _inner.Read32(address);

    public void Write8(uint address, byte _) => ThrowReadOnly(address);
    public void Write16(uint address, ushort _) => ThrowReadOnly(address);
    public void Write32(uint address, uint _) => ThrowReadOnly(address);
    private static void ThrowReadOnly(uint address) => throw new InvalidOperationException($"Attempted to write to read-only region at 0x{address:X8}");
}