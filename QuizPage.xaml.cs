using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using System.Text.Json;
using System.Text;
using Microsoft.Maui.ApplicationModel;

namespace 爸爸的爱;

// 占位类型：满足 XAML 的 <local:NoImeEntry/> 引用（已隐藏不显示、不弹键盘）
public class NoImeEntry : Entry { }

public partial class QuizPage : ContentPage
{
    //题目与状态（保持原有字段)
    private readonly List<Vocab> _quizList;
    private int _currentIndex =0;
    private int _hintCount =0;
    private const int MaxHints =3;
    private int _wrongCount =0;
    private const int MaxWrong =3;
    private int _correctCount =0;
    private readonly Action<bool, int, int> _onQuizRoundFinished;
    private int _timePerQuestion =30;
    private CancellationTokenSource _cts;
    private bool _isQuizFinished = false;
    private bool _writtenMode = false;
    private int _totalHintCount =0;
    private HashSet<int> _wrongIndexes = new();
    private const int MaxWrongWords =4;

    private List<string> _userAnswers = new();
    private int _submitCount =0;
    private bool _isShift = false;

    private readonly Color KeyPressed = Color.FromArgb("#909090");

    // 自绘输入框文本与光标
    private string _text = string.Empty;
    private int _caret =0; // 光标位置（0.._text.Length)
    private bool _caretBlink = true;
    private CancellationTokenSource _blinkCts;
    private double _panStartX;
    private int _panStartCaret;

    public QuizPage(Action<bool, int, int> onQuizRoundFinished)
    {
        InitializeComponent();
        _onQuizRoundFinished = onQuizRoundFinished;

        //读取设置
        var selectedUnits = SettingsPage.GetSelectedUnits();
        int questionCount = SettingsPage.GetQuestionCount();
        _timePerQuestion = SettingsPage.GetTimePerQuestion();
        _writtenMode = Preferences.Get("WrittenMode", false);

        // 保证每次进入都重置状态
        _isQuizFinished = false;
        _currentIndex =0;
        _wrongCount =0;
        _correctCount =0;
        _hintCount =0;

        // 按设置筛选题库，每个单元至少1题
        var pool = VocabRepository.AllVocabs
            .Where(v => selectedUnits.Any(su => su.book == v.Book && su.unit == v.Unit))
            .ToList();

        // 保证每个单元至少1题
        var perUnit = selectedUnits
            .Select(su => pool.Where(v => v.Book == su.book && v.Unit == su.unit).OrderBy(_ => Guid.NewGuid()).FirstOrDefault())
            .Where(v => v != null)
            .ToList();

        // 剩余题目随机补足
        var rest = pool.Except(perUnit).OrderBy(_ => Guid.NewGuid()).Take(Math.Max(questionCount - perUnit.Count,0)).ToList();
        _quizList = perUnit.Concat(rest).ToList();

        UpdateModeUI();
        ShowCurrentQuestion();
        StartCaretBlink();
    }

