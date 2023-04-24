using CsvHelper;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using NAudio.Vorbis;
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
    public Line(Mutagen.Bethesda.Oblivion.IDialogResponseGetter response, string extractedPath)
    {
        if (response.ResponseText == null || response.Data == null)
            throw new ArgumentException("Null text or data", nameof(response));

        Text = response.ResponseText;
        EmotionType = response.Data.Emotion.ToString();
        EmotionValue = response.Data.EmotionValue;
        ExtractedPath = extractedPath;
    }

    public Line(Mutagen.Bethesda.Skyrim.IDialogResponseGetter response, string extractedPath)
    {
        if (response.Text?.String == null)
            throw new ArgumentException("Null text", nameof(response));

        Text = response.Text.String;
        EmotionType = response.Emotion.ToString();

        // Skyrim changed this enum value to puzzled, was pained in new vegas
        if (response.Emotion == Emotion.Puzzled)
            EmotionType = "Pained";

        EmotionValue = (int)response.EmotionValue;
        ExtractedPath = extractedPath;
    }

    public string Text { get; }
    public string EmotionType { get; }
    public int EmotionValue { get; }
    // Path relative to voice\ directory
    public string ExtractedPath { get; }
}

public abstract class Patcher<TMod, TDialogInfo, TInfoEntry>
    where TMod : class, IModGetter
    where TDialogInfo : class, IMajorRecordGetter
{
    public static readonly Regex VoiceRegex = new(@"sound\\voice\\(?<plugin>\w+\.es[mp])\\\w+\\([mf]\\)?\w+[0-9a-f]{2}(?<info>[0-9a-f]{6})_(?<index>\d+)\.(mp3|ogg)", RegexOptions.IgnoreCase);

    /// <summary>
    /// Get the <see cref="InfoIndex"/> of a voice files INFO record
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public InfoIndex GetVoiceFormKey(string path)
    {
        var match = VoiceRegex.Match(path);
        if (!match.Success)
            throw new ArgumentException("Path does not match expected regex");

        var formKey = FormKey.Factory($"{match.Groups["info"]}:{match.Groups["plugin"]}");
        var index = int.Parse(match.Groups["index"].Value);

        return new InfoIndex(formKey, index);

    }

    // Some files are empty or nearly empty. Windows media player refuses to play these, no sure about MATLAB
    // Exclude them to be safe, these likely won't contribute much anyway
    public static readonly TimeSpan MinAudioDuration = TimeSpan.FromSeconds(0.25);

    public bool IsAudioValid(IArchiveFile file, [NotNullWhen(false)] out string? error)
    {
        if (file.Path.EndsWith(".mp3"))
            return IsMp3Valid(new MemoryStream(file.GetBytes()), out error);
        else if (file.Path.EndsWith(".ogg"))
            return IsOggValid(new MemoryStream(file.GetBytes()), out error);
        else
            throw new ArgumentException("Unsupported file type", nameof(file));
    }

    public bool IsMp3Valid(Stream data, [NotNullWhen(false)] out string? error)
    {
        try
        {
            using (var reader = new Mp3FileReader(data))
            {
                if (reader.TotalTime < MinAudioDuration)
                {
                    error = "File is too short";
                    return false;
                }

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

    public bool IsOggValid(Stream data, [NotNullWhen(false)] out string? error)
    {
        try
        {
            using (var reader = new VorbisWaveReader(data))
            {
                if (reader.TotalTime < MinAudioDuration)
                {
                    error = "File is too short";
                    return false;
                }

                while (reader.ReadByte() != -1)
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

    public abstract TMod CreateMod(ModPath path);

    public ILoadOrder<IModListing<TMod>> CreateLoadOrder(string dataDir, List<string> plugins)
    {
        var pluginMods = plugins
            .Select(plugin => CreateMod(Path.Combine(dataDir, plugin)))
            .Select(mod => new ModListing<TMod>(mod));

        return new LoadOrder<IModListing<TMod>>(pluginMods);
    }

    public abstract IEnumerable<TDialogInfo> EnumerateInfos(ILoadOrder<IModListing<TMod>> order);

    public abstract IReadOnlyList<TInfoEntry> GetResponses(TDialogInfo info);

    public abstract Line MakeLine(TInfoEntry infoEntry, string path);

    public List<Line> GetAndExtractLines(ILoadOrder<IModListing<TMod>> loadOrder, List<IArchiveReader> archives, string languageCode)
    {
        // Give a large initial capacity to reduce time spent resizing
        var lines = new List<Line>(65536);

        var byPath = archives
            .SelectMany(bsa => bsa.Files)
            // Use the last copy if a file exists in multiple BSAs. This is how the game handles it
            .Reverse()
            .DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Where(file => VoiceRegex.IsMatch(file.Path))
            .GroupBy(file => GetVoiceFormKey(file.Path))
            .ToDictionary(group => group.Key, group => group.ToList());


        Parallel.ForEach(EnumerateInfos(loadOrder), response =>
        {
            for (int i = 0; i < GetResponses(response).Count; i++)
            {
                var line = GetResponses(response)[i];

                if (!byPath.TryGetValue(new(response.FormKey, i + 1), out var files))
                    continue;

                foreach (var file in files)
                {
                    // Some files are empty or corrupted. Remove them here
                    if (IsAudioValid(file, out var error))
                    {
                        //Console.WriteLine($"Extract {file.Path}");
                        var extractedPath = Path.Combine("voice", languageCode, file.Path);

                        Directory.CreateDirectory(Path.GetDirectoryName(extractedPath));
                        File.WriteAllBytes(extractedPath, file.GetBytes());

                        var relativePath = Path.Combine(languageCode, file.Path);

                        lock (lines)
                            lines.Add(MakeLine(line, relativePath));
                    }
                    else
                    {
                        Console.WriteLine($"Invalid file {file.Path}:");
                        Console.WriteLine(error);
                    }
                }
            }
        });

        return lines;
    }

    public List<Line> ExtractData(List<string> plugins, List<string> archives, string dataRoot, List<string> dataDirs)
    {
        var lines = new List<Line>();

        foreach (var dataDir in dataDirs)
        {
            var fullPath = Path.Combine(dataRoot, dataDir);
            var loadOrder = CreateLoadOrder(fullPath, plugins);

            var thisArchives = archives
                .Select(bsa => Archive.CreateReader(GameRelease.Oblivion, Path.Combine(fullPath, bsa)))
                .ToList();

            lines.AddRange(GetAndExtractLines(loadOrder, thisArchives, dataDir));
        }

        return lines;
    }
}

public class OblivionPatcher : Patcher<IOblivionModGetter, IDialogItemGetter, Mutagen.Bethesda.Oblivion.IDialogResponseGetter>
{
    public override IOblivionModGetter CreateMod(ModPath path)
    {
        return OblivionMod.CreateFromBinaryOverlay(path);
    }

    public override IEnumerable<IDialogItemGetter> EnumerateInfos(IOblivionLoadOrder loadOrder)
    {
        return loadOrder.PriorityOrder.DialogItem().WinningOverrides();
    }

    public override IReadOnlyList<Mutagen.Bethesda.Oblivion.IDialogResponseGetter> GetResponses(IDialogItemGetter info)
    {
        return info.Responses;
    }

    public override Line MakeLine(Mutagen.Bethesda.Oblivion.IDialogResponseGetter infoEntry, string path)
    {
        return new Line(infoEntry, path);
    }
}

// Mutagen doesn't oficially supprot new vegas, but skyrim uses a similar enough format for dialog infos to be read
// Trying to load as OblivionMod threw an exception about an invalid GRUP
public class NvPatcher : Patcher<ISkyrimModGetter, IDialogResponsesGetter, Mutagen.Bethesda.Skyrim.IDialogResponseGetter>
{
    public override ISkyrimModGetter CreateMod(ModPath path)
    {
        return SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimLE);
    }

    public override IEnumerable<IDialogResponsesGetter> EnumerateInfos(ILoadOrder<IModListing<ISkyrimModGetter>> order)
    {
        return order.PriorityOrder.DialogResponses().WinningOverrides();
    }

    public override IReadOnlyList<Mutagen.Bethesda.Skyrim.IDialogResponseGetter> GetResponses(IDialogResponsesGetter info)
    {
        return info.Responses;
    }

    public override Line MakeLine(Mutagen.Bethesda.Skyrim.IDialogResponseGetter infoEntry, string path)
    {
        return new Line(infoEntry, path);
    }
}

public static class Program
{
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
            // These versions use the English recordings for dialogue
            //@"Oblivion GOTY Italian\Data",
            //@"Oblivion GOTY Spanish\Data",
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

        var newVegasDataRoot = @"E:\XboxGames\Fallout- New Vegas Ultimate Edition (PC)\Content";

        var newVegasDataDirs = new List<string>()
        {
            @"Fallout New Vegas English\Data",
            @"Fallout New Vegas French\Data",
            @"Fallout New Vegas German\Data",
            @"Fallout New Vegas Italian\Data",
            @"Fallout New Vegas Spanish\Data",
        };

        var data = new OblivionPatcher().ExtractData(oblivionPlugins, oblivionArchives, oblivionDataRoot, oblivionDataDirs);

        using var writer = new StreamWriter("extracted_data_oblivion.csv");
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(data);

        var dataNv = new NvPatcher().ExtractData(newVegasPlugins, newVegasArchives, newVegasDataRoot, newVegasDataDirs);
        using var writerNv = new StreamWriter("extracted_data_new_vegas.csv");
        using var csvNv = new CsvWriter(writerNv, CultureInfo.InvariantCulture);
        csvNv.WriteRecords(dataNv);
    }
}