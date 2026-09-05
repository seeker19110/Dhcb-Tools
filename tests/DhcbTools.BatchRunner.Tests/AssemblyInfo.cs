using Xunit;

// Console.SetOut/SetError là trạng thái TOÀN CỤC của tiến trình, mà bộ này bắt output bằng cách đổi
// hai cái đó. Chạy song song thì hai ca giẫm lên nhau: trên CI Linux, ca "IDS lệch chuẩn" đọc được
// output của ca "job hỏng" và đỏ, còn ở máy Windows thì tình cờ xanh (§47). Tắt song song cho cả
// assembly là cách duy nhất chắc chắn — bộ này chỉ 17 ca, chạy hết dưới 100 ms.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
