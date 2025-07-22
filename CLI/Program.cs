using Core.Memory.Regions;
using Core.Abstract;

var iwram = new WritableMemoryRegion(0x0300_0000, 32 * 1024);
iwram.Write32(0x0300_0004, 0xDEADBEEF);
Console.WriteLine($"Read32: 0x{iwram.Read32(0x300_0004):X8}");