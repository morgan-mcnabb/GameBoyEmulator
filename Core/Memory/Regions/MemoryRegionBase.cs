using System.Runtime.CompilerServices;
using Core.Abstract;

namespace Core.Memory.Regions;

public abstract class MemoryRegionBase : IMemoryRegion
{
    protected readonly uint _start, _end;
    protected readonly byte[] _buffer;

    protected MemoryRegionBase(uint startAddress, uint sizeBytes)
    {
        _start = startAddress;
        _end = _start + sizeBytes - 1;
        _buffer = new byte[sizeBytes];
    }

    protected MemoryRegionBase(uint startAddress, byte[] backingData)
    {
        _start = startAddress;
        _end = _start + (uint)backingData.Length - 1;
        _buffer = backingData;
    }
    
    public bool Handles(uint address) => address >= _start && address <= _end;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected int Offset(uint address) => (int)(address - _start);
    
    protected byte Load8 (int offset) => _buffer[offset];
    protected void Store8 (int offset, byte value) => _buffer[offset] = value;
    
    protected ushort Load16 (int offset) => (ushort)(_buffer[offset] | (_buffer[offset + 1] << 8));
    protected void Store16(int offset, ushort value)
    {
        _buffer[offset] = (byte)(value & 0xFF);
        _buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    protected uint Load32Aligned(int offset)
    {
        if ((uint)offset > _buffer.Length - 4)
            throw new IndexOutOfRangeException("32-but read straddles region boundary");
        
       return (uint)(_buffer[offset] |
               (_buffer[offset + 1] << 8) |
               (_buffer[offset + 2] << 16) |
               (_buffer[offset + 3] << 24));
    }

    protected void Store32Aligned(int offset, uint value)
    {
        if ((uint)offset > _buffer.Length - 4)
            throw new IndexOutOfRangeException("32-but read straddles region boundary"); 
        _buffer[offset] = (byte)(value & 0xFF);
        _buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        _buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        _buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
    
    // leave these abstract so those who inherit can decide writability
    public abstract byte Read8(uint address);
    public abstract ushort Read16(uint address);
    public abstract uint Read32(uint address);
    public abstract void Write8(uint address, byte value);
    public abstract void Write16(uint address, ushort value);
    public abstract void Write32(uint address, uint value);
    
}