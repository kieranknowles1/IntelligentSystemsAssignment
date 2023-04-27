function [annotations] = load_data(csvPath)
    annotations = readtable(csvPath, "Delimiter", ",");
    annotations.EmotionType = categorical(annotations.EmotionType);
end
