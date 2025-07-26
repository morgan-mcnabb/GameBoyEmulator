using System.Collections.Generic;
using Core.Cpu;
using Core.Cpu.Decoding;
using FluentAssertions;
using Xunit;

namespace Core.Tests.Cpu;

/// <summary>
/// Exhaustive, data-driven tests for <see cref="BarrelShifter"/> covering:
/// • All four shift types (LSL, LSR, ASR, ROR/RRX)  
/// • Every architectural edge-case for the shift amount (0, 1, 31, 32, &gt; 32)  
/// • Both carry-in paths, plus verification of carry-out behaviour  
/// • Positive and negative input values for arithmetic right shifts
/// </summary>
public sealed class BarrelShifterTests
{
    [Theory]
    [MemberData(nameof(Vectors))]
    public void Apply_produces_expected_result_and_carry(
        uint inputValue,
        BarrelShifter.ShiftType shiftType,
        int shiftAmount,
        bool carryInFlag,
        uint expectedResult,
        bool expectedCarryOut)
    {
        // Act
        var actualResult = BarrelShifter.Apply(
            inputValue,
            shiftType,
            shiftAmount,
            carryInFlag,
            out var actualCarryOut);

        // Assert – both data and flags must match the reference values.
        actualResult.Should().Be(
            expectedResult,
            $"result of {shiftType} #{shiftAmount} applied to 0x{inputValue:X8}");

        actualCarryOut.Should().Be(
            expectedCarryOut,
            $"carry-out of {shiftType} #{shiftAmount} applied to 0x{inputValue:X8}");
    }

    /// <summary>
    /// Comprehensive reference vectors derived from the ARM ARM pseudo-code.
    /// </summary>
    public static IEnumerable<object[]> Vectors()
    {
        // ─────────── LSL (Logical Left) ───────────
        yield return [0x12345678u, BarrelShifter.ShiftType.LogicalLeft, 0,  false, 0x12345678u, false]; // carry unchanged (false)
        yield return [0x12345678u, BarrelShifter.ShiftType.LogicalLeft, 1,  false, 0x2468ACF0u, false];
        yield return [0x12345678u, BarrelShifter.ShiftType.LogicalLeft, 31, false, 0x00000000u, false];
        yield return [0x12345678u, BarrelShifter.ShiftType.LogicalLeft, 32, false, 0x00000000u, false];
        yield return [0x12345678u, BarrelShifter.ShiftType.LogicalLeft, 33, false, 0x00000000u, false];

        // ─────────── LSR (Logical Right) ──────────
        yield return [0x12345678u, BarrelShifter.ShiftType.LogicalRight, 0,  false, 0x00000000u, false]; // 0 ⇒ behaves like 32
        yield return [0x12345678u, BarrelShifter.ShiftType.LogicalRight, 1,  false, 0x091A2B3Cu, false];
        yield return [0x12345678u, BarrelShifter.ShiftType.LogicalRight, 31, false, 0x00000000u, false];
        yield return [0x12345678u, BarrelShifter.ShiftType.LogicalRight, 32, false, 0x00000000u, false];
        yield return [0x12345678u, BarrelShifter.ShiftType.LogicalRight, 33, false, 0x00000000u, false];

        // ─────────── ASR (Arithmetic Right) – positive value ───────────
        yield return [0x12345678u, BarrelShifter.ShiftType.ArithmeticRight, 0,  false, 0x00000000u, false];
        yield return [0x12345678u, BarrelShifter.ShiftType.ArithmeticRight, 1,  false, 0x091A2B3Cu, false];
        yield return [0x12345678u, BarrelShifter.ShiftType.ArithmeticRight, 31, false, 0x00000000u, false];
        yield return [0x12345678u, BarrelShifter.ShiftType.ArithmeticRight, 32, false, 0x00000000u, false];
        yield return [0x12345678u, BarrelShifter.ShiftType.ArithmeticRight, 33, false, 0x00000000u, false];

        // ─────────── ASR – negative value (sign bit set) ───────────
        yield return [0x92345678u, BarrelShifter.ShiftType.ArithmeticRight, 0,  false, 0xFFFFFFFFu, true];
        yield return [0x92345678u, BarrelShifter.ShiftType.ArithmeticRight, 1,  false, 0xC91A2B3Cu, false];
        yield return [0x92345678u, BarrelShifter.ShiftType.ArithmeticRight, 8,  false, 0xFF923456u, false];
        yield return [0x92345678u, BarrelShifter.ShiftType.ArithmeticRight, 31, false, 0xFFFFFFFFu, false];
        yield return [0x92345678u, BarrelShifter.ShiftType.ArithmeticRight, 32, false, 0xFFFFFFFFu, true];
        yield return [0x92345678u, BarrelShifter.ShiftType.ArithmeticRight, 33, false, 0xFFFFFFFFu, true];

        // ─────────── ROR (Rotate Right, amount ≠ 0) ───────────
        yield return [0x12345678u, BarrelShifter.ShiftType.RotateRight, 1,  false, 0x091A2B3Cu, false];
        yield return [0x12345678u, BarrelShifter.ShiftType.RotateRight, 4,  false, 0x81234567u, true];
        yield return [0x12345678u, BarrelShifter.ShiftType.RotateRight, 31, false, 0x2468ACF0u, false];

        // ─────────── RRX (Rotate Right with Extend, amount = 0) ───────────
        yield return [0x12345678u, BarrelShifter.ShiftType.RotateRight, 0, false, 0x091A2B3Cu, false];
        yield return [0x12345678u, BarrelShifter.ShiftType.RotateRight, 0, true,  0x891A2B3Cu, false];
        yield return [0x80000001u, BarrelShifter.ShiftType.RotateRight, 0, false, 0x40000000u, true];
    }
}
