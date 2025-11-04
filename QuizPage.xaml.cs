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
    private bool _challengeMode = false; // 新增：挑战模式
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

    // 新增：倒计时可暂停/恢复
    private int _remainingSeconds;
    private bool _timerRunning;
    private bool _windowEventsAttached;
    private bool _isNavigatingAway;

    // 新增：精确测量文本宽度用于定位光标
    private Label _measureLabel;

    public QuizPage(Action<bool, int, int> onQuizRoundFinished, bool challengeMode = false)
    {
        InitializeComponent();
        _onQuizRoundFinished = onQuizRoundFinished;
        _challengeMode = challengeMode;

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
        _timerRunning = false;
        _isNavigatingAway = false;

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

        // 挑战模式下：提示按钮作为退出按钮
        if (HintButton != null)
        {
            if (_challengeMode)
            {
                HintButton.IsVisible = true;
                HintButton.Text = "退出";
            }
            else
            {
                HintButton.Text = "提示";
            }
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
        // 加入题号：如 "1.他们"
        QuestionLabel.Text = $"{_currentIndex +1}. {vocab.Meaning}";
        FeedbackLabel.Text = string.Empty;
        _hintCount =0;
        _submitCount =0;
        HintButton.IsEnabled = !_writtenMode && !_challengeMode && _totalHintCount < MaxHints;
        PhoneticLabel.IsVisible = false; // 切题时隐藏音标

        SetText(string.Empty,0);

        // 初始化倒计时
        _remainingSeconds = _timePerQuestion;
        TimerLabel.Text = $"倒计时：{_remainingSeconds}秒";

        // 启动倒计时
        PauseTimer();
        _cts = new CancellationTokenSource();
        _ = StartTimer(_cts.Token);

        // 确保前后台事件绑定
        AttachWindowEvents();
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
        if (_timerRunning) return;
        _timerRunning = true;
        try
        {
            int seconds = _remainingSeconds;
            while (seconds >0)
            {
                TimerLabel.Text = $"倒计时：{seconds}秒";
                await Task.Delay(1000);
                if (token.IsCancellationRequested || _isQuizFinished)
                {
                    _remainingSeconds = seconds; //记录剩余时间
                    return;
                }
                seconds--;
            }
            _remainingSeconds =0;
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
                // 普通模式/挑战模式下，超时计为错误并跳过
                EnsureAnswerPlaceholder(_currentIndex);
                if (_challengeMode)
                {
                    await EndChallenge(false, "时间到，挑战结束！");
                    return;
                }
                // 普通模式逻辑
                if (!_wrongIndexes.Contains(_currentIndex)) { _wrongIndexes.Add(_currentIndex); _wrongCount++; }
                // 若已达最大错误，直接失败退出，避免先切题再退出导致的并发导航
                if (_wrongCount >= MaxWrongWords)
                {
                    await FailAndExit("错误超过3个单词，闯关失败！");
                    return;
                }

                FeedbackLabel.Text = "超时，自动进入下一题！";
                await Task.Delay(500);
                _currentIndex++;
                ShowCurrentQuestion();
            }
        }
        finally
        {
            _timerRunning = false;
        }
    }

    private void PauseTimer()
    {
        _cts?.Cancel();
        _cts = null;
        _timerRunning = false;
    }

    private void ResumeTimerIfNeeded()
    {
        if (_isQuizFinished) return;
        if (_remainingSeconds <=0) return;
        if (_timerRunning) return;
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _ = StartTimer(_cts.Token);
    }

    private void AttachWindowEvents()
    {
        if (_windowEventsAttached) return;
        if (Window is null) return;
        Window.Activated += OnWindowActivated;
        Window.Deactivated += OnWindowDeactivated;
        _windowEventsAttached = true;
    }

    private void DetachWindowEvents()
    {
        if (!_windowEventsAttached) return;
        if (Window is null) { _windowEventsAttached = false; return; }
        Window.Activated -= OnWindowActivated;
        Window.Deactivated -= OnWindowDeactivated;
        _windowEventsAttached = false;
    }

    private void OnWindowActivated(object sender, EventArgs e)
    {
        // 回到前台时恢复计时（两种模式一致）
        ResumeTimerIfNeeded();
    }

    private void OnWindowDeactivated(object sender, EventArgs e)
    {
        //进入后台时暂停计时（两种模式一致）
        PauseTimer();
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

    private void EnsureMeasureLabel()
    {
        if (_measureLabel != null) return;
        _measureLabel = new Label
        {
            LineBreakMode = LineBreakMode.NoWrap,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalTextAlignment = TextAlignment.Center
        };
    }

    private double MeasureTextWidth(string s)
    {
        EnsureMeasureLabel();
        // 同步字体样式以保持测量一致性
        _measureLabel.FontSize = BeforeLabel.FontSize;
        _measureLabel.FontFamily = BeforeLabel.FontFamily;
        _measureLabel.FontAttributes = BeforeLabel.FontAttributes;
        _measureLabel.CharacterSpacing = BeforeLabel.CharacterSpacing;
        _measureLabel.Text = s;
        var size = _measureLabel.Measure(double.PositiveInfinity, double.PositiveInfinity);
        return size.Width;
    }

    private void LocateCaretByTouch(double x)
    {
        // 使用逐字符测量找到最接近的插入位置
        double width = InputGrid.Width -2; // 内容区宽度
        if (_text.Length ==0 || width <=0)
        {
            _caret =0;
            UpdateInputVisual();
            return;
        }

        //处理边界与内边距
        double padding =8; // 与视觉留白基本一致
        double target = Math.Clamp(x - padding,0, Math.Max(width - padding,0));

        // 快速边界判断
        double total = MeasureTextWidth(_text);
        if (target >= total)
        { _caret = _text.Length; UpdateInputVisual(); return; }
        if (target <=0)
        { _caret =0; UpdateInputVisual(); return; }

        //线性扫描（单词长度有限，性能足够）
        int best =0;
        double bestDiff = double.MaxValue;
        for (int i =0; i <= _text.Length; i++)
        {
            double w = MeasureTextWidth(_text.Substring(0, i));
            double diff = Math.Abs(w - target);
            if (diff < bestDiff)
            { bestDiff = diff; best = i; }
        }
        _caret = Math.Clamp(best,0, _text.Length);
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
        if (_challengeMode)
        {
            // 挑战模式下，提示按钮作为退出
            _ = EndChallenge(true, "已退出挑战");
            return;
        }
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
        PauseTimer();
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
            PauseTimer();
            _correctCount++;
            _currentIndex++;
            SetText(string.Empty,0);
            ShowCurrentQuestion();
        }
        else
        {
            if (_challengeMode)
            {
                await EndChallenge(false, "答错一个单词，挑战结束！");
                return;
            }
            if (_submitCount <3) { FeedbackLabel.Text = "错误，请重试！"; await ShakeEntry(InputHost); }
            else { if (!_wrongIndexes.Contains(_currentIndex)) { _wrongIndexes.Add(_currentIndex); _wrongCount++; } PauseTimer(); if (_wrongCount >= MaxWrongWords) { await FailAndExit("错误超过3个单词，闯关失败！"); return; } _currentIndex++; SetText(string.Empty,0); ShowCurrentQuestion(); }
        }
    }

    // 晃动动画
    private async Task ShakeEntry(VisualElement element)
    {
        uint duration =50; for (int i =0; i <3; i++) { await element.TranslateTo(-15,0, duration); await element.TranslateTo(15,0, duration); } await element.TranslateTo(0,0, duration);
    }

    private async Task EndChallenge(bool isUserExit, string message)
    {
        if (_isQuizFinished || _isNavigatingAway) return;
        _isQuizFinished = true;
        _isNavigatingAway = true;
        PauseTimer();
        DetachWindowEvents();
        int score = _correctCount;
        SaveChallengeRecord(score);
        if (isUserExit)
        {
            Preferences.Set("QuizExitReason", "exit");
        }
        int best = GetChallengeBest();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await DisplayAlert("挑战结束", $"{message}\n本次答对：{score} 个单词。\n最高纪录：{best} 个单词。", "确定");
            _onQuizRoundFinished?.Invoke(false, score, _quizList.Count);
            if (Navigation.ModalStack.Contains(this))
                await Navigation.PopModalAsync();
        });
    }

    private async Task FailAndExit(string message)
    {
        if (_isQuizFinished || _isNavigatingAway) return;
        _isQuizFinished = true;
        _isNavigatingAway = true;
        PauseTimer();
        DetachWindowEvents();
        if (_challengeMode)
        {
            // 挑战模式走挑战结束记录
            await EndChallenge(false, message);
            return;
        }
        NormalizeUserAnswers();
        SaveQuizHistory(_correctCount);
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await DisplayAlert("失败", message, "确定");
            _onQuizRoundFinished?.Invoke(false, _correctCount, _quizList.Count);
            if (Navigation.ModalStack.Contains(this))
                await Navigation.PopModalAsync();
        });
    }

    private async Task ShowSummaryAndAutoFinish()
    {
        if (_challengeMode)
        {
            // 挑战模式不展示答案清单，直接记录并结束（视为成功通关）
            int score = _correctCount;
            SaveChallengeRecord(score);
            int best = GetChallengeBest();
            await DisplayAlert("挑战完成", $"本次答对：{score} 个单词。\n最高纪录：{best} 个单词。", "确定");
            _onQuizRoundFinished?.Invoke(true, score, _quizList.Count);
            DetachWindowEvents();
            await Navigation.PopModalAsync();
            return;
        }

        QuestionLabel.Text = "判卷中";
        SubmitButton.IsVisible = false;
        NextButton.IsVisible = false;
        HintButton.IsVisible = false;
        TimerLabel.Text = "";

        var summary = string.Join(Environment.NewLine, _quizList.Select((v, i) => $"{i +1}. {v.Meaning} - {v.Word}"));
        await DisplayAlert("所有单词与答案", summary, "完成");

        NormalizeUserAnswers();
        int score2 = _correctCount;
        SaveQuizRecord(score2);
        SaveQuizHistory(score2);
        await DisplayAlert("成绩", $"本次已自动判卷：答对 {score2}/{_quizList.Count} 个单词。", "确定");
        _onQuizRoundFinished?.Invoke(true, score2, _quizList.Count);
        DetachWindowEvents();
        await Navigation.PopModalAsync();
    }

    private async Task ShowSummaryAndScoreInput()
    {
        if (_challengeMode)
        {
            // 挑战模式不手动录分，直接按当前得分记录
            int score = _correctCount;
            SaveChallengeRecord(score);
            int best = GetChallengeBest();
            await DisplayAlert("挑战结束", $"本次答对：{score} 个单词。\n最高纪录：{best} 个单词。", "确定");
            _onQuizRoundFinished?.Invoke(true, score, _quizList.Count);
            DetachWindowEvents();
            await Navigation.PopModalAsync();
            return;
        }

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
        int score3 =0;
        int.TryParse(result, out score3);
        if (score3 <0) score3 =0;
        if (score3 >100) score3 =100;
        NormalizeUserAnswers();
        SaveQuizRecord(score3);
        await DisplayAlert("成绩已保存", $"本次答对：{score3}个单词！", "确定");
        SaveQuizHistory(score3);
        _onQuizRoundFinished?.Invoke(true, score3, _quizList.Count);
        DetachWindowEvents();
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

    private record ChallengeRecord(DateTime Date, int CorrectCount);

    private void SaveChallengeRecord(int score)
    {
        string file = Path.Combine(FileSystem.AppDataDirectory, "ChallengeRecords.json");
        List<ChallengeRecord> all = new();
        if (File.Exists(file))
        {
            try
            {
                var json = File.ReadAllText(file);
                all = JsonSerializer.Deserialize<List<ChallengeRecord>>(json) ?? new();
            }
            catch { }
        }
        all.Insert(0, new ChallengeRecord(DateTime.Now, score));
        File.WriteAllText(file, JsonSerializer.Serialize(all));
    }

    private int GetChallengeBest()
    {
        string file = Path.Combine(FileSystem.AppDataDirectory, "ChallengeRecords.json");
        if (!File.Exists(file)) return _correctCount;
        try
        {
            var json = File.ReadAllText(file);
            var all = JsonSerializer.Deserialize<List<ChallengeRecord>>(json) ?? new();
            return all.Count ==0 ? _correctCount : Math.Max(_correctCount, all.Max(r => r.CorrectCount));
        }
        catch { return _correctCount; }
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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AttachWindowEvents();
        // 如果从后台返回且未完成，恢复计时（两种模式一致）
        ResumeTimerIfNeeded();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 页面消失（返回/关闭）时，确保停止计时并解绑
        PauseTimer();
        DetachWindowEvents();
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
                if (_challengeMode)
                {
                    await EndChallenge(true, "已退出挑战");
                    return;
                }
                _onQuizRoundFinished?.Invoke(false, _correctCount, _quizList.Count);
                if (Navigation.ModalStack.Contains(this))
                    await Navigation.PopModalAsync();
            }
            else
            {
                _isQuizFinished = false; //继续答题时允许交互
                ResumeTimerIfNeeded();
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
