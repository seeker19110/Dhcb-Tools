namespace DhcbTools.Shared.Logic.Checks
{
    /// <summary>
    /// Ghi chú cho <c>HealthReport</c> khi một phép đếm không trọn (§61): "0 connector hở" phải phân biệt được
    /// với "không đếm được" — trước đó mọi ngoại lệ trong lượt quét bị nuốt và số đếm dở đọc như số thật.
    /// </summary>
    public static class HealthReportNotes
    {
        public static string ConnectorScanNote(int skipped, string? aborted)
        {
            if (!string.IsNullOrEmpty(aborted))
            {
                return $" (quét đổ giữa chừng: {aborted} — số đếm dở)";
            }

            return skipped > 0 ? $" ({skipped} phần tử không đọc được — cận dưới)" : string.Empty;
        }
    }
}