    private void StartCaretBlink()
    {
        _blinkCts?.Cancel();
        _blinkCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!_blinkCts.IsCancellationRequested)
            {
                await Task.Delay(500);
                if (_blinkCts.IsCancellationRequested) break;
                _caretBlink = !_caretBlink;
                MainThread.BeginInvokeOnMainThread(() => CaretBox.IsVisible = _caretBlink && !_writtenMode);
            }
        });
    }

    private void UpdateInputVisual()
    {
        var left = _caret <= _text.Length ? _text.Substring(0, _caret) : _text;
        var right = _caret <= _text.Length ? _text.Substring(_caret) : string.Empty;
        BeforeLabel.Text = left;
        AfterLabel.Text = right;
        CaretBox.IsVisible = !_writtenMode && _caretBlink;
    }

    private void SetText(string value, int? caret = null)
    {
        _text = value ?? string.Empty;
        _caret = Math.Clamp(caret ?? _text.Length,0, _text.Length);
        UpdateInputVisual();
    }

    private void InsertAtCaret(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        _text = _text.Insert(_caret, s);
        _caret += s.Length;
        UpdateInputVisual();
    }

    private void BackspaceAtCaret()
    {
        if (_caret <=0 || _text.Length ==0) return;
        _text = _text.Remove(_caret -1,1);
        _caret--;
        UpdateInputVisual();
    }

    private void MoveCaretBy(int delta)
    {
        _caret = Math.Clamp(_caret + delta,0, _text.Length);
        UpdateInputVisual();
    }

    private void UpdateModeUI()
    {
        // 自绘输入框始终显示；仅控制虚拟键盘/按钮
        if (_writtenMode)
        {
            SubmitButton.IsVisible = false;
            NextButton.IsVisible = true;
            VirtualKeyboardGrid.IsVisible = false;
        }
        else
        {
            SubmitButton.IsVisible = true;
            NextButton.IsVisible = false;
            VirtualKeyboardGrid.IsVisible = true;
        }
    }

    private async void ShowCurrentQuestion()
    {
        if (_isQuizFinished) return;
        if (_currentIndex >= _quizList.Count)
        {
            _isQuizFinished = true;
            if (_writtenMode)
                await ShowSummaryAndScoreInput();
            else
                await ShowSummaryAndAutoFinish();
            return;
        }
        var vocab = _quizList[_currentIndex];
        QuestionLabel.Text = vocab.Meaning;
        FeedbackLabel.Text = string.Empty;
        _hintCount =0;
        _submitCount =0;
        HintButton.IsEnabled = !_writtenMode && _totalHintCount < MaxHints;
        PhoneticLabel.IsVisible = false; // 切题时隐藏音标

        SetText(string.Empty,0);

        // 初始化倒计时
        TimerLabel.Text = $"倒计时：{_timePerQuestion}秒";

        // 启动倒计时
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = StartTimer(_cts.Token);
    }

    // 若当前题没有答案，写入“(未作答)”占位
    private void EnsureAnswerPlaceholder(int index)
    {
        if (_userAnswers.Count <= index) _userAnswers.Add("(未作答)");
        else if (string.IsNullOrWhiteSpace(_userAnswers[index])) _userAnswers[index] = "(未作答)";
    }

    //结束前补齐答案长度
    private void NormalizeUserAnswers()
    {
        while (_userAnswers.Count < _quizList.Count) _userAnswers.Add("(未作答)");
    }

    private async Task StartTimer(CancellationToken token)
    {
        int seconds = _timePerQuestion;
        while (seconds >0)
        {
            TimerLabel.Text = $"倒计时：{seconds}秒";
            await Task.Delay(1000);
            if (token.IsCancellationRequested || _isQuizFinished) return;
            seconds--;
        }
        TimerLabel.Text = "倒计时：0秒";
        if (_isQuizFinished) return;

        if (_writtenMode)
        {
            // 笔答模式下，超时视为未作答并进入下一题
            EnsureAnswerPlaceholder(_currentIndex);
            await Task.Delay(300);
            _currentIndex++;
            ShowCurrentQuestion();
        }
        else
        {
            // 普通模式下，超时计为错误并跳过，记录未作答
            if (!_wrongIndexes.Contains(_currentIndex)) { _wrongIndexes.Add(_currentIndex); _wrongCount++; }
            EnsureAnswerPlaceholder(_currentIndex);
            FeedbackLabel.Text = "超时，自动进入下一题！";
            await Task.Delay(500);
            _currentIndex++;
            ShowCurrentQuestion();
            if (_wrongCount >= MaxWrongWords) { await FailAndExit("错误超过3个单词，闯关失败！"); }
        }
    }

    // 输入区域触摸：点击/拖动定位光标
    private void OnInputTouchStart(object sender, TouchEventArgs e)
    {
        LocateCaretByTouch(e.Touches[0].X);
        _panStartX = e.Touches[0].X;
        _panStartCaret = _caret;
    }
    private void OnInputTouchDrag(object sender, TouchEventArgs e)
    {
        var dx = e.Touches[0].X - _panStartX;
        int step = (int)(dx /14); //估算每14px移动一个字符（与字号近似）
        _caret = Math.Clamp(_panStartCaret + step,0, _text.Length);
        UpdateInputVisual();
    }
    private void OnInputTouchEnd(object sender, TouchEventArgs e)
    {
        // nothing
    }

    private void LocateCaretByTouch(double x)
    {
        // 简化估算：使用 BeforeLabel 的字体宽度近似（每字符 ~14px ，与你设置的 FontSize 接近）
        double width = InputGrid.Width -2; // 留出边距
        if (width <=0 || _text.Length ==0) { _caret =0; UpdateInputVisual(); return; }
        double avg = Math.Max(width / Math.Max(_text.Length,1),8);
        int pos = (int)Math.Round(x / avg);
        _caret = Math.Clamp(pos,0, _text.Length);
        UpdateInputVisual();
    }

    private async void OnKeyButtonClicked(object sender, EventArgs e)
    {
        if (_writtenMode) return;
        if (sender is Button btn)
        {
            await PressVisual(btn);
            string ch = btn.Text;
            ch = _isShift ? ch.ToUpperInvariant() : ch.ToLowerInvariant();
            InsertAtCaret(ch);
        }
    }

    private async Task PressVisual(Button btn)
    {
        var old = btn.BackgroundColor;
        btn.BackgroundColor = KeyPressed;
        await Task.Delay(80);
        btn.BackgroundColor = old;
    }

    private void OnShiftClicked(object sender, EventArgs e)
    {
        _isShift = !_isShift;
        if (sender is Button b) b.Text = _isShift ? "大写" : "小写";
        RefreshKeyboardCase();
    }

    private void RefreshKeyboardCase()
    {
        foreach (var b in VirtualKeyboardGrid.LogicalChildren.OfType<Grid>().SelectMany(g => g.LogicalChildren.OfType<Button>()))
        {
            if ((b.ClassId ?? "") != "Letter") continue;
            if (string.IsNullOrEmpty(b.Text) || b.Text == ".") continue;
            b.Text = _isShift ? b.Text.ToUpperInvariant() : b.Text.ToLowerInvariant();
        }
    }

    private void OnSpacePanUpdated(object sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartX = e.TotalX; _panStartCaret = _caret; break;
            case GestureStatus.Running:
                var dx = e.TotalX - _panStartX; int step = (int)(dx /20); _caret = Math.Clamp(_panStartCaret + step,0, _text.Length); UpdateInputVisual(); break;
        }
    }
    private async void OnSpaceClicked(object sender, EventArgs e)
    { if (_writtenMode) return; if (sender is Button b) await PressVisual(b); InsertAtCaret(" "); }
    private async void OnBackspaceClicked(object sender, EventArgs e)
    { if (_writtenMode) return; if (sender is Button b) await PressVisual(b); BackspaceAtCaret(); }

    private void OnHintClicked(object sender, EventArgs e)
    {
        if (_isQuizFinished) return;
        var vocab = _quizList[_currentIndex];
        if (_writtenMode)
        {
            PhoneticLabel.IsVisible = true;
            PhoneticLabel.Text = string.IsNullOrWhiteSpace(vocab.Phonetic) ? "无音标" : $"音标：{vocab.Phonetic}";
            HintButton.IsEnabled = false;
        }
        else
        {
            if (_hintCount ==0 && _totalHintCount < MaxHints)
            {
                int len = Math.Min(3, vocab.Word.Length);
                SetText(vocab.Word.Substring(0, len), len); // 光标在末尾
                _hintCount =1; _totalHintCount++; HintButton.IsEnabled = false; if (_totalHintCount >= MaxHints) HintButton.IsEnabled = false;
            }
        }
    }

    // 下一个按钮（仅笔答模式下可见）
    private void OnNextClicked(object sender, EventArgs e)
    {
        if (_isQuizFinished) return;
        _cts?.Cancel();
        EnsureAnswerPlaceholder(_currentIndex);
        _currentIndex++;
        ShowCurrentQuestion();
    }

    private async void OnSubmitClicked(object sender, EventArgs e)
    {
        if (_isQuizFinished || _writtenMode) return;
        var vocab = _quizList[_currentIndex];
        var userInput = _text.Trim();
        if (_userAnswers.Count <= _currentIndex) _userAnswers.Add(userInput); else _userAnswers[_currentIndex] = userInput;
        _submitCount++;
        if (string.Equals(userInput, vocab.Word, StringComparison.OrdinalIgnoreCase))
        {
            _cts?.Cancel();
            _correctCount++;
            _currentIndex++;
            SetText(string.Empty,0);
            ShowCurrentQuestion();
        }
        else
        {
            if (_submitCount <3) { FeedbackLabel.Text = "错误，请重试！"; await ShakeEntry(InputHost); }
            else { if (!_wrongIndexes.Contains(_currentIndex)) { _wrongIndexes.Add(_currentIndex); _wrongCount++; } _cts?.Cancel(); _currentIndex++; SetText(string.Empty,0); ShowCurrentQuestion(); if (_wrongCount >= MaxWrongWords) { await FailAndExit("错误超过3个单词，闯关失败！"); } }
        }
    }

    // 晃动动画
    private async Task ShakeEntry(VisualElement element)
    {
        uint duration =50; for (int i =0; i <3; i++) { await element.TranslateTo(-15,0, duration); await element.TranslateTo(15,0, duration); } await element.TranslateTo(0,0, duration);
    }

    private async Task FailAndExit(string message)
    {
        if (_isQuizFinished) return;
        _isQuizFinished = true;
        NormalizeUserAnswers();
        SaveQuizHistory(_correctCount);
        await DisplayAlert("失败", message, "确定");
        _onQuizRoundFinished?.Invoke(false, _correctCount, _quizList.Count);
        await Navigation.PopModalAsync();
    }

    private async Task ShowSummaryAndAutoFinish()
    {
        QuestionLabel.Text = "判卷中";
        SubmitButton.IsVisible = false;
        NextButton.IsVisible = false;
        HintButton.IsVisible = false;
        TimerLabel.Text = "";

        var summary = string.Join(Environment.NewLine, _quizList.Select((v, i) => $"{i +1}. {v.Meaning} - {v.Word}"));
        await DisplayAlert("所有单词与答案", summary, "完成");

        NormalizeUserAnswers();
        int score = _correctCount;
        SaveQuizRecord(score);
        SaveQuizHistory(score);
        await DisplayAlert("成绩", $"本次已自动判卷：答对 {score}/{_quizList.Count} 个单词。", "确定");
        _onQuizRoundFinished?.Invoke(true, score, _quizList.Count);
        await Navigation.PopModalAsync();
    }

    private async Task ShowSummaryAndScoreInput()
    {
        QuestionLabel.Text = "判卷区";
        SubmitButton.IsVisible = false;
        NextButton.IsVisible = false;
        HintButton.IsVisible = false;
        TimerLabel.Text = "";

        // 展示所有题目和答案
        var summary = string.Join(Environment.NewLine, _quizList.Select((v, i) => $"{i +1}. {v.Meaning} - {v.Word}"));
        await DisplayAlert("所有单词与答案", summary, "判卷");

        // 弹出分数输入
        string result = await DisplayPromptAsync("成绩录入", "请输入正确题目数量：", "确定", "取消", "0", maxLength:3, keyboard: Keyboard.Numeric);
        int score =0;
        int.TryParse(result, out score);
        if (score <0) score =0;
        if (score >100) score =100;
        NormalizeUserAnswers();
        SaveQuizRecord(score);
        await DisplayAlert("成绩已保存", $"本次答对：{score}个单词！", "确定");
        SaveQuizHistory(score);
        _onQuizRoundFinished?.Invoke(true, score, _quizList.Count);
        await Navigation.PopModalAsync();
    }

    private void SaveQuizRecord(int score)
    {
        // 简单保存到Preferences
        var records = Preferences.Get("QuizRecords", "");
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var newRecord = $"{now} 分数:{score}";
        if (!string.IsNullOrEmpty(records)) records += "|";
        records += newRecord;
        Preferences.Set("QuizRecords", records);
    }

    private void SaveQuizHistory(int score)
    {
        var history = new QuizHistory
        {
            Date = DateTime.Now,
            Meanings = _quizList.Select(v => v.Meaning).ToList(),
            Answers = _quizList.Select(v => v.Word).ToList(),
            UserAnswers = _userAnswers,
            CorrectCount = score,
            TotalCount = _quizList.Count,
            IsSuccess = score == _quizList.Count
        };
        string file = Path.Combine(FileSystem.AppDataDirectory, "QuizHistory.json");
        List<QuizHistory> all = new();
        if (File.Exists(file))
        {
            try
            {
                var json = File.ReadAllText(file);
                all = JsonSerializer.Deserialize<List<QuizHistory>>(json) ?? new();
            }
            catch { }
        }
        all.Insert(0, history);
        File.WriteAllText(file, JsonSerializer.Serialize(all));
    }

    // 拦截返回按钮
    protected override bool OnBackButtonPressed()
    {
        if (_isQuizFinished) return true;
        _isQuizFinished = true;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            bool exit = await DisplayAlert("提示", "确定要退出闯关吗？", "退出", "继续答题");
            if (exit)
            {
                Preferences.Set("QuizExitReason", "exit");
                _onQuizRoundFinished?.Invoke(false, _correctCount, _quizList.Count);
                await Navigation.PopModalAsync();
            }
            else
            {
                _isQuizFinished = false; //继续答题时允许交互
            }
        });
        return true; // 阻止默认返回
    }

    //兼容 GraphicsView 的 MoveInteraction事件（转调拖动处理）
    private void OnInputTouchMove(object sender, TouchEventArgs e)
    {
        OnInputTouchDrag(sender, e);
    }
}

public class QuizHistory
{
    public DateTime Date { get; set; }
    public List<string> Meanings { get; set; } = new();
    public List<string> Answers { get; set; } = new();
    public List<string> UserAnswers { get; set; } = new();
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
    public bool IsSuccess { get; set; }
}
