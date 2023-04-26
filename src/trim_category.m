function [trimmed] = trim_category(data, category, newCount)
    % Trim the specified category to the given number of samples
    % This is done by randomly selecting samples to keep

    % Get the indices of the samples to keep
    indices = randsample(find(data.EmotionType == category), newCount);

    % Add all samples from other categories
    keep = [indices; find(data.EmotionType ~= category)];

    % Remove all samples not in the keep list
    trimmed = data(keep, :);
end
