using System.Collections.Generic;
using System.Text;
using BirthdayIsland.Models;

namespace BirthdayIsland.Services;

/// <summary>
/// 把生日数据导出成 CSV 文本。
/// </summary>
/// <remarks>
/// 和导入端（<see cref="ExcelImportService"/>）对称：表头用同一套列名，生日优先写回导入时的原始文本，
/// 这样「导出 → 再导入」不会丢信息（只有月日的人不会被补成 2000-03-27）。
/// 文件由调用方按 UTF-8 带 BOM 落盘 —— 不带 BOM 的话 Excel 会用 ANSI 打开，中文全是乱码。
/// </remarks>
public static class BirthdayCsvWriter
{
    private static readonly string[] Headers = { "姓名", "生日", "班级", "备注" };

    public static string Build(IEnumerable<BirthdayPerson> people)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Headers));

        foreach (var person in people)
        {
            builder.AppendLine(string.Join(",",
                Escape(person.Name),
                Escape(GetBirthdayText(person)),
                Escape(person.ClassName ?? string.Empty),
                Escape(person.Remark ?? string.Empty)));
        }

        return builder.ToString();
    }

    private static string GetBirthdayText(BirthdayPerson person) =>
        string.IsNullOrWhiteSpace(person.RawBirthday)
            ? person.Birthday.ToString("yyyy-MM-dd")
            : person.RawBirthday;

    /// <summary>按 RFC 4180 转义：含逗号、引号或换行的字段整体加引号，内部引号翻倍。</summary>
    private static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
