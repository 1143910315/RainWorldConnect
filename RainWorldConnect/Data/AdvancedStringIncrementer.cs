using System.Text;

namespace RainWorldConnect.Data {
    public class AdvancedStringIncrementer {
        // 扩展字符集，包含大写字母、小写字母、数字和常见符号
        private static readonly char[] CharacterSet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

        // 使用字典映射字符到索引，提高查找性能
        private static readonly Dictionary<char, int> CharToIndexMap = CharacterSet.Select(static (c, i) => new { c, i }).ToDictionary(static x => x.c, static x => x.i);

        public static string IncrementString(string input) {
            // 将字符串转换为数字列表，无效字符视为进位
            List<int> digits = [.. input.Select(static c => CharToIndexMap.TryGetValue(c, out int index) ? index : CharacterSet.Length)];
            if (digits.Count == 0) {
                digits.Add(0);
            } else {
                digits[0] += 1;
            }
            for (int i = 0; i < digits.Count; i++) {
                int v = digits[i];
                digits[i] = v % CharacterSet.Length;
                try {
                    digits[i + 1] += v / CharacterSet.Length;
                } catch (ArgumentOutOfRangeException) {
                    int addValue = v / CharacterSet.Length;
                    if (addValue > 0) {
                        digits.Add(addValue - 1);
                    }
                }
            }
            return digits.Aggregate(new StringBuilder(), (sb, digit) => sb.Append(CharacterSet[digit])).ToString();
        }
    }
}
