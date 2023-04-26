function write_counts_table(annotations, file)
    % Write category counts to file for LaTeX
    disp('Category counts:');
    names = categories(annotations);
    counts = countcats(annotations);
    for i = 1:length(names)
        fprintf('%s: %d\n', names{i}, counts(i));
    end

    countsTable = table(names, counts, 'VariableNames', ["Name", "Count"]);
    writetable(countsTable, file);
end
