namespace DhcbTools.Shared.Hosting
{
    /// <summary>Kết quả còn công việc bị bỏ qua không được báo hoàn thành đầy đủ; no-op thật vẫn giữ trạng thái cũ.</summary>
    public static class CommandOutcome
    {
        public static CommandResult WithIncompleteWork(this CommandResult result, bool incomplete)
        {
            if (incomplete)
            {
                result.PartialSuccess = true;
            }

            return result;
        }
    }
}
