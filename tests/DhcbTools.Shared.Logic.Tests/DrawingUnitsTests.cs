using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Dung sai "Mm" của config AutoCAD đổi theo INSUNITS — bản vẽ khai mét không còn nhận 300 "mm" thành 300 m.</summary>
public class DrawingUnitsTests
{
    [Theory]
    [InlineData(4, 300.0, 300.0)]       // mm: không đổi
    [InlineData(0, 300.0, 300.0)]       // không khai: coi như mm (giả định cũ)
    [InlineData(6, 300.0, 0.3)]         // mét
    [InlineData(5, 300.0, 30.0)]        // cm
    [InlineData(1, 25.4, 1.0)]          // inch
    [InlineData(2, 304.8, 1.0)]         // foot
    [InlineData(99, 300.0, 300.0)]      // mã lạ: coi như mm
    public void FromMillimeters(int insunits, double mm, double expected)
    {
        Assert.Equal(expected, DrawingUnits.FromMillimeters(mm, insunits), 9);
    }

    [Fact]
    public void MoiMaDonViDaiDeuCoHeSo()
    {
        foreach (var code in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 21 })
        {
            Assert.True(DrawingUnits.MillimetersPerUnit(code) > 0, "INSUNITS " + code);
        }

        Assert.Null(DrawingUnits.MillimetersPerUnit(0));
        Assert.Equal(304.8006096, DrawingUnits.MillimetersPerUnit(21)!.Value, 6);
    }
}
