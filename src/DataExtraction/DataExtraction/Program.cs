using CsvHelper;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Order;
using NAudio.Wave;
using System.Diagnostics.CodeAnalysis;
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

    public static bool IsMp3Valid(Stream data, [NotNullWhen(false)] out string? error)
    {
        try
        {
            using (var reader = new Mp3FileReader(data))
            {
                // Read all frames, but don't do anything with them to validate the file
                while (reader.ReadNextFrame() != null)
                {

                }
            }

            error = null;
            return true;
        }
        catch (InvalidDataException e)
        {
            error = e.Message;
            return false;
        }
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


        Parallel.ForEach(loadOrder.PriorityOrder.DialogItem().WinningOverrides(), response =>
        {
            for (int i = 0; i < response.Responses.Count; i++)
            {
                var line = response.Responses[i];

                if (!byPath.TryGetValue(new(response.FormKey, i + 1), out var files))
                    continue;

                foreach (var file in files)
                {
                    // Some files are empty or corrupted. Remove them here
                    byte[] bytes = file.GetBytes();
                    if (IsMp3Valid(new MemoryStream(bytes), out var error))
                    {
                        //Console.WriteLine($"Extract {file.Path}");
                        var extractedPath = Path.Combine("voice", languageCode, file.Path);

                        Directory.CreateDirectory(Path.GetDirectoryName(extractedPath));
                        File.WriteAllBytes(extractedPath, bytes);

                        lock (lines)
                            lines.Add(new Line(line, extractedPath));
                    }
                    else
                    {
                        Console.WriteLine($"Invalid MP3 {file.Path}:");
                        Console.WriteLine(error);
                    }
                }
            }
        });

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
        var oblivionPlugins = new List<string>()
        {
            "Oblivion.esm",
            "DLCShiveringIsles.esp",
            "Knights.esp",
        };

        // Only need archives containing dialogue
        var oblivionArchives = new List<string>()
        {
            "Oblivion - Voices1.bsa",
            "Oblivion - Voices2.bsa",
            "DLCShiveringIsles - Voices.bsa",
            "Knights.bsa",
        };

        // TODO: Dehardcode
        var oblivionDataRoot = @"E:\XboxGames\The Elder Scrolls IV- Oblivion (PC)\Content";

        var oblivionDataDirs = new List<string>()
        {
            @"Oblivion GOTY English\Data",
            @"Oblivion GOTY French\Data",
            @"Oblivion GOTY German\Data",
            @"Oblivion GOTY Italian\Data",
            @"Oblivion GOTY Spanish\Data",
        };

        // Like Oblivion, only include plugins and archives with dialogue
        var newVegasPlugins = new List<string>()
        {
            "FalloutNV.esm",
            "DeadMoney.esm",
            "HonestHearts.esm",
            "LonesomeRoad.esm",
            "OldWorldBlues.esm",
        };

        var newVegasArchives = new List<string>()
        {
            "Fallout - Voices1.bsa",
            "DeadMoney - Main.bsa",
            "HonestHearts - Main.bsa",
            "LonesomeRoad - Main.bsa",
            "OldWorldBlues - Main.bsa",
        };

        var data = ExtractData(oblivionPlugins, oblivionArchives, oblivionDataRoot, oblivionDataDirs);

        using var writer = new StreamWriter("extracted_data_oblivion.csv");
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(data);
    }
}