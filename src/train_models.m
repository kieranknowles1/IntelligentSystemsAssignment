NUM_UNITS = 200;
% Adjust based on available GPU memory. 256 works on a 10gb card
BATCH_SIZE = 256;
INITIAL_LEARNING_RATE = 0.005;
MAX_EPOCHS = 100;

if ~exist('data', 'var')
    data = load('extracted_data.mat');
end

% Create audio feature extractor
% Features extracted are based on HelperExtractAudiotoryFeatures
% From the week 10 workshop and the Speech Emotion Recognition example code
% https://uk.mathworks.com/help/deeplearning/ug/sequential-feature-selection-for-speech-emotion-recognition.html
extractor = audioFeatureExtractor( ...
    ... % Use a 0.025 second window
    Window=hann(round(0.025*data.samplerate_ob), 'periodic'), ...
    SampleRate=data.samplerate_ob, ...
    OverlapLength=round(0.010*data.samplerate_ob), ...
    barkSpectrum=true, ...
    gtcc=true, ...
    gtccDelta=true, ...
    mfccDelta=true, ...
    SpectralDescriptorInput='melSpectrum', ...
    spectralCrest=true ...
);

disp("Trim neutal annotations");
trimmed = trim_category(data.annotations_ob, 'Neutral', 20000);

% //TODO: 5% For testing
% Split data into training, and validation sets.
% 95% training, 5% validation. Can get away with this as the dataset is large
disp("Split data");
[files_train, files_validate, labels_train, labels_validate] = split_data(trimmed, 0.95);

disp("Extract features");

tallTrain = tall(files_train);
tallTrain = cellfun(@(file) load_and_transform(extractor, fullfile('in/oblivion/voice/', file)), tallTrain, UniformOutput=false);

tallValidate = tall(files_validate);
tallValidate = cellfun(@(file) load_and_transform(extractor, fullfile('in/oblivion/voice/', file)), tallValidate, UniformOutput=false);

disp("Creating layers");
% Create a BiLSTM network
% Use a dropout layer before and after the BiLSTM layer to reduce overfitting
layers = [
    sequenceInputLayer(extractor.FeatureVectorLength)
    dropoutLayer()
    bilstmLayer(NUM_UNITS, OutputMode='last')
    dropoutLayer()
    % Flatten into a vector. 1 element per class
    fullyConnectedLayer(length(categories(data.datastore_ob.Labels)))
    % Convert to probabilities
    softmaxLayer()
    classificationLayer()
];

disp("Creating training options. This will take a while");

[validateData, validateLabels] = remove_nans(gather(tallValidate), labels_validate);

options = trainingOptions("sgdm", ...
    Plots="training-progress", ...
    MaxEpochs=MAX_EPOCHS, ...
    MiniBatchSize=BATCH_SIZE, ...
    ... % Use the GPU for training. This is much faster than the CPU
    ExecutionEnvironment="multi-gpu", ...
    Shuffle="every-epoch", ...
    ... % Save the state of the network every epoch. May want to do this more often
    CheckpointPath=pwd() ...
    ... % Can't get this to work. Documentation is terrible and even ChatGPT wasn't helpful
    ... % ValidationData={validateData, validateLabels}
);

disp("Loading all training data");
[predictors, labels] = remove_nans(gather(tallTrain), labels_train);

disp("Training network. This will take longer");

network = trainNetwork(predictors, labels, layers, options);

disp("Saving trained network");
save("network_ob.mat", "network");

function [trainFiles, validateFiles, trainLabels, validateLabels] = split_data(data, trainFraction)
    numTrain = round(trainFraction * height(data));

    perms = randperm(height(data));

    trainFiles = data.ExtractedPath(perms(1:numTrain));
    validateFiles = data.ExtractedPath(perms(numTrain+1:end));

    trainLabels = data.EmotionType(perms(1:numTrain));
    validateLabels = data.EmotionType(perms(numTrain+1:end));
end

function [transformed] = load_and_transform(extractor, file)
    data = audioread(file);
    transformed = extract(extractor, data)';
end

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
