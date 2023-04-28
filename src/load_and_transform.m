function [transformed] = load_and_transform(extractor, file)
    % If the audio contains multiple channels, average them
    data = mean(audioread(file), 2);
    transformed = extract(extractor, data)';
end
