function [transformed] = load_and_transform(extractor, file)
    data = audioread(file);
    transformed = extract(extractor, data)';
end