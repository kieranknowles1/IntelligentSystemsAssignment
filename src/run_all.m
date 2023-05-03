

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

[network_ob, testdata_ob, testlabels_ob] = train_single(annotations_ob, samplerate_ob, 'in/oblivion/voice/');
save('network_ob.mat', 'network_ob');

[network_nv, testdata_nv, testlabels_nv] = train_single(annotations_nv, samplerate_nv, 'in/newvegas/voice/');
save('network_nv.mat', 'network_nv');

% Benchmark both models

results_ob_same = test_network(network_ob, testdata_ob, testlabels_ob);
results_nv_same = test_network(network_nv, testdata_nv, testlabels_nv);

results_ob_other = test_network(network_ob, testdata_nv, testlabels_nv);
results_nv_other = test_network(network_nv, testdata_ob, testlabels_ob);

% Display confusion matrices and save them to files

figure;
subplot(2, 2, 1);
confusionmat(results_ob_same, categorical(testlabels_ob));
title('Oblivion - same dataset');
subplot(2, 2, 2);
confusionmat(results_ob_other, categorical(testlabels_nv));
title('Oblivion - different dataset');
subplot(2, 2, 3);
confusionmat(results_nv_same, categorical(testlabels_nv));
title('New Vegas - same dataset');
subplot(2, 2, 4);
confusionmat(results_nv_other, categorical(testlabels_ob));
title('New Vegas - different dataset');

saveas(gcf, 'out/confusion_matrices.svg');
