namespace Core.Abstract;

/// <summary>
/// minimal contract for any CPU implementation in the emulator
/// </summary>
public interface ICpu
{
    /// <summary>
    /// Program Counter.
    /// </summary>
    uint Pc { get; }

    /// <summary>
    /// executes exactly one fetch-decode-execute cycle/
    /// </summary>
    void Step();
}