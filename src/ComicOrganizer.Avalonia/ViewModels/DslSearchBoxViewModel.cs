using ComicOrganizer.App.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ComicOrganizer.Avalonia.ViewModels;

public sealed partial class DslTokenBlockVm : ObservableObject
{
    private readonly Action<DslTokenBlockVm> _onRemove;
    private readonly Action<DslTokenBlockVm> _onEdit;

    public DslTokenBlockVm(
        SearchTerm term,
        Action<DslTokenBlockVm> onRemove,
        Action<DslTokenBlockVm> onEdit)
    {
        ArgumentNullException.ThrowIfNull(term);
        Term = term;
        _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
        _onEdit = onEdit ?? throw new ArgumentNullException(nameof(onEdit));

        Icon = term.Negated ? "🚫" : SearchQueryParser.GetFieldIcon(term.Field);
        string label = SearchQueryParser.GetFieldDisplayLabel(term.Field);
        FieldBadgeText = term.Negated ? $"-{label}" : label;
        ValueText = term.Value;
        AccentHex = PickFieldAccentHex(term.Field, term.Negated);
    }

    public SearchTerm Term { get; }

    public SearchField Field => Term.Field;

    public string Value => Term.Value;

    public bool IsNegated => Term.Negated;

    public string Icon { get; }

    public string FieldBadgeText { get; }

    public string ValueText { get; }

    public string AccentHex { get; }

    public string TooltipText => IsNegated
        ? $"排除條件：{FieldBadgeText}:{ValueText}（點擊修改，按 ✕ 移除）"
        : $"篩選條件：{FieldBadgeText}:{ValueText}（點擊修改，按 ✕ 移除）";

    [RelayCommand]
    private void Remove() => _onRemove(this);

    [RelayCommand]
    private void Edit() => _onEdit(this);

    private static string PickFieldAccentHex(SearchField field, bool negated)
    {
        if (negated)
        {
            return "#EF4444";
        }

        return field switch
        {
            SearchField.Artist => "#3B82F6",
            SearchField.Tag => "#8B5CF6",
            SearchField.Series => "#10B981",
            SearchField.TranslationGroup => "#06B6D4",
            SearchField.Language => "#F59E0B",
            SearchField.ReadStatus => "#EC4899",
            _ => "#64748B",
        };
    }
}

public sealed partial class DslSuggestionItemVm : ObservableObject
{
    private readonly Action<DslSuggestionItemVm> _onSelect;

    public DslSuggestionItemVm(
        DslSuggestionItem source,
        bool isSelected,
        Action<DslSuggestionItemVm> onSelect)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        _isSelected = isSelected;
        _onSelect = onSelect ?? throw new ArgumentNullException(nameof(onSelect));
    }

    public DslSuggestionItem Source { get; }

    public string Icon => Source.Icon;

    public string CategoryBadge => Source.CategoryBadge;

    public string DisplayText => Source.DisplayText;

    public string SubText => Source.SubText;

    public bool IsFieldPrefix => Source.Kind == DslSuggestionKind.FieldPrefix;

    public string ActionHintText => IsFieldPrefix ? "選欄位 ▸" : "轉區塊 ↵";

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => _onSelect(this);
}

public sealed class DslQuickFieldChipVm
{
    private readonly Action<DslFieldDescriptor> _onActivate;

    public DslQuickFieldChipVm(DslFieldDescriptor descriptor, Action<DslFieldDescriptor> onActivate)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _onActivate = onActivate ?? throw new ArgumentNullException(nameof(onActivate));
        DisplayLabel = $"{descriptor.Icon} @{descriptor.Key}: {descriptor.Label}";
        TooltipText = $"點擊插入 @{descriptor.Key}: 並列出資料庫中的{descriptor.Label}選項";
        ActivateCommand = new RelayCommand(() => _onActivate(Descriptor));
    }

    public DslFieldDescriptor Descriptor { get; }

    public string DisplayLabel { get; }

    public string TooltipText { get; }

    public IRelayCommand ActivateCommand { get; }
}

