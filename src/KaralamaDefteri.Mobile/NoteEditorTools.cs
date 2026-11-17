using System.Text.RegularExpressions;

namespace KaralamaDefteri.Mobile;

internal sealed partial class NotePage
{
    private readonly VerticalStackLayout checklistRows = new() { Spacing = 4, Padding = 6 };
    private readonly Entry findText = MobileStyle.Entry("Bul");
    private readonly Entry replacementText = MobileStyle.Entry("Değiştirilecek metin");
    private readonly VerticalStackLayout findPanel = new() { Spacing = 4, IsVisible = false };
    private ScrollView checklistView = null!;
    private Button checklistButton = null!;
    private Button checklistTextButton = null!;
    private Button? tagsButton;
    private bool checklistMode;
    private bool showChecklistText;
    private bool caseSensitive;
    private bool wholeWord;
    private bool regexSearch;

    protected override bool OnBackButtonPressed()
    {
        if (!findPanel.IsVisible) return base.OnBackButtonPressed();
        findPanel.IsVisible = false;
        showChecklistText = false;
        RefreshEditorTools();
        return true;
    }

    private void InitializeEditorTools(Grid layout)
    {
        var features = new FeatureFlags(store);
        checklistMode = note?.Checklist == true;
        var tools = new HorizontalStackLayout { Spacing = 6 };
        if (features.Enabled(Feature.Tags))
        {
            tagsButton = MobileStyle.Button("Etiketler", async () => await EditTags());
            tools.Children.Add(tagsButton);
        }
        checklistButton = MobileStyle.Button("Kontrol listesi", () =>
        {
            checklistMode = !checklistMode;
            showChecklistText = false;
            body.Text = ChecklistText.Convert(body.Text ?? "", checklistMode);
            dirty = true;
            Flush();
            RefreshEditorTools();
        });
        checklistButton.IsVisible = features.Enabled(Feature.Checklist);
        checklistTextButton = MobileStyle.Button("Metni düzenle", () =>
        {
            showChecklistText = !showChecklistText;
            RefreshEditorTools();
        });
        tools.Children.Add(checklistButton);
        tools.Children.Add(checklistTextButton);
        tools.Children.Add(MobileStyle.Button("Bul / değiştir", () =>
        {
            findPanel.IsVisible = !findPanel.IsVisible;
            if (findPanel.IsVisible) showChecklistText = true;
            RefreshEditorTools();
        }));
        var searchOptions = new HorizontalStackLayout { Spacing = 6 };
        AddSearchFlag(searchOptions, "Harf duyarlı", value => caseSensitive = value);
        AddSearchFlag(searchOptions, "Tam kelime", value => wholeWord = value);
        AddSearchFlag(searchOptions, "Regex", value => regexSearch = value);
        var findActions = new HorizontalStackLayout { Spacing = 6 };
        findActions.Children.Add(MobileStyle.Button("Sonrakini bul", () => FindOrReplace(false, false)));
        if (note?.Deleted is null)
        {
            findActions.Children.Add(MobileStyle.Button("Değiştir", () => FindOrReplace(true, false)));
            findActions.Children.Add(MobileStyle.Button("Tümünü değiştir", () => FindOrReplace(true, true)));
        }
        findPanel.Children.Add(findText);
        if (note?.Deleted is null) findPanel.Children.Add(replacementText);
        findPanel.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = searchOptions });
        findPanel.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = findActions });
        var toolbar = new VerticalStackLayout { Spacing = 4 };
        toolbar.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = tools });
        toolbar.Children.Add(findPanel);
        layout.Add(toolbar, 0, 1);
        var surface = new Grid();
        surface.Add(body);
        checklistView = new ScrollView { Content = checklistRows };
        surface.Add(checklistView);
        layout.Add(surface, 0, 2);
    }

    private static void AddSearchFlag(HorizontalStackLayout options, string text, Action<bool> change)
    {
        var box = new CheckBox();
        SemanticProperties.SetDescription(box, text);
        box.CheckedChanged += (_, args) => change(args.Value);
        var label = MobileStyle.Label(text, 12);
        label.VerticalOptions = LayoutOptions.Center;
        options.Children.Add(box);
        options.Children.Add(label);
    }

    private void RefreshEditorTools()
    {
        if (tagsButton is not null)
        {
            var count = store.TagNames(body.Text ?? "").Length;
            tagsButton.Text = count == 0 ? "Etiketler" : $"Etiketler ({count})";
        }
        var checklist = checklistButton.IsVisible && checklistMode;
        checklistButton.Text = checklist ? "Düz metne çevir" : "Kontrol listesi";
        checklistButton.IsEnabled = note?.Deleted is null;
        checklistTextButton.IsVisible = checklist;
        checklistTextButton.Text = showChecklistText ? "Listeyi göster" : "Metni düzenle";
        checklistView.IsVisible = checklist && !showChecklistText;
        body.IsVisible = !checklistView.IsVisible;
        if (!checklistView.IsVisible) return;
        checklistRows.Children.Clear();
        var lines = ChecklistText.Lines(body.Text ?? "");
        if (note?.Deleted is null)
        {
            var entry = MobileStyle.Entry("Yeni madde");
            var add = MobileStyle.Button("Ekle", () => AddItem());
            entry.Completed += (_, _) => AddItem();
            var header = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 6 };
            header.Add(entry);
            header.Add(add, 1);
            checklistRows.Children.Add(header);
            void AddItem()
            {
                if (string.IsNullOrWhiteSpace(entry.Text)) return;
                var content = body.Text ?? "";
                body.Text = content.Length == 0 ? $"[ ] {entry.Text.Trim()}" : $"{content}\n[ ] {entry.Text.Trim()}";
                Flush();
            }
        }
        foreach (var index in Enumerable.Range(0, lines.Length).OrderBy(i => ChecklistText.Complete(lines[i])))
        {
            var line = lines[index];
            if (ChecklistText.Item(line) && string.IsNullOrWhiteSpace(ChecklistText.Label(line))) continue;
            if (!ChecklistText.Item(line))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    checklistRows.Children.Add(MobileStyle.Label(ImageMarkers.Pattern().Replace(line, "Resim")));
                continue;
            }
            var complete = ChecklistText.Complete(line);
            var label = MobileStyle.Label(ChecklistText.Label(line));
            if (complete) label.TextDecorations = TextDecorations.Strikethrough;
            label.VerticalOptions = LayoutOptions.Center;
            var box = new CheckBox { IsChecked = complete, IsEnabled = note?.Deleted is null };
            SemanticProperties.SetDescription(box, ChecklistText.Label(line));
            box.CheckedChanged += (_, args) =>
            {
                lines[index] = (args.Value ? "[x] " : "[ ] ") + ChecklistText.Label(line);
                body.Text = string.Join("\n", lines);
                Flush();
            };
            var row = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
            row.Add(box);
            row.Add(label, 1);
            checklistRows.Children.Add(row);
        }
    }

    private async Task EditTags()
    {
        var tags = store.TagNames(body.Text ?? "").Select(tag => "#" + tag).ToArray();
        var readOnly = note?.Deleted is not null;
        var action = await DisplayActionSheet("Etiketler", "Kapat", null, readOnly ? tags : ["Etiket ekle", .. tags]);
        if (readOnly || action is null or "Kapat") return;
        if (action == "Etiket ekle")
        {
            var input = await DisplayPromptAsync("Etiket ekle", "Etiket adı", "Ekle", "Kapat", maxLength: 80);
            if (string.IsNullOrWhiteSpace(input)) return;
            var token = input.Trim().TrimStart('#');
            if (!Regex.IsMatch(token, @"\A[\p{L}\p{N}_-]+\z"))
            {
                feedback.Text = "Etikette harf, rakam, tire ve alt çizgi kullanılabilir.";
                return;
            }
            if (store.TagNames(body.Text ?? "").Contains(NoteStore.Fold(token))) return;
            var content = body.Text ?? "";
            body.Text = content.Length == 0 ? "#" + token : content + "\n#" + token;
        }
        else if (await DisplayActionSheet(action, "Kapat", "Etiketi kaldır") == "Etiketi kaldır")
            body.Text = NoteStore.RemoveTag(body.Text ?? "", action[1..]);
        else return;
        Flush();
    }

    private void FindOrReplace(bool replace, bool all)
    {
        if (string.IsNullOrEmpty(findText.Text)) { feedback.Text = "Aranacak metni gir."; return; }
        try
        {
            var content = body.Text ?? "";
            var pattern = regexSearch ? findText.Text : Regex.Escape(findText.Text);
            if (wholeWord) pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
            var regex = new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
            var markers = ImageMarkers.Pattern().Matches(content).Cast<Match>().ToArray();
            var matches = regex.Matches(content).Cast<Match>().Where(match => match.Length > 0 &&
                !markers.Any(marker => match.Index < marker.Index + marker.Length && match.Index + match.Length > marker.Index)).ToArray();
            if (matches.Length == 0) { feedback.Text = "Eşleşme bulunamadı."; return; }
            var start = body.CursorPosition;
            var length = body.SelectionLength;
            var selected = replace ? matches.FirstOrDefault(match => match.Index == start && match.Length == length) : null;
            var next = selected ?? matches.FirstOrDefault(match => match.Index >= start + length) ?? matches[0];
            if (replace)
            {
                var replacement = replacementText.Text ?? "";
                var edits = all ? matches : [next];
                var text = new System.Text.StringBuilder(content);
                foreach (var match in edits.Reverse())
                    text.Remove(match.Index, match.Length).Insert(match.Index, regexSearch ? match.Result(replacement) : replacement);
                body.Text = text.ToString();
                Flush();
                body.CursorPosition = Math.Min(next.Index, body.Text.Length);
                body.SelectionLength = 0;
                feedback.Text = $"{edits.Length} eşleşme değiştirildi.";
            }
            else
            {
                body.Focus();
                body.CursorPosition = next.Index;
                body.SelectionLength = next.Length;
                feedback.Text = $"{Array.IndexOf(matches, next) + 1}/{matches.Length} eşleşme";
            }
        }
        catch (RegexMatchTimeoutException) { feedback.Text = "Arama zaman aşımına uğradı."; }
        catch (ArgumentException) { feedback.Text = "Arama deseni veya değiştirme ifadesi geçersiz."; }
    }
}
