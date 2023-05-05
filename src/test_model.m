function [results] = test_model(network, data, labels)
    confidences = predict(network, data);

    [~, indexes] = max(confidences, [], 2);

    results = categorical(indexes, 1:length(labels), labels);
end