/// <summary>
/// 支援 <c>@</c> / <c>/</c> 觸發雙階段資料庫 Auto-Complete 與標籤區塊化（Token Chips）的搜尋框 ViewModel。
/// </summary>
public sealed partial class DslSearchBoxViewModel : ViewModelBase
{
    private readonly IAssetMetaStore _store;
    private readonly string _lang;
    private bool _suppressInputHandling;

    public DslSearchBoxViewModel(IAssetMetaStore store, string lang = "zh-TW")
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lang = lang;

        QuickFieldChips = DslSuggestionService.SupportedFields
            .Select(d => new DslQuickFieldChipVm(d, OnQuickFieldActivated))
            .ToList();
    }

    public BatchObservableCollection<DslTokenBlockVm> TokenBlocks { get; } = [];

    public BatchObservableCollection<DslSuggestionItemVm> Suggestions { get; } = [];

    public IReadOnlyList<DslQuickFieldChipVm> QuickFieldChips { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyContent))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyContent))]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private string _watermark = "輸入關鍵字，或打 @ 喚出標籤／作者區塊自動完成（例如 @artist: 或 -@tag:）";

    [ObservableProperty]
    private bool _isSuggestionPopupOpen;

    [ObservableProperty]
    private int _selectedSuggestionIndex = -1;

    public bool HasBlocks => TokenBlocks.Count > 0;

    public bool HasAnyContent => TokenBlocks.Count > 0 || !string.IsNullOrWhiteSpace(InputText);

    public DslSuggestionItemVm? SelectedSuggestion =>
        SelectedSuggestionIndex >= 0 && SelectedSuggestionIndex < Suggestions.Count
            ? Suggestions[SelectedSuggestionIndex]
            : null;

    /// <summary>當組合後的 DSL 查詢字串發生變更時觸發。</summary>
    public event Action<string>? QueryChanged;

    /// <summary>當使用者新增／移除區塊或按下 Enter 明確提交搜尋時觸發。</summary>
    public event Action<string>? SearchSubmitted;

    /// <summary>通知 View 將鍵盤焦點移回文字框末端。</summary>
    public event Action? FocusInputEndRequested;

    partial void OnInputTextChanged(string value)
    {
        if (_suppressInputHandling)
        {
            return;
        }

        // 若使用者以空白結束一個完整結構化 token（例如 "@artist:Flugel "），自動收斂為區塊膠囊
        if (DslSuggestionService.TryExtractCompletedTrailingBlock(value, out var completedBlock, out var remainingText)
            && completedBlock != null)
        {
            _suppressInputHandling = true;
            try
            {
                AddBlockInternal(completedBlock);
                InputText = remainingText;
            }
            finally
            {
                _suppressInputHandling = false;
            }

            RefreshSuggestions(InputText);
            SyncComposedQuery(submit: true);
            FocusInputEndRequested?.Invoke();
            return;
        }

        RefreshSuggestions(value);
        SyncComposedQuery(submit: false);
    }

    partial void OnSelectedSuggestionIndexChanged(int value)
    {
        for (int i = 0; i < Suggestions.Count; i++)
        {
            Suggestions[i].IsSelected = i == value;
        }
    }

    [RelayCommand]
    public void TriggerAtMenu()
    {
        if (string.IsNullOrEmpty(InputText))
        {
            InputText = "@";
        }
        else if (!InputText.EndsWith('@') && !char.IsWhiteSpace(InputText[^1]))
        {
            InputText = InputText + " @";
        }
        else if (char.IsWhiteSpace(InputText[^1]))
        {
            InputText = InputText + "@";
        }
        else
        {
            RefreshSuggestions(InputText);
        }

        FocusInputEndRequested?.Invoke();
    }

    [RelayCommand]
    public void ClearAll()
    {
        _suppressInputHandling = true;
        try
        {
            TokenBlocks.Clear();
            InputText = string.Empty;
        }
        finally
        {
            _suppressInputHandling = false;
        }

        ClosePopup();
        NotifyBlockMetrics();
        SyncComposedQuery(submit: true);
        FocusInputEndRequested?.Invoke();
    }

    [RelayCommand]
    public void SubmitSearch()
    {
        CommitAnyTrailingStructuredToken();
        ClosePopup();
        SyncComposedQuery(submit: true);
    }

    [RelayCommand]
    public void ClosePopup()
    {
        IsSuggestionPopupOpen = false;
        Suggestions.Clear();
        SelectedSuggestionIndex = -1;
    }

    public void MoveSelectionDown()
    {
        if (!IsSuggestionPopupOpen || Suggestions.Count == 0)
        {
            RefreshSuggestions(InputText);
            return;
        }

        SelectedSuggestionIndex = (SelectedSuggestionIndex + 1) % Suggestions.Count;
    }

    public void MoveSelectionUp()
    {
        if (!IsSuggestionPopupOpen || Suggestions.Count == 0)
        {
            return;
        }

        SelectedSuggestionIndex = (SelectedSuggestionIndex - 1 + Suggestions.Count) % Suggestions.Count;
    }

    /// <summary>
    /// 處理由鍵盤 Enter 或 Tab 觸發的行為：
    /// - 若下拉選單開啟中且有選取項目，套用該建議（第一階段展開欄位，第二階段收斂成區塊 Chip）。
    /// - 若下拉選單未開啟且按下 Enter（<paramref name="isTab"/> = false），將末尾完整欄位 token 收斂為區塊並送出搜尋。
    /// </summary>
    public bool TryHandleEnterOrTab(bool isTab)
    {
        if (IsSuggestionPopupOpen && SelectedSuggestion is { } selected)
        {
            AcceptSuggestion(selected);
            return true;
        }

        if (isTab)
        {
            return false;
        }

        SubmitSearch();
        return true;
    }

    /// <summary>當輸入框為空時按下 Backspace，移除最後一顆標籤區塊。</summary>
    public bool TryHandleBackspaceWhenInputEmpty()
    {
        if (!string.IsNullOrEmpty(InputText) || TokenBlocks.Count == 0)
        {
            return false;
        }

        TokenBlocks.RemoveAt(TokenBlocks.Count - 1);
        NotifyBlockMetrics();
        SyncComposedQuery(submit: true);
        return true;
    }

    public void AcceptSuggestion(DslSuggestionItemVm? suggestionVm)
    {
        if (suggestionVm == null)
        {
            return;
        }

        var item = suggestionVm.Source;
        string current = InputText ?? string.Empty;
        int start = Math.Clamp(item.TokenStartIndex, 0, current.Length);
        string before = current[..start];

        if (item.Kind == DslSuggestionKind.FieldPrefix)
        {
            // 第一階段：把 "@art" 替換為 "@artist:" 並立即展開第二階段 DB 候選清單
            _suppressInputHandling = true;
            try
            {
                InputText = before + item.InsertValue;
            }
            finally
            {
                _suppressInputHandling = false;
            }

            RefreshSuggestions(InputText);
            SyncComposedQuery(submit: false);
            FocusInputEndRequested?.Invoke();
            return;
        }

        // 第二階段：將選中的 DB 作者／標籤／系列直接收斂為區塊化 Chip
        _suppressInputHandling = true;
        try
        {
            AddBlockInternal(new SearchTerm(item.Field, item.InsertValue, item.Negated));
            InputText = before.TrimEnd();
            if (InputText.Length > 0)
            {
                InputText += " ";
            }
        }
        finally
        {
            _suppressInputHandling = false;
        }

        ClosePopup();
        SyncComposedQuery(submit: true);
        FocusInputEndRequested?.Invoke();
    }

    /// <summary>由外部（例如點擊虛擬庫節點、模糊建議或探索牆跳轉）設定完整 DSL 字串並自動區塊化。</summary>
    public void SetQueryFromExternal(string? rawDsl, bool triggerSubmit = false)
    {
        var normalized = rawDsl?.Trim() ?? string.Empty;
        var (blocks, freeText) = DslSuggestionService.SplitQueryIntoBlocksAndFreeText(normalized);

        _suppressInputHandling = true;
        try
        {
            var vms = blocks.Select(CreateBlockVm).ToList();
            TokenBlocks.ReplaceAll(vms);
            InputText = freeText;
        }
        finally
        {
            _suppressInputHandling = false;
        }

        ClosePopup();
        NotifyBlockMetrics();
        SyncComposedQuery(submit: triggerSubmit);
    }

    public void RefreshSuggestionsNow() => RefreshSuggestions(InputText);

    private void OnQuickFieldActivated(DslFieldDescriptor descriptor)
    {
        string prefix = $"@{descriptor.Key}:";
        string current = InputText ?? string.Empty;

        _suppressInputHandling = true;
        try
        {
            if (string.IsNullOrWhiteSpace(current))
            {
                InputText = prefix;
            }
            else
            {
                int lastStart = DslSuggestionService.FindLastUnquotedTokenStart(current);
                var lastToken = current[lastStart..];
                if (lastToken is "@" or "-@" or "/" or "-/" || lastToken.EndsWith(':'))
                {
                    bool neg = lastToken.StartsWith('-');
                    InputText = current[..lastStart] + (neg ? "-" : string.Empty) + prefix;
                }
                else if (char.IsWhiteSpace(current[^1]))
                {
                    InputText = current + prefix;
                }
                else
                {
                    InputText = current + " " + prefix;
                }
            }
        }
        finally
        {
            _suppressInputHandling = false;
        }

        RefreshSuggestions(InputText);
        FocusInputEndRequested?.Invoke();
    }

    private void CommitAnyTrailingStructuredToken()
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            return;
        }

        var padded = InputText.TrimEnd() + " ";
        if (DslSuggestionService.TryExtractCompletedTrailingBlock(padded, out var completedBlock, out var remainingText)
            && completedBlock != null)
        {
            _suppressInputHandling = true;
            try
            {
                AddBlockInternal(completedBlock);
                InputText = remainingText.TrimEnd();
            }
            finally
            {
                _suppressInputHandling = false;
            }
        }
    }

    private void AddBlockInternal(SearchTerm term)
    {
        bool exists = TokenBlocks.Any(b =>
            b.Field == term.Field
            && b.IsNegated == term.Negated
            && string.Equals(b.Value, term.Value, StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            TokenBlocks.Add(CreateBlockVm(term));
            NotifyBlockMetrics();
        }
    }

    private DslTokenBlockVm CreateBlockVm(SearchTerm term)
        => new(term, OnRemoveBlock, OnEditBlock);

    private void OnRemoveBlock(DslTokenBlockVm block)
    {
        if (TokenBlocks.Remove(block))
        {
            NotifyBlockMetrics();
            SyncComposedQuery(submit: true);
            FocusInputEndRequested?.Invoke();
        }
    }

    private void OnEditBlock(DslTokenBlockVm block)
    {
        if (!TokenBlocks.Remove(block))
        {
            return;
        }

        NotifyBlockMetrics();
        string tokenText = SearchQueryParser.FormatTermToken(block.Term, includeAtPrefix: true);

        _suppressInputHandling = true;
        try
        {
            var trimmed = (InputText ?? string.Empty).TrimEnd();
            InputText = string.IsNullOrEmpty(trimmed) ? tokenText : $"{trimmed} {tokenText}";
        }
        finally
        {
            _suppressInputHandling = false;
        }

        RefreshSuggestions(InputText);
        SyncComposedQuery(submit: false);
        FocusInputEndRequested?.Invoke();
    }

    private void RefreshSuggestions(string? currentInput)
    {
        var items = DslSuggestionService.GetSuggestions(_store, currentInput, _lang, maxResults: 12);
        if (items.Count == 0)
        {
            ClosePopup();
            return;
        }

        var vms = items
            .Select((item, idx) => new DslSuggestionItemVm(item, idx == 0, AcceptSuggestion))
            .ToList();

        Suggestions.ReplaceAll(vms);
        SelectedSuggestionIndex = 0;
        IsSuggestionPopupOpen = true;
    }

    private void SyncComposedQuery(bool submit)
    {
        var composed = DslSuggestionService.ComposeDslQuery(
            TokenBlocks.Select(b => b.Term),
            InputText);

        bool changed = !string.Equals(QueryText, composed, StringComparison.Ordinal);
        if (changed)
        {
            QueryText = composed;
            QueryChanged?.Invoke(composed);
        }

        if (submit)
        {
            SearchSubmitted?.Invoke(composed);
        }
    }

    private void NotifyBlockMetrics()
    {
        OnPropertyChanged(nameof(HasBlocks));
        OnPropertyChanged(nameof(HasAnyContent));
    }
}
