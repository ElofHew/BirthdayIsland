using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BirthdayIsland.Models;
using ClosedXML.Excel;

namespace BirthdayIsland.Services;

/// <summary>
/// 生日表格导入。支持 .xlsx（ClosedXML）与 .csv（手写解析）。
/// 两种格式共用同一套表头别名和日期解析，避免「Excel 能认、CSV 不认」这种漂移。
/// 老的 .xls 是二进制格式，ClosedXML 读不了，所以在 Import 入口就明确拒绝。
/// </summary>
public class ExcelImportService
{
    private const int DefaultYear = 2000;

    private static readonly string[] NameHeaders =
        { "姓名", "名字", "name" };

    private static readonly string[] BirthdayHeaders =
        { "生日", "出生日期", "birthday", "birthdate" };

    private static readonly string[] ClassHeaders =
        { "班级", "class", "classname" };

    private static readonly string[] RemarkHeaders =
        { "备注", "remark", "note" };

    public BirthdayImportResult Import(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new BirthdayImportResult
            {
                Fatal = true,
                Message = "导入失败：文件不存在，请重新选择。"
            };
        }

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".csv" => ImportCsv(filePath),
            ".xlsx" or ".xlsm" => ImportWorkbook(filePath),
            ".xls" => new BirthdayImportResult
            {
                Fatal = true,
                // 不自动处理 .xls：那是 BIFF 二进制格式，ClosedXML 只认 OpenXML，
                // 让人回去另存一次比自己糊一个 BIFF 解析器靠谱得多。
                Message = "导入失败：不支持旧版 .xls，请在 Excel 里「另存为」.xlsx 或 .csv 后重试。"
            },
            _ => new BirthdayImportResult
            {
                Fatal = true,
                Message = "导入失败：只支持 .xlsx 或 .csv 文件。"
            }
        };
    }

    // ------------------------------------------------------------------ xlsx

    private BirthdayImportResult ImportWorkbook(string filePath)
    {
        var result = new BirthdayImportResult();

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(filePath);
        }
        catch (IOException)
        {
            result.Fatal = true;
            result.Message = "导入失败：文件可能正在被其它程序占用，请关闭后重试。";
            return result;
        }
        catch (Exception)
        {
            result.Fatal = true;
            result.Message = "导入失败：文件格式错误或已损坏，请确认是有效的 .xlsx 文件。";
            return result;
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet == null)
            {
                result.Fatal = true;
                result.Message = "导入失败：表格中没有工作表。";
                return result;
            }

            var range = sheet.RangeUsed();
            if (range == null)
            {
                result.Fatal = true;
                result.Message = "导入失败：表格为空。";
                return result;
            }

            // 第一行作为表头
            var headerRow = range.FirstRow();
            int nameCol = FindColumn(headerRow, NameHeaders);
            int birthdayCol = FindColumn(headerRow, BirthdayHeaders);
            int classCol = FindColumn(headerRow, ClassHeaders);
            int remarkCol = FindColumn(headerRow, RemarkHeaders);

            if (nameCol == 0)
            {
                result.Fatal = true;
                result.Message = "导入失败：未找到“姓名”列，请确认表格第一行包含“姓名”或“Name”。";
                return result;
            }

            if (birthdayCol == 0)
            {
                result.Fatal = true;
                result.Message = "导入失败：未找到“生日”列，请确认表格第一行包含“生日”或“Birthday”。";
                return result;
            }

            // 从第二行开始解析数据
            foreach (var row in range.Rows().Skip(1))
            {
                int rowNumber = row.RowNumber();
                var nameCell = row.Cell(nameCol);
                string name = nameCell.GetString().Trim();

                // 姓名为空：跳过空行（不计为错误）
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var birthdayCell = row.Cell(birthdayCol);
                string rawBirthday = birthdayCell.GetString().Trim();

                if (!TryParseBirthday(birthdayCell, out var birthday))
                {
                    result.Errors.Add(new BirthdayImportError
                    {
                        RowNumber = rowNumber,
                        RawName = name,
                        RawBirthday = rawBirthday,
                        Reason = string.IsNullOrWhiteSpace(rawBirthday)
                            ? "生日为空"
                            : $"无法识别生日格式：{rawBirthday}"
                    });
                    continue;
                }

                result.People.Add(new BirthdayPerson
                {
                    Name = name,
                    RawBirthday = rawBirthday,
                    Birthday = birthday,
                    ClassName = classCol > 0 ? row.Cell(classCol).GetString().Trim() : null,
                    Remark = remarkCol > 0 ? row.Cell(remarkCol).GetString().Trim() : null
                });
            }
        }

        FillSummary(result);
        return result;
    }

    // ------------------------------------------------------------------ csv

    private BirthdayImportResult ImportCsv(string filePath)
    {
        var result = new BirthdayImportResult();

        string text;
        try
        {
            text = ReadCsvText(filePath);
        }
        catch (IOException)
        {
            result.Fatal = true;
            result.Message = "导入失败：文件可能正在被其它程序占用，请关闭后重试。";
            return result;
        }
        catch (Exception)
        {
            result.Fatal = true;
            result.Message = "导入失败：无法读取文件，请确认是有效的文本文件。";
            return result;
        }

        var rows = ParseCsvRows(text);

        // 表头 = 第一行有内容的行。有些导出工具会在最前面留空行。
        int headerIndex = rows.FindIndex(r => r.Any(f => !string.IsNullOrWhiteSpace(f)));
        if (headerIndex < 0)
        {
            result.Fatal = true;
            result.Message = "导入失败：文件里没有内容。";
            return result;
        }

        var header = rows[headerIndex];
        int nameCol = FindColumnIndex(header, NameHeaders);
        int birthdayCol = FindColumnIndex(header, BirthdayHeaders);
        int classCol = FindColumnIndex(header, ClassHeaders);
        int remarkCol = FindColumnIndex(header, RemarkHeaders);

        if (nameCol < 0)
        {
            result.Fatal = true;
            result.Message = "导入失败：未找到“姓名”列，请确认表格第一行包含“姓名”或“Name”。";
            return result;
        }

        if (birthdayCol < 0)
        {
            result.Fatal = true;
            result.Message = "导入失败：未找到“生日”列，请确认表格第一行包含“生日”或“Birthday”。";
            return result;
        }

        for (int i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            int rowNumber = i + 1;
            string name = CellText(row, nameCol);

            if (string.IsNullOrWhiteSpace(name))
                continue;

            string rawBirthday = CellText(row, birthdayCol);

            if (!TryParseBirthdayText(rawBirthday, out var birthday))
            {
                result.Errors.Add(new BirthdayImportError
                {
                    RowNumber = rowNumber,
                    RawName = name,
                    RawBirthday = rawBirthday,
                    Reason = string.IsNullOrWhiteSpace(rawBirthday)
                        ? "生日为空"
                        : $"无法识别生日格式：{rawBirthday}"
                });
                continue;
            }

            result.People.Add(new BirthdayPerson
            {
                Name = name,
                RawBirthday = rawBirthday,
                Birthday = birthday,
                ClassName = classCol >= 0 ? CellText(row, classCol) : null,
                Remark = remarkCol >= 0 ? CellText(row, remarkCol) : null
            });
        }

        FillSummary(result);
        return result;
    }

    /// <summary>
    /// 读 CSV 文本。编码按 BOM → 严格 UTF-8 → GB18030 的顺序试：
    /// 中文 Windows 上 Excel「另存为 CSV」默认写 ANSI（GBK 系），没有 BOM 且不是合法 UTF-8，
    /// 直接按 UTF-8 解会得到一片问号，用户只会看到「找不到姓名列」。
    /// </summary>
    private static string ReadCsvText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // GB18030 是 GBK / GB2312 的超集，这一族编码都能解。
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
    }

    /// <summary>
    /// 按 RFC 4180 切行切列：双引号包裹的字段内允许逗号、换行；两个连续双引号表示一个双引号。
    /// 只认逗号分隔（中文区域的 Excel 导出默认就是逗号），不猜分号或制表符。
    /// </summary>
    private static List<string[]> ParseCsvRows(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    // CRLF 交给后面的 \n 收尾；单独出现的 \r（老 Mac 换行）不支持，会当成空白。
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add(fields.ToArray());
                    fields.Clear();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        // 最后一行没有换行符时也要收进来
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add(fields.ToArray());
        }

        return rows;
    }

    private static string CellText(string[] row, int index) =>
        index >= 0 && index < row.Length ? row[index].Trim() : string.Empty;

    /// <summary>在 CSV 表头里找列，返回 0 基下标；找不到返回 -1。</summary>
    private static int FindColumnIndex(IReadOnlyList<string> headers, string[] aliases)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            if (HeaderMatches(headers[i], aliases))
                return i;
        }

        return -1;
    }

    private static void FillSummary(BirthdayImportResult result)
    {
        result.Message = result.SuccessCount == 0 && result.ErrorCount == 0
            ? "导入完成：表格中没有有效数据。"
            : $"导入完成：成功 {result.SuccessCount} 条，失败 {result.ErrorCount} 条。";
    }

    // ------------------------------------------------------------------ 公共解析

    /// <summary>在表头行中查找匹配任一别名的列号（1 基）；找不到返回 0。</summary>
    private static int FindColumn(IXLRangeRow headerRow, string[] aliases)
    {
        foreach (var cell in headerRow.Cells())
        {
            if (HeaderMatches(cell.GetString(), aliases))
                return cell.Address.ColumnNumber;
        }

        return 0;
    }

    private static bool HeaderMatches(string header, string[] aliases)
    {
        header = header.Trim();
        if (header.Length == 0)
            return false;

        foreach (var alias in aliases)
        {
            if (string.Equals(header, alias, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryParseBirthday(IXLCell cell, out DateTime birthday)
    {
        birthday = default;

        // 1) Excel 原生日期单元格
        if (cell.DataType == XLDataType.DateTime)
        {
            try
            {
                birthday = cell.GetDateTime();
                return true;
            }
            catch
            {
                // 继续按文本解析
            }
        }

        // 2) Excel 经常把“6月19日”“06-19”“2010年12月25日”等自动转换成日期序列号(数字)。
        //    这种单元格 DataType 是 Number，GetString() 只能得到像 “46192” 的序列号文本，
        //    必须用 OADate 还原成真正的日期，否则会被当成“无法识别的格式”。
        if (cell.DataType == XLDataType.Number)
        {
            try
            {
                double serial = cell.GetDouble();
                // Excel 日期序列号合理范围：1(1899-12-31) ~ 2958465(9999-12-31)
                if (serial >= 1 && serial < 2958466)
                {
                    birthday = DateTime.FromOADate(serial);
                    return true;
                }
            }
            catch
            {
                // 继续按文本解析
            }
        }

        string raw = cell.GetString().Trim();
        return TryParseBirthdayText(raw, out birthday);
    }

    /// <summary>
    /// 解析多种生日文本格式：
    /// 2008-03-27 / 2008/03/27 / 2008.03.27 / 03-27 / 03/27 / 3月27日 / 2008年3月27日。
    /// 仅有月日时使用默认年份 2000。
    /// </summary>
    public static bool TryParseBirthdayText(string raw, out DateTime birthday)
    {
        birthday = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        raw = raw.Trim();

        // 中文“X年X月X日 / X月X日”
        var cn = Regex.Match(raw, @"^(?:(\d{1,4})\s*年)?\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日?$");
        if (cn.Success)
        {
            int year = cn.Groups[1].Success ? int.Parse(cn.Groups[1].Value) : DefaultYear;
            int month = int.Parse(cn.Groups[2].Value);
            int day = int.Parse(cn.Groups[3].Value);
            return TryBuildDate(year, month, day, out birthday);
        }

        // 用 - / . 分隔的数字
        var parts = raw.Split(new[] { '-', '/', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out int y3) &&
            int.TryParse(parts[1], out int m3) &&
            int.TryParse(parts[2], out int d3))
        {
            return TryBuildDate(y3, m3, d3, out birthday);
        }

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out int m2) &&
            int.TryParse(parts[1], out int d2))
        {
            return TryBuildDate(DefaultYear, m2, d2, out birthday);
        }

        // 兜底：交给 .NET 通用解析
        if (DateTime.TryParse(raw, CultureInfo.CurrentCulture,
                DateTimeStyles.None, out var parsed) ||
            DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsed))
        {
            birthday = parsed;
            return true;
        }

        // 兜底2：CSV 转存时 Excel 可能把日期写成纯数字序列号（文本形式），如 "46192"。
        //         仅在数值落在常见出生年份范围(约 1927~2119)时按 OADate 还原，避免误伤普通数字。
        if (long.TryParse(raw, out long serial) && serial >= 10000 && serial < 80000)
        {
            try
            {
                birthday = DateTime.FromOADate(serial);
                return true;
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    private static bool TryBuildDate(int year, int month, int day, out DateTime date)
    {
        date = default;
        if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1 || day > 31)
            return false;

        // 闰日：若给定年份不是闰年但写了 2/29，则归一化到闰年 2000，便于按月日比较。
        if (month == 2 && day == 29 && !DateTime.IsLeapYear(year))
            year = DefaultYear;

        if (day > DateTime.DaysInMonth(year, month))
            return false;

        date = new DateTime(year, month, day);
        return true;
    }
}
