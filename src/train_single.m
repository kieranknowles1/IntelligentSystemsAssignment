function [network] = train_single(fileEmotion, sampleRate, voicePath)
    NUM_UNITS = 512;
    % Adjust based on available GPU memory. The option provided here works on a 10gb card
    BATCH_SIZE = 64;
    INITIAL_LEARNING_RATE = 0.005;
    MAX_EPOCHS = 100;

    % Create audio feature extractor
    % Features extracted are based on HelperExtractAudiotoryFeatures
    % From the week 10 workshop and the Speech Emotion Recognition example code
    % https://uk.mathworks.com/help/deeplearning/ug/sequential-feature-selection-for-speech-emotion-recognition.html
    extractor = audioFeatureExtractor( ...
        ... % Use a 0.025 second window
        Window=hann(round(0.025*sampleRate), 'periodic'), ...
        SampleRate=sampleRate, ...
        OverlapLength=round(0.010*sampleRate), ...
        barkSpectrum=true, ...
        gtcc=true, ...
        gtccDelta=true, ...
        mfccDelta=true, ...
        SpectralDescriptorInput='melSpectrum', ...
        spectralCrest=true ...
    );

    disp("Trim neutal annotations");
    trimmed = trim_category(fileEmotion, 'Neutral', 20000);

    % //TODO: 5% For testing
    % Split data into training, and validation sets.
    % 95% training, 5% validation. Can get away with this as the dataset is large
    disp("Split data");
    [files_train, files_validate, labels_train, labels_validate] = split_data(trimmed, 0.95);

    disp("Extract features");

    tallTrain = tall(files_train);
    tallTrain = cellfun(@(file) load_and_transform(extractor, fullfile(voicePath, file)), tallTrain, UniformOutput=false);

    tallValidate = tall(files_validate);
    tallValidate = cellfun(@(file) load_and_transform(extractor, fullfile(voicePath, file)), tallValidate, UniformOutput=false);

    disp("Creating layers");
    % Create a BiLSTM network
    % Use a dropout layer before and after the BiLSTM layer to reduce overfitting
    layers = [
        sequenceInputLayer(extractor.FeatureVectorLength)
        dropoutLayer()
        bilstmLayer(NUM_UNITS, OutputMode='last')
        dropoutLayer()
        % Flatten into a vector. 1 element per class
        fullyConnectedLayer(length(categories(fileEmotion.EmotionType)))
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
        ... % Save the state of the network every epoch.
        CheckpointPath=pwd() ...
        ... % Can't get this to work. Documentation is terrible and even ChatGPT wasn't helpful
        ... % ValidationData={validateData, validateLabels}
    );

    disp("Loading all training data");
    [predictors, labels] = remove_nans(gather(tallTrain), labels_train);

    disp("Training network. This will take longer");

    network = trainNetwork(predictors, labels, layers, options);
end
