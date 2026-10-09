namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>
    /// Đổi khoảng cách mm của config (<c>rowToleranceMm</c>, <c>moveToleranceMm</c>) sang đơn vị bản vẽ theo biến
    /// <c>INSUNITS</c>. Trước đây giá trị "Mm" được so thẳng trong đơn vị bản vẽ: bản vẽ khai mét thì
    /// <c>rowToleranceMm = 300</c> thành 300 m — mọi block rơi vào một hàng, đánh số sai thứ tự mà không báo gì.
    /// </summary>
    public static class DrawingUnits
    {
        /// <summary>Số mm của một đơn vị bản vẽ theo mã <c>INSUNITS</c>; <c>null</c> khi không khai (0) hoặc mã lạ.</summary>
        public static double? MillimetersPerUnit(int insunits)
        {
            switch (insunits)
            {
                case 1: return 25.4;                    // inch
                case 2: return 304.8;                   // foot
                case 3: return 1609344.0;               // mile
                case 4: return 1.0;                     // millimeter
                case 5: return 10.0;                    // centimeter
                case 6: return 1000.0;                  // meter
                case 7: return 1000000.0;               // kilometer
                case 8: return 0.0000254;               // microinch
                case 9: return 0.0254;                  // mil
                case 10: return 914.4;                  // yard
                case 11: return 0.0000001;              // angstrom
                case 12: return 0.000001;               // nanometer
                case 13: return 0.001;                  // micron
                case 14: return 100.0;                  // decimeter
                case 15: return 10000.0;                // decameter
                case 16: return 100000.0;               // hectometer
                case 21: return 1200000.0 / 3937.0;     // US survey foot
                default: return null;                   // 0 = không khai; 17–20 (gigameter, AU…) không dùng cho bản vẽ xây dựng
            }
        }

        /// <summary>
        /// Đổi <paramref name="millimeters"/> sang đơn vị bản vẽ. Không khai đơn vị (<c>INSUNITS = 0</c>) hoặc mã lạ thì
        /// coi bản vẽ vẽ theo mm — đúng giả định cũ, nên job chạy trên bản vẽ mm/không khai đơn vị không đổi kết quả.
        /// </summary>
        public static double FromMillimeters(double millimeters, int insunits) =>
            millimeters / (MillimetersPerUnit(insunits) ?? 1.0);

        /// <summary>Đổi tọa độ bản vẽ sang mm cho CSV nhập vào Revit. Không khai đơn vị thì giữ giả định mm.</summary>
        public static double ToMillimeters(double drawingValue, int insunits) =>
            drawingValue * (MillimetersPerUnit(insunits) ?? 1.0);
    }
}
