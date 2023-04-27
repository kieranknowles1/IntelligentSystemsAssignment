function write_counts_table(annotations, file)
    % Write category counts to file for LaTeX
    disp('Category counts:');
    names = categories(annotations);
    counts = countcats(annotations);
    total = sum(counts);
    fractions = round(counts / total, 2);
    for i = 1:length(names)
        fprintf('%s: %d\n', names{i}, counts(i));
    end

    countsTable = table(names, counts, fractions, 'VariableNames', ["Name", "Count", "Proportion"]);
    countsTable(end+1, :) = {'Total', total, 1.0};
    writetable(countsTable, file);
end
