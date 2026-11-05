#nullable disable
using System.Text;
using AIStoryBuildersV2.Shared.Models;
using ExcelDataReader;
using OfficeOpenXml;

namespace AIStoryBuildersV2.Shared.Stories;

// F18 training data (V1 AIStoryBuildersService.ExportImport.cs). V2 builds files in memory instead of a temp folder.
public partial class AIStoryBuildersService
{
    public const string TrainingSystemMessage = "You are a fiction novel writing software program that creates prose from the story beats provided.";

    #region public async Task<List<TrainingData>> CreateTrainingDataAsync(Story objStory)
    /// <summary>V1: one row per line of every paragraph; the User text is the line's story beats from the AI.</summary>
    public async Task<List<TrainingData>> CreateTrainingDataAsync(Story objStory)
    {
        try
        {
            var colTrainingData = new List<TrainingData>();
            var colChapters = GetChapters(objStory);

            foreach (var objChapter in colChapters)
            {
                RaiseText($"{objChapter.ChapterName} out of {colChapters.Count}", 5);

                var colParagraphs = GetParagraphs(objChapter);
                int i = 1;
                foreach (var objParagraph in colParagraphs)
                {
                    RaiseText($"Parsing Paragraph {i} out of {colParagraphs.Count}", 5);

                    foreach (string Paragraph in (objParagraph.ParagraphContent ?? "").Split('\n'))
                    {
                        string ParagraphDescription = await OrchestratorMethods.GetStoryBeats(Paragraph);
                        colTrainingData.Add(new TrainingData { System = TrainingSystemMessage, User = ParagraphDescription, Assistant = Paragraph });
                    }
                    i++;
                }
            }

            RaiseText("Training data complete!", 2);
            return colTrainingData;
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("CreateTrainingData: " + ex.Message);
            return new List<TrainingData>();
        }
    }
    #endregion

    #region public byte[] ExportTrainingData(List<TrainingData> colTrainingData)
    /// <summary>V1's workbook (sheet "Training Data", columns System, User, Assistant), built in memory.</summary>
    public byte[] ExportTrainingData(List<TrainingData> colTrainingData)
    {
        ExcelLicense.EnsureSet();
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Training Data");
        worksheet.Cells[1, 1].Value = "System";
        worksheet.Cells[1, 2].Value = "User";
        worksheet.Cells[1, 3].Value = "Assistant";

        int i = 2;
        foreach (var line in colTrainingData)
        {
            worksheet.Cells[i, 1].Value = line.System;
            worksheet.Cells[i, 2].Value = line.User;
            worksheet.Cells[i, 3].Value = line.Assistant;
            i++;
        }
        return package.GetAsByteArray();
    }
    #endregion
}

/// <summary>The JsonL Utility and the OpenAI upload (F18): rows as V1's chat-format JSON lines.</summary>
public static class TrainingDataJsonl
{
    /// <summary>One <c>{"messages":[system,user,assistant]}</c> line per row, as V1's <c>Conversation.ToString()</c>.</summary>
    public static string ToJsonl(IEnumerable<TrainingData> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            var conversation = new Conversation(
            [
                new Message(Role.System, row.System ?? ""),
                new Message(Role.User, row.User ?? ""),
                new Message(Role.Assistant, row.Assistant ?? ""),
            ]);
            sb.Append(conversation.ToString()).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reads rows 2 and on (System, User, Assistant) of every sheet. V1 read the file stream after copying it, which was
    /// already at its end; this reads the copied bytes.
    /// </summary>
    public static List<TrainingData> FromExcel(byte[] workbook)
    {
        ExcelLicense.EnsureCodePages();
        var rows = new List<TrainingData>();
        using var stream = new MemoryStream(workbook);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        do
        {
            var first = true;
            while (reader.Read())
            {
                if (first) { first = false; continue; }
                if (reader.FieldCount < 3) continue;
                rows.Add(new TrainingData { System = Cell(reader, 0), User = Cell(reader, 1), Assistant = Cell(reader, 2) });
            }
        }
        while (reader.NextResult());
        return rows;
    }

    static string Cell(IExcelDataReader reader, int i) => reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i)) ?? "";
}

/// <summary>Process-wide library setup that EPPlus and ExcelDataReader require; set once and never changed.</summary>
static class ExcelLicense
{
    static readonly Lazy<bool> License = new(() =>
    {
        // EPPlus 8 needs a license choice before use. V1 used EPPlus under its non-commercial terms (see README).
        ExcelPackage.License.SetNonCommercialOrganization("AIStoryBuilders");
        return true;
    });

    static readonly Lazy<bool> CodePages = new(() =>
    {
        // ExcelDataReader needs the legacy code pages for .xls files.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return true;
    });

    public static void EnsureSet() => _ = License.Value;

    public static void EnsureCodePages() => _ = CodePages.Value;
}
