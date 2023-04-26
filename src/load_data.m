function [annotations, datastore] = load_data(csvPath, voicePath)
    annotations = readtable(csvPath, "Delimiter", ",");
    annotations.EmotionType = categorical(annotations.EmotionType);

    datastore = audioDatastore(voicePath, "IncludeSubfolders", true);

    % Create a dictionary mapping. Need to use the full paths here and convert to strings
    fullPaths = string(fullfile(datastore.Folders{1}, annotations.ExtractedPath));
    dict = dictionary(fullPaths, annotations.EmotionType);

    % Create a labels array for the datastore
    labels = arrayfun(@(x) dict(x), datastore.Files);

    datastore.Labels = labels;
end
