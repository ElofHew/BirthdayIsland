using System.Linq;
using BirthdayIsland.Models;

namespace BirthdayIsland.Services;

/// <summary>
/// 生日文本的显示/编辑转换。
/// </summary>
/// <remarks>
/// <see cref="BirthdayPerson.RawBirthday"/> 存的是导入时的原始文本，但 Excel 的日期单元格
/// 在 ClosedXML 里是数字类型，<c>GetString()</c> 拿到的是日期序列号（例如 46192），
/// 直接显示给用户就是「40000+ 的怪数字」。所以凡是拿它展示的地方都先过一遍这里。
/// </remarks>
public static class BirthdayText
{
    /// <summary>
    /// 取可展示、可编辑的生日文本：原始文本有意义就用它，否则用解析后的日期。
    /// </summary>
    public static string ToEditable(BirthdayPerson person)
    {
        string raw = (person.RawBirthday ?? string.Empty).Trim();
        if (raw.Length > 0 && !IsDateSerial(raw))
            return raw;

        return person.Birthday.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// 判断原始文本是不是一个纯数字（Excel 日期序列号的形态）。
    /// </summary>
    /// <remarks>
    /// 只看「全数字 + 落在合理序列号区间」：避免把 2008 这种年份当成序列号，
    /// 也避免误伤 "03-27" 之类本来就有意义的写法。
    /// </remarks>
    private static bool IsDateSerial(string text)
    {
        if (text.Length == 0 || !text.All(char.IsDigit))
            return false;

        return long.TryParse(text, out long value) && value is >= 1 and < 2958466;
    }
}
