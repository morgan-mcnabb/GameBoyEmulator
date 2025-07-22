using System;
using System.Runtime.CompilerServices;
using Core.Abstract;

namespace Core.Memory.Regions;

/// <summary>
/// Little-Endian RAM/ROM mirror that allows both reads and writes.
/// For 16/32 bit ops the GBA ignores the lowest address bits so we emulate that
/// by masking <c>address</c> inside each method.
/// </summary>
public sealed class WritableMemoryRegion(uint startAddress, uint sizeBytes) : MemoryRegionBase(startAddress, sizeBytes)
{
    public override byte Read8(uint address) => Load8(Offset(address));
    public override ushort Read16(uint address) => Load16(Offset(address & ~1u));
    public override uint Read32(uint address) => Load32Aligned(Offset(address & ~3u));
    public override void Write8(uint address, byte value) => Store8(Offset(address), value);
    public override void Write16(uint address, ushort value) => Store16(Offset(address & ~1u), value);
    public override void Write32(uint address, uint value) => Store32Aligned(Offset(address & ~3u), value);
    
}