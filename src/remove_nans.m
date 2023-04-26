function [cleaned, cleanedLabels] = remove_nans(data, labels)
    % Remove all rows with NaNs
    % Not sure how they got there in the first place

    good = zeros(height(data), 1);
    for i = 1:height(data)
        good(i) = ~anynan(data{i});
    end

    cleaned = data(find(good));
    cleanedLabels = labels(find(good));
end
