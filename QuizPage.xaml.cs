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
    private List<Vocab> _challengeSource; // 挑战模式原始题库，用于无限循环
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
    private bool _challengeMode = false; // 挑战模式
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

    // 倒计时可暂停/恢复
    private int _remainingSeconds;
    private bool _timerRunning;
    private bool _windowEventsAttached;
    private bool _isNavigatingAway;
    private bool _confirmingExit; // 防抖：返回确认中

    // 精确测量文本宽度用于定位光标
    private Label _measureLabel;

    // 挑战模式失败词汇记录
    private Vocab? _failedVocab;

    //方向键拖动起点
    private double _arrowPanStartX;
    private int _arrowPanStartCaret;

    // 新增：挑战层次展示（如 六年级上册）
    private readonly string? _challengeLevel;

    public QuizPage(Action<bool, int, int> onQuizRoundFinished, bool challengeMode = false, IEnumerable<Vocab>? challengePool = null, string? challengeLevel = null)
    {
        InitializeComponent();
        _onQuizRoundFinished = onQuizRoundFinished;
        _challengeMode = challengeMode;
        _challengeLevel = challengeLevel;

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
        _confirmingExit = false;
        _failedVocab = null;

        //让方向键宽度与空格键一致
        SpaceButton.SizeChanged += (_, __) =>
        {
            if (SpaceButton.Width >0)
            {
                // 增加原来一半的长度（1.5 倍）
                ArrowButton.WidthRequest = SpaceButton.Width *1.5;
            }
        };

        // 初始化题库
        if (_challengeMode && challengePool != null)
        {
            // 挑战模式：使用传入的题库；并保存源题库用于无限循环
            _challengeSource = challengePool.ToList();
            _quizList = new List<Vocab>(_challengeSource.Count);
            AppendShuffledChallengePool(); //先填充一轮
        }
        else
        {
            // 普通/复习：按设置筛选题库，每个单元至少1题
            var pool = VocabRepository.AllVocabs
                .Where(v => selectedUnits.Any(su => su.book == v.Book && su.unit == v.Unit))
                .ToList();

            // 保证每个单元至少1题
            var perUnit = selectedUnits
                .Select(su => pool.Where(v => v.Book == su.book && v.Unit == su.unit).OrderBy(_ => Guid.NewGuid()).FirstOrDefault())
                .Where(v => v != null)
                .ToList();

            // 剩余题目随机补足
            var rest = pool.Except(perUnit!).OrderBy(_ => Guid.NewGuid()).Take(Math.Max(questionCount - perUnit.Count,0)).ToList();
            _quizList = perUnit.Concat(rest).ToList()!;
        }

        UpdateModeUI();
        ShowCurrentQuestion();
        StartCaretBlink();
    }

    private void AppendShuffledChallengePool()
    {
        if (_challengeSource == null || _challengeSource.Count ==0)
        {
            _challengeSource = VocabRepository.AllVocabs.ToList();
        }
        // 打乱一次
        var shuffled = _challengeSource.OrderBy(_ => Guid.NewGuid()).ToList();

        // 按分段概率混入扩充词：
        //1-10:0%，11-20:20%，21-30:50%，31-40:80%，41-50:100%，之后每10个必插入1个
        if (VocabRepository.ExtraVocabs != null && VocabRepository.ExtraVocabs.Count >0)
        {
            var mixed = new List<Vocab>(shuffled.Count + shuffled.Count /10 +4);
            int countInGroup =0; // 当前分组内已加入的基础词数量（不含扩充）
            int blockIndex =0; // 分组序号（0基）：0=1-10，1=11-20，...

            foreach (var v in shuffled)
            {
                mixed.Add(v);
                countInGroup++;

                if (countInGroup ==10)
                {
                    double probability = GetExtraProbabilityForBlock(blockIndex);
                    bool shouldInsertExtra = probability >=1.0 || (probability >0 && Random.Shared.NextDouble() < probability);

                    if (shouldInsertExtra)
                    {
                        var extra = VocabRepository.ExtraVocabs[Random.Shared.Next(VocabRepository.ExtraVocabs.Count)];
                        // 在最近的10个基础词范围内随机插入位置（不包含之前组的元素）
                        int start = Math.Max(0, mixed.Count -10); //这10个基础词的范围
                        int insertPos = Random.Shared.Next(start, mixed.Count +1);
                        mixed.Insert(insertPos, extra);
                    }

                    // 下一组
                    countInGroup =0;
                    blockIndex++;
                }
            }
            _quizList.AddRange(mixed);
        }
        else
        {
            _quizList.AddRange(shuffled);
        }
    }

    private static double GetExtraProbabilityForBlock(int blockIndex)
    {
        return blockIndex switch
        {
            0 =>0.0, //1-10
            1 =>0.2, //11-20
            2 =>0.5, //21-30
            3 =>0.8, //31-40
            4 =>1.0, //41-50
            _ =>1.0 //之后每10个必插入1个
        };
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

        // 挑战模式下：提示按钮作为退出按钮；底部显示挑战层次
        if (HintBarButton != null)
        {
            if (_challengeMode)
            {
                HintBarButton.IsVisible = true;
                HintBarButton.Text = "退出";
                HintBarButton.IsEnabled = true; // 确保可点
                if (FooterMetaLabel != null)
                {
                    FooterMetaLabel.IsVisible = !string.IsNullOrWhiteSpace(_challengeLevel);
                    if (FooterMetaLabel.IsVisible)
                        FooterMetaLabel.Text = $"挑战层次：{_challengeLevel}";
                }
            }
            else
            {
                HintBarButton.Text = "提示";
                if (FooterMetaLabel != null)
                {
                    FooterMetaLabel.IsVisible = false;
                    FooterMetaLabel.Text = string.Empty;
                }
            }
        }
    }

    private async void ShowCurrentQuestion()
    {
        if (_isQuizFinished) return;
        if (_currentIndex >= _quizList.Count)
        {
            if (_challengeMode)
            {
                // 无限循环：追加一轮新的随机题目
                AppendShuffledChallengePool();
            }
            else
            {
                _isQuizFinished = true;
                if (_writtenMode)
                    await ShowSummaryAndScoreInput();
                else
                    await ShowSummaryAndAutoFinish();
                return;
            }
        }
        var vocab = _quizList[_currentIndex];
        // 加入题号：如 "1.他们"
        QuestionLabel.Text = $"{_currentIndex +1}. {vocab.Meaning}";
        FeedbackLabel.Text = string.Empty;
        _hintCount =0;
        _submitCount =0;
        if (_challengeMode)
        {
            // 挑战模式使用退出按钮，始终可用
            HintBarButton.IsVisible = true;
            HintBarButton.Text = "退出";
            HintBarButton.IsEnabled = true;
        }
        else
        {
            HintBarButton.IsEnabled = !_writtenMode && _totalHintCount < MaxHints;
        }
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
                    //记录失败词汇并结束挑战
                    _failedVocab = _quizList[_currentIndex];
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
        //保险：挑战模式回到前来时确保退出按钮可点和层次显示
        if (_challengeMode)
        {
            HintBarButton.IsVisible = true;
            HintBarButton.IsEnabled = true;
            HintBarButton.Text = "退出";
            if (FooterMetaLabel != null)
            {
                FooterMetaLabel.IsVisible = !string.IsNullOrWhiteSpace(_challengeLevel);
                if (FooterMetaLabel.IsVisible)
                    FooterMetaLabel.Text = $"挑战层次：{_challengeLevel}";
            }
        }
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
        // 已取消空格键拖动功能
    }
    private async void OnSpaceClicked(object sender, EventArgs e)
    { if (_writtenMode) return; if (sender is Button b) await PressVisual(b); InsertAtCaret(" "); }

    private async void OnBackspaceClicked(object sender, EventArgs e)
    { if (_writtenMode) return; if (sender is Button b) await PressVisual(b); BackspaceAtCaret(); }

    private void OnArrowPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _arrowPanStartX = e.TotalX;
                _arrowPanStartCaret = _caret;
                break;
            case GestureStatus.Running:
                var dx = e.TotalX - _arrowPanStartX;
                int step = (int)(dx /20); // 与原来空格键一致手感
                _caret = Math.Clamp(_arrowPanStartCaret + step,0, _text.Length);
                UpdateInputVisual();
                break;
        }
    }

    private async void OnHintClicked(object sender, EventArgs e)
    {
        // 挑战模式：增加『是否结束挑战？』确认
        if (_challengeMode)
        {
            if (_isNavigatingAway) return;
            bool confirm = false;
            try
            {
                confirm = await DisplayAlert("提示", "是否结束挑战？", "是", "否");
            }
            catch { /* 若在后台，DisplayAlert可能失败，直接当否处理 */ }
            if (!confirm) return;
            _ = EndChallenge(true, "已退出挑战");
            return;
        }

        if (_isQuizFinished) return;
        var vocab = _quizList[_currentIndex];
        if (_writtenMode)
        {
            PhoneticLabel.IsVisible = true;
            PhoneticLabel.Text = string.IsNullOrWhiteSpace(vocab.Phonetic) ? "无音标" : $"音标：{vocab.Phonetic}";
            HintBarButton.IsEnabled = false;
        }
        else
        {
            if (_hintCount ==0 && _totalHintCount < MaxHints)
            {
                int len = Math.Min(3, vocab.Word.Length);
                SetText(vocab.Word.Substring(0, len), len); // 光标在末尾
                _hintCount =1; _totalHintCount++; HintBarButton.IsEnabled = false; if (_totalHintCount >= MaxHints) HintBarButton.IsEnabled = false;
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
                //记录失败词汇并结束挑战
                _failedVocab = vocab;
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
        if (_isNavigatingAway) return;
        _isQuizFinished = true;
        _isNavigatingAway = true;
        PauseTimer();
        DetachWindowEvents();
        int score = _correctCount;
        SaveChallengeRecord(score, _failedVocab, isUserExit, isSuccess: false);
        // 新增：挑战模式也写入卷子记录
        NormalizeUserAnswers();
        SaveQuizHistory(score);
        if (isUserExit)
        {
            Preferences.Set("QuizExitReason", "exit");
        }
        int best = GetChallengeBest();

        // 无论是否能弹出提示，都要退出页面
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    var extra = string.IsNullOrWhiteSpace(_challengeLevel) ? string.Empty : $"\n挑战层次：{_challengeLevel}";
                    await DisplayAlert("挑战结束", $"{message}\n本次答对：{score} 个单词。\n最高纪录：{best} 个单词。{extra}", "确定");
                }
                catch { /* 忽略因切到后台导致的弹窗异常 */ }
                _onQuizRoundFinished?.Invoke(false, score, _quizList.Count);
                if (Navigation.ModalStack.Contains(this))
                    await Navigation.PopModalAsync();
            });
        }
        catch
        {
            // 如果主线程弹窗失败，也直接回到首页
            _onQuizRoundFinished?.Invoke(false, score, _quizList.Count);
            if (Navigation.ModalStack.Contains(this))
                await Navigation.PopModalAsync();
        }
    }

    private async Task FailAndExit(string message)
    {
        if (_isNavigatingAway) return;
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
            SaveChallengeRecord(score, null, isUserExit: false, isSuccess: true);
            // 新增：挑战模式也写入卷子记录
            NormalizeUserAnswers();
            SaveQuizHistory(score);
            int best = GetChallengeBest();
            var extra = string.IsNullOrWhiteSpace(_challengeLevel) ? string.Empty : $"\n挑战层次：{_challengeLevel}";
            await DisplayAlert("挑战完成", $"本次答对：{score} 个单词。\n最高纪录：{best} 个单词。{extra}", "确定");
            _onQuizRoundFinished?.Invoke(true, score, _quizList.Count);
            DetachWindowEvents();
            await Navigation.PopModalAsync();
            return;
        }

        QuestionLabel.Text = "判卷中";
        SubmitButton.IsVisible = false;
        NextButton.IsVisible = false;
        HintBarButton.IsVisible = false;
        TimerLabel.Text = "";

        var summary = string.Join(Environment.NewLine, _quizList.Select((v, i) => $"{i +1}. {v.Meaning} - {v.Word}"));
        await DisplayAlert("所有单词与答案", summary, "完成");

        NormalizeUserAnswers();
        int score2 = _correctCount;
        SaveQuizRecord(score2);
        SaveQuizHistory(score2);
        var rangeText = BuildSelectedRangeText();
        var tail = string.IsNullOrEmpty(rangeText) ? string.Empty : $"\n{rangeText}";
        await DisplayAlert("成绩", $"本次已自动判卷：答对 {score2}/{_quizList.Count} 个单词。{tail}", "确定");
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
            SaveChallengeRecord(score, null, isUserExit: false, isSuccess: true);
            // 新增：挑战模式也写入卷子记录
            NormalizeUserAnswers();
            SaveQuizHistory(score);
            int best = GetChallengeBest();
            var extra = string.IsNullOrWhiteSpace(_challengeLevel) ? string.Empty : $"\n挑战层次：{_challengeLevel}";
            await DisplayAlert("挑战结束", $"本次答对：{score} 个单词。\n最高纪录：{best} 个单词。{extra}", "确定");
            _onQuizRoundFinished?.Invoke(true, score, _quizList.Count);
            DetachWindowEvents();
            await Navigation.PopModalAsync();
            return;
        }

        QuestionLabel.Text = "判卷区";
        SubmitButton.IsVisible = false;
        NextButton.IsVisible = false;
        HintBarButton.IsVisible = false;
        TimerLabel.Text = "";

        // 展示所有单词和答案
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
        var rangeText2 = BuildSelectedRangeText();
        if (!string.IsNullOrEmpty(rangeText2))
        {
            await DisplayAlert("范围", rangeText2, "关闭");
        }
        _onQuizRoundFinished?.Invoke(true, score3, _quizList.Count);
        DetachWindowEvents();
        await Navigation.PopModalAsync();
    }

    private string BuildSelectedRangeText()
    {
        // 从已生成的题库中反推范围：按 Book+Unit 去重
        var pairs = _quizList
            .Select(v => (Book: v.Book, Unit: v.Unit))
            .Distinct()
            .OrderBy(p => p.Book)
            .ThenBy(p => p.Unit)
            .ToList();
        if (pairs.Count ==0) return string.Empty;
        var sb = new StringBuilder();
        sb.Append("范围：");
        // 每项换行（首项直接跟随，不额外前缀）
        for (int i =0; i < pairs.Count; i++)
        {
            if (i ==0)
                sb.Append($"{pairs[i].Book}-{pairs[i].Unit}");
            else
                sb.Append($"\n{pairs[i].Book}-{pairs[i].Unit}");
        }
        return sb.ToString();
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

    // 挑战记录持久化模型（写入文件）
    private class ChallengeRecordModel
    {
        public DateTime Date { get; set; }
        public int CorrectCount { get; set; }
        public string? FailedWord { get; set; }
        public string? FailedMeaning { get; set; }
        public string? FailedPhonetic { get; set; }
        public bool IsExit { get; set; }
        public bool IsSuccess { get; set; }
        public string? Level { get; set; } // 新增：挑战层次
    }

    private void SaveChallengeRecord(int score, Vocab? failed, bool isUserExit, bool isSuccess)
    {
        string file = Path.Combine(FileSystem.AppDataDirectory, "ChallengeRecords.json");
        List<ChallengeRecordModel> all = new();
        if (File.Exists(file))
        {
            try
            {
                var json = File.ReadAllText(file);
                all = JsonSerializer.Deserialize<List<ChallengeRecordModel>>(json) ?? new();
            }
            catch { }
        }
        all.Insert(0, new ChallengeRecordModel
        {
            Date = DateTime.Now,
            CorrectCount = score,
            FailedWord = failed?.Word,
            FailedMeaning = failed?.Meaning,
            FailedPhonetic = failed?.Phonetic,
            IsExit = isUserExit,
            IsSuccess = isSuccess,
            Level = _challengeLevel
        });
        File.WriteAllText(file, JsonSerializer.Serialize(all));
    }

    private int GetChallengeBest()
    {
        string file = Path.Combine(FileSystem.AppDataDirectory, "ChallengeRecords.json");
        if (!File.Exists(file)) return _correctCount;
        try
        {
            var json = File.ReadAllText(file);
            //兼容旧版本：只取最大 CorrectCount
            using var doc = JsonDocument.Parse(json);
            int best =0;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.TryGetProperty("CorrectCount", out var cc))
                {
                    best = Math.Max(best, cc.GetInt32());
                }
            }
            return Math.Max(best, _correctCount);
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
            IsSuccess = score == _quizList.Count,
            RangeText = BuildSelectedRangeText()
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
        if (_confirmingExit || _isNavigatingAway) return true;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                _confirmingExit = true;
                bool exit = await DisplayAlert("提示", "确定要退出闯关吗？", "退出", "继续答题");
                if (exit)
                {
                    Preferences.Set("QuizExitReason", "exit");
                    // 无论哪种模式，先停止题目倒计时并解绑事件，避免计时回调在退出后继续触发
                    PauseTimer();
                    DetachWindowEvents();

                    if (_challengeMode)
                    {
                        await EndChallenge(true, "已退出挑战");
                        return;
                    }
                    // 普通模式：退出也保存考试历史
                    NormalizeUserAnswers();
                    SaveQuizHistory(_correctCount);

                    _isQuizFinished = true;
                    _onQuizRoundFinished?.Invoke(false, _correctCount, _quizList.Count);
                    if (Navigation.ModalStack.Contains(this))
                        await Navigation.PopModalAsync();
                }
                else
                {
                    //继续答题
                    ResumeTimerIfNeeded();
                }
            }
            finally
            {
                _confirmingExit = false;
            }
        });
        return true; // 阻止默认返回
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
    public string? RangeText { get; set; } // 新增：保存本次题库范围文本
}
