

% Import data from CSVs
% Use the CSV generated by DataExtraction
% This is easier than calling C# directly

annotations_ob = load_data("in/oblivion/extracted_data_oblivion.csv");
samplerate_ob = 44100;
write_counts_table(annotations_ob.EmotionType, "out/category_counts_oblivion.csv");

annotations_nv = load_data("in/newvegas/extracted_data_new_vegas.csv");
samplerate_nv = 22050;
write_counts_table(annotations_nv.EmotionType, "out/category_counts_new_vegas.csv");

save('extracted_data.mat');

% Train both models

network_ob = train_single(annotations_ob, samplerate_ob, 'in/oblivion/voice/');
save('network_ob.mat', 'network_ob');

network_nv = train_single(annotations_nv, samplerate_nv, 'in/newvegas/voice/');
save('network_nv.mat', 'network_nv');

% //TODO: Benchmark both models