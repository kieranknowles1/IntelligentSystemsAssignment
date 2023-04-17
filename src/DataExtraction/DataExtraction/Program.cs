using CsvHelper;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Order;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DataExtraction;

using IOblivionLoadOrder = ILoadOrder<IModListing<IOblivionModGetter>>;

public struct InfoIndex
{
    public FormKey Id { get; }
    public int Index { get; }

    public InfoIndex(FormKey id, int index)
    {
        Id = id;
        Index = index;
    }

    public override string ToString()
    {
        return $"{Id}_{Index}";
    }
}

public class Line
{
    public Line(IDialogResponseGetter response, string extractedPath)
    {
        if (response.ResponseText == null || response.Data == null)
            throw new ArgumentException("Null text or data", nameof(response));

        Text = response.ResponseText;
        EmotionType = response.Data.Emotion;
        EmotionValue = response.Data.EmotionValue;
        ExtractedPath = extractedPath;
    }

    public string Text { get; }
    public EmotionType EmotionType { get; }
    public int EmotionValue { get; }
    public string ExtractedPath { get; }
}

public static class Program
{
    public static readonly Regex VoiceRegex = new(@"sound\\voice\\(?<plugin>\w+\.es[mp])\\[a-z ]+\\[mf]\\\w+[0-9a-f]{2}(?<info>[0-9a-f]{6})_(?<index>\d+)\.mp3", RegexOptions.IgnoreCase);

    /// <summary>
    /// Get the <see cref="InfoIndex"/> of a voice files INFO record
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static InfoIndex GetVoiceFormKey(string path)
    {
        var match = VoiceRegex.Match(path);
        if (!match.Success)
            throw new ArgumentException("Path does not match expected regex");

        var formKey = FormKey.Factory($"{match.Groups["info"]}:{match.Groups["plugin"]}");
        var index = int.Parse(match.Groups["index"].Value);

        return new InfoIndex(formKey, index);

    }

    public static IOblivionLoadOrder CreateLoadOrder(string dataDir, List<string> plugins)
    {
        var pluginMods = plugins
            .Select(plugin => OblivionMod.CreateFromBinaryOverlay(Path.Combine(dataDir, plugin)))
            .Select(mod => new ModListing<IOblivionModGetter>(mod));

        return new LoadOrder<IModListing<IOblivionModGetter>>(pluginMods);
    }

    public static List<Line> LinesFromLoadOrder(IOblivionLoadOrder loadOrder, List<IArchiveReader> archives, string languageCode)
    {
        var lines = new List<Line>();

        var linkCache = loadOrder.ToImmutableLinkCache();

        var byPath = archives
            .SelectMany(bsa => bsa.Files)
            // Use the last copy if a file exists in multiple BSAs. This is how the game handles it
            .Reverse()
            .DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Where(file => VoiceRegex.IsMatch(file.Path))
            .GroupBy(file => GetVoiceFormKey(file.Path))
            .ToDictionary(group => group.Key, group => group.ToList());


        foreach (var response in loadOrder.PriorityOrder.DialogItem().WinningOverrides())
        {
            for (int i = 0; i < response.Responses.Count; i++)
            {
                var line = response.Responses[i];

                if (!byPath.TryGetValue(new(response.FormKey, i + 1), out var files))
                    continue;

                foreach (var file in files)
                {
                    var extractedPath = Path.Combine("voice", languageCode, file.Path);

                    Console.WriteLine($"Extract file '{extractedPath}' for INFO {response.FormKey}_{i+1}");
                    Directory.CreateDirectory(Path.GetDirectoryName(extractedPath));
                    File.WriteAllBytes(extractedPath, file.GetBytes());

                    lines.Add(new Line(line, extractedPath));
                }
            }
        }

        return lines;
    }

    public static List<Line> ExtractData(List<string> plugins, List<string> archives, string dataRoot, List<string> dataDirs)
    {
        var lines = new List<Line>();

        foreach (var dataDir in dataDirs)
        {
            var fullPath = Path.Combine(dataRoot, dataDir);
            var loadOrder = CreateLoadOrder(fullPath, plugins);

            var thisArchives = archives
                .Select(bsa => Archive.CreateReader(GameRelease.Oblivion, Path.Combine(fullPath, bsa)))
                .ToList();

            lines.AddRange(LinesFromLoadOrder(loadOrder, thisArchives, dataDir));
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

        // Only need archives containing dialogue
        var archives = new List<string>()
        {
            "Oblivion - Voices1.bsa",
            "Oblivion - Voices2.bsa",
            "DLCShiveringIsles - Voices.bsa",
            "Knights.bsa",
        };

        // TODO: Dehardcode
        var dataRoot = @"E:\XboxGames\The Elder Scrolls IV- Oblivion (PC)\Content";

        var dataDirs = new List<string>()
        {
            @"Oblivion GOTY English\Data",
            @"Oblivion GOTY French\Data",
            @"Oblivion GOTY German\Data",
            @"Oblivion GOTY Italian\Data",
            @"Oblivion GOTY Spanish\Data",
        };

        var data = ExtractData(plugins, archives, dataRoot, dataDirs);

        using var writer = new StreamWriter("extracted_data.csv");
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(data);
    }
}