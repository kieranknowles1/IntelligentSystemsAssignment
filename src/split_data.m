function [trainFiles, validateFiles, trainLabels, validateLabels] = split_data(data, trainFraction)
    numTrain = round(trainFraction * height(data));

    perms = randperm(height(data));

    trainFiles = data.ExtractedPath(perms(1:numTrain));
    validateFiles = data.ExtractedPath(perms(numTrain+1:end));

    trainLabels = data.EmotionType(perms(1:numTrain));
    validateLabels = data.EmotionType(perms(numTrain+1:end));
end
