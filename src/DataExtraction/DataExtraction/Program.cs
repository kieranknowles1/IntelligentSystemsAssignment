using CsvHelper;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Order;
using System.Globalization;

namespace DataExtraction;

public class Line
{
    public Line(IDialogResponseGetter response)
    {
        if (response.ResponseText == null || response.Data == null)
            throw new ArgumentException("Null text or data", nameof(response));

        Text = response.ResponseText;
        EmotionType = response.Data.Emotion;
        EmotionValue = response.Data.EmotionValue;
    }

    public string Text { get; }
    public EmotionType EmotionType { get; }
    public int EmotionValue { get; }
    //public string ExtractedPath { get; }
}

public static class Program
{
    public static ILoadOrder<IModListing<IOblivionModGetter>> CreateLoadOrder(string dataDir, List<string> plugins)
    {
        var pluginMods = plugins
            .Select(plugin => OblivionMod.CreateFromBinaryOverlay(Path.Combine(dataDir, plugin)))
            .Select(mod => new ModListing<IOblivionModGetter>(mod));

        return new LoadOrder<IModListing<IOblivionModGetter>>(pluginMods);
    }

    public static List<Line> ExtractData(List<string> plugins, string dataRoot, List<string> dataDirs)
    {
        var lines = new List<Line>();

        foreach (var dataDir in dataDirs)
        {
            var fullPath = Path.Combine(dataRoot, dataDir);
            var loadOrder = CreateLoadOrder(fullPath, plugins);

            foreach (var response in loadOrder.PriorityOrder.DialogItem().WinningOverrides())
            {
                foreach (var line in response.Responses)
                {
                    // TODO: Extract file
                    lines.Add(new(line));
                }
            }
        }

        return lines;
    }

    public static void Main(string[] args)
    {
        // Using game pass version as it downloads all languages
        // The only DLCs included are KOTN and Shivering isles. These and the base game include
        // vast majority of dialogue anyway
        var plugins = new List<string>()
        {
            "Oblivion.esm",
            "DLCShiveringIsles.esp",
            "Knights.esp",
        };

        var dataRoot = @"E:\XboxGames\The Elder Scrolls IV- Oblivion (PC)\Content";

        var dataDirs = new List<string>()
        {
            @"Oblivion GOTY English\Data",
            @"Oblivion GOTY French\Data",
            @"Oblivion GOTY German\Data",
            @"Oblivion GOTY Italian\Data",
            @"Oblivion GOTY Spanish\Data",
        };

        var data = ExtractData(plugins, dataRoot, dataDirs);

        using (var writer = new StreamWriter("extracted_data.csv"))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(data);
        }
    }
}